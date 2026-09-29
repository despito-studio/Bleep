Shader "Dead Air/Source Surface" {
Properties {
_Color("Color",Color)=(1,1,1,1) _MainTex("Canvas texture",2D)="white"{}
_Glossiness("Smoothness",Range(0,1))=0.06 _Metallic("Metallic",Range(0,1))=0
_EmissionColor("Emission",Color)=(0,0,0,0) _EmissionMap("Emission map",2D)="white"{}
_Cutoff("Alpha cutoff",Range(0,1))=0 _Cull("Cull",Float)=2
_SrcBlend("Source blend",Float)=1 _DstBlend("Destination blend",Float)=0 _ZWrite("Depth write",Float)=1
}
SubShader {
Tags {"RenderType"="Opaque"} LOD 200
Cull [_Cull] Blend [_SrcBlend] [_DstBlend] ZWrite [_ZWrite]
CGPROGRAM
#pragma surface surf Standard fullforwardshadows keepalpha
#pragma target 3.0
sampler2D _MainTex,_EmissionMap;
fixed4 _Color; half _Glossiness,_Metallic,_Cutoff; half4 _EmissionColor;
struct Input {float2 uv_MainTex;float2 uv_EmissionMap;};
void surf(Input IN,inout SurfaceOutputStandard o){
fixed4 c=tex2D(_MainTex,IN.uv_MainTex)*_Color;clip(c.a-_Cutoff);
o.Albedo=c.rgb;o.Alpha=c.a;o.Metallic=_Metallic;o.Smoothness=_Glossiness;
o.Emission=tex2D(_EmissionMap,IN.uv_EmissionMap).rgb*_EmissionColor.rgb;
}
ENDCG
}
Fallback "Diffuse"
}
