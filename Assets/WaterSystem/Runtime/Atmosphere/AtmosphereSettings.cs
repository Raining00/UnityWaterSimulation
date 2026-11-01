using System;
using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>Physical sky parameters. Earth defaults; scattering coefficients are in 10^-3 per kilometre.</summary>
    [Serializable]
    public sealed class AtmosphereSettings
    {
        [Header("Planet (kilometres)")]
        [Min(100)] public float PlanetRadius = 6360;
        [Min(1)] public float AtmosphereHeight = 100;
        [Header("Rayleigh scattering (air molecules, x0.001 per km)")]
        public Vector3 RayleighScattering = new Vector3(5.802f, 13.558f, 33.1f);
        [Range(0, 4)] public float RayleighDensity = 1;
        [Min(0.1f)] public float RayleighScaleHeight = 8;
        [Header("Mie scattering (aerosols and haze, x0.001 per km)")]
        [Min(0)] public float MieScattering = 3.996f;
        [Min(0)] public float MieAbsorption = 4.4f;
        [Tooltip("Haze amount. 1 is a clear day; 3 to 6 is hazy.")]
        [Range(0, 8)] public float MieDensity = 1;
        [Range(-0.99f, 0.99f)] public float MieAnisotropy = 0.8f;
        [Min(0.1f)] public float MieScaleHeight = 1.2f;
        [Header("Ozone absorption (x0.001 per km)")]
        public Vector3 OzoneAbsorption = new Vector3(0.65f, 1.881f, 0.085f);
        [Range(0, 4)] public float OzoneDensity = 1;
        [Min(0)] public float OzoneLayerCenter = 25;
        [Min(0.1f)] public float OzoneLayerWidth = 15;
        [Header("Sun")]
        public Color SunColor = Color.white;
        [Tooltip("URP intensity of the sun light above the atmosphere. Written to the light when Lighting > Drive Sun Light is on.")]
        [Min(0)] public float SunIntensity = 2;
        [Tooltip("Visible disc size relative to the real sun (0.53 degrees).")]
        [Range(0.2f, 10)] public float SunDiscScale = 1.5f;
        [Header("Rendering")]
        [Tooltip("Sky radiance scale. 1 is consistent with URP Lambertian shading under the sun light.")]
        [Range(0, 8)] public float Exposure = 1;
        [Range(0, 4)] public float AerialPerspectiveScale = 1;
        [Tooltip("Furthest distance (km) covered by the aerial perspective volume.")]
        [Range(20, 1000)] public float MaxAerialDistance = 200;
        [Tooltip("Average surface reflectance seen by the sky model. Water is dark.")]
        public Color GroundAlbedo = new Color(0.08f, 0.1f, 0.12f);
        [Tooltip("Faint radiance floor so a night sky is not pitch black.")]
        public Color NightSky = new Color(0.004f, 0.006f, 0.012f);

        public float TopRadius => PlanetRadius + AtmosphereHeight;

        public void Validate()
        {
            PlanetRadius = Mathf.Max(100, Finite(PlanetRadius, 6360));
            AtmosphereHeight = Mathf.Max(1, Finite(AtmosphereHeight, 100));
            RayleighScattering = Vector3.Max(Vector3.zero, Finite(RayleighScattering, new Vector3(5.802f, 13.558f, 33.1f)));
            RayleighDensity = Mathf.Clamp(Finite(RayleighDensity, 1), 0, 4);
            RayleighScaleHeight = Mathf.Max(0.1f, Finite(RayleighScaleHeight, 8));
            MieScattering = Mathf.Max(0, Finite(MieScattering, 3.996f));
            MieAbsorption = Mathf.Max(0, Finite(MieAbsorption, 4.4f));
            MieDensity = Mathf.Clamp(Finite(MieDensity, 1), 0, 8);
            MieAnisotropy = Mathf.Clamp(Finite(MieAnisotropy, 0.8f), -0.99f, 0.99f);
            MieScaleHeight = Mathf.Max(0.1f, Finite(MieScaleHeight, 1.2f));
            OzoneAbsorption = Vector3.Max(Vector3.zero, Finite(OzoneAbsorption, new Vector3(0.65f, 1.881f, 0.085f)));
            OzoneDensity = Mathf.Clamp(Finite(OzoneDensity, 1), 0, 4);
            OzoneLayerCenter = Mathf.Max(0, Finite(OzoneLayerCenter, 25));
            OzoneLayerWidth = Mathf.Max(0.1f, Finite(OzoneLayerWidth, 15));
            SunIntensity = Mathf.Max(0, Finite(SunIntensity, 2));
            SunDiscScale = Mathf.Clamp(Finite(SunDiscScale, 1.5f), 0.2f, 10);
            Exposure = Mathf.Clamp(Finite(Exposure, 1), 0, 8);
            AerialPerspectiveScale = Mathf.Clamp(Finite(AerialPerspectiveScale, 1), 0, 4);
            MaxAerialDistance = Mathf.Clamp(Finite(MaxAerialDistance, 200), 20, 1000);
        }
        /// <summary>Changing any of these invalidates the transmittance and multiple-scattering tables.</summary>
        public int LutHash => HashCode.Combine(
            HashCode.Combine(PlanetRadius, AtmosphereHeight, RayleighScattering, RayleighDensity, RayleighScaleHeight, MieScattering, MieAbsorption, MieDensity),
            HashCode.Combine(MieAnisotropy, MieScaleHeight, OzoneAbsorption, OzoneDensity, OzoneLayerCenter, OzoneLayerWidth, GroundAlbedo));

        internal static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        internal static Vector3 Finite(Vector3 value, Vector3 fallback) => new Vector3(Finite(value.x, fallback.x), Finite(value.y, fallback.y), Finite(value.z, fallback.z));
    }
}
