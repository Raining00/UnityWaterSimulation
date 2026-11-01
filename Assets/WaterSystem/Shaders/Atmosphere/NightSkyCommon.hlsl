#ifndef WATERSYSTEM_NIGHT_SKY_INCLUDED
#define WATERSYSTEM_NIGHT_SKY_INCLUDED

#include "AtmosphereCommon.hlsl"

TEXTURE2D(_NightSkyGalaxyTexture); SAMPLER(sampler_NightSkyGalaxyTexture);
float4 _NightSkyParams;       // enabled, star radiance, galaxy radiance, solar visibility
float4 _NightSkyStars;        // density, angular size multiplier, twinkle amount, time seconds
float4 _NightSkyTint;         // RGB linear tint * atmosphere exposure; w horizon fade sine
float4 _NightSkyTextureSize;  // width, height, max mip, unused
float4x4 _NightSkyWorldToSky;

float3 NightStarHash(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}

// Cubic cells avoid lat/long poles. Star centres remain well inside their cells, including face
// edges, so no neighbour search is required. The solid-angle Jacobian corrects the cell density.
float3 NightStars(float3 d, float angularFootprint)
{
    float3 ad = abs(d);
    float face;
    float2 p;
    if (ad.z >= max(ad.x, ad.y)) { face = d.z >= 0 ? 0 : 1; p = d.xy / ad.z; }
    else if (ad.x >= ad.y) { face = d.x >= 0 ? 2 : 3; p = d.zy / ad.x; }
    else { face = d.y >= 0 ? 4 : 5; p = d.xz / ad.y; }
    const float grid = 120;
    float2 cell = floor((p * 0.5 + 0.5) * grid);
    float3 seed = NightStarHash(float3(cell, face + 17));
    float2 cellCentre = ((cell + 0.5) / grid) * 2 - 1;
    float probability = _NightSkyStars.x * pow(1 + dot(cellCentre, cellCentre), -1.5);
    if (seed.z >= probability) return 0;
    float2 starPoint = ((cell + 0.2 + seed.xy * 0.6) / grid) * 2 - 1;
    float3 centre;
    if (face < 2) centre = float3(starPoint.xy, face < 1 ? 1 : -1);
    else if (face < 4) centre = float3(face < 3 ? 1 : -1, starPoint.y, starPoint.x);
    else centre = float3(starPoint.x, face < 5 ? 1 : -1, starPoint.y);
    centre = normalize(centre);
    float3 variation = NightStarHash(float3(cell + 71.3, face + 43));
    float sigma = 0.0003 * _NightSkyStars.y * lerp(0.7, 1.3, variation.x);
    float sigmaAA = sqrt(sigma * sigma + angularFootprint * angularFootprint * 0.16);
    float distanceSquared = dot(d - centre, d - centre);
    float profile = exp(-distanceSquared / max(2 * sigmaAA * sigmaAA, 1e-10));
    // Preserve approximate integrated energy when a star is smaller than a pixel.
    profile *= sigma * sigma / max(sigmaAA * sigmaAA, 1e-10);
    float magnitude = 0.25 + pow(variation.y, 8) * 8;
    float twinkle = 1 + _NightSkyStars.z * sin(_NightSkyStars.w * (1.3 + variation.z * 2) + seed.x * 50);
    float3 temperature = lerp(float3(1, 0.76, 0.55), float3(0.65, 0.8, 1), variation.z);
    return profile * magnitude * twinkle * temperature * _NightSkyParams.y;
}

float3 NightSkyRadiance(float3 worldDir, float angularFootprint, bool includeStars)
{
    if (_NightSkyParams.x < 0.5 || _NightSkyParams.w <= 0.0001 || worldDir.y <= 0) return 0;
    float horizon = smoothstep(0, max(_NightSkyTint.w, 0.001), worldDir.y);
    float3 d = normalize(mul((float3x3)_NightSkyWorldToSky, worldDir));
    float2 uv = float2(atan2(d.x, d.z) / (2 * PI) + 0.5, asin(clamp(d.y, -1, 1)) / PI + 0.5);
    float mip = clamp(log2(max(1, angularFootprint * _NightSkyTextureSize.y / PI)), 0, _NightSkyTextureSize.z);
    float3 emission = SAMPLE_TEXTURE2D_LOD(_NightSkyGalaxyTexture, sampler_NightSkyGalaxyTexture, uv, mip).rgb * _NightSkyParams.z;
    if (includeStars) emission += NightStars(d, angularFootprint);
    // Emission is outside the scattering atmosphere, so apply view transmittance exactly once.
    float3 transmission = SampleTransmittance(_AtmosphereCamera.w, clamp(worldDir.y, 0, 1));
    return emission * _NightSkyTint.rgb * transmission * (_NightSkyParams.w * horizon);
}
#endif
