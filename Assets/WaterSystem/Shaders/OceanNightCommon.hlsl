#ifndef WATERSYSTEM_OCEAN_NIGHT_INCLUDED
#define WATERSYSTEM_OCEAN_NIGHT_INCLUDED

#include "Atmosphere/AtmosphereCommon.hlsl"

// Declare inside each shader's UnityPerMaterial buffer to preserve SRP material layout.
#define OCEAN_NIGHT_MATERIAL_FIELDS \
    float4 _OceanNightControl, _OceanNightTransition, _OceanNightLighting; \
    float4 _OceanNightDeep, _OceanNightShallow, _OceanNightHorizon, _OceanNightGlow, _OceanNightMoonTint;

TEXTURECUBE(_AtmosphereSkyReflection); SAMPLER(sampler_AtmosphereSkyReflection);
float4 _AtmosphereSkyReflectionParams;
// Celestial globals are read independently of the cloud-only _MoonLight intensity.
float4 _MoonDirection, _MoonParams, _MoonSurface, _MoonTint;

// Functions are placed after UnityPerMaterial by the including shader.
#endif

#if defined(OCEAN_NIGHT_FUNCTIONS) && !defined(WATERSYSTEM_OCEAN_NIGHT_FUNCTIONS_INCLUDED)
#define WATERSYSTEM_OCEAN_NIGHT_FUNCTIONS_INCLUDED
float OceanNightAmount(float3 mainSunDirection)
{
    if (_OceanNightControl.x < 0.5) return 0;
    if (_OceanNightControl.y < 0.5) return saturate(_OceanNightControl.z);
    float sunY = AtmosphereEnabled() ? _AtmosphereSun.y : mainSunDirection.y;
    float t = saturate((_OceanNightTransition.x - sunY) /
        max(_OceanNightTransition.x - _OceanNightTransition.y, 0.0001));
    return t * t * (3 - 2 * t);
}

float OceanNightSmoothFoam(float2 uv, int cascade);

float OceanNightSampleFoam(float2 uv, int cascade, float night)
{
    float original = SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanFoam,sampler_OceanFoam,uv,cascade,0).r;
    if (night <= 0) return original;
    return lerp(original,OceanNightSmoothFoam(uv,cascade),night);
}

// x: accumulated whitecap coverage, y: remembered minimum Jacobian of this cascade (see BuildFoam).
// Filtered with the caller's footprint, so distant foam averages into coverage instead of aliasing.
float2 OceanSampleFoam(float2 uv, int cascade, float night, float2 uvDx, float2 uvDy)
{
    float2 original = SAMPLE_TEXTURE2D_ARRAY_GRAD(_OceanFoam,sampler_OceanFoam,uv,cascade,uvDx,uvDy).rg;
    if (night <= 0) return original;
    return float2(lerp(original.x,OceanNightSmoothFoam(uv,cascade),night),original.y);
}

float OceanNightSmoothFoam(float2 uv, int cascade)
{
    // Large FFT domains can have metre-wide texels. HDR emission exposes the square support of
    // ordinary bilinear reconstruction; a positive cubic B-spline uses four bilinear taps and
    // has no overshoot or negative coverage. The simulation and daytime sampling stay unchanged.
    float2 texel = uv * _FFTResolution - 0.5;
    float2 cell = floor(texel), f = frac(texel), f2 = f*f, f3 = f2*f;
    float2 w0 = (1-3*f+3*f2-f3)/6;
    float2 w1 = (4-6*f2+3*f3)/6;
    float2 w2 = (1+3*f+3*f2-3*f3)/6;
    float2 w3 = f3/6;
    float2 g0 = w0+w1, g1 = w2+w3;
    float2 a = (cell-0.5+w1/max(g0,1e-5))/_FFTResolution;
    float2 b = (cell+1.5+w3/max(g1,1e-5))/_FFTResolution;
    float s00 = SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanFoam,sampler_OceanFoam,a,cascade,0).r;
    float s10 = SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanFoam,sampler_OceanFoam,float2(b.x,a.y),cascade,0).r;
    float s01 = SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanFoam,sampler_OceanFoam,float2(a.x,b.y),cascade,0).r;
    float s11 = SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanFoam,sampler_OceanFoam,b,cascade,0).r;
    return lerp(lerp(s00,s10,g1.x),lerp(s01,s11,g1.x),g1.y);
}

float3 OceanNightPalette(float deepBlend)
{
    return lerp(_OceanNightShallow.rgb, _OceanNightDeep.rgb, saturate(deepBlend));
}

float3 OceanNightEnvironment(float3 direction, float roughness, float3 dayEnvironment, float night)
{
    if (night <= 0) return dayEnvironment;
    float3 fill = lerp(_OceanNightHorizon.rgb, _OceanNightDeep.rgb, sqrt(saturate(direction.y)));
    float3 environment = 0;
    if (AtmosphereEnabled() && _AtmosphereSkyReflectionParams.x > 0)
    {
        float mip = roughness * (1.7 - 0.7 * roughness) * _AtmosphereSkyReflectionParams.x;
        environment = SAMPLE_TEXTURECUBE_LOD(_AtmosphereSkyReflection, sampler_AtmosphereSkyReflection, direction, mip).rgb;
    }
    // Avoid stale day reflection probes/panoramas in manual night mode as well.
    environment += fill * _OceanNightTransition.w;
    return lerp(dayEnvironment, environment, night);
}

float3 OceanMoonDirection()
{
    return SafeNormalize(_MoonDirection.xyz + float3(0, 1e-6, 0));
}

float3 OceanMoonLight(float night)
{
    if (night <= 0 || !AtmosphereEnabled() || _MoonParams.x < 0.5 || _MoonDirection.y <= 0) return 0;
    float3 direction = OceanMoonDirection();
    float horizon = smoothstep(0, 0.08, direction.y);
    float3 transmission = SampleTransmittance(_AtmosphereRadii.x + 0.0005, direction.y);
    // Panorama alpha is cloud transmittance, not opacity. This is a distant-cloud approximation,
    // not a second cloud-shadow march per water pixel.
    float cloudTransmission = SampleCloudPanorama(direction).a;
    return _MoonTint.rgb * _OceanNightMoonTint.rgb * (_MoonParams.y * _MoonSurface.w * PI *
        _OceanNightLighting.x * night * horizon * cloudTransmission) * transmission;
}

float3 OceanNightFoamEmission(float foamMask, float detailAverage, float2 footprint, float foamScale, float night)
{
    float cells = 0.2 + 0.8 * smoothstep(0.3, 0.75, detailAverage);
    float pattern = lerp(1, cells, _OceanNightLighting.w);
    // The FFT foam arrays have no mips. Suppress unresolved emissive flecks at grazing distance.
    float distanceAA = rcp(1 + max(footprint.x, footprint.y) * foamScale);
    float coverage = pow(saturate(foamMask), max(0.25, _OceanNightControl.w));
    return _OceanNightGlow.rgb * (_OceanNightLighting.z * night * coverage * pattern * distanceAA);
}
#endif
