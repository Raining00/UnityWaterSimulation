using System;
using UnityEngine;

namespace WaterSystem.Ocean
{
    public static class OceanButterfly
    {
        /// <summary>Radix-2 DIT inverse FFT: LUT[x=output, y=stage] = (weight.real, weight.imag, inputA, inputB).</summary>
        public static Color[] BuildLookup(int resolution, out int stages)
        {
            if (resolution < 2 || !Mathf.IsPowerOfTwo(resolution)) throw new ArgumentException("FFT resolution must be a power of two.");
            stages = 0;
            for (int n = resolution; n > 1; n >>= 1) stages++;
            var entries = new Color[resolution * stages];
            for (int stage = 0; stage < stages; stage++)
            {
                int span = 1 << (stage + 1), half = span >> 1;
                for (int output = 0; output < resolution; output++)
                {
                    int offset = output % half;
                    int a = output / span * span + offset;
                    int b = a + half;
                    float angle = 2 * Mathf.PI * offset / span;
                    float sign = output % span < half ? 1 : -1;
                    if (stage == 0) { a = ReverseBits(a, stages); b = ReverseBits(b, stages); }
                    entries[stage * resolution + output] = new Color(sign * Mathf.Cos(angle), sign * Mathf.Sin(angle), a, b);
                }
            }
            return entries;
        }

        static int ReverseBits(int value, int count)
        {
            int result = 0;
            for (int i = 0; i < count; i++) { result = (result << 1) | (value & 1); value >>= 1; }
            return result;
        }
    }
}
