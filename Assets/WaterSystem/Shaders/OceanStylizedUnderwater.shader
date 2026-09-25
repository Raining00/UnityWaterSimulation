Shader "Hidden/WaterSystem/StylizedUnderwater"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "StylizedUnderwater"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X_FLOAT(_OceanSceneDepth);
            TEXTURE2D_ARRAY(_OceanDisplacement); SAMPLER(sampler_OceanDisplacement);
            TEXTURE2D(_StylizedNoiseTex); SAMPLER(sampler_StylizedNoiseTex);
            TEXTURE2D(_StylizedCausticTex); SAMPLER(sampler_StylizedCausticTex);

            int _OceanSimulationReady, _FFTCascadeCount, _FFTResolution;
            float4 _OceanDomainSizes, _OceanOrigin;
            float _OceanInfinite, _UnderwaterTime;
            float4 _UnderwaterSun, _UnderwaterSunDirection;
            float4 _StylizedNearColor, _StylizedFarColor, _StylizedWaterlineColor;
            float4 _StylizedFog, _StylizedDepthFog, _StylizedWaterline;
            float4 _StylizedDistortion, _StylizedCaustics, _StylizedShafts;
            float4 _StylizedCausticTex_TexelSize;

            float DomainWeight(float2 xz)
            {
                float2 offset = abs(xz - _OceanOrigin.xz);
                if (_OceanInfinite < 0.5)
                    return max(offset.x, offset.y) <= _OceanOrigin.w * 0.5 ? 1 : 0;
                return 1 - smoothstep(_OceanOrigin.w * 0.4, _OceanOrigin.w * 0.5,
                    max(offset.x, offset.y));
            }

            float3 Displacement(float2 xz)
            {
                float3 displacement = 0;
                if (_OceanSimulationReady != 0)
                {
                    [loop] for (int cascade = 0; cascade < _FFTCascadeCount; cascade++)
                        displacement += SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanDisplacement,
                            sampler_OceanDisplacement,
                            xz / _OceanDomainSizes[cascade] + 0.5 / _FFTResolution,
                            cascade, 0).xyz;
                }
                return displacement * DomainWeight(xz);
            }

            float SurfaceHeight(float2 worldXZ)
            {
                // Undo the FFT horizontal displacement before looking up its vertical height.
                float2 baseXZ = worldXZ;
                [unroll] for (int i = 0; i < 2; i++)
                    baseXZ = worldXZ - Displacement(baseXZ).xz;
                return _OceanOrigin.y + Displacement(baseXZ).y;
            }

            float3 CausticPattern(float2 worldXZ, float timeOffset, float mip)
            {
                float2 uv = worldXZ * _StylizedCaustics.x;
                // Match the surface's two moving projections. The source texture stores
                // offset red, green and blue filaments; thresholding its red channel alone
                // discarded most of the network on the seabed.
                float3 a = SAMPLE_TEXTURE2D_LOD(_StylizedCausticTex, sampler_StylizedCausticTex,
                    uv + timeOffset * float2(0.019, -0.014), mip).rgb;
                float3 b = SAMPLE_TEXTURE2D_LOD(_StylizedCausticTex, sampler_StylizedCausticTex,
                    uv * 0.8 + timeOffset * float2(-0.011, 0.017), mip).rgb;
                return min(a, b) * 2;
            }

            float3 LightShafts(float3 start, float3 direction, float lengthInWater, float fog)
            {
                if (_StylizedShafts.x <= 0 || _UnderwaterSunDirection.y <= 0.05 || fog <= 0)
                    return 0;
                float span = min(lengthInWater, _StylizedShafts.z);
                float3 samplePosition = start + direction * (span * 0.5);
                float depth = max(0, _OceanOrigin.y - samplePosition.y);
                float sunY = max(_UnderwaterSunDirection.y, 0.1);
                // The projected coordinate is constant along a sun ray. A single broad,
                // spatially filtered pattern gives continuous beams; distance controls
                // their integrated scattering rather than exposing depth-march slices.
                float2 entryXZ = samplePosition.xz +
                    _UnderwaterSunDirection.xz * (depth / sunY);
                float2 uv = entryXZ * (_StylizedCaustics.x * _StylizedShafts.y) +
                    _UnderwaterTime * _StylizedCaustics.w * float2(0.019, -0.014);
                // Blur across a fraction of the pattern period, not a fixed texel radius. Twelve
                // texels barely dents a 1024px network, which left this term as a hard stencil of
                // the caustic's cell walls instead of broad light.
                float2 filterWidth = float2(0.16, 0.16);
                float3 pattern =
                    SAMPLE_TEXTURE2D_LOD(_StylizedCausticTex, sampler_StylizedCausticTex,
                        uv + float2(-filterWidth.x, -filterWidth.y), 0).rgb +
                    SAMPLE_TEXTURE2D_LOD(_StylizedCausticTex, sampler_StylizedCausticTex,
                        uv + float2(filterWidth.x, -filterWidth.y), 0).rgb +
                    SAMPLE_TEXTURE2D_LOD(_StylizedCausticTex, sampler_StylizedCausticTex,
                        uv + float2(-filterWidth.x, filterWidth.y), 0).rgb +
                    SAMPLE_TEXTURE2D_LOD(_StylizedCausticTex, sampler_StylizedCausticTex,
                        uv + filterWidth, 0).rgb;
                // The source network is sparse and mostly dark, so a hard smoothstep between two
                // thresholds collapsed it into a binary outline of its own cells - the wrong
                // pattern that showed up as pale honeycomb over the whole view. A gentle gain
                // keeps it a soft low-contrast field, which is what actually reads as light.
                float beam = saturate(dot(pattern * 0.25, float3(0.299, 0.587, 0.114)) * 4);
                float scattering = (1 - exp(-span * 0.08)) * exp(-depth * 0.08);
                // Distance fog saturates at 1 and so provides no falloff at range; without this
                // the beams keep painting the far water instead of living in the near column.
                float reach = 1 - saturate(lengthInWater / max(1, _StylizedShafts.z));
                return _UnderwaterSun.rgb * (_StylizedShafts.x * 0.4 * fog * beam * scattering *
                    reach * DomainWeight(entryXZ));
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                #if UNITY_REVERSED_Z
                    const float nearDepth = 1;
                #else
                    const float nearDepth = UNITY_NEAR_CLIP_VALUE;
                #endif
                float3 start = ComputeWorldSpacePosition(uv, nearDepth, UNITY_MATRIX_I_VP);
                float signedDepth = SurfaceHeight(start.xz) - start.y;
                float width = max(_StylizedWaterline.x, fwidth(signedDepth));
                float wet = smoothstep(-width, width, signedDepth);
                float2 bounds = abs(start.xz - _OceanOrigin.xz);
                float inside = _OceanInfinite > 0.5 ||
                    max(bounds.x, bounds.y) <= _OceanOrigin.w * 0.5 ? 1 : 0;
                wet *= inside;
                float lineMask = (1 - smoothstep(width * 0.4, width * 2.2,
                    abs(signedDepth))) * _StylizedWaterline.y * inside;

                float raw = SAMPLE_TEXTURE2D_X_LOD(_OceanSceneDepth, sampler_PointClamp, uv, 0).r;
                #if UNITY_REVERSED_Z
                    float deviceDepth = raw;
                    bool hasGeometry = raw > 0.000001;
                #else
                    float deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                    bool hasGeometry = raw < 0.999999;
                #endif
                float3 end = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
                float3 ray = end - start;
                float rayLength = max(length(ray), 0.0001);
                float3 direction = ray / rayLength;
                float lengthInWater = min(rayLength, _StylizedFog.z);
                if (_OceanInfinite < 0.5)
                {
                    float2 side = _OceanOrigin.xz + sign(direction.xz) * _OceanOrigin.w * 0.5;
                    float2 exitDistance = (side - start.xz) /
                        float2(abs(direction.x) > 1e-6 ? direction.x : 1e-6,
                               abs(direction.z) > 1e-6 ? direction.z : 1e-6);
                    exitDistance = lerp(float2(1e8, 1e8), exitDistance,
                        step(1e-6, abs(direction.xz)));
                    lengthInWater = min(lengthInWater, max(0, min(exitDistance.x, exitDistance.y)));
                }

                // The image warp is world-anchored; it never moves the depth or waterline tests.
                float2 warpPosition = start.xz + direction.xz * min(lengthInWater, 25);
                float2 flow = float2(_UnderwaterTime * _StylizedDistortion.z,
                    -_UnderwaterTime * _StylizedDistortion.z * 0.71);
                float2 noiseUV = warpPosition * _StylizedDistortion.y;
                float noiseA = SAMPLE_TEXTURE2D(_StylizedNoiseTex, sampler_StylizedNoiseTex,
                    noiseUV + flow).r;
                float noiseB = SAMPLE_TEXTURE2D(_StylizedNoiseTex, sampler_StylizedNoiseTex,
                    noiseUV * 1.37 - flow.yx).r;
                float reach = saturate(lengthInWater / _StylizedDistortion.w);
                float2 warp = (float2(noiseA, noiseB) - 0.5) *
                    (_StylizedDistortion.x * reach * wet);

                // Resolve derivatives before the waterline's divergent early-out.
                float3 normal = SafeNormalize(cross(ddy(end), ddx(end)));
                normal *= dot(normal, -direction) < 0 ? -1 : 1;
                float footprint = max(length(ddx(end.xz)), length(ddy(end.xz)));
                float causticMip = max(0, log2(max(footprint * _StylizedCaustics.x *
                    _StylizedCausticTex_TexelSize.z, 1)));

                if (wet <= 0 && lineMask <= 0) return original;
                float3 sceneColor = original.rgb;
                if (_StylizedDistortion.x > 0)
                    sceneColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp,
                        clamp(uv + warp, 0.001, 0.999), 0).rgb;

                float3 samplePoint = start + direction * lengthInWater;
                float belowSurface = max(0, SurfaceHeight(samplePoint.xz) - samplePoint.y);
                if (hasGeometry && rayLength <= _StylizedFog.z &&
                    belowSurface > 0.25 && _StylizedCaustics.y > 0)
                {
                    float3 pattern = CausticPattern(end.xz,
                        _UnderwaterTime * _StylizedCaustics.w, causticMip);
                    float fade = exp(-belowSurface / _StylizedCaustics.z);
                    float3 bounce = 0.15 + 0.5 * sceneColor;
                    sceneColor += bounce * _UnderwaterSun.rgb * pattern *
                        _StylizedCaustics.y * fade * saturate(normal.y);
                }

                float distanceFog = 1 - exp(-max(0, lengthInWater - _StylizedFog.y) *
                    _StylizedFog.x);
                float cameraDepth = max(0, signedDepth);
                float deepFog = 1 - exp(-max(0, max(cameraDepth, belowSurface) - _StylizedDepthFog.x) *
                    _StylizedDepthFog.y);
                float fog = 1 - (1 - distanceFog) * (1 - deepFog);
                float3 fogColor = lerp(_StylizedNearColor.rgb, _StylizedFarColor.rgb,
                    saturate(distanceFog * 0.8 + deepFog * 0.7));
                fogColor *= _StylizedFog.w * lerp(1, _StylizedDepthFog.z, deepFog);
                fogColor *= 0.7 + 0.3 * _UnderwaterSun.rgb;
                float3 color = lerp(sceneColor, fogColor, fog);
                color += LightShafts(start, direction, lengthInWater, fog);
                color = lerp(original.rgb, color, wet);
                color = lerp(color, _StylizedWaterlineColor.rgb, lineMask);
                return half4(color, original.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
