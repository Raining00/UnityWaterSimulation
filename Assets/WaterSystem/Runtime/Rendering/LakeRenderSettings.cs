using System;
using UnityEngine;

namespace WaterSystem.Ocean
{
    [Serializable]
    public sealed class LakeRenderSettings
    {
        [Header("Color and depth")]
        // Value copied from the actual Ocean.mat assignment, not the shader's fallback default.
        public Color ShallowColor = new Color(0f, 0.754717f, 0.47389516f, 1f);
        public Color DepthColor = new Color(0f, 0.47883922f, 0.5283019f, 1f);
        [Min(0.01f)] public float Depth = 13.3f;
        [Min(0.0001f)] public float EdgesFade = 0.01f;
        [Min(0.01f)] public float DistanceFade = 500f;
        [Min(0f)] public float DistanceFadeOffset = 1000f;

        [Header("Surface normals and refraction")]
        [Range(0f, 1f)] public float NormalPower = 0.39f;
        [Min(0f)] public float NormalScale = 2f;
        [Min(0f)] public float NormalSpeed = 0.5f;
        public Vector4 NormalDirection = new Vector4(-1f, 5f, 2.7f, -0.2f);
        [Range(0f, 5f)] public float RefractionPower = 5f;

        [Header("Shore foam")]
        [Min(0f)] public float FoamDistance = 1f;
        [Range(0f, 1f)] public float FoamPower = 0.1f;
        [Min(0f)] public float FoamScale = 5f;
        [Min(0f)] public float FoamSpeed = 0.5f;

        [Header("Caustics")]
        public Color CausticsColor = new Color(0f, 1f, 0.7731986f, 1f);
        [Min(0f)] public float CausticsScale = 0.97f;
        [Min(0f)] public float CausticsSpeed = 1.5f;
        public float CausticsOffset = 3.5f;

        [Header("Surface response")]
        [Range(0f, 1f)] public float Metallic = 0.224f;
        [Range(0f, 1f)] public float Smoothness = 1f;

        public void Apply(Material material)
        {
            material.SetColor("_ShallowColor", ShallowColor);
            material.SetColor("_DepthColor", DepthColor);
            material.SetFloat("_Depth", Mathf.Max(0.01f, Depth));
            material.SetFloat("_EdgesFade", Mathf.Max(0.0001f, EdgesFade));
            material.SetFloat("_DistanceFade", Mathf.Max(0.01f, DistanceFade));
            material.SetFloat("_DistanceFadeOffset", Mathf.Max(0f, DistanceFadeOffset));
            material.SetFloat("_NormalPower", Mathf.Clamp01(NormalPower));
            material.SetFloat("_NormalScale", Mathf.Max(0f, NormalScale));
            material.SetFloat("_NormalSpeed", Mathf.Max(0f, NormalSpeed));
            material.SetVector("_NormalDirection", NormalDirection);
            material.SetFloat("_RefractionPower", Mathf.Clamp(RefractionPower, 0f, 5f));
            material.SetFloat("_FoamDistance", Mathf.Max(0f, FoamDistance));
            material.SetFloat("_FoamPower", Mathf.Clamp01(FoamPower));
            material.SetFloat("_FoamScale", Mathf.Max(0f, FoamScale));
            material.SetFloat("_FoamSpeed", Mathf.Max(0f, FoamSpeed));
            material.SetColor("_CausticsColor", CausticsColor);
            material.SetFloat("_CausticsScale", Mathf.Max(0f, CausticsScale));
            material.SetFloat("_CausticsSpeed", Mathf.Max(0f, CausticsSpeed));
            material.SetFloat("_CausticsOffset", CausticsOffset);
            material.SetFloat("_Metallic", Mathf.Clamp01(Metallic));
            material.SetFloat("_Smoothness", Mathf.Clamp01(Smoothness));
        }

        public void Validate()
        {
            Depth = Mathf.Max(0.01f, Depth);
            EdgesFade = Mathf.Max(0.0001f, EdgesFade);
            DistanceFade = Mathf.Max(0.01f, DistanceFade);
            DistanceFadeOffset = Mathf.Max(0f, DistanceFadeOffset);
            NormalPower = Mathf.Clamp01(NormalPower);
            NormalScale = Mathf.Max(0f, NormalScale);
            NormalSpeed = Mathf.Max(0f, NormalSpeed);
            RefractionPower = Mathf.Clamp(RefractionPower, 0f, 5f);
            FoamDistance = Mathf.Max(0f, FoamDistance);
            FoamPower = Mathf.Clamp01(FoamPower);
            FoamScale = Mathf.Max(0f, FoamScale);
            FoamSpeed = Mathf.Max(0f, FoamSpeed);
            CausticsScale = Mathf.Max(0f, CausticsScale);
            CausticsSpeed = Mathf.Max(0f, CausticsSpeed);
            Metallic = Mathf.Clamp01(Metallic);
            Smoothness = Mathf.Clamp01(Smoothness);
        }
    }
}
