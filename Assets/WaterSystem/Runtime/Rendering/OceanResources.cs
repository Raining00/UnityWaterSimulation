using UnityEngine;

namespace WaterSystem.Ocean
{
    // A single packaged resource reference retains shaders/textures in player builds. No material asset is needed.
    public sealed class OceanResources : ScriptableObject
    {
        public Shader SurfaceShader;
        public ComputeShader FFT;
        public Texture2D Foam, Caustic;
        public ShaderVariantCollection Variants;
        static OceanResources cached;
        public static OceanResources Load()
        {
            if(cached==null)cached=Resources.Load<OceanResources>("WaterSystem/OceanDefaults");
            if(cached==null)throw new System.InvalidOperationException("Missing packaged WaterSystem/OceanDefaults resource.");
            return cached;
        }
    }
}
