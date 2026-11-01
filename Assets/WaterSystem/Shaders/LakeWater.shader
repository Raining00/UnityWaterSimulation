Shader "WaterSystem/Lake"
{
    Properties
    {
        [NoScaleOffset] _NormalMap("Water normal", 2D) = "bump" {}
        [NoScaleOffset] _FoamMask("Foam mask", 2D) = "white" {}
        [HideInInspector] _FoamNormal("Legacy foam normal", 2D) = "bump" {}
        _ShallowColor("Shallow color", Color) = (0,0.754717,0.47389516,1)
        _DepthColor("Depth color (alpha controls refraction overlay)", Color) = (0,0.47883922,0.5283019,1)
        _CausticsColor("Caustics color", Color) = (0,1,0.7731986,1)
        _Depth("Depth", Float) = 13.3
        _EdgesFade("Intersection fade", Float) = 0.01
        _DistanceFade("Distance fade length", Float) = 500
        _DistanceFadeOffset("Distance fade start", Float) = 1000
        _NormalPower("Normal strength", Range(0,1)) = 0.39
        _NormalScale("Normal scale", Float) = 2
        _NormalSpeed("Normal speed", Float) = 0.5
        _NormalDirection("Normal directions", Vector) = (-1,5,2.7,-0.2)
        _RefractionPower("Refraction strength", Range(0,5)) = 5
        _FoamDistance("Shore foam distance", Float) = 1
        _FoamPower("Shore foam strength", Range(0,1)) = 0.1
        _FoamScale("Foam scale", Float) = 5
        _FoamSpeed("Foam speed", Float) = 0.5
        _CausticsScale("Caustics scale", Float) = 0.97
        _CausticsSpeed("Caustics speed", Float) = 1.5
        _CausticsOffset("Caustics light offset", Float) = 3.5
        _Metallic("Metallic", Range(0,1)) = 0.224
        _Smoothness("Smoothness", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "UniversalMaterialType"="Lit" }
        Pass
        {
            Name "LakeForward"
            Tags { "LightMode"="UniversalForward" }
            // The reference water writes depth in its transparent forward pass.
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK

            #define _SURFACE_TYPE_TRANSPARENT 1
            #define _NORMALMAP 1
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_FoamMask); SAMPLER(sampler_FoamMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DepthColor;
                float4 _CausticsColor;
                float4 _NormalDirection;
                float _Depth;
                float _EdgesFade;
                float _DistanceFade;
                float _DistanceFadeOffset;
                float _NormalPower;
                float _NormalScale;
                float _NormalSpeed;
                float _RefractionPower;
                float _FoamDistance;
                float _FoamPower;
                float _FoamScale;
                float _FoamSpeed;
                float _CausticsScale;
                float _CausticsSpeed;
                float _CausticsOffset;
                float _Metallic;
                float _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                half4 fogAndVertexLight : TEXCOORD3;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 4);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);

                // Use the mesh tangent and its handedness, as the original Plane does.
                float3 tangent = TransformObjectToWorldDir(input.tangentOS.xyz, false);
                if (dot(tangent, tangent) < 1e-6)
                {
                    float3 axis = abs(output.normalWS.y) < 0.98 ? float3(0,1,0) : float3(0,0,1);
                    tangent = cross(axis, output.normalWS);
                }
                float sign = (input.tangentOS.w < 0 ? -1.0 : 1.0) * GetOddNegativeScale();
                output.tangentWS = float4(SafeNormalize(tangent), sign);
                output.fogAndVertexLight = half4(ComputeFogFactor(position.positionCS.z), VertexLighting(position.positionWS, output.normalWS));
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                return output;
            }

            float3 ReadWaterNormal(float2 worldXZ, float time)
            {
                float2 uv = worldXZ * (_NormalScale / 100.0);
                float speed = _NormalSpeed / 100.0;
                float3 a = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap,
                    uv + _NormalDirection.xy * speed * time), _NormalPower);
                float3 b = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap,
                    uv * (_NormalScale * 1.2) + _NormalDirection.zw * (speed * 2.0) * time), _NormalPower);
                a.z = lerp(1.0, a.z, saturate(_NormalPower));
                b.z = lerp(1.0, b.z, saturate(_NormalPower));
                return BlendNormal(a, b);
            }

            float2 CausticHash(float2 cell)
            {
                float2 seed = float2(dot(cell, float2(127.1,311.7)), dot(cell, float2(269.5,183.3)));
                return frac(sin(seed) * 43758.5453);
            }

            // The source uses nearest squared distance (F1), not Voronoi borders (F2-F1).
            float CausticCell(float2 uv, float time)
            {
                float2 cell = floor(uv);
                float2 local = frac(uv);
                float nearest = 8.0;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 offset = float2(x,y);
                        float2 site = sin(time + CausticHash(cell + offset) * 6.2831) * 0.5 + 0.5;
                        float2 delta = local - offset - site;
                        nearest = min(nearest, 0.5 * dot(delta, delta));
                    }
                }
                return nearest;
            }

            float ReadCaustics(float3 bottomWS, float time)
            {
                float bottomHeightOS = TransformWorldToObject(bottomWS).y;
                float2 uv = bottomWS.xz - _MainLightPosition.xz * bottomHeightOS * _CausticsOffset;
                return saturate(CausticCell(uv * (_CausticsScale * 0.5), time * _CausticsSpeed)
                              + CausticCell(uv * _CausticsScale, time * _CausticsSpeed));
            }

            float ReadShoreFoam(float2 worldXZ, float verticalDepth, float time)
            {
                float2 uv = worldXZ * (_FoamScale / 100.0);
                float motion = time * (_FoamSpeed / 100.0);
                float maskA = SAMPLE_TEXTURE2D(_FoamMask, sampler_FoamMask, uv + motion.xx).r;
                float maskB = SAMPLE_TEXTURE2D(_FoamMask, sampler_FoamMask, 1.0 - uv + motion.xx).r;
                float coverage = 1.0 - verticalDepth / max(_FoamDistance, 1e-4);
                float edge = 1.0 - verticalDepth / max(_FoamDistance * 0.1, 1e-4);
                // Limit the square root to the shore band to avoid invalid powers in deep water.
                float foam = edge + coverage * sqrt(max(coverage * maskA * maskB, 0.0));
                return saturate((verticalDepth / max(_EdgesFade, 1e-4)) * foam * _FoamPower);
            }

            float3 OverlayColor(float3 baseColor, float3 sceneColor)
            {
                return lerp(2.0 * baseColor * sceneColor,
                    1.0 - 2.0 * (1.0 - baseColor) * (1.0 - sceneColor), step(0.5, baseColor));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float rawDepth = SampleSceneDepth(screenUV);
                float surfaceEyeDepth = -TransformWorldToView(input.positionWS).z;
                float sceneEyeDepth = unity_OrthoParams.w > 0.5
                    ? LinearDepthToEyeDepth(rawDepth) : LinearEyeDepth(rawDepth, _ZBufferParams);
                float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float viewAngle = abs(viewDirection.y);
                float verticalDepth = abs(sceneEyeDepth - surfaceEyeDepth) * viewAngle;
                float shallowFactor = FastSRGBToLinear(1.0 - saturate(verticalDepth / max(_Depth, 1e-4)));

                float3 normalTS = ReadWaterNormal(input.positionWS.xz, _Time.y);
                float3 normal = SafeNormalize(input.normalWS);
                float3 bitangent = input.tangentWS.w * cross(normal, input.tangentWS.xyz);
                float3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS,
                    half3x3(input.tangentWS.xyz, bitangent, normal)));

                // Reconstruct the opaque lake bed. Caustics follow its geometry and light direction.
                #if UNITY_REVERSED_Z
                    float reconstructionDepth = rawDepth;
                    bool hasBottom = rawDepth > 1e-6;
                #else
                    float reconstructionDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                    bool hasBottom = rawDepth < 1.0 - 1e-6;
                #endif
                float caustics = 0.0;
                if (hasBottom)
                {
                    float3 bottomWS = ComputeWorldSpacePosition(screenUV, reconstructionDepth, UNITY_MATRIX_I_VP);
                    caustics = ReadCaustics(bottomWS, _Time.y);
                }

                float deepFactor = 1.0 - shallowFactor;
                float2 refractedUV = screenUV + normalTS.xy * (_RefractionPower / 10.0) * deepFactor * deepFactor;
                float3 refraction = saturate(SampleSceneColor(saturate(refractedUV)).rgb);
                float3 waterColor = lerp(_ShallowColor.rgb, _CausticsColor.rgb, caustics);
                waterColor = lerp(waterColor, _DepthColor.rgb, deepFactor);
                waterColor = saturate(lerp(waterColor, OverlayColor(waterColor, refraction), 1.0 - _DepthColor.a));
                // Match the original: shallow water reveals the untinted scene-color sample.
                waterColor = lerp(waterColor, refraction, shallowFactor);
                waterColor = saturate(waterColor + ReadShoreFoam(input.positionWS.xz, verticalDepth, _Time.y));

                float edgeFade = saturate(verticalDepth / max(_EdgesFade, 1e-4));
                float distanceFade = (surfaceEyeDepth - _ProjectionParams.y - _DistanceFadeOffset) / max(_DistanceFade, 1e-4);
                float alpha = edgeFade * saturate(1.0 - viewAngle * distanceFade);

                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.normalWS = normalWS;
                lighting.viewDirectionWS = viewDirection;
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    lighting.shadowCoord = float4(screenUV, 0.0, 1.0);
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                lighting.fogCoord = input.fogAndVertexLight.x;
                lighting.vertexLighting = input.fogAndVertexLight.yzw;
                lighting.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, normalWS);
                lighting.normalizedScreenSpaceUV = screenUV;
                lighting.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = waterColor;
                surface.normalTS = normalTS;
                surface.specular = half3(0.5,0.5,0.5);
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.occlusion = 1.0;
                surface.alpha = alpha;
                half4 color = UniversalFragmentPBR(lighting, surface);
                color.rgb = MixFog(color.rgb, lighting.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
