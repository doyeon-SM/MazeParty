Shader "MazeParty/Toon Shader FX"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map",2D)="white"{}
        [MainColor] _BaseColor("Base Color",Color)=(1,1,1,1)
        [HideInInspector] _Color("Legacy Color",Color)=(1,1,1,1)
        [HideInInspector] _MainTex("Legacy Texture",2D)="white"{}
        [Normal] _BumpMap("Normal Map",2D)="bump"{}
        _BumpScale("Normal Scale",Float)=1
        [HDR] _EmissionColor("Emission",Color)=(0,0,0,1)
        _EmissionMap("Emission Map",2D)="white"{}
        [HDR] _AmbientColor("Ambient",Color)=(0.4,0.4,0.4,1)
        [HDR] _SpecularColor("Specular",Color)=(0.9,0.9,0.9,1)
        _Glossiness("Glossiness",Float)=32
        [HDR] _RimColor("Rim",Color)=(1,1,1,1)
        _RimBlend("Rim Blend",Range(0,1))=0.5
        _RimThreshold("Rim Threshold",Range(0,1))=0.1
        _ToonEdge("Toon Edge Softness",Range(0,0.5))=0.025
        [ToggleUI] _AlphaClip("Alpha Clip",Float)=0
        _Cutoff("Alpha Cutoff",Range(0,1))=0.5
        [ToggleUI] _UseAlphaMap("Use Separate Alpha",Float)=0
        _AlphaMap("Alpha Map",2D)="white"{}
        [Enum(Opaque,0,Transparent,1)] _Surface("Surface",Float)=0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull",Float)=2
        [HideInInspector] _SrcBlend("Source Blend",Float)=1
        [HideInInspector] _DstBlend("Destination Blend",Float)=0
        [HideInInspector] _ZWrite("Depth Write",Float)=1
        [HideInInspector] _Metallic("Legacy Metallic",Float)=0
        [HideInInspector] _Smoothness("Legacy Smoothness",Float)=0.5
    }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        Cull[_Cull]
        HLSLINCLUDE
        #pragma target 3.5
        #include "ToonFxMesh.hlsl"
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags{"LightMode"="UniversalForwardOnly"}
            Blend[_SrcBlend][_DstBlend]
            ZWrite[_ZWrite]
            HLSLPROGRAM
            #pragma vertex ToonVertex
            #pragma fragment ToonFragment
            #pragma shader_feature_local _NORMALMAP
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
            Tags{"LightMode"="ShadowCaster"}
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ToonShadowVertex
            #pragma fragment ToonDepthFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags{"LightMode"="DepthOnly"}
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex ToonVertex
            #pragma fragment ToonDepthFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags{"LightMode"="DepthNormalsOnly"}
            ZWrite On
            HLSLPROGRAM
            #pragma vertex ToonVertex
            #pragma fragment ToonNormalsFragment
            #pragma shader_feature_local _NORMALMAP
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    Fallback Off
}
