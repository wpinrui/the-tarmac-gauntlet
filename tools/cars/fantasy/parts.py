"""The Fantasy's parts: lamps, grilles, glass, trim, spoiler, mirrors, exhaust, interior, wheels,
badge and name. Every shape and size here is the Fantasy's own, from rows.json and Ivan's
reference, kept plain and smooth. Runs inside Blender's Python module, called by export.py."""
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
    "trim": ((0.025, 0.025, 0.03), 0.65, 1.0, None),
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
    "badge_dark": ((0.07, 0.08, 0.09), 0.7, 1.0, None),
    "name": ((0.86, 0.87, 0.89), 0.98, 1.0, None),
    "interior": ((0.09, 0.09, 0.1), 0.15, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.66, 0.68, 0.7), 0.9, 1.0, None),
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


def plane_fr(pts, out):
    """A frame on the best plane through 3D points, its rays running into the body (out is a
    rough outward direction), and a map from 3D points to its 2D."""
    p = np.asarray(pts, float).reshape(-1, 3)
    c = p.mean(0)
    _u, _s, vt = np.linalg.svd(p - c)
    n = vt[2]
    if n @ np.asarray(out, float) < 0:
        n = -n
    u = vt[0]; v = np.cross(n, u)
    fr = blend.frame(c, u, v, -n)
    return fr, lambda q: np.column_stack([(np.asarray(q) - c) @ u, (np.asarray(q) - c) @ v])


def poly(pts):
    return Polygon(pts).buffer(0)


def flip(g):
    """Mirror a 2D shape about its first axis (y for front and rear frames)."""
    return sscale(g, -1, 1, origin=(0, 0))


def mirrored(g):
    """A front or rear view shape made even: its left half (y > 0) and that half mirrored."""
    half = g.intersection(box(0, -5, 5, 5))
    return half.union(flip(half)).buffer(1e-4).buffer(-1e-4)


def soft(g, r):
    """Round off a shape's corners."""
    return g.buffer(-r, join_style=1).buffer(r, join_style=1)


def biggest(g):
    if g.geom_type != "Polygon":
        g = max(g.geoms, key=lambda q: q.area)
    return Polygon(g.exterior)


def smooth_loop(pts, s=0.0004, n=160):
    """A closed outline as one smooth spline through its points."""
    from scipy.interpolate import splev, splprep
    p = np.asarray(pts, float)
    p = geom.resample(np.vstack([p, p[:1]]), 0.01)[:-1]
    tck, _u = splprep([p[:, 0], p[:, 1]], s=s, per=1, k=3)
    x, y = splev(np.linspace(0, 1, n, endpoint=False), tck)
    return poly(np.column_stack([x, y]))


# ---------------------------------------------------------------- lamps
class Wrap:
    """A frame bending round a front or rear corner of the body. In plan it is a rounded
    rectangle: rays run straight in across the face, fan out round a corner of radius rc
    centred at (xc, yc), then run straight in along the side. 2D (u, z): u runs from the
    centre line out round the corner and along the side."""

    def __init__(self, end, sgn, xc, yc, rc, tilt=0.0):
        self.f = 1 if end == "front" else -1
        self.tilt = tilt
        self.sgn, self.xc, self.yc, self.rc = sgn, xc, yc, rc
        self.u1 = yc + rc * np.pi / 2

    def __call__(self, u, z):
        f, s = self.f, self.sgn
        if u <= self.yc:
            o, d, w = (self.xc + f * (self.rc + 0.5), u), (-f, 0.0), 0.0
        elif u >= self.u1:
            x = self.xc - f * (u - self.u1)
            o, d, w = (x, self.yc + self.rc + 0.5), (0.0, -1.0), 1.0
        else:
            t = (u - self.yc) / self.rc
            c, n = np.cos(t), np.sin(t)
            o, d, w = (self.xc + f * c * (self.rc + 0.5), self.yc + n * (self.rc + 0.5)), (-f * c, -n), n
        k = np.tan(self.tilt) * w
        return np.array([o[0], o[1] * s, z + 0.5 * k]), np.array([d[0], d[1] * s, -k])

    def to_uv(self, p):
        """The (u, z) whose ray passes through the 3D point p."""
        x, y, z = float(p[0]), abs(float(p[1])), float(p[2])
        f, k = self.f, np.tan(self.tilt)
        dx = f * (x - self.xc)
        if y <= self.yc:
            return y, z
        if dx <= 0:
            u = self.u1 - dx
            run = self.yc + self.rc + 0.5 - y
            return u, z - k * 0.5 + k * run
        t = np.arctan2(y - self.yc, dx)
        u = self.yc + self.rc * t
        run = self.rc + 0.5 - np.hypot(dx, y - self.yc)
        w = np.sin(t)
        return u, z - k * w * 0.5 + k * w * run

    def outline(self, loops):
        """One smooth outline in (u, z) from the lamp's 3D edge loops."""
        u = unary_union([poly([self.to_uv(p) for p in loop]) for loop in loops])
        u = biggest(u.buffer(0.01, join_style=1).buffer(-0.01, join_style=1))
        return soft(u, 0.006).simplify(0.001)


# The head lamps sweep from the grille back along the wing; the tail lamps stand up the
# corners of the tailgate. Each frame bends round its corner of the body.
HEAD_WRAP = dict(end="front", xc=NOSE - 0.42, yc=0.36, rc=0.38, tilt=0.45)
TAIL_WRAP = dict(end="rear", xc=TAIL + 0.42, yc=0.44, rc=0.34, tilt=0.15)


def headlamps(ref, cutters, out, sgn):
    """The lamp glass traced from the reference. Inside, our own: a big round projector at the
    inner end, a smaller one beside it, and a light strip along the top edge back to the tip."""
    side = "l" if sgn > 0 else "r"
    wr = Wrap(sgn=sgn, **HEAD_WRAP)
    shp = wr.outline(R["head"])
    cutters += blend.lay(ref, f"cut_head_{side}", shp, wr, -0.016, 0.006, "housing", 0.01, along_ray=True)
    b = shp.bounds
    inner = shp.buffer(-0.012)
    lenses = []
    for i, (t, r) in enumerate(((0.16, 0.036), (0.34, 0.027))):
        u = b[0] + (b[2] - b[0]) * t
        cut = inner.intersection(LineString([(u, -5), (u, 5)]))
        if cut.is_empty:
            continue
        z = (cut.bounds[1] + cut.bounds[3]) / 2
        c = Point(u, z)
        r = min(r, (cut.bounds[3] - cut.bounds[1]) / 2 - 0.004)
        out += blend.lay(ref, f"lamp_head_{side}{i}", c.buffer(r, 48).intersection(inner), wr, -0.0115, -0.008, "lamp_head", 0.004, along_ray=True)
        out += blend.lay(ref, f"head_reflector_{side}{i}", c.buffer(r + 0.011, 48).difference(c.buffer(r + 0.002, 48)).intersection(inner),
                         wr, -0.0112, -0.0098, "reflector", 0.004, along_ray=True)
        lenses.append(c.buffer(r + 0.016, 48))
    # the strip: a band just under the top edge, from beside the lenses to the tip
    top = shp.buffer(-0.008).difference(shp.buffer(-0.020)).difference(unary_union(lenses))
    upper = top.intersection(box(b[0], (b[1] + b[3]) / 2 + 0.005, b[2], b[3]))
    if not upper.is_empty:
        out += blend.lay(ref, f"lamp_head_{side}2", biggest(upper), wr, -0.0115, -0.008, "lamp_head", 0.004, along_ray=True)
    out += blend.lay(ref, f"head_glass_{side}", shp.buffer(-0.003), wr, -0.0045, -0.0015, "head_glass", 0.01, along_ray=True)


def taillamps(ref, cutters, out, sgn):
    """The lamp glass traced from the reference. Inside, our own: a tail light ring round the
    lamp's upper half and a brake light filling its lower part."""
    side = "l" if sgn > 0 else "r"
    wr = Wrap(sgn=sgn, **TAIL_WRAP)
    shp = wr.outline(R["tail"])
    cutters += blend.lay(ref, f"cut_tail_{side}", shp, wr, -0.016, 0.006, "housing", 0.01, along_ray=True)
    b = shp.bounds
    split = b[1] + 0.42 * (b[3] - b[1])
    core = shp.buffer(-0.012)
    upper = core.intersection(box(-5, split + 0.006, 5, 5)).difference(core.buffer(-0.022))
    lower = core.intersection(box(-5, -5, 5, split - 0.006))
    out += blend.lay(ref, f"lamp_tail_{side}", biggest(upper), wr, -0.0115, -0.008, "lamp_tail", 0.004, along_ray=True)
    out += blend.lay(ref, f"lamp_brake_{side}", biggest(lower), wr, -0.0115, -0.008, "lamp_brake", 0.004, along_ray=True)
    out += blend.lay(ref, f"tail_glass_{side}", shp.buffer(-0.003), wr, -0.0045, -0.0015, "tail_glass", 0.01, along_ray=True)


# ---------------------------------------------------------------- front and rear
SLATS = (0.275, 0.315, 0.395, 0.455)    # heights of the bars across the lower grille


def front_parts(ref, cutters, out):
    fr = front_fr()
    # the slim upper grille under the bonnet's edge
    up = soft(mirrored(poly(R["grille_upper"])), 0.006)
    out += blend.lay(ref, "grille_upper", up, fr, 0.0015, 0.0045, "grille", 0.006)
    # the wide lower grille, recessed, with plain bars across it
    lo = soft(mirrored(poly(R["grille_lower"])), 0.03)
    cutters += blend.lay(ref, "cut_grille", lo, fr, -0.035, 0.006, "grille", 0.012, along_ray=True)
    out += blend.lay(ref, "grille_back", lo.buffer(-0.002), fr, -0.029, -0.025, "grille", 0.012, along_ray=True)
    for i, z in enumerate(SLATS):
        bar = box(-1, z - 0.007, 1, z + 0.007).intersection(lo.buffer(-0.004))
        if not bar.is_empty:
            out += blend.lay(ref, f"grille_slat_{i}", bar, fr, -0.0245, -0.006, "trim", 0.006, along_ray=True)
    # the chin lip
    chin = soft(mirrored(poly(R["chin"])), 0.02)
    out += blend.lay(ref, "chin", chin.intersection(box(-1, -1, 1, 0.19)), fr, 0.0015, 0.005, "trim", 0.01)
    # round fog lamps in black bezels
    fy, fz, fr_r = R["fog"]
    for sgn, side in ((1, "l"), (-1, "r")):
        c = Point(abs(fy) * sgn, fz)
        bezel = c.buffer(fr_r + 0.012, 48)
        cutters += blend.lay(ref, f"cut_fog_{side}", bezel, fr, -0.022, 0.006, "grille", 0.006, along_ray=True)
        out += blend.lay(ref, f"fog_ring_{side}", c.buffer(fr_r * 0.62 + 0.008, 40).difference(c.buffer(fr_r * 0.62 + 0.0015, 40)),
                         fr, -0.0175, -0.013, "reflector", 0.004, along_ray=True)
        out += blend.lay(ref, f"lamp_head_fog_{side}", c.buffer(fr_r * 0.62, 40), fr, -0.0179, -0.011, "lamp_head", 0.004, along_ray=True)


def rear_parts(ref, cutters, out):
    fr = rear_fr()
    d = soft(mirrored(poly(R["diffuser"])), 0.02)
    out += blend.lay(ref, "diffuser", d, fr, 0.0015, 0.006, "trim", 0.01)
    refl = soft(poly(R["reflector"]), 0.008)
    for sgn, side in ((1, "l"), (-1, "r")):
        g = refl if sgn > 0 else flip(refl)
        out += blend.lay(ref, f"reflector_{side}", g, fr, 0.0015, 0.004, "tail_glass", 0.006)
    b = R["brake3"]
    strip = soft(box(-b["half"], b["z"] - b["h"] / 2, b["half"], b["z"] + b["h"] / 2), 0.008)
    out += blend.lay(ref, "lamp_brake_top", strip, fr, 0.0012, 0.004, "lamp_brake", 0.006)


def exhaust():
    """Two round tips side by side under the right of the bumper."""
    e = R["exhaust"]
    cut, pipes = [], []
    for i, y in enumerate(e["y"]):
        prof = [(e["r"] - 0.003, 0.0), (e["r"], 0.0), (e["r"], 0.22), (e["r"] - 0.003, 0.22)]
        v, f = geom.lathe(prof, 48, axis="z")
        v = np.column_stack([e["x"] + v[:, 2], y + v[:, 0], e["z"] + v[:, 1]])
        pipes.append(blend.make(f"exhaust_{i}", v, f, "chrome"))
        v, f = geom.disc((e["x"] + 0.11, y, e["z"]), (1, 0, 0), e["r"] + 0.004, 0.26)
        cut.append(blend.make("cut_exhaust", v, f, "trim"))
    return cut, pipes


# ---------------------------------------------------------------- glass and trim
GLASS_BAND = 0.016    # black band round the windscreen and the rear glass
SIDE_BAND = 0.012     # black band round the side windows


def pane(pts):
    """A side window outline (x, z): one straight line along its bottom, fitted to the low
    edge's middle, the rest one smooth curve, corners rounded."""
    p = np.asarray(pts, float)
    g0 = poly(p)
    x0, x1 = g0.bounds[0], g0.bounds[2]
    xs = np.linspace(x0 + 0.15 * (x1 - x0), x1 - 0.15 * (x1 - x0), 40)
    lo = [g0.intersection(LineString([(x, -5), (x, 5)])).bounds[1] for x in xs]
    k = np.polyfit(xs, lo, 1)
    g = smooth_loop(p, 0.0002)
    under = Polygon([(x0 - 1, np.polyval(k, x0 - 1)), (x1 + 1, np.polyval(k, x1 + 1)), (x1 + 1, 5), (x0 - 1, 5)])
    full = g.intersection(g0.buffer(0.004)).intersection(under)
    return soft(biggest(full), 0.01), k


def side_shapes():
    """In side view (x, z): the black opening round the three side windows, the panes inside
    it, and the belt line under the door glass. The pillars between the panes are black too."""
    door, k = pane(R["door_glass"])
    quarter, _k = pane(R["quarter_glass"])
    tri, _k = pane(R["tri_glass"])
    allp = unary_union([door, quarter, tri])
    # bridge the pillars: the upper and lower edges of all three panes, run on across the gaps
    from scipy.ndimage import maximum_filter1d, minimum_filter1d
    x0, x1 = allp.bounds[0], allp.bounds[2]
    xs = np.linspace(x0 + 0.001, x1 - 0.001, 400)
    hi, lo = [], []
    for x in xs:
        c = allp.intersection(LineString([(x, -5), (x, 5)]))
        hi.append(c.bounds[3] if not c.is_empty else np.nan)
        lo.append(c.bounds[1] if not c.is_empty else np.nan)
    hi, lo = np.array(hi), np.array(lo)
    ok = ~np.isnan(hi)
    hi = maximum_filter1d(np.interp(xs, xs[ok], hi[ok]), 30)
    lo = minimum_filter1d(np.interp(xs, xs[ok], lo[ok]), 30)
    env = Polygon(np.vstack([np.column_stack([xs, lo]), np.column_stack([xs, hi])[::-1]])).buffer(0)
    opening = env.intersection(allp.convex_hull).union(allp)
    opening = soft(biggest(opening.buffer(SIDE_BAND, join_style=1)), 0.012)
    panes = [soft(g.buffer(-0.002, join_style=1), 0.008) for g in (tri, door, quarter)]
    return opening, panes, k


def belt(x):
    return float(np.polyval(side_shapes()[2], x))


def side_frame(sgn):
    """A frame on the side window's own plane, and a map from side view (x, z) shapes into it."""
    opening = side_shapes()[0]
    xz = np.array(opening.exterior.coords)
    pts = np.column_stack([xz[:, 0], [body.y_at(x, z) * sgn for x, z in xz], xz[:, 1]])
    fr, to2 = plane_fr(pts, (0, sgn, 0.4))

    def lift(g):
        def one(q):
            def ring(c):
                c = np.asarray(c)[:-1]
                return to2(np.column_stack([c[:, 0], [body.y_at(x, z) * sgn for x, z in c], c[:, 1]]))
            return Polygon(ring(q.exterior.coords), [ring(i.coords) for i in q.interiors]).buffer(0)
        return unary_union([one(q) for q in getattr(g, "geoms", [g])])
    return fr, lift


def plain_glass(pts, lo, hi, r=0.05):
    """A glass outline rebuilt from three plain fits to the measured one: the low edge and the
    high edge as even curves across the car, and the sides as straight lines. pts are
    (along, across); lo and hi pick the low and high edge points by their along value."""
    from shapely.geometry import MultiPoint
    p = np.asarray(pts, float)
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
    hull = MultiPoint(np.vstack([p, p * [1, -1]])).convex_hull     # never wider than the measured glass
    return soft(band.intersection(sides).intersection(hull), r)


def screen_shapes():
    s = R["screen"]
    span = s["x_base"] - s["x_top"]
    outline = plain_glass(s["outline"], lambda a: a > s["x_base"] - 0.25 * span, lambda a: a < s["x_top"] + 0.12 * span, 0.06)
    glass = outline.buffer(-GLASS_BAND, join_style=1)
    return outline, glass, outline.difference(glass.buffer(-0.005))


def screen_fr():
    s = R["screen"]
    return slope_fr(s["x_top"], body.z_top(s["x_top"]), s["x_base"], body.z_top(s["x_base"]))


def rear_glass_fr():
    """A frame on the rear glass's own plane, its 2D (across, up the slope)."""
    pts = np.array(R["rear_glass3d"])
    fr, to2 = plane_fr(pts, (-1, 0, 0.6))
    o, u, v, d = fr
    if u[1] < 0:                       # across runs to the car's left
        u = -u
    v = np.cross(-d, u)
    if v[2] < 0:
        v = -v; u = -u
    fr = blend.frame(o, u, v, d)
    return fr, lambda q: np.column_stack([(np.asarray(q) - o) @ u, (np.asarray(q) - o) @ v])


def rear_shapes():
    fr, to2 = rear_glass_fr()
    p = to2(np.array(R["rear_glass3d"]))[:, ::-1]      # (up the slope, across)
    a = p[:, 0]
    span = a.max() - a.min()
    outline = plain_glass(p, lambda t: t < a.min() + 0.25 * span, lambda t: t > a.max() - 0.2 * span, 0.06)
    outline = sscale(outline.buffer(0), 1, 1)
    outline = Polygon([(q[1], q[0]) for q in np.array(outline.exterior.coords)])     # back to (across, up)
    glass = outline.buffer(-GLASS_BAND, join_style=1)
    return fr, outline, glass, outline.difference(glass.buffer(-0.005))


def glass_parts(ref0, cutters, out):
    opening, panes, _k = side_shapes()
    for sgn, side in ((1, "l"), (-1, "r")):
        fr, lift = side_frame(sgn)
        trim = lift(opening.difference(unary_union([p.buffer(-0.004) for p in panes])))
        out += blend.lay(ref0, f"window_trim_{side}", trim, fr, 0.0015, 0.0035, "trim", 0.01, along_ray=True)
        for i, p in enumerate(panes):
            g = lift(p)
            cutters += blend.lay(ref0, f"cut_side_{side}{i}", g.buffer(-0.002), fr, -0.08, 0.006, "trim", along_ray=True)
            out += blend.lay(ref0, f"side_glass_{side}{i}", g.buffer(-0.005), fr, -0.008, -0.004, "glass", 0.02, along_ray=True)
    fr, to_fr = screen_fr()
    _o, g, t = (to_fr(q) for q in screen_shapes())
    cutters += blend.lay(ref0, "cut_screen", g.buffer(-0.003), fr, -0.08, 0.006, "trim", along_ray=True)
    out += blend.lay(ref0, "screen_glass", g.buffer(-0.006), fr, -0.008, -0.004, "glass", 0.02, along_ray=True)
    out += blend.lay(ref0, "screen_trim", t, fr, 0.0015, 0.0035, "trim", 0.01)
    fr, _o, g, t = rear_shapes()
    cutters += blend.lay(ref0, "cut_rear", g.buffer(-0.003), fr, -0.08, 0.004, "trim", along_ray=True)
    out += blend.lay(ref0, "rear_glass", g.buffer(-0.006), fr, -0.008, -0.004, "glass", 0.02, along_ray=True)
    out += blend.lay(ref0, "rear_trim", t, fr, 0.0015, 0.0035, "trim", 0.01)


# ---------------------------------------------------------------- shut lines
def door_path():
    """The door's outline, open where it runs under the window. Each end runs on into the
    window's black band."""
    opening, _p, _k = side_shapes()
    path = np.array(R["door"])
    band = opening.buffer(-0.006)

    def carry(end, prev):
        t = (end - prev) / np.linalg.norm(end - prev)
        for s in np.arange(0.005, 0.4, 0.005):
            q = end + t * s
            if band.contains(Point(q)):
                return [(end + t * x).tolist() for x in np.arange(0.02, s, 0.02)] + [q.tolist()]
        return []
    path = geom.resample(path, 0.01)
    head = carry(path[0], path[min(6, len(path) - 1)])
    tail = carry(path[-1], path[max(-7, -len(path))])
    return head[::-1] + path.tolist() + tail


BONNET_REAR = 1.28     # behind this the measured lid outline is its rear edge


def bonnet_path():
    """The bonnet shut line in plan, as one plain U: out of the windscreen's lower corner,
    forward along the wing, round the lamps and across the nose. The lid runs under the
    windscreen, so the line has no rear edge."""
    from scipy.interpolate import splev, splprep
    o, _g, _t = screen_shapes()
    c = np.array(o.exterior.coords)
    corner = c[np.argmax(c[:, 1] + 0.6 * c[:, 0])]
    b = geom.resample(np.vstack([R["bonnet"], R["bonnet"][:1]]), 0.01)
    y_edge = b[:, 1].max()
    # the measured lid's rear edge, short of the windscreen, is left out
    keep = (b[:, 1] > 0.02) & ((b[:, 1] > y_edge - 0.03) | (b[:, 0] > BONNET_REAR)) & (b[:, 0] > corner[0] + 0.06)
    side = b[keep]
    side = side[np.argsort(np.arctan2(side[:, 1], side[:, 0] - BONNET_REAR))[::-1]]
    nose = b[:, 0].max()
    half = np.vstack([[corner[0] + 0.01, corner[1] - 0.01], side, [[nose, 0.0]]])
    pts = np.vstack([half, half[-2::-1] * [1, -1]])
    tck, _u = splprep([pts[:, 0], pts[:, 1]], s=0.003, k=3)
    x, y = splev(np.linspace(0, 1, 480), tck)
    q = np.column_stack([x, y])
    q = q[: int(np.argmax(q[:, 1] <= 0))]
    q = np.vstack([q, [[q[-1, 0], 0.0]]])
    return np.vstack([q, (q[-2::-1] * [1, -1])])


def tailgate_path():
    """The tailgate's sides and bottom, seen from behind, made even."""
    t = np.array(R["tailgate"])
    half = t[t[:, 0] >= 0]
    half = half[np.argsort(-half[:, 1])]
    half = np.vstack([half, [[0.0, half[:, 1].min()]]])
    from scipy.interpolate import splev, splprep
    pts = np.vstack([half, half[-2::-1] * [-1, 1]])
    tck, _u = splprep([pts[:, 0], pts[:, 1]], s=0.002, k=3)
    x, y = splev(np.linspace(0, 1, 360), tck)
    return np.column_stack([x, y])


def grooves(ref):
    """Shut lines as V grooves to cut: doors, bonnet, tailgate."""
    obs = []
    path = door_path()
    for sgn, side in ((1, "l"), (-1, "r")):
        obs.append(blend.groove(ref, f"g_door_{side}", path, side_fr(sgn)))
    obs.append(blend.groove(ref, "g_bonnet", bonnet_path(), top_fr()))
    obs.append(blend.groove(ref, "g_tailgate", tailgate_path(), rear_fr()))
    return obs


# ---------------------------------------------------------------- doors
DOOR_DEPTH = 0.05     # below the cabin floor the door takes the sill this deep
DOOR_WALL = 0.09      # above it, the whole wall: the cabin is hollow behind it
DOOR_GAP = 0.0015     # half the gap round the door, inside the shut line's groove
DOOR_OPEN = 65.0      # degrees the door swings out


def door_outline():
    """The left door seen from the side: inside its shut line, and the window frame round the
    door glass and the small front glass, back to the B-pillar's middle."""
    d = door_path()
    opening, _p, _k = side_shapes()
    frame = opening.intersection(box(sum(R["b_pillar"]) / 2, -1, 5, 5))
    return unary_union([Polygon(d).buffer(0), frame])


def mirror_footprint():
    """The mirror on the door seen from the side, with room: taken only outside the skin."""
    m = R["mirror"]
    return box(m["x_back"] - 0.05, m["z0"] - 0.06, m["x_front"] + 0.06, m["z1"] + 0.06)


def door_hinge(sgn=1):
    """The hinge axis up the door's front edge, outside the skin, and the open angle. Turning
    about an axis pointing up, a negative angle swings the left door's back edge out and a
    positive one the right's. sgn picks the side: 1 left, -1 right."""
    d = np.array(R["door"])
    xf = d[:, 0].max() + 0.004
    zs = d[:, 1]
    y = max(body.y_at(xf - 0.01, z) for z in np.linspace(zs.min(), zs.max(), 20)) + 0.003
    return (xf, y * sgn, zs.min()), (xf, y * sgn, zs.max()), -DOOR_OPEN * sgn


def door(ref, shell, obs, floor, sgn=1):
    """Cut the left (sgn 1) or right (sgn -1) door free of the body and its window trim; the
    door glass, card and mirror glass go with it. Returns the door's pieces."""
    side = "l" if sgn > 0 else "r"
    o = door_outline()
    regions = [(o.intersection(box(-5, -1, 5, floor + 0.02)), DOOR_DEPTH),
               (o.intersection(box(-5, floor + 0.02, 5, 5)), DOOR_WALL), (mirror_footprint(), -0.004)]
    trim = [ob for ob in obs if ob.name == f"window_trim_{side}"]
    roles = {f"side_glass_{side}0": "glass", f"side_glass_{side}1": "glass", f"mirror_glass_{side}": "mirror",
             f"door_card_{side}": "card"}
    return blend.free_door(ref, side_fr(sgn), regions, DOOR_GAP, shell, trim, roles, o, name=f"door_{side}")


# ---------------------------------------------------------------- spoiler
def spoiler():
    """Roof spoiler: the traced profile, smoothed, carried across and trimmed to the roof's own
    section, so its sides follow the roof."""
    g = soft(poly(R["spoiler"]["profile"]).buffer(0.01, join_style=1).buffer(-0.01, join_style=1), 0.012)
    prof = np.array(biggest(g).simplify(0.002).exterior.coords)[:-1]
    h = R["spoiler"]["half"]
    ring = geom.resample(np.vstack([prof, prof[:1]]), 0.006)[:-1]
    rings = [np.column_stack([ring[:, 0], np.full(len(ring), y), ring[:, 1]]) for y in (-h, h)]
    v, f = geom.close_rings(rings)
    sp = blend.make("spoiler", v, f, "body")
    x0 = float(prof[:, 0].max())
    sec = body.section(body.at(body.CV, x0))
    roof = Polygon(sec).buffer(0.004)
    ring2 = np.array(roof.exterior.coords)[:-1]
    rings = [np.column_stack([np.full(len(ring2), x), ring2[:, 0], ring2[:, 1]]) for x in (x0 + 0.05, prof[:, 0].min() - 0.05)]
    v, f = geom.close_rings(rings)
    blend.boolean(sp, blend.make("roofline", v, f, "body"), "INTERSECT")
    return sp


# ---------------------------------------------------------------- mirrors
MIRROR_R = 0.03       # corner rounding of the glass recess
MIRROR_SWEEP = 0.12   # radians the housing turns back from square to the car


def _rounded_bar(name, y0, y1, z0, z1, x0, x1, r, sgn, mat="body"):
    face = soft(box(y0, z0, y1, z1), r)
    ring = body.even(np.array(face.exterior.coords)[:-1], 96)
    rings = [np.column_stack([np.full(96, x), ring[:, 0] * sgn, ring[:, 1]]) for x in (x0, x1)]
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def _root():
    m = R["mirror"]
    return body.y_at((m["x_back"] + m["x_front"]) / 2, m["z0"] + 0.03) - 0.02     # sunk into the door


MIRROR_X = 0.10       # housing depth front to back at its outer end
MIRROR_ROOT = 0.45    # the arm's size at the door, as a share of the housing's


def _housing():
    """(y0, y1, z0, z1, x0, x1) of the housing at its outer end."""
    m = R["mirror"]
    return m["y_out"] - 0.16, m["y_out"], m["z0"] + 0.03, m["z1"], m["x_back"], m["x_back"] + MIRROR_X


def _sweep(ob, sgn):
    """Turn a mirror piece about its root, so the outer end sits further back."""
    from mathutils import Matrix
    m = R["mirror"]
    p = Matrix.Translation((m["x_back"] + 0.06, sgn * _root(), 0))
    ob.data.transform(p @ Matrix.Rotation(sgn * MIRROR_SWEEP, 4, "Z") @ p.inverted())
    return ob


def mirrors():
    """One smooth piece from the door out to the housing: a slim root growing into the full
    housing, its back face flat for the glass, its front rounded."""
    y0, y1, z0, z1, x0, x1 = _housing()
    root = _root()
    zc = (z0 + z1) / 2
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        rings = []
        ys = np.linspace(root, y1, 24)
        for y in ys:
            t = np.clip((y - root) / (y0 + 0.03 - root), 0, 1)
            k = MIRROR_ROOT + (1 - MIRROR_ROOT) * (1 - (1 - t) ** 2)      # grows fast, then holds
            if y > y1 - 0.03:                                            # the outer end rounds off
                k *= np.sqrt(max(1 - ((y - (y1 - 0.03)) / 0.032) ** 2, 0.05))
            h, d = (z1 - z0) * k, MIRROR_X * (0.6 + 0.4 * k)
            r = min(h, d) * 0.45
            face = soft(box(x0, zc - h / 2 - (1 - k) * 0.02, x0 + d, zc + h / 2 - (1 - k) * 0.02), r)
            ring = body.even(np.array(face.exterior.coords)[:-1], 64)
            rings.append(np.column_stack([ring[:, 0], np.full(64, y * sgn), ring[:, 1]]))
        v, f = geom.close_rings(rings)
        obs.append(_sweep(blend.make(f"mirror_{side}", v, f, "body"), sgn))
    return obs


def mirror_recess():
    """A shallow recess in each housing's back, with the glass inside."""
    y0, y1, z0, z1, x0, _x1 = _housing()
    cut, glass = [], []
    for sgn, side in ((1, "l"), (-1, "r")):
        cut.append(_sweep(_rounded_bar(f"cut_mirror_{side}", y0 + 0.012, y1 - 0.035, z0 + 0.012, z1 - 0.012,
                                       x0 - 0.03, x0 + 0.007, MIRROR_R - 0.012, sgn, "trim"), sgn))
        glass.append(_sweep(_rounded_bar(f"mirror_glass_{side}", y0 + 0.0135, y1 - 0.0365, z0 + 0.0135, z1 - 0.0135,
                                         x0 + 0.002, x0 + 0.005, MIRROR_R - 0.0135, sgn, "mirror_glass"), sgn))
    return cut, glass


def arches():
    """Arch cutters and the wheel tubs kept out of the cabin."""
    cut, tubs = [], []
    for x in (W["front_x"], W["rear_x"]):
        for sgn in (1, -1):
            v, f = geom.disc((x, 0.83 * sgn, W["r"]), (0, 1, 0), W["arch_r"], 0.56, n=96)
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


def _tube(name, path, r, mat, n=16):
    """A round tube along a 3D path, ends closed."""
    p = np.asarray(path, float)
    rings = []
    for i in range(len(p)):
        t = p[min(i + 1, len(p) - 1)] - p[max(i - 1, 0)]
        t /= np.linalg.norm(t)
        a = np.array([0, 0, 1.0]) if abs(t[2]) < 0.9 else np.array([0, 1.0, 0])
        u = np.cross(t, a); u /= np.linalg.norm(u)
        v = np.cross(t, u)
        ang = np.linspace(0, 2 * np.pi, n, endpoint=False)
        rings.append(np.array([p[i] + r * (np.cos(q) * u + np.sin(q) * v) for q in ang]))
    vv, ff = geom.close_rings(rings)
    return blend.make(name, vv, ff, mat)


def interior(floor):
    """Firewall, dash, door cards, two front seats with the driver on the left, a rear bench,
    the steering wheel and the rear view mirror."""
    x0 = R["screen"]["x_base"] - 0.06
    obs = [_loft("firewall", x0 - 0.010, x0 - 0.004, lambda x: box(-0.5, floor + 0.002, 0.5, 3), n=2),
           _loft("dash", x0 - 0.0125, 0.50, lambda x: box(-0.5, 0.66, 0.5, 0.86 + 0.08 * (1 - (x0 - x) / 0.7)), n=10)]
    belt_z = belt(0.2)
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, floor + 0.06, 1.2, belt_z - 0.02) if s > 0 else box(-1.2, floor + 0.06, -0.3, belt_z - 0.02)).intersection(
            body.inside(x, 0.035 + 0.003)).difference(body.inside(x, 0.035 + 0.025))
        obs.append(_loft(f"door_card_{side}", -0.42, 0.78, clip, n=14))
        yc = 0.36 * sgn
        obs.append(_rbox(f"seat_base_{side}", -0.42, 0.08, yc - 0.24, yc + 0.24, floor + 0.002, 0.50))
        obs.append(_rbox(f"seat_back_{side}", -0.56, -0.44, yc - 0.23, yc + 0.23, 0.501, 1.10, lean=0.2))
        obs.append(_rbox(f"headrest_{side}", -0.71, -0.61, yc - 0.12, yc + 0.12, 1.101, 1.22, r=0.035, lean=0.1))
    obs.append(_rbox("bench_base", -1.12, -0.68, -0.48, 0.48, floor + 0.002, 0.50))
    obs.append(_rbox("bench_back", -1.24, -1.13, -0.48, 0.48, 0.501, 1.00, lean=0.24))
    # the steering wheel ahead of the driver's seat on the left, on a column into the dash
    yc = 0.36
    ang = np.linspace(0, 2 * np.pi, 41)
    ring = [(0.30 + 0.05 * np.sin(a), yc + 0.18 * np.cos(a), 0.88 + 0.17 * np.sin(a)) for a in ang]
    col = _tube("column", np.array([(0.30, yc, 0.875), (0.52, yc, 0.84)]), 0.025, "trim")
    for ob in (_tube("steering_wheel", np.array(ring), 0.016, "trim"),
               _tube("hub", np.array([(0.29, yc, 0.88), (0.31, yc, 0.877)]), 0.065, "trim")):
        blend.boolean(col, ob, "UNION")
    obs.append(col)
    xm = R["screen"]["x_top"] - 0.03
    roof_in = body.inside(xm, 0.035).bounds[3]
    top = roof_in - 0.06
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.11, 0.11, top - 0.05, top, r=0.02)
    stem = _rbox("stem", xm - 0.016, xm - 0.004, -0.012, 0.012, top - 0.015, roof_in - 0.0008, r=0.004)
    obs.append(blend.union_all([m, stem], "mirror_rear"))
    return obs


# ---------------------------------------------------------------- wheels
def wheels():
    """Five spoke 16 inch wheels in silver, each spoke splitting in two towards the rim, as the
    Zetec S's own."""
    obs = []
    hw = W["width"] / 2
    yc = W["outer_y"] - hw
    rr = W["rim_r"]
    tyre = soft(box(rr, -hw, W["r"], hw), 0.03)
    tyre = tyre.union(box(rr, -hw + 0.012, rr + 0.03, hw - 0.012))
    tp = body.even(np.array(tyre.exterior.coords)[:-1], 96)
    rim = [(rr - 0.001, -hw + 0.012), (rr - 0.001, hw - 0.01), (rr - 0.012, hw - 0.002),
           (rr - 0.024, hw - 0.006), (rr - 0.024, -hw + 0.012)]
    for x, fb in ((W["front_x"], "f"), (W["rear_x"], "r")):
        for sgn, lr in ((1, "l"), (-1, "r")):
            def place(v, s=sgn):
                v = np.asarray(v, float)
                return np.column_stack([x + v[:, 0], (yc + v[:, 1]) * s, W["r"] + v[:, 2]])
            v, f = geom.lathe(tp, 96, axis="y")
            obs.append(blend.make(f"wheel_tyre_{fb}{lr}", place(v), f, "tyre"))
            v, f = geom.lathe(rim, 96, axis="y")
            parts = [blend.make("rim", place(v), f, "rim")]
            v, f = geom.disc((0, hw - 0.05, 0), (0, 1, 0), 0.07, 0.05, n=48)
            parts.append(blend.make("hub", place(v), f, "rim"))
            for k in range(5):
                a = 2 * np.pi * k / 5
                for split in (-0.07, 0.07):
                    pts = []
                    for r, w2, da, h0, h1 in ((0.062, 0.018, 0.0, hw - 0.075, hw - 0.028),
                                              (rr - 0.018, 0.012, split, hw - 0.07, hw - 0.022)):
                        ca, sa = np.cos(a + da), np.sin(a + da)
                        for dw in (-w2, w2):
                            for hh in (h0, h1):
                                pts.append((r * ca - dw * sa, hh, r * sa + dw * ca))
                    v = np.array(pts)
                    f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
                    parts.append(blend.make("spoke", place(v), f, "rim"))
            obs.append(blend.union_all(parts, f"wheel_rim_{fb}{lr}"))
    return obs


# ---------------------------------------------------------------- badge and name
BRAND = HERE.parent.parent.parent / "src/renderer/assets/brands/liberty"


def _quad(p0, p1, p2, n=10):
    t = np.linspace(0, 1, n)[1:, None]
    return list(map(tuple, (1 - t) ** 2 * np.array(p0) + 2 * (1 - t) * t * np.array(p1) + t * t * np.array(p2)))


def badge_shapes():
    """The Liberty badge from liberty-badge-chrome.svg: [(shape, material)], y up, 1 unit tall.
    A chrome hexagon ring round a dark field, and the chrome bell on the field, its crack cut
    through."""
    svg = (BRAND / "liberty-badge-chrome.svg").read_text()
    hexes = [Polygon([tuple(map(float, p.split(","))) for p in pts.split()])
             for pts in re.findall(r'<polygon points="([^"]+)"', svg)]
    outer, inner = hexes
    bell = Polygon([(74, 70)] + _quad((74, 70), (74, 52), (100, 52)) + _quad((100, 52), (126, 52), (126, 70)) +
                   [(130, 116)] + _quad((130, 116), (132, 134), (150, 140)) + [(150, 148), (50, 148), (50, 140)] +
                   _quad((50, 140), (68, 134), (70, 116)))
    bell = unary_union([bell, box(92, 44, 108, 52), Point(100, 157).buffer(8, 32)])
    crack = LineString([(104, 56), (97, 80), (106, 98), (99, 122), (104, 136)]).buffer(1.75, cap_style=1, join_style=1)
    chrome = unary_union([outer.difference(inner), bell.difference(crack)])
    field = inner.buffer(0.5)
    b = outer.bounds
    k = 1 / (b[3] - b[1])

    def fit(g):
        g = sscale(g, 1, -1, origin=(0, 0))
        g = translate(g, -(b[0] + b[2]) / 2, (b[1] + b[3]) / 2)
        return sscale(g, k, k, origin=(0, 0))
    return [(fit(field), "badge_dark"), (fit(chrome), "chrome")]


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
    """The badge: its dark field just proud of the panel, the chrome standing above it."""
    obs = []
    (field, m0), (chrome, m1) = badge_shapes()
    obs += _flat(ref, field, size, fr, a, b, up, f"{name}_f", 0.0012, 0.003, m0, 0.003)
    obs += _flat(ref, chrome, size, fr, a, b, up, f"{name}_c", 0.0012, 0.0055, m1, 0.003)
    return obs


def options(ref):
    """Every badge and name option, named opt_<group>_<n>_<i>. Each lies flat on its panel."""
    sp = R["spots"]
    nm = name_shape()
    up, fwd = (0, 0, 1), (-1, 0, 0)
    out = []

    def add(group, n, objs):
        for i, ob in enumerate(objs):
            ob.name = f"opt_{group}_{n}_{i}"
            out.append(ob)
    # front: 1 on the upper grille (the reference's place), 2 on the bonnet's nose
    add("frontbadge", 1, badge(ref, 0.07, front_fr(), 0, sp["grille_z"], up, "fb"))
    add("frontbadge", 2, badge(ref, 0.08, top_fr(), sp["bonnet_x"], 0, fwd, "fb"))
    # rear: 1 on the tailgate under the glass (the reference's place), 2 lower down
    add("rearbadge", 1, badge(ref, 0.075, rear_fr(), 0, sp["tailgate_z"], up, "rb"))
    add("rearbadge", 2, badge(ref, 0.075, rear_fr(), 0, sp["tailgate_low_z"], up, "rb"))
    cap = 0.032
    # name: 1 on the tailgate's right (the reference's place), 2 its left, 3 on both quarters
    add("name", 1, _flat(ref, nm, cap, rear_fr(), -sp["name_y"], sp["name_z"], up, "nm", 0.0012, 0.0035, "name"))
    add("name", 2, _flat(ref, nm, cap, rear_fr(), sp["name_y"], sp["name_z"], up, "nm", 0.0012, 0.0035, "name"))
    q = []
    for sgn in (1, -1):
        q += _flat(ref, nm, cap, side_fr(sgn), sp["quarter_x"], sp["quarter_z"], up, "nm", 0.0012, 0.0035, "name")
    add("name", 3, q)
    return out
