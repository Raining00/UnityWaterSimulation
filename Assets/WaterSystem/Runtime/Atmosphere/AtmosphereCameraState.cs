using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace WaterSystem.Ocean
{
    /// <summary>Per-camera sky-view and aerial perspective tables plus the temporally filtered cloud buffers.</summary>
    internal sealed class AtmosphereCameraState : IDisposable
    {
        public RenderTexture SkyView, Aerial, CloudRaw, CloudRawDepth;
        public readonly RenderTexture[] History = new RenderTexture[2];
        public readonly RenderTexture[] HistoryDepth = new RenderTexture[2];
        public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        public Matrix4x4 ViewProjection = Matrix4x4.identity, InverseViewProjection = Matrix4x4.identity, PreviousViewProjection = Matrix4x4.identity;
        public int HistoryIndex, LastSeenFrame, Width, Height, JitterIndex;
        public bool HistoryValid;
        public RenderTexture CurrentHistory => History[HistoryIndex];

        public AtmosphereCameraState(int skyViewWidth, int skyViewHeight, int aerialSize)
        {
            SkyView = Create("Atmosphere / Sky-view LUT", skyViewWidth, skyViewHeight, 1, GraphicsFormat.R16G16B16A16_SFloat, TextureDimension.Tex2D, FilterMode.Bilinear);
            Aerial = Create("Atmosphere / Aerial perspective LUT", aerialSize, aerialSize, aerialSize, GraphicsFormat.R16G16B16A16_SFloat, TextureDimension.Tex3D, FilterMode.Bilinear);
        }
        public void EnsureCloudBuffers(int width, int height)
        {
            if (CloudRaw != null && Width == width && Height == height) return;
            ReleaseClouds();
            Width = width; Height = height;
            CloudRaw = Create("Clouds / March", width, height, 1, GraphicsFormat.R16G16B16A16_SFloat, TextureDimension.Tex2D, FilterMode.Bilinear);
            CloudRawDepth = Create("Clouds / March distance", width, height, 1, GraphicsFormat.R32_SFloat, TextureDimension.Tex2D, FilterMode.Bilinear);
            for (int i = 0; i < 2; i++)
            {
                History[i] = Create("Clouds / History " + i, width, height, 1, GraphicsFormat.R16G16B16A16_SFloat, TextureDimension.Tex2D, FilterMode.Bilinear);
                HistoryDepth[i] = Create("Clouds / History distance " + i, width, height, 1, GraphicsFormat.R32_SFloat, TextureDimension.Tex2D, FilterMode.Bilinear);
            }
            HistoryIndex = 0;
            HistoryValid = false;
        }
        internal static RenderTexture Create(string name, int width, int height, int depth, GraphicsFormat format, TextureDimension dimension, FilterMode filter, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.LoadStore) || !SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample))
                throw new NotSupportedException("Atmosphere rendering requires UAV and sampling support for " + format);
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = format, depthBufferBits = 0, dimension = dimension, volumeDepth = dimension == TextureDimension.Tex2D ? 1 : depth,
                msaaSamples = 1, enableRandomWrite = true, useMipMap = false, autoGenerateMips = false, sRGB = false
            };
            var texture = new RenderTexture(descriptor) { name = name, filterMode = filter, wrapMode = wrap, hideFlags = HideFlags.HideAndDontSave };
            if (!texture.Create()) throw new InvalidOperationException("Cannot allocate " + name);
            return texture;
        }
        internal static void Destroy(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture); else UnityEngine.Object.DestroyImmediate(texture);
            texture = null;
        }
        void ReleaseClouds()
        {
            Destroy(ref CloudRaw);
            Destroy(ref CloudRawDepth);
            for (int i = 0; i < 2; i++) { Destroy(ref History[i]); Destroy(ref HistoryDepth[i]); }
            Width = Height = 0;
            HistoryValid = false;
        }
        public void Dispose()
        {
            ReleaseClouds();
            Destroy(ref SkyView);
            Destroy(ref Aerial);
        }
    }
}
