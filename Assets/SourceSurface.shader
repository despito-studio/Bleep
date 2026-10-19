Shader "Dead Air/Source Surface"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
        _MainTex("Canvas texture", 2D) = "white" {}
        _Glossiness("Smoothness", Range(0,1)) = 0.18
        _Metallic("Metallic", Range(0,1)) = 0
        _BumpMap("Surface normal", 2D) = "bump" {}
        _SurfaceMap("Smoothness variation", 2D) = "white" {}
        _MaskMap("AO / roughness / metalness", 2D) = "white" {}
        _MaskMapEnabled("Use PBR mask", Float) = 0
        _BumpScale("Normal strength", Range(0,2)) = 1
        _OcclusionStrength("Occlusion strength", Range(0,1)) = 1
        _MetallicMapStrength("Metalness map strength", Range(0,1)) = 1
        _RoughnessFactor("Roughness factor", Range(0,1)) = 1
        _SnowCover("Outdoor foliage snow", Range(0,1)) = 0
        _SnowTex("Snow albedo", 2D) = "white" {}
        _PlanarReflection("Polished floor reflection", Range(0,1)) = 0
        _EmissionColor("Emission", Color) = (0,0,0,0)
        _EmissionMap("Emission map", 2D) = "white" {}
        _Cutoff("Alpha cutoff", Range(0,1)) = 0
        _Cull("Cull", Float) = 2
        _SrcBlend("Source blend", Float) = 1
        _DstBlend("Destination blend", Float) = 0
        _ZWrite("Depth write", Float) = 1
        _ZTest("Depth test", Float) = 4
        _DeadAirOwnerBodyEye("Owner body camera", Vector) = (0,0,0,0)
        _DeadAirOwnerBodyUp("Owner body view up", Vector) = (0,1,0,0)
        _DeadAirOwnerBodyForward("Owner body view forward", Vector) = (0,0,1,0)
        _DeadAirOwnerBodyClearance("Owner camera clearance", Vector) = (0.06,0.04,0,0.12)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 150
        Cull [_Cull]
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        ZTest [_ZTest]

        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf StandardSpecular keepalpha fullforwardshadows addshadow
        #pragma multi_compile_instancing
        #include "Lighting.cginc"

        sampler2D _MainTex, _EmissionMap, _BumpMap, _SurfaceMap, _MaskMap;
        sampler2D _SnowTex;
        sampler2D _DeadAirReflectionTex;
        float4x4 _DeadAirReflectionVP;
        float4 _DeadAirReflectionTex_TexelSize;
        float _DeadAirReflectionFloor, _DeadAirReflectionEnabled;
        float _DeadAirReflectionsDisabled;
        float4 _DeadAirOwnerBodyEye, _DeadAirOwnerBodyUp, _DeadAirOwnerBodyForward, _DeadAirOwnerBodyClearance;
        fixed4 _Color;
        half _Glossiness, _Metallic, _Cutoff, _PlanarReflection;
        half _MaskMapEnabled, _BumpScale, _OcclusionStrength, _MetallicMapStrength;
        half _RoughnessFactor;
        half _SnowCover;
        half4 _EmissionColor;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_EmissionMap;
            float2 uv_BumpMap;
            float2 uv_SurfaceMap;
            float3 worldPos;
            float3 worldNormal;
            float3 viewDir;
            INTERNAL_DATA
        };
        void surf(Input IN, inout SurfaceOutputStandardSpecular o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            clip(c.a - _Cutoff);
            if (_DeadAirOwnerBodyEye.w > .5)
            {
                float3 fromEye = IN.worldPos - _DeadAirOwnerBodyEye.xyz;
                // Only the owner's clipped surface has this property block.
                // Keep the lens and the hidden neck clear. Visual body fitting
                // uses the source eyes, without cutting a line across the view.
                clip(length(fromEye) - _DeadAirOwnerBodyClearance.x);
                clip(-fromEye.y - _DeadAirOwnerBodyClearance.y);
            }
            half reflections = 1 - saturate(_DeadAirReflectionsDisabled);
            half3 mask = tex2D(_MaskMap, IN.uv_MainTex).rgb;
            half metallic = lerp(_Metallic, mask.b * _MetallicMapStrength, saturate(_MaskMapEnabled)) * reflections;
            o.Albedo = c.rgb * (1 - metallic);
            o.Normal = UnpackNormal(tex2D(_BumpMap, lerp(IN.uv_BumpMap, IN.uv_MainTex, saturate(_MaskMapEnabled))));
            o.Normal.xy *= _BumpScale;
            o.Normal = normalize(o.Normal);
            o.Alpha = c.a;
            half roughnessSmoothness = 1 - mask.g * _RoughnessFactor;
            o.Smoothness = _Glossiness * lerp(tex2D(_SurfaceMap, IN.uv_SurfaceMap).r,
                roughnessSmoothness, saturate(_MaskMapEnabled)) * reflections;
            o.Specular = lerp(half3(.04, .04, .04), c.rgb, metallic) * reflections;
            o.Occlusion = lerp(1, mask.r, _OcclusionStrength * saturate(_MaskMapEnabled));
            if (_SnowCover > .01)
            {
                half cover = smoothstep(.25, .8, WorldNormalVector(IN, o.Normal).y) * _SnowCover;
                half3 snow = tex2D(_SnowTex, IN.worldPos.xz * .5).rgb * half3(.86, .91, .97);
                o.Albedo = lerp(o.Albedo, snow, cover);
                o.Specular = lerp(o.Specular, half3(.04, .04, .04) * reflections, cover);
                o.Smoothness = lerp(o.Smoothness, .12h, cover);
            }
            o.Emission = tex2D(_EmissionMap, IN.uv_EmissionMap).rgb * _EmissionColor.rgb;
            o.Emission += o.Albedo * (_DeadAirOwnerBodyEye.w * _DeadAirOwnerBodyClearance.w);
            // Reflection comes from an actual mirrored camera, so lamps, furniture
            // and doorways appear at their correct positions on the polished floor.
            if (reflections > .5 && _PlanarReflection > .01 && _DeadAirReflectionEnabled > .5 &&
                abs(IN.worldPos.y - _DeadAirReflectionFloor) < .12)
            {
                float3 normal = WorldNormalVector(IN, o.Normal);
                float4 reflected = mul(_DeadAirReflectionVP, float4(IN.worldPos, 1));
                float2 uv = reflected.xy / reflected.w * .5 + .5;
                #if UNITY_UV_STARTS_AT_TOP
                    uv.y = 1 - uv.y;
                #endif
                float inside = step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1);
                float2 blur = _DeadAirReflectionTex_TexelSize.xy * lerp(3.5, .7, o.Smoothness);
                half3 reflection = (tex2D(_DeadAirReflectionTex, uv + blur).rgb +
                    tex2D(_DeadAirReflectionTex, uv - blur).rgb +
                    tex2D(_DeadAirReflectionTex, uv + float2(blur.x, -blur.y)).rgb +
                    tex2D(_DeadAirReflectionTex, uv + float2(-blur.x, blur.y)).rgb) * .25;
                half fresnel = .07 + .38 * pow(1 - saturate(dot(normalize(IN.viewDir), o.Normal)), 5);
                half weight = _PlanarReflection * o.Smoothness * fresnel * inside * saturate(normal.y);
                o.Albedo *= 1 - weight;
                o.Emission += reflection * weight;
            }
        }
        ENDCG
    }
    Fallback "Diffuse"
}
