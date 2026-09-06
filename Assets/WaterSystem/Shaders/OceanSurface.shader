Shader "WaterSystem/Ocean"
{
    Properties
    {
        _DeepColor ("Deep water scattering", Color) = (0.006,0.09,0.12,1)
        _ShallowColor ("Lit water scattering", Color) = (0.035,0.38,0.32,1)
        _Absorption ("RGB absorption per metre", Vector) = (0.22,0.065,0.035,0)
        _Scattering ("Scattering density", Range(0.001,1)) = 0.12
        _Roughness ("Surface roughness", Range(0.04,0.7)) = 0.16
        _NormalStrength ("Detail normal strength", Range(0,3)) = 1
        _RefractionStrength ("Screen refraction", Range(0,0.08)) = 0.018
        _ReflectionStrength ("Environment reflection", Range(0,2)) = 1
        _SkyHorizon ("Fallback sky horizon", Color) = (0.48,0.65,0.76,1)
        _SkyZenith ("Fallback sky zenith", Color) = (0.08,0.27,0.5,1)
        _SubsurfaceStrength ("Wave backlighting", Range(0,3)) = 0.65
        _FoamTex ("Foam detail (KWS2)", 2D) = "white" {}
        _FoamColor ("Foam color", Color) = (0.87,0.94,0.93,1)
        _FoamScale ("Foam detail repeats per metre", Float) = 0.5
        _FoamStrength ("Foam opacity", Range(0,3)) = 1.5
        _CausticTex ("Caustic pattern (KWS2)", 2D) = "black" {}
        _CausticScale ("Caustic repeats per metre", Float) = 0.12
        _CausticStrength ("Caustic brightness", Range(0,5)) = 0.6
        _CausticDepth ("Caustic depth falloff (m)", Float) = 18
        [Toggle] _UseSceneTextures ("URP depth and opaque textures available", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-10" }
        Pass
        {
            Name "OceanForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            // Refraction is composed explicitly with the opaque color copy. Writing depth resolves overlapping waves.
            Blend One Zero
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex OceanVertex
            #pragma fragment OceanFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D_ARRAY(_OceanDisplacement); SAMPLER(sampler_OceanDisplacement);
            TEXTURE2D_ARRAY(_OceanNormals); SAMPLER(sampler_OceanNormals);
            TEXTURE2D_ARRAY(_OceanFoam); SAMPLER(sampler_OceanFoam);
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            TEXTURE2D(_CausticTex); SAMPLER(sampler_CausticTex);
            // Per-ocean values are supplied by MaterialPropertyBlock, not shared global shader state.
            int _OceanSimulationReady, _FFTCascadeCount, _FFTResolution;
            float4 _OceanDomainSizes, _OceanOrigin;
            float _OceanSimulationTime;
            float _OceanInfinite, _OceanHorizonExtent;
            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _ShallowColor, _Absorption, _SkyHorizon, _SkyZenith, _FoamColor;
                float _Scattering, _Roughness, _NormalStrength, _RefractionStrength, _ReflectionStrength;
                float _SubsurfaceStrength, _FoamScale, _FoamStrength, _CausticScale, _CausticStrength;
                float _CausticDepth, _UseSceneTextures;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 horizon : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float WaveWeight(float2 baseXZ)
            {
                if(_OceanInfinite<0.5)return 1;
                float2 distanceFromCenter=abs(baseXZ-_OceanOrigin.xz);
                return 1-smoothstep(_OceanOrigin.w*0.4,_OceanOrigin.w*0.5,max(distanceFromCenter.x,distanceFromCenter.y));
            }
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 baseXZ : TEXCOORD1;
                float fog : TEXCOORD2;
                nointerpolation float horizon : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings OceanVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.horizon=input.horizon.y;
                if(input.horizon.y>0.5)
                {
                    // Full-screen triangle. The fragment shader intersects each view ray with the
                    // water plane, so no 100 km world triangle crosses the near/far clip planes.
                    output.positionCS=float4(input.positionOS.xy,
                    #if UNITY_REVERSED_Z
                        0.000001,
                    #else
                        0.999999,
                    #endif
                        1);
                    output.positionWS=0;output.baseXZ=0;output.fog=0;
                    return output;
                }
                float3 position = TransformObjectToWorld(input.positionOS.xyz);
                output.baseXZ = position.xz;
                float waveWeight=WaveWeight(output.baseXZ);
                if (_OceanSimulationReady != 0 && waveWeight>0)
                {
                    [loop] for (int i=0; i<_FFTCascadeCount; i++)
                        position += SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanDisplacement,sampler_OceanDisplacement,
                            output.baseXZ / _OceanDomainSizes[i] + 0.5 / _FFTResolution,i,0).xyz*waveWeight;
                }
                output.positionWS = position;
                output.positionCS = TransformWorldToHClip(position);
                output.fog = ComputeFogFactor(output.positionCS.z);
                if(_OceanInfinite>0.5)
                {
                    // Keep the true XY/W perspective horizon, but place distant WATER inside the far plane.
                    // Clamp all patches too, so a short camera far clip cannot open a gap before the analytical horizon.
                    #if UNITY_REVERSED_Z
                        output.positionCS.z=max(output.positionCS.z,output.positionCS.w*0.000001);
                    #else
                        output.positionCS.z=min(output.positionCS.z,output.positionCS.w*0.999999);
                    #endif
                }
                return output;
            }

            float RawToDevice(float raw)
            {
                #if UNITY_REVERSED_Z
                    return raw;
                #else
                    return lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
                #endif
            }
            bool HasOpaqueDepth(float raw)
            {
                #if UNITY_REVERSED_Z
                    return raw > 0.000001;
                #else
                    return raw < 0.999999;
                #endif
            }
            float3 DepthPosition(float2 uv, float raw) { return ComputeWorldSpacePosition(uv,RawToDevice(raw),UNITY_MATRIX_I_VP); }
            float Fresnel(float cosine) { return 0.02037 + 0.97963 * pow(1-saturate(cosine),5); }
            float GGX(float3 n, float3 v, float3 l, float roughness)
            {
                float3 h = SafeNormalize(v+l);
                float nl = saturate(dot(n,l)), nv = saturate(dot(n,v)), nh = saturate(dot(n,h));
                float a = roughness*roughness, a2 = a*a;
                float denom = nh*nh*(a2-1)+1;
                float d = a2 / max(PI*denom*denom,1e-7);
                float k = (roughness+1)*(roughness+1)*0.125;
                float g = (nl / max(nl*(1-k)+k,1e-4)) * (nv / max(nv*(1-k)+k,1e-4));
                return min(30, d*g*Fresnel(dot(v,h)) / max(4*nv,1e-4));
            }
            struct FragmentOutput { half4 color : SV_Target; float depth : SV_Depth; };
            FragmentOutput OceanFragment(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                bool analyticalHorizon=input.horizon>0.5;
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float3 positionWS=input.positionWS;
                float2 baseXZ=input.baseXZ;
                float fog=input.fog;
                if(analyticalHorizon)
                {
                    #if UNITY_REVERSED_Z
                        const float nearDepth=1,farDepth=0;
                    #else
                        const float nearDepth=UNITY_NEAR_CLIP_VALUE,farDepth=1;
                    #endif
                    float3 rayStart=ComputeWorldSpacePosition(screenUV,nearDepth,UNITY_MATRIX_I_VP);
                    float3 rayEnd=ComputeWorldSpacePosition(screenUV,farDepth,UNITY_MATRIX_I_VP);
                    float3 ray=rayEnd-rayStart;
                    if(abs(ray.y)<1e-7)discard;
                    float intersection=(_OceanOrigin.y-rayStart.y)/ray.y;
                    if(intersection<=0)discard;
                    positionWS=rayStart+ray*intersection;
                    // Detailed quadtree patches own the root square, including its far-clipped part.
                    float2 rootDistance=abs(positionWS.xz-_OceanOrigin.xz);
                    // A tiny overlap lets the depth test close sub-pixel rasterization cracks at
                    // the independently drawn quadtree boundary. Waves are already almost flat here.
                    if(max(rootDistance.x,rootDistance.y)<=_OceanOrigin.w*0.499)discard;
                    float2 fromCamera=positionWS.xz-_WorldSpaceCameraPos.xz;
                    float horizontalDistance=length(fromCamera);
                    if(horizontalDistance>_OceanHorizonExtent)
                        positionWS.xz=_WorldSpaceCameraPos.xz+fromCamera*(_OceanHorizonExtent/horizontalDistance);
                    baseXZ=positionWS.xz;
                    fog=ComputeFogFactor(TransformWorldToHClip(positionWS).z);
                }
                float2 slope = 0;
                float foam = 0;
                float waveWeight=WaveWeight(baseXZ);
                // Evaluate derivatives before the varying horizon branch; the FFT arrays have no mips.
                float2 baseFootprint=max(abs(ddx(baseXZ)),abs(ddy(baseXZ)));
                float distanceToCamera = distance(positionWS,_WorldSpaceCameraPos);
                if (_OceanSimulationReady != 0 && waveWeight>0)
                {
                    [loop] for (int i=0; i<_FFTCascadeCount; i++)
                    {
                        float2 uv = baseXZ / _OceanDomainSizes[i] + 0.5 / _FFTResolution;
                        float4 sampleNormal = SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanNormals,sampler_OceanNormals,uv,i,0);
                        // Suppress subpixel normal sparkle with the actual UV footprint, independent of patch LOD.
                        float2 footprint = baseFootprint / _OceanDomainSizes[i];
                        float detail = 1 / (1+max(footprint.x,footprint.y)*_FFTResolution);
                        slope += -sampleNormal.xz / max(sampleNormal.y,0.15) * detail;
                        float f = SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanFoam,sampler_OceanFoam,uv,i,0).r;
                        foam = 1-(1-foam)*(1-f);
                    }
                }
                slope*=waveWeight;foam*=waveWeight;
                float3 n = normalize(float3(-slope.x*_NormalStrength,1,-slope.y*_NormalStrength));
                float faceSign = analyticalHorizon?(_WorldSpaceCameraPos.y>=_OceanOrigin.y?1:-1):IS_FRONT_VFACE(frontFace,1,-1);
                n *= faceSign;
                float3 v = GetWorldSpaceNormalizeViewDir(positionWS);
                float nv = saturate(dot(n,v));
                // Shadow coordinates and reflection probes lose useful precision tens of kilometres
                // from the origin. The analytical far surface uses stable unshadowed sun/fallback sky.
                Light sun;
                if(analyticalHorizon)sun=GetMainLight();
                else sun=GetMainLight(TransformWorldToShadowCoord(positionWS));
                float sunlight = sun.shadowAttenuation * sun.distanceAttenuation;
                float2 refractUV = screenUV;
                float thickness = 80;
                float3 background = _DeepColor.rgb;
                float3 bottomWS = positionWS - float3(0,80,0);
                bool hasBottom = false;
                if (_UseSceneTextures > 0.5 && !analyticalHorizon)
                {
                    float originalDepth = SampleSceneDepth(screenUV);
                    if (HasOpaqueDepth(originalDepth))
                    {
                        float3 opaqueWS = DepthPosition(screenUV,originalDepth);
                        thickness = min(100, distance(positionWS,opaqueWS));
                        float3 viewNormal = mul((float3x3)UNITY_MATRIX_V,n);
                        refractUV = clamp(screenUV + viewNormal.xy*_RefractionStrength*saturate(thickness*0.2),0.002,0.998);
                        float distortedDepth = SampleSceneDepth(refractUV);
                        float3 candidate = DepthPosition(refractUV,distortedDepth);
                        // Reject foreground silhouettes and samples above the current water surface.
                        if (!HasOpaqueDepth(distortedDepth) || dot(candidate-positionWS,v)>0 || candidate.y>positionWS.y+0.2)
                            refractUV = screenUV;
                        else opaqueWS = candidate;
                        bottomWS = opaqueWS;
                        thickness = min(100,distance(positionWS,bottomWS));
                        hasBottom = true;
                        background = SampleSceneColor(refractUV);
                    }
                }
                float3 transmittance = exp(-max(_Absorption.rgb,0.001)*thickness);
                float3 scatterColor = lerp(_ShallowColor.rgb,_DeepColor.rgb,1-exp(-thickness*_Scattering));
                float3 lighting = 0.25 + sun.color*(0.35+0.65*saturate(sun.direction.y))*sunlight;
                float3 refraction = background*transmittance + scatterColor*(1-transmittance)*lighting;
                if (hasBottom && faceSign>0 && _CausticStrength>0)
                {
                    float depthBelowWater = max(0,_OceanOrigin.y-bottomWS.y);
                    float2 cuv = bottomWS.xz*_CausticScale;
                    float t = _OceanSimulationTime*0.025;
                    float c1 = SAMPLE_TEXTURE2D(_CausticTex,sampler_CausticTex,cuv+float2(t,-t*0.7)).r;
                    float c2 = SAMPLE_TEXTURE2D(_CausticTex,sampler_CausticTex,cuv*1.17+float2(-t*0.6,t)).r;
                    float caustic = min(c1,c2)*2 * exp(-depthBelowWater/max(_CausticDepth,0.1));
                    refraction += background * sun.color * caustic * _CausticStrength * transmittance * sunlight;
                }
                float3 r = reflect(-v,n);
                float3 fallbackSky = lerp(_SkyHorizon.rgb,_SkyZenith.rgb,sqrt(saturate(r.y)));
                float3 environment=fallbackSky;
                if(!analyticalHorizon)
                {
                    environment = GlossyEnvironmentReflection(r,_Roughness,1);
                    environment = lerp(fallbackSky,environment,saturate(dot(environment,float3(1,1,1))*5));
                }
                float fresnel = Fresnel(nv);
                float3 color = lerp(refraction,environment*_ReflectionStrength,fresnel);
                color += sun.color*sunlight*GGX(n,v,sun.direction,_Roughness);
                float backlight = pow(saturate(dot(v,-sun.direction)),5) * (1-nv) * saturate(positionWS.y-_OceanOrigin.y+0.3);
                color += _ShallowColor.rgb*sun.color*backlight*_SubsurfaceStrength*sunlight;
                foam=saturate(foam*_FoamStrength);
                if(foam>0.0001)
                {
                    float2 foamUV = baseXZ * _FoamScale;
                    float textureFoam = SAMPLE_TEXTURE2D_LOD(_FoamTex,sampler_FoamTex,foamUV+_OceanSimulationTime*float2(0.008,0.004),0).r;
                    float textureFoam2 = SAMPLE_TEXTURE2D_LOD(_FoamTex,sampler_FoamTex,foamUV*0.63-_OceanSimulationTime*float2(0.003,0.006),0).r;
                    foam *= smoothstep(0.15,0.8,(textureFoam+textureFoam2)*0.5);
                    color = lerp(color,_FoamColor.rgb*(0.3+sun.color*saturate(dot(n,sun.direction))*sunlight),foam);
                }
                if (faceSign<0)
                    color = lerp(color,_DeepColor.rgb,1-exp(-distanceToCamera*0.04));
                FragmentOutput output;
                output.color=half4(MixFog(color,fog),1);
                // Vertex depth clamp keeps triangles crossing the far plane alive. Recompute fragment
                // depth from the true interpolated world position so those triangles still occlude
                // nearby opaque objects correctly, instead of interpolating the clamped corner depths.
                float4 clip=TransformWorldToHClip(positionWS);
                float deviceDepth=clip.z/clip.w;
                #if UNITY_REVERSED_Z
                    output.depth=max(deviceDepth,0.000001);
                #else
                    output.depth=min((deviceDepth-UNITY_NEAR_CLIP_VALUE)/(1-UNITY_NEAR_CLIP_VALUE),0.999999);
                #endif
                return output;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
