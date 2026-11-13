"""Renders the headlamp beams and the floodlights' beams in the mist at night, as the race draws them before and after
the whiteout fix, at each quality level.

Cycles renders the scene as the race's opaque pass leaves it: the road, the grass, the cars, the masts and the pit
garage under the night sky and the garage's own lights. Then the rest is done here the way the game's shaders do it:
the floodlights' pools (LightPool.shader), the headlamp beam (before: a flat card in the car's frame that multiplies
what is behind it, LightPool.shader; after: cast on the depth like a decal, HeadlampBeam.shader), the floodlights'
beams in the mist (Volumetrics.shader, one frame with no history, as when the camera moves fast) and the post stack.

Usage: python beams.py out_dir shot hour       shot is "crest", "garage" or "straight"; REUSE=1 keeps an earlier render
Run with a Python that has bpy (Blender as a module), numpy, scipy, pillow and OpenEXR.
"""
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "tools", "linear-preview"))
import bpy  # noqa: E402
from mathutils import Vector, Euler  # noqa: E402

import sky as skyclock  # noqa: E402
from post import lin, frame, bloom, save, LUMA  # noqa: E402

out_dir, shot, hour = sys.argv[1], sys.argv[2], float(sys.argv[3])
W, H = int(os.environ.get("W", "1280")), int(os.environ.get("H", "720"))

# NightLook and CarLamps.
FLOOD = np.array([1.0, 0.95, 0.86])
HEAD = np.array([0.9, 0.95, 1.0])
POOL_BRIGHTENING, POOL_LIFT = 7.0, 0.035
# The beam's brightening was 5 on the card, multiplying the pools too; it is 10 cast as a decal, before the pools.
BEAM_BRIGHTENING, BEAM_LIFT, BEAM_CAP = 5.0, 0.03, 0.4
BEAM_ADD = 10.0
BEAM_LENGTH, BEAM_NEAR, BEAM_FAR = 24.0, 1.8, 9.0
BEAM_BELOW, BEAM_ABOVE = 3.0, 0.6
# NightLights and VolumetricsFeature.
BEAM_STRENGTH = 30000.0 * float(lin(0.2)) / 0.2
FOG_REACH = 700.0
MIST_DENSITY, MIST_HEIGHT, GROUND = 0.0004 + 0.0008, 40.0, 0.0
# Quality levels: render scale, bloom, the mist's march divisor and steps (none below High).
LEVELS = {"low": (0.67, False, 0, 0), "medium": (0.8, True, 0, 0), "high": (1.0, True, 3, 16), "ultra": (1.0, True, 2, 24)}


def b(v):
    return (v[0], v[2], v[1])


def material(name, colour, rough, metal=0.0, emit=None, strength=1.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*lin(colour), 1)
    p.inputs["Roughness"].default_value = rough
    p.inputs["Metallic"].default_value = metal
    if emit is not None:
        p.inputs["Emission Color"].default_value = (*lin(emit), 1)
        p.inputs["Emission Strength"].default_value = strength
    return m


def box(centre, size, mat, rot=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=b(centre))
    ob = bpy.context.object
    ob.scale = b(size)
    if rot is not None: ob.rotation_euler = rot
    ob.data.materials.append(mat)
    return ob


def strip(name, xs, zs, height, mat):
    """A ground strip across xs and along zs, its height from height(x, z)."""
    verts, faces = [], []
    for j, z in enumerate(zs):
        for i, x in enumerate(xs):
            verts.append(b((x, height(x, z), z)))
    n = len(xs)
    for j in range(len(zs) - 1):
        for i in range(n - 1):
            a = j * n + i
            faces.append((a, a + 1, a + n + 1, a + n))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    me.materials.append(mat)
    return ob


# ---------------------------------------------------------------- the shots

def crest_height(x, z):
    """A steep climb to a crest 12 m ahead of the car, then a drop of 12 m that levels out 120 m on."""
    if z < -40: return -4.8
    if z < 0: return 0.12 * z
    if z < 30: return 0.12 * z - 0.005 * z * z
    if z < 60: return -0.9 - 0.18 * (z - 30)
    if z < 120: return -6.3 - 0.18 * (z - 60) + 0.0015 * (z - 60) ** 2
    return -11.7


def flat(x, z):
    return 0.0


SHOTS = {
    # ground height, car pitch (degrees, nose up), masts along the track, garage, the car raised (m: up on its jacks)
    "crest": (crest_height, math.degrees(math.atan(0.12)) + 3.0, True, False, 0.0),
    "straight": (flat, 0.0, True, False, 0.0),
    "garage": (flat, 0.0, False, True, 0.0),
}
LIFT = SHOTS[shot][4]
# Other cars, each by its nose (x, z) and heading (degrees from ahead): their beams are drawn too.
OTHERS = {"crest": [(1.0, -7.0, 0.0), (-1.2, -14.0, 0.0)], "straight": [(2.0, -9.0, 0.0)],
          "garage": [(-4.0, 4.5, 90.0), (-12.0, 4.5, 90.0), (-6.0, -6.0, 90.0)]}


def scene(s):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = int(os.environ.get("SAMPLES", "16"))
    sc.cycles.use_denoising = False
    sc.cycles.max_bounces = 2
    sc.cycles.diffuse_bounces = 1
    sc.cycles.glossy_bounces = 1
    sc.render.resolution_x, sc.render.resolution_y = W, H
    height, pitch, masts, garage, _ = SHOTS[shot]
    grass = material("grass", (0.25, 0.34, 0.18), 0.95)
    asphalt = material("asphalt", (0.17, 0.17, 0.18), 0.75)
    line = material("line", (0.93, 0.93, 0.91), 0.6)
    concrete = material("concrete", (0.55, 0.55, 0.53), 0.8)
    xs = np.linspace(-6, 6, 13)
    zs = np.concatenate([np.linspace(-60, 120, 361), np.linspace(125, 1500, 60)])
    strip("track", xs, zs, height, asphalt)
    for x in (-5.6, 5.6):
        strip("edge", np.array([x - 0.06, x + 0.06]), zs, lambda X, Z: height(X, Z) + 0.006, line)
    gx = np.concatenate([np.linspace(-400, -6, 12), np.linspace(6, 400, 12)])
    for side in (gx[:12], gx[12:]):
        strip("grass", side, zs, lambda X, Z: height(X, Z) - 0.02, grass)
    if masts:
        steel = material("steel", (0.55, 0.57, 0.58), 0.4, 0.5)
        lens = material("lens", FLOOD, 0.3, emit=FLOOD, strength=2.0)
        for k in range(-2, 34):
            z = k * 45.0
            side = 1 if k % 2 else -1
            y0 = height(side * 15, z)
            box((side * 15, y0 + 9, z), (0.4, 18, 0.4), steel)
            box((side * 14.4, y0 + 18, z), (0.9, 0.9, 0.9), lens)
    # The cars ahead, with their tail lamps.
    tail = material("tail", (0.32, 0.03, 0.03), 0.4, emit=(1, 0.06, 0.03), strength=0.9)
    for i, (x, z, colour) in enumerate(((-2.2, 17.0, (0.85, 0.7, 0.1)), (2.4, 31.0, (0.1, 0.55, 0.15)))):
        if garage: break
        y0 = height(x, z)
        paint = material(f"paint{i}", colour, 0.25)
        box((x, y0 + 0.55, z), (1.9, 0.8, 4.4), paint)
        box((x, y0 + 1.15, z - 0.3), (1.5, 0.45, 2.2), paint)
        for sx in (-0.6, 0.6): box((x + sx, y0 + 0.75, z - 2.21), (0.4, 0.12, 0.02), tail)
    if garage:
        # The pit lane, the garage in front of the car, lit from its ceiling, and the pit wall behind.
        box((0, -0.01, 12), (40, 0.02, 60), concrete)
        floor = material("garage floor", (0.42, 0.42, 0.44), 0.5)
        wall = material("garage wall", (0.78, 0.76, 0.72), 0.85)
        box((0, 0.0, 13), (9, 0.02, 14), floor)
        box((0, 2.5, 20.1), (9, 5, 0.2), wall)
        for x in (-4.6, 4.6): box((x, 2.5, 13), (0.2, 5, 14), wall)
        box((0, 5.1, 13), (9.4, 0.2, 14.4), wall)
        for x in (-12.5, 12.5): box((x, 3.0, 6.1), (16, 6, 0.2), wall)
        box((0, 5.6, 6.1), (9, 1.2, 0.2), wall)
        strip_light = material("strip", (1, 1, 1), 0.5, emit=(1, 0.97, 0.9), strength=6.0)
        for z in (9.0, 13.0, 17.0):
            box((0, 4.98, z), (6, 0.05, 0.25), strip_light)
            l = bpy.data.lights.new(f"garage{z}", "AREA")
            l.shape = "RECTANGLE"
            l.size, l.size_y = 6.0, 0.3
            l.energy = 150.0
            l.color = tuple(lin((1, 0.97, 0.9)))
            o = bpy.data.objects.new(f"garage{z}", l)
            o.location = b((0, 4.9, z))
            bpy.context.scene.collection.objects.link(o)
        tools = material("tool cart", (0.7, 0.1, 0.08), 0.4)
        for x, z in ((-3.4, 18.5), (3.3, 18.6), (-3.6, 11)):
            box((x, 0.5, z), (1.0, 1.0, 0.6), tools)
        box((0, 0.6, -9), (40, 1.2, 0.4), concrete)
    other = material("other car", (0.2, 0.25, 0.6), 0.3)
    for origin, right, up, fwd in other_frames():
        c = origin + up * 0.6 - fwd * 2.2
        yaw = math.atan2(fwd[0], fwd[2])
        pitch_o = math.asin(max(min(fwd[1], 1), -1))
        box(c, (1.9, 0.9, 4.4), other, rot=(pitch_o, 0, -yaw))
    # The own car's bonnet, under the camera, nose at the beam's start.
    bonnet = material("bonnet", (0.55, 0.12, 0.05), 0.45)
    car = bpy.data.objects.new("car", None)
    sc.collection.objects.link(car)
    car.rotation_euler = (math.radians(pitch), 0, 0)
    car.location = (0, 0, LIFT)  # Blender x is Unity x: nose up is +x in Blender's right-handed frame
    ob = box((0, 0.55, -0.9), (1.8, 0.4, 1.8), bonnet)
    ob.parent = car
    world(s)
    moon(s)
    return pitch


def world(s):
    """The sky behind, and the night's ambient light on everything else."""
    w = bpy.data.worlds.new("sky")
    w.use_nodes = True
    nt = w.node_tree
    bg = nt.nodes["Background"]
    lp = nt.nodes.new("ShaderNodeLightPath")
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    nt.links.new(lp.outputs["Is Camera Ray"], mix.inputs[0])
    amb = skyclock.ambient(s["amb_sky"] * 0.5 + s["amb_eq"] * 0.5)
    sky_lin, cloud_lin = radiance_for(SKY_SEEN, s), radiance_for(CLOUD_SEEN, s)
    mix.inputs[6].default_value = (*amb, 1)
    # The sky the camera sees, with moonlit cumulus over it as in the race at night.
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 3.0
    noise.inputs["Detail"].default_value = 6.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.5
    ramp.color_ramp.elements[0].color = (*sky_lin, 1)
    ramp.color_ramp.elements[1].position = 0.7
    ramp.color_ramp.elements[1].color = (*cloud_lin, 1)
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], mix.inputs[7])
    nt.links.new(mix.outputs[2], bg.inputs["Color"])
    bpy.context.scene.world = w


# The race's sky and moonlit cloud at 19:03, as they come out on screen in Ivan's frames (sRGB).
SKY_SEEN, CLOUD_SEEN = (62, 78, 110), (165, 175, 195)


def radiance_for(seen, s):
    """The linear sky radiance that the post stack turns into this sRGB colour on screen."""
    want = lin(np.asarray(seen, dtype=np.float64) / 255.0)
    g = skyclock.grade(s)
    r = want.copy() * 0.3
    for _ in range(60):
        got = lin(frame(r[None, None, :], **g)[0, 0])
        r = r * (want / np.maximum(got, 1e-6)) ** 0.7
    return r


def moon(s):
    d = s["light_dir"]
    if d[1] <= 0: return
    l = bpy.data.lights.new("moon", "SUN")
    i = skyclock.match(s["colour"], s["light"], s["amb_sky"], max(d[1], 0.3))
    l.energy = math.pi * i
    l.color = tuple(lin(s["colour"]))
    o = bpy.data.objects.new("moon", l)
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector(b(-d)).to_track_quat("-Z", "Y")
    bpy.context.scene.collection.objects.link(o)


def car_frame(pitch, origin=None, yaw=0.0):
    """A car's frame in Unity's axes: its origin on the ground under the nose, its right, up and forward."""
    p, y = math.radians(pitch), math.radians(yaw)
    flat_fwd = np.array([math.sin(y), 0.0, math.cos(y)])
    fwd = flat_fwd * math.cos(p) + np.array([0, 1.0, 0]) * math.sin(p)
    up = np.array([0, 1.0, 0]) * math.cos(p) - flat_fwd * math.sin(p)
    right = np.cross(up, fwd)
    if origin is None: origin = np.array([0.0, LIFT, 0.0])
    return np.asarray(origin, dtype=np.float64), right, up, fwd


def other_frames():
    """The other cars whose beams reach into the shot: close behind on the climb, or passing along the pit lane."""
    height, pitch = SHOTS[shot][0], SHOTS[shot][1]
    out = []
    for x, z, yaw in OTHERS[shot]:
        out.append(car_frame(pitch if shot == "crest" else 0.0, (x, height(x, z), z), yaw))
    return out


def camera(pitch):
    origin, right, up, fwd = car_frame(pitch)
    if shot == "garage":
        # The low camera on the car's nose, as in Ivan's frame of the pit lane: no bonnet in view.
        pos = origin + up * 0.7 + fwd * 0.1
        target = pos + fwd * 10 - up * 0.4
    else:
        pos = origin + up * 1.25 - fwd * 2.4
        target = pos + fwd * 10 - up * 0.9
    cam = bpy.data.cameras.new("cam")
    cam.sensor_fit = "VERTICAL"
    fov = 46.0
    cam.angle_y = math.radians(fov)
    cam.clip_end = 20000
    co = bpy.data.objects.new("cam", cam)
    co.location = b(pos)
    co.rotation_mode = "QUATERNION"
    co.rotation_quaternion = (Vector(b(target)) - Vector(b(pos))).to_track_quat("-Z", "Y")
    bpy.context.scene.collection.objects.link(co)
    bpy.context.scene.camera = co
    q = co.rotation_quaternion
    ys, xs = np.mgrid[0:H, 0:W]
    t = math.tan(math.radians(fov) / 2)
    v = np.stack([(xs + 0.5 - W / 2) / (H / 2) * t, -(ys + 0.5 - H / 2) / (H / 2) * t, -np.ones((H, W))], -1)
    dirs = v @ np.array(q.to_matrix()).T
    dirs /= np.linalg.norm(dirs, axis=-1, keepdims=True)
    return pos, dirs[..., [0, 2, 1]]


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


# ---------------------------------------------------------------- the game's shaders, per pixel

def pools(pos, up_facing, height):
    """LightPool's falloff for the masts' pools, which lie on the track under each lamp's aim."""
    p = np.zeros(pos.shape[:-1])
    # The pit roof's pools along the lane, or the masts' along the track.
    centres = [(x, -1.0) for x in (-24.0, -8.0, 8.0, 24.0)] if shot == "garage" else \
        [(3.0 if k % 2 else -3.0, k * 45.0) for k in range(-2, 34)]
    for x, z in centres:
        d = np.sqrt((pos[..., 0] - x) ** 2 + (pos[..., 2] - z) ** 2) / 26.0
        p = np.maximum(p, np.clip(1 - d * d, 0, 1) ** 2)
    return p * up_facing


def beam_card(cam, dirs, depth_t, fr):
    """Before: the flat card 5 cm over the car's ground, drawn where it is in front of the scene. Returns its falloff."""
    origin, right, up, fwd = fr
    o = origin + up * 0.05
    denom = dirs @ up
    t = ((o - cam) @ up) / np.where(np.abs(denom) > 1e-6, denom, 1e-6)
    hit = cam + dirs * t[..., None]
    rel = hit - origin
    x, z = rel @ right, rel @ fwd
    tt = z / BEAM_LENGTH
    half = (BEAM_NEAR + (BEAM_FAR - BEAM_NEAR) * np.clip(tt, 0, 1)) * 0.5
    u = x / half
    v = np.clip(tt, 0, 1) ** 0.7 * 2 - 1
    f = np.clip(1 - u * u - v * v, 0, 1) ** 2
    seen = (t > 0) & (t <= depth_t * 1.0005) & (tt >= 0) & (tt <= 1)
    return np.where(seen, f, 0.0)


def normals_from_depth(pos, cam):
    """HeadlampBeam's normal: the nearer step to each neighbour across and down, facing the camera."""
    def step(back, ahead):
        a, bb = ahead - pos, pos - back
        return np.where(((a * a).sum(-1) < (bb * bb).sum(-1))[..., None], a, bb)
    p = np.pad(pos, ((1, 1), (1, 1), (0, 0)), mode="edge")
    dx = step(p[1:-1, :-2], p[1:-1, 2:])
    dy = step(p[2:, 1:-1], p[:-2, 1:-1])
    n = np.cross(dy, dx)
    n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-9)
    flip = ((cam - pos) * n).sum(-1) < 0
    return np.where(flip[..., None], -n, n)


def sm(a, b_, x):
    t = np.clip((x - a) / (b_ - a), 0, 1)
    return t * t * (3 - 2 * t)


def beam_decal(pos, sky, cam, fr):
    """After: HeadlampBeam's weight at each pixel: inside the beam, near the car's ground, facing up."""
    origin, right, up, fwd = fr
    rel = pos - origin
    x, y, z = rel @ right, rel @ up, rel @ fwd
    t = z / BEAM_LENGTH
    inside = (t > 0) & (t < 1) & ~sky
    tc = np.clip(t, 0, 1)
    half = (BEAM_NEAR + (BEAM_FAR - BEAM_NEAR) * tc) * 0.5
    u = x / half
    v = tc ** 0.7 * 2 - 1
    shape = np.clip(1 - u * u - v * v, 0, 1) ** 2
    band = sm(-BEAM_BELOW, -BEAM_BELOW * 0.5, y) * (1 - sm(BEAM_ABOVE * 0.5, BEAM_ABOVE, y))
    n = normals_from_depth(pos, cam)
    return np.where(inside, shape * band * sm(0.55, 0.8, n[..., 1]), 0.0)


def ign(h, w, frame_no=0):
    ys, xs = np.mgrid[0:h, 0:w] + 0.5
    xs = xs + 5.588238 * (frame_no & 63)
    return np.mod(52.9829189 * np.mod(0.06711056 * xs + 0.00583715 * ys, 1.0), 1.0)


def mast_beams(cam, height):
    """NightLights' beams: the lenses nearest the camera, their reach and cone as Floodlight works them out."""
    beams = []
    for k in range(-2, 34):
        z = k * 45.0
        side = 1 if k % 2 else -1
        lens = np.array([side * 14.4, height(side * 14.4, z) + 18.0, z])
        target = np.array([side * 3.0, height(side * 3.0, z), z])
        reach = np.linalg.norm(target - lens)
        cone = min(max(2 * math.degrees(math.atan(26.0 * 1.1 / reach)), 30), 120)
        if np.linalg.norm(lens - cam) < 400:
            beams.append((lens, (target - lens) / reach, reach * 1.6, math.cos(math.radians(cone / 2))))
    beams.sort(key=lambda bm: np.linalg.norm(bm[0] - cam))
    return beams[:16]


def beam_at(bm, p):
    lens, aim, reach, edge = bm
    v = p - lens
    dd = (v * v).sum(-1)
    c = (v / np.sqrt(dd + 1e-4)[..., None]) @ aim
    cone = sm(edge, edge + (1 - edge) * 0.4, c)
    return np.where(dd < reach * reach, cone * np.clip(1 - dd / (reach * reach), 0, 1) / (dd + 9.0), 0.0)


def rho_at(p):
    return MIST_DENSITY * np.exp(-np.maximum(p[..., 1] - GROUND, 0) / MIST_HEIGHT)


def mist_old(cam, d, reach, jitter, beams, steps):
    lit = np.zeros(d.shape[:-1])
    T = np.ones_like(lit)
    for i in range(steps):
        a, b_ = i / steps, (i + 1) / steps
        t0, t1 = reach * a * a, reach * b_ * b_
        t = t0 + (t1 - t0) * jitter
        p = cam + d * t[..., None]
        rho = rho_at(p)
        through = np.exp(-rho * (t1 - t0))
        light = sum(beam_at(bm, p) for bm in beams)
        lit += T * light * (1 - through)
        T *= through
    return lit


def mist_new(cam, d, reach, jitter, beams, samples=8):
    lit = np.zeros(d.shape[:-1])
    for bm in beams:
        lens, aim, r, edge = bm
        w0 = cam - lens
        bb = d @ w0
        c = w0 @ w0 - r * r
        disc = bb * bb - c
        root = np.sqrt(np.maximum(disc, 0))
        t0, t1 = np.maximum(-bb - root, 0), np.minimum(-bb + root, reach)
        da, e = d @ aim, aim @ w0
        side = np.maximum(1 - da * da, 1e-4)
        t_near = (da * e - bb) / side
        s_near = np.maximum((e - da * bb) / side, 0)
        wide = s_near * math.sqrt(max(1 - edge * edge, 0)) / max(edge, 0.05) + 2.0
        stay = wide / np.maximum(np.sqrt(side), 0.05)
        ta, tb = np.maximum(t0, t_near - stay), np.minimum(t1, t_near + stay)
        ok = (disc > 0) & (tb > ta)
        dt = np.where(ok, (tb - ta) / samples, 0)
        for i in range(samples):
            t = ta + dt * (i + jitter)
            p = cam + d * t[..., None]
            rho = rho_at(p)
            lit += np.where(ok, beam_at(bm, p) * rho * np.exp(-rho * t) * dt, 0)
    return lit


def resample(img, scale):
    """Draws at a lower render scale and scales back up, as the upscaler does without its history."""
    from scipy.ndimage import zoom
    if scale >= 0.999: return img
    small = zoom(img, (scale, scale, 1), order=1)
    return zoom(small, (img.shape[0] / small.shape[0], img.shape[1] / small.shape[1], 1), order=1)


def main():
    os.makedirs(out_dir, exist_ok=True)
    s = skyclock.at(hour)
    pitch = scene(s)
    cam, dirs = camera(pitch)
    vl = bpy.context.scene.view_layers[0]
    for attr in ("use_pass_normal", "use_pass_z", "use_pass_position"):
        setattr(vl, attr, True)
    sc = bpy.context.scene
    sc.render.image_settings.media_type = "MULTI_LAYER_IMAGE"
    sc.render.image_settings.file_format = "OPEN_EXR_MULTILAYER"
    sc.render.image_settings.color_depth = "32"
    tag = f"{shot}-{hour:g}"
    path = os.path.join(out_dir, tag + ".exr")
    sc.render.filepath = path
    if not (os.environ.get("REUSE") and os.path.exists(path)):
        bpy.ops.render.render(write_still=True)
    L = read_layers(path)
    opaque = L["Combined"][..., :3]
    pos = L["Position"][..., [0, 2, 1]]
    nrm = L["Normal"][..., [0, 2, 1]]
    sky = np.linalg.norm(pos - cam, axis=-1) > 15000
    sky |= np.linalg.norm(pos, axis=-1) < 1e-6
    dist = np.where(sky, 1e7, np.linalg.norm(pos - cam, axis=-1))
    on = skyclock.sm(0, 0.12, s["night"])
    ft, ht = lin(FLOOD), lin(HEAD)

    # The masts' pools, over the opaque scene.
    height = SHOTS[shot][0]
    up_facing = (nrm[..., 1] > 0.7) & ~sky & (pos[..., 1] - np.vectorize(height)(pos[..., 0], pos[..., 2]) < 0.2)
    p = pools(pos, up_facing, height)[..., None]

    frames = [car_frame(pitch)] + other_frames()
    cards = [beam_card(cam, dirs, dist, fr)[..., None] for fr in frames]
    add = np.zeros_like(opaque)
    for fr in frames:
        w = beam_decal(pos, sky, cam, fr)[..., None]
        a = opaque * ht * BEAM_ADD * on * w
        lum = (a @ LUMA)[..., None]
        add += a / (1 + lum / max(BEAM_CAP * on, 1e-4)) + ht * BEAM_LIFT * on * w

    beams = mast_beams(cam, height) if SHOTS[shot][2] else []
    g = skyclock.grade(s)
    for level, (scale, has_bloom, div, steps) in LEVELS.items():
        for name in ("before", "after"):
            # The volumetrics are composited after the skybox, before the transparent pass.
            mist = np.zeros_like(opaque)
            if div and beams and on > 0.001:
                h, w_ = H // div, W // div
                d = dirs[div // 2::div, div // 2::div][:h, :w_]
                reach = np.minimum(dist[div // 2::div, div // 2::div][:h, :w_], FOG_REACH)
                jit = ign(h, w_, 7)
                lit = mist_old(cam, d, reach, jit, beams, steps) if name == "before" else mist_new(cam, d, reach, jit, beams)
                light = ft * on * BEAM_STRENGTH / (4 * math.pi)
                m = np.repeat(np.repeat(lit, div, 0), div, 1)[:H, :W, None] * light
                mist = np.pad(m, ((0, H - m.shape[0]), (0, W - m.shape[1]), (0, 0)))
            if name == "before":
                # The pools and the card in the transparent pass, multiplying all that is drawn behind them.
                behind = opaque + mist
                behind = behind * (1 + ft * POOL_BRIGHTENING * on * p) + ft * POOL_LIFT * on * p
                rad = behind
                for f_old in cards:
                    rad = rad * (1 + ht * BEAM_BRIGHTENING * on * f_old) + ht * BEAM_LIFT * on * f_old
            else:
                # The decal reads the opaque copy, taken before the volumetrics.
                rad = (opaque + mist) * (1 + ft * POOL_BRIGHTENING * on * p) + ft * POOL_LIFT * on * p + add
            r = resample(rad, scale)
            bl = bloom(r, g["exposure"]) if has_bloom else None
            grade = g if has_bloom else dict(exposure=g["exposure"])
            save(os.path.join(out_dir, f"{tag}-{level}-{name}.png"), frame(r, bloom=bl, **grade))
    print("rendered", tag, "night", round(s["night"], 3), "lights on", round(on, 3))


main()
