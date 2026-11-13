"""Model every refined material and ship part of the industry screen and render an icon of each.

Run:  blender -b --python build_goods.py -- <icons_dir> [id ...] [--fast]

Reads Assets/Carrier/Resources/Data/industry.json ("refined" and "parts"), builds each item
procedurally (bmesh primitives, lathes, curves), and renders <icons_dir>/<id>.png: 512x512 RGBA,
transparent background (film_transparent, straight alpha), Cycles on the CPU, 64 samples +
OpenImageDenoise. Optional ids render just those items; --fast renders 256 px / 16 samples
previews. The items are only ever seen as these icons, so the materials are Cycles node
materials (procedural noise/wave/brick textures, bevel-node edge wear, AO grime, thin film) and
nothing is exported.

Every item is modelled roughly 2 units across, standing on z = 0, with the camera's side being
-Y / +X. The camera direction and focal length are the same for every icon; the camera is moved
(never rotated) so each object fills FILL of the frame, and the studio lights (key, fill, two
rims) and a shadow catcher are placed relative to the object's bounds.
"""
import bpy, bmesh, math, os, sys, json, time, random
import numpy as np
from mathutils import Vector, Matrix, Euler

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, "..", ".."))
DATA = os.path.join(PROJECT, "Assets", "Carrier", "Resources", "Data", "industry.json")

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
FAST = "--fast" in argv
argv = [a for a in argv if not a.startswith("--")]
OUT = argv[0] if argv else os.path.join(PROJECT, "Assets", "Carrier", "Resources", "Industry", "Icons")
ONLY = argv[1:]
os.makedirs(OUT, exist_ok=True)

RES = 256 if FAST else 512
SAMPLES = 16 if FAST else 64
FILL = 0.78                      # the object's larger projected extent, as a fraction of the frame
CAM_AZ, CAM_EL, CAM_LENS = 35.0, 28.0, 60.0   # degrees from -Y towards +X, elevation, mm (36 mm sensor)

# ----------------------------------------------------------------------------------------------
# scene

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = "CYCLES"
try:
    bpy.context.preferences.addons["cycles"].preferences.compute_device_type = "NONE"
except Exception:
    pass
cy = scene.cycles
cy.device = "CPU"
cy.samples = SAMPLES
cy.use_adaptive_sampling = True
cy.adaptive_threshold = 0.03
cy.use_denoising = True
cy.denoiser = "OPENIMAGEDENOISE"
cy.denoising_use_gpu = False
cy.denoising_input_passes = "RGB_ALBEDO_NORMAL"
cy.max_bounces, cy.diffuse_bounces, cy.glossy_bounces = 12, 3, 4
cy.transmission_bounces, cy.transparent_max_bounces, cy.volume_bounces = 12, 8, 0
cy.caustics_reflective = cy.caustics_refractive = False
cy.blur_glossy = 0.5
cy.film_transparent_glass = False
scene.render.film_transparent = True
scene.render.resolution_x = scene.render.resolution_y = RES
scene.render.resolution_percentage = 100
ims = scene.render.image_settings
ims.file_format, ims.color_mode, ims.color_depth, ims.compression = "PNG", "RGBA", "8", 90
scene.view_settings.view_transform = "AgX"
for look in ("AgX - Medium High Contrast", "Medium High Contrast"):
    try:
        scene.view_settings.look = look
        break
    except Exception:
        pass
scene.view_settings.exposure = 0.0

OPT = {}   # per-item render options set by the builders ("backdrop": strength)
ITEM = bpy.data.collections.new("Item")
scene.collection.children.link(ITEM)
CREATED = []
FONT = None
for fp in (r"C:\Windows\Fonts\bahnschrift.ttf", r"C:\Windows\Fonts\arialbd.ttf"):
    if os.path.exists(fp):
        try:
            FONT = bpy.data.fonts.load(fp)
            break
        except Exception:
            pass


def rad(v):
    return [math.radians(a) for a in v]


def MT(loc=(0, 0, 0), rot=(0, 0, 0), scale=None):
    return Matrix.LocRotScale(Vector(loc), Euler(rad(rot)), Vector(scale) if scale else None)


def face_frame(p, az, up_tilt=0.0):
    """Frame at point p whose +Z faces horizontal azimuth `az` (deg from -Y towards +X), +Y up."""
    a = math.radians(az)
    n = Vector((math.sin(a), -math.cos(a), 0))
    t = Vector((math.cos(a), math.sin(a), 0))
    m = Matrix((t, Vector((0, 0, 1)), n)).transposed().to_4x4()
    m.translation = Vector(p)
    if up_tilt:
        m = m @ Matrix.Rotation(math.radians(-up_tilt), 4, "X")
    return m


# ----------------------------------------------------------------------------------------------
# node helpers

def _sock(coll, key):
    if isinstance(key, int):
        return coll[key]
    c = [s for s in coll if s.identifier == key] or [s for s in coll if s.name == key]
    if not c:
        raise KeyError(key)
    for s in c:
        if getattr(s, "enabled", True):
            return s
    return c[0]


def _out(v):
    if isinstance(v, bpy.types.Node):
        for s in v.outputs:
            if getattr(s, "enabled", True):
                return s
    return v


def put(nt, s, v):
    v = _out(v)
    if isinstance(v, bpy.types.NodeSocket):
        nt.links.new(v, s)
        return
    if isinstance(v, (tuple, list)) and len(v) == 3:
        try:
            if len(s.default_value) == 4:
                v = (*v, 1.0)
        except TypeError:
            pass
    s.default_value = v


def N(nt, kind, ins=None, **attrs):
    n = nt.nodes.new(kind)
    for k, v in attrs.items():
        setattr(n, k, v)
    for k, v in (ins or {}).items():
        if v is not None:
            put(nt, _sock(n.inputs, k), v)
    return n


def O(n, key):
    return _sock(n.outputs, key)


def mth(nt, op, a, b=None, clamp=False):
    ins = {0: a}
    if b is not None:
        ins[1] = b
    return O(N(nt, "ShaderNodeMath", ins, operation=op, use_clamp=clamp), "Value")


def mapr(nt, v, a, b, c=0.0, d=1.0, clamp=True):
    return O(N(nt, "ShaderNodeMapRange", {"Value": v, "From Min": a, "From Max": b, "To Min": c, "To Max": d},
               clamp=clamp), "Result")


def mixc(nt, fac, a, b, blend="MIX"):
    return O(N(nt, "ShaderNodeMix", {"Factor": fac, "A": a, "B": b}, data_type="RGBA", blend_type=blend), "Result")


def mixf(nt, fac, a, b):
    return O(N(nt, "ShaderNodeMix", {"Factor": fac, "A": a, "B": b}, data_type="FLOAT"), "Result")


def ramp(nt, fac, stops, interp="LINEAR"):
    n = N(nt, "ShaderNodeValToRGB", {"Fac": fac})
    cr = n.color_ramp
    cr.interpolation = interp
    while len(cr.elements) < len(stops):
        cr.elements.new(0.5)
    for e, (p, c) in zip(cr.elements, stops):
        e.position = p
        e.color = (*c, 1.0) if len(c) == 3 else c
    return O(n, "Color")


def noise(nt, co, scale, detail=4.0, rough=0.55, distortion=0.0):
    return O(N(nt, "ShaderNodeTexNoise", {"Vector": co, "Scale": scale, "Detail": detail, "Roughness": rough,
                                          "Distortion": distortion}), "Fac")


def wave(nt, co, scale, kind="BANDS", direction="Z", profile="SIN", distortion=0.0, detail=2.0):
    a = dict(wave_type=kind, wave_profile=profile)
    if kind == "BANDS":
        a["bands_direction"] = direction
    else:
        a["rings_direction"] = direction
    return O(N(nt, "ShaderNodeTexWave", {"Vector": co, "Scale": scale, "Distortion": distortion, "Detail": detail},
               **a), "Fac")


def sep(nt, co, axis):
    return O(N(nt, "ShaderNodeSeparateXYZ", {"Vector": co}), axis)


# ----------------------------------------------------------------------------------------------
# materials

def new_mat(name):
    m = bpy.data.materials.new(name)
    nt = m.node_tree
    nt.nodes.clear()
    return m, nt


def pbr(name, color, metal=0.0, rough=0.5, *, var=0.1, cvar=0.12, scale=3.0, wear=0.0,
        wear_col=(0.56, 0.56, 0.57), wear_rough=0.32, grime=0.35, bump=0.12, bevel=0.006, coat=0.0,
        aniso=0.0, film=0.0, sheen=0.0, spec=0.5, trans=0.0, ior=1.5, pattern=None, coords="Object",
        ao=True, chips=0.5, subsurf=0.0):
    m, nt = new_mat(name)
    out = N(nt, "ShaderNodeOutputMaterial")
    bs = N(nt, "ShaderNodeBsdfPrincipled")
    nt.links.new(O(bs, "BSDF"), _sock(out.inputs, "Surface"))
    co = O(N(nt, "ShaderNodeTexCoord"), coords)
    pat = pattern(nt, co) if pattern else {}
    n1 = noise(nt, co, scale, 6, 0.6)
    col = pat.get("col", color)
    v = mapr(nt, n1, 0.3, 0.7, 1 - cvar, 1 + cvar)
    col = mixc(nt, 1.0, col, v, "MULTIPLY")
    if grime > 0 and ao:
        aon = N(nt, "ShaderNodeAmbientOcclusion", {"Distance": 0.06}, samples=4)
        g = mapr(nt, O(aon, "AO"), 0.15, 1.0, 1 - grime, 1.0)
        col = mixc(nt, 1.0, col, g, "MULTIPLY")
    r0 = pat.get("rough", rough)
    rgh = mth(nt, "ADD", r0, mapr(nt, n1, 0.0, 1.0, -var, var, False))
    met = pat.get("metal", metal)
    bev = N(nt, "ShaderNodeBevel", {"Radius": bevel}, samples=4)
    nrm = O(bev, "Normal")
    if wear > 0:
        geo = N(nt, "ShaderNodeNewGeometry")
        dot = O(N(nt, "ShaderNodeVectorMath", {0: nrm, 1: O(geo, "Normal")}, operation="DOT_PRODUCT"), "Value")
        edge = mth(nt, "SUBTRACT", 1.0, dot)
        n2 = noise(nt, co, scale * 9, 5, 0.65)
        e = mth(nt, "MULTIPLY", edge, mapr(nt, n2, 0.3, 0.7, 0.2, 1.6))
        mask = mapr(nt, e, 0.012, 0.05)
        if chips > 0:
            n3 = noise(nt, co, scale * 4, 8, 0.7)
            chip = mapr(nt, n3, 0.70, 0.73, 0.0, chips)
            mask = mth(nt, "MAXIMUM", mask, chip)
        mask = mth(nt, "MULTIPLY", mask, wear)
        col = mixc(nt, mask, col, wear_col)
        met = mixf(nt, mask, met, 1.0)
        rgh = mixf(nt, mask, rgh, wear_rough)
    h = pat.get("height")
    if bump > 0 or h is not None:
        hh = noise(nt, co, scale * 25, 3, 0.5)
        if h is not None:
            hh = mth(nt, "ADD", mth(nt, "MULTIPLY", hh, 0.15), h)
        bp = N(nt, "ShaderNodeBump", {"Strength": max(bump, 0.05) if h is None else pat.get("hstr", 0.4),
                                      "Distance": pat.get("hdist", 0.002), "Height": hh, "Normal": nrm})
        nrm = O(bp, "Normal")
    ins = {"Base Color": col, "Metallic": met, "Roughness": rgh, "Normal": nrm, "Coat Weight": coat,
           "Coat Roughness": 0.08, "Anisotropic": aniso, "Sheen Weight": sheen, "Specular IOR Level": spec,
           "Transmission Weight": trans, "IOR": ior, "Subsurface Weight": subsurf}
    if coat:
        ins["Coat Normal"] = O(bev, "Normal")
    f = pat.get("film", film)
    if f:
        ins["Thin Film Thickness"] = f
        ins["Thin Film IOR"] = 2.0
    for k, val in ins.items():
        put(nt, _sock(bs.inputs, k), val)
    return m


def glow(name, color, strength):
    m, nt = new_mat(name)
    out = N(nt, "ShaderNodeOutputMaterial")
    e = N(nt, "ShaderNodeEmission", {"Color": color, "Strength": strength})
    nt.links.new(O(e, "Emission"), _sock(out.inputs, "Surface"))
    return m


def glass(name, color, ior=1.5, rough=0.0, diamond=False):
    m, nt = new_mat(name)
    out = N(nt, "ShaderNodeOutputMaterial")
    bev = N(nt, "ShaderNodeBevel", {"Radius": 0.004}, samples=6)
    if diamond:
        g = N(nt, "ShaderNodeBsdfGlass", {"Color": color, "Roughness": rough, "IOR": ior, "Normal": bev})
        nt.links.new(O(g, "BSDF"), _sock(out.inputs, "Surface"))
    else:
        g = N(nt, "ShaderNodeBsdfPrincipled", {"Base Color": color, "Roughness": rough, "IOR": ior,
                                               "Transmission Weight": 1.0, "Normal": bev})
        nt.links.new(O(g, "BSDF"), _sock(out.inputs, "Surface"))
    return m


# --- patterns: functions (nt, coords) -> {"col", "rough", "height", "film", ...}

def p_hazard(period=0.09, c1=(0.85, 0.58, 0.02), c2=(0.02, 0.02, 0.02)):
    def f(nt, co):
        w = wave(nt, co, 0.628 / period, "BANDS", "DIAGONAL", "SAW")
        return {"col": ramp(nt, w, [(0.0, c1), (0.5, c2)], "CONSTANT")}
    return f


def p_wood(nt, co):
    w = wave(nt, co, 1.6, "RINGS", "X", "SIN", distortion=7.0, detail=3)
    n = noise(nt, co, 14, 3)
    t = mth(nt, "ADD", mth(nt, "MULTIPLY", w, 0.6), mth(nt, "MULTIPLY", n, 0.4))
    return {"col": ramp(nt, t, [(0.25, (0.30, 0.18, 0.08)), (0.75, (0.58, 0.40, 0.22))]), "rough": 0.75,
            "height": mth(nt, "MULTIPLY", w, 0.3), "hstr": 0.25}


def p_rings(period, ca, cb, direction="Z", hstr=0.3):
    def f(nt, co):
        w = wave(nt, co, 0.314 / period, "RINGS", direction, "SIN", distortion=0.4, detail=2)
        n = noise(nt, co, 40, 2)
        t = mth(nt, "ADD", mth(nt, "MULTIPLY", w, 0.5), mth(nt, "MULTIPLY", n, 0.5))
        return {"col": ramp(nt, t, [(0.2, ca), (0.8, cb)]), "height": w, "hstr": hstr}
    return f


def p_bands(period, ca, cb, direction="Z"):
    def f(nt, co):
        w = wave(nt, co, 0.314 / period, "BANDS", direction, "SIN", distortion=0.3, detail=1)
        n = noise(nt, co, 6, 3)
        return {"col": ramp(nt, n, [(0.3, ca), (0.7, cb)]), "height": w, "hstr": 0.5}
    return f


def p_weave(cell, ca=(0.012, 0.012, 0.014), cb=(0.05, 0.05, 0.055)):
    def f(nt, co):
        ch = O(N(nt, "ShaderNodeTexChecker", {"Vector": co, "Scale": 1.0 / cell, "Color1": (1, 1, 1),
                                              "Color2": (0, 0, 0)}), "Fac")
        wx = wave(nt, co, 0.314 / cell, "BANDS", "X", "SIN")
        wy = wave(nt, co, 0.314 / cell, "BANDS", "Y", "SIN")
        return {"col": mixc(nt, ch, ca, cb), "rough": mixf(nt, ch, 0.42, 0.26), "height": mixf(nt, ch, wx, wy),
                "hstr": 0.6, "hdist": 0.003}
    return f


def p_film_noise(lo, hi, scale=2.0):
    def f(nt, co):
        return {"film": mapr(nt, noise(nt, co, scale, 3), 0.3, 0.7, lo, hi)}
    return f


def p_heat(axis, a, b, ta, tb):
    """Heat tint: thin film thickness from ta (at axis coord a) to tb (at b), with some noise."""
    def f(nt, co):
        t = mapr(nt, sep(nt, co, axis), a, b, ta, tb)
        n = mapr(nt, noise(nt, co, 3, 3), 0.3, 0.7, -15, 15)
        return {"film": mth(nt, "ADD", t, n)}
    return f


def p_heat_col(axis, a, b):
    """Heat-tinted steel: blue/purple at the hot end (a), through bronze and straw, to bare steel (b)."""
    def f(nt, co):
        t = mapr(nt, sep(nt, co, axis), a, b)
        t = mth(nt, "ADD", t, mapr(nt, noise(nt, co, 4, 3), 0.3, 0.7, -0.08, 0.08))
        return {"col": ramp(nt, t, [(0.0, (0.22, 0.26, 0.45)), (0.22, (0.38, 0.27, 0.42)),
                                    (0.42, (0.62, 0.42, 0.25)), (0.62, (0.66, 0.56, 0.38)),
                                    (0.85, (0.55, 0.54, 0.53))])}
    return f


def p_frost(zmax, base):
    def f(nt, co):
        z = mapr(nt, sep(nt, co, "Z"), 0.0, zmax, 1.0, 0.0)
        n = noise(nt, co, 9, 6, 0.65)
        m = mapr(nt, mth(nt, "ADD", z, mth(nt, "MULTIPLY", n, 0.9)), 0.95, 1.15)
        return {"col": mixc(nt, m, base, (0.85, 0.9, 0.95)), "rough": mixf(nt, m, 0.4, 0.95), "metal": 0.0,
                "height": mth(nt, "MULTIPLY", m, n), "hstr": 0.4}
    return f


def p_dies(cell):
    def f(nt, co):
        b = N(nt, "ShaderNodeTexBrick", {"Vector": co, "Scale": 1.0 / cell, "Mortar Size": 0.04,
                                         "Color1": (1, 1, 1), "Color2": (1, 1, 1), "Mortar": (0, 0, 0)},
              offset=0.0, squash=1.0)
        fac = O(b, "Fac")
        n = noise(nt, co, 3, 2)
        return {"film": mth(nt, "ADD", mapr(nt, n, 0.3, 0.7, 380, 620), mth(nt, "MULTIPLY", fac, 150)),
                "rough": mixf(nt, fac, 0.05, 0.3)}
    return f


MATDEF = {
    # paints (wear shows bare steel)
    "paint_hull": lambda n: pbr(n, (0.19, 0.22, 0.26), 0, 0.5, wear=0.7),
    "paint_light": lambda n: pbr(n, (0.50, 0.52, 0.55), 0, 0.45, wear=0.5),
    "paint_olive": lambda n: pbr(n, (0.17, 0.20, 0.11), 0, 0.55, wear=0.7),
    "paint_red": lambda n: pbr(n, (0.50, 0.05, 0.03), 0, 0.45, wear=0.5),
    "paint_blue": lambda n: pbr(n, (0.03, 0.14, 0.50), 0, 0.4, wear=0.5),
    "paint_yellow": lambda n: pbr(n, (0.85, 0.55, 0.03), 0, 0.45, wear=0.7),
    "paint_orange": lambda n: pbr(n, (0.85, 0.25, 0.03), 0, 0.45, wear=0.55),
    "paint_cyan": lambda n: pbr(n, (0.02, 0.40, 0.55), 0, 0.35, wear=0.5),
    "paint_white": lambda n: pbr(n, (0.78, 0.78, 0.76), 0, 0.4, wear=0.45),
    "paint_grey": lambda n: pbr(n, (0.36, 0.37, 0.39), 0, 0.5, wear=0.6),
    "paint_dark": lambda n: pbr(n, (0.07, 0.075, 0.085), 0, 0.5, wear=0.6),
    "hazard": lambda n: pbr(n, (1, 1, 1), 0, 0.5, wear=0.6, pattern=p_hazard()),
    "hazard_fine": lambda n: pbr(n, (1, 1, 1), 0, 0.5, wear=0.6, pattern=p_hazard(0.05)),
    # metals
    "steel_plate": lambda n: pbr(n, (0.16, 0.17, 0.19), 1, 0.42, cvar=0.35, scale=2.0, var=0.15),
    "steel": lambda n: pbr(n, (0.50, 0.51, 0.53), 1, 0.35),
    "steel_dark": lambda n: pbr(n, (0.22, 0.23, 0.25), 1, 0.4),
    "gunmetal": lambda n: pbr(n, (0.12, 0.125, 0.135), 1, 0.38, wear=0.3, wear_col=(0.45, 0.45, 0.46)),
    "titanium": lambda n: pbr(n, (0.42, 0.405, 0.385), 1, 0.34, cvar=0.1),
    "ti_heat": lambda n: pbr(n, (0.42, 0.405, 0.385), 1, 0.25, pattern=p_film_noise(220, 480, 4.0)),
    "aluminium": lambda n: pbr(n, (0.86, 0.87, 0.89), 1, 0.22, aniso=0.4, cvar=0.06),
    "alu_dark": lambda n: pbr(n, (0.55, 0.56, 0.58), 1, 0.3, cvar=0.06),
    "alu_coil": lambda n: pbr(n, (1, 1, 1), 1, 0.2, cvar=0.04,
                              pattern=p_rings(0.006, (0.70, 0.71, 0.73), (0.92, 0.93, 0.95))),
    "copper": lambda n: pbr(n, (0.93, 0.48, 0.28), 1, 0.25, cvar=0.08),
    "copper_wind": lambda n: pbr(n, (1, 1, 1), 1, 0.25, grime=0.2,
                                 pattern=p_bands(0.014, (0.80, 0.36, 0.18), (0.97, 0.55, 0.33))),
    "nickel": lambda n: pbr(n, (0.50, 0.48, 0.44), 1, 0.38, cvar=0.15, scale=4.0, var=0.15),
    "magnet_body": lambda n: pbr(n, (0.42, 0.42, 0.43), 1, 0.3),
    "gold": lambda n: pbr(n, (1.0, 0.68, 0.24), 1, 0.16, cvar=0.04, grime=0.15),
    "gold_foil": lambda n: pbr(n, (1.0, 0.68, 0.24), 1, 0.22, cvar=0.04, grime=0.1, bump=0.6, scale=2.0),
    "silver": lambda n: pbr(n, (0.95, 0.94, 0.92), 1, 0.12, cvar=0.03, grime=0.15),
    "platinum": lambda n: pbr(n, (0.74, 0.74, 0.72), 1, 0.2, cvar=0.03, grime=0.15),
    "zircaloy": lambda n: pbr(n, (0.68, 0.69, 0.70), 1, 0.22, cvar=0.05),
    "brass": lambda n: pbr(n, (0.88, 0.60, 0.24), 1, 0.25, cvar=0.08),
    "chrome": lambda n: pbr(n, (0.9, 0.9, 0.9), 1, 0.06, cvar=0.02, grime=0.1),
    "nozzle": lambda n: pbr(n, (1, 1, 1), 1, 0.32, pattern=p_heat_col("Z", -0.25, 1.0)),
    "nozzle_small": lambda n: pbr(n, (1, 1, 1), 1, 0.32, pattern=p_heat_col("Z", 0.0, 0.36)),
    "fin": lambda n: pbr(n, (0.24, 0.25, 0.27), 1, 0.35, cvar=0.05, grime=0.3),
    "bolt": lambda n: pbr(n, (0.40, 0.40, 0.42), 1, 0.4, cvar=0.2, grime=0.0),
    # other stock
    "wood": lambda n: pbr(n, (1, 1, 1), 0, 0.75, pattern=p_wood, bevel=0.004, grime=0.4),
    "rubber": lambda n: pbr(n, (0.025, 0.025, 0.027), 0, 0.7, grime=0.0),
    "cable_red": lambda n: pbr(n, (0.45, 0.04, 0.03), 0, 0.45, grime=0.0),
    "cable_yellow": lambda n: pbr(n, (0.85, 0.55, 0.03), 0, 0.45, grime=0.0),
    "cable_black": lambda n: pbr(n, (0.03, 0.03, 0.035), 0, 0.45, grime=0.0),
    "cable_blue": lambda n: pbr(n, (0.03, 0.18, 0.55), 0, 0.4, grime=0.0),
    "plastic_white": lambda n: pbr(n, (0.80, 0.82, 0.83), 0, 0.3, cvar=0.03, grime=0.2, subsurf=0.2),
    "plastic_black": lambda n: pbr(n, (0.03, 0.03, 0.035), 0, 0.4, grime=0.2),
    "plastic_spool": lambda n: pbr(n, (0.05, 0.055, 0.07), 0, 0.35, grime=0.2),
    "cap_blue": lambda n: pbr(n, (0.03, 0.12, 0.42), 0, 0.25, cvar=0.04, grime=0.2, coat=0.5),
    "case_shell": lambda n: pbr(n, (0.06, 0.065, 0.07), 0, 0.6, bump=0.5, scale=6.0),
    "foam_navy": lambda n: pbr(n, (0.02, 0.04, 0.16), 0, 0.95, sheen=1.0, bump=0.8, scale=8.0, grime=0.5),
    "cardboard": lambda n: pbr(n, (0.50, 0.36, 0.20), 0, 0.85, grime=0.2),
    "carbon_roll": lambda n: pbr(n, (1, 1, 1), 0, 0.3, pattern=p_weave(0.03), coords="UV", spec=0.9,
                                 cvar=0.05, grime=0.2),
    "carbon_sheet": lambda n: pbr(n, (1, 1, 1), 0, 0.3, pattern=p_weave(0.03), spec=0.9, cvar=0.05,
                                  grime=0.2),
    "carbon_end": lambda n: pbr(n, (1, 1, 1), 0, 0.35, pattern=p_rings(0.004, (0.01, 0.01, 0.012),
                                                                        (0.06, 0.06, 0.065)), grime=0.1),
    "label_white": lambda n: pbr(n, (0.82, 0.82, 0.78), 0, 0.6, grime=0.1, cvar=0.04),
    "label_yellow": lambda n: pbr(n, (0.90, 0.68, 0.04), 0, 0.5, grime=0.1, cvar=0.04),
    "stencil_white": lambda n: pbr(n, (0.85, 0.85, 0.82), 0, 0.6, ao=False, cvar=0.1),
    "stencil_black": lambda n: pbr(n, (0.02, 0.02, 0.02), 0, 0.6, ao=False),
    "stencil_red": lambda n: pbr(n, (0.6, 0.04, 0.03), 0, 0.6, ao=False),
    "wafer": lambda n: pbr(n, (0.30, 0.32, 0.38), 1, 0.04, cvar=0.0, grime=0.0, bump=0.0,
                           pattern=p_film_noise(380, 560, 1.5)),
    "wafer_dies": lambda n: pbr(n, (0.30, 0.32, 0.38), 1, 0.05, cvar=0.0, grime=0.0, bump=0.0,
                                pattern=p_dies(0.045)),
    "frost_white": lambda n: pbr(n, (0.80, 0.80, 0.78), 0, 0.4, wear=0.3, pattern=p_frost(0.45, (0.80, 0.80, 0.78))),
    "frost_cyan": lambda n: pbr(n, (0.02, 0.40, 0.55), 0, 0.35, wear=0.5, pattern=p_frost(0.35, (0.02, 0.40, 0.55))),
    # glass and glow
    "glass_armour": lambda n: glass(n, (0.94, 0.985, 0.97), 1.52, 0.0),
    "glass_pvb": lambda n: glass(n, (0.99, 0.97, 0.90), 1.48, 0.05),
    "glass_edge": lambda n: pbr(n, (0.25, 0.55, 0.45), 0, 0.08, grime=0.0, cvar=0.15, spec=1.0, bump=0.0),
    "glass_pvb_edge": lambda n: pbr(n, (0.75, 0.72, 0.55), 0, 0.3, grime=0.0, bump=0.0),
    "glass_teal": lambda n: glass(n, (0.45, 0.85, 0.80), 1.52, 0.02),
    "glass_clear": lambda n: glass(n, (0.95, 0.97, 1.0), 1.5, 0.0),
    "glass_dark": lambda n: glass(n, (0.35, 0.36, 0.40), 1.5, 0.0),
    "diamond": lambda n: glass(n, (1.0, 1.0, 1.0), 2.42, 0.0, diamond=True),
    "lens": lambda n: glass(n, (0.92, 0.9, 1.0), 1.7, 0.0, diamond=True),
    "glow_cyan": lambda n: glow(n, (0.25, 0.85, 1.0), 12.0),
    "glow_blue": lambda n: glow(n, (0.30, 0.55, 1.0), 10.0),
    "glow_orange": lambda n: glow(n, (1.0, 0.45, 0.08), 14.0),
    "glow_red": lambda n: glow(n, (1.0, 0.06, 0.03), 14.0),
    "glow_green": lambda n: glow(n, (0.25, 1.0, 0.3), 12.0),
    "glow_violet": lambda n: glow(n, (0.55, 0.25, 1.0), 5.0),
    "glow_amber": lambda n: glow(n, (1.0, 0.6, 0.1), 10.0),
}
MATS = {}


def M(name):
    if name not in MATS:
        MATS[name] = MATDEF[name](name)
    return MATS[name]


# ----------------------------------------------------------------------------------------------
# geometry helpers (everything ends up in ITEM; transforms are baked unless bake=False)

def finish(bm, name, mat, loc=(0, 0, 0), rot=(0, 0, 0), Mx=None, bake=True, smooth=None, bev=0.0):
    L = MT(loc, rot)
    full = (Mx @ L) if Mx is not None else L
    if bake:
        bm.transform(full)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    if smooth is not None:
        me.shade_smooth()
        me.set_sharp_from_angle(angle=math.radians(smooth))
    me.materials.append(M(mat))
    o = bpy.data.objects.new(name, me)
    ITEM.objects.link(o)
    o["baked"] = bake
    if not bake:
        o.matrix_world = full
    if bev:
        b = o.modifiers.new("bev", "BEVEL")
        b.width, b.segments, b.limit_method = bev, 2, "ANGLE"
        b.angle_limit = math.radians(40)
    CREATED.append(o)
    return o


def box(size, loc=(0, 0, 0), mat="steel", rot=(0, 0, 0), Mx=None, taper=None, bev=0.0, bake=True):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= size[0]
        v.co.y *= size[1]
        v.co.z *= size[2]
        if taper and v.co.z > 0:
            v.co.x *= taper[0]
            v.co.y *= taper[1]
    return finish(bm, "box", mat, loc, rot, Mx, bake=bake, bev=bev)


def cyl(r, h, loc=(0, 0, 0), mat="steel", rot=(0, 0, 0), Mx=None, segs=32, r2=None, smooth=True, bake=True):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r,
                          radius2=r if r2 is None else r2, depth=h)
    return finish(bm, "cyl", mat, loc, rot, Mx, bake=bake, smooth=40 if smooth else None)


def sphere(r, loc=(0, 0, 0), mat="steel", Mx=None, segs=24):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=segs // 2, radius=r)
    return finish(bm, "sph", mat, loc, (0, 0, 0), Mx, smooth=80)


def lathe(profile, loc=(0, 0, 0), mat="steel", rot=(0, 0, 0), Mx=None, segs=48, closed=False, bake=True,
          smooth=45, arc=None):
    """Revolve a (r, z) profile about Z. Go bottom->top on the outside for outward normals.
    `arc`=(a0, a1) degrees revolves only part way (closed profiles get end caps)."""
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new()
    rmax = max(r for r, _ in profile)
    s = [0.0]
    for k in range(1, len(profile)):
        s.append(s[-1] + math.hypot(profile[k][0] - profile[k - 1][0], profile[k][1] - profile[k - 1][1]))
    if arc:
        a0, a1 = math.radians(arc[0]), math.radians(arc[1])
        angs = [a0 + (a1 - a0) * i / segs for i in range(segs + 1)]
    else:
        angs = [2 * math.pi * i / segs for i in range(segs)]
    rings = []
    for r, z in profile:
        if r < 1e-6 and not arc:
            rings.append([bm.verts.new((0, 0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(a), r * math.sin(a), z)) for a in angs])
    pairs = list(zip(range(len(profile) - 1), range(1, len(profile))))
    if closed:
        pairs.append((len(profile) - 1, 0))
    nseg = segs
    for a, b in pairs:
        A, B = rings[a], rings[b]
        for i in range(nseg):
            j = i + 1 if arc else (i + 1) % segs
            if len(A) == 1 and len(B) == 1:
                continue
            if len(A) == 1:
                vs, us = [A[0], B[j], B[i]], [(i + 0.5, a), (i + 1, b), (i, b)]
            elif len(B) == 1:
                vs, us = [A[i], A[j], B[0]], [(i, a), (i + 1, a), (i + 0.5, b)]
            else:
                vs, us = [A[i], A[j], B[j], B[i]], [(i, a), (i + 1, a), (i + 1, b), (i, b)]
            try:
                f = bm.faces.new(vs)
            except ValueError:
                continue
            for loop, (ui, k) in zip(f.loops, us):
                loop[uvl].uv = (ui / segs * 2 * math.pi * rmax, s[k])
    if arc and closed:
        for idx in (0, segs):
            vs = [rings[k][idx] for k in range(len(profile))]
            try:
                bm.faces.new(vs)
            except ValueError:
                pass
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return finish(bm, "lathe", mat, loc, rot, Mx, bake=bake, smooth=smooth)


def ring(r_in, r_out, h, loc=(0, 0, 0), mat="steel", rot=(0, 0, 0), Mx=None, segs=48, bake=True):
    return lathe([(r_in, -h / 2), (r_out, -h / 2), (r_out, h / 2), (r_in, h / 2)], loc, mat, rot, Mx, segs,
                 closed=True, bake=bake, smooth=30)


def torus(R, r, loc=(0, 0, 0), mat="steel", rot=(0, 0, 0), Mx=None, segs=48, rsegs=12):
    prof = [(R + r * math.cos(2 * math.pi * k / rsegs), r * math.sin(2 * math.pi * k / rsegs)) for k in range(rsegs)]
    return lathe(prof, loc, mat, rot, Mx, segs, closed=True, smooth=60)


def prism(pts, depth, loc=(0, 0, 0), mat="steel", rot=(0, 0, 0), Mx=None, smooth=None):
    """Extrude a 2D polygon (x, y) along Z, centred on z = 0."""
    bm = bmesh.new()
    lo = [bm.verts.new((x, y, -depth / 2)) for x, y in pts]
    hi = [bm.verts.new((x, y, depth / 2)) for x, y in pts]
    bm.faces.new(list(reversed(lo)))
    bm.faces.new(hi)
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new([lo[i], lo[j], hi[j], hi[i]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return finish(bm, "prism", mat, loc, rot, Mx, smooth=smooth)


def pipe(points, r, mat="steel", Mx=None, res=4, vector=False, caps=True):
    cu = bpy.data.curves.new("pipe", "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = r
    cu.bevel_resolution = res
    cu.use_fill_caps = caps
    cu.resolution_u = 16
    sp = cu.splines.new("BEZIER")
    sp.bezier_points.add(len(points) - 1)
    for bp, p in zip(sp.bezier_points, points):
        p = Vector(p)
        if Mx is not None:
            p = Mx @ p
        bp.co = p
        bp.handle_left_type = bp.handle_right_type = "VECTOR" if vector else "AUTO"
    sp.use_smooth = True
    cu.materials.append(M(mat))
    o = bpy.data.objects.new("pipe", cu)
    ITEM.objects.link(o)
    o["baked"] = True
    CREATED.append(o)
    return o


def text(s, loc=(0, 0, 0), rot=(0, 0, 0), size=0.1, mat="stencil_white", Mx=None, align="CENTER", extrude=0.0):
    cu = bpy.data.curves.new("txt", "FONT")
    cu.body = s
    cu.size = size
    cu.align_x = align
    cu.align_y = "CENTER"
    cu.extrude = extrude
    if FONT:
        cu.font = FONT
    cu.materials.append(M(mat))
    o = bpy.data.objects.new("txt", cu)
    ITEM.objects.link(o)
    L = MT(loc, rot)
    o.matrix_world = (Mx @ L) if Mx is not None else L
    CREATED.append(o)
    return o


def bolt(loc, rot=(0, 0, 0), r=0.018, h=0.012, Mx=None, mat="bolt"):
    return cyl(r, h, loc, mat, rot, Mx, segs=6, smooth=False)


def bolt_ring(Mx, radius, count, r=0.018, h=0.012, z=0.0, mat="bolt", phase=0.0):
    for i in range(count):
        a = 2 * math.pi * (i + phase) / count
        bolt((radius * math.cos(a), radius * math.sin(a), z + h / 2), (0, 0, math.degrees(a)), r, h, Mx, mat)


def mark():
    return len(CREATED)


def move(start, Mx):
    """Apply Mx to every object created since `start`."""
    for o in CREATED[start:]:
        if o.get("baked"):
            o.data.transform(Mx)
        else:
            o.matrix_world = Mx @ o.matrix_world


def trefoil(Mx, size, back="label_yellow", fore="stencil_black", plate="square"):
    """Radiation trefoil sign in local XY facing +Z; `size` is the plate's width."""
    if plate == "square":
        box((size, size, 0.006), (0, 0, 0.003), back, Mx=Mx)
    else:
        cyl(size / 2, 0.006, (0, 0, 0.003), back, Mx=Mx, segs=32)
    R = size * 0.07
    cyl(R, 0.004, (0, 0, 0.008), fore, Mx=Mx, segs=20)
    for k in range(3):
        c = math.radians(90 + 120 * k)
        a0, a1 = c - math.radians(30), c + math.radians(30)
        pts = [(R * 1.5 * math.cos(a0 + (a1 - a0) * i / 8), R * 1.5 * math.sin(a0 + (a1 - a0) * i / 8)) for i in range(9)]
        pts += [(R * 5 * math.cos(a1 - (a1 - a0) * i / 8), R * 5 * math.sin(a1 - (a1 - a0) * i / 8)) for i in range(9)]
        prism(pts, 0.004, (0, 0, 0.008), fore, Mx=Mx)


def hv_sign(Mx, size):
    """Yellow high-voltage triangle with a black border and lightning bolt, local XY facing +Z."""
    tri = lambda s: [(-s / 2, -s * 0.289), (s / 2, -s * 0.289), (0, s * 0.577)]
    prism(tri(size), 0.006, (0, 0, 0.003), "stencil_black", Mx=Mx)
    prism(tri(size * 0.82), 0.004, (0, -size * 0.0, 0.007), "label_yellow", Mx=Mx)
    s = size * 0.38
    zig = [(0.05 * s, 0.55 * s), (-0.25 * s, -0.02 * s), (0.02 * s, 0.02 * s), (-0.1 * s, -0.5 * s),
           (0.25 * s, 0.08 * s), (-0.02 * s, 0.04 * s)]
    prism(zig, 0.004, (0, -size * 0.04, 0.011), "stencil_black", Mx=Mx)


def pallet(w, d, Mx=None):
    """Wooden pallet, deck top at z = 0.16, deck boards along X."""
    for y in (-d / 2 + 0.07, 0, d / 2 - 0.07):
        box((w, 0.14, 0.022), (0, y, 0.011), "wood", Mx=Mx)
    for x in (-w / 2 + 0.07, 0, w / 2 - 0.07):
        for y in (-d / 2 + 0.07, 0, d / 2 - 0.07):
            box((0.14, 0.14, 0.09), (x, y, 0.067), "wood", Mx=Mx)
        box((0.14, d, 0.022), (x, 0, 0.123), "wood", Mx=Mx)
    n = 5
    for i in range(n):
        y = -d / 2 + 0.1 + (d - 0.2) * i / (n - 1)
        box((w, 0.2, 0.026), (0, y, 0.147), "wood", Mx=Mx)


def strap_yz(x, y0, y1, zb, zt, mat="paint_blue", w=0.05, t=0.006):
    """A strap round a stack in the YZ plane at x: over the top and down both sides."""
    box((w, y1 - y0 + 2 * t, t), (x, (y0 + y1) / 2, zt + t / 2), mat)
    box((w, t, zt - zb), (x, y0 - t / 2, (zt + zb) / 2), mat)
    box((w, t, zt - zb), (x, y1 + t / 2, (zt + zb) / 2), mat)


def strap_xz(y, x0, x1, zb, zt, mat="paint_blue", w=0.05, t=0.006):
    box((x1 - x0 + 2 * t, w, t), ((x0 + x1) / 2, y, zt + t / 2), mat)
    box((t, w, zt - zb), (x0 - t / 2, y, (zt + zb) / 2), mat)
    box((t, w, zt - zb), (x1 + t / 2, y, (zt + zb) / 2), mat)


def bell_profile(r0, r1, z0, z1, t=0.02, n=14, power=0.55):
    """Closed (r, z) profile of a nozzle bell with wall thickness t, opening towards +z."""
    outer = [(r0 + (r1 - r0) * (k / n) ** power + t, z0 + (z1 - z0) * k / n) for k in range(n + 1)]
    inner = [(r0 + (r1 - r0) * (k / n) ** power, z0 + (z1 - z0) * k / n) for k in range(n, -1, -1)]
    return outer + inner


# ----------------------------------------------------------------------------------------------
# refined materials

def b_SteelPlate():
    pallet(2.1, 1.35)
    rnd = random.Random(3)
    z, t = 0.16, 0.05
    for i in range(8):
        dx, dy = rnd.uniform(-0.025, 0.025), rnd.uniform(-0.02, 0.02)
        box((1.95, 1.22, t), (dx, dy, z + t / 2), "steel_plate")
        box((0.12, 0.004, t * 0.7), (0.70 + dx, -0.611 + dy, z + t / 2), "paint_red")
        box((0.004, 0.10, t * 0.7), (0.976 + dx, -0.45 + dy, z + t / 2), "paint_red")
        z += t
    top = z
    for x in (-0.6, 0.6):
        strap_yz(x, -0.63, 0.63, 0.13, top)
        box((0.07, 0.09, 0.02), (x, -0.25, top + 0.012), "steel")
    text("S355 J2   50 MM", (-0.15, 0.12, top + 0.001), size=0.11)
    text("HEAT 4471-B", (-0.15, -0.05, top + 0.001), size=0.08)
    box((0.004, 0.16, 0.12), (0.99, 0.2, top - 0.08), "label_yellow")


def b_TitaniumBillet():
    s, L, gap = 0.17, 1.9, 0.035
    rnd = random.Random(5)
    z = 0.0
    for li, cnt in enumerate((5, 4, 3)):
        span = cnt * s + (cnt - 1) * gap + 0.1
        for x in (-0.6, 0.6):
            box((0.1, span, 0.06), (x, 0, z + 0.03), "wood")
        z += 0.06
        for i in range(cnt):
            y = (i - (cnt - 1) / 2) * (s + gap)
            dx = rnd.uniform(-0.06, 0.06)
            box((L, s, s), (dx, y, z + s / 2), "titanium")
            for sx in (-1, 1):
                box((0.004, s * 0.998, s * 0.998), (dx + sx * (L / 2 + 0.002), y, z + s / 2), "ti_heat")
            box((0.006, 0.05, 0.05), (dx + L / 2 + 0.005, y + 0.045, z + s / 2 + 0.045), "paint_white")
        if li == 0:
            y0 = -(cnt - 1) / 2 * (s + gap) - s / 2 - 0.001
            text("Ti-6Al-4V   BILLET 170", (0.0, y0, z + s / 2), (90, 0, 0), 0.075)
        z += s


def b_AluminiumSheet():
    pallet(1.5, 1.25)
    Ro, Ri, W = 0.78, 0.30, 0.95
    cz = 0.16 + Ro
    coil = MT((0, 0, cz), (90, 0, 0))
    c = 0.012
    lathe([(Ri, -W / 2 + c), (Ri + c, -W / 2), (Ro - c, -W / 2), (Ro, -W / 2 + c), (Ro, W / 2 - c), (Ro - c, W / 2),
           (Ri + c, W / 2), (Ri, W / 2 - c)], (0, 0, 0), "alu_coil", (0, 0, 0), coil, segs=96, closed=True,
          bake=False, smooth=30)
    for sx in (-1, 1):
        pts = [(0.30, 0.0), (0.74, 0.0), (0.74, 0.17), (0.44, 0.17)]
        prism([(sx * x, y) for x, y in pts], 1.0, (0, 0, 0.16), "wood", (90, 0, 0))
    t, w = 0.006, 0.045
    for th in (90, 215, 325):
        st = mark()
        box((t, w, W + 2 * t), (Ro + t / 2, 0, 0), "paint_blue")
        box((t, w, W + 2 * t), (Ri - t / 2, 0, 0), "paint_blue")
        for sz in (-1, 1):
            box((Ro - Ri, w, t), ((Ro + Ri) / 2, 0, sz * (W / 2 + t / 2)), "paint_blue")
        move(st, coil @ MT((0, 0, 0), (0, 0, th)))
    lab = MT((-0.42, -W / 2 - 0.003, cz + 0.42), (90, 0, 0))
    box((0.30, 0.18, 0.004), (0, 0, 0), "label_white", Mx=lab)
    text("AL 5083-H321", (0, 0.04, 0.003), size=0.05, mat="stencil_black", Mx=lab)
    text("4.0 x 1250", (0, -0.03, 0.003), size=0.045, mat="stencil_black", Mx=lab)


def spool(Mx, Rf, Rw, W):
    lathe([(0.07, -W / 2 - 0.04), (Rf, -W / 2 - 0.04), (Rf, -W / 2), (0.07, -W / 2)], mat="plastic_spool",
          Mx=Mx, closed=True, smooth=30)
    lathe([(0.07, W / 2), (Rf, W / 2), (Rf, W / 2 + 0.04), (0.07, W / 2 + 0.04)], mat="plastic_spool",
          Mx=Mx, closed=True, smooth=30)
    ring(0.07, 0.11, W + 0.1, mat="plastic_spool", Mx=Mx)
    lathe([(0.11, -W / 2), (Rw, -W / 2), (Rw + 0.012, 0), (Rw, W / 2), (0.11, W / 2)], mat="copper_wind",
          Mx=Mx, segs=64, closed=True, bake=False, smooth=60)
    lab = Mx @ MT((0, -(Rf + 0.11) / 2 - 0.02, W / 2 + 0.041))
    box((0.14, 0.09, 0.003), (0, 0, 0), "label_white", Mx=lab)
    text("CU-ETP 2.5", (0, 0, 0.002), size=0.028, mat="stencil_black", Mx=lab)


def b_CopperWire():
    spool(MT((-0.32, 0.30, 0.66 / 2 + 0.04)), 0.55, 0.46, 0.62)
    spool(MT((0.52, -0.28, 0.40), (0, 90, 15)), 0.40, 0.33, 0.48)
    spool(MT((-0.55, -0.55, 0.22 + 0.04), (0, 0, 0)), 0.30, 0.25, 0.36)
    pipe([(0.55, -0.56, 0.62), (0.72, -0.78, 0.45), (0.8, -0.85, 0.06), (0.6, -0.95, 0.01), (0.35, -0.9, 0.01)],
         0.009, "copper")


def b_Superalloy():
    for x in (-0.25, 0.25):
        box((0.1, 0.9, 0.07), (x, 0, 0.035), "wood")
    L, wb, wt, h, pitch = 0.64, 0.15, 0.12, 0.12, 0.158
    z = 0.07
    for layer in range(4):
        along_x = layer % 2 == 0
        for i in range(4):
            o = (i - 1.5) * pitch
            if along_x:
                box((L, wb, h), (0, o, z + h / 2), "nickel", taper=(0.9, wt / wb))
            else:
                box((wb, L, h), (o, 0, z + h / 2), "nickel", taper=(wt / wb, 0.9))
        z += h
    for i in range(4):
        o = (i - 1.5) * pitch
        box((0.05, wt * 0.95, 0.004), (0.18, o, z + 0.001), "paint_blue")
    text("Ni-Co 718", (-0.12, -1.5 * pitch, z + 0.002), size=0.045)
    for y in (-0.16, 0.16):
        strap_xz(y, -L / 2 - 0.01, L / 2 + 0.01, 0.07, z, "steel_dark", 0.035, 0.005)


def b_Magnets():
    W, D, H = 1.55, 1.05, 0.14
    box((W, D, 0.04), (0, 0, 0.02), "paint_grey")
    for sx in (-1, 1):
        box((0.03, D, H), (sx * (W / 2 - 0.015), 0, H / 2), "paint_grey")
    for sy in (-1, 1):
        box((W, 0.03, H), (0, sy * (D / 2 - 0.015), H / 2), "paint_grey")
    bw, bd, bh = 0.32, 0.2, 0.13
    for r in range(4):
        for c in range(4):
            x = (c - 1.5) * (bw + 0.03)
            y = (r - 1.5) * (bd + 0.03)
            flip = (r + c) % 2
            for k, col in enumerate(("paint_red", "paint_blue")):
                sx = (-1 if k == 0 else 1) * (1 if flip else -1)
                box((bw / 2 - 0.004, bd, bh), (x + sx * bw / 4, y, 0.04 + bh / 2), col)
            box((0.008, bd * 0.98, bh * 0.98), (x, y, 0.04 + bh / 2), "magnet_body")
    st = mark()
    for k, col in enumerate(("paint_red", "paint_blue")):
        box((bw / 2 - 0.004, bd, bh), ((k - 0.5) * bw / 2, 0, bh / 2), col)
    box((0.008, bd * 0.98, bh * 0.98), (0, 0, bh / 2), "magnet_body")
    move(st, MT((0.2, -0.13, 0.04 + bh), (0, 0, 28)))
    st = mark()
    for k, col in enumerate(("paint_blue", "paint_red")):
        box((bw / 2 - 0.004, bd, bh), ((k - 0.5) * bw / 2, 0, bh / 2), col)
    box((0.008, bd * 0.98, bh * 0.98), (0, 0, bh / 2), "magnet_body")
    move(st, MT((0.18, -0.12, 0.04 + 2 * bh), (0, 0, 16)))
    lab = MT((-0.35, -D / 2 - 0.002, H / 2 + 0.005), (90, 0, 0))
    box((0.55, 0.1, 0.004), (0, 0, 0), "label_yellow", Mx=lab)
    text("STRONG MAGNET  NdFeB", (0, 0, 0.003), size=0.045, mat="stencil_black", Mx=lab)


def foil_sheet(x0, x1, y0, y1, z, mat, amp=0.006):
    bm = bmesh.new()
    nx, ny = 8, 10
    vs = []
    for j in range(ny + 1):
        row = []
        for i in range(nx + 1):
            x = x0 + (x1 - x0) * i / nx
            y = y0 + (y1 - y0) * j / ny
            row.append(bm.verts.new((x, y, z + amp * math.sin(j * 1.3 + i * 0.7))))
        vs.append(row)
    for j in range(ny):
        for i in range(nx):
            bm.faces.new([vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = finish(bm, "foil", mat, smooth=60)
    o.modifiers.new("sol", "SOLIDIFY").thickness = 0.003


def bar(loc, L, w, h, mat, stamp=None, rz=0.0):
    box((L, w, h), (loc[0], loc[1], loc[2] + h / 2), mat, (0, 0, rz), taper=(0.88, 0.76))
    if stamp:
        text(stamp, (loc[0], loc[1], loc[2] + h + 0.001), (0, 0, rz), w * 0.28, "stencil_black")


def b_Conductors():
    box((1.3, 1.1, 0.03), (-0.02, -0.05, 0.015), "rubber")
    for sx in (-1, 1):
        box((0.03, 1.1, 0.05), (-0.02 + sx * 0.635, -0.05, 0.025), "rubber")
    top = 0.03
    L, w, h = 0.5, 0.21, 0.11
    gx = -0.3
    for y in (-0.23, 0.0, 0.23):
        bar((gx, y + 0.05, top), L, w, h, "gold")
    for y in (-0.115, 0.115):
        bar((gx, y + 0.05, top + h), L, w, h, "gold")
    bar((gx, 0.05, top + 2 * h), L, w, h, "gold", "AU 999.9")
    bar((0.38, 0.33, top), 0.56, 0.24, 0.12, "silver", "AG 999", rz=90)
    bar((0.38, 0.33, top + 0.12), 0.56, 0.24, 0.12, "silver", rz=90)
    bar((0.40, -0.05, top), 0.42, 0.19, 0.09, "platinum", "PT 950", rz=90)
    cyl(0.09, 0.62, (0.05, -0.43, top + 0.09), "gold_foil", (0, 90, 0), segs=48)
    cyl(0.03, 0.64, (0.05, -0.43, top + 0.09), "cardboard", (0, 90, 0))
    foil_sheet(-0.26, 0.36, -0.5, -0.58, top + 0.004, "gold_foil", 0.003)
    foil_sheet(-0.26, 0.36, -0.52, -0.58, top + 0.004, "gold_foil", 0.003)


def b_FuelRods():
    pitch, rr, L = 0.078, 0.033, 2.1
    zc = 0.56
    rods = [(q, r) for q in range(-3, 4) for r in range(-3, 4) if abs(q + r) <= 3]
    for q, r in rods:
        y = pitch * (q + r / 2)
        z = pitch * (r * math.sqrt(3) / 2)
        cyl(rr, L, (0, y, zc + z), "zircaloy", (0, 90, 0), segs=16)
    Rh = 3 * pitch + 0.055
    hexp = lambda R: [(R * math.cos(math.radians(60 * k)), R * math.sin(math.radians(60 * k))) for k in range(6)]
    for x in (-0.72, -0.24, 0.24, 0.72):
        prism(hexp(Rh), 0.05, (x, 0, zc), "steel", (0, 90, 0))
    for sx in (-1, 1):
        prism(hexp(Rh + 0.01), 0.12, (sx * (L / 2 + 0.06), 0, zc), "steel_dark", (0, 90, 0))
        prism(hexp(Rh * 0.6), 0.06, (sx * (L / 2 + 0.15), 0, zc), "steel", (0, 90, 0))
    trefoil(MT((L / 2 + 0.181, 0, zc), (90, 0, 90)), 0.26, plate="round")
    for x in (-0.6, 0.6):
        box((0.14, 0.62, zc - Rh * 0.866 - 0.06), (x, 0, 0.06 + (zc - Rh * 0.866 - 0.06) / 2), "paint_yellow")
        box((0.145, 0.625, 0.06), (x, 0, 0.12), "hazard")
        box((0.2, 0.7, 0.06), (x, 0, 0.03), "steel_dark")
    for y in (-0.25, 0.25):
        box((1.5, 0.08, 0.04), (0, y, 0.02), "steel_dark")
    text("U-235  4.2%  ASSY 0117", (0, -0.312, 0.30), (90, 0, 0), 0.05, "stencil_black")


def b_SiliconWafer():
    Rw, n, pitch = 0.42, 13, 0.065
    cz = 0.5
    for i in range(n):
        x = (i - (n - 1) / 2) * pitch
        cyl(Rw, 0.008, (x, 0, cz), "wafer", (0, 90, 0), segs=64)
    X = (n - 1) / 2 * pitch + 0.06
    for sy in (-1, 1):
        box((2 * X, 0.035, 0.42), (0, sy * 0.44, 0.32), "plastic_white", (sy * -8, 0, 0))
        for i in range(n + 1):
            x = (i - n / 2) * pitch
            box((0.026, 0.06, 0.36), (x, sy * 0.405, 0.33), "plastic_white", (sy * -8, 0, 0))
        cyl(0.025, 2 * X, (0, sy * 0.2, 0.1), "plastic_white", (0, 90, 0))
    for sx in (-1, 1):
        x = sx * (X + 0.02)
        box((0.04, 0.9, 0.1), (x, 0, 0.12), "plastic_white")
        box((0.04, 0.9, 0.07), (x, 0, 0.68), "plastic_white")
        for sy in (-1, 1):
            box((0.04, 0.1, 0.62), (x, sy * 0.4, 0.4), "plastic_white")
        box((0.04, 0.12, 0.55), (x, 0, 0.42), "plastic_white")
    box((0.05, 0.3, 0.06), (X + 0.06, 0, 0.42), "plastic_white")
    st = mark()
    cyl(0.34, 0.03, (0, 0, 0.015), "plastic_black", segs=48)
    cyl(Rw * 0.78, 0.006, (0, 0, 0.033), "wafer_dies", segs=64)
    move(st, MT((0.15, -0.78, 0.08), (32, 0, 18)))
    box((0.4, 0.22, 0.08), (0.17, -0.7, 0.04), "plastic_black", (0, 0, 18))


def pane(Mx, w, h, k):
    """Laminated pane in local XZ (thickness along -Y), bottom edge at z = 0. One glass slab (so rays
    through a stack stay within the bounce limit) with the laminate shown on its edges."""
    g, p = 0.026, 0.006
    T = 2 * g + p
    box((w, T, h), (0, -T / 2, h / 2), "glass_armour", Mx=Mx)
    e = 0.0015
    for (sx, sz, ew, eh) in ((0, 1, w, e), (0, -1, w, e), (1, 0, e, h), (-1, 0, e, h)):
        for (y0, t, m) in ((0, g, "glass_edge"), (g, p, "glass_pvb_edge"), (g + p, g, "glass_edge")):
            box((ew if sx else w + 2 * e, t, eh if sz else h + 2 * e),
                (sx * (w / 2 + e / 2), -y0 - t / 2, h / 2 + sz * (h / 2 + e / 2)), m, Mx=Mx)
    for sx in (-1, 1):
        for zz in (0.0, h):
            box((0.08, T + 0.012, 0.08), (sx * (w / 2 - 0.03), -T / 2, zz + (0.03 if zz == 0 else -0.03)),
                "plastic_black", Mx=Mx)
    if k == 0:
        box((0.26, 0.002, 0.12), (w / 2 - 0.3, -T - 0.001, h - 0.18), "label_white", Mx=Mx)
        text("ARMOUR GLASS 58", (w / 2 - 0.3, -T - 0.003, h - 0.16), (90, 0, 0), 0.032, "stencil_black", Mx=Mx)
        text("LAMINATED  4+1", (w / 2 - 0.3, -T - 0.003, h - 0.2), (90, 0, 0), 0.026, "stencil_red", Mx=Mx)


def b_ArmourGlass():
    OPT["backdrop"] = 1.0
    lean = 18.0
    H = 1.3
    for sx in (-1, 1):
        x = sx * 0.72
        for sy in (-1, 1):
            st = mark()
            box((0.07, 0.07, H / math.cos(math.radians(lean)) + 0.05), (0, 0, 0), "paint_red")
            move(st, MT((x, sy * (H * math.tan(math.radians(lean)) / 2 + 0.03), 0.08 + H / 2), (sy * lean, 0, 0)))
        box((0.08, 1.2, 0.08), (x, 0, 0.04), "paint_red")
    box((1.52, 0.08, 0.08), (0, 0, H + 0.08), "paint_red")
    for sy in (-1, 1):
        box((1.6, 0.3, 0.05), (0, sy * 0.55, 0.06), "paint_red")
        box((1.5, 0.26, 0.012), (0, sy * 0.55, 0.091), "rubber")
    for k in range(3):
        Mx = MT((0, -0.49 - k * 0.064, 0.097), (-lean, 0, 0))
        pane(Mx, 1.42, 1.2 - k * 0.05, 0 if k == 2 else 5)
    for k in range(2):
        Mx = MT((0, 0.49 + k * 0.064, 0.097), (-lean, 0, 180))
        pane(Mx, 1.42, 1.15, 5)


def b_DiamondOptics():
    OPT["backdrop"] = 1.0
    W, D, H = 1.5, 1.0, 0.2
    box((W, D, H), (0, 0, H / 2), "case_shell", bev=0.02)
    for sy in (-1, 1):
        box((W + 0.01, 0.02, 0.03), (0, sy * (D / 2 - 0.005), H - 0.01), "alu_dark")
    for sx in (-1, 1):
        box((0.02, D + 0.01, 0.03), (sx * (W / 2 - 0.005), 0, H - 0.01), "alu_dark")
    box((W - 0.06, D - 0.06, 0.02), (0, 0, H - 0.005), "foam_navy")
    for x in (-0.45, 0.45):
        box((0.12, 0.03, 0.08), (x, -D / 2 - 0.012, H - 0.04), "alu_dark")
    lid = MT((0, D / 2, H), (-102, 0, 0))
    st = mark()
    box((W, D, 0.12), (0, -D / 2, 0.06), "case_shell", bev=0.02)
    box((W - 0.06, D - 0.06, 0.02), (0, -D / 2, -0.004), "foam_navy")
    move(st, lid)
    plate = lid @ MT((0, -D * 0.5, -0.016), (180, 0, 0))
    box((0.6, 0.16, 0.004), (0, 0, 0), "alu_dark", Mx=plate)
    text("DIAMOND OPTICS", (0, 0.025, 0.003), size=0.055, mat="stencil_black", Mx=plate)
    text("FRAGILE - HANDLE WITH CARE", (0, -0.04, 0.003), size=0.03, mat="stencil_red", Mx=plate)
    top = H + 0.005
    lp = [(0.0, -0.075), (0.15, -0.055), (0.26, -0.02), (0.27, 0.0), (0.26, 0.02), (0.15, 0.055), (0.0, 0.075)]
    lathe(lp, (-0.35, 0.0, top + 0.2), "diamond", (62, 0, -20), segs=64, smooth=80)
    lathe([(0.0, -0.03), (0.13, -0.012), (0.14, 0.0), (0.14, 0.02), (0.0, 0.02)], (0.05, -0.28, top + 0.03),
          "diamond", (0, 0, 0), segs=48, smooth=70)
    s = 0.3
    tri = [(-s / 2, 0), (s / 2, 0), (0, s * 0.866)]
    prism(tri, 0.42, (0.42, -0.12, top), "diamond", (90, 0, 70))
    gem = [(0.0, 0.0), (0.17, 0.16), (0.17, 0.175), (0.11, 0.235), (0.0, 0.235)]
    lathe(gem, (0.18, 0.22, top - 0.04), "diamond", (0, 0, 10), segs=8, smooth=None)


def roll_c(Mx, R, L):
    lathe([(R, -L / 2), (R, L / 2)], mat="carbon_roll", Mx=Mx, segs=64, smooth=60)
    for sz in (-1, 1):
        ring(0.075, R, 0.004, (0, 0, sz * L / 2), "carbon_end", Mx=Mx, segs=64, bake=False)
    ring(0.06, 0.075, L + 0.08, mat="cardboard", Mx=Mx)
    cyl(R + 0.002, 0.05, (0, 0, L / 2 - 0.12), "label_yellow", Mx=Mx, segs=64)


def b_CarbonFibre():
    R, L = 0.27, 1.55
    rolls = [(-0.285, R), (0.285, R), (0.0, R + math.sqrt((2 * R + 0.005) ** 2 - 0.285 ** 2))]
    for y, z in rolls:
        roll_c(MT((0, y, z), (0, 90, 0)), R, L)
    for x in (-0.55, 0.55):
        for sy in (-1, 1):
            prism([(0, 0), (0.16, 0), (0.0, 0.13)], 0.12, (x, sy * 0.45, 0), "wood", (90, 0, 90 + 90 * sy))
    bm = bmesh.new()
    nx, ny = 6, 14
    vs = []
    for j in range(ny + 1):
        row = []
        t = j / ny
        for i in range(nx + 1):
            x = -L / 2 + 0.04 + (L - 0.08) * i / nx
            y = -0.285 - 0.03 - t * 0.85
            z = 0.004 + 0.02 * math.exp(-t * 6) + 0.012 * math.sin(t * 9 + i * 0.4) * t
            row.append(bm.verts.new((x, y, z)))
        vs.append(row)
    for j in range(ny):
        for i in range(nx):
            bm.faces.new([vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = finish(bm, "sheet", "carbon_sheet", smooth=60)
    o.modifiers.new("sol", "SOLIDIFY").thickness = 0.004
    lab = MT((0.45, -0.285, 2 * R + 0.001))
    box((0.22, 0.12, 0.003), (0, 0, 0), "label_white", Mx=lab)
    text("CF 3K TWILL", (0, 0.015, 0.002), size=0.032, mat="stencil_black", Mx=lab)
    text("200 GSM", (0, -0.03, 0.002), size=0.026, mat="stencil_black", Mx=lab)


def drum(Mx, valve=False, gauge=False):
    prof = [(0, 0.01), (0.285, 0.01), (0.3, 0.03), (0.3, 0.29), (0.313, 0.30), (0.313, 0.315), (0.3, 0.325),
            (0.3, 0.57), (0.313, 0.58), (0.313, 0.595), (0.3, 0.605), (0.3, 0.86), (0.306, 0.875), (0.29, 0.89),
            (0.278, 0.875), (0.278, 0.862), (0, 0.862)]
    lathe(prof, mat="frost_cyan", Mx=Mx, segs=48, smooth=40)
    cyl(0.302, 0.1, (0, 0, 0.44), "paint_white", Mx=Mx, segs=48)
    cyl(0.035, 0.03, (0.17, 0.0, 0.875), "steel", Mx=Mx, segs=16)
    pl = Mx @ face_frame((0, 0, 0.445), 15) @ MT((0, 0, 0.3))
    box((0.13, 0.13, 0.004), (0, 0, 0.005), "stencil_black", (0, 0, 45), Mx=pl)
    box((0.115, 0.115, 0.004), (0, 0, 0.008), "label_white", (0, 0, 45), Mx=pl)
    text("NH3", (0, 0, 0.011), size=0.04, mat="stencil_black", Mx=pl)
    if valve:
        cyl(0.03, 0.18, (-0.1, 0.05, 0.95), "steel", Mx=Mx, segs=16)
        torus(0.065, 0.012, (-0.1, 0.05, 1.04), "paint_red", Mx=Mx, segs=24, rsegs=8)
        box((0.12, 0.012, 0.012), (-0.1, 0.05, 1.04), "paint_red", Mx=Mx)
        box((0.012, 0.12, 0.012), (-0.1, 0.05, 1.04), "paint_red", Mx=Mx)
    if gauge:
        g = Mx @ MT((0.08, -0.12, 0.98), (70, 0, 20))
        cyl(0.012, 0.14, (0, 0.05, -0.04), "steel", (70, 0, 0), Mx=g, segs=12)
        cyl(0.06, 0.03, (0, 0, 0), "steel", Mx=g, segs=32)
        cyl(0.05, 0.004, (0, 0, 0.016), "label_white", Mx=g, segs=32)
        box((0.004, 0.04, 0.003), (0.008, 0.012, 0.02), "stencil_red", (0, 0, -30), Mx=g)


def b_Coolant():
    box((1.4, 1.2, 0.1), (0, 0.05, 0.05), "paint_dark")
    for y in (-0.45, 0.05, 0.55):
        box((1.42, 0.06, 0.03), (0, y, 0.115), "steel_dark")
    drum(MT((-0.32, -0.22, 0.1)), valve=True)
    drum(MT((0.33, -0.2, 0.1), (0, 0, 40)), gauge=True)
    drum(MT((0.0, 0.36, 0.1), (0, 0, 80)), valve=True)
    pipe([(-0.42, -0.17, 1.13), (-0.3, -0.1, 1.28), (-0.12, 0.15, 1.28), (-0.07, 0.27, 1.13)], 0.025, "rubber")


def canister(Mx):
    prof = [(0, 0.0), (0.2, 0.0), (0.2, 0.06), (0.185, 0.07), (0.185, 0.9), (0.15, 1.0), (0.09, 1.04), (0, 1.045)]
    lathe(prof, mat="frost_white", Mx=Mx, segs=48, smooth=40)
    for z in (0.18, 0.78):
        cyl(0.188, 0.06, (0, 0, z), "paint_orange", Mx=Mx, segs=48)
    cyl(0.09, 0.1, (0, 0, 1.08), "steel", Mx=Mx, segs=32)
    cyl(0.1, 0.03, (0, 0, 1.12), "gunmetal", Mx=Mx, segs=32)
    cyl(0.015, 0.1, (0, 0, 1.17), "steel", Mx=Mx, segs=12)
    box((0.2, 0.03, 0.03), (0, 0, 1.22), "paint_dark", Mx=Mx)
    w = Mx @ face_frame((0, 0, 0.5), 20) @ MT((0, 0, 0.18))
    box((0.13, 0.46, 0.03), (0, 0, 0.0), "gunmetal", Mx=w)
    box((0.08, 0.40, 0.01), (0, 0, 0.012), "glow_blue", Mx=w)
    for k in range(7):
        sphere(0.026, (0, -0.17 + k * 0.057, 0.014), "glow_cyan", Mx=w, segs=12)
    box((0.09, 0.41, 0.006), (0, 0, 0.042), "glass_clear", Mx=w)
    for sy in (-1, 1):
        for sx in (-1, 1):
            bolt((sx * 0.05, sy * 0.21, 0.017), (0, 0, 0), 0.008, 0.008, Mx=w)


def b_FusionPellets():
    box((1.3, 0.62, 0.07), (0, 0, 0.035), "paint_dark")
    for x in (-0.42, 0.0, 0.42):
        canister(MT((x, 0.0, 0.07)))
    for sx in (-1, 1):
        box((0.06, 0.06, 0.9), (sx * 0.64, 0.2, 0.52), "paint_yellow")
    box((1.34, 0.06, 0.08), (0, 0.2, 0.92), "paint_yellow")
    for x in (-0.42, 0.0, 0.42):
        torus(0.2, 0.015, (x, 0.0, 0.92), "paint_yellow", segs=40, rsegs=8)
    lab = MT((-0.0, -0.312, 0.035), (90, 0, 0))
    box((0.5, 0.05, 0.002), (0, 0, 0), "label_yellow", Mx=lab)
    text("CRYO  D-He3  -253 C", (0, 0, 0.002), size=0.035, mat="stencil_black", Mx=lab)


# ----------------------------------------------------------------------------------------------
# ship parts

def b_ArmourPlating():
    R, th, ti = 2.6, 0.16, 0.06
    a0, a1 = -116, -64
    Mx = MT((0, R - 0.3, 0.1), (0, 0, 0))
    H = 1.5
    st = mark()
    lathe([(R - th, 0), (R, 0), (R, H), (R - th, H)], mat="paint_hull", segs=24, closed=True, arc=(a0, a1), smooth=20)
    lathe([(R - th - ti, 0.02), (R - th, 0.02), (R - th, H - 0.02), (R - th - ti, H - 0.02)], mat="titanium",
          segs=24, closed=True, arc=(a0 + 0.3, a1 - 0.3), smooth=20)
    rb = R - th - ti
    for z in (0.2, 0.75, 1.3):
        lathe([(rb - 0.2, z - 0.03), (rb, z - 0.03), (rb, z + 0.03), (rb - 0.2, z + 0.03)], mat="steel_dark",
              segs=24, closed=True, arc=(a0 + 1, a1 - 1), smooth=20)
        for k in range(9):
            a = math.radians(a0 + 3 + (a1 - a0 - 6) * k / 8)
            bolt(((rb - 0.1) * math.cos(a), (rb - 0.1) * math.sin(a), z + 0.036), (0, 0, 0), 0.016, 0.012)
    for a in (-100, -80):
        lathe([(rb - 0.2, 0.05), (rb, 0.05), (rb, H - 0.05), (rb - 0.2, H - 0.05)], mat="steel_dark", segs=1,
              closed=True, arc=(a - 0.7, a + 0.7), smooth=20)
    lathe([(R, 1.32), (R + 0.003, 1.32), (R + 0.003, 1.4), (R, 1.4)], mat="paint_red", segs=24, closed=True,
          arc=(a0 + 0.5, a1 - 0.5), smooth=20)
    lathe([(R, 0.05), (R + 0.003, 0.05), (R + 0.003, 0.16), (R, 0.16)], mat="hazard", segs=24, closed=True,
          arc=(a0 + 0.5, a0 + 14), smooth=20)
    lathe([(R, 0.745), (R + 0.002, 0.745), (R + 0.002, 0.757), (R, 0.757)], mat="paint_dark", segs=24, closed=True,
          arc=(a0 + 0.5, a1 - 0.5), smooth=20)
    lathe([(R, 0.02), (R + 0.002, 0.02), (R + 0.002, H - 0.02), (R, H - 0.02)], mat="paint_dark", segs=1,
          closed=True, arc=(-90.15, -89.85), smooth=20)
    for zz in (0.06, H - 0.06):
        for k in range(14):
            a = math.radians(a0 + 1.6 + (a1 - a0 - 3.2) * k / 13)
            bolt(((R + 0.004) * math.cos(a), (R + 0.004) * math.sin(a), zz), (90, 0, math.degrees(a) + 90), 0.02, 0.012)
    for aa in (a0 + 1.6, a1 - 1.6):
        for k in range(1, 9):
            a = math.radians(aa)
            bolt(((R + 0.004) * math.cos(a), (R + 0.004) * math.sin(a), 0.06 + (H - 0.12) * k / 9),
                 (90, 0, math.degrees(a) + 90), 0.02, 0.012)
    a = math.radians(-100)
    text("HULL SECT 07-B", (R * math.cos(a) + 0.0, R * math.sin(a) - 0.004, 0.5), (90, 0, -10), 0.07)
    a = math.radians(-78)
    text("FR 112", (R * math.cos(a), R * math.sin(a) - 0.004, 1.1), (90, 0, 12), 0.08)
    for a in (-104, -76):
        r = math.radians(a)
        box((0.08, 0.06, 0.08), ((R - 0.1) * math.cos(r), (R - 0.1) * math.sin(r), H + 0.04), "steel_dark")
        torus(0.04, 0.012, ((R - 0.1) * math.cos(r), (R - 0.1) * math.sin(r), H + 0.1), "steel", (90, 0, a + 90),
              segs=16, rsegs=8)
    move(st, Mx)
    for x in (-0.7, 0.7):
        box((0.12, 0.7, 0.1), (x, 0.0, 0.05), "paint_dark")
        box((0.08, 0.06, 0.5), (x, 0.32, 0.3), "paint_dark", (-25, 0, 0))
    move(0, MT((0, 0, 0), (0, 0, -8)))


def b_CompositeArmour():
    OPT["backdrop"] = 0.6
    W, H, T = 1.8, 1.3, 0.1
    st = mark()
    for sy in (-1, 1):
        box((W, 0.1, T), (0, sy * (H / 2 - 0.05), 0), "gunmetal")
    for sx in (-1, 1):
        box((0.1, H - 0.2, T), (sx * (W / 2 - 0.05), 0, 0), "gunmetal")
    for x in (-0.3, 0.3):
        box((0.06, H - 0.2, T), (x, 0, 0), "gunmetal")
    box((W - 0.2, 0.06, T), (0, 0, 0), "gunmetal")
    cw, chh = 0.52, 0.52
    for ix, x in enumerate((-0.6, 0.0, 0.6)):
        for iy, y in enumerate((-0.31, 0.31)):
            box((cw, chh, 0.035), (x, y, 0.0), "titanium")
            box((cw - 0.04, chh - 0.04, 0.035), (x, y, 0.035), "carbon_sheet")
            box((cw - 0.09, chh - 0.09, 0.045), (x, y, 0.075), "glass_teal", bev=0.012)
    for sx in (-1, 1):
        for sy in (-1, 1):
            for k in range(5):
                t = k / 4
                bolt((sx * (W / 2 - 0.05), sy * (H / 2 - 0.05) * (1 - t) + 0 * t, T / 2 + 0.006), r=0.018)
            for k in range(1, 7):
                bolt((sx * (W / 2 - 0.05) * (1 - k / 7), sy * (H / 2 - 0.05), T / 2 + 0.006), r=0.018)
    box((0.3, 0.06, 0.004), (-0.6, -(H / 2 - 0.05), T / 2 + 0.002), "hazard_fine")
    text("CMP-ARM  Ti/CF/GL", (0.45, -(H / 2 - 0.05), T / 2 + 0.002), size=0.05)
    move(st, MT((0, 0, 0.64), (62, 0, 0)))
    for x in (-0.65, 0.65):
        box((0.1, 1.0, 0.06), (x, 0.2, 0.03), "paint_dark")
        st = mark()
        box((0.06, 0.06, 0.8), (0, 0, 0), "paint_dark")
        move(st, MT((x, 0.4, 0.38), (35, 0, 0)))
    move(0, MT((0, 0, 0), (0, 0, 8)))


def b_ShieldEmitter():
    cyl(0.6, 0.1, (0, 0, 0.05), "gunmetal", segs=48)
    bolt_ring(MT((0, 0, 0.1)), 0.52, 16, 0.02, 0.014)
    cyl(0.28, 0.8, (0, 0, 0.5), "gunmetal", segs=48)
    for i, z in enumerate((0.22, 0.42, 0.62)):
        lathe([(0.28, z - 0.08), (0.43, z - 0.08), (0.45, z), (0.43, z + 0.08), (0.28, z + 0.08)], mat="copper_wind",
              segs=64, closed=True, bake=False, smooth=60)
    for z in (0.12, 0.32, 0.52, 0.72):
        cyl(0.5, 0.035, (0, 0, z + 0.005), "gunmetal", segs=48)
    cyl(0.36, 0.1, (0, 0, 0.82), "paint_grey", segs=48)
    cyl(0.13, 0.25, (0, 0, 0.97), "steel_dark", segs=24)
    pivot = MT((0, 0, 1.1), (0, 0, 0))
    st = mark()
    f = 0.55
    R = 0.88
    n = 14
    front = [(0.1 + (R - 0.1) * k / n, ((0.1 + (R - 0.1) * k / n) ** 2) / (4 * f)) for k in range(n + 1)]
    back = [(r, z - 0.035) for r, z in reversed(front)]
    lathe(back + front, mat="paint_light", segs=64, closed=True, smooth=40)
    for r in (0.32, 0.56, 0.80):
        z = r * r / (4 * f)
        torus(r, 0.03, (0, 0, z + 0.012), "gunmetal", segs=64, rsegs=10)
        torus(r, 0.014, (0, 0, z + 0.04), "glow_cyan", segs=64, rsegs=8)
    lathe([(0.12, 0.0), (0.06, 0.25), (0.03, 0.42), (0.0, 0.44)], mat="steel_dark", segs=24, smooth=50)
    sphere(0.055, (0, 0, 0.45), "glow_cyan", segs=16)
    for k in range(3):
        a = math.radians(90 + 120 * k)
        pipe([(R * 0.95 * math.cos(a), R * 0.95 * math.sin(a), (R * 0.95) ** 2 / (4 * f)), (0, 0, 0.40)], 0.012,
             "steel_dark", vector=True)
    cyl(0.16, 0.08, (0, 0, -0.06), "gunmetal", segs=24)
    move(st, pivot @ MT((0, 0, 0), (0, 0, 35)) @ MT((0, 0, 0), (32, 0, 0)))
    st = mark()
    for sx in (-1, 1):
        box((0.05, 0.16, 0.3), (sx * 0.19, 0, 1.0), "paint_grey")
    cyl(0.05, 0.46, (0, 0, 1.1), "steel", (0, 90, 0), segs=16)
    move(st, MT((0, 0, 0), (0, 0, 35)))
    text("EMITTER  MK IV", (0, -0.361, 0.82), (90, 0, 0), 0.05)
    move(mark() - 1, MT((0, 0, 0), (0, 0, 20)))


def b_CapacitorBank():
    Wd, Dp = 1.7, 0.85
    box((Wd, Dp, 0.06), (0, 0, 0.03), "paint_dark")
    for sx in (-1, 1):
        for sy in (-1, 1):
            box((0.06, 0.06, 0.9), (sx * (Wd / 2 - 0.03), sy * (Dp / 2 - 0.03), 0.51), "paint_dark")
        box((0.06, Dp, 0.06), (sx * (Wd / 2 - 0.03), 0, 0.93), "paint_dark")
    for sy in (-1, 1):
        box((Wd, 0.06, 0.06), (0, sy * (Dp / 2 - 0.03), 0.33), "paint_dark")
    for row, y in enumerate((-0.19, 0.19)):
        for c, x in enumerate((-0.6, -0.2, 0.2, 0.6)):
            cyl(0.16, 0.72, (x, y, 0.06 + 0.36), "cap_blue", segs=40)
            cyl(0.152, 0.03, (x, y, 0.795), "alu_dark", segs=40)
            torus(0.152, 0.012, (x, y, 0.78), "alu_dark", segs=40, rsegs=8)
            box((0.2, 0.006, 0.004), (x, y, 0.81), "paint_dark", (0, 0, 45))
            box((0.2, 0.006, 0.004), (x, y, 0.81), "paint_dark", (0, 0, -45))
            st = mark()
            box((0.06, 0.58, 0.004), (0, 0, 0), "label_white")
            for k in range(5):
                box((0.03, 0.008, 0.004), (0, -0.2 + k * 0.1, 0.003), "stencil_black")
            move(st, face_frame((x, y, 0.42), 25) @ MT((0, 0, 0.161)))
            for ty in (-0.07, 0.07):
                cyl(0.028, 0.07, (x, y + ty, 0.845), "steel", segs=16)
                bolt((x, y + ty, 0.9), (0, 0, 0), 0.03, 0.02)
    for y in (-0.19, 0.19):
        for ty in (-0.07, 0.07):
            box((1.42, 0.07, 0.018), (0.0, y + ty, 0.889), "copper")
    box((0.08, 0.5, 0.02), (0.75, 0, 0.87), "copper")
    box((0.08, 0.5, 0.02), (-0.75, 0, 0.87), "copper")
    pipe([(-0.78, -0.12, 0.87), (-1.0, -0.2, 0.75), (-1.05, -0.45, 0.2), (-0.9, -0.75, 0.03), (-0.5, -0.85, 0.03)],
         0.04, "cable_red")
    pipe([(-0.78, 0.12, 0.87), (-1.05, 0.05, 0.7), (-1.15, -0.2, 0.2), (-1.0, -0.55, 0.03), (-0.75, -0.75, 0.03)],
         0.04, "cable_black")
    hv_sign(MT((0.0, -Dp / 2 - 0.001, 0.33), (90, 0, 0)), 0.22)
    text("DANGER  40 kV", (0.5, -Dp / 2 - 0.002, 0.03), (90, 0, 0), 0.04)


def b_FusionThruster():
    st = mark()
    lathe([(0.0, -0.98), (0.22, -0.96), (0.3, -0.88), (0.3, -0.8)], mat="steel_dark", segs=48, smooth=50)
    lathe([(0.3, -0.8), (0.3, -0.38), (0.17, -0.2), (0.15, -0.14)], mat="nozzle", segs=48, smooth=50, bake=False)
    lathe(bell_profile(0.15, 0.62, -0.14, 0.95, 0.02, 16, 0.55), mat="nozzle", segs=64, closed=True, bake=False,
          smooth=50)
    for z, r in ((0.25, None), (0.62, None), (0.95, None)):
        t = (z + 0.14) / 1.09
        rr = 0.15 + 0.47 * t ** 0.55 + 0.02
        torus(rr + 0.008, 0.016, (0, 0, z - (0.008 if z > 0.9 else 0)), "steel_dark", segs=64, rsegs=8)
    for k in range(3):
        z = -0.34 + k * 0.085
        lathe([(0.2, z - 0.028), (0.36, z - 0.028), (0.37, z), (0.36, z + 0.028), (0.2, z + 0.028)],
              mat="copper_wind", segs=64, closed=True, bake=False, smooth=60)
        ring(0.2, 0.39, 0.016, (0, 0, z + 0.042), "gunmetal", segs=48)
    ring(0.2, 0.39, 0.016, (0, 0, -0.34 - 0.042), "gunmetal", segs=48)
    torus(0.44, 0.03, (0, 0, -0.43), "steel", segs=48, rsegs=10)
    cyl(0.14, 0.02, (0, 0, -0.17), "glow_blue", segs=24)
    box((0.75, 0.75, 0.06), (0, 0, -1.03), "gunmetal")
    for a in (45, 135, 225, 315):
        r = math.radians(a)
        pipe([(0.33 * math.cos(r), 0.33 * math.sin(r), -1.0), (0.28 * math.cos(r), 0.28 * math.sin(r), -0.8)],
             0.025, "steel_dark", vector=True)
    for k, a in enumerate((20, 140, 260)):
        r = math.radians(a)
        c, s = math.cos(r), math.sin(r)
        pipe([(0.25 * c, 0.25 * s, -1.0), (0.38 * c, 0.38 * s, -0.9), (0.4 * c, 0.4 * s, -0.65),
              (0.44 * c, 0.44 * s, -0.45)], 0.028, "cable_yellow" if k == 0 else "steel")
    move(st, MT((0, 0, 0.75), (0, 90, -20)))
    for x in (-0.7, 0.05):
        box((0.1, 0.8, 0.06), (x, 0.0, 0.03), "paint_dark")
        prism([(-0.3, 0), (0.3, 0), (0.12, 0.42), (-0.12, 0.42)], 0.06, (x, 0, 0.06), "paint_yellow", (90, 0, 90))
    move(mark() - 4, MT((0, 0, 0), (0, 0, -20)))


def jet(Mx):
    torus(0.13, 0.02, (0, 0, 0.0), "paint_orange", (90, 0, 0), Mx=Mx, segs=32, rsegs=8)
    cyl(0.075, 0.1, (0, 0, 0.0), "steel_dark", Mx=Mx, segs=24)
    lathe(bell_profile(0.045, 0.14, 0.05, 0.36, 0.012, 10, 0.6), mat="nozzle_small", Mx=Mx, segs=48, closed=True,
          bake=False, smooth=50)
    torus(0.152, 0.01, (0, 0, 0.355), "steel_dark", Mx=Mx, segs=40, rsegs=6)


def b_VectoringJets():
    box((0.95, 0.95, 0.16), (0, 0, 0.08), "paint_grey", bev=0.02)
    box((0.97, 0.97, 0.04), (0, 0, 0.04), "hazard")
    cyl(0.2, 0.16, (0, 0, 0.24), "gunmetal", segs=32)
    cyl(0.12, 0.05, (0, 0, 0.345), "steel", segs=24)
    for i, a in enumerate((45, 135, 225, 315)):
        r = math.radians(a)
        c, s = math.cos(r), math.sin(r)
        base = Vector((0.3 * c, 0.3 * s, 0.32))
        tilt = 38 + (i % 2) * 8
        Mx = Matrix.Translation(base) @ Matrix.Rotation(r, 4, "Z") @ Matrix.Rotation(math.radians(tilt), 4, "Y")
        box((0.08, 0.14, 0.16), (0.3 * c, 0.3 * s, 0.24), "paint_grey", (0, 0, a))
        jet(Mx)
        for k in (-1, 1):
            p0 = Vector((0.22 * c - k * 0.08 * s, 0.22 * s + k * 0.08 * c, 0.16))
            p1 = Mx @ Vector((k * 0.07, 0, 0.18))
            pipe([p0, p1], 0.012, "chrome", vector=True)
        pipe([(0.1 * c, 0.1 * s, 0.3), (0.2 * c, 0.2 * s, 0.38), Mx @ Vector((0, 0, -0.04))], 0.014, "steel")
    text("RCS-4  QUAD", (0.0, -0.476, 0.1), (90, 0, 0), 0.05)


def b_CargoPod():
    L, Wd, H = 2.0, 1.05, 1.0
    z0 = 0.06
    box((L - 0.1, Wd, H), (0, 0, z0 + H / 2), "aluminium", bev=0.05)
    for sx in (-1, 1):
        x = sx * (L / 2 - 0.05)
        box((0.12, Wd + 0.06, 0.1), (x, 0, z0 + 0.05), "paint_orange")
        box((0.12, Wd + 0.06, 0.1), (x, 0, z0 + H - 0.05), "paint_orange")
        for sy in (-1, 1):
            box((0.12, 0.1, H), (x, sy * (Wd / 2 - 0.02), z0 + H / 2), "paint_orange")
        for sy in (-1, 1):
            for zz in (z0 + 0.06, z0 + H - 0.06):
                box((0.14, 0.14, 0.14), (x, sy * (Wd / 2 - 0.02), zz), "paint_yellow")
                box((0.06, 0.006, 0.04), (x, sy * (Wd / 2 + 0.051), zz), "stencil_black")
                box((0.06, 0.04, 0.006), (x, sy * (Wd / 2 - 0.02), zz + (0.071 if zz > 0.5 else -0.071)),
                    "stencil_black")
    nrib = 11
    for i in range(nrib):
        x = -L / 2 + 0.22 + (L - 0.44) * i / (nrib - 1)
        if abs(x) < 0.42:
            continue
        for sy in (-1, 1):
            box((0.04, 0.03, H - 0.16), (x, sy * (Wd / 2 + 0.012), z0 + H / 2), "aluminium")
        box((0.04, Wd - 0.12, 0.03), (x, 0, z0 + H + 0.012), "aluminium")
    hx, hz = 0.0, z0 + H / 2 - 0.02
    box((0.72, 0.02, 0.76), (hx, -Wd / 2 - 0.006, hz), "alu_dark")
    for (sx, sz, w, h) in ((0, 0.4, 0.84, 0.06), (0, -0.4, 0.84, 0.06), (-0.39, 0, 0.06, 0.74), (0.39, 0, 0.06, 0.74)):
        box((w, 0.02, h), (hx + sx, -Wd / 2 - 0.012, hz + sz), "hazard_fine")
    for zz in (0.22, -0.22):
        cyl(0.025, 0.1, (hx - 0.34, -Wd / 2 - 0.03, hz + zz), "steel_dark", segs=16)
    pipe([(0.28, -Wd / 2 - 0.016, hz - 0.08), (0.28, -Wd / 2 - 0.07, hz - 0.05), (0.28, -Wd / 2 - 0.07, hz + 0.05),
          (0.28, -Wd / 2 - 0.016, hz + 0.08)], 0.012, "steel", vector=True)
    text("CARGO  20 T", (0.0, -Wd / 2 - 0.018, hz + 0.22), (90, 0, 0), 0.09, "stencil_black")
    text("POD-1138", (0.0, -Wd / 2 - 0.018, hz + 0.1), (90, 0, 0), 0.06, "stencil_black")
    text("1138", (L / 2 + 0.012, 0, z0 + H / 2), (90, 0, 90), 0.16, "stencil_black")


def rail_barrel(y, x0, x1):
    for sz in (-1, 1):
        box((x1 - x0, 0.07, 0.035), ((x0 + x1) / 2, y, sz * 0.05), "steel_dark")
    for sy in (-1, 1):
        box((x1 - x0 - 0.02, 0.02, 0.15), ((x0 + x1) / 2, y + sy * 0.045, 0), "plastic_black")
    box((x1 - x0 - 0.06, 0.04, 0.012), ((x0 + x1) / 2 + 0.01, y, 0), "glow_blue")
    j0, j1 = x0 + 0.05, x1 - 0.55
    box((j1 - j0, 0.17, 0.17), ((j0 + j1) / 2, y, 0), "gunmetal")
    nf = int((j1 - j0) / 0.06)
    for k in range(nf):
        x = j0 + 0.03 + k * 0.06
        box((0.014, 0.26, 0.26), (x, y, 0), "fin")
    box((0.08, 0.2, 0.2), (x1 - 0.08, y, 0), "gunmetal")
    for xx in (x1 - 0.35, x1 - 0.2):
        box((0.03, 0.12, 0.17), (xx, y, 0), "steel")


def b_RailBarrels():
    st = mark()
    x0, x1 = -0.75, 1.75
    for y in (-0.17, 0.17):
        rail_barrel(y, x0, x1)
        pipe([(-0.75, y, 0.16), (-0.5, y, 0.15), (0.7, y, 0.15), (0.9, y, 0.14)], 0.018, "cable_blue")
    box((0.6, 0.72, 0.46), (-1.05, 0, 0), "gunmetal", bev=0.02)
    box((0.62, 0.74, 0.06), (-1.05, 0, 0.12), "hazard")
    for sy in (-1, 1):
        for k in range(3):
            cyl(0.045, 0.06, (-1.2 + k * 0.15, sy * 0.38, -0.05), "steel", (90, 0, 0), segs=16)
    pipe([(-1.35, -0.2, 0.0), (-1.55, -0.3, -0.1), (-1.6, -0.2, -0.35)], 0.035, "cable_black")
    for x in (0.0, 0.9):
        box((0.08, 0.66, 0.3), (x, 0, 0), "paint_grey")
    text("RG-2  HIGH VOLTAGE", (-1.05, -0.361, -0.12), (90, 0, 0), 0.05)
    move(st, MT((0, 0, 0.42), (0, -4, -20)))
    for x in (-0.5, 0.9):
        st = mark()
        box((0.12, 0.8, 0.08), (0, 0, 0.04), "paint_dark")
        box((0.1, 0.1, 0.25), (0, 0, 0.18), "paint_dark")
        move(st, MT((x, 0, 0), (0, 0, -20)))


def b_FireControl():
    W, D, H = 1.1, 0.78, 0.68
    z0 = 0.05
    for sx in (-1, 1):
        for sy in (-1, 1):
            cyl(0.05, 0.05, (sx * 0.45, sy * 0.3, 0.025), "rubber", segs=16)
    box((W, D, H), (0, 0, z0 + H / 2), "paint_olive", bev=0.03)
    box((W - 0.16, 0.025, H - 0.16), (0.0, -D / 2 - 0.012, z0 + H / 2), "paint_olive", bev=0.008)
    for sx in (-1, 1):
        for sz in (-1, 1):
            bolt((sx * (W / 2 - 0.12), -D / 2 - 0.03, z0 + H / 2 + sz * (H / 2 - 0.12)), (90, 0, 0), 0.02, 0.012)
    box((0.025, D - 0.16, H - 0.16), (W / 2 + 0.012, 0, z0 + H / 2), "paint_olive", bev=0.008)
    eye = MT((0.22, -D / 2 - 0.025, z0 + H * 0.62), (90, 0, 0))
    lathe([(0.0, 0.0), (0.17, 0.0), (0.17, 0.05), (0.13, 0.07), (0.12, 0.05), (0.0, 0.05)], mat="gunmetal", Mx=eye,
          segs=48, smooth=40)
    cyl(0.11, 0.02, (0, 0, 0.05), "glow_red", Mx=eye, segs=32)
    cyl(0.05, 0.03, (0, 0, 0.06), "stencil_black", Mx=eye, segs=24)
    lathe([(0.115, 0.0), (0.09, 0.06), (0.0, 0.085)], (0, 0, 0.045), "glass_dark", Mx=eye, segs=32, smooth=70)
    bolt_ring(eye, 0.15, 8, 0.012, 0.008, 0.05, phase=0.5)
    for k, m in enumerate(("glow_green", "glow_amber", "glow_green")):
        sphere(0.014, (-0.08 + k * 0.05, -D / 2 - 0.03, z0 + 0.25), m, segs=10)
    for k in range(6):
        box((0.3, 0.01, 0.018), (-0.25, -D / 2 - 0.03, z0 + 0.42 + k * 0.035), "paint_dark")
    text("FCS-7  MK II", (-0.25, -D / 2 - 0.026, z0 + 0.13), (90, 0, 0), 0.06)
    box((0.2, 0.004, 0.05), (0.22, -D / 2 - 0.026, z0 + 0.14), "hazard_fine")
    cols = ("cable_black", "cable_yellow", "cable_blue")
    for k, z in enumerate((0.2, 0.36, 0.52)):
        y = -0.18 + k * 0.17
        pm = MT((W / 2 + 0.025, y, z0 + z), (0, 90, 0))
        cyl(0.055, 0.03, (0, 0, 0.015), "steel_dark", Mx=pm, segs=24)
        cyl(0.04, 0.08, (0, 0, 0.06), "steel", Mx=pm, segs=24)
        cyl(0.03, 0.06, (0, 0, 0.12), "rubber", Mx=pm, segs=16)
        pipe([(W / 2 + 0.17, y, z0 + z), (W / 2 + 0.32, y - 0.05, z0 + z - 0.05), (W / 2 + 0.38, y - 0.12, 0.1),
              (W / 2 + 0.3, y - 0.3, 0.025), (W / 2 + 0.05, y - 0.45 - k * 0.04, 0.025)], 0.022, cols[k])
    for x in (-0.3, 0.3):
        pipe([(x, -0.15, z0 + H), (x, -0.15, z0 + H + 0.08), (x, 0.15, z0 + H + 0.08), (x, 0.15, z0 + H)], 0.018,
             "steel_dark", vector=True)
    plate = MT((W / 2 + 0.026, 0.2, z0 + H - 0.18), (90, 0, 90))
    box((0.16, 0.08, 0.004), (0, 0, 0), "alu_dark", Mx=plate)
    text("S/N 0442", (0, 0, 0.003), size=0.025, mat="stencil_black", Mx=plate)


def shell(Mx, L=0.42, r=0.055):
    """Shell along local +Z from z = 0: brass case, driving band, dark projectile."""
    cyl(r * 1.08, 0.02, (0, 0, 0.01), "brass", Mx=Mx, segs=20)
    cyl(r, L * 0.62, (0, 0, L * 0.31), "brass", Mx=Mx, segs=20)
    lathe([(r * 0.92, L * 0.62), (r * 0.92, L * 0.72), (r * 0.8, L * 0.86), (r * 0.35, L * 0.97), (0, L)],
          mat="steel_dark", Mx=Mx, segs=20, smooth=60)
    cyl(r * 0.95, L * 0.03, (0, 0, L * 0.66), "paint_red", Mx=Mx, segs=20)


def b_AutoLoader():
    cx, cy0 = -0.25, 0.15
    box((1.05, 1.05, 0.12), (cx, cy0, 0.06), "gunmetal", bev=0.02)
    cyl(0.6, 0.05, (cx, cy0, 0.145), "steel_dark", segs=64)
    cyl(0.6, 0.05, (cx, cy0, 0.74), "steel_dark", segs=64)
    cyl(0.1, 0.62, (cx, cy0, 0.45), "steel", segs=24)
    for k in range(14):
        a = 2 * math.pi * k / 14
        shell(MT((cx + 0.47 * math.cos(a), cy0 + 0.47 * math.sin(a), 0.17)), 0.55, 0.06)
    teeth = 28
    pts = []
    for i in range(teeth * 2):
        a = math.pi * i / teeth
        rr = 0.36 if i % 2 == 0 else 0.32
        pts += [(rr * math.cos(a - 0.04), rr * math.sin(a - 0.04)), (rr * math.cos(a + 0.04), rr * math.sin(a + 0.04))]
    prism(pts, 0.05, (cx, cy0, 0.79), "paint_yellow")
    cyl(0.12, 0.08, (cx, cy0, 0.84), "gunmetal", segs=24)
    bolt_ring(MT((cx, cy0, 0.765)), 0.54, 16, 0.018, 0.012)
    fh = MT((0.32, -0.15, 0.45), (0, 0, -30))
    box((0.3, 0.32, 0.36), (0.0, 0, 0), "gunmetal", Mx=fh, bev=0.015)
    box((0.31, 0.33, 0.05), (0.0, 0, 0.12), "hazard_fine", Mx=fh)
    cyl(0.12, 0.34, (0, 0, -0.05), "steel_dark", (90, 0, 0), Mx=fh, segs=24)
    text("ALS-12", (0.0, -0.162, -0.08), (90, 0, 0), 0.05, Mx=fh)
    path = [Vector((0.5, -0.35, 0.36)), Vector((0.75, -0.55, 0.22)), Vector((0.9, -0.75, 0.06)),
            Vector((0.85, -1.05, 0.045)), Vector((0.55, -1.2, 0.045)), Vector((0.2, -1.15, 0.045))]
    pts = []
    for i in range(len(path) - 1):
        for k in range(12):
            pts.append(path[i].lerp(path[i + 1], k / 12))
    pts.append(path[-1])
    dist = [0.0]
    for i in range(1, len(pts)):
        dist.append(dist[-1] + (pts[i] - pts[i - 1]).length)
    step, s = 0.115, 0.0
    j = 0
    while s < dist[-1]:
        while dist[j + 1] < s:
            j += 1
        t = (s - dist[j]) / (dist[j + 1] - dist[j])
        p = pts[j].lerp(pts[j + 1], t)
        tan = (pts[j + 1] - pts[j]).normalized()
        side = tan.cross(Vector((0, 0, 1))).normalized()
        up = side.cross(tan).normalized()
        Mx = Matrix((tan, up, -side)).transposed().to_4x4()
        Mx.translation = p
        shell(Mx @ MT((0, 0, -0.17)), 0.34, 0.042)
        box((0.05, 0.03, 0.2), (0, -0.04, 0.0), "steel_dark", Mx=Mx)
        s += step


def b_BeamLens():
    st = mark()
    cyl(0.3, 1.0, (0, 0, -0.32), "gunmetal", segs=48)
    for k in range(10):
        z = -0.74 + k * 0.07
        ring(0.3, 0.54, 0.022, (0, 0, z), "fin", segs=64)
    cyl(0.4, 0.22, (0, 0, 0.29), "paint_white", segs=64)
    ring(0.3, 0.4, 0.06, (0, 0, 0.43), "gunmetal", segs=64)
    bolt_ring(MT((0, 0, 0.46)), 0.35, 12, 0.014, 0.01)
    ring(0.26, 0.31, 0.06, (0, 0, 0.49), "steel", segs=64)
    ring(0.21, 0.26, 0.06, (0, 0, 0.53), "gunmetal", segs=64)
    cyl(0.3, 0.01, (0, 0, 0.405), "plastic_black", segs=48)
    cyl(0.11, 0.01, (0, 0, 0.408), "glow_violet", segs=32)
    torus(0.2, 0.006, (0, 0, 0.412), "glow_violet", segs=48, rsegs=6)
    lathe([(0.0, 0.0), (0.3, 0.0), (0.3, 0.012), (0.0, 0.03)], (0, 0, 0.415), "lens", segs=64, smooth=70)
    lathe([(0.0, 0.0), (0.26, 0.0), (0.26, 0.012), (0.0, 0.035)], (0, 0, 0.47), "lens", segs=64, smooth=70)
    lathe([(0.0, 0.0), (0.21, 0.0), (0.21, 0.012), (0.12, 0.04), (0.0, 0.05)], (0, 0, 0.53), "lens", segs=64,
          smooth=70)
    torus(0.215, 0.008, (0, 0, 0.562), "glow_violet", segs=64, rsegs=6)
    torus(0.42, 0.025, (0, 0, 0.2), "steel", segs=64, rsegs=10)
    for a in (-50, 50, 180):
        r = math.radians(a + 90)
        c, s = math.cos(r), math.sin(r)
        pipe([(0.42 * c, 0.42 * s, 0.2), (0.58 * c, 0.58 * s, 0.05), (0.6 * c, 0.6 * s, -0.75),
              (0.3 * c, 0.3 * s, -0.95)], 0.025, "cable_blue")
    cyl(0.2, 0.06, (0, 0, -0.85), "steel_dark", segs=32)
    move(st, MT((0, 0, 0.7), (76, 0, 28)))
    st = mark()
    box((0.9, 0.55, 0.06), (0, 0.15, 0.03), "paint_dark")
    for y in (-0.15, 0.55):
        box((0.5, 0.08, 0.16), (0.0, y, 0.12), "paint_grey")
    text("BFL-3  DIAMOND STACK", (0, -0.126, 0.03), (90, 0, 0), 0.035)
    move(st, MT((0, 0, 0), (0, 0, 28)))


def b_ReactorCore():
    R = 0.55
    prof = [(0.0, 0.18), (0.3, 0.2), (0.48, 0.26), (0.55, 0.34), (0.55, 1.0), (0.48, 1.12), (0.3, 1.2), (0.0, 1.22)]
    lathe(prof, mat="paint_hull", segs=64, smooth=40)
    ring(0.4, 0.46, 0.2, (0, 0, 0.1), "hazard", segs=64)
    ring(0.54, 0.66, 0.09, (0, 0, 0.98), "gunmetal", segs=64)
    bolt_ring(MT((0, 0, 1.025)), 0.61, 24, 0.018, 0.016)
    for k in range(5):
        a = 2 * math.pi * k / 5
        x, y = (0.0, 0.0) if k == 0 else (0.22 * math.cos(a), 0.22 * math.sin(a))
        cyl(0.05, 0.26, (x, y, 1.3), "steel", segs=16)
        cyl(0.06, 0.03, (x, y, 1.43), "gunmetal", segs=16)
    w = face_frame((0, 0, 0.62), 12) @ MT((0, 0, R - 0.02))
    lathe([(0.15, 0.0), (0.24, 0.0), (0.24, 0.06), (0.2, 0.09), (0.15, 0.09)], mat="gunmetal", Mx=w, segs=48,
          closed=True, smooth=40)
    cyl(0.155, 0.02, (0, 0, 0.03), "glow_orange", Mx=w, segs=48)
    for k in range(-2, 3):
        box((0.022, 0.29, 0.01), (k * 0.055, 0, 0.045), "stencil_black", Mx=w)
    cyl(0.155, 0.02, (0, 0, 0.065), "glass_clear", Mx=w, segs=48)
    bolt_ring(w, 0.2, 10, 0.013, 0.01, 0.09)
    trefoil(face_frame((0, 0, 0.62), -42) @ MT((0, 0.1, R + 0.004)), 0.24)
    text("CORE 2", (0, 0, 0.0), (0, 0, 0), 0.07, Mx=face_frame((0, 0, 0.88), 12) @ MT((0, 0, R + 0.002)))
    for a in (160, 60, -10):
        r = math.radians(a)
        c, s = math.cos(r), math.sin(r)
        far = (1.0 * c, 1.0 * s)
        cyl(0.075, 0.12, (0.42 * c, 0.42 * s, 1.12), "steel", (0, 0, 0), segs=24)
        pipe([(0.42 * c, 0.42 * s, 1.17), (0.48 * c, 0.48 * s, 1.32), (0.8 * c, 0.8 * s, 1.3), (far[0], far[1], 0.9),
              (far[0] * 1.02, far[1] * 1.02, 0.08)], 0.06, "paint_cyan")
        torus(0.065, 0.02, (far[0] * 1.02, far[1] * 1.02, 0.12), "gunmetal", segs=24, rsegs=8)
        cyl(0.1, 0.06, (far[0] * 1.02, far[1] * 1.02, 0.03), "gunmetal", segs=24)


def b_MiningHead():
    cyl(0.55, 0.1, (0, 0, 0.05), "paint_dark", segs=64)
    ring(0.545, 0.56, 0.05, (0, 0, 0.06), "hazard", segs=64)
    bolt_ring(MT((0, 0, 0.1)), 0.49, 16, 0.02, 0.012)
    st = mark()
    cyl(0.44, 0.08, (0, 0, 0.14), "paint_yellow", segs=64)
    for sx in (-1, 1):
        box((0.1, 0.26, 0.62), (sx * 0.44, 0, 0.48), "paint_yellow", bev=0.01)
        cyl(0.09, 0.08, (sx * 0.5, 0, 0.72), "gunmetal", (0, 90, 0), segs=24)
    head = mark()
    lathe([(0.0, -0.66), (0.2, -0.66), (0.26, -0.6), (0.3, -0.55), (0.3, 0.12), (0.26, 0.2), (0.13, 0.5),
           (0.11, 0.56), (0.0, 0.56)], mat="paint_yellow", segs=64, smooth=40)
    for k in range(18):
        a = 360 * k / 18
        box((0.16, 0.016, 0.55), (0.37, 0, -0.24), "fin", Mx=MT((0, 0, 0), (0, 0, a)))
    ring(0.29, 0.33, 0.04, (0, 0, 0.06), "gunmetal", segs=64)
    ring(0.29, 0.33, 0.04, (0, 0, -0.56), "gunmetal", segs=64)
    torus(0.12, 0.025, (0, 0, 0.56), "gunmetal", segs=48, rsegs=8)
    cyl(0.1, 0.01, (0, 0, 0.555), "glow_green", segs=32)
    lathe([(0.0, 0.0), (0.1, 0.0), (0.0, 0.05)], (0, 0, 0.56), "diamond", segs=8, smooth=None)
    ring(0.27, 0.272, 0.08, (0, 0, -0.1), "hazard_fine", segs=64)
    cyl(0.06, 0.3, (0, 0, -0.35), "gunmetal", (0, 90, 0), segs=16)
    pipe([(0.0, 0.12, -0.66), (0.0, 0.2, -0.85), (0.0, 0.25, -0.95)], 0.04, "cable_black")
    move(head, MT((0, 0, 0.72), (62, 0, 0)))
    text("ML-9", (0.491, 0, 0.36), (90, 0, 90), 0.07, "stencil_black")
    move(st, MT((0, 0, 0), (0, 0, 10)))


# ---- the shipyard's sections (structural parts: built for a destroyer or a cruiser, not fitted)

def b_HullFrame():
    """A hull frame section: two deep ring frames joined by stringers, a keel beam under them."""
    L, R = 2.0, 0.85
    for x in (-L / 2 + 0.08, L / 2 - 0.08):
        torus(R, 0.07, (x, 0, R + 0.1), "paint_grey", rot=(0, 90, 0), segs=40, rsegs=10)
        for k in range(8):
            a = math.radians(k * 45 + 22.5)
            box((0.06, 0.05, R * 0.9), (x, math.cos(a) * R * 0.45, R + 0.1 + math.sin(a) * R * 0.45), "steel_dark",
                rot=(math.degrees(a) - 90, 0, 0))
    for k in range(10):
        a = math.radians(k * 36)
        box((L, 0.08, 0.08), (0, math.cos(a) * R, R + 0.1 + math.sin(a) * R), "steel_plate", rot=(k * 36, 0, 0))
    box((L + 0.1, 0.22, 0.12), (0, 0, 0.11), "gunmetal", bev=0.01)
    box((0.4, 0.004, 0.12), (0, -R - 0.045, R + 0.1), "hazard_fine")
    text("FRAME 41", (0, -0.12, 0.06), (90, 0, 0), 0.06, "stencil_white")


def b_TurretMount():
    """A turret mount: the training ring and its bearing race on a deck plate, the drive housing."""
    box((1.8, 1.8, 0.12), (0, 0, 0.06), "paint_hull", bev=0.02)
    cyl(0.78, 0.18, (0, 0, 0.21), "gunmetal", segs=48)
    torus(0.7, 0.05, (0, 0, 0.32), "steel", segs=48, rsegs=10)
    cyl(0.6, 0.12, (0, 0, 0.36), "steel_dark", segs=48)
    for k in range(24):
        a = math.radians(k * 15)
        bolt((math.cos(a) * 0.72, math.sin(a) * 0.72, 0.3), (0, 0, 0), 0.02, 0.014)
    box((0.42, 0.3, 0.3), (0.55, -0.62, 0.27), "paint_yellow", bev=0.02)
    cyl(0.1, 0.36, (0.55, -0.42, 0.27), "steel", rot=(90, 0, 0), segs=20)
    box((0.3, 0.004, 0.08), (0.55, -0.772, 0.3), "stencil_black")
    text("TRAIN", (-0.4, -0.902, 0.06), (90, 0, 0), 0.06, "stencil_white")


def b_MissileCells():
    """A block of four vertical launch cells: square tubes in a frame, their hatches shut."""
    W = 1.5
    box((W, W, 1.6), (0, 0, 0.8), "paint_hull", bev=0.02)
    for ix in (-1, 1):
        for iy in (-1, 1):
            x, y = ix * W / 4, iy * W / 4
            box((W / 2 - 0.1, W / 2 - 0.1, 0.06), (x, y, 1.63), "paint_grey", bev=0.01)
            box((W / 2 - 0.3, 0.04, 0.02), (x, y, 1.67), "hazard_fine")
            cyl(0.05, 0.05, (x + 0.22, y + 0.22, 1.67), "steel_dark", segs=16)
    for k in range(5):
        box((W + 0.02, 0.04, 0.04), (0, -W / 2 - 0.01, 0.2 + k * 0.32), "steel_dark")
    box((0.5, 0.004, 0.14), (0, -W / 2 - 0.004, 0.4), "paint_red")
    text("VLS 4", (0, -W / 2 - 0.006, 1.1), (90, 0, 0), 0.12, "stencil_white")


def b_BridgeModule():
    """A bridge module: a tapered block with a band of armoured windows and a sensor mast."""
    box((2.0, 1.2, 0.9), (0, 0, 0.45), "paint_light", taper=(0.82, 0.78), bev=0.03)
    box((1.55, 0.02, 0.18), (0, -0.53, 0.72), "glass_dark", rot=(-14, 0, 0))
    for k in range(6):
        box((0.03, 0.03, 0.2), (-0.7 + k * 0.28, -0.54, 0.72), "paint_dark", rot=(-14, 0, 0))
    cyl(0.05, 0.6, (0.4, 0.1, 1.2), "steel", segs=16)
    box((0.5, 0.06, 0.06), (0.4, 0.1, 1.4), "steel_dark")
    sphere(0.12, (-0.45, 0.1, 1.0), "paint_white")
    box((1.0, 0.004, 0.08), (0, -0.6, 0.18), "hazard_fine")
    text("BRIDGE", (0, -0.6, 0.38), (90, 0, 0), 0.1, "stencil_black")


def b_CrewQuarters():
    """Crew quarters: a long habitation block with rows of portholes and an airlock."""
    L, W, H = 2.0, 0.9, 0.8
    box((L, W, H), (0, 0, H / 2 + 0.04), "paint_white", bev=0.04)
    for row in (0.3, 0.6):
        for k in range(7):
            cyl(0.05, 0.02, (-0.75 + k * 0.25, -W / 2 - 0.005, row), "glow_cyan", rot=(90, 0, 0), segs=16)
            torus(0.055, 0.012, (-0.75 + k * 0.25, -W / 2 - 0.012, row), "steel", rot=(90, 0, 0), segs=20, rsegs=6)
    box((0.3, 0.06, 0.55), (L / 2 - 0.1, -W / 2 - 0.02, 0.36), "paint_orange", bev=0.01)
    box((0.02, 0.4, 0.04), (0, 0, H + 0.06), "steel_dark")
    box((L + 0.02, 0.06, 0.06), (0, -W / 2, 0.06), "paint_dark")
    text("DECK 3  BERTHS 40", (-0.2, -W / 2 - 0.006, 0.76), (90, 0, 0), 0.06, "stencil_black")


BUILD = {k[2:]: v for k, v in globals().items() if k.startswith("b_")}

# ----------------------------------------------------------------------------------------------
# camera, lights, world


def setup_world():
    w = bpy.data.worlds.new("World")
    scene.world = w
    nt = w.node_tree
    nt.nodes.clear()
    out = N(nt, "ShaderNodeOutputWorld")
    z = sep(nt, O(N(nt, "ShaderNodeTexCoord"), "Generated"), "Z")
    col = ramp(nt, mapr(nt, z, -1, 1), [(0.0, (0.02, 0.022, 0.026)), (0.5, (0.09, 0.095, 0.11)),
                                        (1.0, (0.22, 0.235, 0.27))])
    bg = N(nt, "ShaderNodeBackground", {"Color": col, "Strength": 1.0})
    nt.links.new(O(bg, "Background"), _sock(out.inputs, "Surface"))


setup_world()
cam_data = bpy.data.cameras.new("Cam")
cam_data.lens = CAM_LENS
cam_data.sensor_width = 36
cam_data.clip_start = 0.01
cam_data.clip_end = 500
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

# (name, azimuth, elevation, power per R^2, size per R, colour)
LIGHTS = [("Key", -40, 48, 230, 1.8, (1.0, 0.96, 0.90)),
          ("Fill", 95, 12, 60, 3.5, (0.85, 0.92, 1.0)),
          ("Rim", 190, 30, 260, 1.5, (0.95, 0.97, 1.0)),
          ("Rim2", 140, 50, 110, 1.4, (1.0, 0.95, 0.9))]
LOBJ = {}
for name, az, el, pw, sz, col in LIGHTS:
    ld = bpy.data.lights.new(name, "AREA")
    ld.shape = "DISK"
    ld.color = col
    lo = bpy.data.objects.new(name, ld)
    scene.collection.objects.link(lo)
    if name != "Key":   # only the key (and the world) cast shadows onto the shadow catcher
        lo.light_linking.receiver_collection = ITEM
    LOBJ[name] = lo

catcher = bpy.data.objects.new("Catcher", bpy.data.meshes.new("Catcher"))
bmc = bmesh.new()
bmesh.ops.create_grid(bmc, x_segments=1, y_segments=1, size=0.5)
bmc.to_mesh(catcher.data)
bmc.free()
scene.collection.objects.link(catcher)
catcher.is_shadow_catcher = True
catcher.visible_transmission = False

# a card behind the object, seen only in reflections and through glass, so glass has something to show
backdrop = bpy.data.objects.new("Backdrop", bpy.data.meshes.new("Backdrop"))
bmc = bmesh.new()
bmesh.ops.create_grid(bmc, x_segments=1, y_segments=1, size=0.5)
bmc.to_mesh(backdrop.data)
bmc.free()
scene.collection.objects.link(backdrop)
backdrop.data.materials.append(bpy.data.materials.new("BackdropMat"))
_bnt = backdrop.data.materials[0].node_tree
_bnt.nodes.clear()
_bz = sep(_bnt, O(N(_bnt, "ShaderNodeTexCoord"), "Generated"), "Y")
_be = N(_bnt, "ShaderNodeEmission", {"Color": ramp(_bnt, _bz, [(0.0, (0.25, 0.27, 0.3)), (1.0, (0.75, 0.8, 0.85))]),
                                      "Strength": 1.0})
_bnt.links.new(O(_be, "Emission"), _sock(N(_bnt, "ShaderNodeOutputMaterial").inputs, "Surface"))
BACK_EMIT = _be
backdrop.visible_camera = False
backdrop.visible_diffuse = False
backdrop.visible_shadow = False
backdrop.visible_volume_scatter = False


def dirv(az, el):
    a, e = math.radians(az), math.radians(el)
    return Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))


def item_points():
    dg = bpy.context.evaluated_depsgraph_get()
    arrs = []
    for o in ITEM.objects:
        oe = o.evaluated_get(dg)
        try:
            me = oe.to_mesh()
        except RuntimeError:
            continue
        if me is None:
            continue
        n = len(me.vertices)
        if n:
            a = np.empty(n * 3, np.float32)
            me.vertices.foreach_get("co", a)
            a = a.reshape(-1, 3).astype(np.float64)
            Mw = np.array(oe.matrix_world)
            arrs.append(a @ Mw[:3, :3].T + Mw[:3, 3])
        oe.to_mesh_clear()
    return np.concatenate(arrs)


def frame_and_light():
    bpy.context.view_layer.update()
    P = item_points()
    lo, hi = P.min(0), P.max(0)
    C = Vector(((lo + hi) / 2).tolist())
    R = float(np.linalg.norm(hi - lo) / 2)
    d = dirv(CAM_AZ, CAM_EL)
    q = (-d).to_track_quat("-Z", "Y")
    cam.rotation_euler = q.to_euler()
    Rm = q.to_matrix()
    right, up, back = [np.array(Rm.col[i]) for i in range(3)]
    t = (cam_data.sensor_width / 2) / cam_data.lens
    pos = np.array(C + d * (R / t / FILL + R))
    for _ in range(12):
        v = P - pos
        zz = -(v @ back)
        u = (v @ right) / (zz * t)
        w = (v @ up) / (zz * t)
        ext = max(u.max() - u.min(), w.max() - w.min()) / 2
        cu, cw = (u.max() + u.min()) / 2, (w.max() + w.min()) / 2
        zc = -((np.array(C) - pos) @ back)
        pos = pos + right * cu * zc * t + up * cw * zc * t
        pos = pos + back * (ext / FILL - 1) * zc
    cam.location = Vector(pos.tolist())
    for name, az, el, pw, sz, col in LIGHTS:
        lo_ = LOBJ[name]
        dv = dirv(az + CAM_AZ, el)
        lo_.location = C + dv * (R * 5.0)
        lo_.rotation_euler = (-dv).to_track_quat("-Z", "Y").to_euler()
        lo_.data.size = sz * R
        lo_.data.energy = pw * R * R
    bs = OPT.get("backdrop", 0.0)
    backdrop.hide_render = bs <= 0
    if bs > 0:
        backdrop.location = C - d * (R * 3.0)
        backdrop.rotation_euler = q.to_euler()
        backdrop.scale = (R * 9, R * 9, 1)
        _sock(BACK_EMIT.inputs, "Strength").default_value = bs
    catcher.location = (C.x, C.y, float(lo[2]) - 0.0005)
    catcher.scale = (R * 40, R * 40, 1)


def clear_item():
    for o in list(ITEM.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.curves):
        for dblock in list(coll):
            if dblock.users == 0:
                coll.remove(dblock)
    CREATED.clear()


def main():
    with open(DATA, encoding="utf-8") as f:
        data = json.load(f)
    items = [e["id"] for e in data["refined"]] + [e["id"] for e in data["parts"]]
    if ONLY:
        items = [i for i in items if i in ONLY]
    times = {}
    for iid in items:
        if iid not in BUILD:
            print("NO BUILDER for", iid)
            continue
        clear_item()
        OPT.clear()
        t0 = time.time()
        BUILD[iid]()
        frame_and_light()
        scene.render.filepath = os.path.join(OUT, iid + ".png")
        bpy.ops.render.render(write_still=True)
        times[iid] = time.time() - t0
        print("ICON %-16s %5.1f s  (%d objects)" % (iid, times[iid], len(ITEM.objects)), flush=True)
    if times:
        print("TOTAL %.1f s for %d icons, mean %.1f s" % (sum(times.values()), len(times),
                                                          sum(times.values()) / len(times)))


main()
