using System.Collections.Generic;
using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>
    /// Describes what a moving hull leaves on the water: the trail its wave pattern is bent along,
    /// and the foam it churns up. OceanWakeSystem turns both into the textures the ocean samples.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ShipSectionBuoyancy), typeof(ShipMotor), typeof(Rigidbody))]
    [AddComponentMenu("Water System/Ship Wake Emitter")]
    public sealed class ShipWakeEmitter : MonoBehaviour
    {
        internal static readonly List<ShipWakeEmitter> Active = new List<ShipWakeEmitter>();

        // The newest sample follows the hull, so one slot is kept for it on top of the stored trail.
        const int MaxStoredNodes = 63;
        const float Gravity = 9.81f;

        [Header("Foam (coverage reached at the reference speed; above 1 stays solid for longer)")]
        [Min(0)] public float SideFoamScale = 0.72f;
        [Min(0)] public float BowFoamScale = 1.1f;
        [Min(0)] public float SternFoamScale = 1.2f;
        [Tooltip("Horizontal speed in m/s at which foam and waves reach full strength.")]
        [Min(0.1f)] public float ForwardSpeedReference = 4.8f;
        [Min(0.1f)] public float SideRadius = 0.7f;
        [Min(0.1f)] public float BowRadius = 1.15f;
        [Min(0.1f)] public float SternRadius = 1.1f;
        [Min(0)] public float MinimumSubmergedVolume = 0.1f;

        [Header("Waves (precomputed Kelvin pattern, no simulation)")]
        [Tooltip("Height in metres of the largest wave beside the hull at the reference speed. Zero disables ship waves.")]
        [Min(0)] public float WaveHeight = 0.45f;
        [Tooltip("Seconds a stretch of the wave pattern lives after the hull has passed.")]
        [Min(1)] public float WaveLifetime = 22f;
        [Tooltip("Where the waves start, in hull lengths ahead of the hull centre along the direction of travel.")]
        [Range(-0.5f, 0.5f)] public float WaveOrigin = 0.2f;
        [Tooltip("Foam left where the bow and stern waves are steep enough to spill.")]
        [Range(0, 2)] public float CrestFoam = 0.8f;

        struct TrailNode
        {
            public Vector2 Position;
            public Vector2 Backward;    // Opposite to the direction of travel when the hull was here.
            public float Strength;      // Wave-making strength when the hull was here, 0..1.
            public float Wavenumber;    // g / U^2 of the waves this stretch was made with, in 1/m.
            public float Phase;         // Where in the wave pattern this stretch is now, in radians of its transverse waves.
            public float BirthTime;
        }

        // Oldest first. The live head at the wave origin is appended while building the GPU nodes.
        readonly List<TrailNode> trail = new List<TrailNode>(MaxStoredNodes + 1);
        ShipSectionBuoyancy buoyancy;
        ShipMotor motor;
        Rigidbody body;
        Vector2 direction = Vector2.up;   // Smoothed horizontal direction of travel.
        Vector2 origin, hullCentre;
        float speed, strength, patternSpeed, headWavenumber, headPhase, slack, hullLength = 1;
        int steppedFrame = -1;

        internal OceanRenderer Ocean => buoyancy != null ? ((IOceanSurfaceQueryClient)buoyancy).Ocean : null;
        internal Vector2 HullCentre => hullCentre;
        internal Vector2 Direction => direction;

        void Awake()
        {
            buoyancy = GetComponent<ShipSectionBuoyancy>();
            motor = GetComponent<ShipMotor>();
            body = GetComponent<Rigidbody>();
        }

        void OnEnable()
        {
            if (buoyancy == null) buoyancy = GetComponent<ShipSectionBuoyancy>();
            if (motor == null) motor = GetComponent<ShipMotor>();
            if (body == null) body = GetComponent<Rigidbody>();
            if (!Active.Contains(this)) Active.Add(this);
            ResetTrail();
        }

        void OnDisable()
        {
            Active.Remove(this);
            ResetTrail();
        }

        void ResetTrail()
        {
            trail.Clear();
            patternSpeed = headWavenumber = headPhase = slack = speed = strength = 0;
            steppedFrame = -1;
        }

        /// <summary>Advances the trail once per rendered frame. Returns false while the emitter has nothing to describe.</summary>
        /// <param name="shortestWavelength">Shortest wave in metres that the wake window can still resolve.</param>
        internal bool Step(float deltaTime, float time, float shortestWavelength)
        {
            if (buoyancy == null || motor == null || body == null) return false;
            if (steppedFrame == Time.frameCount) return true;
            steppedFrame = Time.frameCount;

            // Heave is not travel: only the horizontal velocity makes a wake.
            Vector3 velocity = body.linearVelocity;
            var horizontal = new Vector2(velocity.x, velocity.z);
            speed = horizontal.magnitude;
            if (speed > 0.3f)
            {
                direction = Vector2.Lerp(direction, horizontal / speed, 1 - Mathf.Exp(-deltaTime * 6f));
                float length = direction.magnitude;
                direction = length > 1e-4f ? direction / length : horizontal / speed;
            }
            float bowZ = buoyancy.WakeBowZ, sternZ = buoyancy.WakeSternZ;
            hullLength = Mathf.Max(0.5f, (bowZ - sternZ) * Mathf.Abs(transform.lossyScale.z));
            Vector3 centre = transform.TransformPoint(new Vector3(0, 0, 0.5f * (bowZ + sternZ)));
            hullCentre = new Vector2(centre.x, centre.z);
            origin = hullCentre + direction * (WaveOrigin * hullLength);

            // The waves that keep pace with a hull are 2 pi U^2 / g long, so a slow hull makes short
            // ones. Below the speed whose waves the wake window can still resolve it makes none, and
            // from there they grow in gradually: wave making is weak long before it is strong.
            float slowest = Mathf.Sqrt(Gravity * Mathf.Max(shortestWavelength, 0.01f) / (2 * Mathf.PI));
            float reference = Mathf.Max(ForwardSpeedReference, 0.1f);
            bool afloat = buoyancy.CurrentSubmergedVolume >= MinimumSubmergedVolume;
            float speed01 = Mathf.Clamp01(speed / reference);
            float making = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(slowest, Mathf.Max(reference, 1.5f * slowest), speed));
            strength = afloat ? speed01 * Mathf.Sqrt(speed01) * making : 0;

            // The speed that sets the wavelength of what the hull lays down NOW. Waves already on the
            // water keep the wavelength they were made with (TrailNode.Wavenumber): rescaling a whole
            // wake to the current speed of the hull would squeeze it into ripples whenever the hull
            // stops. The speed follows the hull up closely and down slowly, and holds once the hull is
            // too slow to make waves, so braking does not end the wake in a tail of ever shorter ones.
            if (speed > patternSpeed) patternSpeed = Mathf.Lerp(patternSpeed, speed, 1 - Mathf.Exp(-deltaTime * 6f));
            else if (speed >= slowest) patternSpeed = Mathf.Lerp(patternSpeed, speed, 1 - Mathf.Exp(-deltaTime * 0.3f));
            float waveSpeed = Mathf.Max(patternSpeed, slowest);
            headWavenumber = Gravity / (waveSpeed * waveSpeed);

            float spacing = 0.3f * hullLength;
            if (trail.Count > 0 && (trail[trail.Count - 1].Position - origin).sqrMagnitude > 25 * spacing * spacing)
                trail.Clear();   // Teleported: an old trail must not stretch across the jump.

            float share = AdvanceWake(deltaTime, time);
            JoinWake(share, deltaTime, time);
            if (trail.Count == 0 || (trail[trail.Count - 1].Position - origin).sqrMagnitude >= spacing * spacing)
                AddNode(strength, headPhase, time);
            while (trail.Count > 1 && time - trail[0].BirthTime > WaveLifetime) trail.RemoveAt(0);
            return true;
        }

        /// <summary>
        /// Moves the waves that are already on the water. They no longer depend on the hull: every
        /// stretch advances through the pattern at the beat of the visible wake, which keeps its
        /// crests rolling on at their own speed after the hull has slowed down or stopped.
        /// Returns how much of that beat the hull itself sets, 0..1.
        /// </summary>
        float AdvanceWake(float deltaTime, float time)
        {
            float weight = 0, beat = 0, peak = 0;
            for (int i = 0; i < trail.Count; i++)
            {
                TrailNode node = trail[i];
                float visible = node.Strength * AgeFade(time - node.BirthTime);
                weight += visible;
                // Deep-water waves of wavenumber k pass a fixed point at sqrt(g k) radians per second.
                beat += visible * Mathf.Sqrt(Gravity * node.Wavenumber);
                peak = Mathf.Max(peak, visible);
            }
            // A hull that makes waves as large as those behind it leads; a hull that has stopped
            // making waves leaves the beat to the wake.
            float headBeat = Mathf.Sqrt(Gravity * headWavenumber);
            float share = peak > 1e-3f ? Mathf.Clamp01(strength / peak) : 1;
            float clock = weight > 1e-3f ? Mathf.Lerp(beat / weight, headBeat, share) : headBeat;
            for (int i = 0; i < trail.Count; i++)
            {
                TrailNode node = trail[i];
                // The pattern is blank long before this limit; it only keeps the last sample of a resting hull finite.
                node.Phase = Mathf.Min(node.Phase + clock * deltaTime, 4096f);
                trail[i] = node;
            }
            return share;
        }

        /// <summary>Finds the phase at the hull: where in the pattern the waves it makes now begin.</summary>
        void JoinWake(float share, float deltaTime, float time)
        {
            headPhase = 0;
            if (trail.Count == 0) return;
            TrailNode newest = trail[trail.Count - 1];
            // The phase with which the waves of the hull would join those behind it without a seam.
            // It is zero for a hull at steady speed, and grows when the wake runs on past a slower hull.
            float continuity = newest.Phase -
                0.5f * (headWavenumber + newest.Wavenumber) * (origin - newest.Position).magnitude;
            if (newest.Strength <= 0)
            {
                // Flat water behind the hull: there is nothing to join, so its waves start at the hull.
                // When it gets under way again after a rest, a silent sample at the hull keeps the gap
                // between the phase of the old wake and that of the new one inside water without waves.
                if (strength > 0 && continuity > 0.5f)
                {
                    AddNode(0, 0, time);
                    continuity = 0;
                }
                slack = continuity;
            }
            else
            {
                // The hull still trails waves of its own. As it makes larger ones again they have to
                // start at the hull, so the seam is given up gradually and spread thinly along the new
                // stretch, instead of being closed by dragging the old crests backwards.
                slack = Mathf.Min(Mathf.Max(continuity, 0), slack + 0.3f * headWavenumber * speed * share * deltaTime);
            }
            headPhase = Mathf.Max(0, continuity - slack);
        }

        void AddNode(float nodeStrength, float phase, float time)
        {
            if (trail.Count == MaxStoredNodes) trail.RemoveAt(0);
            trail.Add(new TrailNode
            {
                Position = origin, Backward = -direction, Strength = nodeStrength,
                Wavenumber = headWavenumber, Phase = phase, BirthTime = time
            });
            slack = 0;
        }

        float AgeFade(float age)
        {
            float t = Mathf.Clamp01(age / Mathf.Max(1, WaveLifetime));
            return 1 - t * t * (3 - 2 * t);
        }

        /// <summary>Appends this ship's wave trail and foam deposits in the layout of WakeDeposit.compute.</summary>
        internal void AppendWake(List<WakeNode> nodes, List<WakeShip> ships, List<WakeSplat> splats, float time, float waveScale)
        {
            AppendSplats(splats);
            float height = WaveHeight * waveScale;
            if (height <= 0 || trail.Count == 0) return;

            int start = nodes.Count;
            // Newest first: the live head, then the stored samples from young to old.
            // The live head must follow every rendered pose, independently of stored-node spacing.
            // A sample born this frame coincides with it and shares its direction, so the wedge
            // between the two is empty and needs no special case.
            nodes.Add(new WakeNode
            {
                Position = origin, Backward = -direction, Phase = headPhase, Amplitude = strength * height,
                Breaking = strength * strength * CrestFoam, Wavenumber = headWavenumber
            });
            float peak = strength * height;
            float reach = OceanWakeSystem.KelvinReach(headWavenumber, hullLength);
            Vector2 sum = origin;
            for (int i = trail.Count - 1; i >= 0; i--)
            {
                TrailNode sample = trail[i];
                float age = time - sample.BirthTime;
                float amplitude = sample.Strength * AgeFade(age) * height;
                // Only the first seconds of a wave are steep enough to spill.
                float young = Mathf.Clamp01(1 - age / 2.5f);
                nodes.Add(new WakeNode
                {
                    Position = sample.Position, Backward = sample.Backward, Phase = sample.Phase, Amplitude = amplitude,
                    Breaking = sample.Strength * sample.Strength * young * young * CrestFoam, Wavenumber = sample.Wavenumber
                });
                sum += sample.Position;
                if (amplitude <= 1e-4f) continue;
                peak = Mathf.Max(peak, amplitude);
                reach = Mathf.Max(reach, OceanWakeSystem.KelvinReach(sample.Wavenumber, hullLength));
            }
            int count = nodes.Count - start;
            if (peak <= 1e-4f) { nodes.RemoveRange(start, count); return; }

            Vector2 boundsCentre = sum / count;
            float radius = 0;
            for (int i = 0; i < count; i++)
            {
                WakeNode node = nodes[start + i];
                radius = Mathf.Max(radius, (node.Position - boundsCentre).magnitude);
                // How fast the trail turns here, from the samples on either side. The kernel uses it to
                // tell where the inside of a turn squeezes the pattern (see BuildWakeWaves).
                WakeNode newer = nodes[start + Mathf.Max(i - 1, 0)], older = nodes[start + Mathf.Min(i + 1, count - 1)];
                float turn = newer.Backward.x * older.Backward.y - newer.Backward.y * older.Backward.x;
                node.Curvature = turn / Mathf.Max((older.Position - newer.Position).magnitude, 0.3f * hullLength);
                nodes[start + i] = node;
            }
            ships.Add(new WakeShip
            {
                BoundsCentre = boundsCentre, BoundsRadius = radius + reach, HullLength = hullLength,
                NodeStart = start, NodeCount = count
            });
        }

        void AppendSplats(List<WakeSplat> output)
        {
            if (buoyancy.CurrentSubmergedVolume < MinimumSubmergedVolume) return;
            float reference = Mathf.Max(ForwardSpeedReference, 0.1f);
            float speed01 = Mathf.Clamp01(speed / reference);
            float energy = speed01 * speed01;
            float throttle = Mathf.Abs(motor.CurrentThrottle);
            // A propeller churns the water behind the stern even before the hull has gathered way.
            float wash = Mathf.Max(speed01 * Mathf.Sqrt(speed01), throttle * 0.45f);
            if (energy < 0.004f && wash < 0.02f) return;

            if (energy >= 0.004f)
            {
                int sections = buoyancy.WakeSectionCount;
                for (int i = 0; i < sections; i++)
                {
                    if (buoyancy.GetWakeHalfBeam(i) <= 0) continue;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 point = buoyancy.GetWakeSectionPoint(i, side);
                        Vector3 pointVelocity = body.GetPointVelocity(point);
                        // A turning hull sweeps its outer side through the water faster than its inner one.
                        float flow = new Vector2(pointVelocity.x, pointVelocity.z).magnitude / reference;
                        float level = SideFoamScale * energy * Mathf.Clamp(0.4f + flow, 0.4f, 1.5f);
                        output.Add(new WakeSplat(point, SideRadius, level, 0.5f * level, 0.7f));
                    }
                }
                Vector3 bow = transform.TransformPoint(new Vector3(0, 0, buoyancy.WakeBowZ));
                output.Add(new WakeSplat(bow, BowRadius, BowFoamScale * energy, 0.6f * energy, 0.6f));
            }
            Vector3 stern = transform.TransformPoint(new Vector3(0, 0, buoyancy.WakeSternZ));
            output.Add(new WakeSplat(stern, SternRadius, SternFoamScale * wash, Mathf.Clamp01(1.2f * wash), 0.45f));
        }
    }
}
