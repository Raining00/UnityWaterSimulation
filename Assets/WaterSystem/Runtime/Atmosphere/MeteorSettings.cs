using System;
using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>Art-directed celestial streaks with luminous heads, split trails and fading wakes.</summary>
    [Serializable]
    public sealed class MeteorSettings
    {
        public bool Enabled = true;
        [Min(0)] public float Intensity = 8;
        [ColorUsage(false, true)] public Color HeadColor = new Color(0.72f, 0.94f, 1);
        [ColorUsage(false, true)] public Color TailColor = new Color(0.05f, 0.5f, 1);
        [ColorUsage(false, true)] public Color SplitColor = new Color(0.95f, 0.12f, 0.55f);

        [Header("Flight path (world orientation; angles in degrees)")]
        [Tooltip("Centre of the visible flight. Zero azimuth faces +Z; 90 faces +X.")]
        [Range(-180, 180)] public float Azimuth;
        [Range(5, 85)] public float Elevation = 45;
        [Tooltip("Flight angle in the sky tangent plane. Zero moves right; negative angles descend.")]
        [Range(-180, 180)] public float PathAngle = -25;
        [Range(5, 100)] public float TravelAngle = 60;
        [Tooltip("Time taken to cross the complete path, in seconds.")]
        [Min(0.5f)] public float Duration = 6;
        [Tooltip("Age of the luminous wake behind the moving head, in seconds.")]
        [Min(0.1f)] public float TailDuration = 3;
        [Tooltip("Time for the deposited trail to disappear after flight, in seconds.")]
        [Min(0.1f)] public float Afterglow = 3;
        [Tooltip("Angular radius of the luminous head, in degrees.")]
        [Range(0.01f, 1)] public float HeadSize = 0.15f;
        [Tooltip("Angular half-width of the bright trail, in degrees.")]
        [Range(0.005f, 0.5f)] public float TrailWidth = 0.065f;
        [Tooltip("Maximum bend in degrees. Limited to 20% of Travel Angle to keep narrow paths continuous.")]
        [Range(-20, 20)] public float Bend = 4;

        [Header("Breakup")]
        [Range(0, 3)] public int SplitCount = 3;
        [Tooltip("Divergence of the split trails relative to the main path, in degrees.")]
        [Range(0, 20)] public float SplitAngle = 5;
        [Range(0, 5)] public int Fragments = 3;

        [Header("Automatic events")]
        public bool Automatic = true;
        [Tooltip("Minimum and maximum delay between events, in seconds.")]
        public Vector2 SpawnInterval = new Vector2(14, 24);
        [Min(0)] public float FirstDelay = 1.5f;
        [Tooltip("Random orientation variation around the configured path, in degrees.")]
        [Range(0, 60)] public float DirectionVariation = 12;
        public int Seed = 8173;

        [Header("Visibility")]
        [Tooltip("Fade by sun elevation independently of the stars and galaxy switch.")]
        public bool AutomaticNight = true;
        [Range(0, 1)] public float ManualVisibility = 1;
        [Range(-30, 5)] public float FadeStartElevation = -4;
        [Range(-40, 0)] public float FullNightElevation = -14;

        [Header("Preview")]
        [Tooltip("Freeze one event at the configured orientation. Overrides automatic and triggered events.")]
        public bool Preview;
        [Tooltip("Seconds since the preview event began. Disable Preview to resume live events.")]
        [Min(0)] public float PreviewTime = 4;

        public void Validate()
        {
            Intensity = Clamp(Intensity, 8, 0, 100);
            HeadColor = ValidateColor(HeadColor, new Color(0.72f, 0.94f, 1));
            TailColor = ValidateColor(TailColor, new Color(0.05f, 0.5f, 1));
            SplitColor = ValidateColor(SplitColor, new Color(0.95f, 0.12f, 0.55f));
            Azimuth = Clamp(Azimuth, 0, -180, 180);
            Elevation = Clamp(Elevation, 45, 5, 85);
            PathAngle = Clamp(PathAngle, -25, -180, 180);
            TravelAngle = Clamp(TravelAngle, 60, 5, 100);
            Duration = Clamp(Duration, 6, 0.5f, 60);
            TailDuration = Clamp(TailDuration, 3, 0.1f, 30);
            Afterglow = Clamp(Afterglow, 3, 0.1f, 30);
            HeadSize = Clamp(HeadSize, 0.15f, 0.01f, 1);
            TrailWidth = Clamp(TrailWidth, 0.065f, 0.005f, 0.5f);
            float maximumBend = Mathf.Min(20, TravelAngle * 0.2f);
            Bend = Clamp(Bend, 4, -maximumBend, maximumBend);
            SplitCount = Mathf.Clamp(SplitCount, 0, 3);
            SplitAngle = Clamp(SplitAngle, 5, 0, 20);
            Fragments = Mathf.Clamp(Fragments, 0, 5);
            SpawnInterval.x = Clamp(SpawnInterval.x, 14, 1, 600);
            SpawnInterval.y = Clamp(SpawnInterval.y, 24, SpawnInterval.x, 600);
            FirstDelay = Clamp(FirstDelay, 1.5f, 0, 600);
            DirectionVariation = Clamp(DirectionVariation, 12, 0, 60);
            ManualVisibility = Clamp(ManualVisibility, 1, 0, 1);
            FadeStartElevation = Clamp(FadeStartElevation, -4, -30, 5);
            FullNightElevation = Clamp(FullNightElevation, -14, -40, FadeStartElevation - 0.5f);
            PreviewTime = Clamp(PreviewTime, 4, 0, Duration + Afterglow);
        }

        public float Visibility(Vector3 sunDirection)
        {
            if (!Enabled) return 0;
            if (!AutomaticNight) return ManualVisibility;
            float elevation = Mathf.Asin(Mathf.Clamp(sunDirection.y, -1, 1)) * Mathf.Rad2Deg;
            float t = Mathf.InverseLerp(FadeStartElevation, FullNightElevation, elevation);
            return t * t * (3 - 2 * t);
        }

        static float Clamp(float value, float fallback, float minimum, float maximum) =>
            Mathf.Clamp(AtmosphereSettings.Finite(value, fallback), minimum, maximum);

        static Color ValidateColor(Color value, Color fallback) => new Color(
            Clamp(value.r, fallback.r, 0, 100), Clamp(value.g, fallback.g, 0, 100), Clamp(value.b, fallback.b, 0, 100), 1);
    }
}
