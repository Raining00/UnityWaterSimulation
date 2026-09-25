using UnityEditor;
using UnityEngine;

namespace WaterSystem.Ocean.Editor
{
    [CustomEditor(typeof(OceanRenderer))]
    public sealed class OceanRendererEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var mode = serializedObject.FindProperty("ShadingMode");
            DrawPropertiesExcluding(serializedObject, "Rendering", "Stylized", "Underwater", "StylizedUnderwater");
            // Each mode pairs its surface settings with its own underwater volume pass.
            if (mode.enumValueIndex == (int)OceanShadingMode.Physical)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Rendering"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Underwater"), true);
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Stylized"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("StylizedUnderwater"), true);
            }
            serializedObject.ApplyModifiedProperties();
            var ocean = (OceanRenderer)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox($"Minimum patch: {ocean.MinimumPatchSize:0.##} m\n" +
                $"Finest vertex spacing: {ocean.MinimumPatchSize / ocean.PatchResolution:0.###} m\n" +
                $"Last camera: {ocean.LastCameraName}\n" +
                $"Leaves: {ocean.LastLeafCount}  Visible: {ocean.LastVisibleCount}\n" +
                $"Instance submissions: {ocean.LastDrawCount}  Triangles: {ocean.LastTriangleCount:N0}", MessageType.Info);
            EditorGUILayout.HelpBox("OceanSettings and FFTCompute are connected automatically. Select Shading Mode, then edit Rendering / Stylized here or through C#. Style changes preserve the FFT simulation. Materials are managed internally.",MessageType.None);
            if (ocean.transform.rotation != Quaternion.identity || ocean.transform.lossyScale != Vector3.one)
                EditorGUILayout.HelpBox("The ocean is world-aligned. Transform rotation and scale are ignored; change Root Size to resize it.", MessageType.Info);
            if (GUILayout.Button("Rebuild Geometry / Reinitialize Simulation"))
            {
                ocean.RequestRebuild();
                SceneView.RepaintAll();
            }
        }
        public override bool RequiresConstantRepaint() => true;
    }
}
