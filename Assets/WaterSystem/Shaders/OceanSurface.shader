Shader "WaterSystem/Ocean"
{
    Properties
    {
        [HideInInspector] _OceanNightControl ("Night control", Vector) = (1,1,1,0.7)
        [HideInInspector] _OceanNightTransition ("Night fade and fill", Vector) = (-0.0348995,-0.207912,0.85,0.35)
        [HideInInspector] _OceanNightLighting ("Moon and fluorescence", Vector) = (0.06,0.26,6,0.65)
        [HideInInspector] _OceanNightMoonTint ("Moon surface tint", Color) = (0.60,0.78,1,1)
        [HideInInspector] _OceanNightDeep ("Night deep", Color) = (0.018,0.065,0.18,1)
        [HideInInspector] _OceanNightShallow ("Night shallow", Color) = (0.025,0.20,0.34,1)
        [HideInInspector] _OceanNightHorizon ("Night horizon", Color) = (0.055,0.14,0.29,1)
        [HideInInspector][HDR] _OceanNightGlow ("Foam emission color", Color) = (0.04,0.65,1,1)
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
        [HideInInspector] _FoamPattern ("Foam pattern (lace, bubbles, relief, clumps)", 2D) = "gray" {}
        _FoamColor ("Foam color", Color) = (0.87,0.94,0.93,1)
        _FoamPatternParams ("Foam tile metres, fine lace, bubbles, clumping", Vector) = (8,0.32,0.2,0.9)
        _FoamShadingParams ("Foam coverage, edge softness, relief shading, animation", Vector) = (1,0.045,0.5,1)
        _AerationColor ("Aerated water color", Color) = (0.05,0.33,0.33,1)
        _AerationParams ("Aeration strength, share implied by surface foam", Vector) = (0.85,0.6,0,0)
        _CausticTex ("Caustic pattern", 2D) = "black" {}
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
            #include "OceanNightCommon.hlsl"

            TEXTURE2D_ARRAY(_OceanDisplacement); SAMPLER(sampler_OceanDisplacement);
            TEXTURE2D_ARRAY(_OceanNormals); SAMPLER(sampler_OceanNormals);
            TEXTURE2D_ARRAY(_OceanFoam); SAMPLER(sampler_OceanFoam);
            TEXTURE2D(_FoamPattern); SAMPLER(sampler_FoamPattern);
            TEXTURE2D(_CausticTex); SAMPLER(sampler_CausticTex);
            // Per-ocean values are supplied by MaterialPropertyBlock, not shared global shader state.
            int _OceanSimulationReady, _FFTCascadeCount, _FFTResolution;
            float4 _OceanDomainSizes, _OceanOrigin;
            // x: Jacobian below which crests break, y: inverse softness of that threshold.
            float4 _OceanFoamCrest;
            // x: ripple damping in churned water, yz: camera distances between which ship waves stop displacing vertices.
            float4 _OceanWakeSurface;
            float _OceanSimulationTime;
            float _OceanInfinite, _OceanHorizonExtent;
            #include "OceanWakeCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _ShallowColor, _Absorption, _SkyHorizon, _SkyZenith, _FoamColor;
                float4 _FoamPatternParams, _FoamShadingParams, _AerationColor, _AerationParams;
                float4 _FoamPattern_TexelSize;
                float _Scattering, _Roughness, _NormalStrength, _RefractionStrength, _ReflectionStrength;
                float _SubsurfaceStrength, _CausticScale, _CausticStrength;
                float _CausticDepth, _UseSceneTextures;
                float _UnderwaterEnabled, _UnderwaterIOR;
                float4 _UnderwaterScatter;
                OCEAN_NIGHT_MATERIAL_FIELDS
            CBUFFER_END
            #define OCEAN_NIGHT_FUNCTIONS 1
            #include "OceanNightCommon.hlsl"
            #include "OceanFoamCommon.hlsl"

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
                // Ship waves lift the same vertices. They fade out where the mesh becomes too coarse
                // to carry them; their per-pixel slopes continue beyond that distance.
                position.y += OceanWakeHeight(output.baseXZ)*waveWeight*
                    (1-smoothstep(_OceanWakeSurface.y,_OceanWakeSurface.z,distance(position,_WorldSpaceCameraPos)));
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
            float3 OceanAtmosphereReflection(float3 direction, float roughness)
            {
                float mip = roughness * (1.7 - 0.7 * roughness) * _AtmosphereSkyReflectionParams.x;
                return SAMPLE_TEXTURECUBE_LOD(_AtmosphereSkyReflection, sampler_AtmosphereSkyReflection, direction, mip).rgb;
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
                // Evaluate derivatives before any varying branch; every filtered lookup below reuses them.
                float2 baseDx=ddx(baseXZ),baseDy=ddy(baseXZ);
                float2 baseFootprint=max(abs(baseDx),abs(baseDy));
                float distanceToCamera = distance(positionWS,_WorldSpaceCameraPos);
                float night = OceanNightAmount(GetMainLight().direction);
                OceanWake wake = SampleOceanWake(baseXZ,baseDx,baseDy);
                if (_OceanSimulationReady != 0 && waveWeight>0)
                {
                    float fold = 0;
                    [loop] for (int i=0; i<_FFTCascadeCount; i++)
                    {
                        float2 uv = baseXZ / _OceanDomainSizes[i] + 0.5 / _FFTResolution;
                        float4 sampleNormal = SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanNormals,sampler_OceanNormals,uv,i,0);
                        // Suppress subpixel normal sparkle with the actual UV footprint, independent of patch LOD.
                        float2 footprint = baseFootprint / _OceanDomainSizes[i];
                        float detail = 1 / (1+max(footprint.x,footprint.y)*_FFTResolution);
                        // Churned water behind a hull is visibly smoother: its short ripples are damped, the swell is not.
                        detail *= 1-wake.aeration*_OceanWakeSurface.x*saturate(1.5-_OceanDomainSizes[i]/40);
                        slope += -sampleNormal.xz / max(sampleNormal.y,0.15) * detail;
                        float2 f = OceanSampleFoam(uv,i,night,baseDx/_OceanDomainSizes[i],baseDy/_OceanDomainSizes[i]);
                        foam = 1-(1-foam)*(1-f.x);
                        fold += f.y-1;
                    }
                    // The cascades fold the surface together, so a crest breaks where the SUM of their
                    // remembered Jacobians drops below the threshold, not where one band does on its own.
                    float crest = saturate((_OceanFoamCrest.x-1-fold)*_OceanFoamCrest.y);
                    foam = 1-(1-foam)*(1-crest);
                }
                slope += wake.gradient;
                foam = 1-(1-foam)*(1-saturate(wake.foam));
                slope*=waveWeight;foam*=waveWeight;
                // Bubbles carried below the surface: the ship's churned trail, and a share under any surface foam.
                float aeration = saturate(wake.aeration*waveWeight+foam*_AerationParams.y)*_AerationParams.x;
                float3 n = normalize(float3(-slope.x*_NormalStrength,1,-slope.y*_NormalStrength));
                float faceSign = analyticalHorizon?(_WorldSpaceCameraPos.y>=_OceanOrigin.y?1:-1):IS_FRONT_VFACE(frontFace,1,-1);
                n *= faceSign;
                float3 v = GetWorldSpaceNormalizeViewDir(positionWS);
                float NoV = saturate(dot(n,v));
                // Shadow coordinates and reflection probes lose useful precision tens of kilometres
                // from the origin. The analytical far surface uses stable unshadowed sun.
                Light sun;
                if(analyticalHorizon)sun=GetMainLight();
                else sun=GetMainLight(TransformWorldToShadowCoord(positionWS));
                float sunlight = sun.shadowAttenuation * sun.distanceAttenuation;
                float3 moonDirection = OceanMoonDirection();
                float3 moonLight = OceanMoonLight(night);
                // bool atmosphereActive = AtmosphereEnabled() && _AtmosphereSkyReflectionParams.x > 0;
                // if (atmosphereActive)
                //     sunlight *= smoothstep(-0.02, 0.05, _AtmosphereSun.y);
                // else
                sunlight *= lerp(1.0,smoothstep(-0.02,0.05,sun.direction.y),night);
                sunlight *= 1-night;
                // Scene ambient tracks the physical sky at night. The legacy constants remain
                // available for ocean-only scenes without an atmosphere component.
                // float3 skyAmbient = atmosphereActive ? max(SampleSH(float3(0, 1, 0)), 0.0) : 0.25;
                float3 skyAmbient = 0.25;
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
                scatterColor = lerp(scatterColor,OceanNightPalette(1-exp(-thickness*_Scattering)),night);
                // Aerated water scatters light straight back: it turns milky and bright, and hides what lies below.
                scatterColor = lerp(scatterColor,lerp(_AerationColor.rgb,_OceanNightShallow.rgb,night),aeration);
                transmittance *= 1-aeration*0.9;
                float3 lighting = 0.25 + sun.color*(0.35+0.65*saturate(sun.direction.y))*sunlight;
                // if (atmosphereActive)
                //     lighting = skyAmbient + sun.color * saturate(sun.direction.y) * sunlight;
                lighting = lerp(lighting,_OceanNightTransition.z+skyAmbient*0.5,night);
                lighting += moonLight*saturate(moonDirection.y)*0.35;
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
                // if (atmosphereActive)
                // {
                //     // A dark reflection is valid at night. Read the live atmosphere cubemap
                //     // directly, including at the horizon; local probes may still contain a day sky.
                //     environment = OceanAtmosphereReflection(r, _Roughness);
                // }
                // else if(!analyticalHorizon)
                // {
                    // environment = GlossyEnvironmentReflection(r,_Roughness,1);
                    // environment = lerp(fallbackSky,environment,saturate(dot(environment,float3(1,1,1))*5));
                // }
                environment = GlossyEnvironmentReflection(r,_Roughness,1);
                environment = lerp(fallbackSky,environment,saturate(dot(environment,float3(1,1,1))*5));
                environment = OceanNightEnvironment(r,_Roughness,environment,night);
                float fresnel = Fresnel(NoV);
                float3 color = lerp(refraction,environment*_ReflectionStrength,fresnel);
                color += sun.color*sunlight*GGX(n,v,sun.direction,_Roughness);
                color += moonLight*GGX(n,v,moonDirection,max(_Roughness,_OceanNightLighting.y));
                float backlight = pow(saturate(dot(v,-sun.direction)),5) * (1-NoV) * saturate(positionWS.y-_OceanOrigin.y+0.3);
                color += _ShallowColor.rgb*sun.color*backlight*_SubsurfaceStrength*sunlight;
                float moonBacklight = pow(saturate(dot(v,-moonDirection)),5)*(1-NoV)*saturate(positionWS.y-_OceanOrigin.y+0.3);
                color += _OceanNightShallow.rgb*moonLight*moonBacklight*_SubsurfaceStrength;
                if (faceSign<0 && _UnderwaterEnabled>0.5)
                {
                    // Water -> air: Snell's window and total internal reflection beyond the critical angle.
                    float cosI = saturate(dot(n,v));
                    float sinT2 = _UnderwaterIOR*_UnderwaterIOR*(1-cosI*cosI);
                    float internalReflection = 1;
                    float3 transmittedColor = _UnderwaterScatter.rgb;
                    if (sinT2<1)
                    {
                        float cosT = sqrt(max(0,1-sinT2));
                        float rs = (_UnderwaterIOR*cosI-cosT)/max(_UnderwaterIOR*cosI+cosT,1e-5);
                        float rp = (cosI-_UnderwaterIOR*cosT)/max(cosI+_UnderwaterIOR*cosT,1e-5);
                        internalReflection = saturate((rs*rs+rp*rp)*0.5);
                        float3 airRay = refract(-v,n,_UnderwaterIOR);
                        transmittedColor = lerp(_SkyHorizon.rgb,_SkyZenith.rgb,sqrt(saturate(airRay.y)));
                        // if (atmosphereActive)
                        //     transmittedColor = OceanAtmosphereReflection(airRay, 0.0);
                        // Screen-space above-water objects are approximate; rays outside the viewport use sky.
                        if (_UseSceneTextures>0.5 && !analyticalHorizon)
                        {
                            float4 projected = TransformWorldToHClip(positionWS+airRay*max(20,distanceToCamera*3));
                            float4 screen = ComputeScreenPos(projected);
                            float2 airUV = screen.xy/max(screen.w,1e-5);
                            if (projected.w>0 && all(airUV>0.002) && all(airUV<0.998))
                            {
                                float raw = SampleSceneDepth(airUV);
                                if (HasOpaqueDepth(raw) && DepthPosition(airUV,raw).y>positionWS.y-0.1)
                                {
                                    float2 edge = min(airUV,1-airUV);
                                    float screenWeight = smoothstep(0.002,0.06,min(edge.x,edge.y));
                                    transmittedColor = lerp(transmittedColor,SampleSceneColor(airUV),screenWeight);
                                }
                            }
                        }
                    }
                    // No air-side GGX or repeated absorption here: the underwater pass integrates the view path once.
                    color = lerp(transmittedColor,_UnderwaterScatter.rgb,internalReflection);
                    foam *= 0.2;
                }
                foam=saturate(foam*_FoamShadingParams.x);
                if(foam>0.0005 || aeration>0.02)
                {
                    OceanFoam surface = EvaluateOceanFoam(foam,aeration,baseXZ,baseDx,baseDy,_OceanSimulationTime,
                        normalize(lerp(sun.direction.xz,moonDirection.xz,night)+float2(1e-4,0)));
                    // Foam is a thick scatterer lying on the water: light wraps around it, and the
                    // ripples underneath do not shade it the way they shade the mirror-like surface.
                    float3 foamNormal = normalize(n+float3(0,faceSign,0));
                    // float3 foamAmbient = atmosphereActive ? max(SampleSH(n), 0.0) : 0.3;
                    float3 foamAmbient = 0.3;
                    // Half the sun keeps sunlit foam below the clipping point, so its relief survives tone mapping.
                    float3 foamLighting = foamAmbient+sun.color*(0.5*saturate((dot(foamNormal,sun.direction)+0.35)/1.35)*sunlight);
                    foamLighting = lerp(foamLighting,foamAmbient+_OceanNightHorizon.rgb*0.15,night);
                    foamLighting += moonLight*saturate(dot(foamNormal,moonDirection));
                    // Thin rims let the aerated water show through; creases of thick foam fall into bluish shade.
                    float3 foamAlbedo = lerp(lerp(_AerationColor.rgb,_FoamColor.rgb,0.55),_FoamColor.rgb,surface.thickness);
                    foamAlbedo *= lerp(float3(0.45,0.66,0.90),1,min(surface.shade,1))*max(surface.shade,1);
                    color = lerp(color,foamAlbedo*foamLighting,surface.alpha);
                    color += OceanNightFoamEmission(surface.alpha,surface.pattern,baseFootprint,1/max(_FoamPatternParams.x,0.01),night);
                }
                if (faceSign<0 && _UnderwaterEnabled<0.5)
                    color = lerp(color,_DeepColor.rgb,1-exp(-distanceToCamera*0.04));
                FragmentOutput output;
                output.color=half4(faceSign<0 && _UnderwaterEnabled>0.5 ? color : MixFog(color,fog),1);
                output.color.rgb = AtmosphereApplyCloudsInFront(output.color.rgb, screenUV, positionWS);
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
