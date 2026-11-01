using UnityEngine;

namespace WaterSystem.Ocean
{
    [ExecuteAlways, DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Water System/Lake")]
    public sealed class LakeRenderer : MonoBehaviour
    {
        [Header("Lake surface")]
        [Tooltip("Uses the MeshFilter mesh on this object. The lake surface should be flat and suitably subdivided.")]
        public LakeRenderSettings Rendering = new LakeRenderSettings();
        [SerializeField, HideInInspector] int settingsVersion;

        MeshRenderer meshRenderer;
        Material runtimeMaterial;
        Material previousMaterial;
        Shader activeShader;

        void OnEnable()
        {
            UpgradeSettings();
            Rendering ??= new LakeRenderSettings();
            Rendering.Validate();
            meshRenderer = GetComponent<MeshRenderer>();
            EnsureMaterial();
            ApplySettings();
        }

        void OnValidate()
        {
            UpgradeSettings();
            Rendering ??= new LakeRenderSettings();
            Rendering.Validate();
            if (isActiveAndEnabled)
            {
                EnsureMaterial();
                ApplySettings();
            }
        }

        void LateUpdate()
        {
            if (isActiveAndEnabled)
            {
                EnsureMaterial();
                ApplySettings();
            }
        }

        void UpgradeSettings()
        {
            if (settingsVersion >= 1) return;

            Rendering ??= new LakeRenderSettings();
            var previousShaderDefault = new Color(0f, 0.6810271f, 0.6886792f, 1f);
            if (Approximately(Rendering.ShallowColor, previousShaderDefault))
                Rendering.ShallowColor = new Color(0f, 0.754717f, 0.47389516f, 1f);

            settingsVersion = 1;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        static bool Approximately(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r) && Mathf.Approximately(a.g, b.g) &&
                   Mathf.Approximately(a.b, b.b) && Mathf.Approximately(a.a, b.a);
        }

        void OnDisable()
        {
            if (meshRenderer != null && meshRenderer.sharedMaterial == runtimeMaterial)
                meshRenderer.sharedMaterial = previousMaterial;
            DestroyOwned(runtimeMaterial);
            runtimeMaterial = null;
            activeShader = null;
        }

        void EnsureMaterial()
        {
            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
            var resources = LakeResources.Load();
            if (resources.SurfaceShader == null)
                throw new System.InvalidOperationException("LakeDefaults is missing its lake surface shader.");

            if (runtimeMaterial != null && activeShader == resources.SurfaceShader) return;

            if (meshRenderer.sharedMaterial != runtimeMaterial)
                previousMaterial = meshRenderer.sharedMaterial;
            DestroyOwned(runtimeMaterial);
            activeShader = resources.SurfaceShader;
            runtimeMaterial = new Material(activeShader)
            {
                name = name + " (lake runtime material)",
                hideFlags = HideFlags.HideAndDontSave
            };
            runtimeMaterial.SetTexture("_NormalMap", resources.NormalMap);
            runtimeMaterial.SetTexture("_FoamMask", resources.FoamMask);
            runtimeMaterial.SetTexture("_FoamNormal", resources.FoamNormal);
            meshRenderer.sharedMaterial = runtimeMaterial;
        }

        void ApplySettings()
        {
            if (runtimeMaterial != null) Rendering.Apply(runtimeMaterial);
        }

        static void DestroyOwned(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
