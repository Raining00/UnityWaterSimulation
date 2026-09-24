using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace WaterSystem.Ocean
{
    // Matches SurfaceQuery.compute: float3 + float + float3 + float = 32 bytes.
    [StructLayout(LayoutKind.Sequential)]
    public struct OceanSurfaceQueryResult
    {
        public Vector3 Position;
        public float SignedDepth;
        public Vector3 Normal;
        public float Reserved;
    }

    /// <summary>
    /// Records batched surface queries after the active ocean simulation has recorded its work.
    /// This component deliberately depends on OceanSurfaceQueryResources instead of FFTCompute.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Water System/Ocean Surface Queries")]
    public sealed class OceanSurfaceQuerySystem : MonoBehaviour
    {
        const int ResultStride = sizeof(float) * 8;
        const int MaxInFlight = 3;

        OceanRenderer owner;
        readonly HashSet<BouyantBody> activeBodies = new();
        readonly List<QuerySlot> slots = new(MaxInFlight);
        int generation;
        long nextRequestId;
        bool readbackErrorLogged;

        sealed class QuerySlot
        {
            public ComputeBuffer Points;
            public ComputeBuffer Results;
            public int Capacity;
            public int Count;
            public int Generation;
            public long RequestId;
            public double SimulationTime;
            public bool Busy;
            public bool Completed;
            public bool Failed;
            public bool Retired;
            public Exception Error;
            public OceanSurfaceQueryResult[] CpuResults = Array.Empty<OceanSurfaceQueryResult>();
            public readonly List<BodyQueryRange> Ranges = new();
            public Action<AsyncGPUReadbackRequest> Callback;

            public void DisposeBuffers()
            {
                Points?.Release();
                Results?.Release();
                Points = Results = null;
                Capacity = 0;
            }
        }

        /// <summary>True after the owning ocean has initialized this service.</summary>
        public bool IsInitialized { get; private set; }

        /// <summary>Last frame in which query work was considered for recording.</summary>
        public int LastRecordedFrame { get; private set; } = -1;

        Vector3[] queryPoints = Array.Empty<Vector3>();
        
        public int QueryPointCount { get; private set; }

        public readonly struct BodyQueryRange
        {
            public readonly BouyantBody Body;
            public readonly int StartIndex;
            public readonly int Count;
            public readonly int Version;

            public BodyQueryRange(BouyantBody body, int startIndex, int count, int version)
            {
                Body = body;
                StartIndex = startIndex;
                Count = count;
                Version = version;
            }
        }

        [SerializeField, HideInInspector] private ComputeShader queryShader;
        int queryKernelIndex = -1;
        uint queryThreadGroupSizeX;

        private static class QueryShaderIDs
        {
            public static readonly int QueryPointCount = Shader.PropertyToID("_QueryPointCount");
            public static readonly int FFTCascadeCount = Shader.PropertyToID("_FFTCascadeCount");
            public static readonly int FFTResolution = Shader.PropertyToID("_FFTResolution");
            public static readonly int OceanDomainSizes = Shader.PropertyToID("_OceanDomainSizes");
            public static readonly int OceanSeaLevel = Shader.PropertyToID("_OceanSeaLevel");
            public static readonly int QueryPoints = Shader.PropertyToID("_QueryPoints");
            public static readonly int QueryResults = Shader.PropertyToID("_QueryResults");
            public static readonly int OceanDisplacement = Shader.PropertyToID("_OceanDisplacement");
            public static readonly int OceanNormals = Shader.PropertyToID("_OceanNormals");
        }

        internal void Initialize(OceanRenderer ocean)
        {
            if (ocean == null) throw new ArgumentNullException(nameof(ocean));
            Release();
            owner = ocean;
            queryShader = OceanResources.Load().SurfaceQuery;
            if (queryShader == null)
                throw new InvalidOperationException("OceanDefaults is missing SurfaceQuery.compute.");
            if (!queryShader.HasKernel("QueryKernel"))
                throw new InvalidOperationException("SurfaceQuery.compute is missing QueryKernel.");
            queryKernelIndex = queryShader.FindKernel("QueryKernel");
            queryShader.GetKernelThreadGroupSizes(queryKernelIndex, out queryThreadGroupSizeX, out uint sizeY, out uint sizeZ);
            if (queryThreadGroupSizeX == 0 || sizeY == 0 || sizeZ == 0)
                throw new InvalidOperationException("SurfaceQuery.compute has an invalid thread-group size.");
            IsInitialized = true;

            // OnEnable may have run before the ocean had a query component, or may not run again
            // when entering Play Mode with scene/domain reload disabled. Repair registration once
            // per initialization, rather than searching the scene every simulation frame.
            activeBodies.RemoveWhere(body => body == null || !body.isActiveAndEnabled || body.Ocean != ocean);
            foreach (var body in FindObjectsByType<BouyantBody>(FindObjectsInactive.Exclude))
                if (body.isActiveAndEnabled && body.Ocean == ocean) activeBodies.Add(body);
        }

        public void Register(BouyantBody body)
        {
            if (body != null) activeBodies.Add(body);
        }

        public void Unregister(BouyantBody body)
        {
            if (ReferenceEquals(body, null)) return;
            activeBodies.Remove(body);
        }

        void Update() => PublishCompleted();

        /// <summary>
        /// Appends query commands to the same command buffer as the simulation. Commands recorded
        /// here execute after FFT displacement/normal generation because command buffer order is preserved.
        /// </summary>
        internal void RecordQueries(
            CommandBuffer commands,
            in OceanSurfaceQueryResources resources,
            in OceanSimulationFrame frame)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            PublishCompleted();
            if (!IsInitialized || owner == null || !resources.IsValid) return;

            LastRecordedFrame = frame.FrameIndex;

            // Registration is intentionally independent of point counts: a body may regenerate its
            // samples after registration. Recalculate the exact batch size from the current state.
            activeBodies.RemoveWhere(body => body == null);
            int requiredPointCount = 0;
            foreach (var body in activeBodies)
            {
                if (!body.isActiveAndEnabled) continue;
                requiredPointCount += body.SamplePointCount;
            }

            QueryPointCount = requiredPointCount;
            if (requiredPointCount == 0 || !SystemInfo.supportsAsyncGPUReadback) return;

            QuerySlot slot = GetFreeSlot(requiredPointCount);
            if (slot == null) return; // Keep the last result rather than waiting for the GPU.

            if (queryPoints.Length < requiredPointCount)
                Array.Resize(ref queryPoints, Mathf.NextPowerOfTwo(Mathf.Max(1, requiredPointCount)));

            int offset = 0;
            slot.Ranges.Clear();
            foreach (BouyantBody body in activeBodies)
            {
                if (body == null || body.SamplePointCount == 0 || !body.isActiveAndEnabled) continue;
                int startIndex = offset;
                for (int i = 0; i < body.SamplePointCount; i++)
                    queryPoints[offset + i] = body.GetWorldSamplePoint(i);
                offset += body.SamplePointCount;
                slot.Ranges.Add(new BodyQueryRange(body, startIndex, body.SamplePointCount, body.QueryVersion));
            }
            QueryPointCount = offset;
            // Debug.Log("Total Query Points: " + QueryPointCount);
            if (QueryPointCount == 0) return;

            slot.Points.SetData(queryPoints, 0, 0, QueryPointCount);
            commands.SetComputeIntParam(queryShader, QueryShaderIDs.QueryPointCount, QueryPointCount);
            commands.SetComputeIntParam(queryShader, QueryShaderIDs.FFTCascadeCount, resources.CascadeCount);
            commands.SetComputeIntParam(queryShader, QueryShaderIDs.FFTResolution, resources.Resolution);
            commands.SetComputeVectorParam(queryShader, QueryShaderIDs.OceanDomainSizes, resources.DomainSizes);
            commands.SetComputeFloatParam(queryShader, QueryShaderIDs.OceanSeaLevel, resources.SeaLevel);
            commands.SetComputeTextureParam(queryShader, queryKernelIndex, QueryShaderIDs.OceanDisplacement, resources.Displacement);
            commands.SetComputeTextureParam(queryShader, queryKernelIndex, QueryShaderIDs.OceanNormals, resources.Normal);
            commands.SetComputeBufferParam(queryShader, queryKernelIndex, QueryShaderIDs.QueryPoints, slot.Points);
            commands.SetComputeBufferParam(queryShader, queryKernelIndex, QueryShaderIDs.QueryResults, slot.Results);
            commands.DispatchCompute(queryShader, queryKernelIndex,
                (QueryPointCount + (int)queryThreadGroupSizeX - 1) / (int)queryThreadGroupSizeX, 1, 1);

            slot.Count = QueryPointCount;
            slot.Generation = generation;
            slot.RequestId = ++nextRequestId;
            slot.SimulationTime = resources.SimulationTime;
            slot.Failed = slot.Completed = false;
            slot.Error = null;
            commands.RequestAsyncReadback(slot.Results, QueryPointCount * ResultStride, 0, slot.Callback);
            slot.Busy = true;
        }

        QuerySlot GetFreeSlot(int requiredCount)
        {
            QuerySlot slot = null;
            foreach (var candidate in slots)
                if (!candidate.Busy) { slot = candidate; break; }
            if (slot == null && slots.Count < MaxInFlight)
            {
                slot = new QuerySlot();
                QuerySlot captured = slot;
                slot.Callback = request => OnReadback(captured, request);
                slots.Add(slot);
            }
            if (slot == null) return null;

            if (slot.Points != null && slot.Results != null && slot.Capacity >= requiredCount) return slot;
            slot.DisposeBuffers();
            slot.Capacity = Mathf.NextPowerOfTwo(Mathf.Max(1, requiredCount));
            try
            {
                slot.Points = new ComputeBuffer(slot.Capacity, sizeof(float) * 3, ComputeBufferType.Structured);
                slot.Results = new ComputeBuffer(slot.Capacity, ResultStride, ComputeBufferType.Structured);
                slot.CpuResults = new OceanSurfaceQueryResult[slot.Capacity];
            }
            catch { slot.DisposeBuffers(); throw; }
            return slot;
        }

        void OnReadback(QuerySlot slot, AsyncGPUReadbackRequest request)
        {
            if (slot.Retired || slot.Generation != generation)
            {
                slot.DisposeBuffers();
                return;
            }
            try
            {
                slot.Failed = request.hasError;
                if (!slot.Failed)
                {
                    var data = request.GetData<OceanSurfaceQueryResult>();
                    if (data.Length < slot.Count) slot.Failed = true;
                    else for (int i = 0; i < slot.Count; i++) slot.CpuResults[i] = data[i];
                }
            }
            catch (Exception exception)
            {
                slot.Failed = true;
                slot.Error = exception;
            }
            slot.Completed = true;
        }

        void PublishCompleted()
        {
            if (!IsInitialized) return;
            foreach (var slot in slots)
            {
                if (!slot.Busy || !slot.Completed) continue;
                if (slot.Failed && !readbackErrorLogged)
                {
                    if (slot.Error != null) Debug.LogException(slot.Error, this);
                    else Debug.LogWarning("Ocean surface query GPU readback failed; keeping the last valid results.", this);
                    readbackErrorLogged = true;
                }
                if (!slot.Failed && slot.Generation == generation)
                    foreach (var range in slot.Ranges)
                    {
                        var body = range.Body;
                        if (body == null || !body.isActiveAndEnabled || body.Ocean != owner ||
                            !activeBodies.Contains(body) || body.QueryVersion != range.Version) continue;
                        body.AcceptSurfaceResults(slot.CpuResults, range.StartIndex, range.Count,
                            slot.RequestId, slot.SimulationTime, range.Version);
                    }
                slot.Ranges.Clear();
                slot.Busy = slot.Completed = false;
                slot.Error = null;
            }
        }

        internal void Release()
        {
            generation++;
            foreach (var body in activeBodies)
                if (body != null) body.ClearSurfaceResults();
            owner = null;
            IsInitialized = false;
            readbackErrorLogged = false;
            LastRecordedFrame = -1;
            QueryPointCount = 0;
            foreach (var slot in slots)
            {
                slot.Ranges.Clear();
                if (slot.Busy && !slot.Completed) slot.Retired = true;
                else slot.DisposeBuffers();
            }
            slots.Clear();
            queryShader = null;
            queryKernelIndex = -1;
            queryThreadGroupSizeX = 0;
        }

        void OnDestroy() => Release();
    }
}
