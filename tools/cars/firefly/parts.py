"""The Firefly's parts: round head and tail lamps, indicators, the lower intake with its fog lamps,
glass, shut lines, doors, mirrors, interior, five spoke wheels, badge and name. Every shape and size
here is the Firefly's own, from rows.json, kept plain and smooth. Runs inside Blender's Python
module, called by export.py."""
import json
import sys
from pathlib import Path

import numpy as np
from shapely.affinity import affine_transform, scale as sscale, translate
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
    "trim": ((0.025, 0.025, 0.028), 0.45, 1.0, None),
    "grille": ((0.035, 0.035, 0.04), 0.2, 1.0, None),
    "glass": ((0.06, 0.08, 0.1), 0.92, 0.42, None),
    "head_glass": ((0.92, 0.94, 0.96), 0.95, 0.2, None),
    "tail_glass": ((0.5, 0.04, 0.05), 0.92, 0.5, None),
    "amber_glass": ((0.95, 0.5, 0.06), 0.9, 0.75, None),
    "housing": ((0.06, 0.06, 0.065), 0.5, 1.0, None),
    "reflector": ((0.8, 0.81, 0.83), 0.97, 1.0, None),
    "lamp_head": ((0.95, 0.95, 0.93), 0.6, 1.0, (1.0, 0.98, 0.92)),
    "lamp_tail": ((0.5, 0.03, 0.04), 0.5, 1.0, (1.0, 0.06, 0.04)),
    "lamp_brake": ((0.6, 0.04, 0.05), 0.5, 1.0, (1.0, 0.04, 0.03)),
    "lamp_rain": ((0.55, 0.04, 0.05), 0.5, 1.0, (1.0, 0.05, 0.03)),
    "chrome": ((0.86, 0.87, 0.89), 0.98, 1.0, None),
    "badge_black": ((0.086, 0.09, 0.102), 0.6, 1.0, None),
    "interior": ((0.075, 0.075, 0.085), 0.15, 1.0, None),
    "seat": ((0.11, 0.11, 0.12), 0.2, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.78, 0.79, 0.8), 0.7, 1.0, None),
    "dark_metal": ((0.14, 0.14, 0.15), 0.8, 1.0, None),
}
LAMPS = {"Headlights": ["lamp_head"], "Tail lights": ["lamp_tail"], "Brake lights": ["lamp_brake"],
         "Rain lights": ["lamp_rain"]}
GROUPS = {"frontbadge": "Front badge", "rearbadge": "Rear badge", "name": "Name"}


# ---------------------------------------------------------------- frames
def front_fr():
    return blend.frame((NOSE + 0.3, 0, 0), (0, 1, 0), (0, 0, 1), (-1, 0, 0))


def rear_fr():
    return blend.frame((TAIL - 0.3, 0, 0), (0, 1, 0), (0, 0, 1), (1, 0, 0))


def side_fr(sgn):
    return blend.frame((0, 1.4 * sgn, 0), (1, 0, 0), (0, 0, 1), (0, -sgn, 0))


def top_fr():
    return blend.frame((0, 0, 2.0), (1, 0, 0), (0, 1, 0), (0, 0, -1))


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


def belt(x):
    k, c = R["belt"]
    return k * x + c


def glass_fr(sgn):
    """A frame square to the side glass, which leans in from its foot to the roof edge, and a
    map from the side view (x, z) into it."""
    v = body.values(-0.1)
    y0, z0, y1, z1 = v["wg"], v["zs"], v["wr"], v["zr"]
    a = np.arctan2(y0 - y1, z1 - z0)
    up = np.array([0.0, -np.sin(a) * sgn, np.cos(a)])
    out = np.array([0.0, np.cos(a) * sgn, np.sin(a)])
    fr = blend.frame(np.array([0.0, y0 * sgn, z0]) + out * 0.3, (1, 0, 0), up, -out)
    return fr, lambda g: sscale(translate(g, 0, -z0), 1, 1 / np.cos(a), origin=(0, 0))


def path_fr(hits, closed=False):
    """A frame that follows a path laid on the body, its rays running in along the body's normal.
    Open: 2D point (s, 0) is the spot s along the path. Closed: the loop is unrolled onto a circle
    of the same length, so 2D point (r cos t, r sin t) is the spot r t along it. hits: [(location,
    normal)] in order. Returns the frame, and the path's 2D points every step along it."""
    p = np.array([h[0] for h in hits]); n = np.array([h[1] for h in hits])
    if closed:
        p, n = np.vstack([p, p[:1]]), np.vstack([n, n[:1]])
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(p, axis=0), axis=1))])
    length = s[-1]
    rad = length / (2 * np.pi)

    def fr(a, b):
        t = (np.arctan2(b, a) % (2 * np.pi)) * rad if closed else a
        loc = np.array([np.interp(t, s, p[:, k]) for k in range(3)])
        nr = np.array([np.interp(t, s, n[:, k]) for k in range(3)]); nr /= np.linalg.norm(nr)
        return loc + nr * 0.05, -nr
    m = max(3, int(length / 0.008))
    if closed:
        ts = np.linspace(0, 2 * np.pi, m, endpoint=False)
        path = [(rad * np.cos(t), rad * np.sin(t)) for t in ts]
    else:
        path = [(t, 0.0) for t in np.linspace(0, length, m)]
    return fr, path


def hits_of(ref, fr, pts):
    out = []
    for a, b in pts:
        loc, nrm = blend.cast(ref, fr, a, b)
        if loc is not None:
            out.append((loc, nrm))
    return out


# ---------------------------------------------------------------- lamps
# Each round lamp is traced on a render looking straight at it, and the lamp is that outline
# pushed along the view (rows.lamp_prism): a pocket cut into the skin, a dark floor, and a clear
# lens lying just under the skin's own surface across it.
LAMP_DEPTH = 0.04


def lamp_volume(name, prism, sgn):
    """The closed prism between a lamp's two traced caps, mirrored to the right for sgn -1."""
    import bmesh
    near, far = (np.asarray(c, float) * [1, sgn, 1] for c in prism)
    n = len(near)
    faces = [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))]
    faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
    ob = blend.make(name, np.vstack([near, far]), faces, "housing")
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data); bm.free()
    return ob


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
        import bpy
        src = self.at(d)
        c = src.copy(); c.data = src.data.copy(); c.name = name
        bpy.context.scene.collection.objects.link(c)
        return c

    def drop(self):
        import bpy
        for c in self.made.values():
            bpy.data.objects.remove(c)
        self.made = {}


def lamp(skins, name, prism, lens_prism, sgn, lens_mat, cutters, out):
    import bpy
    pocket = lamp_volume(f"cut_{name}", prism, sgn)
    blend.boolean(pocket, skins.copy(LAMP_DEPTH, "deep"), "DIFFERENCE")
    cutters.append(pocket)
    lens = lamp_volume(f"{name}_glass", lens_prism, sgn)
    blend.boolean(lens, skins.copy(0.002, "outer"), "INTERSECT")
    blend.boolean(lens, skins.copy(0.006, "inner"), "DIFFERENCE")
    for p in lens.data.polygons:
        p.material_index = 0
    lens.data.materials[0] = bpy.data.materials[lens_mat]
    out.append(lens)


def lamp_fr(view, sgn):
    """A frame looking straight at the left (sgn 1) or right lamp, along its traced view."""
    import rows
    v = rows.LAMP_VIEWS[view]
    to_b = lambda q: np.array([-q[1], q[0] * sgn, q[2]])     # reference axes to build axes
    d, r, up = to_b(v["d"]), to_b(v["right"]), to_b(v["up"])
    if sgn < 0:
        r = -r
    mid = np.array(R[f"{view}_mid"])
    c = np.array([mid[2], mid[0] * sgn, mid[1]])
    return blend.frame(c - d * 0.4, r, up, d), c


def footprint(ob, fr):
    """The outline of a mesh seen along frame fr's rays, in fr's (u, v)."""
    o, u, v, _d = fr
    p = np.array([vv.co for vv in ob.data.vertices]) - o
    uv = np.column_stack([p @ u, p @ v])
    tris = [Polygon(uv[list(f.vertices)]) for f in ob.data.polygons]
    return unary_union([t.buffer(1e-5) for t in tris if t.area > 1e-10]).buffer(0)


def _floor(ref, name, g, fr, mat, d0, d1, step=0.004):
    """A flat part a little below the skin inside a lamp, laid along the lamp's view so it stays
    square to the lamp, not to the car."""
    return blend.lay(ref, name, g, fr, d0, d1, mat, step, along_ray=True)


def headlamps(ref, skins, cutters, out):
    """Our own interior: a chrome bowl filling the lens with one bright round lamp in its middle
    and a slim crescent lamp along its top."""
    for sgn, side in ((1, "l"), (-1, "r")):
        lamp(skins, f"head_{side}", R["head_prism"], R["head_lens"], sgn, "head_glass", cutters, out)
        fr, _c = lamp_fr("head", sgn)
        seen = footprint(out[-1], fr).buffer(-0.006)
        cx, cy = seen.centroid.x, seen.centroid.y
        rad = np.sqrt(seen.area / np.pi)
        core = Point(cx, cy - 0.1 * rad).buffer(0.42 * rad, 48)
        moon = seen.buffer(-0.01).difference(Point(cx, cy - 0.12 * rad).buffer(0.86 * rad, 64))
        moon = max(getattr(moon, "geoms", [moon]), key=lambda g: g.area)
        out += _floor(ref, f"head_reflector_{side}", seen.difference(core.buffer(0.004)).difference(moon.buffer(0.004)),
                      fr, "reflector", -0.03, -0.024)
        out += _floor(ref, f"lamp_head_{side}0", core, fr, "lamp_head", -0.03, -0.026)
        out += _floor(ref, f"lamp_head_{side}1", soft(moon, 0.003), fr, "lamp_head", -0.03, -0.026)


def taillamps(ref, skins, cutters, out):
    """Our own interior: a ring round the lens glows as the tail lamp, the round middle is the
    brake lamp."""
    for sgn, side in ((1, "l"), (-1, "r")):
        lamp(skins, f"tail_{side}", R["tail_prism"], R["tail_lens"], sgn, "tail_glass", cutters, out)
        fr, _c = lamp_fr("tail", sgn)
        seen = footprint(out[-1], fr).buffer(-0.008)
        c = seen.centroid
        rad = np.sqrt(seen.area / np.pi)
        brake = Point(c.x, c.y).buffer(0.55 * rad, 48)
        ring = seen.difference(brake.buffer(0.008))
        out += _floor(ref, f"lamp_tail_{side}", ring, fr, "lamp_tail", -0.03, -0.026)
        out += _floor(ref, f"lamp_brake_{side}", brake, fr, "lamp_brake", -0.03, -0.026)


# ---------------------------------------------------------------- front and rear
def _pocket(name, shape, mat, ref, cutters, out, fr, depth, sides=True, back=True):
    """A recess cut along fr's rays with a dark back, mirrored to both sides when sides."""
    for sgn, side in (((1, "l"), (-1, "r")) if sides else ((1, ""),)):
        g = shape if sgn > 0 else flip(shape)
        k = f"_{side}" if side else ""
        cutters += blend.lay(ref, f"cut_{name}{k}", g, fr, -depth, 0.006, mat, 0.006, along_ray=True)
        if back:
            out += blend.lay(ref, f"{name}_back{k}", g.buffer(-0.002), fr, -depth + 0.001, -depth + 0.005, mat, 0.006,
                             along_ray=True)


def front_parts(ref, cutters, out):
    """The amber indicator under each headlamp, the bumper's lower intake with two bars, and a
    round fog lamp at each end of it."""
    fr = front_fr()
    u0, z0, u1, z1 = R["indicator"]
    ind = soft(box(u0, z0, u1, z1), (z1 - z0) / 2 - 0.001)
    for sgn, side in ((1, "l"), (-1, "r")):
        g = ind if sgn > 0 else flip(ind)
        out += blend.lay(ref, f"indicator_{side}", g, fr, -0.001, 0.004, "amber_glass", 0.004)
    gr = half_shape(R["grille"], 0.01)
    _pocket("intake", gr, "grille", ref, cutters, out, fr, 0.05, sides=False)
    u, z, r = R["fog"]
    fog = Point(u, z).buffer(r * 1.25, 48)
    bars = unary_union([box(-1, zb - 0.006, 1, zb + 0.006) for zb in R["grille_bars"]]).intersection(gr.buffer(-0.012))
    bars = bars.difference(both(fog.buffer(0.006)))
    out += blend.lay(ref, "intake_bars", bars, fr, -0.03, -0.012, "chrome", 0.006, along_ray=True)
    for sgn, side in ((1, "l"), (-1, "r")):
        g = (lambda q: q) if sgn > 0 else flip
        out += blend.lay(ref, f"fog_ring_{side}", g(fog.difference(Point(u, z).buffer(r * 0.95, 48))), fr, -0.035, -0.02,
                         "chrome", 0.004, along_ray=True)
        out += blend.lay(ref, f"fog_back_{side}", g(Point(u, z).buffer(r * 0.97, 48)), fr, -0.045, -0.04, "reflector",
                         0.004, along_ray=True)
        out += blend.lay(ref, f"lamp_head_fog_{side}", g(Point(u, z).buffer(r * 0.45, 32)), fr, -0.04, -0.034,
                         "lamp_head", 0.004, along_ray=True)


def rear_parts(ref, cutters, out):
    """The high stop lamp under the rear glass, and a round rain lamp in a pocket low in each
    corner of the bumper."""
    fr = rear_fr()
    s = half_shape(R["stop"], 0.003)
    out += blend.lay(ref, "lamp_brake_high", s, fr, -0.001, 0.004, "lamp_brake", 0.004)
    u, z, r, rp = R["rain"]
    pk = Point(u, z).buffer(rp, 48)         # the rear frame's first axis is the car's left
    _pocket("rain_pocket", pk, "grille", ref, cutters, out, fr, 0.035)
    for sgn, side in ((1, "l"), (-1, "r")):
        g = Point(u * sgn, z).buffer(r, 40)
        out += blend.lay(ref, f"lamp_rain_{side}", g, fr, -0.03, -0.024, "lamp_rain", 0.004, along_ray=True)


# ---------------------------------------------------------------- glass and trim
SIDE_BAND = 0.016     # black frame round the side windows
GLASS_BAND = 0.024    # black band round the windscreen and the rear glass


def dlo_line():
    """The side window opening rebuilt from plain fits: its front edge straight, the arch over the
    top one even curve, and the belt straight."""
    d = np.array(R["dlo"], float)
    arch = d[1:-1]
    k = np.polyfit(arch[:, 0], arch[:, 1], 4)
    xs = np.linspace(arch[0, 0], arch[-1, 0], 48)
    top = np.column_stack([xs, np.polyval(k, xs)])
    xr = d[-1, 0]
    return np.vstack([[d[0, 0], belt(d[0, 0])], top, [[xr, belt(xr)]]])


def dlo_shapes():
    """The side window opening, its glass (door glass and quarter glass, split by the B-pillar's
    black) and the black band."""
    dlo = soft(poly(dlo_line()), 0.02)
    glass = dlo.buffer(-SIDE_BAND, join_style=1)
    b0, b1 = R["b_pillar"]
    glass = glass.difference(box(b0, -1, b1, 3))
    glass = unary_union([soft(p, 0.012) for p in getattr(glass, "geoms", [glass]) if p.area > 1e-4])
    return dlo, glass, dlo.difference(glass.buffer(-0.004))


def _window(s):
    """A window's outline in plan: its foot and head each one even curve across the car, its
    sides straight."""
    u = np.linspace(-1, 1, 25)
    foot = [(s["x_base_mid"] + (s["x_base_edge"] - s["x_base_mid"]) * t * t, s["w_base"] * t) for t in u]
    head = [(s["x_top_mid"] + (s["x_top_edge"] - s["x_top_mid"]) * t * t, s["w_top"] * t) for t in u[::-1]]
    return poly(foot + head)


def screen_shapes():
    o = soft(_window(R["screen"]), 0.05)
    g = o.buffer(-GLASS_BAND, join_style=1)
    return o, g, o.difference(g.buffer(-0.004))


def rear_shapes():
    o = soft(_window(R["rear_glass"]), 0.07)
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


def door_path():
    """The door's shut line as one path: down its front edge from the belt (straight, then along
    the wing's slanted end), along the bottom, and up its rear edge down the B-pillar's middle."""
    fd, rd = R["front_door"], R["rear_door"]
    pts = [list(fd[0]), list(fd[1])] + geom.catmull_open(fd[1:], 6).tolist()[1:] + [rd[1], rd[0]]
    return fillet(pts, R["shut_round"])


def lid_hits(ref):
    """The hatch's shut line on the body as one loop: across the roof at the rear glass's head
    from right to left (seen from above), down the left side beside the glass, in under the lamp
    and across over the bumper, and up the right side (seen from behind; the rear frame's first
    axis is the car's left)."""
    lid = R["lid"]
    u0 = lid[0][0]
    xr = R["lid_roof"]
    roof = hits_of(ref, top_fr(), [(xr, t * u0) for t in np.linspace(-1, 1, 13)])
    down_l = hits_of(ref, rear_fr(), [(p[0], p[1]) for p in lid[1:]])
    up_r = hits_of(ref, rear_fr(), [(-p[0], p[1]) for p in lid[-2:0:-1]])
    return roof + down_l + up_r


def _path_groove(ref, name, hits, closed=False):
    """A V groove along a path laid on the body through the traced hits, smoothed, its rays along
    the body's normal."""
    pts = np.array([h[0] for h in hits])
    nr = np.array([h[1] for h in hits])
    sm = geom.catmull_closed(pts, 6) if closed else geom.catmull_open(pts, 6)
    idx = [int(np.argmin(np.linalg.norm(pts - q, axis=1))) for q in sm]
    fr, path = path_fr([(q, nr[j]) for q, j in zip(sm, idx)], closed)
    return blend.groove(ref, name, path, fr, closed=closed)


def grooves(ref):
    """Shut lines as V grooves to cut: doors, bonnet, hatch."""
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        obs.append(blend.groove(ref, f"g_door_{side}", door_path(), side_fr(sgn)))
    left = hits_of(ref, top_fr(), [tuple(p) for p in R["bonnet_plan"]])
    left += hits_of(ref, front_fr(), [tuple(p) for p in R["bonnet_nose"]])
    right = [(loc * [1, -1, 1], nrm * [1, -1, 1]) for loc, nrm in left[-2::-1]]
    obs.append(_path_groove(ref, "g_bonnet", left + right))
    obs.append(_path_groove(ref, "g_hatch", lid_hits(ref), closed=True))
    return obs


# ---------------------------------------------------------------- doors
DOOR_DEPTH = 0.05     # below the cabin floor the door takes the sill this deep
DOOR_WALL = 0.20      # above it, the whole wall up to the belt, deep enough to reach the cabin's hollow
FRAME_DEPTH = 0.07    # the window frame above the belt, shallow so it never takes the roof's edge
DOOR_GAP = 0.0015     # half the gap round the door, inside the shut line's groove
DOOR_OPEN = 64.0      # degrees the door swings out
FLOOR = 0.24


def b_mid():
    return R["rear_door"][0][0]


def ahead_of(x):
    return box(x, -1, 5, 5)


def door_outline():
    """The left door seen from the side: inside its shut line, closed across the top along the
    belt, and the window's black frame round its glass back to the B-pillar's middle."""
    lower = Polygon(door_path()).buffer(0)
    dlo, _g, _t = dlo_shapes()
    frame = dlo.buffer(SIDE_BAND * 0.5, join_style=2).intersection(ahead_of(b_mid()))
    return unary_union([lower, frame]).buffer(0)


def door_hinge(sgn=1):
    """The hinge axis up the door's front edge, outside the skin, and the open angle. A negative
    angle about an axis pointing up swings the left door's back edge out. sgn: 1 left, -1 right."""
    o = door_outline()
    _x0, z0, _x1, _z1 = o.bounds
    xf = max(p[0] for p in R["front_door"][:2]) + 0.004
    zb = belt(xf)
    y = max(body.y_at(xf - 0.02, z) for z in np.linspace(z0 + 0.02, zb - 0.02, 20)) + 0.003
    return (xf, y * sgn, z0), (xf, y * sgn, zb), -DOOR_OPEN * sgn


def door(ref, shell, obs, sgn=1):
    """Cut the left (sgn 1) or right (sgn -1) door free of the body and its window trim; its
    glass, card and mirror go with it."""
    side = "l" if sgn > 0 else "r"
    o = door_outline()
    lower = Polygon(door_path()).buffer(0)
    regions = [(o.intersection(box(-5, -1, 5, FLOOR + 0.03)), DOOR_DEPTH),
               (lower.intersection(box(-5, FLOOR + 0.02, 5, 5)), DOOR_WALL),
               (o.difference(lower.buffer(-0.01)).intersection(box(-5, FLOOR + 0.02, 5, 5)), FRAME_DEPTH)]
    trim = [ob for ob in obs if ob.name == f"window_trim_{side}"]
    roles = {f"side_glass_{side}": "glass", f"side_glass_{side}_1": "glass",
             f"mirror_{side}": "mirror", f"mirror_glass_{side}": "mirror_glass", f"door_card_{side}": "card"}
    return blend.free_door(ref, side_fr(sgn), regions, DOOR_GAP, shell, trim, roles, o, name=f"door_{side}")


# ---------------------------------------------------------------- mirrors
MIRROR_SWEEP = 0.10   # radians the housing turns back from square to the car


def _pod(name, y0, y1, z0, z1, x0, x1, sgn, mat="body", r=0.03, taper=0.7):
    """A rounded housing: its face (y, z) a rounded box, lofted from x0 (the back, full size) to
    x1 (the nose, shrunk by taper towards its outer side), rounded along the way."""
    face = soft(box(y0, z0, y1, z1), r)
    ring = body.even(np.array(face.exterior.coords)[:-1], 96)
    cy, cz = y1, (z0 + z1) / 2
    rings = []
    for t in np.linspace(0, 1, 9):
        k = 1 - (1 - taper) * t ** 2
        rr = np.column_stack([cy + (ring[:, 0] - cy) * k, cz + (ring[:, 1] - cz) * k])
        rings.append(np.column_stack([np.full(96, x0 + (x1 - x0) * t), rr[:, 0] * sgn, rr[:, 1]]))
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
    """A rounded body-coloured housing on a black foot at the door's front corner, its glass in
    a recess in the housing's back."""
    m = R["mirror"]
    xb, xf = m["x_back"], m["x_front"]
    y0, y1, z0, z1 = m["y_in"] + 0.02, m["y_out"], m["z0"], m["z1"]
    root = body.y_at(xb + 0.04, belt(xb + 0.04) - 0.02) - 0.02
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        house = _pod(f"mirror_{side}", y0, y1, z0, z1, xb, xf, sgn, r=0.035)
        foot = _pod(f"mirror_foot_{side}", root, y0 + 0.03, belt(xb) - 0.03, z0 + 0.03, xb + 0.01, xf - 0.02, sgn,
                    mat="trim", r=0.012, taper=0.9)
        cut = _pod(f"cut_mirror_{side}", y0 + 0.008, y1 - 0.008, z0 + 0.008, z1 - 0.008, xb - 0.03, xb + 0.006, sgn,
                   r=0.027, taper=1.0)
        blend.boolean(house, cut, "DIFFERENCE")
        glass = _pod(f"mirror_glass_{side}", y0 + 0.0095, y1 - 0.0095, z0 + 0.0095, z1 - 0.0095, xb + 0.0015,
                     xb + 0.0045, sgn, "mirror_glass", r=0.025, taper=1.0)
        obs += [_sweep(house, sgn), _sweep(glass, sgn), _sweep(foot, sgn)]
    return obs


# ---------------------------------------------------------------- arches
def arches():
    """Arch cutters and the wheel tubs kept out of the cabin."""
    cut, tubs = [], []
    yc = W["outer_y"] - W["width"] / 2
    for x in (W["front_x"], W["rear_x"]):
        for sgn in (1, -1):
            v, f = geom.disc((x, (yc + 0.14) * sgn, W["r"]), (0, 1, 0), W["arch_r"], 0.56, n=96)
            cut.append(blend.make("arch", v, f, "interior"))
            v, f = geom.disc((x, (yc + 0.10) * sgn, W["r"]), (0, 1, 0), W["arch_r"] + 0.03, 0.62, n=96)
            tubs.append(blend.make("tub", v, f, "interior"))
    return cut, tubs


def tub_drums():
    yc = W["outer_y"] - W["width"] / 2
    return [(x, W["r"], W["arch_r"] + 0.03, yc + 0.10 - 0.31) for x in (W["front_x"], W["rear_x"])]


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


STEER = (0.36, 0.37, 0.84)     # the wheel's centre, in front of the left seat
STEER_TILT = 30.0               # degrees the wheel's face turns up from upright


def steering():
    """A round wheel on the left, square to its column, with three spokes."""
    from mathutils import Matrix
    r, t = 0.18, 0.016
    v, f = geom.lathe([(r - t + t * np.cos(a), t * np.sin(a)) for a in np.linspace(0, 2 * np.pi, 12, endpoint=False)], 64, axis="y")
    rim = blend.make("steering", v, f, "interior")
    spokes = [geom.box(-0.016, 0.016, -0.008, 0.008, -r + 0.01, -0.04), geom.box(-r + 0.01, -0.04, -0.008, 0.008, -0.016, 0.016),
              geom.box(0.04, r - 0.01, -0.008, 0.008, -0.016, 0.016)]
    obs = [rim] + [blend.make("spoke", *p, "interior") for p in spokes]
    v, f = geom.disc((0, 0, 0), (0, 1, 0), 0.06, 0.05, n=32)
    obs.append(blend.make("boss", v, f, "interior"))
    prof = [(0.018, -0.40)] + [(0.026, t) for t in np.linspace(-0.40, -0.02, 40)] + [(0.018, -0.02)]
    v, f = geom.lathe(prof, 24, axis="y")
    obs.append(blend.make("column", v, f, "interior"))
    wheel = blend.union_all(obs, "steering")
    wheel.data.transform(Matrix.Translation(STEER) @ Matrix.Rotation(np.radians(STEER_TILT), 4, "Y")
                         @ Matrix.Rotation(np.pi / 2, 4, "Z"))
    return wheel


def interior():
    """Firewall, a dash meeting it, two front seats, a rear bench, door cards, wheel and mirror."""
    x0 = R["screen"]["x_base_mid"] - 0.10
    bz = belt(0.0)
    obs = [_loft("firewall", x0 - 0.010, x0 - 0.004, lambda x: box(-0.9, FLOOR + 0.002, 0.9, 3), n=2),
           _loft("dash", x0 - 0.0125, 0.40, lambda x: box(-0.9, bz - 0.28, 0.9, bz - 0.10 + 0.12 * (1 - (x0 - x) / 0.7) + 0.04),
                 n=10, gap=0.03)]
    xf = R["front_door"][0][0] - 0.04
    xr = b_mid() + 0.04
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, FLOOR + 0.06, 1.2, belt(x) - 0.06) if s > 0 else box(-1.2, FLOOR + 0.06, -0.3, belt(x) - 0.06)).intersection(
            body.inside(x, 0.035 + 0.006)).difference(body.inside(x, 0.035 + 0.024))
        obs.append(_loft(f"door_card_{side}", xr, xf, clip, n=10))
        yc = 0.36 * sgn
        obs.append(_rbox(f"seat_base_{side}", -0.30, 0.18, yc - 0.23, yc + 0.23, FLOOR + 0.006, FLOOR + 0.17))
        obs.append(_rbox(f"seat_back_{side}", -0.44, -0.32, yc - 0.22, yc + 0.22, FLOOR + 0.171, FLOOR + 0.70, lean=0.22))
        obs.append(_rbox(f"headrest_{side}", -0.57, -0.47, yc - 0.12, yc + 0.12, FLOOR + 0.701, FLOOR + 0.81, r=0.035, lean=0.1))
    obs.append(_rbox("rear_base", -1.00, -0.66, -0.50, 0.50, FLOOR + 0.04, FLOOR + 0.20))
    obs.append(_rbox("rear_back", -1.12, -1.01, -0.48, 0.48, FLOOR + 0.201, FLOOR + 0.56, lean=0.35))
    obs.append(steering())
    xm = R["screen"]["x_top_mid"] - 0.04
    roof_in = body.inside(xm, 0.035).bounds[3]
    top = roof_in - 0.05
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.11, 0.11, top - 0.05, top, r=0.018, mat="interior")
    stem = _rbox("stem", xm - 0.016, xm - 0.004, -0.012, 0.012, top - 0.015, roof_in - 0.0008, r=0.004, mat="interior")
    obs.append(blend.union_all([m, stem], "mirror_rear"))
    return obs


# ---------------------------------------------------------------- wheels
SPOKES = 5


def wheels():
    """Five spoke alloys: a dished face cut into five broad spokes, a plain lip and a centre cap."""
    obs = []
    hw = W["width"] / 2
    yc = W["outer_y"] - hw
    rr = W["rim_r"]
    tyre = soft(box(rr, -hw, W["r"], hw), 0.03).union(box(rr, -hw + 0.012, rr + 0.03, hw - 0.012))
    tp = body.even(np.array(tyre.exterior.coords)[:-1], 96)
    rim = [(rr - 0.001, -hw + 0.012), (rr - 0.001, hw - 0.010), (rr - 0.010, hw - 0.003), (rr - 0.018, hw - 0.010),
           (rr - 0.024, hw - 0.024), (0.07, hw - 0.034), (0.05, hw - 0.022), (0.0, hw - 0.022), (0.0, hw - 0.05),
           (rr - 0.03, hw - 0.055), (rr - 0.02, -hw + 0.012)]
    # one window between two spokes: a rounded sector between radii r0 and r1
    r0, r1 = 0.085, rr - 0.032
    half = np.pi / SPOKES - np.radians(13)
    ang = np.linspace(-half, half, 24)
    sector = soft(Polygon([(r0 * np.cos(a), r0 * np.sin(a)) for a in ang * 0.55]
                          + [(r1 * np.cos(a), r1 * np.sin(a)) for a in ang[::-1]]), 0.014)
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
            for k in range(SPOKES):
                a = 2 * np.pi * k / SPOKES + np.pi / SPOKES
                c, s_ = np.cos(a), np.sin(a)
                ring = np.array(sector.exterior.coords)[:-1]
                pts = [(px * c - py * s_, px * s_ + py * c) for px, py in ring]
                rings = [np.array([(p[0], d, p[1]) for p in pts]) for d in (hw - 0.08, hw + 0.01)]
                v, f = geom.close_rings(rings)
                cutters.append(blend.make("window", place(v), f, "dark_metal"))
            blend.boolean(rimob, blend.union_all(cutters, "windows"), "DIFFERENCE")
            v, f = geom.disc((0, hw - 0.018, 0), (0, 1, 0), 0.045, 0.012, n=40)
            cap = blend.make(f"wheel_cap_{fb}{lr}", place(v), f, "chrome")
            v, f = geom.disc((0, hw - 0.09, 0), (0, 1, 0), rr - 0.04, 0.012, n=64)
            disc = blend.make(f"wheel_hub_{fb}{lr}", place(v), f, "dark_metal")
            obs += [rimob, cap, disc]
    return obs


# ---------------------------------------------------------------- badge and name
def _quad(p0, p1, p2, n=10):
    t = np.linspace(0, 1, n)[1:, None]
    return list(map(tuple, (1 - t) ** 2 * np.array(p0) + 2 * (1 - t) * t * np.array(p1) + t * t * np.array(p2)))


def badge_mark():
    """The Kaiser mark from src/renderer/assets/brands/kaiser (a ring and a hooked stem, cut by
    three slits), in the SVG's units, y down."""
    ring = Point(90, 100).buffer(52, 128).difference(Point(90, 100).buffer(26, 128))
    stem = [(128, 48), (154, 48), (154, 124)] + _quad((154, 124), (154, 136), (168, 136)) + \
        [(174, 136), (174, 152), (160, 152)] + _quad((160, 152), (128, 152), (128, 122))
    return unary_union([ring, Polygon(stem)])


def _to_unit(g):
    g = affine_transform(g, [1.15, 0, 0, 1.15, -23, -15])
    for x, y in ((96, 80), (104, 100), (112, 120)):
        g = g.difference(box(x, y, x + 110, y + 9))
    return g


def badge_shapes(style):
    """(outline, [(shape, material)]) of the badge, centred, 1 unit tall, y up. chrome: the mark
    in chrome; colour: the mark in the brand's near black, as the colour SVG draws it."""
    mark = _to_unit(badge_mark().buffer(1.5 if style == "chrome" else 0.0, join_style=1))
    mark = sscale(mark, 1, -1, origin=(0, 0))
    b = mark.bounds
    k = 1 / (b[3] - b[1])
    fix = lambda g: sscale(translate(g, -(b[0] + b[2]) / 2, -(b[1] + b[3]) / 2), k, k, origin=(0, 0))
    return fix(mark), [(fix(mark), "chrome" if style == "chrome" else "badge_black")]


def name_shape():
    """Firefly in the Kaiser brand font, 1 unit = its cap height, centred."""
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
    """The mark standing a little proud of the panel, on a thin dark base for the colour style."""
    outline, pieces = badge_shapes(style)
    objs = []
    for i, (g, m) in enumerate(pieces):
        objs += _flat(ref, g, size, fr, a, b, up, f"{name}_{i}", 0.0012, 0.0032, m, 0.002)
    return objs


def options(ref):
    """Badge and name options, named opt_<group>_<n>_<i>. Each lies flat on its panel. Odd
    numbers are the chrome badge, even the black badge, at the same spot."""
    nm = name_shape()
    up = (0, 0, 1)
    out = []

    def add(group, n, objs):
        for i, ob in enumerate(objs):
            ob.name = f"opt_{group}_{n}_{i}"
            out.append(ob)
    nose = R["bonnet_nose"]
    nose_z = nose[-1][1] + 0.09                       # on the bonnet's round front, over its edge
    lid_z = R["lid"][-1][1] + 0.16                    # the hatch's middle, above its bottom edge
    top_x = R["bonnet_plan"][-1][0] - 0.06
    for k, style in ((0, "chrome"), (1, "colour")):
        # front: on the bonnet's round front (the reference's spot), or up on the bonnet's top
        add("frontbadge", 1 + k, badge(ref, style, 0.085, front_fr(), 0, nose_z, up, "fb"))
        add("frontbadge", 3 + k, badge(ref, style, 0.075, top_fr(), top_x, 0, (-1, 0, 0), "fb"))
        # rear: on the hatch's middle between the lamps (the reference's spot), or higher, under the glass
        add("rearbadge", 1 + k, badge(ref, style, 0.085, rear_fr(), 0, lid_z, up, "rb"))
        add("rearbadge", 3 + k, badge(ref, style, 0.07, rear_fr(), 0, lid_z + 0.1, up, "rb"))
    cap = 0.028
    # name: on the hatch right of the badge (where the reference's trim badges sit), left of it,
    # or low on both doors behind the front wheel
    add("name", 1, _flat(ref, nm, cap, rear_fr(), 0.2, lid_z - 0.03, up, "nm", 0.0012, 0.0025, "chrome"))
    add("name", 2, _flat(ref, nm, cap, rear_fr(), -0.2, lid_z - 0.03, up, "nm", 0.0012, 0.0025, "chrome"))
    q = []
    xd = R["front_door"][0][0] - 0.22
    for sgn in (1, -1):
        q += _flat(ref, nm, cap * 1.3, side_fr(sgn), xd, R["door_bottom"] + 0.1, up, "nm", 0.0012, 0.0025, "chrome")
    add("name", 3, q)
    return out
