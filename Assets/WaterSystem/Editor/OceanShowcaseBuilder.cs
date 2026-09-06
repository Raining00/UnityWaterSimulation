using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace WaterSystem.Ocean.Editor
{
    public static class OceanShowcaseBuilder
    {
        public const string ScenePath = "Assets/WaterSystem/Demo/DeepOceanDemo.unity";
        public const string ComputePath = "Assets/WaterSystem/Shaders/OceanFFT.compute";

        public static void EnsureResources()
        {
            ConfigureTexture("Assets/WaterSystem/Textures/FluidsFoamTex.png", 1024);
            ConfigureTexture("Assets/WaterSystem/Textures/Caustic_000.png", 512);
            OceanDefaultsBuilder.Ensure();
        }
        static void ConfigureTexture(string path, int size)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            if (importer.maxTextureSize == size && !importer.sRGBTexture && importer.wrapMode == TextureWrapMode.Repeat) return;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        [MenuItem("Tools/Water System/Configure Selected Ocean")]
        public static void ConfigureSelected()
        {
            var ocean = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<OceanRenderer>() : null;
            if (ocean == null) { Debug.LogWarning("Select a GameObject containing OceanRenderer."); return; }
            Undo.RecordObject(ocean, "Configure FFT ocean");
            OceanDefaultsBuilder.Ensure();
            ocean.EnsureComponents();
            var fft=ocean.FFT;
            ocean.RequestRebuild();
            EditorUtility.SetDirty(ocean); EditorUtility.SetDirty(fft);
            EnsurePipelineTextures();
            EditorSceneManager.MarkSceneDirty(ocean.gameObject.scene);
        }
        public static void EnsurePipelineTextures()
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                if (!urp.supportsCameraDepthTexture || !urp.supportsCameraOpaqueTexture)
                {
                    Undo.RecordObject(urp, "Enable ocean depth and refraction textures");
                    urp.supportsCameraDepthTexture = true;
                    urp.supportsCameraOpaqueTexture = true;
                    EditorUtility.SetDirty(urp);
                }
            }
        }

        [MenuItem("Tools/Water System/Create Deep Ocean Demo")]
        public static void CreateDemo()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            { EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)); return; }
            EnsureResources();
            EnsurePipelineTextures();
            Scene previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var ocean = new GameObject("FFT Ocean").AddComponent<OceanRenderer>();
                ocean.RootSize = 4096; ocean.MaxDepth = 8; ocean.PatchResolution = 32;
                var settings = ocean.Waves;
                settings.windSpeed = 10; settings.windRotation = 25; settings.windTurbulence = 0.35f;
                settings.heightScale = 1.25f; settings.choppiness = 1.25f;
                settings.separateCascadeBands = true;
                settings.foamThreshold = 0.65f; settings.foamSoftness = 0.25f; settings.foamDecay = 0.8f;
                var fft = ocean.FFT;
                fft.RunInEditMode = true;
                fft.Configure(settings, AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath));
                ocean.Simulation = fft;
                var camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera"; camera.transform.position = new Vector3(0,7,-35);
                camera.transform.LookAt(new Vector3(0,1,65)); camera.nearClipPlane = 0.2f; camera.farClipPlane = 1800;
                camera.fieldOfView = 60; camera.clearFlags = CameraClearFlags.Skybox;
                camera.allowHDR = true;
                camera.gameObject.AddComponent<AudioListener>();
                var cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.requiresColorTexture = true; cameraData.requiresDepthTexture = true;
                var sun = new GameObject("Ocean Sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.8f;
                sun.color = new Color(1,0.94f,0.84f); sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(22,-165,0);
                RenderSettings.sun = sun;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.35f,0.48f,0.65f);
                RenderSettings.ambientEquatorColor = new Color(0.2f,0.28f,0.33f);
                RenderSettings.ambientGroundColor = new Color(0.1f,0.15f,0.18f);
                string skyPath = "Assets/WaterSystem/Demo/Materials/OceanSky.mat";
                var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
                if (sky == null)
                {
                    sky = new Material(Shader.Find("Skybox/Procedural"));
                    sky.SetFloat("_SunSize",0.025f); sky.SetFloat("_AtmosphereThickness",1);
                    sky.SetFloat("_Exposure",1.1f); AssetDatabase.CreateAsset(sky,skyPath);
                }
                RenderSettings.skybox = sky;
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = 0.00055f; RenderSettings.fogColor = new Color(0.53f,0.66f,0.73f);
                var sand = Lit("SubmergedStone",new Color(0.55f,0.5f,0.32f));
                var buoy = Lit("OceanMarker",new Color(0.95f,0.36f,0.035f));
                Primitive("Submerged platform (refraction / caustics)",PrimitiveType.Cube,new Vector3(-20,-6,20),new Vector3(25,1,30),sand);
                for (int i=0;i<4;i++)
                    Primitive("Submerged reference block",PrimitiveType.Cube,new Vector3(-28+i*5,-4.5f,22),new Vector3(2,2,2),sand);
                for (int i=0;i<3;i++)
                    Primitive("Fixed distance marker",PrimitiveType.Cylinder,new Vector3(30+i*15,0.7f,35+i*55),new Vector3(0.7f,1.5f,0.7f),buoy);
                PrefabUtility.SaveAsPrefabAsset(ocean.gameObject,"Assets/WaterSystem/Demo/FFT Ocean.prefab");
                if (!EditorSceneManager.SaveScene(scene,ScenePath)) throw new IOException("Could not save ocean demo.");
            }
            finally { SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene,true); }
            AssetDatabase.SaveAssets();
            Debug.Log("Created " + ScenePath);
        }
        static Material Lit(string name,Color color)
        {
            var path="Assets/WaterSystem/Demo/Materials/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material!=null)return material;
            material=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor",color); material.SetFloat("_Smoothness",0.25f);
            AssetDatabase.CreateAsset(material,path);return material;
        }
        static void Primitive(string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(type); go.name=name; go.transform.position=position; go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=material;
        }
    }
}
