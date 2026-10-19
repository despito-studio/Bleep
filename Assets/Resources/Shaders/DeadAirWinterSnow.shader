Shader "Dead Air/Winter Snow"
{
    Properties
    {
        _Color("Snow color", Color) = (0.82, 0.88, 0.94, 1)
        _MainTex("Fine snow grain", 2D) = "white" {}
        _BumpMap("Snow crystalline normal", 2D) = "bump" {}
        _MaskMap("AO / roughness / metalness", 2D) = "white" {}
        _MaskMapEnabled("Use scanned PBR snow", Float) = 0
        _BumpScale("Normal strength", Range(0,2)) = 1
        _OcclusionStrength("Occlusion strength", Range(0,1)) = 1
        _MetallicMapStrength("Metalness map strength", Range(0,1)) = 1
        _Glossiness("Smoothness", Range(0,1)) = 0.09
        _Metallic("Metallic", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 150
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows
        sampler2D _MainTex, _BumpMap, _MaskMap;
        fixed4 _Color;
        half _Glossiness, _Metallic;
        half _MaskMapEnabled, _BumpScale, _OcclusionStrength;
        struct Input { float2 uv_MainTex; fixed4 color : COLOR; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed3 grain = tex2D(_MainTex, IN.uv_MainTex).rgb;
            o.Albedo = grain * _Color.rgb * IN.color.rgb;
            half3 mask = tex2D(_MaskMap, IN.uv_MainTex).rgb;
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            o.Normal.xy *= _BumpScale;
            o.Normal = normalize(o.Normal);
            o.Smoothness = _Glossiness * lerp(grain.r, 1 - mask.g, _MaskMapEnabled);
            o.Occlusion = lerp(1, mask.r, _OcclusionStrength * _MaskMapEnabled);
            o.Metallic = _Metallic;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
