"""Renders a stretch of the circuit as the race draws it, in Gamma and in Linear colour space.

A stand-in for the race scene, built from the game's own numbers: the sky, sun, moon, ambient light, fog and grade from
SkyClock, SkyGrade and the sky shader (sky.py), the track, kerb and grass colours, a car, a tree line and far hills,
and at night the floodlights' pools as LightPool.shader draws them. Cycles works out only what Unity works out per
pixel: where the sun or moon falls and its shadows, and the sky's reflection in the paint. The colour spaces then
differ only in how the shading adds up (see hq.py), with the Linear lamp from LinearLight.Match.

Usage: python3 raceday.py out_dir shot hour     shot is "straight" or "car"; REUSE=1 keeps an earlier render
"""
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import sky as skyclock  # noqa: E402
from post import lin, enc, frame, bloom, save  # noqa: E402

out_dir, shot, hour = sys.argv[1], sys.argv[2], float(sys.argv[3])
W, H = 1600, 900
# The most a glint of the sun or a lamp gives, per unit of light: about where the game's widened highlight peaks.
GLINT = 100.0


def b(v):
    return (v[0], v[2], v[1])


def material(name, colour, rough, metal=0.0, coat=0.0, emit=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    # Colours are the game's sRGB numbers: Cycles works in linear light.
    p.inputs["Base Color"].default_value = (*lin(colour), 1)
    p.inputs["Roughness"].default_value = rough
    p.inputs["Metallic"].default_value = metal
    p.inputs["Coat Weight"].default_value = coat
    p.inputs["Coat Roughness"].default_value = 0.03
    if emit is not None:
        p.inputs["Emission Color"].default_value = (*lin(emit), 1)
        p.inputs["Emission Strength"].default_value = 1.0
    return m


def box(name, centre, size, mat):
    bpy.ops.mesh.primitive_cube_add(size=1, location=b(centre))
    ob = bpy.context.object
    ob.name = name
    ob.scale = b(size)
    ob.data.materials.append(mat)
    return ob


def scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = int(os.environ.get("SAMPLES", "24"))
    sc.cycles.use_denoising = False
    sc.cycles.max_bounces = 2
    sc.cycles.diffuse_bounces = 0
    sc.cycles.glossy_bounces = 1
    sc.render.resolution_x, sc.render.resolution_y = W, H
    grass = material("grass", (0.25, 0.34, 0.18), 0.95)
    asphalt = material("asphalt", (0.17, 0.17, 0.18), 0.75)
    white = material("line", (0.93, 0.93, 0.91), 0.6)
    red = material("kerb", (0.78, 0.1, 0.1), 0.6)
    trees = material("trees", (0.16, 0.22, 0.10), 0.9)
    hills = material("hills", (0.30, 0.38, 0.21), 0.95)
    steel = material("armco", (0.62, 0.63, 0.65), 0.35, metal=0.8)
    box("ground", (0, -0.05, 1000), (9000, 0.1, 9000), grass)
    box("track", (0, 0, 900), (12, 0.02, 2000), asphalt)
    for x in (-5.7, 5.7): box("edge", (x, 0.012, 900), (0.12, 0.02, 2000), white)
    for side in (-1, 1):
        for k in range(-40, 800):
            box("kerb", (side * 6.6, 0.03, k * 1.25 + 0.625), (1.2, 0.06, 1.25), red if k % 2 == 0 else white)
        box("armco", (side * 12, 0.55, 900), (0.08, 0.35, 2000), steel)
    rng = np.random.default_rng(3)
    for k in range(260):
        side = -1 if k % 2 else 1
        z, x = rng.uniform(-50, 1400), side * rng.uniform(38, 140)
        r = rng.uniform(3, 6)
        bpy.ops.mesh.primitive_ico_sphere_add(radius=r, subdivisions=2, location=b((x, r * 1.6, z)))
        bpy.context.object.data.materials.append(trees)
    for x, z, r in ((-2500, 3500, 900), (1800, 4200, 1200), (300, 5200, 1400), (-4200, 2000, 1000)):
        bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=b((x, -r * 0.82, z)))
        ob = bpy.context.object
        ob.scale = (1, 1, 0.5)
        ob.data.materials.append(hills)
    car()
    return sc


def car():
    path = os.path.join(ROOT, "unity", "Assets", "Art", "Vehicles", "CheonmaCX24Si", "CheonmaCX24Si.obj")
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=path)
    for ob in set(bpy.data.objects) - before:
        ob.location = b((-2.5, 0, 24))
        ob.rotation_euler = (0, 0, math.pi)
        for m in ob.data.materials:
            p = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
            if p is None: continue
            c = np.array(p.inputs["Base Color"].default_value[:3])
            if m.name.startswith("body"):
                c = np.array([0.78, 0.06, 0.08])
                p.inputs["Coat Weight"].default_value = 1
                p.inputs["Coat Roughness"].default_value = 0.03
                p.inputs["Roughness"].default_value = 0.35
            p.inputs["Base Color"].default_value = (*lin(c), 1)


def world(s):
    """What the paint reflects: the sky, linear. Diffuse light from the sky is the ambient AOV-free trilight below."""
    w = bpy.data.worlds.new("sky")
    w.use_nodes = True
    nt = w.node_tree
    bg = nt.nodes["Background"]
    lp = nt.nodes.new("ShaderNodeLightPath")
    tex = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tex.outputs["Generated"], sep.inputs[0])
    ramp = nt.nodes.new("ShaderNodeMix"); ramp.data_type = "RGBA"
    up = nt.nodes.new("ShaderNodeMath"); up.operation = "POWER"; up.inputs[1].default_value = 0.3
    clamp = nt.nodes.new("ShaderNodeClamp")
    nt.links.new(sep.outputs[2], clamp.inputs[0])
    nt.links.new(clamp.outputs[0], up.inputs[0])
    nt.links.new(up.outputs[0], ramp.inputs[0])
    ramp.inputs[6].default_value = (*lin(s["fog"]), 1)
    ramp.inputs[7].default_value = (*lin(s["zenith"]), 1)
    gl = nt.nodes.new("ShaderNodeMath"); gl.operation = "MAXIMUM"
    nt.links.new(lp.outputs["Is Glossy Ray"], gl.inputs[0])
    nt.links.new(lp.outputs["Is Camera Ray"], gl.inputs[1])
    mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = "RGBA"; mul.blend_type = "MULTIPLY"
    nt.links.new(gl.outputs[0], mul.inputs[0])
    mul.inputs[6].default_value = (0, 0, 0, 1)
    nt.links.new(ramp.outputs[2], mul.inputs[7])
    mul.blend_type = "MIX"
    nt.links.new(mul.outputs[2], bg.inputs["Color"])
    bg.inputs["Strength"].default_value = 1
    bpy.context.scene.world = w


def lamp(s):
    d = s["light_dir"]
    l = bpy.data.lights.new("sun", "SUN")
    l.energy = math.pi  # white, intensity 1: scaled per colour space after
    l.angle = math.radians(0.6)
    o = bpy.data.objects.new("sun", l)
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector(b(-d)).to_track_quat("-Z", "Y")
    bpy.context.scene.collection.objects.link(o)


def camera(pos, target, fov):
    cam = bpy.data.cameras.new("cam")
    cam.sensor_fit = "VERTICAL"
    cam.angle_y = math.radians(fov)
    cam.clip_end = 20000
    co = bpy.data.objects.new("cam", cam)
    co.location = b(pos)
    co.rotation_mode = "QUATERNION"
    co.rotation_quaternion = (Vector(b(target)) - Vector(b(pos))).to_track_quat("-Z", "Y")
    bpy.context.scene.collection.objects.link(co)
    bpy.context.scene.camera = co
    # Each pixel's view direction, in Unity's frame, for the sky.
    q = co.rotation_quaternion
    ys, xs = np.mgrid[0:H, 0:W]
    t = math.tan(math.radians(fov) / 2)
    vx = (xs + 0.5 - W / 2) / (H / 2) * t
    vy = -(ys + 0.5 - H / 2) / (H / 2) * t
    v = np.stack([vx, vy, -np.ones_like(vx)], -1)
    m = np.array(q.to_matrix())
    dirs = v @ m.T
    dirs /= np.linalg.norm(dirs, axis=-1, keepdims=True)
    return dirs[..., [0, 2, 1]]


def read_layers(path):
    import OpenEXR
    out = {}
    with OpenEXR.File(path, separate_channels=True) as f:
        for part in f.parts:
            chans = {k.rpartition(".")[2]: v.pixels for k, v in part.channels.items()}
            name = next(iter(part.channels)).rpartition(".")[0].rpartition(".")[2]
            keys = [c for c in "RGB" if c in chans] or [c for c in "XYZ" if c in chans] or list(chans)
            out[name] = np.stack([chans[c] for c in keys], -1).astype(np.float64)
    return out


def trilight(s, n, srgb):
    """Unity's trilight ambient by normal (Unity frame, y up): sRGB numbers for Gamma, linear for Linear."""
    sky_, eq, gr = (s["amb_sky"], s["amb_eq"], s["amb_ground"])
    if not srgb: sky_, eq, gr = lin(sky_), lin(eq), lin(gr)
    y = n[..., 1:2]
    return np.where(y > 0, eq + (sky_ - eq) * y, eq + (gr - eq) * (-y))


def pools(s, pos):
    """The floodlights' pools at night: share of each pool's middle at each ground point, from LightPool's falloff."""
    p = np.zeros(pos.shape[:-1])
    for k in range(-2, 40):
        z = k * 45.0
        x = 3.0 if k % 2 else -3.0
        d = np.sqrt((pos[..., 0] - x) ** 2 + (pos[..., 2] - z) ** 2) / 26.0
        p = np.maximum(p, np.clip(1 - d * d, 0, 1) ** 2)
    return p * (pos[..., 1] < 0.2)


def main():
    os.makedirs(out_dir, exist_ok=True)
    s = skyclock.at(hour)
    sc = scene()
    night = s["night"]
    lamps = []
    if night > 0.1:
        glow = material("lens", (1, 0.95, 0.86), 0.3, emit=np.array([1, 0.95, 0.86]) * 2.0)
        for k in range(-2, 40):
            z = k * 45.0
            side = 1 if k % 2 else -1
            box("pole", (side * 15, 9, z), (0.4, 18, 0.4), material("pole", (0.4, 0.4, 0.42), 0.5))
            box("lens", (side * 14.4, 18, z), (0.9, 0.9, 0.9), glow)
    world(s)
    lamp(s)
    if shot == "car":
        dirs = camera((3.2, 1.0, 17.5), (-2.3, 0.75, 23.2), 34)
    else:
        dirs = camera((8.0, 1.7, -6.0), (-1.0, 0.6, 60.0), 50)
    vl = sc.view_layers[0]
    for attr in ("use_pass_diffuse_color", "use_pass_diffuse_direct", "use_pass_glossy_color", "use_pass_glossy_direct",
                 "use_pass_glossy_indirect", "use_pass_normal", "use_pass_z", "use_pass_emit", "use_pass_position"):
        setattr(vl, attr, True)
    sc.render.image_settings.media_type = "MULTI_LAYER_IMAGE"
    sc.render.image_settings.file_format = "OPEN_EXR_MULTILAYER"
    sc.render.image_settings.color_depth = "32"
    tag = f"race-{shot}-{hour:g}"
    path = os.path.join(out_dir, tag + ".exr")
    sc.render.filepath = path
    if not (os.environ.get("REUSE") and os.path.exists(path)):
        bpy.ops.render.render(write_still=True)
    L = read_layers(path)
    dcol, ddir = L["Diffuse Color"], L["Diffuse Direct"]
    gcol, gdir, gind = L["Glossy Color"], L["Glossy Direct"], L["Glossy Indirect"]
    # Cycles reflects the sun's whole disc in the clear coat, and its odd stray samples run to millions. The game's
    # sun is a point, its highlight widened on curved paint (CarPaint.hlsl) so it can't spike: cap both the same way.
    gdir = np.minimum(gdir, GLINT)
    gind = np.minimum(gind, GLINT)
    emit = L["Emit"] if "Emit" in L else np.zeros_like(dcol)
    n = L["Normal"][..., [0, 2, 1]]
    pos = L["Position"][..., [0, 2, 1]]
    depth = L["Depth"][..., 0] if L["Depth"].ndim == 3 else L["Depth"]
    open_sky = depth > 15000
    c = s["colour"]
    i_g = s["light"]
    i_l = skyclock.match(c, i_g, s["amb_sky"], max(s["light_dir"][1], 0.3))
    print(f"{tag}: sun or moon {i_g:.3f} in Gamma, {i_l:.3f} in Linear")
    sky_g = skyclock.sky(s, dirs)
    f = np.exp(-s["density"] * np.minimum(depth, 15000))[..., None]
    p = pools(s, pos)[..., None] * (night > 0.1)
    on = skyclock.sm(0, 0.12, night)
    tint = np.array([1, 0.95, 0.86])

    # Gamma: shading on sRGB numbers, decoded by the post stack.
    v = enc(dcol) * (ddir * i_g * c + trilight(s, n, True)) + enc(gcol) * (gdir * i_g * c + enc(gind)) + enc(emit)
    bright = tint * ((1 + 7 * on) ** (1 / 2.2) - 1)
    v = v * (1 + bright * p) + tint * (0.035 * on) ** (1 / 2.2) * p
    v = s["fog"] + (v - s["fog"]) * f
    v = np.where(open_sky[..., None], sky_g, v)
    r_g = lin(v)

    # Linear.
    r = dcol * (ddir * i_l * lin(c) + trilight(s, n, False)) + gcol * (gdir * i_l * lin(c) + gind) + emit
    r = r * (1 + lin(tint) * 7 * on * p) + lin(tint) * 0.035 * on * p
    fog = lin(s["fog"])
    r = fog + (r - fog) * f
    r_l = np.where(open_sky[..., None], lin(sky_g), r)

    g = skyclock.grade(s)
    for name, rad in (("gamma", r_g), ("linear", r_l)):
        np.save(os.path.join(out_dir, f"{tag}-{name}.npy"), rad.astype(np.float32))
        save(os.path.join(out_dir, f"{tag}-{name}.png"), frame(rad, bloom=bloom(rad, g["exposure"]), **g))
    print("rendered", tag)


main()
