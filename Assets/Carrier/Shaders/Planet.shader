// A planet lit by the system's star (URP). Each planet is lit from where the star actually is
// (the global _CarrierStarPos, or the direction to it from the planet's real place,
// _CarrierLightDir, which ScaledSpace gives each planet: a far planet is drawn nearer than it
// is), so planets far from the ship still show the right day and night sides; the directional
// sun light only follows the ship. Optional emission (lava) glows on the night side too. The
// ground seen close to (PlanetTerrain, _TERRAIN) adds its points' own colours, and in a planet's
// air the fog lies over it.
Shader "Carrier/Planet"
{
    Properties
    {
        _BaseMap ("Surface", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _EmissionMap ("Glow map", 2D) = "black" {}
        [HDR] _EmissionColor ("Glow colour", Color) = (0, 0, 0, 1)
        _Ambient ("Night side light", Range(0, 0.3)) = 0.03
        _Wrap ("Terminator softness", Range(0, 0.5)) = 0.12
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "Planet"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma shader_feature_local _TERRAIN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2;
                float4 color : TEXCOORD3; float fog : TEXCOORD4;
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
            float4 _CarrierStarPos;     // xyz: star position, w: 1 when set
            float4 _CarrierStarColor;
            float4 _CarrierLightDir;    // xyz: toward the star from where the planet really is, w: 1 when set (per renderer)

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _EmissionMap_ST;
                float4 _EmissionColor;
                float _Ambient;
                float _Wrap;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 toStar = _CarrierLightDir.w > 0.5 ? _CarrierLightDir.xyz
                              : _CarrierStarPos.w > 0.5 ? normalize(_CarrierStarPos.xyz - i.positionWS) : float3(0, 1, 0);
                float3 starCol = _CarrierStarPos.w > 0.5 ? _CarrierStarColor.rgb : float3(1, 1, 1);
                float ndl = saturate((dot(n, toStar) + _Wrap) / (1.0 + _Wrap));
                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                #if defined(_TERRAIN)
                    // the ground's own colour at this point (the map, worked out finer than the texture holds)
                    albedo = i.color.rgb * _BaseColor.rgb;
                #endif
                float3 glow = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
                float3 c = albedo * (starCol * ndl + _Ambient) + glow * (1.0 - 0.6 * ndl);
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
