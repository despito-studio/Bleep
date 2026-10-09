Shader "Dead Air/Window Glass"
{
    Properties
    {
        _Color("Glass tint and opacity", Color) = (0.12,0.18,0.19,0.18)
        _Glossiness("Smoothness", Range(0,1)) = 0.8
        _ReflectionStrength("Light reflection", Range(0,1)) = 0.12
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
        half _Glossiness, _ReflectionStrength;
        struct Input { float3 worldPos; };

        inline half4 LightingWindowGlass(SurfaceOutput s, half3 viewDir, UnityGI gi)
        {
            half3 halfDirection = normalize(gi.light.dir + viewDir);
            half highlight = pow(saturate(dot(s.Normal, halfDirection)), lerp(24.0h, 128.0h, s.Gloss));
            return half4(gi.light.color * highlight * _ReflectionStrength, s.Alpha);
        }

        inline void LightingWindowGlass_GI(SurfaceOutput s, UnityGIInput data, inout UnityGI gi)
        {
            gi = UnityGlobalIllumination(data, 1.0, s.Normal);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            // One constant alpha, with no texture, dither or world-space grain.
            // Illumination affects only the smooth reflection, never opacity.
            o.Albedo = 0;
            o.Emission = _Color.rgb;
            o.Alpha = _Color.a;
            o.Gloss = _Glossiness;
            o.Specular = 0;
        }
        ENDCG
    }
    Fallback Off
}
