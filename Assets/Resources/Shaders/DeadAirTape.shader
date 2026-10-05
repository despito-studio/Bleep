Shader "Hidden/DeadAir/Tape"
{
    Properties { _MainTex ("Station frame", 2D) = "white" {} _BloomTex ("Practical glow", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _BloomTex;
            float4 _MainTex_TexelSize;
            float _BloomStrength, _Trip, _TripClock;
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
                // Geometry stays steady in ordinary play. Only eaten mushrooms bend the view.
                float trip = saturate(_Trip), clock = _TripClock;
                float2 p = uv - .5;
                uv += trip * float2(sin(p.y * 13 + clock * 1.6), cos(p.x * 11 - clock * 1.3)) * .009;
                uv = clamp(uv, _MainTex_TexelSize.xy, 1 - _MainTex_TexelSize.xy);
                float2 split = float2(.005 * trip * (1 + .3 * sin(clock)), 0);
                float3 scene = tex2D(_MainTex, uv).rgb;
                if (trip > .001)
                {
                    scene.r = tex2D(_MainTex, clamp(uv + split, 0, 1)).r;
                    scene.b = tex2D(_MainTex, clamp(uv - split, 0, 1)).b;
                }
                scene += tex2D(_BloomTex, uv).rgb * _BloomStrength;
                float3 color = aces(scene);
                // Tape effects operate on display color; Unity performs the final sRGB write.
                color = LinearToGammaSpace(color);
                float luma = dot(color, float3(.299, .587, .114));
                // Cool shadow colour and warm highlights tie the whole station together.
                float shadow = 1 - smoothstep(.06, .52, luma);
                float highlight = smoothstep(.52, .95, luma);
                color *= lerp(float3(1, 1, 1), float3(.91, 1.025, 1.075), shadow * .42);
                color *= lerp(float3(1, 1, 1), float3(1.04, 1.015, .945), highlight * .55);
                float3 psychedelic = color.gbr * float3(1.08, .93, 1.14);
                color = lerp(color, psychedelic, trip * (.28 + .2 * sin(clock * .7)));
                color += trip * .035 * sin(float3(0, 2.1, 4.2) + clock * .8 + luma * 6);
                color = lerp(color, float3(luma * .82, luma * .99, luma * .86), _Recording * .7);
                color += (noise(uv * float2(1100, 740) + frac(_Clock) * 100) - .5) * (_Grain + (_Nightmare * .04 + _Insanity * .03) * step(.001, _Grain));
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
