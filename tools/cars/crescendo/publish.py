"""Writes viewer.html, the orbitable viewer of out/ArcherCrescendo.obj with every option.

Run: python3 export.py out && python3 publish.py"""
import sys
from pathlib import Path

import bpy  # noqa: F401  (parts needs Blender loaded)

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "common"))
import viewer  # noqa: E402
from parts import GROUPS, LAMPS, MATERIALS, R, door_hinge  # noqa: E402

p0, p1, deg = door_hinge()
DOOR = {"label": "Door", "parts": "door_l", "styles": {"Hinged at the front": {"axis": [p0, p1], "open": deg}}}

if __name__ == "__main__":
    viewer.write(HERE / "out" / f"{R['name']}.obj", HERE / "viewer.html", "Archer Crescendo",
                 MATERIALS, R["picks"], LAMPS, GROUPS, doors=[DOOR])
