"""The Daruma's parts: lamps, grilles, fog lamps, sill strips, glass, shut lines, front doors,
mirrors, interior, wheels, badge and name. Every shape and size here is the Daruma's own, from
rows.json, kept plain and smooth. Runs inside Blender's Python module, called by export.py."""
import json
import re
import sys
from pathlib import Path

import numpy as np
from shapely.affinity import scale as sscale, translate
from shapely.geometry import Point, Polygon, box
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
    "body": ((0.98, 0.58, 0.04), 0.7, 1.0, None),
    "trim": ((0.03, 0.03, 0.035), 0.45, 1.0, None),
    "grille": ((0.035, 0.035, 0.04), 0.25, 1.0, None),
    "glass": ((0.07, 0.09, 0.11), 0.92, 0.45, None),
    "head_glass": ((0.92, 0.94, 0.96), 0.95, 0.22, None),
    "tail_glass": ((0.55, 0.03, 0.04), 0.92, 0.55, None),
    "housing": ((0.06, 0.06, 0.065), 0.5, 1.0, None),
    "reflector": ((0.8, 0.81, 0.83), 0.97, 1.0, None),
    "lamp_head": ((0.95, 0.95, 0.92), 0.6, 1.0, (1.0, 0.97, 0.9)),
    "lamp_tail": ((0.5, 0.03, 0.04), 0.5, 1.0, (1.0, 0.06, 0.04)),
    "lamp_brake": ((0.62, 0.04, 0.05), 0.5, 1.0, (1.0, 0.04, 0.03)),
    "chrome": ((0.87, 0.88, 0.9), 0.98, 1.0, None),
    "badge_black": ((0.05, 0.045, 0.04), 0.6, 1.0, None),
    "badge_orange": ((0.93, 0.42, 0.12), 0.6, 1.0, None),
    "badge_cream": ((0.95, 0.91, 0.85), 0.6, 1.0, None),
    "interior": ((0.09, 0.09, 0.1), 0.15, 1.0, None),
    "seat": ((0.14, 0.13, 0.125), 0.2, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.74, 0.75, 0.77), 0.75, 1.0, None),
    "dark_metal": ((0.16, 0.16, 0.17), 0.8, 1.0, None),
}
LAMPS = {"Headlights": ["lamp_head"], "Tail lights": ["lamp_tail"], "Brake lights": ["lamp_brake"]}
GROUPS = {"frontbadge": "Front badge", "rearbadge": "Rear badge", "name": "Name"}


# ---------------------------------------------------------------- frames
def front_fr():
    return blend.frame((NOSE, 0, 0), (0, 1, 0), (0, 0, 1), (-1, 0, 0))


def rear_fr():
    return blend.frame((TAIL, 0, 0), (0, -1, 0), (0, 0, 1), (1, 0, 0))


def side_fr(sgn):
    return blend.frame((0, 1.2 * sgn, 0), (1, 0, 0), (0, 0, 1), (0, -sgn, 0))


def top_fr():
    return blend.frame((0, 0, 2.0), (1, 0, 0), (0, 1, 0), (0, 0, -1))


def poly(pts):
    return Polygon(pts).buffer(0)


def flip(g):
    """Mirror a 2D shape about its first axis (y for front and rear frames)."""
    return sscale(g, -1, 1, origin=(0, 0))


def both(g):
    return unary_union([g, flip(g)])


def both_plan(g):
    """A plan shape (x, y) joined to its mirror across the centre line."""
    return unary_union([g, sscale(g, 1, -1, origin=(0, 0))])


def soft(g, r):
    return g.buffer(-r, join_style=1).buffer(r, join_style=1)


def slope_fr(x0, z0, x1, z1):
    """A frame square to a panel sloping from (x0, z0) to (x1, z1) along the car, and a map from
    the top view (x, y) into it, so a shape drawn from above keeps its width once laid."""
    t = np.array([x1 - x0, 0.0, z1 - z0]); t /= np.linalg.norm(t)
    d = np.array([t[2], 0.0, -t[0]])
    if d[2] > 0:
        d = -d
    fr = blend.frame(np.array([x0, 0, z0]) - d * 0.3, t, (0, 1, 0), d)
    return fr, lambda g: sscale(translate(g, -x0, 0), 1 / t[0], 1, origin=(0, 0))


def belt(x):
    k, c = R["belt"]
    return k * x + c


def glass_fr(sgn):
    """A frame square to the side glass, which leans in from its foot to the roof edge, and a
    map from the side view (x, z) into it."""
    v = body.values(0.0)
    y0, z0, y1, z1 = v["wg"], v["zs"], v["wr"], v["zr"]
    a = np.arctan2(y0 - y1, z1 - z0)
    up = np.array([0.0, -np.sin(a) * sgn, np.cos(a)])
    out = np.array([0.0, np.cos(a) * sgn, np.sin(a)])
    fr = blend.frame(np.array([0.0, y0 * sgn, z0]) + out * 0.3, (1, 0, 0), up, -out)
    return fr, lambda g: sscale(translate(g, 0, -z0), 1, 1 / np.cos(a), origin=(0, 0))


# ---------------------------------------------------------------- lamps
# A lamp wraps round a corner of the body, so its outline is traced twice: from ahead (or behind)
# and from the side. A vertical plane at 45 degrees through the corner splits the lamp: ahead of
# it the end outline bounds the lamp, beside it the side outline does.

def _prism(name, shape, axis, lo, hi, sgn=1):
    """A closed solid: a flat shape extruded along a build axis from lo to hi. shape is (a, b)
    in the view's own coordinates: front or rear (y, z), side (x, z), top (x, y)."""
    pts, tris = blend.triangulate(shape, 0.02)
    pts = np.asarray(pts, float)
    n = len(pts)
    def place(d):
        if axis == "x":
            return np.column_stack([np.full(n, d), pts[:, 0] * sgn, pts[:, 1]])
        if axis == "y":
            return np.column_stack([pts[:, 0], np.full(n, d * sgn), pts[:, 1]])
        return np.column_stack([pts[:, 0], pts[:, 1] * sgn, np.full(n, d)])
    verts = np.vstack([place(lo), place(hi)])
    count = {}
    for t in tris:
        for e in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            count[tuple(sorted(e))] = count.get(tuple(sorted(e)), 0) + 1
    faces = [tuple(t[::-1]) for t in tris] + [(t[0] + n, t[1] + n, t[2] + n) for t in tris]
    for t in tris:
        for a, b in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            if count[tuple(sorted((a, b)))] == 1:
                faces.append((a, b, b + n, a + n))
    return blend.make(name, verts, faces, "housing")


def _half_space(name, corner, normal, size=3.0):
    """A big box filling the side of the vertical plane through corner (x, y) that normal (x, y)
    points to."""
    n = np.array([normal[0], normal[1], 0.0]); n /= np.linalg.norm(n)
    t = np.array([-n[1], n[0], 0.0])
    c = np.array([corner[0], corner[1], 0.0])
    v, f = geom.box(0, size, -size, size, -size, size)
    v = np.asarray(v, float)
    v = c + np.outer(v[:, 0], n) + np.outer(v[:, 1], t) + np.outer(v[:, 2], (0, 0, 1))
    return blend.make(name, v, f, "housing")


def lamp_volume(name, ends, side, corner, end_dir, sgn, x_lo, x_hi, shrink=0.0):
    """The solid inside one lamp's traced outlines: ends (y, z) seen from ahead or behind, side
    (x, z), each shrunk by shrink. corner (x, y) is where the lamp turns round the body; end_dir
    is +1 for a lamp at the nose, -1 at the tail."""
    sh = lambda p: poly(p).buffer(-shrink) if shrink else poly(p)
    c = (corner[0], corner[1] * sgn)
    a = _prism(name, sh(ends), "x", x_lo, x_hi, sgn)
    blend.boolean(a, _half_space(name + "_h", c, (end_dir, -sgn)), "INTERSECT")
    b = _prism(name + "_s", sh(side), "y", 0.0, 1.5, sgn)
    blend.boolean(b, _half_space(name + "_hs", c, (-end_dir, sgn)), "INTERSECT")
    blend.boolean(a, b, "UNION")
    return a


LAMP_DEPTH = 0.035    # how deep each lamp's pocket sinks into the body


class Skins:
    """Copies of the plain skin pushed in by a few depths, made once and shared by the lamps."""

    def __init__(self, plain):
        self.plain, self.made = plain, {}

    def at(self, d):
        if d not in self.made:
            c = blend.inflate(self.plain, -d)
            c.hide_render = True
            self.made[d] = c
        return self.made[d]

    def copy(self, d, name):
        src = self.at(d)
        c = src.copy(); c.data = src.data.copy(); c.name = name
        import bpy
        bpy.context.scene.collection.objects.link(c)
        return c

    def drop(self):
        import bpy
        for c in self.made.values():
            bpy.data.objects.remove(c)
        self.made = {}


def lamp(skins, name, ends, side, corner, end_dir, sgn, x_lo, x_hi, lens_mat, cutters, out):
    """One lamp: a pocket cut into the skin, its floor and walls dark, and a clear lens lying just
    under the skin's own surface across it."""
    pocket = lamp_volume(f"cut_{name}", ends, side, corner, end_dir, sgn, x_lo, x_hi)
    blend.boolean(pocket, skins.copy(LAMP_DEPTH, "deep"), "DIFFERENCE")
    cutters.append(pocket)
    lens = lamp_volume(f"{name}_glass", ends, side, corner, end_dir, sgn, x_lo, x_hi, shrink=0.002)
    blend.boolean(lens, skins.copy(0.002, "outer"), "INTERSECT")
    blend.boolean(lens, skins.copy(0.006, "inner"), "DIFFERENCE")
    for p in lens.data.polygons:
        p.material_index = 0
    lens.data.materials[0] = __import__("bpy").data.materials[lens_mat]
    out.append(lens)


def headlamps(ref, skins, cutters, out):
    fr = front_fr()
    xs = [p[0] for p in R["head_side"]]
    for sgn, side in ((1, "l"), (-1, "r")):
        lamp(skins, f"head_{side}", R["head"], R["head_side"], R["head_corner"], 1, sgn, min(xs) - 0.05, NOSE + 0.2,
             "head_glass", cutters, out)
        # our own interior: two round reflector bowls, a lamp in each, on the lamp's front face
        hd = poly(R["head"])
        b = hd.bounds
        for i, (k, r) in enumerate(((0.62, 0.042), (0.83, 0.034))):
            c = Point(b[0] + k * (b[2] - b[0]), b[1] + 0.52 * (b[3] - b[1]))
            cell = c.buffer(r, 32).intersection(hd.buffer(-0.008))
            cell = cell if sgn > 0 else flip(cell)
            out += blend.lay(ref, f"head_reflector_{side}{i}", cell.buffer(0.007).difference(cell), fr, -0.03, -0.024,
                             "reflector", 0.004, along_ray=True)
            out += blend.lay(ref, f"lamp_head_{side}{i}", cell.buffer(-0.002), fr, -0.03, -0.026, "lamp_head", 0.005,
                             along_ray=True)


def taillamps(ref, skins, cutters, out):
    fr = rear_fr()
    xs = [p[0] for p in R["tail_side"]]
    for sgn, side in ((1, "l"), (-1, "r")):
        lamp(skins, f"tail_{side}", R["tail"], R["tail_side"], R["tail_corner"], -1, sgn, TAIL - 0.2, max(xs) + 0.05,
             "tail_glass", cutters, out)
        # our own interior: the upper half glows as the tail lamp, a round brake lamp low and outboard
        tl = poly(R["tail"])
        b = tl.bounds
        tail = tl.intersection(box(b[0], b[1] + 0.45 * (b[3] - b[1]), b[2], b[3])).buffer(-0.012, join_style=1)
        brake = Point(b[0] + 0.72 * (b[2] - b[0]), b[1] + 0.27 * (b[3] - b[1])).buffer(0.032, 32).intersection(
            tl.buffer(-0.01))
        put = (lambda g: flip(g)) if sgn > 0 else (lambda g: g)     # rear frame: u runs to the right
        out += blend.lay(ref, f"lamp_tail_{side}", put(tail), fr, -0.03, -0.026, "lamp_tail", 0.006, along_ray=True)
        out += blend.lay(ref, f"lamp_brake_{side}", put(brake), fr, -0.03, -0.026, "lamp_brake", 0.005, along_ray=True)


def stop_lamp(ref, out):
    """The high stop lamp across the top of the rear glass."""
    s = both(poly(R["stop"]))
    out += blend.lay(ref, "lamp_brake_high", soft(s, 0.006), rear_fr(), -0.001, 0.004, "lamp_brake", 0.006)


# ---------------------------------------------------------------- front
def front_parts(ref, cutters, out):
    fr = front_fr()
    # upper grille under the bonnet's front edge, in a chrome surround, three slats
    up = soft(both(poly(R["grille_up"])), 0.006)
    cutters += blend.lay(ref, "cut_grille_up", up, fr, -0.035, 0.006, "grille", 0.01, along_ray=True)
    out += blend.lay(ref, "grille_up_back", up.buffer(-0.002), fr, -0.034, -0.03, "grille", 0.01, along_ray=True)
    out += blend.lay(ref, "grille_up_chrome", up.buffer(0.009, join_style=1).difference(up.buffer(-0.001)), fr,
                     -0.002, 0.004, "chrome", 0.006)
    b = up.bounds
    for i, z in enumerate(R["grille_up_slats"]):
        bar = box(b[0] - 0.01, z - 0.004, b[2] + 0.01, z + 0.004).intersection(up.buffer(0.004))
        out += blend.lay(ref, f"grille_slat_{i}", bar, fr, -0.022, -0.006, "chrome", 0.008, along_ray=True)
    # lower grille in the bumper: two slats and three posts
    lo = soft(both(poly(R["grille_low"])), 0.008)
    cutters += blend.lay(ref, "cut_grille_low", lo, fr, -0.04, 0.006, "grille", 0.012, along_ray=True)
    out += blend.lay(ref, "grille_low_back", lo.buffer(-0.002), fr, -0.039, -0.035, "grille", 0.012, along_ray=True)
    b = lo.bounds
    bars = [box(b[0] - 0.01, z - 0.005, b[2] + 0.01, z + 0.005) for z in R["grille_low_slats"]]
    for u in R["grille_low_bars"]:
        bars += [box(u - 0.005, b[1] - 0.01, u + 0.005, b[3] + 0.01), box(-u - 0.005, b[1] - 0.01, -u + 0.005, b[3] + 0.01)]
    out += blend.lay(ref, "grille_low_bars", unary_union(bars).intersection(lo.buffer(0.004)), fr, -0.03, -0.008,
                     "trim", 0.008, along_ray=True)
    # fog lamp pockets in the bumper's corners, a round lamp in each
    fu, fz, fr_r = R["fog_lamp"]
    for sgn, side in ((1, "l"), (-1, "r")):
        pk = soft(poly(R["fog"]), 0.006)
        lampd = Point(fu, fz).buffer(fr_r, 40)
        if sgn < 0:
            pk, lampd = flip(pk), flip(lampd)
        cutters += blend.lay(ref, f"cut_fog_{side}", pk, fr, -0.035, 0.006, "grille", 0.008, along_ray=True)
        out += blend.lay(ref, f"fog_back_{side}", pk.buffer(-0.002).difference(lampd.buffer(0.004)), fr, -0.034, -0.03,
                         "grille", 0.008, along_ray=True)
        out += blend.lay(ref, f"fog_reflector_{side}", lampd.buffer(0.004).difference(lampd.buffer(-0.004)), fr, -0.034,
                         -0.024, "reflector", 0.004, along_ray=True)
        out += blend.lay(ref, f"lamp_head_fog_{side}", lampd.buffer(-0.005), fr, -0.032, -0.026, "lamp_head", 0.004,
                         along_ray=True)


def sills(ref, out):
    """The black sill strip under the doors, from arch to arch."""
    xa, xb, z1 = R["sill"]
    x0, x1 = max(xa, xb), min(xa, xb)
    zb = min(s["zb"] for s in body.ST if x1 <= s["x"] <= x0)
    for sgn, side in ((1, "l"), (-1, "r")):
        out += blend.lay(ref, f"sill_{side}", soft(box(x1, zb - 0.06, x0, z1), 0.01), side_fr(sgn), -0.002, 0.005,
                         "trim", 0.012)


# ---------------------------------------------------------------- glass and trim
SIDE_BAND = 0.016     # black frame round the side windows
GLASS_BAND = 0.02     # black band round the windscreen and the rear window


def _even_curve(pts, deg):
    """One even curve through traced points (x, z), by least squares."""
    p = np.asarray(pts, float)
    return np.poly1d(np.polyfit(p[:, 0], p[:, 1], deg))


def dlo_line():
    """The side window opening rebuilt from plain fits: the belt straight, the top one even curve
    from the A-pillar's foot to the D-pillar's head, and the rear edge straight."""
    d = np.array(R["dlo"], float)
    top = d[1:15]                        # A-pillar foot to the rear top corner
    f = _even_curve(top, 4)
    xs = np.linspace(top[0, 0], top[-1, 0], 40)
    upper = [[x, float(f(x))] for x in xs]
    x_rear_low = d[16, 0]
    x_front = d[0, 0]
    return np.array([[x_front, belt(x_front)]] + upper + [[x_rear_low, belt(x_rear_low)]])


def dlo_shapes():
    """The side window opening, its glass (split by the quarter light's post, the B-pillar and the
    rear door's post) and the black band."""
    dlo = soft(poly(dlo_line()), 0.014)
    glass = dlo.buffer(-SIDE_BAND, join_style=1)
    b0, b1 = R["b_pillar"]
    posts = [box(b0, -1, b1, 3)]
    for (x0, z0), (x1, z1) in (R["quarter"], R["divider"]):
        posts.append(Polygon([(x0 + 0.012, z0 + 0.2), (x0 - 0.012, z0 + 0.2), (x1 - 0.012, z1 - 0.2), (x1 + 0.012, z1 - 0.2)]))
    glass = glass.difference(unary_union(posts).buffer(0.002))
    glass = unary_union([soft(p, 0.008) for p in getattr(glass, "geoms", [glass]) if p.area > 1e-4])
    return dlo, glass, dlo.difference(glass.buffer(-0.004))


def _window(x_base_mid, x_base_edge, w_base, x_top_mid, x_top_edge, w_top, rear=False):
    """A window's outline in plan: its foot and head each one even curve across the car (bowed
    by the traced middle and corner), its sides straight."""
    n = 24
    u = np.linspace(-1, 1, n)
    foot = [(x_base_mid + (x_base_edge - x_base_mid) * t * t, w_base * t) for t in u]
    head = [(x_top_mid + (x_top_edge - x_top_mid) * t * t, w_top * t) for t in u[::-1]]
    return poly(foot + head)


def screen_shapes():
    s = R["screen"]
    o = soft(_window(s["x_base_mid"], s["x_base_edge"], s["w_base"], s["x_top_mid"], s["x_top_edge"], s["w_top"]), 0.04)
    g = o.buffer(-GLASS_BAND, join_style=1)
    return o, g, o.difference(g.buffer(-0.004))


def rear_shapes():
    s = R["rear_glass"]
    o = soft(_window(s["x_base_mid"], s["x_base_edge"], s["w_base"], s["x_top_mid"], s["x_top_edge"], s["w_top"]), 0.05)
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
    """A polyline with each corner replaced by a smooth bend of radius about r."""
    p = [np.asarray(q, float) for q in pts]
    out = [p[0]]
    for a, b, c in zip(p[:-2], p[1:-1], p[2:]):
        u, v = (a - b) / np.linalg.norm(a - b), (c - b) / np.linalg.norm(c - b)
        ang = np.arccos(np.clip(u @ v, -1, 1))
        if ang > np.pi - 1e-3:
            out.append(b); continue
        d = min(r / np.tan(ang / 2), 0.45 * np.linalg.norm(a - b), 0.45 * np.linalg.norm(c - b))
        p0, p1 = b + u * d, b + v * d
        for t in np.linspace(0, 1, n):
            out.append((1 - t) ** 2 * p0 + 2 * (1 - t) * t * b + t * t * p1)
    out.append(p[-1])
    return [q.tolist() for q in out]


def b_x(z):
    """The B shut line's x at height z: straight down the middle of the B-pillar's black."""
    (x0, z0), (x1, z1) = R["b_shut"]
    return x0 + (x1 - x0) * (z - z0) / (z1 - z0)


def b_path():
    zt = belt(b_x(belt(0.0))) + 0.03
    return [[b_x(zt), zt], [b_x(R["door_bottom"]), R["door_bottom"]]]


def front_path():
    """The front door's front edge from the quarter light's foot down the wing's back, then its
    bottom straight back to the B line."""
    pts = [list(p) for p in R["front_door"]]
    zb = R["door_bottom"]
    return fillet(pts + [[b_x(zb), zb]], R["shut_round"])


def rear_path():
    """The rear door's rear edge: straight down from the window's corner, round the rear arch on
    one circle, then its bottom straight forward to the B line."""
    pts = [list(p) for p in R["rear_door"]]
    xc, zc, r = W["rear_x"], W["r"], R["rear_arch_r"]
    zb = R["door_bottom"]
    a0 = np.arctan2(pts[-1][1] - zc, pts[-1][0] - xc)
    a1 = np.pi + np.arcsin((zc - zb) / r)       # round the front of the arch, down to the bottom line
    arc = [[xc + r * np.cos(a), zc + r * np.sin(a)] for a in np.linspace(a0, a1, 28)]
    line = fillet(pts + [arc[1]], R["shut_round"])[:-1]
    tail = fillet([arc[-2], arc[-1], [b_x(zb), zb]], R["shut_round"])
    return line + arc[1:-1] + tail


def bonnet_path():
    """The bonnet line in plan: from the windscreen's foot inboard of its corner, out to the
    bonnet's back corner, forward along the wing and the lamp, across the nose and back."""
    half = np.array(R["bonnet"])
    path = np.vstack([half, (half[-2::-1] * [1, -1])])
    return geom.catmull_open(path, 6)


def lid_paths():
    """The tailgate's shut line: across the roof at the spoiler's front (seen from above) and,
    seen from behind, down beside the rear glass, along the lamps' inner edges and across the
    bumper's top."""
    xr = R["lid_roof"]
    L = np.array(R["lid"])
    u0 = L[0, 0]
    plan_line = fillet([[xr - 0.09, -u0 - 0.006], [xr, -u0], [xr, u0], [xr - 0.09, u0 + 0.006]], 0.03)
    # seen from behind the rear frame's first axis points to the car's right
    down = np.vstack([L, L[-2::-1] * [-1, 1]])        # down the left, across, up the right
    return plan_line, geom.catmull_open(down, 6)


def grooves(ref):
    """Shut lines as V grooves to cut: doors, bonnet, tailgate."""
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        fr = side_fr(sgn)
        obs.append(blend.groove(ref, f"g_fdoor_{side}", front_path(), fr))
        obs.append(blend.groove(ref, f"g_bdoor_{side}", b_path(), fr))
        obs.append(blend.groove(ref, f"g_rdoor_{side}", rear_path(), fr))
    obs.append(blend.groove(ref, "g_bonnet", bonnet_path(), top_fr()))
    plan_line, down = lid_paths()
    obs.append(blend.groove(ref, "g_lid_roof", plan_line, top_fr()))
    obs.append(blend.groove(ref, "g_lid", down, rear_fr()))
    return obs


# ---------------------------------------------------------------- front doors
DOOR_DEPTH = 0.05     # below the cabin floor the door takes the sill this deep
DOOR_WALL = 0.08      # above it, the whole wall: the cabin is hollow behind it
DOOR_GAP = 0.0015     # half the gap round the door, inside the shut line's groove
DOOR_OPEN = 65.0      # degrees the door swings out
FLOOR = 0.26


def door_outline():
    """The left front door seen from the side: inside its front edge, bottom and the B line,
    closed across the belt, and the window frame round the quarter light and the door glass back
    to the B-pillar's middle."""
    front = front_path()
    b = b_path()
    lower = Polygon(front + b[::-1]).buffer(0)
    dlo, _g, _t = dlo_shapes()
    bc = b_x(belt(b_x(R["door_bottom"])))
    frame = dlo.buffer(SIDE_BAND * 0.5, join_style=2).intersection(box(bc, -1, 5, 5))
    return unary_union([lower, frame]).buffer(0)


def door_hinge(sgn=1):
    """The hinge axis up the door's front edge, outside the skin, and the open angle. A negative
    angle about an axis pointing up swings the left door's back edge out. sgn: 1 left, -1 right."""
    o = door_outline()
    _x0, z0, x1, _z1 = o.bounds
    xf = max(p[0] for p in R["front_door"]) + 0.004
    zb = belt(xf)
    y = max(body.y_at(xf - 0.02, z) for z in np.linspace(z0 + 0.02, zb - 0.02, 20)) + 0.003
    return (xf, y * sgn, z0), (xf, y * sgn, zb), -DOOR_OPEN * sgn


def door(ref, shell, obs, sgn=1):
    """Cut the left (sgn 1) or right (sgn -1) front door free of the body and its window trim;
    its glass, card and mirror go with it."""
    side = "l" if sgn > 0 else "r"
    o = door_outline()
    regions = [(o.intersection(box(-5, -1, 5, FLOOR + 0.03)), DOOR_DEPTH),
               (o.intersection(box(-5, FLOOR + 0.02, 5, 5)), DOOR_WALL)]
    trim = [ob for ob in obs if ob.name == f"window_trim_{side}"]
    roles = {f"side_glass_{side}": "glass", f"side_glass_{side}_1": "glass", f"side_glass_{side}_2": "glass",
             f"side_glass_{side}_3": "glass", f"mirror_{side}": "mirror", f"mirror_glass_{side}": "mirror_glass",
             f"door_card_{side}": "card"}
    return blend.free_door(ref, side_fr(sgn), regions, DOOR_GAP, shell, trim, roles, o, name=f"door_{side}")


# ---------------------------------------------------------------- mirrors
MIRROR_SWEEP = 0.12   # radians the housing turns back from square to the car


def _pod(name, y0, y1, z0, z1, x0, x1, sgn, mat="body", r=0.03, taper=0.75):
    """A rounded housing: its face (y, z) a rounded box, lofted from x0 (the back, full size) to
    x1 (the nose, shrunk by taper towards its outer top), rounded along the way."""
    face = soft(box(y0, z0, y1, z1), r)
    ring = body.even(np.array(face.exterior.coords)[:-1], 96)
    cy, cz = y1, (z0 + z1) / 2
    rings = []
    for t in np.linspace(0, 1, 9):
        k = 1 - (1 - taper) * t ** 2
        rr = np.column_stack([cy + (ring[:, 0] - cy) * k, cz + (ring[:, 1] - cz) * k])
        rings.append(np.column_stack([np.full(96, x0 + (x1 - x0) * t), rr[:, 0] * sgn, rr[:, 1]]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def _sweep(ob, sgn):
    from mathutils import Matrix
    m = R["mirror"]
    p = Matrix.Translation(((m["x_back"] + m["x_front"]) / 2, sgn * m["y_in"], 0))
    ob.data.transform(p @ Matrix.Rotation(sgn * MIRROR_SWEEP, 4, "Z") @ p.inverted())
    return ob


def mirrors():
    """A body-coloured housing on a short arm from the door's front top corner, and its glass in a
    recess in the housing's back."""
    m = R["mirror"]
    xb, xf = m["x_back"], m["x_front"]
    y0, y1, z0, z1 = m["y_in"] + 0.03, m["y_out"], m["z0"], m["z1"]
    root = body.y_at(xb + 0.05, belt(xb + 0.05) - 0.03) - 0.02
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        house = _pod(f"mirror_{side}", y0, y1, z0, z1, xb, xf, sgn)
        arm = _pod(f"mirror_arm_{side}", root, y0 + 0.03, belt(xb) - 0.03, z0 + 0.05, xb + 0.02, xf - 0.03, sgn,
                   r=0.012, taper=0.9)
        blend.boolean(house, arm, "UNION")
        cut = _pod(f"cut_mirror_{side}", y0 + 0.008, y1 - 0.008, z0 + 0.008, z1 - 0.008, xb - 0.03, xb + 0.006, sgn,
                   r=0.022, taper=1.0)
        blend.boolean(house, cut, "DIFFERENCE")
        glass = _pod(f"mirror_glass_{side}", y0 + 0.0095, y1 - 0.0095, z0 + 0.0095, z1 - 0.0095, xb + 0.0015,
                     xb + 0.0045, sgn, "mirror_glass", r=0.02, taper=1.0)
        obs += [_sweep(house, sgn), _sweep(glass, sgn)]
    return obs


def arches():
    """Arch cutters and the wheel tubs kept out of the cabin."""
    cut, tubs = [], []
    yc = W["outer_y"] - W["width"] / 2
    for x in (W["front_x"], W["rear_x"]):
        for sgn in (1, -1):
            v, f = geom.disc((x, (yc + 0.12) * sgn, W["r"]), (0, 1, 0), W["arch_r"], 0.50, n=96)
            cut.append(blend.make("arch", v, f, "interior"))
            v, f = geom.disc((x, (yc + 0.10) * sgn, W["r"]), (0, 1, 0), W["arch_r"] + 0.03, 0.62, n=96)
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


STEER = (0.42, 0.36, 0.86)     # the wheel's centre, in front of the left seat
STEER_TILT = 32.0               # degrees the wheel's face turns up from upright


def steering():
    """A round wheel on the left, square to its column, with three spokes and the column."""
    from mathutils import Matrix
    r, t = 0.185, 0.015
    v, f = geom.lathe([(r - t + t * np.cos(a), t * np.sin(a)) for a in np.linspace(0, 2 * np.pi, 12, endpoint=False)], 64, axis="y")
    rim = blend.make("steering", v, f, "interior")
    parts = [geom.box(-0.012, 0.012, -0.008, 0.008, -r + 0.01, -0.03), geom.box(-r + 0.01, -0.03, -0.008, 0.008, -0.014, 0.014),
             geom.box(0.03, r - 0.01, -0.008, 0.008, -0.014, 0.014)]
    obs = [rim] + [blend.make("spoke", *p, "interior") for p in parts]
    v, f = geom.disc((0, 0, 0), (0, 1, 0), 0.05, 0.05, n=32)
    obs.append(blend.make("boss", v, f, "interior"))
    v, f = geom.disc((0, -0.17, 0), (0, 1, 0), 0.025, 0.30, n=24)
    obs.append(blend.make("column", v, f, "interior"))
    wheel = blend.union_all(obs, "steering")
    wheel.data.transform(Matrix.Translation(STEER) @ Matrix.Rotation(np.radians(STEER_TILT), 4, "Y")
                         @ Matrix.Rotation(np.pi / 2, 4, "Z"))
    return wheel


def interior():
    x0 = R["screen"]["x_base_mid"] - 0.12
    belt_z = belt(0.0)
    obs = [_loft("firewall", x0 - 0.010, x0 - 0.004, lambda x: box(-0.8, FLOOR + 0.002, 0.8, 3), n=2),
           _loft("dash", x0 - 0.0125, 0.52, lambda x: box(-0.8, belt_z - 0.14, 0.8, belt_z - 0.14 + 0.16 * (1 - (x0 - x) / 0.55) + 0.05),
                 n=10, gap=0.006)]
    bc = b_x(belt(0.0))
    xf = min(p[0] for p in R["front_door"]) - 0.04
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, FLOOR + 0.08, 1.2, belt(x) - 0.02) if s > 0 else box(-1.2, FLOOR + 0.08, -0.3, belt(x) - 0.02)).intersection(
            body.inside(x, 0.035 + 0.003)).difference(body.inside(x, 0.035 + 0.022))
        obs.append(_loft(f"door_card_{side}", bc + 0.006, xf, clip, n=10))
        obs.append(_loft(f"door_card_{side}_rear", W["rear_x"] + R["rear_arch_r"] + 0.03, bc - 0.006, clip, n=8))
        yc = 0.36 * sgn
        obs.append(_rbox(f"seat_base_{side}", -0.16, 0.34, yc - 0.24, yc + 0.24, FLOOR + 0.002, FLOOR + 0.26))
        obs.append(_rbox(f"seat_back_{side}", -0.30, -0.18, yc - 0.23, yc + 0.23, FLOOR + 0.261, FLOOR + 0.78, lean=0.22))
        obs.append(_rbox(f"headrest_{side}", -0.43, -0.34, yc - 0.12, yc + 0.12, FLOOR + 0.781, FLOOR + 0.90, r=0.035, lean=0.1))
    obs.append(_rbox("bench_base", -0.92, -0.42, -0.46, 0.46, FLOOR + 0.004, FLOOR + 0.25))
    obs.append(_rbox("bench_back", -1.06, -0.90, -0.46, 0.46, FLOOR + 0.235, FLOOR + 0.74, lean=0.2))
    obs.append(steering())
    xm = R["screen"]["x_top_mid"] - 0.04
    roof_in = body.inside(xm, 0.035).bounds[3]
    top = roof_in - 0.06
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.11, 0.11, top - 0.05, top, r=0.018, mat="interior")
    stem = _rbox("stem", xm - 0.016, xm - 0.004, -0.012, 0.012, top - 0.015, roof_in - 0.0008, r=0.004, mat="interior")
    obs.append(blend.union_all([m, stem], "mirror_rear"))
    return obs


# ---------------------------------------------------------------- wheels
WINDOWS = 6           # triangular windows round the wheel's flat face


def wheels():
    """Flat faced alloys: a plain disc face with a ring of rounded triangular windows near the
    rim, and a small cap."""
    obs = []
    hw = W["width"] / 2
    yc = W["outer_y"] - hw
    rr = W["rim_r"]
    tyre = soft(box(rr, -hw, W["r"], hw), 0.032).union(box(rr, -hw + 0.012, rr + 0.03, hw - 0.012))
    tp = body.even(np.array(tyre.exterior.coords)[:-1], 96)
    rim = [(rr - 0.001, -hw + 0.012), (rr - 0.001, hw - 0.012), (rr - 0.012, hw - 0.004), (rr - 0.02, hw - 0.01),
           (rr - 0.028, hw - 0.022), (0.06, hw - 0.026), (0.045, hw - 0.018), (0.0, hw - 0.018), (0.0, hw - 0.045),
           (rr - 0.032, hw - 0.045), (rr - 0.02, -hw + 0.012)]
    # one window: a rounded triangle pointing in, between radii r0 and r1
    r0, r1, half = 0.085, rr - 0.035, np.radians(17)
    tri = soft(Polygon([(r0, 0), (r1 * np.cos(half), r1 * np.sin(half)), (r1 * np.cos(half), -r1 * np.sin(half))]), 0.01)
    for x, fb in ((W["front_x"], "f"), (W["rear_x"], "r")):
        for sgn, lr in ((1, "l"), (-1, "r")):
            def place(v, s=sgn):
                v = np.asarray(v, float)
                return np.column_stack([x + v[:, 0], (yc + v[:, 1]) * s, W["r"] + v[:, 2]])
            v, f = geom.lathe(tp, 96, axis="y")
            obs.append(blend.make(f"wheel_tyre_{fb}{lr}", place(v), f, "tyre"))
            v, f = geom.lathe(rim, 96, axis="y")
            if sgn < 0:
                f = [tuple(q[::-1]) for q in f]
            rimob = blend.make(f"wheel_rim_{fb}{lr}", place(v), f, "rim")
            cutters = []
            for k in range(WINDOWS):
                a = 2 * np.pi * k / WINDOWS
                c, s_ = np.cos(a), np.sin(a)
                ring = np.array(tri.exterior.coords)[:-1]
                pts = [(px * c - py * s_, px * s_ + py * c) for px, py in ring]
                rings = [np.array([(p[0], d, p[1]) for p in pts]) for d in (hw - 0.07, hw + 0.01)]
                v, f = geom.close_rings(rings)
                cutters.append(blend.make("window", place(v), f, "dark_metal"))
            blend.boolean(rimob, blend.union_all(cutters, "windows"), "DIFFERENCE")
            v, f = geom.disc((0, hw - 0.013, 0), (0, 1, 0), 0.04, 0.012, n=32)
            cap = blend.make(f"wheel_cap_{fb}{lr}", place(v), f, "dark_metal")
            v, f = geom.disc((0, hw - 0.075, 0), (0, 1, 0), rr - 0.04, 0.012, n=64)
            disc = blend.make(f"wheel_hub_{fb}{lr}", place(v), f, "dark_metal")
            obs += [rimob, cap, disc]
    return obs


# ---------------------------------------------------------------- badge and name
BRAND = HERE.parent.parent.parent / "src/renderer/assets/brands/tora"


def _badge_shapes(svg_name, colours):
    """The Tora badge from an SVG of the brand: (whole outline, [(shape, material)]), flat,
    centred, 1 unit tall, y up. Later shapes cover earlier ones; mirrored groups are drawn mirrored."""
    svg = (BRAND / svg_name).read_text()
    shapes, mirror = [], []
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
            shapes.append((Polygon(pts).buffer(0), colours(tok.group(2))))
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
    return fix(whole), out


def badge_colour():
    cols = {"#16130f": "badge_black", "#ee6a1f": "badge_orange", "#f3e9d8": "badge_cream"}
    return _badge_shapes("tora-badge-colour.svg", lambda c: cols[c])


def badge_chrome():
    return _badge_shapes("tora-badge-chrome.svg", lambda c: "badge_black" if c.startswith("#") else "chrome")


def name_shape():
    """Daruma in the brand font, 1 unit = its cap height, centred."""
    gl = json.loads((HERE / "glyphs.json").read_text())
    g = unary_union([Polygon(p[0], p[1:]) for p in gl["pieces"]])
    b = g.bounds
    g = translate(g, -(b[0] + b[2]) / 2, -gl["cap"] / 2)
    return sscale(g, 1 / gl["cap"], 1 / gl["cap"], origin=(0, 0))


def _flat(ref, g, size, fr, a, b, up, name, d0, d1, mat, step=0.004):
    """Lay shape g (right, up, 1 unit = size) flat on the panel that fr's ray at (a, b) meets."""
    pf = blend.panel_frame(ref, fr, a, b, up)
    return blend.lay(ref, name, sscale(g, size, size, origin=(0, 0)), pf, d0, d1, mat, step)


BADGE_RIM = 0.06      # the chrome outline round every badge, in badge heights


def badge(ref, style, size, fr, a, b, up, name):
    """A chrome outline round a dark base in the badge's shape, each colour on top, every layer a
    little apart so no faces touch."""
    outline, pieces = badge_chrome() if style == "chrome" else badge_colour()
    objs = _flat(ref, outline.buffer(BADGE_RIM, join_style=1), size, fr, a, b, up, name + "_rim", 0.0010, 0.0022,
                 "chrome", 0.002)
    objs += _flat(ref, outline, size, fr, a, b, up, name + "_base", 0.0032, 0.0038, "badge_black", 0.002)
    for i, (g, m) in enumerate(pieces):
        if m == "badge_black":
            continue
        g = g.buffer(-0.3 * 0.001 / size)
        if g.is_empty:
            continue
        objs += _flat(ref, g, size, fr, a, b, up, f"{name}_{i}", 0.0048, 0.0058, m, 0.002)
    return objs


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
    gz = [p[1] for p in R["grille_up"]]
    up_z = (max(gz) + min(gz)) / 2
    bonnet_x = max(p[0] for p in R["bonnet"]) - 0.16
    lid_z = R["lid"][-1][1] + 0.16
    for k, style in ((0, "chrome"), (1, "colour")):
        # front: in the middle of the upper grille (the reference's spot), or on the bonnet's nose
        add("frontbadge", 1 + k, badge(ref, style, 0.07, front_fr(), 0, up_z, up, "fb"))
        add("frontbadge", 3 + k, badge(ref, style, 0.06, top_fr(), bonnet_x, 0, fwd, "fb"))
        # rear: on the tailgate's middle between the lamps (the reference's spot), or just under the glass
        add("rearbadge", 1 + k, badge(ref, style, 0.07, rear_fr(), 0, lid_z, up, "rb"))
        add("rearbadge", 3 + k, badge(ref, style, 0.06, rear_fr(), 0, lid_z + 0.09, up, "rb"))
    cap = 0.03
    tl = poly(R["tail"]).bounds
    name_z = lid_z - 0.09
    # name: on the tailgate left of the badge (the reference's spot), right of it, or low on both front doors
    add("name", 1, _flat(ref, nm, cap, rear_fr(), -0.30, name_z, up, "nm", 0.0012, 0.0025, "chrome"))
    add("name", 2, _flat(ref, nm, cap, rear_fr(), 0.30, name_z, up, "nm", 0.0012, 0.0025, "chrome"))
    q = []
    wx = W["front_x"] - W["arch_r"] - 0.32
    for sgn in (1, -1):
        q += _flat(ref, nm, cap, side_fr(sgn), wx, R["door_bottom"] + 0.08, up, "nm", 0.0012, 0.0025, "chrome")
    add("name", 3, q)
    return out
