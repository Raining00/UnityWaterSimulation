using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace WaterSystem.Ocean
{
    [DisallowMultipleComponent, RequireComponent(typeof(OceanSettings))]
    [AddComponentMenu("Water System/FFT Simulation")]
    public sealed class FFTCompute : OceanSimulationProvider
    {
        [SerializeField,HideInInspector] OceanSettings oceanSettings;
        [SerializeField,HideInInspector] ComputeShader computeShader;
        public OceanSettings Settings => oceanSettings != null ? oceanSettings : GetComponent<OceanSettings>();
        public OceanTextures Textures { get; } = new OceanTextures();
        public OceanSpectrumParameters Parameters { get; } = new OceanSpectrumParameters();
        public string Status { get; private set; } = "Not initialized";
        public bool HasOutput { get; private set; }
        public double SimulationTime { get; private set; }
        public int LastDispatchCount { get; private set; }
        public override Vector2 MaximumDisplacement => oceanSettings != null ? oceanSettings.maximumDisplacement : Vector2.zero;

        OceanRenderer owner;
        ComputeShader activeShader;
        Kernel initialize, evolve, inverse, displacement, normals, clearFoam, buildFoam;
        int initialHash, lastFrame = -1;
        bool initialized, spectrumDirty = true, rebuildRequested;
        double lastAbsoluteTime = double.NaN;
        float previousOffset;
        bool foamDirty;

        public void Configure(OceanSettings settings, ComputeShader shader)
        {
            oceanSettings = settings;
            computeShader = shader;
            RequestReinitialize();
        }

        readonly struct Kernel
        {
            public readonly int Index;
            public readonly uint X, Y, Z;
            public Kernel(ComputeShader shader, string name)
            {
                if (!shader.HasKernel(name)) throw new InvalidOperationException("Ocean compute shader missing kernel: " + name);
                Index = shader.FindKernel(name);
                shader.GetKernelThreadGroupSizes(Index, out uint x, out uint y, out uint z);
                X = x; Y = y; Z = z;
                if (x == 0 || y == 0 || z == 0) throw new InvalidOperationException("Invalid thread group: " + name);
            }
        }

        public override void Initialize(in OceanSimulationContext context)
        {
            Release();
            owner = context.Ocean;
            if (oceanSettings == null) oceanSettings = GetComponent<OceanSettings>();
            if (oceanSettings == null) throw new InvalidOperationException("FFTCompute requires OceanSettings.");
            computeShader=OceanResources.Load().FFT;
            if(computeShader==null)throw new InvalidOperationException("OceanDefaults is missing OceanFFT.compute.");
            Parameters.UpdateFrom(oceanSettings);
            SimulationTime = previousOffset = oceanSettings.timeOffset;
            initialized = true;
            Status = "Prepared; waiting for first frame";
        }

        [ContextMenu("Reinitialize FFT")]
        public void RequestReinitialize()
        {
            rebuildRequested = true;
            if (owner != null) owner.RequestRebuild(); // Also clears the renderer's exception guard.
        }
        void OnValidate() => rebuildRequested = true;

        public override void RecordSimulation(CommandBuffer commands, in OceanSimulationFrame frame)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (!initialized) throw new InvalidOperationException("Initialize FFTCompute before recording simulation.");
            if (lastFrame == frame.FrameIndex) return;
            lastFrame = frame.FrameIndex;
            LastDispatchCount = 0;
            Parameters.UpdateFrom(oceanSettings);
            // Absolute-time differences avoid frame-rate-dependent slowing caused by the renderer's clamped DeltaTime.
            // Integrating the current scale avoids a phase jump when timeScale changes. Zero pauses the simulation.
            double elapsed = double.IsNaN(lastAbsoluteTime) ? 0 : Math.Max(0, frame.Time - lastAbsoluteTime);
            lastAbsoluteTime = frame.Time;
            double delta = elapsed * oceanSettings.timeScale;
            SimulationTime += delta + oceanSettings.timeOffset - previousOffset;
            previousOffset = oceanSettings.timeOffset;
            if (computeShader == null)
            {
                if (activeShader != null || Textures.IsAllocated) ReleaseGPU();
                Status = "Waiting for Compute Shader (no FFT dispatch or output)";
                return;
            }
            if (activeShader != computeShader || rebuildRequested || !Textures.IsAllocated ||
                Textures.Resolution != Parameters.Resolution || Textures.CascadeCount != Parameters.CascadeCount)
            {
                ReleaseGPU();
                activeShader = computeShader;
                initialize = new Kernel(activeShader, "InitializeSpectrum");
                evolve = new Kernel(activeShader, "UpdateSpectrum");
                inverse = new Kernel(activeShader, "InverseFFT");
                displacement = new Kernel(activeShader, "BuildDisplacement");
                normals = new Kernel(activeShader, "BuildNormals");
                clearFoam = new Kernel(activeShader, "ClearFoam");
                buildFoam = new Kernel(activeShader, "BuildFoam");
                Textures.Allocate(Parameters);
                foamDirty = true;
                rebuildRequested = false;
            }
            RecordConstants(commands, (float)delta);
            if (spectrumDirty || initialHash != Parameters.InitialSpectrumHash)
            {
                commands.SetComputeTextureParam(activeShader, initialize.Index, OceanFFTShaderIDs.Initial, Textures.SpectrumInitial);
                Dispatch(commands, initialize);
                initialHash = Parameters.InitialSpectrumHash;
                spectrumDirty = false;
            }
            commands.SetComputeTextureParam(activeShader, evolve.Index, OceanFFTShaderIDs.Initial, Textures.SpectrumInitial);
            BindComponents(commands, evolve);
            Dispatch(commands, evolve);
            InverseTransform(commands, Textures.SpectrumX);
            InverseTransform(commands, Textures.SpectrumY);
            InverseTransform(commands, Textures.SpectrumZ);
            BindComponents(commands, displacement);
            commands.SetComputeTextureParam(activeShader, displacement.Index, OceanFFTShaderIDs.Displacement, Textures.FFTDisplacement);
            Dispatch(commands, displacement);
            commands.SetComputeTextureParam(activeShader, normals.Index, OceanFFTShaderIDs.Displacement, Textures.FFTDisplacement);
            commands.SetComputeTextureParam(activeShader, normals.Index, OceanFFTShaderIDs.Normal, Textures.FFTNormal);
            Dispatch(commands, normals);
            if (foamDirty)
            {
                commands.SetComputeTextureParam(activeShader, clearFoam.Index, OceanFFTShaderIDs.FoamResult, Textures.FoamPrevious);
                Dispatch(commands, clearFoam);
                commands.SetComputeTextureParam(activeShader, clearFoam.Index, OceanFFTShaderIDs.FoamResult, Textures.FoamCurrent);
                Dispatch(commands, clearFoam);
                foamDirty = false;
            }
            commands.SetComputeTextureParam(activeShader, buildFoam.Index, OceanFFTShaderIDs.Normal, Textures.FFTNormal);
            commands.SetComputeTextureParam(activeShader, buildFoam.Index, OceanFFTShaderIDs.FoamPrevious, Textures.FoamPrevious);
            commands.SetComputeTextureParam(activeShader, buildFoam.Index, OceanFFTShaderIDs.FoamResult, Textures.FoamCurrent);
            Dispatch(commands, buildFoam);
            Textures.SwapFoam();
            // OceanRenderer immediately executes the buffer before BindResources. This flag is not a GPU fence.
            HasOutput = true;
            Status = "FFT commands recorded";
        }

        void RecordConstants(CommandBuffer commands, float delta)
        {
            var p = Parameters;
            commands.SetComputeIntParam(activeShader, OceanFFTShaderIDs.Resolution, p.Resolution);
            commands.SetComputeIntParam(activeShader, OceanFFTShaderIDs.CascadeCount, p.CascadeCount);
            commands.SetComputeIntParam(activeShader, OceanFFTShaderIDs.StageCount, p.StageCount);
            commands.SetComputeIntParam(activeShader, OceanFFTShaderIDs.Seed, p.Seed);
            commands.SetComputeFloatParam(activeShader, OceanFFTShaderIDs.Normalization, p.InverseTransformNormalization);
            double time = oceanSettings.loopPeriod > 0 ? SimulationTime % oceanSettings.loopPeriod : SimulationTime;
            commands.SetComputeFloatParam(activeShader, OceanFFTShaderIDs.Time, (float)time);
            commands.SetComputeFloatParam(activeShader, OceanFFTShaderIDs.DeltaTime, delta);
            commands.SetComputeVectorParam(activeShader, OceanFFTShaderIDs.Wind, new Vector4(p.WindDirection.x, p.WindDirection.y, p.WindSpeed, p.Turbulence));
            commands.SetComputeVectorParam(activeShader, OceanFFTShaderIDs.Spectrum, new Vector4(oceanSettings.spectrumAmplitude, oceanSettings.spectrumAlpha, oceanSettings.spectrumBeta, p.PeakAngularFrequency));
            commands.SetComputeVectorParam(activeShader, OceanFFTShaderIDs.Dispersion, new Vector4(p.Gravity, p.LoopAngularFrequency, oceanSettings.shortWaveDamping, 0));
            commands.SetComputeVectorParam(activeShader, OceanFFTShaderIDs.Output, new Vector4(oceanSettings.choppiness, oceanSettings.normalStrength, 0, 0));
            commands.SetComputeVectorParam(activeShader, OceanFFTShaderIDs.FoamParameters, new Vector4(oceanSettings.foamThreshold, oceanSettings.foamSoftness, oceanSettings.foamDecay, oceanSettings.foamBuildRate));
            commands.SetComputeVectorArrayParam(activeShader, OceanFFTShaderIDs.CascadeGeometry, p.CascadeGeometry);
            commands.SetComputeVectorArrayParam(activeShader, OceanFFTShaderIDs.CascadeSpectrum, p.CascadeSpectrum);
        }

        void BindComponents(CommandBuffer commands, Kernel kernel)
        {
            commands.SetComputeTextureParam(activeShader, kernel.Index, OceanFFTShaderIDs.X, Textures.SpectrumX);
            commands.SetComputeTextureParam(activeShader, kernel.Index, OceanFFTShaderIDs.Y, Textures.SpectrumY);
            commands.SetComputeTextureParam(activeShader, kernel.Index, OceanFFTShaderIDs.Z, Textures.SpectrumZ);
        }

        void InverseTransform(CommandBuffer commands, RenderTexture component)
        {
            RenderTexture input = component;
            commands.SetComputeTextureParam(activeShader, inverse.Index, OceanFFTShaderIDs.Butterfly, Textures.ButterflyLookup);
            for (int axis = 0; axis < 2; axis++)
            {
                commands.SetComputeIntParam(activeShader, OceanFFTShaderIDs.Axis, axis);
                for (int stage = 0; stage < Parameters.StageCount; stage++)
                {
                    var output = input == Textures.Ping ? Textures.Pong : Textures.Ping;
                    commands.SetComputeIntParam(activeShader, OceanFFTShaderIDs.Stage, stage);
                    commands.SetComputeTextureParam(activeShader, inverse.Index, OceanFFTShaderIDs.Input, input);
                    commands.SetComputeTextureParam(activeShader, inverse.Index, OceanFFTShaderIDs.Result, output);
                    Dispatch(commands, inverse);
                    input = output;
                }
            }
            // Preserve all three spatial components while reusing the same two scratch arrays.
            commands.CopyTexture(input, component);
        }

        void Dispatch(CommandBuffer commands, Kernel kernel)
        {
            commands.DispatchCompute(activeShader, kernel.Index,
                (Parameters.Resolution + (int)kernel.X - 1) / (int)kernel.X,
                (Parameters.Resolution + (int)kernel.Y - 1) / (int)kernel.Y,
                (Parameters.CascadeCount + (int)kernel.Z - 1) / (int)kernel.Z);
            LastDispatchCount++;
        }

        public override void BindResources(MaterialPropertyBlock properties, Camera camera)
        {
            properties.SetInt(OceanFFTShaderIDs.Ready, HasOutput ? 1 : 0);
            properties.SetInt(OceanFFTShaderIDs.CascadeCount, Parameters.CascadeCount);
            properties.SetInt(OceanFFTShaderIDs.Resolution, Parameters.Resolution);
            properties.SetVector(OceanFFTShaderIDs.DomainSizes, Parameters.DomainSizes);
            properties.SetFloat(OceanFFTShaderIDs.Time, (float)(oceanSettings != null && oceanSettings.loopPeriod > 0 ? SimulationTime % oceanSettings.loopPeriod : SimulationTime));
            if (!HasOutput) return;
            properties.SetTexture(OceanFFTShaderIDs.Displacement, Textures.FFTDisplacement);
            properties.SetTexture(OceanFFTShaderIDs.Normal, Textures.FFTNormal);
            properties.SetTexture(OceanFFTShaderIDs.Foam, Textures.FoamPrevious);
        }

        public override bool TryGetSurfaceQueryResources(out OceanSurfaceQueryResources resources)
        {
            if (!HasOutput || !Textures.IsAllocated)
            {
                resources = default;
                return false;
            }

            resources = new OceanSurfaceQueryResources(
                Textures.FFTDisplacement,
                Textures.FFTNormal,
                Parameters.DomainSizes,
                Parameters.CascadeCount,
                Parameters.Resolution,
                owner != null ? owner.transform.position.y : transform.position.y,
                SimulationTime);
            return resources.IsValid;
        }

        void ReleaseGPU()
        {
            Textures.Dispose();
            activeShader = null;
            HasOutput = false;
            spectrumDirty = true;
        }
        public override void Release()
        {
            ReleaseGPU();
            owner = null;
            initialized = false;
            lastFrame = -1;
            lastAbsoluteTime = double.NaN;
            SimulationTime = 0;
            LastDispatchCount = 0;
            Status = "Released";
        }
        void OnDisable()
        {
            if (owner != null) owner.RequestRebuild();
            Release();
        }
        void OnDestroy() => Release();
    }
}
