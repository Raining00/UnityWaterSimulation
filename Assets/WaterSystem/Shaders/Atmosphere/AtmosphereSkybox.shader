Shader "WaterSystem/Atmosphere/Skybox"
{
    // Reads the global sky-view LUT written by AtmosphereRenderer. Without an active atmosphere it
    // falls back to a plain gradient so the material never renders black.
    Properties
    {
        _Exposure ("Exposure", Range(0, 8)) = 1
        _SunDiscIntensity ("Sun disc intensity", Range(0, 4)) = 1
        _FallbackHorizon ("Fallback horizon", Color) = (0.48, 0.65, 0.76, 1)
        _FallbackZenith ("Fallback zenith", Color) = (0.08, 0.27, 0.5, 1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off
        Pass
        {
            Name "AtmosphereSky"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "NightSkyCommon.hlsl"
            #include "MoonCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Exposure, _SunDiscIntensity;
                float4 _FallbackHorizon, _FallbackZenith;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 directionWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.directionWS = TransformObjectToWorldDir(input.positionOS.xyz, false);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 direction = normalize(input.directionWS);
                float3 color;
                if (AtmosphereEnabled())
                {
                    float footprint = max(length(ddx(direction)), length(ddy(direction)));
                    float3 background = NightSkyRadiance(direction, footprint, true)
                        + AtmosphereSunDisc(direction) * _SunDiscIntensity;
                    MoonSkyResult moon = EvaluateMoon(direction, footprint);
                    // Air scattering lies in front of the solid moon; stars and the sun lie behind it.
                    color = AtmosphereSkyRadiance(direction) + background * (1 - moon.coverage) + moon.radiance;
                    if (_AtmosphereWorld.w > 0.5)
                    {
                        // Cameras without the full-screen cloud pass (reflection probes) composite the panorama here.
                        float4 clouds = SampleCloudPanorama(direction);
                        color = color * clouds.a + clouds.rgb;
                    }
                }
                else color = lerp(_FallbackHorizon.rgb, _FallbackZenith.rgb, sqrt(saturate(direction.y)));
                return half4(color * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
