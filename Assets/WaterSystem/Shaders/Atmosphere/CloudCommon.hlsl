#ifndef WATERSYSTEM_CLOUD_COMMON_INCLUDED
#define WATERSYSTEM_CLOUD_COMMON_INCLUDED

// Volumetric cloud layer shared by the camera march, the reflection panorama and the shadow map.
// Density follows the Schneider / Nubis recipe: a tiling Perlin-Worley shape texture eroded by a
// Worley detail texture, gated by a weather map (coverage, type, wetness) and a per-type height profile.
#include "AtmosphereCommon.hlsl"

TEXTURE3D(_CloudShapeNoise);  SAMPLER(sampler_CloudShapeNoise);
TEXTURE3D(_CloudDetailNoise); SAMPLER(sampler_CloudDetailNoise);
TEXTURE2D(_CloudWeatherMap);  SAMPLER(sampler_CloudWeatherMap);
StructuredBuffer<float4> _AtmosphereAmbient; // 0 sky, 1 equator, 2 ground, 3 sun transmittance, 4 clear sky, 5 clear ground

float4 _CloudLayer;        // x bottom altitude m, y thickness m, z 1/thickness, w 1/weather tiling (1/m)
float4 _CloudShape;        // x 1/shape scale (1/m), y 1/detail scale (1/m), z detail strength, w cloud type bias
float4 _CloudDensity;      // x extinction at full density (1/m), y scattering albedo, z wetness darkening, w unused
float4 _CloudWind;         // xy accumulated wind offset (m), z wind shear, w time (s)
float4 _CloudPhase;        // x forward g, y backward g, z blend, w ambient strength
float4 _CloudMultiScatter; // x attenuation, y contribution, z phase decay, w powder strength
float4 _CloudMarch;        // x max steps, y max distance (m), z step growth per km, w light step length (m)
// Camera-only moon lighting. The moon is deliberately excluded from the cloud panorama,
// reflection environment, ambient probe and sun cookie until surface moonlight is implemented.
float4 _MoonDirection;     // xyz direction towards the moon, w cos of disc angular radius
float4 _MoonLight;         // xyz directional illuminance with exposure applied, w enabled

float CloudRemap(float value, float low0, float high0, float low1, float high1)
{
    return low1 + (value - low0) * (high1 - low1) / max(high0 - low0, 1e-5);
}
float CloudHeightFraction(float3 positionKm)
{
    return (length(positionKm) - _AtmosphereRadii.z) / max(_AtmosphereRadii.w - _AtmosphereRadii.z, 1e-5);
}
float4 SampleCloudWeather(float2 positionXZ)
{
    float2 uv = (positionXZ + _CloudWind.xy) * _CloudLayer.w;
    return SAMPLE_TEXTURE2D_LOD(_CloudWeatherMap, sampler_CloudWeatherMap, uv, 0);
}
// Vertical density profile: stratus stays low and flat, cumulus grows tall.
float CloudHeightGradient(float heightFraction, float type)
{
    const float4 stratus = float4(0.02, 0.10, 0.22, 0.38);
    const float4 stratocumulus = float4(0.02, 0.14, 0.48, 0.68);
    const float4 cumulus = float4(0.01, 0.10, 0.72, 0.96);
    float4 g = type < 0.5 ? lerp(stratus, stratocumulus, type * 2.0) : lerp(stratocumulus, cumulus, type * 2.0 - 1.0);
    return smoothstep(g.x, g.y, heightFraction) * (1.0 - smoothstep(g.z, g.w, heightFraction));
}
// positionWS in metres (noise coordinates), heightFraction from the planet frame. Returns [0,1].
float CloudDensity(float3 positionWS, float heightFraction, bool detail, out float4 weather)
{
    weather = SampleCloudWeather(positionWS.xz);
    float coverage = weather.r;
    float type = saturate(weather.g + _CloudShape.w);
    float gradient = CloudHeightGradient(heightFraction, type);
    float density = 0.0;
    if (coverage > 0.002 && gradient > 0.002)
    {
        float3 windOffset = float3(_CloudWind.x, 0, _CloudWind.y);
        float3 shearOffset = windOffset * (_CloudWind.z * heightFraction * min(1.0, _CloudLayer.y / max(length(windOffset), 1.0)));
        float3 shapePosition = (positionWS + windOffset + shearOffset) * _CloudShape.x;
        float4 shape = SAMPLE_TEXTURE3D_LOD(_CloudShapeNoise, sampler_CloudShapeNoise, shapePosition, 0);
        float lowFrequencyFbm = shape.g * 0.625 + shape.b * 0.25 + shape.a * 0.125;
        float base = saturate(CloudRemap(shape.r, lowFrequencyFbm - 1.0, 1.0, 0.0, 1.0));
        // Taller cloud types thin out towards their tops.
        coverage *= lerp(1.0, saturate(1.15 - heightFraction), type * 0.6);
        density = saturate(CloudRemap(base, 1.0 - coverage, 1.0, 0.0, 1.0)) * coverage;
        if (detail && density > 0.0)
        {
            float3 detailPosition = (positionWS + windOffset * 1.4 + shearOffset) * _CloudShape.y;
            float3 d = SAMPLE_TEXTURE3D_LOD(_CloudDetailNoise, sampler_CloudDetailNoise, detailPosition, 0).rgb;
            float highFrequencyFbm = d.r * 0.625 + d.g * 0.25 + d.b * 0.125;
            float modifier = lerp(highFrequencyFbm, 1.0 - highFrequencyFbm, saturate(heightFraction * 10.0));
            density = saturate(CloudRemap(density, modifier * _CloudShape.z, 1.0, 0.0, 1.0));
        }
    }
    return density * gradient;
}

float CloudDualPhase(float cosTheta, float phaseScale)
{
    float forward = HenyeyGreensteinPhase(_CloudPhase.x * phaseScale, cosTheta);
    float backward = HenyeyGreensteinPhase(_CloudPhase.y * phaseScale, cosTheta);
    return lerp(forward, backward, _CloudPhase.z);
}
// Wrenninge's multiple-scattering octaves: attenuated extinction, contribution and phase anisotropy.
float CloudMultiScatterSunlight(float opticalDepth, float cosTheta)
{
    float attenuation = 1.0, contribution = 1.0, phaseScale = 1.0, scattered = 0.0;
    [unroll] for (int octave = 0; octave < 5; octave++)
    {
        scattered += contribution * exp(-opticalDepth * attenuation) * CloudDualPhase(cosTheta, phaseScale);
        attenuation *= _CloudMultiScatter.x;
        contribution *= _CloudMultiScatter.y * _CloudDensity.y;
        phaseScale *= _CloudMultiScatter.z;
    }
    return scattered;
}
// Optical depth towards a directional source with exponentially growing steps.
float CloudLightOpticalDepth(float3 positionWS, float3 positionKm, int steps, float3 lightDirection)
{
    float opticalDepth = 0.0;
    float stepLength = _CloudMarch.w;
    float t = stepLength * 0.5;
    float4 weather;
    for (int i = 0; i < steps; i++)
    {
        float3 sampleKm = positionKm + lightDirection * (t * 0.001);
        float h = CloudHeightFraction(sampleKm);
        if (h > 1.0 || h < 0.0) break;
        opticalDepth += CloudDensity(positionWS + lightDirection * t, h, i < 2, weather) * stepLength;
        t += stepLength;
        stepLength *= 1.5;
    }
    return opticalDepth * _CloudDensity.x;
}
float CloudLightOpticalDepth(float3 positionWS, float3 positionKm, int steps)
{
    return CloudLightOpticalDepth(positionWS, positionKm, steps, _AtmosphereSun.xyz);
}
// Like sunlight, moonlight crosses the air above the sample and can be hidden by the planet.
// This is independent of the sun direction and therefore remains valid during the night.
float3 MoonViewTransmittance(float3 positionKm)
{
    float radius = length(positionKm);
    float3 up = positionKm / max(radius, 1e-5);
    float zenithCos = dot(up, _MoonDirection.xyz);
    if (RaySphereIntersectNearest(positionKm, _MoonDirection.xyz,
        ATMOSPHERE_PLANET_RADIUS_OFFSET * up, _AtmosphereRadii.x) >= 0.0) return 0;
    float horizonFade = smoothstep(-0.02, 0.02, zenithCos);
    return SampleTransmittance(radius, zenithCos) * horizonFade;
}
// Both line intersections (any sign) with a sphere centred at the origin.
bool LineSphereIntersect(float3 origin, float3 direction, float radius, out float t0, out float t1)
{
    float b = dot(origin, direction);
    float c = dot(origin, origin) - radius * radius;
    float delta = b * b - c;
    t0 = 0.0;
    t1 = 0.0;
    bool hit = delta >= 0.0;
    if (hit)
    {
        float sq = sqrt(delta);
        t0 = -b - sq;
        t1 = -b + sq;
    }
    return hit;
}
struct CloudMarchResult
{
    float3 radiance;      // premultiplied in-scattered radiance
    float transmittance;
    float meanDistance;   // metres along the ray
};

// Energy-conserving march (Frostbite integration) with coarse/fine stepping and aerial perspective on the result.
CloudMarchResult MarchClouds(float3 directionWS, float jitter, int maxSteps, int lightSteps, int aerialSamples, bool includeMoon)
{
    CloudMarchResult result;
    result.radiance = 0; result.transmittance = 1; result.meanDistance = 0;
    float tStart, tEnd;
    bool valid = CloudLayerSegment(directionWS, tStart, tEnd);
    float maxDistance = _CloudMarch.y;
    valid = valid && tStart <= maxDistance;
    tEnd = min(tEnd, maxDistance);
    if (valid) result.meanDistance = 0.5 * (tStart + tEnd);
    float3 cameraKm = AtmosphereCameraPlanetKm();
    float coarse = _CloudLayer.y / 32.0;
    float t = tStart;
    bool fine = false;
    int empties = 0;
    float3 L = 0;
    float T = 1.0;
    int stepCount = valid ? maxSteps : 0;
    float distanceSum = 0.0, weightSum = 0.0;
    float cosTheta = dot(directionWS, _AtmosphereSun.xyz);
    float moonCosTheta = dot(directionWS, _MoonDirection.xyz);
    float3 skyAmbient = _AtmosphereAmbient[4].rgb, groundAmbient = _AtmosphereAmbient[5].rgb;
    float3 topAmbient = skyAmbient;
    float3 bottomAmbient = lerp(groundAmbient, skyAmbient, 0.6);
    float powderWeight = _CloudMultiScatter.w * saturate(0.5 - 0.5 * cosTheta);
    for (int i = 0; i < stepCount && t < tEnd; i++)
    {
        float growth = 1.0 + t * 0.001 * _CloudMarch.z;
        float remainingDistance = tEnd - t;
        float stepSize = min(remainingDistance, max((fine ? coarse * 0.33 : coarse) * growth, remainingDistance / (stepCount - i)));
        float sampleDistance = t + saturate(jitter) * stepSize;
        float3 positionKm = cameraKm + directionWS * (sampleDistance * 0.001);
        float h = CloudHeightFraction(positionKm);
        if (h < 0.0 || h > 1.0) { t += stepSize; continue; }
        float3 positionWS = _AtmosphereCamera.xyz + directionWS * sampleDistance;
        float4 weather;
        float density = CloudDensity(positionWS, h, fine, weather);
        if (density > 0.001)
        {
            if (!fine)
            {
                fine = true;
                density = CloudDensity(positionWS, h, true, weather);
            }
            if (density <= 0.001) { t += stepSize; continue; }
            empties = 0;
            float sigmaT = density * _CloudDensity.x;
            float sigmaS = sigmaT * _CloudDensity.y;
            float3 sunTransmittance = SunTransmittanceAt(positionKm);
            float3 sunLight = 0;
            if (any(sunTransmittance > 0.0005))
            {
                float opticalDepth = CloudLightOpticalDepth(positionWS, positionKm, lightSteps);
                float powder = 1.0 - exp(-density * 4.0);
                sunLight = _AtmosphereSunIlluminance.xyz * sunTransmittance * CloudMultiScatterSunlight(opticalDepth, cosTheta) * lerp(1.0, powder, powderWeight);
            }
            float3 moonLight = 0;
            if (includeMoon && _MoonLight.w > 0.5 && any(_MoonLight.xyz > 0.0))
            {
                float3 moonTransmittance = MoonViewTransmittance(positionKm);
                if (any(moonTransmittance > 0.0005))
                {
                    float moonOpticalDepth = CloudLightOpticalDepth(positionWS, positionKm, lightSteps, _MoonDirection.xyz);
                    float moonPowder = 1.0 - exp(-density * 4.0);
                    float moonPowderWeight = _CloudMultiScatter.w * saturate(0.5 - 0.5 * moonCosTheta);
                    moonLight = _MoonLight.xyz * moonTransmittance *
                        CloudMultiScatterSunlight(moonOpticalDepth, moonCosTheta) *
                        lerp(1.0, moonPowder, moonPowderWeight);
                }
            }
            float3 ambient = lerp(bottomAmbient, topAmbient, h) * _CloudPhase.w * (1.0 - weather.b * _CloudDensity.z * (1.0 - h));
            // Dense cores receive less sky light than wisps, which gives overcast bases their structure.
            ambient *= lerp(1.0, 0.85, saturate(density * 2.5));
            float3 S = sigmaS * (sunLight + moonLight + ambient);
            float stepTransmittance = exp(-sigmaT * stepSize);
            L += T * (S - S * stepTransmittance) / sigmaT;
            float absorbed = T * (1.0 - stepTransmittance);
            distanceSum += sampleDistance * absorbed;
            weightSum += absorbed;
            T *= stepTransmittance;
            if (T < 0.005) break;
        }
        else if (fine)
        {
            empties++;
            if (empties > 6) fine = false;
        }
        t += stepSize;
    }
    if (weightSum > 1e-5) result.meanDistance = distanceSum / weightSum;
    // Fade the layer out before the march distance limit instead of cutting it.
    float fade = 1.0 - smoothstep(0.6, 1.0, result.meanDistance / maxDistance);
    L *= fade;
    T = lerp(1.0, T, fade);
    // Aerial perspective between the camera and the cloud.
    if (valid && aerialSamples > 0 && T < 0.999)
    {
        AtmosphereIntegration ap = IntegrateScatteredLuminance(cameraKm, directionWS, _AtmosphereSun.xyz, _AtmosphereSunIlluminance.xyz,
            false, aerialSamples, result.meanDistance * 0.001, false, true, true);
        L = L * ap.transmittance + ap.luminance * _AtmosphereSunIlluminance.w * (1.0 - T);
    }
    result.radiance = L;
    result.transmittance = T;
    return result;
}
#endif
