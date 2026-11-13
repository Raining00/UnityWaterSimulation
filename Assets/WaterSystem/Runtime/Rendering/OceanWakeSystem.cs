using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace WaterSystem.Ocean
{
    /// <summary>
    /// Ship wakes of one ocean: a world-space window that follows the ships and is shared by every
    /// camera. It holds the foam and aeration history, and the waves the hulls raise. The waves are
    /// not simulated; a precomputed Kelvin pattern is bent along each ship's trail every frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OceanWakeSystem : MonoBehaviour
    {
        // Layout of KelvinWakeAtlas.exr. Must match Tools/WaterTextures/generate_kelvin_wake.py.
        static readonly float[] KelvinFroude = { 0.28f, 0.345f, 0.424f, 0.522f, 0.643f, 0.791f, 0.974f, 1.2f };
        // The same table as the kernel reads it: float4 _KelvinFroude[2].
        static readonly Vector4[] KelvinFroudeVectors =
        {
            new Vector4(KelvinFroude[0], KelvinFroude[1], KelvinFroude[2], KelvinFroude[3]),
            new Vector4(KelvinFroude[4], KelvinFroude[5], KelvinFroude[6], KelvinFroude[7])
        };
        internal const float KelvinLengthExtent = 16f, KelvinWidthExtent = 5.8f;
        const int KelvinRows = 192;
        // Seconds without any ship input after which the history has decayed to nothing and updates stop.
        const float IdleSeconds = 45f;
        // Window texels a ship wave needs per wavelength. Hulls too slow to make waves that long make none.
        const float TexelsPerWave = 6f;

        static readonly int NodesId = Shader.PropertyToID("_WakeNodes");
        static readonly int ShipsId = Shader.PropertyToID("_WakeShips");
        static readonly int SplatsId = Shader.PropertyToID("_WakeSplats");
        static readonly int ShipCountId = Shader.PropertyToID("_WakeShipCount");
        static readonly int SplatCountId = Shader.PropertyToID("_WakeSplatCount");
        static readonly int KelvinAtlasId = Shader.PropertyToID("_KelvinAtlas");
        static readonly int KelvinLayoutId = Shader.PropertyToID("_KelvinLayout");
        static readonly int KelvinFroudeId = Shader.PropertyToID("_KelvinFroude");
        static readonly int WaveResultId = Shader.PropertyToID("_WakeWaveResult");
        static readonly int WavesId = Shader.PropertyToID("_WakeWaves");
        static readonly int PreviousId = Shader.PropertyToID("_PreviousWake");
        static readonly int ResultId = Shader.PropertyToID("_WakeResult");
        static readonly int WindowId = Shader.PropertyToID("_WakeWindow");
        static readonly int ShiftXId = Shader.PropertyToID("_WakeShiftX");
        static readonly int ShiftYId = Shader.PropertyToID("_WakeShiftY");
        static readonly int FoamParamsId = Shader.PropertyToID("_WakeFoamParams");
        static readonly int DeltaTimeId = Shader.PropertyToID("_WakeDeltaTime");
        static readonly int SurfaceWavesId = Shader.PropertyToID("_OceanWakeWaves");
        static readonly int SurfaceFoamId = Shader.PropertyToID("_OceanWakeFoam");
        static readonly int OriginSizeId = Shader.PropertyToID("_OceanWakeOriginSize");
        static readonly int SurfaceParamsId = Shader.PropertyToID("_OceanWakeSurface");

        [Header("Window (world space, follows the ships)")]
        [Tooltip("Edge length in metres of the square that remembers wakes. Foam and waves leaving it are forgotten.")]
        [Min(32)] public float WorldSize = 192f;
        [Tooltip("Texels along one edge (256 to 2048). 1024 over 192 m resolves about 19 cm.")]
        public int Resolution = 1024;

        [Header("Foam history")]
        [Tooltip("Share of the foam that dissolves per second.")]
        [Min(0)] public float DecayPerSecond = 0.22f;
        [Tooltip("Share of the bubbles below the surface that rise out per second. Slower than the foam, so a pale trail outlasts the white water.")]
        [Min(0)] public float AerationDecayPerSecond = 0.1f;
        [Tooltip("How fast foam spreads sideways, in square metres per second.")]
        [Range(0, 0.2f)] public float FoamSpread = 0.02f;
        [Tooltip("How fast the bubble trail spreads sideways, in square metres per second.")]
        [Range(0, 1)] public float AerationSpread = 0.2f;

        [Header("Ship waves")]
        [Tooltip("Multiplies the wave height of every Ship Wake Emitter on this ocean. Zero disables ship waves.")]
        [Range(0, 3)] public float WaveScale = 1f;
        [Tooltip("How strongly churned water flattens the short ripples of the surface: the smooth lane behind a ship.")]
        [Range(0, 1)] public float RippleDamping = 0.6f;

        readonly List<WakeNode> nodes = new List<WakeNode>(128);
        readonly List<WakeShip> ships = new List<WakeShip>(4);
        readonly List<WakeSplat> splats = new List<WakeSplat>(64);
        RenderTexture[] history;
        RenderTexture waves;
        ComputeBuffer nodeBuffer, shipBuffer, splatBuffer;
        ComputeShader shader;
        int waveKernel = -1, foamKernel = -1, readIndex, allocatedResolution;
        Vector2 origin, centre;
        float allocatedSize, idleTime;
        bool hasOrigin, active, wavesWritten;

        void OnDisable() => Release();

        void OnValidate()
        {
            WorldSize = Mathf.Max(32, WorldSize);
            Resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(1, Resolution)), 256, 2048);
        }

        /// <summary>
        /// Furthest distance from the trail, in metres, at which a stretch of wake still shows waves.
        /// The kernel picks the atlas slices from the stretch's own wavenumber (see SampleKelvin).
        /// </summary>
        internal static float KelvinReach(float wavenumber, float hullLength)
        {
            float froude = 1f / Mathf.Sqrt(Mathf.Max(wavenumber * hullLength, 1e-6f));
            int lower = 0;
            while (lower < KelvinFroude.Length - 2 && froude >= KelvinFroude[lower + 1]) lower++;
            // Of the two slices that are blended, the lower one is read at the smaller scale and reaches further.
            return KelvinWidthExtent / Mathf.Max(wavenumber * KelvinFroude[lower] * KelvinFroude[lower], 1e-6f);
        }

        /// <summary>
        /// Records this frame's wake update. The FFT displacement is needed because window texels are
        /// undisplaced ocean coordinates: the kernels move them to where the water actually is before
        /// comparing them with a hull.
        /// </summary>
        internal void RecordUpdate(CommandBuffer commands, float deltaTime, in OceanSurfaceQueryResources surface, OceanRenderer owner)
        {
            if (!Application.isPlaying || !SystemInfo.supportsComputeShaders || !surface.IsValid) return;
            float time = Time.time;
            nodes.Clear();
            ships.Clear();
            splats.Clear();
            Vector2 focus = Vector2.zero;
            int emitters = 0;
            foreach (var emitter in ShipWakeEmitter.Active)
            {
                if (emitter == null || !emitter.isActiveAndEnabled) continue;
                var ocean = emitter.Ocean;
                if (ocean != null && ocean != owner) continue;
                if (!emitter.Step(deltaTime, time, TexelsPerWave * WorldSize / Mathf.Max(Resolution, 1))) continue;
                emitter.AppendWake(nodes, ships, splats, time, WaveScale);
                // Most of a wake lies behind its ship, so the window leads with the stern side.
                focus += emitter.HullCentre - emitter.Direction * (0.2f * WorldSize);
                emitters++;
            }

            bool input = splats.Count > 0 || ships.Count > 0;
            idleTime = input ? 0 : idleTime + deltaTime;
            if (idleTime > IdleSeconds || (!hasOrigin && !input))
            {
                // Nothing left to show. Dropping the window also drops the stale history with it.
                active = hasOrigin = false;
                return;
            }
            EnsureResources();

            if (emitters > 0)
            {
                focus /= emitters;
                // A turning ship swings its stern side round; ease the window after it instead of snapping.
                if (!hasOrigin || (focus - centre).sqrMagnitude > 0.09f * WorldSize * WorldSize) centre = focus;
                else centre = Vector2.Lerp(centre, focus, 1 - Mathf.Exp(-deltaTime / 1.5f));
            }
            float texel = WorldSize / Resolution;
            // Whole-texel steps let the history be copied instead of resampled (see UpdateWakeFoam).
            var nextOrigin = new Vector2(
                Mathf.Round((centre.x - 0.5f * WorldSize) / texel) * texel,
                Mathf.Round((centre.y - 0.5f * WorldSize) / texel) * texel);
            Vector2 shift = hasOrigin ? (nextOrigin - origin) / texel : new Vector2(4 * Resolution, 4 * Resolution);
            int shiftX = Mathf.Clamp(Mathf.RoundToInt(shift.x), -4 * Resolution, 4 * Resolution);
            int shiftY = Mathf.Clamp(Mathf.RoundToInt(shift.y), -4 * Resolution, 4 * Resolution);

            var kelvin = OceanResources.Load().KelvinWake;
            int shipCount = kelvin != null ? Mathf.Min(ships.Count, 16) : 0;
            int splatCount = Mathf.Min(splats.Count, 256);
            Upload(ref nodeBuffer, nodes, nodes.Count, 48);
            Upload(ref shipBuffer, ships, shipCount, 32);
            Upload(ref splatBuffer, splats, splatCount, 32);

            commands.SetComputeVectorParam(shader, WindowId, new Vector4(nextOrigin.x, nextOrigin.y, texel, Resolution));
            commands.SetComputeIntParam(shader, OceanFFTShaderIDs.CascadeCount, surface.CascadeCount);
            commands.SetComputeIntParam(shader, OceanFFTShaderIDs.Resolution, surface.Resolution);
            commands.SetComputeVectorParam(shader, OceanFFTShaderIDs.DomainSizes, surface.DomainSizes);
            int groups = (Resolution + 7) / 8;

            // Rebuild the waves while a ship makes any, and once more afterwards to leave the texture flat.
            if (shipCount > 0 || wavesWritten)
            {
                commands.SetComputeIntParam(shader, ShipCountId, shipCount);
                commands.SetComputeBufferParam(shader, waveKernel, NodesId, nodeBuffer);
                commands.SetComputeBufferParam(shader, waveKernel, ShipsId, shipBuffer);
                commands.SetComputeTextureParam(shader, waveKernel, KelvinAtlasId, kelvin != null ? kelvin : Texture2D.blackTexture);
                commands.SetComputeVectorParam(shader, KelvinLayoutId,
                    new Vector4(KelvinLengthExtent, KelvinWidthExtent, KelvinFroude.Length, KelvinRows));
                commands.SetComputeVectorArrayParam(shader, KelvinFroudeId, KelvinFroudeVectors);
                commands.SetComputeTextureParam(shader, waveKernel, OceanFFTShaderIDs.Displacement, surface.Displacement);
                commands.SetComputeTextureParam(shader, waveKernel, WaveResultId, waves);
                commands.DispatchCompute(shader, waveKernel, groups, groups, 1);
                commands.GenerateMips(waves);
                wavesWritten = shipCount > 0;
            }

            int writeIndex = 1 - readIndex;
            float perTexel = 4 * Mathf.Max(0, deltaTime) / (texel * texel);
            commands.SetComputeIntParam(shader, SplatCountId, splatCount);
            commands.SetComputeIntParam(shader, ShiftXId, shiftX);
            commands.SetComputeIntParam(shader, ShiftYId, shiftY);
            commands.SetComputeFloatParam(shader, DeltaTimeId, Mathf.Clamp(deltaTime, 0, 0.1f));
            // The spread is an explicit diffusion step; the clamp keeps it stable at any frame rate.
            commands.SetComputeVectorParam(shader, FoamParamsId, new Vector4(Mathf.Max(0, DecayPerSecond),
                Mathf.Max(0, AerationDecayPerSecond), Mathf.Min(0.8f, FoamSpread * perTexel), Mathf.Min(0.8f, AerationSpread * perTexel)));
            commands.SetComputeBufferParam(shader, foamKernel, SplatsId, splatBuffer);
            commands.SetComputeTextureParam(shader, foamKernel, OceanFFTShaderIDs.Displacement, surface.Displacement);
            commands.SetComputeTextureParam(shader, foamKernel, WavesId, waves);
            commands.SetComputeTextureParam(shader, foamKernel, PreviousId, history[readIndex]);
            commands.SetComputeTextureParam(shader, foamKernel, ResultId, history[writeIndex]);
            commands.DispatchCompute(shader, foamKernel, groups, groups, 1);
            commands.GenerateMips(history[writeIndex]);

            readIndex = writeIndex;
            origin = nextOrigin;
            hasOrigin = active = true;
        }

        /// <summary>Supplies the wake textures of this ocean to a surface or volume material.</summary>
        /// <param name="geometryFadeStart">Camera distance at which ship waves start to leave the mesh.</param>
        /// <param name="geometryFadeEnd">Camera distance beyond which they only shade the surface.</param>
        public void Bind(MaterialPropertyBlock properties, float geometryFadeStart = 150, float geometryFadeEnd = 300)
        {
            bool usable = active && history != null && waves != null;
            properties.SetTexture(SurfaceWavesId, usable ? waves : (Texture)Texture2D.blackTexture);
            properties.SetTexture(SurfaceFoamId, usable ? history[readIndex] : (Texture)Texture2D.blackTexture);
            properties.SetVector(OriginSizeId, usable ? new Vector4(origin.x, origin.y, WorldSize, 1f / WorldSize) : Vector4.zero);
            properties.SetVector(SurfaceParamsId, new Vector4(Mathf.Clamp01(RippleDamping),
                geometryFadeStart, Mathf.Max(geometryFadeEnd, geometryFadeStart + 1), 0));
        }

        /// <summary>
        /// Lets a compute kernel that includes OceanWakeCommon.hlsl add the ship waves to its water height.
        /// Accepts a missing system, because the kernel needs its texture bound either way.
        /// </summary>
        internal static void BindCompute(OceanWakeSystem system, CommandBuffer commands, ComputeShader target, int kernel)
        {
            bool usable = system != null && system.active && system.waves != null;
            commands.SetComputeTextureParam(target, kernel, SurfaceWavesId, usable ? system.waves : (Texture)Texture2D.blackTexture);
            commands.SetComputeVectorParam(target, OriginSizeId, usable
                ? new Vector4(system.origin.x, system.origin.y, system.WorldSize, 1f / system.WorldSize) : Vector4.zero);
        }

        void EnsureResources()
        {
            OnValidate();
            if (history != null && allocatedResolution == Resolution && Mathf.Approximately(allocatedSize, WorldSize)) return;
            Release();
            shader = OceanResources.Load().WakeDeposit;
            if (shader == null || !shader.HasKernel("BuildWakeWaves") || !shader.HasKernel("UpdateWakeFoam"))
                throw new InvalidOperationException("OceanDefaults is missing WakeDeposit.compute with BuildWakeWaves / UpdateWakeFoam.");
            waveKernel = shader.FindKernel("BuildWakeWaves");
            foamKernel = shader.FindKernel("UpdateWakeFoam");
            waves = CreateTexture("Wake Waves");
            history = new[] { CreateTexture("Wake Foam 0"), CreateTexture("Wake Foam 1") };
            allocatedResolution = Resolution;
            allocatedSize = WorldSize;
        }

        RenderTexture CreateTexture(string label)
        {
            var texture = new RenderTexture(Resolution, Resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = $"{name} {label}", enableRandomWrite = true, hideFlags = HideFlags.HideAndDontSave,
                // Compute writes mip 0; the chain is regenerated after every update so that a wake seen
                // from far away is filtered instead of shimmering.
                useMipMap = true, autoGenerateMips = false,
                filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp
            };
            texture.Create();
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
            return texture;
        }

        static void Upload<T>(ref ComputeBuffer buffer, List<T> data, int count, int stride) where T : struct
        {
            // A kernel needs every buffer bound, so keep at least one element even when there is no data.
            if (buffer == null || buffer.count < count)
            {
                buffer?.Release();
                buffer = new ComputeBuffer(Mathf.NextPowerOfTwo(Mathf.Max(1, count)), stride, ComputeBufferType.Structured);
            }
            if (count > 0) buffer.SetData(data, 0, 0, count);
        }

        void Release()
        {
            DestroyTexture(waves);
            if (history != null)
                foreach (var texture in history) DestroyTexture(texture);
            waves = null;
            history = null;
            nodeBuffer?.Release();
            shipBuffer?.Release();
            splatBuffer?.Release();
            nodeBuffer = shipBuffer = splatBuffer = null;
            shader = null;
            waveKernel = foamKernel = -1;
            readIndex = allocatedResolution = 0;
            allocatedSize = idleTime = 0;
            hasOrigin = active = wavesWritten = false;
        }

        static void DestroyTexture(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture);
        }
    }

    // The three structs below mirror WakeDeposit.compute field for field.

    [StructLayout(LayoutKind.Sequential)]
    internal struct WakeNode
    {
        public Vector2 Position;
        public Vector2 Backward;
        public float Phase;
        public float Amplitude;
        public float Breaking;
        public float Wavenumber;
        public float Curvature;
        public Vector3 Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WakeShip
    {
        public Vector2 BoundsCentre;
        public float BoundsRadius;
        public float HullLength;
        public float NodeStart;
        public float NodeCount;
        public Vector2 Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WakeSplat
    {
        public Vector2 Center;
        public float Radius;
        public float Foam;
        public float Aeration;
        public float Turbulence;
        public Vector2 Padding;

        public WakeSplat(Vector3 position, float radius, float foam, float aeration, float turbulence)
        {
            Center = new Vector2(position.x, position.z);
            Radius = radius;
            Foam = foam;
            Aeration = aeration;
            Turbulence = turbulence;
            Padding = Vector2.zero;
        }
    }
}
