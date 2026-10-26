using UnityEngine;
using UnityEngine.Rendering;

// Preserve the newly loaded scene across the source scene's OnDisable restorations.
public sealed class SummitTransferEnvironment
{
    AmbientMode mode; Color light,sky,equator,ground,fogColour,subtractive;
    float intensity,reflection,density,start,end; Material skybox; Light sun;
    SphericalHarmonicsL2 probe; bool fog; FogMode fogMode;
    DefaultReflectionMode reflectionMode; int resolution; Texture customReflection;
    public static SummitTransferEnvironment Capture()
    {
        return new SummitTransferEnvironment {
            mode=RenderSettings.ambientMode,light=RenderSettings.ambientLight,sky=RenderSettings.ambientSkyColor,
            equator=RenderSettings.ambientEquatorColor,ground=RenderSettings.ambientGroundColor,
            intensity=RenderSettings.ambientIntensity,probe=RenderSettings.ambientProbe,skybox=RenderSettings.skybox,
            sun=RenderSettings.sun,subtractive=RenderSettings.subtractiveShadowColor,
            fog=RenderSettings.fog,fogMode=RenderSettings.fogMode,fogColour=RenderSettings.fogColor,
            density=RenderSettings.fogDensity,start=RenderSettings.fogStartDistance,end=RenderSettings.fogEndDistance,
            reflection=RenderSettings.reflectionIntensity,reflectionMode=RenderSettings.defaultReflectionMode,
            resolution=RenderSettings.defaultReflectionResolution,customReflection=RenderSettings.customReflectionTexture};
    }
    public void Apply()
    {
        RenderSettings.ambientMode=mode;RenderSettings.ambientLight=light;RenderSettings.ambientSkyColor=sky;
        RenderSettings.ambientEquatorColor=equator;RenderSettings.ambientGroundColor=ground;
        RenderSettings.ambientIntensity=intensity;RenderSettings.ambientProbe=probe;RenderSettings.skybox=skybox;
        RenderSettings.sun=sun;RenderSettings.subtractiveShadowColor=subtractive;
        RenderSettings.fog=fog;RenderSettings.fogMode=fogMode;RenderSettings.fogColor=fogColour;
        RenderSettings.fogDensity=density;RenderSettings.fogStartDistance=start;RenderSettings.fogEndDistance=end;
        RenderSettings.reflectionIntensity=reflection;RenderSettings.defaultReflectionMode=reflectionMode;
        RenderSettings.defaultReflectionResolution=resolution;RenderSettings.customReflectionTexture=customReflection;
    }
}
