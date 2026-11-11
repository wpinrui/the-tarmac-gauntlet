"""Builds the Liberty Fantasy in Blender and writes LibertyFantasy.obj and .mtl.

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

FLOOR = 0.32


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


def join(shell, part):
    """Union a part onto the body. Exact booleans can fail on near touching faces: then nudge
    the part a fraction of a millimetre and try again."""
    n0 = len(shell.data.polygons)
    for k in range(4):
        keep, extra = copy(shell, "keep"), copy(part, part.name + "_try")
        blend.boolean(shell, extra, "UNION", tolerant=k > 0)
        if len(shell.data.polygons) >= 0.6 * n0:
            bpy.data.objects.remove(keep)
            bpy.data.objects.remove(part)
            return
        shell.data = keep.data
        bpy.data.objects.remove(keep)
        part.location.x += 0.0003
    raise RuntimeError(f"joining {part.name} wrecked the body")


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
        parts.headlamps(ref, cutters, out, sgn)
        parts.taillamps(ref, cutters, out, sgn)
    parts.front_parts(ref, cutters, out)
    parts.rear_parts(ref, cutters, out)
    parts.glass_parts(ref0, cutters, out)
    print("laid", len(out), "parts,", len(cutters), "cutters")

    arch, tubs = parts.arches()
    v, f = body.cavity(R["screen"]["x_base"] - 0.06, body.TAIL + 0.12, FLOOR)
    cab = blend.make("cabin", v, f, "interior")
    for t in tubs:
        blend.boolean(cab, t, "DIFFERENCE")
    cut(shell, arch)
    blend.boolean(shell, cab, "DIFFERENCE")
    print("cabin cut")
    cut(shell, cutters)
    print("openings cut, hollows dropped:", blend.drop_voids(shell))
    join(shell, blend.union_all(parts.mirrors(), "mirrors"))   # after the glass, so no opening cuts them
    mcut, mglass = parts.mirror_recess()
    cut(shell, mcut)
    out += mglass
    ecut, pipes = parts.exhaust()
    cut(shell, ecut)
    out += pipes
    cut(shell, parts.grooves(ref))
    print("grooves cut")

    out += parts.interior(FLOOR)
    doors = [parts.door(ref, shell, out, FLOOR, sgn)[0] for sgn in (1, -1)]
    print("doors cut free")
    for ob in [shell] + doors:
        blend.tidy(ob)
        blend.thin(ob, 0.55)

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
    if game:
        blend.write_doors(out_dir / f"{R['name']}.doors.json", {"door_l": parts.door_hinge(1), "door_r": parts.door_hinge(-1)})
    print("wrote", out_dir / f"{R['name']}.obj")
