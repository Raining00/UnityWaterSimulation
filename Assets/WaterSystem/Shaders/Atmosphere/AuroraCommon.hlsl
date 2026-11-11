#ifndef WATERSYSTEM_AURORA_INCLUDED
#define WATERSYSTEM_AURORA_INCLUDED

// Artistic high-altitude curtains; altitude/colour layering is inspired by
// https://science.nasa.gov/sun/auroras/ rather than a magnetic-field simulation.
#include "AtmosphereCommon.hlsl"

TEXTURE2D(_AuroraNoise); SAMPLER(sampler_AuroraNoise);
float4 _AuroraParams;      // enabled, radiance scale, solar visibility, integration samples
float4 _AuroraLayer;       // bottom altitude km, height km, distance km, width km
float4 _AuroraShape;       // fold scale km, fold amplitude km, horizontal extent km, curtain count
float4 _AuroraDetail;      // fine-ray strength, curtain spacing km, noise texture size, max mip
float4 _AuroraFrame;       // sin/cos of azimuth, world anchor X/Z in km
float4 _AuroraAnimation;   // periodic animation phase, horizon fade sine, unused, unused
float4 _AuroraLowerColor, _AuroraUpperColor;
float _AuroraCameraVisible;

float AuroraSphereExit(float3 origin, float3 ray, float radius)
{
    float b = dot(origin, ray);
    return -b + sqrt(max(0, b*b - dot(origin,origin) + radius*radius));
}

float3 AuroraRadiance(float3 direction, float angularFootprint)
{
    if (_AuroraParams.x < 0.5 || _AuroraParams.y <= 0 || _AuroraParams.z <= 0 ||
        _AuroraCameraVisible < 0.5 || direction.y <= 0) return 0;
    float3 origin = AtmosphereCameraPlanetKm();
    float radius = _AtmosphereRadii.x;
    float cameraRadius = length(origin);
    // An upward view outside the layer cannot enter it. Inside the layer, start at the eye.
    if (cameraRadius >= radius+_AuroraLayer.x+_AuroraLayer.y) return 0;
    float start = cameraRadius < radius+_AuroraLayer.x
        ? AuroraSphereExit(origin,direction,radius+_AuroraLayer.x) : 0;
    float end = AuroraSphereExit(origin,direction,radius+_AuroraLayer.x+_AuroraLayer.y);
    float stepLength = (end-start)/_AuroraParams.w;
    float3 worldOffset = float3(_AtmosphereCamera.x*0.001-_AuroraFrame.z,0,
                                _AtmosphereCamera.z*0.001-_AuroraFrame.w);
    float2 rayXZ = float2(direction.x*_AuroraFrame.y-direction.z*_AuroraFrame.x,
                          direction.x*_AuroraFrame.x+direction.z*_AuroraFrame.y);
    float2 cameraXZ = float2(worldOffset.x*_AuroraFrame.y-worldOffset.z*_AuroraFrame.x,
                             worldOffset.x*_AuroraFrame.x+worldOffset.z*_AuroraFrame.y);
    // Conservative interval rejection avoids marching away from the curtain region.
    float2 a = cameraXZ+rayXZ*start, b = cameraXZ+rayXZ*end;
    float reach = _AuroraShape.y*2.25 + _AuroraDetail.y*2 + _AuroraLayer.w*4;
    if (max(a.y,b.y) < _AuroraLayer.z-reach || min(a.y,b.y) > _AuroraLayer.z+reach ||
        min(a.x,b.x) > _AuroraShape.z || max(a.x,b.x) < -_AuroraShape.z) return 0;

    float phase = _AuroraAnimation.x;
    float3 sum = 0;
    // Filter the narrow sheets over a ray segment to prevent skipped sheets and staircase bands.
    float segmentWidth = length(rayXZ)*stepLength;
    float width = sqrt(_AuroraLayer.w*_AuroraLayer.w + segmentWidth*segmentWidth*0.15);
    float energy = _AuroraLayer.w/width;
    [loop] for (int i=0;i<(int)_AuroraParams.w;i++)
    {
        float distanceKm = start+(i+0.5)*stepLength;
        float3 samplePosition = origin+direction*distanceKm;
        float altitude = (length(samplePosition)-radius-_AuroraLayer.x)/_AuroraLayer.y;
        float2 q = cameraXZ+rayXZ*distanceKm;
        float extent = 1-smoothstep(_AuroraShape.z*0.65,_AuroraShape.z,abs(q.x));
        if (extent <= 0) continue;
        float along = q.x/_AuroraShape.x;
        float footprint = max(1e-6,angularFootprint*distanceKm/_AuroraShape.x);
        float coarseMip = clamp(log2(max(1,footprint*0.13*_AuroraDetail.z)),0,_AuroraDetail.w);
        float2 coarse = SAMPLE_TEXTURE2D_LOD(_AuroraNoise,sampler_AuroraNoise,
            float2(along*0.13+phase,0.37),coarseMip).rg;
        float fineMip = clamp(log2(max(1,footprint*1.7*_AuroraDetail.z)),0,_AuroraDetail.w);
        float fine = SAMPLE_TEXTURE2D_LOD(_AuroraNoise,sampler_AuroraNoise,
            float2(along*1.7-phase,0.73),fineMip).g;
        float fold = (sin(along*1.6+phase*2*PI) + 0.35*sin(along*4.1-phase*4*PI)
            + (coarse.x-0.5)*1.3)*_AuroraShape.y;
        float density = 0;
        [loop] for (int curtain=0;curtain<(int)_AuroraShape.w;curtain++)
        {
            float offset = (curtain-(_AuroraShape.w-1)*0.5)*_AuroraDetail.y;
            float ripple = sin(along*2.7+curtain*1.7+phase*2*PI)*_AuroraShape.y*0.22;
            float d = (q.y-_AuroraLayer.z-fold-offset-ripple)/width;
            density += exp2(-d*d*1.442695)*energy;
        }
        float rayHeight = lerp(0.55,1,coarse.y);
        float h = altitude/max(rayHeight,0.1);
        float vertical = smoothstep(0,0.055,h)*pow(saturate(1-h),1.15);
        vertical *= 0.6+1.2*exp(-h*12);
        float rays = lerp(1,0.12+1.6*pow(saturate(fine),1.8),_AuroraDetail.x);
        float3 tint = lerp(_AuroraLowerColor.rgb,_AuroraUpperColor.rgb,smoothstep(0.25,0.9,h));
        sum += tint*(density*vertical*rays*extent*stepLength/_AuroraLayer.y);
    }
    float horizon = smoothstep(0,_AuroraAnimation.y,direction.y);
    float3 transmission = SampleTransmittance(_AtmosphereCamera.w,saturate(direction.y));
    return sum*transmission*(_AuroraParams.y*_AuroraParams.z*horizon);
}
#endif
