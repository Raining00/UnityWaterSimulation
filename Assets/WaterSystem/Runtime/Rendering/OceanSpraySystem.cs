using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace WaterSystem.Ocean
{
    /// <summary>Camera-local GPU whitecap spray. Owned and disposed by OceanRenderer.CameraState.</summary>
    internal sealed class OceanSpraySystem : IDisposable
    {
        // Four float4s, shared with OceanSprayData.hlsl. No per-frame CPU particle arrays.
        const int Stride = 64;
        GraphicsBuffer particles, visibleIndices, drawArguments, tileCounts;
        RenderTexture previousDisplacement;
        Material material;
        ComputeShader shader;
        int clearKernel, stepKernel, cullKernel, clearTilesKernel, capacity, tick, tileCount, spectrumSeed;
        Vector4 previousDomains;
        readonly Plane[] frustumPlanes = new Plane[6];
        readonly Vector4[] planeVectors = new Vector4[6];
        double previousTime = double.NaN;
        Vector3 previousCenter;
        bool failed;
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();

        public void Render(OceanRenderer ocean, FFTCompute fft, Camera camera, Vector3 rootCenter)
        {
            var settings = ocean.Spray;
            if (settings == null || !settings.Enabled || fft == null || !fft.HasOutput ||
                !SystemInfo.supportsComputeShaders || !SystemInfo.supportsInstancing)
            {
                Dispose();
                return;
            }
            if (failed) return;
            try
            {
                settings.Validate();
                var resources = OceanResources.Load();
                if (particles == null || capacity != settings.Capacity)
                {
                    Dispose();
                    if (resources.SprayCompute == null || resources.SprayShader == null || !resources.SprayShader.isSupported)
                        throw new InvalidOperationException("OceanDefaults is missing supported ocean spray shaders.");
                    shader = resources.SprayCompute;
                    clearKernel = shader.FindKernel("ClearSpray");
                    stepKernel = shader.FindKernel("UpdateSpray");
                    cullKernel = shader.FindKernel("CullSpray");
                    clearTilesKernel = shader.FindKernel("ClearTiles");
                    capacity = settings.Capacity;
                    particles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, Stride);
                    visibleIndices = new GraphicsBuffer(GraphicsBuffer.Target.Append, capacity, sizeof(uint));
                    drawArguments = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawArgs.size);
                    drawArguments.SetData(new[] { new GraphicsBuffer.IndirectDrawArgs { vertexCountPerInstance = 6 } });
                    material = new Material(resources.SprayShader) { name = "Ocean spray (runtime)", hideFlags = HideFlags.HideAndDontSave };
                    Clear();
                }
                bool resetHistory = EnsureHistory(fft);
                Vector3 center = new Vector3(camera.transform.position.x, rootCenter.y, camera.transform.position.z);
                double time = fft.SimulationTime;
                double elapsed = double.IsNaN(previousTime) ? 0 : time - previousTime;
                // Teleports, time scrubbing and large stalls restart rather than launch a burst of stale spray.
                if (resetHistory || elapsed < 0 || elapsed > 0.25 || (center - previousCenter).sqrMagnitude > settings.Radius * settings.Radius)
                {
                    Clear();
                    elapsed = 0;
                    Graphics.CopyTexture(fft.Textures.FFTDisplacement, previousDisplacement);
                }
                previousTime = time;
                previousCenter = center;
                if (elapsed > 0)
                {
                    BindSimulation(ocean, fft, rootCenter, center);
                    // Exact integration for constant drag/acceleration; one step and collision query per frame.
                    shader.SetFloat("_SprayDeltaTime", (float)elapsed);
                    shader.SetInt("_SprayTick", ++tick);
                    shader.Dispatch(stepKernel, (capacity + 63) / 64, 1, 1);
                    Graphics.CopyTexture(fft.Textures.FFTDisplacement, previousDisplacement);
                }
                Cull(camera, settings);
                properties.Clear();
                properties.SetBuffer("_OceanSprayParticles", particles);
                properties.SetBuffer("_SprayVisibleIndices", visibleIndices);
                properties.SetTexture("_SplashAtlas", resources.SplashAtlas != null ? resources.SplashAtlas : Texture2D.blackTexture);
                var atlas = resources.SplashAtlas != null ? resources.SplashAtlas : Texture2D.blackTexture;
                properties.SetVector("_SplashAtlas_TexelSize", new Vector4(1f/atlas.width, 1f/atlas.height, atlas.width, atlas.height));
                properties.SetVector("_SprayDetail", new Vector4(settings.SplashSizeMultiplier, settings.HighlightStrength, settings.Backlighting, settings.ReceiveSunShadows ? 1 : 0));
                properties.SetVector("_SprayCenterRadius", new Vector4(center.x, center.y, center.z, settings.Radius));
                properties.SetColor("_SprayColor", ocean.ActiveFoamColor * settings.Tint);
                properties.SetVector("_SprayAppearance", new Vector4(settings.Opacity, settings.MistSizeMultiplier,
                    settings.SoftIntersectionDistance, ocean.UsesSceneTextures ? 1 : 0));
                // Conservative bounds include wind drift, full ballistic flight and initial wave displacement.
                float life = settings.Lifetime.y;
                float maxSize = settings.Size.y * Mathf.Max(settings.MistSizeMultiplier, settings.SplashSizeMultiplier) * 3;
                float reach = settings.Radius * 1.3f + fft.MaximumDisplacement.x + maxSize;
                float height = fft.MaximumDisplacement.y + (settings.UpwardSpeed.y*1.35f+settings.MaxInheritedSpeed)*life + maxSize + 2;
                var parameters = new RenderParams(material)
                {
                    camera = camera, layer = ocean.gameObject.layer, matProps = properties,
                    worldBounds = new Bounds(center, new Vector3(reach * 2, height * 2, reach * 2)),
                    shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                    lightProbeUsage = LightProbeUsage.Off
                };
                Graphics.RenderPrimitivesIndirect(parameters, MeshTopology.Triangles, drawArguments);
            }
            catch (Exception exception)
            {
                Dispose();
                failed = true;
                Debug.LogWarning("Ocean spray disabled for this camera: " + exception.Message, ocean);
            }
        }

        bool EnsureHistory(FFTCompute fft)
        {
            var current = fft.Textures.FFTDisplacement;
            // Wind can change continuously. Do not clear live spray every time H0 is regenerated;
            // the velocity clamp handles amplitude changes. Reset only when sample coordinates/seeds change.
            bool changed = spectrumSeed != fft.Parameters.Seed || previousDomains != fft.Parameters.DomainSizes;
            if (previousDisplacement == null || previousDisplacement.width != current.width || previousDisplacement.volumeDepth != current.volumeDepth)
            {
                DestroyHistory();
                var descriptor = current.descriptor;
                descriptor.enableRandomWrite = false;
                previousDisplacement = new RenderTexture(descriptor) { name = "Ocean spray / previous wave displacement", hideFlags = HideFlags.HideAndDontSave };
                if (!previousDisplacement.Create()) throw new InvalidOperationException("Cannot allocate ocean spray velocity history.");
                changed = true;
            }
            spectrumSeed = fft.Parameters.Seed;
            previousDomains = fft.Parameters.DomainSizes;
            return changed;
        }

        void Cull(Camera camera, OceanSpraySettings settings)
        {
            int width = Mathf.Max(1, (camera.pixelWidth + 63) / 64);
            int height = Mathf.Max(1, (camera.pixelHeight + 63) / 64);
            int count = width * height;
            if (tileCounts == null || tileCount != count)
            {
                tileCounts?.Dispose();
                tileCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, sizeof(uint));
                tileCount = count;
            }
            visibleIndices.SetCounterValue(0);
            shader.SetInt("_SprayTileCount", tileCount);
            shader.SetBuffer(clearTilesKernel, "_SprayTiles", tileCounts);
            shader.Dispatch(clearTilesKernel, (tileCount + 63) / 64, 1, 1);
            GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
            for (int i = 0; i < 6; i++)
            {
                var plane = frustumPlanes[i];
                planeVectors[i] = new Vector4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance);
            }
            shader.SetVectorArray("_SprayFrustum", planeVectors);
            shader.SetMatrix("_SprayViewProjection", GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix);
            shader.SetVector("_SprayTileLayout", new Vector4(width, height, settings.MaxParticlesPerTile, 0));
            shader.SetVector("_SpraySizeMultipliers", new Vector4(settings.MistSizeMultiplier, settings.SplashSizeMultiplier, 0, 0));
            shader.SetInt("_SprayCapacity", capacity);
            shader.SetBuffer(cullKernel, "_OceanSprayParticles", particles);
            shader.SetBuffer(cullKernel, "_SprayVisibleIndices", visibleIndices);
            shader.SetBuffer(cullKernel, "_SprayTiles", tileCounts);
            shader.Dispatch(cullKernel, (capacity + 63) / 64, 1, 1);
            // instanceCount is the second uint of Unity's non-indexed indirect argument structure.
            GraphicsBuffer.CopyCount(visibleIndices, drawArguments, sizeof(uint));
        }

        void Clear()
        {
            shader.SetInt("_SprayCapacity", capacity);
            shader.SetBuffer(clearKernel, "_OceanSprayParticles", particles);
            shader.Dispatch(clearKernel, (capacity + 63) / 64, 1, 1);
            previousTime = double.NaN;
            tick = 0;
        }

        void BindSimulation(OceanRenderer ocean, FFTCompute fft, Vector3 root, Vector3 center)
        {
            var s = ocean.Spray;
            var p = fft.Parameters;
            shader.SetInt("_SprayCapacity", capacity);
            shader.SetInt("_SpraySeed", p.Seed);
            shader.SetInt("_FFTCascadeCount", p.CascadeCount);
            shader.SetInt("_FFTResolution", p.Resolution);
            shader.SetVector("_OceanDomainSizes", p.DomainSizes);
            shader.SetVector("_OceanOrigin", new Vector4(root.x, root.y, root.z, ocean.RootSize));
            shader.SetFloat("_OceanInfinite", ocean.InfiniteHorizon ? 1 : 0);
            shader.SetVector("_SprayCenterRadius", new Vector4(center.x, center.y, center.z, s.Radius));
            shader.SetVector("_SprayEmission", new Vector4(s.EmissionRate / capacity, s.FoamThreshold, s.BreakingThreshold, s.MistFraction));
            shader.SetVector("_SprayLifetimeSize", new Vector4(s.Lifetime.x, s.Lifetime.y, s.Size.x, s.Size.y));
            shader.SetVector("_SprayMotion", new Vector4(s.UpwardSpeed.x, s.UpwardSpeed.y, p.Gravity * s.GravityScale, s.Drag));
            shader.SetVector("_SprayWind", new Vector4(p.WindDirection.x, p.WindDirection.y, p.WindSpeed * s.WindInfluence, 0));
            shader.SetVector("_SprayDynamics", new Vector4(s.WaveVelocityInheritance, s.MaxInheritedSpeed, s.SplashFraction, s.Turbulence));
            shader.SetFloat("_SprayBurstSpread", s.BurstSpread);
            shader.SetVector("_SprayFoam", new Vector4(fft.Settings.foamThreshold, fft.Settings.foamSoftness,
                Mathf.Max(0, ocean.ActiveFoamStrength), Mathf.Max(0.001f, ocean.ActiveFoamScale)));
            shader.SetFloat("_OceanSimulationTime", (float)(fft.Settings.loopPeriod > 0 ? fft.SimulationTime % fft.Settings.loopPeriod : fft.SimulationTime));
            shader.SetBuffer(stepKernel, "_OceanSprayParticles", particles);
            shader.SetTexture(stepKernel, "_OceanDisplacement", fft.Textures.FFTDisplacement);
            shader.SetTexture(stepKernel, "_SprayPreviousDisplacement", previousDisplacement);
            shader.SetTexture(stepKernel, "_OceanNormals", fft.Textures.FFTNormal);
            shader.SetTexture(stepKernel, "_OceanFoam", fft.Textures.FoamPrevious);
            shader.SetTexture(stepKernel, "_FoamTex", ocean.ActiveFoamTexture);
        }

        public void Dispose()
        {
            particles?.Dispose();
            particles = null;
            visibleIndices?.Dispose(); visibleIndices = null;
            drawArguments?.Dispose(); drawArguments = null;
            tileCounts?.Dispose(); tileCounts = null;
            tileCount = 0;
            DestroyHistory();
            if (material != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(material);
                else UnityEngine.Object.DestroyImmediate(material);
            }
            material = null;
            shader = null;
            capacity = tick = 0;
            previousTime = double.NaN;
            failed = false;
        }
        void DestroyHistory()
        {
            if (previousDisplacement == null) return;
            previousDisplacement.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(previousDisplacement);
            else UnityEngine.Object.DestroyImmediate(previousDisplacement);
            previousDisplacement = null;
        }
    }
}
