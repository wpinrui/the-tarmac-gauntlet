"""Defect checks on out/ArcherTachyon.obj: open or inward shells, floating parts,
intersecting pairs and z-fighting, with the doors shut and open. Run after Ivan confirms the direction.

Run: python3 export.py out && python3 check.py"""
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "common"))
import bpy  # noqa: E402,F401  (check needs Blender loaded first)
import check  # noqa: E402
from parts import DOOR_PICK, door_hinge  # noqa: E402

if __name__ == "__main__":
    hits = check.run(HERE / "out" / "ArcherTachyon.obj", doors={"door_l": door_hinge(DOOR_PICK, 1), "door_r": door_hinge(DOOR_PICK, -1)})
    print("\n".join(hits) or "clean")
    sys.exit(1 if hits else 0)
