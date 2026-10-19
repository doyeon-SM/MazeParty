// URP preview port of the installed Simple Toon and Toon Shader FX lighting.
// Opaque meshes only. Original third-party files are deliberately untouched.
#ifndef MAZEPARTY_TOON_PREVIEW_INCLUDED
#define MAZEPARTY_TOON_PREVIEW_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);
CBUFFER_START(UnityPerMaterial)
float4 _MainTex_ST;
float4 _Color;
float4 _ShnColor;
float4 _AmbientColor;
float4 _SpecularColor;
float4 _RimColor;
float _ColIntense, _ColBright, _AmbientCol, _Segmented;
float _Steps, _StpSmooth, _Offset, _Clipped;
float _MinLight, _MaxLight, _Lumin, _ShnOverlap;
float _ShnIntense, _ShnRange, _ShnSmooth, _Glossiness;
float _RimBlend, _RimThreshold, _Smoothness;
CBUFFER_END

struct PreviewAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct PreviewVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    half fog : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

PreviewVaryings PreviewVertex(PreviewAttributes input)
{
    PreviewVaryings o = (PreviewVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
    o.positionCS = p.positionCS;
    o.positionWS = p.positionWS;
    o.normalWS = TransformObjectToWorldNormal(input.normalOS);
    o.uv = TRANSFORM_TEX(input.uv, _MainTex);
    o.fog = ComputeFogFactor(p.positionCS.z);
    return o;
}

// STCore.cginc's segmented/smoothed ramp, with guards for zero-width ranges.
float SimpleRamp(float ndl, float attenuation)
{
    float offset = clamp(_Offset, -1.0, 1.0);
    float intensity = saturate((ndl + offset) / max(1.0 + offset, 0.0001));
    float steps = _Segmented > 0.5 ? max(floor(_Steps), 1.0) : 1.0;
    float smoothing = _Segmented > 0.5 ? _StpSmooth : 1.0;
    float stepSize = 1.0 / steps;
    float bandIndex = ceil(intensity / stepSize);
    float band = bandIndex * stepSize;
    float start = band - stepSize;
    float end = start + stepSize * smoothing;
    float t = abs(start - end) < 0.0001 ? 0.0 : saturate((intensity - end) / (start - end));
    float curve = t * t * (3.0 - 2.0 * t);
    float blend = -(2.0 / ((smoothing + 0.34) * 4.7)) + 1.3;
    float transition = saturate(lerp(curve, t, blend));
    float reduce = bandIndex == 1.0 ? 1.0 - saturate((_Offset - 1.0) / 0.1) : 1.0;
    float dim = saturate(band - transition * reduce * stepSize) * saturate(attenuation);
    float maxLight = max(_MaxLight, _MinLight);
    float ceiling = maxLight + _Lumin;
    if (_Clipped > 0.5)
        return clamp(_MinLight + saturate(dim - _MinLight) / max(maxLight - _MinLight, 0.0001)
                     * (ceiling - _MinLight), _MinLight, ceiling);
    return _MinLight + (ceiling - _MinLight) * dim;
}

half3 ShadePreviewLight(Light light, half3 normal, half3 view, half3 textureColor, bool mainLight)
{
    float ndl = dot(normal, light.direction);
    float attenuation = light.distanceAttenuation * light.shadowAttenuation;
#if defined(TOON_SIMPLE)
    float ramp = SimpleRamp(ndl, attenuation);
    half3 tint = lerp(_Color.rgb, light.color, _AmbientCol);
    half3 color = (textureColor * tint * _ColIntense + _ColBright) * ramp;
    // The source's main pass blends light color, while additional lights take
    // their strength from attenuation and are combined with BlendOp Max.
    float width = _ShnRange * 2.0;
    float distanceToLight = abs(ndl - 1.0);
    float edge = width * (1.0 - _ShnSmooth);
    float shineT = abs(width - edge) < 0.0001 ? 1.0 : saturate((distanceToLight - width) / (edge - width));
    float shine = shineT * shineT * (3.0 - 2.0 * shineT);
    float dim = 1.0 - (1.0 - saturate(attenuation)) * (1.0 - _ShnOverlap);
    return lerp(color, _ShnColor.rgb, _ShnIntense * step(distanceToLight, width) * shine * dim);
#else
    // ToonPass.cginc: two-tone diffuse plus specular and lit-side rim.
    float lit = smoothstep(0.0, max(_Smoothness, 0.0001), ndl * attenuation) * 0.75;
    half3 halfVector = SafeNormalize(light.direction + view);
    float highlight = pow(saturate(dot(normal, halfVector) * lit), max(_Glossiness * _Glossiness, 0.0001));
    half3 specular = smoothstep(0.005, 0.01, highlight) * _SpecularColor.rgb;
    float rimDot = 1.0 - saturate(dot(view, normal));
    // Clamp fractional pow inputs to prevent NaNs on back-facing surfaces.
    float rimRaw = rimDot * pow(saturate(ndl), max(_RimThreshold, 0.0001));
    half3 rim = smoothstep(_RimBlend - 0.01, _RimBlend + 0.01, rimRaw) * _RimColor.rgb;
    // Keep ambient visible inside URP cast shadows and add it only in the base pass.
    half3 ambient = mainLight ? _AmbientColor.rgb : half3(0, 0, 0);
    return ((lit * light.color + specular + rim) * attenuation + ambient) * _Color.rgb * textureColor;
#endif
}

half4 PreviewFragment(PreviewVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half3 normal = NormalizeNormalPerPixel(input.normalWS);
    half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
    half3 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb;
    float4 shadowCoord;
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
#else
    shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#endif
    Light mainLight = GetMainLight(shadowCoord);
    half3 color = ShadePreviewLight(mainLight, normal, view, tex, true);

    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
#if USE_CLUSTER_LIGHT_LOOP
    UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++lightIndex)
    {
        Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1));
        half3 extra = ShadePreviewLight(light, normal, view, tex, false);
#if defined(TOON_SIMPLE)
        color = max(color, extra);
#else
        color += extra;
#endif
    }
#endif
#if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
    uint count = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(count)
        Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1));
        half3 extra = ShadePreviewLight(light, normal, view, tex, false);
#if defined(TOON_SIMPLE)
        color = max(color, extra);
#else
        color += extra;
#endif
    LIGHT_LOOP_END
#endif
    return half4(MixFog(color, input.fog), 1.0);
}

float3 _LightDirection;
float3 _LightPosition;
PreviewVaryings PreviewShadowVertex(PreviewAttributes input)
{
    PreviewVaryings o = PreviewVertex(input);
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 direction = normalize(_LightPosition - o.positionWS);
#else
    float3 direction = _LightDirection;
#endif
    o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(o.positionWS, o.normalWS, direction)));
    return o;
}
half4 PreviewDepthFragment(PreviewVaryings input) : SV_Target { return input.positionCS.z; }
half4 PreviewNormalsFragment(PreviewVaryings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float3 normal = NormalizeNormalPerPixel(input.normalWS);
#if defined(_GBUFFER_NORMALS_OCT)
    return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normal) * 0.5 + 0.5)), 0);
#else
    return half4(normal, 0);
#endif
}
#endif
