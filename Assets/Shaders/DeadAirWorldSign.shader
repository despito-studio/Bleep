Shader "Dead Air/World Sign"
{
    Properties
    {
        _MainTex ("Font atlas", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Glyph cutoff", Range(0,1)) = 0.15
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest+10" "RenderType"="TransparentCutout" }
        Cull Back
        ZTest LEqual
        ZWrite On
        CGPROGRAM
        // Painted letters receive the same spot, point and ambient light as their board.
        // The font atlas supplies only coverage, never self illumination.
        #pragma surface surf Lambert vertex:vert alphatest:_Cutoff fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        struct Input { float2 uv_MainTex; fixed4 vertexColor; };
        void vert(inout appdata_full vertex,out Input output)
        {
            UNITY_INITIALIZE_OUTPUT(Input,output);
            output.vertexColor=vertex.color;
        }
        void surf(Input input,inout SurfaceOutput output)
        {
            fixed4 ink=input.vertexColor*_Color;
            output.Albedo=ink.rgb;
            output.Alpha=tex2D(_MainTex,input.uv_MainTex).a*ink.a;
            output.Emission=0;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
