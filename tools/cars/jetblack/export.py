"""Builds the Archer Jet Black in Blender and writes ArcherJetBlack.obj and .mtl.

Run: python3 export.py out            every badge and name option, for the viewer
     python3 export.py <dir> --game   only Ivan's picks, Unity axes
"""
import sys
from pathlib import Path

import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "common"))
import blend  # noqa: E402
import body  # noqa: E402
import parts  # noqa: E402
from parts import MATERIALS, R  # noqa: E402

FLOOR = 0.25
STOP = None     # a .blend path: save the scene just before the doors are cut, and stop


def copy(ob, name):
    c = ob.copy(); c.data = ob.data.copy(); c.name = name
    bpy.context.scene.collection.objects.link(c)
    return c


def cut(target, cutters):
    for c in cutters:
        name, n0 = c.name, len(target.data.polygons)
        keep = copy(target, "keep")
        blend.boolean(target, c, "DIFFERENCE")
        if len(target.data.polygons) < 0.6 * n0:
            target.data = keep.data
            print(f"cutting {name} wrecked {target.name}: left out")
        bpy.data.objects.remove(keep)


def build(game=False):
    blend.reset(MATERIALS)
    v, f = body.skin()
    shell = blend.make("body", v, f, "body")
    blend.relax(shell)
    plain = copy(shell, "plain")
    ref = blend.tree(plain)

    out = []
    cutters = []
    for sgn in (1, -1):
        parts.headlamps(ref, cutters, out, sgn)
        parts.taillamps(ref, cutters, out, sgn)
    parts.front_parts(ref, cutters, out)
    parts.glass_parts(ref, cutters, out)
    parts.side_vents(ref, cutters, out)
    parts.hood_vents(ref, cutters, out)
    parts.third_brake(ref, out)
    print("laid", len(out), "parts,", len(cutters), "cutters")

    arch, tubs = parts.arches()
    v, f = body.cavity(parts.CABIN_FRONT, parts.CABIN_BACK, FLOOR)
    cab = blend.make("cabin", v, f, "interior")
    for t in tubs:
        blend.boolean(cab, t, "DIFFERENCE")
    cut(shell, arch)
    blend.boolean(shell, cab, "DIFFERENCE")
    print("cabin cut")
    cut(shell, cutters)
    print("openings cut, hollows dropped:", blend.drop_voids(shell))
    housings = parts.mirrors()
    mcut, mglass = parts.mirror_recess()
    for h, c in zip(housings, mcut):
        cut(h, [c])
    out += housings + parts.mirror_arms() + mglass
    ecut, pipes, pocket = parts.exhaust()
    cut(shell, ecut)
    out += pipes
    cut(shell, parts.rear_parts(ref, out, pocket))
    cut(shell, parts.grooves(ref))
    print("grooves cut")

    out += parts.interior(FLOOR)
    if STOP:
        bpy.ops.wm.save_as_mainfile(filepath=STOP)
        return
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
        blend.smooth(ob, 50 if ob.name.startswith("wheel_tyre") else 35)
    return obs


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    game = "--game" in sys.argv
    out_dir = Path(args[0]) if args else HERE / "out"
    obs = build(game)
    blend.export(obs, out_dir, R["name"], MATERIALS, game=game)
    if game:
        blend.write_doors(out_dir / f"{R['name']}.doors.json",
                          {"door_l": parts.door_hinge(parts.DOOR_PICK, 1), "door_r": parts.door_hinge(parts.DOOR_PICK, -1)})
    print("wrote", out_dir / f"{R['name']}.obj")
