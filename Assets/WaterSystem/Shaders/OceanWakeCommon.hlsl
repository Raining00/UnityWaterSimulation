#ifndef WATERSYSTEM_OCEAN_WAKE_INCLUDED
#define WATERSYSTEM_OCEAN_WAKE_INCLUDED

// World-space wake window written by OceanWakeSystem and shared by every camera. It is addressed
// with the UNDISPLACED ocean coordinate (baseXZ in the surface shaders), like the FFT textures.
//   _OceanWakeWaves  x: wave height in metres, yz: its world-space gradient, w: breaking crests
//   _OceanWakeFoam   x: foam coverage, y: aeration (bubbles carried below the surface)
Texture2D _OceanWakeWaves; SamplerState sampler_OceanWakeWaves;
Texture2D _OceanWakeFoam; SamplerState sampler_OceanWakeFoam;
// xy: world XZ of the window's minimum corner, z: size in metres (0 while no wake exists), w: 1 / size.
float4 _OceanWakeOriginSize;

float2 OceanWakeUV(float2 baseXZ, out float weight)
{
    float2 uv = (baseXZ - _OceanWakeOriginSize.xy) * _OceanWakeOriginSize.w;
    float2 border = min(uv, 1.0 - uv);
    // Fade towards the border, so history that leaves the window never ends in a visible square.
    weight = _OceanWakeOriginSize.z > 0.0 ? smoothstep(0.0, 0.04, min(border.x, border.y)) : 0.0;
    return uv;
}

// Unfiltered lookup for vertex displacement and height queries. x: height, yz: gradient.
float3 OceanWakeSurface(float2 baseXZ)
{
    float weight;
    float2 uv = OceanWakeUV(baseXZ, weight);
    if (weight <= 0.0) return 0.0;
    return _OceanWakeWaves.SampleLevel(sampler_OceanWakeWaves, uv, 0).xyz * weight;
}

float OceanWakeHeight(float2 baseXZ) { return OceanWakeSurface(baseXZ).x; }

struct OceanWake
{
    float2 gradient;   // d(height)/dx, d(height)/dz
    float foam;
    float aeration;
};

// baseDx/baseDy are the screen-space derivatives of baseXZ. They must be taken outside of any
// branch; the explicit gradients then keep these lookups legal inside one.
OceanWake SampleOceanWake(float2 baseXZ, float2 baseDx, float2 baseDy)
{
    OceanWake wake;
    wake.gradient = 0;
    wake.foam = 0;
    wake.aeration = 0;
    float weight;
    float2 uv = OceanWakeUV(baseXZ, weight);
    if (weight <= 0.0) return wake;
    float2 dx = baseDx * _OceanWakeOriginSize.w, dy = baseDy * _OceanWakeOriginSize.w;
    float4 waves = _OceanWakeWaves.SampleGrad(sampler_OceanWakeWaves, uv, dx, dy);
    float4 foam = _OceanWakeFoam.SampleGrad(sampler_OceanWakeFoam, uv, dx, dy);
    wake.gradient = waves.yz * weight;
    wake.foam = foam.x * weight;
    wake.aeration = foam.y * weight;
    return wake;
}
#endif
