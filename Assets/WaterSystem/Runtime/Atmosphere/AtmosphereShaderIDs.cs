using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>Single source of truth for atmosphere and cloud shader property names.</summary>
    internal static class AtmosphereShaderIDs
    {
        public static readonly int Radii = Shader.PropertyToID("_AtmosphereRadii");
        public static readonly int Rayleigh = Shader.PropertyToID("_AtmosphereRayleigh");
        public static readonly int Mie = Shader.PropertyToID("_AtmosphereMie");
        public static readonly int MieAbsorption = Shader.PropertyToID("_AtmosphereMieAbsorption");
        public static readonly int Ozone = Shader.PropertyToID("_AtmosphereOzone");
        public static readonly int Ground = Shader.PropertyToID("_AtmosphereGround");
        public static readonly int Sun = Shader.PropertyToID("_AtmosphereSun");
        public static readonly int SunIlluminance = Shader.PropertyToID("_AtmosphereSunIlluminance");
        public static readonly int Camera = Shader.PropertyToID("_AtmosphereCamera");
        public static readonly int World = Shader.PropertyToID("_AtmosphereWorld");
        public static readonly int LUT = Shader.PropertyToID("_AtmosphereLUT");
        public static readonly int Night = Shader.PropertyToID("_AtmosphereNight");
        public static readonly int TransmittanceLUT = Shader.PropertyToID("_AtmosphereTransmittanceLUT");
        public static readonly int MultiScatterLUT = Shader.PropertyToID("_AtmosphereMultiScatterLUT");
        public static readonly int SkyViewLUT = Shader.PropertyToID("_AtmosphereSkyViewLUT");
        public static readonly int AerialLUT = Shader.PropertyToID("_AtmosphereAerialLUT");
        public static readonly int InverseViewProjection = Shader.PropertyToID("_AtmosphereInverseViewProjection");
        public static readonly int TransmittanceOutput = Shader.PropertyToID("_TransmittanceOutput");
        public static readonly int MultiScatterOutput = Shader.PropertyToID("_MultiScatterOutput");
        public static readonly int SkyViewOutput = Shader.PropertyToID("_SkyViewOutput");
        public static readonly int AerialOutput = Shader.PropertyToID("_AerialOutput");
        public static readonly int SkyCubemapOutput = Shader.PropertyToID("_SkyCubemapOutput");
        public static readonly int SkyCubemapSize = Shader.PropertyToID("_SkyCubemapSize");
        public static readonly int AmbientOutput = Shader.PropertyToID("_AmbientOutput");
        public static readonly int Ambient = Shader.PropertyToID("_AtmosphereAmbient");
        // Celestial emission and shared water reflection.
        public static readonly int NightSkyParams = Shader.PropertyToID("_NightSkyParams");
        public static readonly int NightSkyStars = Shader.PropertyToID("_NightSkyStars");
        public static readonly int NightSkyTint = Shader.PropertyToID("_NightSkyTint");
        public static readonly int NightSkyTextureSize = Shader.PropertyToID("_NightSkyTextureSize");
        public static readonly int NightSkyWorldToSky = Shader.PropertyToID("_NightSkyWorldToSky");
        public static readonly int NightSkyGalaxyTexture = Shader.PropertyToID("_NightSkyGalaxyTexture");
        public static readonly int SkyReflection = Shader.PropertyToID("_AtmosphereSkyReflection");
        public static readonly int SkyReflectionParams = Shader.PropertyToID("_AtmosphereSkyReflectionParams");
        // Moon display and camera-cloud-only light.
        public static readonly int MoonDirection = Shader.PropertyToID("_MoonDirection");
        public static readonly int MoonParams = Shader.PropertyToID("_MoonParams");
        public static readonly int MoonSurface = Shader.PropertyToID("_MoonSurface");
        public static readonly int MoonTint = Shader.PropertyToID("_MoonTint");
        public static readonly int MoonRight = Shader.PropertyToID("_MoonRight");
        public static readonly int MoonUp = Shader.PropertyToID("_MoonUp");
        public static readonly int MoonTextureSize = Shader.PropertyToID("_MoonTextureSize");
        public static readonly int MoonSurfaceTexture = Shader.PropertyToID("_MoonSurfaceTexture");
        public static readonly int MoonLight = Shader.PropertyToID("_MoonLight");
        public static readonly int MoonCameraVisible = Shader.PropertyToID("_MoonCameraVisible");
        // Camera-sky aurora. Deliberately excluded from ambient/reflection compute products.
        public static readonly int AuroraParams = Shader.PropertyToID("_AuroraParams");
        public static readonly int AuroraLayer = Shader.PropertyToID("_AuroraLayer");
        public static readonly int AuroraShape = Shader.PropertyToID("_AuroraShape");
        public static readonly int AuroraDetail = Shader.PropertyToID("_AuroraDetail");
        public static readonly int AuroraFrame = Shader.PropertyToID("_AuroraFrame");
        public static readonly int AuroraAnimation = Shader.PropertyToID("_AuroraAnimation");
        public static readonly int AuroraLowerColor = Shader.PropertyToID("_AuroraLowerColor");
        public static readonly int AuroraUpperColor = Shader.PropertyToID("_AuroraUpperColor");
        public static readonly int AuroraNoise = Shader.PropertyToID("_AuroraNoise");
        public static readonly int AuroraCameraVisible = Shader.PropertyToID("_AuroraCameraVisible");
        // Clouds
        public static readonly int CloudShapeNoise = Shader.PropertyToID("_CloudShapeNoise");
        public static readonly int CloudDetailNoise = Shader.PropertyToID("_CloudDetailNoise");
        public static readonly int CloudWeatherMap = Shader.PropertyToID("_CloudWeatherMap");
        public static readonly int CloudPanorama = Shader.PropertyToID("_CloudPanorama");
        public static readonly int CloudBuffer = Shader.PropertyToID("_CloudBuffer");
        public static readonly int CloudShadowMap = Shader.PropertyToID("_CloudShadowMap");
        public static readonly int CloudShadowWorldToLight = Shader.PropertyToID("_CloudShadowWorldToLight");
        public static readonly int CloudShadowParams = Shader.PropertyToID("_CloudShadowParams");
        public static readonly int CloudLayer = Shader.PropertyToID("_CloudLayer");
        public static readonly int CloudShape = Shader.PropertyToID("_CloudShape");
        public static readonly int CloudDensity = Shader.PropertyToID("_CloudDensity");
        public static readonly int CloudWind = Shader.PropertyToID("_CloudWind");
        public static readonly int CloudPhase = Shader.PropertyToID("_CloudPhase");
        public static readonly int CloudMultiScatter = Shader.PropertyToID("_CloudMultiScatter");
        public static readonly int CloudMarch = Shader.PropertyToID("_CloudMarch");
        public static readonly int CloudTemporal = Shader.PropertyToID("_CloudTemporal");
        public static readonly int CloudBufferSize = Shader.PropertyToID("_CloudBufferSize");
        public static readonly int CloudPanoramaSize = Shader.PropertyToID("_CloudPanoramaSize");
        public static readonly int CloudShadowSetup = Shader.PropertyToID("_CloudShadowSetup");
        public static readonly int CloudShadowDirection = Shader.PropertyToID("_CloudShadowDirection");
        public static readonly int CloudInverseViewProjection = Shader.PropertyToID("_CloudInverseViewProjection");
        public static readonly int CloudPreviousViewProjection = Shader.PropertyToID("_CloudPreviousViewProjection");
        public static readonly int CloudShadowLightToWorld = Shader.PropertyToID("_CloudShadowLightToWorld");
        public static readonly int CloudRaw = Shader.PropertyToID("_CloudRaw");
        public static readonly int CloudRawDepth = Shader.PropertyToID("_CloudRawDepth");
        public static readonly int CloudRawRead = Shader.PropertyToID("_CloudRawRead");
        public static readonly int CloudRawDepthRead = Shader.PropertyToID("_CloudRawDepthRead");
        public static readonly int CloudHistory = Shader.PropertyToID("_CloudHistory");
        public static readonly int CloudHistoryDepth = Shader.PropertyToID("_CloudHistoryDepth");
        public static readonly int CloudHistoryOut = Shader.PropertyToID("_CloudHistoryOut");
        public static readonly int CloudHistoryDepthOut = Shader.PropertyToID("_CloudHistoryDepthOut");
        public static readonly int CloudPanoramaOutput = Shader.PropertyToID("_CloudPanoramaOutput");
        public static readonly int CloudShadowOutput = Shader.PropertyToID("_CloudShadowOutput");
        // Noise
        public static readonly int ShapeOutput = Shader.PropertyToID("_ShapeOutput");
        public static readonly int DetailOutput = Shader.PropertyToID("_DetailOutput");
        public static readonly int WeatherOutput = Shader.PropertyToID("_WeatherOutput");
        public static readonly int NoiseSize = Shader.PropertyToID("_NoiseSize");
        public static readonly int WeatherParams = Shader.PropertyToID("_WeatherParams");
        public static readonly int WeatherTime = Shader.PropertyToID("_WeatherTime");
    }
}
