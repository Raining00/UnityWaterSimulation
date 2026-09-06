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
        [Header("Whitecaps and caustics")]
        public Color FoamColor = new Color(0.87f,0.94f,0.93f);
        [Min(0.001f)] public float FoamScale = 0.5f;
        [Range(0,3)] public float FoamStrength = 1.5f;
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
            material.SetColor("_FoamColor",FoamColor); material.SetFloat("_FoamScale",Mathf.Max(0.001f,FoamScale));
            material.SetFloat("_FoamStrength",Mathf.Max(0,FoamStrength));
            material.SetFloat("_CausticScale",Mathf.Max(0.001f,CausticScale));
            material.SetFloat("_CausticStrength",Mathf.Max(0,CausticStrength));
            material.SetFloat("_CausticDepth",Mathf.Max(0.1f,CausticDepth));
            material.SetFloat("_UseSceneTextures",UseSceneTextures?1:0);
            material.SetTexture("_FoamTex",resources.Foam); material.SetTexture("_CausticTex",resources.Caustic);
        }

        public void ImportLegacy(Material material)
        {
            if(material==null||material.shader.name!="WaterSystem/Ocean")return;
            DeepColor=material.GetColor("_DeepColor"); ShallowColor=material.GetColor("_ShallowColor");
            Absorption=(Vector3)material.GetVector("_Absorption"); Scattering=material.GetFloat("_Scattering");
            Roughness=material.GetFloat("_Roughness"); NormalStrength=material.GetFloat("_NormalStrength");
            RefractionStrength=material.GetFloat("_RefractionStrength"); ReflectionStrength=material.GetFloat("_ReflectionStrength");
            SubsurfaceStrength=material.GetFloat("_SubsurfaceStrength"); SkyHorizon=material.GetColor("_SkyHorizon"); SkyZenith=material.GetColor("_SkyZenith");
            FoamColor=material.GetColor("_FoamColor"); FoamScale=material.GetFloat("_FoamScale"); FoamStrength=material.GetFloat("_FoamStrength");
            CausticScale=material.GetFloat("_CausticScale"); CausticStrength=material.GetFloat("_CausticStrength"); CausticDepth=material.GetFloat("_CausticDepth");
            UseSceneTextures=material.GetFloat("_UseSceneTextures")>0.5f;
        }
    }
}
