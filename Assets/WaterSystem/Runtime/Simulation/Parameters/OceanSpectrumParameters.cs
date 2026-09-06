using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>Resolved CPU/GPU contract. Reused each frame; not a second independently editable settings source.</summary>
    public sealed class OceanSpectrumParameters
    {
        public int Resolution { get; private set; }
        public int CascadeCount { get; private set; }
        public int StageCount { get; private set; }
        public int Seed { get; private set; }
        public float InverseResolution => 1f / Resolution;
        public float InverseTransformNormalization => 1f / (Resolution * Resolution);
        public float WindSpeed { get; private set; }
        public Vector2 WindDirection { get; private set; }
        public float Turbulence { get; private set; }
        public float Gravity { get; private set; }
        public float PeakAngularFrequency { get; private set; }
        public float WindLengthScale { get; private set; }
        public float LoopAngularFrequency { get; private set; }
        public Vector4 DomainSizes { get; private set; }
        public Vector4 HeightScales { get; private set; }
        // Per cascade: (L, 1/L, L/N, 2PI/L).
        public readonly Vector4[] CascadeGeometry = new Vector4[OceanSettings.MaxCascades];
        // Per cascade: (minimum radial k, maximum radial k, vertical weight, turbulence).
        public readonly Vector4[] CascadeSpectrum = new Vector4[OceanSettings.MaxCascades];
        public int InitialSpectrumHash { get; private set; }
        // H0 + 5 complex fields + displacement/normal + two R16 foam histories + butterfly LUT.
        public long EstimatedTextureBytes => (long)Resolution * Resolution * CascadeCount * 76 + (long)Resolution * StageCount * 16;

        public void UpdateFrom(OceanSettings settings)
        {
            settings.Validate();
            Resolution = (int)settings.fftWaveQuality;
            CascadeCount = settings.fftWaveCascades;
            StageCount = 0;
            for (int n = Resolution; n > 1; n >>= 1) StageCount++;
            Seed = settings.randomSeed;
            settings.ResolveWind(out float speed, out var direction, out float turbulence);
            WindSpeed = speed;
            WindDirection = direction;
            Turbulence = turbulence;
            Gravity = settings.gravity;
            // Zero wind is explicitly calm; the shader must emit zero spectrum instead of dividing by U.
            PeakAngularFrequency = speed > 1e-5f ? settings.peakFrequencyFactor * Gravity / speed : 0;
            WindLengthScale = speed * speed / Gravity;
            LoopAngularFrequency = settings.loopPeriod > 0 ? 2 * Mathf.PI / settings.loopPeriod : 0;
            var domains = Vector4.zero;
            var weights = Vector4.zero;
            for (int i = 0; i < OceanSettings.MaxCascades; i++)
            {
                if (i >= CascadeCount) { CascadeGeometry[i] = CascadeSpectrum[i] = Vector4.zero; continue; }
                float length = settings.wavesDomainSizes[i] * settings.waveAreaScale;
                domains[i] = length;
                weights[i] = settings.wavesDomainHeightScales[i] * settings.heightScale;
                CascadeGeometry[i] = new Vector4(length, 1f / length, length / Resolution, 2 * Mathf.PI / length);
                float lower = settings.separateCascadeBands && i + 1 < CascadeCount
                    ? Mathf.PI * Resolution / (settings.wavesDomainSizes[i + 1] * settings.waveAreaScale) : 0;
                // A radial cutoff at axis Nyquist deliberately omits the corners of the square spectrum.
                float upper = settings.separateCascadeBands ? Mathf.PI * Resolution / length : Mathf.Sqrt(2) * Mathf.PI * Resolution / length;
                CascadeSpectrum[i] = new Vector4(lower, upper, weights[i], Mathf.Max(turbulence, settings.cascadeTurbulenceFloor[i]));
            }
            DomainSizes = domains;
            HeightScales = weights;
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Seed;
                Add(Resolution); Add(CascadeCount); Add(WindSpeed); Add(WindDirection.x); Add(WindDirection.y);
                Add(Gravity); Add(PeakAngularFrequency); Add(settings.spectrumAmplitude); Add(settings.spectrumAlpha);
                Add(settings.spectrumBeta); Add(settings.shortWaveDamping);
                for (int i = 0; i < CascadeCount; i++)
                {
                    Add(CascadeGeometry[i].x); Add(CascadeSpectrum[i].x); Add(CascadeSpectrum[i].y); Add(CascadeSpectrum[i].w);
                }
                InitialSpectrumHash = hash;
                void Add(float value) { hash = hash * 31 + value.GetHashCode(); }
            }
        }
    }
}
