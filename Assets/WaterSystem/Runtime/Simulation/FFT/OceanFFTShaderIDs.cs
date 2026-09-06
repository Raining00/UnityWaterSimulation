using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>Single naming contract for the future compute shader and water material.</summary>
    public static class OceanFFTShaderIDs
    {
        public static readonly int Resolution = Shader.PropertyToID("_FFTResolution");
        public static readonly int CascadeCount = Shader.PropertyToID("_FFTCascadeCount");
        public static readonly int StageCount = Shader.PropertyToID("_FFTStageCount");
        public static readonly int Stage = Shader.PropertyToID("_FFTStage");
        public static readonly int Axis = Shader.PropertyToID("_FFTAxis");
        public static readonly int Normalization = Shader.PropertyToID("_FFTNormalization");
        public static readonly int Seed = Shader.PropertyToID("_SpectrumSeed");
        public static readonly int Time = Shader.PropertyToID("_OceanSimulationTime");
        public static readonly int DeltaTime = Shader.PropertyToID("_OceanSimulationDeltaTime");
        public static readonly int Wind = Shader.PropertyToID("_OceanWind");
        public static readonly int Spectrum = Shader.PropertyToID("_OceanSpectrum");
        public static readonly int Dispersion = Shader.PropertyToID("_OceanDispersion");
        public static readonly int Output = Shader.PropertyToID("_OceanOutput");
        public static readonly int CascadeGeometry = Shader.PropertyToID("_OceanCascadeGeometry");
        public static readonly int CascadeSpectrum = Shader.PropertyToID("_OceanCascadeSpectrum");
        public static readonly int Initial = Shader.PropertyToID("_SpectrumInitial");
        public static readonly int X = Shader.PropertyToID("_SpectrumX");
        public static readonly int Y = Shader.PropertyToID("_SpectrumY");
        public static readonly int Z = Shader.PropertyToID("_SpectrumZ");
        public static readonly int Butterfly = Shader.PropertyToID("_FFTButterfly");
        public static readonly int Input = Shader.PropertyToID("_FFTInput");
        public static readonly int Result = Shader.PropertyToID("_FFTResult");
        public static readonly int Displacement = Shader.PropertyToID("_OceanDisplacement");
        public static readonly int Normal = Shader.PropertyToID("_OceanNormals");
        public static readonly int DomainSizes = Shader.PropertyToID("_OceanDomainSizes");
        public static readonly int Ready = Shader.PropertyToID("_OceanSimulationReady");
        public static readonly int FoamParameters = Shader.PropertyToID("_OceanFoamParameters");
        public static readonly int FoamPrevious = Shader.PropertyToID("_FoamPrevious");
        public static readonly int FoamResult = Shader.PropertyToID("_FoamResult");
        public static readonly int Foam = Shader.PropertyToID("_OceanFoam");
    }
}
