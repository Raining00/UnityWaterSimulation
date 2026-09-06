using System;
using Unity.Mathematics;
using UnityEngine;

namespace WaterSystem.Ocean
{
    [DisallowMultipleComponent, AddComponentMenu("Water System/Ocean Spectrum Settings")]
    public sealed class OceanSettings : MonoBehaviour
    {
        public const int MaxCascades = 4;
        public const int MinResolution = 32;
        public const int MaxResolution = 512;
        static readonly int[] DefaultDomains = { 5, 20, 100, 600 };
        static readonly float[] DefaultHeights = { 0.5f, 0.5f, 0.6f, 0.9f };
        static readonly float[] DefaultTurbulence = { 0.5f, 0.25f, 0, 0 };

        // Existing serialized field names are retained to preserve scene/prefab data.
        [Header("Wind (metres/second, degrees; 0 degrees = world +X)")]
        [Min(0)] public float windSpeed = 5;
        public float windRotation;
        [Range(0, 1)] public float windTurbulence = 0.5f;
        public WindZone windZone;
        [Min(0)] public float windZoneSpeedMultiplier = 1;
        [Min(0)] public float windZoneTurbulenceMultiplier = 1;

        [Header("FFT layout (independent of geometry resolution)")]
        public uint fftWaveQuality = 128;
        [Range(1, MaxCascades)] public int fftWaveCascades = 4;
        [Min(0.01f)] public float waveAreaScale = 1;
        [Tooltip("Small to large, in metres. Effective domain = this value * waveAreaScale.")]
        public int[] wavesDomainSizes = { 5, 20, 100, 600 };
        public float[] wavesDomainHeightScales = { 0.5f, 0.5f, 0.6f, 0.9f };
        [Tooltip("Optional radial band separation at the next larger cascade's axis Nyquist wavenumber.")]
        public bool separateCascadeBands;

        [Header("Deep-water Pierson-Moskowitz spectrum")]
        [Min(0.01f)] public float gravity = 9.81f;
        public int randomSeed = 12345;
        [Min(0)] public float spectrumAmplitude = 1;
        [Min(0)] public float spectrumAlpha = 0.0081f;
        [Min(0)] public float spectrumBeta = 1.291f;
        [Min(0.01f)] public float peakFrequencyFactor = 0.87f;
        [Tooltip("Gaussian short-wave damping length in metres; zero disables damping.")]
        [Min(0)] public float shortWaveDamping = 0.01f;
        [Tooltip("Minimum directional turbulence per cascade, as in KWS2's small-domain treatment.")]
        public float[] cascadeTurbulenceFloor = { 0.5f, 0.25f, 0, 0 };

        [Header("Time and output")]
        [Min(0)] public float timeScale = 1;
        [Min(0)] public float timeOffset;
        [Tooltip("Zero: non-looping. Positive: quantize angular frequency to multiples of 2 PI / loopPeriod.")]
        [Min(0)] public float loopPeriod;
        [Min(0)] public float heightScale = 1;
        [Min(-5)] public float choppiness = 1;
        [Min(0)] public float normalStrength = 1;
        [Tooltip("Conservative absolute displacement bounds in metres (horizontal X/Z, vertical Y). Not an RMS wave height.")]
        public Vector2 maximumDisplacement = new Vector2(5, 15);

        [Header("Whitecaps (horizontal Jacobian compression)")]
        [Range(0, 1.5f)] public float foamThreshold = 0.85f;
        [Min(0.01f)] public float foamSoftness = 0.4f;
        [Min(0)] public float foamDecay = 0.5f;
        [Min(0)] public float foamBuildRate = 3;

        public void Validate()
        {
            fftWaveQuality = (uint)Mathf.ClosestPowerOfTwo((int)Math.Min(MaxResolution, Math.Max(MinResolution, fftWaveQuality)));
            fftWaveCascades = Mathf.Clamp(fftWaveCascades, 1, MaxCascades);
            windSpeed = NonNegative(windSpeed, 5);
            windRotation = Mathf.Repeat(Finite(windRotation, 0), 360);
            windTurbulence = Mathf.Clamp01(Finite(windTurbulence, 0.5f));
            windZoneSpeedMultiplier = NonNegative(windZoneSpeedMultiplier, 1);
            windZoneTurbulenceMultiplier = NonNegative(windZoneTurbulenceMultiplier, 1);
            waveAreaScale = Mathf.Clamp(Finite(waveAreaScale, 1), 0.01f, 1000);
            gravity = Mathf.Max(0.01f, Finite(gravity, 9.81f));
            spectrumAmplitude = NonNegative(spectrumAmplitude, 1);
            spectrumAlpha = NonNegative(spectrumAlpha, 0.0081f);
            spectrumBeta = NonNegative(spectrumBeta, 1.291f);
            peakFrequencyFactor = Mathf.Max(0.01f, Finite(peakFrequencyFactor, 0.87f));
            shortWaveDamping = NonNegative(shortWaveDamping, 0.01f);
            timeScale = NonNegative(timeScale, 1);
            timeOffset = NonNegative(timeOffset, 0);
            loopPeriod = NonNegative(loopPeriod, 0);
            heightScale = NonNegative(heightScale, 1);
            choppiness = Mathf.Clamp(choppiness, -3, 3);
            normalStrength = NonNegative(normalStrength, 1);
            maximumDisplacement = new Vector2(NonNegative(maximumDisplacement.x, 5), NonNegative(maximumDisplacement.y, 15));
            foamThreshold = Mathf.Clamp(Finite(foamThreshold, 0.85f), 0, 1.5f);
            foamSoftness = Mathf.Max(0.01f, Finite(foamSoftness, 0.4f));
            foamDecay = NonNegative(foamDecay, 0.5f);
            foamBuildRate = NonNegative(foamBuildRate, 3);
            Resize(ref wavesDomainSizes, DefaultDomains);
            Resize(ref wavesDomainHeightScales, DefaultHeights);
            Resize(ref cascadeTurbulenceFloor, DefaultTurbulence);
            for (int i = 0; i < MaxCascades; i++)
            {
                int minimum = i == 0 ? 1 : wavesDomainSizes[i - 1] + 1;
                wavesDomainSizes[i] = Mathf.Clamp(wavesDomainSizes[i], minimum, 1000000 - MaxCascades + i);
                wavesDomainHeightScales[i] = NonNegative(wavesDomainHeightScales[i], 1);
                cascadeTurbulenceFloor[i] = Mathf.Clamp01(Finite(cascadeTurbulenceFloor[i], 0));
            }
        }

        public void ResolveWind(out float speed, out Vector2 direction, out float turbulence)
        {
            float angle = windRotation * Mathf.Deg2Rad;
            direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            speed = windSpeed;
            turbulence = windTurbulence;
            // WindZone force is not a physical m/s quantity: the multiplier explicitly defines the conversion.
            // Spherical zones and pulse settings are not a uniform ocean wind, so they use the manual values.
            if (windZone == null || !windZone.gameObject.activeInHierarchy || windZone.mode != WindZoneMode.Directional) return;
            var forward = windZone.transform.forward;
            var horizontal = new Vector2(forward.x, forward.z);
            if (horizontal.sqrMagnitude > 1e-8f) direction = horizontal.normalized;
            speed = NonNegative(windZone.windMain * windZoneSpeedMultiplier, 0);
            turbulence = Mathf.Clamp01(Finite(windZone.windTurbulence * windZoneTurbulenceMultiplier, 0));
        }

        void OnValidate() => Validate();
        static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        static float NonNegative(float value, float fallback) => Mathf.Max(0, Finite(value, fallback));
        static void Resize<T>(ref T[] array, T[] defaults)
        {
            if (array != null && array.Length == MaxCascades) return;
            var replacement = (T[])defaults.Clone();
            if (array != null) Array.Copy(array, replacement, Math.Min(array.Length, MaxCascades));
            array = replacement;
        }
    }
}
