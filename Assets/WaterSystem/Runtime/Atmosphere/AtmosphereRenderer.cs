using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaterSystem.Ocean
{
    /// <summary>
    /// Physically based sky, aerial perspective and volumetric clouds for URP, driven like the ocean:
    /// compute work is recorded per camera in beginCameraRendering and the full-screen composites are
    /// queue-ordered Graphics.RenderMesh draws, so no renderer feature or asset setup is required.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Water System/Atmosphere")]
    public sealed class AtmosphereRenderer : MonoBehaviour
    {
        [Tooltip("Directional light used as the sun. Empty: RenderSettings.sun, then the first directional light.")]
        public Light Sun;
        [Tooltip("Optional ocean. Its surface height is sea level and its wind can drive the clouds.")]
        public OceanRenderer Ocean;
        [Tooltip("Sea level (planet surface) in world metres when no ocean is linked.")]
        public float SeaLevel;
        public AtmosphereSettings Atmosphere = new AtmosphereSettings();
        public CloudSettings Clouds = new CloudSettings();
        public NightSkySettings NightSky = new NightSkySettings();
        public MoonSettings Moon = new MoonSettings();
        public AuroraSettings Aurora = new AuroraSettings();
        public MeteorSettings Meteors = new MeteorSettings();
        readonly MeteorSkyState meteorState = new MeteorSkyState();
        bool moonHistoryReset;
        public AtmosphereLightingSettings Lighting = new AtmosphereLightingSettings();
        [Tooltip("Optional Game camera filter. Scene view cameras use their own buffers.")]
        public Camera TargetCamera;
        public bool ShowInSceneView = true;

        public string Status { get; private set; } = "Inactive";
        public Vector3 SunDirection { get; private set; } = Vector3.up;
        public Vector3 SunTransmittance { get; private set; } = Vector3.one;
        public Vector2Int LastCloudBufferSize { get; private set; }
        public string LastCameraName { get; private set; }
        public bool AmbientReadbackValid => ambientValid;
        public Texture ReflectionCubemap => skyCubemap;
        public Texture CloudShadowTexture => cloudShadowMap;
        public Texture WeatherTexture => weatherMap;
        public Texture CloudPanoramaTexture => cloudPanorama;
        public Texture TransmittanceTexture => transmittanceLUT;
        public Light ActiveSun => activeSun;
        public float SeaLevelY => Ocean != null ? Ocean.transform.position.y : SeaLevel;
        public int ActiveMeteorTrackCount => meteorState.Count;

        const int SkyViewWidth = 192, SkyViewHeight = 108, AerialSize = 32, WeatherSize = 512;
        const int PanoramaWidth = 512, PanoramaHeight = 256, ShapeNoiseSize = 128, DetailNoiseSize = 32;

        readonly struct Kernel
        {
            public readonly int Index;
            public readonly uint X, Y, Z;
            public Kernel(ComputeShader shader, string name)
            {
                if (!shader.HasKernel(name)) throw new InvalidOperationException(shader.name + " is missing kernel " + name);
                Index = shader.FindKernel(name);
                shader.GetKernelThreadGroupSizes(Index, out X, out Y, out Z);
            }
            public void Dispatch(CommandBuffer commands, ComputeShader shader, int threadsX, int threadsY, int threadsZ)
            {
                commands.DispatchCompute(shader, Index, Groups(threadsX, X), Groups(threadsY, Y), Groups(threadsZ, Z));
            }
            static int Groups(int threads, uint size) => Mathf.Max(1, (threads + (int)size - 1) / (int)size);
        }

        ComputeShader lutShader, noiseShader, cloudShader;
        Kernel transmittanceKernel, multiScatterKernel, skyViewKernel, aerialKernel, cubemapKernel, ambientKernel;
        Kernel shapeKernel, detailKernel, weatherKernel, marchKernel, resolveKernel, panoramaKernel, shadowKernel;
        RenderTexture transmittanceLUT, multiScatterLUT, weatherMap, cloudShadowMap, cloudPanorama, skyCubemap, skyCubemapFaces, shapeNoise, detailNoise;
        GraphicsBuffer ambientBuffer;
        AsyncGPUReadbackRequest readback;
        bool readbackPending, ambientValid;
        readonly Vector4[] ambientData = new Vector4[4];
        Vector3 smoothedSky, smoothedEquator, smoothedGround;
        Material skyboxMaterial, aerialMaterial, compositeMaterial;
        Shader skyboxShader;
        Mesh triangle;
        CommandBuffer commands;
        readonly Dictionary<Camera, AtmosphereCameraState> cameras = new Dictionary<Camera, AtmosphereCameraState>();
        readonly List<Camera> expiredCameras = new List<Camera>();
        Light activeSun;
        int sunSearchFrame = -1000;
        int lutHash, frameIndex = -1;
        bool noiseReady, cloudsActive, failed, unsupportedReported, forceNewFrame;
        double previousTime;
        float weatherTime, cameraRadiusKm, lastFrameDelta;
        Vector2 windOffset, shadowOffset;
        float shadowExtent;
        // Scene state we drive, restored on disable.
        bool savedRenderSettings, skyboxAssigned, cookieApplied;
        AmbientMode savedAmbientMode;
        Color savedAmbientSky, savedAmbientEquator, savedAmbientGround;
        DefaultReflectionMode savedReflectionMode;
        Texture savedReflectionTexture;
        bool savedFog;
        Material savedSkybox;
        Light cookieLight;
        Texture savedCookie;
        Vector2 savedCookieSize, savedCookieOffset;

        void OnEnable()
        {
            meteorState.Reset();
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
            moonHistoryReset = true;
            frameIndex = -1;
            failed = unsupportedReported = false;
            lutHash = 0;
            noiseReady = false;
            previousTime = 0;
            readbackPending = false;
        }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            RestoreSceneState();
            ReleaseResources();
            // Ocean shaders fall back to Unity fog and probe reflections.
            Shader.SetGlobalVector(AtmosphereShaderIDs.World, new Vector4(SeaLevelY, 0, 0, 0));
            Shader.SetGlobalVector(AtmosphereShaderIDs.CloudShadowParams, Vector4.zero);
            Shader.SetGlobalVector(AtmosphereShaderIDs.NightSkyParams, Vector4.zero);
            Shader.SetGlobalVector(AtmosphereShaderIDs.MoonParams, Vector4.zero);
            Shader.SetGlobalVector(AtmosphereShaderIDs.MoonLight, Vector4.zero);
            Shader.SetGlobalFloat(AtmosphereShaderIDs.MoonCameraVisible, 0);
            Shader.SetGlobalVector(AtmosphereShaderIDs.AuroraParams, Vector4.zero);
            Shader.SetGlobalFloat(AtmosphereShaderIDs.AuroraCameraVisible, 0);
            Shader.SetGlobalVector("_MeteorParams", Vector4.zero);
            Shader.SetGlobalFloat("_MeteorCameraVisible", 0);
            meteorState.Reset();
            Shader.SetGlobalVector(AtmosphereShaderIDs.SkyReflectionParams, Vector4.zero);
            Status = "Inactive";
        }
        void Reset()
        {
            if (Ocean == null) Ocean = FindFirstObjectByType<OceanRenderer>();
            if (Sun == null) Sun = RenderSettings.sun;
            if (Ocean != null) SeaLevel = Ocean.transform.position.y;
        }
        void OnValidate()
        {
            Atmosphere?.Validate();
            Clouds?.Validate();
            NightSky?.Validate();
            Moon?.Validate();
            Aurora?.Validate();
            Meteors?.Validate();
            forceNewFrame = true;
            moonHistoryReset = true;
        }
        [ContextMenu("Regenerate Cloud Noise")]
        public void RequestNoiseRebuild() => noiseReady = false;
        [ContextMenu("Rebuild Atmosphere Tables")]
        public void RequestRebuild()
        {
            forceNewFrame = true;
            lutHash = 0;
            noiseReady = false;
            failed = false;
            foreach (var state in cameras.Values) state.HistoryValid = false;
        }
        public void ApplyFairWeatherClouds()
        {
            Clouds ??= new CloudSettings();
            var preset = new CloudSettings();
            Clouds.Enabled = true;
            Clouds.Coverage = preset.Coverage;
            Clouds.CloudType = preset.CloudType;
            Clouds.TypeVariation = preset.TypeVariation;
            Clouds.Precipitation = preset.Precipitation;
            Clouds.DetailStrength = preset.DetailStrength;
            Clouds.Extinction = preset.Extinction;
            Clouds.Albedo = preset.Albedo;
            Clouds.AmbientStrength = preset.AmbientStrength;
            Clouds.MultiScatteringAttenuation = preset.MultiScatteringAttenuation;
            Clouds.MultiScatteringContribution = preset.MultiScatteringContribution;
            Clouds.MultiScatteringPhaseDecay = preset.MultiScatteringPhaseDecay;
            RequestRebuild();
        }
        /// <summary>Treat the next camera as a new frame even if Time.frameCount has not advanced (offline rendering).</summary>
        public void InvalidateFrame() => forceNewFrame = true;

        /// <summary>Launch one cinematic meteor along the configured world-space sky path.</summary>
        [ContextMenu("Trigger Meteor")]
        public void TriggerMeteor()
        {
            meteorState.Trigger(Time.timeAsDouble);
            forceNewFrame = true;
        }

        void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != null && camera.cameraType == CameraType.Reflection)
            {
                Shader.SetGlobalFloat(AtmosphereShaderIDs.MoonCameraVisible, 0);
                Shader.SetGlobalFloat(AtmosphereShaderIDs.AuroraCameraVisible, 0);
                Shader.SetGlobalFloat("_MeteorCameraVisible", 0);
                return;
            }
            if (!isActiveAndEnabled || camera == null || failed) return;
            if (camera.cameraType == CameraType.SceneView) { if (!ShowInSceneView) return; }
            else if (camera.cameraType != CameraType.Game || (TargetCamera != null && camera != TargetCamera)) return;
            if ((camera.cullingMask & (1 << gameObject.layer)) == 0) return;
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supports3DTextures)
            {
                if (!unsupportedReported) Debug.LogError("Atmosphere rendering requires compute shaders and 3D textures.", this);
                unsupportedReported = true;
                return;
            }
            try
            {
                EnsureResources();
                var state = GetState(camera);
                commands.Clear();
                bool newFrame = forceNewFrame || frameIndex != Time.frameCount;
                forceNewFrame = false;
                if (newFrame)
                {
                    frameIndex = Time.frameCount;
                    RecordFrame(camera);
                }
                RecordCamera(camera, state);
                if (newFrame) RecordFrameSky(camera);
                RecordClouds(camera, state);
                Graphics.ExecuteCommandBuffer(commands);
                if (newFrame)
                {
                    UpdateAmbientReadback();
                    ApplySceneState(camera);
                    ExpireCameras();
                    Status = string.Format("Sun elevation {0:0.0} deg, transmittance ({1:0.00}, {2:0.00}, {3:0.00}){4}",
                        Mathf.Asin(Mathf.Clamp(SunDirection.y, -1, 1)) * Mathf.Rad2Deg, SunTransmittance.x, SunTransmittance.y, SunTransmittance.z,
                        cloudsActive ? string.Format(", clouds {0}x{1}", LastCloudBufferSize.x, LastCloudBufferSize.y) : ", clouds off");
                }
                IssueDraws(camera, state);
                LastCameraName = camera.name;
            }
            catch (Exception exception)
            {
                failed = true;
                Status = "Failed: " + exception.Message;
                Debug.LogException(exception, this);
            }
        }

        void EndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            // Reflection probes deliberately omit the new moon until object-lighting support is added.
            if (camera != null && camera.cameraType == CameraType.Reflection)
            {
                Shader.SetGlobalFloat(AtmosphereShaderIDs.MoonCameraVisible, 1);
                Shader.SetGlobalFloat(AtmosphereShaderIDs.AuroraCameraVisible, 1);
                Shader.SetGlobalFloat("_MeteorCameraVisible", 1);
            }
        }

        // ------------------------------------------------------------------ resources
        void EnsureResources()
        {
            Atmosphere ??= new AtmosphereSettings();
            Clouds ??= new CloudSettings();
            NightSky ??= new NightSkySettings();
            Moon ??= new MoonSettings();
            Aurora ??= new AuroraSettings();
            Meteors ??= new MeteorSettings();
            Lighting ??= new AtmosphereLightingSettings();
            Atmosphere.Validate();
            Clouds.Validate();
            NightSky.Validate();
            Moon.Validate();
            Aurora.Validate();
            Meteors.Validate();
            var resources = AtmosphereResources.Load();
            if (lutShader != resources.AtmosphereLUT || noiseShader != resources.CloudNoise || cloudShader != resources.VolumetricClouds || skyboxShader != resources.Skybox)
            {
                lutShader = resources.AtmosphereLUT; noiseShader = resources.CloudNoise; cloudShader = resources.VolumetricClouds; skyboxShader = resources.Skybox;
                if (lutShader == null || noiseShader == null || cloudShader == null || skyboxShader == null || resources.AerialPerspective == null || resources.CloudComposite == null)
                    throw new InvalidOperationException("AtmosphereDefaults is missing shaders. Restore the shader references in Assets/WaterSystem/Resources/WaterSystem/AtmosphereDefaults.asset.");
                transmittanceKernel = new Kernel(lutShader, "TransmittanceLUT");
                multiScatterKernel = new Kernel(lutShader, "MultiScatteringLUT");
                skyViewKernel = new Kernel(lutShader, "SkyViewLUT");
                aerialKernel = new Kernel(lutShader, "AerialPerspectiveLUT");
                cubemapKernel = new Kernel(lutShader, "SkyCubemap");
                ambientKernel = new Kernel(lutShader, "AmbientProbe");
                shapeKernel = new Kernel(noiseShader, "ShapeNoise");
                detailKernel = new Kernel(noiseShader, "DetailNoise");
                weatherKernel = new Kernel(noiseShader, "WeatherMap");
                marchKernel = new Kernel(cloudShader, "CloudMarch");
                resolveKernel = new Kernel(cloudShader, "CloudResolve");
                panoramaKernel = new Kernel(cloudShader, "CloudPanorama");
                shadowKernel = new Kernel(cloudShader, "CloudShadowMap");
                lutHash = 0;
                noiseReady = false;
            }
            commands ??= new CommandBuffer { name = "Atmosphere / sky and clouds" };
            transmittanceLUT ??= AtmosphereCameraState.Create("Atmosphere / Transmittance LUT", 256, 64, 1, GraphicsFormat.R16G16B16A16_SFloat, TextureDimension.Tex2D, FilterMode.Bilinear);
            multiScatterLUT ??= AtmosphereCameraState.Create("Atmosphere / Multiple scattering LUT", 32, 32, 1, GraphicsFormat.R16G16B16A16_SFloat, TextureDimension.Tex2D, FilterMode.Bilinear);
            weatherMap ??= AtmosphereCameraState.Create("Clouds / Weather map", WeatherSize, WeatherSize, 1, GraphicsFormat.R8G8B8A8_UNorm, TextureDimension.Tex2D, FilterMode.Bilinear, TextureWrapMode.Repeat);
            if (cloudPanorama == null)
            {
                cloudPanorama = AtmosphereCameraState.Create("Clouds / Panorama", PanoramaWidth, PanoramaHeight, 1, GraphicsFormat.R16G16B16A16_SFloat, TextureDimension.Tex2D, FilterMode.Bilinear);
                cloudPanorama.wrapModeU = TextureWrapMode.Repeat;
                cloudPanorama.wrapModeV = TextureWrapMode.Clamp;
            }
            shapeNoise ??= AtmosphereCameraState.Create("Clouds / Shape noise", ShapeNoiseSize, ShapeNoiseSize, ShapeNoiseSize, GraphicsFormat.R8G8B8A8_UNorm, TextureDimension.Tex3D, FilterMode.Bilinear, TextureWrapMode.Repeat);
            detailNoise ??= AtmosphereCameraState.Create("Clouds / Detail noise", DetailNoiseSize, DetailNoiseSize, DetailNoiseSize, GraphicsFormat.R8G8B8A8_UNorm, TextureDimension.Tex3D, FilterMode.Bilinear, TextureWrapMode.Repeat);
            if (ambientBuffer == null)
            {
                ambientBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 6, 16);
                ambientBuffer.SetData(new[] { new Vector4(0.3f, 0.4f, 0.55f, 1), new Vector4(0.25f, 0.3f, 0.35f, 1), new Vector4(0.08f, 0.1f, 0.12f, 1), Vector4.one,
                    new Vector4(0.3f, 0.4f, 0.55f, 1), new Vector4(0.08f, 0.1f, 0.12f, 1) });
            }
            if (skyCubemap == null || skyCubemapFaces == null || skyCubemap.width != Clouds.ReflectionResolution)
            {
                // Compute shaders cannot write to a cubemap directly (there is no RWTextureCube); the kernel fills a six slice
                // array and the slices are copied into the cubemap faces, which requires copies between texture types.
                if ((SystemInfo.copyTextureSupport & CopyTextureSupport.DifferentTypes) == 0)
                    throw new NotSupportedException("Atmosphere rendering requires CopyTexture between texture arrays and cubemaps.");
                AtmosphereCameraState.Destroy(ref skyCubemap);
                AtmosphereCameraState.Destroy(ref skyCubemapFaces);
                skyCubemap = new RenderTexture(Clouds.ReflectionResolution, Clouds.ReflectionResolution, 0, GraphicsFormat.R16G16B16A16_SFloat)
                {
                    name = "Atmosphere / Sky reflection cubemap", dimension = TextureDimension.Cube,
                    useMipMap = true, autoGenerateMips = false, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
                };
                if (!skyCubemap.Create()) throw new InvalidOperationException("Cannot allocate the sky reflection cubemap.");
                skyCubemapFaces = AtmosphereCameraState.Create("Atmosphere / Sky reflection faces", Clouds.ReflectionResolution, Clouds.ReflectionResolution, 6,
                    GraphicsFormat.R16G16B16A16_SFloat, TextureDimension.Tex2DArray, FilterMode.Bilinear);
            }
            if (cloudShadowMap == null || cloudShadowMap.width != Clouds.ShadowResolution)
            {
                AtmosphereCameraState.Destroy(ref cloudShadowMap);
                var format = PickFormat(GraphicsFormat.R16_UNorm, GraphicsFormat.R8_UNorm, GraphicsFormat.R16_SFloat, GraphicsFormat.R32_SFloat);
                cloudShadowMap = AtmosphereCameraState.Create("Clouds / Shadow map", Clouds.ShadowResolution, Clouds.ShadowResolution, 1, format, TextureDimension.Tex2D, FilterMode.Bilinear);
            }
            aerialMaterial ??= new Material(resources.AerialPerspective) { name = "Atmosphere aerial perspective (runtime)", hideFlags = HideFlags.HideAndDontSave };
            compositeMaterial ??= new Material(resources.CloudComposite) { name = "Atmosphere cloud composite (runtime)", hideFlags = HideFlags.HideAndDontSave };
            if (triangle == null)
            {
                triangle = new Mesh { name = "Atmosphere full-screen triangle", hideFlags = HideFlags.HideAndDontSave };
                triangle.SetVertices(new[] { new Vector3(-1, -1, 0), new Vector3(3, -1, 0), new Vector3(-1, 3, 0) });
                triangle.SetTriangles(new[] { 0, 1, 2 }, 0);
                triangle.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
                triangle.UploadMeshData(true);
            }
        }
        static GraphicsFormat PickFormat(params GraphicsFormat[] candidates)
        {
            foreach (var format in candidates)
                if (SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.LoadStore) && SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample)) return format;
            return candidates[candidates.Length - 1];
        }
        AtmosphereCameraState GetState(Camera camera)
        {
            if (!cameras.TryGetValue(camera, out var state))
            {
                state = new AtmosphereCameraState(SkyViewWidth, SkyViewHeight, AerialSize);
                cameras.Add(camera, state);
            }
            state.LastSeenFrame = Time.frameCount;
            return state;
        }
        void ExpireCameras()
        {
            expiredCameras.Clear();
            foreach (var pair in cameras)
                if (pair.Key == null || Time.frameCount - pair.Value.LastSeenFrame > 120) expiredCameras.Add(pair.Key);
            foreach (var camera in expiredCameras)
            {
                cameras[camera].Dispose();
                cameras.Remove(camera);
            }
        }
        void ReleaseResources()
        {
            foreach (var state in cameras.Values) state.Dispose();
            cameras.Clear();
            AtmosphereCameraState.Destroy(ref transmittanceLUT);
            AtmosphereCameraState.Destroy(ref multiScatterLUT);
            AtmosphereCameraState.Destroy(ref weatherMap);
            AtmosphereCameraState.Destroy(ref cloudShadowMap);
            AtmosphereCameraState.Destroy(ref cloudPanorama);
            AtmosphereCameraState.Destroy(ref skyCubemap);
            AtmosphereCameraState.Destroy(ref skyCubemapFaces);
            AtmosphereCameraState.Destroy(ref shapeNoise);
            AtmosphereCameraState.Destroy(ref detailNoise);
            ambientBuffer?.Dispose();
            ambientBuffer = null;
            DestroyOwned(skyboxMaterial); skyboxMaterial = null;
            DestroyOwned(aerialMaterial); aerialMaterial = null;
            DestroyOwned(compositeMaterial); compositeMaterial = null;
            DestroyOwned(triangle); triangle = null;
            commands?.Release();
            commands = null;
            lutShader = noiseShader = cloudShader = null;
            skyboxShader = null;
            readbackPending = false;
            ambientValid = false;
        }
        static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        // ------------------------------------------------------------------ per frame
        static Color ToLinear(Color color) => QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
        static Color ToInspector(Color color) => QualitySettings.activeColorSpace == ColorSpace.Linear ? color.gamma : color;

        void ResolveSun()
        {
            activeSun = Sun != null && Sun.type == LightType.Directional ? Sun : RenderSettings.sun;
            if ((activeSun == null || activeSun.type != LightType.Directional || !activeSun.isActiveAndEnabled) && Time.frameCount - sunSearchFrame > 60)
            {
                sunSearchFrame = Time.frameCount;
                activeSun = null;
                foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (light.type == LightType.Directional && light.isActiveAndEnabled) { activeSun = light; break; }
            }
            SunDirection = activeSun != null ? -activeSun.transform.forward : -(Quaternion.Euler(50, -30, 0) * Vector3.forward);
        }

        void RecordFrame(Camera camera)
        {
            ResolveSun();
            double now = Time.timeAsDouble;
            lastFrameDelta = previousTime == 0 ? 0 : Mathf.Clamp((float)(now - previousTime), 0, 0.1f);
            previousTime = now;
            weatherTime += lastFrameDelta * Clouds.EvolutionSpeed;
            Vector2 windDirection = Clouds.WindDirection;
            float windSpeed = Clouds.WindSpeed;
            if (Clouds.FollowOceanWind && Ocean != null && Ocean.FFT != null)
            {
                var parameters = Ocean.FFT.Parameters;
                windDirection = parameters.WindDirection;
                windSpeed = parameters.WindSpeed * Clouds.OceanWindMultiplier;
            }
            if (windDirection.sqrMagnitude < 1e-6f) windDirection = Vector2.right;
            windOffset += windDirection.normalized * (windSpeed * lastFrameDelta);
            cloudsActive = Clouds.Enabled;

            float bottom = Atmosphere.PlanetRadius, top = Atmosphere.TopRadius;
            float cloudBottom = bottom + Clouds.BottomAltitude * 0.001f;
            float cloudTop = cloudBottom + Clouds.Thickness * 0.001f;
            Color sun = ToLinear(Atmosphere.SunColor) * (Mathf.PI * Atmosphere.SunIntensity * Atmosphere.Exposure);
            Color ground = ToLinear(Atmosphere.GroundAlbedo), night = ToLinear(Atmosphere.NightSky) * Atmosphere.Exposure;
            Vector3 rayleigh = Atmosphere.RayleighScattering * (0.001f * Atmosphere.RayleighDensity);
            float mie = Atmosphere.MieScattering * 0.001f * Atmosphere.MieDensity;
            float mieAbsorption = Atmosphere.MieAbsorption * 0.001f * Atmosphere.MieDensity;
            Vector3 ozone = Atmosphere.OzoneAbsorption * (0.001f * Atmosphere.OzoneDensity);
            commands.SetGlobalVector(AtmosphereShaderIDs.Radii, new Vector4(bottom, top, cloudBottom, cloudTop));
            commands.SetGlobalVector(AtmosphereShaderIDs.Rayleigh, new Vector4(rayleigh.x, rayleigh.y, rayleigh.z, Atmosphere.RayleighScaleHeight));
            commands.SetGlobalVector(AtmosphereShaderIDs.Mie, new Vector4(mie, mie, mie, Atmosphere.MieScaleHeight));
            commands.SetGlobalVector(AtmosphereShaderIDs.MieAbsorption, new Vector4(mieAbsorption, mieAbsorption, mieAbsorption, Atmosphere.MieAnisotropy));
            commands.SetGlobalVector(AtmosphereShaderIDs.Ozone, new Vector4(ozone.x, ozone.y, ozone.z, Atmosphere.OzoneLayerCenter));
            commands.SetGlobalVector(AtmosphereShaderIDs.Ground, new Vector4(ground.r, ground.g, ground.b, Atmosphere.OzoneLayerWidth));
            commands.SetGlobalVector(AtmosphereShaderIDs.Sun, new Vector4(SunDirection.x, SunDirection.y, SunDirection.z, Mathf.Cos(0.2665f * Atmosphere.SunDiscScale * Mathf.Deg2Rad)));
            commands.SetGlobalVector(AtmosphereShaderIDs.SunIlluminance, new Vector4(sun.r, sun.g, sun.b, Atmosphere.AerialPerspectiveScale));
            commands.SetGlobalVector(AtmosphereShaderIDs.Night, new Vector4(night.r, night.g, night.b, 0));
            RecordNightSky(now);
            RecordMoon();
            RecordAurora(now);
            meteorState.Record(commands, Meteors, now, SunDirection, Atmosphere.Exposure);
            commands.SetGlobalVector(AtmosphereShaderIDs.LUT, new Vector4(SkyViewWidth, SkyViewHeight, AerialSize, Atmosphere.MaxAerialDistance));
            commands.SetGlobalVector(AtmosphereShaderIDs.World, new Vector4(SeaLevelY, 1, cloudsActive ? 1 : 0, 0));
            commands.SetGlobalVector(AtmosphereShaderIDs.CloudLayer, new Vector4(Clouds.BottomAltitude, Clouds.Thickness, 1f / Clouds.Thickness, 1f / (Clouds.WeatherScale * 1000f)));
            commands.SetGlobalVector(AtmosphereShaderIDs.CloudShape, new Vector4(1f / Clouds.ShapeScale, 1f / Clouds.DetailScale, Clouds.DetailStrength, 0));
            commands.SetGlobalVector(AtmosphereShaderIDs.CloudDensity, new Vector4(Clouds.Extinction, Clouds.Albedo, Clouds.WetnessDarkening, 0));
            commands.SetGlobalVector(AtmosphereShaderIDs.CloudWind, new Vector4(-windOffset.x, -windOffset.y, Clouds.WindShear, (float)now));
            commands.SetGlobalVector(AtmosphereShaderIDs.CloudPhase, new Vector4(Clouds.ForwardScattering, Clouds.BackScattering, Clouds.PhaseBlend, Clouds.AmbientStrength));
            commands.SetGlobalVector(AtmosphereShaderIDs.CloudMultiScatter, new Vector4(Clouds.MultiScatteringAttenuation, Clouds.MultiScatteringContribution, Clouds.MultiScatteringPhaseDecay, Clouds.PowderStrength));
            commands.SetGlobalVector(AtmosphereShaderIDs.CloudMarch, new Vector4(Clouds.MaxSteps, Clouds.MaxDistance, Clouds.StepGrowth, Clouds.LightStepLength));
            commands.SetGlobalTexture(AtmosphereShaderIDs.TransmittanceLUT, transmittanceLUT);
            commands.SetGlobalTexture(AtmosphereShaderIDs.MultiScatterLUT, multiScatterLUT);
            commands.SetGlobalTexture(AtmosphereShaderIDs.CloudShapeNoise, shapeNoise);
            commands.SetGlobalTexture(AtmosphereShaderIDs.CloudDetailNoise, detailNoise);
            commands.SetGlobalTexture(AtmosphereShaderIDs.CloudWeatherMap, weatherMap);
            commands.SetGlobalTexture(AtmosphereShaderIDs.CloudPanorama, cloudPanorama);
            commands.SetGlobalBuffer(AtmosphereShaderIDs.Ambient, ambientBuffer);

            int hash = Atmosphere.LutHash;
            if (hash != lutHash)
            {
                commands.SetComputeTextureParam(lutShader, transmittanceKernel.Index, AtmosphereShaderIDs.TransmittanceOutput, transmittanceLUT);
                transmittanceKernel.Dispatch(commands, lutShader, 256, 64, 1);
                commands.SetComputeTextureParam(lutShader, multiScatterKernel.Index, AtmosphereShaderIDs.TransmittanceLUT, transmittanceLUT);
                commands.SetComputeTextureParam(lutShader, multiScatterKernel.Index, AtmosphereShaderIDs.MultiScatterOutput, multiScatterLUT);
                multiScatterKernel.Dispatch(commands, lutShader, 32, 32, 64);
                lutHash = hash;
            }
            commands.SetComputeVectorParam(noiseShader, AtmosphereShaderIDs.NoiseSize, new Vector4(ShapeNoiseSize, DetailNoiseSize, WeatherSize, 0));
            if (!noiseReady)
            {
                commands.SetComputeTextureParam(noiseShader, shapeKernel.Index, AtmosphereShaderIDs.ShapeOutput, shapeNoise);
                shapeKernel.Dispatch(commands, noiseShader, ShapeNoiseSize, ShapeNoiseSize, ShapeNoiseSize);
                commands.SetComputeTextureParam(noiseShader, detailKernel.Index, AtmosphereShaderIDs.DetailOutput, detailNoise);
                detailKernel.Dispatch(commands, noiseShader, DetailNoiseSize, DetailNoiseSize, DetailNoiseSize);
                noiseReady = true;
            }
            if (cloudsActive)
            {
                commands.SetComputeVectorParam(noiseShader, AtmosphereShaderIDs.WeatherParams, new Vector4(Clouds.Coverage, Clouds.CloudType, Clouds.TypeVariation, Clouds.WeatherDetail));
                commands.SetComputeVectorParam(noiseShader, AtmosphereShaderIDs.WeatherTime, new Vector4(weatherTime, Clouds.Precipitation, Clouds.CoverageSoftness, 0));
                commands.SetComputeTextureParam(noiseShader, weatherKernel.Index, AtmosphereShaderIDs.WeatherOutput, weatherMap);
                weatherKernel.Dispatch(commands, noiseShader, WeatherSize, WeatherSize, 1);
            }
        }

        void RecordMoon()
        {
            if (moonHistoryReset)
            {
                foreach (var state in cameras.Values) state.HistoryValid = false;
                moonHistoryReset = false;
            }
            Vector3 direction = Moon.Direction(SunDirection).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, direction);
            if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(Vector3.forward, direction);
            right = Quaternion.AngleAxis(Moon.TextureRotation, direction) * right.normalized;
            Vector3 up = Vector3.Cross(direction, right).normalized;
            float radius = Moon.AngularDiameter * 0.5f * Mathf.Deg2Rad;
            Color tint = ToLinear(Moon.Tint) * Atmosphere.Exposure;
            float illumination = Moon.IlluminatedFraction;
            Color light = tint * (Mathf.PI * Moon.CloudLightIntensity * illumination);
            Texture2D texture = Moon.SurfaceTexture != null ? Moon.SurfaceTexture : AtmosphereResources.Load().MoonSurface;
            if (texture == null) texture = Texture2D.whiteTexture;
            commands.SetGlobalFloat(AtmosphereShaderIDs.MoonCameraVisible, 1);
            commands.SetGlobalVector(AtmosphereShaderIDs.MoonDirection, new Vector4(direction.x, direction.y, direction.z, Mathf.Cos(radius)));
            commands.SetGlobalVector(AtmosphereShaderIDs.MoonParams,
                new Vector4(Moon.Enabled ? 1 : 0, Moon.Brightness, Moon.HaloIntensity, Moon.HaloRadius * Mathf.Deg2Rad));
            commands.SetGlobalVector(AtmosphereShaderIDs.MoonSurface,
                new Vector4(radius, Moon.Phase * 2 * Mathf.PI, Moon.HaloFalloff, illumination));
            commands.SetGlobalVector(AtmosphereShaderIDs.MoonTint, new Vector4(tint.r, tint.g, tint.b, 1));
            commands.SetGlobalVector(AtmosphereShaderIDs.MoonRight, right);
            commands.SetGlobalVector(AtmosphereShaderIDs.MoonUp, up);
            commands.SetGlobalVector(AtmosphereShaderIDs.MoonTextureSize,
                new Vector4(texture.width, texture.height, Mathf.Max(0, texture.mipmapCount - 1), 0));
            commands.SetGlobalVector(AtmosphereShaderIDs.MoonLight,
                new Vector4(light.r, light.g, light.b, Moon.Enabled && Moon.CloudLightIntensity > 0 && illumination > 0.0001f ? 1 : 0));
            commands.SetGlobalTexture(AtmosphereShaderIDs.MoonSurfaceTexture, texture);
        }

        void RecordAurora(double now)
        {
            var texture = Aurora.NoiseTexture != null ? Aurora.NoiseTexture : AtmosphereResources.Load().AuroraNoise;
            if (texture == null) texture = Texture2D.grayTexture;
            float azimuth = Aurora.Azimuth * Mathf.Deg2Rad;
            // Every use of this phase in the shader is periodic, so wrapping does not cause a jump.
            float phase = (float)((now * Aurora.Speed / 60.0 + Aurora.AnimationOffset) % 1.0);
            Color lower = ToLinear(Aurora.LowerColor) * Atmosphere.Exposure;
            Color upper = ToLinear(Aurora.UpperColor) * Atmosphere.Exposure;
            commands.SetGlobalFloat(AtmosphereShaderIDs.AuroraCameraVisible, 1);
            commands.SetGlobalTexture(AtmosphereShaderIDs.AuroraNoise, texture);
            commands.SetGlobalVector(AtmosphereShaderIDs.AuroraParams,
                new Vector4(Aurora.Enabled ? 1 : 0, Aurora.Intensity, Aurora.Visibility(SunDirection), Aurora.Samples));
            commands.SetGlobalVector(AtmosphereShaderIDs.AuroraLayer,
                new Vector4(Aurora.BottomAltitude, Aurora.Height, Aurora.Distance, Aurora.Width));
            commands.SetGlobalVector(AtmosphereShaderIDs.AuroraShape,
                new Vector4(Aurora.FoldScale, Aurora.FoldStrength, Aurora.HorizontalExtent, Aurora.CurtainCount));
            commands.SetGlobalVector(AtmosphereShaderIDs.AuroraDetail,
                new Vector4(Aurora.RayStrength, Aurora.CurtainSpacing, texture.width, Mathf.Max(0, texture.mipmapCount - 1)));
            commands.SetGlobalVector(AtmosphereShaderIDs.AuroraFrame,
                new Vector4(Mathf.Sin(azimuth), Mathf.Cos(azimuth), transform.position.x * 0.001f, transform.position.z * 0.001f));
            commands.SetGlobalVector(AtmosphereShaderIDs.AuroraAnimation,
                new Vector4(phase, Mathf.Sin(Aurora.HorizonFade * Mathf.Deg2Rad), 0, 0));
            commands.SetGlobalVector(AtmosphereShaderIDs.AuroraLowerColor, new Vector4(lower.r, lower.g, lower.b, 0));
            commands.SetGlobalVector(AtmosphereShaderIDs.AuroraUpperColor, new Vector4(upper.r, upper.g, upper.b, 0));
        }

        void RecordNightSky(double now)
        {
            Texture2D galaxy = NightSky.GalaxyTexture != null ? NightSky.GalaxyTexture : AtmosphereResources.Load().MilkyWay;
            if (galaxy == null) galaxy = Texture2D.blackTexture;
            Color tint = ToLinear(NightSky.Tint) * Atmosphere.Exposure;
            float rotation = NightSky.AutomaticRotation ? (float)((now / (NightSky.RotationPeriodMinutes * 60.0)) % 1.0) * 360f : 0;
            var worldToSky = Matrix4x4.Rotate(Quaternion.Euler(NightSky.Rotation + new Vector3(0, rotation, 0))).transpose;
            commands.SetGlobalVector(AtmosphereShaderIDs.NightSkyParams,
                new Vector4(NightSky.Enabled ? 1 : 0, NightSky.StarIntensity, NightSky.GalaxyIntensity, NightSky.Visibility(SunDirection)));
            commands.SetGlobalVector(AtmosphereShaderIDs.NightSkyStars,
                new Vector4(NightSky.StarDensity, NightSky.StarSize, NightSky.TwinkleStrength, (float)(now % 10000.0)));
            commands.SetGlobalVector(AtmosphereShaderIDs.NightSkyTint,
                new Vector4(tint.r, tint.g, tint.b, Mathf.Sin(NightSky.HorizonFade * Mathf.Deg2Rad)));
            commands.SetGlobalVector(AtmosphereShaderIDs.NightSkyTextureSize,
                new Vector4(galaxy.width, galaxy.height, Mathf.Max(0, galaxy.mipmapCount - 1), 0));
            commands.SetGlobalMatrix(AtmosphereShaderIDs.NightSkyWorldToSky, worldToSky);
            commands.SetGlobalTexture(AtmosphereShaderIDs.NightSkyGalaxyTexture, galaxy);
            // Compute kernels require explicit resource bindings even when the runtime night switch is off.
            commands.SetComputeTextureParam(lutShader, cubemapKernel.Index, AtmosphereShaderIDs.NightSkyGalaxyTexture, galaxy);
            commands.SetComputeTextureParam(lutShader, ambientKernel.Index, AtmosphereShaderIDs.NightSkyGalaxyTexture, galaxy);
        }

        void RecordCamera(Camera camera, AtmosphereCameraState state)
        {
            Vector3 position = camera.transform.position;
            float altitudeKm = Mathf.Clamp((position.y - SeaLevelY) * 0.001f, 0.0005f, Atmosphere.AtmosphereHeight - 0.1f);
            cameraRadiusKm = Atmosphere.PlanetRadius + altitudeKm;
            commands.SetGlobalVector(AtmosphereShaderIDs.Camera, new Vector4(position.x, position.y, position.z, cameraRadiusKm));
            // OpenGL clip convention on both sides: URP screen UVs are Y-up NDC, which this matches without platform flips.
            state.ViewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
            state.InverseViewProjection = state.ViewProjection.inverse;
            BindAtmosphereTables(lutShader, skyViewKernel);
            commands.SetComputeTextureParam(lutShader, skyViewKernel.Index, AtmosphereShaderIDs.SkyViewOutput, state.SkyView);
            skyViewKernel.Dispatch(commands, lutShader, SkyViewWidth, SkyViewHeight, 1);
            commands.SetGlobalTexture(AtmosphereShaderIDs.SkyViewLUT, state.SkyView);
            commands.SetComputeMatrixParam(lutShader, AtmosphereShaderIDs.InverseViewProjection, state.InverseViewProjection);
            BindAtmosphereTables(lutShader, aerialKernel);
            commands.SetComputeTextureParam(lutShader, aerialKernel.Index, AtmosphereShaderIDs.AerialOutput, state.Aerial);
            aerialKernel.Dispatch(commands, lutShader, AerialSize, AerialSize, AerialSize);
            commands.SetGlobalTexture(AtmosphereShaderIDs.AerialLUT, state.Aerial);
        }

        // Camera-independent sky products that need a sky-view LUT: computed once per frame from the first camera.
        void RecordFrameSky(Camera camera)
        {
            var state = GetState(camera);
            commands.SetComputeTextureParam(lutShader, ambientKernel.Index, AtmosphereShaderIDs.SkyViewLUT, state.SkyView);
            commands.SetComputeTextureParam(lutShader, ambientKernel.Index, AtmosphereShaderIDs.TransmittanceLUT, transmittanceLUT);
            commands.SetComputeTextureParam(lutShader, ambientKernel.Index, AtmosphereShaderIDs.CloudPanorama, cloudPanorama);
            commands.SetComputeTextureParam(lutShader, cubemapKernel.Index, AtmosphereShaderIDs.SkyViewLUT, state.SkyView);
            commands.SetComputeTextureParam(lutShader, cubemapKernel.Index, AtmosphereShaderIDs.TransmittanceLUT, transmittanceLUT);
            commands.SetComputeTextureParam(lutShader, cubemapKernel.Index, AtmosphereShaderIDs.CloudPanorama, cloudPanorama);
            if (cloudsActive)
            {
                BindAtmosphereTables(cloudShader, panoramaKernel);
                BindCloudNoise(panoramaKernel);
                commands.SetComputeVectorParam(cloudShader, AtmosphereShaderIDs.CloudPanoramaSize, new Vector4(PanoramaWidth, PanoramaHeight, 0, 0));
                commands.SetComputeBufferParam(cloudShader, panoramaKernel.Index, AtmosphereShaderIDs.Ambient, ambientBuffer);
                commands.SetComputeTextureParam(cloudShader, panoramaKernel.Index, AtmosphereShaderIDs.CloudPanoramaOutput, cloudPanorama);
                panoramaKernel.Dispatch(commands, cloudShader, PanoramaWidth, PanoramaHeight, 1);
            }
            commands.SetComputeBufferParam(lutShader, ambientKernel.Index, AtmosphereShaderIDs.AmbientOutput, ambientBuffer);
            commands.DispatchCompute(lutShader, ambientKernel.Index, 1, 1, 1);
            commands.SetComputeFloatParam(lutShader, AtmosphereShaderIDs.SkyCubemapSize, skyCubemap.width);
            commands.SetComputeTextureParam(lutShader, cubemapKernel.Index, AtmosphereShaderIDs.SkyCubemapOutput, skyCubemapFaces);
            cubemapKernel.Dispatch(commands, lutShader, skyCubemap.width, skyCubemap.width, 6);
            for (int face = 0; face < 6; face++) commands.CopyTexture(skyCubemapFaces, face, 0, skyCubemap, face, 0);
            commands.GenerateMips(skyCubemap);
            commands.SetGlobalTexture(AtmosphereShaderIDs.SkyReflection, skyCubemap);
            commands.SetGlobalVector(AtmosphereShaderIDs.SkyReflectionParams, new Vector4(Mathf.Log(skyCubemap.width, 2), 0, 0, 0));
            bool shadows = cloudsActive && Clouds.CastShadows && activeSun != null;
            if (shadows)
            {
                BindCloudNoise(shadowKernel);
                shadowExtent = Clouds.ShadowExtent;
                Matrix4x4 worldToLight = activeSun.transform.worldToLocalMatrix;
                Vector3 centerLocal = worldToLight.MultiplyPoint3x4(camera.transform.position);
                shadowOffset = new Vector2(centerLocal.x, centerLocal.y) / shadowExtent;
                float strength = Clouds.ShadowStrength * Mathf.Clamp01((SunDirection.y - 0.02f) / 0.12f);
                Vector3 forward = activeSun.transform.forward;
                commands.SetComputeVectorParam(cloudShader, AtmosphereShaderIDs.CloudShadowSetup, new Vector4(shadowExtent, shadowOffset.x, shadowOffset.y, cloudShadowMap.width));
                commands.SetComputeVectorParam(cloudShader, AtmosphereShaderIDs.CloudShadowDirection, new Vector4(forward.x, forward.y, forward.z, strength));
                commands.SetComputeMatrixParam(cloudShader, AtmosphereShaderIDs.CloudShadowLightToWorld, activeSun.transform.localToWorldMatrix);
                commands.SetComputeTextureParam(cloudShader, shadowKernel.Index, AtmosphereShaderIDs.CloudShadowOutput, cloudShadowMap);
                shadowKernel.Dispatch(commands, cloudShader, cloudShadowMap.width, cloudShadowMap.width, 1);
                commands.SetGlobalTexture(AtmosphereShaderIDs.CloudShadowMap, cloudShadowMap);
                commands.SetGlobalMatrix(AtmosphereShaderIDs.CloudShadowWorldToLight, worldToLight);
                commands.SetGlobalVector(AtmosphereShaderIDs.CloudShadowParams, new Vector4(1f / shadowExtent, shadowOffset.x, shadowOffset.y, 1));
            }
            else commands.SetGlobalVector(AtmosphereShaderIDs.CloudShadowParams, Vector4.zero);
        }

        void RecordClouds(Camera camera, AtmosphereCameraState state)
        {
            // Full-screen draws read this; skybox-only cameras composite the panorama instead.
            commands.SetGlobalVector(AtmosphereShaderIDs.World, new Vector4(SeaLevelY, 1, cloudsActive ? 1 : 0, 0));
            if (!cloudsActive) { state.HistoryValid = false; return; }
            int width = Mathf.Max(8, camera.pixelWidth / Clouds.Downsample);
            int height = Mathf.Max(8, camera.pixelHeight / Clouds.Downsample);
            state.EnsureCloudBuffers(width, height);
            BindAtmosphereTables(cloudShader, marchKernel);
            BindCloudNoise(marchKernel);
            LastCloudBufferSize = new Vector2Int(width, height);
            int jitterIndex = (state.JitterIndex++ & 15) + 1;
            var jitter = new Vector2(AtmosphereMath.Halton(jitterIndex, 2) - 0.5f, AtmosphereMath.Halton(jitterIndex, 3) - 0.5f);
            float history = state.HistoryValid ? Clouds.TemporalBlend : 0;
            commands.SetComputeVectorParam(cloudShader, AtmosphereShaderIDs.CloudTemporal, new Vector4(history, jitter.x, jitter.y, AtmosphereMath.Halton(jitterIndex, 5)));
            commands.SetComputeVectorParam(cloudShader, AtmosphereShaderIDs.CloudBufferSize, new Vector4(width, height, 1f / width, 1f / height));
            commands.SetComputeMatrixParam(cloudShader, AtmosphereShaderIDs.CloudInverseViewProjection, state.InverseViewProjection);
            commands.SetComputeMatrixParam(cloudShader, AtmosphereShaderIDs.CloudPreviousViewProjection, state.HistoryValid ? state.PreviousViewProjection : state.ViewProjection);
            commands.SetComputeBufferParam(cloudShader, marchKernel.Index, AtmosphereShaderIDs.Ambient, ambientBuffer);
            commands.SetComputeTextureParam(cloudShader, marchKernel.Index, AtmosphereShaderIDs.CloudRaw, state.CloudRaw);
            commands.SetComputeTextureParam(cloudShader, marchKernel.Index, AtmosphereShaderIDs.CloudRawDepth, state.CloudRawDepth);
            marchKernel.Dispatch(commands, cloudShader, width, height, 1);
            int read = state.HistoryIndex, write = 1 - read;
            commands.SetComputeTextureParam(cloudShader, resolveKernel.Index, AtmosphereShaderIDs.CloudRawRead, state.CloudRaw);
            commands.SetComputeTextureParam(cloudShader, resolveKernel.Index, AtmosphereShaderIDs.CloudRawDepthRead, state.CloudRawDepth);
            commands.SetComputeTextureParam(cloudShader, resolveKernel.Index, AtmosphereShaderIDs.CloudHistory, state.History[read]);
            commands.SetComputeTextureParam(cloudShader, resolveKernel.Index, AtmosphereShaderIDs.CloudHistoryDepth, state.HistoryDepth[read]);
            commands.SetComputeTextureParam(cloudShader, resolveKernel.Index, AtmosphereShaderIDs.CloudHistoryOut, state.History[write]);
            commands.SetComputeTextureParam(cloudShader, resolveKernel.Index, AtmosphereShaderIDs.CloudHistoryDepthOut, state.HistoryDepth[write]);
            resolveKernel.Dispatch(commands, cloudShader, width, height, 1);
            state.HistoryIndex = write;
            state.HistoryValid = true;
            state.PreviousViewProjection = state.ViewProjection;
            commands.SetGlobalTexture(AtmosphereShaderIDs.CloudBuffer, state.History[write]);
            state.Properties.SetTexture(AtmosphereShaderIDs.CloudBuffer, state.History[write]);
        }

        void BindAtmosphereTables(ComputeShader shader, Kernel kernel)
        {
            commands.SetComputeTextureParam(shader, kernel.Index, AtmosphereShaderIDs.TransmittanceLUT, transmittanceLUT);
            commands.SetComputeTextureParam(shader, kernel.Index, AtmosphereShaderIDs.MultiScatterLUT, multiScatterLUT);
        }

        void BindCloudNoise(Kernel kernel)
        {
            commands.SetComputeTextureParam(cloudShader, kernel.Index, AtmosphereShaderIDs.CloudShapeNoise, shapeNoise);
            commands.SetComputeTextureParam(cloudShader, kernel.Index, AtmosphereShaderIDs.CloudDetailNoise, detailNoise);
            commands.SetComputeTextureParam(cloudShader, kernel.Index, AtmosphereShaderIDs.CloudWeatherMap, weatherMap);
        }

        void IssueDraws(Camera camera, AtmosphereCameraState state)
        {
            var bounds = new Bounds(camera.transform.position, Vector3.one * 100);
            if (Lighting.ApplyAerialPerspective) Draw(aerialMaterial, camera, state, bounds);
            if (cloudsActive) Draw(compositeMaterial, camera, state, bounds);
        }
        void Draw(Material material, Camera camera, AtmosphereCameraState state, Bounds bounds)
        {
            var parameters = new RenderParams(material)
            {
                camera = camera, layer = gameObject.layer, matProps = state.Properties, worldBounds = bounds,
                shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off, motionVectorMode = MotionVectorGenerationMode.ForceNoMotion
            };
            Graphics.RenderMesh(parameters, triangle, 0, Matrix4x4.identity);
        }

        // ------------------------------------------------------------------ scene state
        void UpdateAmbientReadback()
        {
            if (!SystemInfo.supportsAsyncGPUReadback) return;
            if (readbackPending)
            {
                if (!readback.done) return;
                readbackPending = false;
                if (!readback.hasError)
                {
                    var data = readback.GetData<Vector4>();
                    for (int i = 0; i < 4 && i < data.Length; i++) ambientData[i] = data[i];
                    ambientValid = true;
                }
            }
            readback = AsyncGPUReadback.Request(ambientBuffer);
            readbackPending = true;
        }
        void SaveSceneStateOnce()
        {
            if (savedRenderSettings) return;
            savedRenderSettings = true;
            savedAmbientMode = RenderSettings.ambientMode;
            savedAmbientSky = RenderSettings.ambientSkyColor;
            savedAmbientEquator = RenderSettings.ambientEquatorColor;
            savedAmbientGround = RenderSettings.ambientGroundColor;
            savedReflectionMode = RenderSettings.defaultReflectionMode;
            savedReflectionTexture = RenderSettings.customReflectionTexture;
            savedFog = RenderSettings.fog;
            savedSkybox = RenderSettings.skybox;
        }
        void ApplySceneState(Camera camera)
        {
            SaveSceneStateOnce();
            SunTransmittance = AtmosphereMath.SunTransmittance(Atmosphere, cameraRadiusKm, SunDirection);
            if (Lighting.DriveSunLight && activeSun != null)
            {
                Color sun = ToLinear(Atmosphere.SunColor);
                activeSun.color = ToInspector(new Color(sun.r * SunTransmittance.x, sun.g * SunTransmittance.y, sun.b * SunTransmittance.z, 1));
                activeSun.intensity = Atmosphere.SunIntensity;
            }
            if (Lighting.DriveAmbient && ambientValid)
            {
                Color floor = ToLinear(Lighting.MinimumAmbient);
                Vector3 sky = Vector3.Max((Vector3)(Vector4)ambientData[0] * Lighting.AmbientIntensity, new Vector3(floor.r, floor.g, floor.b));
                Vector3 equator = Vector3.Max((Vector3)(Vector4)ambientData[1] * Lighting.AmbientIntensity, new Vector3(floor.r, floor.g, floor.b));
                Vector3 ground = Vector3.Max((Vector3)(Vector4)ambientData[2] * Lighting.AmbientIntensity, new Vector3(floor.r, floor.g, floor.b) * 0.5f);
                float blend = smoothedSky == Vector3.zero ? 1 : 1 - Mathf.Exp(-lastFrameDelta * 6);
                smoothedSky = Vector3.Lerp(smoothedSky, sky, blend);
                smoothedEquator = Vector3.Lerp(smoothedEquator, equator, blend);
                smoothedGround = Vector3.Lerp(smoothedGround, ground, blend);
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = ToInspector(new Color(smoothedSky.x, smoothedSky.y, smoothedSky.z, 1));
                RenderSettings.ambientEquatorColor = ToInspector(new Color(smoothedEquator.x, smoothedEquator.y, smoothedEquator.z, 1));
                RenderSettings.ambientGroundColor = ToInspector(new Color(smoothedGround.x, smoothedGround.y, smoothedGround.z, 1));
            }
            if (Lighting.DriveReflections)
            {
                if (RenderSettings.defaultReflectionMode != DefaultReflectionMode.Custom) RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                if (RenderSettings.customReflectionTexture != skyCubemap) RenderSettings.customReflectionTexture = skyCubemap;
            }
            if (Lighting.DisableUnityFog && RenderSettings.fog) RenderSettings.fog = false;
            if (Lighting.DriveSkybox) EnsureSkybox();
            bool cookie = cloudsActive && Clouds.CastShadows && Clouds.SunLightCookie && activeSun != null;
            if (cookie) ApplyCookie();
            else RemoveCookie();
        }
        void EnsureSkybox()
        {
            var current = RenderSettings.skybox;
            if (current != null && current.shader == skyboxShader) return;
            if (skyboxMaterial == null) skyboxMaterial = new Material(skyboxShader) { name = "Atmosphere sky (runtime)", hideFlags = HideFlags.HideAndDontSave };
            RenderSettings.skybox = skyboxMaterial;
            skyboxAssigned = true;
        }
        void ApplyCookie()
        {
            if (cookieLight != activeSun)
            {
                RemoveCookie();
                cookieLight = activeSun;
                savedCookie = activeSun.cookie;
                var saved = activeSun.GetComponent<UniversalAdditionalLightData>();
                savedCookieSize = saved != null ? saved.lightCookieSize : Vector2.one;
                savedCookieOffset = saved != null ? saved.lightCookieOffset : Vector2.zero;
                cookieApplied = true;
            }
            if (activeSun.cookie != cloudShadowMap) activeSun.cookie = cloudShadowMap;
            var data = activeSun.GetUniversalAdditionalLightData();
            data.lightCookieSize = new Vector2(shadowExtent, shadowExtent);
            data.lightCookieOffset = shadowOffset;
        }
        void RemoveCookie()
        {
            if (!cookieApplied) return;
            cookieApplied = false;
            if (cookieLight != null)
            {
                if (cookieLight.cookie == cloudShadowMap) cookieLight.cookie = savedCookie;
                var data = cookieLight.GetComponent<UniversalAdditionalLightData>();
                if (data != null) { data.lightCookieSize = savedCookieSize; data.lightCookieOffset = savedCookieOffset; }
            }
            cookieLight = null;
            savedCookie = null;
        }
        void RestoreSceneState()
        {
            RemoveCookie();
            if (!savedRenderSettings) return;
            savedRenderSettings = false;
            if (Lighting == null) return;
            if (Lighting.DriveAmbient)
            {
                RenderSettings.ambientMode = savedAmbientMode;
                RenderSettings.ambientSkyColor = savedAmbientSky;
                RenderSettings.ambientEquatorColor = savedAmbientEquator;
                RenderSettings.ambientGroundColor = savedAmbientGround;
            }
            if (Lighting.DriveReflections)
            {
                RenderSettings.defaultReflectionMode = savedReflectionMode;
                RenderSettings.customReflectionTexture = savedReflectionTexture;
            }
            if (Lighting.DisableUnityFog) RenderSettings.fog = savedFog;
            if (skyboxAssigned && RenderSettings.skybox == skyboxMaterial) RenderSettings.skybox = savedSkybox;
            skyboxAssigned = false;
            smoothedSky = smoothedEquator = smoothedGround = Vector3.zero;
        }
    }
}
