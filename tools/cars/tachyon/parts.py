"""The Tachyon's parts: lamps, intakes, glass, trim, mirrors, exhaust, diffuser, airbrake,
interior, wheels, badge and name. Every shape and size here is the Tachyon's own, from rows.json,
kept plain and smooth. Runs inside Blender's Python module, called by export.py."""
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
    "grille": ((0.04, 0.04, 0.045), 0.3, 1.0, None),
    "glass": ((0.07, 0.09, 0.11), 0.92, 0.45, None),
    "head_glass": ((0.92, 0.94, 0.96), 0.95, 0.22, None),
    "housing": ((0.05, 0.05, 0.055), 0.5, 1.0, None),
    "reflector": ((0.78, 0.79, 0.81), 0.97, 1.0, None),
    "lamp_head": ((0.95, 0.95, 0.92), 0.6, 1.0, (1.0, 0.97, 0.9)),
    "lamp_tail": ((0.5, 0.03, 0.04), 0.5, 1.0, (1.0, 0.06, 0.04)),
    "lamp_brake": ((0.6, 0.04, 0.05), 0.5, 1.0, (1.0, 0.04, 0.03)),
    "chrome": ((0.86, 0.87, 0.89), 0.98, 1.0, None),
    "dark_metal": ((0.16, 0.16, 0.17), 0.8, 1.0, None),
    "engine_metal": ((0.42, 0.43, 0.45), 0.85, 1.0, None),
    "badge_red": ((0.83, 0.13, 0.17), 0.6, 1.0, None),
    "badge_black": ((0.08, 0.08, 0.08), 0.6, 1.0, None),
    "interior": ((0.09, 0.09, 0.1), 0.15, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.32, 0.33, 0.35), 0.85, 1.0, None),
}
LAMPS = {"Headlights": ["lamp_head"], "Tail lights": ["lamp_tail"], "Brake lights": ["lamp_brake"]}
GROUPS = {"frontbadge": "Front badge", "rearbadge": "Rear badge", "name": "Name", "airbrake": "Airbrake"}


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


def z_at(x, y):
    """Height of the skin's top at x, y across."""
    h = body.half(body.at(body.CV, x))
    top = h[len(h) // 2:]
    return float(np.interp(abs(y), top[::-1, 0], top[::-1, 1]))


# ---------------------------------------------------------------- head lamps
def head_shape(sgn):
    tris = np.array(R["head"]) * [1, sgn, 1]
    fr, to2 = plane_fr(tris, (1, sgn, 0.3))
    g = unary_union([Polygon(to2(t)).buffer(1e-4) for t in tris])
    g = soft(biggest(g.buffer(0.01, join_style=1).buffer(-0.01, join_style=1)), 0.012).simplify(0.002)
    return fr, to2, g, tris.reshape(-1, 3)


def headlamps(ref, cutters, out, sgn):
    """The lamp glass traced from the reference. Inside, our own: two round projectors in a row
    along the lamp's lower edge, and a light blade along its upper edge."""
    side = "l" if sgn > 0 else "r"
    fr, to2, shp, v = head_shape(sgn)
    cutters += blend.lay(ref, f"cut_head_{side}", shp, fr, -0.018, 0.006, "housing", 0.01, along_ray=True)
    inner = shp.buffer(-0.022)
    front = to2([v[v[:, 0].argmax()]])[0]
    back = to2([v[v[:, 0].argmin()]])[0]
    ring = inner.exterior
    rings = []
    for i, t in enumerate((0.22, 0.48)):
        p = Point(front + (back - front) * t)
        c = ring.interpolate(ring.project(p))
        c = Point(np.array(c.coords[0]) + (np.array(inner.centroid.coords[0]) - np.array(c.coords[0])) * 0.25)
        lens = c.buffer(0.021, 48).intersection(shp.buffer(-0.008))
        out += blend.lay(ref, f"lamp_head_{side}{i}", lens, fr, -0.0125, -0.009, "lamp_head", 0.004, along_ray=True)
        out += blend.lay(ref, f"head_reflector_{side}{i}", c.buffer(0.031, 48).difference(c.buffer(0.022, 48)).intersection(shp.buffer(-0.008)),
                         fr, -0.0122, -0.0105, "reflector", 0.004, along_ray=True)
        rings.append(c.buffer(0.036, 48))
    blade = shp.buffer(-0.008).difference(shp.buffer(-0.018)).difference(unary_union(rings))
    top = LineString([front, back]).parallel_offset(0.5, "left")
    keep = blade.intersection(unary_union([top.buffer(0.49, cap_style=2)]))
    if not keep.is_empty:
        out += blend.lay(ref, f"lamp_head_{side}2", biggest(keep), fr, -0.0125, -0.009, "lamp_head", 0.004, along_ray=True)
    out += blend.lay(ref, f"head_glass_{side}", shp.buffer(-0.003), fr, -0.0045, -0.0015, "head_glass", 0.01, along_ray=True)


# ---------------------------------------------------------------- front
def intake_shape():
    """The lower intake seen from ahead: the two side mouths and the low slot between them,
    made even and rounded."""
    return soft(mirrored(poly(R["intake"])), 0.02)


def front_parts(ref, cutters, out):
    fr = front_fr()
    g = intake_shape()
    cutters += blend.lay(ref, "cut_intake", g, fr, -0.045, 0.006, "grille", 0.012, along_ray=True)
    out += blend.lay(ref, "intake_back", g.buffer(-0.002), fr, -0.039, -0.035, "grille", 0.012, along_ray=True)
    for sgn, side in ((1, "l"), (-1, "r")):
        # one blade across each side mouth, level with the slot's top
        pts = [(0.40, 0.305), (0.56, 0.315), (0.70, 0.33), (0.78, 0.34)]
        line = LineString(pts).buffer(0.008, cap_style=2)
        bar = sscale(line, sgn, 1, origin=(0, 0)).intersection(g.buffer(-0.008))
        if not bar.is_empty:
            out += blend.lay(ref, f"intake_blade_{side}", bar, fr, -0.0345, -0.010, "dark_metal", 0.006, along_ray=True)


# ---------------------------------------------------------------- glass and trim
GLASS_BAND = 0.014    # black band round the windscreen and the engine glass


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


def pane(pts):
    """A side window outline (x, z): one straight line along its bottom, the rest one smooth
    curve, corners rounded."""
    p = np.asarray(pts, float)
    i0, i1 = p[:, 0].argmin(), p[:, 0].argmax()
    k = np.polyfit(p[[i0, i1], 0], p[[i0, i1], 1], 1)
    low = p[p[:, 1] <= np.polyval(k, p[:, 0]) + 0.015]
    k = np.polyfit(low[:, 0], low[:, 1], 1)
    g = smooth_loop(p, 0.0006)
    under = Polygon([(p[:, 0].min() - 1, np.polyval(k, p[:, 0].min() - 1)), (p[:, 0].max() + 1, np.polyval(k, p[:, 0].max() + 1)),
                     (p[:, 0].max() + 1, 5), (p[:, 0].min() - 1, 5)])
    full = g.union(Polygon(np.vstack([p, p[:1]])).buffer(0)).intersection(under)
    return soft(biggest(full), 0.012), k


QUARTER_GAP = 0.04     # black pillar between the door glass and the quarter glass


def side_shapes():
    """In side view (x, z): the black opening round both side windows, the door glass and the
    quarter glass filling the opening behind it, and the door glass's bottom line."""
    door, k = pane(R["door_glass"])
    quarter, _k = pane(R["quarter_glass"])
    opening = soft(door.union(quarter).buffer(0.05, join_style=1).buffer(-0.05, join_style=1), 0.02)
    opening = opening.union(soft(unary_union([door, quarter]).convex_hull.intersection(
        box(-0.65, 0.88, -0.15, 1.2)), 0.02)).buffer(0.002)
    inner = opening.buffer(-0.014, join_style=1)
    x0 = door.bounds[0]
    front = soft(inner.intersection(door.buffer(-0.012, join_style=1)), 0.01)
    back = soft(biggest(inner.intersection(box(-5, 0, x0 - QUARTER_GAP, 5))), 0.015)
    return opening, [front, back], k


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


ENGINE_FRAME = 0.055     # the black frame round the engine glass


def engine_shapes():
    # the measured glass is already four straight edges: kept as it is, made even
    g = poly(R["engine_glass"]).intersection(box(-5, 0, 5, 5))
    outline = soft(g.union(sscale(g, 1, -1, origin=(0, 0))).buffer(1e-4), 0.04)
    glass = outline.buffer(-GLASS_BAND, join_style=1)
    return outline, glass, outline.buffer(ENGINE_FRAME, join_style=1).difference(glass.buffer(-0.005))


def engine_fr():
    e = np.array(R["engine_glass"])
    x0, x1 = e[:, 0].min() - ENGINE_FRAME, e[:, 0].max() + ENGINE_FRAME
    return slope_fr(x0, body.z_top(x0), x1, body.z_top(x1))


def glass_parts(ref, cutters, out):
    opening, panes, _k = side_shapes()
    for sgn, side in ((1, "l"), (-1, "r")):
        fr, lift = side_frame(sgn)
        trim = lift(opening.difference(unary_union([p.buffer(-0.004) for p in panes])))
        out += blend.lay(ref, f"window_trim_{side}", trim, fr, 0.0015, 0.0035, "trim", 0.01, along_ray=True)
        for i, p in enumerate(panes):
            g = lift(p)
            cutters += blend.lay(ref, f"cut_side_{side}{i}", g.buffer(-0.002), fr, -0.08, 0.006, "trim", along_ray=True)
            out += blend.lay(ref, f"side_glass_{side}{i}", g.buffer(-0.005), fr, -0.008, -0.004, "glass", 0.02, along_ray=True)
    for name, shapes, frame in (("screen", screen_shapes(), screen_fr()), ("engine", engine_shapes(), engine_fr())):
        fr, to_fr = frame
        _o, g, t = (to_fr(q) for q in shapes)
        cutters += blend.lay(ref, f"cut_{name}", g.buffer(-0.003), fr, -0.08, 0.006, "trim", along_ray=True)
        out += blend.lay(ref, f"{name}_glass", g.buffer(-0.006), fr, -0.008, -0.004, "glass", 0.02, along_ray=True)
        out += blend.lay(ref, f"{name}_trim", t, fr, 0.0015, 0.0035, "trim", 0.01)


LOUVRE_X1 = -1.72    # the black louvre panel runs from the engine glass back to here
LOUVRES = 5


def louvres(ref, out):
    """A black panel behind the engine glass with slats across it."""
    o, _g, _t = engine_shapes()
    b = o.bounds
    x0 = b[0] - ENGINE_FRAME + 0.004
    w = (b[3] - b[1]) / 2 - 0.02
    panel = soft(box(LOUVRE_X1, -w, x0, w), 0.03).union(box(x0 - 0.02, -w, x0 + 0.02, w)).difference(o.buffer(ENGINE_FRAME + 0.002))
    out += blend.lay(ref, "louvre_panel", panel, top_fr(), 0.0015, 0.0035, "grille", 0.01)
    for i, x in enumerate(np.linspace(LOUVRE_X1 + 0.04, x0 - 0.05, LOUVRES)):
        slat = soft(box(x - 0.011, -w + 0.03, x + 0.011, w - 0.03), 0.008)
        out += blend.lay(ref, f"louvre_{i}", slat, top_fr(), 0.0034, 0.009, "trim", 0.008)


# ---------------------------------------------------------------- side intake
INTAKE_DEPTH = 0.03


def side_intake():
    return soft(smooth_loop(R["side_intake"], 0.0006), 0.02)


def side_intake_cut(ref):
    g = side_intake()
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        obs += blend.lay(ref, f"cut_sideintake_{side}", g, side_fr(sgn), -INTAKE_DEPTH, 0.006, "grille", 0.01, along_ray=True)
    return obs


# ---------------------------------------------------------------- shut lines
def door_path():
    """The door's outline, open where it runs under the window. Its front end runs into the
    window's black band at the nearest point; its rear end carries straight on up its rear
    edge into the band."""
    opening, _p, _k = side_shapes()
    d = np.array(soft(poly(R["door"]), 0.03).exterior.coords)[:-1]
    d = geom.resample(np.vstack([d, d[:1]]), 0.01)[:-1]
    near = opening.buffer(0.03).union(box(-5, 0.70, 0.2, 5))      # the notched top rear corner goes
    keep = np.array([not near.contains(Point(p)) for p in d])
    k = int(np.argmax(~keep & np.roll(keep, 1)))
    rolled, kr = np.roll(d, -k, axis=0), np.roll(keep, -k)
    path = rolled[kr]
    band = opening.buffer(-0.007)

    def into(p):
        q = np.array(opening.exterior.interpolate(opening.exterior.project(Point(p))).coords[0])
        return (q + np.array([0, 0.015])).tolist()

    def carry(end, prev):
        t = (end - prev) / np.linalg.norm(end - prev)
        for s in np.arange(0.01, 0.4, 0.005):
            q = end + t * s
            if band.contains(Point(q)):
                return [(end + t * x).tolist() for x in np.arange(0.02, s, 0.02)] + [q.tolist()]
        return [into(end)]
    a, b = path[0], path[-1]
    head = carry(a, path[min(8, len(path) - 1)]) if a[0] < b[0] else [into(a)]
    tail = carry(b, path[max(-9, -len(path))]) if b[0] < a[0] else [into(b)]
    return head[::-1] + path.tolist() + tail


BONNET_GAP = 0.035     # the bonnet line keeps this far inside the head lamps


def bonnet_path():
    """The bonnet shut line in plan as one plain U: out of the windscreen's lower corner,
    straight forward along the wing, round the lamps' inner edge and across the nose. The lid
    runs under the windscreen, so the line has no rear edge."""
    from scipy.interpolate import splev, splprep
    o, _g, _t = screen_shapes()
    c = np.array(o.exterior.coords)
    corner = c[np.argmax(c[:, 1] + 0.6 * c[:, 0])]
    h = np.array(R["head"]).reshape(-1, 3)
    lamp = Polygon(h[:, :2]).convex_hull.buffer(BONNET_GAP)
    inner = lambda x: lamp.intersection(LineString([(x, 0), (x, 2)])).bounds[1]
    xn = poly(R["bonnet"]).bounds[2] - 0.01
    y0 = corner[1] - 0.015
    half = np.array([(corner[0] + 0.01, y0), (1.55, y0 - 0.01), (1.85, min(y0 - 0.03, inner(1.85))),
                     (2.05, inner(2.05)), (xn - 0.05, 0.30), (xn, 0.0)])
    pts = np.vstack([half, half[-2::-1] * [1, -1]])
    tck, _u = splprep([pts[:, 0], pts[:, 1]], s=0.004, k=3)
    x, y = splev(np.linspace(0, 1, 480), tck)
    q = np.column_stack([x, y])
    q = q[: int(np.argmax(q[:, 1] <= 0))]
    q = np.vstack([q, [[q[-1, 0], 0.0]]])
    return np.vstack([q, (q[-2::-1] * [1, -1])])


ENGINE_LID_Y = 0.47    # the engine cover's shut line runs this far out


def engine_loop():
    """The engine cover in plan, round the engine glass and the louvres, ahead of the airbrake."""
    x1 = R["airbrake"]["front"][0] + 0.03
    e = soft(poly(R["engine_cover"]).intersection(box(x1, -ENGINE_LID_Y, 0, ENGINE_LID_Y)), 0.06)
    return np.array(e.simplify(0.002).exterior.coords)[:-1]


def grooves(ref):
    """Shut lines as V grooves to cut: doors, bonnet, engine cover."""
    obs = []
    path = door_path()
    for sgn, side in ((1, "l"), (-1, "r")):
        obs.append(blend.groove(ref, f"g_door_{side}", path, side_fr(sgn)))
    obs.append(blend.groove(ref, "g_bonnet", bonnet_path(), top_fr()))
    obs.append(blend.groove(ref, "g_engine", engine_loop(), top_fr(), closed=True))
    return obs


# ---------------------------------------------------------------- mirrors
MIRROR_H = 0.10      # housing height
MIRROR_W = 0.16      # housing width, out from the arm
MIRROR_D = 0.08      # housing depth front to back
MIRROR_R = 0.035     # corner rounding
MIRROR_SWEEP = 0.15  # radians the housing turns back from square to the car
ARM_X = 0.10         # the arm runs forward from the housing to the door by this much


def _rounded_bar(name, y0, y1, z0, z1, x0, x1, r, sgn, mat="body"):
    face = soft(box(y0, z0, y1, z1), r)
    ring = body.even(np.array(face.exterior.coords)[:-1], 96)
    rings = [np.column_stack([np.full(96, x), ring[:, 0] * sgn, ring[:, 1]]) for x in (x0, x1)]
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def _housing():
    m = R["mirror"]
    zc = m["z1"] - MIRROR_H / 2
    return m["y_out"] - MIRROR_W, m["y_out"], zc - MIRROR_H / 2, zc + MIRROR_H / 2


def _sweep(ob, sgn):
    from mathutils import Matrix
    m = R["mirror"]
    y0, _y1, _z0, _z1 = _housing()
    p = Matrix.Translation((m["x_back"] + MIRROR_D / 2, sgn * y0, 0))
    ob.data.transform(p @ Matrix.Rotation(sgn * MIRROR_SWEEP, 4, "Z") @ p.inverted())
    return ob


def _arm(name, sgn, xa, za, y_end):
    """A slim black stalk from under the housing, forward and down into the door."""
    x1 = xa + ARM_X
    z1 = belt(x1) - 0.05
    root = body.y_at(x1, z1) - 0.02
    face = soft(box(-0.016, -0.009, 0.016, 0.009), 0.007)
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
    return blend.make(name, v, f, "trim")


def mirrors():
    """Body coloured housings. Their stalks are separate black parts."""
    m = R["mirror"]
    y0, y1, z0, z1 = _housing()
    xb = m["x_back"]
    return [_sweep(_rounded_bar(f"mirror_{side}", y0, y1, z0, z1, xb, xb + MIRROR_D, MIRROR_R, sgn), sgn)
            for sgn, side in ((1, "l"), (-1, "r"))]


def mirror_arms():
    m = R["mirror"]
    y0, _y1, z0, _z1 = _housing()
    return [_arm(f"mirror_arm_{side}", sgn, m["x_back"] + MIRROR_D / 2, z0 + 0.012, y0 + 0.05) for sgn, side in ((1, "l"), (-1, "r"))]


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
    for x in np.arange(TAIL, TAIL + 0.6, 0.004):
        if body.y_at(x, z) >= abs(y):
            return float(x)
    return TAIL


def tip_shape():
    """One exhaust tip seen from behind, the left one."""
    return soft(smooth_loop(R["exhaust"][0], 0.0003), 0.015)


TIP_PROUD = 0.0      # each tip ends level with the rearmost skin round it
TIP_LEN = 0.30       # and runs this far into the body


def exhaust():
    """Two tips side by side high in the middle of the tail, standing out of the black band,
    each a chrome tube with a dark inside."""
    tip = tip_shape()
    cut, pipes, holes = [], [], []
    for sgn, side in ((1, "l"), (-1, "r")):
        t = tip if sgn > 0 else flip(tip)
        x_end = min(_tail_x(y, z) for y, z in np.array(t.exterior.coords)) - TIP_PROUD
        hole = t.buffer(0.001)
        ring = body.even(np.array(hole.exterior.coords)[:-1], 128)
        rings = [np.column_stack([np.full(128, x), ring[:, 0], ring[:, 1]]) for x in (x_end - 0.05, x_end + TIP_LEN)]
        v, f = geom.close_rings(rings)
        cut.append(blend.make(f"cut_exhaust_{side}", v, f, "trim"))
        holes.append(hole)
        outer = body.even(np.array(t.buffer(-0.001).exterior.coords)[:-1], 96)
        inner = body.even(np.array(t.buffer(-0.007).exterior.coords)[:-1], 96)
        rings = []
        for x, rg in ((x_end, inner), (x_end, outer), (x_end + TIP_LEN, outer), (x_end + TIP_LEN, inner)):
            rings.append(np.column_stack([np.full(len(rg), x), rg[:, 0], rg[:, 1]]))
        v, f = geom.close_rings(rings, cap=False)
        m = len(rings[0])
        for j in range(m):
            k = (j + 1) % m
            f.append((3 * m + j, 3 * m + k, k, j))
        pipes.append(blend.make(f"exhaust_{side}", v, f, "chrome"))
        rg = body.even(np.array(t.buffer(-0.0075).exterior.coords)[:-1], 96)
        rings = [np.column_stack([np.full(96, x), rg[:, 0], rg[:, 1]]) for x in (x_end + 0.06, x_end + 0.07)]
        v, f = geom.close_rings(rings)
        pipes.append(blend.make(f"exhaust_inner_{side}", v, f, "trim"))
    return cut, pipes, unary_union(holes)


def rear_parts(ref, out, pocket):
    """The black band across the tail with the tail lamp slats on it, and the diffuser."""
    fr = rear_fr()
    band = soft(mirrored(poly(R["rear_band"])), 0.015).difference(pocket.buffer(0.001))
    out += blend.lay(ref, "rear_band", band, fr, 0.0015, 0.0035, "grille", 0.008, along_ray=True)
    for i, (s, mat) in enumerate(zip(R["tail_slats"], ("lamp_brake", "lamp_tail"))):
        g = soft(poly(s), 0.004)
        for sgn, side in ((1, "l"), (-1, "r")):
            q = g if sgn > 0 else flip(g)
            out += blend.lay(ref, f"{mat}_{side}{i}", q, fr, 0.0034, 0.0075, mat, 0.006, along_ray=True)
    d = soft(mirrored(poly(R["diffuser"])), 0.02)
    out += blend.lay(ref, "diffuser", d, fr, 0.002, 0.0045, "grille", 0.008, along_ray=True)
    for i, y in enumerate((-0.36, -0.12, 0.12, 0.36)):
        fin = box(y - 0.006, 0.235, y + 0.006, 0.40).intersection(d.buffer(-0.01))
        out += blend.lay(ref, f"diffuser_fin_{i}", fin, fr, 0.0044, 0.05, "grille", 0.006, along_ray=True)
    return []


# ---------------------------------------------------------------- airbrake
AB_T = 0.024     # the airbrake's thickness at its thickest
FLUSH_DOWN = 0.035   # lowered, its leading edge sits this much below the raised one's
FLUSH_DROP = 0.025   # and its trailing edge this much lower again


def _ab_span():
    plan = soft(poly(R["airbrake"]["plan"]), 0.02)
    ymax = plan.bounds[3] - 0.004
    return plan, ymax


def airbrake(raised):
    """The airbrake: a flat wing spanning the tail. Raised, at the angle the reference model
    shows on two black struts; lowered, nearly level with the deck as a lip over the tail."""
    a = R["airbrake"]
    plan, ymax = _ab_span()
    (xf, zf), (xb, zb) = a["front"], a["back"]
    if raised:
        chord = lambda x: zf + (zb - zf) * (x - xf) / (xb - xf)
    else:
        chord = lambda x: zf - FLUSH_DOWN - FLUSH_DROP * (x - xf) / (xb - xf)
    rings = []
    n = 24
    for y in np.linspace(-ymax, ymax, 41):
        seg = plan.intersection(LineString([(-5, y), (5, y)]))
        if seg.is_empty:
            continue
        x0, _a, x1, _b = seg.bounds
        s = np.linspace(0, 1, n)
        xs = x1 + (x0 - x1) * s                          # front to back
        th = AB_T * np.clip(np.sqrt(4 * s * (1 - s)), 0.25, 1)
        base = chord(xs) - AB_T / 2
        top = np.column_stack([xs, np.full(n, y), base + th])
        bot = np.column_stack([xs, np.full(n, y), base + (AB_T - th) * 0.35])
        rings.append(np.vstack([bot, top[::-1][1:-1]]))
    v, f = geom.close_rings(rings)
    obs = [blend.make("airbrake", v, f, "body")]
    for sgn, side in ((1, "l"), (-1, "r")):
        y = 0.36 * sgn
        x0, x1 = xb + 0.06, xb + 0.14
        zt = float(chord((x0 + x1) / 2))
        zd = min(z_at(x0, y), z_at(x1, y)) - 0.02
        obs.append(_rbox(f"airbrake_strut_{side}", x0, x1, y - 0.007, y + 0.007, zd, zt, r=0.005, mat="trim"))
    return obs


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
    for x in (x0, x0 + 0.015, x1 - 0.015, x1):
        k = 0.92 if x in (x0, x1) else 1.0
        cy, cz = (y0 + y1) / 2, (z0 + z1) / 2
        rr = np.column_stack([cy + (ring[:, 0] - cy) * k, cz + (ring[:, 1] - cz) * k])
        rings.append(np.column_stack([x - lean * (rr[:, 1] - z0), rr]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


CABIN_BACK = -0.50      # the bulkhead behind the seats; the engine sits behind it


def interior(floor):
    x0 = R["screen"]["x_base"] - 0.05
    dash_top = 0.74
    obs = [_loft("firewall", x0 - 0.010, x0 - 0.004, lambda x: box(-0.5, floor + 0.002, 0.5, 3), n=2),
           _loft("dash", x0 - 0.0125, 0.62, lambda x: box(-0.5, 0.58, 0.5, dash_top + 0.08 * (1 - (x0 - x) / 0.7)), n=10)]
    belt_z = belt(0.2)
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, floor + 0.08, 1.2, belt_z - 0.02) if s > 0 else box(-1.2, floor + 0.08, -0.3, belt_z - 0.02)).intersection(
            body.inside(x, 0.035 + 0.003)).difference(body.inside(x, 0.035 + 0.025))
        obs.append(_loft(f"door_card_{side}", CABIN_BACK + 0.25, 0.60, clip, n=14))
        yc = 0.30 * sgn
        obs.append(_rbox(f"seat_base_{side}", -0.30, 0.20, yc - 0.21, yc + 0.21, floor + 0.002, 0.34))
        obs.append(_rbox(f"seat_back_{side}", -0.40, -0.28, yc - 0.20, yc + 0.20, 0.341, 0.90, lean=0.14))
    xm = R["screen"]["x_top"] + 0.06
    roof_in = body.inside(xm, 0.035).bounds[3]
    top = roof_in - 0.06
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.11, 0.11, top - 0.05, top, r=0.02)
    stem = _rbox("stem", xm - 0.016, xm - 0.004, -0.012, 0.012, top - 0.015, roof_in - 0.0008, r=0.004)
    obs.append(blend.union_all([m, stem], "mirror_rear"))
    return obs


# ---------------------------------------------------------------- engine
BAY_FLOOR = 0.42        # the engine bay's floor under the glass


def bay_ends():
    o, _g, _t = engine_shapes()
    b = o.bounds
    return b[0] - 0.02, min(b[2] + 0.02, CABIN_BACK - 0.03)


def engine():
    """Our own flat V8 under the glass: a dark block, a pair of long dark cam covers and a
    metal plenum between them with a round throttle body at its front."""
    x0, x1 = bay_ends()
    x0, x1 = x0 + 0.10, x1 - 0.06
    obs = [_rbox("engine_block", x0, x1, -0.26, 0.26, BAY_FLOOR + 0.003, 0.62, r=0.04, mat="dark_metal")]
    for sgn, side in ((1, "l"), (-1, "r")):
        y = 0.18 * sgn
        obs.append(_rbox(f"engine_cam_{side}", x0 + 0.03, x1 - 0.03, y - 0.07, y + 0.07, 0.622, 0.70, r=0.03, mat="dark_metal"))
    obs.append(_rbox("engine_plenum", x0 + 0.05, x1 - 0.05, -0.10, 0.10, 0.622, 0.76, r=0.045, mat="engine_metal"))
    v, f = geom.disc((x1 - 0.02, 0, 0.69), (1, 0, 0), 0.045, 0.06, n=48)
    obs.append(blend.make("engine_throttle", v, f, "engine_metal"))
    return obs


# ---------------------------------------------------------------- wheels
def wheels():
    """Five twin spoke wheels, 19 inch at the front and 20 at the back, in dark grey."""
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
            v, f = geom.disc((0, hw - 0.055, 0), (0, 1, 0), 0.07, 0.05, n=48)
            parts.append(blend.make("hub", place(v), f, "rim"))
            for k in range(10):
                a = 2 * np.pi * (k // 2) / 5 + (0.13 if k % 2 else -0.13)
                ca, sa = np.cos(a), np.sin(a)
                pts = []
                for r, w2, h0, h1 in ((0.06, 0.015, hw - 0.075, hw - 0.03), (rr - 0.018, 0.016, hw - 0.07, hw - 0.022)):
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


def options(ref):
    """Badge, name and airbrake options, named opt_<group>_<n>_<i>. Badges and names lie flat
    on their panels. Odd badge numbers are chrome, even the colour badge, at the same spot."""
    nm = name_shape()
    up = (0, 0, 1)
    out = []

    def add(group, n, objs):
        for i, ob in enumerate(objs):
            ob.name = f"opt_{group}_{n}_{i}"
            out.append(ob)
    _x, _y, fz, _r = R["badge_front"]
    for k, style in ((0, "chrome"), (1, "colour")):
        # front: on the nose tip (the reference), or low on the bumper above the intake slot
        add("frontbadge", 1 + k, badge(ref, style, 0.06, front_fr(), 0, fz, up, "fb", BADGE_T))
        add("frontbadge", 3 + k, badge(ref, style, 0.07, front_fr(), 0, 0.36, up, "fb", BADGE_T))
    # rear: Ivan's pick, chrome on the black band between the exhausts
    tz = float(np.mean(np.array(R["exhaust"][0])[:, 1]))
    add("rearbadge", 1, badge(ref, "chrome", 0.06, rear_fr(), 0, tz, up, "rb", BADGE_T, lift=0.0035))
    cap = 0.03
    add("name", 1, _flat(ref, nm, cap, rear_fr(), 0.45, 0.55, up, "nm", 0.0012, NAME_T, "chrome"))
    add("name", 2, _flat(ref, nm, cap, rear_fr(), -0.45, 0.55, up, "nm", 0.0012, NAME_T, "chrome"))
    add("name", 3, _flat(ref, nm, cap, rear_fr(), 0, 0.48, up, "nm", 0.0012, NAME_T, "chrome"))
    add("airbrake", 1, airbrake(True))
    add("airbrake", 2, airbrake(False))
    return out
