"""Checks the exported Oboe: open or inward shells, floating parts, intersecting pairs, z-fighting.

Run: python3 check.py [out_dir]
"""
import sys
from pathlib import Path

import bpy
import bmesh
import numpy as np
from mathutils.bvhtree import BVHTree

from export import NAME

OUT = Path(sys.argv[1] if len(sys.argv) > 1 else Path(__file__).with_name("out"))
TOUCH = 0.004     # parts closer than this are attached
ZFIGHT = 0.0004   # parallel faces closer than this fight


def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.obj_import(filepath=str(OUT / f"{NAME}.obj"), forward_axis="NEGATIVE_Z", up_axis="Y")
    data = {}
    for ob in bpy.context.scene.objects:
        bm = bmesh.new(); bm.from_mesh(ob.data)
        bm.transform(ob.matrix_world)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        data[ob.name] = bm
    return data


def volume(bm):
    if not bm.verts:
        return 0
    bm.verts.ensure_lookup_table()
    c = sum((v.co for v in bm.verts), bm.verts[0].co * 0) / len(bm.verts)
    return sum((f.verts[0].co - c).dot((f.verts[1].co - c).cross(f.verts[2].co - c)) for f in bm.faces) / 6


def main():
    data = load()
    trees = {k: BVHTree.FromBMesh(bm) for k, bm in data.items()}
    hits = []
    for k, bm in data.items():
        open_edges = sum(1 for e in bm.edges if not e.is_manifold)
        if open_edges:
            hits.append(f"open shell: {k} ({open_edges} edges)")
        if volume(bm) < 0:
            hits.append(f"inward shell: {k}")
    names = list(data)
    for i, a in enumerate(names):
        if a.startswith("wheel_"):
            continue
        near = False
        va = [v.co for v in data[a].verts]
        for b in names:
            if b == a:
                continue
            t = trees[b]
            if any((t.find_nearest(v)[3] or 1) < TOUCH for v in va[:: max(1, len(va) // 400)]):
                near = True
                break
        if not near:
            hits.append(f"floating: {a}")
    for i, a in enumerate(names):
        for b in names[i + 1:]:
            if trees[a].overlap(trees[b]):
                hits.append(f"intersecting: {a} x {b}")
    for i, a in enumerate(names):
        for b in names:
            if b <= a:
                continue
            n = 0
            for f in data[a].faces:
                loc, nrm, _i, d = trees[b].find_nearest(f.calc_center_median())
                if d is not None and d < ZFIGHT and abs(nrm.dot(f.normal)) > 0.99:
                    n += 1
            if n:
                hits.append(f"z-fighting: {a} x {b} ({n} faces)")
    print("\n".join(hits) if hits else "clean")
    return len(hits)


if __name__ == "__main__":
    sys.exit(1 if main() else 0)
