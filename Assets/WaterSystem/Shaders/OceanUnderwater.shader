Shader "Hidden/WaterSystem/Underwater"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Underwater"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // Ceiling for the shaft march. The live sample count is a uniform, so the loop trip count
            // stays coherent across the wavefront while the compiler still sees a bounded loop.
            #define UNDERWATER_MAX_SHAFT_STEPS 32
            // Texels the shaft gobo is allowed to advance per march step. Past this the march stops
            // resolving the pattern and the accumulation degrades from an integral into noise.
            #define UNDERWATER_SHAFT_TEXELS_PER_STEP 4.0

            TEXTURE2D_X_FLOAT(_OceanSceneDepth);
            TEXTURE2D_ARRAY(_OceanDisplacement); SAMPLER(sampler_OceanDisplacement);
            TEXTURE2D_ARRAY(_OceanNormals); SAMPLER(sampler_OceanNormals);
            TEXTURE2D(_CausticTex); SAMPLER(sampler_CausticTex);
            // Supplied explicitly by OceanUnderwaterSettings: a MaterialPropertyBlock texture
            // override does not refresh Unity's automatic _TexelSize uniform, and a zero here would
            // silently pin the shaft prefilter to mip 0 and bring the aliasing straight back.
            float4 _CausticTex_TexelSize;
            int _OceanSimulationReady, _FFTCascadeCount, _FFTResolution;
            float4 _OceanDomainSizes, _OceanOrigin;
            float _OceanInfinite, _OceanSimulationTime;
            float4 _UnderwaterExtinction, _UnderwaterScatter, _UnderwaterParameters, _UnderwaterCaustics;
            float4 _UnderwaterSun, _UnderwaterSunDirection;
            float4 _UnderwaterWarp, _UnderwaterFlow, _UnderwaterShafts, _UnderwaterShaftFlow;
            float _UnderwaterShaftSteps, _UnderwaterTime, _UnderwaterFlowSpeed;

            float Weight(float2 xz)
            {
                float2 d = abs(xz-_OceanOrigin.xz);
                if(_OceanInfinite<0.5) return max(d.x,d.y)<=_OceanOrigin.w*0.5 ? 1 : 0;
                return 1-smoothstep(_OceanOrigin.w*0.4,_OceanOrigin.w*0.5,max(d.x,d.y));
            }
            float3 Displacement(float2 xz)
            {
                float3 d = 0;
                if(_OceanSimulationReady!=0)
                    for(int c=0;c<_FFTCascadeCount;c++)
                        d += SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanDisplacement,sampler_OceanDisplacement,xz/_OceanDomainSizes[c]+0.5/_FFTResolution,c,0).xyz;
                return d*Weight(xz);
            }
            float Height(float2 worldXZ)
            {
                // Invert horizontal choppiness so the query agrees with the displaced surface mesh.
                float2 baseXZ = worldXZ;
                [unroll] for(int i=0;i<2;i++) baseXZ = worldXZ-Displacement(baseXZ).xz;
                return _OceanOrigin.y+Displacement(baseXZ).y;
            }

            // ------------------------------------------------------------- flowing distortion

            // Tan of the simulated surface slope, using the same convention as OceanSurface.shader so
            // the warp follows the waves the player actually sees rather than an unrelated field.
            // The per-cascade footprint term is the same sub-pixel suppression the surface shader
            // uses: the finest cascade is a 5 m domain, so without it the warp would be driven by
            // wave detail smaller than one pixel and read as jitter rather than as flow.
            float2 SurfaceSlope(float2 baseXZ,float2 footprint)
            {
                float2 slope = 0;
                if(_OceanSimulationReady!=0)
                {
                    [loop] for(int c=0;c<_FFTCascadeCount;c++)
                    {
                        float4 n = SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanNormals,sampler_OceanNormals,
                            baseXZ/_OceanDomainSizes[c]+0.5/_FFTResolution,c,0);
                        float detail = 1/(1+max(footprint.x,footprint.y)/_OceanDomainSizes[c]*_FFTResolution);
                        slope += -n.xz/max(n.y,0.15)*detail;
                    }
                }
                return slope*Weight(baseXZ);
            }

            // Two coarse taps of the caustic network, advected in different directions, stand in for
            // the flow that the FFT slope does not carry. Reusing that texture keeps the wobble
            // phase-locked to the light it focuses, and its Repeat wrap lets the drift be folded into
            // [0,1) so the advection keeps full float precision for hours of play.
            // Sampling is left to implicit LOD on purpose: the caller feeds world coordinates whose
            // screen footprint explodes at grazing angles, and hardware mip selection is what keeps
            // that from turning into shimmer. It also means the caller must not sit in divergent
            // control flow, which is why the warp is evaluated before the waterline early-out.
            float2 FlowNoise(float2 xz,float scale)
            {
                float2 drift = _UnderwaterFlow.yz*_UnderwaterTime*(scale*_UnderwaterFlowSpeed);
                drift -= floor(drift);
                float2 p = xz*scale;
                float a = SAMPLE_TEXTURE2D(_CausticTex,sampler_CausticTex,p+drift).r;
                float b = SAMPLE_TEXTURE2D(_CausticTex,sampler_CausticTex,p*1.31+float2(-drift.y,drift.x)*1.4).r;
                // The pivot removes the dark background of the network so the field varies around
                // zero; a pure offset would only translate the image and be invisible.
                return (float2(a,b)-_UnderwaterFlow.w)*2;
            }

            float2 WarpOffset(float2 baseXZ,float2 footprint)
            {
                float2 slope = SurfaceSlope(baseXZ,footprint)*_UnderwaterWarp.y;
                float2 flow = FlowNoise(baseXZ,_UnderwaterFlow.x)*_UnderwaterWarp.z;
                return (slope+flow)*_UnderwaterWarp.x;
            }

            // ------------------------------------------------------------------ light shafts

            // Henyey-Greenstein phase. Normalised so g = 0 is isotropic and the strength knob stays
            // meaningful; g > 0 gathers the beams around the sun the way forward scattering does.
            float PhaseHG(float cosTheta,float g)
            {
                float g2 = g*g;
                float d = max(1+g2-2*g*cosTheta,1e-4);
                return (1-g2)/(4*PI*d*sqrt(d));
            }

            // Single scattering towards the eye. Sunlight is focused by the wavy surface, so the
            // pattern it entered through is the caustic network projected up the sun direction.
            // Integrating that along the view ray is what produces the Tyndall beams: because the
            // entry point slides along the ray, the network smears into streaks by itself.
            float3 LightShafts(float3 start,float3 direction,float lengthInWater,float3 extinction)
            {
                float strength = _UnderwaterShafts.x;
                if(strength<=0||lengthInWater<=0) return float3(0,0,0);
                float3 sunDirection = _UnderwaterSunDirection.xyz;
                float sunLength = length(sunDirection);
                if(sunLength<1e-4) return float3(0,0,0);
                sunDirection /= sunLength;
                if(sunDirection.y<0.02) return float3(0,0,0);

                float vertical = max(sunDirection.y,0.08);
                float phase = PhaseHG(dot(direction,sunDirection),clamp(_UnderwaterShafts.z,0,0.95));
                float goboScale = _UnderwaterShafts.y;
                // Authored in metres per second, so it converts into the pattern's UV space through
                // goboScale. FlowSpeed scales every animated rate in this shader from one place.
                float2 goboDrift = _UnderwaterShaftFlow.xy*_UnderwaterTime*(goboScale*_UnderwaterFlowSpeed);
                goboDrift -= floor(goboDrift);
                float descentAbsorption = _UnderwaterShafts.w;
                // The medium only scatters the fraction of the light it attenuates.
                float density = max(_UnderwaterExtinction.w,0);

                int steps = (int)clamp(_UnderwaterShaftSteps,1.0,32.0);
                float stepLength = lengthInWater/steps;
                // The gobo must not vary faster than the march can resolve. One step slides the
                // entry point this far, so read the mip that keeps a handful of texels per step.
                // Without this the pattern advances most of a tile per sample, the twelve taps
                // stop being an integral and become shot noise - which is what turned the shafts
                // into scattered light patches once the sun was tilted off vertical.
                // How far the entry point slides per step, in the gobo's UV space. Both the view ray
                // and the upwind walk along the sun move it, so both terms belong in the rate:
                // overlooking the view-ray term would under-filter and let the aliasing back in.
                float2 entryVelocity = direction.xz-sunDirection.xz*(direction.y/vertical);
                float entryStepUV = stepLength*length(entryVelocity)*goboScale;
                float goboMip = max(0.0,log2(max(entryStepUV*_CausticTex_TexelSize.z,1.0)/UNDERWATER_SHAFT_TEXELS_PER_STEP));
                float maxRun = _OceanOrigin.w*0.5;
                float3 accum = 0;
                [loop] for(int i=0;i<steps;i++)
                {
                    float distanceAlongRay = (i+0.5)*stepLength;
                    float3 samplePosition = start+direction*distanceAlongRay;
                    // Walk up the sun ray to the surface: that patch of water is where this light
                    // entered, so that is where the focused pattern has to be read. A low sun sends
                    // that patch a long way upwind, so cap the run inside the simulated water
                    // rather than letting the pattern escape the patch that has wave data.
                    float descent = max(_OceanOrigin.y-samplePosition.y,0);
                    float2 run = sunDirection.xz*(descent/vertical);
                    float runLength = length(run);
                    if(runLength>maxRun) run *= maxRun/max(runLength,1e-4);
                    float2 entryXZ = samplePosition.xz+run;
                    float inside = 1;
                    if(_OceanInfinite<0.5)
                    {
                        float2 offset = abs(entryXZ-_OceanOrigin.xz);
                        inside = max(offset.x,offset.y)<=maxRun ? 1 : 0;
                    }
                    float2 cuv = entryXZ*goboScale+goboDrift;
                    float c1 = SAMPLE_TEXTURE2D_LOD(_CausticTex,sampler_CausticTex,cuv,goboMip).r;
                    float c2 = SAMPLE_TEXTURE2D_LOD(_CausticTex,sampler_CausticTex,cuv*1.17+goboDrift.yx*0.6,goboMip).r;
                    float gobo = min(c1,c2)*2*inside;
                    // Absorbed on the way down from the surface, and again on the way to the eye.
                    accum += exp(-extinction*descent*descentAbsorption)*exp(-extinction*distanceAlongRay)*gobo;
                }
                return accum*(stepLength*density*phase*strength)*_UnderwaterSun.rgb;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv);
                #if UNITY_REVERSED_Z
                    const float nearDepth=1;
                #else
                    const float nearDepth=UNITY_NEAR_CLIP_VALUE;
                #endif
                float3 start = ComputeWorldSpacePosition(uv,nearDepth,UNITY_MATRIX_I_VP);
                float signedDepth = Height(start.xz)-start.y;
                float width = max(_UnderwaterParameters.z,fwidth(signedDepth));
                float wet = smoothstep(-width,width,signedDepth);
                float2 boundary = abs(start.xz-_OceanOrigin.xz);
                if(_OceanInfinite<0.5 && max(boundary.x,boundary.y)>_OceanOrigin.w*0.5) wet=0;

                float raw = SAMPLE_TEXTURE2D_X_LOD(_OceanSceneDepth,sampler_PointClamp,uv,0).r;
                #if UNITY_REVERSED_Z
                    float deviceDepth=raw;
                    bool hasGeometry=raw>0.000001;
                #else
                    float deviceDepth=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
                    bool hasGeometry=raw<0.999999;
                #endif
                float3 end = ComputeWorldSpacePosition(uv,deviceDepth,UNITY_MATRIX_I_VP);
                float3 ray = end-start;
                float rayLength = max(length(ray),0.0001);
                float3 direction = ray/rayLength;
                float lengthInWater = min(rayLength,_UnderwaterParameters.x);
                if(_OceanInfinite<0.5)
                {
                    float2 side = _OceanOrigin.xz+sign(direction.xz)*_OceanOrigin.w*0.5;
                    float2 exitDistance = (side-start.xz)/float2(abs(direction.x)>1e-6?direction.x:1e-6,abs(direction.z)>1e-6?direction.z:1e-6);
                    exitDistance = lerp(float2(1e8,1e8),exitDistance,step(1e-6,abs(direction.xz)));
                    lengthInWater = min(lengthInWater,max(0,min(exitDistance.x,exitDistance.y)));
                }
                // Derivatives must be evaluated outside the waterline branch, and the warp reads
                // textures with implicit LOD, so both are resolved before the early-out below.
                float3 normal = SafeNormalize(cross(ddy(end),ddx(end)));
                normal *= dot(normal,-direction)<0 ? -1 : 1;
                // Screen footprint of the viewed point, used by the warp's sub-pixel suppression.
                float2 baseFootprint = max(abs(ddx(end.xz)),abs(ddy(end.xz)));

                // Warp the colour lookup only. Depth, the waterline and the volume integrals stay on
                // the undistorted ray, so waterline crossings and the extinction path remain stable.
                // Anchoring to the viewed geometry is what makes the wobble vary across the screen:
                // the near-plane point sits inside a half-metre disc and would only translate the
                // image. The reach fade keeps close silhouettes from smearing, and wet keeps the
                // mask edge from tearing.
                float2 warp = 0;
                if(_UnderwaterWarp.x>0)
                {
                    float reach = saturate(lengthInWater/max(_UnderwaterWarp.w,0.01));
                    warp = clamp(WarpOffset(end.xz,baseFootprint)*(reach*wet),-0.05,0.05);
                }
                if(wet<=0) return original;

                float3 sceneColor = original.rgb;
                if(any(abs(warp)>1e-5))
                    sceneColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_LinearClamp,uv+warp,0).rgb;

                float bottomDepth = _OceanOrigin.y-end.y;
                if(hasGeometry && bottomDepth>0.2 && rayLength<_UnderwaterParameters.x && _UnderwaterParameters.w>0)
                {
                    // Reject the water boundary itself; only submerged, upward-facing surfaces receive caustics.
                    float submerged = saturate((Height(end.xz)-end.y-0.15)*2);
                    float2 cuv = end.xz*_UnderwaterCaustics.x;
                    float t = _OceanSimulationTime*0.025;
                    float c1 = SAMPLE_TEXTURE2D(_CausticTex,sampler_CausticTex,cuv+float2(t,-t*0.7)).r;
                    float c2 = SAMPLE_TEXTURE2D(_CausticTex,sampler_CausticTex,cuv*1.17+float2(-t*0.6,t)).r;
                    float caustic = min(c1,c2)*2*exp(-bottomDepth/_UnderwaterCaustics.y);
                    sceneColor += sceneColor*caustic*_UnderwaterParameters.w*submerged*saturate(normal.y)*_UnderwaterSun.rgb;
                }
                float3 extinction = max(_UnderwaterExtinction.rgb+_UnderwaterExtinction.w,0);
                float3 transmission = exp(-extinction*lengthInWater);
                float depthDarkening = exp(-max(0,_OceanOrigin.y-start.y)*_UnderwaterParameters.y);
                float3 scattering = _UnderwaterScatter.rgb*depthDarkening*(0.35+0.65*_UnderwaterSun.rgb);
                float3 color = sceneColor*transmission+scattering*(1-transmission);
                color += LightShafts(start,direction,lengthInWater,extinction);
                return half4(lerp(original.rgb,color,wet),original.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
