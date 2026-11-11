using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace WaterSystem.Ocean
{
    /// <summary>Bounded CPU event scheduler and allocation-free parameter upload for the camera sky.</summary>
    internal sealed class MeteorSkyState
    {
        const int Capacity = 16, ParentCapacity = 3;
        struct Event
        {
            public bool Active, Nominal;
            public double Start;
            public uint Seed;
        }

        static readonly int ParamsID = Shader.PropertyToID("_MeteorParams");
        static readonly int HeadColorID = Shader.PropertyToID("_MeteorHeadColor");
        static readonly int TailColorID = Shader.PropertyToID("_MeteorTailColor");
        static readonly int SplitColorID = Shader.PropertyToID("_MeteorSplitColor");
        static readonly int CameraVisibleID = Shader.PropertyToID("_MeteorCameraVisible");
        static readonly int DirectionID = Shader.PropertyToID("_MeteorDirection");
        static readonly int TangentID = Shader.PropertyToID("_MeteorTangent");
        static readonly int BitangentID = Shader.PropertyToID("_MeteorBitangent");
        static readonly int TrackID = Shader.PropertyToID("_MeteorTrack");
        static readonly int LifeID = Shader.PropertyToID("_MeteorLife");
        static readonly int StyleID = Shader.PropertyToID("_MeteorStyle");
        static readonly int GeometryID = Shader.PropertyToID("_MeteorGeometry");

        readonly Event[] events = new Event[ParentCapacity];
        readonly Vector4[] directions = new Vector4[Capacity];
        readonly Vector4[] tangents = new Vector4[Capacity];
        readonly Vector4[] bitangents = new Vector4[Capacity];
        readonly Vector4[] tracks = new Vector4[Capacity];
        readonly Vector4[] lives = new Vector4[Capacity];
        readonly Vector4[] styles = new Vector4[Capacity];
        readonly Vector4[] geometry = new Vector4[Capacity];
        uint randomState = 8173, triggerSerial;
        int lastSeed;
        bool seedReady, clockReady, scheduled;
        double lastTime, nextSpawn;

        public int Count { get; private set; }

        public void Reset()
        {
            Array.Clear(events, 0, events.Length);
            Count = 0;
            seedReady = clockReady = scheduled = false;
            lastTime = nextSpawn = 0;
            randomState = 8173;
            triggerSerial = 0;
        }

        /// <summary>Request one event at the nominal orientation, independently of automatic spawning.</summary>
        public void Trigger(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) return;
            AddEvent(now, true, 8173u + ++triggerSerial * 747796405u);
        }

        public void Record(CommandBuffer commands, MeteorSettings settings, double now, Vector3 sunDirection, float exposure)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) now = 0;
            if (clockReady && now < lastTime) Reset();
            clockReady = true;
            lastTime = now;
            if (!seedReady || lastSeed != settings.Seed)
            {
                randomState = unchecked((uint)settings.Seed) ^ 0x9e3779b9u;
                if (randomState == 0) randomState = 1;
                seedReady = true;
                lastSeed = settings.Seed;
                scheduled = false;
            }

            Count = 0;
            float visibility = settings.Visibility(sunDirection);
            double retention = settings.Duration + settings.Afterglow;
            for (int i = 0; i < ParentCapacity; i++)
                if (events[i].Active && now - events[i].Start > retention) events[i].Active = false;

            if (!settings.Enabled)
            {
                Array.Clear(events, 0, events.Length);
                scheduled = false;
            }
            else if (settings.Preview)
            {
                scheduled = false;
                AppendGroup(settings, settings.PreviewTime, true, unchecked((uint)settings.Seed));
            }
            else
            {
                if (settings.Automatic && visibility > 0.01f)
                {
                    if (!scheduled) { nextSpawn = now + settings.FirstDelay; scheduled = true; }
                    if (now >= nextSpawn)
                    {
                        AddEvent(now, false, Next(ref randomState));
                        // Schedule from now rather than replaying missed events after a camera was inactive.
                        nextSpawn = now + Mathf.Lerp(settings.SpawnInterval.x, settings.SpawnInterval.y, Unit(ref randomState));
                    }
                }
                else scheduled = false;
                // If the fixed upload budget fills, prefer recent events to already fading wakes.
                int visited = 0;
                for (int pass = 0; pass < ParentCapacity && Count < Capacity; pass++)
                {
                    int newest = -1;
                    for (int i = 0; i < ParentCapacity; i++)
                        if (events[i].Active && (visited & (1 << i)) == 0 &&
                            (newest < 0 || events[i].Start > events[newest].Start)) newest = i;
                    if (newest < 0) break;
                    visited |= 1 << newest;
                    AppendGroup(settings, (float)(now - events[newest].Start), events[newest].Nominal, events[newest].Seed);
                }
            }

            commands.SetGlobalFloat(CameraVisibleID, 1);
            commands.SetGlobalVector(ParamsID, new Vector4(settings.Intensity * exposure, visibility, Count,
                Mathf.Sin(2 * Mathf.Deg2Rad)));
            commands.SetGlobalVector(HeadColorID, Linear(settings.HeadColor));
            commands.SetGlobalVector(TailColorID, Linear(settings.TailColor));
            commands.SetGlobalVector(SplitColorID, Linear(settings.SplitColor));
            if (Count == 0) return;
            commands.SetGlobalVectorArray(DirectionID, directions);
            commands.SetGlobalVectorArray(TangentID, tangents);
            commands.SetGlobalVectorArray(BitangentID, bitangents);
            commands.SetGlobalVectorArray(TrackID, tracks);
            commands.SetGlobalVectorArray(LifeID, lives);
            commands.SetGlobalVectorArray(StyleID, styles);
            commands.SetGlobalVectorArray(GeometryID, geometry);
        }

        void AddEvent(double now, bool nominal, uint seed)
        {
            int slot = 0;
            for (int i = 0; i < ParentCapacity; i++)
            {
                if (!events[i].Active) { slot = i; break; }
                if (events[i].Start < events[slot].Start) slot = i;
            }
            events[slot] = new Event { Active = true, Nominal = nominal, Start = now, Seed = seed };
        }

        void AppendGroup(MeteorSettings settings, float age, bool nominal, uint seed)
        {
            if (age < 0 || Count >= Capacity) return;
            if (seed == 0) seed = 1;
            float azimuth = settings.Azimuth;
            float elevation = settings.Elevation;
            float angle = settings.PathAngle;
            if (!nominal)
            {
                azimuth += (Unit(ref seed) * 2 - 1) * settings.DirectionVariation;
                elevation = Mathf.Clamp(elevation + (Unit(ref seed) * 2 - 1) * settings.DirectionVariation * 0.4f, 5, 85);
                angle += (Unit(ref seed) * 2 - 1) * settings.DirectionVariation * 0.3f;
            }
            float az = azimuth * Mathf.Deg2Rad, el = elevation * Mathf.Deg2Rad;
            Vector3 centre = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
            Vector3 right = Vector3.Cross(Vector3.up, centre).normalized;
            Vector3 up = Vector3.Cross(centre, right).normalized;
            float rotation = angle * Mathf.Deg2Rad;
            Vector3 along = right * Mathf.Cos(rotation) + up * Mathf.Sin(rotation);
            Vector3 across = Vector3.Cross(centre, along).normalized;
            float half = Mathf.Tan(settings.TravelAngle * 0.5f * Mathf.Deg2Rad);
            float splitX = Mathf.Lerp(-half, half, 0.35f);
            float bend = Mathf.Tan(settings.Bend * Mathf.Deg2Rad);
            float width = Mathf.Tan(settings.TrailWidth * Mathf.Deg2Rad);
            float radius = Mathf.Tan(settings.HeadSize * Mathf.Deg2Rad);
            float noise = Unit(ref seed) * 100;
            Append(centre, along, across, bend, noise, -half, half, age, 0,
                settings.Duration, settings.TailDuration, settings.Afterglow, 1, width, radius, 0, half, splitX);

            for (int branch = 0; branch < settings.SplitCount; branch++)
            {
                float sign = (branch & 1) == 0 ? 1 : -1;
                float slope = sign * Mathf.Tan(settings.SplitAngle * (0.55f + Unit(ref seed) * 0.45f) * Mathf.Deg2Rad);
                float duration = settings.Duration * 0.65f * Mathf.Lerp(0.93f, 1, Unit(ref seed));
                float end = half - half * Mathf.Lerp(0.025f, 0.12f, Unit(ref seed));
                float brightness = Mathf.Lerp(0.2f, 0.55f, Unit(ref seed));
                float tint = Mathf.Lerp(0.6f, 1, Unit(ref seed));
                Append(centre, along, across, bend, noise + branch * 5.73f, splitX, end,
                    age - settings.Duration * 0.35f, slope, duration, settings.TailDuration * 0.8f,
                    settings.Afterglow, brightness, width * 0.65f, radius * 0.65f, tint, half, splitX);
            }
            for (int fragment = 0; fragment < settings.Fragments; fragment++)
            {
                float startFraction = Mathf.Lerp(0.45f, 0.7f, Unit(ref seed));
                float start = Mathf.Lerp(-half, half, startFraction);
                float slope = (Unit(ref seed) * 2 - 1) * Mathf.Tan(settings.SplitAngle * 1.8f * Mathf.Deg2Rad);
                float duration = settings.Duration * (1 - startFraction) * Mathf.Lerp(0.94f, 1, Unit(ref seed));
                float brightness = Mathf.Lerp(0.1f, 0.3f, Unit(ref seed));
                float trail = Mathf.Lerp(0.15f, 0.4f, Unit(ref seed));
                Append(centre, along, across, bend, noise + fragment * 8.91f, start, half,
                    age - settings.Duration * startFraction, slope, duration, trail,
                    Mathf.Min(settings.Afterglow, 0.6f), brightness, width * 0.35f,
                    Mathf.Tan(0.04f * Mathf.Deg2Rad), Unit(ref seed) * 0.7f, half, start);
            }
        }

        void Append(Vector3 centre, Vector3 along, Vector3 across, float bend, float seed,
            float start, float end, float age, float slope, float duration, float trail, float afterglow,
            float brightness, float width, float radius, float tint, float half, float splitX)
        {
            if (Count >= Capacity || age < 0 || age >= duration + afterglow) return;
            int i = Count++;
            directions[i] = centre;
            tangents[i] = new Vector4(along.x, along.y, along.z, bend);
            bitangents[i] = new Vector4(across.x, across.y, across.z, seed);
            tracks[i] = new Vector4(start, end, age, slope);
            lives[i] = new Vector4(duration, trail, afterglow, brightness);
            styles[i] = new Vector4(width, radius, tint, 0);
            geometry[i] = new Vector4(half, splitX, 0, 0);
        }

        static Color Linear(Color color) => QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
        static uint Next(ref uint state)
        {
            // Private xorshift state; never disturbs UnityEngine.Random used by gameplay.
            if (state == 0) state = 1;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }
        static float Unit(ref uint state) => (Next(ref state) & 0x00ffffffu) / 16777216f;
    }
}
