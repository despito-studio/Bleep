Shader "Hidden/DeadAir/Bloom"
{
    Properties { _MainTex ("HDR scene", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float _Threshold;
        float2 _Direction;
        float3 bright(float2 uv)
        {
            float3 c = max(0, tex2D(_MainTex, uv).rgb);
            float peak = max(c.r, max(c.g, c.b));
            // Soft knee keeps small lamps stable at the deliberately low scene resolution.
            float knee = saturate((peak - _Threshold + .25) / .5);
            float contribution = max(peak - _Threshold, knee * knee * .125);
            return c * contribution / max(peak, .0001);
        }
        float4 extract(v2f_img i) : SV_Target
        {
            float2 d = _MainTex_TexelSize.xy;
            return float4((bright(i.uv + d) + bright(i.uv - d) +
                bright(i.uv + float2(d.x, -d.y)) + bright(i.uv + float2(-d.x, d.y))) * .25, 1);
        }
        float4 blur(v2f_img i) : SV_Target
        {
            float3 c = tex2D(_MainTex, i.uv).rgb * .227027;
            c += (tex2D(_MainTex, i.uv + _Direction * 1.384615).rgb +
                  tex2D(_MainTex, i.uv - _Direction * 1.384615).rgb) * .316216;
            c += (tex2D(_MainTex, i.uv + _Direction * 3.230769).rgb +
                  tex2D(_MainTex, i.uv - _Direction * 3.230769).rgb) * .070270;
            return float4(c, 1);
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment extract
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment blur
            ENDCG
        }
    }
    Fallback Off
}
