Shader "Hidden/DeadAir/AmbientOcclusion"
{
    Properties { _MainTex("World", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _CameraDepthNormalsTexture, _OcclusionTex;
        float4x4 _InverseProjection;
        float4 _CameraProjectionScale;
        float _Radius;
        float3 positionAt(float2 uv, float depth)
        {
            float4 ray = mul(_InverseProjection, float4(uv * 2 - 1, 1, 1));
            ray.xyz /= ray.w;
            return ray.xyz * (depth * _ProjectionParams.z / max(.0001, -ray.z));
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment frag
            half4 frag(v2f_img input) : SV_Target
            {
                float depth; float3 normal;
                DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, input.uv), depth, normal);
                if (depth > .99 || depth <= .00001) return 1;
                float3 center = positionAt(input.uv, depth);
                float2 radius = _Radius * _CameraProjectionScale.xy / max(.3, depth * _ProjectionParams.z) * .5;
                float obstruction = 0;
                [unroll] for (int i = 0; i < 12; i++)
                {
                    float angle = i * 2.399963;
                    float2 uv = saturate(input.uv + float2(cos(angle), sin(angle)) * radius * sqrt((i + .5) / 12.0));
                    float sampleDepth; float3 sampleNormal;
                    DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, uv), sampleDepth, sampleNormal);
                    float3 delta = positionAt(uv, sampleDepth) - center;
                    float distance = length(delta);
                    obstruction += max(0, dot(normal, delta / max(distance, .0001)) - .12) * saturate(1 - distance / _Radius);
                }
                return 1 - saturate(obstruction / 12 * 2.5) * .55;
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            half4 frag(v2f_img input) : SV_Target
            {
                half4 color = tex2D(_MainTex, input.uv);
                color.rgb *= tex2D(_OcclusionTex, input.uv).r;
                return color;
            }
            ENDCG
        }
    }
}
