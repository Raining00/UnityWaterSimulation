using System;
using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>Visible lunar surface and camera-cloud illumination. Does not create a scene light.</summary>
    [Serializable]
    public sealed class MoonSettings
    {
        public bool Enabled = true;
        [Header("Position")]
        [Tooltip("Keep the moon opposite the sun. Disable for manual azimuth/elevation.")]
        public bool FollowSun = true;
        [Range(-180, 180)] public float Azimuth;
        [Range(-90, 90)] public float Elevation = 35;
        [Header("Lunar surface")]
        [Tooltip("Optional 2:1 equirectangular surface colour map, longitude zero at its centre. Empty uses the packaged NASA map.")]
        public Texture2D SurfaceTexture;
        [Tooltip("Apparent angular diameter in degrees. The real moon is approximately 0.52 degrees.")]
        [Range(0.1f, 12)] public float AngularDiameter = 0.52f;
        [Min(0)] public float Brightness = 2;
        public Color Tint = Color.white;
        [Range(-180, 180)] public float TextureRotation;
        [Tooltip("Artistic phase independent of position: 0/1 new moon, 0.25/0.75 half moon, 0.5 full moon.")]
        [Range(0, 1)] public float Phase = 0.5f;
        [Header("Glow")]
        [Min(0)] public float HaloIntensity = 0.04f;
        [Tooltip("Angular radius of the glow, in degrees, independently of the disc diameter.")]
        [Range(0.1f, 40)] public float HaloRadius = 6;
        [Range(0.5f, 12)] public float HaloFalloff = 3;
        [Header("Clouds only")]
        [Tooltip("Moon illuminance used only by the camera's volumetric cloud march. No ambient, reflection or object light is added.")]
        [Min(0)] public float CloudLightIntensity = 0.08f;

        public float IlluminatedFraction => 0.5f - 0.5f * Mathf.Cos(Phase * 2 * Mathf.PI);

        public Vector3 Direction(Vector3 sunDirection)
        {
            if (FollowSun) return -sunDirection;
            float azimuth = Azimuth * Mathf.Deg2Rad, elevation = Elevation * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(azimuth) * Mathf.Cos(elevation), Mathf.Sin(elevation),
                               Mathf.Cos(azimuth) * Mathf.Cos(elevation));
        }

        public void Validate()
        {
            Azimuth = Mathf.Clamp(AtmosphereSettings.Finite(Azimuth, 0), -180, 180);
            Elevation = Mathf.Clamp(AtmosphereSettings.Finite(Elevation, 35), -90, 90);
            AngularDiameter = Mathf.Clamp(AtmosphereSettings.Finite(AngularDiameter, 0.52f), 0.1f, 12);
            Brightness = Mathf.Max(0, AtmosphereSettings.Finite(Brightness, 2));
            TextureRotation = Mathf.Clamp(AtmosphereSettings.Finite(TextureRotation, 0), -180, 180);
            Phase = Mathf.Clamp01(AtmosphereSettings.Finite(Phase, 0.5f));
            HaloIntensity = Mathf.Max(0, AtmosphereSettings.Finite(HaloIntensity, 0.04f));
            HaloRadius = Mathf.Clamp(AtmosphereSettings.Finite(HaloRadius, 6), 0.1f, 40);
            HaloFalloff = Mathf.Clamp(AtmosphereSettings.Finite(HaloFalloff, 3), 0.5f, 12);
            CloudLightIntensity = Mathf.Max(0, AtmosphereSettings.Finite(CloudLightIntensity, 0.08f));
        }
    }
}
