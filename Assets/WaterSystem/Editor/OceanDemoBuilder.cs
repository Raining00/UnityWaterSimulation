using UnityEditor;
using UnityEngine;

namespace WaterSystem.Ocean.Editor
{
    /// <summary>
    /// Scene-authoring entry points for the ocean. The legacy "Create Ocean Demo" builder that
    /// targeted Assets/WaterSystem/Demo/OceanDemo.unity was removed with the other demo scenes;
    /// demo scenes are now owned by OceanShowcaseBuilder and OceanStylizedDemoBuilder.
    /// </summary>
    public static class OceanDemoBuilder
    {
        [MenuItem("GameObject/Water System/Ocean", false, 10)]
        public static void CreateOcean(MenuCommand command)
        {
            OceanDefaultsBuilder.Ensure();
            OceanShowcaseBuilder.EnsurePipelineTextures();
            var go = new GameObject("Quadtree Ocean");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            go.AddComponent<OceanRenderer>();
            Undo.RegisterCreatedObjectUndo(go, "Create Quadtree Ocean");
            Selection.activeGameObject = go;
        }
    }
}
