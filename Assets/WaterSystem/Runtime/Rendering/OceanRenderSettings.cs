using System;
using UnityEngine;

namespace WaterSystem.Ocean
{
    [Serializable]
    public sealed class OceanRenderSettings
    {
        [Header("Water color and light absorption")]
        public Color DeepColor = new Color(0.006f,0.09f,0.12f);
        public Color ShallowColor = new Color(0.035f,0.38f,0.32f);
        public Vector3 Absorption = new Vector3(0.22f,0.065f,0.035f);
        [Range(0.001f,1)] public float Scattering = 0.12f;
        [Header("Surface lighting")]
        [Range(0.04f,0.7f)] public float Roughness = 0.16f;
        [Range(0,3)] public float NormalStrength = 1;
        [Range(0,0.08f)] public float RefractionStrength = 0.018f;
        [Range(0,2)] public float ReflectionStrength = 1;
        [Range(0,3)] public float SubsurfaceStrength = 0.65f;
        public Color SkyHorizon = new Color(0.48f,0.65f,0.76f);
        public Color SkyZenith = new Color(0.08f,0.27f,0.5f);
        [Header("Foam (whitecaps and ship wakes)")]
        public Color FoamColor = new Color(0.87f,0.94f,0.93f);
        [Tooltip("Multiplies the simulated coverage before it is drawn. Above 1 keeps foam solid for longer.")]
        [Range(0,3)] public float FoamCoverage = 1;
        [Tooltip("World size in metres of one tile of the foam pattern. The largest lace cells are about a ninth of it.")]
        [Min(0.5f)] public float FoamPatternSize = 8;
        [Tooltip("Width of the foam edge in pattern units. Low values give crisp lace, high values a soft haze.")]
        [Range(0.005f,0.3f)] public float FoamEdgeSoftness = 0.045f;
        [Tooltip("Strength of the second, three times finer lace layer that frays the edges up close.")]
        [Range(0,1)] public float FoamFineDetail = 0.32f;
        [Tooltip("Share of the surface that loose bubbles may cover around foam and over churned water.")]
        [Range(0,0.6f)] public float FoamBubbles = 0.2f;
        [Tooltip("How unevenly half-dissolved foam thins out. Zero dissolves everywhere at the same pace.")]
        [Range(0,1.5f)] public float FoamClumping = 0.9f;
        [Tooltip("Depth of the soft shading inside thick foam.")]
        [Range(0,1)] public float FoamReliefShading = 0.5f;
        [Tooltip("Speed at which the two lace layers creep against each other. Zero freezes the pattern on the water.")]
        [Range(0,4)] public float FoamAnimation = 1;
        [Header("Aerated water below foam")]
        [Tooltip("Scattering color of water full of fine bubbles: the pale turquoise around breaking waves and in a ship's trail.")]
        public Color AerationColor = new Color(0.05f,0.33f,0.33f);
        [Range(0,1)] public float AerationStrength = 0.85f;
        [Tooltip("How much aeration surface foam implies on its own. Wakes add their longer-lived trail on top.")]
        [Range(0,1)] public float AerationFromFoam = 0.6f;
        [Header("Caustics")]
        [Min(0.001f)] public float CausticScale = 0.12f;
        [Range(0,5)] public float CausticStrength = 0.6f;
        [Min(0.1f)] public float CausticDepth = 18;
        public bool UseSceneTextures = true;

        // Applying every camera is inexpensive and makes direct C# field edits live without rebuilds or allocations.
        public void Apply(Material material, OceanResources resources)
        {
            material.SetColor("_DeepColor",DeepColor); material.SetColor("_ShallowColor",ShallowColor);
            material.SetVector("_Absorption",new Vector4(Mathf.Max(0,Absorption.x),Mathf.Max(0,Absorption.y),Mathf.Max(0,Absorption.z),0));
            material.SetFloat("_Scattering",Mathf.Clamp(Scattering,0.001f,1));
            material.SetFloat("_Roughness",Mathf.Clamp(Roughness,0.04f,0.7f));
            material.SetFloat("_NormalStrength",Mathf.Max(0,NormalStrength));
            material.SetFloat("_RefractionStrength",Mathf.Clamp(RefractionStrength,0,0.08f));
            material.SetFloat("_ReflectionStrength",Mathf.Max(0,ReflectionStrength));
            material.SetFloat("_SubsurfaceStrength",Mathf.Max(0,SubsurfaceStrength));
            material.SetColor("_SkyHorizon",SkyHorizon); material.SetColor("_SkyZenith",SkyZenith);
            material.SetColor("_FoamColor",FoamColor);
            material.SetVector("_FoamPatternParams",new Vector4(Mathf.Max(0.5f,FoamPatternSize),Mathf.Clamp01(FoamFineDetail),
                Mathf.Clamp(FoamBubbles,0,0.6f),Mathf.Clamp(FoamClumping,0,1.5f)));
            material.SetVector("_FoamShadingParams",new Vector4(Mathf.Max(0,FoamCoverage),Mathf.Clamp(FoamEdgeSoftness,0.005f,0.3f),
                Mathf.Clamp01(FoamReliefShading),Mathf.Max(0,FoamAnimation)));
            material.SetColor("_AerationColor",AerationColor);
            material.SetVector("_AerationParams",new Vector4(Mathf.Clamp01(AerationStrength),Mathf.Clamp01(AerationFromFoam),0,0));
            material.SetFloat("_CausticScale",Mathf.Max(0.001f,CausticScale));
            material.SetFloat("_CausticStrength",Mathf.Max(0,CausticStrength));
            material.SetFloat("_CausticDepth",Mathf.Max(0.1f,CausticDepth));
            material.SetFloat("_UseSceneTextures",UseSceneTextures?1:0);
            material.SetTexture("_FoamPattern",resources.FoamPattern); material.SetTexture("_CausticTex",resources.Caustic);
        }

        public void ImportLegacy(Material material)
        {
            if(material==null||material.shader.name!="WaterSystem/Ocean")return;
            DeepColor=material.GetColor("_DeepColor"); ShallowColor=material.GetColor("_ShallowColor");
            Absorption=(Vector3)material.GetVector("_Absorption"); Scattering=material.GetFloat("_Scattering");
            Roughness=material.GetFloat("_Roughness"); NormalStrength=material.GetFloat("_NormalStrength");
            RefractionStrength=material.GetFloat("_RefractionStrength"); ReflectionStrength=material.GetFloat("_ReflectionStrength");
            SubsurfaceStrength=material.GetFloat("_SubsurfaceStrength"); SkyHorizon=material.GetColor("_SkyHorizon"); SkyZenith=material.GetColor("_SkyZenith");
            // The foam pattern parameters have no counterpart in materials saved before the erosion-based foam.
            FoamColor=material.GetColor("_FoamColor");
            CausticScale=material.GetFloat("_CausticScale"); CausticStrength=material.GetFloat("_CausticStrength"); CausticDepth=material.GetFloat("_CausticDepth");
            UseSceneTextures=material.GetFloat("_UseSceneTextures")>0.5f;
        }
    }
}
