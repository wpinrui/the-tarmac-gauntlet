"""Writes out/LibertyCollier.obj and .mtl from build.py's groups.

Viewer axes: x forward, y up, z right (build y left becomes -z).
Option objects are named opt_<group>_<n>_<part> so the viewer can toggle them.
"""
import sys
from pathlib import Path

import numpy as np

import build

NAME = "LibertyCollier"
OUT = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).with_name("out")

MATERIALS = {
    # name: (diffuse rgb, specular, alpha, emissive rgb)
    "body": ((0.89, 0.46, 0.06), 0.7, 1.0, None),
    "glass": ((0.06, 0.07, 0.09), 0.9, 1.0, None),
    "trim": ((0.10, 0.10, 0.11), 0.3, 1.0, None),
    "grille": ((0.08, 0.08, 0.09), 0.3, 1.0, None),
    "interior": ((0.06, 0.06, 0.07), 0.1, 1.0, None),
    "tyre": ((0.10, 0.10, 0.11), 0.1, 1.0, None),
    "rim": ((0.91, 0.92, 0.93), 0.7, 1.0, None),
    "rim_inner": ((0.30, 0.31, 0.33), 0.4, 1.0, None),
    "hub": ((0.77, 0.78, 0.80), 0.9, 1.0, None),
    "brake_disc": ((0.42, 0.43, 0.44), 0.5, 1.0, None),
    "mirror_glass": ((0.70, 0.73, 0.76), 1.0, 1.0, None),
    "chrome": ((0.88, 0.90, 0.92), 1.0, 1.0, None),
    "soot": ((0.04, 0.04, 0.04), 0.1, 1.0, None),
    "badge_red": ((0.75, 0.16, 0.18), 0.7, 1.0, None),
    "badge_navy": ((0.08, 0.16, 0.29), 0.7, 1.0, None),
    "badge_gold": ((0.78, 0.60, 0.24), 0.9, 1.0, None),
    "badge_dark": ((0.10, 0.12, 0.14), 0.8, 1.0, None),
    "lamp_head": ((0.67, 0.70, 0.74), 0.9, 1.0, (1.0, 0.97, 0.9)),
    "lamp_fog": ((0.76, 0.78, 0.82), 0.9, 1.0, (1.0, 0.97, 0.9)),
    "lamp_tail": ((0.43, 0.04, 0.06), 0.7, 1.0, (0.55, 0.0, 0.0)),
}
RENAME = {"paint": "body", "liner": "interior", "headlamp": "lamp_head", "fog": "lamp_fog",
          "taillamp": "lamp_tail", "nuts": "hub", "brake": "brake_disc", "mirror": "mirror_glass"}

# numbered viewer choices: group -> [(build position, variant)]
CHOICES = {
    "frontbadge": [("nose", "colour"), ("nose", "chrome"), ("grille", "colour"), ("grille", "chrome")],
    "rearbadge": [("tailgate", "colour"), ("tailgate", "chrome"), ("low", "colour"), ("low", "chrome")],
    "name": [(p, f) for p in ("left", "right", "centre", "sides") for f in ("script", "block")],
}
GROUP = {"badgeF": "frontbadge", "badgeR": "rearbadge", "name": "name"}


def object_name(key):
    if not key.startswith("opt|"):
        return key, RENAME.get(key, key)
    _, g, pos, var, mat = key.split("|")
    g = GROUP[g]
    n = CHOICES[g].index((pos, var)) + 1
    return f"opt_{g}_{n}_{mat}", RENAME.get(mat, mat)


def main():
    groups = build.build()
    lines, used, base = [f"mtllib {NAME}.mtl"], set(), 1
    for key, m in groups.items():
        obj, mat = object_name(key)
        used.add(mat)
        v = np.asarray(m.vertices)
        v = np.c_[v[:, 0], v[:, 2], -v[:, 1]]
        lines.append(f"o {obj}")
        lines += ["v {:.5f} {:.5f} {:.5f}".format(*p) for p in v]
        lines.append(f"usemtl {mat}")
        lines += ["f {} {} {}".format(*(f + base)) for f in m.faces]
        base += len(v)
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / f"{NAME}.obj").write_text("\n".join(lines) + "\n")
    print("triangles", sum(len(m.faces) for m in groups.values()))
    out = []
    for name in sorted(used):
        col, spec, alpha, emit = MATERIALS[name]
        out += [f"newmtl {name}", "Kd {:.3f} {:.3f} {:.3f}".format(*col),
                "Ks {0:.2f} {0:.2f} {0:.2f}".format(spec * 0.5), "Ns 200", f"d {alpha:.2f}",
                "Ke {:.2f} {:.2f} {:.2f}".format(*(emit or (0, 0, 0))), ""]
    (OUT / f"{NAME}.mtl").write_text("\n".join(out))


if __name__ == "__main__":
    main()
