using System;
using UnityEngine;

namespace WaterSystem.Ocean
{
    [Serializable]
    public sealed class OceanSpraySettings
    {
        public bool Enabled = true;
        [Tooltip("Fixed GPU particle capacity per camera. No CPU readback or GameObjects per particle.")]
        [Range(256, 65536)] public int Capacity = 8192;
        [Tooltip("Emission radius around each camera in world metres; distant water uses surface foam only.")]
        [Range(5, 200)] public float Radius = 45;
        [Tooltip("Candidate locations tested per simulation second, before foam/breaking rejection.")]
        [Min(0)] public float EmissionRate = 12000;
        [Tooltip("Minimum visible surface foam coverage, including FoamStrength and foam detail texture.")]
        [Range(0, 0.95f)] public float FoamThreshold = 0.12f;
        [Tooltip("Reject old foam where current Jacobian compression is no longer producing whitecaps.")]
        [Range(0, 0.95f)] public float BreakingThreshold = 0.02f;
        [Tooltip("Minimum/maximum lifetime in ocean simulation seconds.")]
        public Vector2 Lifetime = new Vector2(0.65f, 1.6f);
        [Tooltip("Minimum/maximum droplet diameter in metres.")]
        public Vector2 Size = new Vector2(0.06f, 0.18f);
        [Tooltip("Minimum/maximum upward launch speed in metres per simulation second.")]
        public Vector2 UpwardSpeed = new Vector2(2, 4);
        [Range(0, 1)] public float WindInfluence = 0.25f;
        [Range(0, 5)] public float Drag = 1.2f;
        [Range(0, 2)] public float GravityScale = 0.8f;
        [Range(0, 1)] public float MistFraction = 0.2f;
        [Range(1, 8)] public float MistSizeMultiplier = 3.5f;
        [Range(0, 1)] public float Opacity = 0.65f;
        [Min(0.01f)] public float SoftIntersectionDistance = 0.35f;
        public Color Tint = Color.white;
        [Header("Breaking-wave motion and splash detail")]
        [Range(0, 2)] public float WaveVelocityInheritance = 0.8f;
        [Range(0, 20)] public float MaxInheritedSpeed = 8;
        [Tooltip("Fraction of non-mist particles using the four-pattern splash atlas.")]
        [Range(0, 1)] public float SplashFraction = 0.35f;
        [Range(1, 16)] public float SplashSizeMultiplier = 12;
        [Range(0, 2)] public float BurstSpread = 0.6f;
        [Range(0, 3)] public float Turbulence = 0.65f;
        [Range(0, 3)] public float HighlightStrength = 0.8f;
        [Range(0, 2)] public float Backlighting = 0.6f;
        public bool ReceiveSunShadows = true;
        [Tooltip("Maximum visible particles per 64x64 pixel tile. Simulation continues for hidden particles.")]
        [Range(8, 256)] public int MaxParticlesPerTile = 64;

        internal void Validate()
        {
            Capacity = Mathf.Clamp(Capacity, 256, 65536);
            Radius = Mathf.Clamp(Finite(Radius, 45), 5, 200);
            EmissionRate = Mathf.Clamp(Finite(EmissionRate, 12000), 0, 1000000);
            FoamThreshold = Mathf.Clamp(Finite(FoamThreshold, 0.12f), 0, 0.95f);
            BreakingThreshold = Mathf.Clamp(Finite(BreakingThreshold, 0.02f), 0, 0.95f);
            Lifetime = Range(Lifetime, 0.1f, 5);
            Size = Range(Size, 0.01f, 2);
            UpwardSpeed = Range(UpwardSpeed, 0, 12);
            WindInfluence = Mathf.Clamp01(Finite(WindInfluence, 0.25f));
            Drag = Mathf.Clamp(Finite(Drag, 1.2f), 0, 5);
            GravityScale = Mathf.Clamp(Finite(GravityScale, 0.8f), 0, 2);
            MistFraction = Mathf.Clamp01(Finite(MistFraction, 0.2f));
            MistSizeMultiplier = Mathf.Clamp(Finite(MistSizeMultiplier, 3.5f), 1, 8);
            Opacity = Mathf.Clamp01(Finite(Opacity, 0.65f));
            SoftIntersectionDistance = Mathf.Max(0.01f, Finite(SoftIntersectionDistance, 0.35f));
            WaveVelocityInheritance = Mathf.Clamp(Finite(WaveVelocityInheritance, 0.8f), 0, 2);
            MaxInheritedSpeed = Mathf.Clamp(Finite(MaxInheritedSpeed, 8), 0, 20);
            SplashFraction = Mathf.Clamp01(Finite(SplashFraction, 0.35f));
            SplashSizeMultiplier = Mathf.Clamp(Finite(SplashSizeMultiplier, 12), 1, 16);
            BurstSpread = Mathf.Clamp(Finite(BurstSpread, 0.6f), 0, 2);
            Turbulence = Mathf.Clamp(Finite(Turbulence, 0.65f), 0, 3);
            HighlightStrength = Mathf.Clamp(Finite(HighlightStrength, 0.8f), 0, 3);
            Backlighting = Mathf.Clamp(Finite(Backlighting, 0.6f), 0, 2);
            MaxParticlesPerTile = Mathf.Clamp(MaxParticlesPerTile, 8, 256);
        }
        static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        static Vector2 Range(Vector2 value, float min, float max)
        {
            float low = Mathf.Clamp(Finite(value.x, min), min, max);
            return new Vector2(low, Mathf.Clamp(Finite(value.y, low), low, max));
        }
    }
}
