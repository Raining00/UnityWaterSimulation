using System;
using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>Volumetric cloud layer: weather simulation, shape, lighting and quality.</summary>
    [Serializable]
    public sealed class CloudSettings
    {
        public bool Enabled = true;
        [Header("Layer (metres above sea level)")]
        [Min(50)] public float BottomAltitude = 1500;
        [Min(100)] public float Thickness = 2600;
        [Header("Weather simulation")]
        [Range(0, 1)] public float Coverage = 0.5f;
        [Tooltip("0 flat stratus, 1 towering cumulus.")]
        [Range(0, 1)] public float CloudType = 0.85f;
        [Range(0, 1)] public float TypeVariation = 0.3f;
        [Tooltip("World size (km) of one weather map tile.")]
        [Range(2, 400)] public float WeatherScale = 40;
        [Tooltip("Large weather cells per tile.")]
        [Range(1, 8)] public float WeatherDetail = 4;
        [Range(0.02f, 0.5f)] public float CoverageSoftness = 0.18f;
        [Tooltip("How quickly formations build and dissolve.")]
        [Range(0, 5)] public float EvolutionSpeed = 1;
        [Tooltip("Darkens the base of thick clouds.")]
        [Range(0, 1)] public float Precipitation = 0.05f;
        [Header("Wind")]
        [Tooltip("Use the linked ocean's wind direction and speed.")]
        public bool FollowOceanWind = true;
        [Tooltip("Winds aloft are stronger than at the surface.")]
        [Range(0.1f, 6)] public float OceanWindMultiplier = 2.5f;
        public Vector2 WindDirection = new Vector2(1, 0.35f);
        [Tooltip("Metres per second when not following the ocean.")]
        [Min(0)] public float WindSpeed = 15;
        [Tooltip("Maximum horizontal lean across the layer, relative to its thickness. Detail noise also drifts faster than the cloud body.")]
        [Range(0, 3)] public float WindShear = 0.6f;
        [Header("Shape")]
        [Tooltip("Metres covered by one repeat of the shape noise.")]
        [Min(200)] public float ShapeScale = 7000;
        [Tooltip("Metres covered by one repeat of the detail noise.")]
        [Min(20)] public float DetailScale = 900;
        [Range(0, 1)] public float DetailStrength = 0.3f;
        [Tooltip("Extinction per metre at full density.")]
        [Range(0.002f, 0.3f)] public float Extinction = 0.012f;
        [Header("Lighting")]
        [Range(0.5f, 1)] public float Albedo = 0.99f;
        [Range(0, 0.99f)] public float ForwardScattering = 0.75f;
        [Range(-0.99f, 0)] public float BackScattering = -0.3f;
        [Range(0, 1)] public float PhaseBlend = 0.35f;
        [Range(0, 3)] public float AmbientStrength = 1.2f;
        [Range(0, 1)] public float PowderStrength = 0.4f;
        [Range(0.1f, 1)] public float MultiScatteringAttenuation = 0.35f;
        [Range(0.1f, 1)] public float MultiScatteringContribution = 0.7f;
        [Range(0.1f, 1)] public float MultiScatteringPhaseDecay = 0.5f;
        [Range(0, 1)] public float WetnessDarkening = 0.6f;
        [Header("Quality")]
        [Tooltip("Cloud buffer resolution divisor. Temporal reprojection recovers detail.")]
        [Range(1, 4)] public int Downsample = 2;
        [Range(16, 256)] public int MaxSteps = 128;
        [Tooltip("Metres between the first sun samples; later samples grow 1.5x.")]
        [Range(5, 200)] public float LightStepLength = 35;
        [Tooltip("Metres. Clouds fade out before this distance.")]
        [Min(5000)] public float MaxDistance = 90000;
        [Tooltip("Step growth per kilometre of distance; keeps distant clouds affordable.")]
        [Range(0, 1)] public float StepGrowth = 0.2f;
        [Range(0, 0.98f)] public float TemporalBlend = 0.9f;
        [Header("Shadows and reflections")]
        public bool CastShadows = true;
        [Range(0, 1)] public float ShadowStrength = 0.9f;
        [Tooltip("Metres covered by the cloud shadow map around the camera.")]
        [Min(2000)] public float ShadowExtent = 24000;
        public int ShadowResolution = 1024;
        [Tooltip("Also project the cloud shadows onto URP materials through the sun light cookie.")]
        public bool SunLightCookie = true;
        [Tooltip("Per-face resolution of the dynamic sky cubemap used for reflections.")]
        public int ReflectionResolution = 128;

        public void Validate()
        {
            BottomAltitude = Mathf.Max(50, AtmosphereSettings.Finite(BottomAltitude, 1500));
            Thickness = Mathf.Max(100, AtmosphereSettings.Finite(Thickness, 2600));
            Coverage = Mathf.Clamp01(AtmosphereSettings.Finite(Coverage, 0.5f));
            CloudType = Mathf.Clamp01(AtmosphereSettings.Finite(CloudType, 0.85f));
            TypeVariation = Mathf.Clamp01(AtmosphereSettings.Finite(TypeVariation, 0.3f));
            WeatherScale = Mathf.Clamp(AtmosphereSettings.Finite(WeatherScale, 40), 2, 400);
            WeatherDetail = Mathf.Clamp(AtmosphereSettings.Finite(WeatherDetail, 4), 1, 8);
            CoverageSoftness = Mathf.Clamp(AtmosphereSettings.Finite(CoverageSoftness, 0.18f), 0.02f, 0.5f);
            EvolutionSpeed = Mathf.Clamp(AtmosphereSettings.Finite(EvolutionSpeed, 1), 0, 5);
            Precipitation = Mathf.Clamp01(AtmosphereSettings.Finite(Precipitation, 0.05f));
            OceanWindMultiplier = Mathf.Clamp(AtmosphereSettings.Finite(OceanWindMultiplier, 2.5f), 0.1f, 6);
            WindSpeed = Mathf.Max(0, AtmosphereSettings.Finite(WindSpeed, 15));
            WindShear = Mathf.Clamp(AtmosphereSettings.Finite(WindShear, 0.6f), 0, 3);
            ShapeScale = Mathf.Max(200, AtmosphereSettings.Finite(ShapeScale, 7000));
            DetailScale = Mathf.Max(20, AtmosphereSettings.Finite(DetailScale, 900));
            DetailStrength = Mathf.Clamp01(AtmosphereSettings.Finite(DetailStrength, 0.3f));
            Extinction = Mathf.Clamp(AtmosphereSettings.Finite(Extinction, 0.012f), 0.002f, 0.3f);
            Albedo = Mathf.Clamp(AtmosphereSettings.Finite(Albedo, 0.99f), 0.5f, 1);
            ForwardScattering = Mathf.Clamp(AtmosphereSettings.Finite(ForwardScattering, 0.75f), 0, 0.99f);
            BackScattering = Mathf.Clamp(AtmosphereSettings.Finite(BackScattering, -0.3f), -0.99f, 0);
            PhaseBlend = Mathf.Clamp01(AtmosphereSettings.Finite(PhaseBlend, 0.35f));
            AmbientStrength = Mathf.Clamp(AtmosphereSettings.Finite(AmbientStrength, 1.2f), 0, 3);
            PowderStrength = Mathf.Clamp01(AtmosphereSettings.Finite(PowderStrength, 0.4f));
            MultiScatteringAttenuation = Mathf.Clamp(AtmosphereSettings.Finite(MultiScatteringAttenuation, 0.35f), 0.1f, 1);
            MultiScatteringContribution = Mathf.Clamp(AtmosphereSettings.Finite(MultiScatteringContribution, 0.7f), 0.1f, 1);
            MultiScatteringPhaseDecay = Mathf.Clamp(AtmosphereSettings.Finite(MultiScatteringPhaseDecay, 0.5f), 0.1f, 1);
            WetnessDarkening = Mathf.Clamp01(AtmosphereSettings.Finite(WetnessDarkening, 0.6f));
            Downsample = Mathf.Clamp(Downsample, 1, 4);
            MaxSteps = Mathf.Clamp(MaxSteps, 16, 256);
            LightStepLength = Mathf.Clamp(AtmosphereSettings.Finite(LightStepLength, 35), 5, 200);
            MaxDistance = Mathf.Max(5000, AtmosphereSettings.Finite(MaxDistance, 90000));
            StepGrowth = Mathf.Clamp01(AtmosphereSettings.Finite(StepGrowth, 0.2f));
            TemporalBlend = Mathf.Clamp(AtmosphereSettings.Finite(TemporalBlend, 0.9f), 0, 0.98f);
            ShadowStrength = Mathf.Clamp01(AtmosphereSettings.Finite(ShadowStrength, 0.9f));
            ShadowExtent = Mathf.Max(2000, AtmosphereSettings.Finite(ShadowExtent, 24000));
            ShadowResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(ShadowResolution), 256, 2048);
            ReflectionResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(ReflectionResolution), 32, 512);
        }
    }
}
