using UnityEditor;
using UnityEngine;

namespace WaterSystem.Ocean.Editor
{
    [CustomEditor(typeof(OceanRenderer))]
    public sealed class OceanRendererEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var ocean = (OceanRenderer)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox($"Minimum patch: {ocean.MinimumPatchSize:0.##} m\n" +
                $"Finest vertex spacing: {ocean.MinimumPatchSize / ocean.PatchResolution:0.###} m\n" +
                $"Last camera: {ocean.LastCameraName}\n" +
                $"Leaves: {ocean.LastLeafCount}  Visible: {ocean.LastVisibleCount}\n" +
                $"Instance submissions: {ocean.LastDrawCount}  Triangles: {ocean.LastTriangleCount:N0}", MessageType.Info);
            EditorGUILayout.HelpBox("OceanSettings and FFTCompute are connected automatically. Edit Rendering here or through ocean.Rendering in C#. No material asset is required.",MessageType.None);
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
