"""Defect checks on out/CheonmaCX24Si.obj: open or inward shells, floating parts, intersecting
pairs and z-fighting, with the doors shut and open on the picked hinge. Run after Ivan
confirms the direction.

Run: python3 export.py out && python3 check.py"""
import sys
from pathlib import Path

import bpy  # noqa: F401  (export needs Blender loaded)

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "common"))
import check  # noqa: E402
from export import DOOR_PICK, NAME, door_hinge  # noqa: E402

if __name__ == "__main__":
    doors = {"door_l": door_hinge(DOOR_PICK, 1), "door_r": door_hinge(DOOR_PICK, -1)}
    hits = check.run(HERE / "out" / f"{NAME}.obj", doors=doors)
    print("\n".join(hits) or "clean")
    sys.exit(1 if hits else 0)
