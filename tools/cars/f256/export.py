"""Builds the Feretti F-256V8T in Blender and writes FerettiF256V8T.obj and .mtl.

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
import parts  # noqa: E402
from parts import MATERIALS, R  # noqa: E402


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
    shell = parts.shell()
    cut(shell, [parts.cockpit_cut()] + parts.inlet_cuts())
    blend.tidy(shell)
    print("body cut")

    rear = parts.rear_wing()
    plain = parts.merge([copy(shell, "plain"), copy(rear, "rw_ref")], "plain")     # rays only: no boolean
    ref = blend.tree(plain)

    parts.floor()
    parts.rain_light()
    parts.front_wing()
    parts.wheels()
    parts.suspension()
    parts.mirrors()
    parts.steering()
    parts.driver()
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
        blend.smooth(ob, 50 if ob.name.startswith(("wheel_tyre", "driver")) else 35)
    return obs


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    game = "--game" in sys.argv
    out_dir = Path(args[0]) if args else HERE / "out"
    obs = build(game)
    blend.export(obs, out_dir, R["name"], MATERIALS, game=game)
    print("wrote", out_dir / f"{R['name']}.obj")
