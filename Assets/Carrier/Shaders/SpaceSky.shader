// The space sky (Space_Skybox, and the Nine Hells' own): one skybox material, three layers.
// - The far nebula: TC_Space, an equirect mapped as Skybox/Panoramic mapped it, graded: exposure,
//   a black level, a contrast curve round a pivot, a soft ceiling (so bloom is left to the stars)
//   and saturation. Its mip level is chosen here from the view: the usual way, mipmaps on an
//   equirect leave a seam where it wraps round.
// - The galaxy's masks (OrbitalBase's T_StarsMasks01/02, the layers of Unreal's M_NightSky), on
//   only with _SKY_MASKS (the builder sets it when they're staged): a band of nebula clouds (01:
//   G the clouds, R small stars, B the dark dust and the rift down the middle) and a band of
//   bright knots (02's G). Each is tinted, lies on its own great circle (_CloudEuler, _KnotEuler:
//   the band is the local XZ plane), is tiled along it, and turns very slowly round its own pole,
//   so the layers part as time goes by.
// - Point stars, made per cell of a cube round the sky (equal-angle, so the cells are near enough
//   the same size all over): a layer of a few thousand bright ones and a finer one of faint ones.
//   The brightest are HDR (above 1) so bloom makes them crisp points; they twinkle a little, and
//   a few bright ones are coloured. A star is drawn at least about a pixel wide and keeps its
//   light as the pixel grows, so a low-resolution render (a reflection) sees it dim, not blown out.
// _SkyEuler turns the whole sky, so each system can have its own. (The tints are colours, so
// sRGB: Unity makes them linear on the way here.)
Shader "Carrier/SpaceSky"
{
    Properties
    {
        _SkyEuler ("Sky rotation (Euler degrees)", Vector) = (0, 0, 0, 0)

        [Header(Far nebula)]
        [NoScaleOffset] _NebulaTex ("Nebula (equirect, HDR)", 2D) = "black" {}
        _NebulaTint ("Tint", Color) = (1, 1, 1, 1)
        _NebulaExposure ("Exposure", Float) = 1.3
        _NebulaBlack ("Black level", Float) = 0.022
        _NebulaPivot ("Contrast pivot", Float) = 0.12
        _NebulaContrast ("Contrast", Float) = 1.5
        _NebulaCeiling ("Soft ceiling", Float) = 1.5
        _NebulaSaturation ("Saturation", Float) = 1.2

        [Header(Galaxy masks)]
        [Toggle(_SKY_MASKS)] _Masks ("Masks staged", Float) = 0
        [NoScaleOffset] _CloudTex ("Clouds (R stars, G clouds, B dust)", 2D) = "black" {}
        _CloudEuler ("Clouds band (Euler degrees)", Vector) = (0, 0, 40, 0)
        _CloudBand ("Clouds band: half width (deg), repeats, offset, edge", Vector) = (28, 4, 0, 0.35)
        [HDR] _CloudTint ("Clouds tint", Color) = (0.70, 0.77, 1.09, 1)
        _CloudPower ("Clouds contrast", Float) = 1.6
        _CloudDust ("Dust in the clouds", Range(0, 1)) = 0.6
        [HDR] _BandStarTint ("The band's small stars", Color) = (1.09, 1.09, 1.17, 1)
        _DustDarken ("Dust in front of the nebula and stars", Range(0, 1)) = 0.3
        _CloudSpin ("Clouds turn (deg/s)", Float) = 0.01
        [NoScaleOffset] _KnotTex ("Knots (G)", 2D) = "black" {}
        _KnotEuler ("Knots band (Euler degrees)", Vector) = (6, 25, 46, 0)
        _KnotBand ("Knots band: half width (deg), repeats, offset, edge", Vector) = (34, 3, 0.3, 0.35)
        [HDR] _KnotTint ("Knots tint", Color) = (0.95, 0.88, 0.98, 1)
        _KnotPower ("Knots contrast", Float) = 1.6
        _KnotSpin ("Knots turn (deg/s)", Float) = -0.015

        [Header(Stars)]
        _StarSeed ("Seed", Float) = 0
        _StarCells ("Cells per cube face: bright, faint", Vector) = (96, 220, 0, 0)
        _StarDensity ("Share of cells with a star: bright, faint", Vector) = (0.09, 0.04, 0, 0)
        _StarBrightness ("Brightest (HDR): bright, faint", Vector) = (4, 0.8, 0, 0)
        _StarFalloff ("Faint to bright (power)", Float) = 6
        _StarSize ("Star radius (radians)", Float) = 0.0006
        _Twinkle ("Twinkle", Range(0, 1)) = 0.15
        _ColouredStars ("Share of the bright ones coloured", Range(0, 1)) = 0.25
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
            #pragma target 3.0
            #pragma shader_feature_local _SKY_MASKS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            static const float SKY_PI = 3.14159265359;
            static const float SKY_TAU = 6.28318530718;

            TEXTURE2D(_NebulaTex); SAMPLER(sampler_NebulaTex);
            TEXTURE2D(_CloudTex); SAMPLER(sampler_CloudTex);
            TEXTURE2D(_KnotTex); SAMPLER(sampler_KnotTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _SkyEuler;
                float4 _NebulaTex_HDR;
                half4 _NebulaTint;
                float _NebulaExposure, _NebulaBlack, _NebulaPivot, _NebulaContrast, _NebulaCeiling, _NebulaSaturation;
                float _Masks;
                float4 _CloudEuler, _CloudBand;
                half4 _CloudTint, _BandStarTint;
                float _CloudPower, _CloudDust, _DustDarken, _CloudSpin;
                float4 _KnotEuler, _KnotBand;
                half4 _KnotTint;
                float _KnotPower, _KnotSpin;
                float _StarSeed;
                float4 _StarCells, _StarDensity, _StarBrightness;
                float _StarFalloff, _StarSize, _Twinkle, _ColouredStars;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 sky : TEXCOORD0;     // the view direction in the sky's own frame
                float3 cloud : TEXCOORD1;   // and in each band's, turned (all linear in the
                float3 knot : TEXCOORD2;    // position, so they interpolate exactly)
            };

            // the rotation Unity's Quaternion.Euler makes: round Z, then X, then Y
            float3x3 EulerMatrix(float3 deg)
            {
                float3 s, c;
                sincos(radians(deg), s, c);
                float3x3 rx = float3x3(1, 0, 0, 0, c.x, -s.x, 0, s.x, c.x);
                float3x3 ry = float3x3(c.y, 0, s.y, 0, 1, 0, -s.y, 0, c.y);
                float3x3 rz = float3x3(c.z, -s.z, 0, s.z, c.z, 0, 0, 0, 1);
                return mul(ry, mul(rx, rz));
            }

            // world to a frame turned by `deg` (a row vector times the matrix: its inverse)
            float3 IntoFrame(float3 v, float3 deg) { return mul(v, EulerMatrix(deg)); }

            float3 TurnY(float3 v, float angle)
            {
                float s, c;
                sincos(angle, s, c);
                return float3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                float3 sky = IntoFrame(v.positionOS.xyz, _SkyEuler.xyz);
                o.sky = sky;
                o.cloud = TurnY(IntoFrame(sky, _CloudEuler.xyz), radians(_CloudSpin * _Time.y));
                o.knot = TurnY(IntoFrame(sky, _KnotEuler.xyz), radians(_KnotSpin * _Time.y));
                return o;
            }

            // The gradients for sampling at `uv`, whose x wraps round: taken from `altX` too (the
            // same coordinate with its wrap somewhere else) and the smaller kept, so the mip level
            // is right across the wrap and no seam shows.
            void SeamGrads(float2 uv, float altX, out float2 gx, out float2 gy)
            {
                gx = ddx(uv);
                gy = ddy(uv);
                float ax = ddx(altX), ay = ddy(altX);
                if (abs(ax) < abs(gx.x)) gx.x = ax;
                if (abs(ay) < abs(gy.x)) gy.x = ay;
            }

            // A band's texture coordinates for direction `d` in its frame (the band is the XZ
            // plane): x along it (`band.y` repeats round, offset by `band.z`), y across it (the
            // middle of the texture, 0.2 to 0.8 at its edges); `fade` 1 inside, 0 beyond its
            // half width `band.x` (degrees), from `band.w` of the way out.
            float2 BandUV(float3 d, float4 band, out float altX, out float fade)
            {
                float across = asin(clamp(d.y, -1.0, 1.0)) / radians(band.x);
                fade = 1.0 - smoothstep(band.w, 1.0, abs(across));
                altX = atan2(-d.z, -d.x) / SKY_TAU * band.y + band.z;
                return float2(atan2(d.z, d.x) / SKY_TAU * band.y + band.z, 0.5 + 0.3 * across);
            }

            float3 SkyHash(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            // One layer of stars: the cell of the cube round the sky that `d` is in, and the one
            // star that cell may hold, kept well inside it (so the cell's edge never cuts it).
            // `pixel` is the pixel's size in radians.
            float3 StarLayer(float3 d, float cells, float density, float brightness, float seed, float pixel)
            {
                float3 a = abs(d);
                float major, axis;
                float2 uv;
                if (a.x >= a.y && a.x >= a.z) { major = d.x; uv = d.yz; axis = 0; }
                else if (a.y >= a.z) { major = d.y; uv = d.xz; axis = 1; }
                else { major = d.z; uv = d.xy; axis = 2; }
                uv /= abs(major);
                float2 e = atan(uv) * (4.0 / SKY_PI);          // equal angles across the face, -1 to 1
                float2 cell = floor((e * 0.5 + 0.5) * cells);
                float face = axis * 2.0 + (major < 0.0 ? 1.0 : 0.0);
                float3 h = SkyHash(float3(cell, face * 17.0 + seed));
                if (h.z >= density) return float3(0, 0, 0);
                float3 h2 = SkyHash(h * 157.3 + float3(3.1, 7.7, 1.3));
                float2 se = (cell + 0.25 + 0.5 * h.xy) / cells * 2.0 - 1.0;
                float2 su = tan(se * (SKY_PI / 4.0));
                float side = major < 0.0 ? -1.0 : 1.0;
                float3 sd = axis == 0 ? float3(side, su.x, su.y) : (axis == 1 ? float3(su.x, side, su.y) : float3(su.x, su.y, side));
                sd = normalize(sd);
                // (the chord, not acos: acos loses the small angles a star is drawn at)
                float dist = length(d - sd);
                float sigma = max(_StarSize, pixel * 0.6);
                float peak = pow(h2.x, _StarFalloff) * brightness * min(1.0, (_StarSize * _StarSize) / (sigma * sigma));
                float t = _Time.y;
                float twinkle = 1.0 + _Twinkle * (0.6 * sin(t * (1.3 + 2.7 * h2.y) + SKY_TAU * h2.z) +
                                                  0.4 * sin(t * (4.1 + 3.3 * h.x) + SKY_TAU * h.y));
                float3 tint = lerp(float3(0.78, 0.86, 1.0), float3(1.0, 0.9, 0.76), h2.y);
                if (h2.x > 0.85 && h2.z < _ColouredStars)
                    tint = h.x > 0.5 ? float3(1.3, 0.55, 0.3) : float3(0.55, 0.75, 1.3);
                return tint * (peak * twinkle * exp(-0.5 * (dist * dist) / (sigma * sigma)));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.sky);
                // the pixel's size in radians (here, before any branch, as derivatives need)
                float pixel = max(length(ddx(d)), length(ddy(d)));

                // ---- the far nebula, mapped as Skybox/Panoramic did (latitude-longitude)
                float2 nuv = float2(0.5 - atan2(d.z, d.x) / SKY_TAU, 1.0 - acos(clamp(d.y, -1.0, 1.0)) / SKY_PI);
                float2 gx, gy;
                SeamGrads(nuv, frac(nuv.x + 0.5), gx, gy);
                float4 n = SAMPLE_TEXTURE2D_GRAD(_NebulaTex, sampler_NebulaTex, nuv, gx, gy);
                // (an HDR texture kept encoded on some platforms: decoded as DecodeHDR does)
                if (_NebulaTex_HDR.x > 0.0)
                    n.rgb *= _NebulaTex_HDR.x * pow(max(_NebulaTex_HDR.w * (n.a - 1.0) + 1.0, 0.0), _NebulaTex_HDR.y);
                float3 neb = n.rgb * _NebulaExposure;
                float lum = dot(neb, float3(0.2126, 0.7152, 0.0722));
                float graded = _NebulaPivot * pow(max((lum - _NebulaBlack) / max(_NebulaPivot, 1e-4), 1e-6), _NebulaContrast);
                graded = graded * _NebulaCeiling / (graded + _NebulaCeiling);
                neb *= graded / max(lum, 1e-5);
                neb = max(lerp(float3(graded, graded, graded), neb, _NebulaSaturation), 0.0) * _NebulaTint.rgb;

                // ---- the stars
                float3 stars = StarLayer(d, _StarCells.x, _StarDensity.x, _StarBrightness.x, _StarSeed, pixel) +
                               StarLayer(d, _StarCells.y, _StarDensity.y, _StarBrightness.y, _StarSeed + 50.0, pixel);

                float3 col;
            #if defined(_SKY_MASKS)
                // ---- the galaxy's band: clouds, its small stars, and its dust in front of the rest
                float altX, fade;
                float2 cuv = BandUV(normalize(i.cloud), _CloudBand, altX, fade);
                SeamGrads(cuv, altX, gx, gy);
                float4 m = SAMPLE_TEXTURE2D_GRAD(_CloudTex, sampler_CloudTex, cuv.yx, gx.yx, gy.yx);
                float3 clouds = pow(max(m.g, 0.0), _CloudPower) * lerp(1.0, m.b, _CloudDust) * fade * _CloudTint.rgb;
                float3 bandStars = m.r * m.r * m.b * fade * _BandStarTint.rgb;
                float dust = lerp(1.0, m.b, _DustDarken * fade);
                // ---- and the bright knots, on a band of their own
                float kAltX, kFade;
                float2 kuv = BandUV(normalize(i.knot), _KnotBand, kAltX, kFade);
                SeamGrads(kuv, kAltX, gx, gy);
                float4 k = SAMPLE_TEXTURE2D_GRAD(_KnotTex, sampler_KnotTex, kuv.yx, gx.yx, gy.yx);
                float3 knots = pow(max(k.g, 0.0), _KnotPower) * kFade * _KnotTint.rgb;
                col = (neb + stars) * dust + clouds + bandStars + knots;
            #else
                col = neb + stars;
            #endif
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
