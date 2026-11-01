using UnityEngine;

namespace WaterSystem.Ocean
{
    public sealed class LakeResources : ScriptableObject
    {
        public Shader SurfaceShader;
        public Texture2D NormalMap;
        public Texture2D FoamMask;
        public Texture2D FoamNormal;

        static LakeResources cached;

        public static LakeResources Load()
        {
            if (cached == null)
                cached = Resources.Load<LakeResources>("WaterSystem/LakeDefaults");

            if (cached == null)
                throw new System.InvalidOperationException("Missing packaged WaterSystem/LakeDefaults resource.");

            return cached;
        }
    }
}
