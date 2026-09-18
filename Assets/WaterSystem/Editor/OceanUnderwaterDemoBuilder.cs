using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace WaterSystem.Ocean.Editor
{
    public static class OceanUnderwaterDemoBuilder
    {
        public const string ScenePath = "Assets/WaterSystem/Demo/UnderwaterDemo.unity";
        const string Folder = "Assets/WaterSystem/Demo/Underwater";
        [MenuItem("Tools/Water System/Create Underwater Demo")]
        public static void CreateDemo()
        {
            var existing = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (existing != null) { EditorGUIUtility.PingObject(existing); return; }
            for (int i=0;i<SceneManager.sceneCount;i++)
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                { Debug.LogWarning("Save the untitled scene before creating the additive underwater demo."); return; }
            OceanUnderwaterInstaller.EnsureInstalled();
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            Scene previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var ocean = new GameObject("Physical FFT Ocean").AddComponent<OceanRenderer>();
                ocean.Waves.windSpeed=7; ocean.Waves.heightScale=0.65f; ocean.Waves.choppiness=1;
                ocean.Waves.separateCascadeBands=true;
                ocean.RootSize=2048; ocean.MaxDepth=7; ocean.PatchResolution=32;
                var camera = new GameObject("Dive Camera").AddComponent<Camera>();
                camera.tag="MainCamera"; camera.nearClipPlane=0.1f; camera.farClipPlane=1000;
                camera.transform.SetPositionAndRotation(new Vector3(0,-3,-12),Quaternion.Euler(8,0,0));
                camera.backgroundColor=new Color(0.45f,0.65f,0.83f); camera.clearFlags=CameraClearFlags.SolidColor;
                camera.gameObject.AddComponent<AudioListener>();
                camera.gameObject.AddComponent<OceanDivePreview>();
                var data=camera.GetUniversalAdditionalCameraData(); data.requiresColorTexture=true; data.requiresDepthTexture=true;
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type=LightType.Directional; sun.intensity=1.3f; sun.shadows=LightShadows.Soft;
                sun.transform.rotation=Quaternion.Euler(45,-25,0); RenderSettings.sun=sun;
                RenderSettings.fog=false; RenderSettings.ambientMode=AmbientMode.Flat;
                RenderSettings.ambientLight=new Color(0.35f,0.4f,0.45f);
                var sand=Material("Sand",new Color(0.72f,0.68f,0.5f));
                var rock=Material("Rock",new Color(0.35f,0.42f,0.45f));
                var red=Material("Red distance markers",new Color(0.8f,0.08f,0.025f));
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Seabed";
                floor.transform.position=new Vector3(0,-11,45);floor.transform.localScale=new Vector3(180,2,180);
                floor.GetComponent<Renderer>().sharedMaterial=sand;
                for(int i=0;i<7;i++)
                {
                    var stone=GameObject.CreatePrimitive(PrimitiveType.Sphere);stone.name="Submerged rock "+i;
                    stone.transform.position=new Vector3((i%2==0?-1:1)*(5+i),-8.5f,2+i*10);
                    stone.transform.localScale=new Vector3(5+i%3,4,6);
                    stone.GetComponent<Renderer>().sharedMaterial=rock;
                    var marker=GameObject.CreatePrimitive(PrimitiveType.Cube);marker.name="Absorption marker "+i;
                    marker.transform.position=new Vector3(0,-5,5+i*12);marker.transform.localScale=new Vector3(1.4f,4,1.4f);
                    marker.GetComponent<Renderer>().sharedMaterial=red;
                }
                var pier=GameObject.CreatePrimitive(PrimitiveType.Cube);pier.name="Surface crossing pillar";
                pier.transform.position=new Vector3(8,0,12);pier.transform.localScale=new Vector3(2,14,2);
                pier.GetComponent<Renderer>().sharedMaterial=rock;
                EditorSceneManager.SaveScene(scene,ScenePath);
            }
            finally
            {
                if(previous.IsValid())SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene,true);AssetDatabase.SaveAssets();
            }
        }
        static Material Material(string name,Color color)
        {
            string path=Folder+"/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material!=null)return material;
            material=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor",color); material.SetFloat("_Smoothness",0.2f);
            AssetDatabase.CreateAsset(material,path);return material;
        }
    }
}
