"""The Hanabi's parts: lamps, openings, glass, roof options, mirrors, shut lines, interior,
wheels, badge and name. Every shape and size here is the Hanabi's own, from rows.json and
Ivan's S2000 model. Runs inside Blender's Python module, called by export.py."""
import json
import sys
from pathlib import Path

import numpy as np
from shapely.affinity import scale as sscale, translate
from shapely.geometry import LineString, Point, Polygon, box
from shapely.ops import unary_union

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "common"))
import blend  # noqa: E402
import geom  # noqa: E402
import body  # noqa: E402

HERE = Path(__file__).resolve().parent
R = body.ROWS
NOSE, TAIL = body.NOSE, body.TAIL
W = R["wheel"]
BELT = R["belt"]

MATERIALS = {
    # name: (rgb, spec, alpha, emit)
    "body": ((0.97, 0.56, 0.05), 0.75, 1.0, None),
    "trim": ((0.025, 0.025, 0.03), 0.6, 1.0, None),
    "grille": ((0.04, 0.04, 0.045), 0.3, 1.0, None),
    "softtop": ((0.07, 0.07, 0.075), 0.08, 1.0, None),
    "glass": ((0.07, 0.09, 0.11), 0.92, 0.45, None),
    "head_glass": ((0.9, 0.93, 0.96), 0.95, 0.25, None),
    "tail_glass": ((0.35, 0.05, 0.06), 0.92, 0.45, None),
    "housing": ((0.045, 0.045, 0.05), 0.5, 1.0, None),
    "reflector": ((0.78, 0.79, 0.81), 0.97, 1.0, None),
    "amber": ((1.0, 0.5, 0.08), 0.85, 0.9, None),
    "lamp_head": ((0.95, 0.95, 0.92), 0.6, 1.0, (1.0, 0.97, 0.9)),
    "lamp_tail": ((0.55, 0.04, 0.05), 0.5, 1.0, (1.0, 0.06, 0.04)),
    "lamp_brake": ((0.62, 0.04, 0.05), 0.5, 1.0, (1.0, 0.04, 0.03)),
    "chrome": ((0.86, 0.87, 0.89), 0.98, 1.0, None),
    "badge_orange": ((0.93, 0.42, 0.12), 0.7, 1.0, None),
    "badge_black": ((0.09, 0.075, 0.06), 0.7, 1.0, None),
    "badge_cream": ((0.95, 0.91, 0.85), 0.7, 1.0, None),
    "interior": ((0.09, 0.09, 0.1), 0.15, 1.0, None),
    "seat": ((0.12, 0.12, 0.13), 0.12, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.66, 0.67, 0.69), 0.9, 1.0, None),
}
LAMPS = {"Headlights": ["lamp_head"], "Tail lights": ["lamp_tail"], "Brake lights": ["lamp_brake"]}
GROUPS = {"roof": "Roof", "frontbadge": "Front badge", "rearbadge": "Rear badge", "name": "Name"}


# ---------------------------------------------------------------- frames
def front_fr():
    return blend.frame((NOSE, 0, 0), (0, 1, 0), (0, 0, 1), (-1, 0, 0))


def rear_fr():
    return blend.frame((TAIL, 0, 0), (0, 1, 0), (0, 0, 1), (1, 0, 0))


def side_fr(sgn):
    return blend.frame((0, 0.7 * sgn, 0), (1, 0, 0), (0, 0, 1), (0, -sgn, 0))


def top_fr():
    return blend.frame((0, 0, 1.6), (1, 0, 0), (0, 1, 0), (0, 0, -1))


def plane_fr(p, sgn=1):
    """The frame of a lamp's own plane; sgn -1 mirrors it to the car's right."""
    k = np.array([1, sgn, 1.0])
    o, u, v, n = (np.asarray(p[a]) * k for a in ("o", "u", "v", "n"))
    return blend.frame(o, u, v, -n)


def poly(pts):
    return Polygon(pts).buffer(0)


def smooth2d(g, r):
    """Round off small notches and spurs."""
    return g.buffer(r, join_style=1).buffer(-2 * r, join_style=1).buffer(r, join_style=1)


def in_plane(src, dst):
    """src's outline carried into dst's plane coordinates."""
    o, u, v = (np.asarray(src[a]) for a in ("o", "u", "v"))
    O, U, V = (np.asarray(dst[a]) for a in ("o", "u", "v"))
    pts = [o + u * a + v * b for a, b in src["outline"]]
    return poly([((p - O) @ U, (p - O) @ V) for p in pts])


def to_plane(p3, dst):
    O, U, V = (np.asarray(dst[a]) for a in ("o", "u", "v"))
    p = np.asarray(p3) - O
    return float(p @ U), float(p @ V)


# ---------------------------------------------------------------- lamps
def headlamps(ref, cutters, out):
    h = R["head"]
    shp = smooth2d(poly(h["outline"]), 0.004)
    amber = in_plane(R["head_amber"], h).intersection(shp.buffer(-0.006))
    rest = shp.buffer(-0.008).difference(amber.buffer(0.01))
    # one projector, set in the widest part of the lamp ahead of the amber corner
    c = rest.buffer(-0.02).centroid if not rest.buffer(-0.02).is_empty else rest.centroid
    rad = min(0.034, max(0.018, 0.9 * rest.buffer(0).exterior.distance(c)))
    for sgn, side in ((1, "l"), (-1, "r")):
        fr = plane_fr(h, sgn)
        cutters += blend.lay(ref, f"cut_head_{side}", shp, fr, -0.016, 0.006, "housing", 0.008, along_ray=True)
        out += blend.lay(ref, f"head_back_{side}", shp.buffer(-0.001), fr, -0.0152, -0.013, "housing", 0.008, along_ray=True)
        out += blend.lay(ref, f"head_amber_{side}", amber, fr, -0.0128, -0.010, "amber", 0.006, along_ray=True)
        ring = c.buffer(rad + 0.012, 48).difference(c.buffer(rad + 0.002, 48)).intersection(rest)
        out += blend.lay(ref, f"head_reflector_{side}", ring, fr, -0.0128, -0.0108, "reflector", 0.004, along_ray=True)
        out += blend.lay(ref, f"lamp_head_{side}", c.buffer(rad, 48).intersection(rest), fr, -0.0128, -0.0095, "lamp_head", 0.004, along_ray=True)
        out += blend.lay(ref, f"head_glass_{side}", shp.buffer(-0.002), fr, -0.0045, -0.0018, "head_glass", 0.008, along_ray=True)


def taillamps(ref, cutters, out):
    t = R["tail"]
    shp = smooth2d(poly(t["outline"]), 0.003)
    inner = shp.buffer(-0.006)
    red = in_plane(R["tail_red"], t).intersection(inner)
    amber = in_plane(R["tail_amber"], t).intersection(inner)
    # the round lamp inboard of the amber one: the brake light, same size, our own placement
    a3 = np.asarray(R["tail_amber"]["o"])
    ac = Point(*to_plane(a3 + np.array([0, -0.14, 0]), t))
    ar = np.sqrt(amber.area / np.pi)
    brake = ac.buffer(ar, 48).intersection(inner)
    for sgn, side in ((1, "l"), (-1, "r")):
        fr = plane_fr(t, sgn)
        cutters += blend.lay(ref, f"cut_tail_{side}", shp, fr, -0.016, 0.006, "housing", 0.008, along_ray=True)
        out += blend.lay(ref, f"tail_back_{side}", shp.buffer(-0.001), fr, -0.0152, -0.013, "housing", 0.008, along_ray=True)
        out += blend.lay(ref, f"lamp_tail_{side}", red, fr, -0.0128, -0.0098, "lamp_tail", 0.005, along_ray=True)
        out += blend.lay(ref, f"tail_amber_{side}", amber, fr, -0.0128, -0.0098, "amber", 0.004, along_ray=True)
        for nm, disc in (("ring_a", amber), ("ring_b", brake)):
            ring = disc.buffer(0.009, join_style=1).difference(disc.buffer(0.0015, join_style=1)).intersection(inner)
            out += blend.lay(ref, f"tail_{nm}_{side}", ring, fr, -0.0128, -0.0108, "reflector", 0.004, along_ray=True)
        out += blend.lay(ref, f"lamp_brake_{side}", brake, fr, -0.0128, -0.0098, "lamp_brake", 0.004, along_ray=True)
        out += blend.lay(ref, f"tail_glass_{side}", shp.buffer(-0.002), fr, -0.0045, -0.0018, "tail_glass", 0.008, along_ray=True)


def small_lamps(ref, out):
    """Side markers on the front wings and the brake light strip on the boot lid."""
    m = R["marker"]
    shp = smooth2d(poly(m["outline"]), 0.002)
    for sgn, side in ((1, "l"), (-1, "r")):
        out += blend.lay(ref, f"marker_{side}", shp, plane_fr(m, sgn), 0.0006, 0.0032, "amber", 0.004)
    strip = smooth2d(poly(R["brake3"]), 0.002)
    out += blend.lay(ref, "lamp_brake_high", strip, rear_fr(), 0.0006, 0.0034, "lamp_brake", 0.006)


# ---------------------------------------------------------------- front openings
def openings(ref, cutters):
    """The grille and the two side intakes, as cutters; their shapes in (y, z) for painting."""
    fr = front_fr()
    grille = smooth2d(poly(R["grille"]), 0.006)
    intake = smooth2d(poly(R["intake"]), 0.004)
    shapes = [grille, intake, sscale(intake, -1, 1, origin=(0, 0))]
    for i, g in enumerate(shapes):
        cutters += blend.lay(ref, f"cut_open_{i}", g, fr, -0.07, 0.008, "grille", 0.01, along_ray=True)
    return shapes


def grille_parts(ref, shapes):
    """Slats across the grille and a mesh in each intake, set back in the openings."""
    fr = front_fr()
    out = []
    g = shapes[0].buffer(-0.003)
    z0, z1 = g.bounds[1], g.bounds[3]
    n = 4
    for i in range(1, n):
        z = z0 + (z1 - z0) * i / n
        out += blend.lay(ref, f"grille_slat_{i}", g.intersection(box(-2, z - 0.006, 2, z + 0.006)), fr,
                         -0.034, -0.022, "grille", 0.006, along_ray=True)
    for i, s in enumerate(shapes):
        out += blend.lay(ref, f"grille_back_{i}", s.buffer(-0.001), fr, -0.06, -0.052, "grille", 0.01, along_ray=True)
    return out


# ---------------------------------------------------------------- roof and glass
def envelope():
    """The windscreen and roof's outer surface as a solid, used only to lay glass and trim on."""
    xs, rcv = body.roof_curves()
    v, f = body.loft(lambda x: body.roof_section(rcv, x, BELT), xs[0], xs[-1], 0.02)
    return blend.make("envelope", v, f, "trim"), xs, rcv


def screen_shapes():
    """In plan: the frame round the sides and top, the black band, the glass. The base tucks
    under the cowl, clear of the roof surface's front end."""
    sc = poly(R["screen"]["outline"]).convex_hull
    base = R["roof"]["x1"] - 0.04
    sc = sc.intersection(box(R["screen"]["x_top"] - 0.005, -2, base, 2))
    frame = sc.buffer(0.03, join_style=1).intersection(box(-5, -2, base, 2))
    glass = sc.buffer(-0.012, join_style=1).union(sc.intersection(box(base - 0.03, -2, base, 2)).buffer(-0.012).buffer(0.012))
    glass = glass.intersection(sc.buffer(-0.012).union(box(base - 0.04, -0.4, base, 0.4)).buffer(0))
    return frame, sc, glass


def screen_parts(env_bvh, ref):
    """Windscreen: a body-colour frame, a black band round the glass, the glass. Its foot is
    drawn down onto the body, fading out towards the top."""
    fr = top_fr()
    frame, sc, glass = screen_shapes()
    out = blend.lay(env_bvh, "screen_frame", frame.difference(sc), fr, -0.004, 0.006, "body", 0.01)
    out += blend.lay(env_bvh, "screen_band", sc.difference(glass), fr, -0.004, 0.003, "trim", 0.008)
    out += blend.lay(env_bvh, "screen_glass", glass, fr, -0.004, 0.0, "glass", 0.02)
    x_top = R["screen"]["x_top"]
    for ob in out:
        _seat(ob, ref, x_top)
    return out


def _seat(ob, ref, x_top):
    """Lower each point by the gap to the body under the screen's foot, less towards the top."""
    from mathutils import Vector
    me = ob.data
    x_foot = R["roof"]["x1"] - 0.04
    gaps = []
    for y in np.linspace(-0.6, 0.6, 25):
        loc = blend.cast(ref, top_fr(), x_foot - 0.06, y)[0]
        if loc is not None:
            gaps.append(loc[2])
    body_z = float(np.median(gaps)) if gaps else BELT
    zs = np.array([v.co.z for v in me.vertices])
    xs = np.array([v.co.x for v in me.vertices])
    foot = zs[xs > x_foot - 0.12].min() if (xs > x_foot - 0.12).any() else body_z
    drop = max(0.0, foot - body_z - 0.004)
    for v in me.vertices:
        w = np.clip((v.co.x - x_top - 0.1) / (x_foot - x_top - 0.1), 0, 1)
        v.co.z -= drop * w


def side_glass_shape():
    g = smooth2d(poly(R["side_glass"]), 0.015)
    return g.buffer(-0.004)


def rear_glass_shape():
    return smooth2d(poly(R["rear_glass"]), 0.015).buffer(-0.004)


def roof_shell(xs, rcv, plain, mat):
    """The top as a shell 18 mm thick, behind the windscreen, standing on the body."""
    x_front = R["screen"]["x_top"] + 0.02
    x0 = float(xs[0])
    v, f = body.loft(lambda x: body.roof_section(rcv, x, BELT, drop=0.12), x0, x_front, 0.02,
                  centre=lambda x: (0.0, BELT))
    outer = blend.make("roof_outer", v, f, mat)
    v, f = body.loft(lambda x: body.roof_section(rcv, x, BELT, drop=0.2, grow=-0.018), x0 - 0.05, x_front + 0.05, 0.02,
                  centre=lambda x: (0.0, BELT))
    blend.boolean(outer, blend.make("roof_inner", v, f, mat), "DIFFERENCE")
    keep = plain.copy(); keep.data = plain.data.copy()
    import bpy
    bpy.context.scene.collection.objects.link(keep)
    blend.boolean(outer, keep, "DIFFERENCE")                # stands on the body, no overlap
    return outer


def roof_options(env_bvh, xs, rcv, plain):
    """1 soft top (the reference), 2 roof down, 3 body-colour hardtop. Returns {n: [objects]}."""
    sg, rg = side_glass_shape(), rear_glass_shape()
    opts = {}
    for n, mat in ((1, "softtop"), (3, "body")):
        top = roof_shell(xs, rcv, plain, mat)
        cuts = []
        for sgn, side in ((1, "l"), (-1, "r")):
            cuts += blend.lay(env_bvh, f"cut_rs_{side}", sg, side_fr(sgn), -0.05, 0.01, "trim", 0.01, along_ray=True)
        cuts += blend.lay(env_bvh, "cut_rr", rg, rear_fr(), -0.05, 0.01, "trim", 0.01, along_ray=True)
        for c in cuts:
            blend.boolean(top, c, "DIFFERENCE")
        objs = [top]
        for sgn, side in ((1, "l"), (-1, "r")):
            objs += blend.lay(env_bvh, f"roof_side_glass_{side}", sg.buffer(0.006), side_fr(sgn), -0.012, -0.008, "glass", 0.02, along_ray=True)
            if n == 3:
                band = sg.buffer(0.012).difference(sg.buffer(-0.002))
                objs += blend.lay(env_bvh, f"roof_side_band_{side}", band, side_fr(sgn), 0.0005, 0.0025, "trim", 0.008)
        objs += blend.lay(env_bvh, "roof_rear_glass", rg.buffer(0.006), rear_fr(), -0.012, -0.008, "glass", 0.02, along_ray=True)
        opts[n] = objs
    opts[2] = [boot_cover()]
    return opts


def boot_cover():
    """Roof down: the folded top under a low padded cover behind the seats."""
    x0, x1 = -1.34, COCKPIT_X[0] + 0.02
    rings = []
    for x in np.linspace(x0, x1, 14):
        t = (x - x0) / (x1 - x0)
        k = np.sin(np.pi * t) ** 0.35
        hw = 0.62 * (0.92 + 0.08 * k)
        top = body.z_top(x) + 0.045 * k + 0.004
        base = body.z_top(x) - 0.03
        rr = min(0.03, (top - base) / 2.2)
        sh = box(-hw, base, hw, top).buffer(-rr, join_style=1).buffer(rr, join_style=1)
        ring = body.even(np.array(sh.exterior.coords)[:-1], 64)
        rings.append(np.column_stack([np.full(64, x), ring]))
    v, f = geom.close_rings(rings)
    return blend.make("boot_cover", v, f, "softtop")


# ---------------------------------------------------------------- cockpit
COCKPIT_X = (-1.12, 0.45)
COCKPIT_FLOOR = 0.30


def cockpit_half(x):
    """Half width of the opening at x, from the door tops and deck, rounded at the back."""
    pts = np.array(R["cockpit"])
    y = float(np.interp(x, pts[::-1, 0], pts[::-1, 1])) - 0.012
    y = min(y, 0.705)
    back = x - COCKPIT_X[0]
    if back < 0.12:
        y -= 0.12 - np.sqrt(max(0.0, 0.12 ** 2 - (0.12 - back) ** 2))
    front = COCKPIT_X[1] - x
    if front < 0.2:
        y = min(y, 0.56 + 0.145 * front / 0.2)
    return max(y, 0.2)


def cockpit_cutters():
    """The cockpit as two cutters: the room inside the body below the door tops, and the
    opening through the door tops and deck seen from above."""
    from shapely.geometry.polygon import orient
    rings = []
    for x in np.linspace(COCKPIT_X[0], COCKPIT_X[1], 40):
        g = body.inside(x, 0.035).intersection(box(-3, COCKPIT_FLOOR, 3, BELT - 0.05))
        if g.geom_type != "Polygon":
            g = max(g.geoms, key=lambda q: q.area)
        ring = body.even(np.array(orient(g, 1).exterior.coords)[:-1], 96)
        rings.append(np.column_stack([np.full(96, x), ring]))
    v, f = geom.close_rings(rings)
    room = blend.make("cockpit_room", v, f, "interior")
    xs = np.linspace(COCKPIT_X[0], COCKPIT_X[1], 80)
    plan = Polygon([(x, cockpit_half(x)) for x in xs] + [(x, -cockpit_half(x)) for x in xs[::-1]])
    plan = orient(plan.buffer(0), 1)
    loop = geom.resample(np.array(plan.exterior.coords), 0.01)[:-1]
    rings = [np.column_stack([loop, np.full(len(loop), z)]) for z in (BELT - 0.09, 2.0)]
    v, f = geom.close_rings(rings)
    return [room, blend.make("cockpit_opening", v, f, "interior")]


def arches():
    """Arch cutters and the wheel tubs kept out of the cockpit."""
    cut, tubs = [], []
    for (ax, ar) in ((W["front"]["x"], W["arch_r"][0]), (W["rear"]["x"], W["arch_r"][1])):
        for sgn in (1, -1):
            v, f = geom.disc((ax, 0.84 * sgn, W["r"]), (0, 1, 0), ar, 0.56, n=96)
            cut.append(blend.make("arch", v, f, "interior"))
            v, f = geom.disc((ax, 0.84 * sgn, W["r"]), (0, 1, 0), ar + 0.035, 0.74, n=96)
            tubs.append(blend.make("tub", v, f, "interior"))
    return cut, tubs


# ---------------------------------------------------------------- shut lines
def grooves(ref):
    obs = []
    door = [(x, min(z, BELT - 0.012)) for x, z in R["door"]]       # stop short of the door top's curl
    for sgn, side in ((1, "l"), (-1, "r")):
        obs.append(blend.groove(ref, f"g_door_{side}", door, side_fr(sgn)))
    # the traced outlines carry the hinge tabs: leave them out
    bonnet = [p for p in R["bonnet"] if p[0] > 0.385]
    boot = [p for p in R["boot"] if p[0] < -1.39]
    for name, pts in (("bonnet", bonnet), ("boot", boot)):
        g = smooth2d(poly(pts), 0.012).buffer(-0.004, join_style=1)
        ring = geom.resample(np.array(g.exterior.coords), 0.01)[:-1]
        # two U shaped halves, split where the line crosses the centre line, overlapping a little
        mid = np.where(np.abs(ring[:, 1]) < 0.02)[0]
        a = mid[np.argmax(ring[mid, 0])]
        b = mid[np.argmin(ring[mid, 0])]
        ring = np.roll(ring, -a, axis=0)
        b = (b - a) % len(ring)
        obs.append(blend.groove(ref, f"g_{name}_a", ring[: b + 3], top_fr()))
        obs.append(blend.groove(ref, f"g_{name}_b", np.vstack([ring[b - 2:], ring[:3]]), top_fr()))
    return obs


# ---------------------------------------------------------------- mirrors and exhaust
def _mirror_rings(sgn, inset=0.0, x_lo=None, x_hi=None, y_from=None):
    rows = [r for r in R["mirror"] if y_from is None or r[0] >= y_from]
    rings = []
    for y, x0, x1, z0, z1 in rows:
        a = x0 + inset if x_lo is None else x0 + x_lo
        b = x1 - inset if x_hi is None else x0 + x_hi
        sh = box(a, z0 + inset, b, z1 - inset)
        rr = min(0.022, (b - a) / 2.2, (z1 - z0 - 2 * inset) / 2.2)
        sh = sh.buffer(-rr, join_style=1).buffer(rr, join_style=1)
        ring = body.even(np.array(sh.exterior.coords)[:-1], 64)
        rings.append(np.column_stack([ring[:, 0], np.full(64, y * sgn), ring[:, 1]]))
    return rings


MIRROR_FOOT = 0.84


def mirrors():
    """Each housing lofted through the measured slices, its root sunk into the door top."""
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        rings = _mirror_rings(sgn)
        root = rings[0].copy(); root[:, 1] = (R["mirror"][0][0] - 0.03) * sgn
        z0, z1 = root[:, 2].min(), root[:, 2].max()
        root[:, 2] = MIRROR_FOOT + (root[:, 2] - z0) * (z1 - MIRROR_FOOT) / (z1 - z0)     # down onto the door
        tip = rings[-1].copy()
        c = tip.mean(0)
        tip = c + (tip - c) * 0.6; tip[:, 1] = (R["mirror"][-1][0] + 0.006) * sgn
        rings = [root] + rings + [tip]
        if sgn < 0:
            rings = rings[::-1]
        v, f = geom.close_rings(rings)
        obs.append(blend.make(f"mirror_{side}", v, f, "body"))
    return obs


def mirror_recess():
    """A shallow recess in each housing's back face, with the glass in it."""
    y_from = R["mirror"][0][0] + 0.03
    cut, glass = [], []
    for sgn, side in ((1, "l"), (-1, "r")):
        for name, x_lo, x_hi, inset, out, mat in (("cut_mirror", -0.03, 0.008, 0.008, cut, "trim"),
                                                   ("mirror_glass", 0.002, 0.005, 0.0095, glass, "mirror_glass")):
            rings = _mirror_rings(sgn, inset, x_lo, x_hi, y_from)[:-1]
            if sgn < 0:
                rings = rings[::-1]
            v, f = geom.close_rings(rings)
            out.append(blend.make(f"{name}_{side}", v, f, mat))
    return cut, glass


def _skin_x(y, z):
    """The rearmost x where the skin reaches out to (y, z)."""
    for x in np.arange(TAIL, TAIL + 0.5, 0.004):
        if body.y_at(x, z) >= abs(y):
            return float(x)
    return TAIL


def exhaust():
    e = R["exhaust"]
    cut, pipes = [], []
    x_end = _skin_x(e["y"] + e["r"], e["z"]) - 0.025       # the tips stand just proud of the bumper
    e = dict(e, x=x_end)
    for sgn, side in ((1, "l"), (-1, "r")):
        y = e["y"] * sgn
        prof = [(e["r"] - 0.004, 0.0), (e["r"], 0.0), (e["r"], 0.25), (e["r"] - 0.004, 0.25)]
        v, f = geom.lathe(prof, 48, axis="z")
        v = np.column_stack([e["x"] + v[:, 2], y + v[:, 0], e["z"] + v[:, 1]])
        pipes.append(blend.make(f"exhaust_{side}", v, f, "chrome"))
        v, f = geom.disc((e["x"] + 0.13, y, e["z"]), (1, 0, 0), e["r"] + 0.005, 0.3)
        cut.append(blend.make("cut_exhaust", v, f, "trim"))
    return cut, pipes


# ---------------------------------------------------------------- interior
def _rbox(name, x0, x1, y0, y1, z0, z1, r=0.03, lean=0.0, mat="seat"):
    sh = box(y0, z0, y1, z1).buffer(-r, join_style=1).buffer(r, join_style=1)
    ring = body.even(np.array(sh.exterior.coords)[:-1], 48)
    rings = []
    cy, cz = (y0 + y1) / 2, (z0 + z1) / 2
    for x in (x0, x0 + 0.012, x1 - 0.012, x1):
        k = 0.93 if x in (x0, x1) else 1.0
        rr = np.column_stack([cy + (ring[:, 0] - cy) * k, cz + (ring[:, 1] - cz) * k])
        rings.append(np.column_stack([x - lean * (rr[:, 1] - z0), rr]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def _wall(name, x0, x1, clip, n=12, mat="interior"):
    """A panel following the inside of the body between x0 and x1, cut to clip(x)."""
    rings = []
    for x in np.linspace(x0, x1, n):
        g = body.inside(x, 0.035 + 0.003).intersection(clip(x))
        if g.geom_type != "Polygon":
            g = max(getattr(g, "geoms", [g]), key=lambda q: q.area)
        ring = body.even(np.array(g.exterior.coords)[:-1], 64)
        rings.append(np.column_stack([np.full(64, x), ring]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def _tube(name, path, r, mat):
    pts = geom.resample(path, 0.02)
    rings = []
    for i, p in enumerate(pts):
        t = pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]
        t /= np.linalg.norm(t)
        a = np.cross(t, [1.0, 0, 0]); a /= np.linalg.norm(a)
        b = np.cross(t, a)
        ang = np.linspace(0, 2 * np.pi, 20, endpoint=False)
        rings.append(np.array([p + r * (np.cos(q) * a + np.sin(q) * b) for q in ang]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def interior():
    x_front, x_back = COCKPIT_X[1] - 0.004, COCKPIT_X[0] + 0.004
    fl = COCKPIT_FLOOR + 0.002
    obs = [_wall("firewall", x_front - 0.008, x_front, lambda x: box(-1, fl, 1, BELT - 0.04), n=2),
           _wall("bulkhead", x_back, x_back + 0.008, lambda x: box(-1, fl, 1, BELT - 0.04), n=2),
           _wall("dash", 0.12, x_front - 0.0085, lambda x: box(-1, 0.62 + 0.12 * (x - 0.12) / 0.33, 1, BELT - 0.04), n=10)]
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, 0.42, 1.2, BELT - 0.05) if s > 0 else box(-1.2, 0.42, -0.3, BELT - 0.05)).intersection(
            body.inside(x, 0.035 + 0.003)).difference(body.inside(x, 0.035 + 0.03))
        obs.append(_wall(f"door_card_{side}", -0.72, 0.28, clip, n=12))
        yc = 0.33 * sgn
        obs.append(_rbox(f"seat_base_{side}", -0.62, -0.12, yc - 0.23, yc + 0.23, fl, 0.45))
        obs.append(_rbox(f"seat_back_{side}", -0.76, -0.64, yc - 0.23, yc + 0.23, 0.451, 1.0, lean=0.2))
        obs.append(_rbox(f"seat_wing_l_{side}", -0.78, -0.62, yc + 0.17, yc + 0.25, 0.5, 0.95, r=0.03, lean=0.2))
        obs.append(_rbox(f"seat_wing_r_{side}", -0.78, -0.62, yc - 0.25, yc - 0.17, 0.5, 0.95, r=0.03, lean=0.2))
        # roll hoop behind the seat, standing on the bulkhead's face
        hx = x_back + 0.03
        path = [(hx, yc - 0.17, BELT - 0.03), (hx, yc - 0.155, 1.06), (hx, yc - 0.11, 1.12),
                (hx, yc, 1.135), (hx, yc + 0.11, 1.12), (hx, yc + 0.155, 1.06), (hx, yc + 0.17, BELT - 0.03)]
        obs.append(_tube(f"hoop_{side}", geom.catmull_open(path, 6), 0.021, "trim"))
    # steering wheel ahead of the right seat, and the rear view mirror under the screen's top
    sw = []
    yc = -0.33
    ang = np.linspace(0, 2 * np.pi, 41)
    ring = [(0.2 + 0.05 * np.sin(a), yc + 0.18 * np.cos(a), 0.74 + 0.17 * np.sin(a)) for a in ang]
    obs.append(_tube("steering_wheel", np.array(ring), 0.016, "trim"))
    return obs


def rear_view_mirror(env_bvh):
    xm = R["screen"]["x_top"] + 0.06
    loc, _n = blend.cast(env_bvh, top_fr(), xm, 0.0)
    top = float(loc[2]) - 0.012 if loc is not None else 1.2
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.11, 0.11, top - 0.11, top - 0.05, r=0.02, mat="interior")
    stem = _rbox("stem", xm - 0.014, xm - 0.006, -0.01, 0.01, top - 0.06, top + 0.004, r=0.004, mat="interior")
    return blend.union_all([m, stem], "mirror_rear")


# ---------------------------------------------------------------- wheels
def wheels():
    obs = []
    for ax, key, fb in ((W["front"]["x"], "front", "f"), (W["rear"]["x"], "rear", "r")):
        w = W[key]["width"]
        hw = w / 2
        yc = W[key]["track"] / 2
        tyre = box(W["rim_r"], -hw, W["r"], hw).buffer(-0.03, join_style=1).buffer(0.03, join_style=1)
        tyre = tyre.union(box(W["rim_r"], -hw + 0.012, W["rim_r"] + 0.03, hw - 0.012))
        tp = body.even(np.array(tyre.exterior.coords)[:-1], 96)
        rim = [(W["rim_r"] - 0.001, -hw + 0.012), (W["rim_r"] - 0.001, hw - 0.012), (W["rim_r"] - 0.014, hw - 0.004),
               (W["rim_r"] - 0.026, hw - 0.01), (W["rim_r"] - 0.026, -hw + 0.012)]
        for sgn, lr in ((1, "l"), (-1, "r")):
            def place(v, s=sgn):
                v = np.asarray(v, float)
                return np.column_stack([ax + v[:, 0], s * (yc + v[:, 1]), W["r"] + v[:, 2]])
            v, f = geom.lathe(tp, 96, axis="y")
            obs.append(blend.make(f"wheel_tyre_{fb}{lr}", place(v), f, "tyre"))
            v, f = geom.lathe(rim, 96, axis="y")
            parts = [blend.make("rim", place(v), f, "rim")]
            face = hw - 0.03
            v, f = geom.disc((0, face - 0.02, 0), (0, 1, 0), 0.07, 0.045, n=48)
            parts.append(blend.make("hub", place(v), f, "rim"))
            for k in range(5):
                a = 2 * np.pi * k / 5
                ca, sa = np.cos(a), np.sin(a)
                pts = []
                # a spoke that narrows and leans outward towards the rim
                for r, w2, h0, h1 in ((0.05, 0.03, face - 0.04, face), (W["rim_r"] - 0.02, 0.02, face - 0.03, face - 0.012)):
                    for dw in (-w2, w2):
                        for hh in (h0, h1):
                            pts.append((r * ca - dw * sa, hh, r * sa + dw * ca))
                f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
                parts.append(blend.make("spoke", place(np.array(pts)), f, "rim"))
            obs.append(blend.union_all(parts, f"wheel_rim_{fb}{lr}"))
    return obs


# ---------------------------------------------------------------- badge and name
BADGE_SVG = HERE.parent.parent.parent / "src/renderer/assets/brands/tora/tora-badge-colour.svg"


def badge_pieces():
    """The Tora badge, full colour with a chrome rim: [(shape, material)], flat, centred, 1 unit
    tall, y up. Later shapes in the drawing cover earlier ones, so each colour is what shows."""
    import re
    svg = BADGE_SVG.read_text()
    colours = {"#16130f": "badge_black", "#ee6a1f": "badge_orange", "#f3e9d8": "badge_cream"}
    shapes = []
    # walk the drawing in order, tracking the mirror groups
    pos, mirror = 0, []
    for tok in re.finditer(r'<g transform="translate\(200 0\) scale\(-1 1\)">|</g>|<polygon points="([^"]+)" fill="([^"]+)"/>', svg):
        t = tok.group(0)
        if t.startswith("<g"):
            mirror.append(True)
        elif t == "</g>":
            if mirror:
                mirror.pop()
        else:
            pts = [tuple(map(float, p.split(","))) for p in tok.group(1).split()]
            if mirror:
                pts = [(200 - x, y) for x, y in pts]
            shapes.append((Polygon(pts).buffer(0), colours[tok.group(2)]))
    whole = unary_union([s for s, _m in shapes])
    b = whole.bounds
    k = 1 / (b[3] - b[1])
    fix = lambda g: sscale(translate(sscale(g, 1, -1, origin=(0, 0)), -(b[0] + b[2]) / 2, (b[1] + b[3]) / 2), k, k, origin=(0, 0))
    out, covered = [], None
    for s, m in reversed(shapes):
        vis = s if covered is None else s.difference(covered)
        covered = s if covered is None else covered.union(s)
        if not vis.is_empty:
            out.append((fix(vis), m))
    outline = fix(whole)
    return outline, out


def name_shape():
    gl = json.loads((HERE / "glyphs.json").read_text())
    g = unary_union([Polygon(p[0], p[1:]) for p in gl["pieces"]])
    b = g.bounds
    g = translate(g, -(b[0] + b[2]) / 2, -gl["cap"] / 2)
    return sscale(g, 1 / gl["cap"], 1 / gl["cap"], origin=(0, 0))


def _flat(ref, g, size, fr, a, b, up, name, d0, d1, mat, step=0.004):
    """Lay shape g (right, up, 1 unit = size) flat on the panel that fr's ray at (a, b) meets."""
    pf = blend.panel_frame(ref, fr, a, b, up)
    return blend.lay(ref, name, sscale(g, size, size, origin=(0, 0)), pf, d0, d1, mat, step)


def badge(ref, size, fr, a, b, up, name):
    """Chrome rim under the colour face; each colour a little apart so no faces touch."""
    outline, pieces = badge_pieces()
    rim = 0.07
    objs = _flat(ref, outline.buffer(rim, join_style=2), size, fr, a, b, up, name + "_rim", 0.0012, 0.0030, "chrome", 0.003)
    for i, (g, m) in enumerate(pieces):
        g = g.buffer(-0.3 * 0.001 / size)
        if g.is_empty:
            continue
        objs += _flat(ref, g, size, fr, a, b, up, f"{name}_{i}", 0.0030, 0.0040, m, 0.002)
    return objs


def options(ref):
    """Badge and name options, named opt_<group>_<n>_<i>. Each lies flat on its panel."""
    sp = R["spots"]
    nm = name_shape()
    up, fwd = (0, 0, 1), (-1, 0, 0)
    out = []

    def add(group, n, objs):
        for i, ob in enumerate(objs):
            ob.name = f"opt_{group}_{n}_{i}"
            out.append(ob)
    # front: 1 on the nose above the grille (the reference), 2 on the bonnet's front edge
    add("frontbadge", 1, badge(ref, 0.06, front_fr(), sp["front"][0], sp["front"][1], up, "fb"))
    add("frontbadge", 2, badge(ref, 0.06, top_fr(), sp["bonnet_x"], 0, fwd, "fb"))
    # rear: 1 middle of the boot lid (the reference), 2 lower, above the plate recess
    add("rearbadge", 1, badge(ref, 0.06, rear_fr(), 0, sp["rear"][1], up, "rb"))
    add("rearbadge", 2, badge(ref, 0.06, rear_fr(), 0, sp["rear_low_z"], up, "rb"))
    cap = 0.028
    q = []
    for sgn in (1, -1):
        q += _flat(ref, nm, cap, side_fr(sgn), sp["side"][0], sp["side"][1], up, "nm", 0.0012, 0.0025, "chrome")
    add("name", 1, q)
    add("name", 2, _flat(ref, nm, cap, rear_fr(), -0.2, sp["rear"][1] - 0.045, up, "nm", 0.0012, 0.0025, "chrome"))
    return out
