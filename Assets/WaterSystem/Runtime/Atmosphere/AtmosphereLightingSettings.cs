using System;
using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>How the atmosphere drives the rest of the scene. Everything it changes is restored when disabled.</summary>
    [Serializable]
    public sealed class AtmosphereLightingSettings
    {
        [Tooltip("Set the sun light colour and intensity from the atmosphere transmittance (warm, dim sunsets).")]
        public bool DriveSunLight = true;
        [Tooltip("Set tri-light ambient colours from the sky, including cloud cover.")]
        public bool DriveAmbient = true;
        [Range(0, 4)] public float AmbientIntensity = 1;
        public Color MinimumAmbient = new Color(0.01f, 0.012f, 0.02f);
        [Tooltip("Use the dynamic sky cubemap (sky and clouds) as the default reflection for all materials.")]
        public bool DriveReflections = true;
        [Tooltip("Assign the atmosphere skybox when the scene uses another skybox material.")]
        public bool DriveSkybox = true;
        [Tooltip("Turn off Unity's distance fog; aerial perspective replaces it.")]
        public bool DisableUnityFog = true;
        [Tooltip("Full-screen aerial perspective on opaque geometry.")]
        public bool ApplyAerialPerspective = true;
    }
}
