Shader "Dead Air/Art Mirror"
{
    Properties
    {
        _Color ("Silvering tint", Color) = (.95,.96,.95,1)
        _MainTex ("Silvering wear", 2D) = "white" {}
        _Glossiness ("Silvering smoothness", Range(0,1)) = .96
        _ReflectionStrength ("Reflected light", Range(0,1)) = .96
        _Tarnish ("Shallow surface tarnish", Range(0,.2)) = .045
        _MirrorTex ("Reflected scene", 2D) = "black" {}
        _ReflectionReady ("Valid reflection", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back ZWrite On ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex, _MirrorTex;
            float4 _MainTex_ST, _MirrorTex_TexelSize;
            float4x4 _MirrorVP;
            half4 _Color;
            half _Glossiness, _ReflectionStrength, _Tarnish, _ReflectionReady;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float3 world : TEXCOORD1; };
            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.world = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }
            half4 frag(v2f input) : SV_Target
            {
                half3 surface = tex2D(_MainTex, input.uv).rgb;
                float4 projected = mul(_MirrorVP, float4(input.world, 1));
                float2 uv = projected.xy / max(.0001, projected.w) * .5 + .5;
                #if UNITY_UV_STARTS_AT_TOP
                    uv.y = 1 - uv.y;
                #endif
                half inside = step(.0001, projected.w) * step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1);
                float2 blur = _MirrorTex_TexelSize.xy * (1 - _Glossiness) * 1.5;
                half3 reflected = (tex2D(_MirrorTex, uv + blur).rgb + tex2D(_MirrorTex, uv - blur).rgb) * .5;
                half wear = saturate((1 - dot(surface, half3(.2126,.7152,.0722))) * _Tarnish);
                half weight = saturate(_ReflectionReady) * inside * _ReflectionStrength;
                half3 silver = _Color.rgb * (.08 + surface * .04);
                return half4(lerp(silver, reflected * _Color.rgb, weight) * (1 - wear * .65), 1);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
