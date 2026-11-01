using System;
using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>Shared surface-only night palette, moon highlight and foam emission for both ocean shaders.</summary>
    [Serializable]
    public sealed class OceanNightSettings
    {
        public bool Enabled = true;
        [Tooltip("Blend by the atmosphere sun's elevation, or the main light when no atmosphere is active.")]
        public bool Automatic = true;
        [Tooltip("Used when Automatic is off: 0 is day and 1 is full night.")]
        [Range(0, 1)] public float ManualBlend = 1;
        [Range(-20, 5)] public float FadeStartElevation = -2;
        [Range(-40, 0)] public float FullNightElevation = -12;

        [Header("Night ocean palette")]
        public Color DeepColor = new Color(0.018f, 0.065f, 0.18f);
        public Color ShallowColor = new Color(0.025f, 0.20f, 0.34f);
        public Color HorizonColor = new Color(0.055f, 0.14f, 0.29f);
        [Tooltip("Artistic surface fill, independent of moon visibility. Keeps the night sea readable.")]
        [Range(0, 2)] public float AmbientIntensity = 0.85f;
        [Tooltip("Blue fill mixed with the live night sky reflection. Zero uses only the sky/probes.")]
        [Range(0, 1)] public float ReflectionFill = 0.35f;

        [Header("Moonlight on this ocean only")]
        [Tooltip("Uses Atmosphere/Moon direction, tint, brightness and phase. Does not create a scene Light.")]
        [Range(0, 2)] public float MoonLightIntensity = 0.06f;
        [Tooltip("Additional surface-only tint. A cool blue balances the warm atmospheric colour of a low moon.")]
        public Color MoonTint = new Color(0.60f, 0.78f, 1);
        [Range(0.04f, 0.7f)] public float MoonRoughness = 0.26f;

        [Header("Bioluminescent foam")]
        [ColorUsage(false, true)] public Color FoamColor = new Color(0.04f, 0.65f, 1);
        [Tooltip("HDR emission from the existing FFT, wake and intersection foam masks. Bloom supplies the surrounding glow.")]
        [Range(0, 20)] public float FoamEmission = 6f;
        [Tooltip("Brightness variation from the existing foam texture. No new noise or simulation is allocated.")]
        [Range(0, 1)] public float FoamDetail = 0.65f;
        [Tooltip("Below 1 reveals faint foam; above 1 concentrates glow in dense foam. Zero coverage always stays dark.")]
        [Range(0.25f, 2)] public float FoamResponse = 0.7f;

        static readonly int ControlId = Shader.PropertyToID("_OceanNightControl");
        static readonly int TransitionId = Shader.PropertyToID("_OceanNightTransition");
        static readonly int LightingId = Shader.PropertyToID("_OceanNightLighting");
        static readonly int DeepId = Shader.PropertyToID("_OceanNightDeep");
        static readonly int ShallowId = Shader.PropertyToID("_OceanNightShallow");
        static readonly int HorizonId = Shader.PropertyToID("_OceanNightHorizon");
        static readonly int GlowId = Shader.PropertyToID("_OceanNightGlow");
        static readonly int MoonTintId = Shader.PropertyToID("_OceanNightMoonTint");

        public void Apply(Material material)
        {
            // Validate at application as well: runtime C# edits do not invoke OnValidate.
            float start = Mathf.Clamp(Finite(FadeStartElevation, -2), -20, 5);
            float full = Mathf.Clamp(Finite(FullNightElevation, -12), -40, start - 0.5f);
            material.SetVector(ControlId, new Vector4(Enabled ? 1 : 0, Automatic ? 1 : 0,
                Mathf.Clamp01(Finite(ManualBlend, 1)), Mathf.Clamp(Finite(FoamResponse, 0.7f), 0.25f, 2)));
            material.SetVector(TransitionId, new Vector4(Mathf.Sin(start * Mathf.Deg2Rad),
                Mathf.Sin(full * Mathf.Deg2Rad), Mathf.Clamp(Finite(AmbientIntensity, 0.85f), 0, 2),
                Mathf.Clamp01(Finite(ReflectionFill, 0.35f))));
            material.SetVector(LightingId, new Vector4(Mathf.Clamp(Finite(MoonLightIntensity, 0.06f), 0, 2),
                Mathf.Clamp(Finite(MoonRoughness, 0.26f), 0.04f, 0.7f),
                Mathf.Clamp(Finite(FoamEmission, 6f), 0, 20), Mathf.Clamp01(Finite(FoamDetail, 0.65f))));
            material.SetColor(DeepId, DeepColor);
            material.SetColor(ShallowId, ShallowColor);
            material.SetColor(HorizonId, HorizonColor);
            material.SetColor(GlowId, FoamColor);
            material.SetColor(MoonTintId, MoonTint);
        }

        static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }
}
