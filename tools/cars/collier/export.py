"""Builds the Collier in Blender's Python module and exports OBJ and MTL.

Run: python3 export.py [out_dir] [--game]
"""
import math
import sys
from pathlib import Path

import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from shapely.geometry import LineString, Point, Polygon, box as sbox
from shapely.ops import unary_union

import body
import decals
import parts
from body import ROWS

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "common"))
import blend as common  # noqa: E402  (door cutting and hinges)

ARGS = [a for a in sys.argv[1:] if not a.startswith("--")]
GAME = "--game" in sys.argv  # only Ivan's picks, axes for Unity
OUT = Path(ARGS[0] if ARGS else Path(__file__).with_name("out"))
NAME = "LibertyCollier"

MATERIALS = {
    # name: (diffuse rgb, specular, alpha, emissive rgb)
    "body": ((0.78, 0.79, 0.80), 0.6, 1.0, None),
    "trim": ((0.04, 0.04, 0.045), 0.7, 1.0, None),
    "grille": ((0.07, 0.07, 0.075), 0.3, 1.0, None),
    "chrome": ((0.80, 0.81, 0.83), 1.0, 1.0, None),
    "glass": ((0.10, 0.12, 0.14), 0.9, 0.45, None),
    "head_glass": ((0.85, 0.9, 0.95), 0.9, 0.22, None),
    "tail_glass": ((0.55, 0.04, 0.05), 0.9, 0.55, None),
    "housing": ((0.10, 0.10, 0.11), 0.4, 1.0, None),
    "reflector": ((0.70, 0.71, 0.73), 0.9, 1.0, None),
    "tyre": ((0.05, 0.05, 0.05), 0.1, 1.0, None),
    "rim": ((0.72, 0.73, 0.75), 0.7, 1.0, None),
    "hub": ((0.45, 0.45, 0.47), 0.8, 1.0, None),
    "mirror_glass": ((0.55, 0.6, 0.65), 1.0, 1.0, None),
    "interior": ((0.09, 0.09, 0.10), 0.2, 1.0, None),
    "name": ((0.80, 0.81, 0.83), 1.0, 1.0, None),
    "lamp_head": ((0.95, 0.95, 0.92), 0.9, 1.0, (1.0, 0.97, 0.9)),
    "lamp_tail": ((0.45, 0.03, 0.03), 0.6, 1.0, (0.55, 0.0, 0.0)),
    "lamp_brake": ((0.50, 0.03, 0.03), 0.6, 1.0, (1.0, 0.0, 0.0)),
}

FRONT_LIMIT = ROWS["wheel"]["front_x"] + ROWS["wheel"]["arch_r"] + 0.03
REAR_LIMIT = ROWS["wheel"]["rear_x"] - ROWS["wheel"]["arch_r"] - 0.03
HEAD_BACK = ROWS["head_back"]
TAIL_FRONT = ROWS["tail_front"]
BVH = []
SIDE_CUT = ROWS["mirror"]["y_in"] - 0.016
FRONT = lambda: ((6.0, 0, 0), (0, 1, 0), (0, 0, 1), (-1, 0, 0))
REAR = lambda: ((-6.0, 0, 0), (0, 1, 0), (0, 0, 1), (1, 0, 0))
TOP = lambda: ((0, 0, 6.0), (1, 0, 0), (0, 1, 0), (0, 0, -1))
SIDE = lambda s: ((0, 6.0 * s, 0), (1, 0, 0), (0, 0, 1), (0, -s, 0))


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for name, (col, *_rest) in MATERIALS.items():
        m = bpy.data.materials.new(name)
        m.diffuse_color = (*col, 1.0)


def make(name, verts, faces, mat="body", recalc=True):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(map(float, v)) for v in verts], [], [tuple(int(i) for i in f) for f in faces])
    me.validate()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    me.materials.append(bpy.data.materials[mat])
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    if recalc:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    return ob


def boolean(target, other, op):
    mod = target.modifiers.new("b", "BOOLEAN")
    mod.operation = op
    mod.object = other
    mod.solver = "EXACT"
    mod.material_mode = "TRANSFER"
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(other)


def prism(name, loop3d_a, loop3d_b, mat="body"):
    v, f = body._close([np.asarray(loop3d_a), np.asarray(loop3d_b)])
    return make(name, v, f, mat)


def cylinder_y(name, x, z, r, y0, y1, mat, n=72):
    a = np.linspace(0, 2 * np.pi, n, endpoint=False)
    ring = np.column_stack([x + r * np.cos(a), np.zeros(n), z + r * np.sin(a)])
    r0 = ring.copy(); r0[:, 1] = y0
    r1 = ring.copy(); r1[:, 1] = y1
    return make(name, *body._close([r0, r1]), mat)


def mirror_loop(loop):
    return [[-p[0], p[1]] for p in loop][::-1]


def full(half):
    """Close a left-half outline that starts and ends on the centre line into one loop."""
    h = [list(p) for p in half]
    other = [[-p[0], p[1]] for p in h[1:-1]][::-1]
    return h + other


def loops_of(geom):
    """Shapely polygon(s) -> list of loop lists (outer first, then holes) per polygon."""
    polys = [geom] if geom.geom_type == "Polygon" else list(geom.geoms)
    out = []
    for p in polys:
        if p.is_empty or p.area < 1e-6:
            continue
        p = p.simplify(0.0008)
        out.append([list(p.exterior.coords)[:-1]] + [list(i.coords)[:-1] for i in p.interiors])
    return out


def smooth_poly(pts, n=8):
    """Round a picked polyline into a smooth closed curve (Catmull-Rom)."""
    p = np.asarray(pts, float)
    m = len(p)
    out = []
    for i in range(m):
        p0, p1, p2, p3 = p[i - 1], p[i], p[(i + 1) % m], p[(i + 2) % m]
        for t in np.linspace(0, 1, n, endpoint=False):
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    return np.array(out)


def lay(bvh, name, geom, frame, d0, d1, mat, max_edge=0.015):
    """Front and rear shapes stay ahead of the front arches and behind the rear ones."""
    o = frame[0]
    low = ROWS["wheel"]["r"] + ROWS["wheel"]["arch_r"] + 0.04
    accept = ((lambda p: p[0] > FRONT_LIMIT or p[2] > low) if o[0] > 1 else
              (lambda p: p[0] < REAR_LIMIT or p[2] > low) if o[0] < -1 else None)
    """Lay a shapely shape onto the body as raised solids. Returns objects."""
    obs = []
    for i, loops in enumerate(loops_of(geom)):
        r = decals.raised(bvh, loops, frame, d0, d1, max_edge, accept)
        if r:
            obs.append(make(f"{name}_{i}" if i else name, r[0], r[1], mat, recalc=False))
        else:
            print("missed", name)
    return obs


def fit(bvh, poly, frame, accept):
    """Pull each outline point towards the middle until its ray lands where accept allows,
    so every layer of a lamp shares one outline that sits on the right panel."""
    o, uu, vv, d = (np.asarray(a, float) for a in frame)
    c = np.array(poly.centroid.coords[0])
    out = []
    for p in np.array(poly.exterior.coords)[:-1]:
        for k in np.linspace(1.0, 0.2, 81):
            q = c + (p - c) * k
            loc, _n, _i, _d = bvh.ray_cast(Vector(o + uu * q[0] + vv * q[1] - d * 0.5), Vector(d))
            if loc is not None and accept(np.array(loc)):
                break
        out.append(q)
    return Polygon(out).buffer(0)


def hit(bvh, frame, u, v):
    o, uu, vv, d = (np.asarray(a, float) for a in frame)
    start = o + uu * u + vv * v - d * 0.5
    loc, nrm, _i, _d = bvh.ray_cast(Vector(start), Vector(d))
    return np.array(loc), np.array(nrm)


# ---------------------------------------------------------------- body

def build_shell():
    v, f = body.loft(ROWS["stations"])
    shell = make("body", v, f)
    boolean(shell, make("spoiler", *parts.spoiler(), "body"), "UNION")
    return shell


def cut_arches(shell):
    w = ROWS["wheel"]
    for x in (w["front_x"], w["rear_x"]):
        for s in (1, -1):
            y0, y1 = sorted((0.50 * s, 1.2 * s))
            boolean(shell, cylinder_y("arch", x, w["r"], w["arch_r"], y0, y1, "trim"), "DIFFERENCE")


def mirrors(bvh, trim):
    """Body-coloured housing with glass in its flat back, on a black stalk and sail plate.
    The housing, stalk and sail are joined to the window surround they mount on."""
    m = ROWS["mirror"]
    obs = []
    for sgn, tag in ((1, "l"), (-1, "r")):
        house = make(f"mirror_{tag}", *parts.mirror_housing(sgn), "body")
        boolean(house, make("recess", *parts.mirror_recess(sgn), "trim"), "DIFFERENCE")
        obs.append(make(f"mirror_glass_{tag}", *parts.mirror_glass(sgn), "mirror_glass"))
        surface = lambda x, z: abs(hit(bvh, SIDE(sgn), x, z)[0][1])
        boolean(house, make("stalk", *parts.mirror_stalk(sgn, surface), "trim"), "UNION")
        boolean(trim, house, "UNION")
        # overlaps the surround by 3 mm and sits inside its thickness, so the union is clean
        sail = Polygon(smooth_poly(m["sail"], 6)).buffer(0).difference(side_outline().buffer(-0.003))
        for ob in lay(bvh, "sail", sail, SIDE(sgn), 0.0018, 0.0062, "trim"):
            boolean(trim, ob, "UNION")
    return obs


def groove(bvh, name, path2d, frame, width=0.006, depth=0.006):
    """V groove along a path laid on the body: a triangular tube to cut away."""
    pts = smooth_poly_open(path2d)
    hits = [hit(bvh, frame, *p) for p in pts]
    rings = []
    for i, (p, n) in enumerate(hits):
        a = hits[max(i - 1, 0)][0]; b = hits[min(i + 1, len(hits) - 1)][0]
        t = b - a; t /= np.linalg.norm(t)
        side = np.cross(n, t); side /= np.linalg.norm(side)
        rings.append(np.array([p + n * 0.004 + side * width / 2, p + n * 0.004 - side * width / 2, p - n * depth]))
    v, f = body._close(rings)
    return make(name, v, f, "trim")


def smooth_poly_open(pts, step=0.01):
    p = np.asarray(pts, float)
    seg = np.linalg.norm(np.diff(p, axis=0), axis=1)
    s = np.concatenate([[0], np.cumsum(seg)])
    n = max(2, int(s[-1] / step))
    t = np.linspace(0, s[-1], n)
    return np.column_stack([np.interp(t, s, p[:, 0]), np.interp(t, s, p[:, 1])])


def shut_lines(shell, bvh):
    for s in (1, -1):
        boolean(shell, groove(bvh, "door_shut", ROWS["door_shut"], SIDE(s)), "DIFFERENCE")
    # bonnet: one line from each headlight back to the cowl, then across just ahead of the
    # windscreen base, so no two grooves overlap
    sc = ROWS["screen"]
    hb = 0.66
    across = [(sc["x_base"] + (sc["x_side"] - sc["x_base"]) * (y / 0.69) ** 2 + 0.04, y)
              for y in np.linspace(hb, -hb, 40)]
    # sides start at the inner rear corner of each headlight, where the front line ends
    tips = [(HEAD_BACK + 0.02, 0.645 * s) for s in (1, -1)]
    boolean(shell, groove(bvh, "bonnet_rear", across, TOP()), "DIFFERENCE")
    for tip, end in ((tips[0], across[0]), (tips[1], across[-1])):
        boolean(shell, groove(bvh, "bonnet_side", [tip, end], TOP()), "DIFFERENCE")
    boolean(shell, groove(bvh, "bonnet_front", ROWS["bonnet_front"], FRONT()), "DIFFERENCE")
    boolean(shell, groove(bvh, "tailgate", ROWS["tailgate_shut"], REAR()), "DIFFERENCE")


# ---------------------------------------------------------------- glass and trim

BAND = 0.028   # black surround around every window


PILLAR = []   # cached A-pillar line: x, y, z where the greenhouse turns through 45 degrees


def pillar_line():
    """The A-pillar runs where the greenhouse surface faces 45 degrees between up and out.
    The windscreen stops there from above and the side glass stops there from the side."""
    if not PILLAR:
        sc = ROWS["screen"]
        for x in np.linspace(sc["x_top"] - 0.25, sc["x_side"] + 0.02, 48):
            for y in np.linspace(0.3, 0.86, 600):
                loc, n = hit(BVH[0], TOP(), x, y)
                if n[1] >= n[2]:
                    PILLAR.append(loc)
                    break
    return np.array(PILLAR)


def screen_outline():
    """Windscreen surround in plan. Its sides stop on the A-pillar line."""
    sc = ROWS["screen"]
    x_top, x_base, x_side = sc["x_top"], sc["x_base"], sc["x_side"]
    pl = pillar_line()
    edge = lambda x: float(np.interp(x, pl[:, 0], pl[:, 1])) - 0.004
    xs = np.linspace(x_side, x_top, 30)
    left = [(x, edge(x)) for x in xs]
    hb = edge(x_side)
    base = [(x_base + (x_side - x_base) * (y / hb) ** 2, y) for y in np.linspace(-hb, hb, 25)]
    right = [(x, -y) for x, y in left[::-1]]
    return Polygon(base + left[1:] + right[:-1]).buffer(0)


def side_outline():
    """Side glass surround in side view. Its front top edge rises to the A-pillar line, then
    blends into the drawn roof line behind the windscreen."""
    dlo = Polygon(smooth_poly(ROWS["dlo"], 6)).buffer(0)
    sc = ROWS["screen"]
    pl = pillar_line()
    top = ROWS["dlo"][1:10]   # front corner up over the roof line
    tx = np.array([p[0] for p in top])[::-1]
    tz = np.array([p[1] for p in top])[::-1]
    xs = np.linspace(sc["x_top"] - 0.2, sc["x_side"], 50)
    w = np.clip((xs - (sc["x_top"] - 0.2)) / 0.3, 0, 1)
    w = w * w * (3 - 2 * w)
    t = w * (np.interp(xs, pl[:, 0], pl[:, 2]) - 0.004) + (1 - w) * np.interp(xs, tx, tz)
    fill = Polygon(list(zip(xs, t)) + [(xs[-1], 0.94), (xs[0], 0.94)]).buffer(0)
    out = unary_union([dlo, fill]).buffer(0.01, join_style=1).buffer(-0.01, join_style=1)
    return out


def pillar_strip(bvh):
    """Black strip over the A-pillar corner, joining the windscreen and side glass surrounds."""
    sc = ROWS["screen"]
    pl = pillar_line()
    xs = np.linspace(sc["x_side"], sc["x_top"], 40)
    out = []
    for s, tag in ((1, "l"), (-1, "r")):
        grid = []
        for x in xs:
            y = float(np.interp(x, pl[:, 0], pl[:, 1]))
            z = float(np.interp(x, pl[:, 0], pl[:, 2]))
            a, _n = hit(bvh, TOP(), x, y - 0.014)
            b, _n = hit(bvh, SIDE(1), x, z - 0.014)
            row = []
            for k in np.linspace(0, 1, 9):
                q = a + (b - a) * k
                loc, nrm, _i, _d = bvh.find_nearest(Vector(q))
                row.append((np.array(loc), np.array(nrm)))
            grid.append(row)
        ni, nj = len(grid), len(grid[0])
        verts = []
        for d in (0.0062, 0.0018):   # inside the surround's thickness, so the union is clean
            for row in grid:
                for loc, nrm in row:
                    p = loc + nrm * d
                    verts.append((p[0], p[1] * s, p[2]))
        idx = lambda layer, i, j: layer * ni * nj + i * nj + j
        faces = []
        for i in range(ni - 1):
            for j in range(nj - 1):
                faces.append((idx(0, i, j), idx(0, i + 1, j), idx(0, i + 1, j + 1), idx(0, i, j + 1)))
                faces.append((idx(1, i, j + 1), idx(1, i + 1, j + 1), idx(1, i + 1, j), idx(1, i, j)))
        for i in range(ni - 1):
            for j in (0, nj - 1):
                faces.append((idx(0, i, j), idx(1, i, j), idx(1, i + 1, j), idx(0, i + 1, j)))
        for j in range(nj - 1):
            for i in (0, ni - 1):
                faces.append((idx(0, i, j), idx(0, i, j + 1), idx(1, i, j + 1), idx(1, i, j)))
        out.append(make(f"a_pillar_{tag}", verts, faces, "trim"))
    return out


def rear_outline():
    """Rear glass surround in rear view. Its sides follow a smooth fit of the line where the
    tailgate turns 45 degrees round the corner, so they run straight and even."""
    pts = np.array(ROWS["rear_glass"])
    z0, z1 = pts[:, 1].min(), pts[:, 1].max()
    zs = np.linspace(z0 + 0.01, z1 - 0.01, 24)
    line = []
    for z in zs:
        for y in np.linspace(0.2, 0.86, 400):
            loc, n = hit(BVH[0], REAR(), y, z)
            if n[1] >= -n[0]:
                line.append(y)
                break
    side = np.poly1d(np.polyfit(zs, line, 1))
    zz = np.linspace(z0, z1, 30)
    right = [(min(side(z) - 0.02, 0.59), z) for z in zz]
    left = [(-y, z) for y, z in right[::-1]]
    poly = Polygon(right + left).buffer(0)
    return poly.buffer(-0.045, join_style=1).buffer(0.045, join_style=1)


def glass_shapes():
    """Shapely outlines: surround, glass panes, per window, in their frame's 2D space."""
    dlo = side_outline()
    inner = dlo.buffer(-BAND, join_style=1)
    bp = ROWS["b_pillar"]
    xa, xb = min(p[0] for p in bp), max(p[0] for p in bp)
    sail = Polygon(smooth_poly(ROWS["mirror"]["sail"], 6)).buffer(0)
    door = inner.intersection(sbox(xb, -1, 5, 3)).difference(sail.buffer(0.008, join_style=1))
    quarter = inner.intersection(sbox(-5, -1, xa, 3))
    screen = screen_outline()
    rear = rear_outline()
    return {
        "side": (dlo, [door, quarter]),
        "screen": (screen, [screen.buffer(-BAND * 1.4, join_style=1)]),
        "rear": (rear, [rear.buffer(-BAND * 1.2, join_style=1)]),
    }


def merge(obs, name):
    """Union same-mounted pieces into one solid, so no two of them overlap or touch."""
    base = obs[0]
    base.name = name
    for ob in obs[1:]:
        boolean(base, ob, "UNION")
    return base


def windows(bvh):
    """One black surround for all the glass, with a slot under its inner edge that each pane
    sits in, clear of the trim on every face."""
    g = glass_shapes()
    trims, slots, glass = [], [], []
    for s, tag in ((1, "l"), (-1, "r")):
        dlo, panes = g["side"]
        trims += lay(bvh, f"side_trim_{tag}", dlo.difference(unary_union(panes)), SIDE(s), 0.0015, 0.0065, "trim")
        for i, p in enumerate(panes):
            slots += lay(bvh, "slot", p.buffer(0.005, join_style=1), SIDE(s), 0.0005, 0.0038, "trim")
            glass += lay(bvh, f"side_glass_{tag}{i}", p.buffer(0.004, join_style=1), SIDE(s), 0.0015, 0.003, "glass")
    for key, frame in (("screen", TOP()), ("rear", REAR())):
        outer, panes = g[key]
        trims += lay(bvh, f"{key}_trim", outer.difference(panes[0]), frame, 0.0015, 0.0065, "trim")
        slots += lay(bvh, "slot", panes[0].buffer(0.005, join_style=1), frame, 0.0005, 0.0038, "trim")
        glass += lay(bvh, f"{key}_glass", panes[0].buffer(0.004, join_style=1), frame, 0.0015, 0.003, "glass")
    trim = merge(trims + pillar_strip(bvh), "window_trim")
    for sl in slots:
        boolean(trim, sl, "DIFFERENCE")
    return trim, glass


def resample(poly, n):
    """n points round a polygon by arc length, starting at the bottom centre, counter-clockwise."""
    from shapely.geometry.polygon import orient
    ring = np.array(orient(poly, 1).exterior.coords)[:-1]
    start = int(np.argmin(np.abs(ring[:, 0]) + 10 * (ring[:, 1] - ring[:, 1].min())))
    ring = np.roll(ring, -start, axis=0)
    ring = np.vstack([ring, ring[:1]])
    seg = np.linalg.norm(np.diff(ring, axis=0), axis=1)
    s = np.concatenate([[0], np.cumsum(seg)])
    t = np.linspace(0, s[-1], n, endpoint=False)
    return np.column_stack([np.interp(t, s, ring[:, 0]), np.interp(t, s, ring[:, 1])])


def cabin(shell):
    """Hollow the cabin, open the windows and fit the interior."""
    st = ROWS["stations"]
    X = lambda px: px  # stations are in metres already
    rows = []
    for r in st:
        q = dict(r)
        rows.append(q)
    cav = []
    xs, cv = body.curves(st)
    for x0, x1, floor in ((0.90, -0.86, 0.30), (-0.86, -1.60, 0.70)):
        rings = []
        for x in np.linspace(x1, x0, 40):
            sec = body.section({k: float(cv[k](x)) for k in body.KEYS})
            inner = Polygon(sec).buffer(-0.03).intersection(sbox(-3, floor, 3, 5))
            rings.append(np.column_stack([np.full(64, x), resample(inner, 64)]))
        cav.append(make("cavity", *body._close(rings), "interior"))
    boolean(cav[0], cav[1], "UNION")
    boolean(shell, cav[0], "DIFFERENCE")
    g = glass_shapes()
    for s in (1, -1):
        for p in g["side"][1]:
            loop = np.array(p.buffer(-0.008).exterior.coords)[:-1]
            a = np.column_stack([loop[:, 0], np.full(len(loop), 0.3 * s), loop[:, 1]])
            b = a.copy(); b[:, 1] = SIDE_CUT * s   # stops short of the door mirror
            boolean(shell, prism("side_cut", a, b, "interior"), "DIFFERENCE")
    loop = np.array(g["screen"][1][0].buffer(-0.008).exterior.coords)[:-1]
    boolean(shell, prism("screen_cut", np.column_stack([loop, np.full(len(loop), 0.98)]),
                         np.column_stack([loop, np.full(len(loop), 1.7)]), "interior"), "DIFFERENCE")
    loop = np.array(g["rear"][1][0].buffer(-0.008).exterior.coords)[:-1]
    boolean(shell, prism("rear_cut", np.column_stack([np.full(len(loop), -2.3), loop]),
                         np.column_stack([np.full(len(loop), -1.25), loop]), "interior"), "DIFFERENCE")
    bits = [
        parts.box(0.62, 0.92, -0.68, 0.68, 0.62, 0.86),          # dash
        parts.box(0.55, 0.66, 0.30, 0.44, 0.66, 0.76),           # steering column
        parts.disc(np.array([0.50, 0.37, 0.78]), (-1, 0, 0.35), 0.18, 0.035),  # steering wheel
        parts.box(0.20, 0.33, -0.06, 0.06, 0.30, 0.55),          # centre console
    ]
    for s in (1, -1):
        y0, y1 = sorted((0.12 * s, 0.60 * s))
        bits += [parts.box(-0.05, 0.40, y0, y1, 0.30, 0.46),      # front seat base
                 parts.box(-0.20, -0.04, y0, y1, 0.30, 1.05),     # front seat back
                 ] + ([] if s > 0 else [parts.box(-0.05, 0.80, -0.68, -0.58, 0.32, 0.78)])  # right door card
    bits += [parts.box(-0.82, -0.40, -0.62, 0.62, 0.30, 0.48),   # rear bench
             parts.box(-0.92, -0.78, -0.62, 0.62, 0.30, 0.98)]   # rear bench back
    for i, (v, f) in enumerate(bits):
        boolean(shell, make(f"interior_{i}", v, f, "interior"), "UNION")
    door_card()


# ---------------------------------------------------------------- driver's door
DOOR_DEPTH = 0.05     # below the cabin floor the door takes the sill this deep
DOOR_WALL = 0.08      # above it, the whole wall: the cabin is hollow behind it
DOOR_GAP = 0.0015     # half the gap round the door, inside the shut line's groove
DOOR_OPEN = 65.0      # degrees the door swings out
CABIN_FLOOR = 0.30


def door_card():
    """The left door's card, its own part lying just inside the door's wall, so it swings
    with the door."""
    xs, cv = body.curves(ROWS["stations"])
    rings = []
    for x in np.linspace(-0.05, 0.78, 10):
        sec = Polygon(body.section({k: float(cv[k](x)) for k in body.KEYS})).buffer(0)
        band = sec.buffer(-0.033).difference(sec.buffer(-0.058)).intersection(sbox(0.3, 0.32, 1.2, 0.78))
        if band.geom_type != "Polygon":
            band = max(band.geoms, key=lambda g: g.area)
        r = resample(band, 48)
        rings.append(np.column_stack([np.full(len(r), x), r]))
    return make("door_card_l", *body._close(rings), "interior")


def door_outline():
    """The left door seen from the side: inside its shut line, closed straight across between
    the line's two ends, and the window frame round its glass back to the B-pillar's middle."""
    d = ROWS["door_shut"]
    bc = float(np.mean([p[0] for p in ROWS["b_pillar"]]))
    return unary_union([Polygon(d).buffer(0), side_outline().intersection(sbox(bc, -1, 5, 5))])


def mirror_footprint():
    """The mirror on the door seen from the side, with room: taken only outside the skin."""
    m = ROWS["mirror"]
    return sbox(m["x_rear"] - 0.05, m["z"] - m["h_out"] / 2 - 0.05, m["x_rear"] + m["depth"] + 0.08,
                m["z"] + m["h_out"] / 2 + 0.05)


def door_hinge(bvh):
    """The hinge axis up the door's front edge, outside the skin, and the open angle. Turning
    about an axis pointing up, a negative angle swings the door's back edge out."""
    d = np.array(ROWS["door_shut"])
    xf = float(d[:, 0].max()) + 0.004
    z0, z1 = float(d[:, 1].min()), float(d[:, 1].max())
    y = max(hit(bvh, SIDE(1), xf - 0.01, z)[0][1] for z in np.linspace(z0 + 0.02, z1, 20)) + 0.003
    return (xf, y, z0), (xf, y, z1), -DOOR_OPEN


def door(bvh, shell, trim):
    """Cut the left door free of the body and the window trim; its glass, card and mirror
    glass go with it. Returns the door's pieces."""
    o = door_outline()
    regions = [(o.intersection(sbox(-5, -1, 5, CABIN_FLOOR + 0.02)), DOOR_DEPTH),
               (o.intersection(sbox(-5, CABIN_FLOOR + 0.02, 5, 5)), DOOR_WALL), (mirror_footprint(), -0.004)]
    roles = {"side_glass_l0": "glass", "mirror_glass_l": "mirror", "door_card_l": "card"}
    fr = common.frame((0, 0.7, 0), (1, 0, 0), (0, 0, 1), (0, -1, 0))
    return common.free_door(bvh, fr, regions, DOOR_GAP, shell, [trim], roles, o)


# ---------------------------------------------------------------- lamps, grille, rear

def lamps(bvh):
    obs = []
    head = fit(bvh, Polygon(smooth_poly(ROWS["head"], 6)).buffer(0), FRONT(), lambda p: p[0] > HEAD_BACK)
    tail = fit(bvh, Polygon(smooth_poly(ROWS["tail"], 6)).buffer(0), REAR(), lambda p: p[0] < TAIL_FRONT)
    for s, tag in ((1, "l"), (-1, "r")):
        k = lambda g: g if s < 0 else Polygon([(-x, y) for x, y in g.exterior.coords])
        h = k(head)
        obs += lay(bvh, f"head_housing_{tag}", h.buffer(-0.002), FRONT(), 0.002, 0.0038, "housing", 0.006)
        cx, cz = h.centroid.x, h.centroid.y
        minx, miny, maxx, maxy = h.bounds
        # our own interior: a chrome reflector band and two projectors
        refl = h.buffer(-0.012).intersection(sbox(minx, cz - 0.03, maxx, maxy))
        obs += lay(bvh, f"head_reflector_{tag}", refl, FRONT(), 0.0046, 0.0056, "reflector", 0.006)
        inner = max(h.exterior.coords, key=lambda p: -p[1])  # lowest tip (inner end)
        for i, (fx, fz, r) in enumerate(((0.62, 0.30, 0.040), (0.30, 0.62, 0.034))):
            u = minx + (maxx - minx) * (fx if s < 0 else 1 - fx)
            v = miny + (maxy - miny) * fz
            # laid on the panel so its face follows the lens curve
            obs += lay(bvh, f"lamp_head_{tag}{i}", Point(u, v).buffer(r, 48), FRONT(), 0.0064, 0.0082, "lamp_head", 0.005)
        obs += lay(bvh, f"head_glass_{tag}", h, FRONT(), 0.0095, 0.0115, "head_glass", 0.006)
        t = k(tail)
        obs += lay(bvh, f"tail_housing_{tag}", t.buffer(-0.002), REAR(), 0.002, 0.0038, "housing", 0.006)
        tminx, tminy, tmaxx, tmaxy = t.bounds
        mid = tminy + (tmaxy - tminy) * 0.55
        inner_t = t.buffer(-0.01)
        obs += lay(bvh, f"lamp_tail_{tag}", inner_t.intersection(sbox(-5, mid + 0.004, 5, 5)), REAR(), 0.0046, 0.0068, "lamp_tail", 0.006)
        obs += lay(bvh, f"lamp_brake_{tag}", inner_t.intersection(sbox(-5, -5, 5, mid - 0.004)), REAR(), 0.0046, 0.0068, "lamp_brake", 0.006)
        obs += lay(bvh, f"tail_glass_{tag}", t, REAR(), 0.009, 0.011, "tail_glass", 0.006)
        # fog lamp with a chrome ring
        f = ROWS["fog"]
        p, n = hit(bvh, FRONT(), f["c"][0] * -s, f["c"][1])
        obs.append(make(f"fog_ring_{tag}", *parts.tube_ring(p + n * 0.006, n, f["r"] * 0.78, f["r"], 0.01), "chrome"))
        obs.append(make(f"lamp_fog_{tag}", *parts.disc(p + n * 0.006, n, f["r"] * 0.76, 0.008), "lamp_head"))
    b3 = Polygon(ROWS["brake3"])
    obs += lay(bvh, "lamp_brake_3", b3, REAR(), 0.002, 0.0045, "lamp_brake", 0.005)
    return obs


def grille(bvh):
    obs = []
    low = ROWS["wheel"]["r"] + ROWS["wheel"]["arch_r"] + 0.04
    mouth = fit(bvh, Polygon(smooth_poly(full(ROWS["mouth"]), 4)).buffer(0), FRONT(),
                lambda p: p[0] > FRONT_LIMIT or p[2] > low)
    f = ROWS["fog"]
    for s in (1, -1):   # the fog lamps sit in holes in the mouth
        mouth = mouth.difference(Point(f["c"][0] * s, f["c"][1]).buffer(f["r"] + 0.0015, 48))
    obs += lay(bvh, "mouth", mouth, FRONT(), 0.002, 0.0036, "grille", 0.008)
    for i, (z, y0) in enumerate(ROWS["slats"]):
        bar = sbox(y0, z - 0.004, -y0, z + 0.004)
        obs += lay(bvh, f"slat_{i}", bar, FRONT(), 0.0044 if i else 0.002, 0.0068 if i else 0.0045, "chrome", 0.008)
    chin = Polygon(smooth_poly(full(ROWS["chin"]), 4)).buffer(0)
    obs += lay(bvh, "chin", chin, FRONT(), 0.0025, 0.0045, "grille", 0.005)
    band = Polygon(smooth_poly(full(ROWS["rear_band"]), 4)).buffer(0)
    obs += lay(bvh, "rear_band", band, REAR(), 0.002, 0.0045, "body", 0.008)
    for s, tag in ((1, "l"), (-1, "r")):
        strip = Polygon(ROWS["strip"]).buffer(0.002, join_style=1)
        # split at the door gap so the shut line runs through the strip
        strip = strip.difference(LineString(smooth_poly_open(ROWS["door_shut"])).buffer(0.0035))
        obs += lay(bvh, f"side_strip_{tag}", strip, SIDE(s), 0.0015, 0.006, "body")
    return obs


def wheels():
    obs = []
    w = ROWS["wheel"]
    for x in (w["front_x"], w["rear_x"]):
        for s in (1, -1):
            yc = s * (w["outer_y"] - w["width"] / 2)
            tag = f"{'f' if x > 0 else 'r'}{'l' if s > 0 else 'r'}"
            made = {}
            for name, (v, f, mat) in parts.wheel_parts().items():
                v = v.copy(); v[:, 1] *= s
                if s < 0:
                    f = [t[::-1] for t in f]
                v += np.array([x, yc, w["r"]])
                made[name] = make(f"{name}_{tag}", v, f, mat)
            rim = made["rim"]
            rim.name = f"wheel_rim_{tag}"
            # spokes overlap at the hub, so each one joins on its own
            sp = made.pop("spokes")
            sv = np.array([tuple(v.co) for v in sp.data.vertices])
            sf = [tuple(p.vertices) for p in sp.data.polygons]
            bpy.data.objects.remove(sp)
            boolean(rim, made["hub"], "UNION")
            for i in range(0, len(sf), 6):
                idx = sorted({j for f in sf[i:i + 6] for j in f})
                remap = {j: k for k, j in enumerate(idx)}
                boolean(rim, make("spoke", sv[idx], [tuple(remap[j] for j in f) for f in sf[i:i + 6]], "rim"), "UNION")
            made["tyre"].name = f"wheel_tyre_{tag}"
            obs += [made["tyre"], rim]
    return obs


def surface_frame(p, n, right):
    """Badge frame lying flat on the surface at p, so a sloped panel doesn't stretch it."""
    n = np.asarray(n, float); n /= np.linalg.norm(n)
    up = np.array([0, 0, 1.0]) - n * n[2]; up /= np.linalg.norm(up)
    return tuple(p + n * 0.3), tuple(up), tuple(right), tuple(-n)


def options(bvh):
    """Badge and name placements for Ivan to toggle between in the viewer."""
    obs = []
    picks = ROWS["picks"]

    def add(group, n, pieces):
        if GAME and picks[group] != n:
            return
        for i, (v, f, mat) in enumerate(pieces):
            label = f"{group}_{i}" if GAME else f"opt_{group}_{n}_{i}"
            obs.append(make(label, v, f, mat, recalc=False))

    o = ROWS["opts"]
    # Front badge: 1 nose (the reference's spot), 2 bonnet front.
    z = o["nose_z"]; p, n = hit(bvh, FRONT(), 0.0, z)
    add("frontbadge", 1, decals.badge(bvh, *surface_frame(p, n, (0, 1, 0)), 0.075))
    x = o["bonnet_x"]
    add("frontbadge", 2, decals.badge(bvh, (x, 0, 2.5), (1, 0, 0), (0, 1, 0), (0, 0, -1), 0.075))
    # Rear badge: 1 tailgate top centre (reference), 2 tailgate middle.
    for n_, z in ((1, o["rear_z1"]), (2, o["rear_z2"])):
        p, n = hit(bvh, REAR(), 0.0, z)
        add("rearbadge", n_, decals.badge(bvh, *surface_frame(p, n, (0, -1, 0)), 0.07))
    # Name: 1 tailgate lower left (reference), 2 tailgate lower right, 3 rear quarters.
    cap = 0.045
    _, length = decals.name(bvh, (0, 0, 0), (1, 0, 0), (0, 0, 1), (0, 0, -1), cap)
    for n_, yc in ((1, o["name_y"]), (2, -o["name_y"])):
        z = o["name_z"]
        p, _n = hit(bvh, REAR(), yc, z)
        add("name", n_, decals.name(bvh, (p[0] - 0.3, yc + length / 2, z), (0, -1, 0), (0, 0, 1), (1, 0, 0), cap)[0])
    for s in (1, -1):
        xc, z = o["quarter_x"], o["quarter_z"]
        add("name", 3, decals.name(bvh, (xc + s * length / 2, 2.0 * s, z), (-s, 0, 0), (0, 0, 1), (0, -s, 0), cap)[0])
    return obs


def tidy(ob):
    """Weld the hairline seams the booleans leave and close any pinhole they open."""
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=2e-5)
    for _ in range(5):   # needle triangles with no area: collapse their shortest edge
        bm.faces.ensure_lookup_table()
        flat = [f for f in bm.faces if f.is_valid and f.calc_area() < 1e-10]
        if not flat:
            break
        edges = {min(f.edges, key=lambda e: e.calc_length()) for f in flat}
        bmesh.ops.collapse(bm, edges=[e for e in edges if e.is_valid])
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    # a slit too thin to fill closes by collapsing its short side
    bmesh.ops.collapse(bm, edges=[e for e in bm.edges if e.is_boundary and e.calc_length() < 5e-4])
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.delete(bm, geom=[e for e in bm.edges if not e.link_faces], context="EDGES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(ob.data); bm.free()


def smooth(ob, angle=35):
    me = ob.data
    for p in me.polygons:
        p.use_smooth = True
    me.set_sharp_from_angle(angle=math.radians(angle))


def main():
    reset()
    shell = build_shell()
    cut_arches(shell)
    bvh = BVHTree.FromObject(shell, bpy.context.evaluated_depsgraph_get())
    BVH.append(bvh)
    obs = [shell]
    trim, glass = windows(bvh)
    obs += [trim] + glass
    obs += lamps(bvh)
    obs += grille(bvh)
    obs += mirrors(bvh, trim)
    obs += options(bvh)
    if "--fast" not in sys.argv:
        shut_lines(shell, bvh)
    if "--fast" not in sys.argv:
        cabin(shell)
    for name, (v, f, mat) in parts.exhaust_parts().items():
        boolean(shell, make(name, v, f, mat), "UNION")   # pipes run back into the bumper
    obs += wheels()
    if "--fast" not in sys.argv:
        pieces = door(bvh, shell, trim)
        print("door cut free:", [o.name for o in pieces])
        obs += [p for p in pieces if p not in obs]
    for ob in obs:
        tidy(ob)
        smooth(ob)
    OUT.mkdir(parents=True, exist_ok=True)
    tris = sum(sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob in obs)
    print("triangles", tris)
    if GAME:
        # Unity reads OBJ as right-handed and mirrors x: write forward as +z, left as +x, up as +y.
        from mathutils import Matrix
        m = Matrix(((0, 1, 0, 0), (0, 0, 1, 0), (1, 0, 0, 0), (0, 0, 0, 1)))
        for ob in obs:
            ob.data.transform(m)
        axes = dict(forward_axis="Y", up_axis="Z")
    else:
        axes = dict(forward_axis="NEGATIVE_Z", up_axis="Y")
    bpy.ops.wm.obj_export(filepath=str(OUT / f"{NAME}.obj"), export_selected_objects=False,
                          export_materials=True, export_normals=True, export_uv=False,
                          export_triangulated_mesh=True, path_mode="RELATIVE", **axes)
    write_mtl(OUT / f"{NAME}.mtl")
    if GAME:
        common.write_doors(OUT / f"{NAME}.doors.json", {"door_l": door_hinge(BVH[0])})


def write_mtl(path):
    """Own MTL so lamp emissive and glass alpha survive."""
    used = set()
    for line in path.with_suffix(".obj").read_text().splitlines():
        if line.startswith("usemtl "):
            used.add(line.split()[1])
    out = []
    for name in sorted(used):
        col, spec, alpha, emit = MATERIALS[name]
        out += [f"newmtl {name}", "Kd {:.3f} {:.3f} {:.3f}".format(*col),
                "Ks {0:.2f} {0:.2f} {0:.2f}".format(spec * 0.5), "Ns 200", f"d {alpha:.2f}"]
        out.append("Ke {:.2f} {:.2f} {:.2f}".format(*(emit or (0, 0, 0))))
        out.append("")
    path.write_text("\n".join(out))


if __name__ == "__main__":
    main()
