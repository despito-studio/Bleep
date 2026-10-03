Shader "Dead Air/Night Sky"
{
    Properties
    {
        _FogEdge ("Fog at the horizon", Vector) = (.0052,.008,.0152,1)
        _Horizon ("Low sky radiance", Vector) = (.016,.023,.038,1)
        _Zenith ("Zenith radiance", Vector) = (.007,.010,.019,1)
        _MoonDirection ("Direction toward moon", Vector) = (.35,.47,-.81,0)
        _MoonRadius ("Moon angular radius", Float) = .012
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float4 _FogEdge, _Horizon, _Zenith, _MoonDirection;
            float _MoonRadius;
            struct v2f { float4 position:SV_POSITION; float3 ray:TEXCOORD0; };
            v2f vert(appdata_base input)
            {
                v2f o;
                o.position = UnityObjectToClipPos(input.vertex);
                o.ray = mul(unity_ObjectToWorld,input.vertex).xyz - _WorldSpaceCameraPos;
                return o;
            }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7))) * 43758.5453); }
            float smoothNoise(float2 p)
            {
                float2 cell=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash(cell),hash(cell+float2(1,0)),f.x),
                    lerp(hash(cell+float2(0,1)),hash(cell+1),f.x),f.y);
            }
            float cloudNoise(float2 p)
            {
                return .57*smoothNoise(p)+.28*smoothNoise(p*2.03+17.3)+.15*smoothNoise(p*4.11+39.1);
            }
            float4 frag(v2f input):SV_Target
            {
                float3 ray=normalize(input.ray);
                float altitude=saturate(ray.y);
                float3 upperSky=lerp(_Horizon.rgb,_Zenith.rgb,smoothstep(.08,1,altitude));
                float3 color=lerp(_FogEdge.rgb,upperSky,smoothstep(0,.16,altitude));
                // Sparse celestial points use derivative antialiasing; no large
                // additive quads or screen-space random glitter.
                float2 skyUV=float2(atan2(ray.x,ray.z)*.159154943+.5,asin(clamp(ray.y,-1,1))*.318309886+.5);
                float2 cells=skyUV*float2(530,260), cell=floor(cells), f=frac(cells);
                float chance=hash(cell+63.7);
                float2 starCenter=.18+.64*float2(hash(cell+2.9),hash(cell+7.1));
                float starRadius=lerp(.045,.13,hash(cell+11.4));
                float pixelWidth=max(length(fwidth(cells))*.4,.025);
                float stars=(1-smoothstep(starRadius,starRadius+pixelWidth,length(f-starCenter)))*step(.9965,chance);
                stars*=smoothstep(.055,.28,ray.y);
                float3 starColor=lerp(float3(.32,.37,.46),float3(.5,.45,.35),hash(cell+22.3));

                float3 moonDirection=normalize(_MoonDirection.xyz);
                float3 moonRight=normalize(cross(float3(0,1,0),moonDirection));
                float3 moonUp=cross(moonDirection,moonRight);
                float facing=dot(ray,moonDirection);
                float2 lunar=float2(dot(ray,moonRight),dot(ray,moonUp))/_MoonRadius;
                float lunarRadius=length(lunar);
                float rim=max(fwidth(lunarRadius),.025);
                float disc=(1-smoothstep(1-rim,1+rim,lunarRadius))*step(.95,facing);
                float3 lunarNormal=float3(lunar,sqrt(saturate(1-dot(lunar,lunar))));
                float phase=saturate(dot(lunarNormal,normalize(float3(-.48,.15,.87))));
                float maria=lerp(.63,1,smoothstep(.28,.7,cloudNoise(lunar*3.1+15.3)));
                float3 moonColor=float3(.36,.38,.36)*(.08+.92*phase)*maria;
                float angular=sqrt(max(0,2*(1-facing)));
                float halo=exp(-angular*34)*.006;

                float2 cloudUV=ray.xz/max(.15,ray.y+.12)*.82+float2(_Time.y*.0009,0);
                float cloud=smoothstep(.40,.65,cloudNoise(cloudUV+29.4));
                cloud*=smoothstep(.015,.17,ray.y)*.88;
                color+=float3(.48,.58,.76)*halo;
                color+=stars*starColor*(1-disc);
                color=lerp(color,moonColor,disc);
                // Broad dark clouds cover the moon and stars too.
                float3 cloudColor=lerp(_Horizon.rgb*.18,_Zenith.rgb*.24,altitude)+halo*.12;
                color=lerp(color,cloudColor,cloud);
                return float4(max(color,0),1);
            }
            ENDCG
        }
    }
    Fallback Off
}
