// ProductionV2 map contract. Base alpha is opacity. Surface R/G/B/A is
// metallic / roughness / occlusion / thickness. Thickness is retained data;
// Unity Standard does not implement a skin scattering model from that channel.
sampler2D _MainTex, _BumpMap, _SurfaceMap;
fixed4 _Color;
half _UseSurfaceMap, _Metallic, _Roughness;
half _MetallicScale, _RoughnessScale, _NormalScale, _OcclusionStrength, _Cutoff;
float4 _DeadAirOwnerCamera, _DeadAirOwnerHeadCenter, _DeadAirOwnerHeadRadii;
float4x4 _DeadAirOwnerHeadWorldToLocal;

struct Input
{
    float2 uv_MainTex;
    float2 uv_BumpMap;
    float2 uv_SurfaceMap;
    float3 worldPos;
};

void surf(Input IN, inout SurfaceOutputStandard o)
{
    // Renderer property blocks enable this only on the owner's presentation.
    // All source vertices remain intact; external cameras and shadows render
    // the complete body. The ellipsoid comes from this character's anatomy.
    #if !defined(UNITY_PASS_SHADOWCASTER)
    if (_DeadAirOwnerCamera.w > .5 &&
        distance(_WorldSpaceCameraPos, _DeadAirOwnerCamera.xyz) < .001)
    {
        float3 headLocal = mul(_DeadAirOwnerHeadWorldToLocal, float4(IN.worldPos, 1)).xyz;
        float3 radii = max(_DeadAirOwnerHeadRadii.xyz, float3(.0001, .0001, .0001));
        clip(length((headLocal - _DeadAirOwnerHeadCenter.xyz) / radii) - 1);
    }
    #endif
    fixed4 base = tex2D(_MainTex, IN.uv_MainTex) * _Color;
    clip(base.a - _Cutoff);
    half4 surface = tex2D(_SurfaceMap, IN.uv_SurfaceMap);
    o.Albedo = base.rgb;
    o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_BumpMap), _NormalScale);
    o.Metallic = saturate(lerp(_Metallic, surface.r * _MetallicScale, _UseSurfaceMap));
    half roughness = clamp(lerp(_Roughness, surface.g * _RoughnessScale, _UseSurfaceMap), 0.04h, 1.0h);
    o.Smoothness = 1.0h - roughness;
    o.Occlusion = lerp(1.0h, surface.b, _OcclusionStrength * _UseSurfaceMap);
    o.Alpha = base.a;
}
