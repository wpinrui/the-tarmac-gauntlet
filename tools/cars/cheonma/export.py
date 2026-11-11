"""Builds the Cheonma in Blender's Python module and exports OBJ and MTL.

Run: python3 export.py [out_dir]
"""
import math
import sys
from pathlib import Path

import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

import body
import parts
from body import ROWS

ARGS = [a for a in sys.argv[1:] if not a.startswith("--")]
GAME = "--game" in sys.argv  # only Ivan's picks, axes for Unity
OUT = Path(ARGS[0] if ARGS else Path(__file__).with_name("out"))
NAME = "CheonmaCX24Si"

MATERIALS = {
    # name: (diffuse rgb, specular, alpha, emissive rgb)
    "body": ((0.80, 0.81, 0.83), 0.6, 1.0, None),
    "carbon": ((0.06, 0.06, 0.07), 0.3, 1.0, None),
    "glass": ((0.10, 0.12, 0.14), 0.9, 0.55, None),
    "head_glass": ((0.85, 0.9, 0.95), 0.9, 0.25, None),
    "housing": ((0.12, 0.12, 0.13), 0.4, 1.0, None),
    "tyre": ((0.05, 0.05, 0.05), 0.1, 1.0, None),
    "rim": ((0.08, 0.08, 0.09), 0.5, 1.0, None),
    "hub": ((0.55, 0.55, 0.57), 0.8, 1.0, None),
    "mirror_glass": ((0.55, 0.6, 0.65), 1.0, 1.0, None),
    "interior": ((0.10, 0.10, 0.11), 0.2, 1.0, None),
    "badge_navy": ((0.11, 0.165, 0.227), 0.6, 1.0, None),
    "badge_amber": ((0.89, 0.64, 0.23), 0.8, 1.0, None),
    "badge_white": ((0.97, 0.97, 0.97), 0.6, 1.0, None),
    "badge_red": ((0.78, 0.24, 0.16), 0.6, 1.0, None),
    "name": ((0.11, 0.165, 0.227), 0.6, 1.0, None),
    "lamp_head": ((0.95, 0.95, 0.92), 0.9, 1.0, (1.0, 0.97, 0.9)),
    "lamp_tail": ((0.35, 0.02, 0.02), 0.6, 1.0, (0.55, 0.0, 0.0)),
    "lamp_brake": ((0.40, 0.02, 0.02), 0.6, 1.0, (1.0, 0.0, 0.0)),
    "lamp_rain": ((0.40, 0.02, 0.02), 0.6, 1.0, (1.0, 0.0, 0.0)),
}


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


def cylinder_y(name, x, z, r, y0, y1, n=64):
    a = np.linspace(0, 2 * np.pi, n, endpoint=False)
    ring = np.column_stack([x + r * np.cos(a), np.zeros(n), z + r * np.sin(a)])
    r0 = ring.copy(); r0[:, 1] = y0
    r1 = ring.copy(); r1[:, 1] = y1
    v, f = body._close([r0, r1])
    return make(name, v, f)


def build_body():
    pcs = body.pieces()
    shell = make("body", *pcs.pop("nose"))
    for name, (v, f) in pcs.items():
        boolean(shell, make(name, v, f), "UNION")
    return shell


def cut_arches(shell):
    w = ROWS["wheel"]
    for x in (w["front_x"], w["rear_x"]):
        for s in (1, -1):
            y0, y1 = sorted((0.585 * s, 1.2 * s))
            boolean(shell, cylinder_y("arch", x, w["r"], w["r"] + 0.025, y0, y1), "DIFFERENCE")


def paint_glass(ob):
    """Canopy windscreen and side windows take the glass material."""
    me = ob.data
    me.materials.append(bpy.data.materials["glass"])
    gi = len(me.materials) - 1
    X, Z = ROWS_X, ROWS_Z
    for p in me.polygons:
        c, n = p.center, p.normal
        if abs(c.y) > 0.33 or c.z < 0.60:
            continue
        screen = X(1180) < c.x < X(1468) and c.z > 0.80 and n.x > 0.15 and n.z > 0.2
        side = X(990) < c.x < X(1185) and Z(300) < c.z < Z(218) and abs(n.y) > 0.6
        if screen or side:
            p.material_index = gi


from rows import X as ROWS_X, Z as ROWS_Z  # noqa: E402

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "common"))
import blend as common  # noqa: E402  (door cutting and hinges)
import geom as cgeom  # noqa: E402


def place_wheels():
    w = ROWS["wheel"]
    obs = []
    for x in (w["front_x"], w["rear_x"]):
        for s in (1, -1):
            yc = s * (w["outer_y"] - w["width"] / 2)
            for name, (v, f, mat) in parts.wheel_parts().items():
                v = v.copy()
                v[:, 1] *= s
                if s < 0:
                    f = [t[::-1] for t in f]
                v += np.array([x, yc, w["r"]])
                obs.append(make(f"{name}_{'f' if x > 0 else 'r'}{'l' if s > 0 else 'r'}", v, f, mat))
    return obs


def projected_slab(bvh, name, loop2d, ray, off, thick, mat, flip=False, scale=1.0):
    """Slab lying on the body: a 2D outline, mapped to rays by ray(u, v) -> (origin, dir),
    filled with concentric rings, offset off along the hit normal, thickness thick."""
    loop = np.asarray(loop2d, float)
    cen = loop.mean(0)
    loop = cen + (loop - cen) * scale
    rings = [cen + (loop - cen) * k for k in np.linspace(1.0, 0.02, 12)]
    layers = []
    for depth in (off, off + thick):
        pts = []
        for ring in rings:
            for u, v in ring:
                o, d = ray(u, v)
                loc, nrm, _i, _d = bvh.ray_cast(Vector(o), Vector(d))
                pts.append(np.array(loc) + np.array(nrm) * depth)
        layers.append(np.array(pts).reshape(len(rings), len(loop), 3))
    v, f = sheet(layers)
    if flip:
        f = [t[::-1] for t in f]
    return make(name, v, f, mat)


def projected_ring(bvh, name, loop2d, ray, off, thick, mat, flip=False, outer=1.08):
    """Bezel ring between the outline and the outline scaled by outer."""
    loop = np.asarray(loop2d, float)
    cen = loop.mean(0)
    rings = [cen + (loop - cen) * k for k in (outer, 1.0)]
    layers = []
    for depth in (off, off + thick):
        pts = []
        for ring in rings:
            for u, v in ring:
                o, d = ray(u, v)
                loc, nrm, _i, _d = bvh.ray_cast(Vector(o), Vector(d))
                pts.append(np.array(loc) + np.array(nrm) * depth)
        layers.append(np.array(pts).reshape(2, len(loop), 3))
    m = len(loop)
    verts = np.concatenate([layers[0].reshape(-1, 3), layers[1].reshape(-1, 3)])
    faces = []
    for j in range(m):
        k = (j + 1) % m
        o0, i0, o1, i1 = j, m + j, 2 * m + j, 3 * m + j
        ok, ik, o1k, i1k = k, m + k, 2 * m + k, 3 * m + k
        faces += [(o0, i0, ik, ok)[::-1], (o1, o1k, i1k, i1)[::-1], (o0, ok, o1k, o1), (i0, i1, i1k, ik)]
    if flip:
        faces = [t[::-1] for t in faces]
    return make(name, verts, faces, mat)


def densify(poly, n=16):
    poly = np.asarray(poly, float)
    out = []
    for i in range(len(poly)):
        a, b = poly[i], poly[(i + 1) % len(poly)]
        for t in np.linspace(0, 1, n, endpoint=False):
            out.append(a + (b - a) * t)
    return np.array(out)


def headlights(shell, bvh):
    """Housing, lamps and glass on each pod's sloped front face."""
    h = ROWS["head"]
    (xt, zt), (xb, zb) = h["face_top"], h["face_bot"]
    sc, cx = h["yf"]["scale"], h["yf"]["centre"]
    obs = []
    for side, tag in ((1, "l"), (-1, "r")):
        def ray(px, py, side=side):
            y = (cx - px) * sc * side
            t = (py - h["py_top"]) / (h["py_bot"] - h["py_top"])
            return (3.0, y, zt + (zb - zt) * t), (-1, 0, 0)
        loop = densify(h["frame_px"])
        obs.append(projected_ring(bvh, f"head_frame_{tag}", loop, ray, 0.0015, 0.019, "carbon", side < 0, 1.08))
        obs.append(projected_slab(bvh, f"head_housing_{tag}", loop, ray, 0.0015, 0.0055, "housing", side < 0, 0.985))
        obs.append(projected_slab(bvh, f"head_glass_{tag}", loop, ray, 0.022, 0.003, "head_glass", side < 0, 1.04))
        for i, (px, py, r) in enumerate(h["lamps_px"] + [h["led_px"]]):
            o, d = ray(px, py)
            loc, nrm, _i, _d = bvh.ray_cast(Vector(o), Vector(d))
            p, n = np.array(loc), np.array(nrm)
            obs.append(make(f"lamp_head_{tag}{i}", *parts.disc(p + n * 0.0115, n, r * sc, 0.004), "lamp_head"))
    return obs


def windows(bvh):
    """Windscreen and side windows as glass slabs lying on the canopy."""
    X, Z = ROWS_X, ROWS_Z
    g = ROWS["glass"]
    obs = []
    screen = densify([[X(px), y] for px, y in g["screen_plan"]])
    obs.append(projected_slab(bvh, "windscreen", screen, lambda x, y: ((x, y, 3.0), (0, 0, -1)), 0.003, 0.006, "glass"))
    for side, tag in ((1, "l"), (-1, "r")):
        win = densify([[X(px), Z(py)] for px, py in g["side_px"]])
        obs.append(projected_slab(bvh, f"side_window_{tag}", win,
                                  lambda x, z, s=side: ((x, 3.0 * s, z), (0, -s, 0)), 0.003, 0.006, "glass", side < 0))
    return obs


def sheet(layers):
    """Closed slab from two layers of concentric rings (outer ring first)."""
    nr, m, _ = layers[0].shape
    verts = np.concatenate([layers[0].reshape(-1, 3), layers[1].reshape(-1, 3)])
    off = nr * m
    faces = []
    for L, flip in ((0, True), (1, False)):
        base = L * off
        for r in range(nr - 1):
            for j in range(m):
                k = (j + 1) % m
                q = (base + r * m + j, base + r * m + k, base + (r + 1) * m + k, base + (r + 1) * m + j)
                faces.append(q[::-1] if flip else q)
    for j in range(m):
        k = (j + 1) % m
        faces.append((j, k, off + k, off + j))
    # inner caps (tiny last ring) close with a fan
    for L, flip in ((0, True), (1, False)):
        c = len(verts)
        ring = layers[L][-1]
        verts = np.vstack([verts, ring.mean(0)])
        base = L * off + (nr - 1) * m
        for j in range(m):
            k = (j + 1) % m
            t = (base + j, base + k, c)
            faces.append(t[::-1] if flip else t)
    return verts, faces


def smooth(ob, angle=35):
    me = ob.data
    for p in me.polygons:
        p.use_smooth = True
    me.set_sharp_from_angle(angle=math.radians(angle))


def solid(name, v, f, mat):
    return make(name, v, f, mat)


def prism(name, loop3d_a, loop3d_b, mat="body"):
    v, f = body._close([np.asarray(loop3d_a), np.asarray(loop3d_b)])
    return make(name, v, f, mat)


def scaled(loop, k):
    loop = np.asarray(loop, float)
    c = loop.mean(0)
    return c + (loop - c) * k


def cockpit(shell):
    """Hollow the cockpit, open the windows and fit the interior."""
    X, Z = ROWS_X, ROWS_Z
    can = ROWS["canopy"]
    xs = np.array([r["x"] for r in can])
    from scipy.interpolate import PchipInterpolator
    zt = PchipInterpolator(xs, [r["zt"] for r in can])
    hw = PchipInterpolator(xs, [r["yo"] for r in can])
    rows = []
    for x in np.linspace(X(965), X(1430), 9):
        w = float(hw(x)) - 0.025
        rows.append(dict(x=float(x), yi=-w, yo=w, zb=0.25, zt=float(zt(x)) - 0.025,
                         rti=0.18, rto=0.18, rb=0.03))
    boolean(shell, make("cavity", *body.loft(rows, step=0.04), "interior"), "DIFFERENCE")
    g = ROWS["glass"]
    plan = scaled(densify([[X(px), y] for px, y in g["screen_plan"]]), 0.97)
    lo = np.column_stack([plan, np.full(len(plan), 0.72)])
    hi = np.column_stack([plan, np.full(len(plan), 1.3)])
    boolean(shell, prism("screen_cut", lo, hi, "interior"), "DIFFERENCE")
    for s in (1, -1):
        side = scaled(densify([[X(px), Z(py)] for px, py in g["side_px"]]), 0.97)
        a = np.column_stack([side[:, 0], np.full(len(side), 0.0), side[:, 1]])
        b = a.copy(); b[:, 1] = 1.0 * s
        boolean(shell, prism("side_cut", a, b, "interior"), "DIFFERENCE")
    # Interior: seat, dash, wheel on its column, door cards, mirror. All fused to the tub.
    bits = [
        parts.box(-0.10, 0.30, -0.05, 0.25, 0.22, 0.36),          # seat base, left of centre by the door
        parts.box(-0.16, -0.03, -0.05, 0.25, 0.22, 0.74),         # seat back on the bulkhead
        parts.box(0.80, 1.00, -0.24, 0.24, 0.50, 0.60),           # dash
        parts.box(0.60, 0.82, 0.04, 0.12, 0.52, 0.58),            # steering column
        parts.disc(np.array([0.60, 0.08, 0.57]), (-1, 0, 0.25), 0.12, 0.03),  # steering wheel
        parts.box(0.00, 0.75, 0.24, 0.32, 0.36, 0.52),            # door card left
        parts.box(0.00, 0.75, -0.32, -0.24, 0.36, 0.52),          # door card right
        parts.box(0.36, 0.39, -0.05, 0.05, 0.90, 0.93),           # interior mirror
        parts.box(0.37, 0.38, -0.006, 0.006, 0.92, 1.04),         # mirror stem into the roof
    ]
    for i, (v, f) in enumerate(bits):
        boolean(shell, make(f"interior_{i}", v, f, "interior"), "UNION")


# ---------------------------------------------------------------- driver's door
DOOR_SIDE = [(-0.12, 0.62), (0.514, 0.62), (0.354, 1.0), (-0.12, 1.0)]   # seen from the side
DOOR_IN, DOOR_OUT = 0.26, 0.45    # the canopy wall lies between these, clear of the seat and mirror
DOOR_GAP = 0.0015                 # half the gap round the door
DOOR_ROUND = 0.04                 # its back corners are rounded
# hinges, each (p0, p1, degrees): up and forward on a raked axis along the door's front edge,
# as the reference's door; or out to the side on an upright axis
DOOR_STYLES = {
    "Up and forward": ((0.52, 0.31, 0.62), (0.36, 0.22, 1.0), -75.0),
    "Out to the side": ((0.52, 0.31, 0.62), (0.52, 0.31, 1.0), -65.0),
}
DOOR_PICK = "Up and forward"


def door_outline():
    """The door seen from the side, its back corners rounded and its hinge edge kept straight."""
    from shapely.geometry import Polygon as P, box
    g = P(DOOR_SIDE)
    return g.buffer(-DOOR_ROUND, join_style=1).buffer(DOOR_ROUND, join_style=1).union(g.intersection(box(0.2, 0, 2, 2)))


def _door_solid(grow):
    g = door_outline().buffer(grow, join_style=2)
    ring = cgeom.resample(np.vstack([np.array(g.exterior.coords)]), 0.01)[:-1]
    rings = [np.column_stack([ring[:, 0], np.full(len(ring), y), ring[:, 1]]) for y in (DOOR_IN - grow, DOOR_OUT + grow)]
    v, f = cgeom.close_rings(rings)
    return [make("door_solid", v, f, "interior")]


def door(shell):
    """Cut the left door free of the canopy; its window goes with it."""
    fr = common.frame((0, 0.7, 0), (1, 0, 0), (0, 0, 1), (0, -1, 0))
    roles = {"side_window_l": "glass"}
    return common.free_door(None, fr, [], DOOR_GAP, shell, [], roles, door_outline(), solids=_door_solid)


def mirror_recesses(shell):
    m = ROWS["mirror"]
    xb = m["x"] - m["rx"] * 0.45
    s = np.sqrt(1 - 0.45 ** 2) * 0.82
    a = np.linspace(0, 2 * np.pi, 24, endpoint=False)
    for sgn in (1, -1):
        y = m["y"] * sgn
        ring = np.column_stack([np.zeros(24), y + m["ry"] * s * np.cos(a), m["z"] + m["rz"] * s * np.sin(a)])
        r0 = ring + np.array([xb - 0.01, 0, 0]); r1 = ring + np.array([xb + 0.005, 0, 0])
        boolean(shell, prism("recess", r0, r1, "housing"), "DIFFERENCE")


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
            for k in ("spokes", "hub"):
                boolean(rim, made[k], "UNION")
            made["tyre"].name = f"wheel_tyre_{tag}"
            obs += [made["tyre"], rim]
    return obs


ATTACH = ("splitter", "floor", "rear_box", "scoop", "wing", "endplate_l", "endplate_r", "pylon",
          "diffuser_fin_0", "diffuser_fin_1", "diffuser_fin_2", "mirror_l", "mirror_r",
          "mirror_stalk_l", "mirror_stalk_r", "rear_housing_l", "rear_housing_r", "rain_housing")


def options(bvh):
    """Badge and name placements for Ivan to toggle between in the viewer."""
    import decals
    obs = []

    picks = ROWS["picks"]

    def add(group, n, pieces):
        if GAME and picks[group] != n:
            return
        for i, (v, f, mat) in enumerate(pieces):
            label = f"{group}_{i}" if GAME else f"opt_{group}_{n}_{i}"
            obs.append(make(label, v, f, mat, recalc=False))

    # Front badge: 1 nose top ahead of the windscreen, 2 nose tip.
    add("frontbadge", 1, decals.badge(bvh, (1.22, 0, 1.5), (-1, 0, 0), (0, -1, 0), (0, 0, -1), 0.085))
    add("frontbadge", 2, decals.badge(bvh, (2.6, 0, 0.255), (0, 0, 1), (0, -1, 0), (-1, 0, 0), 0.07))
    # Rear badge: 1 fin sides, 2 wing endplates.
    for s in (1, -1):
        add("rearbadge", 1, decals.badge(bvh, (-1.40, 0.5 * s, 1.0), (0, 0, 1), (-s, 0, 0), (0, -s, 0), 0.085))
        add("rearbadge", 2, decals.badge(bvh, (ROWS_X(110) - 0.03, 1.5 * s, 0.97), (0, 0, 1), (-s, 0, 0), (0, -s, 0), 0.1))
    # Name: 1 fin sides, 2 front fender tops, 3 rear fender sides.
    for s in (1, -1):
        g, length = decals.name(bvh, (0, 0, 0), (1, 0, 0), (0, 0, 1), (0, -s, 0), 0.045)
        x0 = -0.85 if s > 0 else -0.85 - length
        add("name", 1, decals.name(bvh, (x0, 0.5 * s, 0.90), (-s, 0, 0), (0, 0, 1), (0, -s, 0), 0.045)[0])
        x0 = 1.62 if s > 0 else 1.62 - decals.name(bvh, (0, 0, 0), (1, 0, 0), (0, 0, 1), (0, 0, -1), 0.05)[1]
        add("name", 2, decals.name(bvh, (x0, 0.66 * s - 0.025 * s, 1.5), (-s, 0, 0), (0, -s, 0), (0, 0, -1), 0.05)[0])
        g, length = decals.name(bvh, (0, 0, 0), (1, 0, 0), (0, 0, 1), (0, -s, 0), 0.04)
        x0 = -0.45 if s > 0 else -0.45 - length
        add("name", 3, decals.name(bvh, (x0, 1.5 * s, 0.45), (-s, 0, 0), (0, 0, 1), (0, -s, 0), 0.04)[0])
    return obs


def main():
    reset()
    shell = build_body()
    loose = {**body.trim_pieces(), **parts.wing_parts(), **parts.mirror_parts(), **parts.rear_lamp_parts()}
    for name in ATTACH:
        v, f, mat = loose.pop(name)
        boolean(shell, make(name, v, f, mat), "UNION")
    mirror_recesses(shell)
    cut_arches(shell)
    bvh = BVHTree.FromObject(shell, bpy.context.evaluated_depsgraph_get())
    obs = [shell]
    obs += headlights(shell, bvh)
    obs += windows(bvh)
    obs += options(bvh)
    cockpit(shell)
    pieces = door(shell)
    print("door cut free:", [o.name for o in pieces])
    obs += [p for p in pieces if p not in obs]
    for name, (v, f, mat) in loose.items():
        obs.append(make(name, v, f, mat))
    obs += wheels()
    for ob in obs:
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
                          export_triangulated_mesh=True,
                          path_mode="RELATIVE", **axes)
    write_mtl(OUT / f"{NAME}.mtl")
    if GAME:
        common.write_doors(OUT / f"{NAME}.doors.json", {"door_l": DOOR_STYLES[DOOR_PICK]})


def write_mtl(path):
    """Own MTL so lamp emissive and glass alpha survive."""
    used = set()
    obj = path.with_suffix(".obj").read_text().splitlines()
    for line in obj:
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
