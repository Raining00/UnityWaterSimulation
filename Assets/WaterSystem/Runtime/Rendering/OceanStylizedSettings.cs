using System;
using UnityEngine;

namespace WaterSystem.Ocean
{
    public enum OceanShadingMode { Physical = 0, Stylized = 1 }

    [Serializable]
    public sealed class OceanStylizedSettings
    {
        [Header("Reference palette (shallow turquoise / blue open sea)")]
        public Color ShallowColor = new Color(0.05f, 0.57f, 0.62f);
        public Color TurquoiseColor = new Color(0.005f, 0.30f, 0.43f);
        public Color DeepColor = new Color(0.006f, 0.075f, 0.20f);
        public Color HorizonColor = new Color(0.17f, 0.37f, 0.52f);
        [Min(0.1f)] public float ShallowDepth = 2.5f;
        [Min(0.1f)] public float DeepDepth = 22;
        [Range(0.01f, 3)] public float DepthDensity = 0.45f;
        [Range(0, 0.2f)] public float ViewDensity = 0.025f;
        [Min(1)] public float HorizonBlendDistance = 1400;
        [Header("FFT and scrolling detail normals")]
        [Range(0, 3)] public float NormalStrength = 1.15f;
        [Range(0, 2)] public float DetailNormalStrength = 0.4f;
        [Min(0.001f)] public float NormalScale = 0.09f;
        public Vector2 NormalSpeed = new Vector2(0.018f, 0.009f);
        [Min(1)] public float DetailFadeDistance = 450;
        [Header("Reflection, light and refraction")]
        public Color SkyHorizon = new Color(0.30f, 0.53f, 0.72f);
        public Color SkyZenith = new Color(0.035f, 0.18f, 0.42f);
        [Tooltip("Blend the packaged cloud panorama into the fallback sky. Set to zero for a plain sky gradient. Scene reflection probes take precedence.")]
        [Range(0, 1)] public float SkyTextureStrength = 0.65f;
        [Range(0, 1)] public float ReflectionStrength = 0.55f;
        [Range(1, 8)] public float FresnelPower = 4;
        [Range(0, 2)] public float SunHighlight = 0.45f;
        [Range(0.04f, 0.7f)] public float Roughness = 0.24f;
        [Range(0, 0.08f)] public float RefractionStrength = 0.012f;
        [Header("FFT whitecaps and depth intersection foam")]
        public Color FoamColor = new Color(0.93f, 0.99f, 1);
        [Range(0, 3)] public float FoamStrength = 1.25f;
        [Min(0.001f)] public float FoamScale = 0.17f;
        [Tooltip("Depth-based shore appearance only; does not simulate shallow-water dynamics.")]
        [Range(0, 2)] public float ShoreFoamStrength = 0.85f;
        [Min(0.01f)] public float ShoreFoamDepth = 1.3f;
        [Min(0.001f)] public float ShoreNoiseScale = 0.18f;
        [Range(0, 2)] public float ShoreFlowSpeed = 0.25f;
        [Header("Seabed caustics and URP textures")]
        [Min(0.001f)] public float CausticScale = 0.12f;
        [Range(0, 3)] public float CausticStrength = 0.45f;
        [Min(0.1f)] public float CausticDepth = 8;
        [Tooltip("Enable URP Depth Texture and Opaque Texture on the camera/pipeline. Disable for an opaque color-only fallback.")]
        public bool UseSceneTextures = true;

        public void Apply(Material material, OceanResources resources)
        {
            material.SetColor("_ShallowColor", ShallowColor);
            material.SetColor("_TurquoiseColor", TurquoiseColor);
            material.SetColor("_DeepColor", DeepColor);
            material.SetColor("_HorizonColor", HorizonColor);
            material.SetVector("_WaterDepth", new Vector4(Mathf.Max(0.1f, ShallowDepth), Mathf.Max(ShallowDepth + 0.1f, DeepDepth), Mathf.Max(0.01f, DepthDensity), Mathf.Max(0, ViewDensity)));
            material.SetFloat("_HorizonBlendDistance", Mathf.Max(1, HorizonBlendDistance));
            material.SetFloat("_NormalStrength", Mathf.Max(0, NormalStrength));
            material.SetFloat("_DetailNormalStrength", Mathf.Max(0, DetailNormalStrength));
            material.SetFloat("_NormalScale", Mathf.Max(0.001f, NormalScale));
            material.SetVector("_NormalSpeed", NormalSpeed);
            material.SetFloat("_DetailFadeDistance", Mathf.Max(1, DetailFadeDistance));
            material.SetColor("_SkyHorizon", SkyHorizon);
            material.SetColor("_SkyZenith", SkyZenith);
            material.SetFloat("_SkyTextureStrength", Mathf.Clamp01(SkyTextureStrength));
            material.SetFloat("_ReflectionStrength", Mathf.Clamp01(ReflectionStrength));
            material.SetFloat("_FresnelPower", Mathf.Max(1, FresnelPower));
            material.SetFloat("_SunHighlight", Mathf.Max(0, SunHighlight));
            material.SetFloat("_Roughness", Mathf.Clamp(Roughness, 0.04f, 0.7f));
            material.SetFloat("_RefractionStrength", Mathf.Clamp(RefractionStrength, 0, 0.08f));
            material.SetColor("_FoamColor", FoamColor);
            material.SetFloat("_FoamStrength", Mathf.Max(0, FoamStrength));
            material.SetFloat("_FoamScale", Mathf.Max(0.001f, FoamScale));
            material.SetVector("_ShoreFoam", new Vector4(Mathf.Max(0, ShoreFoamStrength), Mathf.Max(0.01f, ShoreFoamDepth), Mathf.Max(0.001f, ShoreNoiseScale), Mathf.Max(0, ShoreFlowSpeed)));
            material.SetFloat("_CausticScale", Mathf.Max(0.001f, CausticScale));
            material.SetFloat("_CausticStrength", Mathf.Max(0, CausticStrength));
            material.SetFloat("_CausticDepth", Mathf.Max(0.1f, CausticDepth));
            material.SetFloat("_UseSceneTextures", UseSceneTextures ? 1 : 0);
            material.SetTexture("_DetailNormal", resources.StylizedNormal);
            material.SetTexture("_FoamTex", resources.StylizedFoam);
            material.SetTexture("_ShoreNoise", resources.StylizedIntersection);
            material.SetTexture("_CausticTex", resources.StylizedCaustic);
            material.SetTexture("_SkyTex", resources.StylizedSky);
        }
    }
}
