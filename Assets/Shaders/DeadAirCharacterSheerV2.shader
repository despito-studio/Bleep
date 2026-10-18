Shader "Dead Air/Character Sheer V2"
{
    Properties
    {
        _Color("Base tint", Color) = (1,1,1,1)
        _MainTex("Base color (sRGB), opacity (A)", 2D) = "white" {}
        _BumpMap("Tangent normal", 2D) = "bump" {}
        _NormalScale("Normal strength", Range(0,2)) = 1
        [Toggle] _UseSurfaceMap("Use packed material map", Float) = 0
        _SurfaceMap("Metallic (R), roughness (G), AO (B), thickness (A)", 2D) = "white" {}
        _Metallic("Unmapped metallic", Range(0,1)) = 0
        _Roughness("Unmapped roughness", Range(0.04,1)) = 0.57
        _MetallicScale("Mapped metallic scale", Range(0,1)) = 1
        _RoughnessScale("Mapped roughness scale", Range(0,2)) = 1
        _OcclusionStrength("Occlusion strength", Range(0,1)) = 1
        _Cutoff("Optional alpha cutoff", Range(0,1)) = 0
        [HideInInspector] _DeadAirOwnerCamera("Owner camera and enable", Vector) = (0,0,0,0)
        [HideInInspector] _DeadAirOwnerHeadCenter("Head-local clip centre", Vector) = (0,0,0,0)
        [HideInInspector] _DeadAirOwnerHeadRadii("Head-local clip radii", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 300
        Cull Back
        ZWrite Off
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard alpha:premul fullforwardshadows
        #pragma multi_compile_instancing
        #include "UnityStandardUtils.cginc"
        #include "DeadAirCharacterSurfaceV2.cginc"
        ENDCG
    }
    Fallback Off
}
