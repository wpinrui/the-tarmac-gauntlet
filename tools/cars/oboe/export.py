"""Builds the Kaiser Oboe in Blender and writes KaiserOboe.obj and .mtl.

Run: python3 export.py out            every badge and name option, for the viewer
     python3 export.py <dir> --game   only Ivan's picks, Unity axes
"""
import sys
from pathlib import Path

import bpy
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "common"))
import blend  # noqa: E402
import body  # noqa: E402
import parts  # noqa: E402
from parts import MATERIALS, R  # noqa: E402

FLOOR = 0.35


def copy(ob, name):
    c = ob.copy(); c.data = ob.data.copy(); c.name = name
    bpy.context.scene.collection.objects.link(c)
    return c


def cut(target, cutters):
    for c in cutters:
        name, n0 = c.name, len(target.data.polygons)
        blend.boolean(target, c, "DIFFERENCE")
        if len(target.data.polygons) < 0.6 * n0:
            raise RuntimeError(f"cutting {name} wrecked {target.name}")


def build(game=False):
    blend.reset(MATERIALS)
    v, f = body.skin()
    shell = blend.make("body", v, f, "body")
    blend.relax(shell)
    ref0 = blend.tree(shell)                       # plain skin, for glass laid under the spoiler

    blend.boolean(shell, parts.spoiler(), "UNION")
    plain = copy(shell, "plain")
    ref = blend.tree(plain)

    out = []
    cutters = []
    for sgn in (1, -1):
        parts.lamp_parts(ref, cutters, out, sgn, "head")
        parts.lamp_parts(ref, cutters, out, sgn, "tail")
    parts.front_parts(ref, cutters, out)
    parts.glass_parts(ref0, cutters, out)
    out += parts.strips(ref)
    print("laid", len(out), "parts,", len(cutters), "cutters")

    arch, tubs = parts.arches()
    v, f = body.cavity(R["screen"]["x_base"] + 0.08, body.ROWS["stations"][0]["x"] - 0.0, FLOOR)
    cab = blend.make("cabin", v, f, "interior")
    for t in tubs:
        blend.boolean(cab, t, "DIFFERENCE")
    cut(shell, arch)
    blend.boolean(shell, cab, "DIFFERENCE")
    print("cabin cut")
    cut(shell, cutters)
    print("openings cut")
    for m in parts.mirrors():                      # after the glass, so no opening cuts them
        blend.boolean(shell, m, "UNION")
    mcut, mglass = parts.mirror_recess()
    cut(shell, mcut)
    out += mglass
    ecut, pipes = parts.exhaust()
    cut(shell, ecut)
    out += pipes
    cut(shell, parts.grooves(ref))
    print("grooves cut")

    blend.tidy(shell)
    blend.thin(shell, 0.55)

    out += parts.interior(FLOOR)
    out += parts.wheels()
    opts = parts.options(ref)
    if game:
        picks = R["picks"]
        for ob in opts:
            _o, g, n, i = ob.name.split("_")
            if int(n) != picks[g]:
                bpy.data.objects.remove(ob)
            else:
                ob.name = f"{g}_{i}"
    bpy.data.objects.remove(plain)
    obs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for ob in obs:
        if not ob.name.startswith(("wheel_tyre",)):
            blend.smooth(ob)
        else:
            blend.smooth(ob, 50)
    return obs


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    game = "--game" in sys.argv
    out_dir = Path(args[0]) if args else HERE / "out"
    obs = build(game)
    blend.export(obs, out_dir, R["name"], MATERIALS, game=game)
    print("wrote", out_dir / f"{R['name']}.obj")
