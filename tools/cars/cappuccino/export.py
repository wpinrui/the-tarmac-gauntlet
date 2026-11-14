"""Kaiser Cappuccino: build every piece with Blender's Python module and export OBJ + MTL.
    python3 export.py <dir>          all options, for the viewer
    python3 export.py <dir> --game   only Ivan's picks, Unity axes"""
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "common"))
sys.path.insert(0, str(HERE))

import numpy as np  # noqa: E402
import bpy  # noqa: E402

import blend as B  # noqa: E402
import body  # noqa: E402
import parts  # noqa: E402
import rows  # noqa: E402

NAME = "KaiserCappuccino"

# name: (rgb, spec, alpha, emit)
MATERIALS = {
    "body": ((0.96, 0.55, 0.08), 0.8, 1.0, None),
    "interior": ((0.07, 0.07, 0.08), 0.1, 1.0, None),
    "trim": ((0.03, 0.03, 0.035), 0.3, 1.0, None),
    "glass": ((0.10, 0.12, 0.14), 0.95, 0.35, None),
    "chrome": ((0.80, 0.81, 0.83), 1.0, 1.0, None),
    "rubber": ((0.05, 0.05, 0.05), 0.05, 1.0, None),
    "rim": ((0.62, 0.63, 0.65), 0.9, 1.0, None),
    "brake": ((0.35, 0.35, 0.36), 0.5, 1.0, None),
    "seat": ((0.12, 0.12, 0.13), 0.1, 1.0, None),
    "mirror": ((0.75, 0.78, 0.80), 1.0, 1.0, None),
    "badge": ((0.85, 0.86, 0.88), 1.0, 1.0, None),
    "lamp_housing": ((0.05, 0.05, 0.06), 0.6, 1.0, None),
    "lamp_head": ((0.92, 0.93, 0.95), 0.9, 1.0, (1.0, 0.97, 0.9)),
    "lamp_tail": ((0.45, 0.03, 0.03), 0.8, 1.0, (1.0, 0.05, 0.03)),
    "lamp_brake": ((0.55, 0.04, 0.04), 0.8, 1.0, (1.0, 0.08, 0.05)),
    "lamp_rain": ((0.40, 0.04, 0.04), 0.8, 1.0, (1.0, 0.1, 0.05)),
    "lens": ((0.85, 0.87, 0.9), 0.95, 0.3, None),
}


def build(game=False):
    B.reset(MATERIALS)
    opts = {"rim": 1, "badgef": 1, "badger": 1, "name": 1}
    opts.update(rows.PICKS)
    parts.build(opts, game)
    obs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    return obs


def main():
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else HERE / "out"
    game = "--game" in sys.argv
    obs = build(game)
    for ob in obs:
        B.smooth(ob)
    B.export(obs, out, NAME, MATERIALS, game=game)
    if game:
        B.write_doors(out / f"{NAME}.doors.json", parts.DOORS)
    else:
        import json
        (out / "doors.json").write_text(json.dumps({k: [list(map(float, v[0])), list(map(float, v[1])), v[2]]
                                                    for k, v in parts.DOORS.items()}))


if __name__ == "__main__":
    main()
