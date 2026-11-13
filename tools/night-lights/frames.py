"""Renders the circuit's night lights on the real pit straight, before and after the bounded light model, at Medium and
Ultra, for Ivan's frames: the overcast dusk onboard (17:41) and from the TV camera (17:47, and the same view at 21:00),
the field under the floodlights (19:06), and two frames away from them with only the headlamps (20:43, 20:42).

The floodlights, the circuit's middle line and the pit lane come from the game's own code, dumped by the night check
(FIELD_DUMP=night.json bash tools/night-lights/run.sh), with the floodlights' map the game builds (FloodField). Cycles
renders the opaque pass's sky and moon light on a stand-in of the straight: the road and its lines, the grass, the pit
lane, the pit building, the masts and their lenses, and the cars. The rest is done here as the shaders do it:
  before  the pools multiply what is drawn under them (LightPool.shader), the headlamps light the ground only, with no
          cap (Headlamps.hlsl as it was), the beams in the mist have no cap, the bloom no clamp, and the floodlights
          follow the lights' switch alone
  after   the floodlights' map and the headlamps are added in every surface's own colour and capped together
          (NightLit.hlsl), the beams in the mist are capped, the bloom is clamped, and the floodlights follow the sky's
          real darkness (NightLook.FloodDark)

Usage: python frames.py night.json out_dir frame    frame: dusk-onboard, dusk-tv, tv-21, field, dark, pack
       Run with a Python that has bpy (Blender as a module), numpy, scipy, pillow and OpenEXR. REUSE=1 keeps renders.
"""
import json
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "tools", "linear-preview"))
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import sky as skyclock  # noqa: E402
from post import lin, frame, save, LUMA  # noqa: E402

dump_path, out_dir, frame_name = sys.argv[1], sys.argv[2], sys.argv[3]
W, H = int(os.environ.get("W", "1280")), int(os.environ.get("H", "720"))
D = json.load(open(dump_path))
C, LEFT, HALF = np.array(D["centre"]), np.array(D["left"]), np.array(D["half"])
LANE = np.array(D["lane"])
N = len(C)
LAMPS = D["lamps"]
F = D["field"]
FW, FH = F["Width"], F["Height"]
FIELD = {k: np.array(F[k], dtype=np.float64).reshape(FH, FW) for k in ("light", "ground", "towardX", "towardZ")}

# NightLook, NightLights, Headlamps.hlsl and NightLit.hlsl.
FLOOD, HEAD = np.array([1.0, 0.95, 0.86]), np.array([0.9, 0.95, 1.0])
POOL_BRIGHTENING, POOL_LIFT = 7.0, 0.035
HEADLAMP_GROUND, HEADLAMP_REACH, UNDER_FLOODS, SATURATE = 5.0, 75.0, 0.01, 1.466
FLOOD_GROUND, NIGHT_MOST, BEAM_MOST = 1.5, 0.5, 0.06
FACE, NEAREST, MOST_CARS = 0.0428, 10.6, 1.3
FLOOD_FACING, FLOOD_WRAP, FLOOD_SLANT, HIGH_FROM, HIGH_TO = 2.0, 0.2, 0.3, 8.0, 25.0
BEAM_STRENGTH, BEAM_CARRY = 30000.0 * float(lin(0.2)) / 0.2, 1.6
LENS_GLOW = 2.0
BLOOM_CLAMP = 8.0
FOG_REACH = 700.0
NOSE_Y, FRONT_Z = 0.65, 2.3
# Quality levels: render scale, bloom, the mist's march divisor (none below High).
LEVELS = {"medium": (0.8, True, 0), "ultra": (1.0, True, 2)}

# The pit straight: the track sample nearest the middle of the pit lane.
K0 = int(np.argmin(((C[:, [0, 2]] - LANE[len(LANE) // 2][[0, 2]]) ** 2).sum(1)))
# A stretch far from every floodlight, for the frames with none.
LAMP_XZ = np.array([l["position"] for l in LAMPS])[:, [0, 2]]
FAR = int(np.argmax([np.min(((LAMP_XZ - C[k, [0, 2]]) ** 2).sum(1)) for k in range(N)]))


def wrap(k): return int(k) % N


def fwd_at(k):
    f = C[wrap(k + 1)] - C[wrap(k - 1)]
    return f / np.linalg.norm(f)


def on_track(k, lat, up=0.0):
    k = wrap(k)
    return C[k] + LEFT[k] * lat + np.array([0, up, 0])


# Each frame: hour, the weather's dusk for the lights (9 degrees under an overcast sky), the camera (track sample, across,
# height, the sample and across it looks at, its height there, field of view), and the cars (samples on from the camera's,
# across), the first our own when the camera rides it.
FRAMES = {
    "dusk-onboard": (17 + 41 / 60, 9.0, (K0 - 60, 2.0, 1.1, K0 - 50, 2.0, 0.6, 50.0), [(0, 2.0), (12, -2.5), (30, 1.5)], True),
    "dusk-tv": (17 + 47 / 60, 9.0, (K0 - 170, -40.0, 30.0, K0 - 60, 6.0, 0.0, 40.0), [(-130, 2.5), (-90, -2.0), (-40, 1.0), (10, 30.0)], False),
    "tv-21": (21.0, 9.0, (K0 - 170, -40.0, 30.0, K0 - 60, 6.0, 0.0, 40.0), [(-130, 2.5), (-90, -2.0), (-40, 1.0), (10, 30.0)], False),
    "field": (19.1, 0.0, (K0 - 60, 0.0, 1.15, K0 - 50, 0.0, 0.5, 46.0),
              [(0, 0.0)] + [(dk, lat) for dk, lat in [(5, -2.2), (12, 1.8), (17, -3.0), (24, 2.4), (30, -1.0), (36, 3.2), (43, -2.6),
                                                     (51, 0.8), (59, -3.4), (69, 2.2), (79, -1.4), (-5, 2.6), (-11, 3.0), (-17, -2.8)]], True),
    "dark": (20.72, 0.0, (FAR, 0.0, 1.05, FAR + 10, 0.0, 0.45, 46.0), [(0, 0.0), (55, -1.8), (70, 1.5)], True),
    "pack": (20.7, 0.0, (FAR, 0.0, 1.0, FAR + 6, 0.0, 0.55, 46.0), [(0, 0.0), (6, -2.3), (14, 0.6), (18, 1.8)], True),
}
hour, dusk, view, CARS, ONBOARD = FRAMES[frame_name]
CAM_K = view[0]


# ---------------------------------------------------------------- the scene

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


def b(v):
    return (float(v[0]), float(v[2]), float(v[1]))


def mesh(name, verts, faces, mat, index=0):
    me = bpy.data.meshes.new(name)
    me.from_pydata([b(v) for v in verts], [], faces)
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    ob.pass_index = index
    bpy.context.scene.collection.objects.link(ob)
    return ob


def ribbon(name, ks, lat0, lat1, up, mat, index=0):
    verts, faces = [], []
    for i, k in enumerate(ks):
        verts += [on_track(k, lat0, up), on_track(k, lat1, up)]
        if i: faces.append((2 * i - 2, 2 * i - 1, 2 * i + 1, 2 * i))
    return mesh(name, verts, faces, mat, index)


def box(name, centre, x, y, z, size, mat, index=0):
    """A box with axes x, y, z (unit vectors) and size along each."""
    hx, hy, hz = np.asarray(x) * size[0] / 2, np.asarray(y) * size[1] / 2, np.asarray(z) * size[2] / 2
    c = np.asarray(centre)
    verts = [c + sx * hx + sy * hy + sz * hz for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    return mesh(name, verts, faces, mat, index)


def car(name, k, lat, colour, own):
    f = fwd_at(k)
    up = np.array([0.0, 1.0, 0.0])
    r = np.cross(up, f)
    base = on_track(k, lat)
    paint = material(name, colour, 0.35)
    if own:
        box(name, base + up * 0.33 + f * 1.3, r, up, f, (1.9, 0.62, 2.0), paint, 2)
        return
    box(name, base + up * 0.5, r, up, f, (1.9, 0.75, 4.4), paint, 2)
    box(name + " top", base + up * 1.1 - f * 0.3, r, up, f, (1.5, 0.45, 2.2), paint, 2)
    tail = material(name + " tail", (0.32, 0.03, 0.03), 0.4, emit=(1, 0.06, 0.03), strength=0.9)
    for sx in (-0.6, 0.6): box(name + " tail", base + up * 0.72 + r * sx - f * 2.21, r, up, f, (0.4, 0.12, 0.02), tail, 2)


def scene(s, lights):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = int(os.environ.get("SAMPLES", "12"))
    sc.cycles.use_denoising = False
    sc.cycles.max_bounces = 2
    sc.cycles.diffuse_bounces = 1
    sc.cycles.glossy_bounces = 1
    sc.render.resolution_x, sc.render.resolution_y = W, H
    ks = list(range(CAM_K - 120, CAM_K + 300, 2))
    grass = material("grass", (0.25, 0.34, 0.18), 0.95)
    asphalt = material("asphalt", (0.17, 0.17, 0.18), 0.75)
    line = material("line", (0.93, 0.93, 0.91), 0.6)
    concrete = material("concrete", (0.42, 0.42, 0.4), 0.85)
    building = material("pit building", (0.55, 0.56, 0.57), 0.7)
    steel = material("steel", (0.55, 0.57, 0.58), 0.4, 0.5)
    lens = material("lens", (0.55, 0.57, 0.56), 0.9, emit=FLOOD, strength=LENS_GLOW * skyclock.sm(0, 0.12, lights))
    ribbon("grass", ks, -160.0, 160.0, -0.03, grass)
    half = float(HALF[CAM_K])
    ribbon("road", ks, -half, half, 0.0, asphalt)
    for side in (-1, 1):
        ribbon("edge line", ks, side * (half - 0.35), side * (half - 0.15), 0.01, line)
    # The pit lane and the building behind it, where the lane runs beside the straight.
    lane_lat = [float((LANE[i] - C[np.argmin(((C[:, [0, 2]] - LANE[i][[0, 2]]) ** 2).sum(1))]) @ LEFT[np.argmin(((C[:, [0, 2]] - LANE[i][[0, 2]]) ** 2).sum(1))]) for i in range(0, len(LANE), 10)]
    near_lane = [k for k in ks if np.min(((LANE[:, [0, 2]] - C[wrap(k), [0, 2]]) ** 2).sum(1)) < 45 ** 2]
    if near_lane:
        lat = float(np.median(lane_lat))
        lw = D["laneWidth"]
        ribbon("pit lane", near_lane, lat - lw / 2, lat + lw / 2, 0.02, concrete)
        side = 1 if lat > 0 else -1
        k0, k1 = near_lane[0], near_lane[-1]
        mid = wrap((k0 + k1) // 2)
        f = fwd_at(mid)
        up = np.array([0.0, 1.0, 0.0])
        length = float(np.linalg.norm(C[wrap(k1)] - C[wrap(k0)]))
        box("pit building", on_track(mid, lat + side * (lw / 2 + 8.0), 6.0), np.cross(up, f), up, f, (14.0, 12.0, length), building, 1)
    # The floodlights near: masts under the pit wall's lamps, a lens for each.
    for i, l in enumerate(LAMPS):
        p = np.array(l["position"])
        if np.linalg.norm(p - C[wrap(CAM_K)]) > 700: continue
        aim = np.array(l["aim"])
        x = np.cross([0, 1, 0], aim)
        x /= np.linalg.norm(x)
        y = np.cross(aim, x)
        box(f"lamp {i}", p - aim * 0.2, x, y, aim, (1.1, 0.75, 0.36), steel, 1)
        box(f"lens {i}", p + aim * 0.0, x, y, aim, (1.0, 0.66, 0.03), lens, 1)
        if l["kind"] == "PitWall":
            g = float(sample(FIELD["ground"], p[0], p[2]) if sample(FIELD["ground"], p[0], p[2]) else p[1] - 15)
            box(f"mast {i}", np.array([p[0], (p[1] + g) / 2, p[2]]) - np.cross(aim, x) * 0.0, x, [0, 1, 0], np.cross(x, [0, 1, 0]), (0.4, p[1] - g, 0.4), steel, 1)
    for i, (dk, lat) in enumerate(CARS):
        colours = [(0.85, 0.72, 0.12), (0.2, 0.6, 0.2), (0.7, 0.1, 0.1), (0.85, 0.85, 0.85), (0.2, 0.35, 0.8),
                   (0.85, 0.2, 0.75), (0.95, 0.5, 0.1), (0.1, 0.1, 0.12)]
        car(f"car {i}", CAM_K + dk, lat, colours[i % len(colours)], own=ONBOARD and i == 0)
    world(s)
    moon(s)


def world(s):
    """The night's or dusk's ambient light on everything; the sky behind is drawn in the composite."""
    w = bpy.data.worlds.new("sky")
    w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    amb = skyclock.ambient(s["amb_sky"] * 0.5 + s["amb_eq"] * 0.5) * seen(s)
    bg.inputs["Color"].default_value = (*amb, 1)
    bpy.context.scene.world = w


def seen(s):
    """SkyClock.SkySeen: the sky dims towards a tenth of its colours as night falls."""
    return 1.0 + (0.1 - 1.0) * s["night"]


def moon(s):
    d = s["light_dir"]
    if d[1] <= 0 or s["light"] <= 0: return
    l = bpy.data.lights.new("light", "SUN")
    i = skyclock.match(s["colour"], s["light"], s["amb_sky"], max(d[1], 0.3))
    l.energy = math.pi * i
    l.color = tuple(lin(s["colour"]))
    o = bpy.data.objects.new("light", l)
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector(b(-d)).to_track_quat("-Z", "Y")
    bpy.context.scene.collection.objects.link(o)


def camera():
    k, lat, h, tk, tlat, th, fov = view
    pos = on_track(k, lat, h)
    target = on_track(tk, tlat, th)
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
            for k, v in part.channels.items():
                layer, _, ch = k.rpartition(".")
                layer = layer.rpartition(".")[2]
                out.setdefault(layer, {})[ch] = v.pixels
    res = {}
    for layer, chans in out.items():
        keys = [c for c in "RGB" if c in chans] or [c for c in "XYZ" if c in chans] or list(chans)
        res[layer] = np.stack([chans[c] for c in keys], -1).astype(np.float64)
    return res


# ---------------------------------------------------------------- the game's lights, per pixel

def sm(a, b_, x):
    t = np.clip((x - a) / (b_ - a), 0, 1)
    return t * t * (3 - 2 * t)


def sample(m, x, z):
    gi, gj = (np.asarray(x) - F["X0"]) / F["texel"], (np.asarray(z) - F["Z0"]) / F["texel"]
    inside = (gi >= 0) & (gj >= 0) & (gi <= FW - 1) & (gj <= FH - 1)
    i = np.clip(gi.astype(int) if np.ndim(gi) else int(gi), 0, FW - 2)
    j = np.clip(gj.astype(int) if np.ndim(gj) else int(gj), 0, FH - 2)
    fi, fj = np.clip(gi - i, 0, 1), np.clip(gj - j, 0, 1)
    v = (m[j, i] * (1 - fi) + m[j, i + 1] * fi) * (1 - fj) + (m[j + 1, i] * (1 - fi) + m[j + 1, i + 1] * fi) * fj
    return np.where(inside, v, 0.0)


def flood_light(pos, nrm):
    """NightLit.hlsl's FloodLight: per unit of colour, before the floodlights' level."""
    x, z = pos[..., 0], pos[..., 2]
    light, ground = sample(FIELD["light"], x, z), sample(FIELD["ground"], x, z)
    tx, tz = sample(FIELD["towardX"], x, z), sample(FIELD["towardZ"], x, z)
    ty = np.sqrt(np.clip(1 - tx * tx - tz * tz, 0, 1))
    facing = np.minimum(np.clip(nrm[..., 0] * tx + nrm[..., 1] * ty + nrm[..., 2] * tz, 0, None) / np.maximum(ty, FLOOD_SLANT), FLOOD_FACING)
    high = 1 - sm(HIGH_FROM, HIGH_TO, pos[..., 1] - ground)
    return np.clip(light, 0, 1) * high * (FLOOD_WRAP + (1 - FLOOD_WRAP) * facing)


def old_pools(pos):
    """LightPool's falloff summed over every pool: how far into the pools a point lies."""
    p = np.zeros(pos.shape[:-1])
    for l in LAMPS:
        t = np.array(l["target"])
        if np.linalg.norm(t - C[wrap(CAM_K)]) > 600: continue
        a = np.array(l["axis"])
        bb = np.array([-a[2], 0, a[0]])
        dx, dz = pos[..., 0] - t[0], pos[..., 2] - t[2]
        u = (dx * a[0] + dz * a[2]) / l["Long"]
        v = (dx * bb[0] + dz * bb[2]) / l["Wide"]
        f = np.clip(1 - (u * u + v * v), 0, 1)
        p += f * f
    return p


def floodlit(p):
    """HeadlampGround.Floodlit: how far a spot lies in a floodlight's pool."""
    most = 0.0
    for l in LAMPS:
        t = np.array(l["target"])
        d = p - t
        a = np.array(l["axis"])
        along = (d[0] * a[0] + d[2] * a[2]) / l["Long"]
        across = (d[0] * -a[2] + d[2] * a[0]) / l["Wide"]
        r2 = along * along + across * across
        if r2 >= 2.25: continue
        most = max(most, 1 - float(sm(0, 1, (r2 - 0.64) / (2.25 - 0.64))))
    return most


def headlamps(pos, nrm, lamps, new, ground_ok=None):
    """Headlamps.hlsl, before (the ground's light only, summed to a soft limit) and after (ground and face on)."""
    ground_t, face_t = np.zeros(pos.shape[:-1]), np.zeros(pos.shape[:-1])
    n_up = sm(0.5, 0.9, nrm[..., 1]) * (1.0 if ground_ok is None else ground_ok)
    for at, fwd, h, share in lamps:
        up = np.array([0.0, 1.0, 0.0]) - fwd * fwd[1]
        up /= np.linalg.norm(up)
        right = np.cross(up, fwd)
        v = pos - at
        z, x, y = v @ fwd, v @ right, v @ up
        d = np.maximum(np.linalg.norm(v, axis=-1), 1e-3)
        zc = np.maximum(z, 1e-3)
        ok = (z > 0.2) & (z < HEADLAMP_REACH)
        u = np.abs(x) / np.minimum(2.0 + 0.35 * zc, 6.0)
        side = np.exp(-1.2 * u * u)
        fade = 1 - sm(15.0, HEADLAMP_REACH, z)
        facing = np.clip(np.sum(nrm * -(v / d[..., None]), -1), 0, None)
        along = 0.5 + 0.5 * sm(0.5, 9.0, z)
        if new:
            cut = (1 - sm(0.05, 0.3, y / zc)) * sm(-h - 4.0, -h - 2.5, y)
            ground = along * np.minimum(facing / np.maximum(h / d, 0.01), 1.0)
            near = NEAREST ** 2 / np.maximum(d * d, NEAREST ** 2)
            g = n_up * (1 - sm(0.3, 0.8, v[..., 1] + h))
            base = np.where(ok, side * fade * cut * share, 0)
            ground_t += base * ground * g
            face_t += base * facing * near * (1 - g)
        else:
            cut = (1 - sm(0.0, 0.06, y / zc)) * sm(-h - 4.0, -h - 2.5, y)
            ground_t += np.where(ok, along * fade * side * cut * np.minimum(facing / np.maximum(h / d, 0.01), 1.0) * share, 0)
    sat = lambda s: (1 - np.exp(-SATURATE * s)) / (1 - math.exp(-SATURATE))
    return sat(ground_t) + (FACE * sat(face_t) if new else 0.0)


def knee(x, most):
    k = most / 2
    return np.minimum(x, k) + (most - k) * (1 - np.exp(-np.maximum(x - k, 0) / (most - k)))


def beams_near(cam):
    out = []
    for l in LAMPS:
        p = np.array(l["position"])
        if np.linalg.norm(p - cam) < 400:
            out.append((p, np.array(l["aim"]), l["Reach"] * BEAM_CARRY, math.cos(math.radians(l["Cone"] / 2))))
    out.sort(key=lambda bm: np.linalg.norm(bm[0] - cam))
    return out[:16]


def mist(cam, d, reach, jitter, beams, rho0, ground, samples=8):
    """Volumetrics.shader's BeamMist: the beams' light the mist scatters to the camera, before its colour and cap."""
    lit = np.zeros(d.shape[:-1])
    for lens, aim, r, edge in beams:
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
            rho = rho0 * np.exp(-np.maximum(p[..., 1] - ground, 0) / 40.0)
            v = p - lens
            dd = (v * v).sum(-1)
            cs = (v / np.sqrt(dd + 1e-4)[..., None]) @ aim
            cone = sm(edge, edge + (1 - edge) * 0.4, cs)
            at = np.where(dd < r * r, cone * np.clip(1 - dd / (r * r), 0, 1) / (dd + 9.0), 0.0)
            lit += np.where(ok, at * rho * np.exp(-rho * t) * dt, 0)
    return lit


def ign(h, w, frame_no=0):
    ys, xs = np.mgrid[0:h, 0:w]
    xs = xs + 5.588238 * frame_no
    ys = ys + 5.588238 * frame_no
    return np.mod(52.9829189 * np.mod(0.06711056 * xs + 0.00583715 * ys, 1.0), 1.0)


def bloom(radiance, exposure, clamp=None, threshold=1.2, intensity=0.5, scatter=0.65):
    """URP's bloom, roughly: the light over the threshold, spread wide; clamped first when the bloom has a clamp."""
    from scipy.ndimage import gaussian_filter
    x = np.maximum(radiance, 0)
    if clamp is not None: x = np.minimum(x, clamp)
    x = x * 2 ** exposure
    m = x.max(-1, keepdims=True)
    hot = x * np.maximum(m - threshold, 0) / np.maximum(m, 1e-6)
    h, w = x.shape[:2]
    out = np.zeros_like(x)
    for s_, k in ((0.004, 0.4), (0.012, 0.3), (0.03, 0.2), (0.07, 0.1 + 0.2 * scatter)):
        out += k * np.stack([gaussian_filter(hot[..., c], s_ * w) for c in range(3)], -1)
    return out * intensity


def resample(img, scale):
    from scipy.ndimage import zoom
    if scale >= 0.999: return img
    small = zoom(img, (scale, scale, 1), order=1)
    return zoom(small, (img.shape[0] / small.shape[0], img.shape[1] / small.shape[1], 1), order=1)


def main():
    os.makedirs(out_dir, exist_ok=True)
    s = skyclock.at(hour)
    e = s["e"]
    lights = 1 - float(sm(-12, -4, e - dusk))
    on = float(sm(0, 0.12, lights))
    floods_after = on * float(sm(3, 13, -e))
    heads_on = on * float(sm(4, 16, -e))
    scene(s, lights)
    cam, dirs = camera()
    sc = bpy.context.scene
    vl = sc.view_layers[0]
    for attr in ("use_pass_normal", "use_pass_z", "use_pass_position", "use_pass_diffuse_color", "use_pass_object_index"):
        setattr(vl, attr, True)
    sc.render.image_settings.media_type = "MULTI_LAYER_IMAGE"
    sc.render.image_settings.file_format = "OPEN_EXR_MULTILAYER"
    sc.render.image_settings.color_depth = "32"
    path = os.path.join(out_dir, f"{frame_name}.exr")
    sc.render.filepath = path
    if not (os.environ.get("REUSE") and os.path.exists(path)):
        bpy.ops.render.render(write_still=True)
    L = read_layers(path)
    opaque = L["Combined"][..., :3]
    pos = L["Position"][..., [0, 2, 1]]
    nrm = L["Normal"][..., [0, 2, 1]]
    albedo = L["Diffuse Color"][..., :3] if "Diffuse Color" in L else L["DiffCol"][..., :3]
    index = L["Object Index"][..., 0]
    sky = (np.linalg.norm(pos, axis=-1) < 1e-6)
    dist = np.where(sky, 1e7, np.linalg.norm(pos - cam, axis=-1))
    is_car = (index > 1.5) & ~sky
    ground_shader = ~sky & (index < 0.5)
    # The sky behind, as Sky.shader draws it, dimmed as night falls.
    sky_rad = lin(np.clip(skyclock.sky(s, dirs), 0, None)) * seen(s)
    opaque = np.where(sky[..., None], sky_rad, opaque)

    # Each car's lamps: its nose, heading, height, and the share it throws (less under the floodlights).
    lamps = []
    for dk, lat in CARS:
        k = CAM_K + dk
        f = fwd_at(k)
        at = on_track(k, lat, NOSE_Y) + f * FRONT_Z
        lamps.append((at, f, NOSE_Y, 1.0 + (UNDER_FLOODS - 1.0) * floodlit(at + f * 12)))
    ft, ht = lin(FLOOD), lin(HEAD)
    g = skyclock.grade(s)
    ground_y = float(C[:, 1].min())
    rho = 0.0004 + 0.003 * skyclock.misty(hour) + 0.0008 * lights
    beams = beams_near(cam)
    # The floodlights' real lights on the cars, the same both ways: shaped as the map lights the ground there, at most
    # what the night check finds they give a white car (0.26).
    car_floods = flood_light(pos, nrm)[..., None] * ft * 0.26 * is_car[..., None]
    rads = {}
    for which in ("before", "after"):
        if which == "before":
            head = headlamps(pos, nrm, lamps, False)[..., None] * ht * (HEADLAMP_GROUND * heads_on)
            lit = opaque + np.where(ground_shader[..., None], albedo * head, 0)
            pool = old_pools(pos)[..., None] * (ground_shader & (nrm[..., 1] > 0.7))[..., None]
            rad = lit * (1 + ft * POOL_BRIGHTENING * lights * pool) + ft * POOL_LIFT * lights * pool
            mist_level, mist_cap, clamp = on, None, None
            rad = rad + albedo * car_floods * on
        else:
            head = headlamps(pos, nrm, lamps, True, (~is_car).astype(float))[..., None] * ht * (HEADLAMP_GROUND * heads_on)
            fl = flood_light(pos, nrm)[..., None] * ft * (FLOOD_GROUND * floods_after) * (~is_car)[..., None]
            rad = opaque + np.where(sky[..., None], 0, knee(albedo * (head + fl), NIGHT_MOST))
            mist_level, mist_cap, clamp = floods_after, BEAM_MOST, BLOOM_CLAMP
            rad = rad + albedo * car_floods * floods_after
        rads[which] = (rad, mist_level, mist_cap, clamp)
    for level, (scale, has_bloom, div) in LEVELS.items():
        for which in ("before", "after"):
            rad, mist_level, mist_cap, clamp = rads[which]
            r = resample(rad, scale)
            if div and beams and mist_level > 0.001:
                h, w_ = H // div, W // div
                d = dirs[div // 2::div, div // 2::div][:h, :w_]
                reach = np.minimum(dist[div // 2::div, div // 2::div][:h, :w_], FOG_REACH)
                lit_ = mist(cam, d, reach, ign(h, w_, 7), beams, rho, ground_y)
                glow = lit_[..., None] * ft * (mist_level * BEAM_STRENGTH / (4 * math.pi))
                if mist_cap is not None: glow = knee(glow, mist_cap)
                m = np.repeat(np.repeat(glow, div, 0), div, 1)[:H, :W]
                r = r + np.pad(m, ((0, H - m.shape[0]), (0, W - m.shape[1]), (0, 0)))
            bl = bloom(r, g["exposure"], clamp) if has_bloom else None
            save(os.path.join(out_dir, f"{frame_name}-{level}-{which}.png"), frame(r, bloom=bl, **g))
    print("rendered", frame_name, f"sun {e:.1f}", f"lights {lights:.2f}", f"floods after {floods_after:.2f}", f"headlamps {heads_on:.2f}")


main()
