Shader "Dead Air/Source Surface"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
        _MainTex("Canvas texture", 2D) = "white" {}
        _Glossiness("Smoothness", Range(0,1)) = 0.06
        _Metallic("Metallic", Range(0,1)) = 0
        _LightBanding("Painted light bands", Range(0,1)) = 0.38
        _SurfaceGrain("Surface grain", Range(0,0.1)) = 0.025
        _ShadowTint("Indirect shadow tint", Color) = (0.86,0.95,1.08,1)
        _EmissionColor("Emission", Color) = (0,0,0,0)
        _EmissionMap("Emission map", 2D) = "white" {}
        _Cutoff("Alpha cutoff", Range(0,1)) = 0
        _Cull("Cull", Float) = 2
        _SrcBlend("Source blend", Float) = 1
        _DstBlend("Destination blend", Float) = 0
        _ZWrite("Depth write", Float) = 1
        _ZTest("Depth test", Float) = 4
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
        #pragma surface surf DeadAir keepalpha fullforwardshadows addshadow exclude_path:deferred
        #pragma multi_compile_instancing
        #include "Lighting.cginc"

        sampler2D _MainTex, _EmissionMap;
        fixed4 _Color;
        half _Glossiness, _Metallic, _Cutoff, _LightBanding, _SurfaceGrain;
        half4 _EmissionColor, _ShadowTint;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_EmissionMap;
            float3 worldPos;
        };

        inline half4 LightingDeadAir(SurfaceOutput s, half3 viewDir, UnityGI gi)
        {
            half facing = saturate(dot(s.Normal, gi.light.dir));
            // Broad, feathered value groups reveal the authored polygon planes.
            // Light falloff and shadow attenuation stay continuous and physical.
            half bands = 0.18h * smoothstep(0.0h, 0.18h, facing)
                       + 0.42h * smoothstep(0.25h, 0.52h, facing)
                       + 0.40h * smoothstep(0.64h, 1.0h, facing);
            half diffuse = lerp(facing, bands, _LightBanding);
            half3 halfDirection = normalize(gi.light.dir + viewDir);
            half highlight = pow(saturate(dot(s.Normal, halfDirection)), lerp(12.0h, 96.0h, s.Gloss));
            half3 specularColor = lerp(half3(0.045h,0.045h,0.045h), s.Albedo * 0.45h, s.Specular);
            half3 direct = s.Albedo * diffuse * (1.0h - s.Specular * 0.35h)
                         + specularColor * highlight * s.Gloss;
            half4 result;
            result.rgb = direct * gi.light.color;
            #ifdef UNITY_LIGHT_FUNCTION_APPLY_INDIRECT
                result.rgb += s.Albedo * gi.indirect.diffuse * _ShadowTint.rgb;
            #endif
            result.a = s.Alpha;
            return result;
        }

        inline void LightingDeadAir_GI(SurfaceOutput s, UnityGIInput data, inout UnityGI gi)
        {
            gi = UnityGlobalIllumination(data, 1.0, s.Normal);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            clip(c.a - _Cutoff);
            // Stable 20cm flecks give plain exported materials a restrained worn
            // finish without extra texture assets or animated screen noise.
            float3 cell = frac(floor(IN.worldPos * 5.0) * 0.1031);
            cell += dot(cell, cell.yzx + 33.33);
            half grain = frac((cell.x + cell.y) * cell.z) * 2.0h - 1.0h;
            o.Albedo = c.rgb * (1.0h + grain * _SurfaceGrain);
            o.Alpha = c.a;
            o.Gloss = _Glossiness;
            o.Specular = _Metallic;
            o.Emission = tex2D(_EmissionMap, IN.uv_EmissionMap).rgb * _EmissionColor.rgb;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
