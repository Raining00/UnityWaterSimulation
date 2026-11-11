using System;
using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>High-altitude luminous curtains rendered in the camera sky, independently of ocean lighting.</summary>
    [Serializable]
    public sealed class AuroraSettings
    {
        public bool Enabled = true;
        [Min(0)] public float Intensity = 1.5f;
        [ColorUsage(false, true)] public Color LowerColor = new Color(0.08f, 1, 0.38f);
        [ColorUsage(false, true)] public Color UpperColor = new Color(0.55f, 0.18f, 0.95f);
        [Header("Placement (world orientation; distances in kilometres)")]
        [Tooltip("World azimuth of the curtain region. Zero faces +Z; 90 faces +X.")]
        [Range(-180, 180)] public float Azimuth;
        [Min(20)] public float Distance = 250;
        [Min(50)] public float HorizontalExtent = 900;
        [Min(50)] public float BottomAltitude = 100;
        [Min(20)] public float Height = 140;
        [Header("Curtain shape")]
        [Range(1, 4)] public int CurtainCount = 3;
        [Min(5)] public float CurtainSpacing = 70;
        [Min(1)] public float Width = 12;
        [Min(20)] public float FoldScale = 250;
        [Min(0)] public float FoldStrength = 80;
        [Range(0, 1)] public float RayStrength = 0.85f;
        [Tooltip("Optional seamless linear noise data. Empty uses the original packaged aurora noise.")]
        public Texture2D NoiseTexture;
        [Header("Motion")]
        [Tooltip("Animation cycles per minute. Zero freezes the curtain shape.")]
        [Range(0, 4)] public float Speed = 0.25f;
        [Tooltip("Cycle offset; useful for choosing a frozen pose when Speed is zero.")]
        [Range(0, 1)] public float AnimationOffset;
        [Header("Visibility and quality")]
        [Tooltip("Fade by sun elevation independently of the stars/galaxy switch.")]
        public bool AutomaticNight = true;
        [Range(0, 1)] public float ManualVisibility = 1;
        [Range(-30, 5)] public float FadeStartElevation = -4;
        [Range(-40, 0)] public float FullNightElevation = -14;
        [Range(1, 15)] public float HorizonFade = 3;
        [Tooltip("Sky ray integration samples. Higher values resolve narrow distant curtains more accurately.")]
        [Range(16, 64)] public int Samples = 32;

        public void Validate()
        {
            Intensity = Mathf.Clamp(AtmosphereSettings.Finite(Intensity, 1.5f), 0, 20);
            Azimuth = Mathf.Clamp(AtmosphereSettings.Finite(Azimuth, 0), -180, 180);
            Distance = Mathf.Clamp(AtmosphereSettings.Finite(Distance, 250), 20, 2000);
            HorizontalExtent = Mathf.Clamp(AtmosphereSettings.Finite(HorizontalExtent, 900), 50, 3000);
            BottomAltitude = Mathf.Clamp(AtmosphereSettings.Finite(BottomAltitude, 100), 50, 500);
            Height = Mathf.Clamp(AtmosphereSettings.Finite(Height, 140), 20, 500);
            CurtainCount = Mathf.Clamp(CurtainCount, 1, 4);
            CurtainSpacing = Mathf.Clamp(AtmosphereSettings.Finite(CurtainSpacing, 70), 5, 300);
            Width = Mathf.Clamp(AtmosphereSettings.Finite(Width, 12), 1, 100);
            FoldScale = Mathf.Clamp(AtmosphereSettings.Finite(FoldScale, 250), 20, 1000);
            FoldStrength = Mathf.Clamp(AtmosphereSettings.Finite(FoldStrength, 80), 0, 300);
            RayStrength = Mathf.Clamp01(AtmosphereSettings.Finite(RayStrength, 0.85f));
            Speed = Mathf.Clamp(AtmosphereSettings.Finite(Speed, 0.25f), 0, 4);
            AnimationOffset = Mathf.Repeat(AtmosphereSettings.Finite(AnimationOffset, 0), 1);
            ManualVisibility = Mathf.Clamp01(AtmosphereSettings.Finite(ManualVisibility, 1));
            FadeStartElevation = Mathf.Clamp(AtmosphereSettings.Finite(FadeStartElevation, -4), -30, 5);
            FullNightElevation = Mathf.Clamp(AtmosphereSettings.Finite(FullNightElevation, -14), -40, FadeStartElevation - 0.5f);
            HorizonFade = Mathf.Clamp(AtmosphereSettings.Finite(HorizonFade, 3), 1, 15);
            Samples = Mathf.Clamp(Samples, 16, 64);
        }

        public float Visibility(Vector3 sunDirection)
        {
            if (!Enabled) return 0;
            if (!AutomaticNight) return ManualVisibility;
            float elevation = Mathf.Asin(Mathf.Clamp(sunDirection.y, -1, 1)) * Mathf.Rad2Deg;
            float t = Mathf.InverseLerp(FadeStartElevation, FullNightElevation, elevation);
            return t * t * (3 - 2 * t);
        }
    }
}
