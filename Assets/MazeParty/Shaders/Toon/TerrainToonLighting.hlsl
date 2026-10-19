// Keep URP's native terrain layer, holes, instancing and basemap handling.
// Substitute only its forward lighting function with the approved Toon Shader FX response.
#include "ToonFxLighting.hlsl"
half4 MazeTerrainToon(InputData data, half3 albedo, half metallic, half3 specular,
    half smoothness, half occlusion, half3 emission, half alpha)
{
    return half4(MazeToonLighting(data,albedo,half3(0.4,0.4,0.4)*occlusion,
        half3(0.9,0.9,0.9),32.0,half3(1,1,1),0.5,0.1,0.025)+emission,alpha);
}
#define UniversalFragmentPBR MazeTerrainToon
