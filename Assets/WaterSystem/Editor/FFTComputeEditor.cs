using UnityEditor;
using UnityEngine;

namespace WaterSystem.Ocean.Editor
{
    [CustomEditor(typeof(FFTCompute))]
    public sealed class FFTComputeEditor : UnityEditor.Editor
    {
        readonly OceanSpectrumParameters preview = new OceanSpectrumParameters();
        public override void OnInspectorGUI()
        {
            var fft = (FFTCompute)target;
            var settings = fft.Settings != null ? fft.Settings : fft.GetComponent<OceanSettings>();
            EditorGUILayout.HelpBox(fft.Status + "\nRecorded dispatches: " + fft.LastDispatchCount +
                "\nSimulation time: " + fft.SimulationTime.ToString("F3") + " s", MessageType.Info);
            if (settings != null)
            {
                preview.UpdateFrom(settings);
                EditorGUILayout.LabelField("FFT", $"{preview.Resolution} x {preview.Resolution} x {preview.CascadeCount} cascades");
                EditorGUILayout.LabelField("Stages / axis", preview.StageCount.ToString());
                EditorGUILayout.LabelField("Texture memory (estimate)", (preview.EstimatedTextureBytes / 1048576.0).ToString("F2") + " MiB");
                for (int i = 0; i < preview.CascadeCount; i++)
                {
                    var g = preview.CascadeGeometry[i];
                    EditorGUILayout.LabelField($"Cascade {i}", $"L {g.x:G4} m; dx {g.z:G4} m; dk {g.w:G4} rad/m");
                }
            }
            if (GUILayout.Button("Reinitialize FFT")) fft.RequestReinitialize();
            EditorGUILayout.HelpBox("Managed by OceanRenderer. The default OceanFFT.compute and local OceanSettings are connected automatically. Use the main Ocean component to control edit-mode preview and appearance.", MessageType.None);
        }
        public override bool RequiresConstantRepaint() => true;
    }
}
