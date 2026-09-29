Shader "Dead Air/Source Unlit" {
Properties {
_Color("Color",Color)=(1,1,1,1) _MainTex("Canvas texture",2D)="white"{}
_Cutoff("Alpha cutoff",Float)=0 _Cull("Cull",Float)=2 _FogEnabled("Fog",Float)=1
_SrcBlend("Source blend",Float)=1 _DstBlend("Destination blend",Float)=0 _ZWrite("Depth write",Float)=1
}
SubShader { Tags {"RenderType"="Opaque"} Cull [_Cull] Blend [_SrcBlend] [_DstBlend] ZWrite [_ZWrite]
Pass {
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_fog
#pragma multi_compile_instancing
#include "UnityCG.cginc"
sampler2D _MainTex;float4 _MainTex_ST;fixed4 _Color;float _Cutoff,_FogEnabled;
struct appdata{float4 vertex:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct v2f{float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;UNITY_FOG_COORDS(1) UNITY_VERTEX_INPUT_INSTANCE_ID};
v2f vert(appdata v){v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_TRANSFER_INSTANCE_ID(v,o);o.vertex=UnityObjectToClipPos(v.vertex);o.uv=TRANSFORM_TEX(v.uv,_MainTex);UNITY_TRANSFER_FOG(o,o.vertex);return o;}
fixed4 frag(v2f i):SV_Target{UNITY_SETUP_INSTANCE_ID(i);fixed4 c=tex2D(_MainTex,i.uv)*_Color;clip(c.a-_Cutoff);if(_FogEnabled>.5){UNITY_APPLY_FOG(i.fogCoord,c);}return c;}
ENDCG
}}
}
