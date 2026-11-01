using System;
using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>High-altitude background emission, separate from the low-resolution air-scattering LUT.</summary>
    [Serializable]
    public sealed class NightSkySettings
    {
        public bool Enabled = true;
        [Header("Milky Way")]
        [Tooltip("Optional linear HDR 2:1 equirectangular map. Empty uses the packaged NASA Milky Way.")]
        public Texture2D GalaxyTexture;
        [Min(0)] public float GalaxyIntensity = 0.35f;
        public Color Tint = Color.white;
        [Tooltip("Orientation of the entire celestial sphere in world degrees. Shared by the galaxy and stars.")]
        public Vector3 Rotation = new Vector3(15, 0, 25);
        [Header("Stars")]
        [Min(0)] public float StarIntensity = 3;
        [Range(0, 1)] public float StarDensity = 0.2f;
        [Range(0.25f, 4)] public float StarSize = 1;
        [Range(0, 1)] public float TwinkleStrength = 0.18f;
        [Header("Visibility")]
        [Tooltip("Sun elevation in degrees where the night sky starts becoming visible.")]
        [Range(-30, 5)] public float FadeStartElevation = -3;
        [Tooltip("Sun elevation in degrees where night sky emission reaches full strength.")]
        [Range(-40, 0)] public float FullNightElevation = -15;
        [Tooltip("Fade stars and galaxy into haze within this angle above the horizon.")]
        [Range(0.1f, 20)] public float HorizonFade = 4;
        [Header("Optional celestial rotation")]
        public bool AutomaticRotation;
        [Min(1)] public float RotationPeriodMinutes = 1440;

        public void Validate()
        {
            GalaxyIntensity = Mathf.Max(0, AtmosphereSettings.Finite(GalaxyIntensity, 0.35f));
            StarIntensity = Mathf.Max(0, AtmosphereSettings.Finite(StarIntensity, 3));
            StarDensity = Mathf.Clamp01(AtmosphereSettings.Finite(StarDensity, 0.2f));
            StarSize = Mathf.Clamp(AtmosphereSettings.Finite(StarSize, 1), 0.25f, 4);
            TwinkleStrength = Mathf.Clamp01(AtmosphereSettings.Finite(TwinkleStrength, 0.18f));
            Rotation = AtmosphereSettings.Finite(Rotation, new Vector3(15, 0, 25));
            FadeStartElevation = Mathf.Clamp(AtmosphereSettings.Finite(FadeStartElevation, -3), -30, 5);
            FullNightElevation = Mathf.Clamp(AtmosphereSettings.Finite(FullNightElevation, -15), -40, FadeStartElevation - 0.5f);
            HorizonFade = Mathf.Clamp(AtmosphereSettings.Finite(HorizonFade, 4), 0.1f, 20);
            RotationPeriodMinutes = Mathf.Max(1, AtmosphereSettings.Finite(RotationPeriodMinutes, 1440));
        }

        public float Visibility(Vector3 sunDirection)
        {
            if (!Enabled) return 0;
            float elevation = Mathf.Asin(Mathf.Clamp(sunDirection.y, -1, 1)) * Mathf.Rad2Deg;
            float t = Mathf.InverseLerp(FadeStartElevation, FullNightElevation, elevation);
            return t * t * (3 - 2 * t);
        }
    }
}
