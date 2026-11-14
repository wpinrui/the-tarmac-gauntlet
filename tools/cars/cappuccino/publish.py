"""Kaiser Cappuccino: the orbitable viewer page. Builds every option with export.py first.
    python3 publish.py <out dir>"""
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "common"))
sys.path.insert(0, str(HERE))

import viewer  # noqa: E402

if __name__ == "__main__":
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else HERE / "out"
    subprocess.run([sys.executable, str(HERE / "export.py"), str(out)], check=True)
    import export  # noqa: E402
    import parts  # noqa: E402
    import rows  # noqa: E402
    import json
    doors = json.loads((out / "doors.json").read_text())
    picks = {"rim": 1, "badgef": 1, "badger": 1, "name": 1}
    picks.update(rows.PICKS)
    viewer.write(out / f"{export.NAME}.obj", out / "cappuccino.html", "Kaiser Cappuccino", export.MATERIALS, picks,
                 {"Headlamps": ["lamp_head"], "Tail lamps": ["lamp_tail"], "Brake lamps": ["lamp_brake"],
                  "Rain light": ["lamp_rain"]},
                 parts.OPTION_GROUPS,
                 doors=[{"label": "Doors", "parts": k, "styles": {"open": {"axis": [v[0], v[1]], "open": v[2]}}}
                        for k, v in doors.items()])
