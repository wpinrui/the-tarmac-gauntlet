"""The Hoeori's parts: lamps, grille, fog pockets, gills, diffuser, exhausts, glass, shut lines,
doors, mirrors, rear wing, interior, wheels, badge and name. Every shape and size here is the
Hoeori's own, from rows.json, kept plain and smooth. Runs inside Blender's Python module, called
by export.py."""
import json
import re
import sys
from pathlib import Path

import numpy as np
from shapely.affinity import rotate, scale as sscale, skew, translate
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
    "body": ((0.98, 0.58, 0.04), 0.75, 1.0, None),
    "trim": ((0.025, 0.025, 0.03), 0.5, 1.0, None),
    "grille": ((0.03, 0.03, 0.035), 0.2, 1.0, None),
    "glass": ((0.06, 0.08, 0.1), 0.92, 0.42, None),
    "head_glass": ((0.92, 0.94, 0.96), 0.95, 0.2, None),
    "tail_glass": ((0.5, 0.03, 0.05), 0.92, 0.5, None),
    "housing": ((0.07, 0.07, 0.075), 0.5, 1.0, None),
    "reflector": ((0.78, 0.8, 0.82), 0.97, 1.0, None),
    "lamp_head": ((0.95, 0.95, 0.93), 0.6, 1.0, (1.0, 0.98, 0.92)),
    "lamp_tail": ((0.5, 0.03, 0.04), 0.5, 1.0, (1.0, 0.06, 0.04)),
    "lamp_brake": ((0.6, 0.04, 0.05), 0.5, 1.0, (1.0, 0.04, 0.03)),
    "lamp_rain": ((0.55, 0.04, 0.05), 0.5, 1.0, (1.0, 0.05, 0.03)),
    "chrome": ((0.86, 0.87, 0.89), 0.98, 1.0, None),
    "exhaust": ((0.62, 0.6, 0.57), 0.9, 1.0, None),
    "badge_navy": ((0.11, 0.165, 0.227), 0.6, 1.0, None),
    "badge_gold": ((0.89, 0.64, 0.23), 0.8, 1.0, None),
    "badge_white": ((0.96, 0.96, 0.95), 0.6, 1.0, None),
    "badge_red": ((0.78, 0.24, 0.16), 0.6, 1.0, None),
    "interior": ((0.08, 0.08, 0.09), 0.15, 1.0, None),
    "seat": ((0.12, 0.12, 0.125), 0.2, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.55, 0.56, 0.58), 0.8, 1.0, None),
    "dark_metal": ((0.16, 0.16, 0.17), 0.8, 1.0, None),
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
    return blend.frame((0, 1.0 * sgn, 0), (1, 0, 0), (0, 0, 1), (0, -sgn, 0))


def top_fr():
    return blend.frame((0, 0, 1.8), (1, 0, 0), (0, 1, 0), (0, 0, -1))


def poly(pts):
    return Polygon(pts).buffer(0)


def flip(g):
    """Mirror a 2D shape about its first axis (y for front and rear frames)."""
    return sscale(g, -1, 1, origin=(0, 0))


def both(g):
    return unary_union([g, flip(g)])


def both_plan(g):
    return unary_union([g, sscale(g, 1, -1, origin=(0, 0))])


def soft(g, r):
    return g.buffer(-r, join_style=1).buffer(r, join_style=1)


def half_shape(pts):
    """A shape traced as its left half from the centre line, joined to its mirror."""
    return soft(both(poly([(0.0, pts[0][1])] + [tuple(p) for p in pts] + [(0.0, pts[-1][1])])), 0.004)


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
def head_shape():
    """The left headlamp seen from ahead, as traced, its corners barely rounded."""
    return soft(poly(R["head"]), 0.006)


def headlamps(ref, cutters, out):
    """Each headlamp sits on the nose's slanted corner; seen from ahead it keeps its traced outline.
    Our own interior: a round projector in a reflector cup outboard, a slim lamp strip inboard."""
    fr = front_fr()
    for sgn, side in ((1, "l"), (-1, "r")):
        shp = head_shape()
        b = shp.bounds
        cx, cz = b[2] - 0.072, (b[1] + b[3]) / 2 + 0.006
        cup = Point(cx, cz).buffer(0.052, 48)
        strip = shp.intersection(box(b[0], -5, cx - 0.075, 5)).buffer(-0.016, join_style=1)
        strip = max(getattr(strip, "geoms", [strip]), key=lambda g: g.area)
        g = (lambda q: q) if sgn > 0 else flip
        cutters += blend.lay(ref, f"cut_head_{side}", g(shp), fr, -0.045, 0.006, "housing", 0.008, along_ray=True)
        out += blend.lay(ref, f"head_back_{side}", g(shp.buffer(-0.002)), fr, -0.044, -0.04, "housing", 0.008, along_ray=True)
        out += blend.lay(ref, f"head_reflector_{side}", g(cup.difference(cup.buffer(-0.012))), fr, -0.039, -0.03,
                         "reflector", 0.004, along_ray=True)
        out += blend.lay(ref, f"lamp_head_{side}0", g(cup.buffer(-0.013)), fr, -0.034, -0.03, "lamp_head", 0.005, along_ray=True)
        out += blend.lay(ref, f"lamp_head_{side}1", g(soft(strip, 0.004)), fr, -0.034, -0.03, "lamp_head", 0.005, along_ray=True)
        out += blend.lay(ref, f"head_glass_{side}", g(shp.buffer(-0.002)), fr, -0.0055, -0.0025, "head_glass", 0.008, along_ray=True)


def tail_shape():
    return soft(poly(R["tail"]), 0.006)


def taillamps(ref, cutters, out):
    """Each tail lamp wraps the rear corner; seen from behind it keeps its traced outline. Our own
    interior: a round tail lamp ring round a brake lamp outboard, and a slim brake bar inboard."""
    fr = rear_fr()
    for sgn, side in ((1, "l"), (-1, "r")):
        shp = tail_shape()
        b = shp.bounds
        cx, cz = b[2] - 0.075, (b[1] + b[3]) / 2 - 0.005
        ring = Point(cx, cz).buffer(0.058, 48)
        bar = shp.intersection(box(b[0], -5, cx - 0.075, 5)).buffer(-0.014, join_style=1)
        bar = max(getattr(bar, "geoms", [bar]), key=lambda g: g.area)
        g = (lambda q: q) if sgn > 0 else flip
        cutters += blend.lay(ref, f"cut_tail_{side}", g(shp), fr, -0.035, 0.006, "housing", 0.008, along_ray=True)
        out += blend.lay(ref, f"tail_back_{side}", g(shp.buffer(-0.002)), fr, -0.034, -0.03, "housing", 0.008, along_ray=True)
        out += blend.lay(ref, f"lamp_tail_{side}", g(ring.difference(ring.buffer(-0.02))), fr, -0.029, -0.025, "lamp_tail",
                         0.004, along_ray=True)
        out += blend.lay(ref, f"lamp_brake_{side}0", g(ring.buffer(-0.026)), fr, -0.029, -0.025, "lamp_brake", 0.004,
                         along_ray=True)
        out += blend.lay(ref, f"lamp_brake_{side}1", g(soft(bar, 0.004)), fr, -0.029, -0.025, "lamp_brake", 0.005,
                         along_ray=True)
        out += blend.lay(ref, f"tail_glass_{side}", g(shp.buffer(-0.002)), fr, -0.0055, -0.0025, "tail_glass", 0.008,
                         along_ray=True)


# ---------------------------------------------------------------- front
def front_parts(ref, cutters, out):
    fr = front_fr()
    mouth = half_shape(R["grille"])
    cutters += blend.lay(ref, "cut_grille", mouth, fr, -0.05, 0.006, "grille", 0.01, along_ray=True)
    out += blend.lay(ref, "grille_back", mouth.buffer(-0.002), fr, -0.049, -0.045, "grille", 0.01, along_ray=True)
    # a gloss black bar across the mouth's top, and three slats below it
    b = mouth.bounds
    out += blend.lay(ref, "grille_bar", mouth.intersection(box(-1, R["grille_bar"], 1, 1)).buffer(-0.003), fr,
                     -0.03, -0.012, "trim", 0.008, along_ray=True)
    for i, z in enumerate(R["grille_slats"]):
        bar = mouth.intersection(box(-1, z - 0.005, 1, z + 0.005)).buffer(-0.001)
        out += blend.lay(ref, f"grille_slat_{i}", bar, fr, -0.04, -0.02, "trim", 0.008, along_ray=True)
    # fog lamp pockets at the bumper's corners
    x0, z0, x1, z1 = R["fog"]["box"]
    fx, fz, fr_r = R["fog"]["lamp"]
    pocket = soft(box(x0, z0, x1, z1), 0.02)
    for sgn, side in ((1, "l"), (-1, "r")):
        g = (lambda q: q) if sgn > 0 else flip
        cutters += blend.lay(ref, f"cut_fog_{side}", g(pocket), fr, -0.04, 0.006, "grille", 0.008, along_ray=True)
        out += blend.lay(ref, f"fog_back_{side}", g(pocket.buffer(-0.002)), fr, -0.039, -0.035, "grille", 0.008, along_ray=True)
        lamp = Point(fx, fz).buffer(fr_r, 40)
        out += blend.lay(ref, f"fog_ring_{side}", g(lamp.buffer(0.006).difference(lamp)), fr, -0.034, -0.022, "reflector",
                         0.004, along_ray=True)
        out += blend.lay(ref, f"lamp_head_fog_{side}", g(lamp), fr, -0.034, -0.026, "lamp_head", 0.005, along_ray=True)


def gills(ref, out):
    """The slim black vent on each front wing behind the arch."""
    g = soft(poly(R["gill"]), 0.006)
    for sgn, side in ((1, "l"), (-1, "r")):
        out += blend.lay(ref, f"gill_{side}", g, side_fr(sgn), -0.001, 0.004, "trim", 0.006)


def rear_parts(ref, out):
    fr = rear_fr()
    val = half_shape(R["valance"])
    out += blend.lay(ref, "valance", val, fr, -0.001, 0.004, "trim", 0.012, along_ray=True)
    rain = poly(R["rain"] + [[-p[0], p[1]] for p in R["rain"][1:][::-1]])
    out += blend.lay(ref, "lamp_rain", soft(rain, 0.002), fr, 0.003, 0.007, "lamp_rain", 0.004, along_ray=True)
    out += exhausts(ref)


def exhausts(ref):
    """A plain round tip each side, out through the diffuser."""
    e = R["exhaust"]
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        y, z = e["w"] * sgn, e["z"]
        hit, _n = blend.cast(ref, rear_fr(), e["w"] * sgn, z)
        xs = hit[0] if hit is not None else TAIL + 0.05
        prof = [(e["r"] - 0.006, -0.12), (e["r"], -0.12), (e["r"], 0.012), (e["r"] - 0.006, 0.012)]
        v, f = geom.lathe(prof, 48, axis="z")
        v = np.column_stack([xs - v[:, 2], y + v[:, 0], z + v[:, 1]])
        obs.append(blend.make(f"exhaust_{side}", v, [q[::-1] for q in f], "exhaust"))
        v, f = geom.disc((xs + 0.06, y, z), (1, 0, 0), e["r"] - 0.004, 0.01, n=40)
        obs.append(blend.make(f"exhaust_in_{side}", v, f, "trim"))
    return obs


# ---------------------------------------------------------------- glass and trim
SIDE_BAND = 0.016     # black frame round the side windows
GLASS_BAND = 0.02     # black band round the windscreen and the rear window


def dlo_line():
    """The traced window opening, its top edge refit as one even curve and its foot as the belt."""
    d = np.array(R["dlo"], float)
    top = (d[:, 1] > belt(d[:, 0]) + 0.2) & (d[:, 0] < 0.0) & (d[:, 0] > -0.6)
    k = np.polyfit(d[top, 0], d[top, 1], 3)
    d[top, 1] = np.polyval(k, d[top, 0])
    low = d[:, 1] < belt(d[:, 0]) + 0.01
    d[low, 1] = belt(d[low, 0])
    return d


def dlo_shapes():
    """The side window opening, its glass (split by the B-pillar) and the black band."""
    dlo = soft(poly(dlo_line()), 0.02)
    glass = dlo.buffer(-SIDE_BAND, join_style=1)
    b0, b1 = R["b_pillar"]
    glass = glass.difference(box(b0 - 0.012, -1, b1 + 0.012, 3))
    glass = unary_union([soft(p, 0.01) for p in getattr(glass, "geoms", [glass])])
    return dlo, glass, dlo.difference(glass.buffer(-0.004))


def screen_shapes():
    s = R["screen"]
    half = [(s["x_base"] + 0.01, 0.0), (s["x_base"] + 0.01, s["w_base"]), (s["x_top"], s["w_top"]), (s["x_top"], 0.0)]
    o = soft(both_plan(poly(half)), 0.05)
    g = o.buffer(-GLASS_BAND, join_style=1)
    return o, g, o.difference(g.buffer(-0.004))


def rear_shapes():
    s = R["rear_glass"]
    half = [(s["x_top"], 0.0), (s["x_top"], s["w_top"]), (s["x_base"] + 0.01, s["w_base"]), (s["x_base"] + 0.01, 0.0)]
    o = soft(both_plan(poly(half)), 0.06)
    g = o.buffer(-GLASS_BAND, join_style=1)
    return o, g, o.difference(g.buffer(-0.004))


def screen_fr():
    s = R["screen"]
    return slope_fr(s["x_base"], body.z_top(s["x_base"]), s["x_top"], body.z_top(s["x_top"]))


def rear_glass_fr():
    s = R["rear_glass"]
    return slope_fr(s["x_base"], body.z_top(s["x_base"]), s["x_top"], body.z_top(s["x_top"]))


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
    sill, round to the rear edge and up it to the middle of the B-pillar's black."""
    (fx0, fz0), (fx1, fz1) = R["front_door"]
    rd = R["rear_door"]
    pts = [[fx0, fz0], [fx1, fz1], rd[2], rd[1], rd[0]]
    return fillet(pts, [R["shut_round"], R["rear_round"], R["rear_round"]])


def bonnet_path():
    """The bonnet's shut line in plan: from the windscreen's foot on the right, out and forward
    along the wing, across the nose, and back along the left wing to the windscreen."""
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
    xr = TAIL + 0.07                    # the lid's rear face, above the bumper
    zd = body.z_top(xr + 0.05)
    return LidWrap(xr + 0.04, zd - 0.04, 0.04)


def lid_loop():
    fr = lid_wrap()
    L = R["lid"]
    u0 = fr.deck(L["x_glass"] - 0.03)
    u1 = fr.tail(L["z0"])
    shape = soft(box(u0, -L["w"], u1, L["w"]), L["r"])
    return fr, np.array(shape.exterior.coords)[:-1]


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
FLOOR = 0.22


def door_outline():
    """The left door seen from the side: inside its shut line, closed across the top along the
    belt, and the window's black frame round its glass back to the B-pillar's middle."""
    path = door_path()
    lower = Polygon(path).buffer(0)
    dlo, _g, _t = dlo_shapes()
    frame = dlo.buffer(SIDE_BAND * 0.5, join_style=2).intersection(box(R["b_shut"], -1, 5, 5))
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
MIRROR_R = 0.03       # housing corner rounding
MIRROR_SWEEP = 0.18   # radians the housing turns back from square to the car


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
    """A body-coloured housing on a black stalk from the door's front top, its glass in a recess
    in the housing's back."""
    m = R["mirror"]
    xb, xf = m["x_back"], m["x_front"]
    x0, x1 = xb, xb + 0.09
    y0, y1, z0, z1 = m["y_in"] + 0.02, m["y_out"], m["z0"], m["z1"]
    root = body.y_at(xf - 0.03, belt(xf) - 0.04) - 0.015
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        house = _rounded_bar(f"mirror_{side}", y0, y1, z0, z1, x0, x1, MIRROR_R, sgn, "body")
        cut = _rounded_bar(f"cut_mirror_{side}", y0 + 0.008, y1 - 0.008, z0 + 0.008, z1 - 0.008, x0 - 0.03, x0 + 0.006,
                           MIRROR_R - 0.008, sgn)
        blend.boolean(house, cut, "DIFFERENCE")
        arm = _rounded_bar(f"mirror_arm_{side}", root, y0 + 0.03, belt(xf) - 0.06, z0 + 0.03, xf - 0.07, xf - 0.02, 0.012, sgn)
        glass = _rounded_bar(f"mirror_glass_{side}", y0 + 0.0095, y1 - 0.0095, z0 + 0.0095, z1 - 0.0095, x0 + 0.0015,
                             x0 + 0.0045, MIRROR_R - 0.0095, sgn, "mirror_glass")
        blend.boolean(house, arm, "UNION")
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


# ---------------------------------------------------------------- rear wing
def wing():
    """A blade across the boot on two posts: thin, its trailing edge lifted a little."""
    w = R["wing"]
    span, xf, xb = w["span"], w["x_front"], w["x_back"]
    z0, z1 = w["z0"], w["z1"]
    prof = Polygon([(xf, z0 + 0.004), (xf + 0.02, z0 + 0.012), (xb + 0.02, z1 + 0.004), (xb, z1 + 0.006),
                    (xb, z1 - 0.004), (xf + 0.03, z0 - 0.004)])
    prof = soft(prof, 0.003)
    ring = body.even(np.array(prof.exterior.coords)[:-1], 48)
    rings = []
    for y in np.linspace(-span, span, 41):
        k = 1 - 0.6 * (abs(y) / span) ** 6          # the tips round off in plan
        cx = (xf + xb) / 2
        rings.append(np.column_stack([cx + (ring[:, 0] - cx) * k, np.full(48, y), ring[:, 1]]))
    v, f = geom.close_rings(rings)
    obs = [blend.make("wing", v, f, "body")]
    for sgn, side in ((1, "l"), (-1, "r")):
        y = w["post_w"] * sgn
        xm = (xf + xb) / 2 - 0.02
        zb = body.z_top(xm, w["post_w"]) - 0.01
        v, f = geom.box(xm - 0.05, xm + 0.04, y - 0.008, y + 0.008, zb, z0 + 0.005)
        obs.append(blend.make(f"wing_post_{side}", v, f, "body"))
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


STEER = (0.02, 0.36, 0.72)     # the wheel's centre, in front of the left seat
STEER_TILT = 25.0               # degrees the wheel's face turns up from upright


def steering():
    """A small round sports wheel on the left, square to its column, with three spokes."""
    from mathutils import Matrix
    r, t = 0.185, 0.016
    v, f = geom.lathe([(r - t + t * np.cos(a), t * np.sin(a)) for a in np.linspace(0, 2 * np.pi, 12, endpoint=False)], 64, axis="y")
    rim = blend.make("steering", v, f, "interior")
    parts = [geom.box(-0.014, 0.014, -0.008, 0.008, -r + 0.01, -0.03), geom.box(-r + 0.01, -0.03, -0.008, 0.008, -0.014, 0.014),
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
    """Firewall, a dash meeting it, two low bucket seats, a small rear bench, door cards, wheel and mirror."""
    x0 = R["screen"]["x_base"] - 0.05
    belt_z = belt(0.0)
    obs = [_loft("firewall", x0 - 0.010, x0 - 0.004, lambda x: box(-0.8, FLOOR + 0.002, 0.8, 3), n=2),
           _loft("dash", x0 - 0.0125, 0.30, lambda x: box(-0.8, belt_z - 0.13, 0.8, belt_z - 0.13 + 0.14 * (1 - (x0 - x) / 0.6) + 0.04), n=10)]
    bc = R["b_shut"]
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, FLOOR + 0.06, 1.2, belt(x) - 0.02) if s > 0 else box(-1.2, FLOOR + 0.06, -0.3, belt(x) - 0.02)).intersection(
            body.inside(x, 0.035 + 0.003)).difference(body.inside(x, 0.035 + 0.022))
        obs.append(_loft(f"door_card_{side}", bc + 0.04, R["front_door"][1][0] - 0.04, clip, n=10))
        yc = 0.36 * sgn
        obs.append(_rbox(f"seat_base_{side}", -0.52, -0.04, yc - 0.22, yc + 0.22, FLOOR + 0.002, FLOOR + 0.16))
        obs.append(_rbox(f"seat_back_{side}", -0.66, -0.54, yc - 0.21, yc + 0.21, FLOOR + 0.161, FLOOR + 0.66, lean=0.28))
        obs.append(_rbox(f"headrest_{side}", -0.80, -0.70, yc - 0.12, yc + 0.12, FLOOR + 0.661, FLOOR + 0.76, r=0.035, lean=0.12))
    obs.append(_rbox("bench_base", -1.02, -0.78, -0.40, 0.40, FLOOR + 0.04, FLOOR + 0.18))
    obs.append(_rbox("bench_back", -1.14, -1.04, -0.40, 0.40, FLOOR + 0.181, FLOOR + 0.48, lean=0.45))
    obs.append(steering())
    xm = R["screen"]["x_top"] - 0.03
    roof_in = body.inside(xm, 0.035).bounds[3]
    top = roof_in - 0.05
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.11, 0.11, top - 0.05, top, r=0.018, mat="interior")
    stem = _rbox("stem", xm - 0.016, xm - 0.004, -0.012, 0.012, top - 0.015, roof_in - 0.0008, r=0.004, mat="interior")
    obs.append(blend.union_all([m, stem], "mirror_rear"))
    return obs


# ---------------------------------------------------------------- wheels
SPOKES = 5            # twin spokes: five pairs


def wheels():
    """Alloy wheels: five twin spokes, a deep lip and a small centre cap."""
    obs = []
    hw = W["width"] / 2
    yc = W["outer_y"] - hw
    rr = W["rim_r"]
    tyre = soft(box(rr, -hw, W["r"], hw), 0.03).union(box(rr, -hw + 0.012, rr + 0.03, hw - 0.012))
    tp = body.even(np.array(tyre.exterior.coords)[:-1], 96)
    # rim: lip, barrel, a flat face set in from the lip, the hub
    rim = [(rr - 0.001, -hw + 0.012), (rr - 0.001, hw - 0.012), (rr - 0.012, hw - 0.004), (rr - 0.022, hw - 0.012),
           (rr - 0.026, hw - 0.03), (0.06, hw - 0.03), (0.05, hw - 0.02), (0.0, hw - 0.02), (0.0, hw - 0.055),
           (rr - 0.03, hw - 0.055), (rr - 0.02, -hw + 0.012)]
    # windows between the spoke pairs and inside each pair, cut through the face
    windows = []
    for k in range(SPOKES):
        a = 2 * np.pi * k / SPOKES
        big = Polygon([(r * np.cos(t), r * np.sin(t)) for r, t in
                       [(0.075, a + 0.33), (rr - 0.034, a + 0.17), (rr - 0.034, a + 2 * np.pi / SPOKES - 0.17),
                        (0.075, a + 2 * np.pi / SPOKES - 0.33)]])
        windows.append(soft(big.buffer(-0.002), 0.012))
        slit = Polygon([(r * np.cos(t), r * np.sin(t)) for r, t in
                        [(0.105, a - 0.035), (rr - 0.04, a - 0.06), (rr - 0.04, a + 0.06), (0.105, a + 0.035)]])
        windows.append(soft(slit, 0.006))
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
            for wpoly in windows:
                ring = body.even(np.array(wpoly.exterior.coords)[:-1], 48)
                rings = [np.column_stack([ring[:, 0], np.full(48, yy), ring[:, 1]]) for yy in (hw - 0.07, hw + 0.01)]
                v, f = geom.close_rings(rings)
                cutters.append(blend.make("hole", place(v), f, "tyre"))
            blend.boolean(rimob, blend.union_all(cutters, "holes"), "DIFFERENCE")
            v, f = geom.disc((0, hw - 0.012, 0), (0, 1, 0), 0.032, 0.012, n=32)
            obs += [rimob, blend.make(f"wheel_cap_{fb}{lr}", place(v), f, "dark_metal")]
            v, f = geom.disc((0, hw - 0.06, 0), (0, 1, 0), rr - 0.035, 0.008, n=64)
            obs.append(blend.make(f"wheel_disc_{fb}{lr}", place(v), f, "dark_metal"))
    return obs


# ---------------------------------------------------------------- badge and name
BRAND = HERE.parent.parent.parent / "src/renderer/assets/brands/seorak"


def _hexagon(pts_attr):
    nums = [float(t) for t in re.findall(r"-?[\d.]+", pts_attr)]
    return Polygon(list(zip(nums[0::2], nums[1::2])))


def _badge_layers():
    """The Seorak badge from seorak-badge-colour.svg: the navy plate (its stroke counted in), the
    gold ring, the white peak and the red peak, as flat shapes in drawing order."""
    svg = (BRAND / "seorak-badge-colour.svg").read_text()
    polys = []
    for m in re.finditer(r"<polygon\s([^>]*)/>", svg):
        a = dict(re.findall(r'([\w-]+)="([^"]*)"', m.group(1)))
        polys.append((_hexagon(a["points"]), a))
    plate, ring, peak, red = polys
    plate_g = plate[0].buffer(float(plate[1]["stroke-width"]) / 2, join_style=1)
    rw = float(ring[1]["stroke-width"]) / 2
    ring_g = ring[0].buffer(rw, join_style=1).difference(ring[0].buffer(-rw, join_style=1))
    return plate_g, ring_g, peak[0], red[0]


def _fit(shapes, ref):
    """Flip y up and scale so ref is 1 unit tall, centred."""
    b = ref.bounds
    k = 1 / (b[3] - b[1])
    return [sscale(translate(sscale(g, 1, -1, origin=(0, 0)), -(b[0] + b[2]) / 2, (b[1] + b[3]) / 2), k, k, origin=(0, 0))
            for g in shapes]


def badge_colour():
    plate, ring, peak, red = _badge_layers()
    gap = 0.8
    layers = [(plate.difference(unary_union([ring, peak, red]).buffer(gap)), "badge_navy"), (ring, "badge_gold"),
              (peak.difference(red.buffer(gap)), "badge_white"), (red, "badge_red")]
    shapes = _fit([g for g, _m in layers] + [plate], plate)
    return [(s, m) for s, (_g, m) in zip(shapes[:-1], layers) if not s.is_empty]


def badge_chrome():
    """The chrome badge: the hexagon ring and the two peaks, kept apart by a fine gap."""
    plate, _ring, peak, red = _badge_layers()
    svg = (BRAND / "seorak-badge-chrome.svg").read_text()
    m = re.findall(r"<polygon\s([^>]*)/>", svg)[1]
    a = dict(re.findall(r'([\w-]+)="([^"]*)"', m))
    rw = float(a["stroke-width"]) / 2
    hexa = _hexagon(a["points"])
    ring = hexa.buffer(rw, join_style=1).difference(hexa.buffer(-rw, join_style=1))
    whole = unary_union([ring, peak.difference(red.buffer(1.0)), red])
    return _fit([whole], plate)[0]


def name_shape():
    """Hoeori in the brand font, 1 unit = its cap height, centred."""
    gl = json.loads((HERE / "glyphs.json").read_text())
    g = unary_union([Polygon(p[0], p[1:]) for p in gl["pieces"]])
    b = g.bounds
    g = translate(g, -(b[0] + b[2]) / 2, -gl["cap"] / 2)
    return sscale(g, 1 / gl["cap"], 1 / gl["cap"], origin=(0, 0))


def _flat(ref, g, size, fr, a, b, up, name, d0, d1, mat, step=0.004):
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
    top_z = max(p[1] for p in R["grille"])
    head_z = min(p[1] for p in R["head"])
    L = R["lid"]
    lid_hi = L["z0"] + 0.18
    lid_lo = L["z0"] + 0.07
    for k, style in ((0, "chrome"), (1, "colour")):
        # front: on the nose between the lamps, above the grille (the reference), or on the bonnet's nose
        add("frontbadge", 1 + k, badge(ref, style, 0.07, front_fr(), 0, (top_z + head_z) / 2 + 0.01, up, "fb"))
        add("frontbadge", 3 + k, badge(ref, style, 0.06, top_fr(), NOSE - 0.15, 0, fwd, "fb"))
        # rear: high on the lid's middle (the reference), or low on it
        add("rearbadge", 1 + k, badge(ref, style, 0.06, rear_fr(), 0, lid_hi, up, "rb"))
        add("rearbadge", 3 + k, badge(ref, style, 0.07, rear_fr(), 0, lid_lo, up, "rb"))
    cap = 0.026
    # name: low on the lid's right (the reference), or on both front wings above the gills
    add("name", 1, _flat(ref, nm, cap, rear_fr(), -L["w"] * 0.62, lid_lo, up, "nm", 0.0012, NAME_T, "chrome"))
    q = []
    gx = float(np.mean([p[0] for p in R["gill"]]))
    gz = max(p[1] for p in R["gill"]) + 0.04
    for sgn in (1, -1):
        fr = side_fr(sgn)
        q += _flat(ref, nm, cap, fr, gx, gz, up, "nm", 0.0012, NAME_T, "chrome")
    add("name", 2, q)
    return out
