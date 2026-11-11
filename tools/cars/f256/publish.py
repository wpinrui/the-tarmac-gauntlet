"""Writes viewer.html, the orbitable viewer of out/FerettiF256V8T.obj with every option.

Run: python3 export.py out && python3 publish.py"""
import sys
from pathlib import Path

import bpy  # noqa: F401  (parts needs Blender loaded)

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "common"))
import viewer  # noqa: E402
from parts import GROUPS, LAMPS, MATERIALS, R, SHOW  # noqa: E402

if __name__ == "__main__":
    viewer.write(HERE / "out" / f"{R['name']}.obj", HERE / "viewer.html", "Feretti F-256V8T",
                 MATERIALS, R["picks"], LAMPS, GROUPS, SHOW)
