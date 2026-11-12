"""The Giga's parts: lamps, grilles, running lamps, vents, splitter, diffuser, exhausts, glass,
shut lines, doors, mirrors, skirts, rear wing, interior, wheels, badge and name. Every shape and
size here is the Giga's own, from rows.json, kept plain and smooth. Runs inside Blender's Python
module, called by export.py."""
import json
import re
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

MATERIALS = {
    # name: (rgb, spec, alpha, emit)
    "body": ((0.98, 0.58, 0.04), 0.75, 1.0, None),
    "trim": ((0.022, 0.022, 0.026), 0.5, 1.0, None),
    "grille": ((0.035, 0.035, 0.04), 0.2, 1.0, None),
    "glass": ((0.06, 0.08, 0.1), 0.92, 0.42, None),
    "head_glass": ((0.92, 0.94, 0.96), 0.95, 0.2, None),
    "tail_glass": ((0.45, 0.03, 0.05), 0.92, 0.5, None),
    "housing": ((0.06, 0.06, 0.065), 0.5, 1.0, None),
    "reflector": ((0.8, 0.81, 0.83), 0.97, 1.0, None),
    "lamp_head": ((0.95, 0.95, 0.93), 0.6, 1.0, (1.0, 0.98, 0.92)),
    "lamp_tail": ((0.5, 0.03, 0.04), 0.5, 1.0, (1.0, 0.06, 0.04)),
    "lamp_brake": ((0.6, 0.04, 0.05), 0.5, 1.0, (1.0, 0.04, 0.03)),
    "lamp_rain": ((0.55, 0.04, 0.05), 0.5, 1.0, (1.0, 0.05, 0.03)),
    "chrome": ((0.86, 0.87, 0.89), 0.98, 1.0, None),
    "exhaust": ((0.7, 0.68, 0.64), 0.92, 1.0, None),
    "badge_black": ((0.086, 0.075, 0.059), 0.6, 1.0, None),
    "badge_orange": ((0.93, 0.42, 0.12), 0.7, 1.0, None),
    "badge_cream": ((0.95, 0.91, 0.85), 0.6, 1.0, None),
    "interior": ((0.075, 0.075, 0.085), 0.15, 1.0, None),
    "seat": ((0.11, 0.11, 0.12), 0.2, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.2, 0.2, 0.215), 0.75, 1.0, None),
    "dark_metal": ((0.14, 0.14, 0.15), 0.8, 1.0, None),
}
LAMPS = {"Headlights": ["lamp_head"], "Tail lights": ["lamp_tail"], "Brake lights": ["lamp_brake"],
         "Rain light": ["lamp_rain"]}
GROUPS = {"frontbadge": "Front badge", "rearbadge": "Rear badge", "name": "Name"}
SHOW = {"Wing": ["wing"]}


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
    v = body.values(-0.4)
    y0, z0, y1, z1 = v["wg"], v["zs"], v["wr"], v["zr"]
    a = np.arctan2(y0 - y1, z1 - z0)
    up = np.array([0.0, -np.sin(a) * sgn, np.cos(a)])
    out = np.array([0.0, np.cos(a) * sgn, np.sin(a)])
    fr = blend.frame(np.array([0.0, y0 * sgn, z0]) + out * 0.3, (1, 0, 0), up, -out)
    return fr, lambda g: sscale(translate(g, 0, -z0), 1, 1 / np.cos(a), origin=(0, 0))


# ---------------------------------------------------------------- lamps
def head_shape():
    """The left headlamp seen from ahead, as traced: a slanted wedge rising outboard."""
    return soft(poly(R["head"]), 0.005)


def headlamps(ref, cutters, out):
    """Each headlamp sits on the nose's corner and runs back up the wing; seen from ahead it keeps
    its traced outline. Our own interior: two round projectors side by side in reflector cups,
    and a slim lamp strip along the lamp's foot."""
    fr = front_fr()
    for sgn, side in ((1, "l"), (-1, "r")):
        shp = head_shape()
        b = shp.bounds
        cz = b[1] + 0.034
        cups = [Point(b[2] - 0.05, cz + 0.004).buffer(0.032, 48), Point(b[2] - 0.12, cz).buffer(0.03, 48)]
        strip = shp.intersection(box(b[0], b[1], b[2], b[1] + 0.012)).buffer(-0.003, join_style=1)
        strip = max(getattr(strip, "geoms", [strip]), key=lambda g: g.area)
        g = (lambda q: q) if sgn > 0 else flip
        cutters += blend.lay(ref, f"cut_head_{side}", g(shp), fr, -0.05, 0.006, "housing", 0.006, along_ray=True)
        out += blend.lay(ref, f"head_back_{side}", g(shp.buffer(-0.002)), fr, -0.047, -0.043, "housing", 0.006, along_ray=True)
        for i, cup in enumerate(cups):
            out += blend.lay(ref, f"head_reflector_{side}{i}", g(cup.difference(cup.buffer(-0.009))), fr, -0.0422, -0.034,
                             "reflector", 0.004, along_ray=True)
            out += blend.lay(ref, f"lamp_head_{side}{i}", g(cup.buffer(-0.01)), fr, -0.0422, -0.037, "lamp_head", 0.004,
                             along_ray=True)
        out += blend.lay(ref, f"lamp_head_{side}2", g(soft(strip, 0.002)), fr, -0.0422, -0.037, "lamp_head", 0.003,
                         along_ray=True)
        out += blend.lay(ref, f"head_glass_{side}", g(shp.buffer(-0.002)), fr, -0.0055, -0.0025, "head_glass", 0.006,
                         along_ray=True)


def ring_shapes():
    """The two round tail lamps on the left of the tail, as traced."""
    return [Point(u, z).buffer(r, 64) for u, z, r in R["tail_rings"]]


def taillamps(ref, cutters, out):
    """Two round lamps each side; seen from behind they keep their traced circles. Our own
    interior: a tail lamp ring round a round brake lamp in each."""
    fr = rear_fr()
    for sgn, side in ((1, "l"), (-1, "r")):
        g = (lambda q: q) if sgn > 0 else flip
        for i, shp in enumerate(ring_shapes()):
            r = (shp.bounds[2] - shp.bounds[0]) / 2
            c = shp.centroid
            ring = c.buffer(r * 0.82, 64).difference(c.buffer(r * 0.6, 64))
            core = c.buffer(r * 0.5, 64)
            k = f"{side}{i}"
            cutters += blend.lay(ref, f"cut_tail_{k}", g(shp), fr, -0.035, 0.006, "housing", 0.006, along_ray=True)
            out += blend.lay(ref, f"tail_back_{k}", g(shp.buffer(-0.002)), fr, -0.034, -0.03, "housing", 0.006, along_ray=True)
            out += blend.lay(ref, f"lamp_tail_{k}", g(ring), fr, -0.029, -0.025, "lamp_tail", 0.004, along_ray=True)
            out += blend.lay(ref, f"lamp_brake_{k}", g(core), fr, -0.029, -0.025, "lamp_brake", 0.004, along_ray=True)
            out += blend.lay(ref, f"tail_glass_{k}", g(shp.buffer(-0.002)), fr, -0.0055, -0.0025, "tail_glass", 0.006,
                             along_ray=True)


# ---------------------------------------------------------------- front
def front_parts(ref, cutters, out):
    """The slim upper grille, the big mouth under the body-coloured bar, the running lamps in the
    bumper's corners and the black splitter across the nose."""
    fr = front_fr()
    up = half_shape(R["grille_up"], 0.003)
    mouth = half_shape(R["grille"], 0.006)
    cutters += blend.lay(ref, "cut_grille_up", up, fr, -0.04, 0.006, "grille", 0.006, along_ray=True)
    out += blend.lay(ref, "grille_up_back", up.buffer(-0.002), fr, -0.039, -0.035, "grille", 0.006, along_ray=True)
    cutters += blend.lay(ref, "cut_grille", mouth, fr, -0.07, 0.006, "grille", 0.01, along_ray=True)
    out += blend.lay(ref, "grille_back", mouth.buffer(-0.002), fr, -0.069, -0.065, "grille", 0.01, along_ray=True)
    for i, z in enumerate(R["grille_slats"]):
        bar = mouth.intersection(box(-1, z - 0.004, 1, z + 0.004)).buffer(-0.001)
        out += blend.lay(ref, f"grille_slat_{i}", bar, fr, -0.06, -0.035, "trim", 0.008, along_ray=True)
    drl = soft(poly(R["drl"]), 0.004)
    for sgn, side in ((1, "l"), (-1, "r")):
        g = (lambda q: q) if sgn > 0 else flip
        cutters += blend.lay(ref, f"cut_drl_{side}", g(drl), fr, -0.03, 0.006, "housing", 0.004, along_ray=True)
        out += blend.lay(ref, f"drl_back_{side}", g(drl.buffer(-0.001)), fr, -0.029, -0.025, "housing", 0.004, along_ray=True)
        out += blend.lay(ref, f"lamp_head_drl_{side}", g(drl.buffer(-0.004)), fr, -0.0245, -0.02, "lamp_head", 0.003,
                         along_ray=True)
    b = max(s["w"] for s in body.ST)
    zb = min(s["zb"] for s in body.ST if s["x"] > NOSE - 0.3)
    lip = box(-b, zb + 0.012, b, R["splitter"])
    out += blend.lay(ref, "splitter", lip.difference(mouth.buffer(0.004)), fr, -0.003, 0.004, "trim", 0.012)


def gills(ref, out):
    """The black vent on each front wing behind the arch: a short bar over a long slanted slot."""
    g = soft(poly(R["gill"]), 0.004)
    for sgn, side in ((1, "l"), (-1, "r")):
        out += blend.lay(ref, f"gill_{side}", g, side_fr(sgn), -0.001, 0.004, "trim", 0.005)


def skirts(ref, out):
    """The black side skirt along each sill, between the arches, under the doors."""
    x0 = W["rear_x"] + W["arch_r"] + 0.02
    x1 = W["front_x"] - W["arch_r"] - 0.02
    zb = min(s["zb"] for s in body.ST)
    g = soft(box(x0, zb + 0.008, x1, R["skirt"]), 0.006)
    for sgn, side in ((1, "l"), (-1, "r")):
        out += blend.lay(ref, f"skirt_{side}", g, side_fr(sgn), -0.002, 0.004, "trim", 0.012)


def rear_parts(ref, out):
    fr = rear_fr()
    val = half_shape(R["valance"], 0.008)
    out += blend.lay(ref, "valance", val, fr, -0.003, 0.004, "trim", 0.012)
    u, z0, z1 = R["rain"]
    out += blend.lay(ref, "lamp_rain", soft(box(-u, z0, u, z1), 0.004), fr, 0.0045, 0.0085, "lamp_rain", 0.004,
                     along_ray=True)
    out += exhausts(ref)


def exhausts(ref):
    """Four round tips, two each side, out through the diffuser."""
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        for i, (w, z, r) in enumerate(R["exhausts"]):
            y = w * sgn
            hit, _n = blend.cast(ref, rear_fr(), y, z + 0.8 * r)
            xs = (hit[0] if hit is not None else R["exhaust_x"]) - 0.025     # just out past the bumper above
            prof = [(r - 0.005, -0.14), (r, -0.14), (r, 0.01), (r - 0.005, 0.01)]
            v, f = geom.lathe(prof, 48, axis="z")
            v = np.column_stack([xs - v[:, 2], y + v[:, 0], z + v[:, 1]])
            obs.append(blend.make(f"exhaust_{side}{i}", v, [q[::-1] for q in f], "exhaust"))
            v, f = geom.disc((xs + 0.07, y, z), (1, 0, 0), r - 0.003, 0.01, n=40)
            obs.append(blend.make(f"exhaust_in_{side}{i}", v, f, "trim"))
    return obs


# ---------------------------------------------------------------- glass and trim
SIDE_BAND = 0.014     # black frame round the side windows
GLASS_BAND = 0.022    # black band round the windscreen and the rear window


def _fit_edge(pts, deg, n=24):
    """One even curve through traced points (x, z): a polynomial fit, resampled."""
    p = np.asarray(pts, float)
    k = np.polyfit(p[:, 0], p[:, 1], deg)
    xs = np.linspace(p[0, 0], p[-1, 0], n)
    return np.column_stack([xs, np.polyval(k, xs)])


def dlo_line():
    """The window opening rebuilt from plain fits: the A-pillar edge and the roof edge each one
    even curve, the quarter light's tail one straight line, and its foot the belt."""
    d = np.array(R["dlo"], float)
    a_pillar = _fit_edge(d[0:5], 2)
    roof = _fit_edge(d[4:10], 2)
    tail = d[[9, 11]]
    xs = np.linspace(d[12, 0], d[0, 0], 30)
    foot = np.column_stack([xs, [belt(x) for x in xs]])
    a_pillar[0] = foot[-1]
    return np.vstack([a_pillar, roof[1:], tail[1:], foot])


def pillar_line():
    """The slanted division between door glass and quarter light: its centre line, extended."""
    (x0, z0), (x1, z1) = R["b_pillar"]["foot"], R["b_pillar"]["head"]
    t = np.array([x1 - x0, z1 - z0]); t /= np.linalg.norm(t)
    p0 = np.array([x0, z0]) - t * 0.5
    p1 = np.array([x0, z0]) + t * 1.0
    return p0, p1


def ahead_of_pillar(off=0.0):
    """Everything ahead of the division's centre line (moved forward by off), seen from the side."""
    p0, p1 = pillar_line()
    p0 = p0 + [off, 0]; p1 = p1 + [off, 0]
    return Polygon([tuple(p0), tuple(p1), (p1[0] + 5, p1[1]), (p0[0] + 5, p0[1])])


def dlo_shapes():
    """The side window opening, its glass (split by the division) and the black band."""
    dlo = soft(poly(dlo_line()), 0.018)
    glass = dlo.buffer(-SIDE_BAND, join_style=1)
    half = R["b_pillar"]["w"] / 2 + 0.006
    glass = glass.intersection(ahead_of_pillar(half)).union(glass.difference(ahead_of_pillar(-half)))
    glass = unary_union([soft(p, 0.01) for p in getattr(glass, "geoms", [glass])])
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
    """The door's shut line as one path: down its front edge from the window's corner, along the
    sill, then slanting up its rear edge to the foot of the division's middle."""
    fd = R["front_door"]
    rd = R["rear_door"]
    pts = list(fd) + [rd[2], rd[1], rd[0]]
    fr_, sr, rr = R["front_round"], R["shut_round"], R["rear_round"]
    return fillet(pts, [fr_, fr_, sr, sr, rr])


def bonnet_path():
    """The bonnet's shut line in plan: from the windscreen's foot on the right, out and forward
    along the wing and the lamp, across the nose, and back along the left side to the windscreen."""
    b = np.array(R["bonnet"], float)            # nose middle first, out to the left windscreen corner
    return np.vstack([(b * [1, -1])[::-1], b[1:]])


class LidWrap:
    """A frame bending over the boot's back corner. 2D (u, y): u runs back along the deck (rays
    straight down), round the corner, then down the tail (rays straight forward)."""

    def __init__(self, xc, zc, rc):
        self.xc, self.zc, self.rc = xc, zc, rc
        self.u1 = rc * np.pi / 2

    def __call__(self, u, y):
        far = self.rc + 0.5
        if u <= 0:
            o, d = (self.xc - u, self.zc + far), (0.0, -1.0)
        elif u >= self.u1:
            o, d = (self.xc - far, self.zc - (u - self.u1)), (1.0, 0.0)
        else:
            t = u / self.rc
            d = (np.sin(t), -np.cos(t))
            o = (self.xc - d[0] * far, self.zc - d[1] * far)
        return np.array([o[0], y, o[1]]), np.array([d[0], 0.0, d[1]])

    def deck(self, x):
        return self.xc - x

    def tail(self, z):
        return self.u1 + self.zc - z


def lid_wrap():
    xr = body.TAIL + 0.08                 # the lid's rear face, above the lamps
    zd = body.z_top(xr + 0.06)
    return LidWrap(xr + 0.06, zd - 0.06, 0.06)


def lid_loop():
    """The boot lid: from the rear glass's foot back over the deck, round the corner and down
    the tail, its foot running above the inner lamps and kept clear of them."""
    fr = lid_wrap()
    L = R["lid"]
    u0 = fr.deck(L["x_glass"] - 0.03)
    us, u1 = fr.tail(L["z_side"]), fr.tail(L["z0"])
    half = [(u0, 0.0), (u0, L["w"]), (us, L["w"]), (u1, L["w_low"]), (u1, 0.0)]
    shape = both(poly([(y, u) for u, y in half]))
    for u, z, r in R["tail_rings"][1:]:
        for s in (1, -1):
            shape = shape.difference(Point(u * s, fr.tail(z)).buffer(r + 0.012, 64))
    shape = soft(shape.buffer(0), 0.03)
    if shape.geom_type != "Polygon":
        shape = max(shape.geoms, key=lambda g: g.area)
    loop = np.array(shape.exterior.coords)[:-1]
    return fr, loop[:, ::-1]


def grooves(ref):
    """Shut lines as V grooves to cut: doors, bonnet, boot lid."""
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        obs.append(blend.groove(ref, f"g_door_{side}", door_path(), side_fr(sgn)))
    obs.append(blend.groove(ref, "g_bonnet", bonnet_path(), top_fr()))
    fr, lid = lid_loop()
    obs.append(blend.groove(ref, "g_lid", lid, fr, closed=True))
    return obs


# ---------------------------------------------------------------- doors
DOOR_DEPTH = 0.05     # below the cabin floor the door takes the sill this deep
DOOR_WALL = 0.08      # above it, the whole wall: the cabin is hollow behind it
DOOR_GAP = 0.0015     # half the gap round the door, inside the shut line's groove
DOOR_OPEN = 62.0      # degrees the door swings out
FLOOR = 0.20


def b_shut():
    """Where the door's shut line meets the belt, at the division's foot."""
    return R["rear_door"][0][0]


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
    regions = [(o.intersection(box(-5, -1, 5, FLOOR + 0.03)), DOOR_DEPTH),
               (o.intersection(box(-5, FLOOR + 0.02, 5, 5)), DOOR_WALL)]
    trim = [ob for ob in obs if ob.name == f"window_trim_{side}"]
    roles = {f"side_glass_{side}": "glass", f"side_glass_{side}_1": "glass",
             f"mirror_{side}": "mirror", f"mirror_glass_{side}": "mirror_glass", f"door_card_{side}": "card"}
    return blend.free_door(ref, side_fr(sgn), regions, DOOR_GAP, shell, trim, roles, o, name=f"door_{side}")


# ---------------------------------------------------------------- mirrors
MIRROR_R = 0.036      # housing corner rounding
MIRROR_SWEEP = 0.14   # radians the housing turns back from square to the car


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


def mirrors():
    """A body-coloured housing on a body-coloured stalk from the door's front top, its glass in a
    recess in the housing's back."""
    m = R["mirror"]
    xb, xf = m["x_back"], m["x_front"]
    x0, x1 = xb, xb + 0.1
    y0, y1, z0, z1 = m["y_in"] + 0.03, m["y_out"], m["z0"] + 0.01, m["z1"]
    root = body.y_at(xf - 0.05, belt(xf) - 0.04) - 0.015
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        house = _rounded_bar(f"mirror_{side}", y0, y1, z0, z1, x0, x1, MIRROR_R, sgn, "body")
        cut = _rounded_bar(f"cut_mirror_{side}", y0 + 0.008, y1 - 0.008, z0 + 0.008, z1 - 0.008, x0 - 0.03, x0 + 0.006,
                           MIRROR_R - 0.008, sgn)
        blend.boolean(house, cut, "DIFFERENCE")
        arm = _rounded_bar(f"mirror_arm_{side}", root, y0 + 0.03, belt(xf) - 0.06, z0 + 0.03, xb + 0.03, xb + 0.075, 0.012,
                           sgn, "body")
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
            v, f = geom.disc((x, (yc + 0.12) * sgn, W["r"]), (0, 1, 0), W["arch_r"], 0.50, n=96)
            cut.append(blend.make("arch", v, f, "interior"))
            v, f = geom.disc((x, (yc + 0.10) * sgn, W["r"]), (0, 1, 0), W["arch_r"] + 0.03, 0.62, n=96)
            tubs.append(blend.make("tub", v, f, "interior"))
    return cut, tubs


def tub_drums():
    """The wheel tubs as (x, z, r, y_in) for keeping them out of the cabin."""
    return [(x, W["r"], W["arch_r"] + 0.03, out - wd / 2 + 0.10 - 0.31) for x, wd, out in _axles()]


# ---------------------------------------------------------------- rear wing
def wing():
    """A broad blade over the deck, its tips turning down, on two slim posts."""
    w = R["wing"]
    span, xf, xb = w["span"], w["x_front"], w["x_back"]
    z0, z1 = w["z0"], w["z1"]
    prof = Polygon([(xf, z0 + 0.006), (xf - 0.03, z0 + 0.016), (xb + 0.03, z1 + 0.004), (xb, z1 + 0.007),
                    (xb, z1 - 0.005), (xf - 0.04, z0 - 0.004)])
    prof = soft(prof, 0.003)
    ring = body.even(np.array(prof.exterior.coords)[:-1], 48)
    rings = []
    for y in np.linspace(-span, span, 49):
        t = abs(y) / span
        k = 1 - 0.35 * t ** 8                  # the tips round off in plan
        drop = 0.035 * t ** 6                  # and turn down
        cx = (xf + xb) / 2
        rings.append(np.column_stack([cx + (ring[:, 0] - cx) * k, np.full(48, y), ring[:, 1] - drop]))
    v, f = geom.close_rings(rings)
    obs = [blend.make("wing", v, f, "body")]
    for sgn, side in ((1, "l"), (-1, "r")):
        y = w["post_w"] * sgn
        xm = (xf + xb) / 2 + 0.03
        zb = body.z_top(xm, w["post_w"]) - 0.01
        v, f = geom.box(xm - 0.06, xm + 0.04, y - 0.008, y + 0.008, zb, z0 + 0.008)
        obs.append(blend.make(f"wing_post_{side}", v, f, "trim"))
    return obs


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


STEER = (0.10, 0.38, 0.72)     # the wheel's centre, in front of the left seat
STEER_TILT = 24.0               # degrees the wheel's face turns up from upright


def steering():
    """A round wheel on the left, square to its column, with two side spokes and one below."""
    from mathutils import Matrix
    r, t = 0.19, 0.017
    v, f = geom.lathe([(r - t + t * np.cos(a), t * np.sin(a)) for a in np.linspace(0, 2 * np.pi, 12, endpoint=False)], 64, axis="y")
    rim = blend.make("steering", v, f, "interior")
    parts = [geom.box(-0.016, 0.016, -0.008, 0.008, -r + 0.01, -0.035), geom.box(-r + 0.01, -0.035, -0.008, 0.008, -0.02, 0.02),
             geom.box(0.035, r - 0.01, -0.008, 0.008, -0.02, 0.02)]
    obs = [rim] + [blend.make("spoke", *p, "interior") for p in parts]
    v, f = geom.disc((0, 0, 0), (0, 1, 0), 0.055, 0.05, n=32)
    obs.append(blend.make("boss", v, f, "interior"))
    v, f = geom.disc((0, -0.17, 0), (0, 1, 0), 0.026, 0.30, n=24)
    obs.append(blend.make("column", v, f, "interior"))
    wheel = blend.union_all(obs, "steering")
    wheel.data.transform(Matrix.Translation(STEER) @ Matrix.Rotation(np.radians(STEER_TILT), 4, "Y")
                         @ Matrix.Rotation(np.pi / 2, 4, "Z"))
    return wheel


def interior():
    """Firewall, a dash meeting it, two bucket seats, two small rear seats, door cards, wheel and mirror."""
    x0 = R["screen"]["x_base_mid"] - 0.05
    belt_z = belt(0.0)
    obs = [_loft("firewall", x0 - 0.010, x0 - 0.004, lambda x: box(-0.9, FLOOR + 0.002, 0.9, 3), n=2),
           _loft("dash", x0 - 0.0125, 0.24, lambda x: box(-0.9, belt_z - 0.30, 0.9, belt_z - 0.12 + 0.14 * (1 - (x0 - x) / 0.7) + 0.04),
                 n=10, gap=0.045)]
    bc = b_shut()
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, FLOOR + 0.06, 1.2, belt(x) - 0.02) if s > 0 else box(-1.2, FLOOR + 0.06, -0.3, belt(x) - 0.02)).intersection(
            body.inside(x, 0.035 + 0.008)).difference(body.inside(x, 0.035 + 0.026))
        obs.append(_loft(f"door_card_{side}", bc + 0.04, R["front_door"][1][0] - 0.12, clip, n=10))   # clear of the dash as it swings
        yc = 0.38 * sgn
        obs.append(_rbox(f"seat_base_{side}", -0.58, -0.10, yc - 0.23, yc + 0.23, FLOOR + 0.002, FLOOR + 0.15))
        obs.append(_rbox(f"seat_back_{side}", -0.72, -0.60, yc - 0.22, yc + 0.22, FLOOR + 0.151, FLOOR + 0.66, lean=0.26))
        obs.append(_rbox(f"headrest_{side}", -0.86, -0.76, yc - 0.12, yc + 0.12, FLOOR + 0.661, FLOOR + 0.76, r=0.035, lean=0.12))
        ry = 0.33 * sgn
        obs.append(_rbox(f"rear_base_{side}", -1.10, -0.86, ry - 0.17, ry + 0.17, FLOOR + 0.06, FLOOR + 0.20))
        obs.append(_rbox(f"rear_back_{side}", -1.18, -1.07, ry - 0.17, ry + 0.17, FLOOR + 0.201, FLOOR + 0.52, lean=0.5))
    obs.append(steering())
    xm = R["screen"]["x_top_mid"] - 0.03
    roof_in = body.inside(xm, 0.035).bounds[3]
    top = roof_in - 0.05
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.12, 0.12, top - 0.05, top, r=0.018, mat="interior")
    stem = _rbox("stem", xm - 0.016, xm - 0.004, -0.012, 0.012, top - 0.015, roof_in - 0.0008, r=0.004, mat="interior")
    obs.append(blend.union_all([m, stem], "mirror_rear"))
    return obs


# ---------------------------------------------------------------- wheels
SPOKES = 10           # ten slim straight spokes


def wheels():
    """Dark alloy wheels: ten slim spokes from a small hub to a thin lip, a centre cap."""
    obs = []
    rr = W["rim_r"]
    for x, wd, out, fb in [a + (k,) for a, k in zip(_axles(), ("f", "r"))]:
        hw = wd / 2
        yc = out - hw
        tyre = soft(box(rr, -hw, W["r"], hw), 0.03).union(box(rr, -hw + 0.012, rr + 0.03, hw - 0.012))
        tp = body.even(np.array(tyre.exterior.coords)[:-1], 96)
        rim = [(rr - 0.001, -hw + 0.012), (rr - 0.001, hw - 0.012), (rr - 0.01, hw - 0.004), (rr - 0.018, hw - 0.01),
               (rr - 0.022, hw - 0.04), (0.07, hw - 0.04), (0.06, hw - 0.022), (0.0, hw - 0.022), (0.0, hw - 0.06),
               (rr - 0.03, hw - 0.06), (rr - 0.02, -hw + 0.012)]
        windows = []
        for k in range(SPOKES):
            a = 2 * np.pi * k / SPOKES
            win = Polygon([(r * np.cos(t), r * np.sin(t)) for r, t in
                           [(0.085, a + 0.17), (rr - 0.03, a + 0.075), (rr - 0.03, a + 2 * np.pi / SPOKES - 0.075),
                            (0.085, a + 2 * np.pi / SPOKES - 0.17)]])
            windows.append(soft(win, 0.008))
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
            for wpoly in windows:
                ring = body.even(np.array(wpoly.exterior.coords)[:-1], 48)
                rings = [np.column_stack([ring[:, 0], np.full(48, yy), ring[:, 1]]) for yy in (hw - 0.075, hw + 0.01)]
                v, f = geom.close_rings(rings)
                cutters.append(blend.make("hole", place(v), f, "tyre"))
            blend.boolean(rimob, blend.union_all(cutters, "holes"), "DIFFERENCE")
            v, f = geom.disc((0, hw - 0.014, 0), (0, 1, 0), 0.034, 0.012, n=32)
            obs += [rimob, blend.make(f"wheel_cap_{fb}{lr}", place(v), f, "chrome")]
            v, f = geom.disc((0, hw - 0.075, 0), (0, 1, 0), rr - 0.04, 0.008, n=64)
            obs.append(blend.make(f"wheel_disc_{fb}{lr}", place(v), f, "dark_metal"))
    return obs


# ---------------------------------------------------------------- badge and name
BRAND = HERE.parent.parent.parent / "src/renderer/assets/brands/tora"


def _badge_shapes(svg_name, colours):
    """The Tora badge from an SVG of the brand: [(shape, material)] with what each colour shows,
    flat, centred, 1 unit tall, y up, and the badge's whole outline. Later shapes cover earlier
    ones; the mirrored groups are drawn mirrored."""
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
    """Giga in the brand font, 1 unit = its cap height, centred."""
    gl = json.loads((HERE / "glyphs.json").read_text())
    g = unary_union([Polygon(p[0], p[1:]) for p in gl["pieces"]])
    b = g.bounds
    g = translate(g, -(b[0] + b[2]) / 2, -gl["cap"] / 2)
    return sscale(g, 1 / gl["cap"], 1 / gl["cap"], origin=(0, 0))


def _flat(ref, g, size, fr, a, b, up, name, d0, d1, mat, step=0.004):
    """Lay shape g (right, up, 1 unit = size) flat on the panel that fr's ray at (a, b) meets."""
    pf = blend.panel_frame(ref, fr, a, b, up)
    return blend.lay(ref, name, sscale(g, size, size, origin=(0, 0)), pf, d0, d1, mat, step)


def badge(ref, style, size, fr, a, b, up, name):
    """A dark base the badge's shape under each colour, every layer a little apart so no faces touch."""
    outline, pieces = badge_chrome() if style == "chrome" else badge_colour()
    objs = _flat(ref, outline, size, fr, a, b, up, name + "_base", 0.0012, 0.0030, "badge_black", 0.002)
    for i, (g, m) in enumerate(pieces):
        if m == "badge_black":
            continue
        g = g.buffer(-0.3 * 0.001 / size)
        if g.is_empty:
            continue
        objs += _flat(ref, g, size, fr, a, b, up, f"{name}_{i}", 0.0036, 0.0046, m, 0.002)
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
    bar_z = (max(p[1] for p in R["grille"]) + min(p[1] for p in R["grille_up"])) / 2
    bonnet_x = max(p[0] for p in R["bonnet"]) - 0.12
    L = R["lid"]
    lid_z = L["z0"] + 0.05
    panel_z = (L["z0"] + max(p[1] for p in R["valance"])) / 2
    for k, style in ((0, "chrome"), (1, "colour")):
        # front: on the bar between the grilles (the reference's spot), or on the bonnet's nose
        add("frontbadge", 1 + k, badge(ref, style, 0.048, front_fr(), 0, bar_z, up, "fb"))
        add("frontbadge", 3 + k, badge(ref, style, 0.06, top_fr(), bonnet_x, 0, fwd, "fb"))
        # rear: on the lid's foot in the middle (the reference's spot), or lower on the tail panel
        add("rearbadge", 1 + k, badge(ref, style, 0.055, rear_fr(), 0, lid_z, up, "rb"))
        add("rearbadge", 3 + k, badge(ref, style, 0.07, rear_fr(), 0, panel_z, up, "rb"))
    cap = 0.026
    # name: on the tail right of the middle under the lid (the reference's spot), or on both wings
    # just above the vents
    add("name", 1, _flat(ref, nm, cap, rear_fr(), -L["w_low"] * 0.75, L["z0"] - 0.04, up, "nm", 0.0012, 0.0025, "chrome"))
    q = []
    g = np.array(R["gill"])
    gx, gz = float(g[:2, 0].mean()), float(g[:, 1].max()) + 0.03
    for sgn in (1, -1):
        q += _flat(ref, nm, cap, side_fr(sgn), gx, gz, up, "nm", 0.0012, 0.0025, "chrome")
    add("name", 2, q)
    return out
