Shader "WaterSystem/Atmosphere/Cloud Composite"
{
    // Full-screen triangle issued by AtmosphereRenderer before transparents: upsamples the temporally
    // filtered cloud buffer onto the sky and onto opaque surfaces that lie beyond the cloud layer.
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20" }
        Pass
        {
            Name "CloudComposite"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend One SrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "AtmosphereCommon.hlsl"
            #include "AtmosphereFullscreen.hlsl"
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                if (!AtmosphereCloudsEnabled()) return half4(0, 0, 0, 1);
                #if UNITY_REVERSED_Z
                    const float farDepth = 0;
                #else
                    const float farDepth = 1;
                #endif
                float3 farWS = ComputeWorldSpacePosition(uv, farDepth, UNITY_MATRIX_I_VP);
                float3 direction = normalize(farWS - _WorldSpaceCameraPos);
                float entry = CloudLayerEntryDistance(direction);
                if (entry < 0.0) return half4(0, 0, 0, 1);
                float raw = SampleSceneDepth(uv);
                if (HasOpaqueDepth(raw))
                {
                    // Opaque geometry in front of the layer hides the clouds; geometry beyond it is covered.
                    float3 positionWS = ComputeWorldSpacePosition(uv, RawToDevice(raw), UNITY_MATRIX_I_VP);
                    if (distance(positionWS, _WorldSpaceCameraPos) < entry) return half4(0, 0, 0, 1);
                }
                float4 clouds = SAMPLE_TEXTURE2D_LOD(_CloudBuffer, sampler_CloudBuffer, uv, 0);
                return half4(clouds.rgb, clouds.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
