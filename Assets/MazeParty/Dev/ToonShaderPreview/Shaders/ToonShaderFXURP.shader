// Test conversion of the installed Toon Shader FX package. Opaque meshes only.
Shader "MazeParty/Preview/Toon Shader FX URP"
{
    Properties
    {
        [MainTexture] _MainTex ("Main Texture", 2D) = "white" {}
        [MainColor] _Color ("Color", Color) = (1,1,1,1)
        [HDR] _AmbientColor ("Ambient Color", Color) = (0.4,0.4,0.4,1)
        [HDR] _SpecularColor ("Specular Color", Color) = (0.9,0.9,0.9,1)
        _Glossiness ("Glossiness", Float) = 32
        [HDR] _RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimBlend ("Rim Blend", Range(0,1)) = 0.5
        _RimThreshold ("Rim Threshold", Range(0,1)) = 0.1
        _Smoothness ("Smoothness", Range(0,0.5)) = 0.025
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Back
        ZWrite On
        HLSLINCLUDE
        #pragma target 3.5
        #define TOON_FX 1
        #include "ToonPreviewCommon.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex PreviewVertex
            #pragma fragment PreviewFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex PreviewShadowVertex
            #pragma fragment PreviewDepthFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex PreviewVertex
            #pragma fragment PreviewDepthFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma vertex PreviewVertex
            #pragma fragment PreviewNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            ENDHLSL
        }

    }
    Fallback Off
}
