using System;
using UnityEngine;

namespace WaterSystem.Ocean
{
    [Serializable]
    public sealed class OceanStylizedUnderwaterSettings
    {
        public bool Enabled = true;

        [Header("Water volume")]
        [Tooltip("The near and far fog colors follow the stylized surface palette.")]
        [Range(0f, 0.3f)] public float FogDensity = 0.09f;
        [Min(0f)] public float FogStartDistance = 0f;
        [Min(1f)] public float MaximumDistance = 120f;
        [Range(0f, 2f)] public float FogBrightness = 0.85f;
        [Min(0f)] public float DeepFogStart = 10f;
        [Range(0f, 0.3f)] public float DeepFogDensity = 0.035f;
        [Range(0f, 1f)] public float DeepFogBrightness = 0.55f;

        [Header("FFT waterline")]
        public Color WaterlineColor = new Color(0.62f, 0.94f, 0.96f);
        [Range(0f, 1f)] public float WaterlineStrength = 0.35f;
        [Range(0.001f, 0.2f)] public float WaterlineSoftness = 0.025f;

        [Header("Flowing refraction")]
        [Range(0f, 0.05f)] public float DistortionStrength = 0.008f;
        [Min(0.001f)] public float DistortionScale = 0.025f;
        [Range(0f, 2f)] public float DistortionSpeed = 0.12f;
        [Min(0.1f)] public float DistortionDistance = 18f;

        [Header("Seabed light")]
        [Range(0f, 3f)] public float CausticStrength = 0.9f;
        [Tooltip("How far the seabed caustics remain visible below the surface.")]
        [Min(0.1f)] public float CausticDepth = 20f;
        [Range(0f, 3f)] public float CausticAnimationSpeed = 0.5f;
        [Range(0f, 2f)] public float ShaftStrength = 0.18f;
        [Tooltip("Light shaft pattern scale relative to the seabed caustics. 1 matches them; below 1 makes the beams wider, above 1 tighter.")]
        [Range(0.05f, 1f)] public float ShaftPatternScale = 1f;
        [Tooltip("Maximum visible length of sunbeams through the water.")]
        [Range(5f, 100f)] public float ShaftMaximumDistance = 50f;

        internal void Bind(MaterialPropertyBlock properties, OceanStylizedSettings surface,
            OceanResources resources)
        {
            properties.SetColor("_StylizedNearColor",
                Color.Lerp(surface.ShallowColor, surface.TurquoiseColor, 0.35f));
            properties.SetColor("_StylizedFarColor", surface.DeepColor);
            properties.SetVector("_StylizedFog", new Vector4(Mathf.Max(0f, FogDensity),
                Mathf.Max(0f, FogStartDistance), Mathf.Max(1f, MaximumDistance),
                Mathf.Max(0f, FogBrightness)));
            properties.SetVector("_StylizedDepthFog", new Vector4(Mathf.Max(0f, DeepFogStart),
                Mathf.Max(0f, DeepFogDensity), Mathf.Clamp01(DeepFogBrightness), 0f));
            properties.SetColor("_StylizedWaterlineColor", WaterlineColor);
            properties.SetVector("_StylizedWaterline", new Vector4(
                Mathf.Max(0.001f, WaterlineSoftness), Mathf.Clamp01(WaterlineStrength), 0f, 0f));
            properties.SetVector("_StylizedDistortion", new Vector4(
                Mathf.Max(0f, DistortionStrength), Mathf.Max(0.001f, DistortionScale),
                Mathf.Max(0f, DistortionSpeed), Mathf.Max(0.1f, DistortionDistance)));
            properties.SetVector("_StylizedCaustics", new Vector4(
                Mathf.Max(0.001f, surface.CausticScale), Mathf.Max(0f, CausticStrength),
                Mathf.Max(0.1f, CausticDepth), Mathf.Max(0f, CausticAnimationSpeed)));
            properties.SetVector("_StylizedShafts", new Vector4(
                Mathf.Max(0f, ShaftStrength), Mathf.Clamp(ShaftPatternScale, 0.05f, 1f),
                Mathf.Clamp(ShaftMaximumDistance, 5f, 100f), 0f));
            properties.SetTexture("_StylizedCausticTex", resources.StylizedCaustic);
            properties.SetTexture("_StylizedNoiseTex", resources.StylizedIntersection);
            float width = resources.StylizedCaustic != null ? resources.StylizedCaustic.width : 512f;
            float height = resources.StylizedCaustic != null ? resources.StylizedCaustic.height : 512f;
            properties.SetVector("_StylizedCausticTex_TexelSize",
                new Vector4(1f / width, 1f / height, width, height));
        }
    }
}
