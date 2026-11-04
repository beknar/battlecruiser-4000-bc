// A planet's atmosphere (URP, additive): a glowing rim round the planet's edge, bright on the
// day side and fading out over the night side. Put on a sphere slightly larger than the planet.
Shader "Carrier/PlanetAtmosphere"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.45, 0.75, 1.6, 1)
        _RimPower ("Rim sharpness", Range(0.5, 10)) = 4
        _Strength ("Strength", Range(0, 4)) = 1.4
        _NightLevel ("Glow on the night side", Range(0, 1)) = 0.05
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Atmosphere"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _RimPower;
                float _Strength;
                float _NightLevel;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float rim = pow(1.0 - saturate(dot(n, viewDir)), _RimPower);
                Light sun = GetMainLight();
                float day = saturate(dot(n, sun.direction) * 0.8 + 0.35);
                float glow = rim * lerp(_NightLevel, 1.0, day) * _Strength;
                return half4(_Color.rgb * glow, 1);
            }
            ENDHLSL
        }
    }
}
