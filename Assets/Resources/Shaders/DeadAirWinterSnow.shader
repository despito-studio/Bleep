Shader "Dead Air/Winter Snow"
{
    Properties
    {
        _Color("Snow color", Color) = (0.82, 0.88, 0.94, 1)
        _MainTex("Fine snow grain", 2D) = "white" {}
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
        sampler2D _MainTex;
        fixed4 _Color;
        half _Glossiness, _Metallic;
        struct Input { float2 uv_MainTex; fixed4 color : COLOR; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed3 grain = tex2D(_MainTex, IN.uv_MainTex).rgb;
            o.Albedo = grain * _Color.rgb * IN.color.rgb;
            o.Smoothness = _Glossiness * grain.r;
            o.Metallic = _Metallic;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
