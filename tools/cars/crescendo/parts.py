"""The Crescendo's parts: lamps, grille, intakes, glass, trim, mirrors, exhaust, interior,
wheels, badge and name. Every shape and size here is the Crescendo's own, from rows.json and
Ivan's references, kept plain and smooth. Runs inside Blender's Python module, called by export.py."""
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
CV = body.CV
NOSE, TAIL = body.NOSE, body.TAIL
W = R["wheel"]

MATERIALS = {
    # name: (rgb, spec, alpha, emit)
    "body": ((0.98, 0.58, 0.04), 0.75, 1.0, None),
    "trim": ((0.025, 0.025, 0.03), 0.65, 1.0, None),
    "grille": ((0.04, 0.04, 0.045), 0.3, 1.0, None),
    "glass": ((0.07, 0.09, 0.11), 0.92, 0.45, None),
    "head_glass": ((0.92, 0.94, 0.96), 0.95, 0.22, None),
    "tail_glass": ((0.55, 0.03, 0.04), 0.92, 0.55, None),
    "housing": ((0.05, 0.05, 0.055), 0.5, 1.0, None),
    "reflector": ((0.78, 0.79, 0.81), 0.97, 1.0, None),
    "lamp_head": ((0.95, 0.95, 0.92), 0.6, 1.0, (1.0, 0.97, 0.9)),
    "lamp_tail": ((0.5, 0.03, 0.04), 0.5, 1.0, (1.0, 0.06, 0.04)),
    "lamp_brake": ((0.6, 0.04, 0.05), 0.5, 1.0, (1.0, 0.04, 0.03)),
    "chrome": ((0.86, 0.87, 0.89), 0.98, 1.0, None),
    "badge_black": ((0.11, 0.11, 0.12), 0.6, 1.0, None),
    "badge_red": ((0.83, 0.13, 0.17), 0.6, 1.0, None),
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


def poly(pts):
    return Polygon(pts).buffer(0)


def flip(g):
    """Mirror a 2D shape about its first axis (y for front and rear frames)."""
    return sscale(g, -1, 1, origin=(0, 0))


def soft(g, r):
    """Round off a shape's corners."""
    return g.buffer(-r, join_style=1).buffer(r, join_style=1)


class Wrap:
    """A frame bending round a front or rear corner of the body. In plan it is a rounded
    rectangle: rays run straight in across the face, fan out round a corner of radius rc
    centred at (xc, yc), then run straight in along the side. 2D (u, z): u runs from the
    centre line out round the corner and along the side."""

    def __init__(self, end, sgn, xc, yc, rc):
        self.f = 1 if end == "front" else -1
        self.sgn, self.xc, self.yc, self.rc = sgn, xc, yc, rc
        self.u1 = yc + rc * np.pi / 2

    def __call__(self, u, z):
        f, s = self.f, self.sgn
        if u <= self.yc:
            o, d = (self.xc + f * (self.rc + 0.5), u), (-f, 0.0)
        elif u >= self.u1:
            o, d = (self.xc - f * (u - self.u1), self.yc + self.rc + 0.5), (0.0, -1.0)
        else:
            t = (u - self.yc) / self.rc
            c, n = np.cos(t), np.sin(t)
            o, d = (self.xc + f * c * (self.rc + 0.5), self.yc + n * (self.rc + 0.5)), (-f * c, -n)
        return np.array([o[0], o[1] * s, z]), np.array([d[0], d[1] * s, 0.0])

    def to_uv(self, p):
        """The (u, z) whose ray passes through the 3D point p."""
        x, y, z = float(p[0]), abs(float(p[1])), float(p[2])
        dx = self.f * (x - self.xc)
        if y <= self.yc:
            return y, z
        if dx <= 0:
            return self.u1 - dx, z
        return self.yc + self.rc * np.arctan2(y - self.yc, dx), z

    def outline(self, tris, r=0.012):
        """One smooth outline in (u, z) of a lamp's triangles."""
        u = unary_union([Polygon([self.to_uv(p) for p in t]).buffer(1e-4) for t in tris])
        u = u.buffer(0.01, join_style=1).buffer(-0.01, join_style=1)
        if u.geom_type != "Polygon":
            u = max(u.geoms, key=lambda g: g.area)
        return soft(Polygon(u.exterior), r).simplify(0.002)


HEAD_WRAP = dict(end="front", xc=NOSE - 0.45, yc=0.40, rc=0.45)
TAIL_WRAP = dict(end="rear", xc=TAIL + 0.45, yc=0.40, rc=0.45)


# ---------------------------------------------------------------- lamps
def headlamps(ref, cutters, out, sgn):
    side = "l" if sgn > 0 else "r"
    wr = Wrap(sgn=sgn, **HEAD_WRAP)
    shp = wr.outline(R["head"])
    cutters += blend.lay(ref, f"cut_head_{side}", shp, wr, -0.016, 0.006, "housing", 0.01, along_ray=True)
    keep = shp.buffer(-0.01)
    for i, (x, y, z, r) in enumerate(R["head_lenses"]):
        c = Point(*wr.to_uv((x, y, z)))
        r = min(r, 0.045)
        out += blend.lay(ref, f"lamp_head_{side}{i}", c.buffer(r, 48).intersection(keep), wr,
                         -0.0105, -0.007, "lamp_head", 0.004, along_ray=True)
        out += blend.lay(ref, f"head_reflector_{side}{i}", c.buffer(r + 0.013, 48).difference(c.buffer(r + 0.002, 48)).intersection(keep),
                         wr, -0.0102, -0.0088, "reflector", 0.004, along_ray=True)
    out += blend.lay(ref, f"head_glass_{side}", shp.buffer(-0.003), wr, -0.0045, -0.0015, "head_glass", 0.01, along_ray=True)


def taillamps(ref, cutters, out, sgn):
    side = "l" if sgn > 0 else "r"
    wr = Wrap(sgn=sgn, **TAIL_WRAP)
    shp = wr.outline(R["tail"])
    cutters += blend.lay(ref, f"cut_tail_{side}", shp, wr, -0.016, 0.006, "housing", 0.01, along_ray=True)
    core = shp.buffer(-0.012)
    b = shp.bounds
    zsplit = b[1] + 0.45 * (b[3] - b[1])
    out += blend.lay(ref, f"lamp_tail_{side}", core.intersection(box(-5, zsplit + 0.004, 5, 5)), wr,
                     -0.011, -0.008, "lamp_tail", 0.005, along_ray=True)
    out += blend.lay(ref, f"lamp_brake_{side}", core.intersection(box(-5, -5, 5, zsplit - 0.004)), wr,
                     -0.011, -0.008, "lamp_brake", 0.005, along_ray=True)
    out += blend.lay(ref, f"tail_glass_{side}", shp.buffer(-0.003), wr, -0.0045, -0.0015, "tail_glass", 0.01, along_ray=True)


# ---------------------------------------------------------------- front
def grille_shape():
    """The grille seen from ahead, both halves, corners rounded."""
    g = poly(R["grille"])
    return soft(g.union(flip(g)), 0.02)


GRILLE_BARS = (0.40, 0.66)    # the two chrome bars, as fractions up the grille


def front_parts(ref, cutters, out):
    fr = front_fr()
    g = grille_shape()
    cutters += blend.lay(ref, "cut_grille", g, fr, -0.03, 0.006, "grille", 0.01, along_ray=True)
    out += blend.lay(ref, "grille_ring", g.difference(g.buffer(-0.012)), fr, -0.003, 0.003, "chrome", 0.005)
    out += blend.lay(ref, "grille_back", g.buffer(-0.006), fr, -0.0245, -0.021, "grille", 0.012, along_ray=True)
    b = g.bounds
    for i, k in enumerate(GRILLE_BARS):
        z = b[1] + k * (b[3] - b[1])
        bar = g.buffer(-0.01).intersection(box(-5, z - 0.009, 5, z + 0.009))
        out += blend.lay(ref, f"grille_bar_{i}", bar, fr, -0.02, -0.004, "chrome", 0.006, along_ray=True)
    mid = soft(poly(R["intake_mid"]).union(flip(poly(R["intake_mid"]))), 0.02)
    cutters += blend.lay(ref, "cut_intake_mid", mid, fr, -0.03, 0.006, "grille", 0.012, along_ray=True)
    out += blend.lay(ref, "intake_mid_back", mid.buffer(-0.006), fr, -0.0245, -0.021, "grille", 0.012, along_ray=True)
    fy, fz, fr_r = R["fog"]
    for sgn, side in ((1, "l"), (-1, "r")):
        s = soft(poly(R["intake_side"]), 0.015)
        s = s if sgn < 0 else flip(s)
        cutters += blend.lay(ref, f"cut_intake_{side}", s, fr, -0.03, 0.006, "grille", 0.012, along_ray=True)
        out += blend.lay(ref, f"intake_back_{side}", s.buffer(-0.006), fr, -0.0245, -0.021, "grille", 0.012, along_ray=True)
        c = Point(fy * sgn, fz)
        cutters += blend.lay(ref, f"cut_fog_{side}", c.buffer(fr_r + 0.006, 48), fr, -0.025, 0.006, "housing", 0.006, along_ray=True)
        out += blend.lay(ref, f"fog_ring_{side}", c.buffer(fr_r + 0.002, 48).difference(c.buffer(fr_r - 0.012, 48)),
                         fr, -0.0195, -0.016, "chrome", 0.004, along_ray=True)
        out += blend.lay(ref, f"lamp_head_fog_{side}", c.buffer(fr_r - 0.0135, 48), fr, -0.02, -0.0165, "lamp_head", 0.004, along_ray=True)


# ---------------------------------------------------------------- glass and trim
MIRROR_X0, MIRROR_X1 = R["mirror"]["x_back"], R["mirror"]["x_front"]


def dlo_line():
    """The measured window opening with its top edge refit as one smooth curve, so the roof
    line shows no dip over the B-pillar."""
    d = np.array(R["dlo"], float)
    top = (d[:, 1] > 1.25) & (d[:, 0] > -0.95) & (d[:, 0] < 0.15)
    d[top, 1] = np.polyval(np.polyfit(d[top, 0], d[top, 1], 3), d[top, 0])
    return d


def dlo_shapes():
    """The side window opening, its glass (split by the pillars) and the black band round it."""
    dlo = soft(poly(dlo_line()), 0.015)
    glass = dlo.buffer(-0.02, join_style=1)
    b0, b1 = R["b_pillar"]
    d0, d1 = R["divider"]
    glass = glass.difference(box(b0 - 0.012, -1, b1 + 0.012, 3)).difference(box(d0 - 0.006, -1, d1 + 0.006, 3))
    glass = glass.difference(box(MIRROR_X0 + 0.04, -1, 5, 5))       # the corner ahead of the mirror is black
    glass = unary_union([soft(p, 0.008) for p in getattr(glass, "geoms", [glass])])
    return dlo, glass, dlo.difference(glass)


def screen_shapes():
    outline = soft(poly(R["screen"]["outline"]), 0.03)
    glass = outline.buffer(-0.03, join_style=1)
    return outline, glass, outline.difference(glass)


def rear_shapes():
    rg = soft(poly(R["rear_glass"]), 0.03)
    glass = rg.buffer(-0.016, join_style=1)
    return rg, glass, rg.difference(glass)


def glass_parts(ref, cutters, out):
    _d, g, t = dlo_shapes()
    for sgn, side in ((1, "l"), (-1, "r")):
        fr = side_fr(sgn)
        cutters += blend.lay(ref, f"cut_side_{side}", g.buffer(-0.003), fr, -0.08, 0.006, "trim", along_ray=True)
        out += blend.lay(ref, f"side_glass_{side}", g.buffer(-0.006), fr, -0.008, -0.004, "glass", 0.02, along_ray=True)
        out += blend.lay(ref, f"window_trim_{side}", t, fr, 0.0015, 0.0035, "trim", 0.01)
    _o, g, t = screen_shapes()
    cutters += blend.lay(ref, "cut_screen", g.buffer(-0.003), top_fr(), -0.08, 0.006, "trim", along_ray=True)
    out += blend.lay(ref, "screen_glass", g.buffer(-0.006), top_fr(), -0.008, -0.004, "glass", 0.02, along_ray=True)
    out += blend.lay(ref, "screen_trim", t, top_fr(), 0.0015, 0.0035, "trim", 0.01)
    _o, g, t = rear_shapes()
    cutters += blend.lay(ref, "cut_rear", g.buffer(-0.003), rear_fr(), -0.08, 0.004, "trim", along_ray=True)
    out += blend.lay(ref, "rear_glass", g.buffer(-0.006), rear_fr(), -0.008, -0.004, "glass", 0.02, along_ray=True)
    out += blend.lay(ref, "rear_trim", t, rear_fr(), 0.0015, 0.0035, "trim", 0.01)


# ---------------------------------------------------------------- shut lines
FRONT_DOOR_X = 0.88      # the front door's front edge at the belt


def belt(x):
    """Height of the belt line (the window opening's straight bottom edge) at x."""
    k = R["belt"]
    return k[0] * x + k[1]


def _open_below(pts, zcut):
    """The part of a closed outline below zcut, as an open path from one end of the removed
    top to the other."""
    pts = np.asarray(pts, float)
    keep = pts[:, 1] < zcut
    k = int(np.argmax(~keep & np.roll(keep, 1)))         # first dropped point after a kept one
    rolled, kr = np.roll(pts, -k, axis=0), np.roll(keep, -k)
    return rolled[kr]


def _into_glass(p, d=0.025):
    """The point of the window opening nearest p, moved d into it, so a shut line ends on the
    black trim."""
    dlo = poly(R["dlo"])
    q = np.array(dlo.exterior.interpolate(dlo.exterior.project(Point(p))).coords[0])
    c = np.array(dlo.centroid.coords[0])
    return (q + (c - q) / np.linalg.norm(c - q) * d).tolist()


def door_paths():
    """Rear door: its measured outline below its top edge, carried up to the window opening.
    Front door: a plain front edge from the shoulder under the A-pillar and a bottom edge
    running back to the rear door."""
    rd_all = np.array(R["rear_door"])
    rd = _open_below(rd_all, rd_all[:, 1].max() - 0.06)
    if rd[0][0] > rd[-1][0]:
        rd = rd[::-1]
    # the shared edge runs down the middle of the B-pillar black, so each door shows the same width
    bc = float(np.mean(R["b_pillar"]))
    edge = rd[:, 0] > bc - 0.04
    rd[edge, 0] += bc - float(rd[-1][0]) + 0.005
    rear = [_into_glass(rd[0])] + rd.tolist() + [[bc, belt(bc) + 0.03]]
    bottom = float(rd[:, 1].min())
    fe = rd[edge]
    z_end = bottom + 0.004
    xs = float(np.interp(z_end, fe[::-1, 1] if fe[0, 1] > fe[-1, 1] else fe[:, 1],
                         fe[::-1, 0] if fe[0, 1] > fe[-1, 1] else fe[:, 0]))
    x0 = MIRROR_X0 + MIRROR_D + 0.03       # clear of the mirror arm
    front = [_into_glass((x0, belt(x0)))] + geom.catmull_open([
        (x0, belt(x0) - 0.002), (FRONT_DOOR_X - 0.06, belt(FRONT_DOOR_X - 0.06) + 0.004),
        (FRONT_DOOR_X, belt(FRONT_DOOR_X) - 0.05), (FRONT_DOOR_X + 0.01, 0.72), (FRONT_DOOR_X - 0.005, 0.5),
        (FRONT_DOOR_X - 0.06, bottom + 0.004)], 8).tolist()
    front += [[xs - 0.002, z_end]]
    return rear, front


def bonnet_loop():
    """The bonnet in plan: a plain arc at the cowl, straight sides along the wing tops, then
    back over the headlamps' top edges and straight across above the grille."""
    half = [(0.97, 0.0), (0.955, 0.3), (0.925, 0.55), (0.905, 0.70), (0.935, 0.775), (1.02, 0.79),
            (1.25, 0.79), (1.50, 0.79), (1.68, 0.78), (1.77, 0.735), (1.85, 0.655), (1.92, 0.565),
            (1.975, 0.46), (2.005, 0.33), (2.02, 0.16), (2.025, 0.0)]
    pts = half + [(x, -y) for x, y in half[-2:0:-1]]
    return geom.catmull_closed(pts, 8)


class LidWrap:
    """A frame bending over the boot's back corner, for the lid's shut line. 2D (u, y): u runs
    back along the deck (rays straight down), round the corner, then down the tail (rays
    straight forward). The corner is centred at (xc, zc) with radius rc."""

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


LID_WRAP = dict(xc=TAIL + 0.27, zc=0.95, rc=0.10)


def lid_loop():
    """Boot lid as one loop in the LidWrap frame: across the deck at the rear glass foot, back
    along both deck sides at one width, over the corner, then down the tail inside the lamps
    and across under them."""
    fr = LidWrap(**LID_WRAP)
    zc = LID_WRAP["zc"]
    lid = poly(R["lid"])
    low = lid.intersection(box(-5, -5, 5, zc))
    ys = float(low.bounds[2])
    x_glass = float(np.min(np.array(R["rear_glass_side"])[:, 0]))
    u0 = fr.deck(x_glass - 0.03)
    ring = np.array(low.exterior.coords)[:-1]
    shape = Polygon([(fr.tail(z), y) for y, z in ring]).union(box(u0, -ys, fr.tail(zc) + 0.001, ys))
    shape = soft(shape.buffer(0), 0.03)
    return fr, np.array(shape.exterior.coords)[:-1]


def grooves(ref):
    """Shut lines as V grooves to cut: doors, bonnet, boot lid. Each loop is one closed groove."""
    obs = []
    rear, front = door_paths()
    for sgn, side in ((1, "l"), (-1, "r")):
        obs.append(blend.groove(ref, f"g_rdoor_{side}", rear, side_fr(sgn)))
        obs.append(blend.groove(ref, f"g_fdoor_{side}", front, side_fr(sgn)))
    obs.append(blend.groove(ref, "g_bonnet", bonnet_loop(), top_fr(), closed=True))
    fr, lid = lid_loop()
    obs.append(blend.groove(ref, "g_lid", lid, fr, closed=True))
    return obs


# ---------------------------------------------------------------- mirrors and exhaust
MIRROR_H = 0.11      # housing height
MIRROR_W = 0.19      # housing width, out from the arm
MIRROR_D = 0.075     # housing depth front to back
MIRROR_R = 0.03      # corner rounding
MIRROR_SWEEP = 0.22  # radians the housing turns back from square to the car


def _rounded_bar(name, y0, y1, z0, z1, x0, x1, r, sgn, mat="body"):
    face = soft(box(y0, z0, y1, z1), r)
    ring = body.even(np.array(face.exterior.coords)[:-1], 96)
    rings = [np.column_stack([np.full(96, x), ring[:, 0] * sgn, ring[:, 1]]) for x in (x0, x1)]
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def _mirror_root():
    m = R["mirror"]
    return body.y_at(m["x_back"] + 0.05, R["belt_z"] - 0.03) - 0.02      # sunk into the door


def _housing():
    m = R["mirror"]
    zc = (m["z0"] + m["z1"]) / 2
    return m["y_out"] - MIRROR_W, m["y_out"], zc - MIRROR_H / 2, zc + MIRROR_H / 2


def _sweep(ob, sgn):
    from mathutils import Matrix
    m = R["mirror"]
    p = Matrix.Translation((m["x_back"] + MIRROR_D / 2, sgn * _mirror_root(), 0))
    ob.data.transform(p @ Matrix.Rotation(sgn * MIRROR_SWEEP, 4, "Z") @ p.inverted())
    return ob


def mirrors():
    """A rounded housing on a short arm from the door's front top corner."""
    m = R["mirror"]
    y0, y1, z0, z1 = _housing()
    xb = m["x_back"]
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        house = _rounded_bar(f"mirror_{side}", y0, y1, z0, z1, xb, xb + MIRROR_D, MIRROR_R, sgn)
        arm = _rounded_bar(f"mirror_arm_{side}", _mirror_root(), y0 + 0.03, R["belt_z"] - 0.04, z0 + 0.04,
                           xb + 0.012, xb + MIRROR_D - 0.008, 0.012, sgn)
        blend.boolean(house, arm, "UNION")
        obs.append(_sweep(house, sgn))
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


def _oval(cy, cz, a, b, n=48):
    t = np.linspace(0, 2 * np.pi, n, endpoint=False)
    return np.column_stack([cy + a * np.cos(t), cz + b * np.sin(t)])


def exhaust():
    """Two oval tips each side, in one black pocket."""
    e = R["exhaust"]
    w = (e["y_out"] - e["y_in"]) / 2           # one tip's width
    a, b = w / 2 - 0.004, e["h"] / 2
    cut, pipes = [], []
    for sgn, side in ((1, "l"), (-1, "r")):
        x_end = _tail_x(e["y_out"], e["z"]) - 0.02
        pocket = soft(box(e["y_in"] - 0.012, e["z"] - b - 0.012, e["y_out"] + 0.012, e["z"] + b + 0.012), b)
        ring = body.even(np.array(pocket.exterior.coords)[:-1], 96)
        rings = [np.column_stack([np.full(96, x), ring[:, 0] * sgn, ring[:, 1]]) for x in (x_end + 0.08, x_end - 0.3)]
        v, f = geom.close_rings(rings)
        cut.append(blend.make(f"cut_exhaust_{side}", v, f, "trim"))
        for i in range(2):
            cy = e["y_in"] + w * (i + 0.5)
            outer, inner = _oval(cy, e["z"], a, b), _oval(cy, e["z"], a - 0.005, b - 0.005)
            rings = []
            for x, rg in ((x_end, inner), (x_end, outer), (x_end + 0.2, outer), (x_end + 0.2, inner)):
                rings.append(np.column_stack([np.full(len(rg), x), rg[:, 0] * sgn, rg[:, 1]]))
            v, f = geom.close_rings(rings, cap=False)
            m = len(rings[0])
            for j in range(m):                     # close the tube: inner wall back to the first ring
                k = (j + 1) % m
                f.append((3 * m + j, 3 * m + k, k, j))
            if sgn < 0:
                f = [tuple(q[::-1]) for q in f]
            pipes.append(blend.make(f"exhaust_{side}{i}", v, f, "chrome"))
    return cut, pipes


def arches():
    """Arch cutters and the wheel tubs kept out of the cabin."""
    cut, tubs = [], []
    for x in (W["front_x"], W["rear_x"]):
        for sgn in (1, -1):
            v, f = geom.disc((x, 0.84 * sgn, W["r"]), (0, 1, 0), W["arch_r"], 0.56, n=96)
            cut.append(blend.make("arch", v, f, "interior"))
            v, f = geom.disc((x, 0.86 * sgn, W["r"]), (0, 1, 0), W["arch_r"] + 0.035, 0.70, n=96)
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


def _rbox(name, x0, x1, y0, y1, z0, z1, r=0.03, lean=0.0):
    sh = soft(box(y0, z0, y1, z1), r)
    ring = body.even(np.array(sh.exterior.coords)[:-1], 48)
    rings = []
    for x in (x0, x0 + 0.015, x1 - 0.015, x1):
        k = 0.92 if x in (x0, x1) else 1.0
        cy, cz = (y0 + y1) / 2, (z0 + z1) / 2
        rr = np.column_stack([cy + (ring[:, 0] - cy) * k, cz + (ring[:, 1] - cz) * k])
        rings.append(np.column_stack([x - lean * (rr[:, 1] - z0), rr]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, "interior")




def interior(floor):
    x0 = R["screen"]["x_base"] - 0.05
    obs = [_loft("firewall", x0 - 0.010, x0 - 0.004, lambda x: box(-0.5, floor + 0.002, 0.5, 3), n=2),
           _loft("dash", x0 - 0.0125, 0.45, lambda x: box(-0.5, 0.78, 0.5, 0.78 + 0.17 * (1 - (x0 - x) / 0.5) + 0.03), n=10)]
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, 0.42, 1.2, R["belt_z"] - 0.02) if s > 0 else box(-1.2, 0.42, -0.3, R["belt_z"] - 0.02)).intersection(
            body.inside(x, 0.035 + 0.003)).difference(body.inside(x, 0.035 + 0.025))
        obs.append(_loft(f"door_card_{side}", -1.30, 0.80, clip, n=16))
        yc = 0.38 * sgn
        obs.append(_rbox(f"seat_base_{side}", -0.42, 0.10, yc - 0.25, yc + 0.25, floor + 0.002, 0.50))
        obs.append(_rbox(f"seat_back_{side}", -0.56, -0.44, yc - 0.24, yc + 0.24, 0.501, 1.12, lean=0.22))
        obs.append(_rbox(f"headrest_{side}", -0.72, -0.62, yc - 0.13, yc + 0.13, 1.121, 1.25, r=0.035, lean=0.1))
    obs.append(_rbox("bench_base", -1.20, -0.74, -0.55, 0.55, floor + 0.002, 0.52))
    obs.append(_rbox("bench_back", -1.36, -1.22, -0.55, 0.55, 0.521, 1.02, lean=0.25))
    xm = R["screen"]["x_top"] - 0.03
    roof_in = body.inside(xm, 0.035).bounds[3]
    top = roof_in - 0.07
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.12, 0.12, top - 0.055, top, r=0.02)
    stem = _rbox("stem", xm - 0.016, xm - 0.004, -0.012, 0.012, top - 0.015, roof_in - 0.0008, r=0.004)
    obs.append(blend.union_all([m, stem], "mirror_rear"))
    return obs


# ---------------------------------------------------------------- wheels
def wheels():
    """Plain five-spoke 18 inch wheels."""
    obs = []
    hw = W["width"] / 2
    yc = W["outer_y"] - hw
    tyre = soft(box(W["rim_r"], -hw, W["r"], hw), 0.035)
    tyre = tyre.union(box(W["rim_r"], -hw + 0.012, W["rim_r"] + 0.03, hw - 0.012))
    tp = body.even(np.array(tyre.exterior.coords)[:-1], 96)
    rim = [(W["rim_r"] - 0.001, -hw + 0.012), (W["rim_r"] - 0.001, hw - 0.01), (W["rim_r"] - 0.012, hw - 0.002),
           (W["rim_r"] - 0.024, hw - 0.006), (W["rim_r"] - 0.024, -hw + 0.012)]
    for x, fb in ((W["front_x"], "f"), (W["rear_x"], "r")):
        for sgn, lr in ((1, "l"), (-1, "r")):
            def place(v, s=sgn):
                v = np.asarray(v, float)
                return np.column_stack([x + v[:, 0], (yc + v[:, 1]) * s, W["r"] + v[:, 2]])
            v, f = geom.lathe(tp, 96, axis="y")
            ob = blend.make(f"wheel_tyre_{fb}{lr}", place(v), f, "tyre")
            obs.append(ob)
            v, f = geom.lathe(rim, 96, axis="y")
            parts = [blend.make("rim", place(v), f, "rim")]
            v, f = geom.disc((0, 0.07, 0), (0, 1, 0), 0.08, 0.05, n=48)
            parts.append(blend.make("hub", place(v), f, "rim"))
            for k in range(5):
                a = 2 * np.pi * k / 5
                ca, sa = np.cos(a), np.sin(a)
                pts = []
                for r, w2, h0, h1 in ((0.065, 0.03, 0.045, 0.092), (W["rim_r"] - 0.018, 0.022, 0.05, 0.10)):
                    for dw in (-w2, w2):
                        for hh in (h0, h1):
                            pts.append((r * ca - dw * sa, hh, r * sa + dw * ca))
                v = np.array(pts)
                f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
                parts.append(blend.make("spoke", place(v), f, "rim"))
            obs.append(blend.union_all(parts, f"wheel_rim_{fb}{lr}"))
    return obs


# ---------------------------------------------------------------- badge and name
BRAND = HERE.parent.parent.parent / "src/renderer/assets/brands/archer"


def _svg_shapes(name):
    """Every filled shape in a brand SVG, in drawing order: [(shapely shape, fill)]."""
    svg = re.sub(r"<(metadata|defs|mask)\b.*?</\1>", "", (BRAND / name).read_text(), flags=re.S)
    out = []
    for m in re.finditer(r'<(path|rect|polygon)\s([^>]*)/>', svg):
        a = dict(re.findall(r'([\w-]+)="([^"]*)"', m.group(2)))
        if m.group(1) == "path":
            nums = [tuple(map(float, p.split())) for p in re.findall(r"[ML]\s*([\d.]+\s+[\d.]+)", a["d"])]
            g = Polygon(nums)
        elif m.group(1) == "rect":
            x, y, w, h = (float(a[k]) for k in ("x", "y", "width", "height"))
            g = box(x, y, x + w, y + h)
        else:
            g = Polygon([tuple(map(float, p.split(","))) for p in a["points"].split()])
        out.append((g.buffer(0), a.get("fill", ""), a.get("mask")))
    return out


def _fit(shapes, ref):
    """Flip y up and scale so ref is 1 unit tall, centred."""
    b = ref.bounds
    k = 1 / (b[3] - b[1])
    return [sscale(translate(sscale(g, 1, -1, origin=(0, 0)), -(b[0] + b[2]) / 2, (b[1] + b[3]) / 2), k, k, origin=(0, 0))
            for g in shapes]


def badge_chrome():
    """The Archer badge from archer-badge-chrome.svg: the A, cut where the arrow crosses it,
    and the arrow. One chrome shape."""
    sh = _svg_shapes("archer-badge-chrome.svg")
    a = sh[0][0].difference(box(0, 131, 200, 154)).difference(Polygon([(153, 120), (194, 142.5), (153, 165)]))
    whole = unary_union([a] + [g for g, _f, _m in sh[1:]])
    return _fit([whole], whole)[0]


def badge_colour():
    """The Archer badge from archer-badge-colour.svg: [(shape, material)], the red arrow over
    the black A."""
    sh = _svg_shapes("archer-badge-colour.svg")
    red = unary_union([g for g, f, _m in sh if f.lower() == "#d4202c"])
    black = sh[0][0].difference(red.buffer(1.5))
    whole = unary_union([black, red])
    b, r = _fit([black, red], whole)
    return [(b, "badge_black"), (r, "badge_red")]


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


def badge(ref, style, size, fr, a, b, up, name, lift=0.0):
    if style == "chrome":
        return _flat(ref, badge_chrome(), size, fr, a, b, up, name, 0.0012 + lift, 0.0032 + lift, "chrome", 0.003)
    obs = []
    for i, (g, m) in enumerate(badge_colour()):
        obs += _flat(ref, g, size, fr, a, b, up, f"{name}_{i}", 0.0012 + lift, 0.0032 + lift, m, 0.003)
    return obs


def options(ref):
    """Badge and name options, named opt_<group>_<n>_<i>. Each lies flat on its panel.
    Odd numbers are the chrome badge, even the colour badge, at the same spot."""
    sp = R["spots"]
    nm = name_shape()
    up, fwd = (0, 0, 1), (-1, 0, 0)
    out = []

    def add(group, n, objs):
        for i, ob in enumerate(objs):
            ob.name = f"opt_{group}_{n}_{i}"
            out.append(ob)
    gz = sp["grille"][1]
    for k, style in ((0, "chrome"), (1, "colour")):
        # front: in the middle of the grille (the reference), or on the bonnet's front edge
        add("frontbadge", 1 + k, badge(ref, style, 0.10, front_fr(), 0, gz, up, "fb"))
        add("frontbadge", 3 + k, badge(ref, style, 0.045, top_fr(), sp["bonnet_x"], 0, fwd, "fb"))
        # rear: middle of the boot lid (the reference), or high on the lid under the lip
        add("rearbadge", 1 + k, badge(ref, style, 0.07, rear_fr(), 0, sp["lid_z"], up, "rb"))
        add("rearbadge", 3 + k, badge(ref, style, 0.05, rear_fr(), 0, sp["spoiler_z"] - 0.05, up, "rb"))
    cap = 0.026
    lid_y = 0.32
    add("name", 1, _flat(ref, nm, cap, rear_fr(), -lid_y, sp["lid_z"] - 0.01, up, "nm", 0.0012, 0.0025, "chrome"))
    add("name", 2, _flat(ref, nm, cap, rear_fr(), lid_y, sp["lid_z"] - 0.01, up, "nm", 0.0012, 0.0025, "chrome"))
    q = []
    for sgn in (1, -1):
        q += _flat(ref, nm, cap, side_fr(sgn), FRONT_DOOR_X + 0.16, 0.64, up, "nm", 0.0012, 0.0025, "chrome")
    add("name", 3, q)
    return out
