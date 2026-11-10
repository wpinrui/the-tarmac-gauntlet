"""Shared Blender primitives: meshes, booleans, laying flat shapes onto a body, V grooves,
clean-up and OBJ/MTL export. No shapes, sizes, colours or designs live here."""
import math
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from shapely.geometry import Polygon
from shapely.geometry.polygon import orient

from geom import close_rings, resample


def reset(materials):
    """Empty scene with one Blender material per entry of materials {name: (rgb, ...)}."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for name, spec in materials.items():
        m = bpy.data.materials.new(name)
        m.diffuse_color = (*spec[0], 1.0)


def make(name, verts, faces, mat, recalc=True):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(map(float, v)) for v in verts], [], [tuple(int(i) for i in f) for f in faces])
    me.validate()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    me.materials.append(bpy.data.materials[mat])
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-7)
    bmesh.ops.delete(bm, geom=[e for e in bm.edges if not e.link_faces], context="EDGES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    if recalc:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    return ob


def boolean(target, other, op):
    """Apply a boolean (UNION, DIFFERENCE, INTERSECT) and delete the other object."""
    mod = target.modifiers.new("b", "BOOLEAN")
    mod.operation = op
    mod.object = other
    mod.solver = "EXACT"
    mod.material_mode = "TRANSFER"
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(other)


def relax(ob, voxel=0.014, iters=12, factor=0.5):
    """Remesh a lofted solid into even cells and smooth it, so pinched rings at its ends
    leave no ripples."""
    bpy.context.view_layer.objects.active = ob
    for kind, opts in (("REMESH", dict(mode="VOXEL", voxel_size=voxel)),
                       ("LAPLACIANSMOOTH", dict(iterations=iters, lambda_factor=factor,
                                                use_volume_preserve=True))):
        mod = ob.modifiers.new(kind.lower(), kind)
        for k, v in opts.items():
            setattr(mod, k, v)
        bpy.ops.object.modifier_apply(modifier=mod.name)


def thin(ob, ratio):
    """Collapse-decimate a dense mesh to ratio of its faces."""
    bpy.context.view_layer.objects.active = ob
    mod = ob.modifiers.new("thin", "DECIMATE")
    mod.ratio = ratio
    bpy.ops.object.modifier_apply(modifier=mod.name)


def union_all(obs, name):
    base = obs[0]
    base.name = name
    for ob in obs[1:]:
        boolean(base, ob, "UNION")
    return base


def tree(ob):
    return BVHTree.FromObject(ob, bpy.context.evaluated_depsgraph_get())


# A frame is (origin, u, v, d): 2D (a, b) sits at origin + u*a + v*b; rays run along d.
# A frame can also be a function (a, b) -> (start, direction) for rays that bend round a body.

def frame(origin, u, v, d):
    return tuple(np.asarray(a, float) for a in (origin, u, v, d))


def panel_frame(bvh, fr, a, b, up):
    """A frame lying flat on the panel where fr's ray at (a, b) lands: rays run along the
    panel's normal there, u points right and v up (towards the world direction up), so a flat
    shape keeps its true proportions on a sloped panel."""
    loc, nrm = cast(bvh, fr, a, b)
    n = nrm / np.linalg.norm(nrm)
    u = np.cross(np.asarray(up, float), n); u /= np.linalg.norm(u)
    v = np.cross(n, u)
    return frame(loc, u, v, -n)


def cast(bvh, fr, a, b):
    """Ray from the 2D point (a, b) into the body. Returns (location, normal) or (None, None)."""
    if callable(fr):
        start, d = fr(a, b)
    else:
        o, u, v, d = fr
        start = o + u * a + v * b - d * 0.5
    loc, nrm, _i, _dist = bvh.ray_cast(Vector(start), Vector(d))
    if loc is None:
        return None, None
    return np.array(loc), np.array(nrm)


def fit(bvh, poly, fr, accept=None):
    """Pull each outline point towards the middle until its ray lands where accept allows."""
    c = np.array(poly.centroid.coords[0])
    out = []
    for p in np.array(poly.exterior.coords)[:-1]:
        q = p
        for k in np.linspace(1.0, 0.2, 81):
            q = c + (p - c) * k
            loc, _n = cast(bvh, fr, *q)
            if loc is not None and (accept is None or accept(loc)):
                break
        out.append(q)
    return Polygon(out).buffer(0)


def _densify(loop, step):
    out = []
    n = len(loop)
    for i in range(n):
        p, q = np.asarray(loop[i], float), np.asarray(loop[(i + 1) % n], float)
        k = max(1, int(np.ceil(np.linalg.norm(q - p) / step)))
        out += [p + (q - p) * t for t in np.arange(k) / k]
    return out


def triangulate(poly, step):
    """Even triangle mesh of a shapely polygon (holes kept)."""
    import triangle
    poly = orient(poly, 1)
    loops = [list(poly.exterior.coords)[:-1]] + [list(i.coords)[:-1] for i in poly.interiors]
    verts, segs, holes = [], [], []
    for li, loop in enumerate(loops):
        d = _densify(loop, step)
        b = len(verts)
        verts += d
        segs += [(b + i, b + (i + 1) % len(d)) for i in range(len(d))]
        if li:
            holes.append(Polygon(loop).representative_point().coords[0])
    spec = {"vertices": np.array(verts), "segments": np.array(segs)}
    if holes:
        spec["holes"] = np.array(holes)
    t = triangle.triangulate(spec, f"pq28Ya{step * step * 0.6:.10f}")
    return t["vertices"], [tuple(int(i) for i in tri) for tri in t["triangles"]]


def _ray_dir(fr, a, b):
    d = fr(a, b)[1] if callable(fr) else fr[3]
    return np.asarray(d, float) / np.linalg.norm(d)


def lay(bvh, name, geom, fr, d0, d1, mat, step=0.01, accept=None, along_ray=False):
    """Lay a flat shapely shape onto the body as a thin solid between d0 and d1 off the
    surface. A point whose ray misses (or is refused) moves towards the middle of its piece.
    along_ray: offset back along each point's ray instead of the surface normal, which
    cannot fold over a crease (for cutters). Returns a list of objects, one per piece."""
    polys = [geom] if geom.geom_type == "Polygon" else list(getattr(geom, "geoms", []))
    obs = []
    for i, poly in enumerate(p for p in polys if p.area > 1e-7):
        pts, tris = triangulate(poly, step)
        c = np.array(poly.representative_point().coords[0])
        hits = []
        for p in pts:
            got = None
            for k in np.linspace(1.0, 0.2, 41):
                q = c + (np.asarray(p) - c) * k
                loc, nrm = cast(bvh, fr, *q)
                if loc is not None and (accept is None or accept(loc)):
                    got = (loc, nrm)
                    break
            if got is None:
                print("missed", name)
                break
            hits.append(got if not along_ray else (got[0], -_ray_dir(fr, *q)))
        if len(hits) < len(pts):
            continue
        # smooth the normals over each vertex's triangles so the offset has no steps
        nrm = np.array([h[1] for h in hits])
        if along_ray:
            acc = nrm
        else:
            acc = np.zeros_like(nrm)
            for t in tris:
                for j in t:
                    acc[j] += nrm[list(t)].sum(0)
            acc /= np.linalg.norm(acc, axis=1, keepdims=True)
        loc = np.array([h[0] for h in hits])
        n = len(pts)
        verts = np.vstack([loc + acc * d0, loc + acc * d1])
        count = {}
        for t in tris:
            for e in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
                count[tuple(sorted(e))] = count.get(tuple(sorted(e)), 0) + 1
        faces = [(t[2], t[1], t[0]) for t in tris] + [(t[0] + n, t[1] + n, t[2] + n) for t in tris]
        for t in tris:
            for a, b in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
                if count[tuple(sorted((a, b)))] == 1:
                    faces.append((a, b, b + n, a + n))
        obs.append(make(f"{name}_{i}" if i else name, verts, faces, mat))
    return obs


def groove(bvh, name, path2d, fr, width=0.006, depth=0.006, step=0.008):
    """V groove along a 2D path laid on the body, as a solid to cut away."""
    pts = resample(path2d, step)
    hits = [cast(bvh, fr, *p) for p in pts]
    hits = [h for h in hits if h[0] is not None]
    rings = []
    for i, (p, n) in enumerate(hits):
        a = hits[max(i - 1, 0)][0]; b = hits[min(i + 1, len(hits) - 1)][0]
        t = b - a; t /= np.linalg.norm(t)
        side = np.cross(n, t); side /= np.linalg.norm(side)
        rings.append(np.array([p + n * 0.004 + side * width / 2, p + n * 0.004 - side * width / 2,
                               p - n * depth]))
    v, f = close_rings(rings)
    return make(name, v, f, "trim")


def split_by_planes(ob, planes):
    """Cut the mesh along planes [(point, normal)] so a colour change lands on a clean line."""
    bm = bmesh.new(); bm.from_mesh(ob.data)
    for co, no in planes:
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no)
    bm.to_mesh(ob.data); bm.free()


def paint(ob, mat, where):
    """Give every face whose centre passes where(x, y, z) the material mat."""
    if mat not in [m.name for m in ob.data.materials]:
        ob.data.materials.append(bpy.data.materials[mat])
    k = [m.name for m in ob.data.materials].index(mat)
    for p in ob.data.polygons:
        if where(*p.center):
            p.material_index = k


def tidy(ob):
    """Weld hairline seams the booleans leave, drop sliver triangles and close pinholes."""
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=2e-5)
    for _ in range(5):
        bm.faces.ensure_lookup_table()
        flat = [f for f in bm.faces if f.is_valid and f.calc_area() < 1e-10]
        if not flat:
            break
        edges = {min(f.edges, key=lambda e: e.calc_length()) for f in flat}
        bmesh.ops.collapse(bm, edges=[e for e in edges if e.is_valid])
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.collapse(bm, edges=[e for e in bm.edges if e.is_boundary and e.calc_length() < 5e-4])
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.delete(bm, geom=[e for e in bm.edges if not e.link_faces], context="EDGES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(ob.data); bm.free()


def smooth(ob, angle=35):
    for p in ob.data.polygons:
        p.use_smooth = True
    ob.data.set_sharp_from_angle(angle=math.radians(angle))


def export(obs, out_dir, name, materials, game=False):
    """Write <name>.obj and our own <name>.mtl (keeps lamp glow and glass alpha).
    game: Unity axes, forward +Z, up +Y, metres."""
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    print("triangles", sum(sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob in obs))
    if game:
        m = Matrix(((0, 1, 0, 0), (0, 0, 1, 0), (1, 0, 0, 0), (0, 0, 0, 1)))
        for ob in obs:
            ob.data.transform(m)
        axes = dict(forward_axis="Y", up_axis="Z")
    else:
        axes = dict(forward_axis="NEGATIVE_Z", up_axis="Y")
    obj = out / f"{name}.obj"
    bpy.ops.wm.obj_export(filepath=str(obj), export_selected_objects=False, export_materials=True,
                          export_normals=True, export_uv=False, export_triangulated_mesh=True,
                          path_mode="RELATIVE", **axes)
    used = sorted({ln.split()[1] for ln in obj.read_text().splitlines() if ln.startswith("usemtl ")})
    lines = []
    for mname in used:
        col, spec, alpha, emit = materials[mname]
        lines += [f"newmtl {mname}", "Kd {:.3f} {:.3f} {:.3f}".format(*col),
                  "Ks {0:.2f} {0:.2f} {0:.2f}".format(spec * 0.5), "Ns 200", f"d {alpha:.2f}",
                  "Ke {:.2f} {:.2f} {:.2f}".format(*(emit or (0, 0, 0))), ""]
    (out / f"{name}.mtl").write_text("\n".join(lines))
