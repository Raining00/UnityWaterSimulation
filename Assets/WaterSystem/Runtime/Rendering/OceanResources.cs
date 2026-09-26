using UnityEngine;

namespace WaterSystem.Ocean
{
    // A single packaged resource reference retains shaders/textures in player builds. No material asset is needed.
    public sealed class OceanResources : ScriptableObject
    {
        public Shader SurfaceShader;
        public Shader UnderwaterShader, UnderwaterDepthCopyShader;
        public Shader StylizedSurfaceShader;
        // The stylized volume pass is its own shader; it reuses the physical depth-copy shader.
        public Shader StylizedUnderwaterShader;
        public Texture2D StylizedNormal, StylizedFoam, StylizedIntersection, StylizedCaustic, StylizedSky;
        public ComputeShader FFT, SurfaceQuery, WakeDeposit;
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
