using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WaterSystem.Ocean
{
    /// <summary>World-space foam history shared by all cameras rendering this ocean.</summary>
    [DisallowMultipleComponent]
    public sealed class OceanWakeSystem : MonoBehaviour
    {
        public const int Resolution = 512;
        public const float WorldSize = 128f;
        static readonly int PreviousId = Shader.PropertyToID("_PreviousWake");
        static readonly int ResultId = Shader.PropertyToID("_WakeResult");
        static readonly int SplatsId = Shader.PropertyToID("_WakeSplats");
        static readonly int SplatCountId = Shader.PropertyToID("_WakeSplatCount");
        static readonly int DeltaTimeId = Shader.PropertyToID("_WakeDeltaTime");
        static readonly int DecayId = Shader.PropertyToID("_WakeDecay");
        static readonly int SizeId = Shader.PropertyToID("_WakeSize");
        static readonly int PreviousOriginId = Shader.PropertyToID("_PreviousWakeOrigin");
        static readonly int OriginId = Shader.PropertyToID("_WakeOrigin");
        static readonly int FoamTextureId = Shader.PropertyToID("_OceanWakeFoam");
        static readonly int OriginSizeId = Shader.PropertyToID("_OceanWakeOriginSize");
        readonly List<WakeSplat> splats = new List<WakeSplat>(64);
        RenderTexture[] history;
        ComputeBuffer splatBuffer;
        ComputeShader shader;
        int kernel = -1, readIndex;
        Vector2 origin;
        bool hasOrigin, hasDispatch;

        public float DecayPerSecond = 0.22f;

        void OnDisable() => Release();

        public void RecordUpdate(CommandBuffer commands, float deltaTime)
        {
            if (!Application.isPlaying || !SystemInfo.supportsComputeShaders) return;
            EnsureResources();
            splats.Clear();
            Vector2 center = Vector2.zero;
            int ships = 0;
            foreach (var emitter in ShipWakeEmitter.Active)
            {
                if (emitter == null || !emitter.isActiveAndEnabled) continue;
                emitter.AppendWakeSplats(splats);
                Vector3 p = emitter.transform.position;
                center += new Vector2(p.x, p.z);
                ships++;
            }
            if (ships == 0)
            {
                Vector3 p = transform.position;
                center = new Vector2(p.x, p.z);
            }
            else center /= ships;

            Vector2 nextOrigin = ships == 0 && hasOrigin
                ? origin
                : center - Vector2.one * (WorldSize * 0.5f);
            if (!hasOrigin) { origin = nextOrigin; hasOrigin = true; }
            int count = Mathf.Min(splats.Count, 128);
            EnsureSplatBuffer(Mathf.Max(1, count));
            if (count > 0)
                splatBuffer.SetData(splats, 0, 0, count);

            int writeIndex = 1 - readIndex;
            commands.SetComputeTextureParam(shader, kernel, PreviousId, history[readIndex]);
            commands.SetComputeTextureParam(shader, kernel, ResultId, history[writeIndex]);
            commands.SetComputeBufferParam(shader, kernel, SplatsId, splatBuffer);
            commands.SetComputeIntParam(shader, SplatCountId, count);
            commands.SetComputeFloatParam(shader, DeltaTimeId, Mathf.Clamp(deltaTime, 0, 0.1f));
            commands.SetComputeFloatParam(shader, DecayId, Mathf.Max(0, DecayPerSecond));
            commands.SetComputeFloatParam(shader, SizeId, WorldSize);
            commands.SetComputeVectorParam(shader, PreviousOriginId, new Vector4(origin.x, origin.y, 0, 0));
            commands.SetComputeVectorParam(shader, OriginId, new Vector4(nextOrigin.x, nextOrigin.y, 0, 0));
            commands.DispatchCompute(shader, kernel, Resolution / 8, Resolution / 8, 1);
            readIndex = writeIndex;
            origin = nextOrigin;
            hasDispatch = true;
        }

        public void Bind(MaterialPropertyBlock properties)
        {
            if (hasDispatch && history != null)
            {
                properties.SetTexture(FoamTextureId, history[readIndex]);
                properties.SetVector(OriginSizeId, new Vector4(origin.x, origin.y, WorldSize, 1f / WorldSize));
            }
            else
            {
                properties.SetTexture(FoamTextureId, Texture2D.blackTexture);
                properties.SetVector(OriginSizeId, Vector4.zero);
            }
        }

        void EnsureResources()
        {
            if (history != null) return;
            shader = OceanResources.Load().WakeDeposit;
            if (shader == null || !shader.HasKernel("UpdateWake"))
                throw new InvalidOperationException("OceanDefaults is missing WakeDeposit.compute / UpdateWake.");
            kernel = shader.FindKernel("UpdateWake");
            history = new RenderTexture[2];
            for (int i = 0; i < history.Length; i++)
            {
                history[i] = new RenderTexture(Resolution, Resolution, 0, RenderTextureFormat.RHalf,
                    RenderTextureReadWrite.Linear)
                {
                    name = $"{name} Wake Foam {i}", enableRandomWrite = true,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false, autoGenerateMips = false
                };
                history[i].Create();
                var previous = RenderTexture.active;
                RenderTexture.active = history[i];
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = previous;
            }
        }

        void EnsureSplatBuffer(int count)
        {
            if (splatBuffer != null && splatBuffer.count >= count) return;
            splatBuffer?.Release();
            splatBuffer = new ComputeBuffer(Mathf.NextPowerOfTwo(count), 16, ComputeBufferType.Structured);
        }

        void Release()
        {
            if (history != null)
            {
                foreach (var texture in history)
                {
                    if (texture == null) continue;
                    texture.Release();
                    if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture);
                }
            }
            history = null;
            splatBuffer?.Release();
            splatBuffer = null;
            shader = null;
            kernel = -1;
            readIndex = 0;
            hasOrigin = hasDispatch = false;
        }
    }

    public struct WakeSplat
    {
        public Vector2 Center;
        public float Radius;
        public float Strength;
        public WakeSplat(Vector3 position, float radius, float strength)
        {
            Center = new Vector2(position.x, position.z);
            Radius = radius;
            Strength = strength;
        }
    }
}
