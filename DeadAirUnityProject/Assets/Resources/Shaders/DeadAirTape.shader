Shader "Hidden/DeadAir/Tape"
{
    Properties { _MainTex ("Station frame", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Clock, _Aspect, _Grain, _Scanlines, _Threat, _Insanity, _Nightmare, _Recording, _Flicker, _Flash, _Exposure;
            float noise(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
            float3 aces(float3 color)
            {
                // ACES fitted transform and exposure match Three's renderer.
                color *= _Exposure / .6;
                color = mul(float3x3(.59719, .35458, .04823, .07600, .90834, .01566, .02840, .13383, .83777), color);
                float3 a = color * (color + .0245786) - .000090537;
                float3 b = color * (.983729 * color + .4329510) + .238081;
                color = a / b;
                return saturate(mul(float3x3(1.60475, -.53108, -.07367, -.10208, 1.10813, -.00605, -.00327, -.07276, 1.07602), color));
            }
            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float2 p = uv * 2 - 1;
                float2 circular = float2(p.x * _Aspect, p.y);
                float distortion = .08 + _Nightmare * .4 + _Insanity * .25;
                uv += p * dot(p, p) * .008 * distortion;
                uv.x += sin(uv.y * 36 + _Clock * 12) * .006 * _Nightmare * _Flicker;
                uv.y += cos(uv.x * 28 + _Clock * 9) * .003 * _Nightmare * _Flicker;
                float glitch = step(.92, noise(float2(floor(_Clock * 10), floor(uv.y * 24)))) * (_Threat * .8 + _Nightmare * .6 + _Insanity * .5) * _Flicker;
                uv.x += sin(uv.y * 70 + _Clock * 30) * .02 * glitch;
                float tracking = (1 - smoothstep(.015, .045, uv.y)) * step(.002, uv.y);
                uv.x += (noise(float2(_Clock * 30, floor(uv.y * 120))) - .5) * .04 * tracking * _Flicker;
                float shift = .00065 + _Threat * .0025 + _Nightmare * .005 + _Insanity * .003;
                float3 color = aces(tex2D(_MainTex, uv).rgb);
                color.r = lerp(color.r, aces(tex2D(_MainTex, uv + float2(shift, 0)).rgb).r, distortion * .85);
                color.b = lerp(color.b, aces(tex2D(_MainTex, uv - float2(shift, 0)).rgb).b, distortion * .85);
                // Tape effects operate on display color; Unity performs the final sRGB write.
                color = LinearToGammaSpace(color);
                float luma = dot(color, float3(.299, .587, .114));
                color = lerp(color, float3(luma * .82, luma * .99, luma * .86), _Recording * .7);
                color += (noise(uv * float2(1100, 740) + frac(_Clock) * 100) - .5) * (_Grain + (_Nightmare * .04 + _Insanity * .03) * step(.001, _Grain));
                color *= 1 - sin(uv.y * 900) * _Scanlines;
                color *= 1 - smoothstep(.55, 1.85, length(circular)) * (.58 + _Threat * .15);
                color.r += _Threat * .045 + (.09 + sin(_Clock * 4) * .04 * _Flicker) * _Nightmare;
                color.g *= 1 - _Nightmare * .25;
                color = lerp(color, luma.xxx, max(0, _Insanity - .3) * .4);
                color.r += _Flash;
                return float4(GammaToLinearSpace(saturate(color)), 1);
            }
            ENDCG
        }
    }
}
