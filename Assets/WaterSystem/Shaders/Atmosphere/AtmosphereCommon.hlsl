#ifndef WATERSYSTEM_ATMOSPHERE_COMMON_INCLUDED
#define WATERSYSTEM_ATMOSPHERE_COMMON_INCLUDED

// Physically based sky shared by the LUT kernels, the skybox, the full-screen composites and the
// ocean shaders. The model follows Hillaire 2020 ("A Scalable and Production Ready Sky and
// Atmosphere Rendering Technique"): transmittance LUT, multiple-scattering LUT, a per-camera
// sky-view LUT and a per-camera aerial-perspective froxel volume.
//
// Units are kilometres inside the planet frame. The planet centre is always directly below the
// camera, so world directions equal planet-frame directions and only positions are offset:
//   planetPosKm = float3(dx, cameraRadiusKm + dy, dz) * 0.001 with d = worldPos - cameraWorldPos.
// Radiance is expressed in URP light units: a Lambertian white surface lit by a directional light
// of intensity I has radiance I, so the sun illuminance used here is PI * color * intensity.

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

// ---------------------------------------------------------------------------------------------
// Globals written by AtmosphereRenderer (Shader.SetGlobal*). Compute kernels read the same names.
// ---------------------------------------------------------------------------------------------
float4 _AtmosphereRadii;           // x bottom radius km, y top radius km, z cloud bottom radius km, w cloud top radius km
float4 _AtmosphereRayleigh;        // xyz scattering /km, w scale height km
float4 _AtmosphereMie;             // xyz scattering /km, w scale height km
float4 _AtmosphereMieAbsorption;   // xyz absorption /km, w Cornette-Shanks anisotropy
float4 _AtmosphereOzone;           // xyz absorption /km, w layer centre altitude km
float4 _AtmosphereGround;          // xyz ground albedo, w ozone layer half width km
float4 _AtmosphereSun;             // xyz world direction towards the sun, w cos(sun disc angular radius)
float4 _AtmosphereSunIlluminance;  // xyz outer-space sun illuminance (URP units, exposure applied), w aerial perspective scale
float4 _AtmosphereCamera;          // xyz world camera position (m), w camera radius km (clamped inside the atmosphere)
float4 _AtmosphereWorld;           // x sea level world Y (m), y atmosphere enabled, z clouds enabled, w skybox draws the cloud panorama
float4 _AtmosphereLUT;             // x sky-view width, y sky-view height, z aerial slice count, w aerial max distance km
float4 _AtmosphereNight;           // xyz night sky floor radiance, w unused

TEXTURE2D(_AtmosphereTransmittanceLUT); SAMPLER(sampler_AtmosphereTransmittanceLUT);
TEXTURE2D(_AtmosphereMultiScatterLUT);  SAMPLER(sampler_AtmosphereMultiScatterLUT);
TEXTURE2D(_AtmosphereSkyViewLUT);       SAMPLER(sampler_AtmosphereSkyViewLUT);
TEXTURE3D(_AtmosphereAerialLUT);        SAMPLER(sampler_AtmosphereAerialLUT);
TEXTURE2D(_CloudPanorama);              SAMPLER(sampler_CloudPanorama);
TEXTURE2D(_CloudBuffer);                SAMPLER(sampler_CloudBuffer);
TEXTURE2D(_CloudShadowMap);             SAMPLER(sampler_CloudShadowMap);
float4x4 _CloudShadowWorldToLight;      // sun light worldToLocal; shadow texel = local.xy / size - offset + 0.5 (URP cookie mapping)
float4 _CloudShadowParams;              // x 1/size (1/m), y offset u, z offset v, w strength

#define ATMOSPHERE_TRANSMITTANCE_WIDTH 256.0
#define ATMOSPHERE_TRANSMITTANCE_HEIGHT 64.0
#define ATMOSPHERE_MULTISCATTER_SIZE 32.0
#define ATMOSPHERE_PLANET_RADIUS_OFFSET 0.01

bool AtmosphereEnabled() { return _AtmosphereWorld.y > 0.5; }
bool AtmosphereCloudsEnabled() { return _AtmosphereWorld.y > 0.5 && _AtmosphereWorld.z > 0.5; }

// World position (metres) to planet frame (kilometres).
float3 AtmosphereWorldToPlanetKm(float3 positionWS)
{
    float3 d = (positionWS - _AtmosphereCamera.xyz) * 0.001;
    return float3(d.x, _AtmosphereCamera.w + d.y, d.z);
}
float3 AtmosphereCameraPlanetKm() { return float3(0, _AtmosphereCamera.w, 0); }

// ---------------------------------------------------------------------------------------------
// Geometry
// ---------------------------------------------------------------------------------------------
// Nearest non-negative intersection distance with a sphere centred at s0, or -1 when missed.
float RaySphereIntersectNearest(float3 r0, float3 rd, float3 s0, float sR)
{
    float a = dot(rd, rd);
    float3 s0r0 = r0 - s0;
    float b = 2.0 * dot(rd, s0r0);
    float c = dot(s0r0, s0r0) - sR * sR;
    float delta = b * b - 4.0 * a * c;
    float result = -1.0;
    if (delta >= 0.0 && a != 0.0)
    {
        float sq = sqrt(delta);
        float sol0 = (-b - sq) / (2.0 * a);
        float sol1 = (-b + sq) / (2.0 * a);
        if (sol0 >= 0.0 && sol1 >= 0.0) result = min(sol0, sol1);
        else if (sol0 >= 0.0) result = sol0;
        else if (sol1 >= 0.0) result = sol1;
    }
    return result;
}
// Both intersection distances (t0 <= t1) with a sphere centred at the origin; false when missed.
bool RaySphereIntersect(float3 r0, float3 rd, float sR, out float t0, out float t1)
{
    float b = dot(r0, rd);
    float originRadius = length(r0);
    precise float c = (originRadius - sR) * (originRadius + sR);
    float delta = b * b - c;
    t0 = -1.0;
    t1 = -1.0;
    bool hit = delta >= 0.0;
    if (hit)
    {
        float sq = sqrt(delta);
        t0 = -b - sq;
        t1 = -b + sq;
        hit = t1 >= 0.0;
    }
    return hit;
}

// ---------------------------------------------------------------------------------------------
// Participating medium
// ---------------------------------------------------------------------------------------------
struct AtmosphereMedium
{
    float3 scattering;
    float3 scatteringMie;
    float3 scatteringRayleigh;
    float3 extinction;
};
AtmosphereMedium SampleAtmosphereMedium(float3 positionKm)
{
    float altitude = max(0.0, length(positionKm) - _AtmosphereRadii.x);
    float densityRayleigh = exp(-altitude / max(_AtmosphereRayleigh.w, 0.01));
    float densityMie = exp(-altitude / max(_AtmosphereMie.w, 0.01));
    float densityOzone = saturate(1.0 - abs(altitude - _AtmosphereOzone.w) / max(_AtmosphereGround.w, 0.01));
    AtmosphereMedium m;
    m.scatteringRayleigh = _AtmosphereRayleigh.xyz * densityRayleigh;
    m.scatteringMie = _AtmosphereMie.xyz * densityMie;
    m.scattering = m.scatteringRayleigh + m.scatteringMie;
    m.extinction = max(m.scattering + _AtmosphereMieAbsorption.xyz * densityMie + _AtmosphereOzone.xyz * densityOzone, 1e-7);
    return m;
}
float RayleighPhase(float cosTheta) { return 3.0 / (16.0 * PI) * (1.0 + cosTheta * cosTheta); }
// cosTheta = dot(viewRayDirection, sunDirection): forward scattering peaks when looking towards the sun.
float CornetteShanksPhase(float g, float cosTheta)
{
    float k = 3.0 / (8.0 * PI) * (1.0 - g * g) / (2.0 + g * g);
    return k * (1.0 + cosTheta * cosTheta) / pow(max(1.0 + g * g - 2.0 * g * cosTheta, 1e-4), 1.5);
}
float HenyeyGreensteinPhase(float g, float cosTheta)
{
    float denom = 1.0 + g * g - 2.0 * g * cosTheta;
    return (1.0 - g * g) / (4.0 * PI * pow(max(denom, 1e-4), 1.5));
}

// ---------------------------------------------------------------------------------------------
// LUT parametrisations
// ---------------------------------------------------------------------------------------------
float FromUnitToSubUvs(float u, float resolution) { return (u + 0.5 / resolution) * (resolution / (resolution + 1.0)); }
float FromSubUvsToUnit(float u, float resolution) { return (u - 0.5 / resolution) * (resolution / (resolution - 1.0)); }

// Bruneton transmittance mapping: (radius, cos view zenith) <-> uv. Directions below the horizon clamp to the horizon.
void UvToTransmittanceParams(float2 uv, out float viewRadius, out float viewZenithCos)
{
    float bottom = _AtmosphereRadii.x, top = _AtmosphereRadii.y;
    float H = sqrt(max(0.0, top * top - bottom * bottom));
    float rho = H * uv.y;
    viewRadius = sqrt(rho * rho + bottom * bottom);
    float dMin = top - viewRadius;
    float dMax = rho + H;
    float d = dMin + uv.x * (dMax - dMin);
    viewZenithCos = d == 0.0 ? 1.0 : (H * H - rho * rho - d * d) / (2.0 * viewRadius * d);
    viewZenithCos = clamp(viewZenithCos, -1.0, 1.0);
}
float2 TransmittanceParamsToUv(float viewRadius, float viewZenithCos)
{
    float bottom = _AtmosphereRadii.x, top = _AtmosphereRadii.y;
    float H = sqrt(max(0.0, top * top - bottom * bottom));
    float rho = sqrt(max(0.0, viewRadius * viewRadius - bottom * bottom));
    float discriminant = viewRadius * viewRadius * (viewZenithCos * viewZenithCos - 1.0) + top * top;
    float d = max(0.0, -viewRadius * viewZenithCos + sqrt(max(0.0, discriminant)));
    float dMin = top - viewRadius;
    float dMax = rho + H;
    return float2(saturate((d - dMin) / max(dMax - dMin, 1e-5)), saturate(rho / max(H, 1e-5)));
}
float3 SampleTransmittance(float viewRadius, float viewZenithCos)
{
    float2 uv = TransmittanceParamsToUv(viewRadius, viewZenithCos);
    return SAMPLE_TEXTURE2D_LOD(_AtmosphereTransmittanceLUT, sampler_AtmosphereTransmittanceLUT, uv, 0).rgb;
}
// Transmittance towards the sun from a planet-frame position, zero inside the planet's shadow.
float3 SunTransmittanceAt(float3 positionKm)
{
    float r = length(positionKm);
    float3 up = positionKm / max(r, 1e-5);
    float sunZenithCos = dot(up, _AtmosphereSun.xyz);
    float tEarth = RaySphereIntersectNearest(positionKm, _AtmosphereSun.xyz, ATMOSPHERE_PLANET_RADIUS_OFFSET * up, _AtmosphereRadii.x);
    float earthShadow = tEarth >= 0.0 ? 0.0 : 1.0;
    return SampleTransmittance(r, sunZenithCos) * earthShadow;
}
float3 SampleMultipleScattering(float3 positionKm, float sunZenithCos)
{
    float2 uv = saturate(float2(sunZenithCos * 0.5 + 0.5, (length(positionKm) - _AtmosphereRadii.x) / max(_AtmosphereRadii.y - _AtmosphereRadii.x, 1e-3)));
    uv = float2(FromUnitToSubUvs(uv.x, ATMOSPHERE_MULTISCATTER_SIZE), FromUnitToSubUvs(uv.y, ATMOSPHERE_MULTISCATTER_SIZE));
    return SAMPLE_TEXTURE2D_LOD(_AtmosphereMultiScatterLUT, sampler_AtmosphereMultiScatterLUT, uv, 0).rgb;
}

// Hillaire sky-view mapping with the non-linear horizon split. The LUT is symmetric about the sun azimuth.
void UvToSkyViewParams(float2 uv, float viewRadius, out float viewZenithCos, out float lightViewCos)
{
    uv = float2(FromSubUvsToUnit(uv.x, _AtmosphereLUT.x), FromSubUvsToUnit(uv.y, _AtmosphereLUT.y));
    float vHorizon = sqrt(max(0.0, viewRadius * viewRadius - _AtmosphereRadii.x * _AtmosphereRadii.x));
    float cosBeta = vHorizon / viewRadius;
    float beta = acos(cosBeta);
    float zenithHorizonAngle = PI - beta;
    if (uv.y < 0.5)
    {
        float coord = 2.0 * uv.y;
        coord = 1.0 - coord;
        coord *= coord;
        coord = 1.0 - coord;
        viewZenithCos = cos(zenithHorizonAngle * coord);
    }
    else
    {
        float coord = uv.y * 2.0 - 1.0;
        coord *= coord;
        viewZenithCos = cos(zenithHorizonAngle + beta * coord);
    }
    float coordX = uv.x;
    coordX *= coordX;
    lightViewCos = -(coordX * 2.0 - 1.0);
}
float2 SkyViewParamsToUv(bool intersectGround, float viewZenithCos, float lightViewCos, float viewRadius)
{
    float vHorizon = sqrt(max(0.0, viewRadius * viewRadius - _AtmosphereRadii.x * _AtmosphereRadii.x));
    float cosBeta = vHorizon / viewRadius;
    float beta = acos(cosBeta);
    float zenithHorizonAngle = PI - beta;
    float2 uv;
    if (!intersectGround)
    {
        float coord = acos(viewZenithCos) / zenithHorizonAngle;
        coord = 1.0 - coord;
        coord = sqrt(max(0.0, coord));
        coord = 1.0 - coord;
        uv.y = coord * 0.5;
    }
    else
    {
        float coord = (acos(viewZenithCos) - zenithHorizonAngle) / max(beta, 1e-5);
        coord = sqrt(max(0.0, coord));
        uv.y = coord * 0.5 + 0.5;
    }
    uv.x = sqrt(saturate(-lightViewCos * 0.5 + 0.5));
    return float2(FromUnitToSubUvs(uv.x, _AtmosphereLUT.x), FromUnitToSubUvs(uv.y, _AtmosphereLUT.y));
}
// Sky luminance (no sun disc, no clouds) for a world direction seen from the camera.
float3 SampleSkyView(float3 worldDir)
{
    float viewRadius = _AtmosphereCamera.w;
    float3 up = float3(0, 1, 0);
    float viewZenithCos = clamp(worldDir.y, -1.0, 1.0);
    float3 side = cross(up, worldDir);
    float sideLength = length(side);
    side = sideLength > 1e-4 ? side / sideLength : float3(1, 0, 0);
    float3 forward = cross(side, up);
    float2 lightOnPlane = float2(dot(_AtmosphereSun.xyz, forward), dot(_AtmosphereSun.xyz, side));
    float planeLength = length(lightOnPlane);
    float lightViewCos = planeLength > 1e-5 ? lightOnPlane.x / planeLength : 1.0;
    bool intersectGround = RaySphereIntersectNearest(float3(0, viewRadius, 0), worldDir, 0, _AtmosphereRadii.x) >= 0.0;
    float2 uv = SkyViewParamsToUv(intersectGround, viewZenithCos, lightViewCos, viewRadius);
    return SAMPLE_TEXTURE2D_LOD(_AtmosphereSkyViewLUT, sampler_AtmosphereSkyViewLUT, uv, 0).rgb;
}

// ---------------------------------------------------------------------------------------------
// Scattering integration (single scattering + multiple-scattering LUT term)
// ---------------------------------------------------------------------------------------------
struct AtmosphereIntegration
{
    float3 luminance;
    float3 transmittance;
    float3 multiScatAs1;     // integral of scattering (isotropic phase) used by the multiple-scattering LUT
    float3 luminanceNoPhase; // same but with a uniform phase, used by the multiple-scattering LUT
};
// positionKm: planet frame start, worldDir: normalised, maxDistanceKm limits the ray, depthKm < 0 means unlimited.
AtmosphereIntegration IntegrateScatteredLuminance(float3 positionKm, float3 worldDir, float3 sunDir, float3 sunIlluminance,
    bool ground, float sampleCountIni, float maxDistanceKm, bool variableSampleCount, bool mieRayleighPhase, bool useMultiScattering)
{
    AtmosphereIntegration result;
    result.luminance = 0; result.transmittance = 1; result.multiScatAs1 = 0; result.luminanceNoPhase = 0;
    float bottom = _AtmosphereRadii.x, top = _AtmosphereRadii.y;
    float tBottom = RaySphereIntersectNearest(positionKm, worldDir, 0, bottom);
    float tTop = RaySphereIntersectNearest(positionKm, worldDir, 0, top);
    float tMax = 0.0;
    bool valid = true;
    if (tBottom < 0.0)
    {
        if (tTop < 0.0) valid = false; // outside the atmosphere, looking into space
        else tMax = tTop;
    }
    else tMax = tTop > 0.0 ? min(tTop, tBottom) : tBottom;
    tMax = min(tMax, maxDistanceKm);
    float sampleCount = valid ? sampleCountIni : 0.0;
    float sampleCountFloor = max(sampleCount, 1.0);
    float tMaxFloor = tMax;
    if (variableSampleCount && valid)
    {
        sampleCount = lerp(4.0, sampleCountIni, saturate(tMax * 0.01));
        sampleCountFloor = max(floor(sampleCount), 1.0);
        tMaxFloor = tMax * sampleCountFloor / max(sampleCount, 1.0);
    }
    float dt = tMax / max(sampleCount, 1.0);
    float cosTheta = dot(worldDir, sunDir);
    float miePhase = CornetteShanksPhase(_AtmosphereMieAbsorption.w, cosTheta);
    float rayleighPhase = RayleighPhase(cosTheta);
    const float uniformPhase = 1.0 / (4.0 * PI);
    float3 luminance = 0, throughput = 1;
    float t = 0.0;
    const float sampleSegmentT = 0.3;
    for (float s = 0.0; s < sampleCount; s += 1.0)
    {
        if (variableSampleCount)
        {
            float t0 = s / sampleCountFloor, t1 = (s + 1.0) / sampleCountFloor;
            t0 = t0 * t0; t1 = t1 * t1;
            t0 = tMaxFloor * t0;
            t1 = t1 > 1.0 ? tMax : tMaxFloor * t1;
            t = t0 + (t1 - t0) * sampleSegmentT;
            dt = t1 - t0;
        }
        else
        {
            float newT = tMax * (s + sampleSegmentT) / sampleCount;
            dt = newT - t;
            t = newT;
        }
        float3 p = positionKm + t * worldDir;
        AtmosphereMedium medium = SampleAtmosphereMedium(p);
        float3 sampleTransmittance = exp(-medium.extinction * dt);
        float pRadius = length(p);
        float3 up = p / pRadius;
        float sunZenithCos = dot(sunDir, up);
        float3 transmittanceToSun = SampleTransmittance(pRadius, sunZenithCos);
        float3 phaseTimesScattering = mieRayleighPhase ? medium.scatteringMie * miePhase + medium.scatteringRayleigh * rayleighPhase : medium.scattering * uniformPhase;
        float tEarth = RaySphereIntersectNearest(p, sunDir, ATMOSPHERE_PLANET_RADIUS_OFFSET * up, bottom);
        float earthShadow = tEarth >= 0.0 ? 0.0 : 1.0;
        float3 multiScattered = useMultiScattering ? SampleMultipleScattering(p, sunZenithCos) : 0;
        float3 S = sunIlluminance * (earthShadow * transmittanceToSun * phaseTimesScattering + multiScattered * medium.scattering);
        float3 ms = medium.scattering;
        float3 msInt = (ms - ms * sampleTransmittance) / medium.extinction;
        result.multiScatAs1 += throughput * msInt;
        float3 sInt = (S - S * sampleTransmittance) / medium.extinction;
        luminance += throughput * sInt;
        throughput *= sampleTransmittance;
    }
    if (valid && ground && tMax == tBottom && tBottom > 0.0)
    {
        float3 p = positionKm + tBottom * worldDir;
        float pRadius = length(p);
        float3 up = p / pRadius;
        float sunZenithCos = dot(sunDir, up);
        float3 transmittanceToSun = SampleTransmittance(pRadius, sunZenithCos);
        float nDotL = saturate(dot(up, sunDir));
        luminance += sunIlluminance * transmittanceToSun * throughput * nDotL * _AtmosphereGround.xyz / PI;
    }
    result.luminance = luminance;
    result.transmittance = throughput;
    return result;
}

// ---------------------------------------------------------------------------------------------
// Aerial perspective (per camera froxel volume, squared distance distribution)
// ---------------------------------------------------------------------------------------------
float AerialSliceToDistanceKm(float slice) // slice in [0, sliceCount]
{
    float f = slice / _AtmosphereLUT.z;
    return f * f * _AtmosphereLUT.w;
}
// In-scattered luminance and grey transmittance between the camera and a point at screenUV / distance.
void AtmosphereAerialPerspective(float2 screenUV, float distanceMetres, out float3 inscatter, out float transmittance)
{
    float distanceKm = distanceMetres * 0.001;
    float w = sqrt(saturate(distanceKm / _AtmosphereLUT.w));
    float slice = w * _AtmosphereLUT.z;
    float weight = 1.0;
    if (slice < 0.5)
    {
        // Fade the first half slice instead of extrapolating towards the camera.
        weight = saturate(slice * 2.0);
        w = 0.5 / _AtmosphereLUT.z;
    }
    float4 ap = SAMPLE_TEXTURE3D_LOD(_AtmosphereAerialLUT, sampler_AtmosphereAerialLUT, float3(saturate(screenUV), w), 0);
    inscatter = ap.rgb * weight * _AtmosphereSunIlluminance.w;
    transmittance = lerp(1.0, ap.a, weight);
}
float3 AtmosphereApplyAerialPerspective(float3 color, float2 screenUV, float distanceMetres)
{
    float3 inscatter; float transmittance;
    AtmosphereAerialPerspective(screenUV, distanceMetres, inscatter, transmittance);
    return color * transmittance + inscatter;
}

// ---------------------------------------------------------------------------------------------
// Sky, sun disc, clouds and shadows for surface shaders
// ---------------------------------------------------------------------------------------------
float3 AtmosphereSkyRadiance(float3 worldDir)
{
    return SampleSkyView(worldDir) + _AtmosphereNight.xyz;
}
// Panorama lookup: u azimuth, v signed square-root elevation over both hemispheres.
float2 CloudPanoramaUv(float3 worldDir)
{
    float u = dot(worldDir.xz, worldDir.xz) > 1e-8 ? atan2(worldDir.x, worldDir.z) / (2.0 * PI) + 0.5 : 0.5;
    float v = 0.5 + 0.5 * sign(worldDir.y) * sqrt(saturate(abs(worldDir.y)));
    return float2(u, v);
}
float3 CloudPanoramaDirection(float2 uv)
{
    float azimuth = (uv.x - 0.5) * 2.0 * PI;
    float elevation = uv.y * 2.0 - 1.0;
    float y = sign(elevation) * elevation * elevation;
    float c = sqrt(saturate(1.0 - y * y));
    return float3(sin(azimuth) * c, y, cos(azimuth) * c);
}
// rgb premultiplied cloud radiance, a transmittance.
float4 SampleCloudPanorama(float3 worldDir)
{
    float4 result = float4(0, 0, 0, 1);
    if (AtmosphereCloudsEnabled())
        result = SAMPLE_TEXTURE2D_LOD(_CloudPanorama, sampler_CloudPanorama, CloudPanoramaUv(worldDir), 0);
    return result;
}
float3 AtmosphereSunDisc(float3 worldDir)
{
    float cosAngle = dot(worldDir, _AtmosphereSun.xyz);
    float cosRadius = _AtmosphereSun.w;
    float3 result = 0;
    bool visible = cosAngle >= cosRadius && RaySphereIntersectNearest(AtmosphereCameraPlanetKm(), worldDir, 0, _AtmosphereRadii.x) < 0.0;
    if (visible)
    {
        float sinAngle = sqrt(saturate(1.0 - cosAngle * cosAngle));
        float sinRadius = sqrt(saturate(1.0 - cosRadius * cosRadius));
        float x = saturate(sinAngle / max(sinRadius, 1e-6));
        float limb = pow(saturate(1.0 - x * x), 0.3); // approximate limb darkening
        float solidAngle = 2.0 * PI * (1.0 - cosRadius);
        float3 transmittance = SampleTransmittance(_AtmosphereCamera.w, clamp(worldDir.y, -1.0, 1.0));
        result = _AtmosphereSunIlluminance.xyz / max(solidAngle, 1e-7) * transmittance * limb;
    }
    return result;
}
// Segment of the view ray inside the cloud shell, in metres; shared by marching and compositing.
bool CloudLayerSegment(float3 directionWS, out float tStart, out float tEnd)
{
    float3 cameraKm = AtmosphereCameraPlanetKm();
    float bottomNear, bottomFar, topNear, topFar;
    bool hitBottom = RaySphereIntersect(cameraKm, directionWS, _AtmosphereRadii.z, bottomNear, bottomFar);
    bool valid = RaySphereIntersect(cameraKm, directionWS, _AtmosphereRadii.w, topNear, topFar);
    tStart = max(topNear, 0.0);
    tEnd = topFar;
    if (_AtmosphereCamera.w < _AtmosphereRadii.z)
    {
        valid = valid && RaySphereIntersectNearest(cameraKm, directionWS, 0, _AtmosphereRadii.x) < 0.0;
        tStart = max(bottomFar, 0.0);
    }
    else if (hitBottom && bottomNear >= 0.0 && directionWS.y < 0.0)
    {
        tEnd = min(tEnd, bottomNear);
    }
    tStart *= 1000.0;
    tEnd *= 1000.0;
    return valid && tEnd > tStart;
}

float CloudLayerEntryDistance(float3 worldDir)
{
    float entry, exit;
    return CloudLayerSegment(worldDir, entry, exit) ? entry : -1.0;
}
// Composites the camera-space cloud buffer over a surface that lies beyond the cloud layer (camera inside or above it).
float3 AtmosphereApplyCloudsInFront(float3 color, float2 screenUV, float3 positionWS)
{
    float3 result = color;
    if (AtmosphereCloudsEnabled() && _AtmosphereCamera.w >= _AtmosphereRadii.z)
    {
        float3 toSurface = positionWS - _AtmosphereCamera.xyz;
        float distance = length(toSurface);
        float entry = CloudLayerEntryDistance(toSurface / max(distance, 1e-4));
        if (entry >= 0.0 && distance >= entry)
        {
            float4 clouds = SAMPLE_TEXTURE2D_LOD(_CloudBuffer, sampler_CloudBuffer, saturate(screenUV), 0);
            result = color * clouds.a + clouds.rgb;
        }
    }
    return result;
}
// Sunlight attenuation by the cloud layer, 1 = unshadowed.
float AtmosphereCloudShadow(float3 positionWS)
{
    float result = 1.0;
    if (_CloudShadowParams.w > 0.0)
    {
        float2 local = mul(_CloudShadowWorldToLight, float4(positionWS, 1.0)).xy;
        float2 uv = local * _CloudShadowParams.x - _CloudShadowParams.yz + 0.5;
        float shadow = SAMPLE_TEXTURE2D_LOD(_CloudShadowMap, sampler_CloudShadowMap, saturate(uv), 0).r;
        result = lerp(1.0, shadow, _CloudShadowParams.w);
    }
    return result;
}
#endif
