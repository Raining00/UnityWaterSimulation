#ifndef WATERSYSTEM_OCEAN_FOAM_INCLUDED
#define WATERSYSTEM_OCEAN_FOAM_INCLUDED

// Shared foam appearance of the physical and stylized ocean shaders. Include after the material constants:
//   _FoamPattern           R lace erosion field, G bubble field, B relief of dense foam, A macro clumps
//                          (see Tools/WaterTextures/generate_foam_pattern.py)
//   _FoamPatternParams     x: metres per pattern tile, y: fine lace amount, z: bubble amount, w: clumping
//   _FoamShadingParams     x: coverage multiplier, y: edge softness, z: relief shading, w: animation speed
//   _FoamPattern_TexelSize z: pattern width in texels
//
// The simulations only say HOW MUCH foam a place has. The look comes from erosion: the pattern is
// uniformly distributed, so drawing foam where it exceeds 1 - coverage covers exactly that share of
// the surface. Fresh foam is a solid sheet; as coverage decays the sheet opens into a web of
// filaments along the pattern's cell walls and finally into separate specks.

struct OceanFoam
{
    float alpha;       // Share of the pixel hidden by foam.
    float thickness;   // 0 at the translucent rim of a filament, 1 inside dense foam.
    float shade;       // Relief darkening of thick foam, 1 = unshaded.
    float pattern;     // Erosion field, for effects that want the same detail (night emission).
};

OceanFoam EvaluateOceanFoam(float coverage, float aeration, float2 baseXZ, float2 baseDx, float2 baseDy, float time, float2 lightXZ)
{
    float tile = 1.0 / max(_FoamPatternParams.x, 0.01);
    float fine = tile * 3.23;
    float coarse = tile * 0.27;
    float drift = time * _FoamShadingParams.w;
    // The second layer is rotated, about three times finer and creeps the other way. The two never
    // line up twice, which hides the tiling and makes the web crawl instead of sliding as a sheet.
    static const float2x2 Rotation = { 0.7986, -0.6018, 0.6018, 0.7986 };
    float2 uvLace = baseXZ * tile + drift * float2(0.0031, 0.0017);
    float2 uvFine = mul(Rotation, baseXZ) * fine + float2(0.37, 0.11) - drift * float2(0.0042, 0.0061);
    float2 uvClumps = baseXZ * coarse + float2(0.13, 0.71);
    float4 lacePattern = SAMPLE_TEXTURE2D_GRAD(_FoamPattern, sampler_FoamPattern, uvLace, baseDx * tile, baseDy * tile);
    float4 finePattern = SAMPLE_TEXTURE2D_GRAD(_FoamPattern, sampler_FoamPattern, uvFine,
        mul(Rotation, baseDx) * fine, mul(Rotation, baseDy) * fine);
    float clumps = SAMPLE_TEXTURE2D_GRAD(_FoamPattern, sampler_FoamPattern, uvClumps, baseDx * coarse, baseDy * coarse).a;

    // Once the lace is smaller than a pixel the pattern has averaged to its mean. Widening the
    // threshold there turns the erosion into plain coverage, so distant foam keeps its brightness
    // instead of flickering or vanishing.
    float footprint = sqrt(max(length(baseDx) * length(baseDy), 1e-12));
    float far = saturate((log2(max(footprint * tile * _FoamPattern_TexelSize.z, 1e-6)) - 1.0) / 3.0);

    // Clumps shift the coverage most where foam is half dissolved, so it never thins evenly.
    float amount = saturate(coverage + (clumps - 0.5) * _FoamPatternParams.w * 4.0 * coverage * (1.0 - saturate(coverage)));
    float field = saturate(lacePattern.r + (finePattern.r - 0.5) * _FoamPatternParams.y);
    float solid = amount * 1.3;   // A closed sheet from roughly 0.8 upwards.
    float soft = lerp(_FoamShadingParams.y, 0.5, far);
    float lace = smoothstep(1.0 - solid - soft, 1.0 - solid + soft, field);

    // Loose bubbles surround the lace and float over freshly aerated water.
    float bubbleAmount = saturate(max(amount * 1.8, aeration * 0.55)) * _FoamPatternParams.z;
    float bubbles = smoothstep(0.98 - bubbleAmount, 1.02 - bubbleAmount, max(lacePattern.g, finePattern.g)) * 0.7 * (1.0 - far);

    OceanFoam foam;
    foam.alpha = max(lace, bubbles) * smoothstep(0.0, 0.03, max(coverage, aeration * 0.5));
    foam.thickness = saturate((field - (1.0 - solid)) / 0.3) * lace;
    // Thick foam is a lumpy heap. Its relief darkens the hollows, and a second tap a few centimetres
    // towards the light tells which flanks face it - enough to read as volume without a normal map.
    float dense = saturate(solid * 1.4 - 0.75);
    float relief = lacePattern.b * 0.6 + finePattern.b * 0.4;
    float towardsLight = SAMPLE_TEXTURE2D_GRAD(_FoamPattern, sampler_FoamPattern, uvLace + lightXZ * (0.06 * tile), baseDx * tile, baseDy * tile).b;
    float lit = (relief - 0.5) * 1.2 + clamp((lacePattern.b - towardsLight) * 4.0, -0.6, 0.6) * (1.0 - far);
    // Even a closed sheet keeps a trace of the web it will later break into.
    lit += (field - 0.5) * 0.35;
    foam.shade = clamp(1.0 + dense * _FoamShadingParams.z * (lit - 0.3) * 1.6, 0.3, 1.15);
    foam.pattern = field;
    return foam;
}
#endif
