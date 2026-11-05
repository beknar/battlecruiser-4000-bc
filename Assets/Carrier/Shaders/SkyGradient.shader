// The sky of a world with an atmosphere (landing sites): a gradient from the zenith colour down
// to the horizon colour (which matches the fog, so distant ground fades into it) and on to the
// ground colour below, with a sun disc and its glow. The sun direction is set by the game
// (_SunDir points toward the sun).
Shader "Carrier/SkyGradient"
{
    Properties
    {
        _Top ("Zenith", Color) = (0.25, 0.45, 0.85, 1)
        _Horizon ("Horizon", Color) = (0.7, 0.78, 0.88, 1)
        _Bottom ("Below the horizon", Color) = (0.4, 0.42, 0.45, 1)
        _Sharpness ("Horizon band (higher is thinner)", Float) = 2.5
        _SunDir ("Toward the sun", Vector) = (0, 0.5, 0.86, 0)
        _SunColor ("Sun", Color) = (1, 0.95, 0.85, 1)
        _SunSize ("Sun radius (radians)", Float) = 0.025
        _SunGlow ("Sun glow", Float) = 0.35
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Top, _Horizon, _Bottom, _SunColor;
                float4 _SunDir;
                float _Sharpness, _SunSize, _SunGlow;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                half3 col;
                if (d.y >= 0)
                    col = lerp(_Horizon.rgb, _Top.rgb, pow(saturate(d.y), 1.0 / max(0.1, _Sharpness)));
                else
                    col = lerp(_Horizon.rgb, _Bottom.rgb, saturate(-d.y * 6.0));
                float3 s = normalize(_SunDir.xyz);
                float c = dot(d, s);
                float disc = smoothstep(cos(_SunSize), cos(_SunSize * 0.75), c);
                float glow = pow(saturate(c), 12.0) * _SunGlow + pow(saturate(c), 200.0) * _SunGlow * 2.0;
                col += _SunColor.rgb * (disc * 12.0 + glow);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
