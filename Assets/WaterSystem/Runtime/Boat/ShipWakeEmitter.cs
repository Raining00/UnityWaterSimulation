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
            public float Strength;    // Wave-making strength when the hull was here, 0..1.
            public float BirthTime;
        }

        // Oldest first. The live head at the wave origin is appended while building the GPU nodes.
        readonly List<TrailNode> trail = new List<TrailNode>(MaxStoredNodes + 1);
        ShipSectionBuoyancy buoyancy;
        ShipMotor motor;
        Rigidbody body;
        Vector2 direction = Vector2.up;   // Smoothed horizontal direction of travel.
        Vector2 origin, hullCentre;
        float speed, strength, patternSpeed, overtake, hullLength = 1;
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
            patternSpeed = overtake = speed = strength = 0;
            steppedFrame = -1;
        }

        /// <summary>Advances the trail once per rendered frame. Returns false while the emitter has nothing to describe.</summary>
        internal bool Step(float deltaTime, float time)
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

            bool afloat = buoyancy.CurrentSubmergedVolume >= MinimumSubmergedVolume;
            float speed01 = Mathf.Clamp01(speed / Mathf.Max(ForwardSpeedReference, 0.1f));
            strength = afloat ? speed01 * Mathf.Sqrt(speed01) : 0;

            // Waves keep the speed they were made with. The pattern follows an accelerating hull quickly
            // but a braking one slowly, and whatever the hull loses against it lets the pattern run on
            // past the stern - the way a real wake catches up with a boat that stops.
            float follow = speed > patternSpeed ? 1.5f : 0.3f;
            patternSpeed = Mathf.Lerp(patternSpeed, speed, 1 - Mathf.Exp(-deltaTime * follow));
            if (patternSpeed > speed) overtake += (patternSpeed - speed) * deltaTime;
            else overtake *= Mathf.Exp(-deltaTime * speed / hullLength);

            float spacing = 0.3f * hullLength;
            if (trail.Count > 0 && (trail[trail.Count - 1].Position - origin).sqrMagnitude > 25 * spacing * spacing)
                trail.Clear();   // Teleported: an old trail must not stretch across the jump.
            if (trail.Count == 0 || (trail[trail.Count - 1].Position - origin).sqrMagnitude >= spacing * spacing)
            {
                if (trail.Count == MaxStoredNodes) trail.RemoveAt(0);
                trail.Add(new TrailNode { Position = origin, Strength = strength, BirthTime = time });
            }
            while (trail.Count > 1 && time - trail[0].BirthTime > WaveLifetime) trail.RemoveAt(0);
            return true;
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
            // A hull-relative distance threshold here freezes the whole Kelvin phase near each new
            // sample, then jumps it forward when the hull has moved far enough.
            nodes.Add(new WakeNode { Position = origin, Amplitude = strength, Breaking = strength * strength });
            for (int i = trail.Count - 1; i >= 0; i--)
            {
                TrailNode sample = trail[i];
                // On the exact frame a stored node is born it coincides with the live head.
                // Skip only coincident neighbours, avoiding zero-length wedges without quantizing motion.
                if ((sample.Position - nodes[nodes.Count - 1].Position).sqrMagnitude <= 1e-8f) continue;
                float age = Mathf.Clamp01((time - sample.BirthTime) / Mathf.Max(1, WaveLifetime));
                float fade = 1 - age * age * (3 - 2 * age);
                // Only the first seconds of a wave are steep enough to spill.
                float young = Mathf.Clamp01(1 - (time - sample.BirthTime) / 2.5f);
                nodes.Add(new WakeNode
                {
                    Position = sample.Position, Amplitude = sample.Strength * fade,
                    Breaking = sample.Strength * sample.Strength * young * young
                });
            }
            int count = nodes.Count - start;
            if (count < 2) { nodes.RemoveRange(start, count); return; }

            float travelled = overtake, peak = 0;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < count; i++)
            {
                WakeNode node = nodes[start + i];
                if (i > 0) travelled += (node.Position - nodes[start + i - 1].Position).magnitude;
                // Chord directions keep the lines drawn across the trail consistent with its bends.
                Vector2 chord = nodes[start + Mathf.Min(i + 1, count - 1)].Position - nodes[start + Mathf.Max(i - 1, 0)].Position;
                node.Backward = chord.sqrMagnitude > 1e-6f ? chord.normalized : -direction;
                node.Distance = travelled;
                node.Breaking *= CrestFoam;
                node.Amplitude *= height;
                peak = Mathf.Max(peak, node.Amplitude);
                sum += node.Position;
                nodes[start + i] = node;
            }
            if (peak <= 1e-4f) { nodes.RemoveRange(start, count); return; }

            float froude = Mathf.Max(patternSpeed, 0.05f) / Mathf.Sqrt(9.81f * hullLength);
            OceanWakeSystem.ResolveKelvinSlice(froude, out float slice, out float scaleLow, out float scaleHigh);
            Vector2 boundsCentre = sum / count;
            float radius = 0;
            for (int i = 0; i < count; i++)
                radius = Mathf.Max(radius, (nodes[start + i].Position - boundsCentre).magnitude);
            // The pattern reaches further sideways when it is read at a smaller scale.
            radius += OceanWakeSystem.KelvinWidthExtent * hullLength / Mathf.Max(0.05f, Mathf.Min(scaleLow, scaleHigh));
            ships.Add(new WakeShip
            {
                BoundsCentre = boundsCentre, BoundsRadius = radius, HullLength = hullLength,
                Slice = slice, ScaleLow = scaleLow, ScaleHigh = scaleHigh, NodeStart = start, NodeCount = count
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
