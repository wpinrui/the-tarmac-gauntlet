"""Builds the Liberty Collier in Blender's Python module and exports OBJ and MTL.

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

import body
import parts
import skin
from body import ROWS

ARGS = [a for a in sys.argv[1:] if not a.startswith("--")]
GAME = "--game" in sys.argv
OUT = Path(ARGS[0] if ARGS else Path(__file__).with_name("out"))
NAME = "LibertyCollier"

MATERIALS = {
    "body": ((0.80, 0.81, 0.83), 0.6, 1.0, None),
    "trim": ((0.05, 0.05, 0.055), 0.3, 1.0, None),
    "strip": ((0.22, 0.23, 0.24), 0.3, 1.0, None),
    "grille": ((0.07, 0.07, 0.075), 0.3, 1.0, None),
    "chrome": ((0.75, 0.76, 0.78), 1.0, 1.0, None),
    "glass": ((0.10, 0.12, 0.14), 0.9, 0.55, None),
    "head_glass": ((0.85, 0.9, 0.95), 0.9, 0.25, None),
    "tail_glass": ((0.55, 0.08, 0.08), 0.9, 0.45, None),
    "housing": ((0.55, 0.56, 0.58), 0.7, 1.0, None),
    "tyre": ((0.05, 0.05, 0.05), 0.1, 1.0, None),
    "rim": ((0.62, 0.63, 0.65), 0.8, 1.0, None),
    "hub": ((0.45, 0.45, 0.47), 0.8, 1.0, None),
    "mirror_glass": ((0.55, 0.6, 0.65), 1.0, 1.0, None),
    "interior": ((0.16, 0.16, 0.17), 0.2, 1.0, None),
    "seat": ((0.22, 0.22, 0.23), 0.2, 1.0, None),
    "lamp_head": ((0.95, 0.95, 0.92), 0.9, 1.0, (1.0, 0.97, 0.9)),
    "lamp_fog": ((0.95, 0.95, 0.92), 0.9, 1.0, (1.0, 0.97, 0.9)),
    "lamp_indicator": ((0.55, 0.35, 0.05), 0.8, 1.0, (1.0, 0.45, 0.0)),
    "lamp_reverse": ((0.9, 0.9, 0.9), 0.8, 1.0, (1.0, 1.0, 1.0)),
    "lamp_tail": ((0.35, 0.02, 0.02), 0.6, 1.0, (0.55, 0.0, 0.0)),
    "lamp_brake": ((0.40, 0.02, 0.02), 0.6, 1.0, (1.0, 0.0, 0.0)),
    "badge_red": ((0.75, 0.165, 0.184), 0.6, 1.0, None),
    "badge_navy": ((0.078, 0.157, 0.294), 0.6, 1.0, None),
    "badge_gold": ((0.78, 0.60, 0.24), 0.9, 1.0, None),
    "name": ((0.09, 0.16, 0.29), 0.6, 1.0, None),
}

def robust_cast(bvh, o, d):
    """Ray cast that nudges the ray inward and down until it meets the body."""
    o = np.array(o, float)
    for _ in range(40):
        loc, nrm, _i, _d = bvh.ray_cast(Vector(o), Vector(d))
        if loc is not None and abs(d[0]) > 0.5:
            from rows import X_FRONT, X_REAR
            end = X_FRONT if d[0] < 0 else X_REAR
            if abs(loc.x - end) > 0.5:
                loc = None
        if loc is not None:
            return loc, nrm
        dd = np.abs(np.array(d))
        if dd[1] < 0.5:
            o[1] *= 0.98
        if dd[2] < 0.5:
            o[2] -= 0.008
    raise RuntimeError(f"ray missed the body at {o}")


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
                loc, nrm = robust_cast(bvh, o, d)
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
                loc, nrm = robust_cast(bvh, o, d)
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



def prism(name, loop3d_a, loop3d_b, mat="body"):
    v, f = body._close([np.asarray(loop3d_a), np.asarray(loop3d_b)])
    return make(name, v, f, mat)



def scaled(loop, k):
    loop = np.asarray(loop, float)
    c = loop.mean(0)
    return c + (loop - c) * k



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




from rows import X, Z, FY, FZ, RY, RZ, TX, TY  # noqa: E402


def hit(bvh, o, d):
    loc, nrm = robust_cast(bvh, o, d)
    return (np.array(loc), np.array(nrm)) if loc is not None else (None, None)


def front_ray(side):
    """Rays from ahead of the car through front-view pixels; side 1 is the car's left."""
    def ray(px, py):
        return (4.0, side * abs(FY(px)), FZ(py)), (-1, 0, 0)
    return ray


def rear_ray(side):
    def ray(px, py):
        return (-4.0, side * abs(RY(px)), RZ(py)), (1, 0, 0)
    return ray


def side_ray(side):
    def ray(px, py):
        return (X(px), 3.0 * side, Z(py)), (0, -side, 0)
    return ray


def lamp_unit(bvh, tag, outline, ray, units, glass_mat, flip):
    """Bezel, housing, lamps and glass laid on the body."""
    loop = densify(outline)
    obs = [projected_ring(bvh, f"{tag}_bezel", loop, ray, 0.0015, 0.014, "housing", flip, 1.05),
           projected_slab(bvh, f"{tag}_housing", loop, ray, 0.0015, 0.003, "trim", flip, 0.985),
           projected_slab(bvh, f"{tag}_glass", loop, ray, 0.016, 0.003, glass_mat, flip, 1.03)]
    for i, (mat, px, py, r) in enumerate(units):
        o, d = ray(px, py)
        p, n = hit(bvh, o, d)
        obs.append(make(f"{mat}_{tag}{i}", *parts.disc(p + n * 0.0085, n, r * 1.72 / 525, 0.004), mat))
    return obs


def wrap_lamp(tag, loop_su, units, glass_mat, side):
    """Bezel, housing, lamps and glass on the skin, wrapping round the body corner."""
    loop = skin.smooth_loop(densify(loop_su, 4), 2)
    obs = [make(f"{tag}_bezel", *skin.ring(loop, 0.0015, 0.014, side, 1.04), "housing", recalc=False),
           make(f"{tag}_housing", *skin.patch(loop, 0.0015, 0.003, side, 0.985), "trim", recalc=False),
           make(f"{tag}_glass", *skin.patch(loop, 0.0155, 0.003, side, 1.03), glass_mat, recalc=False)]
    for i, (mat, s, u, r) in enumerate(units):
        p = skin.point_su(s, u) * [1, side, 1]
        n = skin.normal_su(s, u) * [1, side, 1]
        obs.append(make(f"{mat}_{tag}{i}", *parts.disc(p + n * 0.0085, n, r, 0.004), mat))
    return obs


def outside(bvh):
    """Glass, lamps, grilles and trim laid on the body skin."""
    obs = []
    g = ROWS["glass"]
    L = ROWS["lamps"]
    for side, tag in ((1, "l"), (-1, "r")):
        flip = side < 0
        sr = side_ray(side)
        for key in ("door_window", "quarter_window"):
            loop = densify(g[key])
            obs.append(projected_slab(bvh, f"{key}_{tag}", loop, sr, 0.003, 0.005, "glass", flip, 1.0))
            obs.append(projected_ring(bvh, f"{key}_trim_{tag}", loop, sr, 0.0015, 0.004, "trim", flip, 1.045))
        obs.append(projected_slab(bvh, f"b_pillar_{tag}", densify(g["b_pillar"]), sr, 0.0015, 0.003, "trim", flip, 0.7))
        fr = front_ray(side)
        rr = rear_ray(side)
        obs += wrap_lamp(f"head_{tag}", L["head_su"], L["head_su_units"], "head_glass", side)
        fr = front_ray(side)
        for i, (px, py, r) in enumerate(L["fog"]):
            px = 651 - px if side > 0 else px
            p, n = hit(bvh, *fr(px, py))
            obs.append(make(f"lamp_fog_{tag}{i}", *parts.disc(p + n * 0.0035, n, r * 1.72 / 525, 0.004), "lamp_fog"))
        obs += wrap_lamp(f"tail_{tag}", L["tail_su"], L["tail_su_units"], "tail_glass", side)
        rp = L["repeater"]
        p, n = hit(bvh, (rp["x"], 3.0 * side, rp["z"]), (0, -side, 0))
        v, f = parts.disc(p + n * 0.0035, n, rp["rz"], 0.004)
        v = p + (v - p) * np.array([rp["rx"] / rp["rz"], 1, 1])
        obs.append(make(f"lamp_indicator_rep_{tag}", v, f, "lamp_indicator"))
    fr = front_ray(1)
    fwd = lambda px, py: ((4.0, FY(px), FZ(py)), (-1, 0, 0))
    obs.append(projected_slab(bvh, "grille_upper", densify(ROWS["grille_upper"]), fwd, 0.0015, 0.004, "grille", True))
    obs.append(projected_slab(bvh, "grille_lower", densify(ROWS["grille_lower"]), fwd, 0.0015, 0.004, "grille", True))
    back = lambda px, py: ((-4.0, RY(px), RZ(py)), (1, 0, 0))
    obs.append(projected_slab(bvh, "rear_strip", densify(ROWS["rear_strip"]), back, 0.0015, 0.004, "strip", False))
    roof = densify([(TX(row), TY(px)) for px, row in g["roof"]])
    obs.append(projected_slab(bvh, "roof_glass", roof, lambda x, y: ((x, y, 3.0), (0, 0, -1)), 0.003, 0.005, "glass"))
    obs.append(projected_slab(bvh, "lamp_brake_high", densify(L["high_brake"]), back, 0.0015, 0.004, "lamp_brake", False))
    rear = densify(g["rear"])
    obs.append(projected_slab(bvh, "rear_glass", rear, back, 0.003, 0.005, "glass", False))
    return obs


def groove(bvh, pts2d, ray, closed=False, width=0.004, depth=0.004):
    """Swept square tube along a surface path, for cutting a shut line."""
    pts = []
    for u, v in densify(pts2d, 10)[: None if closed else -9]:
        p, n = hit(bvh, *ray(u, v))
        if p is not None:
            pts.append((p, n))
    rings = []
    for i, (p, n) in enumerate(pts):
        a = pts[max(i - 1, 0)][0]
        b = pts[min(i + 1, len(pts) - 1)][0]
        t = b - a
        t /= np.linalg.norm(t) + 1e-9
        bn = np.cross(n, t)
        bn /= np.linalg.norm(bn) + 1e-9
        rings.append(np.array([p + n * depth + bn * width / 2, p + n * depth - bn * width / 2,
                               p - n * depth - bn * width * 0.1, p - n * depth + bn * width * 0.1]))
    if closed:
        rings.append(rings[0])
    return body._close(rings)


def shut_lines(shell, bvh):
    sl = ROWS["shut_lines"]
    for side in (1, -1):
        v, f = groove(bvh, sl["door"], side_ray(side))
        boolean(shell, make("door_line", v, f, "trim"), "DIFFERENCE")
    back = lambda px, py: ((-4.0, RY(px), RZ(py)), (1, 0, 0))
    v, f = groove(bvh, sl["tailgate"], back, closed=True)
    boolean(shell, make("tailgate_line", v, f, "trim"), "DIFFERENCE")
    fwd = lambda px, py: ((4.0, FY(px), FZ(py)), (-1, 0, 0))
    v, f = groove(bvh, sl["bonnet"][1:-1], fwd)
    boolean(shell, make("bonnet_line", v, f, "trim"), "DIFFERENCE")


def mirrors(shell):
    m = ROWS["mirror"]
    obs = []
    for s, tag in ((1, "l"), (-1, "r")):
        y = m["y"] * s
        v, f = parts.ellipsoid_shell(m["x"], y, m["z"], m["rx"], m["ry"], m["rz"])
        boolean(shell, make("mirror_head", v, f, "body"), "UNION")
        y0, y1 = sorted((m["root_y"] * s, (m["y"] - m["ry"] * 0.5) * s))
        boolean(shell, make("mirror_arm", *parts.box(m["x"] - 0.03, m["x"] + 0.02, y0, y1, m["root_z"] - 0.02, m["root_z"] + 0.025), "body"), "UNION")
        xb = m["x"] - m["rx"] * 0.45
        k = np.sqrt(1 - 0.45 ** 2) * 0.80
        a = np.linspace(0, 2 * np.pi, 24, endpoint=False)
        ring = np.column_stack([np.zeros(24), y + m["ry"] * k * np.cos(a), m["z"] + m["rz"] * k * np.sin(a)])
        boolean(shell, prism("recess", ring + [xb - 0.01, 0, 0], ring + [xb + 0.005, 0, 0], "trim"), "DIFFERENCE")
        g = ring * np.array([1, 1, 1])
        gk = np.column_stack([np.zeros(24), y + m["ry"] * k * 0.97 * np.cos(a), m["z"] + m["rz"] * k * 0.97 * np.sin(a)])
        obs.append(make(f"mirror_glass_{tag}", *body._close([gk + [xb + 0.0005, 0, 0], gk + [xb + 0.0045, 0, 0]]), "mirror_glass"))
    return obs


def cabin(shell):
    """Hollow the cabin, open the windows and fit the interior."""
    c = ROWS["cabin"]
    rows = []
    for x in np.linspace(c["x0"], c["x1"], 12):
        p = body.params_at(x)
        for k in ("w", "wb", "wt"):
            p[k] -= c["inset"]
        p["zt"] -= c["inset"]
        p["zb"] = c["zb"]
        p["zw"] = max(p["zw"], c["zb"] + 0.1)
        rows.append(dict(x=float(x), **p))
    boolean(shell, make("cavity", *body.loft(rows, step=0.05), "interior"), "DIFFERENCE")
    g = ROWS["glass"]
    for s in (1, -1):
        for key in ("door_window", "quarter_window"):
            loop = scaled(densify([(X(px), Z(py)) for px, py in g[key]]), 0.96)
            a = np.column_stack([loop[:, 0], np.full(len(loop), 0.3 * s), loop[:, 1]])
            b = a.copy(); b[:, 1] = 1.3 * s
            boolean(shell, prism("win_cut", a, b, "interior"), "DIFFERENCE")
    roof = scaled(densify([(TX(row), TY(px)) for px, row in g["roof"]]), 0.96)
    boolean(shell, prism("roof_cut", np.column_stack([roof, np.full(len(roof), 0.95)]),
                         np.column_stack([roof, np.full(len(roof), 2.0)]), "interior"), "DIFFERENCE")
    rear = scaled(densify([(RY(px), RZ(py)) for px, py in g["rear"]]), 0.96)
    boolean(shell, prism("rear_cut", np.column_stack([np.full(len(rear), -1.2), rear]),
                         np.column_stack([np.full(len(rear), -2.6), rear]), "interior"), "DIFFERENCE")
    bits = [
        (parts.box(-0.42, 0.08, 0.12, 0.56, 0.28, 0.46), "seat"),
        (parts.box(-0.42, 0.08, -0.56, -0.12, 0.28, 0.46), "seat"),
        (parts.box(-0.52, -0.38, 0.14, 0.54, 0.28, 1.02), "seat"),
        (parts.box(-0.52, -0.38, -0.54, -0.14, 0.28, 1.02), "seat"),
        (parts.box(-1.30, -0.80, -0.62, 0.62, 0.28, 0.46), "seat"),
        (parts.box(-1.42, -1.28, -0.62, 0.62, 0.28, 0.98), "seat"),
        (parts.box(0.62, 1.00, -0.75, 0.75, 0.62, 0.86), "interior"),
        (parts.box(0.42, 0.66, 0.33, 0.41, 0.78, 0.84), "interior"),
        (parts.disc(np.array([0.42, 0.37, 0.86]), (-1, 0, 0.35), 0.19, 0.035), "interior"),
    ]
    for i, ((v, f), mat) in enumerate(bits):
        boolean(shell, make(f"interior_{i}", v, f, mat), "UNION")


def build_body():
    shell = make("body", *body.loft(ROWS["body"]))
    return shell


def cut_arches(shell):
    w = ROWS["wheel"]
    for x in (w["front_x"], w["rear_x"]):
        for s in (1, -1):
            y0, y1 = sorted((0.60 * s, 1.2 * s))
            boolean(shell, cylinder_y("arch", x, w["r"], w["r"] + 0.035, y0, y1), "DIFFERENCE")


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


def main():
    reset()
    shell = build_body()
    bvh = BVHTree.FromObject(shell, bpy.context.evaluated_depsgraph_get())
    obs = [shell]
    obs += outside(bvh)
    shut_lines(shell, bvh)
    cut_arches(shell)
    cabin(shell)
    obs += mirrors(shell)
    obs += wheels()
    for ob in obs:
        smooth(ob)
    OUT.mkdir(parents=True, exist_ok=True)
    tris = sum(sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob in obs)
    print("triangles", tris)
    if GAME:
        from mathutils import Matrix
        m = Matrix(((0, 1, 0, 0), (0, 0, 1, 0), (1, 0, 0, 0), (0, 0, 0, 1)))
        for ob in obs:
            ob.data.transform(m)
        axes = dict(forward_axis="Y", up_axis="Z")
    else:
        axes = dict(forward_axis="NEGATIVE_Z", up_axis="Y")
    bpy.ops.wm.obj_export(filepath=str(OUT / f"{NAME}.obj"), export_selected_objects=False,
                          export_materials=True, export_normals=True, export_uv=False,
                          path_mode="RELATIVE", **axes)
    write_mtl(OUT / f"{NAME}.mtl")


if __name__ == "__main__":
    main()
