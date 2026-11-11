"""Shared primitive: defect checks on an exported OBJ. Open or inward shells, floating parts,
intersecting pairs and z-fighting. Returns the list of hits."""
import bmesh
import bpy
import re
from mathutils.bvhtree import BVHTree

TOUCH = 0.004     # parts closer than this are attached
ZFIGHT = 0.0004   # parallel faces closer than this fight


def _swing(data, doors):
    """Turn each door's parts open: doors {name prefix: (p0, p1, deg)} in build space."""
    import math
    from mathutils import Matrix, Vector
    for prefix, (p0, p1, deg) in doors.items():
        p0 = Vector(p0); ax = (Vector(p1) - p0).normalized()
        m = Matrix.Translation(p0) @ Matrix.Rotation(math.radians(deg), 4, ax) @ Matrix.Translation(-p0)
        for k, bm in data.items():
            if re.sub(r"^opt_[a-z]+_\d+_", "", k).startswith(prefix):
                bm.transform(m)


def _load(obj_path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.obj_import(filepath=str(obj_path), forward_axis="NEGATIVE_Z", up_axis="Y")
    data = {}
    for ob in bpy.context.scene.objects:
        bm = bmesh.new(); bm.from_mesh(ob.data)
        bm.transform(ob.matrix_world)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        data[ob.name] = bm
    return data


def _volume(bm):
    if not bm.verts:
        return 0
    bm.verts.ensure_lookup_table()
    c = sum((v.co for v in bm.verts), bm.verts[0].co * 0) / len(bm.verts)
    return sum((f.verts[0].co - c).dot((f.verts[1].co - c).cross(f.verts[2].co - c)) for f in bm.faces) / 6


def _apart(a, b):
    """True for parts of two different options of one group, which never appear together."""
    ma, mb = re.match(r"opt_([a-z]+)_(\d+)_", a), re.match(r"opt_([a-z]+)_(\d+)_", b)
    return bool(ma and mb and ma[1] == mb[1] and ma[2] != mb[2])


def run(obj_path, free=("wheel_",), doors=None):
    """free: name prefixes allowed to stand apart (wheels sit in the arches). doors: {name
    prefix: (p0, p1, deg)}, checked swung open on their hinges as well as shut."""
    hits = _run(_load(obj_path), free)
    if doors:
        data = _load(obj_path)
        _swing(data, doors)
        hits += [f"door open, {h}" for h in _run(data, free + tuple(doors))]
    return hits


def _run(data, free):
    trees = {k: BVHTree.FromBMesh(bm) for k, bm in data.items()}
    hits = []
    for k, bm in data.items():
        open_edges = sum(1 for e in bm.edges if not e.is_manifold)
        if open_edges:
            hits.append(f"open shell: {k} ({open_edges} edges)")
        if _volume(bm) < 0:
            hits.append(f"inward shell: {k}")
    names = list(data)
    for a in names:
        if re.sub(r"^opt_[a-z]+_\d+_", "", a).startswith(free):
            continue
        va = [v.co for v in data[a].verts]
        near = any(any((trees[b].find_nearest(v)[3] or 1) < TOUCH for v in va[:: max(1, len(va) // 400)])
                   for b in names if b != a and not _apart(a, b))
        if not near:
            hits.append(f"floating: {a}")
    for i, a in enumerate(names):
        for b in names[i + 1:]:
            if not _apart(a, b) and trees[a].overlap(trees[b]):
                hits.append(f"intersecting: {a} x {b}")
    for i, a in enumerate(names):
        for b in names[i + 1:]:
            if _apart(a, b):
                continue
            n = 0
            for f in data[a].faces:
                _loc, nrm, _i, d = trees[b].find_nearest(f.calc_center_median())
                if d is not None and d < ZFIGHT and abs(nrm.dot(f.normal)) > 0.99:
                    n += 1
            if n:
                hits.append(f"z-fighting: {a} x {b} ({n} faces)")
    return hits
