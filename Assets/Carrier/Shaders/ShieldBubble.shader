// Energy shield bubble (URP, additive). The hexagon tiles come from the StylizedShieldsVol1
// hexagon sphere, whose every tile is UV-mapped onto T_ShieldHexagonUV_01 (white tile border,
// grey tile face). Idle: a faint shimmering rim. Hits (set by Shield.cs, up to 8 at a time):
// the tiles round each impact light up, and a ring of lit tile edges spreads out from it.
Shader "Carrier/ShieldBubble"
{
    Properties
    {
        _MainTex ("Hexagon tiles", 2D) = "white" {}
        _NoiseTex ("Shimmer noise", 2D) = "gray" {}
        [HDR] _Color ("Colour", Color) = (0.35, 0.75, 1.8, 1)
        _Idle ("Idle visibility", Range(0, 1)) = 0.06
        _RimPower ("Rim sharpness", Range(0.5, 8)) = 3
        _Flash ("Whole-bubble flash", Range(0, 1)) = 0
        _HitRadius ("Hit size (m)", Float) = 30
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ShieldBubble"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _NoiseTex_ST;
                float4 _Color;
                float _Idle;
                float _RimPower;
                float _Flash;
                float _HitRadius;
            CBUFFER_END

            // xyz = world position of the hit, w = its age in seconds (negative = slot unused)
            float4 _Hits[8];
            // x = strength, y = size (multiplies _HitRadius)
            float4 _HitStrength[8];

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 toCam = normalize(GetCameraPositionWS() - i.positionWS);
                float rim = pow(1.0 - saturate(abs(dot(n, toCam))), _RimPower);

                float tile = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r;
                float edge = saturate((tile - 0.9) * 10.0);   // the white tile borders
                float face = step(0.5, tile);                 // inside a tile
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.uv * 2.0 + _Time.y * 0.02).r;

                float hit = 0.0;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float age = _Hits[k].w;
                    if (age >= 0.0)
                    {
                        float s = _HitStrength[k].x;
                        float r = _HitRadius * max(0.2, _HitStrength[k].y);
                        float d = distance(i.positionWS, _Hits[k].xyz);
                        float core = saturate(1.0 - d / r) * saturate(1.0 - age / 0.6);
                        float ringR = r * (0.3 + age * 2.4);
                        float ring = saturate(1.0 - abs(d - ringR) / (r * 0.22)) * saturate(1.0 - age / 1.6) * saturate(1.0 - age / 1.2);
                        hit += (core * core * 2.0 + ring) * s;
                    }
                }

                float idle = _Idle * (rim * 1.6 + 0.1) * (0.5 + noise);
                float glow = idle * (0.25 + edge)
                           + hit * (0.3 * face + 1.5 * edge)
                           + _Flash * (0.03 * face + 0.25 * edge + 0.5 * rim);
                if (!front) glow *= 0.45;
                return half4(_Color.rgb * glow, 1.0);
            }
            ENDHLSL
        }
    }
}
