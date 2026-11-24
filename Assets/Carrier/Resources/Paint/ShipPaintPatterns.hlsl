// The ship paint's patterns and decals (for ShipPaint.shader, after Lit's own includes).
// Everything here is in the ship's own space: _PaintToShip takes a renderer's object space into
// its ship's (the hull and every turret alike), and _PaintBounds* are the ship's bounds there
// (bow at +z, up +y, starboard +x).
#ifndef CARRIER_SHIP_PAINT_PATTERNS
#define CARRIER_SHIP_PAINT_PATTERNS

// (not in UnityPerMaterial: the ship's material holds the scheme, each renderer's property
// block its own _PaintToShip and _PaintDecals)
float4x4 _PaintToShip;
float4 _PaintA, _PaintB, _PaintC, _PaintD;
float _PaintPattern, _PaintScale, _PaintAngle, _PaintDetail, _PaintDecals;
float4 _PaintBoundsMin, _PaintBoundsMax;
float4 _LogoRect;   // x: centre along the ship (z), y: centre height (y), z: half its size, w: logo (-1 none)
float4 _TextRect;   // x: centre along the ship, y: centre height, z: letter height, w: how many letters
float4 _Text0, _Text1, _Text2;   // the letters, 4 to each (0 space, 1-26 A-Z, 27-36 0-9, 37 the dash)
float4 _DecalXform; // x: the logo's turn (radians), y: its sides (0 both, 1 port, 2 starboard), z, w: the lettering's
TEXTURE2D(_LogoTex); SAMPLER(sampler_LogoTex);
TEXTURE2D(_FontTex); SAMPLER(sampler_FontTex);

#define LOGO_COLS 4.0
#define LOGO_ROWS 3.0
#define FONT_COLS 8.0
#define FONT_ROWS 5.0
#define FONT_ADVANCE 0.75

// ------------------------------------------------------------------ noise (no sin hashes: steady on every GPU)

float PHash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float PHash13(float3 p3)
{
    p3 = frac(p3 * 0.1031);
    p3 += dot(p3, p3.zyx + 31.32);
    return frac((p3.x + p3.y) * p3.z);
}

float2 PHash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float PNoise3(float3 p)
{
    float3 i = floor(p);
    float3 f = p - i;
    f = f * f * (3.0 - 2.0 * f);
    float a = PHash13(i), b = PHash13(i + float3(1, 0, 0));
    float c = PHash13(i + float3(0, 1, 0)), d = PHash13(i + float3(1, 1, 0));
    float e = PHash13(i + float3(0, 0, 1)), g = PHash13(i + float3(1, 0, 1));
    float h = PHash13(i + float3(0, 1, 1)), k = PHash13(i + float3(1, 1, 1));
    return lerp(lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y), lerp(lerp(e, g, f.x), lerp(h, k, f.x), f.y), f.z);
}

float PFbm3(float3 p)
{
    float v = 0.0, a = 0.5;
    for (int o = 0; o < 4; o++) { v += a * PNoise3(p); p = p * 2.03 + 17.1; a *= 0.5; }
    return v / 0.9375;
}

float PNoise2(float2 p)
{
    float2 i = floor(p);
    float2 f = p - i;
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(PHash12(i), PHash12(i + float2(1, 0)), f.x), lerp(PHash12(i + float2(0, 1)), PHash12(i + float2(1, 1)), f.x), f.y);
}

float PFbm2(float2 p)
{
    float v = 0.0, a = 0.5;
    for (int o = 0; o < 4; o++) { v += a * PNoise2(p); p = p * 2.03 + 17.1; a *= 0.5; }
    return v / 0.9375;
}

// x: distance to the nearest point, y: to the second nearest, z: the nearest one's random value
float3 PVoronoi(float2 x)
{
    float2 n = floor(x);
    float2 f = x - n;
    float f1 = 8.0, f2 = 8.0, id = 0.0;
    for (int j = -1; j <= 1; j++)
        for (int i = -1; i <= 1; i++)
        {
            float2 g = float2(i, j);
            float2 r = g + PHash22(n + g) - f;
            float d = dot(r, r);
            if (d < f1) { f2 = f1; f1 = d; id = PHash12(n + g + 7.7); }
            else if (d < f2) f2 = d;
        }
    return float3(sqrt(f1), sqrt(f2), id);
}

// the hexagon a point is in: gv from its middle, id its middle
void PHexCell(float2 p, out float2 gv, out float2 id)
{
    const float2 r = float2(1.0, 1.7320508);
    const float2 h = r * 0.5;
    float2 a = p - r * floor(p / r) - h;
    float2 q = p - h;
    float2 b = q - r * floor(q / r) - h;
    gv = dot(a, a) < dot(b, b) ? a : b;
    id = p - gv;
}

// 0 in a hexagon's middle, 0.5 on its edge
float PHexDist(float2 gv)
{
    gv = abs(gv);
    return max(dot(gv, float2(0.5, 0.8660254)), gv.x);
}

// 1 at whole numbers, 0 halfway between: stripes with no break where frac wraps
float PTri(float x) { return abs(frac(x) - 0.5) * 2.0; }

// 1 where x is over edge, 0 under, smoothed over about a pixel
float POver(float x, float edge)
{
    float w = max(fwidth(x), 1e-5);
    return saturate((x - edge) / w + 0.5);
}

float POverB(float x, float edge) { return 1.0 - POver(x, edge); }

float2 PRot(float2 p, float a)
{
    float c = cos(a), s = sin(a);
    return float2(p.x * c - p.y * s, p.x * s + p.y * c);
}

// ------------------------------------------------------------------ the ten families
// Each gives (secondary, trim): how much of each colour, the rest primary.

// 0: bands. b: the point across the ship's bounds, -1 to 1 on each axis (bow +z, top +y)
float2 PBands(float3 b, int v)
{
    float2 m = 0;
    if (v == 1) m.x = POverB(b.y, -0.1);                                                    // belly
    else if (v == 2) { m.x = POverB(b.y, 0.0); m.y = POverB(abs(b.y), 0.05); }              // two-tone with a trim line
    else if (v == 3) { m.x = POverB(abs(b.x), 0.22); m.y = POver(abs(b.x), 0.22) * POverB(abs(b.x), 0.3); }   // racing stripe
    else if (v == 4) m.x = POverB(abs(abs(b.x) - 0.22), 0.07);                               // twin stripes
    else if (v == 5) { m.x = POver(b.z, 0.45); m.y = POverB(abs(b.z - 0.45), 0.03); }        // nose
    else if (v == 6) { m.x = POverB(b.z, -0.4); m.y = POverB(abs(b.z + 0.4), 0.03); }        // tail
    else if (v == 7) { m.x = POverB(b.y, -0.25); m.y = POverB(abs(b.y + 0.12), 0.07); }      // waterline
    else if (v == 8) { m.x = POverB(abs(b.z), 0.33); m.y = POverB(abs(abs(b.z) - 0.33), 0.03); }   // middle third
    else if (v == 9) { m.y = POverB(abs(b.x), 0.08); m.x = POverB(abs(b.y - 0.1), 0.08); }   // spine and side stripe
    return m;
}

// 1: stripes
float2 PStripes(float2 uv, int v)
{
    float a = (v == 1 || v == 6) ? 1.5707963 : (v == 2 || v == 7) ? 0.7853982 : (v == 3) ? -0.7853982
            : (v == 4) ? 0.5235988 : (v == 9) ? 1.0471976 : 0.0;
    float x = PRot(uv, a).x * 8.0;
    float t = PTri(x);
    float2 m = 0;
    if (v < 4) m.x = POver(t, 0.5);
    else if (v == 4) m.x = POver(t, 0.7);
    else if (v < 8) { m.x = POver(t, 0.5); m.y = POverB(abs(t - 0.5), 0.06); }
    else if (v == 8) m.y = POver(t, 0.9);
    else { m.x = POver(t, 0.45); m.y = m.x * POver(PTri(x * 0.5), 0.5); }
    return m;
}

// 2: chevrons
float2 PChevrons(float2 uv, int v)
{
    int k = v - 5 * (v / 5);
    float slope = k == 0 ? 0.6 : k == 1 ? 1.0 : k == 2 ? 1.6 : 2.4;
    float x = k == 4 ? uv.x * 6.0 + PTri(uv.y * 6.0) * 0.5 : uv.x * 6.0 + abs(uv.y) * slope * 6.0;
    float t = PTri(x);
    float2 m = 0;
    if (v < 5) m.x = POver(t, 0.5);
    else if (v < 8) { m.x = POver(t, 0.5); m.y = POverB(abs(t - 0.5), 0.07); }
    else { m.x = POver(t, 0.45); m.y = m.x * POver(PTri(x * 0.5), 0.5); }
    return m;
}

float PChecker(float2 g) { return sin(PI * g.x) * sin(PI * g.y); }

// 3: checks, grids, plaid and bricks
float2 PChecks(float2 uv, int v)
{
    float2 g = uv * 8.0;
    float2 m = 0;
    if (v == 0) m.x = POver(PChecker(g), 0.0);
    else if (v == 1) m.x = POver(PChecker(PRot(g, 0.7853982)), 0.0);
    else if (v == 2) m.x = POver(PChecker(g * 2.0), 0.0);
    else if (v == 3) m.y = POver(max(PTri(g.x), PTri(g.y)), 0.88);
    else if (v == 4) { m.x = POver(PChecker(g), 0.0); m.y = POver(max(PTri(g.x), PTri(g.y)), 0.9); }
    else if (v == 5)
    {
        m.x = POver(PTri(g.x * 0.5), 0.6);
        m.y = max(POver(PTri(g.y * 0.5), 0.65), POver(PTri(g.x * 1.5 + 0.5), 0.94));
    }
    else if (v == 6) { float2 h = PRot(g, 0.7853982); h.x *= 0.5; m.x = POver(PChecker(h), 0.0); }
    else if (v == 7)
    {
        float row = floor(g.y);
        float gx = g.x * 0.5 + row * 0.5;
        m.y = max(POver(PTri(g.y), 0.9), POver(PTri(gx), 0.94));
        m.x = POver(PHash12(float2(floor(gx), row)), 0.6);
    }
    else if (v == 8) { m.x = POver(PChecker(g), 0.0); m.y = m.x * POver(PChecker(g * 0.5), 0.0); }
    else { float s = PChecker(g * 0.5); m.x = POver(s, 0.0); m.y = POverB(abs(s), 0.12); }
    return m;
}

// 4: hexagons
float2 PHex(float2 uv, int v)
{
    float f = v == 5 ? 16.0 : v == 6 ? 4.0 : 8.0;
    float2 gv, id;
    PHexCell(uv * f, gv, id);
    float edge = PHexDist(gv);
    float rnd = PHash12(id * 1.37 + 3.1);
    float2 m = 0;
    if (v == 0) m.y = POver(edge, 0.44);
    else if (v == 1 || v == 5) m.x = step(0.5, rnd);
    else if (v == 2) { m.x = step(0.33, rnd) * (1.0 - step(0.66, rnd)); m.y = step(0.66, rnd); }
    else if (v == 3) { m.y = POver(edge, 0.45); m.x = step(0.7, rnd); }
    else if (v == 4) { m.x = step(0.6, rnd); m.y = POver(edge, 0.41); }
    else if (v == 6) { m.y = POver(edge, 0.46); m.x = step(0.75, rnd); }
    else if (v == 7) m.x = POverB(edge, 0.25);
    else if (v == 8) m.x = step(0.0, sin(id.x * 0.8 + id.y * 0.5));
    else { m.y = step(0.88, rnd); m.x = POver(edge, 0.45); }
    return m;
}

// 5: dots and rings
float2 PDots(float2 uv, int v)
{
    float2 g = uv * 10.0;
    float2 m = 0;
    float2 gv, id;
    if (v == 0) m.x = POverB(length(frac(g) - 0.5), 0.3);
    else if (v == 1) { PHexCell(g, gv, id); m.x = POverB(length(gv), 0.3); }
    else if (v == 2) { PHexCell(g * 0.5, gv, id); m.x = POverB(length(gv), 0.3) * step(0.5, PHash12(id + 1.3)); }
    else if (v == 3) m.x = POver(PTri(length(uv) * 10.0), 0.5);
    else if (v == 4) { float r = length(uv) * 10.0; m.x = POver(PTri(r), 0.5); m.y = m.x * POver(PTri(r * 0.5), 0.5); }
    else if (v == 5) { PHexCell(g, gv, id); float d = length(gv); m.x = POverB(d, 0.22); m.y = POverB(d, 0.33) * POver(d, 0.27); }
    else if (v == 6) { PHexCell(g, gv, id); m.x = POverB(length(gv), 0.1 + 0.3 * PHash12(id + 5.1)); }
    else if (v == 7) m.x = POver(PTri(atan2(uv.y, uv.x) / TWO_PI * 24.0), 0.5);
    else if (v == 8) { float3 c = PVoronoi(g * 0.8); m.x = POverB(c.x, 0.25); m.y = POverB(abs(c.x - 0.3), 0.035); }
    else m.x = POverB(length(frac(g * 3.0) - 0.5), 0.25);
    return m;
}

// 6: waves and scales
float2 PWaves(float2 uv, int v)
{
    float2 g = uv * 6.0;
    float2 m = 0;
    if (v == 0) m.x = POver(PTri(g.y + 0.3 * sin(g.x * 3.0)), 0.5);
    else if (v == 1) m.x = POver(PTri(g.y + 0.8 * sin(g.x * 2.0)), 0.5);
    else if (v == 2) m.x = POver(PTri(g.y + 0.3 * sin(g.x * 3.0) + 0.15 * sin(g.x * 7.3)), 0.5);
    else if (v == 3) m.x = POver(PTri(g.y + 0.5 * PTri(g.x)), 0.5);
    else if (v == 4 || v == 5)
    {
        float f = v == 4 ? 1.5 : 0.8;
        float gy = g.y * f, row = floor(gy);
        float gx = g.x * f + 0.5 * row;
        float d = length(float2(frac(gx) - 0.5, frac(gy)));
        if (v == 4) { m.y = POverB(abs(d - 0.5), 0.06); m.x = step(0.5, PHash12(float2(floor(gx), row))) * POverB(d, 0.5); }
        else { m.y = POverB(abs(d - 0.48), 0.09); m.x = POverB(d, 0.45); }
    }
    else if (v == 6)
    {
        float w = sin(length(g - float2(-3, 1)) * 3.0) + sin(length(g - float2(3, -1)) * 3.0);
        m.x = POver(w, 0.3); m.y = POver(w, 1.3);
    }
    else if (v == 7) { float t = PTri(g.y + 0.4 * sin(g.x * 2.0 + 1.7 * sin(g.y * 1.3))); m.x = POver(t, 0.55); m.y = POver(t, 0.85); }
    else if (v == 8) { float t = PTri(g.y + 0.3 * sin(g.x * 3.0)); m.x = POver(t, 0.5); m.y = POverB(abs(t - 0.5), 0.06); }
    else { m.x = POver(PTri(g.y + 0.3 * sin(g.x * 2.0)), 0.6); m.y = POver(PTri(g.x + 0.3 * sin(g.y * 2.0)), 0.7); }
    return m;
}

// 7: woodland camouflage (in three dimensions: no seams round the hull)
float2 PCamo(float3 q, int v)
{
    int k = v - 5 * (v / 5);
    float f = k == 0 ? 3.0 : k == 1 ? 4.5 : k == 2 ? 6.0 : k == 3 ? 8.0 : 11.0;
    float3 s = q * f;
    if (v >= 5) s += (float3(PFbm3(s * 0.5 + 5.0), PFbm3(s * 0.5 + 11.0), PFbm3(s * 0.5 + 23.0)) - 0.5) * 2.0;
    float n1 = PFbm3(s), n2 = PFbm3(s * 1.1 + 41.7);
    float t1 = k == 4 ? 0.56 : 0.5, t2 = k == 4 ? 0.66 : 0.6;
    return float2(POver(n1, t1), POver(n2, t2));
}

// 8: splinter (straight-edged shards) and digital (square pixels)
float2 PSplinter(float2 uv, int v)
{
    float2 m = 0;
    if (v < 5)
    {
        float f = v == 2 || v == 4 ? 8.0 : v == 3 ? 3.0 : 5.0;
        float2 g = uv * f;
        if (v == 1 || v == 3) g.x *= 0.5;
        float3 c = PVoronoi(g);
        if (v == 0 || v == 3) m.x = step(0.5, c.z);
        else { m.x = step(0.4, c.z); m.y = step(0.75, c.z); }
        if (v == 4) m.y = max(m.y, POverB(c.y - c.x, 0.06));
    }
    else
    {
        int k = v - 5;
        float px = k == 0 ? 40.0 : k == 1 ? 60.0 : k == 2 ? 80.0 : k == 3 ? 30.0 : 50.0;
        float2 cell = (floor(uv * px) + 0.5) / px;
        float jitter = (PHash12(floor(uv * px)) - 0.5) * 0.12;
        float blob = k == 2 ? 3.0 : k == 3 ? 6.0 : 4.5;
        float n1 = PFbm2(cell * blob) + jitter, n2 = PFbm2(cell * blob * 1.3 + 19.0) + jitter;
        m.x = step(0.5, n1);
        if (k != 0) m.y = step(k == 4 ? 0.56 : 0.62, n2);
    }
    return m;
}

// 9: tiger stripes, flames, teeth and triangles
float2 PTriangles(float2 g, float f, float tones)
{
    g *= f;
    float2 s = float2(g.x + g.y * 0.57735, g.y * 1.1547);
    float2 cell = floor(s);
    float2 fr = s - cell;
    float rnd = PHash12(cell * 2.0 + (fr.x > fr.y ? 1.0 : 0.0));
    return tones > 2.5 ? float2(step(0.33, rnd) * (1.0 - step(0.66, rnd)), step(0.66, rnd)) : float2(step(0.5, rnd), step(0.85, rnd));
}

float2 PTiger(float2 uv, float3 q, float3 b, int v)
{
    float2 m = 0;
    if (v <= 1)
    {
        float x = uv.x * 7.0 + (PFbm3(q * 3.0) - 0.5) * 3.0;
        float thr = 0.55 + 0.4 * PFbm3(q * 2.3 + 7.0);
        m.x = POver(PTri(x), thr);
        if (v == 1) m.y = POver(PTri(x + 0.5), thr + 0.15);
    }
    else if (v == 2) m.x = POver(PTri(uv.x * 5.0 + (PFbm3(q * 2.0) - 0.5) * 5.0), 0.5);
    else if (v == 3)
    {
        // flames from the bow
        float s = (b.z + 1.0) * 0.5;
        float edge = 0.62 + 0.10 * sin(q.y * 40.0) + 0.16 * (PFbm3(q * 8.0) - 0.5);
        m.x = POver(s, edge); m.y = POver(s, edge + 0.12);
    }
    else if (v == 4) m = PTriangles(uv, 8.0, 2.0);
    else if (v == 5) m.x = POver(PTri(uv.y * 6.0 + frac(uv.x * 6.0) * 0.8), 0.5);
    else if (v == 6)
    {
        // shark's teeth at the bow
        float s = (b.z + 1.0) * 0.5;
        float edge = 0.75 + 0.08 * PTri(q.y * 20.0);
        m.y = POver(s, edge) * POverB(s, edge + 0.04);
        m.x = POver(s, edge + 0.04);
    }
    else if (v == 7) m = PTriangles(uv, 5.0, 3.0);
    else if (v == 8)
    {
        // claw marks: short slashes, three together
        float2 d = PRot(uv, 0.6) * 6.0;
        float x = d.x * 3.0;
        m.x = POver(PTri(x), 0.85) * step(0.6, PHash12(float2(floor(x / 3.0), floor(d.y))));
    }
    else m = PTriangles(uv, 3.0, 2.0);
    return m;
}

// ------------------------------------------------------------------ the scheme on a point

float2 PaintMask(float3 p, float3 n)
{
    float3 lo = _PaintBoundsMin.xyz, hi = _PaintBoundsMax.xyz;
    float3 size = max(hi - lo, 1e-3);
    float3 mid = (lo + hi) * 0.5;
    float len = max(size.z, max(size.x, size.y));
    float3 q = (p - mid) / len;             // the ship about -0.5 to 0.5 along its length
    float3 b = (p - mid) / size * 2.0;       // -1 to 1 across each of its bounds
    // flat patterns: seen from the side, from above or from the bow, whichever this faces most
    float3 an = abs(n);
    float2 uv = (an.x >= an.y && an.x >= an.z) ? q.zy : (an.y >= an.z ? q.zx : q.xy);
    uv = PRot(uv, _PaintAngle) * _PaintScale;
    float3 q3 = q * _PaintScale;

    int pat = (int)clamp(floor(_PaintPattern + 0.5), 0.0, 99.0);
    int fam = pat / 10;
    int v = pat - fam * 10;
    if (fam == 0) return PBands(b, v);
    if (fam == 1) return PStripes(uv, v);
    if (fam == 2) return PChevrons(uv, v);
    if (fam == 3) return PChecks(uv, v);
    if (fam == 4) return PHex(uv, v);
    if (fam == 5) return PDots(uv, v);
    if (fam == 6) return PWaves(uv, v);
    if (fam == 7) return PCamo(q3, v);
    if (fam == 8) return PSplinter(uv, v);
    return PTiger(uv, q3, b, v);
}

float LetterAt(float i)
{
    float4 v = i < 3.5 ? _Text0 : (i < 7.5 ? _Text1 : _Text2);
    float k = i - 4.0 * floor((i + 0.5) / 4.0);
    return k < 0.5 ? v.x : (k < 1.5 ? v.y : (k < 2.5 ? v.z : v.w));
}

// a point (along, up) about a decal's centre, turned back by `a` (so the decal turns by a, as read)
float2 DecalTurn(float2 d, float a)
{
    float c = cos(a), s = sin(a);
    return float2(c * d.x + s * d.y, -s * d.x + c * d.y);
}

// is a decal on this side (sides: 0 both, 1 port only, 2 starboard only)?
float DecalSide(float sides, float side)
{
    return sides < 0.5 ? 1.0 : (sides < 1.5 ? step(side, 0.0) : step(0.0, side));
}

// x: the logo here, y: the lettering (0 to 1)
float2 DecalMask(float3 p, float3 n)
{
    float2 res = 0;
    if (_PaintDecals < 0.5) return res;
    float cx = (_PaintBoundsMin.x + _PaintBoundsMax.x) * 0.5;
    float side = p.x >= cx ? 1.0 : -1.0;     // starboard reads toward the bow, port toward the stern
    float facing = saturate((n.x * side - 0.3) * 6.0);

    // the logo: a square, projected across the ship (turned, and on its sides)
    float2 l = DecalTurn(float2((p.z - _LogoRect.x) * side, p.y - _LogoRect.y), _DecalXform.x) / max(2.0 * _LogoRect.z, 1e-3) + 0.5;
    float2 ldx = ddx(l), ldy = ddy(l);
    if (_LogoRect.w > -0.5)
    {
        float inside = step(0.0, l.x) * step(l.x, 1.0) * step(0.0, l.y) * step(l.y, 1.0);
        float row = floor((_LogoRect.w + 0.5) / LOGO_COLS);
        float col = _LogoRect.w - row * LOGO_COLS;
        float2 lc = clamp(l, 0.005, 0.995);
        float2 auv = float2((col + lc.x) / LOGO_COLS, 1.0 - (row + 1.0 - lc.y) / LOGO_ROWS);
        float2 cells = float2(LOGO_COLS, LOGO_ROWS);
        res.x = SAMPLE_TEXTURE2D_GRAD(_LogoTex, sampler_LogoTex, auv, ldx / cells, ldy / cells).r * inside * facing * DecalSide(_DecalXform.y, side);
    }

    // the lettering: a row of letters, each FONT_ADVANCE of its height wide
    float h = max(_TextRect.z, 1e-3), count = _TextRect.w;
    float2 td = DecalTurn(float2((p.z - _TextRect.x) * side, p.y - _TextRect.y), _DecalXform.z);
    float2 t = float2(td.x / (h * FONT_ADVANCE) + count * 0.5, td.y / h + 0.5);
    float2 tdx = ddx(t), tdy = ddy(t);
    if (count > 0.5)
    {
        float inside = step(0.0, t.x) * step(t.x, count) * step(0.0, t.y) * step(t.y, 1.0);
        float ci = clamp(floor(t.x), 0.0, 11.0);
        float code = LetterAt(ci);
        float row = floor((code + 0.5) / FONT_COLS);
        float col = code - row * FONT_COLS;
        float2 fc = float2(clamp(t.x - ci, 0.005, 0.995), clamp(t.y, 0.005, 0.995));
        float2 auv = float2((col + fc.x) / FONT_COLS, 1.0 - (row + 1.0 - fc.y) / FONT_ROWS);
        float2 cells = float2(FONT_COLS, FONT_ROWS);
        res.y = SAMPLE_TEXTURE2D_GRAD(_FontTex, sampler_FontTex, auv, tdx / cells, tdy / cells).r * inside * facing * DecalSide(_DecalXform.w, side);
    }
    return res;
}

// The hull's colour, repainted: the scheme's colours where the pattern puts them, each as light
// or dark as the hull texture is there against its average (so panel lines and grime show
// through), and the logo and lettering over them.
float3 PaintAlbedo(float3 albedo, float2 uv, float3 p, float3 n)
{
    const float3 luma = float3(0.2126, 0.7152, 0.0722);
    // (the texture's smallest mip is its average colour)
    float3 avgCol = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 14.0).rgb * _BaseColor.rgb;
    float detail = clamp(dot(albedo, luma) / max(dot(avgCol, luma), 0.02), 0.0, 2.0);
    float3 col;
    // pattern -1: the factory finish, the hull's own colours, with only the logo and lettering on it
    if (_PaintPattern < -0.5) col = albedo;
    else
    {
        float2 m = PaintMask(p, n);
        float3 paint = lerp(lerp(_PaintA.rgb, _PaintB.rgb, saturate(m.x)), _PaintC.rgb, saturate(m.y));
        col = paint * lerp(1.0, detail, _PaintDetail);
    }
    float2 d = DecalMask(p, n);
    float3 decal = _PaintD.rgb * lerp(1.0, detail, _PaintDetail * 0.5);
    return lerp(col, decal, saturate(max(d.x, d.y)));
}

#endif
