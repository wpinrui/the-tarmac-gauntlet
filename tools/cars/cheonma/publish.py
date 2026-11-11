"""Writes viewer.html, the orbitable viewer of out/CheonmaCX24Si.obj with every option, a
wireframe toggle, one toggle per lamp group and the driver's door on each hinge style.

Run: python3 export.py out && python3 publish.py"""
import sys
from pathlib import Path

import bpy  # noqa: F401  (export needs Blender loaded)

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "common"))
import viewer  # noqa: E402
from body import ROWS  # noqa: E402
from export import DOOR_PICK, DOOR_STYLES, MATERIALS, NAME  # noqa: E402

LAMPS = {"Headlights": ["lamp_head"], "Tail": ["lamp_tail"], "Brake": ["lamp_brake"], "Rain light": ["lamp_rain"]}
GROUPS = {"frontbadge": "Front badge", "rearbadge": "Rear badge", "name": "Name"}
DOOR = {"label": "Door", "parts": "door_l", "pick": DOOR_PICK,
        "styles": {k: {"axis": [p0, p1], "open": deg} for k, (p0, p1, deg) in DOOR_STYLES.items()}}

if __name__ == "__main__":
    viewer.write(HERE / "out" / f"{NAME}.obj", HERE / "viewer.html", "Seorak Cheonma CX-24Si",
                 MATERIALS, ROWS["picks"], LAMPS, GROUPS, doors=[DOOR])
