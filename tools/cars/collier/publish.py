"""Writes viewer.html, the orbitable viewer of out/LibertyCollier.obj with every option, a
wireframe toggle, one toggle per lamp group and the driver's door on its hinge.

Run: python3 export.py out && python3 publish.py"""
import sys
from pathlib import Path

import bpy  # noqa: F401  (export needs Blender loaded)

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "common"))
import viewer  # noqa: E402
from body import ROWS  # noqa: E402
from export import BVH, MATERIALS, NAME, build_shell, cut_arches, door_hinge  # noqa: E402

LAMPS = {"Headlights": ["lamp_head"], "Tail": ["lamp_tail"], "Brake": ["lamp_brake"]}
GROUPS = {"frontbadge": "Front badge", "rearbadge": "Rear badge", "name": "Name"}

if __name__ == "__main__":
    import export
    export.reset()
    shell = build_shell()
    cut_arches(shell)
    p0, p1, deg = door_hinge(export.BVHTree.FromObject(shell, bpy.context.evaluated_depsgraph_get()))
    door = {"label": "Door", "parts": "door_l", "styles": {"Hinged at the front": {"axis": [p0, p1], "open": deg}}}
    viewer.write(HERE / "out" / f"{NAME}.obj", HERE / "viewer.html", "Liberty Collier",
                 MATERIALS, ROWS["picks"], LAMPS, GROUPS, doors=[door])
