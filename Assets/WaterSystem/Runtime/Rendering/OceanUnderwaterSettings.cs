using System;
using UnityEngine;

namespace WaterSystem.Ocean
{
    [Serializable]
    public sealed class OceanUnderwaterSettings
    {
        public bool Enabled = true;
        [Tooltip("Asymptotic water color after a long underwater path.")]
        public Color ScatteringColor = new Color(0.008f, 0.12f, 0.16f);
        [Tooltip("Scales Rendering.Absorption; red light usually disappears first.")]
        [Range(0, 5)] public float AbsorptionMultiplier = 1;
        [Range(0, 1)] public float ScatteringDensity = 0.035f;
        [Min(1)] public float MaximumDistance = 120;
        [Range(0, 0.25f)] public float DepthLightFalloff = 0.025f;
        [Tooltip("Soft transition width in metres at the FFT waterline on the near plane.")]
        [Range(0.001f, 0.3f)] public float WaterlineSoftness = 0.003f;
        [Range(1.01f, 1.6f)] public float RefractiveIndex = 1.333f;
        [Range(0, 3)] public float CausticStrength = 0.4f;
        [Min(0.1f)] public float CausticDepth = 20;

        [Header("Flow animation")]
        [Tooltip("Master multiplier for every animated rate in this section: the distortion advection and the light-shaft drift. Lower it to calm the motion, 0 to freeze it completely. The wave-driven part of the distortion follows OceanSettings.timeScale instead. With the default Shaft Drift, about 0.35 makes the beams move at the same world speed as the seabed caustics.")]
        [Range(0, 2)] public float FlowSpeed = 0.25f;

        [Header("Flowing distortion")]
        [Tooltip("Screen-space warp in UV units. 0 disables the warp entirely, including its texture fetches.")]
        [Range(0, 0.08f)] public float DistortionStrength = 0.014f;
        [Tooltip("Weight of the simulated wave slope, so the warp follows the waves the player actually sees.")]
        [Range(0, 4)] public float DistortionWaveWeight = 0.6f;
        [Tooltip("Weight of the coarse drifting noise that stands in for the rest of the surface flow.")]
        [Range(0, 4)] public float DistortionFlowWeight = 1.1f;
        [Tooltip("Distance in metres at which the warp reaches full strength. Near geometry barely moves, which is what keeps silhouettes from smearing.")]
        [Min(0.1f)] public float DistortionDistance = 25;
        [Tooltip("Flow noise repeats per metre. Larger values give a tighter, busier wobble.")]
        [Min(0.0001f)] public float DistortionScale = 0.008f;
        [Tooltip("Flow drift in metres per second at Flow Speed 1; the noise is advected along it with wall-clock time.")]
        public Vector2 DistortionDrift = new Vector2(0.9f, 0.55f);
        [Tooltip("Dark level subtracted from the noise field so the warp varies around zero instead of merely translating the image.")]
        [Range(0, 1)] public float DistortionPivot = 0.2f;

        [Header("Light shafts (Tyndall)")]
        [Tooltip("Beam brightness. 0 skips the volumetric march entirely.")]
        [Range(0, 4)] public float ShaftStrength = 0.8f;
        [Tooltip("Caustic-network repeats per metre used as the focused light pattern on the surface. Lower values give fewer, broader beams; the march prefilter keeps any value alias-free, but very high values blur the pattern into a flat glow.")]
        [Min(0.001f)] public float ShaftScale = 0.015f;
        [Tooltip("Forward-scattering anisotropy. Higher values gather the beams around the sun; 0 makes them uniform.")]
        [Range(0, 0.95f)] public float ShaftAnisotropy = 0.7f;
        [Tooltip("Scales the absorption applied to the light's descent from the surface. 0 makes beams equally bright at every depth.")]
        [Range(0, 2)] public float ShaftDepthAbsorption = 0.35f;
        [Tooltip("Samples along each view ray. Higher is smoother and costs proportionally more.")]
        [Range(1, 32)] public int ShaftSteps = 12;
        [Tooltip("Surface pattern drift in metres per second at Flow Speed 1.")]
        public Vector2 ShaftDrift = new Vector2(0.6f, 0.35f);

        internal void ApplySurface(Material material)
        {
            material.SetFloat("_UnderwaterEnabled", Enabled ? 1 : 0);
            material.SetFloat("_UnderwaterIOR", Mathf.Clamp(RefractiveIndex, 1.01f, 1.6f));
            material.SetColor("_UnderwaterScatter", ScatteringColor);
        }

        internal void Bind(MaterialPropertyBlock properties, OceanRenderSettings surface)
        {
            Vector3 absorption = Vector3.Max(Vector3.zero, surface.Absorption) * Mathf.Max(0, AbsorptionMultiplier);
            properties.SetVector("_UnderwaterExtinction", new Vector4(absorption.x, absorption.y, absorption.z, Mathf.Max(0, ScatteringDensity)));
            properties.SetColor("_UnderwaterScatter", ScatteringColor);
            properties.SetVector("_UnderwaterParameters", new Vector4(Mathf.Max(1, MaximumDistance), Mathf.Max(0, DepthLightFalloff), Mathf.Max(0.001f, WaterlineSoftness), Mathf.Max(0, CausticStrength)));
            properties.SetVector("_UnderwaterCaustics", new Vector4(Mathf.Max(0.001f, surface.CausticScale), Mathf.Max(0.1f, CausticDepth), 0, 0));
            var caustic = OceanResources.Load().Caustic;
            properties.SetTexture("_CausticTex", caustic);
            // The shaft march prefilters this texture down to its own sampling rate, which needs the
            // texel size. A MaterialPropertyBlock texture override does not refresh the automatic
            // _TexelSize uniform, so it has to be supplied here.
            float causticWidth = caustic != null ? caustic.width : 512;
            float causticHeight = caustic != null ? caustic.height : 512;
            properties.SetVector("_CausticTex_TexelSize", new Vector4(1f / causticWidth, 1f / causticHeight, causticWidth, causticHeight));
            properties.SetVector("_UnderwaterWarp", new Vector4(Mathf.Max(0, DistortionStrength), Mathf.Max(0, DistortionWaveWeight), Mathf.Max(0, DistortionFlowWeight), Mathf.Max(0.01f, DistortionDistance)));
            properties.SetVector("_UnderwaterFlow", new Vector4(Mathf.Max(0.0001f, DistortionScale), DistortionDrift.x, DistortionDrift.y, Mathf.Clamp(DistortionPivot, 0, 1)));
            properties.SetVector("_UnderwaterShafts", new Vector4(Mathf.Max(0, ShaftStrength), Mathf.Max(0.001f, ShaftScale), Mathf.Clamp(ShaftAnisotropy, 0, 0.95f), Mathf.Max(0, ShaftDepthAbsorption)));
            properties.SetVector("_UnderwaterShaftFlow", new Vector4(ShaftDrift.x, ShaftDrift.y, 0, 0));
            properties.SetFloat("_UnderwaterShaftSteps", Mathf.Clamp(ShaftSteps, 1, 32));
            properties.SetFloat("_UnderwaterFlowSpeed", Mathf.Max(0, FlowSpeed));
        }
    }
}
