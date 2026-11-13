using UnityEditor;
using UnityEngine;

namespace WaterSystem.Ocean.Editor
{
    /// <summary>Hierarchy creation commands. Project settings and shared assets are authored separately.</summary>
    public static class WaterSystemCreateMenu
    {
        [MenuItem("CONTEXT/AtmosphereRenderer/Apply Fair Weather Clouds")]
        static void ApplyFairWeatherClouds(MenuCommand command)
        {
            var atmosphere = (AtmosphereRenderer)command.context;
            Undo.RecordObject(atmosphere, "Apply Fair Weather Clouds");
            atmosphere.ApplyFairWeatherClouds();
            PrefabUtility.RecordPrefabInstancePropertyModifications(atmosphere);
            EditorUtility.SetDirty(atmosphere);
        }

        [MenuItem("GameObject/Water System/Ocean", false, 10)]
        public static void CreateOcean(MenuCommand command)
        {
            var ocean = new GameObject("Quadtree Ocean");
            GameObjectUtility.SetParentAndAlign(ocean, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(ocean, "Create Quadtree Ocean");
            Undo.AddComponent<OceanRenderer>(ocean);
            Selection.activeGameObject = ocean;
        }

        [MenuItem("GameObject/Water System/Lake", false, 11)]
        public static void CreateLake(MenuCommand command)
        {
            var lake = GameObject.CreatePrimitive(PrimitiveType.Plane);
            lake.name = "Lake Surface";
            GameObjectUtility.SetParentAndAlign(lake, command.context as GameObject);
            var collider = lake.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
            Undo.RegisterCreatedObjectUndo(lake, "Create Lake Surface");
            Undo.AddComponent<LakeRenderer>(lake);
            Selection.activeGameObject = lake;
        }

        [MenuItem("GameObject/Water System/Atmosphere", false, 12)]
        public static void CreateAtmosphere(MenuCommand command)
        {
            var atmosphere = new GameObject("Atmosphere");
            GameObjectUtility.SetParentAndAlign(atmosphere, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(atmosphere, "Create Atmosphere");
            Undo.AddComponent<AtmosphereRenderer>(atmosphere);
            Selection.activeGameObject = atmosphere;
        }
    }
}
