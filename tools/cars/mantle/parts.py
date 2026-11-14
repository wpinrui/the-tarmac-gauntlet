"""The Mantle's parts: lamps, grille, intakes, glass, shut lines, front doors, rear door shut lines,
the boot lid's shut line, mirrors, interior, five-spoke wheels, badge and name. Every shape and size
here is the Mantle's own, from rows.json, kept plain and smooth. Runs inside Blender's Python
module, called by export.py."""
import json
import re
import sys
from pathlib import Path

import numpy as np
from shapely.affinity import rotate, scale as sscale, translate
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

MATERIALS = {
    # name: (rgb, spec, alpha, emit)
    "body": ((0.98, 0.58, 0.04), 0.75, 1.0, None),
    "trim": ((0.025, 0.025, 0.028), 0.45, 1.0, None),
    "grille": ((0.03, 0.03, 0.035), 0.2, 1.0, None),
    "glass": ((0.06, 0.08, 0.1), 0.92, 0.42, None),
    "head_glass": ((0.92, 0.94, 0.96), 0.95, 0.2, None),
    "tail_glass": ((0.55, 0.05, 0.06), 0.92, 0.55, None),
    "housing": ((0.05, 0.05, 0.055), 0.5, 1.0, None),
    "reflector": ((0.82, 0.83, 0.85), 0.97, 1.0, None),
    "lamp_head": ((0.95, 0.95, 0.93), 0.6, 1.0, (1.0, 0.98, 0.92)),
    "lamp_tail": ((0.5, 0.03, 0.04), 0.5, 1.0, (1.0, 0.06, 0.04)),
    "lamp_brake": ((0.6, 0.04, 0.05), 0.5, 1.0, (1.0, 0.04, 0.03)),
    "lamp_rain": ((0.55, 0.04, 0.05), 0.5, 1.0, (1.0, 0.05, 0.03)),
    "chrome": ((0.86, 0.87, 0.89), 0.98, 1.0, None),
    "badge_dark": ((0.07, 0.08, 0.1), 0.6, 1.0, None),
    "interior": ((0.075, 0.075, 0.085), 0.15, 1.0, None),
    "seat": ((0.11, 0.11, 0.12), 0.2, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.72, 0.73, 0.75), 0.75, 1.0, None),
    "dark_metal": ((0.14, 0.14, 0.15), 0.8, 1.0, None),
}
LAMPS = {"Headlights": ["lamp_head"], "Tail lights": ["lamp_tail"], "Brake lights": ["lamp_brake"],
         "Rain light": ["lamp_rain"]}
GROUPS = {"frontbadge": "Front badge", "rearbadge": "Rear badge", "name": "Name"}
SHOW = {}


# ---------------------------------------------------------------- frames
def front_fr():
    return blend.frame((NOSE, 0, 0), (0, 1, 0), (0, 0, 1), (-1, 0, 0))


def rear_fr():
    return blend.frame((TAIL, 0, 0), (0, 1, 0), (0, 0, 1), (1, 0, 0))


def side_fr(sgn):
    return blend.frame((0, 1.2 * sgn, 0), (1, 0, 0), (0, 0, 1), (0, -sgn, 0))


def top_fr():
    return blend.frame((0, 0, 1.8), (1, 0, 0), (0, 1, 0), (0, 0, -1))


def poly(pts):
    return Polygon(pts).buffer(0)


def flip(g):
    """Mirror a 2D shape about its first axis (y for front and rear frames)."""
    return sscale(g, -1, 1, origin=(0, 0))


def both(g):
    return unary_union([g, flip(g)])


def soft(g, r):
    return g.buffer(-r, join_style=1).buffer(r, join_style=1)


def biggest(g):
    return max(getattr(g, "geoms", [g]), key=lambda q: q.area)


def smooth_outline(pts, n=240):
    """A closed outline traced as points, rebuilt as one smooth periodic curve with n points."""
    from scipy.interpolate import splev, splprep
    p = np.array(pts, float)
    p = np.vstack([p, p[:1]])
    tck, _u = splprep([p[:, 0], p[:, 1]], s=len(p) * 2e-6, k=3, per=True)
    return Polygon(np.column_stack(splev(np.linspace(0, 1, n, endpoint=False), tck))).buffer(0)


def half_shape(pts, r=0.004):
    """A shape traced as its left half from the centre line, joined to its mirror."""
    return soft(both(poly([(0.0, pts[0][1])] + [tuple(p) for p in pts] + [(0.0, pts[-1][1])])), r)


def slope_fr(x0, z0, x1, z1):
    """A frame square to a panel sloping from (x0, z0) to (x1, z1) along the car, and a map from
    the top view (x, y) into it, so a shape drawn from above keeps its width once laid."""
    t = np.array([x1 - x0, 0.0, z1 - z0]); t /= np.linalg.norm(t)
    d = np.array([t[2], 0.0, -t[0]])
    if d[2] > 0:
        d = -d
    fr = blend.frame(np.array([x0, 0, z0]) - d * 0.3, t, (0, 1, 0), d)
    return fr, lambda g: sscale(translate(g, -x0, 0), 1 / t[0], 1, origin=(0, 0))


_BELT = np.array(R["belt"], float)


def belt(x):
    return float(np.interp(x, _BELT[::-1, 0], _BELT[::-1, 1]))


def glass_fr(sgn):
    """A frame square to the side glass, which leans in from its foot to the roof edge, and a
    map from the side view (x, z) into it."""
    v = body.values(-0.3)
    y0, z0, y1, z1 = v["wg"], v["zs"], v["wr"], v["zr"]
    a = np.arctan2(y0 - y1, z1 - z0)
    up = np.array([0.0, -np.sin(a) * sgn, np.cos(a)])
    out = np.array([0.0, np.cos(a) * sgn, np.sin(a)])
    fr = blend.frame(np.array([0.0, y0 * sgn, z0]) + out * 0.3, (1, 0, 0), up, -out)
    return fr, lambda g: sscale(translate(g, 0, -z0), 1, 1 / np.cos(a), origin=(0, 0))


def _recess(name, shape, mat, ref, cutters, out, fr, depth, sides=True):
    """A recess cut along fr's rays with a dark back, mirrored to both sides when sides."""
    for sgn, side in (((1, "l"), (-1, "r")) if sides else ((1, ""),)):
        g = shape if sgn > 0 else flip(shape)
        k = f"_{side}" if side else ""
        cutters += blend.lay(ref, f"cut_{name}{k}", g, fr, -depth, 0.006, mat, 0.006, along_ray=True)
        out += blend.lay(ref, f"{name}_back{k}", g.buffer(-0.002), fr, -depth + 0.001, -depth + 0.005, mat, 0.006,
                         along_ray=True)


# ---------------------------------------------------------------- lamps
def wrap_fr(axis, sgn, ahead, radius):
    """Rays fanning out from a vertical axis inside a corner, so a lamp can wrap from the nose or
    tail round onto the side. (a, b): a is the angle from straight ahead (ahead 1) or straight
    back (ahead -1), times radius; b is z."""
    xa, ya = axis

    def fr(a, b):
        t = a / radius
        out = np.array([ahead * np.cos(t), np.sin(t) * sgn, 0.0])
        return np.array([xa, ya * sgn, b]) + out * 1.2, -out
    return fr


def wrap_outline(ref, axis, ahead, radius, end_pts, side_pts, end_top, end_fr):
    """A traced lamp outline found on the body (end view, side view, end view again), read in the
    wrap's coordinates and smoothed."""
    xa, ya = axis
    pts = [blend.cast(ref, end_fr, u, z)[0] for u, z in end_pts]
    pts += [blend.cast(ref, side_fr(1), x, z)[0] for x, z in side_pts]
    pts += [blend.cast(ref, end_fr, u, z)[0] for u, z in end_top]
    ab = [(np.arctan2(p[1] - ya, ahead * (p[0] - xa)) * radius, p[2]) for p in pts]
    return smooth_outline(ab, 300)


HEAD_R = 0.25        # the wrap's unrolled coordinate: metres round the corner at this radius
LAMP_STEP = 0.007    # the lamps are laid on this mesh; their outlines keep every point of their smooth curves


def headlamps(ref, cutters, out):
    """Each headlamp is the traced sweep from the grille's corner round onto the side. Our own
    interior: a slim light strip along the whole bottom edge, and three small square lenses in a
    row along the upper middle on a bright reflector."""
    shp = soft(wrap_outline(ref, R["head_axis"], 1, HEAD_R, R["head"], R["head_side"], R["head_top"],
                            front_fr()), 0.008)
    b = shp.bounds
    inner = shp.buffer(-0.007)
    strip = inner.difference(inner.buffer(-0.012).union(box(b[0] - 1, (b[1] + b[3]) / 2 - 0.004, b[2] + 1, b[3] + 1)))
    strip = soft(biggest(strip), 0.002)
    mid = (b[1] + b[3]) / 2 + 0.008
    span = b[2] - b[0]
    lenses = unary_union([soft(box(b[0] + span * f - 0.016, mid - 0.016, b[0] + span * f + 0.016, mid + 0.016), 0.004)
                          for f in (0.42, 0.56, 0.70)]).intersection(inner.buffer(-0.01))
    bowl = lenses.buffer(0.012).intersection(inner.buffer(-0.004))
    for sgn, side in ((1, "l"), (-1, "r")):
        fr = wrap_fr(R["head_axis"], sgn, 1, HEAD_R)
        cutters += blend.lay(ref, f"cut_head_{side}", shp, fr, -0.045, 0.006, "housing", LAMP_STEP, along_ray=True)
        out += blend.lay(ref, f"head_back_{side}", shp.buffer(-0.002), fr, -0.044, -0.040, "housing", LAMP_STEP,
                         along_ray=True)
        out += blend.lay(ref, f"head_reflector_{side}", bowl.difference(lenses.buffer(0.003)), fr, -0.0395, -0.033,
                         "reflector", LAMP_STEP, along_ray=True)
        out += blend.lay(ref, f"lamp_head_{side}0", lenses, fr, -0.0395, -0.031, "lamp_head", LAMP_STEP, along_ray=True)
        out += blend.lay(ref, f"lamp_head_{side}1", strip, fr, -0.0395, -0.034, "lamp_head", LAMP_STEP, along_ray=True)
        out += blend.lay(ref, f"head_glass_{side}", shp.buffer(-0.002), fr, -0.0055, -0.0025, "head_glass", LAMP_STEP,
                         along_ray=True)


TAIL_R = 0.25


def taillamps(ref, cutters, out):
    """Each tail lamp is the traced sweep from the boot lid round onto the side, split where the
    lid's side runs through it. Our own interior: the tail lamp a thin ring just inside the whole
    outline, the brake lamp one long bar through its middle."""
    shp = soft(wrap_outline(ref, R["tail_axis"], -1, TAIL_R, R["tail"], R["tail_side"], R["tail_top"], rear_fr()),
               0.008)
    b = shp.bounds
    inner = shp.buffer(-0.007)
    ring = soft(inner.difference(inner.buffer(-0.010)), 0.002)
    zc = (b[1] + b[3]) / 2
    bar = soft(inner.buffer(-0.022).intersection(box(b[0] - 1, zc - 0.014, b[2] + 1, zc + 0.014)), 0.004)
    bar = biggest(bar)
    # the boot lid's side runs straight through each lamp: split the lamp's glass and lights there
    xa, ya = R["tail_axis"]
    p, _n = blend.cast(ref, rear_fr(), R["lid_w"], (b[1] + b[3]) / 2)
    a = np.arctan2(p[1] - ya, -(p[0] - xa)) * TAIL_R
    gap = box(a - 0.004, -5, a + 0.004, 5)
    ring, bar = ring.difference(gap), bar.difference(gap)
    glass = shp.buffer(-0.002).difference(gap)
    for sgn, side in ((1, "l"), (-1, "r")):
        fr = wrap_fr(R["tail_axis"], sgn, -1, TAIL_R)
        cutters += blend.lay(ref, f"cut_tail_{side}", shp, fr, -0.035, 0.006, "housing", LAMP_STEP, along_ray=True)
        out += blend.lay(ref, f"tail_back_{side}", shp.buffer(-0.002), fr, -0.034, -0.03, "housing", LAMP_STEP,
                         along_ray=True)
        out += blend.lay(ref, f"lamp_tail_{side}", ring, fr, -0.029, -0.025, "lamp_tail", LAMP_STEP, along_ray=True)
        out += blend.lay(ref, f"lamp_brake_{side}", bar, fr, -0.029, -0.025, "lamp_brake", LAMP_STEP, along_ray=True)
        out += blend.lay(ref, f"tail_glass_{side}", glass, fr, -0.0055, -0.0025, "tail_glass", LAMP_STEP,
                         along_ray=True)


# ---------------------------------------------------------------- front and rear
def slats(shape, n, t=0.007):
    """n level bars across a shape, evenly spaced from top to bottom."""
    b = shape.bounds
    zs = np.linspace(b[1], b[3], n + 2)[1:-1]
    return unary_union([box(b[0] - 1, z - t / 2, b[2] + 1, z + t / 2) for z in zs]).intersection(shape.buffer(-0.0005))


def front_parts(ref, cutters, out):
    """The wide grille between the headlamps with four level bars, the lower intake across the
    bumper with one bar, and the black side intakes in the bumper's corners."""
    fr = front_fr()
    for name, n, depth in (("grille", 4, 0.045), ("intake", 1, 0.06)):
        sh = half_shape(R[name], 0.01)
        _recess(name, sh, "grille", ref, cutters, out, fr, depth, sides=False)
        out += blend.lay(ref, f"{name}_slat", slats(sh, n), fr, -depth + 0.006, -0.012, "trim", 0.006, along_ray=True)
    side = soft(poly(R["side_intake"]), 0.006)
    _recess("side_intake", side, "grille", ref, cutters, out, fr, 0.04)


def rear_parts(ref, cutters, out):
    """The dark band low across the bumper's back with the rain light in its middle."""
    fr = rear_fr()
    band = half_shape(R["band_r"], 0.008)
    _recess("band_r", band, "grille", ref, cutters, out, fr, 0.03, sides=False)
    u, z0, z1 = R["rain"]
    out += blend.lay(ref, "lamp_rain", soft(box(-u, z0, u, z1), 0.004), fr, -0.0245, -0.0205, "lamp_rain", 0.004,
                     along_ray=True)


# ---------------------------------------------------------------- glass and trim
SIDE_BAND = 0.016     # black frame round the side windows
GLASS_BAND = 0.026    # black band round the windscreen and the rear glass


def dlo_line():
    """The window opening rebuilt from plain fits: the A-pillar edge a straight line, the roof edge
    one even curve, the quarter window's rear end a straight line, and its foot the belt."""
    d = R["dlo"]
    roof = np.array(d["roof"], float)
    k = np.polyfit(roof[:, 0], roof[:, 1], 3)
    xs = np.linspace(roof[0, 0], roof[-1, 0], 40)
    top = np.column_stack([xs, np.polyval(k, xs)])           # front to back (x falling)
    a = np.array([d["a_foot"], d["a_head"]], float)
    c = np.array([d["c_head"], d["c_foot"]], float)
    fx = np.linspace(c[1, 0], a[0, 0], 40)
    foot = np.column_stack([fx, [belt(x) for x in fx]])
    return np.vstack([a[:1], top, c[1:], foot[1:-1]])


def pillar_line():
    """The black division between the doors' glass: its centre line, extended."""
    (x0, z0), (x1, z1) = R["b_pillar"]["foot"], R["b_pillar"]["head"]
    t = np.array([x1 - x0, z1 - z0]); t /= np.linalg.norm(t)
    return np.array([x0, z0]) - t * 0.5, np.array([x0, z0]) + t * 1.0


def ahead_of_pillar(off=0.0):
    """Everything ahead of the division's centre line (moved forward by off), seen from the side."""
    p0, p1 = pillar_line()
    p0 = p0 + [off, 0]; p1 = p1 + [off, 0]
    return Polygon([tuple(p0), tuple(p1), (p1[0] + 5, p1[1]), (p0[0] + 5, p0[1])])


def dlo_shapes():
    """The side window opening, its glass (front door glass, rear door glass and quarter window,
    split by the black division and the rear post, the front glass stopping short of the black sail
    under the mirror) and the black band."""
    dlo = soft(poly(dlo_line()), 0.025)
    glass = dlo.buffer(-SIDE_BAND, join_style=1)
    half = R["b_pillar"]["w"] / 2
    glass = glass.intersection(ahead_of_pillar(half)).union(glass.difference(ahead_of_pillar(-half)))
    (x0, z0), (x1, z1) = R["c_post"]["foot"], R["c_post"]["head"]
    t = np.array([x1 - x0, z1 - z0]); t /= np.linalg.norm(t)
    a, b = np.array([x0, z0]) - t * 0.3, np.array([x0, z0]) + t * 1.0
    post = LineString([tuple(a), tuple(b)]).buffer(R["c_post"]["w"] / 2, cap_style=2)
    glass = glass.difference(post)
    glass = glass.difference(box(R["vent_x"], -1, 5, 5))
    glass = unary_union([soft(p, 0.01) for p in getattr(glass, "geoms", [glass]) if p.area > 1e-4])
    return dlo, glass, dlo.difference(glass.buffer(-0.004))


def _curve_edge(x_mid, x_edge, w, n=25):
    """An even curve across the car in plan: x at the middle, bending to x_edge at half width w."""
    ys = np.linspace(-w, w, n)
    return [(x_mid + (x_edge - x_mid) * (y / w) ** 2, y) for y in ys]


def _plan_window(s):
    base = _curve_edge(s["x_base_mid"], s["x_base_edge"], s["w_base"])
    top = _curve_edge(s["x_top_mid"], s["x_top_edge"], s["w_top"])
    return poly(base + top[::-1])


def screen_shapes():
    o = soft(_plan_window(R["screen"]), 0.04)
    g = o.buffer(-GLASS_BAND, join_style=1)
    return o, g, o.difference(g.buffer(-0.004))


def rear_shapes():
    o = soft(_plan_window(R["rear_glass"]), 0.05)
    g = o.buffer(-GLASS_BAND, join_style=1)
    return o, g, o.difference(g.buffer(-0.004))


def screen_fr():
    s = R["screen"]
    return slope_fr(s["x_base_mid"], body.z_top(s["x_base_mid"]), s["x_top_mid"], body.z_top(s["x_top_mid"]))


def rear_glass_fr():
    s = R["rear_glass"]
    return slope_fr(s["x_base_mid"], body.z_top(s["x_base_mid"]), s["x_top_mid"], body.z_top(s["x_top_mid"]))


def glass_parts(ref, cutters, out):
    _d, g, t = dlo_shapes()
    for sgn, side in ((1, "l"), (-1, "r")):
        fr, to = glass_fr(sgn)
        cutters += blend.lay(ref, f"cut_side_{side}", to(g.buffer(-0.003)), fr, -0.08, 0.006, "trim", along_ray=True)
        out += blend.lay(ref, f"side_glass_{side}", to(g.buffer(-0.006)), fr, -0.008, -0.004, "glass", 0.02, along_ray=True)
        out += blend.lay(ref, f"window_trim_{side}", to(t), fr, 0.0012, 0.0032, "trim", 0.008, along_ray=True)
    for name, (fr, to), shapes in (("screen", screen_fr(), screen_shapes()), ("rear", rear_glass_fr(), rear_shapes())):
        _o, g, t = (to(q) for q in shapes)
        cutters += blend.lay(ref, f"cut_{name}", g.buffer(-0.003), fr, -0.08, 0.006, "trim", along_ray=True)
        out += blend.lay(ref, f"{name}_glass", g.buffer(-0.006), fr, -0.008, -0.004, "glass", 0.02, along_ray=True)
        out += blend.lay(ref, f"{name}_trim", t, fr, 0.0012, 0.0032, "trim", 0.008, along_ray=True)


# ---------------------------------------------------------------- shut lines
def fillet(pts, r, n=8):
    """A polyline with each corner replaced by a smooth arc of radius r (one per corner)."""
    p = [np.asarray(q, float) for q in pts]
    rs = r if isinstance(r, (list, tuple)) else [r] * (len(p) - 2)
    out = [p[0]]
    for (a, b, c), rr in zip(zip(p[:-2], p[1:-1], p[2:]), rs):
        u, v = (a - b) / np.linalg.norm(a - b), (c - b) / np.linalg.norm(c - b)
        ang = np.arccos(np.clip(u @ v, -1, 1))
        if ang > np.pi - 1e-3:
            out.append(b); continue
        d = min(rr / np.tan(ang / 2), 0.45 * np.linalg.norm(a - b), 0.45 * np.linalg.norm(c - b))
        p0, p1 = b + u * d, b + v * d
        for t in np.linspace(0, 1, max(n, int(rr * 80))):
            out.append((1 - t) ** 2 * p0 + 2 * (1 - t) * t * b + t * t * p1)
    out.append(p[-1])
    return [q.tolist() for q in out]


def door_path():
    """The front door's shut line as one path: down its front edge from the belt, along the sill,
    then up its rear edge to the foot of the black division's middle."""
    fd, rd = R["front_door"], R["rear_door"]
    pts = list(fd) + [rd[2], rd[1], rd[0]]
    er, sr = R["edge_round"], R["shut_round"]
    return fillet(pts, [er, sr, sr, er])


def bonnet_path(ref):
    """The bonnet's shut line in plan, one smooth curve: from the windscreen's foot on the right,
    back along the wing, across the nose above the grille, and back along the left side."""
    left = np.array(R["bonnet"], float)           # nose middle first, out and back to the left windscreen corner
    path = np.vstack([(left * [1, -1])[::-1], left[1:]])
    return geom.catmull_open(path, 8)


def lid_paths(ref):
    """The boot lid's shut line in two runs that meet at its top corners: the sides and bottom
    traced from behind, and the front edge across the deck traced from above, where a line traced
    from behind would graze the nearly flat deck and smear."""
    h = np.array(R["lid"], float)
    lower = np.vstack([(h * [-1, 1])[::-1], h[1:]])
    lower = np.array(fillet(lower.tolist(), 0.02))
    corner, _n = blend.cast(ref, rear_fr(), *h[-1])
    t = np.array(R["lid_top"], float)
    left = np.vstack([[corner[0], corner[1]], t])
    top = np.vstack([left, (left * [1, -1])[::-1][1:]])
    return lower, np.array(fillet(top.tolist(), 0.03))


def back_door_path():
    """The rear door's shut line: one straight run down from the window's foot, one circle round
    the front of the rear arch about the wheel centre, then its bottom straight forward to the
    front door's rear edge."""
    (x0, z0), (x1, z1) = R["back_door"]
    xc, zc, r = W["rear_x"], W["r"], R["back_arch_r"]
    zb = R["door_bottom"]
    a0 = np.arctan2(z1 - zc, x1 - xc)
    a1 = np.arcsin((zb - zc) / r)                  # the circle's front, down to the bottom line
    arc = [[xc + r * np.cos(a), zc + r * np.sin(a)] for a in np.linspace(a0, a1, 24)]
    x_b = R["rear_door"][2][0]
    head = fillet([[x0, z0], arc[0], arc[3]], R["shut_round"])[:-1]
    tail = fillet([arc[-4], arc[-1], [x_b - 0.02, zb]], R["shut_round"])[1:]
    return head + arc[3:-4] + tail


def grooves(ref):
    """Shut lines as V grooves to cut: doors, bonnet, boot lid."""
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        obs.append(blend.groove(ref, f"g_door_{side}", door_path(), side_fr(sgn)))
        obs.append(blend.groove(ref, f"g_back_door_{side}", back_door_path(), side_fr(sgn)))
    obs.append(blend.groove(ref, "g_bonnet", bonnet_path(ref), top_fr()))
    lower, top = lid_paths(ref)
    obs.append(blend.groove(ref, "g_lid", lower, rear_fr()))
    obs.append(blend.groove(ref, "g_lid_top", top, top_fr()))
    return obs


# ---------------------------------------------------------------- doors
DOOR_DEPTH = 0.05     # below the cabin floor the door takes the sill this deep
DOOR_WALL = 0.20      # above it, the whole wall up to the belt, deep enough to reach the cabin's
                      # hollow, so the shoulder over the door goes with the door and leaves no sliver
FRAME_DEPTH = 0.07    # the window frame above the belt, shallow so it never takes the roof's edge
DOOR_GAP = 0.0015     # half the gap round the door, inside the shut line's groove
DOOR_OPEN = 62.0      # degrees the door swings out
FLOOR = 0.25


def door_outline():
    """The left door seen from the side: inside its shut line, closed across the top along the
    belt, and the window's black frame round its glass back to the division's middle."""
    lower = Polygon(door_path()).buffer(0)
    dlo, _g, _t = dlo_shapes()
    frame = dlo.buffer(SIDE_BAND * 0.5, join_style=2).intersection(ahead_of_pillar())
    return unary_union([lower, frame]).buffer(0)


def door_hinge(sgn=1):
    """The hinge axis up the door's front edge, outside the skin, and the open angle. A negative
    angle about an axis pointing up swings the left door's back edge out. sgn: 1 left, -1 right."""
    o = door_outline()
    _x0, z0, x1, _z1 = o.bounds
    xf = x1 + 0.004
    zb = belt(xf)
    y = max(body.y_at(xf - 0.02, z) for z in np.linspace(z0 + 0.02, zb - 0.02, 20)) + 0.003
    return (xf, y * sgn, z0), (xf, y * sgn, zb), -DOOR_OPEN * sgn


def door(ref, shell, obs, sgn=1):
    """Cut the left (sgn 1) or right (sgn -1) door free of the body and its window trim; its
    glass, card and mirror go with it."""
    side = "l" if sgn > 0 else "r"
    o = door_outline()
    lower = Polygon(door_path()).buffer(0)
    regions = [(o.intersection(box(-5, -1, 5, FLOOR + 0.03)), DOOR_DEPTH),
               (lower.intersection(box(-5, FLOOR + 0.02, 5, 5)), DOOR_WALL),
               (o.difference(lower.buffer(-0.01)).intersection(box(-5, FLOOR + 0.02, 5, 5)), FRAME_DEPTH)]
    trim = [ob for ob in obs if ob.name == f"window_trim_{side}"]
    roles = {f"side_glass_{side}": "glass", f"side_glass_{side}_1": "glass", f"side_glass_{side}_2": "glass",
             f"mirror_{side}": "mirror", f"mirror_glass_{side}": "mirror_glass", f"door_card_{side}": "card"}
    return blend.free_door(ref, side_fr(sgn), regions, DOOR_GAP, shell, trim, roles, o, name=f"door_{side}")


# ---------------------------------------------------------------- mirrors
MIRROR_R = 0.04       # housing corner rounding
MIRROR_SWEEP = 0.10   # radians the housing turns back from square to the car


def _rounded_bar(name, y0, y1, z0, z1, x0, x1, r, sgn, mat="trim"):
    face = soft(box(y0, z0, y1, z1), r)
    ring = body.even(np.array(face.exterior.coords)[:-1], 96)
    rings = [np.column_stack([np.full(96, x), ring[:, 0] * sgn, ring[:, 1]]) for x in (x0, x1)]
    v, f = geom.close_rings(rings)
    if sgn < 0:
        f = [tuple(q[::-1]) for q in f]
    return blend.make(name, v, f, mat)


def _sweep(ob, sgn):
    from mathutils import Matrix
    m = R["mirror"]
    p = Matrix.Translation(((m["x_back"] + m["x_front"]) / 2, sgn * m["y_in"], 0))
    ob.data.transform(p @ Matrix.Rotation(sgn * MIRROR_SWEEP, 4, "Z") @ p.inverted())
    return ob


def _pod(name, y0, y1, z0, z1, x0, x1, sgn, mat="body"):
    """A mirror housing: a straight run from its flat back, then its rings easing in on a quarter
    ellipse, so the front is one smooth nose leaning outboard."""
    face = soft(box(y0, z0, y1, z1), min(MIRROR_R, 0.45 * (z1 - z0)))
    ring = body.even(np.array(face.exterior.coords)[:-1], 96)
    cy, cz = y0 + 0.55 * (y1 - y0), (z0 + z1) / 2
    rings = []
    for t in np.concatenate([[0.0, 0.004, 0.2, 0.4], np.linspace(0.46, 1.0, 12)]):
        u = max(t - 0.4, 0.0) / 0.6
        k = max(np.sqrt(max(1 - u * u, 0.0)), 0.25)
        rr = np.column_stack([cy + (ring[:, 0] - cy) * k, cz + (ring[:, 1] - cz) * k])
        rings.append(np.column_stack([np.full(96, x0 + (x1 - x0) * t), rr[:, 0] * sgn, rr[:, 1]]))
    v, f = geom.close_rings(rings)
    if sgn < 0:
        f = [tuple(q[::-1]) for q in f]
    return blend.make(name, v, f, mat)


def mirrors():
    """A painted housing out from the black sail under the A-pillar on a short black arm that runs
    into its underside, its glass filling a shallow recess in the housing's flat back."""
    m = R["mirror"]
    xb, xf = m["x_back"], m["x_front"]
    y0, y1, z0, z1 = m["y_in"] + 0.03, m["y_out"], m["z0"] + 0.02, m["z1"]
    xa = xb + 0.5 * (xf - xb)
    za0, za1 = z0 + 0.010, z0 + 0.040            # the arm's bottom and top, inside the housing's lower half
    root = body.y_at(xa, (za0 + za1) / 2) - 0.03
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        house = _pod(f"mirror_{side}", y0, y1, z0, z1, xb, xf, sgn, "body")
        cut = _rounded_bar(f"cut_mirror_{side}", y0 + 0.008, y1 - 0.008, z0 + 0.008, z1 - 0.008, xb - 0.03, xb + 0.006,
                           MIRROR_R - 0.008, sgn)
        blend.boolean(house, cut, "DIFFERENCE")
        arm = _rounded_bar(f"mirror_arm_{side}", root, y0 + 0.04, za0, za1, xa - 0.035, xa + 0.025, 0.010,
                           sgn, "trim")
        glass = _rounded_bar(f"mirror_glass_{side}", y0 + 0.0095, y1 - 0.0095, z0 + 0.0095, z1 - 0.0095, xb + 0.0015,
                             xb + 0.0045, MIRROR_R - 0.0095, sgn, "mirror_glass")
        blend.boolean(house, arm, "UNION")
        obs += [_sweep(house, sgn), _sweep(glass, sgn)]
    return obs


def _axles():
    """(x, tyre width, outer half width) for the front and rear axles."""
    return [(W["front_x"], W["width_f"], W["outer_y"]), (W["rear_x"], W["width_r"], W["outer_y_r"])]


def arches():
    """Arch cutters and the wheel tubs kept out of the cabin."""
    cut, tubs = [], []
    for x, wd, out in _axles():
        yc = out - wd / 2
        for sgn in (1, -1):
            v, f = geom.disc((x, (yc + 0.10) * sgn, W["r"]), (0, 1, 0), W["arch_r"], 0.60, n=96)
            cut.append(blend.make("arch", v, f, "interior"))
            v, f = geom.disc((x, (yc + 0.10) * sgn, W["r"]), (0, 1, 0), W["arch_r"] + 0.03, 0.62, n=96)
            tubs.append(blend.make("tub", v, f, "interior"))
    return cut, tubs


def tub_drums():
    """The wheel tubs as (x, z, r, y_in) for keeping them out of the cabin."""
    return [(x, W["r"], W["arch_r"] + 0.03, out - wd / 2 + 0.10 - 0.31) for x, wd, out in _axles()]


# ---------------------------------------------------------------- interior
def _loft(name, x0, x1, clip, n=12, gap=0.003, mat="interior"):
    rings = []
    for x in np.linspace(x0, x1, n):
        g = body.inside(x, 0.035 + gap).intersection(clip(x))
        g = biggest(g)
        ring = body.even(np.array(g.exterior.coords)[:-1], 64)
        rings.append(np.column_stack([np.full(64, x), ring]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def _rbox(name, x0, x1, y0, y1, z0, z1, r=0.03, lean=0.0, mat="seat"):
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


STEER = (0.78, 0.37, 0.84)     # the wheel's centre, in front of the left seat
STEER_TILT = 26.0               # degrees the wheel's face turns up from upright


def steering():
    """A round wheel on the left, square to its column, with two level spokes and one down."""
    from mathutils import Matrix
    r, t = 0.185, 0.017
    v, f = geom.lathe([(r - t + t * np.cos(a), t * np.sin(a)) for a in np.linspace(0, 2 * np.pi, 12, endpoint=False)], 64, axis="y")
    rim = blend.make("steering", v, f, "interior")
    parts = [geom.box(-0.02, 0.02, -0.008, 0.008, -r + 0.01, -0.045), geom.box(-r + 0.01, -0.045, -0.008, 0.008, -0.02, 0.02),
             geom.box(0.045, r - 0.01, -0.008, 0.008, -0.02, 0.02)]
    obs = [rim] + [blend.make("spoke", *p, "interior") for p in parts]
    v, f = geom.disc((0, 0, 0), (0, 1, 0), 0.065, 0.05, n=32)
    obs.append(blend.make("boss", v, f, "interior"))
    prof = [(0.018, -0.45)] + [(0.026, t) for t in np.linspace(-0.45, -0.02, 45)] + [(0.018, -0.02)]
    v, f = geom.lathe(prof, 24, axis="y")
    obs.append(blend.make("column", v, f, "interior"))
    wheel = blend.union_all(obs, "steering")
    wheel.data.transform(Matrix.Translation(STEER) @ Matrix.Rotation(np.radians(STEER_TILT), 4, "Y")
                         @ Matrix.Rotation(np.pi / 2, 4, "Z"))
    return wheel


def interior():
    """Firewall, a dash meeting it, two front seats, a rear bench, door cards, wheel and mirror."""
    x0 = R["screen"]["x_base_mid"] - 0.05
    belt_z = belt(0.6)
    tub = W["outer_y"] - W["width_f"] - 0.04       # the firewall stops short of the front wheels' inner faces
    obs = [_loft("firewall", x0 - 0.010, x0 - 0.004, lambda x: box(-tub, FLOOR + 0.002, tub, 3), n=2),
           _loft("dash", x0 - 0.0125, x0 - 0.62, lambda x: box(-0.9, belt_z - 0.30, 0.9, belt_z - 0.08 + 0.12 * (1 - (x0 - x) / 0.62) + 0.04),
                 n=10, gap=0.045)]
    bc = R["rear_door"][0][0]
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, FLOOR + 0.06, 1.2, belt(x) - 0.07) if s > 0 else box(-1.2, FLOOR + 0.06, -0.3, belt(x) - 0.07)).intersection(
            body.inside(x, 0.035 + 0.008)).difference(body.inside(x, 0.035 + 0.026))
        obs.append(_loft(f"door_card_{side}", bc + 0.04, R["front_door"][2][0] - 0.05, clip, n=10))
        yc = 0.37 * sgn
        obs.append(_rbox(f"seat_base_{side}", -0.02, 0.48, yc - 0.23, yc + 0.23, FLOOR - 0.012, FLOOR + 0.16))
        obs.append(_rbox(f"seat_back_{side}", -0.16, -0.04, yc - 0.22, yc + 0.22, FLOOR + 0.161, FLOOR + 0.70, lean=0.22))
        obs.append(_rbox(f"headrest_{side}", -0.29, -0.19, yc - 0.12, yc + 0.12, FLOOR + 0.701, FLOOR + 0.80, r=0.035, lean=0.1))
    obs.append(_rbox("rear_base", -0.98, -0.66, -0.58, 0.58, FLOOR - 0.012, FLOOR + 0.20))
    obs.append(_rbox("rear_back", -1.10, -0.97, -0.58, 0.58, FLOOR + 0.19, FLOOR + 0.58, lean=0.32))
    obs.append(steering())
    xm = R["screen"]["x_top_mid"] - 0.03
    roof_in = body.inside(xm, 0.035).bounds[3]
    top = roof_in - 0.05
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.12, 0.12, top - 0.05, top, r=0.018, mat="interior")
    stem = _rbox("stem", xm - 0.016, xm - 0.004, -0.012, 0.012, top - 0.015, roof_in - 0.0008, r=0.004, mat="interior")
    obs.append(blend.union_all([m, stem], "mirror_rear"))
    return obs


# ---------------------------------------------------------------- wheels
SPOKES = 5            # five spokes from the hub to the rim, as on the reference
SPOKE_TWIST = 9.0     # degrees each spoke's outer end turns ahead of its root


def _spoke(k):
    """One spoke: wide at the hub, narrowing outwards, its outer end turned slightly ahead so the
    five read as a gentle swirl."""
    rr = W["rim_r"]
    pts = []
    for r, hw in ((0.05, 0.036), (0.10, 0.030), (0.16, 0.024), (rr - 0.012, 0.022)):
        a = np.radians(SPOKE_TWIST * (r - 0.05) / (rr - 0.062))
        pts.append((r, hw, a))
    left = [(r * np.cos(a) - hw * np.sin(a), r * np.sin(a) + hw * np.cos(a)) for r, hw, a in pts]
    right = [(r * np.cos(a) + hw * np.sin(a), r * np.sin(a) - hw * np.cos(a)) for r, hw, a in pts]
    return rotate(Polygon(left + right[::-1]), 360 * k / SPOKES + 90, origin=(0, 0))


def wheels():
    """Silver five-spoke wheels: a plain lip, five tapering spokes turned gently ahead, a round
    hub with five dark lug nuts, and a dark disc deep behind the spokes."""
    obs = []
    rr = W["rim_r"]
    face = soft(unary_union([Point(0, 0).buffer(0.062, 48)] + [_spoke(k) for k in range(SPOKES)]), 0.003)
    fr_ring = body.even(np.array(face.exterior.coords)[:-1], 520)
    for x, wd, out, fb in [a + (k,) for a, k in zip(_axles(), ("f", "r"))]:
        hw = wd / 2
        yc = out - hw
        tyre = soft(box(rr, -hw, W["r"], hw), 0.032).union(box(rr, -hw + 0.012, rr + 0.03, hw - 0.012))
        tp = body.even(np.array(tyre.exterior.coords)[:-1], 96)
        rim = [(rr - 0.001, -hw + 0.012), (rr - 0.001, hw - 0.010), (rr - 0.007, hw - 0.003), (rr - 0.015, hw - 0.008),
               (rr - 0.019, hw - 0.045), (rr - 0.019, -hw + 0.012)]
        for sgn, lr in ((1, "l"), (-1, "r")):
            def place(v, s=sgn):
                v = np.asarray(v, float)
                return np.column_stack([x + v[:, 0], (yc + v[:, 1]) * s, W["r"] + v[:, 2]])
            v, f = geom.lathe(tp, 96, axis="y")
            obs.append(blend.make(f"wheel_tyre_{fb}{lr}", place(v), f, "tyre"))
            v, f = geom.lathe(rim, 96, axis="y")
            if sgn < 0:
                f = [tuple(q[::-1]) for q in f]
            obs.append(blend.make(f"wheel_rim_{fb}{lr}", place(v), f, "rim"))
            rings = [np.column_stack([fr_ring[:, 0], np.full(len(fr_ring), yy), fr_ring[:, 1]]) for yy in (hw - 0.055, hw - 0.03)]
            v, f = geom.close_rings(rings, centres=[(0, hw - 0.055, 0), (0, hw - 0.03, 0)])
            if sgn < 0:
                f = [tuple(q[::-1]) for q in f]
            obs.append(blend.make(f"wheel_spokes_{fb}{lr}", place(v), f, "rim"))
            nuts = []
            for k in range(5):
                a = 2 * np.pi * k / 5 + np.pi / 5
                v, f = geom.disc((0.038 * np.cos(a), hw - 0.026, 0.038 * np.sin(a)), (0, 1, 0), 0.009, 0.008, n=12)
                if sgn < 0:
                    f = [tuple(q[::-1]) for q in f]
                nuts.append(blend.make("nut", place(v), f, "dark_metal"))
            obs.append(blend.union_all(nuts, f"wheel_nuts_{fb}{lr}"))
            v, f = geom.disc((0, hw - 0.11, 0), (0, 1, 0), rr - 0.021, 0.008, n=64)
            obs.append(blend.make(f"wheel_disc_{fb}{lr}", place(v), f, "dark_metal"))
    return obs


# ---------------------------------------------------------------- badge and name
BRAND = HERE.parent.parent.parent / "src/renderer/assets/brands/liberty"


def _quad(p0, p1, p2, n=10):
    t = np.linspace(0, 1, n)[1:, None]
    return list(map(tuple, (1 - t) ** 2 * np.array(p0) + 2 * (1 - t) * t * np.array(p1) + t * t * np.array(p2)))


def badge_shapes():
    """The Liberty badge from liberty-badge-chrome.svg, read off its own drawing: the chrome
    hexagon ring round a dark field, and the chrome bell on the field with its crack cut through.
    [(shape, material)], y up, 1 unit tall."""
    svg = (BRAND / "liberty-badge-chrome.svg").read_text()
    outer, inner = [Polygon([tuple(map(float, p.split(","))) for p in pts.split()])
                    for pts in re.findall(r'<polygon points="([^"]+)"', svg)]
    bell = Polygon([(74, 70)] + _quad((74, 70), (74, 52), (100, 52)) + _quad((100, 52), (126, 52), (126, 70)) +
                   [(130, 116)] + _quad((130, 116), (132, 134), (150, 140)) + [(150, 148), (50, 148), (50, 140)] +
                   _quad((50, 140), (68, 134), (70, 116)))
    bell = unary_union([bell, box(92, 44, 108, 52), Point(100, 157).buffer(8, 32)])
    crack = LineString([(104, 56), (97, 80), (106, 98), (99, 122), (104, 136)]).buffer(1.75, cap_style=1, join_style=1)
    chrome = unary_union([outer.difference(inner), bell.difference(crack)])
    b = outer.bounds
    k = 1 / (b[3] - b[1])

    def fit(g):
        g = sscale(g, 1, -1, origin=(0, 0))
        g = translate(g, -(b[0] + b[2]) / 2, (b[1] + b[3]) / 2)
        return sscale(g, k, k, origin=(0, 0))
    return [(fit(inner.buffer(0.5)), "badge_dark"), (fit(chrome), "chrome")]


def name_shape():
    """Mantle in the brand font, 1 unit = its cap height, centred."""
    gl = json.loads((HERE / "glyphs.json").read_text())
    g = unary_union([Polygon(p[0], p[1:]) for p in gl["pieces"]])
    b = g.bounds
    g = translate(g, -(b[0] + b[2]) / 2, -gl["cap"] / 2)
    return sscale(g, 1 / gl["cap"], 1 / gl["cap"], origin=(0, 0))


def _flat(ref, g, size, fr, a, b, up, name, d0, d1, mat, step=0.004):
    """Lay shape g (right, up, 1 unit = size) flat on the panel that fr's ray at (a, b) meets."""
    pf = blend.panel_frame(ref, fr, a, b, up)
    return blend.lay(ref, name, sscale(g, size, size, origin=(0, 0)), pf, d0, d1, mat, step)


NAME_T = 0.0035


def badge(ref, size, fr, a, b, up, name, base=0.0012):
    """The badge: its dark field just proud of the panel, the chrome standing above it. base: how
    far out from the skin it starts. The field is a hair smaller and the chrome starts inside it,
    so no faces of the two coincide."""
    obs = []
    (field, m0), (chrome, m1) = badge_shapes()
    obs += _flat(ref, field.buffer(-0.004), size, fr, a, b, up, f"{name}_f", base, base + 0.0018, m0, 0.003)
    obs += _flat(ref, chrome, size, fr, a, b, up, f"{name}_c", base + 0.001, base + 0.0043, m1, 0.003)
    return obs


def options(ref):
    """Badge and name options, named opt_<group>_<n>_<i>. Each lies flat on its panel."""
    nm = name_shape()
    up, fwd = (0, 0, 1), (-1, 0, 0)
    out = []

    def add(group, n, objs):
        for i, ob in enumerate(objs):
            ob.name = f"opt_{group}_{n}_{i}"
            out.append(ob)
    gz = [p[1] for p in R["grille"]]
    # front: 1 in the grille's middle on its bars (the reference's place), 2 on the bonnet's nose
    add("frontbadge", 1, badge(ref, 0.075, front_fr(), 0, (min(gz) + max(gz)) / 2 + 0.01, up, "fb", base=0.03))
    add("frontbadge", 2, badge(ref, 0.07, top_fr(), NOSE - 0.22, 0, fwd, "fb"))
    # rear: 1 on the boot lid between the lamps (the reference's place), 2 low in the lid's recess
    add("rearbadge", 1, badge(ref, 0.075, rear_fr(), 0, R["gate_z"], up, "rb"))
    add("rearbadge", 2, badge(ref, 0.08, rear_fr(), 0, R["gate_low"], up, "rb"))
    cap = 0.034
    # name: 1 on the lid's left under the lamp (the reference's place), 2 its right, 3 low on both
    # front doors behind their front edge
    nb = nm.bounds
    wn = (nb[2] - nb[0]) * cap
    xs = R["lid_w"] - 0.05 - wn / 2
    zn = R["lid"][0][1] + 0.05
    add("name", 1, _flat(ref, nm, cap, rear_fr(), xs, zn, up, "nm", 0.0012, NAME_T, "chrome"))
    add("name", 2, _flat(ref, nm, cap, rear_fr(), -xs, zn, up, "nm", 0.0012, NAME_T, "chrome"))
    q = []
    xd = R["front_door"][2][0] - 0.30
    for sgn in (1, -1):
        q += _flat(ref, nm, cap * 1.3, side_fr(sgn), xd, R["door_bottom"] + 0.10, up, "nm", 0.0012, NAME_T, "chrome")
    add("name", 3, q)
    return out
