using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace WaterSystem.Ocean.Editor
{
    public static class OceanStylizedDemoBuilder
    {
        public const string ScenePath = "Assets/WaterSystem/Demo/StylizedOceanDemo.unity";
        const string Folder = "Assets/WaterSystem/Demo/Stylized";

        [MenuItem("Tools/Water System/Create Stylized Ocean Demo")]
        public static void CreateDemo()
        {
            var existing = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (existing != null) { EditorGUIUtility.PingObject(existing); return; }
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                {
                    Debug.LogWarning("Save the untitled scene before creating the additive ocean demo.");
                    return;
                }
            var resources = OceanDefaultsBuilder.Ensure();
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            Scene previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var ocean = new GameObject("Stylized FFT Ocean").AddComponent<OceanRenderer>();
                ocean.ShadingMode = OceanShadingMode.Stylized;
                ocean.RootSize = 4096; ocean.MaxDepth = 8; ocean.PatchResolution = 32;
                ocean.Waves.windSpeed = 9; ocean.Waves.windRotation = 65;
                ocean.Waves.heightScale = 0.9f; ocean.Waves.choppiness = 1.6f;
                ocean.Waves.separateCascadeBands = true;
                ocean.Waves.foamThreshold = 0.9f; ocean.Waves.foamSoftness = 0.24f;
                ocean.Waves.foamDecay = 0.75f;
                ocean.Spray.Enabled = true;
                ocean.Spray.Opacity = 0.45f;

                var camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.transform.SetPositionAndRotation(new Vector3(28, 12, -38), Quaternion.Euler(12, -12, 0));
                camera.fieldOfView = 58; camera.nearClipPlane = 0.15f; camera.farClipPlane = 1800;
                camera.clearFlags = CameraClearFlags.Skybox; camera.allowHDR = true;
                camera.gameObject.AddComponent<AudioListener>();
                var cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.requiresColorTexture = true; cameraData.requiresDepthTexture = true;

                var sun = new GameObject("Soft afternoon sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.2f;
                sun.color = new Color(1, 0.98f, 0.93f); sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(48, -45, 0);
                RenderSettings.sun = sun; RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.48f, 0.65f, 0.8f);
                RenderSettings.ambientEquatorColor = new Color(0.43f, 0.53f, 0.6f);
                RenderSettings.ambientGroundColor = new Color(0.32f, 0.34f, 0.34f);
                var sky = new Material(Shader.Find("Skybox/Panoramic"));
                sky.SetTexture("_MainTex", resources.StylizedSky); sky.SetFloat("_Exposure", 0.65f);
                sky = SaveAsset(sky, "CloudSky.mat"); RenderSettings.skybox = sky;

                var sand = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                sand.SetColor("_BaseColor", new Color(0.91f, 0.90f, 0.82f));
                sand.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/WaterSystem/Textures/Stylized/Sand.png"));
                sand.SetFloat("_Smoothness", 0.1f); sand = SaveAsset(sand, "Sand.mat");
                var mesh = SaveAsset(CreateBeach(), "Beach.asset");
                var beach = new GameObject("Sloping sand seabed (opaque depth)");
                beach.AddComponent<MeshFilter>().sharedMesh = mesh;
                beach.AddComponent<MeshRenderer>().sharedMaterial = sand;

                var rock = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                rock.SetColor("_BaseColor", new Color(0.50f, 0.55f, 0.56f));
                rock.SetFloat("_Smoothness", 0.15f); rock = SaveAsset(rock, "Rock.mat");
                for (int i = 0; i < 5; i++)
                {
                    var stone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    stone.name = "Shore rock " + (i + 1);
                    stone.transform.position = new Vector3(64+i*1.6f, -0.2f, 75+i*2.3f);
                    stone.transform.localScale = new Vector3(4+i%2, 2.8f+i*0.3f, 4.5f);
                    stone.transform.rotation = Quaternion.Euler(i*17, i*53, i*11);
                    stone.GetComponent<MeshRenderer>().sharedMaterial = rock;
                }
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.SaveAssets();
            }
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
        }

        static T SaveAsset<T>(T asset, string name) where T : Object
        {
            // A scene can be deleted/recreated without duplicating supporting assets or their GUIDs.
            string path = Folder + "/" + name;
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) { EditorUtility.CopySerialized(asset, existing); Object.DestroyImmediate(asset); return existing; }
            else AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static Mesh CreateBeach()
        {
            const int nx = 96, nz = 160;
            var vertices = new Vector3[(nx+1)*(nz+1)];
            var uv = new Vector2[vertices.Length];
            var indices = new int[nx*nz*6];
            for (int z = 0; z <= nz; z++) for (int x = 0; x <= nx; x++)
            {
                float wx = Mathf.Lerp(-320, 220, (float)x/nx);
                float wz = Mathf.Lerp(-180, 1600, (float)z/nz);
                float shoreline = 70+5*Mathf.Sin(wz*0.012f);
                float height = (wx-shoreline)*0.065f-Mathf.Max(0,wz-120)*0.006f;
                int i = z*(nx+1)+x;
                vertices[i] = new Vector3(wx, height, wz); uv[i] = new Vector2(wx, wz)*0.15f;
                if (x==nx || z==nz) continue;
                int k = (z*nx+x)*6;
                indices[k]=i; indices[k+1]=i+nx+1; indices[k+2]=i+1;
                indices[k+3]=i+1; indices[k+4]=i+nx+1; indices[k+5]=i+nx+2;
            }
            var mesh = new Mesh { name = "Stylized sloping beach", vertices = vertices, uv = uv, triangles = indices };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
