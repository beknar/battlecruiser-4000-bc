// A planet's sky seen from inside its air (URP): a sphere round the camera, drawn behind
// everything (it is as far as the camera sees), coloured from the zenith to the horizon to below
// it about `_Up` (out from the planet, not the world's up: the ship can be anywhere on it), with
// the sun's disc and glow toward `_SunDir`. `_Alpha` thins it out as the ship climbs, so the
// stars and the star show through, and they are gone in the thick air.
Shader "Carrier/SkyDome"
{
    Properties
    {
        _Top ("Zenith", Color) = (0.3, 0.5, 0.8, 1)
        _Horizon ("Horizon", Color) = (0.7, 0.8, 0.9, 1)
        _Bottom ("Below the horizon", Color) = (0.5, 0.5, 0.5, 1)
        _SunColor ("Sun", Color) = (1, 0.95, 0.85, 1)
        _SunSize ("Sun size", Float) = 0.02
        _SunGlow ("Sun glow", Float) = 0.5
        _Up ("Up", Vector) = (0, 1, 0, 0)
        _SunDir ("To the sun", Vector) = (0, 1, 0, 0)
        _Alpha ("Thickness", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-50" "IgnoreProjector" = "True" }
        Pass
        {
            Name "SkyDome"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dirWS : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _Top, _Horizon, _Bottom, _SunColor, _Up, _SunDir;
                float _SunSize, _SunGlow, _Alpha;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.dirWS = ws - GetCameraPositionWS();
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dirWS);
                float y = dot(d, normalize(_Up.xyz));
                float3 c = y >= 0 ? lerp(_Horizon.rgb, _Top.rgb, pow(saturate(y), 0.55)) : lerp(_Horizon.rgb, _Bottom.rgb, pow(saturate(-y), 0.4));
                float s = dot(d, normalize(_SunDir.xyz));
                float disc = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.6, s);
                float glow = pow(saturate(s), 64.0) * _SunGlow + pow(saturate(s), 8.0) * _SunGlow * 0.3;
                c += _SunColor.rgb * (disc * 4.0 + glow);
                return half4(c, saturate(_Alpha + disc * 0.5 * _Alpha));
            }
            ENDHLSL
        }
    }
}
