Shader "WaterSystem/OceanStylized"
{
    // All appearance values are supplied by OceanStylizedSettings; no material asset is needed.
    Properties
    {
        [HideInInspector] _DetailNormal ("Detail normal", 2D) = "bump" {}
        [HideInInspector] _FoamTex ("Whitecaps", 2D) = "white" {}
        [HideInInspector] _ShoreNoise ("Intersection noise", 2D) = "gray" {}
        [HideInInspector] _CausticTex ("Caustics", 2D) = "black" {}
        [HideInInspector] _SkyTex ("Fallback cloud sky", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-10" }
        Pass
        {
            Name "StylizedOceanForward"
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
            TEXTURE2D(_DetailNormal); SAMPLER(sampler_DetailNormal);
            TEXTURE2D(_ShoreNoise); SAMPLER(sampler_ShoreNoise);
            TEXTURE2D(_SkyTex); SAMPLER(sampler_SkyTex);
            // Per-ocean values are supplied by MaterialPropertyBlock, not shared global shader state.
            int _OceanSimulationReady, _FFTCascadeCount, _FFTResolution;
            float4 _OceanDomainSizes, _OceanOrigin;
            float _OceanSimulationTime;
            float _OceanInfinite, _OceanHorizonExtent;
            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _ShallowColor, _TurquoiseColor, _HorizonColor, _SkyHorizon, _SkyZenith, _FoamColor;
                float4 _WaterDepth, _NormalSpeed, _ShoreFoam;
                float _HorizonBlendDistance, _NormalStrength, _DetailNormalStrength, _NormalScale, _DetailFadeDistance;
                float _ReflectionStrength, _FresnelPower, _SunHighlight, _Roughness, _RefractionStrength, _SkyTextureStrength;
                float _FoamScale, _FoamStrength, _CausticScale, _CausticStrength, _CausticDepth, _UseSceneTextures;
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
                float t = _OceanSimulationTime;
                float detailFade = (1-smoothstep(_DetailFadeDistance*0.25,_DetailFadeDistance,distanceToCamera))*waveWeight;
                float2 normalUV = baseXZ*_NormalScale;
                float3 detailA = UnpackNormal(SAMPLE_TEXTURE2D(_DetailNormal,sampler_DetailNormal,normalUV+t*_NormalSpeed.xy));
                float3 detailB = UnpackNormal(SAMPLE_TEXTURE2D(_DetailNormal,sampler_DetailNormal,normalUV*0.73-t*_NormalSpeed.yx*0.8));
                // Both maps use world XZ, so patch edges and LOD changes share the same ripples.
                float2 detailSlope = (detailA.xy/max(detailA.z,0.2)+detailB.xy/max(detailB.z,0.2))*0.5;
                float3 n = normalize(float3(-slope.x*_NormalStrength+detailSlope.x*_DetailNormalStrength*detailFade,
                    1,-slope.y*_NormalStrength+detailSlope.y*_DetailNormalStrength*detailFade));
                float3 v = GetWorldSpaceNormalizeViewDir(positionWS);
                float nv = saturate(dot(n,v));
                Light sun;
                if(analyticalHorizon)sun=GetMainLight();
                else sun=GetMainLight(TransformWorldToShadowCoord(positionWS));
                float sunlight = sun.shadowAttenuation*sun.distanceAttenuation;
                float3 lighting = 0.55+sun.color*(0.30+0.15*saturate(dot(n,sun.direction)))*sunlight;
                float3 background = _DeepColor.rgb;
                float3 bottomWS = positionWS-float3(0,100,0);
                float thickness = 100, verticalDepth = 100, intersectionDepth = 100;
                bool hasBottom = false;
                if(_UseSceneTextures>0.5 && !analyticalHorizon)
                {
                    float raw = SampleSceneDepth(screenUV);
                    if(HasOpaqueDepth(raw))
                    {
                        bottomWS = DepthPosition(screenUV,raw);
                        // Preserve the undistorted shoreline; refracting its mask makes it detach from rocks.
                        intersectionDepth = max(0,positionWS.y-bottomWS.y);
                        hasBottom = dot(bottomWS-positionWS,v)<=0 && bottomWS.y<=positionWS.y+0.05;
                        if(hasBottom)
                        {
                            float3 viewNormal = mul((float3x3)UNITY_MATRIX_V,n);
                            float2 refractUV = clamp(screenUV+viewNormal.xy*_RefractionStrength*saturate(intersectionDepth),0.002,0.998);
                            float distortedRaw = SampleSceneDepth(refractUV);
                            float3 candidate = DepthPosition(refractUV,distortedRaw);
                            if(!HasOpaqueDepth(distortedRaw) || dot(candidate-positionWS,v)>0 || candidate.y>positionWS.y)
                                refractUV = screenUV;
                            else bottomWS = candidate;
                            thickness = min(100,distance(positionWS,bottomWS));
                            verticalDepth = max(0,positionWS.y-bottomWS.y);
                            background = SampleSceneColor(refractUV);
                        }
                    }
                }
                // Independent vertical and view-path extinction keeps shallows transparent from above
                // while long grazing rays acquire the saturated color of the reference ocean.
                float3 waterColor = lerp(_ShallowColor.rgb,_TurquoiseColor.rgb,saturate(verticalDepth/_WaterDepth.x));
                waterColor = lerp(waterColor,_DeepColor.rgb,smoothstep(_WaterDepth.x,_WaterDepth.y,verticalDepth));
                float density = hasBottom ? saturate(1-exp(-verticalDepth*_WaterDepth.z-thickness*_WaterDepth.w)) : 1;
                if(hasBottom && _CausticStrength>0)
                {
                    float2 cuv = bottomWS.xz*_CausticScale;
                    float c1 = SAMPLE_TEXTURE2D(_CausticTex,sampler_CausticTex,cuv+t*float2(0.019,-0.014)).r;
                    float c2 = SAMPLE_TEXTURE2D(_CausticTex,sampler_CausticTex,cuv*0.8+t*float2(-0.011,0.017)).r;
                    float caustic = min(c1,c2)*2*exp(-verticalDepth/_CausticDepth)*saturate(verticalDepth*2);
                    background += background*sun.color*caustic*_CausticStrength*sunlight;
                }
                float3 color = lerp(background,waterColor*lighting,density);
                float3 r = reflect(-v,n);
                float3 sky = lerp(_SkyHorizon.rgb,_SkyZenith.rgb,sqrt(saturate(r.y)));
                // Same latitude-longitude convention as Unity's panoramic skybox.
                float2 skyUV = float2(0.5-atan2(r.z,r.x)/(2*PI),1-acos(clamp(r.y,-1,1))/PI);
                float3 cloudSky = SAMPLE_TEXTURE2D_LOD(_SkyTex,sampler_SkyTex,skyUV,_Roughness*5).rgb;
                sky = lerp(sky,cloudSky,_SkyTextureStrength);
                float3 environment = sky;
                if(!analyticalHorizon)
                {
                    float3 probe = GlossyEnvironmentReflection(r,_Roughness,1);
                    environment = lerp(sky,probe,saturate(dot(probe,float3(1,1,1))*5));
                }
                float reflection = (0.04+0.96*pow(1-nv,_FresnelPower))*_ReflectionStrength;
                color = lerp(color,environment,reflection*saturate(density*4));
                color += sun.color*sunlight*min(1.5,GGX(n,v,sun.direction,_Roughness))*_SunHighlight*saturate(density*4);
                float horizonBlend = 1-exp(-distanceToCamera/_HorizonBlendDistance);
                color = lerp(color,_HorizonColor.rgb,horizonBlend*0.8);

                // Keep this whitecap mask identical to OceanSpray.compute: spray originates in FFT
                // compression foam only, never in the screen-space shore decoration below.
                float2 foamUV = baseXZ*_FoamScale;
                float f1 = SAMPLE_TEXTURE2D_LOD(_FoamTex,sampler_FoamTex,foamUV+t*float2(0.008,0.004),0).r;
                float f2 = SAMPLE_TEXTURE2D_LOD(_FoamTex,sampler_FoamTex,foamUV*0.63-t*float2(0.003,0.006),0).r;
                foam = saturate(foam*_FoamStrength)*smoothstep(0.15,0.8,(f1+f2)*0.5);
                // Fade unresolved no-mip FFT foam before it becomes subpixel noise.
                foam *= 1/(1+max(baseFootprint.x,baseFootprint.y)*_FoamScale);
                float shoreFoam = 0;
                if(hasBottom && _ShoreFoam.x>0)
                {
                    float2 uv = baseXZ*_ShoreFoam.z;
                    float noiseA = SAMPLE_TEXTURE2D(_ShoreNoise,sampler_ShoreNoise,uv+t*float2(0.021,0.013)).r;
                    float noiseB = SAMPLE_TEXTURE2D(_ShoreNoise,sampler_ShoreNoise,uv*1.5-t*float2(0.017,0.011)).r;
                    float noise = (noiseA+noiseB)*0.5;
                    float shore = saturate(1-intersectionDepth/_ShoreFoam.y);
                    float edge = 1-smoothstep(0.02,0.22+noise*0.15,intersectionDepth);
                    float phase = intersectionDepth*7-t*_ShoreFoam.w*6+noise*2;
                    float band = smoothstep(0.55,0.95,sin(phase));
                    shoreFoam = saturate((edge*0.7+band*shore*shore)*smoothstep(0.16,0.72,noise)*_ShoreFoam.x);
                }
                foam = 1-(1-foam)*(1-shoreFoam);
                color = lerp(color,_FoamColor.rgb*(0.6+sun.color*0.4*sunlight),foam);
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
