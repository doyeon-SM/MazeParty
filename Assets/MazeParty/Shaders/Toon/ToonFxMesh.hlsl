#ifndef MAZEPARTY_TOON_FX_MESH_INCLUDED
#define MAZEPARTY_TOON_FX_MESH_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "ToonFxLighting.hlsl"
TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
TEXTURE2D(_AlphaMap); SAMPLER(sampler_AlphaMap);
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
half4 _BaseColor, _EmissionColor, _AmbientColor, _SpecularColor, _RimColor;
float _Glossiness, _RimBlend, _RimThreshold, _ToonEdge;
float _Cutoff, _AlphaClip, _UseAlphaMap, _BumpScale, _Surface;
float _Metallic, _Smoothness;
CBUFFER_END
struct ToonAttributes
{
    float4 positionOS:POSITION;
    float3 normalOS:NORMAL;
    float4 tangentOS:TANGENT;
    float2 uv:TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct ToonVaryings
{
    float4 positionCS:SV_POSITION;
    float3 positionWS:TEXCOORD0;
    half3 normalWS:TEXCOORD1;
    float2 uv:TEXCOORD2;
    half4 tangentWS:TEXCOORD3;
    half fog:TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
ToonVaryings ToonVertex(ToonAttributes input)
{
    ToonVaryings o=(ToonVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input,o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    VertexPositionInputs p=GetVertexPositionInputs(input.positionOS.xyz);
    o.positionCS=p.positionCS; o.positionWS=p.positionWS;
    o.normalWS=TransformObjectToWorldNormal(input.normalOS);
    o.tangentWS=half4(TransformObjectToWorldDir(input.tangentOS.xyz),input.tangentOS.w*GetOddNegativeScale());
    o.uv=TRANSFORM_TEX(input.uv,_BaseMap);
    o.fog=ComputeFogFactor(p.positionCS.z);
    return o;
}
half4 ToonSample(float2 uv)
{
    half4 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv)*_BaseColor;
    if(_UseAlphaMap>0.5) color.a*=SAMPLE_TEXTURE2D(_AlphaMap,sampler_AlphaMap,uv).r;
    if(_AlphaClip>0.5) clip(color.a-_Cutoff);
    return color;
}
half3 ToonNormal(ToonVaryings input)
{
    half3 normal=NormalizeNormalPerPixel(input.normalWS);
#if defined(_NORMALMAP)
    half3 tangent=normalize(input.tangentWS.xyz);
    half3 bitangent=cross(normal,tangent)*input.tangentWS.w;
    half3 normalTS=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,input.uv),_BumpScale);
    normal=normalize(TransformTangentToWorld(normalTS,half3x3(tangent,bitangent,normal)));
#endif
    return normal;
}
half4 ToonFragment(ToonVaryings input):SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half4 albedo=ToonSample(input.uv);
    InputData data=(InputData)0;
    data.positionWS=input.positionWS; data.normalWS=ToonNormal(input);
    data.viewDirectionWS=GetWorldSpaceNormalizeViewDir(input.positionWS);
    data.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(input.positionCS);
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    data.shadowCoord=ComputeScreenPos(TransformWorldToHClip(input.positionWS));
#else
    data.shadowCoord=TransformWorldToShadowCoord(input.positionWS);
#endif
    half3 color=MazeToonLighting(data,albedo.rgb,_AmbientColor.rgb,_SpecularColor.rgb,
        _Glossiness,_RimColor.rgb,_RimBlend,_RimThreshold,_ToonEdge);
    color+=SAMPLE_TEXTURE2D(_EmissionMap,sampler_EmissionMap,input.uv).rgb*_EmissionColor.rgb;
    return half4(MixFog(color,input.fog),_Surface>0.5?albedo.a:1.0);
}
float3 _LightDirection;
float3 _LightPosition;
ToonVaryings ToonShadowVertex(ToonAttributes input)
{
    ToonVaryings o=ToonVertex(input);
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 direction=normalize(_LightPosition-o.positionWS);
#else
    float3 direction=_LightDirection;
#endif
    o.positionCS=ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,direction)));
    return o;
}
half4 ToonDepthFragment(ToonVaryings input):SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    ToonSample(input.uv);
    return input.positionCS.z;
}
half4 ToonNormalsFragment(ToonVaryings input):SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    ToonSample(input.uv);
    float3 normal=ToonNormal(input);
#if defined(_GBUFFER_NORMALS_OCT)
    return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normal)*0.5+0.5)),0);
#else
    return half4(normal,0);
#endif
}
#endif
