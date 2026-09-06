using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace WaterSystem.Ocean
{
    /// <summary>Owns GPU resources only. All array textures have one slice per active spectrum cascade.</summary>
    public sealed class OceanTextures : IDisposable
    {
        readonly List<RenderTexture> ownedTextures = new List<RenderTexture>(10);
        public int Resolution { get; private set; }
        public int CascadeCount { get; private set; }
        public bool IsAllocated => SpectrumInitial != null && SpectrumInitial.IsCreated();
        public RenderTexture SpectrumInitial { get; private set; }
        // Complex spectra become unnormalized spatial complex fields after IFFT, and are overwritten next frame.
        public RenderTexture SpectrumX { get; private set; }
        public RenderTexture SpectrumY { get; private set; }
        public RenderTexture SpectrumZ { get; private set; }
        public RenderTexture Ping { get; private set; }
        public RenderTexture Pong { get; private set; }
        public RenderTexture FFTDisplacement { get; private set; }
        public RenderTexture FFTNormal { get; private set; }
        public Texture2D ButterflyLookup { get; private set; }
        public RenderTexture FoamPrevious { get; private set; }
        public RenderTexture FoamCurrent { get; private set; }
        public void SwapFoam() { var previous = FoamPrevious; FoamPrevious = FoamCurrent; FoamCurrent = previous; }

        [Obsolete("Use FFTDisplacement (correct spelling).")]
        public RenderTexture FFTDisplayment => FFTDisplacement;
        // Row and column transforms use the same butterfly coefficients; only the dispatch axis changes.
        public Texture2D FFTRow => ButterflyLookup;
        public Texture2D FFTColumn => ButterflyLookup;

        public void Allocate(OceanSpectrumParameters parameters)
        {
            if (IsAllocated && Resolution == parameters.Resolution && CascadeCount == parameters.CascadeCount) return;
            Dispose();
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supports2DArrayTextures)
                throw new NotSupportedException("Ocean FFT requires compute shaders and 2D array textures.");
            Resolution = parameters.Resolution;
            CascadeCount = parameters.CascadeCount;
            try
            {
                SpectrumInitial = Create("H0 (h0(k), conjugate h0(-k))", GraphicsFormat.R32G32B32A32_SFloat);
                SpectrumX = Create("Spectrum / Spatial X", GraphicsFormat.R32G32_SFloat);
                SpectrumY = Create("Spectrum / Spatial Y", GraphicsFormat.R32G32_SFloat);
                SpectrumZ = Create("Spectrum / Spatial Z", GraphicsFormat.R32G32_SFloat);
                Ping = Create("FFT Ping", GraphicsFormat.R32G32_SFloat);
                Pong = Create("FFT Pong", GraphicsFormat.R32G32_SFloat);
                FFTDisplacement = Create("Displacement XYZ / Reserved", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear);
                FFTNormal = Create("Normal XYZ / Jacobian", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear);
                FoamPrevious = Create("Foam history", GraphicsFormat.R16_SFloat, FilterMode.Bilinear);
                FoamCurrent = Create("Foam current", GraphicsFormat.R16_SFloat, FilterMode.Bilinear);
                var data = OceanButterfly.BuildLookup(Resolution, out int stages);
                ButterflyLookup = new Texture2D(Resolution, stages, TextureFormat.RGBAFloat, false, true)
                {
                    name = "Ocean / IFFT Butterfly Lookup", filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
                };
                ButterflyLookup.SetPixels(data);
                ButterflyLookup.Apply(false, true);
            }
            catch { Dispose(); throw; }
        }

        RenderTexture Create(string label, GraphicsFormat format, FilterMode filter = FilterMode.Point)
        {
            if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.LoadStore) ||
                !SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample))
                throw new NotSupportedException("Ocean FFT requires UAV/sample support for " + format);
            var descriptor = new RenderTextureDescriptor(Resolution, Resolution)
            {
                graphicsFormat = format, depthBufferBits = 0, dimension = TextureDimension.Tex2DArray,
                volumeDepth = CascadeCount, msaaSamples = 1, enableRandomWrite = true,
                useMipMap = false, autoGenerateMips = false, sRGB = false
            };
            var texture = new RenderTexture(descriptor)
            {
                name = "Ocean / " + label, filterMode = filter, wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave, anisoLevel = 1
            };
            ownedTextures.Add(texture); // Own it before Create, so partial initialization also cleans up.
            if (!texture.Create()) throw new InvalidOperationException("Could not allocate " + texture.name);
            return texture;
        }

        public void Dispose()
        {
            foreach (var texture in ownedTextures)
            {
                if (texture == null) continue;
                texture.Release();
                DestroyOwned(texture);
            }
            ownedTextures.Clear();
            DestroyOwned(ButterflyLookup);
            SpectrumInitial = SpectrumX = SpectrumY = SpectrumZ = Ping = Pong = FFTDisplacement = FFTNormal = null;
            ButterflyLookup = null;
            FoamPrevious = FoamCurrent = null;
            Resolution = CascadeCount = 0;
        }
        static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
