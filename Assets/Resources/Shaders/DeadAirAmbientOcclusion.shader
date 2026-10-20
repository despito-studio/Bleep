Shader "Hidden/DeadAir/AmbientOcclusion"
{
    Properties { _MainTex("World", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        // The deferred renderer already owns a depth buffer. Normals are rebuilt
        // from its derivatives instead of asking for a second depth-normal pass,
        // which would submit every opaque object again.
        sampler2D _MainTex, _CameraDepthTexture, _OcclusionTex;
        float4 _CameraDepthTexture_TexelSize;
        float4x4 _InverseProjection;
        float4 _CameraProjectionScale;
        float _Radius;
        float eyeDepthAt(float2 uv)
        {
            return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
        }
        float3 viewPositionAt(float2 uv, float eyeDepth)
        {
            float4 ray = mul(_InverseProjection, float4(uv * 2 - 1, 1, 1));
            ray.xyz /= ray.w;
            // The ray lies on the far plane; scale it down to the sampled depth.
            return ray.xyz * (eyeDepth / max(.0001, -ray.z));
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
                float centerDepth = eyeDepthAt(input.uv);
                if (centerDepth > _ProjectionParams.z * .995 || centerDepth <= .00001) return 1;
                float3 center = viewPositionAt(input.uv, centerDepth);
                float2 texel = _CameraDepthTexture_TexelSize.xy;
                float2 leftUv = input.uv - float2(texel.x, 0);
                float2 rightUv = input.uv + float2(texel.x, 0);
                float2 downUv = input.uv - float2(0, texel.y);
                float2 upUv = input.uv + float2(0, texel.y);
                float3 normal = normalize(cross(
                    viewPositionAt(rightUv, eyeDepthAt(rightUv)) - viewPositionAt(leftUv, eyeDepthAt(leftUv)),
                    viewPositionAt(upUv, eyeDepthAt(upUv)) - viewPositionAt(downUv, eyeDepthAt(downUv))));
                // View space has the camera at the origin, so a visible surface
                // normal must point back at it.
                if (dot(normal, center) > 0) normal = -normal;
                float2 radius = _Radius * _CameraProjectionScale.xy / max(.3, centerDepth) * .5;
                float obstruction = 0;
                [unroll] for (int i = 0; i < 12; i++)
                {
                    float angle = i * 2.399963;
                    float2 uv = saturate(input.uv + float2(cos(angle), sin(angle)) * radius * sqrt((i + .5) / 12.0));
                    float3 delta = viewPositionAt(uv, eyeDepthAt(uv)) - center;
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
