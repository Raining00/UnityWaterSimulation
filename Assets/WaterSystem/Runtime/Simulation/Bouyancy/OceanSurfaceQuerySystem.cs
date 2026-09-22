using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WaterSystem.Ocean
{
    /// <summary>
    /// Records batched surface queries after the active ocean simulation has recorded its work.
    /// This component deliberately depends on OceanSurfaceQueryResources instead of FFTCompute.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Water System/Ocean Surface Queries")]
    public sealed class OceanSurfaceQuerySystem : MonoBehaviour
    {
        OceanRenderer owner;
        readonly HashSet<BouyantBody> activeBodies = new();

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

            public BodyQueryRange(BouyantBody body, int startIndex, int count)
            {
                Body = body;
                StartIndex = startIndex;
                Count = count;
            }
        }
        readonly Dictionary<BouyantBody, BodyQueryRange> bodyQueryRanges = new();
        
        ComputeBuffer deviceQueryPoints;
        ComputeBuffer deviceQueryResults;
        int deviceBufferCapacity;
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
        }

        public void Register(BouyantBody body)
        {
            if (body != null) activeBodies.Add(body);
        }

        public void Unregister(BouyantBody body)
        {
            if (ReferenceEquals(body, null)) return;
            activeBodies.Remove(body);
            bodyQueryRanges.Remove(body);
        }

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
            if (!IsInitialized || owner == null || !resources.IsValid) return;

            LastRecordedFrame = frame.FrameIndex;

            // Registration is intentionally independent of point counts: a body may regenerate its
            // samples after registration. Recalculate the exact batch size from the current state.
            activeBodies.RemoveWhere(body => body == null);
            bodyQueryRanges.Clear();
            int requiredPointCount = 0;
            foreach (var body in activeBodies)
            {
                if (!body.isActiveAndEnabled) continue;
                requiredPointCount += body.SamplePointCount;
            }

            if (queryPoints.Length < requiredPointCount)
                Array.Resize(ref queryPoints, Mathf.NextPowerOfTwo(Mathf.Max(1, requiredPointCount)));

            int offset = 0;
            // TODO: Collect pending positions, bind resources, dispatch the query kernel and enqueue
            // AsyncGPUReadback. Do not execute or wait for the command buffer from this component.
            foreach (BouyantBody body in activeBodies)
            {
                if (body == null || body.SamplePointCount == 0 || !body.isActiveAndEnabled) continue;
                int startIndex = offset;
                for (int i = 0; i < body.SamplePointCount; i++)
                    queryPoints[offset + i] = body.GetWorldSamplePoint(i);
                offset += body.SamplePointCount;
                bodyQueryRanges.Add(body, new BodyQueryRange(body, startIndex, body.SamplePointCount));
            }
            QueryPointCount = offset;
            if (QueryPointCount == 0) return;

            EnsureDeviceBuffers(QueryPointCount);
            deviceQueryPoints.SetData(queryPoints, 0, 0, QueryPointCount);
            commands.SetComputeIntParam(queryShader, QueryShaderIDs.QueryPointCount, QueryPointCount);
            commands.SetComputeIntParam(queryShader, QueryShaderIDs.FFTCascadeCount, resources.CascadeCount);
            commands.SetComputeIntParam(queryShader, QueryShaderIDs.FFTResolution, resources.Resolution);
            commands.SetComputeVectorParam(queryShader, QueryShaderIDs.OceanDomainSizes, resources.DomainSizes);
            commands.SetComputeFloatParam(queryShader, QueryShaderIDs.OceanSeaLevel, resources.SeaLevel);
            commands.SetComputeTextureParam(queryShader, queryKernelIndex, QueryShaderIDs.OceanDisplacement, resources.Displacement);
            commands.SetComputeTextureParam(queryShader, queryKernelIndex, QueryShaderIDs.OceanNormals, resources.Normal);
            commands.SetComputeBufferParam(queryShader, queryKernelIndex, QueryShaderIDs.QueryPoints, deviceQueryPoints);
            commands.SetComputeBufferParam(queryShader, queryKernelIndex, QueryShaderIDs.QueryResults, deviceQueryResults);
            commands.DispatchCompute(queryShader, queryKernelIndex,
                (QueryPointCount + (int)queryThreadGroupSizeX - 1) / (int)queryThreadGroupSizeX, 1, 1);
        }

        void EnsureDeviceBuffers(int requiredCount)
        {
            if (deviceQueryPoints != null && deviceQueryResults != null && deviceBufferCapacity >= requiredCount) return;

            ReleaseDeviceBuffers();
            deviceBufferCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, requiredCount));
            deviceQueryPoints = new ComputeBuffer(deviceBufferCapacity, sizeof(float) * 3, ComputeBufferType.Structured);
            deviceQueryResults = new ComputeBuffer(deviceBufferCapacity, sizeof(float) * 8, ComputeBufferType.Structured);
        }

        void ReleaseDeviceBuffers()
        {
            deviceQueryPoints?.Release();
            deviceQueryResults?.Release();
            deviceQueryPoints = null;
            deviceQueryResults = null;
            deviceBufferCapacity = 0;
        }

        internal void Release()
        {
            // TODO: Release buffers and invalidate outstanding async readback generations here.
            owner = null;
            IsInitialized = false;
            LastRecordedFrame = -1;
            QueryPointCount = 0;
            bodyQueryRanges.Clear();
            ReleaseDeviceBuffers();
            queryShader = null;
            queryKernelIndex = -1;
            queryThreadGroupSizeX = 0;
        }
    }
}
