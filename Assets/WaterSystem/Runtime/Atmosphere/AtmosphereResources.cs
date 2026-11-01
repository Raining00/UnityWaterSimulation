using UnityEngine;

namespace WaterSystem.Ocean
{
    // Packaged shader references for the sky and clouds, loaded from Resources like OceanResources.
    public sealed class AtmosphereResources : ScriptableObject
    {
        public ComputeShader AtmosphereLUT;
        public ComputeShader CloudNoise;
        public ComputeShader VolumetricClouds;
        public Shader Skybox;
        public Shader AerialPerspective;
        public Shader CloudComposite;
        public Texture2D MilkyWay;
        public Texture2D MoonSurface;
        static AtmosphereResources cached;
        public static AtmosphereResources Load()
        {
            if (cached == null) cached = Resources.Load<AtmosphereResources>("WaterSystem/AtmosphereDefaults");
            if (cached == null) throw new System.InvalidOperationException("Missing packaged WaterSystem/AtmosphereDefaults resource. Restore Assets/WaterSystem/Resources/WaterSystem/AtmosphereDefaults.asset and its shader dependencies.");
            return cached;
        }
    }
}
