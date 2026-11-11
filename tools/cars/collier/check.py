"""Defect checks on out/LibertyCollier.obj: open or inward shells, floating parts, intersecting
pairs and z-fighting, with the doors shut and open. Run after Ivan confirms the direction.

Run: python3 export.py out && python3 check.py"""
import sys
from pathlib import Path

import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "common"))
import check  # noqa: E402
import export  # noqa: E402

if __name__ == "__main__":
    export.reset()
    shell = export.build_shell()
    export.cut_arches(shell)
    bvh = export.BVHTree.FromObject(shell, bpy.context.evaluated_depsgraph_get())
    doors = {"door_l": export.door_hinge(bvh, 1), "door_r": export.door_hinge(bvh, -1)}
    hits = check.run(HERE / "out" / f"{export.NAME}.obj", doors=doors)
    print("\n".join(hits) or "clean")
    sys.exit(1 if hits else 0)
