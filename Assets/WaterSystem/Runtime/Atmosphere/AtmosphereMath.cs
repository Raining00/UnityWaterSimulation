using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>CPU evaluation of the same atmosphere model, used to colour the sun light without a GPU readback.</summary>
    public static class AtmosphereMath
    {
        public static float RaySphereNearest(Vector3 origin, Vector3 direction, Vector3 center, float radius)
        {
            Vector3 o = origin - center;
            float b = 2 * Vector3.Dot(direction, o);
            float c = Vector3.Dot(o, o) - radius * radius;
            float delta = b * b - 4 * c;
            if (delta < 0) return -1;
            float sq = Mathf.Sqrt(delta);
            float s0 = (-b - sq) * 0.5f, s1 = (-b + sq) * 0.5f;
            if (s0 < 0 && s1 < 0) return -1;
            if (s0 < 0) return Mathf.Max(0, s1);
            if (s1 < 0) return Mathf.Max(0, s0);
            return Mathf.Max(0, Mathf.Min(s0, s1));
        }
        /// <summary>Extinction (per km) at an altitude in km.</summary>
        public static Vector3 Extinction(AtmosphereSettings a, float altitudeKm)
        {
            float rayleigh = Mathf.Exp(-altitudeKm / a.RayleighScaleHeight) * a.RayleighDensity * 0.001f;
            float mie = Mathf.Exp(-altitudeKm / a.MieScaleHeight) * a.MieDensity * 0.001f * (a.MieScattering + a.MieAbsorption);
            float ozone = Mathf.Clamp01(1 - Mathf.Abs(altitudeKm - a.OzoneLayerCenter) / a.OzoneLayerWidth) * a.OzoneDensity * 0.001f;
            return a.RayleighScattering * rayleigh + Vector3.one * mie + a.OzoneAbsorption * ozone;
        }
        /// <summary>
        /// Sun transmittance seen from a planet radius (km). Directions below the horizon use the horizon value and
        /// fade to zero over about one degree, matching the clamped GPU table.
        /// </summary>
        public static Vector3 SunTransmittance(AtmosphereSettings a, float radiusKm, Vector3 sunDirection)
        {
            float bottom = a.PlanetRadius, top = a.TopRadius;
            radiusKm = Mathf.Clamp(radiusKm, bottom + 0.0005f, top - 0.1f);
            float horizonCos = -Mathf.Sqrt(Mathf.Max(0, 1 - bottom * bottom / (radiusKm * radiusKm)));
            float mu = Mathf.Max(sunDirection.y, horizonCos + 1e-4f);
            Vector3 origin = new Vector3(0, radiusKm, 0);
            Vector3 direction = new Vector3(Mathf.Sqrt(Mathf.Max(0, 1 - mu * mu)), mu, 0);
            float tTop = RaySphereNearest(origin, direction, Vector3.zero, top);
            if (tTop <= 0) return Vector3.one;
            const int steps = 40;
            float dt = tTop / steps;
            Vector3 tau = Vector3.zero;
            for (int i = 0; i < steps; i++)
            {
                Vector3 p = origin + direction * (dt * (i + 0.5f));
                tau += Extinction(a, Mathf.Max(0, p.magnitude - bottom)) * dt;
            }
            float fade = Mathf.Clamp01(1 + (sunDirection.y - horizonCos) / 0.0175f);
            return new Vector3(Mathf.Exp(-tau.x), Mathf.Exp(-tau.y), Mathf.Exp(-tau.z)) * fade;
        }
        public static float Halton(int index, int radix)
        {
            float fraction = 1, result = 0;
            while (index > 0)
            {
                fraction /= radix;
                result += fraction * (index % radix);
                index /= radix;
            }
            return result;
        }
    }
}
