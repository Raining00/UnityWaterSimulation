Shader "WaterSystem/Atmosphere/Aerial Perspective"
{
    // Full-screen triangle issued by AtmosphereRenderer before transparents: applies the aerial perspective
    // LUT to every opaque pixel. Transparent surfaces handle their own aerial perspective.
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-30" }
        Pass
        {
            Name "AtmosphereAerialPerspective"
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
                float raw = SampleSceneDepth(uv);
                if (!HasOpaqueDepth(raw) || !AtmosphereEnabled()) return half4(0, 0, 0, 1);
                float3 positionWS = ComputeWorldSpacePosition(uv, RawToDevice(raw), UNITY_MATRIX_I_VP);
                float3 inscatter; float transmittance;
                AtmosphereAerialPerspective(uv, distance(positionWS, _WorldSpaceCameraPos), inscatter, transmittance);
                return half4(inscatter, transmittance);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
