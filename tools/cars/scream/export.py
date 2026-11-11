"""Builds the Kaiser Scream in Blender and writes KaiserScream.obj and .mtl.

Run: python3 export.py out            every roof, wing, badge and name option, for the viewer
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


def open_edges(ob):
    import bmesh
    bm = bmesh.new(); bm.from_mesh(ob.data)
    n = sum(1 for e in bm.edges if not e.is_manifold)
    bm.free()
    return n


def triangulate(ob):
    import bmesh
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(ob.data); bm.free()


def cut(target, cutters):
    """Cut each cutter away. Exact booleans can fail on a near grazing cutter or leave open
    edges: then try the hole tolerant mode, then nudge the cutter a fraction of a millimetre,
    and keep the try that leaves the fewest open edges."""
    triangulate(target)
    for c in cutters:
        name, n0, e0 = c.name, len(target.data.polygons), open_edges(target)
        start, tries = target.data, []
        for k in range(6):
            target.data = start.copy()
            blend.boolean(target, copy(c, name + "_try"), "DIFFERENCE", tolerant=k % 2 == 1)
            if len(target.data.polygons) >= 0.6 * n0:
                tries.append((open_edges(target), k, target.data))
                if tries[-1][0] <= e0:
                    break
            c.location.z += 0.0003
        if not tries:
            raise RuntimeError(f"cutting {name} wrecked {target.name}")
        target.data = min(tries, key=lambda t: t[:2])[2]
        if min(tries, key=lambda t: t[:2])[0] > e0:
            print(f"cut {name}: open edges {e0} -> {min(tries, key=lambda t: t[:2])[0]}")
        bpy.data.objects.remove(c)


def join(shell, part):
    """Union a part onto the body, nudging it a fraction of a millimetre if the boolean fails."""
    triangulate(shell)
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
    for sgn in (1, -1):
        parts.headlamps(ref, cutters, out, sgn)
    parts.front_parts(ref, cutters, out)
    parts.repeaters(ref, out)
    parts.side_intake_parts(ref, cutters, out)
    parts.deck_parts(ref, out)
    parts.screen_parts(ref, cutters, out)
    parts.taillamps(ref, cutters, out)
    print("laid", len(out), "parts,", len(cutters), "cutters")

    arch, tubs = parts.arches()
    room, opening = parts.cockpit_cutters()
    for t in tubs:
        blend.boolean(room, t, "DIFFERENCE")
    cut(shell, arch)
    cut(shell, [room, opening])
    print("cockpit cut")
    cut(shell, cutters)
    print("openings cut, hollows dropped:", blend.drop_voids(shell))
    for m in parts.mirrors():
        join(shell, m)
    mcut, mglass = parts.mirror_recess()
    cut(shell, mcut)
    out += mglass
    ecut, pipes = parts.exhaust()
    cut(shell, ecut)
    out += pipes
    parts.rear_parts(ref, out)
    cut(shell, parts.grooves(ref))
    print("grooves cut")

    out += parts.interior()
    print("body faces", len(shell.data.polygons))
    doors = []
    for sgn in (1, -1):
        doors.append(parts.door(ref, shell, sgn)[0])
        print("door cut free:", doors[-1].name, len(doors[-1].data.polygons), "body faces", len(shell.data.polygons))
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
        blend.tidy(ob)
        blend.smooth(ob, 50 if ob.name.startswith("wheel_tyre") else 35)
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
