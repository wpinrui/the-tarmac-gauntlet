"""The Jet Black's parts: lamps, grille, intakes, vents, glass, trim, mirrors, exhaust, diffuser,
interior, wheels, badge and name. Every shape and size here is the Jet Black's own, from
rows.json, kept plain and smooth. Runs inside Blender's Python module, called by export.py."""
import json
import re
import sys
from pathlib import Path

import numpy as np
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
    "body": ((0.97, 0.56, 0.05), 0.78, 1.0, None),
    "trim": ((0.02, 0.02, 0.025), 0.7, 1.0, None),
    "grille": ((0.035, 0.035, 0.04), 0.35, 1.0, None),
    "mesh": ((0.06, 0.06, 0.065), 0.25, 1.0, None),
    "glass": ((0.06, 0.08, 0.1), 0.92, 0.45, None),
    "head_glass": ((0.93, 0.95, 0.97), 0.95, 0.2, None),
    "housing": ((0.045, 0.045, 0.05), 0.5, 1.0, None),
    "reflector": ((0.8, 0.81, 0.83), 0.97, 1.0, None),
    "lamp_head": ((0.96, 0.96, 0.93), 0.6, 1.0, (1.0, 0.97, 0.9)),
    "lamp_tail": ((0.48, 0.03, 0.04), 0.5, 1.0, (1.0, 0.06, 0.04)),
    "lamp_brake": ((0.6, 0.04, 0.05), 0.5, 1.0, (1.0, 0.04, 0.03)),
    "chrome": ((0.87, 0.88, 0.9), 0.98, 1.0, None),
    "dark_metal": ((0.15, 0.15, 0.16), 0.8, 1.0, None),
    "badge_red": ((0.83, 0.13, 0.17), 0.6, 1.0, None),
    "badge_black": ((0.08, 0.08, 0.08), 0.6, 1.0, None),
    "interior": ((0.08, 0.08, 0.09), 0.15, 1.0, None),
    "seat": ((0.12, 0.11, 0.11), 0.2, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.2, 0.2, 0.22), 0.85, 1.0, None),
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
    return blend.frame((0, 0, 1.4), (1, 0, 0), (0, 1, 0), (0, 0, -1))


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


def hull(pts):
    return MultiPoint(np.asarray(pts, float)).convex_hull


def smooth_loop(pts, s=0.0004, n=160):
    """A closed outline as one smooth spline through its points."""
    from scipy.interpolate import splev, splprep
    p = np.asarray(pts, float)
    p = geom.resample(np.vstack([p, p[:1]]), 0.01)[:-1]
    tck, _u = splprep([p[:, 0], p[:, 1]], s=s, per=1, k=3)
    x, y = splev(np.linspace(0, 1, n, endpoint=False), tck)
    return poly(np.column_stack([x, y]))


def z_at(x, y):
    """Height of the skin's top at x, y across."""
    h = body.half(body.at(body.CV, x))
    top = h[len(h) // 2:]
    return float(np.interp(abs(y), top[::-1, 0], top[::-1, 1]))


# ---------------------------------------------------------------- lamps
def lamp_shape(key, sgn, out):
    """A lamp's glass outline traced from its measured 3D loop, flattened onto its own plane."""
    tris = np.array(R[key]) * [1, sgn, 1]
    fr, to2 = plane_fr(tris, out)
    g = unary_union([Polygon(to2(t)).buffer(1e-4) for t in tris])
    g = soft(biggest(g.buffer(0.008, join_style=1).buffer(-0.008, join_style=1)), 0.01).simplify(0.0015)
    return fr, to2, g, tris.reshape(-1, 3)


def headlamps(ref, cutters, out, sgn):
    """The lamp glass traced from the reference. Inside, our own: one round projector at the
    lamp's inner end and a thin light strip along its lower edge."""
    side = "l" if sgn > 0 else "r"
    fr, to2, shp, v = lamp_shape("head", sgn, (1, sgn * 0.6, 0.4))
    cutters += blend.lay(ref, f"cut_head_{side}", shp, fr, -0.02, 0.006, "housing", 0.01, along_ray=True)
    inner = shp.buffer(-0.016)
    tip = to2([v[v[:, 0].argmax()]])[0]
    c = inner.exterior.interpolate(inner.exterior.project(Point(tip)))
    c = Point(np.array(c.coords[0]) + (np.array(inner.centroid.coords[0]) - np.array(c.coords[0])) * 0.45)
    lens = c.buffer(0.026, 48).intersection(shp.buffer(-0.008))
    out += blend.lay(ref, f"lamp_head_{side}0", lens, fr, -0.0135, -0.0095, "lamp_head", 0.004, along_ray=True)
    out += blend.lay(ref, f"head_reflector_{side}", c.buffer(0.038, 48).difference(c.buffer(0.027, 48)).intersection(shp.buffer(-0.008)),
                     fr, -0.013, -0.0112, "reflector", 0.004, along_ray=True)
    # the light strip: the lower part of a band just inside the glass, clear of the projector
    b = shp.bounds
    low = box(b[0] - 1, b[1] - 1, b[2] + 1, b[1] + 0.3 * (b[3] - b[1]))
    strip = shp.buffer(-0.007).difference(shp.buffer(-0.015)).intersection(low).difference(c.buffer(0.045, 48))
    if not strip.is_empty:
        out += blend.lay(ref, f"lamp_head_{side}1", biggest(strip), fr, -0.0135, -0.0095, "lamp_head", 0.003, along_ray=True)
    out += blend.lay(ref, f"head_glass_{side}", shp.buffer(-0.003), fr, -0.0045, -0.0015, "head_glass", 0.01, along_ray=True)


def taillamps(ref, cutters, out, sgn):
    """The tail lamp traced from the reference, wrapping round the rear corner. Inside, our own:
    a tail light bar along its top and a brake light bar under it."""
    side = "l" if sgn > 0 else "r"
    fr, _to2, shp, _v = lamp_shape("tail", sgn, (-1, sgn * 0.6, 0.2))
    cutters += blend.lay(ref, f"cut_tail_{side}", shp, fr, -0.016, 0.006, "housing", 0.01, along_ray=True)
    out += blend.lay(ref, f"tail_back_{side}", shp.buffer(-0.002), fr, -0.0125, -0.0105, "housing", 0.008, along_ray=True)
    b = shp.bounds
    mid = b[1] + 0.5 * (b[3] - b[1])
    inner = shp.buffer(-0.009)
    top = inner.intersection(box(b[0] - 1, mid + 0.004, b[2] + 1, b[3] + 1))
    low = inner.intersection(box(b[0] - 1, b[1] - 1, b[2] + 1, mid - 0.004))
    for mat, g in (("lamp_tail", top), ("lamp_brake", low)):
        if not g.is_empty:
            out += blend.lay(ref, f"{mat}_{side}", biggest(g), fr, -0.0104, -0.0075, mat, 0.004, along_ray=True)
    out += blend.lay(ref, f"tail_glass_{side}", shp.buffer(-0.003), fr, -0.0045, -0.0015, "head_glass", 0.01, along_ray=True)


def third_brake(ref, out):
    """A thin brake light across the top of the rear window."""
    x, y0, y1, _z0, _z1 = R["third_brake"]
    g = soft(box(x - 0.012, y0, x + 0.012, y1), 0.008)
    out += blend.lay(ref, "lamp_brake_top", g, top_fr(), 0.0012, 0.0045, "lamp_brake", 0.006)


# ---------------------------------------------------------------- front
def grille_shape():
    """The grille seen from ahead: its measured outline made even and plain."""
    return soft(mirrored(hull(R["grille"])), 0.03)


def front_parts(ref, cutters, out):
    """The grille with one chrome blade across it either side of the middle, the two outer
    intakes and the low slot, each a dark recess with a mesh back."""
    fr = front_fr()
    g = grille_shape()
    cutters += blend.lay(ref, "cut_grille", g, fr, -0.05, 0.006, "grille", 0.012, along_ray=True)
    out += blend.lay(ref, "grille_back", g.buffer(-0.002), fr, -0.045, -0.04, "mesh", 0.012, along_ray=True)
    out += blend.lay(ref, "grille_frame", g.difference(g.buffer(-0.012)), fr, -0.003, 0.002, "chrome", 0.006, along_ray=True)
    bl = hull(R["blade"])
    by0, bz0, by1, bz1 = bl.bounds
    for sgn, side in ((1, "l"), (-1, "r")):
        bar = soft(box(by0, bz0, by1, bz1), 0.008)
        bar = (bar if sgn > 0 else flip(bar)).intersection(g.buffer(-0.006))
        out += blend.lay(ref, f"grille_blade_{side}", bar, fr, -0.0395, -0.008, "chrome", 0.006, along_ray=True)
    i = soft(hull(R["intake"]), 0.02)
    s = soft(mirrored(hull(R["slot"]).buffer(0.006)), 0.012)
    for name, shape, depth in (("intake_l", i, 0.035), ("intake_r", flip(i), 0.035), ("slot", s, 0.03)):
        cutters += blend.lay(ref, f"cut_{name}", shape, fr, -depth, 0.006, "grille", 0.01, along_ray=True)
        out += blend.lay(ref, f"{name}_back", shape.buffer(-0.002), fr, -depth + 0.006, -depth + 0.01, "mesh", 0.01, along_ray=True)


# ---------------------------------------------------------------- glass and trim
GLASS_BAND = 0.016    # black band round the windscreen and the rear window
SIDE_BAND = 0.016     # black band round the side window


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
    k_s = np.polyfit(a[side], c[side], 1) if side.sum() >= 2 else np.array([0.0, w])
    ys = np.linspace(-1.2 * w, 1.2 * w, 61)
    band = Polygon(np.vstack([np.column_stack([np.polyval(k_lo, ys), ys]),
                              np.column_stack([np.polyval(k_hi, ys), ys])[::-1]]))
    us = np.linspace(a.min() - 0.1, a.max() + 0.1, 41)
    sides = Polygon(np.vstack([np.column_stack([us, np.polyval(k_s, us)]),
                               np.column_stack([us, -np.polyval(k_s, us)])[::-1]]))
    h = MultiPoint(np.vstack([p, p * [1, -1]])).convex_hull     # never wider than the measured glass
    return soft(band.intersection(sides).intersection(h), r)


def side_window():
    """The side window seen from the side (x, z), rebuilt from plain fits: a straight level
    bottom edge, a straight steep rear edge, and one even curve over the top down the
    windscreen pillar to the front tip. Corners rounded."""
    p = np.asarray(R["door_glass"], float)
    low = p[p[:, 1] < p[:, 1].min() + 0.03]
    kb = np.polyfit(low[:, 0], low[:, 1], 1)
    rb = low[low[:, 0].argmin()]                   # where the rear edge leaves the bottom
    rt = p[p[:, 0].argmin()]                       # the rear edge's top
    xf = p[:, 0].max()
    top = p[(p[:, 1] > np.polyval(kb, p[:, 0]) + 0.03) & (p[:, 0] > rt[0] + 0.02)]
    pts = sorted([tuple(q) for q in top] + [tuple(rt), (xf, float(np.polyval(kb, xf)))])
    keep = [pts[0]]
    for q in pts[1:]:
        if q[0] - keep[-1][0] > 0.07 or q == pts[-1]:
            keep.append(q)
    kt = geom.monotone(keep)                       # one even curve, no overshoot
    xs = np.linspace(rt[0] - 0.05, xf, 120)
    zt = np.maximum(kt(np.clip(xs, rt[0], xf)), np.polyval(kb, xs))
    g = Polygon(np.vstack([np.column_stack([xs, np.polyval(kb, xs)]), np.column_stack([xs, zt])[::-1]])).buffer(0)
    d = rt - rb
    n = np.array([d[1], -d[0]]); n = n / np.linalg.norm(n) * (1 if n[0] > 0 else -1)    # points forward
    far = 3.0
    ahead = Polygon([rb - d * far, rt + d * far, rt + d * far + n * far, rb - d * far + n * far])
    return soft(biggest(g.intersection(ahead)), 0.02), kb


def side_shapes():
    """In side view: the black opening round the side window, the glass inside it, and the
    window's bottom line."""
    glass, kb = side_window()
    opening = glass.buffer(SIDE_BAND, join_style=1)
    return opening, glass, kb


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


def screen_shapes():
    s = R["screen"]
    span = s["x_base"] - s["x_top"]
    outline = plain_glass(s["outline"], lambda a: a > s["x_base"] - 0.2 * span, lambda a: a < s["x_top"] + 0.12 * span)
    glass = outline.buffer(-GLASS_BAND, join_style=1)
    return outline, glass, outline.difference(glass.buffer(-0.005))


def screen_fr():
    s = R["screen"]
    return slope_fr(s["x_top"], body.z_top(s["x_top"]), s["x_base"], body.z_top(s["x_base"]))


def back_shapes():
    s = R["rear_glass"]
    span = s["x_top"] - s["x_base"]
    outline = plain_glass(s["outline"], lambda a: a < s["x_base"] + 0.2 * span, lambda a: a > s["x_top"] - 0.15 * span, 0.06)
    glass = outline.buffer(-GLASS_BAND, join_style=1)
    return outline, glass, outline.difference(glass.buffer(-0.005))


def back_fr():
    s = R["rear_glass"]
    return slope_fr(s["x_base"], body.z_top(s["x_base"]), s["x_top"], body.z_top(s["x_top"]))


def glass_parts(ref, cutters, out):
    opening, glass, _k = side_shapes()
    for sgn, side in ((1, "l"), (-1, "r")):
        fr, lift = side_frame(sgn)
        trim = opening.difference(glass.buffer(-0.004))
        out += blend.lay(ref, f"window_trim_{side}", lift(trim), fr, 0.0015, 0.0035, "trim", 0.01, along_ray=True)
        g = lift(glass)
        cutters += blend.lay(ref, f"cut_side_{side}", g.buffer(-0.002), fr, -0.08, 0.006, "trim", along_ray=True)
        out += blend.lay(ref, f"side_glass_{side}", g.buffer(-0.005), fr, -0.008, -0.004, "glass", 0.02, along_ray=True)
    for name, shapes, frame in (("screen", screen_shapes(), screen_fr()), ("back", back_shapes(), back_fr())):
        fr, to_fr = frame
        _o, g, t = (to_fr(q) for q in shapes)
        cutters += blend.lay(ref, f"cut_{name}", g.buffer(-0.003), fr, -0.08, 0.006, "trim", along_ray=True)
        out += blend.lay(ref, f"{name}_glass", g.buffer(-0.006), fr, -0.008, -0.004, "glass", 0.02, along_ray=True)
        out += blend.lay(ref, f"{name}_trim", t, fr, 0.0015, 0.0035, "trim", 0.01)


# ---------------------------------------------------------------- vents
VENT_DEPTH = 0.03
VENT_FINS = 2            # chrome fins across each side vent
HOOD_DEPTH = 0.02
HOOD_SLATS = 3           # slats along each bonnet vent


def side_vent():
    return soft(hull(R["vent"]), 0.02)


def side_vents(ref, cutters, out):
    """A dark vent behind each front wheel with chrome fins running across it."""
    g = side_vent()
    x0, z0, x1, z1 = g.bounds
    for sgn, side in ((1, "l"), (-1, "r")):
        fr = side_fr(sgn)
        cutters += blend.lay(ref, f"cut_vent_{side}", g, fr, -VENT_DEPTH, 0.006, "grille", 0.01, along_ray=True)
        out += blend.lay(ref, f"vent_back_{side}", g.buffer(-0.002), fr, -VENT_DEPTH + 0.004, -VENT_DEPTH + 0.008, "mesh", 0.01, along_ray=True)
        for i in range(VENT_FINS):
            z = z0 + (z1 - z0) * (i + 1) / (VENT_FINS + 1)
            fin = LineString([(x0 - 0.3, z + 0.012), (x1 + 0.3, z - 0.004)]).buffer(0.007, cap_style=2)
            fin = fin.intersection(g.buffer(-0.004)).union(fin.intersection(box(x1 - 0.004, 0, x1 + 0.12, 2)))
            out += blend.lay(ref, f"vent_fin_{side}{i}", soft(biggest(fin), 0.003), fr, -VENT_DEPTH + 0.0075, 0.004, "chrome", 0.005, along_ray=True)


def hood_vents(ref, cutters, out):
    """A dark vent each side of the bonnet near the windscreen, with slats along it."""
    g = soft(hull(R["hood_vent"]), 0.02)
    fr = top_fr()
    for sgn, side in ((1, "l"), (-1, "r")):
        q = g if sgn > 0 else sscale(g, 1, -1, origin=(0, 0))
        cutters += blend.lay(ref, f"cut_hood_{side}", q, fr, -HOOD_DEPTH, 0.006, "grille", 0.01, along_ray=True)
        out += blend.lay(ref, f"hood_back_{side}", q.buffer(-0.002), fr, -HOOD_DEPTH + 0.004, -HOOD_DEPTH + 0.008, "mesh", 0.01, along_ray=True)
        y0, y1 = q.bounds[1], q.bounds[3]
        for i in range(HOOD_SLATS):
            y = y0 + (y1 - y0) * (i + 1) / (HOOD_SLATS + 1)
            slat = box(-5, y - 0.006, 5, y + 0.006).intersection(q.buffer(-0.004))
            if not slat.is_empty:
                out += blend.lay(ref, f"hood_slat_{side}{i}", soft(biggest(slat), 0.003), fr, -HOOD_DEPTH + 0.0075, -0.002, "body", 0.005, along_ray=True)


# ---------------------------------------------------------------- shut lines
def door_shape():
    """The door seen from the side: the reference's door panel cut below the window line, with
    the window opening on top of it. Its edges are plain: straight front, round rear."""
    opening, _g, kb = side_shapes()
    d = soft(poly(R["door"]), 0.03)
    xs = np.linspace(-3, 3, 13)
    under = Polygon(np.vstack([np.column_stack([xs, np.polyval(kb, xs) + 0.01]), [[3, -1], [-3, -1]]]))
    return soft(d.intersection(under).union(opening).buffer(0.0005), 0.02)


def door_path():
    """The door's shut line in side view: its outline, open where the window's black band
    covers the top."""
    opening, _g, _k = side_shapes()
    d = np.array(door_shape().exterior.coords)[:-1]
    d = geom.resample(np.vstack([d, d[:1]]), 0.01)[:-1]
    band = opening.buffer(0.004)
    keep = np.array([not band.contains(Point(p)) for p in d])
    k = int(np.argmax(~keep & np.roll(keep, 1)))
    rolled, kr = np.roll(d, -k, axis=0), np.roll(keep, -k)
    path = rolled[kr]
    return path


BONNET_GAP = 0.02     # the bonnet line keeps this far inside the head lamps


def bonnet_path():
    """The bonnet shut line in plan: out of the windscreen's lower corners, forward along the
    measured bonnet edge, round the lamps' inner edge and across the nose above the grille.
    The lid runs under the windscreen, so the line has no rear edge."""
    from scipy.interpolate import splev, splprep
    o, _g, _t = screen_shapes()
    c = np.array(o.exterior.coords)
    corner = c[np.argmax(c[:, 1] + 0.6 * c[:, 0])]
    lamp = unary_union([Polygon(np.array(t)[:, :2]).buffer(1e-4) for t in R["head"]]).convex_hull.buffer(BONNET_GAP)
    b = poly(R["bonnet"]).difference(lamp)
    b = biggest(b)
    xn = b.bounds[2] - 0.005
    edge = lambda x: b.intersection(LineString([(x, 0), (x, 2)])).bounds[3]
    xs = [corner[0] + 0.01, 0.75, 1.1, 1.45, 1.7, 1.9, 2.0]
    half = [(corner[0] + 0.01, corner[1] - 0.01)] + [(x, edge(x)) for x in xs[1:]] + [(xn - 0.03, 0.30), (xn, 0.0)]
    pts = np.array(half)
    pts = np.vstack([pts, pts[-2::-1] * [1, -1]])
    tck, _u = splprep([pts[:, 0], pts[:, 1]], s=0.003, k=3)
    x, y = splev(np.linspace(0, 1, 480), tck)
    q = np.column_stack([x, y])
    q = q[: int(np.argmax(q[:, 1] <= 0))]
    q = np.vstack([q, [[q[-1, 0], 0.0]]])
    return np.vstack([q, (q[-2::-1] * [1, -1])])


def boot_path():
    """The boot lid's shut line in plan, round the measured lid and inside the tail lamps. It
    runs under the rear window, so the line has no front edge."""
    from scipy.interpolate import splev, splprep
    o, _g, _t = back_shapes()
    c = np.array(o.exterior.coords)
    corner = c[np.argmax(c[:, 1] - 0.6 * c[:, 0])]
    lamp = unary_union([Polygon(np.array(t)[:, :2]).buffer(1e-4) for t in R["tail"]]).convex_hull.buffer(0.02)
    b = biggest(poly(R["boot"]).difference(lamp))
    xt = b.bounds[0] + 0.01
    edge = lambda x: b.intersection(LineString([(x, 0), (x, 2)])).bounds[3]
    xs = [-1.75, -1.9, -2.05, -2.15]
    half = [(corner[0] - 0.01, corner[1] - 0.01)] + [(x, edge(x)) for x in xs] + [(xt + 0.03, 0.28), (xt, 0.0)]
    pts = np.array(half)
    pts = np.vstack([pts, pts[-2::-1] * [1, -1]])
    tck, _u = splprep([pts[:, 0], pts[:, 1]], s=0.003, k=3)
    x, y = splev(np.linspace(0, 1, 480), tck)
    q = np.column_stack([x, y])
    q = q[: int(np.argmax(q[:, 1] <= 0))]
    q = np.vstack([q, [[q[-1, 0], 0.0]]])
    return np.vstack([q, (q[-2::-1] * [1, -1])])


def grooves(ref):
    """Shut lines as V grooves to cut: doors, bonnet, boot lid. The reference's flush spoiler in
    the boot lid is left out: the lid is one plain panel (Ivan, 2026-11-11)."""
    obs = []
    path = door_path()
    for sgn, side in ((1, "l"), (-1, "r")):
        obs.append(blend.groove(ref, f"g_door_{side}", path, side_fr(sgn)))
    obs.append(blend.groove(ref, "g_bonnet", bonnet_path(), top_fr()))
    obs.append(blend.groove(ref, "g_boot", boot_path(), top_fr()))
    return obs


# ---------------------------------------------------------------- doors
DOOR_DEPTH = 0.05     # below the cabin floor the door takes the sill this deep
DOOR_WALL = 0.09      # above it, the whole wall: the cabin is hollow behind it
DOOR_GAP = 0.0015     # half the gap round the door, inside the shut line's groove
DOOR_STYLES = {"Out to the side": -65.0}   # open angle
DOOR_PICK = "Out to the side"


def door_outline():
    return door_shape()


def mirror_footprint():
    """The mirror on the door seen from the side, with room: taken only outside the skin."""
    m = R["mirror"]
    return box(m["x_back"] - 0.03, m["z0"] - 0.08, m["x_front"] + 0.04, m["z1"] + 0.03)


def door_hinge(style, sgn=1):
    """The hinge for the left (sgn 1) or right (sgn -1) door: an upright axis along the door's
    front edge, just outside the skin, and the open angle (right hand rule about p0 to p1)."""
    o = door_outline()
    xf = o.bounds[2] + 0.004
    z0, z1 = 0.30, 0.85
    y = max(body.y_at(xf - 0.01, z) for z in np.linspace(z0, z1, 20)) + 0.003
    return (xf, y * sgn, z0), (xf - 0.03, y * sgn, z1), DOOR_STYLES[style] * sgn


def door(ref, shell, obs, floor, sgn=1):
    """Cut the left (sgn 1) or right (sgn -1) door free of the body; the door's window trim,
    glass, card, mirror arm and mirror glass go with it. Returns the door's pieces."""
    side = "l" if sgn > 0 else "r"
    o = door_outline()
    regions = [(o.intersection(box(-5, -1, 5, floor + 0.02)), DOOR_DEPTH),
               (o.intersection(box(-5, floor + 0.02, 5, 5)), DOOR_WALL), (mirror_footprint(), -0.004)]
    roles = {f"window_trim_{side}": "trim", f"side_glass_{side}": "glass", f"mirror_glass_{side}": "mirror",
             f"mirror_housing_{side}": "housing",
             f"mirror_arm_{side}": "arm", f"door_card_{side}": "card"}
    return blend.free_door(ref, side_fr(sgn), regions, DOOR_GAP, shell, [], roles, o.union(mirror_footprint()),
                           name=f"door_{side}")


# ---------------------------------------------------------------- mirrors
MIRROR_H = 0.11      # housing height
MIRROR_W = 0.19      # housing width, out from the arm
MIRROR_D = 0.12      # housing depth front to back
MIRROR_R = 0.04      # corner rounding
MIRROR_SWEEP = 0.12  # radians the housing turns back from square to the car
ARM_X = 0.09         # the arm runs forward from the housing to the door by this much


def _rounded_bar(name, y0, y1, z0, z1, x0, x1, r, sgn, mat="body"):
    face = soft(box(y0, z0, y1, z1), r)
    ring = body.even(np.array(face.exterior.coords)[:-1], 96)
    rings = [np.column_stack([np.full(96, x), ring[:, 0] * sgn, ring[:, 1]]) for x in (x0, x1)]
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def _housing():
    m = R["mirror"]
    zc = m["z1"] - MIRROR_H / 2 - 0.02
    return m["y_out"] - MIRROR_W, m["y_out"], zc - MIRROR_H / 2, zc + MIRROR_H / 2


def _sweep(ob, sgn):
    from mathutils import Matrix
    m = R["mirror"]
    y0, _y1, _z0, _z1 = _housing()
    p = Matrix.Translation((m["x_back"] + MIRROR_D / 2, sgn * y0, 0))
    ob.data.transform(p @ Matrix.Rotation(sgn * MIRROR_SWEEP, 4, "Z") @ p.inverted())
    return ob


def _arm(name, sgn, xa, za, y_end):
    """A slim body coloured stalk from under the housing, forward and down into the door."""
    x1 = xa + ARM_X
    z1 = belt(x1) - 0.04
    root = body.y_at(x1, z1) - 0.02
    face = soft(box(-0.02, -0.01, 0.02, 0.01), 0.008)
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
    """Body coloured housings, each its own part on the end of its stalk."""
    m = R["mirror"]
    y0, y1, z0, z1 = _housing()
    xb = m["x_back"]
    return [_sweep(_rounded_bar(f"mirror_housing_{side}", y0, y1, z0, z1, xb, xb + MIRROR_D, MIRROR_R, sgn), sgn)
            for sgn, side in ((1, "l"), (-1, "r"))]


def mirror_arms():
    m = R["mirror"]
    y0, _y1, z0, _z1 = _housing()
    return [_arm(f"mirror_arm_{side}", sgn, m["x_back"] + MIRROR_D / 2, z0 + 0.014, y0 + 0.05) for sgn, side in ((1, "l"), (-1, "r"))]


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


# ---------------------------------------------------------------- rear
def _tail_x(y, z):
    """The rearmost x where the skin reaches out to (y, z)."""
    for x in np.arange(TAIL, TAIL + 0.8, 0.004):
        if body.y_at(x, z) >= abs(y):
            return float(x)
    return TAIL


def tip_shape():
    """One exhaust tip seen from behind, the left one: the measured twin outline, plain."""
    return soft(hull(R["exhaust"]), 0.014)


TIP_PROUD = 0.005    # each tip stands this far out of the skin at its middle
TIP_LEN = 0.30       # and runs this far into the body


def exhaust():
    """A wide oval tip low at each rear corner, a chrome tube with a dark inside."""
    tip = tip_shape()
    cut, pipes, holes = [], [], []
    for sgn, side in ((1, "l"), (-1, "r")):
        t = tip if sgn > 0 else flip(tip)
        cy, cz = np.array(t.centroid.coords[0])
        x_end = _tail_x(cy, cz) - TIP_PROUD
        hole = t.buffer(0.001)
        ring = body.even(np.array(hole.exterior.coords)[:-1], 128)
        rings = [np.column_stack([np.full(128, x), ring[:, 0], ring[:, 1]]) for x in (x_end - 0.05, x_end + TIP_LEN)]
        v, f = geom.close_rings(rings)
        cut.append(blend.make(f"cut_exhaust_{side}", v, f, "trim"))
        holes.append(hole)
        outer = body.even(np.array(t.buffer(-0.001).exterior.coords)[:-1], 96)
        inner = body.even(np.array(t.buffer(-0.006).exterior.coords)[:-1], 96)
        rings = []
        for x, rg in ((x_end, inner), (x_end, outer), (x_end + TIP_LEN, outer), (x_end + TIP_LEN, inner)):
            rings.append(np.column_stack([np.full(len(rg), x), rg[:, 0], rg[:, 1]]))
        v, f = geom.close_rings(rings, cap=False)
        m = len(rings[0])
        for j in range(m):
            k = (j + 1) % m
            f.append((3 * m + j, 3 * m + k, k, j))
        pipes.append(blend.make(f"exhaust_{side}", v, f, "chrome"))
        rg = body.even(np.array(t.buffer(-0.0065).exterior.coords)[:-1], 96)
        rings = [np.column_stack([np.full(96, x), rg[:, 0], rg[:, 1]]) for x in (x_end + 0.05, x_end + 0.06)]
        v, f = geom.close_rings(rings)
        pipes.append(blend.make(f"exhaust_inner_{side}", v, f, "trim"))
    return cut, pipes, unary_union(holes)


DIFF_FINS = 4


def rear_parts(ref, out, pocket):
    """A dark diffuser across the bottom of the tail between the exhausts, with upright fins."""
    fr = rear_fr()
    t = tip_shape()
    y1 = t.bounds[0] - 0.03
    z1 = t.bounds[3] + 0.01
    d = soft(box(-y1, 0.0, y1, z1), 0.02).difference(pocket.buffer(0.004))
    out += blend.lay(ref, "diffuser", d, fr, 0.002, 0.0045, "grille", 0.008, along_ray=True)
    for i, y in enumerate(np.linspace(-0.8 * y1, 0.8 * y1, DIFF_FINS)):
        fin = box(y - 0.006, 0.0, y + 0.006, z1 - 0.03).intersection(d.buffer(-0.01))
        out += blend.lay(ref, f"diffuser_fin_{i}", fin, fr, 0.0044, 0.05, "grille", 0.006, along_ray=True)
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
    e = min(0.015, (x1 - x0) / 4)       # the rounded edge, never past the middle of a thin box
    for i, x in enumerate((x0, x0 + e, x1 - e, x1)):
        k = 0.92 if i in (0, 3) else 1.0
        cy, cz = (y0 + y1) / 2, (z0 + z1) / 2
        rr = np.column_stack([cy + (ring[:, 0] - cy) * k, cz + (ring[:, 1] - cz) * k])
        rings.append(np.column_stack([x - lean * (rr[:, 1] - z0), rr]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


CABIN_FRONT = 0.38      # the firewall, under the windscreen's base
CABIN_BACK = -1.12      # the bulkhead behind the seats, under the rear window
SEAT_Y = 0.33           # seat centres out from the middle; the driver sits on the left


def steering_wheel(x, y, z, tilt):
    """A round wheel on the left, on a short column out of the dash."""
    from mathutils import Matrix
    prof = [(0.17, -0.014), (0.184, 0.0), (0.17, 0.014), (0.156, 0.0)]
    v, f = geom.lathe(prof, 64, axis="y")
    rim = blend.make("steering_wheel", v, f, "interior")
    parts = [rim]
    for a in (0.0, 2.2, -2.2):
        hub = _rbox("spoke", -0.008, 0.008, -0.012, 0.012, 0.0, 0.16, r=0.006)
        hub.data.transform(Matrix.Rotation(a, 4, "X") @ Matrix.Rotation(np.pi / 2, 4, "Z"))
        parts.append(hub)
    col = _rbox("column", -0.03, 0.0, -0.025, 0.025, -0.025, 0.025, r=0.01)
    col.data.transform(Matrix.Translation((0, -0.0, 0)) @ Matrix.Rotation(np.pi / 2, 4, "Z") @ Matrix.Scale(9, 4, (1, 0, 0)))
    parts.append(col)
    wheel = blend.union_all(parts, "steering_wheel")
    wheel.data.transform(Matrix.Translation((x, y, z)) @ Matrix.Rotation(tilt, 4, "Y") @ Matrix.Rotation(np.pi / 2, 4, "Z"))
    return wheel


def interior(floor):
    x0 = CABIN_FRONT
    dash_top = 0.80
    obs = [_loft("firewall", x0 - 0.010, x0 - 0.004, lambda x: box(-0.6, floor + 0.002, 0.6, 3), n=2),
           _loft("dash", x0 - 0.0125, 0.10, lambda x: box(-0.6, 0.55, 0.6, dash_top + 0.06 * (1 - (x0 - x) / 0.3)), n=10)]
    belt_z = belt(-0.4)
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, floor + 0.08, 1.2, belt_z - 0.02) if s > 0 else box(-1.2, floor + 0.08, -0.3, belt_z - 0.02)).intersection(
            body.inside(x, 0.035 + 0.003)).difference(body.inside(x, 0.035 + 0.025))
        obs.append(_loft(f"door_card_{side}", -0.85, 0.05, clip, n=14))
        yc = SEAT_Y * sgn
        obs.append(_rbox(f"seat_base_{side}", -0.78, -0.30, yc - 0.22, yc + 0.22, floor + 0.002, 0.36, mat="seat"))
        obs.append(_rbox(f"seat_back_{side}", -0.90, -0.78, yc - 0.21, yc + 0.21, 0.361, 0.95, lean=0.22, mat="seat"))
    obs.append(steering_wheel(0.02, SEAT_Y, 0.74, 0.35))
    xm = R["screen"]["x_top"] + 0.06
    roof_in = body.inside(xm, 0.035).bounds[3]
    top = roof_in - 0.06
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.12, 0.12, top - 0.055, top, r=0.02)
    stem = _rbox("stem", xm - 0.016, xm - 0.004, -0.012, 0.012, top - 0.015, roof_in - 0.0008, r=0.004)
    obs.append(blend.union_all([m, stem], "mirror_rear"))
    return obs


# ---------------------------------------------------------------- wheels
SPOKES = 10


def wheels():
    """Ten straight slim spokes in dark grey, 19 inch at the front and 20 at the back."""
    obs = []
    for x, fb, wr, width, rr in ((W["front_x"], "f", W["r_f"], W["width_f"], W["rim_f"]),
                                 (W["rear_x"], "r", W["r_r"], W["width_r"], W["rim_r"])):
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
            v, f = geom.disc((0, hw - 0.05, 0), (0, 1, 0), 0.065, 0.05, n=48)
            parts.append(blend.make("hub", place(v), f, "rim"))
            for k in range(SPOKES):
                a = 2 * np.pi * k / SPOKES
                ca, sa = np.cos(a), np.sin(a)
                pts = []
                for r, w2, h0, h1 in ((0.055, 0.012, hw - 0.072, hw - 0.028), (rr - 0.018, 0.009, hw - 0.06, hw - 0.02)):
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


BADGE_T = 0.008    # a badge stands proud of its panel by this much
NAME_T = 0.004     # the name's letters stand proud by this much


def badge(ref, style, size, fr, a, b, up, name, depth, lift=0.0):
    if style == "chrome":
        return _flat(ref, badge_chrome(), size, fr, a, b, up, name, 0.0012 + lift, depth + lift, "chrome", 0.003)
    obs = []
    for i, (g, m) in enumerate(badge_colour()):
        obs += _flat(ref, g, size, fr, a, b, up, f"{name}_{i}", 0.0012 + lift, depth + lift, m, 0.003)
    return obs


def grille_badge(style, size):
    """The badge standing in the middle of the grille on a dark round boss, as the reference's
    star does: raised off the grille's back on its own, not laid on the body."""
    x = NOSE - 0.045
    _x, _y, z, _r = R["badge_front"]
    v, f = geom.disc((x + 0.012, 0, z), (1, 0, 0), size * 0.62, 0.03, n=64)
    obs = [blend.make("fb_boss", v, f, "trim")]
    shapes = [(badge_chrome(), "chrome")] if style == "chrome" else badge_colour()
    for i, (g, m) in enumerate(shapes):
        g = sscale(g, size, size, origin=(0, 0))
        for k, q in enumerate(getattr(g, "geoms", [g])):
            pts, tris = blend.triangulate(q, 0.003)
            n = len(pts)
            x0, x1 = x + 0.027, x + 0.031
            verts = np.vstack([np.column_stack([np.full(n, x0), pts[:, 0], z + pts[:, 1]]),
                               np.column_stack([np.full(n, x1), pts[:, 0], z + pts[:, 1]])])
            count = {}
            for t in tris:
                for e in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
                    count[tuple(sorted(e))] = count.get(tuple(sorted(e)), 0) + 1
            faces = [(t[2], t[1], t[0]) for t in tris] + [(t[0] + n, t[1] + n, t[2] + n) for t in tris]
            for t in tris:
                for a, b in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
                    if count[tuple(sorted((a, b)))] == 1:
                        faces.append((a, b, b + n, a + n))
            obs.append(blend.make(f"fb_{i}_{k}", verts, faces, m))
    return obs


def options(ref):
    """Badge and name options, named opt_<group>_<n>_<i>. Badges and names lie flat on their
    panels. Odd badge numbers are chrome, even the colour badge, at the same spot."""
    nm = name_shape()
    up = (0, 0, 1)
    out = []

    def add(group, n, objs):
        for i, ob in enumerate(objs):
            ob.name = f"opt_{group}_{n}_{i}"
            out.append(ob)
    _x, _y, sz, _r = R["nose_star"]
    for k, style in ((0, "chrome"), (1, "colour")):
        # front: in the middle of the grille (the reference), or on the nose above it
        add("frontbadge", 1 + k, grille_badge(style, 0.12))
        add("frontbadge", 3 + k, badge(ref, style, 0.05, front_fr(), 0, sz - 0.01, up, "fb", BADGE_T))
    # rear: on the boot lid's tail between the lamps (the reference)
    _x, _y, rz, _r = R["badge_rear"]
    for k, style in ((0, "chrome"), (1, "colour")):
        add("rearbadge", 1 + k, badge(ref, style, 0.085, rear_fr(), 0, rz, up, "rb", BADGE_T))
    cap = 0.026
    # name: left or right of the rear badge (the reference has a word each side), under it,
    # or on the front wing above the side vent
    add("name", 1, _flat(ref, nm, cap, rear_fr(), -0.36, rz, up, "nm", 0.0012, NAME_T, "chrome"))
    add("name", 2, _flat(ref, nm, cap, rear_fr(), 0.36, rz, up, "nm", 0.0012, NAME_T, "chrome"))
    add("name", 3, _flat(ref, nm, cap, rear_fr(), 0, rz - 0.065, up, "nm", 0.0012, NAME_T, "chrome"))
    v = side_vent().bounds
    side = []
    for sgn in (1, -1):
        side += _flat(ref, nm, cap, side_fr(sgn), (v[0] + v[2]) / 2, v[3] + 0.05, up, "nm", 0.0012, NAME_T, "chrome")
    add("name", 4, side)
    return out

