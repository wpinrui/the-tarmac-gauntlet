"""Kaiser Cappuccino: run the shared defect checks on the viewer export, with the doors shut
and swung open.
    python3 check.py <dir>    the folder export.py wrote"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "common"))

import bpy  # noqa: E402,F401  (loads bmesh)
import check  # noqa: E402

out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent / "out"
doors = {k: (tuple(v[0]), tuple(v[1]), v[2]) for k, v in json.loads((out / "doors.json").read_text()).items()}
hits = check.run(out / "KaiserCappuccino.obj", doors=doors)
for h in hits:
    print(h)
print(len(hits), "hits")
