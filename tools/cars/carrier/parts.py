"""The Carrier's parts: lamps, grille, strip, intake, glass, trim, mirrors, interior, wheels,
badge and name. Every shape and size here is the Carrier's own, from rows.json and Ivan's
reference, kept plain and smooth. Runs inside Blender's Python module, called by export.py."""
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
    "body": ((0.98, 0.58, 0.04), 0.75, 1.0, None),
    "trim": ((0.025, 0.025, 0.03), 0.6, 1.0, None),
    "strip": ((0.03, 0.03, 0.032), 0.35, 1.0, None),
    "grille": ((0.035, 0.035, 0.04), 0.3, 1.0, None),
    "glass": ((0.07, 0.09, 0.11), 0.92, 0.45, None),
    "head_glass": ((0.92, 0.94, 0.96), 0.95, 0.22, None),
    "tail_glass": ((0.55, 0.03, 0.04), 0.92, 0.55, None),
    "housing": ((0.05, 0.05, 0.055), 0.5, 1.0, None),
    "reflector": ((0.78, 0.79, 0.81), 0.97, 1.0, None),
    "lamp_head": ((0.95, 0.95, 0.92), 0.6, 1.0, (1.0, 0.97, 0.9)),
    "lamp_tail": ((0.5, 0.03, 0.04), 0.5, 1.0, (1.0, 0.06, 0.04)),
    "lamp_brake": ((0.6, 0.04, 0.05), 0.5, 1.0, (1.0, 0.04, 0.03)),
    "chrome": ((0.86, 0.87, 0.89), 0.98, 1.0, None),
    "badge_gold": ((0.91, 0.70, 0.23), 0.8, 1.0, None),
    "badge_green": ((0.07, 0.27, 0.23), 0.6, 1.0, None),
    "badge_red": ((0.84, 0.15, 0.18), 0.6, 1.0, None),
    "badge_sage": ((0.66, 0.76, 0.71), 0.6, 1.0, None),
    "badge_cream": ((0.96, 0.94, 0.89), 0.6, 1.0, None),
    "interior": ((0.09, 0.09, 0.1), 0.15, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.6, 0.62, 0.64), 0.9, 1.0, None),
    "steel": ((0.78, 0.79, 0.8), 0.7, 1.0, None),
}
LAMPS = {"Headlights": ["lamp_head"], "Tail lights": ["lamp_tail"], "Brake lights": ["lamp_brake"]}
GROUPS = {"frontbadge": "Front badge", "rearbadge": "Rear badge", "name": "Name", "wheels": "Wheels"}


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


def outline(tris, a, b, r=0.008):
    """One plain outline of triangles seen along the third axis."""
    u = unary_union([Polygon(t[:, [a, b]]).buffer(1e-4) for t in np.asarray(tris)])
    u = u.buffer(0.006, join_style=1).buffer(-0.006, join_style=1)
    if u.geom_type != "Polygon":
        u = max(u.geoms, key=lambda g: g.area)
    return soft(Polygon(u.exterior), r).simplify(0.002)


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

    def outline(self, tris, r=0.008):
        u = unary_union([Polygon([self.to_uv(p) for p in t]).buffer(1e-4) for t in np.asarray(tris)])
        u = u.buffer(0.008, join_style=1).buffer(-0.008, join_style=1)
        if u.geom_type != "Polygon":
            u = max(u.geoms, key=lambda g: g.area)
        return soft(Polygon(u.exterior), r).simplify(0.002)


TAIL_FACE_X = float(np.array(R["tail"]).reshape(-1, 3)[:, 0].min())
TAIL_WRAP = dict(end="rear", xc=TAIL_FACE_X + 0.15, yc=0.64, rc=0.15)
NOSE_WRAP = dict(end="front", xc=NOSE - 0.18, yc=0.60, rc=0.18)


# ---------------------------------------------------------------- lamps
HEAD_BOTTOM = float(np.array(R["head"]).reshape(-1, 3)[:, 2].min())


def head_shape():
    """The left head lamp seen from ahead (y, z): the reference's plain slanted rectangle."""
    return outline(R["head"], 1, 2, 0.006)


def headlamps(ref, cutters, out, sgn):
    side = "l" if sgn > 0 else "r"
    fr = front_fr()
    shp = head_shape()
    shp = shp if sgn > 0 else flip(shp)
    cutters += blend.lay(ref, f"cut_head_{side}", shp, fr, -0.016, 0.006, "housing", 0.01, along_ray=True)
    b = shp.bounds
    inner = shp.buffer(-0.012, join_style=1)
    # our own lamp: one wide reflector with a lamp across its middle
    mid = (b[1] + b[3]) / 2
    lamp = inner.intersection(box(-5, mid - 0.032, 5, mid + 0.032))
    out += blend.lay(ref, f"head_reflector_{side}", inner, fr, -0.0125, -0.0105, "reflector", 0.005, along_ray=True)
    out += blend.lay(ref, f"lamp_head_{side}", soft(lamp.buffer(-0.01, join_style=1), 0.02), fr, -0.0102, -0.0075,
                     "lamp_head", 0.004, along_ray=True)
    out += blend.lay(ref, f"head_glass_{side}", shp.buffer(-0.003), fr, -0.0045, -0.0015, "head_glass", 0.01, along_ray=True)


def taillamps(ref, cutters, out, sgn):
    """The whole cluster outline from the reference, round the corner, all red: race cars carry
    no indicators or reversing lamps. Tail lamp above, brake lamp below."""
    side = "l" if sgn > 0 else "r"
    wr = Wrap(sgn=sgn, **TAIL_WRAP)
    shp = wr.outline(R["tail"])
    cutters += blend.lay(ref, f"cut_tail_{side}", shp, wr, -0.016, 0.006, "housing", 0.01, along_ray=True)
    core = shp.buffer(-0.01, join_style=1)
    b = shp.bounds
    zsplit = b[1] + 0.5 * (b[3] - b[1])
    out += blend.lay(ref, f"lamp_tail_{side}", core.intersection(box(-5, zsplit + 0.004, 5, 5)), wr,
                     -0.011, -0.008, "lamp_tail", 0.005, along_ray=True)
    out += blend.lay(ref, f"lamp_brake_{side}", core.intersection(box(-5, -5, 5, zsplit - 0.004)), wr,
                     -0.011, -0.008, "lamp_brake", 0.005, along_ray=True)
    out += blend.lay(ref, f"tail_glass_{side}", shp.buffer(-0.003), wr, -0.0045, -0.0015, "tail_glass", 0.01, along_ray=True)


# ---------------------------------------------------------------- front
SLOTS = 3                 # grille slots each side, as the reference has
SLOT_H = 0.011            # slot height
SLOT_GAP = 0.013          # body colour between slots
STRIP_GAP = 0.0015        # half the gap where the two halves of the strip meet at the centre


def grille_slots():
    """Three black slots each side in the lower half of the grille panel, stopping short of the
    badge in the middle: (y, z) shapes seen from ahead."""
    g = R["grille"]
    z0 = max(g["z0"], R["strip"]["z1"]) + 0.014
    out = []
    for i in range(SLOTS):
        z = z0 + i * (SLOT_H + SLOT_GAP)
        s = soft(box(0.055, z, g["y"] - 0.012, z + SLOT_H), 0.004)
        out += [s, flip(s)]
    return unary_union(out)


def strip_top():
    return min(R["strip"]["z1"], HEAD_BOTTOM - 0.004)


def front_parts(ref, cutters, out):
    fr = front_fr()
    slots = grille_slots()
    out += blend.lay(ref, "grille_slot", slots, fr, 0.0006, 0.0022, "grille", 0.003)     # thin black slots on the panel
    it = R["intake"]
    intake = soft(box(-it["y"], it["z0"], it["y"], it["z1"]), 0.01)
    cutters += blend.lay(ref, "cut_intake", intake, fr, -0.03, 0.006, "grille", 0.008, along_ray=True)
    out += blend.lay(ref, "intake_back", intake.buffer(-0.004), fr, -0.0255, -0.022, "grille", 0.008, along_ray=True)
    # the black rubbing strip across the nose and round both corners to the front arches
    st = R["strip"]
    z0, z1 = st["z0"], strip_top()
    for sgn, side in ((1, "l"), (-1, "r")):
        wr = Wrap(sgn=sgn, **NOSE_WRAP)
        u_end = wr.to_uv((st["x_back"], 0.8, z0))[0]
        band = soft(box(STRIP_GAP, z0, u_end, z1), 0.006)
        out += blend.lay(ref, f"strip_{side}", band, wr, -0.001, 0.005, "strip", 0.008, along_ray=True)


# ---------------------------------------------------------------- glass and trim
MIRROR_X0, MIRROR_X1 = R["mirror"]["x_back"], R["mirror"]["x_front"]
MIRROR_D = 0.07      # housing depth front to back


def belt(x):
    """Height of the belt line (the window opening's straight bottom edge) at x."""
    k = R["belt"]
    return k[0] * x + k[1]


MIRROR_FOOT = box(MIRROR_X0 - 0.01, belt(MIRROR_X0) - 0.05, MIRROR_X0 + MIRROR_D + 0.01, R["mirror"]["z0"] + 0.06)


def dlo_shapes():
    """The side window opening, its glass (split by the black B-pillar and the rear door's
    divider) and the black band round it."""
    dlo = soft(poly(R["dlo"]), 0.012)
    glass = dlo.buffer(-0.018, join_style=1)
    b0, b1 = R["b_pillar"]
    d0, d1 = R["divider"]
    glass = glass.difference(box(b0 - 0.01, -1, b1 + 0.01, 3)).difference(box(d0 + 0.01, -1, d1 - 0.01, 3))
    glass = glass.difference(box(MIRROR_X0 + 0.02, -1, 5, 5))       # the corner ahead of the mirror is black
    glass = unary_union([soft(p, 0.008) for p in getattr(glass, "geoms", [glass])])
    return dlo, glass, dlo.difference(glass.buffer(-0.005))      # the band covers the cut edge


GLASS_BAND = 0.016    # black band round the windscreen and the rear window


def plain_glass(pts, lo, hi, r=0.04):
    """A glass outline rebuilt from three plain fits to the measured one, so its edges carry
    no waves: the low edge and the high edge as even curves across the car, and the sides as
    straight lines. pts are (along, across); lo and hi pick the low and high edge points by
    their along value."""
    p = np.array(Polygon(pts).buffer(0).exterior.segmentize(0.01).coords)[:-1]     # even points along every edge
    a, c = p[:, 0], np.abs(p[:, 1])
    w = c.max()
    sym = lambda m: (np.concatenate([p[m, 1], -p[m, 1]]), np.concatenate([a[m], a[m]]))
    k_lo = np.polyfit(*sym(lo(a) & (c < 0.8 * w)), 2)
    k_hi = np.polyfit(*sym(hi(a) & (c < 0.8 * w)), 2)
    side = (c > 0.85 * w) & ~lo(a) & ~hi(a)
    k_s = np.polyfit(a[side], c[side], 1) if side.sum() >= 2 else np.array([0.0, w])
    ys = np.linspace(-1.2 * w, 1.2 * w, 61)
    band = Polygon(np.vstack([np.column_stack([np.polyval(k_lo, ys), ys]),
                              np.column_stack([np.polyval(k_hi, ys), ys])[::-1]]))
    us = np.linspace(a.min() - 0.1, a.max() + 0.1, 41)
    sides = Polygon(np.vstack([np.column_stack([us, np.polyval(k_s, us)]),
                               np.column_stack([us, -np.polyval(k_s, us)])[::-1]]))
    return soft(band.intersection(sides), r)


def screen_shapes():
    s = R["screen"]
    span = s["x_base"] - s["x_top"]
    outline_ = plain_glass(s["outline"], lambda a: a > s["x_base"] - 0.2 * span, lambda a: a < s["x_top"] + 0.2 * span)
    glass = outline_.buffer(-GLASS_BAND, join_style=1)
    return outline_, glass, outline_.difference(glass.buffer(-0.005))


def rear_glass_fr():
    """A frame square to the rear window's slope, so its band lands as wide as it is drawn,
    and a map from the rear view (y, z) into it."""
    p = np.array(R["rear_glass_side"])
    k = np.polyfit(p[:, 0], p[:, 1], 1)
    lo = np.array([(p[:, 1].min() - k[1]) / k[0], p[:, 1].min()])
    t = np.array([1.0, 0.0, k[0]]); t /= np.linalg.norm(t)
    d = np.array([t[2], 0.0, -t[0]])                 # into the car, square to the glass
    fr = blend.frame((lo[0], 0, lo[1]), (0, 1, 0), t, d)
    return fr, lambda g: sscale(translate(g, 0, -lo[1]), 1, 1 / t[2], origin=(0, 0))


def rear_shapes():
    p = np.array(R["rear_glass"])[:, ::-1]          # (z, y)
    z0, z1 = p[:, 0].min(), p[:, 0].max()
    span = z1 - z0
    g = plain_glass(p, lambda a: a < z0 + 0.2 * span, lambda a: a > z1 - 0.2 * span, r=0.035)
    rg = Polygon(np.array(g.exterior.coords)[:, ::-1])  # back to (y, z)
    glass = rg.buffer(-GLASS_BAND, join_style=1)
    return rg, glass, rg.difference(glass.buffer(-0.005))


def glass_parts(ref, cutters, out):
    _d, g, t = dlo_shapes()
    for sgn, side in ((1, "l"), (-1, "r")):
        fr = side_fr(sgn)
        cutters += blend.lay(ref, f"cut_side_{side}", g.buffer(-0.003), fr, -0.05, 0.006, "trim", along_ray=True)
        out += blend.lay(ref, f"side_glass_{side}", g.buffer(-0.006), fr, -0.008, -0.004, "glass", 0.02, along_ray=True)
        out += blend.lay(ref, f"window_trim_{side}", t.difference(MIRROR_FOOT), fr, 0.0015, 0.0035, "trim", 0.01)
    _o, g, t = screen_shapes()
    cutters += blend.lay(ref, "cut_screen", g.buffer(-0.003), top_fr(), -0.05, 0.006, "trim", along_ray=True)
    out += blend.lay(ref, "screen_glass", g.buffer(-0.006), top_fr(), -0.008, -0.004, "glass", 0.02, along_ray=True)
    out += blend.lay(ref, "screen_trim", t, top_fr(), 0.0015, 0.0035, "trim", 0.01)
    fr, to_fr = rear_glass_fr()
    _o, g, t = (to_fr(q) for q in rear_shapes())
    cutters += blend.lay(ref, "cut_rear", g.buffer(-0.003), fr, -0.05, 0.004, "trim", along_ray=True)
    out += blend.lay(ref, "rear_glass", g.buffer(-0.006), fr, -0.008, -0.004, "glass", 0.02, along_ray=True)
    out += blend.lay(ref, "rear_trim", t, fr, 0.0015, 0.0035, "trim", 0.01)


# ---------------------------------------------------------------- shut lines
DOOR_BOTTOM = 0.34       # the doors' bottom edge, above the sill and clear of the cabin floor


def _edge_x(g, z, side):
    """The front (side 1) or back (side -1) of shape g at height z."""
    from shapely.geometry import LineString
    cut = g.intersection(LineString([(-5, z), (5, z)]))
    b = cut.bounds
    return b[2] if side > 0 else b[0]


def _into_glass(p, d=0.025):
    """The point of the window opening nearest p, moved d into it, so a shut line ends on the
    black trim."""
    dlo = poly(R["dlo"])
    q = np.array(dlo.exterior.interpolate(dlo.exterior.project(Point(p))).coords[0])
    c = np.array(dlo.centroid.coords[0])
    return (q + (c - q) / np.linalg.norm(c - q) * d).tolist()


def door_paths():
    """Front door: its front edge from the mirror foot down the wing's back and along the
    bottom. Rear door: its back edge round the rear arch, along the bottom, and its front edge
    up the middle of the B-pillar black, so each door shows the same width of black."""
    fd = poly(R["front_door"])
    rd = poly(R["rear_door"])
    bc = float(np.mean(R["b_pillar"]))
    bz = R["belt_z"]
    zs = np.linspace(DOOR_BOTTOM + 0.04, bz - 0.04, 6)
    xf = [_edge_x(fd, z, 1) for z in zs]
    x0 = MIRROR_X0 + MIRROR_D + 0.012
    front = [(x0, belt(x0) + 0.04), (x0 + 0.004, belt(x0) - 0.02)]
    front += [(x, z) for x, z in zip(xf[::-1], zs[::-1])]
    front += [(xf[0] - 0.012, DOOR_BOTTOM + 0.012), (xf[0] - 0.04, DOOR_BOTTOM)]
    front = geom.catmull_open(front, 6).tolist()
    front += [(x, DOOR_BOTTOM) for x in np.linspace(xf[0] - 0.08, bc - 0.004, 12)]     # straight along the bottom
    # the rear door's back edge as measured, below the belt
    pts = np.array(R["rear_door"])
    back = pts[(pts[:, 0] < -0.75) & (pts[:, 1] > DOOR_BOTTOM + 0.02) & (pts[:, 1] < bz - 0.02)]
    back = back[np.argsort(-back[:, 1])]
    rear = [_into_glass((back[0][0], bz + 0.02))] + back.tolist()
    rear += [(back[-1][0] + 0.03, DOOR_BOTTOM), (bc - 0.004, DOOR_BOTTOM), (bc, DOOR_BOTTOM + 0.03), (bc, bz + 0.03)]
    return rear, front


# ---------------------------------------------------------------- driver's door
DOOR_DEPTH = 0.05     # below the cabin floor the door takes the sill this deep
DOOR_WALL = 0.09      # above it, the whole wall: the cabin is hollow behind it
DOOR_GAP = 0.0015     # half the gap round the door, inside the shut line's groove
DOOR_OPEN = 65.0      # degrees the door swings out


def door_outline():
    """The left front door seen from the side: inside its front shut line and the B-pillar's
    middle, closed across the window, and the window frame round its glass back to the
    B-pillar's middle."""
    _rear, front = door_paths()
    bc = float(np.mean(R["b_pillar"]))
    lower = Polygon(front + [(bc, DOOR_BOTTOM), (bc, R["belt_z"] + 0.03)]).buffer(0)
    dlo, _g, _t = dlo_shapes()
    return unary_union([lower, dlo.intersection(box(bc, -1, 5, 5))])


def mirror_footprint():
    """The mirror on the door seen from the side, with room: taken only outside the skin."""
    m = R["mirror"]
    return box(m["x_back"] - 0.05, R["belt_z"] - 0.06, m["x_front"] + 0.06, m["z1"] + 0.05)


def door_hinge(sgn=1):
    """The hinge axis up the door's front edge, outside the skin, and the open angle. Turning
    about an axis pointing up, a negative angle swings the left door's back edge out and a
    positive one the right's. sgn picks the side: 1 left, -1 right."""
    o = door_outline()
    _x0, z0, x1, _z1 = o.bounds
    xf = x1 + 0.004
    y = max(body.y_at(xf - 0.01, z) for z in np.linspace(z0 + 0.02, R["belt_z"], 20)) + 0.003
    return (xf, y * sgn, z0), (xf, y * sgn, R["belt_z"]), -DOOR_OPEN * sgn


def door(ref, shell, obs, floor, sgn=1):
    """Cut the left (sgn 1) or right (sgn -1) front door free of the body and its window trim;
    its glass, card and mirror glass go with it. Returns the door's pieces."""
    side = "l" if sgn > 0 else "r"
    o = door_outline()
    regions = [(o.intersection(box(-5, -1, 5, floor + 0.02)), DOOR_DEPTH),
               (o.intersection(box(-5, floor + 0.02, 5, 5)), DOOR_WALL), (mirror_footprint(), -0.004)]
    regions = [(g, d) for g, d in regions if g.area > 1e-4]
    trim = [ob for ob in obs if ob.name == f"window_trim_{side}"]
    roles = {f"side_glass_{side}": "glass", f"side_glass_{side}_1": "glass", f"side_glass_{side}_2": "glass",
             f"mirror_glass_{side}": "mirror", f"door_card_{side}": "card"}
    return blend.free_door(ref, side_fr(sgn), regions, DOOR_GAP, shell, trim, roles, o, name=f"door_{side}")


def bonnet_path():
    """The bonnet's shut line in plan, open at the back: from the windscreen's lower corner,
    forward along the wing top, round over the head lamp's outer corner, across above the
    lamps and grille, and back down the other side. The bonnet reaches the windscreen."""
    b = np.array(R["bonnet"])
    _o, g, _t = screen_shapes()
    so = np.array(g.exterior.coords)
    corner = so[np.argmax(so[:, 0] + 0.6 * np.abs(so[:, 1]))]
    x0, y0 = float(corner[0]) + 0.008, abs(float(corner[1])) + 0.008     # on the black band, ahead of the glass
    xf = float(b[:, 0].max())
    yw = float(np.percentile(np.abs(b[:, 1]), 97))
    half = [(x0, y0), (x0 + 0.12, yw - 0.005), (x0 + 0.4, yw), ((x0 + xf) / 2 + 0.2, yw), (xf - 0.16, yw - 0.004),
            (xf - 0.05, yw - 0.03), (xf - 0.012, yw - 0.12), (xf, yw - 0.3), (xf, 0.0)]
    left = geom.catmull_open(half, 8)
    right = left[-2::-1] * np.array([1, -1])
    return np.vstack([left, right])


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


LID_TOP = float(np.array(R["lid_rear"])[:, 1].max())
LID_BOTTOM = float(np.array(R["tail"]).reshape(-1, 3)[:, 2].max()) + 0.012     # just above the lamps


def _lid_wrap(rc=0.06):
    """The corner the lid's frame bends round, set from the skin itself: the deck's height and
    the back face's place at the lid's bottom edge."""
    zd = body.z_top(R["spots"]["lid_x"] + 0.25)
    xs = np.arange(TAIL, TAIL + 0.4, 0.002)
    xf = float(next(x for x in xs if body.z_top(x) >= LID_BOTTOM + 0.03))
    return dict(xc=xf + rc, zc=zd - rc, rc=rc)


LID_WRAP = _lid_wrap()


def lid_loop():
    """Boot lid as one loop in the LidWrap frame: across the deck at the rear glass foot, back
    along both deck sides at one width, over the corner, then down the face to just above the
    lamps and across."""
    fr = LidWrap(**LID_WRAP)
    ys = float(np.percentile(np.abs(np.array(R["lid_top"])[:, 1]), 90)) - 0.01
    x_glass = float(np.min(np.array(R["rear_glass_side"])[:, 0]))
    shape = soft(box(fr.deck(x_glass - 0.025), -ys, fr.tail(LID_BOTTOM), ys), 0.03)
    return fr, np.array(shape.exterior.coords)[:-1]


def grooves(ref):
    """Shut lines as V grooves to cut: doors, bonnet, boot lid."""
    obs = []
    rear, front = door_paths()
    for sgn, side in ((1, "l"), (-1, "r")):
        obs.append(blend.groove(ref, f"g_rdoor_{side}", rear, side_fr(sgn)))
        obs.append(blend.groove(ref, f"g_fdoor_{side}", front, side_fr(sgn)))
    obs.append(blend.groove(ref, "g_bonnet", bonnet_path(), top_fr()))
    fr, lid = lid_loop()
    obs.append(blend.groove(ref, "g_lid", lid, fr, closed=True))
    return obs


# ---------------------------------------------------------------- mirrors
MIRROR_H = 0.085     # housing height
MIRROR_W = 0.15      # housing width, out from the arm
MIRROR_R = 0.022     # corner rounding
MIRROR_SWEEP = 0.15  # radians the housing turns back from square to the car


def _rounded_bar(name, y0, y1, z0, z1, x0, x1, r, sgn, mat="trim"):
    face = soft(box(y0, z0, y1, z1), r)
    ring = body.even(np.array(face.exterior.coords)[:-1], 96)
    rings = [np.column_stack([np.full(96, x), ring[:, 0] * sgn, ring[:, 1]]) for x in (x0, x1)]
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def _mirror_root():
    m = R["mirror"]
    return body.y_at(m["x_back"] + 0.04, R["belt_z"] - 0.03) - 0.02      # sunk into the door


def _housing():
    m = R["mirror"]
    zc = (m["z0"] + m["z1"]) / 2 + 0.01
    return m["y_out"] - MIRROR_W, m["y_out"], zc - MIRROR_H / 2, zc + MIRROR_H / 2


def _sweep(ob, sgn):
    from mathutils import Matrix
    m = R["mirror"]
    p = Matrix.Translation((m["x_back"] + MIRROR_D / 2, sgn * _mirror_root(), 0))
    ob.data.transform(p @ Matrix.Rotation(sgn * MIRROR_SWEEP, 4, "Z") @ p.inverted())
    return ob


def mirrors():
    """A black rounded housing on a short arm from the door's front top corner."""
    m = R["mirror"]
    y0, y1, z0, z1 = _housing()
    xb = m["x_back"]
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        house = _rounded_bar(f"mirror_{side}", y0, y1, z0, z1, xb, xb + MIRROR_D, MIRROR_R, sgn)
        arm = _rounded_bar(f"mirror_arm_{side}", _mirror_root(), y0 + 0.03, R["belt_z"] - 0.04, z0 + 0.03,
                           xb + 0.01, xb + MIRROR_D - 0.01, 0.01, sgn)
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
                                       m["x_back"] - 0.03, m["x_back"] + 0.006, MIRROR_R - 0.01, sgn), sgn))
        glass.append(_sweep(_rounded_bar(f"mirror_glass_{side}", y0 + 0.0115, y1 - 0.0115, z0 + 0.0115, z1 - 0.0115,
                                         m["x_back"] + 0.002, m["x_back"] + 0.005, MIRROR_R - 0.0115, sgn, "mirror_glass"), sgn))
    return cut, glass


def arches():
    """Arch cutters and the wheel tubs kept out of the cabin."""
    cut, tubs = [], []
    for x in (W["front_x"], W["rear_x"]):
        for sgn in (1, -1):
            v, f = geom.disc((x, 0.80 * sgn, W["r"]), (0, 1, 0), W["arch_r"], 0.56, n=96)
            cut.append(blend.make("arch", v, f, "interior"))
            v, f = geom.disc((x, 0.82 * sgn, W["r"]), (0, 1, 0), W["arch_r"] + 0.035, 0.70, n=96)
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


def _torus(name, centre, axis, rr, r, n=48, m=12):
    """A ring of tube radius r round a circle of radius rr, square to axis."""
    a = np.asarray(axis, float); a /= np.linalg.norm(a)
    u = np.cross(a, [0, 0, 1.0]); u /= np.linalg.norm(u)
    v = np.cross(a, u)
    rings = []
    for i in range(n):
        t = 2 * np.pi * i / n
        d = np.cos(t) * u + np.sin(t) * v
        c = np.asarray(centre) + d * rr
        rings.append([c + r * (np.cos(s) * d + np.sin(s) * a) for s in np.linspace(0, 2 * np.pi, m, endpoint=False)])
    verts = np.concatenate(rings)
    faces = []
    for i in range(n):
        for j in range(m):
            p, q = i * m, ((i + 1) % n) * m
            faces.append((p + j, p + (j + 1) % m, q + (j + 1) % m, q + j))
    return blend.make(name, verts, faces, "interior")


def _card_floor(x):
    """The door card's bottom edge: clear of the wheel tubs."""
    best = 0.40
    for wx in (W["front_x"], W["rear_x"]):
        dx = abs(x - wx)
        reach = W["arch_r"] + 0.09
        best = max(best, W["r"] + np.sqrt(max(reach ** 2 - dx ** 2, 0.0)))
    return best


SEAT_Y = 0.36        # seat centres off the middle; the driver sits on the left


def interior(floor):
    x0 = R["screen"]["x_base"] - 0.05
    bz = R["belt_z"]
    obs = [_loft("firewall", x0 - 0.010, x0 - 0.004, lambda x: box(-0.5, floor + 0.002, 0.5, 3), n=2),
           _loft("dash", x0 - 0.0125, 0.40, lambda x: box(-0.5, bz - 0.06, 0.5, bz - 0.06 + 0.15 * (1 - (x0 - x) / 0.4) + 0.03), n=10)]
    bc = float(np.mean(R["b_pillar"]))
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, _card_floor(x), 1.2, bz - 0.02) if s > 0 else box(-1.2, _card_floor(x), -0.3, bz - 0.02)).intersection(
            body.inside(x, 0.035 + 0.003)).difference(body.inside(x, 0.035 + 0.025))
        obs.append(_loft(f"door_card_{side}", bc + 0.006, 0.40, clip, n=10))
        obs.append(_loft(f"door_card_{side}_rear", -0.95, bc - 0.006, clip, n=8))
        yc = SEAT_Y * sgn
        obs.append(_rbox(f"seat_base_{side}", -0.46, 0.04, yc - 0.23, yc + 0.23, floor + 0.002, 0.48))
        obs.append(_rbox(f"seat_back_{side}", -0.60, -0.48, yc - 0.22, yc + 0.22, 0.481, 1.03, lean=0.2))
        obs.append(_rbox(f"headrest_{side}", -0.74, -0.64, yc - 0.12, yc + 0.12, 1.031, 1.15, r=0.035, lean=0.1))
    obs.append(_rbox("bench_base", -1.16, -0.78, -0.48, 0.48, floor + 0.004, 0.50))
    obs.append(_rbox("bench_back", -1.32, -1.14, -0.48, 0.48, 0.501, 0.98, lean=0.25))
    # the driver's wheel, on the left, on a short column from the dash
    wc = np.array([0.28, SEAT_Y, bz + 0.07])
    ax = np.array([-1.0, 0.0, 0.45])
    ax /= np.linalg.norm(ax)
    obs.append(_torus("steering_wheel", wc, ax, 0.18, 0.014))
    col = _rbox("steering_column", 0.29, 0.50, SEAT_Y - 0.025, SEAT_Y + 0.025, bz - 0.0, bz + 0.05, r=0.02)
    obs.append(col)
    xm = R["screen"]["x_top"] - 0.03
    roof_in = body.inside(xm, 0.035).bounds[3]
    top = roof_in - 0.06
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.11, 0.11, top - 0.05, top, r=0.02)
    stem = _rbox("stem", xm - 0.016, xm - 0.004, -0.012, 0.012, top - 0.015, roof_in - 0.0008, r=0.004)
    obs.append(blend.union_all([m, stem], "mirror_rear"))
    return obs


# ---------------------------------------------------------------- wheels
CORNERS = [(W["front_x"], "f"), (W["rear_x"], "r")]


def _place(v, x, yc, s):
    v = np.asarray(v, float)
    return np.column_stack([x + v[:, 0], (yc + v[:, 1]) * s, W["r"] + v[:, 2]])


def tyres():
    obs = []
    hw = W["width"] / 2
    yc = W["outer_y"] - hw
    tyre = soft(box(W["rim_r"], -hw, W["r"], hw), 0.03)
    tyre = tyre.union(box(W["rim_r"], -hw + 0.012, W["rim_r"] + 0.03, hw - 0.012))
    tp = body.even(np.array(tyre.exterior.coords)[:-1], 96)
    for x, fb in CORNERS:
        for sgn, lr in ((1, "l"), (-1, "r")):
            v, f = geom.lathe(tp, 96, axis="y")
            obs.append(blend.make(f"wheel_tyre_{fb}{lr}", _place(v, x, yc, sgn), f, "tyre"))
    return obs


def _barrel():
    hw = W["width"] / 2
    return [(W["rim_r"] - 0.001, -hw + 0.012), (W["rim_r"] - 0.001, hw - 0.01), (W["rim_r"] - 0.012, hw - 0.002),
            (W["rim_r"] - 0.022, hw - 0.006), (W["rim_r"] - 0.022, -hw + 0.012)]


def steel_rim(x, yc, sgn, name):
    """The reference's 13 inch steel wheel: a pressed dish with a ring of round holes and a
    small centre cap."""
    hw = W["width"] / 2
    rr = W["rim_r"]
    parts = [blend.make("rim", _place(geom.lathe(_barrel(), 96, axis="y")[0], x, yc, sgn), geom.lathe(_barrel(), 96, axis="y")[1], "steel")]
    dish = [(rr - 0.021, hw - 0.03), (0.105, hw - 0.022), (0.075, hw - 0.012), (0.045, hw - 0.012), (0.0, hw - 0.014),
            (0.0, hw - 0.03), (0.045, hw - 0.028), (0.075, hw - 0.028), (0.105, hw - 0.038), (rr - 0.021, hw - 0.046)]
    v, f = geom.lathe(dish, 96, axis="y")
    parts.append(blend.make("dish", _place(v, x, yc, sgn), f, "steel"))
    v, f = geom.disc((0, hw - 0.009, 0), (0, 1, 0), 0.03, 0.008, n=40)
    parts.append(blend.make("cap", _place(v, x, yc, sgn), f, "chrome"))
    ob = blend.union_all(parts, name)
    holes = []
    for k in range(8):
        a = 2 * np.pi * (k + 0.5) / 8
        c = (0.128 * np.cos(a), hw - 0.03, 0.128 * np.sin(a))
        v, f = geom.disc(c, (0, 1, 0), 0.0135, 0.05, n=24)
        holes.append(blend.make("hole", _place(v, x, yc, sgn), f, "trim"))
    for h in holes:
        blend.boolean(ob, h, "DIFFERENCE")
    return ob


def alloy_rim(x, yc, sgn, name):
    """A plain alloy: eight slim spokes to a small hub."""
    hw = W["width"] / 2
    parts = [blend.make("rim", _place(geom.lathe(_barrel(), 96, axis="y")[0], x, yc, sgn), geom.lathe(_barrel(), 96, axis="y")[1], "rim")]
    v, f = geom.disc((0, hw - 0.02, 0), (0, 1, 0), 0.05, 0.03, n=48)
    parts.append(blend.make("hub", _place(v, x, yc, sgn), f, "rim"))
    for k in range(8):
        a = 2 * np.pi * k / 8
        ca, sa = np.cos(a), np.sin(a)
        pts = []
        for r, w2, h0, h1 in ((0.04, 0.012, hw - 0.034, hw - 0.008), (W["rim_r"] - 0.016, 0.009, hw - 0.04, hw - 0.012)):
            for dw in (-w2, w2):
                for hh in (h0, h1):
                    pts.append((r * ca - dw * sa, hh, r * sa + dw * ca))
        f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        parts.append(blend.make("spoke", _place(pts, x, yc, sgn), f, "rim"))
    return blend.union_all(parts, name)


def wheel_options():
    """Rims as options, named opt_wheels_<n>_<corner>: 1 the reference's steel wheel, 2 an alloy."""
    obs = []
    hw = W["width"] / 2
    yc = W["outer_y"] - hw
    for n, fn in ((1, steel_rim), (2, alloy_rim)):
        for x, fb in CORNERS:
            for sgn, lr in ((1, "l"), (-1, "r")):
                obs.append(fn(x, yc, sgn, f"opt_wheels_{n}_{fb}{lr}"))
    return obs


# ---------------------------------------------------------------- badge and name
BRAND = HERE.parent.parent.parent / "src/renderer/assets/brands/nusa"


def _path(d, n=12):
    """A closed SVG path of M, L, C and Z commands as one or more shapely polygons."""
    toks = re.findall(r"[MLCZ]|-?[\d.]+", d)
    loops, cur, i, cmd = [], [], 0, None
    while i < len(toks):
        t = toks[i]
        if t in "MLCZ":
            cmd = t; i += 1
            if t == "Z":
                loops.append(cur); cur = []
            continue
        if cmd == "M":
            cur = [(float(toks[i]), float(toks[i + 1]))]; i += 2; cmd = "L"
        elif cmd == "L":
            cur.append((float(toks[i]), float(toks[i + 1]))); i += 2
        elif cmd == "C":
            p0 = np.array(cur[-1])
            p1, p2, p3 = (np.array([float(toks[i + k]), float(toks[i + k + 1])]) for k in (0, 2, 4))
            for s in np.linspace(0, 1, n + 1)[1:]:
                cur.append(tuple((1 - s) ** 3 * p0 + 3 * (1 - s) ** 2 * s * p1 + 3 * (1 - s) * s * s * p2 + s ** 3 * p3))
            i += 6
    if cur:
        loops.append(cur)
    return unary_union([Polygon(lp).buffer(0) for lp in loops if len(lp) >= 3])


def _badge_parts():
    """The Nusa badge's shapes from nusa-badge-colour.svg, in drawing order: the gold drop, the
    green field, the red sun, the sage back peaks and the cream front peak."""
    svg = (BRAND / "nusa-badge-colour.svg").read_text()
    paths = re.findall(r'<path d="([^"]+)" fill="([^"]+)"', svg)
    circ = re.search(r'<circle cx="([\d.]+)" cy="([\d.]+)" r="([\d.]+)"', svg)
    outer, inner, back, front = (_path(d) for d, _f in paths)
    sun = Point(float(circ[1]), float(circ[2])).buffer(float(circ[3]), 32)
    return outer, inner, sun, back, front


def _fit(shapes, ref):
    """Flip y up and scale so ref is 1 unit tall, centred."""
    b = ref.bounds
    k = 1 / (b[3] - b[1])
    return [sscale(translate(sscale(g, 1, -1, origin=(0, 0)), -(b[0] + b[2]) / 2, (b[1] + b[3]) / 2), k, k, origin=(0, 0))
            for g in shapes]


GAP = 1.1      # the chrome badge's knock out lines, in badge units, as its SVG draws them


def badge_chrome():
    """The chrome badge, as nusa-badge-chrome.svg knocks it out: the drop's rim, the sun and the
    peaks, each parted from its neighbours by a thin gap. One chrome shape."""
    outer, inner, sun, back, front = _badge_parts()
    rim = outer.buffer(-GAP).difference(inner)
    peaks = back.buffer(-GAP).difference(front.buffer(GAP)).union(front.buffer(-GAP))
    whole = unary_union([rim, sun.buffer(-GAP), peaks.intersection(inner.buffer(-GAP))])
    return _fit([whole], outer)[0]


def badge_colour():
    """The colour badge: [(shape, material)], each colour its own piece, none overlapping."""
    outer, inner, sun, back, front = _badge_parts()
    e = 0.15
    pieces = [(front, "badge_cream"), (back.difference(front), "badge_sage"), (sun, "badge_red")]
    top = unary_union([g for g, _m in pieces])
    pieces.append((inner.difference(top), "badge_green"))
    pieces.append((outer.difference(inner), "badge_gold"))
    shapes = _fit([g.intersection(inner if m != "badge_gold" else outer).buffer(-e) for g, m in pieces], outer)
    return [(g, m) for g, (_g, m) in zip(shapes, pieces)]


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


BADGE_T = 0.008      # the badges stand proud of the panel by this much
NAME_T = 0.004       # the name's letters stand proud by this much


def badge(ref, style, size, fr, a, b, up, name):
    if style == "chrome":
        return _flat(ref, badge_chrome(), size, fr, a, b, up, name, 0.0012, BADGE_T, "chrome", 0.003)
    obs = []
    for i, (g, m) in enumerate(badge_colour()):
        obs += _flat(ref, g, size, fr, a, b, up, f"{name}_{i}", 0.0012, BADGE_T, m, 0.002)
    return obs


def options(ref):
    """Badge and name options, named opt_<group>_<n>_<i>. Each lies flat on its panel.
    Odd numbers are the chrome badge, even the colour badge, at the same spot."""
    g = R["grille"]
    ny, nz, nh = R["spots"]["lid_name"]
    up, fwd = (0, 0, 1), (-1, 0, 0)
    face_z = (LID_BOTTOM + LID_TOP) / 2 - 0.01
    out = []

    def add(group, n, objs):
        for i, ob in enumerate(objs):
            ob.name = f"opt_{group}_{n}_{i}"
            out.append(ob)
    gz = sum(g["badge"][:2]) / 2
    bonnet_x = float(np.array(R["bonnet"])[:, 0].max()) - 0.09
    for k, style in ((0, "chrome"), (1, "colour")):
        # front: in the middle of the grille (the reference), or on the bonnet's front edge
        add("frontbadge", 1 + k, badge(ref, style, 0.105, front_fr(), 0, gz, up, "fb"))
        add("frontbadge", 3 + k, badge(ref, style, 0.07, top_fr(), bonnet_x, 0, fwd, "fb"))
        # rear: on the left of the lid face where the reference has its maker's script, or in the middle
        add("rearbadge", 1 + k, badge(ref, style, 0.07, rear_fr(), -ny, face_z, up, "rb"))
        add("rearbadge", 3 + k, badge(ref, style, 0.07, rear_fr(), 0, face_z, up, "rb"))
    nm = name_shape()
    cap = max(nh * 0.75, 0.03)
    # name: on the right of the lid face (the reference), on the left of it, or on both front wings
    add("name", 1, _flat(ref, nm, cap, rear_fr(), ny, face_z, up, "nm", 0.0012, NAME_T, "chrome"))
    add("name", 2, _flat(ref, nm, cap, rear_fr(), -ny, face_z, up, "nm", 0.0012, NAME_T, "chrome"))
    q = []
    for sgn in (1, -1):
        q += _flat(ref, nm, cap, side_fr(sgn), W["front_x"], W["r"] + W["arch_r"] + 0.08, up,
                   "nm", 0.0012, NAME_T, "chrome")
    add("name", 3, q)
    return out
