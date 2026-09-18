using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace WaterSystem.Ocean.Editor
{
    public static class OceanUnderwaterInstaller
    {
        [InitializeOnLoadMethod]
        static void Schedule() => EditorApplication.delayCall += InstallWhenReady;
        static void InstallWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { Schedule(); return; }
            if (!EditorApplication.isPlayingOrWillChangePlaymode) EnsureInstalled();
        }

        [MenuItem("Tools/Water System/Install Underwater Rendering")]
        public static void EnsureInstalled()
        {
            OceanDefaultsBuilder.Ensure();
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (renderer == null || renderer.rendererFeatures.Exists(f => f is OceanUnderwaterFeature)) continue;
                var feature = ScriptableObject.CreateInstance<OceanUnderwaterFeature>();
                feature.name = "Ocean Underwater";
                AssetDatabase.AddObjectToAsset(feature, renderer);
                renderer.rendererFeatures.Add(feature);
                // Keep the local-id recovery map aligned with the serialized feature list.
                var serialized = new SerializedObject(renderer);
                var map = serialized.FindProperty("m_RendererFeatureMap");
                if (map != null)
                {
                    map.arraySize = renderer.rendererFeatures.Count;
                    for (int i = 0; i < renderer.rendererFeatures.Count; i++)
                        if (renderer.rendererFeatures[i] != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out string _, out long id))
                            map.GetArrayElementAtIndex(i).longValue = id;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                renderer.SetDirty();
                EditorUtility.SetDirty(renderer);
                EditorUtility.SetDirty(feature);
                AssetDatabase.SaveAssetIfDirty(renderer);
            }
        }
    }
}
