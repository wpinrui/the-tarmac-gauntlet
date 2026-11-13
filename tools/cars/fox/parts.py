"""The Fox's parts: lamps, grille, intake, fog lamps, splitter, glass, shut lines, doors,
mirrors, rally wing, roof scoop, bonnet vents, exhaust, interior, multi-spoke wheels, badge and
name. Every shape and size here is the Fox's own, from rows.json, kept plain and smooth. Runs inside Blender's Python
module, called by export.py."""
import json
import re
import sys
from pathlib import Path

import numpy as np
from shapely.affinity import rotate, scale as sscale, skew, translate
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
    "exhaust": ((0.66, 0.64, 0.6), 0.92, 1.0, None),
    "badge_gold": ((0.91, 0.70, 0.23), 0.8, 1.0, None),
    "badge_green": ((0.07, 0.27, 0.23), 0.6, 1.0, None),
    "badge_red": ((0.84, 0.15, 0.18), 0.6, 1.0, None),
    "badge_mint": ((0.66, 0.76, 0.71), 0.6, 1.0, None),
    "badge_cream": ((0.96, 0.94, 0.89), 0.6, 1.0, None),
    "interior": ((0.075, 0.075, 0.085), 0.15, 1.0, None),
    "seat": ((0.11, 0.11, 0.12), 0.2, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.9, 0.9, 0.9), 0.6, 1.0, None),
    "dark_metal": ((0.14, 0.14, 0.15), 0.8, 1.0, None),
}
LAMPS = {"Headlights": ["lamp_head"], "Tail lights": ["lamp_tail"], "Brake lights": ["lamp_brake"],
         "Rain light": ["lamp_rain"]}
GROUPS = {"frontbadge": "Front badge", "rearbadge": "Rear badge", "name": "Name"}
SHOW = {"Wing": ["wing"], "Roof scoop": ["scoop"]}


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


def smooth_line(pts, n=60):
    """An even, smooth curve through traced points (open)."""
    from scipy.interpolate import splev, splprep
    p = np.array(pts, float)
    tck, _u = splprep([p[:, 0], p[:, 1]], s=len(p) * 2e-6, k=3)
    return np.column_stack(splev(np.linspace(0, 1, n), tck))


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
    v = body.values(0.3)
    y0, z0, y1, z1 = v["wg"], v["zs"], v["wr"], v["zr"]
    a = np.arctan2(y0 - y1, z1 - z0)
    up = np.array([0.0, -np.sin(a) * sgn, np.cos(a)])
    out = np.array([0.0, np.cos(a) * sgn, np.sin(a)])
    fr = blend.frame(np.array([0.0, y0 * sgn, z0]) + out * 0.3, (1, 0, 0), up, -out)
    return fr, lambda g: sscale(translate(g, 0, -z0), 1, 1 / np.cos(a), origin=(0, 0))


def _ends(name, shape, d0, d1, mat, ref, cutters, out, fr, depth, sides=True):
    """A recess cut along fr's rays with a dark back, mirrored to both sides when sides."""
    for sgn, side in (((1, "l"), (-1, "r")) if sides else ((1, ""),)):
        g = shape if sgn > 0 else flip(shape)
        k = f"_{side}" if side else ""
        cutters += blend.lay(ref, f"cut_{name}{k}", g, fr, -depth, 0.006, mat, 0.006, along_ray=True)
        out += blend.lay(ref, f"{name}_back{k}", g.buffer(-0.002), fr, -depth + 0.001, -depth + 0.005, mat, 0.006,
                         along_ray=True)


# ---------------------------------------------------------------- lamps
def mirror_lr(sgn):
    return (lambda q: q) if sgn > 0 else flip


HEAD_STEP = 0.007      # the headlamp is laid on this mesh; its outline keeps every point of its smooth curve


def headlamps(ref, cutters, out):
    """Each headlamp is the traced teardrop on the nose's corner. Our own interior: a round
    projector bowl at the inner end with a bright core, and a thin light blade along the upper
    edge running out to the corner."""
    fr = front_fr()
    shp = smooth_outline(R["head"])
    b = shp.bounds
    inner = shp.buffer(-0.008)
    bowl = Point(b[0] + 0.09, (b[1] + b[3]) / 2 - 0.004).buffer(0.042, 64).intersection(inner)
    core = bowl.centroid.buffer(0.022, 48)
    blade = inner.difference(inner.buffer(-0.014)).intersection(box(b[0] + 0.14, (b[1] + b[3]) / 2 + 0.01, b[2], b[3]))
    blade = soft(max(getattr(blade, "geoms", [blade]), key=lambda g: g.area), 0.003)
    for sgn, side in ((1, "l"), (-1, "r")):
        g = mirror_lr(sgn)
        cutters += blend.lay(ref, f"cut_head_{side}", g(shp), fr, -0.05, 0.006, "housing", HEAD_STEP, along_ray=True)
        out += blend.lay(ref, f"head_back_{side}", g(shp.buffer(-0.002)), fr, -0.047, -0.043, "housing", HEAD_STEP, along_ray=True)
        out += blend.lay(ref, f"head_reflector_{side}", g(bowl.difference(core.buffer(0.004))), fr, -0.0422, -0.034,
                         "reflector", HEAD_STEP, along_ray=True)
        out += blend.lay(ref, f"lamp_head_{side}0", g(core), fr, -0.0422, -0.036, "lamp_head", HEAD_STEP, along_ray=True)
        out += blend.lay(ref, f"lamp_head_{side}1", g(blade), fr, -0.041, -0.037, "lamp_head", HEAD_STEP, along_ray=True)
        out += blend.lay(ref, f"head_glass_{side}", g(shp.buffer(-0.002)), fr, -0.0055, -0.0025, "head_glass", 0.006,
                         along_ray=True)
    # fog lamps in the bumper's corners: a black recess with a slim lamp across its middle
    fog = soft(poly(R["fog"]), 0.006)
    fb = fog.bounds
    lamp = soft(fog.buffer(-0.012).intersection(box(-1, (fb[1] + fb[3]) / 2 - 0.012, 1, (fb[1] + fb[3]) / 2 + 0.012)), 0.004)
    for sgn, side in ((1, "l"), (-1, "r")):
        g = mirror_lr(sgn)
        cutters += blend.lay(ref, f"cut_fog_{side}", g(fog), fr, -0.035, 0.006, "housing", 0.005, along_ray=True)
        out += blend.lay(ref, f"fog_back_{side}", g(fog.buffer(-0.002)), fr, -0.034, -0.03, "grille", 0.005, along_ray=True)
        out += blend.lay(ref, f"lamp_head_fog_{side}", g(lamp), fr, -0.029, -0.024, "lamp_head", 0.004, along_ray=True)
        out += blend.lay(ref, f"fog_glass_{side}", g(fog.buffer(-0.002)), fr, -0.0055, -0.0025, "head_glass", 0.005,
                         along_ray=True)


TAIL_R = 0.25        # the wrap's unrolled coordinate: metres round the corner at this radius
TAIL_STEP = 0.008    # the tail lamp is laid on this mesh; its outline keeps every point of its smooth curve


def tail_fr(sgn):
    """Rays fanning out from a vertical axis inside the tail's corner, so the lamp can wrap from
    the tail round onto the side. (a, b): a is the angle from straight back times TAIL_R, b is z."""
    xa, ya = R["tail_axis"]

    def fr(a, b):
        t = a / TAIL_R
        out = np.array([-np.cos(t), np.sin(t) * sgn, 0.0])
        return np.array([xa, ya * sgn, b]) + out * 1.2, -out
    return fr


def tail_outline(ref):
    """The traced lamp outline found on the body, read in the wrap's coordinates and smoothed."""
    xa, ya = R["tail_axis"]
    pts = []
    for (u, z) in R["tail"]:
        pts.append(blend.cast(ref, rear_fr(), u, z)[0])
    for (x, z) in R["tail_side"]:
        pts.append(blend.cast(ref, side_fr(1), x, z)[0])
    for (u, z) in R["tail_top"]:
        pts.append(blend.cast(ref, rear_fr(), u, z)[0])
    ab = [(np.arctan2(p[1] - ya, -(p[0] - xa)) * TAIL_R, p[2]) for p in pts]
    return smooth_outline(ab, 300)


def taillamps(ref, cutters, out):
    """Each tail lamp is the traced outline wrapping the tail's corner. Our own interior: the tail
    lamp a slim band along the top edge running round onto the side, the brake lamp filling the
    lamp's broad part on the tail."""
    shp = tail_outline(ref)
    b = shp.bounds
    inner = shp.buffer(-0.007)
    band = inner.difference(inner.buffer(-0.016).union(box(b[0] - 1, b[1] - 1, b[2] + 1, (b[1] + b[3]) / 2)))
    band = soft(max(getattr(band, "geoms", [band]), key=lambda g: g.area), 0.003)
    brake = inner.buffer(-0.02).intersection(box(b[0] - 1, b[1] - 1, b[0] + 0.55 * (b[2] - b[0]), b[3] + 1))
    brake = soft(max(getattr(brake, "geoms", [brake]), key=lambda g: g.area), 0.006)
    for sgn, side in ((1, "l"), (-1, "r")):
        fr = tail_fr(sgn)
        cutters += blend.lay(ref, f"cut_tail_{side}", shp, fr, -0.035, 0.006, "housing", TAIL_STEP, along_ray=True)
        out += blend.lay(ref, f"tail_back_{side}", shp.buffer(-0.002), fr, -0.034, -0.03, "housing", TAIL_STEP, along_ray=True)
        out += blend.lay(ref, f"lamp_tail_{side}", band, fr, -0.029, -0.025, "lamp_tail", TAIL_STEP, along_ray=True)
        out += blend.lay(ref, f"lamp_brake_{side}", brake, fr, -0.029, -0.025, "lamp_brake", TAIL_STEP, along_ray=True)
        out += blend.lay(ref, f"tail_glass_{side}", shp.buffer(-0.002), fr, -0.0055, -0.0025, "tail_glass", TAIL_STEP,
                         along_ray=True)


# ---------------------------------------------------------------- front and rear
def slats(shape, n, t=0.008):
    """n level bars across a shape, evenly spaced from top to bottom."""
    b = shape.bounds
    zs = np.linspace(b[1], b[3], n + 2)[1:-1]
    return unary_union([box(b[0] - 1, z - t / 2, b[2] + 1, z + t / 2) for z in zs]).intersection(shape.buffer(-0.0005))


def front_parts(ref, cutters, out):
    """The slim upper grille between the headlamps with two slats, the big intake in the bumper
    with three, and the black splitter under the bumper."""
    fr = front_fr()
    for name, n, depth in (("grille", 2, 0.04), ("intake", 3, 0.06)):
        sh = half_shape(R[name], 0.006)
        _ends(name, sh, 0, 0, "grille", ref, cutters, out, fr, depth, sides=False)
        out += blend.lay(ref, f"{name}_slat", slats(sh, n), fr, -depth + 0.006, -0.012, "trim", 0.006, along_ray=True)
    w, z0, z1 = R["splitter"]
    edge = min(w, body.y_at(NOSE - 0.12, (z0 + z1) / 2) - 0.004)
    out += blend.lay(ref, "splitter", soft(box(-edge, z0, edge, z1), 0.006), fr, -0.001, 0.008, "trim", 0.01)


def rear_parts(ref, cutters, out):
    fr = rear_fr()
    band = half_shape(R["band_r"], 0.006)
    _ends("band_r", band, 0, 0, "grille", ref, cutters, out, fr, 0.03, sides=False)
    u, z0, z1 = R["rain"]
    out += blend.lay(ref, "lamp_rain", soft(box(-u, z0, u, z1), 0.004), fr, -0.026, -0.022, "lamp_rain", 0.004,
                     along_ray=True)
    out += exhaust(ref)


def exhaust(ref):
    """One big round tip under the bumper's middle, pointing straight back."""
    y, z, r = R["exhaust"]
    xs = R["exhaust_x"]
    prof = [(r - 0.004, -0.25)] + [(r, t) for t in np.linspace(-0.25, 0.0, 41)] + [(r - 0.004, 0.0)]
    v, f = geom.lathe(prof, 48, axis="z")
    v = np.column_stack([xs - v[:, 2], y + v[:, 0], z + v[:, 1]])
    obs = [blend.make("exhaust", v, [q[::-1] for q in f], "exhaust")]
    v, f = geom.disc((xs + 0.05, y, z), (1, 0, 0), r - 0.003, 0.01, n=40)
    obs.append(blend.make("exhaust_in", v, f, "trim"))
    return obs


# ---------------------------------------------------------------- glass and trim
SIDE_BAND = 0.016     # black frame round the side windows
GLASS_BAND = 0.024    # black band round the windscreen and the hatch glass


def dlo_line():
    """The window opening rebuilt from plain fits: the A-pillar edge a straight line, the roof edge
    one even curve, the quarter window's rear edge a straight line, and its foot the belt."""
    d = R["dlo"]
    roof = np.array(d["roof"], float)
    k = np.polyfit(roof[:, 0], roof[:, 1], 2)
    xs = np.linspace(roof[0, 0], roof[-1, 0], 24)
    top = np.column_stack([xs, np.polyval(k, xs)])           # front to back (x falling)
    a = np.array([d["a_foot"], d["a_head"]], float)
    c = np.array([d["c_head"], d["c_foot"]], float)
    fx = np.linspace(c[1, 0], a[0, 0], 30)
    foot = np.column_stack([fx, [belt(x) for x in fx]])
    return np.vstack([a[:1], top, c, foot[1:-1]])


def pillar_line():
    """The black division between door glass and quarter window: its centre line, extended."""
    (x0, z0), (x1, z1) = R["b_pillar"]["foot"], R["b_pillar"]["head"]
    t = np.array([x1 - x0, z1 - z0]); t /= np.linalg.norm(t)
    return np.array([x0, z0]) - t * 0.5, np.array([x0, z0]) + t * 1.0


def ahead_of_pillar(off=0.0):
    """Everything ahead of the division's centre line (moved forward by off), seen from the side."""
    p0, p1 = pillar_line()
    p0 = p0 + [off, 0]; p1 = p1 + [off, 0]
    return Polygon([tuple(p0), tuple(p1), (p1[0] + 5, p1[1]), (p0[0] + 5, p0[1])])


def dlo_shapes():
    """The side window opening, its glass (door glass and quarter window, split by the black
    division, the door glass stopping short of the black corner by the mirror) and the black band."""
    dlo = soft(poly(dlo_line()), 0.03)
    glass = dlo.buffer(-SIDE_BAND, join_style=1)
    half = R["b_pillar"]["w"] / 2
    glass = glass.intersection(ahead_of_pillar(half)).union(glass.difference(ahead_of_pillar(-half)))
    glass = glass.difference(box(R["vent_x"], -1, 5, 5))
    glass = unary_union([soft(p, 0.012) for p in getattr(glass, "geoms", [glass])])
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
    """The door's shut line as one path: down its front edge from the belt, along the sill, then
    up its rear edge to the foot of the black division's middle."""
    fd, rd = R["front_door"], R["rear_door"]
    pts = list(fd) + [rd[2], rd[1], rd[0]]
    er, sr = R["edge_round"], R["shut_round"]
    return fillet(pts, [er, sr, sr, er])


def bonnet_path(ref):
    """The bonnet's shut line in plan: from the windscreen's foot on the right, back along the
    wing, across the nose above the grille and the lamps' tops, and back along the left side to
    the windscreen. The nose part is traced from ahead, found on the body, and read in plan."""
    nose = []
    for u, z in smooth_line(R["bonnet_nose"], 40):
        loc, _n = blend.cast(ref, front_fr(), u, z)
        nose.append((loc[0], loc[1]))
    side = np.array(R["bonnet"], float)
    left = np.vstack([nose, side])               # nose middle first, out and back to the left windscreen corner
    return np.vstack([(left * [1, -1])[::-1], left[1:]])


def hatch_paths(ref):
    """The tailgate's shut line in two runs that meet at its top corners: the sides and bottom
    traced from behind, and the top across the roof's end traced from above, where a line
    traced from behind would graze the nearly flat roof and smear."""
    h = np.array(R["hatch"], float)
    lower = np.vstack([(h * [-1, 1])[::-1], h[1:]])
    lower = np.array(fillet(lower.tolist(), 0.03))
    corner, _n = blend.cast(ref, rear_fr(), *h[-1])
    t = np.array(R["hatch_top"], float)
    left = np.vstack([[corner[0], corner[1]], t])
    top = np.vstack([left, (left * [1, -1])[::-1][1:]])
    return lower, np.array(fillet(top.tolist(), 0.03))


def grooves(ref):
    """Shut lines as V grooves to cut: doors, bonnet, tailgate."""
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        obs.append(blend.groove(ref, f"g_door_{side}", door_path(), side_fr(sgn)))
    obs.append(blend.groove(ref, "g_bonnet", bonnet_path(ref), top_fr()))
    lower, top = hatch_paths(ref)
    obs.append(blend.groove(ref, "g_hatch", lower, rear_fr()))
    obs.append(blend.groove(ref, "g_hatch_top", top, top_fr()))
    return obs


# ---------------------------------------------------------------- doors
DOOR_DEPTH = 0.05     # below the cabin floor the door takes the sill this deep
DOOR_WALL = 0.20      # above it, the whole wall up to the belt, deep enough to reach the cabin's
                      # hollow, so the shoulder over the door goes with the door and leaves no sliver
FRAME_DEPTH = 0.07    # the window frame above the belt, shallow so it never takes the roof's edge
DOOR_GAP = 0.0015     # half the gap round the door, inside the shut line's groove
DOOR_OPEN = 62.0      # degrees the door swings out
FLOOR = 0.16


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
    roles = {f"side_glass_{side}": "glass", f"side_glass_{side}_1": "glass",
             f"mirror_{side}": "mirror", f"mirror_glass_{side}": "mirror_glass", f"door_card_{side}": "card"}
    return blend.free_door(ref, side_fr(sgn), regions, DOOR_GAP, shell, trim, roles, o, name=f"door_{side}")


# ---------------------------------------------------------------- mirrors
MIRROR_R = 0.045      # housing corner rounding
MIRROR_SWEEP = 0.08   # radians the housing turns back from square to the car


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


def _pod(name, y0, y1, z0, z1, x0, x1, sgn, mat="trim"):
    """A rounded mirror housing: a soft oval face at the back, its rings shrinking towards the
    front on a quarter circle, so the front is one smooth dome."""
    face = soft(box(y0, z0, y1, z1), min(MIRROR_R, 0.45 * (z1 - z0)))
    ring = body.even(np.array(face.exterior.coords)[:-1], 96)
    cy, cz = y0 + 0.4 * (y1 - y0), (z0 + z1) / 2
    rings = []
    for t in np.concatenate([[0.0, 0.004], np.linspace(0.1, 1.0, 14)]):
        k = max(np.sqrt(max(1 - t * t, 0.0)), 0.18)
        rr = np.column_stack([cy + (ring[:, 0] - cy) * k, cz + (ring[:, 1] - cz) * k])
        rings.append(np.column_stack([np.full(96, x0 + (x1 - x0) * t), rr[:, 0] * sgn, rr[:, 1]]))
    v, f = geom.close_rings(rings)
    if sgn < 0:
        f = [tuple(q[::-1]) for q in f]
    return blend.make(name, v, f, mat)


def mirrors():
    """A rounded black housing at the door's front corner on a short black arm from the black
    corner by the windscreen pillar, its glass in a recess in the housing's back."""
    m = R["mirror"]
    xb, xf = m["x_back"], m["x_front"]
    x0, x1 = xb, xf
    y0, y1, z0, z1 = m["y_in"] + 0.03, m["y_out"], m["z0"], m["z1"]
    xa = xb + 0.6 * (xf - xb)
    zb = belt(xa)
    root = body.y_at(xa, zb + 0.03) - 0.03
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        house = _pod(f"mirror_{side}", y0, y1, z0, z1, x0, x1, sgn, "trim")
        cut = _rounded_bar(f"cut_mirror_{side}", y0 + 0.008, y1 - 0.008, z0 + 0.008, z1 - 0.008, x0 - 0.03, x0 + 0.006,
                           MIRROR_R - 0.008, sgn)
        blend.boolean(house, cut, "DIFFERENCE")
        arm = _rounded_bar(f"mirror_arm_{side}", root, y0 + 0.03, zb + 0.012, zb + 0.045, xa - 0.035, xa + 0.02, 0.010,
                           sgn, "trim")
        glass = _rounded_bar(f"mirror_glass_{side}", y0 + 0.0095, y1 - 0.0095, z0 + 0.0095, z1 - 0.0095, x0 + 0.0015,
                             x0 + 0.0045, MIRROR_R - 0.0095, sgn, "mirror_glass")
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


# ---------------------------------------------------------------- rally wing, roof scoop, bonnet vents
WING_T = 0.012        # the wing's endplates are this thick


def wing():
    """The rally wing over the hatch: the traced section carried across the car, its front
    resting on the roof's end, and a flat black endplate at each tip, dropping to a fin at the back."""
    prof = soft(Polygon(R["wing"]), 0.005)
    ring = body.even(np.array(prof.exterior.coords)[:-1], 48)
    span = R["wing_span"]
    rings = [np.column_stack([ring[:, 0], np.full(48, y), ring[:, 1]]) for y in np.linspace(-span + WING_T / 2, span - WING_T / 2, 9)]
    v, f = geom.close_rings(rings)
    obs = [blend.make("wing", v, f, "body")]
    pb = prof.bounds
    for sgn, side in ((1, "l"), (-1, "r")):
        y = sgn * (span - WING_T / 2)
        # the plate: the section grown all round, dropping lower at the back like a fin
        fin = Polygon([(pb[0] + 0.06, pb[1]), (pb[2], pb[1] - 0.05), (pb[2], pb[3]), (pb[0] + 0.06, pb[3])])
        plate = soft(unary_union([prof.buffer(0.022), fin]), 0.01)
        pr = body.even(np.array(plate.exterior.coords)[:-1], 64)
        rr = [np.column_stack([pr[:, 0], np.full(64, y + d), pr[:, 1]]) for d in (-WING_T / 2, WING_T / 2)]
        v, f = geom.close_rings(rr)
        obs.append(blend.make(f"wing_plate_{side}", v, f, "trim"))
    return obs


def scoop():
    """The roof scoop: a low rounded hump on the roof's middle, open at the front."""
    x_back, x_front, w, h = R["scoop"]
    face = soft(box(-w, 0.0, w, h), min(0.02, h * 0.45))
    ring = body.even(np.array(face.exterior.coords)[:-1], 48)
    rings = []
    for x in np.linspace(x_back, x_front, 16):
        t = (x - x_back) / (x_front - x_back)
        k = np.sqrt(np.clip(t / 0.25, 0, 1))                # it rises from the roof towards the front
        base = body.z_top(x) - 0.012
        rings.append(np.column_stack([np.full(48, x), ring[:, 0] * (0.85 + 0.15 * k), base + ring[:, 1] * max(k, 0.05) + 0.004 * (1 - k)]))
    v, f = geom.close_rings(rings)
    obs = [blend.make("scoop", v, f, "body")]
    mouth = soft(box(-w + 0.02, 0.005, w - 0.02, h - 0.004), 0.005)
    mr = body.even(np.array(mouth.exterior.coords)[:-1], 48)
    rr = [np.column_stack([np.full(48, x), mr[:, 0], body.z_top(x_front) - 0.012 + mr[:, 1]]) for x in (x_front - 0.06, x_front + 0.002)]
    v, f = geom.close_rings(rr)
    obs.append(blend.make("scoop_mouth", v, f, "grille"))
    return obs


def vents(ref, out):
    """A black slanted vent each side of the bonnet's middle."""
    sh = soft(poly(R["vent"]), 0.008)
    for sgn, side in ((1, "l"), (-1, "r")):
        g = sh if sgn > 0 else sscale(sh, 1, -1, origin=(0, 0))
        out += blend.lay(ref, f"vent_{side}", g, top_fr(), -0.001, 0.003, "grille", 0.008)
        out += blend.lay(ref, f"vent_slat_{side}", slats(g, 3, 0.006), top_fr(), 0.0028, 0.0045, "trim", 0.006)


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


STEER = (0.48, 0.36, 0.72)     # the wheel's centre, in front of the left seat
STEER_TILT = 28.0               # degrees the wheel's face turns up from upright


def steering():
    """A round wheel on the left, square to its column, with three spokes."""
    from mathutils import Matrix
    r, t = 0.18, 0.016
    v, f = geom.lathe([(r - t + t * np.cos(a), t * np.sin(a)) for a in np.linspace(0, 2 * np.pi, 12, endpoint=False)], 64, axis="y")
    rim = blend.make("steering", v, f, "interior")
    parts = [geom.box(-0.018, 0.018, -0.008, 0.008, -r + 0.01, -0.04), geom.box(-r + 0.01, -0.04, -0.008, 0.008, -0.018, 0.018),
             geom.box(0.04, r - 0.01, -0.008, 0.008, -0.018, 0.018)]
    obs = [rim] + [blend.make("spoke", *p, "interior") for p in parts]
    v, f = geom.disc((0, 0, 0), (0, 1, 0), 0.06, 0.05, n=32)
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
    belt_z = belt(0.0)
    obs = [_loft("firewall", x0 - 0.010, x0 - 0.004, lambda x: box(-0.9, FLOOR + 0.002, 0.9, 3), n=2),
           _loft("dash", x0 - 0.0125, 0.36, lambda x: box(-0.9, belt_z - 0.30, 0.9, belt_z - 0.10 + 0.12 * (1 - (x0 - x) / 0.6) + 0.04),
                 n=10, gap=0.045)]
    bc = R["rear_door"][0][0]
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, FLOOR + 0.06, 1.2, belt(x) - 0.07) if s > 0 else box(-1.2, FLOOR + 0.06, -0.3, belt(x) - 0.07)).intersection(
            body.inside(x, 0.035 + 0.008)).difference(body.inside(x, 0.035 + 0.026))
        obs.append(_loft(f"door_card_{side}", bc + 0.04, R["front_door"][2][0] - 0.05, clip, n=10))
        yc = 0.36 * sgn
        obs.append(_rbox(f"seat_base_{side}", -0.36, 0.12, yc - 0.22, yc + 0.22, FLOOR + 0.006, FLOOR + 0.16))
        obs.append(_rbox(f"seat_back_{side}", -0.50, -0.38, yc - 0.21, yc + 0.21, FLOOR + 0.161, FLOOR + 0.70, lean=0.22))
        obs.append(_rbox(f"headrest_{side}", -0.63, -0.53, yc - 0.12, yc + 0.12, FLOOR + 0.701, FLOOR + 0.80, r=0.035, lean=0.1))
    obs.append(_rbox("rear_base", -1.02, -0.72, -0.52, 0.52, FLOOR + 0.04, FLOOR + 0.20))
    obs.append(_rbox("rear_back", -1.14, -1.03, -0.52, 0.52, FLOOR + 0.201, FLOOR + 0.58, lean=0.3))
    obs.append(steering())
    xm = R["screen"]["x_top_mid"] - 0.03
    roof_in = body.inside(xm, 0.035).bounds[3]
    top = roof_in - 0.05
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.11, 0.11, top - 0.05, top, r=0.018, mat="interior")
    stem = _rbox("stem", xm - 0.016, xm - 0.004, -0.012, 0.012, top - 0.015, roof_in - 0.0008, r=0.004, mat="interior")
    obs.append(blend.union_all([m, stem], "mirror_rear"))
    return obs


# ---------------------------------------------------------------- wheels
SPOKES = 12           # slim straight spokes from the hub to the rim, as on the reference


def wheels():
    """White multi-spoke wheels: a plain lip, a shallow dish behind twelve slim spokes running
    from a round hub out to the rim, and a dark disc deep behind them."""
    obs = []
    rr = W["rim_r"]
    for x, wd, out, fb in [a + (k,) for a, k in zip(_axles(), ("f", "r"))]:
        hw = wd / 2
        yc = out - hw
        tyre = soft(box(rr, -hw, W["r"], hw), 0.035).union(box(rr, -hw + 0.012, rr + 0.03, hw - 0.012))
        tp = body.even(np.array(tyre.exterior.coords)[:-1], 96)
        rim = [(rr - 0.001, -hw + 0.012), (rr - 0.001, hw - 0.010), (rr - 0.008, hw - 0.003), (rr - 0.016, hw - 0.008),
               (rr - 0.02, hw - 0.04), (rr - 0.02, -hw + 0.012)]
        face = unary_union([Point(0, 0).buffer(0.058, 48)] +
                           [rotate(box(0.03, -0.0085, rr - 0.012, 0.0085), 360 * k / SPOKES, origin=(0, 0))
                            for k in range(SPOKES)])
        face = soft(face, 0.003)
        fr = body.even(np.array(face.exterior.coords)[:-1], 480)
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
            rings = [np.column_stack([fr[:, 0], np.full(len(fr), yy), fr[:, 1]]) for yy in (hw - 0.05, hw - 0.028)]
            v, f = geom.close_rings(rings, centres=[(0, hw - 0.05, 0), (0, hw - 0.028, 0)])
            if sgn < 0:
                f = [tuple(q[::-1]) for q in f]
            obs.append(blend.make(f"wheel_spokes_{fb}{lr}", place(v), f, "rim"))
            v, f = geom.disc((0, hw - 0.024, 0), (0, 1, 0), 0.03, 0.008, n=32)
            obs.append(blend.make(f"wheel_cap_{fb}{lr}", place(v), f, "dark_metal"))
            v, f = geom.disc((0, hw - 0.10, 0), (0, 1, 0), rr - 0.022, 0.008, n=64)
            obs.append(blend.make(f"wheel_disc_{fb}{lr}", place(v), f, "dark_metal"))
    return obs


# ---------------------------------------------------------------- badge and name
BRAND = HERE.parent.parent.parent / "src/renderer/assets/brands/nusa"


def _path_polys(d):
    """Closed shapes of an SVG path's d (M, L, C and Z only), as shapely polygons."""
    toks = re.findall(r"[MLCZ]|-?[\d.]+", d)
    out, cur, i, cmd = [], [], 0, None
    while i < len(toks):
        t = toks[i]
        if t in "MLCZ":
            cmd = t
            i += 1
            if cmd == "Z":
                out.append(Polygon(cur).buffer(0)); cur = []
            continue
        if cmd == "M" or cmd == "L":
            cur.append((float(toks[i]), float(toks[i + 1]))); i += 2
        elif cmd == "C":
            p0 = np.array(cur[-1]); a, b, c = (np.array([float(toks[i + 2 * k]), float(toks[i + 2 * k + 1])]) for k in range(3))
            for t in np.linspace(0, 1, 13)[1:]:
                cur.append(tuple((1 - t) ** 3 * p0 + 3 * (1 - t) ** 2 * t * a + 3 * (1 - t) * t * t * b + t ** 3 * c))
            i += 6
    return out


def _badge_layers():
    """The Nusa badge from nusa-badge-colour.svg: [(shape, fill)] in drawing order."""
    svg = (BRAND / "nusa-badge-colour.svg").read_text()
    out = []
    for m in re.finditer(r"<(path|circle)\s([^>]*)/>", svg):
        a = dict(re.findall(r'([\w-]+)="([^"]*)"', m.group(2)))
        if m.group(1) == "circle":
            g = Point(float(a["cx"]), float(a["cy"])).buffer(float(a["r"]), 32)
        else:
            g = unary_union(_path_polys(a["d"]))
        out.append((g, a["fill"].lower()))
    return out


def _fit(shapes, ref):
    """Flip y up and scale so ref is 1 unit tall, centred."""
    b = ref.bounds
    k = 1 / (b[3] - b[1])
    return [sscale(translate(sscale(g, 1, -1, origin=(0, 0)), -(b[0] + b[2]) / 2, (b[1] + b[3]) / 2), k, k, origin=(0, 0))
            for g in shapes]


FILLS = {"#e8b23a": "badge_gold", "#12463a": "badge_green", "#d7262e": "badge_red", "#a9c2b6": "badge_mint",
         "#f4efe4": "badge_cream"}


def badge_colour():
    """Each colour of the badge as its own flat shape, the upper layers cut out of the lower."""
    layers = _badge_layers()
    out = []
    for i, (g, fill) in enumerate(layers):
        above = unary_union([h for h, _f in layers[i + 1:]]) if i + 1 < len(layers) else None
        if above is not None:
            g = g.difference(above.buffer(1.0))
        out.append((g, FILLS[fill]))
    whole = layers[0][0]
    shapes = _fit([g for g, _m in out] + [whole], whole)
    return [(q, m) for q, (_g, m) in zip(shapes[:-1], out) if not q.is_empty]


def badge_chrome():
    """The chrome badge: the rim of the drop, the sun and the mountains, each kept apart by a fine gap."""
    layers = _badge_layers()
    drop, inner, sun, hills, peak = (g for g, _f in layers)
    rim = drop.difference(inner.buffer(1.1))
    hills = hills.difference(peak.buffer(1.1))
    whole = unary_union([rim, sun, hills, peak])
    return _fit([whole], drop)[0]


def name_shape():
    """Fox in the brand font, leaning like the brand's wordmark, 1 unit = its cap height."""
    gl = json.loads((HERE / "glyphs.json").read_text())
    g = unary_union([Polygon(p[0], p[1:]) for p in gl["pieces"]])
    g = skew(g, xs=12, origin=(0, 0))
    b = g.bounds
    g = translate(g, -(b[0] + b[2]) / 2, -gl["cap"] / 2)
    return sscale(g, 1 / gl["cap"], 1 / gl["cap"], origin=(0, 0))


def _flat(ref, g, size, fr, a, b, up, name, d0, d1, mat, step=0.004):
    """Lay shape g (right, up, 1 unit = size) flat on the panel that fr's ray at (a, b) meets."""
    pf = blend.panel_frame(ref, fr, a, b, up)
    return blend.lay(ref, name, sscale(g, size, size, origin=(0, 0)), pf, d0, d1, mat, step)


BADGE_T = 0.006       # badges stand proud of the panel by this much
NAME_T = 0.003


def badge(ref, style, size, fr, a, b, up, name):
    if style == "chrome":
        return _flat(ref, badge_chrome(), size, fr, a, b, up, name, 0.0012, BADGE_T, "chrome", 0.002)
    obs = []
    for i, (g, m) in enumerate(badge_colour()):
        obs += _flat(ref, g, size, fr, a, b, up, f"{name}_{i}", 0.0012, BADGE_T + 0.0005 * i, m, 0.002)
    return obs


def options(ref):
    """Badge and name options, named opt_<group>_<n>_<i>. Each lies flat on its panel. Odd
    numbers are the chrome badge, even the colour badge, at the same spot."""
    nm = name_shape()
    up, fwd = (0, 0, 1), (-1, 0, 0)
    out = []

    def add(group, n, objs):
        for i, ob in enumerate(objs):
            ob.name = f"opt_{group}_{n}_{i}"
            out.append(ob)
    gz = [p[1] for p in R["grille"]]
    bonnet_x = NOSE - 0.30
    gate_z, gate_low = R["gate_z"], R["gate_low"]
    for k, style in ((0, "chrome"), (1, "colour")):
        # front: on the nose under the upper grille's middle (the reference's spot), or on the bonnet's nose
        add("frontbadge", 1 + k, badge(ref, style, 0.07, front_fr(), 0, min(gz) - 0.03, up, "fb"))
        add("frontbadge", 3 + k, badge(ref, style, 0.065, top_fr(), bonnet_x, 0, fwd, "fb"))
        # rear: on the tailgate just under the glass (the reference's spot), or low on the tailgate
        add("rearbadge", 1 + k, badge(ref, style, 0.07, rear_fr(), 0, gate_z, up, "rb"))
        add("rearbadge", 3 + k, badge(ref, style, 0.06, rear_fr(), 0, gate_low, up, "rb"))
    cap = 0.03
    # name: on the tailgate's lower right, or low on both doors behind their front edge
    add("name", 1, _flat(ref, nm, cap, rear_fr(), -0.30, gate_low, up, "nm", 0.0012, NAME_T, "chrome"))
    q = []
    xd = R["front_door"][2][0] - 0.25
    for sgn in (1, -1):
        q += _flat(ref, nm, cap * 1.6, side_fr(sgn), xd, R["door_bottom"] + 0.10, up, "nm", 0.0012, NAME_T, "chrome")
    add("name", 2, q)
    return out
