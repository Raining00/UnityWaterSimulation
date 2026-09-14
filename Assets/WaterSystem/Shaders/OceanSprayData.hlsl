#ifndef OCEAN_SPRAY_DATA_INCLUDED
#define OCEAN_SPRAY_DATA_INCLUDED
struct OceanSprayParticle
{
    float4 positionAge;  // World position, age (simulation seconds).
    float4 velocityLife; // World velocity, lifetime; zero means inactive.
    float4 appearance;   // Diameter, random phase, type (0 droplet, 1 mist, 2 splash), emission intensity.
    float4 surface;      // Cached undisplaced XZ, initial speed, current clearance above the water.
};
#endif
