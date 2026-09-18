using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WaterSystem.Ocean.Editor
{
    public static class OceanDefaultsBuilder
    {
        public static OceanResources Ensure()
        {
            const string folder="Assets/WaterSystem/Resources/WaterSystem";
            Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var resources=AssetDatabase.LoadAssetAtPath<OceanResources>(folder+"/OceanDefaults.asset");
            if(resources==null)
            {
                resources=ScriptableObject.CreateInstance<OceanResources>();
                AssetDatabase.CreateAsset(resources,folder+"/OceanDefaults.asset");
            }
            resources.UnderwaterShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/WaterSystem/Shaders/OceanUnderwater.shader");
            resources.UnderwaterDepthCopyShader = Shader.Find("Hidden/Universal Render Pipeline/CopyDepth");
            resources.SurfaceShader=Shader.Find("WaterSystem/Ocean");
            resources.StylizedSurfaceShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/WaterSystem/Shaders/OceanStylized.shader");
            resources.StylizedNormal = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/WaterSystem/Textures/Stylized/SmoothWaves.png");
            resources.StylizedFoam = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/WaterSystem/Textures/Stylized/FoamSea.png");
            resources.StylizedIntersection = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/WaterSystem/Textures/Stylized/IntersectionNoise.png");
            resources.StylizedCaustic = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/WaterSystem/Textures/Stylized/Caustics.png");
            resources.StylizedSky = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/WaterSystem/Textures/Stylized/CloudSky.png");
            resources.FFT=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/WaterSystem/Shaders/OceanFFT.compute");
            resources.Foam=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/WaterSystem/Textures/FluidsFoamTex.png");
            resources.Caustic=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/WaterSystem/Textures/Caustic_000.png");
            var variants=AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(folder+"/OceanVariants.shadervariants");
            if(variants==null)
            {
                variants=new ShaderVariantCollection();
                foreach(string cluster in new[]{"","_CLUSTER_LIGHT_LOOP"})
                foreach(string shadow in new[]{"","_MAIN_LIGHT_SHADOWS","_MAIN_LIGHT_SHADOWS_CASCADE","_MAIN_LIGHT_SHADOWS_SCREEN"})
                foreach(string soft in new[]{"","_SHADOWS_SOFT","_SHADOWS_SOFT_LOW","_SHADOWS_SOFT_MEDIUM","_SHADOWS_SOFT_HIGH"})
                foreach(string fog in new[]{"","FOG_LINEAR","FOG_EXP","FOG_EXP2"})
                {
                    var keywords=new List<string>{"INSTANCING_ON"};
                    foreach(var word in new[]{cluster,shadow,soft,fog})if(word.Length>0)keywords.Add(word);
                    variants.Add(new ShaderVariantCollection.ShaderVariant(resources.SurfaceShader,PassType.ScriptableRenderPipeline,keywords.ToArray()));
                }
                AssetDatabase.CreateAsset(variants,folder+"/OceanVariants.shadervariants");
            }
            resources.Variants=variants;
            EditorUtility.SetDirty(resources);AssetDatabase.SaveAssets();
            return resources;
        }
    }
}
