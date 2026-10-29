// URP adaptation of Toon Shader FX's lit/rim response, shared by meshes and terrain.
#ifndef MAZEPARTY_TOON_FX_LIGHTING_INCLUDED
#define MAZEPARTY_TOON_FX_LIGHTING_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
half3 MazeToonLight(Light light, half3 normal, half3 view, half3 specularColor,
    float glossiness, half3 rimColor, float rimBlend, float rimThreshold, float edge)
{
    float ndl = dot(normal, light.direction);
    float attenuation = light.distanceAttenuation * light.shadowAttenuation;
    // Quantize surface orientation only; distance and shadow attenuation are applied once below.
    float lit = smoothstep(0.0, max(edge, 0.0001), ndl) * 0.75;
    half3 halfVector = SafeNormalize(light.direction + view);
    float specular = pow(saturate(dot(normal, halfVector) * lit), max(glossiness * glossiness, 0.0001));
    float rim = (1.0 - saturate(dot(view, normal))) * pow(saturate(ndl), max(rimThreshold, 0.0001));
    return (lit
        + smoothstep(0.005, 0.01, specular) * specularColor
        + smoothstep(rimBlend - 0.01, rimBlend + 0.01, rim) * rimColor) * light.color * attenuation;
}
half3 MazeToonLighting(InputData inputData, half3 albedo, half3 ambient,
    half3 specularColor, float glossiness, half3 rimColor, float rimBlend, float rimThreshold, float edge)
{
    half3 n = NormalizeNormalPerPixel(inputData.normalWS);
    half3 v = SafeNormalize(inputData.viewDirectionWS);
    Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, half4(1,1,1,1));
    half3 color = ambient + MazeToonLight(mainLight,n,v,specularColor,glossiness,rimColor,rimBlend,rimThreshold,edge);
#if USE_CLUSTER_LIGHT_LOOP
    UNITY_LOOP for (uint lightIndex=0; lightIndex<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS); ++lightIndex)
    {
        Light light=GetAdditionalLight(lightIndex,inputData.positionWS,half4(1,1,1,1));
        color+=MazeToonLight(light,n,v,specularColor,glossiness,rimColor,rimBlend,rimThreshold,edge);
    }
#endif
#if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
    uint count=GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(count)
        Light light=GetAdditionalLight(lightIndex,inputData.positionWS,half4(1,1,1,1));
        color+=MazeToonLight(light,n,v,specularColor,glossiness,rimColor,rimBlend,rimThreshold,edge);
    LIGHT_LOOP_END
#endif
    return color*albedo;
}
#endif
