Shader "Dead Air/World Sign"
{
    Properties
    {
        _MainTex ("Font atlas", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest+10" "RenderType"="TransparentCutout" }
        Cull Back
        ZTest LEqual
        ZWrite On
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Color;
            struct vertexInput { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct vertexOutput { float4 position:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; UNITY_FOG_COORDS(1) };
            vertexOutput vert(vertexInput input)
            {
                vertexOutput output;
                output.position=UnityObjectToClipPos(input.vertex);
                output.uv=input.uv;
                output.color=input.color*_Color;
                UNITY_TRANSFER_FOG(output,output.position);
                return output;
            }
            fixed4 frag(vertexOutput input):SV_Target
            {
                fixed4 color=input.color;
                color.a*=tex2D(_MainTex,input.uv).a;
                clip(color.a-.15);
                UNITY_APPLY_FOG(input.fogCoord,color);
                return color;
            }
            ENDCG
        }
    }
}
