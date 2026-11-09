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

OUT = Path(sys.argv[1] if len(sys.argv) > 1 else Path(__file__).with_name("out"))
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
    "lamp_head": ((0.95, 0.95, 0.92), 0.9, 1.0, (1.0, 0.97, 0.9)),
    "lamp_tail": ((0.45, 0.05, 0.05), 0.6, 1.0, (0.8, 0.05, 0.03)),
    "lamp_brake": ((0.55, 0.05, 0.05), 0.6, 1.0, (1.0, 0.08, 0.05)),
    "lamp_rain": ((0.55, 0.05, 0.05), 0.6, 1.0, (1.0, 0.1, 0.05)),
}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for name, (col, *_rest) in MATERIALS.items():
        m = bpy.data.materials.new(name)
        m.diffuse_color = (*col, 1.0)


def make(name, verts, faces, mat="body"):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(map(float, v)) for v in verts], [], [tuple(int(i) for i in f) for f in faces])
    me.validate()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    me.materials.append(bpy.data.materials[mat])
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
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
    w = ROWS["wheel"]
    for x in (w["front_x"], w["rear_x"]):
        for s in (1, -1):
            y0, y1 = sorted((0.585 * s, 1.2 * s))
            boolean(shell, cylinder_y("arch", x, w["r"], w["r"] + 0.025, y0, y1), "DIFFERENCE")
    return shell


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
    rings = [cen + (loop - cen) * k for k in (1.0, 0.8, 0.6, 0.4, 0.2, 0.02)]
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


def densify(poly, n=8):
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
        obs.append(projected_slab(bvh, f"head_frame_{tag}", loop, ray, 0.001, 0.004, "carbon", side < 0, 1.08))
        obs.append(projected_slab(bvh, f"head_housing_{tag}", loop, ray, 0.003, 0.004, "housing", side < 0))
        obs.append(projected_slab(bvh, f"head_glass_{tag}", loop, ray, 0.018, 0.003, "head_glass", side < 0))
        for i, (px, py, r) in enumerate(h["lamps_px"] + [h["led_px"]]):
            o, d = ray(px, py)
            loc, nrm, _i, _d = bvh.ray_cast(Vector(o), Vector(d))
            p, n = np.array(loc), np.array(nrm)
            obs.append(make(f"lamp_head_{tag}{i}", *parts.disc(p + n * 0.009, n, r * sc, 0.008), "lamp_head"))
    return obs


def windows(bvh):
    """Windscreen and side windows as glass slabs lying on the canopy."""
    X, Z = ROWS_X, ROWS_Z
    g = ROWS["glass"]
    obs = []
    screen = densify([[X(px), y] for px, y in g["screen_plan"]])
    obs.append(projected_slab(bvh, "windscreen", screen, lambda x, y: ((x, y, 3.0), (0, 0, -1)), 0.002, 0.006, "glass"))
    for side, tag in ((1, "l"), (-1, "r")):
        win = densify([[X(px), Z(py)] for px, py in g["side_px"]])
        obs.append(projected_slab(bvh, f"side_window_{tag}", win,
                                  lambda x, z, s=side: ((x, 3.0 * s, z), (0, -s, 0)), 0.002, 0.006, "glass", side < 0))
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


def main():
    reset()
    shell = build_body()
    obs = [shell]
    for name, (v, f, mat) in {**body.trim_pieces(), **parts.wing_parts(), **parts.mirror_parts(),
                               **parts.rear_lamp_parts()}.items():
        obs.append(make(name, v, f, mat))
    obs += place_wheels()
    bvh = BVHTree.FromObject(shell, bpy.context.evaluated_depsgraph_get())
    obs += headlights(shell, bvh)
    obs += windows(bvh)
    for ob in obs:
        smooth(ob)
    OUT.mkdir(parents=True, exist_ok=True)
    tris = sum(sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob in obs)
    print("triangles", tris)
    bpy.ops.wm.obj_export(filepath=str(OUT / f"{NAME}.obj"), export_selected_objects=False,
                          export_materials=True, export_normals=True, export_uv=False,
                          forward_axis="NEGATIVE_Z", up_axis="Y", path_mode="RELATIVE")
    write_mtl(OUT / f"{NAME}.mtl")


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
