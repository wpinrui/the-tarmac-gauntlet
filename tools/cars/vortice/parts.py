"""The Vortice's parts: lamps, intakes, glass, trim, mirrors, exhaust, diffuser, interior,
wheels, badge and name. Every shape and size here is the Vortice's own, from rows.json and
Ivan's references, kept plain and smooth. Runs inside Blender's Python module, called by export.py."""
import json
import re
import sys
from pathlib import Path

import numpy as np
from shapely import affinity
from shapely.affinity import scale as sscale, translate
from shapely.geometry import LineString, MultiPoint, Point, Polygon, box
from shapely.ops import unary_union

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "common"))
import blend  # noqa: E402
import geom  # noqa: E402
import body  # noqa: E402

HERE = Path(__file__).resolve().parent
R = body.ROWS
NOSE, TAIL = body.NOSE, body.TAIL
W = R["wheel"]

MATERIALS = {
    # name: (rgb, spec, alpha, emit)
    "body": ((0.98, 0.58, 0.04), 0.75, 1.0, None),
    "trim": ((0.025, 0.025, 0.03), 0.65, 1.0, None),
    "grille": ((0.04, 0.04, 0.045), 0.3, 1.0, None),
    "glass": ((0.07, 0.09, 0.11), 0.92, 0.45, None),
    "head_glass": ((0.92, 0.94, 0.96), 0.95, 0.22, None),
    "tail_glass": ((0.55, 0.03, 0.04), 0.92, 0.5, None),
    "housing": ((0.05, 0.05, 0.055), 0.5, 1.0, None),
    "reflector": ((0.78, 0.79, 0.81), 0.97, 1.0, None),
    "lamp_head": ((0.95, 0.95, 0.92), 0.6, 1.0, (1.0, 0.97, 0.9)),
    "lamp_tail": ((0.5, 0.03, 0.04), 0.5, 1.0, (1.0, 0.06, 0.04)),
    "lamp_brake": ((0.6, 0.04, 0.05), 0.5, 1.0, (1.0, 0.04, 0.03)),
    "chrome": ((0.86, 0.87, 0.89), 0.98, 1.0, None),
    "dark_metal": ((0.16, 0.16, 0.17), 0.8, 1.0, None),
    "engine_red": ((0.62, 0.05, 0.06), 0.7, 1.0, None),
    "engine_metal": ((0.42, 0.43, 0.45), 0.85, 1.0, None),
    "badge_gold": ((0.79, 0.64, 0.15), 0.8, 1.0, None),
    "badge_red": ((0.78, 0.06, 0.18), 0.6, 1.0, None),
    "badge_black": ((0.08, 0.08, 0.08), 0.6, 1.0, None),
    "badge_cream": ((0.96, 0.93, 0.89), 0.6, 1.0, None),
    "badge_green": ((0.0, 0.54, 0.29), 0.6, 1.0, None),
    "interior": ((0.09, 0.09, 0.1), 0.15, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.6, 0.62, 0.64), 0.9, 1.0, None),
}
LAMPS = {"Headlights": ["lamp_head"], "Tail lights": ["lamp_tail"], "Brake lights": ["lamp_brake"]}
GROUPS = {"frontbadge": "Front badge", "rearbadge": "Rear badge", "name": "Name"}


# ---------------------------------------------------------------- frames
def front_fr():
    return blend.frame((NOSE, 0, 0), (0, 1, 0), (0, 0, 1), (-1, 0, 0))


def rear_fr():
    return blend.frame((TAIL, 0, 0), (0, 1, 0), (0, 0, 1), (1, 0, 0))


def side_fr(sgn):
    return blend.frame((0, 0.7 * sgn, 0), (1, 0, 0), (0, 0, 1), (0, -sgn, 0))


def top_fr():
    return blend.frame((0, 0, 1.3), (1, 0, 0), (0, 1, 0), (0, 0, -1))


def slope_fr(x0, z0, x1, z1):
    """A frame square to a panel sloping from (x0, z0) to (x1, z1) along the car, and a map from
    the top view (x, y) into it, so a shape drawn from above keeps its width once laid."""
    t = np.array([x1 - x0, 0.0, z1 - z0]); t /= np.linalg.norm(t)
    d = np.array([t[2], 0.0, -t[0]])
    if d[2] > 0:
        d = -d
    fr = blend.frame((x0, 0, z0), t, (0, 1, 0), d)
    return fr, lambda g: sscale(translate(g, -x0, 0), 1 / t[0], 1, origin=(0, 0))


def plane_fr(pts):
    """A frame on the best plane through 3D points, its rays running into the body, and a map
    from 3D points to its 2D."""
    p = np.asarray(pts, float).reshape(-1, 3)
    c = p.mean(0)
    _u, _s, vt = np.linalg.svd(p - c)
    n = vt[2]
    if n @ np.array([c[0], 0.0, c[2] - 0.4]) < 0:       # outward: away from the car's middle
        n = -n
    u = vt[0]; v = np.cross(n, u)
    fr = blend.frame(c, u, v, -n)
    return fr, lambda q: np.column_stack([(np.asarray(q) - c) @ u, (np.asarray(q) - c) @ v])


def poly(pts):
    return Polygon(pts).buffer(0)


def flip(g):
    """Mirror a 2D shape about its first axis (y for front and rear frames)."""
    return sscale(g, -1, 1, origin=(0, 0))


def soft(g, r):
    """Round off a shape's corners."""
    return g.buffer(-r, join_style=1).buffer(r, join_style=1)


def biggest(g):
    if g.geom_type != "Polygon":
        g = max(g.geoms, key=lambda q: q.area)
    return Polygon(g.exterior)


# ---------------------------------------------------------------- lamps
def head_frame(sgn):
    """The head lamp glass is one long sloped face: a frame on its plane."""
    h = np.array(R["head"]).reshape(-1, 3) * [1, sgn, 1]
    return plane_fr(h)


def head_shape(sgn):
    fr, to2 = head_frame(sgn)
    tris = np.array(R["head"]) * [1, sgn, 1]
    g = unary_union([Polygon(to2(t)).buffer(1e-4) for t in tris])
    g = soft(biggest(g.buffer(0.01, join_style=1).buffer(-0.01, join_style=1)), 0.015).simplify(0.002)
    return fr, to2, g


def headlamps(ref, cutters, out, sgn):
    side = "l" if sgn > 0 else "r"
    fr, to2, shp = head_shape(sgn)
    cutters += blend.lay(ref, f"cut_head_{side}", shp, fr, -0.018, 0.006, "housing", 0.01, along_ray=True)
    # our own interior: one round projector lens at the lamp's front, and a light strip along
    # its upper edge
    front = np.array([NOSE, 0.70 * sgn, 0.60])
    c = Point(*to2([front])[0])
    c = shp.buffer(-0.035).exterior.interpolate(shp.buffer(-0.035).exterior.project(c))
    lens = c.buffer(0.03, 48).intersection(shp.buffer(-0.008))
    out += blend.lay(ref, f"lamp_head_{side}0", lens, fr, -0.0125, -0.009, "lamp_head", 0.004, along_ray=True)
    out += blend.lay(ref, f"head_reflector_{side}", c.buffer(0.043, 48).difference(c.buffer(0.032, 48)).intersection(shp.buffer(-0.008)),
                     fr, -0.0122, -0.0105, "reflector", 0.004, along_ray=True)
    strip = shp.buffer(-0.008).difference(shp.buffer(-0.02)).difference(c.buffer(0.06))
    top = biggest(strip) if not strip.is_empty else None
    if top is not None:
        out += blend.lay(ref, f"lamp_head_{side}1", top, fr, -0.0125, -0.009, "lamp_head", 0.004, along_ray=True)
    out += blend.lay(ref, f"head_glass_{side}", shp.buffer(-0.003), fr, -0.0045, -0.0015, "head_glass", 0.01, along_ray=True)


TAIL_POD = 0.012     # the round tail lamp pod is this much wider than its lamp


def _cyl(name, y, z, r, x0, x1, mat, n=96):
    v, f = geom.disc(((x0 + x1) / 2, y, z), (1, 0, 0), r, x1 - x0, n=n)
    return blend.make(name, v, f, mat)


POD_LEN = 0.40       # each tail lamp is a cone running forward into the body over this length
POD_GAP = 0.0015     # the body is cut this much clear of the cone


def _cone(name, y, z, r, x0, mat, n=96):
    """A round cone, full size at its back face and tapering forward as it sinks into the body."""
    t = np.linspace(0, 2 * np.pi, n, endpoint=False)
    rings = []
    for s in np.linspace(0, 1, 16):
        k = 1 - 0.75 * s ** 1.5
        zc = z - 0.06 * s ** 2
        rings.append(np.column_stack([np.full(n, x0 + POD_LEN * s), y + r * k * np.cos(t), zc + r * k * np.sin(t)]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def _ring(name, y, z, r0, r1, x0, x1, mat):
    ob = _cyl(name, y, z, r1, x0, x1, mat)
    blend.boolean(ob, _cyl("hole", y, z, r0, x0 - 0.004, x1 + 0.004, mat), "DIFFERENCE")
    return ob


def tail_x_face():
    """The back of each tail lamp: just behind the rearmost skin across the lamp, so the whole
    lamp stands proud on a face square to the car and reads as a true circle."""
    _x, y, z, r = R["tail_lamp"]
    rim = [_tail_x(y + r * np.cos(t), z + r * np.sin(t)) for t in np.linspace(0, 2 * np.pi, 24, endpoint=False)]
    return min(rim) - 0.012


def tail_pods():
    """Each tail lamp is a red cone standing out of the tail, lit whole as the tail light, with a
    dark bezel round its round face and a brake light in the middle: (body cutters, lamp parts)."""
    _x, y, z, r = R["tail_lamp"]
    xf = tail_x_face()
    R0 = r + TAIL_POD
    cut, parts = [], []
    for sgn, side in ((1, "l"), (-1, "r")):
        yc = y * sgn
        cut.append(_cone(f"cut_tail_{side}", yc, z, R0 + POD_GAP, xf + 0.006, "housing"))
        parts += [_cone(f"lamp_tail_{side}0", yc, z, R0, xf + 0.006, "lamp_tail"),
                  _ring(f"tail_bezel_{side}", yc, z, r - 0.006, R0 - 0.001, xf, xf + 0.0055, "housing"),
                  _ring(f"lamp_tail_{side}1", yc, z, r * 0.55 + 0.0005, r - 0.0065, xf + 0.001, xf + 0.0055, "lamp_tail"),
                  _cyl(f"lamp_brake_{side}", yc, z, r * 0.55, xf + 0.001, xf + 0.0055, "lamp_brake")]
    return cut, parts


# ---------------------------------------------------------------- front
INTAKE_BARS = 0.55      # the two bars reach out this far from the middle
BAR_Z = 0.62            # and sit this far up the intake


def intake_shape():
    """The wide lower intake seen from ahead, both halves, corners rounded."""
    g = poly(R["intake"])
    return soft(g.union(flip(g)), 0.03)


def front_parts(ref, cutters, out):
    fr = front_fr()
    g = intake_shape()
    cutters += blend.lay(ref, "cut_intake", g, fr, -0.045, 0.006, "grille", 0.012, along_ray=True)
    out += blend.lay(ref, "intake_back", g.buffer(-0.002), fr, -0.039, -0.035, "grille", 0.012, along_ray=True)
    b = g.bounds
    zb = b[1] + BAR_Z * (b[3] - b[1])
    for sgn, side in ((1, "l"), (-1, "r")):
        # a bar from beside the middle out to the side, curving down at its outer end
        pts = [(0.06, zb), (0.25, zb), (0.42, zb - 0.005), (INTAKE_BARS, zb - 0.035)]
        line = LineString(geom.catmull_open(pts, 8)).buffer(0.0085, cap_style=1)
        bar = sscale(line, sgn, 1, origin=(0, 0)).intersection(g.buffer(-0.012))
        out += blend.lay(ref, f"intake_bar_{side}", bar, fr, -0.0345, -0.012, "dark_metal", 0.006, along_ray=True)


# ---------------------------------------------------------------- glass and trim
MIRROR_X0, MIRROR_X1 = R["mirror"]["x_back"], R["mirror"]["x_front"]
GLASS_BAND = 0.014    # black band round each window
BONNET_GAP = 0.03     # the bonnet line keeps this far from the head lamps


def plain_glass(pts, lo, hi, r=0.05):
    """A glass outline rebuilt from three plain fits to the measured one: the low edge and the
    high edge as even curves across the car, and the sides as straight lines. pts are
    (along, across); lo and hi pick the low and high edge points by their along value."""
    p = np.asarray(pts, float)
    a, c = p[:, 0], np.abs(p[:, 1])
    w = c.max()
    sym = lambda m: (np.concatenate([p[m, 1], -p[m, 1]]), np.concatenate([a[m], a[m]]))
    k_lo = np.polyfit(*sym(lo(a) & (c < 0.8 * w)), 2)
    k_hi = np.polyfit(*sym(hi(a) & (c < 0.8 * w)), 2)
    side = (c > 0.85 * w) & ~lo(a) & ~hi(a)
    k_s = np.polyfit(a[side], c[side], 1)
    ys = np.linspace(-1.2 * w, 1.2 * w, 61)
    band = Polygon(np.vstack([np.column_stack([np.polyval(k_lo, ys), ys]),
                              np.column_stack([np.polyval(k_hi, ys), ys])[::-1]]))
    us = np.linspace(a.min() - 0.1, a.max() + 0.1, 41)
    sides = Polygon(np.vstack([np.column_stack([us, np.polyval(k_s, us)]),
                               np.column_stack([us, -np.polyval(k_s, us)])[::-1]]))
    hull = MultiPoint(np.vstack([p, p * [1, -1]])).convex_hull     # never wider than the measured glass
    return soft(band.intersection(sides).intersection(hull), r)


def dlo_line():
    """The side window: its measured outline, its bottom refit as one straight line and its top
    as one even curve, meeting at the front and rear tips."""
    d = np.array(R["dlo"], float)
    x0, x1 = d[:, 0].min(), d[:, 0].max()
    mid = (d[:, 0] > x0 + 0.12) & (d[:, 0] < x1 - 0.12)
    zc = np.interp(d[:, 0], [x0, x1], [d[d[:, 0].argmin(), 1], d[d[:, 0].argmax(), 1]])
    lo = mid & (d[:, 1] < zc)
    hi = mid & (d[:, 1] >= zc)
    k_lo = np.polyfit(d[lo, 0], d[lo, 1], 1)
    k_hi = np.polyfit(d[hi, 0], d[hi, 1], 3)
    xs = np.linspace(x0, x1, 80)
    top = np.polyval(k_hi, xs)
    bot = np.polyval(k_lo, xs)
    pinch = np.sqrt(np.clip(np.minimum(xs - x0, x1 - xs) / 0.10, 0, 1))      # meet at the tips
    m = (top + bot) / 2
    top, bot = m + (top - m) * pinch, m + (bot - m) * pinch
    return np.vstack([np.column_stack([xs, bot]), np.column_stack([xs, top])[::-1]]), k_lo


def belt(x):
    return float(np.polyval(dlo_line()[1], x))


def dlo_shapes():
    dlo = soft(poly(dlo_line()[0]), 0.02)
    glass = dlo.buffer(-0.022, join_style=1)
    return dlo, glass, dlo.difference(glass.buffer(-0.005))


def screen_shapes():
    s = R["screen"]
    span = s["x_base"] - s["x_top"]
    outline = plain_glass(s["outline"], lambda a: a > s["x_base"] - 0.2 * span, lambda a: a < s["x_top"] + 0.2 * span)
    glass = outline.buffer(-GLASS_BAND, join_style=1)
    return outline, glass, outline.difference(glass.buffer(-0.005))


def screen_fr():
    s = R["screen"]
    return slope_fr(s["x_top"], body.z_top(s["x_top"]), s["x_base"], body.z_top(s["x_base"]))


ENGINE_GLASS_IN = 0.065    # the engine glass stops this far inside the engine cover's shut line


def rear_shapes():
    """The engine glass: the engine cover's own outline, inset, so the glass fills its panel."""
    e = soft(poly(R["engine_cover"]), 0.05)
    outline = soft(e.buffer(-ENGINE_GLASS_IN, join_style=1), 0.04)
    glass = outline.buffer(-GLASS_BAND, join_style=1)
    return outline, glass, outline.difference(glass.buffer(-0.005))


def rear_glass_fr():
    e = np.array(R["engine_cover"])
    x0, x1 = e[:, 0].min() + ENGINE_GLASS_IN, e[:, 0].max() - ENGINE_GLASS_IN
    return slope_fr(x0, body.z_top(x0), x1, body.z_top(x1))


def glass_parts(ref, cutters, out):
    _d, g, t = dlo_shapes()
    for sgn, side in ((1, "l"), (-1, "r")):
        fr = side_fr(sgn)
        cutters += blend.lay(ref, f"cut_side_{side}", g.buffer(-0.003), fr, -0.08, 0.006, "trim", along_ray=True)
        out += blend.lay(ref, f"side_glass_{side}", g.buffer(-0.006), fr, -0.008, -0.004, "glass", 0.02, along_ray=True)
        out += blend.lay(ref, f"window_trim_{side}", t, fr, 0.0015, 0.0035, "trim", 0.01)
    for name, shapes, frame in (("screen", screen_shapes(), screen_fr()), ("rear", rear_shapes(), rear_glass_fr())):
        fr, to_fr = frame
        _o, g, t = (to_fr(q) for q in shapes)
        cutters += blend.lay(ref, f"cut_{name}", g.buffer(-0.003), fr, -0.08, 0.006, "trim", along_ray=True)
        out += blend.lay(ref, f"{name}_glass", g.buffer(-0.006), fr, -0.008, -0.004, "glass", 0.02, along_ray=True)
        out += blend.lay(ref, f"{name}_trim", t, fr, 0.0015, 0.0035, "trim", 0.01)


# ---------------------------------------------------------------- shut lines
def _into_glass(p, d=0.025):
    """The point of the window opening nearest p, moved d into it, so a shut line ends on the
    black trim."""
    dlo = poly(dlo_line()[0])
    q = np.array(dlo.exterior.interpolate(dlo.exterior.project(Point(p))).coords[0])
    c = np.array(dlo.centroid.coords[0])
    return (q + (c - q) / np.linalg.norm(c - q) * d).tolist()


def door_path():
    """The door's outline, open where it runs under the window, its two ends carried into the
    window's black band."""
    d = np.array(soft(poly(R["door"]), 0.03).exterior.coords)[:-1]
    d = geom.resample(np.vstack([d, d[:1]]), 0.01)[:-1]
    near = poly(dlo_line()[0]).buffer(0.035)
    keep = np.array([not near.contains(Point(p)) for p in d])
    k = int(np.argmax(~keep & np.roll(keep, 1)))           # first dropped point after a kept one
    rolled, kr = np.roll(d, -k, axis=0), np.roll(keep, -k)
    path = rolled[kr]
    return [_into_glass(path[0])] + path.tolist() + [_into_glass(path[-1])]


def bonnet_path():
    """The bonnet shut line in plan: out of the windscreen trim at its lower corner, straight
    forward to the head lamp, along the lamp's inner edge as one straight fit, and across the
    nose, all through a few control points so the line runs as one smooth curve. The lid runs
    under the windscreen, so the line has no rear edge."""
    o, _g, _t = screen_shapes()
    c = np.array(o.exterior.coords)
    corner = c[np.argmax(c[:, 1] - 0.3 * c[:, 0])]            # the screen's lower side corner
    h = np.array(R["head"]).reshape(-1, 3)
    lamp = Polygon(h[:, :2]).convex_hull.buffer(BONNET_GAP)
    xs = np.linspace(h[:, 0].min() + 0.05, h[:, 0].max() - 0.02, 9)
    ys = [lamp.intersection(LineString([(x, 0), (x, 2)])).bounds[1] for x in xs]
    k = np.polyfit(xs, ys, 1)                                 # the lamp's inner edge, one line
    k[1] -= max(0.0, max(np.polyval(k, xs) - ys))             # kept clear of the lamp everywhere
    xa, xb = xs[0], xs[-1]
    side_y = min(corner[1] - 0.02, np.polyval(k, xa))
    xn = R["badge_front"][0] + 0.05
    xm = (xa + xb) / 2
    half = np.array([(corner[0] + 0.01, corner[1] - 0.01), ((corner[0] + xa) / 2, side_y),
                     (xa, np.polyval(k, xa) - 0.005), (xm + 0.12, np.polyval(k, xm + 0.12)),
                     (xn - 0.06, 0.42), (xn, 0.0)])
    pts = np.vstack([half, (half[-2::-1] * [1, -1])])
    from scipy.interpolate import splev, splprep
    tck, _u = splprep([pts[:, 0], pts[:, 1]], s=0.0015, k=3)
    x, y = splev(np.linspace(0, 1, 480), tck)
    q = np.column_stack([x, y])
    q = q[: int(np.argmax(q[:, 1] <= 0))]                   # one half, mirrored, so both sides match
    q = np.vstack([q, [[q[-1, 0], 0.0]]])
    return np.vstack([q, (q[-2::-1] * [1, -1])])


def engine_loop():
    """The engine cover in plan, round the rear glass."""
    e = soft(poly(R["engine_cover"]), 0.05)
    return np.array(e.simplify(0.002).exterior.coords)[:-1]


def grooves(ref):
    """Shut lines as V grooves to cut: doors, bonnet, engine cover. Each loop is one closed groove."""
    obs = []
    path = door_path()
    for sgn, side in ((1, "l"), (-1, "r")):
        obs.append(blend.groove(ref, f"g_door_{side}", path, side_fr(sgn)))
    obs.append(blend.groove(ref, "g_bonnet", bonnet_path(), top_fr()))
    obs.append(blend.groove(ref, "g_engine", engine_loop(), top_fr(), closed=True))
    return obs


# ---------------------------------------------------------------- mirrors and exhaust
MIRROR_H = 0.095     # housing height
MIRROR_W = 0.17      # housing width, out from the arm
MIRROR_D = 0.085     # housing depth front to back
MIRROR_R = 0.035     # corner rounding
MIRROR_SWEEP = 0.18  # radians the housing turns back from square to the car
ARM_X = 0.08         # the arm runs forward from the housing to the door by this much


def _rounded_bar(name, y0, y1, z0, z1, x0, x1, r, sgn, mat="body"):
    face = soft(box(y0, z0, y1, z1), r)
    ring = body.even(np.array(face.exterior.coords)[:-1], 96)
    rings = [np.column_stack([np.full(96, x), ring[:, 0] * sgn, ring[:, 1]]) for x in (x0, x1)]
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def _housing():
    m = R["mirror"]
    zc = m["z1"] - MIRROR_H / 2 - 0.01
    return m["y_out"] - MIRROR_W, m["y_out"], zc - MIRROR_H / 2, zc + MIRROR_H / 2


def _mirror_root(x, z):
    return body.y_at(x, z) - 0.02      # sunk into the door


def _sweep(ob, sgn):
    from mathutils import Matrix
    m = R["mirror"]
    y0, _y1, _z0, _z1 = _housing()
    p = Matrix.Translation((m["x_back"] + MIRROR_D / 2, sgn * y0, 0))
    ob.data.transform(p @ Matrix.Rotation(sgn * MIRROR_SWEEP, 4, "Z") @ p.inverted())
    return ob


def _arm(name, sgn, xa, za, y_end):
    """A slim stalk from under the housing, forward and down into the door below the window."""
    x1 = xa + ARM_X
    z1 = belt(x1) - 0.06
    root = _mirror_root(x1, z1)
    face = soft(box(-0.014, -0.011, 0.014, 0.011), 0.008)
    ring = body.even(np.array(face.exterior.coords)[:-1], 32)
    rings = []
    for t in np.linspace(0, 1, 10):
        x = xa - 0.02 + (ARM_X + 0.04) * t
        s = np.clip((x - xa) / ARM_X, 0, 1)
        s = s * s * (3 - 2 * s)
        yc = y_end + (root - y_end) * s
        zc = za + (z1 - za) * s
        rings.append(np.column_stack([np.full(32, x), (yc + ring[:, 0]) * sgn, zc + ring[:, 1]]))
    if sgn < 0:
        rings = [r[::-1] for r in rings]
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, "body")


def mirrors():
    """A rounded housing on a slim stalk reaching forward and down into the door."""
    m = R["mirror"]
    y0, y1, z0, z1 = _housing()
    xb = m["x_back"]
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        house = _sweep(_rounded_bar(f"mirror_{side}", y0, y1, z0, z1, xb, xb + MIRROR_D, MIRROR_R, sgn), sgn)
        arm = _arm(f"mirror_arm_{side}", sgn, xb + MIRROR_D / 2, z0 + 0.015, y0 + 0.05)
        blend.boolean(house, arm, "UNION")
        obs.append(house)
    return obs


def mirror_recess():
    """A shallow recess in each housing's back, with the glass inside."""
    m = R["mirror"]
    y0, y1, z0, z1 = _housing()
    cut, glass = [], []
    for sgn, side in ((1, "l"), (-1, "r")):
        cut.append(_sweep(_rounded_bar(f"cut_mirror_{side}", y0 + 0.01, y1 - 0.01, z0 + 0.01, z1 - 0.01,
                                       m["x_back"] - 0.03, m["x_back"] + 0.006, MIRROR_R - 0.01, sgn, "trim"), sgn))
        glass.append(_sweep(_rounded_bar(f"mirror_glass_{side}", y0 + 0.0115, y1 - 0.0115, z0 + 0.0115, z1 - 0.0115,
                                         m["x_back"] + 0.002, m["x_back"] + 0.005, MIRROR_R - 0.0115, sgn, "mirror_glass"), sgn))
    return cut, glass


def _tail_x(y, z):
    """The rearmost x where the skin reaches out to (y, z)."""
    for x in np.arange(TAIL, TAIL + 0.5, 0.004):
        if body.y_at(x, z) >= abs(y):
            return float(x)
    return TAIL


def exhaust():
    """Three round tips side by side in one black pocket in the middle."""
    tips = R["exhaust"]
    z = tips[1][2]
    r = max(t[3] for t in tips)
    y_out = tips[-1][1] + r
    x_end = _tail_x(y_out, z) - 0.015
    pocket = soft(box(-y_out - 0.014, z - r - 0.014, y_out + 0.014, z + r + 0.014), r + 0.01)
    ring = body.even(np.array(pocket.exterior.coords)[:-1], 128)
    rings = [np.column_stack([np.full(128, x), ring[:, 0], ring[:, 1]]) for x in (x_end - 0.3, x_end + 0.08)]
    v, f = geom.close_rings(rings)
    cut = [blend.make("cut_exhaust", v, f, "trim")]
    pipes = []
    for i, (_x, y, _z, _r) in enumerate(tips):
        outer = np.column_stack([y + (r - 0.003) * np.cos(t := np.linspace(0, 2 * np.pi, 48, endpoint=False)), z + (r - 0.003) * np.sin(t)])
        inner = np.column_stack([y + (r - 0.008) * np.cos(t), z + (r - 0.008) * np.sin(t)])
        rings = []
        for x, rg in ((x_end, inner), (x_end, outer), (x_end + 0.077, outer), (x_end + 0.077, inner)):
            rings.append(np.column_stack([np.full(len(rg), x), rg[:, 0], rg[:, 1]]))
        v, f = geom.close_rings(rings, cap=False)
        m = len(rings[0])
        for j in range(m):                     # close the tube: inner wall back to the first ring
            k = (j + 1) % m
            f.append((3 * m + j, 3 * m + k, k, j))
        pipes.append(blend.make(f"exhaust_{i}", v, f, "chrome"))
    return cut, pipes, pocket


DIFFUSER_Z0 = 0.20     # below this the tail turns under the car


def rear_parts(ref, out, pocket):
    """The black panels beside the tail lamps and the diffuser, raised on the tail, each along its
    own ray so a curved panel keeps an even lift. Returns no cutters."""
    fr = rear_fr()
    _x, y, z, r = R["tail_lamp"]
    for p in R["rear_panels"]:
        g = soft(poly(p), 0.01)
        for sgn, side in ((1, "l"), (-1, "r")):
            q = g if sgn > 0 else flip(g)
            q = q.difference(Point(y * sgn, z).buffer(r + TAIL_POD + 0.008, 64))
            out += blend.lay(ref, f"rear_panel_{side}", q, fr, 0.0015, 0.0035, "grille", 0.008)
    d = soft(poly(R["diffuser"]), 0.02).difference(pocket.buffer(0.01))
    d = soft(d.intersection(box(-2, DIFFUSER_Z0, 2, 2)), 0.01)     # only where the tail faces back
    out += blend.lay(ref, "diffuser", d, fr, 0.002, 0.0045, "grille", 0.005, along_ray=True)
    return []


def arches():
    """Arch cutters and the wheel tubs kept out of the cabin."""
    cut, tubs = [], []
    for x, wr, ar in ((W["front_x"], W["r_f"], W["arch_f"]), (W["rear_x"], W["r_r"], W["arch_r"])):
        for sgn in (1, -1):
            v, f = geom.disc((x, 0.86 * sgn, wr), (0, 1, 0), ar, 0.56, n=96)
            cut.append(blend.make("arch", v, f, "interior"))
            v, f = geom.disc((x, 0.88 * sgn, wr), (0, 1, 0), ar + 0.035, 0.70, n=96)
            tubs.append(blend.make("tub", v, f, "interior"))
    return cut, tubs


# ---------------------------------------------------------------- interior
def _loft(name, x0, x1, clip, n=12, gap=0.003, mat="interior"):
    rings = []
    for x in np.linspace(x0, x1, n):
        g = body.inside(x, 0.035 + gap).intersection(clip(x))
        if g.geom_type != "Polygon":
            g = max(getattr(g, "geoms", [g]), key=lambda q: q.area)
        ring = body.even(np.array(g.exterior.coords)[:-1], 64)
        rings.append(np.column_stack([np.full(64, x), ring]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def _rbox(name, x0, x1, y0, y1, z0, z1, r=0.03, lean=0.0, mat="interior"):
    sh = soft(box(y0, z0, y1, z1), r)
    ring = body.even(np.array(sh.exterior.coords)[:-1], 48)
    rings = []
    for x in (x0, x0 + 0.015, x1 - 0.015, x1):
        k = 0.92 if x in (x0, x1) else 1.0
        cy, cz = (y0 + y1) / 2, (z0 + z1) / 2
        rr = np.column_stack([cy + (ring[:, 0] - cy) * k, cz + (ring[:, 1] - cz) * k])
        rings.append(np.column_stack([x - lean * (rr[:, 1] - z0), rr]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


CABIN_BACK = -0.52      # the bulkhead behind the seats; the engine sits behind it


def interior(floor):
    x0 = R["screen"]["x_base"] - 0.05
    dash_top = 0.74
    obs = [_loft("firewall", x0 - 0.010, x0 - 0.004, lambda x: box(-0.5, floor + 0.002, 0.5, 3), n=2),
           _loft("dash", x0 - 0.0125, 0.70, lambda x: box(-0.5, 0.60, 0.5, dash_top + 0.10 * (1 - (x0 - x) / 0.7)), n=10)]
    belt_z = belt(0.2)
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, floor + 0.08, 1.2, belt_z - 0.02) if s > 0 else box(-1.2, floor + 0.08, -0.3, belt_z - 0.02)).intersection(
            body.inside(x, 0.035 + 0.003)).difference(body.inside(x, 0.035 + 0.025))
        obs.append(_loft(f"door_card_{side}", CABIN_BACK + 0.25, 0.68, clip, n=14))
        yc = 0.30 * sgn
        obs.append(_rbox(f"seat_base_{side}", -0.35, 0.15, yc - 0.22, yc + 0.22, floor + 0.002, 0.36))
        obs.append(_rbox(f"seat_back_{side}", -0.43, -0.31, yc - 0.21, yc + 0.21, 0.361, 0.92, lean=0.12))
    xm = R["screen"]["x_top"] - 0.03
    roof_in = body.inside(xm, 0.035).bounds[3]
    top = roof_in - 0.06
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.11, 0.11, top - 0.05, top, r=0.02)
    stem = _rbox("stem", xm - 0.016, xm - 0.004, -0.012, 0.012, top - 0.015, roof_in - 0.0008, r=0.004)
    obs.append(blend.union_all([m, stem], "mirror_rear"))
    return obs


# ---------------------------------------------------------------- engine
BAY_FLOOR = 0.42        # the engine bay's floor under the glass


def bay_ends():
    o, _g, _t = rear_shapes()
    b = o.bounds
    return b[0] - 0.02, min(b[2] + 0.02, CABIN_BACK - 0.03)


def engine():
    """Our own mid V8 under the glass: a dark block, a red cam cover on each bank, and a
    metal intake plenum with a row of runners down each side."""
    x0, x1 = bay_ends()
    x0, x1 = x0 + 0.12, x1 - 0.08
    obs = [_rbox("engine_block", x0, x1, -0.24, 0.24, BAY_FLOOR + 0.003, 0.66, r=0.04, mat="dark_metal")]
    for sgn, side in ((1, "l"), (-1, "r")):
        y = 0.19 * sgn
        obs.append(_rbox(f"engine_cam_{side}", x0 + 0.03, x1 - 0.03, y - 0.065, y + 0.065, 0.662, 0.74, r=0.03, mat="engine_red"))
        lo, hi = sorted((0.0856 * sgn, 0.1244 * sgn))     # clear of the plenum and cam cover
        for i, x in enumerate(np.linspace(x0 + 0.10, x1 - 0.10, 4)):
            obs.append(_rbox(f"engine_runner_{side}{i}", x - 0.025, x + 0.025, lo, hi, 0.70, 0.765, r=0.012, mat="engine_metal"))
    obs.append(_rbox("engine_plenum", x0 + 0.06, x1 - 0.06, -0.085, 0.085, 0.662, 0.80, r=0.04, mat="engine_metal"))
    return obs


# ---------------------------------------------------------------- wheels
def wheels():
    """Plain ten-spoke 20 inch wheels: five pairs of slim spokes. Wider tyres at the back."""
    obs = []
    rr = W["rim_r"]
    for x, fb, wr, width in ((W["front_x"], "f", W["r_f"], W["width_f"]), (W["rear_x"], "r", W["r_r"], W["width_r"])):
        hw = width / 2
        yc = W["outer_y"] - hw
        tyre = soft(box(rr, -hw, wr, hw), 0.03)
        tyre = tyre.union(box(rr, -hw + 0.012, rr + 0.03, hw - 0.012))
        tp = body.even(np.array(tyre.exterior.coords)[:-1], 96)
        rim = [(rr - 0.001, -hw + 0.012), (rr - 0.001, hw - 0.01), (rr - 0.012, hw - 0.002),
               (rr - 0.024, hw - 0.006), (rr - 0.024, -hw + 0.012)]
        for sgn, lr in ((1, "l"), (-1, "r")):
            def place(v, s=sgn, x=x, yc=yc, wr=wr):
                v = np.asarray(v, float)
                return np.column_stack([x + v[:, 0], (yc + v[:, 1]) * s, wr + v[:, 2]])
            v, f = geom.lathe(tp, 96, axis="y")
            obs.append(blend.make(f"wheel_tyre_{fb}{lr}", place(v), f, "tyre"))
            v, f = geom.lathe(rim, 96, axis="y")
            parts = [blend.make("rim", place(v), f, "rim")]
            v, f = geom.disc((0, hw - 0.055, 0), (0, 1, 0), 0.075, 0.05, n=48)
            parts.append(blend.make("hub", place(v), f, "rim"))
            for k in range(10):
                a = 2 * np.pi * (k // 2) / 5 + (0.11 if k % 2 else -0.11)
                ca, sa = np.cos(a), np.sin(a)
                pts = []
                for r, w2, h0, h1 in ((0.06, 0.012, hw - 0.075, hw - 0.03), (rr - 0.018, 0.011, hw - 0.07, hw - 0.022)):
                    for dw in (-w2, w2):
                        for hh in (h0, h1):
                            pts.append((r * ca - dw * sa, hh, r * sa + dw * ca))
                v = np.array(pts)
                f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
                parts.append(blend.make("spoke", place(v), f, "rim"))
            obs.append(blend.union_all(parts, f"wheel_rim_{fb}{lr}"))
    return obs


# ---------------------------------------------------------------- badge and name
BRAND = HERE.parent.parent.parent / "src/renderer/assets/brands/feretti"


def _transform(g, t):
    """Apply an SVG transform attribute to a shapely shape."""
    for name, args in re.findall(r"(\w+)\(([^)]*)\)", t or "")[::-1]:
        a = [float(v) for v in re.split(r"[ ,]+", args.strip())]
        if name == "translate":
            g = translate(g, a[0], a[1] if len(a) > 1 else 0)
        elif name == "scale":
            g = sscale(g, a[0], a[1] if len(a) > 1 else a[0], origin=(0, 0))
        elif name == "rotate":
            g = affinity.rotate(g, a[0], origin=(a[1], a[2]) if len(a) > 2 else (0, 0))
    return g


def _path(d):
    """M, L, Q and Z path data as a polygon (curves sampled)."""
    pts, cur = [], (0.0, 0.0)
    for cmd, args in re.findall(r"([MLQZ])([^MLQZ]*)", d):
        v = [float(t) for t in re.findall(r"-?[\d.]+", args)]
        if cmd in "ML":
            for i in range(0, len(v), 2):
                cur = (v[i], v[i + 1]); pts.append(cur)
        elif cmd == "Q":
            for i in range(0, len(v), 4):
                c, e = np.array(v[i:i + 2]), np.array(v[i + 2:i + 4])
                for t in np.linspace(0, 1, 12)[1:]:
                    p = (1 - t) ** 2 * np.array(cur) + 2 * (1 - t) * t * c + t ** 2 * e
                    pts.append(tuple(p))
                cur = tuple(e)
    return Polygon(pts).buffer(0)


def _svg_shapes(name):
    """Every filled or stroked shape in a brand SVG, in drawing order: [(shape, fill)]. A stroke
    becomes its own ring, named by the stroke colour."""
    svg = re.sub(r"<(metadata|defs)\b.*?</\1>", "", (BRAND / name).read_text(), flags=re.S)
    out, stack = [], [""]
    for m in re.finditer(r"<(/?)(\w+)([^>]*?)(/?)>", svg):
        close, tag, attrs, selfclose = m.groups()
        a = dict(re.findall(r'([\w-]+)="([^"]*)"', attrs))
        if tag == "g":
            if close:
                stack.pop()
            elif not selfclose:
                stack.append(stack[-1] + " " + a.get("transform", ""))
            continue
        if close or tag == "svg":
            continue
        if tag == "ellipse":
            g = sscale(Point(0, 0).buffer(1, 32), float(a["rx"]), float(a["ry"]), origin=(0, 0))
            g = translate(g, float(a["cx"]), float(a["cy"]))
        elif tag == "circle":
            g = translate(Point(0, 0).buffer(float(a["r"]), 96), float(a["cx"]), float(a["cy"]))
        elif tag == "rect":
            x, y, w, h = (float(a[k]) for k in ("x", "y", "width", "height"))
            g = box(x, y, x + w, y + h)
            if "rx" in a:
                g = soft(g, float(a["rx"]) * 0.999)
        elif tag == "path":
            g = _path(a["d"])
        else:
            continue
        g = _transform(_transform(g, a.get("transform")), stack[-1])
        stroke = float(a.get("stroke-width", 0) or 0)
        if a.get("fill", "") != "none":
            out.append((g, a.get("fill", "")))
        if stroke and a.get("stroke"):
            if tag == "circle":
                ring = g.buffer(stroke / 2).difference(g.buffer(-stroke / 2))
            else:
                ring = g.boundary.buffer(stroke / 2)
            out.append((ring, a["stroke"]))
    return out


def _fit(shapes, ref):
    """Flip y up and scale so ref is 1 unit tall, centred."""
    b = ref.bounds
    k = 1 / (b[3] - b[1])
    return [sscale(translate(sscale(g, 1, -1, origin=(0, 0)), -(b[0] + b[2]) / 2, (b[1] + b[3]) / 2), k, k, origin=(0, 0))
            for g in shapes]


def _layered(shapes):
    """Painter's order to flat pieces: each shape keeps only what later shapes leave uncovered."""
    out = []
    for i, (g, f) in enumerate(shapes):
        over = unary_union([q for q, _f in shapes[i + 1:]]) if i + 1 < len(shapes) else None
        g = g.difference(over.buffer(0.6)) if over is not None else g
        if not g.is_empty and g.area > 0.5:
            out.append((g, f))
    return out


def badge_chrome():
    """The Feretti badge from feretti-badge-chrome.svg: laurels, ring, tower and bar in chrome,
    with the tower's door and window cut through."""
    sh = _svg_shapes("feretti-badge-chrome.svg")
    chrome = unary_union([g for g, f in sh if f.startswith("url")])
    dark = unary_union([g for g, f in sh if not f.startswith("url")])
    whole = chrome.difference(dark)
    return _fit([whole], unary_union([g for g, _f in sh]))[0].simplify(0.003)


COLOURS = {"#c9a227": "badge_gold", "#c8102e": "badge_red", "#141414": "badge_black",
           "#f4eee3": "badge_cream", "#00894b": "badge_green"}


def badge_colour():
    """The Feretti badge from feretti-badge-colour.svg: [(shape, material)], one flat piece per
    colour, none overlapping."""
    sh = _svg_shapes("feretti-badge-colour.svg")
    pieces = _layered(sh)
    ref = unary_union([g for g, _f in sh])
    shapes = _fit([g for g, _f in pieces], ref)
    by = {}
    for g, (_q, f) in zip(shapes, pieces):
        mat = COLOURS[f.lower()]
        by[mat] = by.get(mat, Polygon()).union(g)
    return [(g.simplify(0.003).buffer(-0.004), m) for m, g in by.items()]     # the colours stand just apart


def name_shape():
    gl = json.loads((HERE / "glyphs.json").read_text())
    g = unary_union([Polygon(p[0], p[1:]) for p in gl["pieces"]])
    b = g.bounds
    g = translate(g, -(b[0] + b[2]) / 2, -gl["cap"] / 2)
    g = sscale(g, 1 / gl["cap"], 1 / gl["cap"], origin=(0, 0))
    return g.buffer(0.02, 8).simplify(0.004)    # the brand's wordmark carries a heavier stroke than the bare font


def _flat(ref, g, size, fr, a, b, up, name, d0, d1, mat, step=0.004):
    """Lay shape g (right, up, 1 unit = size) flat on the panel that fr's ray at (a, b) meets."""
    pf = blend.panel_frame(ref, fr, a, b, up)
    return blend.lay(ref, name, sscale(g, size, size, origin=(0, 0)), pf, d0, d1, mat, step)


FRONT_BADGE_T = 0.010
REAR_BADGE_T = 0.010
NAME_T = 0.004


def badge(ref, style, size, fr, a, b, up, name, depth, lift=0.0):
    if style == "chrome":
        return _flat(ref, badge_chrome(), size, fr, a, b, up, name, 0.0012 + lift, depth + lift, "chrome", 0.003)
    obs = []
    for i, (g, m) in enumerate(badge_colour()):
        obs += _flat(ref, g, size, fr, a, b, up, f"{name}_{i}", 0.0012 + lift, depth + lift, m, 0.003)
    return obs


def options(ref):
    """Badge and name options, named opt_<group>_<n>_<i>. Each lies flat on its panel.
    Odd numbers are the chrome badge, even the colour badge, at the same spot."""
    nm = name_shape()
    up, back = (0, 0, 1), (-1, 0, 0)     # a bonnet badge reads from ahead: its top faces the screen
    out = []

    def add(group, n, objs):
        for i, ob in enumerate(objs):
            ob.name = f"opt_{group}_{n}_{i}"
            out.append(ob)
    ib = intake_shape().bounds
    _x, ty, tz, tr = R["tail_lamp"]
    bx, _by, bz, _br = R["badge_front"]
    for k, style in ((0, "chrome"), (1, "colour")):
        # front: in the middle of the intake (the reference), or on the bonnet's nose (the
        # reference's small shield)
        add("frontbadge", 1 + k, badge(ref, style, 0.075, front_fr(), 0, (ib[1] + ib[3]) / 2, up, "fb", FRONT_BADGE_T, lift=-0.03))
        add("frontbadge", 3 + k, badge(ref, style, 0.075, top_fr(), bx - 0.03, 0, back, "fb", FRONT_BADGE_T))
        # rear: between the tail lamps (the reference), or low on the tail above the exhaust
        add("rearbadge", 1 + k, badge(ref, style, 0.075, rear_fr(), 0, tz - 0.02, up, "rb", REAR_BADGE_T))
        add("rearbadge", 3 + k, badge(ref, style, 0.06, rear_fr(), 0, 0.50, up, "rb", REAR_BADGE_T))
    cap = 0.05
    add("name", 1, _flat(ref, nm, cap, rear_fr(), -0.42, tz - 0.17, up, "nm", 0.0012, NAME_T, "chrome"))
    add("name", 2, _flat(ref, nm, cap, rear_fr(), 0, tz + 0.06, up, "nm", 0.0012, NAME_T, "chrome"))
    q = []
    for sgn in (1, -1):
        q += _flat(ref, nm, cap, side_fr(sgn), W["front_x"] - W["arch_f"] - 0.16, 0.55, up, "nm", 0.0012, NAME_T, "chrome")
    add("name", 3, q)
    add("name", 4, _flat(ref, nm, cap, rear_fr(), 0, tz - 0.115, up, "nm", 0.0012, NAME_T, "chrome"))   # under rear badge 1 and 2
    return out
