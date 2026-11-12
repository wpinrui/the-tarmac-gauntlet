"""The Carrier's parts: lamps, grille, rubbing strip, intake, glass, shut lines, front doors,
mirrors, exhaust, interior, wheels, badge and name. Every shape and size here is the Carrier's
own, from rows.json, kept plain and smooth. Runs inside Blender's Python module, called by export.py."""
import json
import re
import sys
from pathlib import Path

import numpy as np
from shapely.affinity import scale as sscale, skew, translate
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
    "badge_gold": ((0.91, 0.70, 0.23), 0.8, 1.0, None),
    "badge_green": ((0.07, 0.27, 0.23), 0.6, 1.0, None),
    "badge_red": ((0.84, 0.15, 0.18), 0.6, 1.0, None),
    "badge_mint": ((0.66, 0.76, 0.71), 0.6, 1.0, None),
    "badge_cream": ((0.96, 0.94, 0.89), 0.6, 1.0, None),
    "interior": ((0.09, 0.09, 0.1), 0.15, 1.0, None),
    "seat": ((0.14, 0.13, 0.125), 0.2, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.78, 0.79, 0.8), 0.7, 1.0, None),
    "dark_metal": ((0.16, 0.16, 0.17), 0.8, 1.0, None),
}
LAMPS = {"Headlights": ["lamp_head"], "Tail lights": ["lamp_tail"], "Brake lights": ["lamp_brake"]}
GROUPS = {"frontbadge": "Front badge", "rearbadge": "Rear badge", "name": "Name"}


# ---------------------------------------------------------------- frames
def front_fr():
    return blend.frame((NOSE, 0, 0), (0, 1, 0), (0, 0, 1), (-1, 0, 0))


def rear_fr():
    return blend.frame((TAIL, 0, 0), (0, 1, 0), (0, 0, 1), (1, 0, 0))


def side_fr(sgn):
    return blend.frame((0, 1.0 * sgn, 0), (1, 0, 0), (0, 0, 1), (0, -sgn, 0))


def top_fr():
    return blend.frame((0, 0, 1.6), (1, 0, 0), (0, 1, 0), (0, 0, -1))


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


GH_FOOT = body.values(0.0)


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


class Wrap:
    """A frame bending round a front or rear corner in plan: rays run straight in across the
    face, fan out round a corner of radius rc centred at (xc, yc), then run straight in along
    the side. 2D (u, z): u runs from the centre line out round the corner and along the side."""

    def __init__(self, end, sgn, xc, yc, rc):
        self.f = 1 if end == "front" else -1
        self.sgn, self.xc, self.yc, self.rc = sgn, xc, yc, rc
        self.u1 = yc + rc * np.pi / 2

    def __call__(self, u, z):
        f, s = self.f, self.sgn
        far = self.rc + 0.5
        if u <= self.yc:
            o, d = (self.xc + f * far, u), (-f, 0.0)
        elif u >= self.u1:
            o, d = (self.xc - f * (u - self.u1), self.yc + far), (0.0, -1.0)
        else:
            t = (u - self.yc) / self.rc
            c, n = np.cos(t), np.sin(t)
            o, d = (self.xc + f * c * far, self.yc + n * far), (-f * c, -n)
        return np.array([o[0], o[1] * s, z]), np.array([d[0], d[1] * s, 0.0])

    def side_u(self, x):
        """u on the side at x."""
        return self.u1 + self.f * (self.xc - x)


def front_wrap(sgn):
    return Wrap("front", sgn, NOSE - 0.12, 0.56, 0.12)


def rear_wrap(sgn):
    return Wrap("rear", sgn, TAIL + 0.13, 0.56, 0.11)


# ---------------------------------------------------------------- lamps
def head_shape():
    """The left headlamp seen from ahead: the traced outline, corners barely rounded."""
    return soft(poly(R["head"]), 0.008)


def headlamps(ref, cutters, out):
    fr = front_fr()
    for sgn, side in ((1, "l"), (-1, "r")):
        shp = head_shape() if sgn > 0 else flip(head_shape())
        cutters += blend.lay(ref, f"cut_head_{side}", shp, fr, -0.03, 0.006, "housing", 0.01, along_ray=True)
        out += blend.lay(ref, f"head_back_{side}", shp.buffer(-0.002), fr, -0.029, -0.025, "housing", 0.01, along_ray=True)
        b = shp.bounds
        # our own interior: a wide main lamp outboard and a square lamp inboard, each in a reflector
        mid = b[0] + 0.40 * (b[2] - b[0]) if sgn > 0 else b[2] - 0.40 * (b[2] - b[0])
        inner = shp.intersection(box(min(b[0], mid), -5, max(b[0], mid), 5) if sgn > 0 else box(min(mid, b[2]), -5, max(mid, b[2]), 5))
        outer = shp.difference(inner.buffer(0.001))
        for i, cell in enumerate((inner, outer)):
            cell = soft(cell.buffer(-0.012, join_style=2), 0.01)
            out += blend.lay(ref, f"head_reflector_{side}{i}", cell.buffer(0.006).difference(cell), fr, -0.024, -0.02,
                             "reflector", 0.004, along_ray=True)
            out += blend.lay(ref, f"lamp_head_{side}{i}", cell, fr, -0.024, -0.02, "lamp_head", 0.006, along_ray=True)
        out += blend.lay(ref, f"head_glass_{side}", shp.buffer(-0.002), fr, -0.0055, -0.0025, "head_glass", 0.01, along_ray=True)


def tail_shape(wr):
    """The left tail lamp in its wrap's (u, z): across the back from the traced inner edge, round
    the corner and a little way forward along the side."""
    (y0, z1), (y1, _z), (y2, z0), _ = R["tail"]
    u_end = wr.u1 + R["tail_side"]
    return soft(Polygon([(y0, z1), (u_end - 0.025, z1), (u_end, z0), (y0, z0)]), 0.008)


def taillamps(ref, cutters, out):
    for sgn, side in ((1, "l"), (-1, "r")):
        wr = rear_wrap(sgn)
        shp = tail_shape(wr)
        cutters += blend.lay(ref, f"cut_tail_{side}", shp, wr, -0.025, 0.006, "housing", 0.008, along_ray=True)
        b = shp.bounds
        # our own interior: a square brake lamp inboard, the tail lamp in a long band outboard
        brake = shp.intersection(box(b[0], b[1], b[0] + 0.11, b[3])).buffer(-0.01, join_style=2)
        tail = shp.intersection(box(b[0] + 0.125, b[1], b[2] + 1, b[3])).buffer(-0.01, join_style=2)
        out += blend.lay(ref, f"tail_back_{side}", shp.buffer(-0.002), wr, -0.024, -0.02, "housing", 0.008, along_ray=True)
        out += blend.lay(ref, f"lamp_brake_{side}", brake, wr, -0.019, -0.015, "lamp_brake", 0.006, along_ray=True)
        out += blend.lay(ref, f"lamp_tail_{side}", tail, wr, -0.019, -0.015, "lamp_tail", 0.006, along_ray=True)
        out += blend.lay(ref, f"tail_glass_{side}", shp.buffer(-0.002), wr, -0.0055, -0.0025, "tail_glass", 0.008, along_ray=True)


# ---------------------------------------------------------------- front
def front_parts(ref, cutters, out):
    fr = front_fr()
    g = R["grille"]
    mouth = soft(box(-g["w"], g["z0"], g["w"], g["z1"]), 0.006)
    cutters += blend.lay(ref, "cut_grille", mouth, fr, -0.035, 0.006, "grille", 0.01, along_ray=True)
    out += blend.lay(ref, "grille_back", mouth.buffer(-0.002), fr, -0.034, -0.03, "grille", 0.01, along_ray=True)
    for i, z in enumerate(g["slats"]):
        bar = box(-g["w"] + 0.004, z - 0.0045, g["w"] - 0.004, z + 0.0045)
        out += blend.lay(ref, f"grille_slat_{i}", bar, fr, -0.022, -0.004, "body", 0.008, along_ray=True)
    it = R["intake"]
    mouth = soft(box(-it["w"], it["z0"], it["w"], it["z1"]), 0.008)
    cutters += blend.lay(ref, "cut_intake", mouth, fr, -0.035, 0.006, "grille", 0.012, along_ray=True)
    out += blend.lay(ref, "intake_back", mouth.buffer(-0.002), fr, -0.034, -0.03, "grille", 0.012, along_ray=True)
    for i, z in enumerate(it["slats"]):
        bar = box(-it["w"] + 0.004, z - 0.005, it["w"] - 0.004, z + 0.005)
        out += blend.lay(ref, f"intake_slat_{i}", bar, fr, -0.022, -0.006, "trim", 0.01, along_ray=True)
    # the black rubbing strip round the nose, back to the front arches
    s = R["front_strip"]
    for sgn, side in ((1, "l"), (-1, "r")):
        wr = front_wrap(sgn)
        u1 = wr.side_u(s["x_end"])
        band = soft(box(-0.004, s["z0"], u1, s["z1"]), 0.006)
        out += blend.lay(ref, f"front_strip_{side}", band, wr, -0.001, 0.007, "trim", 0.01, along_ray=True)


def rear_parts(ref, out):
    v = R["valance"]
    out += blend.lay(ref, "valance", soft(box(-v["w"], v["z0"], v["w"], v["z1"]), 0.01), rear_fr(), -0.001, 0.003,
                     "trim", 0.012, along_ray=True)


# ---------------------------------------------------------------- glass and trim
SIDE_BAND = 0.016     # black frame round the side windows
GLASS_BAND = 0.018    # black band round the windscreen and the rear window


def dlo_line():
    """The traced window opening, its top edge refit as one even curve and its foot as the belt."""
    d = np.array(R["dlo"], float)
    top = d[:, 1] > np.max(d[:, 1]) - 0.03
    k = np.polyfit(d[top, 0], d[top, 1], 2)
    d[top, 1] = np.polyval(k, d[top, 0])
    low = d[:, 1] < belt(d[:, 0]) + 0.01
    d[low, 1] = belt(d[low, 0])
    return d


def dlo_shapes():
    """The side window opening, its glass (split by the B-pillar and the divider) and the black band."""
    dlo = soft(poly(dlo_line()), 0.012)
    glass = dlo.buffer(-SIDE_BAND, join_style=1)
    b0, b1 = R["b_pillar"]
    (dx0, dz0), (dx1, dz1) = R["divider"]
    div = Polygon([(dx0 + 0.016, dz0 + 0.1), (dx0 - 0.016, dz0 + 0.1), (dx1 - 0.016, dz1 - 0.1), (dx1 + 0.016, dz1 - 0.1)])
    glass = glass.difference(box(b0 - 0.012, -1, b1 + 0.012, 3)).difference(div)
    glass = unary_union([soft(p, 0.008) for p in getattr(glass, "geoms", [glass])])
    return dlo, glass, dlo.difference(glass.buffer(-0.004))


def screen_shapes():
    s = R["screen"]
    half = [(s["x_base"] + 0.01, 0.0), (s["x_base"] + 0.01, s["w_base"]), (s["x_top"], s["w_top"]), (s["x_top"], 0.0)]
    o = soft(both_plan(poly(half)), 0.04)
    g = o.buffer(-GLASS_BAND, join_style=1)
    return o, g, o.difference(g.buffer(-0.004))


def rear_shapes():
    s = R["rear_glass"]
    half = [(s["x_top"], 0.0), (s["x_top"], s["w_top"]), (s["x_base"] + 0.01, s["w_base"]), (s["x_base"] + 0.01, 0.0)]
    o = soft(both_plan(poly(half)), 0.035)
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
def _b_mid():
    return float(np.mean(R["b_pillar"]))


def b_path():
    """The shut line between the doors: down the middle of the B-pillar's black, then the traced line."""
    p = np.array(R["b_shut"], float)
    p[0, 0] = p[1, 0] = _b_mid()
    p[0, 1] = belt(_b_mid()) + 0.03
    return [p[0].tolist()] + geom.catmull_open(p[1:], 8).tolist()


def front_path():
    """The front door's front edge from just above the belt, then its bottom back to the B line."""
    p = np.array(R["front_door"], float)
    p[0, 1] = max(p[0, 1], belt(p[0, 0]) + 0.03)
    edge = geom.catmull_open(p, 8)
    bz = R["door_bottom"]
    xb = float(np.array(b_path())[-1][0])
    return edge.tolist() + [[xb, bz]]


def rear_path():
    """The rear door's rear edge, from the window frame at the roof down the C-pillar, round the
    arch, then its bottom forward to the B line."""
    p = np.array(R["rear_door"], float)
    edge = geom.catmull_open(p, 8)
    xb = float(np.array(b_path())[-1][0])
    return edge.tolist() + [[xb, R["rear_door_bottom"]]]


def bonnet_loop():
    half = np.array(R["bonnet"], float)
    pts = [tuple(p) for p in half] + [(x, -y) for x, y in half[-2::-1]]
    return geom.catmull_closed(pts, 6)


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
    xr = TAIL + 0.13                    # the boot's rear face, above the bumper
    zd = body.z_top(R["lid"]["x_glass"] - 0.3)
    return LidWrap(xr + 0.05, zd - 0.05, 0.05)


def lid_loop():
    fr = lid_wrap()
    L = R["lid"]
    u0 = fr.deck(L["x_glass"] - 0.03)
    u1 = fr.tail(L["z0"] + 0.006)
    shape = soft(box(u0, -L["w"], u1, L["w"]), 0.03)
    return fr, np.array(shape.exterior.coords)[:-1]


def bumper_path(wr):
    """The rear bumper's top edge in the rear wrap's (u, z): across the back, then forward along the side."""
    pts = np.array(R["bumper_top"], float)
    zb = pts[-1, 1]
    side = [(wr.side_u(x), z) for x, z in pts[::-1] if wr.side_u(x) > wr.u1]
    return [(-0.01, zb), (wr.u1 - 0.02, zb)] + side


def grooves(ref):
    """Shut lines as V grooves to cut: doors, bonnet, boot lid, rear bumper."""
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        fr = side_fr(sgn)
        obs.append(blend.groove(ref, f"g_fdoor_{side}", front_path(), fr))
        obs.append(blend.groove(ref, f"g_bdoor_{side}", b_path(), fr))
        obs.append(blend.groove(ref, f"g_rdoor_{side}", rear_path(), fr))
        wr = rear_wrap(sgn)
        path = sorted(bumper_path(wr), key=lambda p: p[0])
        obs.append(blend.groove(ref, f"g_bumper_{side}", path, wr))
    obs.append(blend.groove(ref, "g_bonnet", bonnet_loop(), top_fr(), closed=True))
    fr, lid = lid_loop()
    obs.append(blend.groove(ref, "g_lid", lid, fr, closed=True))
    return obs


# ---------------------------------------------------------------- front doors
DOOR_DEPTH = 0.05     # below the cabin floor the door takes the sill this deep
DOOR_WALL = 0.08      # above it, the whole wall: the cabin is hollow behind it
DOOR_GAP = 0.0015     # half the gap round the door, inside the shut line's groove
DOOR_OPEN = 65.0      # degrees the door swings out
FLOOR = 0.26


def door_outline():
    """The left front door seen from the side: inside its front edge, bottom and the B line,
    closed across the belt, and the window frame round its glass back to the B-pillar's middle."""
    front = front_path()
    b = b_path()
    bc = _b_mid()
    lower = Polygon(front + b[::-1]).buffer(0)
    dlo, _g, _t = dlo_shapes()
    frame = dlo.buffer(SIDE_BAND * 0.5, join_style=2).intersection(box(bc, -1, 5, 5))
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
    """Cut the left (sgn 1) or right (sgn -1) front door free of the body and its window trim; its
    glass, card and mirror go with it."""
    side = "l" if sgn > 0 else "r"
    o = door_outline()
    regions = [(o.intersection(box(-5, -1, 5, FLOOR + 0.02)), DOOR_DEPTH),
               (o.intersection(box(-5, FLOOR + 0.02, 5, 5)), DOOR_WALL)]
    trim = [ob for ob in obs if ob.name == f"window_trim_{side}"]
    roles = {f"side_glass_{side}": "glass", f"side_glass_{side}_1": "glass", f"side_glass_{side}_2": "glass",
             f"mirror_{side}": "mirror", f"mirror_glass_{side}": "mirror_glass", f"door_card_{side}": "card"}
    return blend.free_door(ref, side_fr(sgn), regions, DOOR_GAP, shell, trim, roles, o, name=f"door_{side}")


# ---------------------------------------------------------------- mirrors and exhaust
MIRROR_R = 0.022      # housing corner rounding
MIRROR_SWEEP = 0.15   # radians the housing turns back from square to the car


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
    """A black housing on a short stalk from the door's front top corner, and its glass in a
    recess in the housing's back."""
    m = R["mirror"]
    xb, xf = m["x_back"], m["x_front"]
    x0, x1 = xb, xb + 0.065
    y0, y1, z0, z1 = m["y_in"] + 0.025, m["y_out"], m["z0"], m["z1"]
    root = body.y_at(xb + 0.04, belt(xb + 0.04) - 0.02) - 0.015
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        house = _rounded_bar(f"mirror_{side}", y0, y1, z0, z1, x0, x1, MIRROR_R, sgn)
        arm = _rounded_bar(f"mirror_arm_{side}", root, y0 + 0.02, belt(xb) - 0.035, z0 + 0.035, x0 + 0.012, x1 - 0.008, 0.01, sgn)
        blend.boolean(house, arm, "UNION")
        cut = _rounded_bar(f"cut_mirror_{side}", y0 + 0.008, y1 - 0.008, z0 + 0.008, z1 - 0.008, x0 - 0.03, x0 + 0.006,
                           MIRROR_R - 0.008, sgn)
        blend.boolean(house, cut, "DIFFERENCE")
        glass = _rounded_bar(f"mirror_glass_{side}", y0 + 0.0095, y1 - 0.0095, z0 + 0.0095, z1 - 0.0095, x0 + 0.0015,
                             x0 + 0.0045, MIRROR_R - 0.0095, sgn, "mirror_glass")
        obs += [_sweep(house, sgn), _sweep(glass, sgn)]
    return obs


def exhaust():
    """One round tip under the right of the rear bumper."""
    e = R["exhaust"]
    y, z, r = e["y"], e["z"], e["r"]
    x0 = TAIL + 0.02
    t = np.linspace(0, 2 * np.pi, 40, endpoint=False)
    outer = np.column_stack([y + r * np.cos(t), z + r * np.sin(t)])
    inner = np.column_stack([y + (r - 0.004) * np.cos(t), z + (r - 0.004) * np.sin(t)])
    rings = [np.column_stack([np.full(40, x), rg]) for x, rg in
             ((x0, inner), (x0, outer), (x0 + 0.35, outer), (x0 + 0.35, inner))]
    v, f = geom.close_rings(rings, cap=False)
    m = 40
    for j in range(m):
        k = (j + 1) % m
        f.append((3 * m + j, 3 * m + k, k, j))
    return [blend.make("exhaust", v, f, "dark_metal")]


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


STEER = (0.30, 0.33, 0.78)     # the wheel's centre, in front of the left seat
STEER_TILT = 35.0               # degrees the wheel's face turns up from upright


def steering():
    """A plain round wheel on the left, square to its column, with three spokes and the column."""
    from mathutils import Matrix
    r, t = 0.18, 0.014
    v, f = geom.lathe([(r - t + t * np.cos(a), t * np.sin(a)) for a in np.linspace(0, 2 * np.pi, 12, endpoint=False)], 64, axis="y")
    rim = blend.make("steering", v, f, "interior")
    parts = [geom.box(-0.012, 0.012, -0.008, 0.008, -r + 0.01, -0.03), geom.box(-r + 0.01, -0.03, -0.008, 0.008, -0.012, 0.012),
             geom.box(0.03, r - 0.01, -0.008, 0.008, -0.012, 0.012)]
    obs = [rim] + [blend.make("spoke", *p, "interior") for p in parts]
    v, f = geom.disc((0, 0, 0), (0, 1, 0), 0.045, 0.05, n=32)
    obs.append(blend.make("boss", v, f, "interior"))
    v, f = geom.disc((0, -0.17, 0), (0, 1, 0), 0.025, 0.30, n=24)
    obs.append(blend.make("column", v, f, "interior"))
    wheel = blend.union_all(obs, "steering")
    # local y (the column, pointing at the driver) turns to face back and up
    wheel.data.transform(Matrix.Translation(STEER) @ Matrix.Rotation(np.radians(STEER_TILT), 4, "Y")
                         @ Matrix.Rotation(np.pi / 2, 4, "Z"))
    return wheel


def interior():
    x0 = R["screen"]["x_base"] - 0.05
    belt_z = belt(0.0)
    obs = [_loft("firewall", x0 - 0.010, x0 - 0.004, lambda x: box(-0.8, FLOOR + 0.002, 0.8, 3), n=2),
           _loft("dash", x0 - 0.0125, 0.36, lambda x: box(-0.8, belt_z - 0.12, 0.8, belt_z - 0.12 + 0.15 * (1 - (x0 - x) / 0.5) + 0.04), n=10)]
    bc = _b_mid()
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, FLOOR + 0.08, 1.2, belt(x) - 0.02) if s > 0 else box(-1.2, FLOOR + 0.08, -0.3, belt(x) - 0.02)).intersection(
            body.inside(x, 0.035 + 0.003)).difference(body.inside(x, 0.035 + 0.022))
        obs.append(_loft(f"door_card_{side}", bc + 0.006, R["front_door"][2][0] - 0.03, clip, n=10))
        obs.append(_loft(f"door_card_{side}_rear", R["rear_door"][-2][0] + 0.03, bc - 0.006, clip, n=8))
        yc = 0.33 * sgn
        obs.append(_rbox(f"seat_base_{side}", -0.32, 0.18, yc - 0.23, yc + 0.23, FLOOR + 0.002, FLOOR + 0.22))
        obs.append(_rbox(f"seat_back_{side}", -0.46, -0.34, yc - 0.22, yc + 0.22, FLOOR + 0.221, FLOOR + 0.72, lean=0.22))
        obs.append(_rbox(f"headrest_{side}", -0.58, -0.49, yc - 0.12, yc + 0.12, FLOOR + 0.721, FLOOR + 0.84, r=0.035, lean=0.1))
    obs.append(_rbox("bench_base", -1.02, -0.58, -0.43, 0.43, FLOOR + 0.004, FLOOR + 0.22))
    obs.append(_rbox("bench_back", -1.16, -1.00, -0.43, 0.43, FLOOR + 0.221, FLOOR + 0.70, lean=0.25))
    obs.append(steering())
    xm = R["screen"]["x_top"] - 0.03
    roof_in = body.inside(xm, 0.035).bounds[3]
    top = roof_in - 0.06
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.11, 0.11, top - 0.05, top, r=0.018, mat="interior")
    stem = _rbox("stem", xm - 0.016, xm - 0.004, -0.012, 0.012, top - 0.015, roof_in - 0.0008, r=0.004, mat="interior")
    obs.append(blend.union_all([m, stem], "mirror_rear"))
    return obs


# ---------------------------------------------------------------- wheels
HOLES = 12           # round holes in the steel wheel's face


def wheels():
    """Plain pressed steel wheels: a dished face with a ring of round holes and a small cap."""
    obs = []
    hw = W["width"] / 2
    yc = W["outer_y"] - hw
    rr = W["rim_r"]
    tyre = soft(box(rr, -hw, W["r"], hw), 0.03).union(box(rr, -hw + 0.012, rr + 0.03, hw - 0.012))
    tp = body.even(np.array(tyre.exterior.coords)[:-1], 96)
    # the rim: lip, a dish stepping in, the face, and the hub boss, as one closed profile (r, y)
    rim = [(rr - 0.001, -hw + 0.012), (rr - 0.001, hw - 0.012), (rr - 0.012, hw - 0.004), (rr - 0.02, hw - 0.012),
           (rr - 0.03, hw - 0.03), (0.075, hw - 0.035), (0.05, hw - 0.022), (0.0, hw - 0.022), (0.0, hw - 0.05),
           (rr - 0.035, hw - 0.05), (rr - 0.02, -hw + 0.012)]
    hole_r = 0.011
    ring_r = 0.5 * (0.075 + rr - 0.03)
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
            for k in range(HOLES):
                a = 2 * np.pi * (k + 0.5) / HOLES
                v, f = geom.disc((ring_r * np.cos(a), hw - 0.035, ring_r * np.sin(a)), (0, 1, 0), hole_r, 0.06, n=20)
                cutters.append(blend.make("hole", place(v), f, "tyre"))
            blend.boolean(rimob, blend.union_all(cutters, "holes"), "DIFFERENCE")
            v, f = geom.disc((0, hw - 0.018, 0), (0, 1, 0), 0.035, 0.012, n=32)
            cap = blend.make(f"wheel_cap_{fb}{lr}", place(v), f, "dark_metal")
            obs += [rimob, cap]
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
            for s in np.linspace(0, 1, 13)[1:]:
                cur.append(tuple((1 - s) ** 3 * p0 + 3 * (1 - s) ** 2 * s * a + 3 * (1 - s) * s * s * b + s ** 3 * c))
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
            g = g.difference(above.buffer(0.4))
        out.append((g, FILLS[fill]))
    whole = layers[0][0]
    shapes = _fit([g for g, _m in out] + [whole], whole)
    return [(s, m) for s, (_g, m) in zip(shapes[:-1], out) if not s.is_empty]


def badge_chrome():
    """The chrome badge: the rim of the drop, the sun and the mountains, each kept apart by a fine gap."""
    layers = _badge_layers()
    drop, inner, sun, hills, peak = (g for g, _f in layers)
    rim = drop.difference(inner.buffer(1.1))
    hills = hills.difference(peak.buffer(1.1))
    whole = unary_union([rim, sun, hills, peak])
    return _fit([whole], drop)[0]


def name_shape():
    """Carrier in the brand font, leaning like the brand's wordmark, 1 unit = its cap height."""
    gl = json.loads((HERE / "glyphs.json").read_text())
    g = unary_union([Polygon(p[0], p[1:]) for p in gl["pieces"]])
    g = skew(g, xs=12, origin=(0, 0))
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
    g = R["grille"]
    lid_z = (R["lid"]["z0"] + body.z_top(R["lid"]["x_glass"] - 0.4)) / 2
    lid_y = R["lid"]["w"] * 0.62
    for k, style in ((0, "chrome"), (1, "colour")):
        # front: on the panel above the grille's middle (the reference), or on the bonnet's nose
        add("frontbadge", 1 + k, badge(ref, style, 0.075, front_fr(), 0, g["z1"] + 0.045, up, "fb"))
        add("frontbadge", 3 + k, badge(ref, style, 0.06, top_fr(), NOSE - 0.12, 0, fwd, "fb"))
        # rear: on the boot lid's left, where the reference carries its maker's name, or in its middle
        add("rearbadge", 1 + k, badge(ref, style, 0.06, rear_fr(), lid_y, lid_z, up, "rb"))
        add("rearbadge", 3 + k, badge(ref, style, 0.06, rear_fr(), 0, lid_z, up, "rb"))
    cap = 0.032
    # name: on the boot lid's right (the reference), or low on both front wings behind the arch
    add("name", 1, _flat(ref, nm, cap, rear_fr(), -lid_y, lid_z, up, "nm", 0.0012, NAME_T, "chrome"))
    q = []
    wx = W["front_x"] - W["arch_r"] - 0.16
    for sgn in (1, -1):
        q += _flat(ref, nm, cap, side_fr(sgn), wx, belt(wx) - 0.20, up, "nm", 0.0012, NAME_T, "chrome")
    add("name", 2, q)
    return out
