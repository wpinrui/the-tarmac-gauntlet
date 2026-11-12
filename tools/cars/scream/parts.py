"""The Scream's parts: cockpit, screen, lamps, intakes, grilles, mirrors, exhaust, rear panel,
diffuser, roof panels, interior, wheels, badge and name. Every shape and size here is the
Scream's own, from rows.json, kept plain and smooth. Runs inside Blender's Python module, called
by export.py."""
import json
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
    "trim": ((0.03, 0.03, 0.035), 0.6, 1.0, None),
    "carbon": ((0.06, 0.065, 0.07), 0.8, 1.0, None),
    "grille": ((0.035, 0.035, 0.04), 0.3, 1.0, None),
    "glass": ((0.07, 0.09, 0.11), 0.92, 0.4, None),
    "head_glass": ((0.92, 0.94, 0.96), 0.95, 0.2, None),
    "housing": ((0.06, 0.06, 0.065), 0.5, 1.0, None),
    "reflector": ((0.8, 0.81, 0.83), 0.97, 1.0, None),
    "amber": ((1.0, 0.52, 0.06), 0.85, 0.9, None),
    "lamp_head": ((0.95, 0.95, 0.92), 0.6, 1.0, (1.0, 0.97, 0.9)),
    "lamp_tail": ((0.5, 0.03, 0.04), 0.5, 1.0, (1.0, 0.06, 0.04)),
    "lamp_brake": ((0.62, 0.04, 0.05), 0.5, 1.0, (1.0, 0.04, 0.03)),
    "chrome": ((0.87, 0.88, 0.9), 0.98, 1.0, None),
    "dark_metal": ((0.15, 0.15, 0.16), 0.8, 1.0, None),
    "interior": ((0.08, 0.08, 0.085), 0.15, 1.0, None),
    "seat": ((0.13, 0.12, 0.115), 0.2, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.58, 0.59, 0.61), 0.9, 1.0, None),
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


def smooth_loop(pts, s=0.0004, n=160):
    """A closed outline as one smooth spline through its points."""
    from scipy.interpolate import splev, splprep
    p = np.asarray(pts, float)
    p = geom.resample(np.vstack([p, p[:1]]), 0.01)[:-1]
    tck, _u = splprep([p[:, 0], p[:, 1]], s=s, per=1, k=3)
    x, y = splev(np.linspace(0, 1, n, endpoint=False), tck)
    return poly(np.column_stack([x, y]))


def ring_of(g, n):
    return body.even(np.array(g.exterior.coords)[:-1], n)


# ---------------------------------------------------------------- cockpit and screen
BELT = 0.815          # the door tops and the cockpit's edge
COCKPIT_Y = 0.64      # the opening's half width at the door tops
CABIN_BACK = -0.32    # the bulkhead ahead of the fairings behind the seats
FLOOR = 0.20
SCREEN_DEPTH = 0.045  # the screen's frame and header stand this deep behind its outer face


def screen_line():
    """The screen's outer face down the middle: (x, z) at its base and at its top."""
    s = R["screen"]
    return (s["x_base"], body.z_top(s["x_base"])), (s["x_top"], body.z_top(s["x_top"]))


def plane_x(z):
    """x of the plane SCREEN_DEPTH behind the screen's outer face, at height z."""
    (x0, z0), (x1, z1) = screen_line()
    t = np.array([x1 - x0, z1 - z0]); t /= np.linalg.norm(t)
    n = np.array([-t[1], t[0]])
    if n[0] > 0:
        n = -n                                       # into the car: back and down
    px, pz = x0 + n[0] * SCREEN_DEPTH, z0 + n[1] * SCREEN_DEPTH
    return px + (z - pz) * t[0] / t[1]


def cockpit_cutters():
    """The room inside the body below the door tops, narrower at the top so the doors keep a
    ledge, and the opening above the door tops behind the screen and its header."""
    from shapely.geometry.polygon import orient
    x1 = R["screen"]["x_base"] - 0.06
    rings = []
    for x in np.linspace(CABIN_BACK, x1, 44):
        g = body.inside(x, 0.035).intersection(
            box(-3, FLOOR, 3, BELT - 0.07).union(box(-COCKPIT_Y, FLOOR, COCKPIT_Y, BELT + 0.02)))
        if g.geom_type != "Polygon":
            g = max(g.geoms, key=lambda q: q.area)
        rings.append(np.column_stack([np.full(96, x), ring_of(orient(g, 1), 96)]))
    v, f = geom.close_rings(rings)
    room = blend.make("cockpit_room", v, f, "interior")
    top = 2.0
    sec = Polygon([(CABIN_BACK, BELT), (plane_x(BELT), BELT), (plane_x(top), top), (CABIN_BACK, top)])
    loop = ring_of(orient(sec, 1), 48)
    rings = [np.column_stack([loop[:, 0], np.full(48, y), loop[:, 1]]) for y in (1.4, -1.4)]
    v, f = geom.close_rings(rings)
    return [room, blend.make("cockpit_opening", v, f, "interior")]


GLASS_BAND = 0.016    # black band round the screen


def plain_glass(pts, lo, hi, r=0.05):
    """A glass outline rebuilt from plain fits to the measured one: the low edge and the high
    edge as even curves across the car, and the sides as straight lines. pts are (along,
    across); lo and hi pick the low and high edge points by their along value."""
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
    hull = MultiPoint(np.vstack([p, p * [1, -1]])).convex_hull
    return soft(band.intersection(sides).intersection(hull), r)


def screen_shapes():
    s = R["screen"]
    span = s["x_base"] - s["x_top"]
    outline = plain_glass(s["outline"], lambda a: a > s["x_base"] - 0.2 * span, lambda a: a < s["x_top"] + 0.12 * span)
    glass = outline.buffer(-GLASS_BAND, join_style=1)
    return outline, glass, outline.difference(glass.buffer(-0.005))


def screen_fr():
    (x0, z0), (x1, z1) = screen_line()
    return slope_fr(x1, z1, x0, z0)


def screen_parts(ref, cutters, out):
    fr, to_fr = screen_fr()
    _o, g, t = (to_fr(q) for q in screen_shapes())
    cutters += blend.lay(ref, "cut_screen", g.buffer(-0.003), fr, -0.12, 0.006, "trim", along_ray=True)
    out += blend.lay(ref, "screen_glass", g.buffer(-0.006), fr, -0.010, -0.005, "glass", 0.02, along_ray=True)
    out += blend.lay(ref, "screen_trim", t, fr, 0.0015, 0.0035, "trim", 0.01)


# ---------------------------------------------------------------- head lamps
def head_shape(sgn):
    tris = np.array(R["head"]) * [1, sgn, 1]
    fr, to2 = plane_fr(tris, (1, sgn, 0.4))
    g = unary_union([Polygon(to2(t)).buffer(1e-4) for t in tris])
    g = soft(biggest(g.buffer(0.01, join_style=1).buffer(-0.01, join_style=1)), 0.012).simplify(0.002)
    return fr, to2, g, tris.reshape(-1, 3)


def headlamps(ref, cutters, out, sgn):
    """The lamp glass traced from the reference. Inside, our own: one big round projector in
    the outer lobe, a slim light bar curving along the upper edge towards the inner tip, and the
    amber strip along the bottom where the reference has it."""
    side = "l" if sgn > 0 else "r"
    fr, to2, shp, v = head_shape(sgn)
    cutters += blend.lay(ref, f"cut_head_{side}", shp, fr, -0.02, 0.006, "housing", 0.01, along_ray=True)
    inner = shp.buffer(-0.012)
    amber_pts = to2(np.array(R["head_amber"]).reshape(-1, 3) * [1, sgn, 1])
    from shapely.geometry import MultiPoint
    amber = soft(MultiPoint(amber_pts).convex_hull.buffer(0.004), 0.004).intersection(inner)
    outer = to2([v[np.argmax(v[:, 1] * sgn)]])[0]          # the lamp's outer tip
    c = np.array(inner.centroid.coords[0])
    pc = Point(c + (outer - c) * 0.35)
    rad = min(0.045, inner.exterior.distance(pc) - 0.008)
    lens = pc.buffer(rad, 64)
    ring = pc.buffer(rad + 0.012, 64).difference(pc.buffer(rad + 0.002, 64))
    out += blend.lay(ref, f"lamp_head_{side}0", lens, fr, -0.016, -0.011, "lamp_head", 0.004, along_ray=True)
    out += blend.lay(ref, f"head_reflector_{side}", ring.intersection(inner), fr, -0.0155, -0.0125, "reflector", 0.004, along_ray=True)
    keep = pc.buffer(rad + 0.02, 64).union(amber.buffer(0.01))
    band = inner.difference(shp.buffer(-0.022)).difference(keep)
    c3 = v.mean(0)
    up = (to2([c3 + [0, 0, 0.1]]) - to2([c3]))[0]
    up /= np.linalg.norm(up)
    a0 = np.array(amber.centroid.coords[0]) @ up if not amber.is_empty else -5
    pieces = [q for q in getattr(band, "geoms", [band]) if np.array(q.centroid.coords[0]) @ up > a0 + 0.01]
    bar = unary_union(pieces) if pieces else band
    if not bar.is_empty:
        out += blend.lay(ref, f"lamp_head_{side}1", biggest(bar), fr, -0.016, -0.011, "lamp_head", 0.004, along_ray=True)
    if not amber.is_empty:
        out += blend.lay(ref, f"head_amber_{side}", amber, fr, -0.016, -0.012, "amber", 0.004, along_ray=True)
    out += blend.lay(ref, f"head_glass_{side}", shp.buffer(-0.003), fr, -0.0045, -0.0015, "head_glass", 0.01, along_ray=True)


# ---------------------------------------------------------------- front openings
INTAKE_DEPTH = 0.05


def front_shapes():
    """The three intakes seen from ahead, made even and rounded: the wide middle one and one at
    each side."""
    mid = soft(mirrored(poly(R["intake_mid"])), 0.025)
    s = soft(smooth_loop(R["intake_side"], 0.0003), 0.02)
    return mid, [s, flip(s)]


def front_parts(ref, cutters, out):
    fr = front_fr()
    mid, sides = front_shapes()
    for name, g in [("mid", mid), ("l", sides[0]), ("r", sides[1])]:
        cutters += blend.lay(ref, f"cut_intake_{name}", g, fr, -INTAKE_DEPTH, 0.006, "grille", 0.012, along_ray=True)
        out += blend.lay(ref, f"intake_back_{name}", g.buffer(-0.002), fr, -INTAKE_DEPTH + 0.006, -INTAKE_DEPTH + 0.01,
                         "grille", 0.012, along_ray=True)
    # our own: two thin bars across the middle intake
    b = mid.bounds
    for i, z in enumerate(np.linspace(b[1], b[3], 4)[1:3]):
        bar = box(b[0], z - 0.006, b[2], z + 0.006).intersection(mid.buffer(-0.004))
        out += blend.lay(ref, f"intake_bar_{i}", bar, fr, -INTAKE_DEPTH + 0.01, -0.012, "dark_metal", 0.006, along_ray=True)
    # fog lamps between the intakes, round as the reference has them
    _x, fy, fz, fr_ = R["fog"]
    for sgn, side in ((1, "l"), (-1, "r")):
        c = Point(fy * sgn, fz)
        cutters += blend.lay(ref, f"cut_fog_{side}", c.buffer(fr_ + 0.004, 48), fr, -0.025, 0.006, "housing", 0.006, along_ray=True)
        out += blend.lay(ref, f"fog_ring_{side}", c.buffer(fr_ + 0.002, 48).difference(c.buffer(fr_ - 0.008, 48)), fr,
                         -0.022, -0.012, "chrome", 0.004, along_ray=True)
        out += blend.lay(ref, f"lamp_head_fog_{side}", c.buffer(fr_ - 0.009, 48), fr, -0.02, -0.014, "lamp_head", 0.004, along_ray=True)


def repeaters(ref, out):
    """The amber repeaters on the front wings."""
    g = soft(poly(R["repeater"]), 0.005)
    for sgn, side in ((1, "l"), (-1, "r")):
        out += blend.lay(ref, f"repeater_{side}", g, side_fr(sgn), -0.001, 0.003, "amber", 0.005)


# ---------------------------------------------------------------- side intakes
SIDE_INTAKE_W = 0.075     # the intake's width across its slanting line


def side_intake():
    """The slot behind each door: a band along the reference's slanting grille, rounded."""
    p = np.array(R["side_intake"])
    i0, i1 = p[:, 1].argmax(), p[:, 1].argmin()
    line = LineString([p[i0] + [0.01, -0.02], p[i1] + [0.0, 0.03]])
    return soft(line.buffer(SIDE_INTAKE_W / 2, cap_style=1), 0.02)


def side_intake_parts(ref, cutters, out):
    g = side_intake()
    for sgn, side in ((1, "l"), (-1, "r")):
        fr = side_fr(sgn)
        q = g if sgn > 0 else g
        cutters += blend.lay(ref, f"cut_sideintake_{side}", q, fr, -0.04, 0.006, "grille", 0.01, along_ray=True)
        out += blend.lay(ref, f"sideintake_back_{side}", q.buffer(-0.002), fr, -0.034, -0.03, "grille", 0.01, along_ray=True)


# ---------------------------------------------------------------- engine deck
def grille_shape():
    return soft(smooth_loop(R["grille"], 0.0004), 0.03)


def deck_parts(ref, out):
    """The two grilles in the engine deck, black with slats along the car."""
    g = grille_shape()
    for sgn, side in ((1, "l"), (-1, "r")):
        q = g if sgn > 0 else flip_y(g)
        out += blend.lay(ref, f"deck_grille_{side}", q, top_fr(), 0.0012, 0.0032, "grille", 0.01)
        b = q.bounds
        for i, y in enumerate(np.linspace(b[1], b[3], 7)[1:-1]):
            slat = box(b[0], y - 0.005, b[2], y + 0.005).intersection(q.buffer(-0.012))
            if not slat.is_empty:
                out += blend.lay(ref, f"deck_slat_{side}{i}", biggest(slat), top_fr(), 0.003, 0.0075, "dark_metal", 0.006)


def flip_y(g):
    """Mirror a top or side view shape (x, y) to the other side of the car."""
    return sscale(g, 1, -1, origin=(0, 0))


# ---------------------------------------------------------------- shut lines
DOOR_FRONT = [(0.70, 0.810), (0.785, 0.795), (0.808, 0.700)]   # the door's top front corner, cleaned


def door_shape():
    """The reference's door less the step in its front edge: the top runs on to a rounded
    corner and the front edge drops straight."""
    pts = [p for p in R["door"] if not (p[0] > 0.6 and p[1] > 0.6)]
    i = max(range(len(pts)), key=lambda j: pts[j][0] if pts[j][1] > 0.8 else -9) + 1
    return soft(poly(pts[:i] + DOOR_FRONT + pts[i:]), 0.02)


BONNET_MEET = 0.45    # the bonnet line meets the screen's base this far out from the centre
SCREEN_GAP = -0.008   # and runs this far under the screen's trim
BONNET_EASE = 4       # points of the lid's rear corners the bend replaces


def bonnet_path():
    """The bonnet shut line in plan: out of the windscreen's base inboard of its corners, bending
    out to the lid's rear corners, forward along the wings and round the nose. The lid runs back
    to the screen, so the line has no rear edge."""
    o, _g, _t = screen_shapes()
    b = soft(poly(R["bonnet"]), 0.04).buffer(-0.004)
    ring = geom.resample(np.array(b.exterior.coords), 0.01)[:-1]
    i_l = int(np.argmax(ring[:, 1] - ring[:, 0]))                   # the lid's rear corners
    i_r = int(np.argmax(-ring[:, 1] - ring[:, 0]))
    i_f = int(np.argmax(ring[:, 0]))
    n = len(ring)
    walk = lambda i, j: [(i + k) % n for k in range((j - i) % n + 1)]
    a_ = walk(i_l, i_r)
    idx = a_ if i_f in a_ else walk(i_r, i_l)[::-1]
    path = ring[idx]
    # from the lid's rear corner the line bends inboard and meets the screen's base square on
    edge = o.buffer(SCREEN_GAP).exterior
    hit = edge.intersection(LineString([(-5, BONNET_MEET), (5, BONNET_MEET)]))
    meet = np.array([max(q.x for q in getattr(hit, "geoms", [hit])), BONNET_MEET])
    k = BONNET_EASE                                                 # round the lid's corner
    path = path[k:len(path) - k]
    p0 = path[0]
    side = p0 - path[4]
    ctrl = p0 + side / np.linalg.norm(side) * 0.25 * np.linalg.norm(meet - p0)
    t = np.linspace(0, 1, 24)[:, None]
    bend = ((1 - t) ** 2 * meet + 2 * t * (1 - t) * ctrl + t ** 2 * p0)[:-1]
    return np.vstack([bend, path, bend[::-1] * [1, -1]])


def deck_loop():
    return np.array(soft(poly(R["deck"]), 0.05).buffer(-0.004).simplify(0.002).exterior.coords)[:-1]


def grooves(ref):
    """Shut lines as V grooves to cut: doors, bonnet, engine deck lid."""
    obs = []
    d = np.array(door_shape().exterior.coords)[:-1]
    d = geom.resample(np.vstack([d, d[:1]]), 0.01)
    top = d[:, 1] > BELT - 0.015
    k = int(np.argmax(top & ~np.roll(top, -1)))     # the last point under the top, walking round
    d = np.roll(d, -(k + 1), axis=0)
    path = d[d[:, 1] <= BELT - 0.015]
    for sgn, side in ((1, "l"), (-1, "r")):
        obs.append(blend.groove(ref, f"g_door_{side}", path, side_fr(sgn)))
    obs.append(blend.groove(ref, "g_bonnet", bonnet_path(), top_fr()))
    obs.append(blend.groove(ref, "g_deck", deck_loop(), top_fr(), closed=True))
    return obs


# ---------------------------------------------------------------- doors
DOOR_WALL = 0.09      # the door takes the wall this deep
DOOR_TOP = BELT - 0.07     # above this the door top is a solid ledge to the cockpit's edge
DOOR_GAP = 0.0015     # half the gap round the door, inside the shut line's groove
DOOR_OPEN = 62.0      # degrees the door swings out


def door_outline():
    """The left door seen from the side, inside its shut line. The mirror is a part of its own."""
    return door_shape()


def prism_y(name, g, y0, y1, mat="interior"):
    """A side view shape (x, z) as a solid between y0 and y1, its ends properly filled."""
    pts, tris = blend.triangulate(g, 0.05)
    n = len(pts)
    verts = np.vstack([np.column_stack([pts[:, 0], np.full(n, y), pts[:, 1]]) for y in (y0, y1)])
    count = {}
    for t in tris:
        for e in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            count[tuple(sorted(e))] = count.get(tuple(sorted(e)), 0) + 1
    faces = [(t[2], t[1], t[0]) for t in tris] + [(t[0] + n, t[1] + n, t[2] + n) for t in tris]
    for t in tris:
        for p, q in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            if count[tuple(sorted((p, q)))] == 1:
                faces.append((p, q, q + n, p + n))
    return blend.make(name, verts, faces, mat)


def _door_top(grow, sgn=1):
    """The door top, straight through from the cockpit's edge outwards."""
    g = biggest(door_outline().intersection(box(-5, DOOR_TOP, 5, 5)).buffer(grow, join_style=2))
    return [prism_y("door_top", g, (COCKPIT_Y - 0.01 - grow) * sgn, 1.4 * sgn)]


def door_hinge(sgn=1):
    """The hinge axis up the door's front edge, just outside the skin, and the open angle. A
    negative angle about an axis pointing up swings the left door's back edge out, a positive
    one the right's. sgn picks the side: 1 left, -1 right."""
    d = door_shape()
    xf = d.bounds[2] + 0.004
    z0, z1 = d.bounds[1], BELT
    y = max(body.y_at(xf - 0.01, z) for z in np.linspace(z0 + 0.02, BELT - 0.02, 20)) + 0.003
    return (xf, y * sgn, z0), (xf, y * sgn, z1), -DOOR_OPEN * sgn


def door(ref, shell, sgn=1):
    """Cut the left (sgn 1) or right (sgn -1) door free of the body; its card goes with it. Returns the door's pieces. The exact boolean now and then drops the door's skin
    on a near coincident face: then the cut is undone and tried again with the gap a hair wider."""
    import bpy
    side = "l" if sgn > 0 else "r"
    o = door_outline()
    regions = [(door_shape().intersection(box(-5, -1, 5, DOOR_TOP + 0.01)), DOOR_WALL)]
    roles = {f"door_card_{side}": "card"}
    keep = shell.data.copy()
    for k in range(5):
        pieces = blend.free_door(ref, side_fr(sgn), regions, DOOR_GAP + 0.00007 * k, shell, [], roles, o,
                                 solids=lambda g: _door_top(g, sgn), name=f"door_{side}")
        d = pieces[0]
        skin = sum(1 for p in d.data.polygons if d.material_slots[p.material_index].material.name == "body")
        if skin > 1000:
            return pieces
        print(f"door_{side}: skin lost, trying again")
        for ob in pieces[1:]:
            ob.name = ob.name.replace(f"door_{side}_", "").replace("card", f"door_card_{side}")
        bpy.data.objects.remove(d)
        shell.data = keep.copy()
    raise RuntimeError(f"door_{side} would not cut free")


# ---------------------------------------------------------------- mirrors
MIRROR_SWEEP = 0.12   # radians the housing turns back from square to the car


def _housing(sgn, inset=0.0, x0=None, x1=None):
    """The mirror housing: a rounded lozenge in front view, swept back in plan, as rings along x."""
    m = R["mirror"]
    zc = (m["glass_z0"] + m["glass_z1"]) / 2
    h = (m["glass_z1"] - m["glass_z0"]) / 2 + 0.012 - inset
    y0, y1 = m["glass_y0"] - 0.012 + inset, m["glass_y1"] + 0.006 - inset
    face = soft(box(y0, zc - h, y1, zc + h), max(h - 0.01, 0.01))
    ring = ring_of(face, 96)
    xb = m["x_back"] if x0 is None else x0
    xf = xb + 0.10 if x1 is None else x1
    rings = []
    for x in np.linspace(xb, xf, 8):
        t = (x - m["x_back"]) / 0.10
        k = 1 - 0.35 * max(0.0, t - 0.3) ** 2                       # the nose of the housing rounds off
        cy, cz = (y0 + y1) / 2, zc
        rr = np.column_stack([cy + (ring[:, 0] - cy) * k, cz + (ring[:, 1] - cz) * k])
        rings.append(np.column_stack([np.full(len(rr), x), rr[:, 0] * sgn, rr[:, 1]]))
    if sgn < 0:
        rings = [r[::-1] for r in rings]
    return rings


def _sweep(ob, sgn):
    from mathutils import Matrix
    m = R["mirror"]
    p = Matrix.Translation((m["x_back"] + 0.05, sgn * m["glass_y0"], 0))
    ob.data.transform(p @ Matrix.Rotation(sgn * MIRROR_SWEEP, 4, "Z") @ p.inverted())
    return ob


def mirrors():
    """Body coloured housings on a body coloured stalk down to the door top."""
    m = R["mirror"]
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        v, f = geom.close_rings(_housing(sgn))
        house = _sweep(blend.make(f"mirror_{side}", v, f, "body"), sgn)
        # the stalk: from under the housing's inner end, down and in to the door top
        za = (m["glass_z0"] + m["glass_z1"]) / 2 - 0.02
        xa = m["x_back"] + 0.055
        ya = m["glass_y0"] + 0.02
        face = soft(box(-0.022, -0.009, 0.022, 0.009), 0.008)
        rg = ring_of(face, 32)
        rings = []
        yd = min(body.y_at(xa + 0.06, BELT - 0.01), COCKPIT_Y + 0.12) - 0.03
        for t in np.linspace(0, 1, 10):
            s = t * t * (3 - 2 * t)
            x = xa + 0.06 * t
            y = ya + (yd - ya) * s
            z = za + (BELT - 0.03 - za) * t
            rings.append(np.column_stack([x + rg[:, 0] * 0.3, (y + rg[:, 0] * 0.7) * sgn, z + rg[:, 1]]))
        if sgn < 0:
            rings = [r[::-1] for r in rings]
        v, f = geom.close_rings(rings)
        stalk = blend.make(f"mirror_stalk_{side}", v, f, "body")
        obs.append(blend.union_all([house, stalk], f"mirror_{side}"))
    return obs


def mirror_recess():
    """A shallow recess in each housing's back with the glass inside."""
    m = R["mirror"]
    cut, glass = [], []
    for sgn, side in ((1, "l"), (-1, "r")):
        v, f = geom.close_rings(_housing(sgn, 0.009, m["x_back"] - 0.03, m["x_back"] + 0.008))
        cut.append(_sweep(blend.make(f"cut_mirror_{side}", v, f, "trim"), sgn))
        v, f = geom.close_rings(_housing(sgn, 0.0105, m["x_back"] + 0.003, m["x_back"] + 0.006))
        glass.append(_sweep(blend.make(f"mirror_glass_{side}", v, f, "mirror_glass"), sgn))
    return cut, glass


# ---------------------------------------------------------------- rear
def _tail_x(y, z):
    """The rearmost x where the skin reaches out to (y, z)."""
    for x in np.arange(TAIL, TAIL + 0.6, 0.003):
        if body.y_at(x, z) >= abs(y):
            return float(x)
    return TAIL


def tail_shape():
    return soft(smooth_loop(R["tail"], 0.0003), 0.012)


def taillamps(ref, cutters, out):
    """The tail lamps traced from the reference. Inside, our own: a red bar along the top as the
    tail light and a wider bar below it as the brake light."""
    fr = rear_fr()
    g = tail_shape()
    for sgn, side in ((1, "l"), (-1, "r")):
        q = g if sgn > 0 else flip(g)
        cutters += blend.lay(ref, f"cut_tail_{side}", q, fr, -0.02, 0.006, "housing", 0.008, along_ray=True)
        inner = q.buffer(-0.008)
        b = inner.bounds
        zm = b[1] + (b[3] - b[1]) * 0.55
        top = inner.intersection(box(-5, zm + 0.006, 5, 5))
        low = inner.intersection(box(-5, -5, 5, zm - 0.006))
        out += blend.lay(ref, f"lamp_tail_{side}", biggest(top), fr, -0.016, -0.011, "lamp_tail", 0.004, along_ray=True)
        out += blend.lay(ref, f"lamp_brake_{side}", biggest(low), fr, -0.016, -0.011, "lamp_brake", 0.004, along_ray=True)
        out += blend.lay(ref, f"tail_glass_{side}", q.buffer(-0.003), fr, -0.0045, -0.0015, "head_glass", 0.008, along_ray=True)


def panel_shape():
    """The black panel round the exhausts: a plain rounded band across the tail."""
    b = poly(R["rear_panel"]).bounds
    w = max(abs(b[0]), abs(b[2]))
    return soft(box(-w, b[1], w, b[3]), 0.035)


def diffuser_shape():
    """The black diffuser under the panel, made even and plain."""
    g = mirrored(poly(R["diffuser"]))
    return soft(g.convex_hull.intersection(box(-5, -5, 5, panel_shape().bounds[1] - 0.004)), 0.03)


def tip_shape():
    _x, y, z, r = R["exhaust"][0]
    return Point(y, z).buffer(r, 64)


TIP_LEN = 0.30


def exhaust():
    """Two round tips in the black panel, each a chrome tube with a dark inside."""
    cut, pipes = [], []
    for sgn, side in ((1, "l"), (-1, "r")):
        t = tip_shape() if sgn > 0 else flip(tip_shape())
        x_end = min(_tail_x(y, z) for y, z in np.array(t.exterior.coords)) + 0.004
        hole = t.buffer(0.001)
        ring = ring_of(hole, 128)
        rings = [np.column_stack([np.full(128, x), ring[:, 0], ring[:, 1]]) for x in (x_end - 0.05, x_end + TIP_LEN)]
        v, f = geom.close_rings(rings)
        cut.append(blend.make(f"cut_exhaust_{side}", v, f, "trim"))
        outer = ring_of(t.buffer(-0.001), 96)
        inner = ring_of(t.buffer(-0.006), 96)
        rings = [np.column_stack([np.full(96, x), rg[:, 0], rg[:, 1]])
                 for x, rg in ((x_end, inner), (x_end, outer), (x_end + TIP_LEN, outer), (x_end + TIP_LEN, inner))]
        v, f = geom.close_rings(rings, cap=False)
        m = 96
        for j in range(m):
            k = (j + 1) % m
            f.append((3 * m + j, 3 * m + k, k, j))
        pipes.append(blend.make(f"exhaust_{side}", v, f, "chrome"))
        rg = ring_of(t.buffer(-0.0065), 96)
        rings = [np.column_stack([np.full(96, x), rg[:, 0], rg[:, 1]]) for x in (x_end + 0.05, x_end + 0.06)]
        v, f = geom.close_rings(rings)
        pipes.append(blend.make(f"exhaust_inner_{side}", v, f, "trim"))
    return cut, pipes


def rear_parts(ref, out):
    fr = rear_fr()
    holes = unary_union([tip_shape(), flip(tip_shape())]).buffer(0.004)
    out += blend.lay(ref, "rear_panel", panel_shape().difference(holes), fr, 0.0015, 0.0035, "grille", 0.008, along_ray=True)
    d = diffuser_shape()
    out += blend.lay(ref, "diffuser", d, fr, 0.0015, 0.0035, "grille", 0.008, along_ray=True)
    b = d.bounds
    for i, y in enumerate((-0.42, 0.0, 0.42)):
        fin = box(y - 0.007, b[1], y + 0.007, b[3] - 0.03).intersection(d.buffer(-0.006))
        out += blend.lay(ref, f"diffuser_fin_{i}", fin, fr, 0.0034, 0.05, "grille", 0.006, along_ray=True)


# ---------------------------------------------------------------- hard top
# The reference is an open roadster with two fairings behind the seats: here the fairings go
# and a hard top of our own takes their place. One surface runs from the screen's header over
# the cabin and sweeps down onto the deck, with glass in the doors up to it and a rear window
# in the sweep. Every piece is cut from that one surface, so they meet edge to edge.
TOP_X = (-0.56, -0.32, 0.40)    # the sweep's foot on the deck, the cabin's back, the header
TOP_Z = (1.085, 1.150, 1.122)   # roof centre height at the cabin's back, the crest, the header
TOP_W = (0.60, 0.56, 0.50)      # half width of the roof's edge at the foot, the cabin's back, the header
TOP_CROWN = (0.035, 0.022)      # the edges sit this much lower than the centre, back and front
TOP_ROUND = 0.045               # the roof's edge rounds over this far
TOP_T = 0.010                   # thickness of the roof and the sweep
SILL = (0.70, BELT - 0.015)     # where the side meets the door top: half width, height
PILLAR = ((0.80, 0.70, 0.80), (0.55, 0.61, 0.97))   # the screen's side edge: its foot, its middle
GLASS_T = 0.004
REAR_WINDOW = (0.30, 0.04)      # the rear window's half width, and the margin round it


def _roof_centre(x):
    """Roof centre height: a curve over the cabin, then an easing fall to the deck."""
    xf, xb, xh = TOP_X
    zb, zc, zh = TOP_Z
    if x >= xb:
        s = (x - xb) / (xh - xb)
        return (1 - s) ** 2 * zb + 2 * s * (1 - s) * (2 * zc - (zb + zh) / 2) + s ** 2 * zh
    u = (x - xf) / (xb - xf)
    foot = body.z_top(xf) - 0.012
    slope = 2 * (2 * zc - (zb + zh) / 2 - zb) / (xh - xb)       # the cabin roof's slope at its back
    return foot + (zb - foot) * (3 * u * u - 2 * u ** 3) + slope * (xb - xf) * (u ** 3 - u * u)


def _deck_drop(x, y):
    """How far the skin's top at half width y sits below its top centre, at x."""
    h = body.half(body.at(body.CV, x))
    top = h[int(np.argmax(h[:, 0])):]                          # outermost point up to the top centre
    return body.z_top(x) - float(np.interp(y, top[::-1, 0], top[::-1, 1]))


def _lerp3(x, vals):
    xf, xb, xh = TOP_X
    if x >= xb:
        return vals[1] + (vals[2] - vals[1]) * (x - xb) / (xh - xb)
    return vals[0] + (vals[1] - vals[0]) * (x - xf) / (xb - xf)


def _section(x, n_side=10, n_round=6, n_top=29):
    """One cross section of the top's left half and right half: from the left sill up the
    side, round the edge, over the crown and down to the right sill. Returns (points, k0, k1):
    the side runs to k0 on the left and from k1 on the right."""
    zc = _roof_centre(x)
    xf, xb, xh = TOP_X
    a = np.clip((x - xf) / (xb - xf), 0, 1)                  # 0 at the foot, 1 at the cabin
    w = _lerp3(x, TOP_W)
    crown = TOP_CROWN[0] + (TOP_CROWN[1] - TOP_CROWN[0]) * max(0.0, (x - xb) / (xh - xb))
    crown = crown * a + _deck_drop(xf, TOP_W[0]) * (1 - a)    # the foot follows the deck's crown
    sill = np.array(SILL)
    edge = np.array([w, zc - crown])
    side = sill - edge
    q0 = edge + side / np.linalg.norm(side) * min(TOP_ROUND, 0.45 * np.linalg.norm(side))
    v1 = 1 - TOP_ROUND / w
    q1 = np.array([w * v1, zc - crown * v1 * v1])
    left = [sill + (q0 - sill) * t for t in np.linspace(0, 1, n_side)]
    left += [(1 - t) ** 2 * q0 + 2 * t * (1 - t) * edge + t ** 2 * q1 for t in np.linspace(0, 1, n_round)[1:]]
    top = [np.array([w * v, zc - crown * v * v]) for v in np.linspace(v1, -v1, n_top)[1:-1]]
    half = np.array(left)
    pts = np.vstack([half, top, (half * [-1, 1])[::-1]])
    k0 = n_side - 1
    return np.column_stack([np.full(len(pts), x), pts]), k0, len(pts) - 1 - k0


def _slab(name, outer, t, mat, centre_z=0.80):
    """A grid of points (rows, cols, 3) on the outside, made a closed solid t thick inwards:
    the outside, the inside and four edge strips."""
    g = np.asarray(outer, float)
    n = np.cross(np.gradient(g, axis=0), np.gradient(g, axis=1))
    n /= np.linalg.norm(n, axis=2, keepdims=True) + 1e-12
    rel = g.copy()
    rel[..., 0] = 0
    rel[..., 2] -= centre_z
    flip = np.sign(np.sum(n * rel, axis=2, keepdims=True))
    n *= np.where(flip == 0, 1, flip)
    inner = g - t * n
    R_, C = g.shape[:2]
    verts = np.vstack([g.reshape(-1, 3), inner.reshape(-1, 3)])
    off = R_ * C
    idx = lambda i, j: i * C + j
    faces = []
    for i in range(R_ - 1):
        for j in range(C - 1):
            q = (idx(i, j), idx(i + 1, j), idx(i + 1, j + 1), idx(i, j + 1))
            faces.append(q)
            faces.append(tuple(off + k for k in q[::-1]))
    border = [idx(0, j) for j in range(C)] + [idx(i, C - 1) for i in range(1, R_)] + \
             [idx(R_ - 1, j) for j in range(C - 2, -1, -1)] + [idx(i, 0) for i in range(R_ - 2, 0, -1)]
    for a_, b_ in zip(border, border[1:] + border[:1]):
        faces.append((b_, a_, a_ + off, b_ + off))
    ob = blend.make(name, verts, faces, mat)
    _outward(ob)
    return ob


def _outward(ob):
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()


def hard_top(mat):
    """The roof and sweep in mat, the door glass (it swings with the doors) and the rear window."""
    xf, xb, xh = TOP_X
    xs = np.concatenate([np.linspace(xf, xb, 26)[:-1], np.linspace(xb, xh, 40)])
    secs = [_section(x) for x in xs]
    grid = np.array([s[0] for s in secs])
    k0, k1 = secs[0][1], secs[0][2]
    ib = 25                                                    # the cabin's back
    obs = []

    # the sides of the sweep, behind the doors
    obs.append(_slab("top_side_l", grid[:ib + 1, :k0 + 1], TOP_T, mat))
    obs.append(_slab("top_side_r", grid[:ib + 1, k1:], TOP_T, mat))

    # the cap, with the rear window let into the sweep
    cap = grid[:, k0:k1 + 1]
    hw, m = REAR_WINDOW
    ys = cap[ib // 2, :, 1]
    j0, j1 = int(np.argmin(np.abs(ys - hw))), int(np.argmin(np.abs(ys + hw)))
    i0 = int(np.argmin(np.abs(xs - (xf + m)))) + 1
    i1 = int(np.argmin(np.abs(xs - (xb - m))))
    obs.append(_slab("roof", cap[i1:], TOP_T, mat))
    obs.append(_slab("top_tail", cap[:i0 + 1], TOP_T, mat))
    obs.append(_slab("top_rear_l", cap[i0:i1 + 1, :j0 + 1], TOP_T, mat))
    obs.append(_slab("top_rear_r", cap[i0:i1 + 1, j1:], TOP_T, mat))
    obs.append(_slab("rear_glass", cap[i0:i1 + 1, j0:j1 + 1], GLASS_T, "glass"))

    # the door glass: between the door top, the roof's edge, the screen's side edge and the
    # cabin's back, a patch whose top and back are the cap's and the sweep's own edges
    cab = grid[ib:, :k0 + 1]                                  # rows over x, columns sill to edge
    top_pts, rear_pts = cab[:, -1], cab[0]
    xt = top_pts[:, 0]
    top = lambda u: np.array([np.interp(xb + (xh - xb) * u, xt, top_pts[:, c]) for c in range(3)])
    lo, mid = np.array(PILLAR[0]), np.array(PILLAR[1])
    hi = top(1.0)
    ctrl = 2 * mid - (lo + hi) / 2
    foot = rear_pts[0]
    lo = np.array([lo[0], foot[1], foot[2]])
    bottom = lambda u: foot + (lo - foot) * u
    front = lambda t: (1 - t) ** 2 * lo + 2 * t * (1 - t) * ctrl + t ** 2 * hi
    T = np.linspace(0, 1, len(rear_pts))
    rear = lambda t: np.array([np.interp(t, T, rear_pts[:, c]) for c in range(3)])
    U = np.linspace(0, 1, 48)
    patch = np.array([[(1 - t) * bottom(u) + t * top(u) + (1 - u) * rear(t) + u * front(t)
                       - ((1 - u) * (1 - t) * foot + u * (1 - t) * lo + (1 - u) * t * top(0.0) + u * t * hi)
                       for u in U] for t in T])
    for sgn, side in ((1, "l"), (-1, "r")):
        obs.append(_slab(f"door_{side}_glass", patch * [1, sgn, 1], GLASS_T, "glass"))
    return obs


# ---------------------------------------------------------------- arches
def arches():
    """Arch cutters and the wheel tubs kept out of the cockpit."""
    cut, tubs = [], []
    for x, wr, ar in ((W["front_x"], W["r_f"], W["arch_f"]), (W["rear_x"], W["r_r"], W["arch_r"])):
        for sgn in (1, -1):
            v, f = geom.disc((x, 0.86 * sgn, wr), (0, 1, 0), ar, 0.56, n=96)
            cut.append(blend.make("arch", v, f, "interior"))
            v, f = geom.disc((x, 0.88 * sgn, wr), (0, 1, 0), ar + 0.035, 0.72, n=96)
            tubs.append(blend.make("tub", v, f, "interior"))
    return cut, tubs


# ---------------------------------------------------------------- interior
def _rbox(name, x0, x1, y0, y1, z0, z1, r=0.03, lean=0.0, mat="interior"):
    sh = soft(box(y0, z0, y1, z1), r)
    ring = ring_of(sh, 48)
    rings = []
    for x in (x0, x0 + 0.015, x1 - 0.015, x1):
        k = 0.92 if x in (x0, x1) else 1.0
        cy, cz = (y0 + y1) / 2, (z0 + z1) / 2
        rr = np.column_stack([cy + (ring[:, 0] - cy) * k, cz + (ring[:, 1] - cz) * k])
        rings.append(np.column_stack([x - lean * (rr[:, 1] - z0), rr]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def _loft(name, x0, x1, clip, n=12, gap=0.003, mat="interior"):
    rings = []
    for x in np.linspace(x0, x1, n):
        g = body.inside(x, 0.035 + gap).intersection(clip(x))
        if g.geom_type != "Polygon":
            g = max(getattr(g, "geoms", [g]), key=lambda q: q.area)
        rings.append(np.column_stack([np.full(64, x), ring_of(g, 64)]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def interior():
    """Firewall, a dash meeting it, a centre console, two seats, door cards, the wheel on the
    left, and the mirror hung from the screen header."""
    x0 = R["screen"]["x_base"] - 0.06
    obs = [_loft("firewall", x0 - 0.010, x0 - 0.004, lambda x: box(-0.7, FLOOR + 0.002, 0.7, BELT - 0.072), n=2),
           _loft("dash", x0 - 0.0125, 0.62, lambda x: box(-0.7, 0.56, 0.7, BELT - 0.075), n=10),
           _rbox("console", -0.05, 0.64, -0.09, 0.09, FLOOR + 0.002, 0.52, r=0.04)]
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, FLOOR + 0.06, 1.2, BELT - 0.09) if s > 0 else box(-1.2, FLOOR + 0.06, -0.3, BELT - 0.09)).intersection(
            body.inside(x, 0.035 + 0.003)).difference(body.inside(x, 0.035 + 0.022))
        obs.append(_loft(f"door_card_{side}", CABIN_BACK + 0.06, 0.62, clip, n=14))
        yc = 0.33 * sgn
        obs.append(_rbox(f"seat_base_{side}", -0.20, 0.28, yc - 0.20, yc + 0.20, FLOOR + 0.002, 0.33, mat="seat"))
        obs.append(_rbox(f"seat_back_{side}", -0.27, -0.17, yc - 0.19, yc + 0.19, 0.331, 0.88, lean=0.07, mat="seat"))
    # the wheel, on the left: a rim and three spokes, its axis running forward and down
    from mathutils import Matrix, Vector
    hub = np.array([0.44, 0.33, 0.63])
    axis = np.array([np.cos(np.radians(24)), 0.0, -np.sin(np.radians(24))])
    parts = []
    v, f = geom.lathe(ring_of(Point(0.175, 0).buffer(0.014, 24), 24), 64, axis="y")
    parts.append(blend.make("wheel_ring", v, f, "interior"))
    for a in (90, 210, 330):
        sp = _rbox("spoke", 0.0, 0.165, -0.012, 0.012, -0.008, 0.008, r=0.006)
        sp.data.transform(Matrix.Rotation(np.radians(a), 4, "Y"))
        parts.append(sp)
    v, f = geom.disc((0, 0, 0), (0, 1, 0), 0.045, 0.05, n=32)
    parts.append(blend.make("boss", v, f, "interior"))
    sw = blend.union_all(parts, "steering_wheel")
    rot = Vector((0, 1, 0)).rotation_difference(Vector(tuple(axis))).to_matrix().to_4x4()
    sw.data.transform(Matrix.Translation(tuple(hub)) @ rot)
    obs.append(sw)
    col = 0.17
    v, f = geom.disc(tuple(hub + axis * (col / 2 + 0.02)), tuple(axis), 0.026, col, n=24)
    obs.append(blend.make("steering_column", v, f, "interior"))
    # the mirror under the header
    xm = plane_x(body.z_top(R["screen"]["x_top"]) - 0.03) - 0.01
    zt = body.z_top(R["screen"]["x_top"]) - 0.05
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.11, 0.11, zt - 0.05, zt, r=0.02)
    stem = _rbox("stem", xm - 0.016, xm + 0.03, -0.012, 0.012, zt - 0.012, zt + 0.02, r=0.004)
    obs.append(blend.union_all([m, stem], "mirror_rear"))
    return obs


# ---------------------------------------------------------------- wheels
def wheels():
    """Five broad straight spokes in silver: 19 inch at the front, 20 at the back."""
    obs = []
    for x, fb, wr, width, rr in ((W["front_x"], "f", W["r_f"], W["width_f"], W["rim_f"]),
                                 (W["rear_x"], "r", W["r_r"], W["width_r"], W["rim_r"])):
        hw = width / 2
        yc = W["outer_y"] - hw
        tyre = soft(box(rr, -hw, wr, hw), 0.028)
        tyre = tyre.union(box(rr, -hw + 0.012, rr + 0.03, hw - 0.012))
        tp = ring_of(tyre, 96)
        rim = [(rr - 0.001, -hw + 0.012), (rr - 0.001, hw - 0.008), (rr - 0.012, hw - 0.001),
               (rr - 0.026, hw - 0.006), (rr - 0.026, -hw + 0.012)]
        for sgn, lr in ((1, "l"), (-1, "r")):
            def place(v, s=sgn, x=x, yc=yc, wr=wr):
                v = np.asarray(v, float)
                return np.column_stack([x + v[:, 0], (yc + v[:, 1]) * s, wr + v[:, 2]])
            v, f = geom.lathe(tp, 96, axis="y")
            obs.append(blend.make(f"wheel_tyre_{fb}{lr}", place(v), f, "tyre"))
            v, f = geom.lathe(rim, 96, axis="y")
            parts = [blend.make("rim", place(v), f, "rim")]
            v, f = geom.disc((0, hw - 0.05, 0), (0, 1, 0), 0.075, 0.05, n=48)
            parts.append(blend.make("hub", place(v), f, "rim"))
            for k in range(5):
                a = 2 * np.pi * k / 5
                ca, sa = np.cos(a), np.sin(a)
                pts = []
                for r, w2, h0, h1 in ((0.065, 0.026, hw - 0.07, hw - 0.025), (rr - 0.02, 0.018, hw - 0.065, hw - 0.018)):
                    for dw in (-w2, w2):
                        for hh in (h0, h1):
                            pts.append((r * ca - dw * sa, hh, r * sa + dw * ca))
                v = np.array(pts)
                f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
                parts.append(blend.make("spoke", place(v), f, "rim"))
            obs.append(blend.union_all(parts, f"wheel_rim_{fb}{lr}"))
    return obs


# ---------------------------------------------------------------- badge and name
def _quad(p0, p1, p2, n=10):
    t = np.linspace(0, 1, n)[1:, None]
    return list(map(tuple, (1 - t) ** 2 * np.array(p0) + 2 * (1 - t) * t * np.array(p1) + t * t * np.array(p2)))


def badge_shape():
    """The Kaiser badge from src/renderer/assets/brands/kaiser/kaiser-badge-chrome.svg: the
    ring and the stem, scaled as the file scales them, with the three slits cut across. y up,
    centred, 1 unit tall."""
    from shapely.affinity import affine_transform
    ring = Point(90, 100).buffer(52, 128).difference(Point(90, 100).buffer(26, 128))
    stem = [(128, 48), (154, 48), (154, 124)] + _quad((154, 124), (154, 136), (168, 136)) + \
        [(174, 136), (174, 152), (160, 152)] + _quad((160, 152), (128, 152), (128, 122))
    g = unary_union([ring, Polygon(stem)]).buffer(1.5, join_style=1)
    g = affine_transform(g, [1.15, 0, 0, 1.15, -23, -15])
    for x, y in ((96, 80), (104, 100), (112, 120)):
        g = g.difference(box(x, y, x + 110, y + 9))
    g = sscale(g, 1, -1, origin=(0, 0))
    b = g.bounds
    g = translate(g, -(b[0] + b[2]) / 2, -(b[1] + b[3]) / 2)
    return sscale(g, 1 / (b[3] - b[1]), 1 / (b[3] - b[1]), origin=(0, 0))


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


BADGE_T = 0.006
NAME_T = 0.004
PANEL_T = 0.0035      # the rear panel stands proud by this much: badges on it sit on top


def options(ref):
    """Badge and name options, named opt_<group>_<n>_<i>. Badges and names lie flat
    on their panels."""
    bdg, nm = badge_shape(), name_shape()
    up = (0, 0, 1)
    out = []

    def add(group, n, objs):
        for i, ob in enumerate(objs):
            ob.name = f"opt_{group}_{n}_{i}"
            out.append(ob)
    # front badge: 1 on the bonnet's front edge (the reference), 2 low on the nose
    bz = float(np.array(R["intake_mid"])[:, 1].max())
    add("frontbadge", 1, _flat(ref, bdg, 0.055, front_fr(), 0, 0.615, up, "fb", 0.0012, BADGE_T, "chrome"))
    add("frontbadge", 2, _flat(ref, bdg, 0.055, front_fr(), 0, bz + 0.05, up, "fb", 0.0012, BADGE_T, "chrome"))
    # rear badge: 1 on the black panel between the exhausts (the reference's plate), 2 high on
    # the top of the tail
    pb = panel_shape().bounds
    add("rearbadge", 1, _flat(ref, bdg, 0.06, rear_fr(), 0, (pb[1] + pb[3]) / 2 + 0.03, up, "rb",
                              PANEL_T + 0.0005, PANEL_T + BADGE_T, "chrome"))
    add("rearbadge", 2, _flat(ref, bdg, 0.06, rear_fr(), 0, 0.665, up, "rb", 0.0012, BADGE_T, "chrome"))
    # name: 1 on the top of the tail right of centre (the reference), 2 on the black panel, low
    cap = 0.028
    add("name", 1, _flat(ref, nm, cap, rear_fr(), -0.30, 0.665, up, "nm", 0.0012, NAME_T, "chrome"))
    add("name", 2, _flat(ref, nm, cap, rear_fr(), 0, pb[1] + 0.03, up, "nm", PANEL_T + 0.0005, PANEL_T + NAME_T, "chrome"))
    return out
