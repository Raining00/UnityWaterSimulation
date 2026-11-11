#ifndef WATERSYSTEM_METEOR_INCLUDED
#define WATERSYSTEM_METEOR_INCLUDED

#include "AtmosphereCommon.hlsl"

// Fixed-capacity, camera-independent celestial tracks; supplied once per atmosphere frame.
// Kept out of the ambient/reflection kernels so transient streaks do not change ocean lighting.
#define METEOR_MAX_TRACKS 16
float4 _MeteorParams; // exposed radiance, night visibility, track count, horizon fade sine
float4 _MeteorHeadColor, _MeteorTailColor, _MeteorSplitColor;
float _MeteorCameraVisible;
float4 _MeteorDirection[METEOR_MAX_TRACKS];
float4 _MeteorTangent[METEOR_MAX_TRACKS]; // xyz travel axis, w parabolic bend
float4 _MeteorBitangent[METEOR_MAX_TRACKS]; // xyz across axis, w stable random seed
float4 _MeteorTrack[METEOR_MAX_TRACKS]; // start x, end x, age seconds, branch slope
float4 _MeteorLife[METEOR_MAX_TRACKS]; // flight seconds, tail seconds, afterglow seconds, brightness
float4 _MeteorStyle[METEOR_MAX_TRACKS]; // width, head radius, colour blend, unused
float4 _MeteorGeometry[METEOR_MAX_TRACKS]; // parent half length, split x, unused, unused

float MeteorCurve(float x, float bend, float halfLength, float slope, float splitX)
{
    return bend * (1 - x * x / (halfLength * halfLength)) + slope * max(0, x - splitX);
}

float3 MeteorRadiance(float3 direction, float angularFootprint)
{
    if (_MeteorParams.x <= 0 || _MeteorParams.y <= 0 || _MeteorParams.z < 0.5 ||
        _MeteorCameraVisible < 0.5 || direction.y <= 0) return 0;
    float3 result = 0;
    [loop] for (int i = 0; i < min((int)_MeteorParams.z, METEOR_MAX_TRACKS); i++)
    {
        float facing = dot(direction, _MeteorDirection[i].xyz);
        if (facing < 0.35) continue;
        float2 p = float2(dot(direction, _MeteorTangent[i].xyz),
                         dot(direction, _MeteorBitangent[i].xyz)) / facing;
        float4 track = _MeteorTrack[i], life = _MeteorLife[i], style = _MeteorStyle[i];
        float halfLength = _MeteorGeometry[i].x, splitX = _MeteorGeometry[i].y;
        float bend = _MeteorTangent[i].w;
        float age = track.z;
        if (age < 0 || age >= life.x + life.z) continue;
        float headX = lerp(track.x, track.y, saturate(age / life.x));
        float headY = MeteorCurve(headX, bend, halfLength, track.w, splitX);
        // Conservative angular rectangle rejects most pixels before the profile evaluation.
        float margin = max(style.y * 15, style.x * 14);
        float yBound = abs(bend) + abs(track.w) * (track.y - track.x) + margin;
        if (p.x < track.x - margin || p.x > headX + margin || abs(p.y) > yBound) continue;

        // Gnomonic projection gives one continuous world-oriented curve across skybox faces.
        // Two closest-point Newton steps avoid the stair-stepping of segmented billboard trails.
        float x = clamp(p.x, track.x, headX);
        [unroll] for (int j = 0; j < 2; j++)
        {
            float y = MeteorCurve(x, bend, halfLength, track.w, splitX);
            float gradient = -2 * bend * x / (halfLength * halfLength) + (x > splitX ? track.w : 0);
            float curvature = -2 * bend / (halfLength * halfLength);
            x = clamp(x - ((x - p.x) + (y - p.y) * gradient) /
                      max(0.25, 1 + gradient * gradient + (y - p.y) * curvature), track.x, headX);
        }
        float y = MeteorCurve(x, bend, halfLength, track.w, splitX);
        float distanceToTail = length(p - float2(x, y));
        float depositedAt = saturate((x - track.x) / max(track.y - track.x, 1e-5)) * life.x;
        float tailAge = max(0, age - depositedAt);
        float fade = 1 - smoothstep(life.x, life.x + life.z, age);
        float tailFade = exp2(-2.5 * tailAge / life.y) * fade;
        float aa = max(angularFootprint / max(facing * facing, 0.1), 1e-5);
        float width = style.x * (0.75 + min(tailAge / life.y, 2) * 1.6);
        float softWidth = sqrt(width * width + aa * aa * 0.35);
        float profile = exp2(-2.5 * distanceToTail * distanceToTail / (softWidth * softWidth));
        // Energy-preserving pixel footprint broadening prevents thin ribbons from blinking.
        profile *= width / softWidth;
        float sheath = exp2(-2.2 * distanceToTail * distanceToTail / (width * width * 18 + aa * aa));
        float filament = 0.78 + 0.14 * sin(x * 680 + _MeteorBitangent[i].w) +
                        0.08 * sin(x * 1530 - _MeteorBitangent[i].w * 2.3);
        // Fade away at the birth point instead of leaving a hard rectangular tail cap.
        float birth = smoothstep(track.x, track.x + (track.y - track.x) * 0.08, x);
        float3 tailTint = lerp(_MeteorTailColor.rgb, _MeteorSplitColor.rgb,
                              saturate(style.z + tailAge / life.y * 0.23));
        float3 tail = (tailTint * (profile * filament + sheath * 0.13) +
                       _MeteorHeadColor.rgb * profile * 0.2 * exp2(-tailAge * 3)) * tailFade * birth;

        float2 headOffset = p - float2(headX, headY);
        float headRadius = style.y;
        float resolvedRadius = sqrt(headRadius * headRadius + aa * aa * 0.4);
        float headDistance2 = dot(headOffset, headOffset);
        float core = exp2(-3 * headDistance2 / (resolvedRadius * resolvedRadius)) *
                     headRadius * headRadius / (resolvedRadius * resolvedRadius);
        float halo = exp2(-1.8 * headDistance2 / (headRadius * headRadius * 16 + aa * aa));
        // Small directional diffraction spikes stay attached to the head, never the screen.
        float2 q = abs(headOffset) / max(headRadius, 1e-5);
        float spikes = exp2(-q.x * 1.3 - q.y * 16) + exp2(-q.y * 2 - q.x * 16);
        float ignition = smoothstep(0, min(life.x * 0.1, 0.4), age);
        float headFade = 1 - smoothstep(life.x * 0.94, life.x, age);
        float pulse = 0.94 + 0.06 * sin(age * 19 + _MeteorBitangent[i].w);
        float3 headTint = lerp(_MeteorHeadColor.rgb, _MeteorSplitColor.rgb, style.z * 0.28);
        float3 head = (headTint * (core * 5 + halo * 0.24 + spikes * 0.09)) * ignition * headFade * pulse;
        result += (tail + head) * life.w;
    }
    float horizon = smoothstep(0, _MeteorParams.w, direction.y);
    // Celestial emission passes through air once; the later cloud composite supplies cloud occlusion.
    float3 radiance = result * SampleTransmittance(_AtmosphereCamera.w, saturate(direction.y)) *
                     (_MeteorParams.x * _MeteorParams.y * horizon);
    // Leave headroom for sky exposure and the other HDR sky layers when artists use extreme colours.
    return min(radiance, 6000.0);
}

#endif
