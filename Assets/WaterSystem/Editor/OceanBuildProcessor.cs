using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace WaterSystem.Ocean.Editor
{
    /// <summary>Procedural ocean materials do not exist in scenes for Unity's instancing scanner.</summary>
    public sealed class OceanBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        public void OnPreprocessBuild(BuildReport report)
        {
            OceanUnderwaterInstaller.EnsureInstalled();
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (settings.Length == 0)
                throw new BuildFailedException("Cannot load GraphicsSettings to preserve ocean GPU instancing variants.");
            var serialized = new SerializedObject(settings[0]);
            var stripping = serialized.FindProperty("m_InstancingStripping");
            if (stripping == null)
                throw new BuildFailedException("Cannot locate Instancing Stripping in GraphicsSettings.");
            // Unity 6000.5: StripUnused=0, StripAll=1, KeepAll=2.
            // A ShaderVariantCollection alone does NOT override the built-in instancing stripping.
            // Keep this project setting after build: all procedurally created instanced materials need it.
            if (stripping.intValue == 2) return;
            stripping.intValue = 2;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(settings[0]);
        }
    }
}
