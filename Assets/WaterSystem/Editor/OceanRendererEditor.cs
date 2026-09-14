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
            DrawPropertiesExcluding(serializedObject, "Rendering", "Stylized");
            EditorGUILayout.PropertyField(serializedObject.FindProperty(mode.enumValueIndex == (int)OceanShadingMode.Stylized ? "Stylized" : "Rendering"), true);
            serializedObject.ApplyModifiedProperties();
            var ocean = (OceanRenderer)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox($"Minimum patch: {ocean.MinimumPatchSize:0.##} m\n" +
                $"Finest vertex spacing: {ocean.MinimumPatchSize / ocean.PatchResolution:0.###} m\n" +
                $"Last camera: {ocean.LastCameraName}\n" +
                $"Leaves: {ocean.LastLeafCount}  Visible: {ocean.LastVisibleCount}\n" +
                $"Instance submissions: {ocean.LastDrawCount}  Triangles: {ocean.LastTriangleCount:N0}", MessageType.Info);
            EditorGUILayout.HelpBox("OceanSettings and FFTCompute are connected automatically. Select Shading Mode, then edit Rendering / Stylized and Spray here or through C#. Style changes preserve the FFT simulation. Materials and GPU spray are managed internally.",MessageType.None);
            if (ocean.Spray != null && ocean.Spray.Enabled && ocean.Waves != null &&
                Mathf.Abs(ocean.Waves.choppiness) < 0.0001f && ocean.Waves.foamThreshold <= 1)
                EditorGUILayout.HelpBox("Spray needs breaking whitecaps. Choppiness is zero, so Jacobian compression cannot generate foam at the current threshold. Use a choppy sea state or open OceanSprayDemo to see spray.", MessageType.Info);
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
