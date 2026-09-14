Shader "WaterSystem/Ocean Spray"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "OceanSprayForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "OceanSprayData.hlsl"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawArgs
            #include "UnityIndirect.cginc"
            StructuredBuffer<OceanSprayParticle> _OceanSprayParticles;
            StructuredBuffer<uint> _SprayVisibleIndices;
            TEXTURE2D(_SplashAtlas); SAMPLER(sampler_SplashAtlas);
            float4 _SplashAtlas_TexelSize;
            float4 _SprayCenterRadius, _SprayColor, _SprayAppearance, _SprayDetail;
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                float3 depthFogMist : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                nointerpolation float4 particle : TEXCOORD4; // age, random, kind, water height
            };
            Varyings Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                InitIndirectDrawArgs(0);
                OceanSprayParticle p = _OceanSprayParticles[_SprayVisibleIndices[GetIndirectInstanceID(instanceID)]];
                if (p.velocityLife.w <= 0) { o.positionCS=float4(0,0,0,1); return o; }
                const float2 corners[6] = {float2(-1,-1),float2(-1,1),float2(1,1),float2(-1,-1),float2(1,1),float2(1,-1)};
                float2 corner = corners[GetIndirectVertexID(vertexID)];
                float age = saturate(p.positionAge.w/p.velocityLife.w);
                float mist = p.appearance.z == 1 ? 1 : 0;
                float splash = p.appearance.z == 2 ? 1 : 0;
                float diameter = p.appearance.x*lerp(1,_SprayAppearance.y,mist)*lerp(1,_SprayDetail.x,splash);
                diameter *= lerp(0.75,1.8,age*max(mist,splash));
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float2 screenVelocity = float2(dot(p.velocityLife.xyz,right),dot(p.velocityLife.xyz,up));
                float speed = length(screenVelocity);
                float2 direction = speed > 0.001 ? screenVelocity/speed : float2(0,1);
                // Atlas patterns rotate independently; drops align with velocity, mist rotates slowly.
                float angle = (p.appearance.y-0.5)*1.2 + age*mist*0.5;
                if (mist+splash>0) direction = float2(sin(angle),cos(angle));
                float2 side = float2(direction.y,-direction.x);
                float stretch = lerp(1+min(speed*0.12,1.5),1,max(mist,splash));
                float2 offset = (side*corner.x+direction*corner.y*stretch)*diameter*0.5;
                float3 world = p.positionAge.xyz+right*offset.x+up*offset.y;
                o.positionCS = TransformWorldToHClip(world);
                o.uv = corner;
                float fade = smoothstep(0,0.08,age)*(1-smoothstep(0.45,1,age));
                fade *= 1-smoothstep(_SprayCenterRadius.w*0.75,_SprayCenterRadius.w*1.15,length(p.positionAge.xz-_SprayCenterRadius.xz));
                fade *= smoothstep(0.15,0.8,distance(p.positionAge.xyz,_WorldSpaceCameraPos));
                o.color = float4(_SprayColor.rgb,fade*_SprayAppearance.x*_SprayColor.a*p.appearance.w*lerp(1,0.3,mist));
                o.depthFogMist = float3(-TransformWorldToView(world).z,ComputeFogFactor(o.positionCS.z),mist);
                o.positionWS = world;
                o.particle = float4(age,p.appearance.y,p.appearance.z,p.positionAge.y-p.surface.w);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float r2=dot(i.uv,i.uv);
                float mist=i.depthFogMist.z;
                float splash=i.particle.z==2?1:0;
                float alpha, shine=0, thickness=1;
                if(splash>0)
                {
                    // Splash atlas: four shape variants, NOT four animation frames.
                    float variant=min(3,floor(i.particle.y*4));
                    float2 localUV=i.uv*0.5+0.5;
                    localUV=clamp(localUV,float2(4*_SplashAtlas_TexelSize.x,_SplashAtlas_TexelSize.y),
                        1-float2(4*_SplashAtlas_TexelSize.x,_SplashAtlas_TexelSize.y));
                    float4 atlas=SAMPLE_TEXTURE2D(_SplashAtlas,sampler_SplashAtlas,float2((localUV.x+variant)*0.25,localUV.y));
                    float erosion=smoothstep(i.particle.x*1.1-0.2,i.particle.x*1.1+0.15,atlas.b);
                    float body=atlas.r*erosion;
                    shine=atlas.g*atlas.g*erosion;
                    alpha=saturate(body*0.7+body*body*2+shine)*i.color.a;
                    thickness=atlas.a;
                }
                else
                {
                    clip(1-r2);
                    float shape=pow(saturate(1-r2),lerp(1.6,3,mist));
                    float cloud=0.65+0.35*sin(i.uv.x*11+i.particle.y*23)*sin(i.uv.y*9-i.particle.x*3);
                    alpha=shape*lerp(1,cloud,mist)*i.color.a;
                }
                // Water-aware soft intersection, independent of whether URP depth includes transparents.
                float waterFade=saturate((i.positionWS.y-i.particle.w)/_SprayAppearance.z);
                alpha*=waterFade*lerp(0.6,1,thickness);
                if (_SprayAppearance.w > 0.5)
                {
                    float raw=SampleSceneDepth(GetNormalizedScreenSpaceUV(i.positionCS));
                    float perspectiveDepth=LinearEyeDepth(raw,_ZBufferParams);
                    #if UNITY_REVERSED_Z
                        raw=1-raw;
                    #endif
                    float sceneDepth=lerp(perspectiveDepth,lerp(_ProjectionParams.y,_ProjectionParams.z,raw),unity_OrthoParams.w);
                    alpha*=saturate((sceneDepth-i.depthFogMist.x)/_SprayAppearance.z);
                }
                Light sun=GetMainLight();
                if(_SprayDetail.w>0.5)
                {
                    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                        sun=GetMainLight(float4(GetNormalizedScreenSpaceUV(i.positionCS),0,1));
                    #else
                        sun=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                    #endif
                }
                float3 v=GetWorldSpaceNormalizeViewDir(i.positionWS);
                float3 facing=normalize(UNITY_MATRIX_V[2].xyz);
                float3 n=normalize(UNITY_MATRIX_V[0].xyz*i.uv.x*0.65+UNITY_MATRIX_V[1].xyz*i.uv.y*0.65+facing);
                float diffuse=0.4+0.6*saturate(dot(n,sun.direction));
                float back=pow(saturate(dot(-v,sun.direction)),5)*_SprayDetail.z*lerp(0.25,1,mist);
                float highlight=pow(saturate(dot(reflect(-sun.direction,n),v)),48)*(1-mist)*0.3;
                float3 lighting=max(SampleSH(float3(0,1,0)),0.12)+sun.color*sun.shadowAttenuation*(diffuse+back);
                float3 color=i.color.rgb*lighting+sun.color*sun.shadowAttenuation*(shine+highlight)*_SprayDetail.y;
                return half4(MixFog(color,i.depthFogMist.y),alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
