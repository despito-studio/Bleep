Shader "Dead Air/Window Glass"
{
    Properties
    {
        _Color("Glass tint and opacity", Color) = (0.12,0.18,0.19,0.18)
        _Glossiness("Smoothness", Range(0,1)) = 0.8
        _ReflectionStrength("Light reflection", Range(0,1)) = 0.12
        _DirtMap("Dust and dried water marks", 2D) = "black" {}
        _DirtStrength("Surface weathering", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 150
        Cull Back
        ZWrite Off

        CGPROGRAM
        #pragma target 3.0
        // Fade generates alpha-weighted light passes: additional lights add
        // reflections without blending the background away for every light.
        #pragma surface surf WindowGlass alpha:fade fullforwardshadows nolightmap nodynlightmap novertexlights exclude_path:deferred
        #pragma multi_compile_instancing
        #include "Lighting.cginc"

        fixed4 _Color;
        sampler2D _DirtMap;
        half _Glossiness, _ReflectionStrength, _DirtStrength;
        struct Input { float3 worldPos; float2 uv_DirtMap; };

        inline half4 LightingWindowGlass(SurfaceOutput s, half3 viewDir, UnityGI gi)
        {
            half3 halfDirection = normalize(gi.light.dir + viewDir);
            half highlight = pow(saturate(dot(s.Normal, halfDirection)), lerp(24.0h, 128.0h, s.Gloss));
            half fresnel = .1h + .9h * pow(1 - saturate(abs(dot(s.Normal, viewDir))), 5);
            return half4(gi.light.color * highlight * s.Specular * fresnel, s.Alpha);
        }

        inline void LightingWindowGlass_GI(SurfaceOutput s, UnityGIInput data, inout UnityGI gi)
        {
            gi = UnityGlobalIllumination(data, 1.0, s.Normal);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            half dust = saturate(tex2D(_DirtMap, IN.uv_DirtMap).r * _DirtStrength);
            o.Albedo = 0;
            o.Emission = _Color.rgb + dust * half3(.07, .066, .054);
            o.Alpha = _Color.a;
            o.Gloss = lerp(_Glossiness, .22h, dust);
            o.Specular = _ReflectionStrength * lerp(1, .25h, dust);
        }
        ENDCG
    }
    Fallback Off
}
