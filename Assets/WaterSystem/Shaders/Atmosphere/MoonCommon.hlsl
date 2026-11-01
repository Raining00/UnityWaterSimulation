#ifndef WATERSYSTEM_MOON_INCLUDED
#define WATERSYSTEM_MOON_INCLUDED

#include "AtmosphereCommon.hlsl"
TEXTURE2D(_MoonSurfaceTexture); SAMPLER(sampler_MoonSurfaceTexture);
float4 _MoonDirection;       // xyz towards moon, w cos angular radius
float4 _MoonParams;          // enabled, disc radiance, halo radiance, halo radius radians
float4 _MoonSurface;         // angular radius radians, phase angle, halo falloff, illuminated fraction
float4 _MoonTint;            // RGB linear tint with atmosphere exposure
float4 _MoonRight;
float4 _MoonUp;
float4 _MoonTextureSize;     // width, height, max mip
float _MoonCameraVisible;   // zero for reflection probe cameras

struct MoonSkyResult
{
    float3 radiance;
    float coverage;
};

MoonSkyResult EvaluateMoon(float3 direction, float angularFootprint)
{
    MoonSkyResult result;
    result.radiance = 0;
    result.coverage = 0;
    if (_MoonParams.x < 0.5 || _MoonCameraVisible < 0.5 || direction.y <= 0) return result;
    float facing = dot(direction, _MoonDirection.xyz);
    float angle = acos(clamp(facing, -1, 1));
    float radius = _MoonSurface.x;
    float edgeWidth = max(angularFootprint * 0.65, 1e-5);
    result.coverage = 1 - smoothstep(max(0, radius - edgeWidth), radius + edgeWidth, angle);

    float3 disc = 0;
    if (result.coverage > 0 && facing > 0)
    {
        float2 discXY = float2(dot(direction, _MoonRight.xyz), dot(direction, _MoonUp.xyz)) /
                        max(facing * tan(radius), 1e-6);
        float3 normal = float3(discXY, sqrt(saturate(1 - dot(discXY, discXY))));
        normal = normalize(normal);
        float2 uv = float2(atan2(normal.x, max(normal.z, 1e-5)) / (2 * PI) + 0.5,
                           asin(clamp(normal.y, -1, 1)) / PI + 0.5);
        float mip = clamp(log2(max(1, angularFootprint * _MoonTextureSize.x / max(radius * 2 * PI, 1e-5))),
                          0, _MoonTextureSize.z);
        float3 albedo = SAMPLE_TEXTURE2D_LOD(_MoonSurfaceTexture, sampler_MoonSurfaceTexture, uv, mip).rgb;
        float3 phaseLight = float3(sin(_MoonSurface.y), 0, -cos(_MoonSurface.y));
        float illumination = pow(saturate(dot(normal, phaseLight)), 0.55);
        // Keep the unlit hemisphere very faint, while still occluding stars behind the solid body.
        disc = albedo * (_MoonParams.y * (illumination + 0.008)) * result.coverage;
    }
    float haloMask = pow(saturate(1 - angle / max(_MoonParams.w, 1e-5)), _MoonSurface.z);
    float3 halo = _MoonParams.z * haloMask * _MoonSurface.w;
    float horizon = smoothstep(0, 0.01, direction.y);
    float3 airTransmission = SampleTransmittance(_AtmosphereCamera.w, clamp(direction.y, 0, 1));
    result.coverage *= horizon;
    result.radiance = (disc + halo) * _MoonTint.rgb * airTransmission * horizon;
    return result;
}
#endif
