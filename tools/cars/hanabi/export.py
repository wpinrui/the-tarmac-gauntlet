"""Builds the Tora Hanabi in Blender and writes ToraHanabi.obj and .mtl.

Run: python3 export.py out            every roof, badge and name option, for the viewer
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


def copy(ob, name):
    c = ob.copy(); c.data = ob.data.copy(); c.name = name
    bpy.context.scene.collection.objects.link(c)
    return c


def cut(target, cutters):
    """Cut each cutter away. Exact booleans can fail on a near grazing cutter: then try the
    hole tolerant mode, then nudge the cutter a fraction of a millimetre."""
    for c in cutters:
        name, n0 = c.name, len(target.data.polygons)
        for k in range(4):
            keep, extra = copy(target, "keep"), copy(c, name + "_try")
            blend.boolean(target, extra, "DIFFERENCE", tolerant=k % 2 == 1)
            if len(target.data.polygons) >= 0.6 * n0:
                bpy.data.objects.remove(keep)
                break
            target.data = keep.data
            bpy.data.objects.remove(keep)
            c.location.z += 0.0003
        else:
            raise RuntimeError(f"cutting {name} wrecked {target.name}")
        bpy.data.objects.remove(c)


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
    plain = copy(shell, "plain")
    ref = blend.tree(plain)

    out, cutters = [], []
    parts.headlamps(ref, cutters, out)
    parts.taillamps(ref, cutters, out)
    parts.small_lamps(ref, out)
    shapes = parts.openings(ref, cutters)
    out += parts.grille_parts(ref, shapes)
    print("laid", len(out), "parts,", len(cutters), "cutters")

    arch, tubs = parts.arches()
    cabs = parts.cockpit_cutters()
    for t in tubs:
        blend.boolean(cabs[0], t, "DIFFERENCE")
    cut(shell, arch)
    for c in cabs:
        blend.boolean(shell, c, "DIFFERENCE")
    print("cockpit cut")
    cut(shell, cutters)
    print("openings cut, hollows dropped:", blend.drop_voids(shell))
    # the openings are dark inside
    from shapely.geometry import Point
    blend.paint(shell, "grille", lambda x, y, z: x > 1.6 and any(s.buffer(0.004).contains(Point(y, z)) for s in shapes)
                and not plain_front(ref, x, y, z))
    join(shell, blend.union_all(parts.mirrors(), "mirrors"))
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

    env, xs, rcv = parts.envelope()
    eref = blend.tree(env)
    out += parts.screen_parts(eref, ref)
    out.append(parts.rear_view_mirror(eref))
    roofs = parts.roof_options(eref, xs, rcv, plain)
    bpy.data.objects.remove(env)
    print("roof options built")

    out += parts.interior()
    out += parts.wheels()
    opts = parts.options(ref)
    for n, objs in roofs.items():
        for i, ob in enumerate(objs):
            ob.name = f"opt_roof_{n}_{i}"
            opts.append(ob)
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


def plain_front(ref, x, y, z):
    """True on the bumper's own face, so only the inside of an opening is painted."""
    loc, _n = blend.cast(ref, parts.front_fr(), y, z)
    return loc is not None and abs(loc[0] - x) < 0.004


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    game = "--game" in sys.argv
    out_dir = Path(args[0]) if args else HERE / "out"
    obs = build(game)
    blend.export(obs, out_dir, R["name"], MATERIALS, game=game)
    print("wrote", out_dir / f"{R['name']}.obj")
