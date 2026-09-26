using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace WaterSystem.Ocean
{
    [ExecuteAlways, DisallowMultipleComponent]
    [RequireComponent(typeof(OceanSettings), typeof(FFTCompute), typeof(OceanSurfaceQuerySystem))]
    [RequireComponent(typeof(OceanWakeSystem))]
    [AddComponentMenu("Water System/Ocean")]
    public sealed class OceanRenderer : MonoBehaviour
    {
        [Header("Geometry (world metres; transform rotation/scale are ignored)")]
        [Min(1)] public float RootSize = 2048;
        [Range(0, 8)] public int MaxDepth = 7;
        [Tooltip("Even power of two: quads along each patch edge. Independent of FFT resolution.")]
        [Range(2, 128)] public int PatchResolution = 16;
        [Range(0.25f, 4)] public float LodDistance = 1.5f;
        public bool FollowCamera = true;
        [Tooltip("Maximum wave displacement used for conservative CPU and draw bounds. X: horizontal, Y: vertical.")]
        public Vector2 DisplacementPadding = new Vector2(2, 10);

        [Header("Rendering")]
        public OceanShadingMode ShadingMode = OceanShadingMode.Physical;
        public OceanStylizedSettings Stylized = new OceanStylizedSettings();
        public OceanRenderSettings Rendering = new OceanRenderSettings();
        public OceanUnderwaterSettings Underwater = new OceanUnderwaterSettings();
        // Stylized mode has its own volume pass and palette, so it does not share Underwater above.
        public OceanStylizedUnderwaterSettings StylizedUnderwater = new OceanStylizedUnderwaterSettings();
        [SerializeField, HideInInspector, FormerlySerializedAs("SurfaceMaterial")] Material legacySurfaceMaterial;
        public bool PreviewSimulation = true;
        [Header("Horizon")]
        public bool InfiniteHorizon = true;
        [Min(1000)] public float HorizonDistance = 100000;
        [Tooltip("Optional Game camera filter. Scene view uses its own independent tree.")]
        public Camera TargetCamera;
        public bool ShowInSceneView = true;
        public bool ReceiveShadows = true;
        public bool DrawLodGizmos;

        [HideInInspector] public OceanSimulationProvider Simulation;
        public OceanSettings Waves => GetComponent<OceanSettings>();
        public FFTCompute FFT => GetComponent<FFTCompute>();
        public OceanSurfaceQuerySystem SurfaceQueries
        {
            get
            {
                if (surfaceQuerySystem == null)
                    surfaceQuerySystem = GetComponent<OceanSurfaceQuerySystem>();

                return surfaceQuerySystem;
            }
        }

        public int LastLeafCount { get; private set; }
        public int LastVisibleCount { get; private set; }
        public int LastDrawCount { get; private set; }
        public int LastTriangleCount { get; private set; }
        public string LastCameraName { get; private set; }
        public float MinimumPatchSize => RootSize / (1 << Mathf.Clamp(MaxDepth, 0, 8));
        public Material RuntimeMaterial => runtimeMaterial;

        static readonly List<OceanRenderer> activeOceans = new List<OceanRenderer>();

        internal static OceanRenderer FindUnderwaterOcean(Camera camera)
        {
            if (camera == null || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)) return null;
            OceanRenderer selected = null;
            float closest = float.PositiveInfinity;
            float nearRadius = camera.orthographic ? camera.orthographicSize * Mathf.Sqrt(1 + camera.aspect * camera.aspect)
                : camera.nearClipPlane * (1 + Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) * Mathf.Sqrt(1 + camera.aspect * camera.aspect));
            foreach (var value in activeOceans)
            {
                if (value == null || !value.isActiveAndEnabled || (camera.cullingMask & (1 << value.gameObject.layer)) == 0) continue;
                // Each shading mode owns its own underwater toggle, so the pass only runs for the one in use.
                bool underwaterOn = value.ShadingMode == OceanShadingMode.Stylized
                    ? value.StylizedUnderwater != null && value.StylizedUnderwater.Enabled
                    : value.Underwater != null && value.Underwater.Enabled;
                if (!underwaterOn) continue;
                if (camera.cameraType == CameraType.SceneView ? !value.ShowInSceneView : value.TargetCamera != null && value.TargetCamera != camera) continue;
                float margin = Mathf.Max(value.DisplacementPadding.y, value.Waves.maximumDisplacement.y) + nearRadius;
                if (camera.transform.position.y > value.transform.position.y + margin) continue;
                var center = value.CameraCenter(camera);
                if (!value.InfiniteHorizon && (Mathf.Abs(center.x-camera.transform.position.x)>value.RootSize*0.5f+nearRadius ||
                    Mathf.Abs(center.z-camera.transform.position.z)>value.RootSize*0.5f+nearRadius)) continue;
                float difference = Mathf.Abs(camera.transform.position.y-center.y);
                if (difference < closest) { closest = difference; selected = value; }
            }
            return selected;
        }

        Vector3 CameraCenter(Camera camera)
        {
            var center = transform.position;
            if (FollowCamera)
            {
                float step = MinimumPatchSize;
                center.x += Mathf.Round((camera.transform.position.x-center.x)/step)*step;
                center.z += Mathf.Round((camera.transform.position.z-center.z)/step)*step;
            }
            return center;
        }

        internal void BindUnderwater(MaterialPropertyBlock properties, Camera camera)
        {
            var center = CameraCenter(camera);
            properties.SetVector(OriginId, new Vector4(center.x,center.y,center.z,RootSize));
            properties.SetFloat("_OceanInfinite", InfiniteHorizon ? 1 : 0);
            properties.SetInt("_OceanSimulationReady", 0);
            if (activeSimulation != null && !simulationFailed) activeSimulation.BindResources(properties, camera);
            Underwater.Bind(properties, Rendering);
            // Wall-clock time drives the flow distortion, so the wobble stays alive even when the
            // FFT is paused. Wave-slope detail still comes from _OceanSimulationTime via the textures.
            properties.SetFloat("_UnderwaterTime", (float)Time.timeAsDouble);
            var sun = RenderSettings.sun;
            bool sunActive = sun != null && sun.isActiveAndEnabled;
            Color light = sunActive ? sun.color.linear * sun.intensity * Mathf.Clamp01(-sun.transform.forward.y) : Color.gray;
            properties.SetColor("_UnderwaterSun", light);
            // The shaft march normalises this, so it must never be the zero vector. Without an active
            // sun the beams simply fall back to straight overhead.
            Vector3 toSun = sunActive ? -sun.transform.forward : Vector3.up;
            properties.SetVector("_UnderwaterSunDirection", new Vector4(toSun.x, toSun.y, toSun.z, 0));
        }

        // The stylized volume pass shares the geometry and light inputs above but has its own palette,
        // so it binds through OceanStylizedUnderwaterSettings instead of the physical settings block.
        internal void BindStylizedUnderwater(MaterialPropertyBlock properties, Camera camera)
        {
            var center = CameraCenter(camera);
            properties.SetVector(OriginId, new Vector4(center.x,center.y,center.z,RootSize));
            properties.SetFloat("_OceanInfinite", InfiniteHorizon ? 1 : 0);
            properties.SetInt("_OceanSimulationReady", 0);
            if (activeSimulation != null && !simulationFailed) activeSimulation.BindResources(properties, camera);
            StylizedUnderwater.Bind(properties, Stylized, OceanResources.Load());
            properties.SetFloat("_UnderwaterTime", (float)Time.timeAsDouble);
            var sun = RenderSettings.sun;
            bool sunActive = sun != null && sun.isActiveAndEnabled;
            Color light = sunActive ? sun.color.linear * sun.intensity * Mathf.Clamp01(-sun.transform.forward.y) : Color.gray;
            properties.SetColor("_UnderwaterSun", light);
            Vector3 toSun = sunActive ? -sun.transform.forward : Vector3.up;
            properties.SetVector("_UnderwaterSunDirection", new Vector4(toSun.x, toSun.y, toSun.z, 0));
        }

        sealed class CameraState
        {
            public readonly OceanQuadTree Tree = new OceanQuadTree();
            public readonly Plane[] Planes = new Plane[6];
            public readonly OceanInstanceBatch[] Batches = new OceanInstanceBatch[16];
            public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
            public readonly OceanInstanceBatch Horizon = new OceanInstanceBatch();
            public Vector3 BuiltCamera, BuiltCenter;
            public bool Built;
            public int LastSeenFrame;
            public CameraState() { for (int i = 0; i < 16; i++) Batches[i] = new OceanInstanceBatch(); }
        }

        readonly Dictionary<Camera, CameraState> cameras = new Dictionary<Camera, CameraState>();
        readonly List<Camera> expiredCameras = new List<Camera>();
        Mesh[] meshes;
        int[] triangleCounts;
        Material runtimeMaterial;
        Mesh horizonMesh;
        int geometryHash;
        OceanSimulationProvider activeSimulation;
        OceanSurfaceQuerySystem surfaceQuerySystem;
        OceanWakeSystem wakeSystem;
        CommandBuffer simulationCommands;
        int simulatedFrame = -1;
        double previousTime;
        bool rebuildRequested, unsupportedReported, simulationFailed;
        CameraState lastState;
        static readonly int TimeId = Shader.PropertyToID("_OceanTime");
        static readonly int OriginId = Shader.PropertyToID("_OceanOrigin");

        void OnEnable()
        {
            EnsureComponents();
            if (!activeOceans.Contains(this)) activeOceans.Add(this);
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            rebuildRequested = true;
        }
        void Reset() => EnsureComponents();
        public void EnsureComponents()
        {
            if(GetComponent<OceanSettings>()==null)gameObject.AddComponent<OceanSettings>();
            var fft=GetComponent<FFTCompute>();
            if(fft==null)fft=gameObject.AddComponent<FFTCompute>();
            surfaceQuerySystem=GetComponent<OceanSurfaceQuerySystem>();
            if(surfaceQuerySystem==null)surfaceQuerySystem=gameObject.AddComponent<OceanSurfaceQuerySystem>();
            wakeSystem=GetComponent<OceanWakeSystem>();
            if(wakeSystem==null)wakeSystem=gameObject.AddComponent<OceanWakeSystem>();
            Simulation=fft;
            fft.RunInEditMode=PreviewSimulation;
            Rendering ??= new OceanRenderSettings();
            Stylized ??= new OceanStylizedSettings();
            Underwater ??= new OceanUnderwaterSettings();
            StylizedUnderwater ??= new OceanStylizedUnderwaterSettings();
            if(legacySurfaceMaterial!=null)
            {
                Rendering.ImportLegacy(legacySurfaceMaterial);
                legacySurfaceMaterial=null;
            }
        }
        void OnDisable()
        {
            activeOceans.Remove(this);
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            ReleaseResources();
        }
        void OnValidate()
        {
            RootSize = Mathf.Max(1, RootSize);
            MaxDepth = Mathf.Clamp(MaxDepth, 0, 8);
            PatchResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(PatchResolution), 2, 128);
            LodDistance = Mathf.Clamp(LodDistance, 0.25f, 4);
            DisplacementPadding = Vector2.Max(Vector2.zero, DisplacementPadding);
            HorizonDistance=Mathf.Max(1000,HorizonDistance);
            // No resource work on import threads. Geometry changes are detected in EnsureResources;
            // appearance edits apply live without resetting the FFT or foam history.
        }
        [ContextMenu("Rebuild Ocean Resources")]
        public void RequestRebuild() => rebuildRequested = true;

        void EnsureResources()
        {
            EnsureComponents();
            int hash=HashCode.Combine(RootSize,MaxDepth,PatchResolution,LodDistance);
            if (rebuildRequested || geometryHash != hash)
            {
                ReleaseResources();
                rebuildRequested = false;
                geometryHash=hash;
            }
            var resources=OceanResources.Load();
            var shader = ShadingMode == OceanShadingMode.Stylized ? resources.StylizedSurfaceShader : resources.SurfaceShader;
            if (shader == null) throw new InvalidOperationException("OceanDefaults is missing its " + ShadingMode + " surface shader.");
            if (runtimeMaterial == null || runtimeMaterial.shader != shader)
            {
                // Appearance switches must not release geometry, FFT textures or particle history.
                DestroyOwned(runtimeMaterial);
                runtimeMaterial = new Material(shader)
                {
                    name = name + " (" + ShadingMode + " ocean runtime material)",
                    hideFlags = HideFlags.HideAndDontSave,
                    enableInstancing = true
                };
            }
            if (meshes != null) return;
            horizonMesh=OceanHorizonMesh.Create();
            meshes = new Mesh[16];
            triangleCounts = new int[16];
            for (int i = 0; i < 16; i++)
            {
                meshes[i] = OceanPatchMesh.Create(PatchResolution, (OceanSeam)i);
                triangleCounts[i] = (int)meshes[i].GetIndexCount(0) / 3;
            }
        }

        void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!isActiveAndEnabled || camera == null) return;
            if (camera.cameraType == CameraType.SceneView) { if (!ShowInSceneView) return; }
            else if (camera.cameraType != CameraType.Game || (TargetCamera != null && camera != TargetCamera)) return;
            if ((camera.cullingMask & (1 << gameObject.layer)) == 0) return;
            if (!SystemInfo.supportsInstancing)
            {
                if (!unsupportedReported) Debug.LogError("Quadtree Ocean requires GPU instancing support.", this);
                unsupportedReported = true;
                return;
            }
            try { EnsureResources(); }
            catch (Exception exception) { Debug.LogException(exception, this); enabled = false; return; }
            if (ShadingMode == OceanShadingMode.Stylized) Stylized.Apply(runtimeMaterial, OceanResources.Load());
            else { Rendering.Apply(runtimeMaterial, OceanResources.Load()); Underwater.ApplySurface(runtimeMaterial); }
            UpdateSimulation();
            if (!cameras.TryGetValue(camera, out var state))
            {
                state = new CameraState();
                cameras.Add(camera, state);
            }
            state.LastSeenFrame = Time.frameCount;
            Vector3 position = camera.transform.position;
            float minSize = MinimumPatchSize;
            Vector3 center = transform.position;
            if (FollowCamera)
            {
                center.x += Mathf.Round((position.x - center.x) / minSize) * minSize;
                center.z += Mathf.Round((position.z - center.z) / minSize) * minSize;
            }
            if (!state.Built || center != state.BuiltCenter || (position - state.BuiltCamera).sqrMagnitude > minSize * minSize * 0.0625f)
            {
                state.Tree.Build(center, RootSize, MaxDepth, LodDistance, position);
                state.BuiltCenter = center;
                state.BuiltCamera = position;
                state.Built = true;
            }
            Vector2 padding = DisplacementPadding;
            if (activeSimulation != null && !simulationFailed) padding = Vector2.Max(padding, activeSimulation.MaximumDisplacement);
            GeometryUtility.CalculateFrustumPlanes(camera, state.Planes);
            foreach (var batch in state.Batches) batch.Clear();
            LastVisibleCount = LastDrawCount = LastTriangleCount = 0;
            var tree = state.Tree;
            for (int i = 0; i < tree.Leaves.Count; i++)
            {
                var tile = tree.Leaves[i];
                var bounds = tree.GetBounds(tile, padding.x, padding.y);
                if (!OceanHorizonMesh.Visible(bounds,state.Planes,InfiniteHorizon)) continue;
                float size = tile.Span * tree.CellSize;
                var matrix = Matrix4x4.TRS(tree.GetTileCenter(tile), Quaternion.identity, new Vector3(size, 1, size));
                int variant = (int)tile.Seams;
                // Our CPU culling deliberately omits the far plane for infinite water. Avoid a second
                // far-plane rejection inside Graphics.RenderMeshInstanced by including the camera in its bounds.
                if(InfiniteHorizon)bounds.Encapsulate(position);
                state.Batches[variant].Add(matrix, bounds);
                LastVisibleCount++;
                LastTriangleCount += triangleCounts[variant];
            }
            state.Properties.Clear();
            state.Properties.SetFloat(TimeId, (float)Time.timeAsDouble);
            state.Properties.SetVector(OriginId, new Vector4(center.x, center.y, center.z, RootSize));
            float horizonExtent=OceanHorizonMesh.Extent(camera,center,RootSize,HorizonDistance);
            state.Properties.SetFloat("_OceanInfinite",InfiniteHorizon?1:0);
            state.Properties.SetFloat("_OceanHorizonExtent",horizonExtent);
            if (activeSimulation != null && !simulationFailed)
            {
                try { activeSimulation.BindResources(state.Properties, camera); }
                catch (Exception exception) { FailSimulation(exception); }
            }
            wakeSystem?.Bind(state.Properties);
            for (int i = 0; i < 16; i++)
                LastDrawCount += state.Batches[i].Draw(meshes[i], runtimeMaterial, state.Properties, camera, gameObject.layer, ReceiveShadows);
            if(InfiniteHorizon)
            {
                state.Horizon.Clear();
                var bounds=new Bounds(center,new Vector3(horizonExtent*2,Mathf.Max(1,padding.y*2),horizonExtent*2));
                bounds.Encapsulate(position);
                state.Horizon.Add(Matrix4x4.TRS(center,Quaternion.identity,Vector3.one),bounds);
                LastDrawCount+=state.Horizon.Draw(horizonMesh,runtimeMaterial,state.Properties,camera,gameObject.layer,ReceiveShadows);
                LastTriangleCount+=1;
            }
            LastLeafCount = tree.Leaves.Count;
            LastCameraName = camera.name;
            lastState = state;
        }

        void UpdateSimulation()
        {
            var desired = Simulation != null && Simulation.isActiveAndEnabled && (Application.isPlaying || Simulation.RunInEditMode) ? Simulation : null;
            if (activeSimulation != desired)
            {
                ReleaseSimulation();
                activeSimulation = desired;
                if (activeSimulation != null)
                {
                    try
                    {
                        var init = new OceanSimulationContext(this);
                        activeSimulation.Initialize(in init);
                        surfaceQuerySystem?.Initialize(this);
                        simulationCommands = new CommandBuffer { name = "Ocean / FFT simulation" };
                    }
                    catch (Exception exception) { FailSimulation(exception); }
                }
            }
            if (simulatedFrame == Time.frameCount) return;
            simulatedFrame = Time.frameCount;
            // Keep state for active cameras, and release matrices belonging to destroyed/inactive views.
            expiredCameras.Clear();
            foreach (var pair in cameras)
                if (pair.Key == null || Time.frameCount - pair.Value.LastSeenFrame > 120) expiredCameras.Add(pair.Key);
            foreach (var expired in expiredCameras) cameras.Remove(expired);
            double now = Time.timeAsDouble;
            float delta = previousTime == 0 ? 0 : Mathf.Clamp((float)(now - previousTime), 0, 0.1f);
            previousTime = now;
            if (activeSimulation == null || simulationFailed) return;
            try
            {
                simulationCommands.Clear();
                var frame = new OceanSimulationFrame(now, delta, simulatedFrame);
                activeSimulation.RecordSimulation(simulationCommands, in frame);

                // One world-space foam update per frame, after FFT production and before any camera draw.
                wakeSystem?.RecordUpdate(simulationCommands, delta);

                // Query work is appended after simulation work to the same command buffer. The
                // query system receives a narrow immutable resource view, never the FFT component.
                if (surfaceQuerySystem != null && surfaceQuerySystem.isActiveAndEnabled &&
                    activeSimulation.TryGetSurfaceQueryResources(out var queryResources))
                    surfaceQuerySystem.RecordQueries(simulationCommands, in queryResources, in frame);

                // Submit once, before any ocean draw in this frame. The provider records its compute dispatches here.
                Graphics.ExecuteCommandBuffer(simulationCommands);
            }
            catch (Exception exception) { FailSimulation(exception); }
        }

        void FailSimulation(Exception exception)
        {
            simulationFailed = true;
            Debug.LogException(exception, this);
            // Avoid repeated failed GPU dispatches; geometry continues to render. Rebuild retries initialization.
        }
        void ReleaseSimulation()
        {
            surfaceQuerySystem?.Release();
            if (activeSimulation != null)
            {
                try { activeSimulation.Release(); }
                catch (Exception exception) { Debug.LogException(exception, this); }
            }
            activeSimulation = null;
            simulationCommands?.Release();
            simulationCommands = null;
            simulationFailed = false;
            simulatedFrame = -1;
            previousTime = 0;
        }
        void ReleaseResources()
        {
            ReleaseSimulation();
            if (meshes != null) foreach (var mesh in meshes) DestroyOwned(mesh);
            DestroyOwned(runtimeMaterial);
            DestroyOwned(horizonMesh);
            horizonMesh=null;
            meshes = null;
            runtimeMaterial = null;
            cameras.Clear();
            lastState = null;
            LastLeafCount = LastVisibleCount = LastDrawCount = LastTriangleCount = 0;
        }
        static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        void OnDrawGizmosSelected()
        {
            if (!DrawLodGizmos || lastState == null) return;
            foreach (var tile in lastState.Tree.Leaves)
            {
                Gizmos.color = Color.HSVToRGB(tile.Depth / 9f, 0.85f, 1);
                var bounds = lastState.Tree.GetBounds(tile, 0, 0);
                bounds.center += Vector3.up * 0.1f;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }
        }
    }
}
