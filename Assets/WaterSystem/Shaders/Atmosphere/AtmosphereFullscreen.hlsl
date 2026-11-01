#ifndef WATERSYSTEM_ATMOSPHERE_FULLSCREEN_INCLUDED
#define WATERSYSTEM_ATMOSPHERE_FULLSCREEN_INCLUDED
// Shared vertex stage for the full-screen triangle meshes drawn through Graphics.RenderMesh.
// Positions are already in clip space; the object matrix is ignored.
struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
Varyings Vert(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    #if UNITY_REVERSED_Z
        output.positionCS = float4(input.positionOS.xy, 0.000001, 1);
    #else
        output.positionCS = float4(input.positionOS.xy, 0.999999, 1);
    #endif
    return output;
}
float RawToDevice(float raw)
{
    #if UNITY_REVERSED_Z
        return raw;
    #else
        return lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
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
#endif
