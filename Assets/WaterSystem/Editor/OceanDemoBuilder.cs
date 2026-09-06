using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace WaterSystem.Ocean.Editor
{
    public static class OceanDemoBuilder
    {
        public const string DemoPath = "Assets/WaterSystem/Demo/OceanDemo.unity";

        [MenuItem("Tools/Water System/Create Ocean Demo")]
        public static void CreateDemo()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoPath) != null)
            {
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoPath));
                Debug.Log("Ocean demo already exists: " + DemoPath);
                return;
            }
            Directory.CreateDirectory("Assets/WaterSystem/Demo/Materials");
            AssetDatabase.Refresh();
            OceanDefaultsBuilder.Ensure();
            OceanShowcaseBuilder.EnsurePipelineTextures();
            var marker = CreateLit("ScaleMarker", new Color(0.8f, 0.43f, 0.12f), 0.2f);
            Scene previous = SceneManager.GetActiveScene();
            Scene demo = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(demo);
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.6f, 0.7f, 0.85f);
                RenderSettings.ambientEquatorColor = new Color(0.35f, 0.4f, 0.45f);
                RenderSettings.ambientGroundColor = new Color(0.1f, 0.15f, 0.2f);
                var ocean = new GameObject("Ocean (select to inspect LOD)").AddComponent<OceanRenderer>();
                ocean.DrawLodGizmos = true;
                var camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.transform.position = new Vector3(95, 85, -135);
                camera.transform.LookAt(new Vector3(0, 0, 20));
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 900;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.48f, 0.66f, 0.8f);
                camera.gameObject.AddComponent<AudioListener>();
                var sun = new GameObject("Directional Light").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(45, -30, 0);
                sun.intensity = 1.3f;
                sun.shadows = LightShadows.Soft;
                RenderSettings.sun = sun;
                var markers = new GameObject("Scale markers (10m cubes, 100m apart)");
                for (int i = -2; i <= 2; i++)
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = $"10m marker / X={i * 100}m";
                    cube.transform.SetParent(markers.transform);
                    cube.transform.position = new Vector3(i * 100, 4, 30);
                    cube.transform.localScale = Vector3.one * 10;
                    cube.GetComponent<MeshRenderer>().sharedMaterial = marker;
                }
                if (!EditorSceneManager.SaveScene(demo, DemoPath)) throw new IOException("Could not save " + DemoPath);
                PrefabUtility.SaveAsPrefabAsset(ocean.gameObject, "Assets/WaterSystem/Demo/QuadtreeOcean.prefab");
            }
            finally
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(demo, true);
            }
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoPath));
            Debug.Log("Created " + DemoPath + ". Open it, enter Play, or select Ocean and use Scene view wireframe to inspect LOD.");
        }

        [MenuItem("GameObject/Water System/Ocean", false, 10)]
        public static void CreateOcean(MenuCommand command)
        {
            OceanDefaultsBuilder.Ensure();
            OceanShowcaseBuilder.EnsurePipelineTextures();
            var go = new GameObject("Quadtree Ocean");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            var ocean = go.AddComponent<OceanRenderer>();
            Undo.RegisterCreatedObjectUndo(go, "Create Quadtree Ocean");
            Selection.activeGameObject = go;
        }

        static Material CreateLit(string name, Color color, float smoothness)
        {
            string path = "Assets/WaterSystem/Demo/Materials/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, enableInstancing = true };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
