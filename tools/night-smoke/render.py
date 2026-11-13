"""Renders the road behind a car at night as the race draws it, before and after the night smoke fix.

Cycles renders the opaque scene under the sky's ambient light alone: a straight, the camera's own car at its nose, a car
close ahead on the left and two further on, and with floodlights their masts. The sun or moon, its shadow and the rest
are done here per pixel the way the game's shaders do them:
  shadow   the sun's or moon's shadow as the opaque pass reads it: from a shadow map of the cars, its texels as big as
           the level's first cascade makes them. Medium filters it as URP's soft shadows do (a 5 x 5 tent); Ultra as
           ScreenShadows.shader does (a blocker search, then a 32-tap disc turned per pixel by the same noise).
  pools    LightPool.shader: the floodlights' pools multiply the ground (after: times LampFill, which divides the same
           shadow back out)
  heads    Headlamps.hlsl: every car's headlamps in the ground's own shading, the same before and after
  mist     Volumetrics.shader: the floodlights' beams in the mist, at Ultra
  haze     Haze.shader, at Ultra: the cars' exhaust puffs (before: redrawing the opaque copy; after: scaling what is drawn)
  smoke    Smoke.shader: tyre smoke from the car ahead locking up (before: lit by the moon, the sky and the night glow,
           blocking what is behind; after: the headlamps and floodlights light it too, and at night it hides only as
           much as it is lit)
then bloom and the grade.

Usage: python3 render.py out_dir hour floods shot     floods 0 or 1; shot "ivan" (haze) or "lockup" (tyre smoke)
       REUSE=1 keeps an earlier Cycles render. Needs bpy (Blender as a module), numpy, scipy, pillow and OpenEXR.
"""
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
from post import lin, frame, bloom, save, LUMA  # noqa: E402

out_dir, hour, floods, shot = sys.argv[1], float(sys.argv[2]), sys.argv[3] == "1", sys.argv[4]
W, H = int(os.environ.get("W", "1280")), int(os.environ.get("H", "720"))
FOV = 46.0

# NightLook, CarLamps and CarEffects.
FLOOD = np.array([1.0, 0.95, 0.86])
HEAD = np.array([0.9, 0.95, 1.0])
POOL_BRIGHTENING, POOL_LIFT = 7.0, 0.035
# NightLook.HeadlampGround and HeadlampReach; CarLamps' nose height.
HEADLAMP_GROUND, HEADLAMP_REACH, NOSE_Y = 5.0, 75.0, 0.65
BEAM_STRENGTH = 30000.0 * float(lin(0.2)) / 0.2
FOG_REACH, MIST_DENSITY, MIST_HEIGHT = 700.0, 0.0012, 40.0
SMOKE_TINT = lin(np.array([0.82, 0.82, 0.84]))
LAMP_FILL_MOST = 12.0
SMOKE_LAMP_CAP, SMOKE_DARK_SHOWS = 0.15, 0.6
# Levels: render scale, bloom, volumetric divisor and steps, first cascade's texel (m), screen shadows, haze, soft smoke.
LEVELS = {"medium": dict(scale=0.8, bloom=True, mist=0, texel=0.06, screen=False, haze=False, soft=False),
          "ultra": dict(scale=1.0, bloom=True, mist=2, texel=0.016, screen=True, haze=True, soft=True)}
# ScreenShadows at Ultra (QualityLevels.Shadows[3]): spread, filter taps, search taps, widest texels.
ULTRA_SOFT = (0.008, 32, 24, 24.0)
DISC = np.array([(-0.94201624, -0.39906216), (0.94558609, -0.76890725), (-0.09418410, -0.92938870),
                 (0.34495938, 0.29387760), (-0.91588581, 0.45771432), (-0.81544232, -0.87912464),
                 (-0.38277543, 0.27676845), (0.97484398, 0.75648379), (0.44323325, -0.97511554),
                 (0.53742981, -0.47373420), (-0.26496911, -0.41893023), (0.79197514, 0.19090188),
                 (-0.24188840, 0.99706507), (-0.81409955, 0.91437590), (0.19984126, 0.78641367),
                 (0.14383161, -0.14100790)])

# The cars: centre (x, z), colour, tail lamps. The first is the camera's own car, its nose at z = 0.
OWN = (0.9, -2.2)
CARS = [((-0.2, 8.0), (0.92, 0.92, 0.9)), ((1.8, 30.0), (0.85, 0.25, 0.75)), ((-1.3, 46.0), (0.85, 0.7, 0.1))]
HALF_LENGTH, HALF_WIDTH, BODY_TOP, CABIN_TOP = 2.2, 0.95, 0.85, 1.32
# How fast the cars run, m/s: flat out for the haze, braking into a corner for the lockup.
SPEED = 40.0 if shot == "ivan" else 25.0
# The track heads so the sun or moon is ahead and to the right, as in Ivan's frame: degrees from ahead.
LIGHT_AZIMUTH = 8.0
# The race's night is brighter than sky.py works it out (the floodlit sky glow, the moon over it): its ambient light and
# moonlight are raised by this at full night, which puts the unlit road and the sky where they are in Ivan's frame at
# 20:43 (sRGB 8, 14, 29 and 85, 104, 139).
NIGHT_GAIN = 11.0
SKY_SEEN = (85, 104, 139)


def b(v):
    return (v[0], v[2], v[1])


def material(name, colour, rough, emit=None, strength=1.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*lin(colour), 1)
    p.inputs["Roughness"].default_value = rough
    if emit is not None:
        p.inputs["Emission Color"].default_value = (*lin(emit), 1)
        p.inputs["Emission Strength"].default_value = strength
    return m


def box(centre, size, mat, lamp=False):
    """A box; a lamp is seen glowing but lights nothing, as the game's emissive lamps light nothing."""
    bpy.ops.mesh.primitive_cube_add(size=1, location=b(centre))
    ob = bpy.context.object
    ob.scale = b(size)
    ob.data.materials.append(mat)
    if lamp:
        ob.visible_diffuse = ob.visible_glossy = ob.visible_transmission = ob.visible_shadow = False


def plane(name, x0, x1, z0, z1, y, mat):
    me = bpy.data.meshes.new(name)
    me.from_pydata([b((x0, y, z0)), b((x1, y, z0)), b((x1, y, z1)), b((x0, y, z1))], [], [(0, 1, 2, 3)])
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    me.materials.append(mat)


def car_boxes():
    """Every car as two boxes, body and cabin: (min, max) corners, for the shadow map."""
    out = []
    for (x, z), _ in [(OWN, None)] + CARS:
        out.append((np.array([x - HALF_WIDTH, 0.12, z - HALF_LENGTH]), np.array([x + HALF_WIDTH, BODY_TOP, z + HALF_LENGTH])))
        out.append((np.array([x - 0.75, BODY_TOP, z - 1.3]), np.array([x + 0.75, CABIN_TOP, z + 0.6])))
    return out


def masts():
    """The floodlights along the left of the straight: each lens and the point it aims at."""
    out = []
    for k in range(-2, 34):
        z = k * 45.0 + 20.0
        out.append((np.array([-14.4, 18.0, z]), np.array([-3.0 if k % 2 else 3.0, 0.0, z])))
    return out


def scene(s):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = int(os.environ.get("SAMPLES", "24"))
    sc.cycles.use_denoising = True
    # No bounces: the game's opaque pass has none either, only the sky's ambient and the lights it draws.
    sc.cycles.max_bounces = 0
    sc.render.resolution_x, sc.render.resolution_y = W, H
    asphalt = material("asphalt", (0.17, 0.17, 0.18), 0.55)
    grass = material("grass", (0.25, 0.34, 0.18), 0.95)
    line = material("line", (0.93, 0.93, 0.91), 0.6)
    plane("track", -6, 6, -30, 1500, 0.0, asphalt)
    plane("grass l", -400, -6, -30, 1500, -0.02, grass)
    plane("grass r", 6, 400, -30, 1500, -0.02, grass)
    for x in (-5.6, 5.6): plane("edge", x - 0.06, x + 0.06, -30, 1500, 0.004, line)
    for z in range(-30, 1500, 12): plane("dash", -0.06, 0.06, z, z + 4, 0.004, line)
    tail = material("tail", (0.32, 0.03, 0.03), 0.4, emit=(1, 0.06, 0.03), strength=0.9 * s["on"])
    for i, ((x, z), colour) in enumerate(CARS):
        paint = material(f"paint{i}", colour, 0.3)
        box((x, (0.12 + BODY_TOP) / 2, z), (HALF_WIDTH * 2, BODY_TOP - 0.12, HALF_LENGTH * 2), paint)
        box((x, (BODY_TOP + CABIN_TOP) / 2, z - 0.35), (1.5, CABIN_TOP - BODY_TOP, 1.9), paint)
        for sx in (-0.6, 0.6): box((x + sx, 0.62, z - HALF_LENGTH - 0.01), (0.4, 0.12, 0.02), tail, lamp=True)
        tyre = material(f"tyre{i}", (0.05, 0.05, 0.05), 0.9)
        for sx in (-0.82, 0.82):
            for sz in (-1.35, 1.35): box((x + sx, 0.31, z + sz), (0.24, 0.62, 0.62), tyre)
    if floods:
        steel = material("steel", (0.55, 0.57, 0.58), 0.4)
        lens = material("lens", FLOOD, 0.3, emit=FLOOD, strength=2.0 * s["on"])
        for pos, _ in masts():
            box((pos[0] - 0.6, 9, pos[2]), (0.4, 18, 0.4), steel)
            box(pos, (0.9, 0.9, 0.9), lens, lamp=True)
    w = bpy.data.worlds.new("sky")
    w.use_nodes = True
    nt = w.node_tree
    lp = nt.nodes.new("ShaderNodeLightPath")
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    nt.links.new(lp.outputs["Is Camera Ray"], mix.inputs[0])
    # The ground's ambient light as the game's SH gives it facing up: the sky colour of the trilight.
    mix.inputs[6].default_value = (*(skyclock.ambient(s["amb_sky"]) * s["gain"]), 1)
    seen = lin(s["horizon"] * 0.6 + s["zenith"] * 0.4) if s["night"] < 0.5 else radiance_for(SKY_SEEN, s)
    mix.inputs[7].default_value = (*seen, 1)
    nt.links.new(mix.outputs[2], nt.nodes["Background"].inputs["Color"])
    sc.world = w


def radiance_for(seen, s):
    """The linear sky radiance that the post stack turns into this sRGB colour on screen."""
    want = lin(np.asarray(seen, dtype=np.float64) / 255.0)
    g = skyclock.grade(s)
    r = want.copy() * 0.3
    for _ in range(60):
        got = lin(frame(r[None, None, :], **g)[0, 0])
        r = r * (want / np.maximum(got, 1e-6)) ** 0.7
    return r


def camera():
    pos = np.array([OWN[0], 1.05, OWN[1] + HALF_LENGTH - 0.6])
    target = pos + np.array([-0.05, -0.18, 1.0]) * 10
    cam = bpy.data.cameras.new("cam")
    cam.sensor_fit = "VERTICAL"
    cam.angle_y = math.radians(FOV)
    cam.clip_end = 20000
    co = bpy.data.objects.new("cam", cam)
    co.location = b(pos)
    co.rotation_mode = "QUATERNION"
    co.rotation_quaternion = (Vector(b(target)) - Vector(b(pos))).to_track_quat("-Z", "Y")
    bpy.context.scene.collection.objects.link(co)
    bpy.context.scene.camera = co
    q = co.rotation_quaternion
    ys, xs = np.mgrid[0:H, 0:W]
    t = math.tan(math.radians(FOV) / 2)
    v = np.stack([(xs + 0.5 - W / 2) / (H / 2) * t, -(ys + 0.5 - H / 2) / (H / 2) * t, -np.ones((H, W))], -1)
    rot = np.array(q.to_matrix())
    dirs = (v @ rot.T)[..., [0, 2, 1]]
    dirs /= np.linalg.norm(dirs, axis=-1, keepdims=True)
    fwd = (rot @ np.array([0, 0, -1.0]))[[0, 2, 1]]
    up = (rot @ np.array([0, 1.0, 0]))[[0, 2, 1]]
    return pos, dirs, fwd, up


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


def sm(a, b_, x):
    t = np.clip((x - a) / (b_ - a), 0, 1)
    return t * t * (3 - 2 * t)


def ign(h, w):
    ys, xs = np.mgrid[0:h, 0:w] + 0.5
    return np.mod(52.9829189 * np.mod(0.06711056 * xs + 0.00583715 * ys, 1.0), 1.0)


# ---------------------------------------------------------------- the sun's or moon's shadow

def light_frame(to_light):
    """The shadow map's axes: two across the light and the light's own direction."""
    a = np.cross(to_light, [0, 1.0, 0])
    if np.linalg.norm(a) < 1e-4: a = np.array([1.0, 0, 0])
    a /= np.linalg.norm(a)
    c = np.cross(to_light, a)
    return a, c


def occluded(p, to_light, boxes):
    """True where a ray from p towards the light hits a car: the shadow map's depth test."""
    hit = np.zeros(p.shape[:-1], bool)
    inv = 1.0 / np.where(np.abs(to_light) < 1e-9, 1e-9, to_light)
    for lo, hi in boxes:
        t0 = (lo - p) * inv
        t1 = (hi - p) * inv
        tn = np.minimum(t0, t1).max(-1)
        tf = np.maximum(t0, t1).min(-1)
        hit |= (tf > np.maximum(tn, 0.02))
    return hit


def caster_gap(p, to_light, boxes):
    """How far towards the light the nearest caster is, m; inf where nothing is."""
    gap = np.full(p.shape[:-1], np.inf)
    inv = 1.0 / np.where(np.abs(to_light) < 1e-9, 1e-9, to_light)
    for lo, hi in boxes:
        t0 = (lo - p) * inv
        t1 = (hi - p) * inv
        tn = np.minimum(t0, t1).max(-1)
        tf = np.maximum(t0, t1).min(-1)
        ok = tf > np.maximum(tn, 0.02)
        gap = np.where(ok, np.minimum(gap, np.maximum(tn, 0.0)), gap)
    return gap


def shadow_map_tap(p, offset, to_light, axes, texel, boxes):
    """One tap of the shadow map at p moved across the light by offset (m, in the map's axes), snapped to its texel."""
    a, c = axes
    u = (p @ a + offset[..., 0]) / texel
    v = (p @ c + offset[..., 1]) / texel
    du = (np.floor(u) + 0.5) * texel - (p @ a)
    dv = (np.floor(v) + 0.5) * texel - (p @ c)
    q = p + a * du[..., None] + c * dv[..., None]
    return ~occluded(q, to_light, boxes)


def shadow(p, sky, to_light, level, boxes):
    """The shadow the opaque pass reads, 1 lit to 0 shadowed."""
    texel = LEVELS[level]["texel"]
    axes = light_frame(to_light)
    s = np.ones(p.shape[:-1])
    m = ~sky
    pm = p[m]
    if not LEVELS[level]["screen"]:
        # URP's medium soft shadows: a 5 x 5 tent over the texels.
        acc, wsum = np.zeros(len(pm)), 0.0
        for i in range(-2, 3):
            for j in range(-2, 3):
                w = (3 - abs(i)) * (3 - abs(j))
                acc += w * shadow_map_tap(pm, np.tile([i * texel * 0.6, j * texel * 0.6], (len(pm), 1)), to_light, axes, texel, boxes)
                wsum += w
        s[m] = acc / wsum
        return s
    spread, taps, search, widest_t = ULTRA_SOFT
    widest = widest_t * texel
    noise = ign(*p.shape[:2])[m]
    ang = noise * 2 * math.pi
    co, si = np.cos(ang), np.sin(ang)
    reach = np.clip(spread * 20.0, 2 * texel, widest)
    gaps, found = np.zeros(len(pm)), np.zeros(len(pm))
    for i in range(search):
        d = DISC[i & 15] * (1.0 if i < 16 else 0.5)
        o = np.stack([co * d[0] - si * d[1], si * d[0] + co * d[1]], -1) * reach
        a, c = axes
        q = pm + a * o[:, :1] + c * o[:, 1:]
        g = caster_gap(q, to_light, boxes)
        ok = np.isfinite(g)
        gaps += np.where(ok, g, 0)
        found += ok
    lit = np.ones(len(pm))
    any_ = found > 0
    radius = np.clip(np.where(any_, gaps / np.maximum(found, 1), 0) * spread, 1.25 * texel, widest)
    acc = np.zeros(len(pm))
    for j in range(taps):
        d = DISC[j & 15]
        if j >= 16: d = np.array([d[1], -d[0]]) * 0.6
        o = np.stack([co * d[0] - si * d[1], si * d[0] + co * d[1]], -1) * radius[:, None]
        acc += shadow_map_tap(pm, o, to_light, axes, texel, boxes)
    lit = np.where(any_, acc / taps, 1.0)
    s[m] = lit
    return s


# ---------------------------------------------------------------- the lamps on the ground

def headlamps():
    """Each car's lamps as HeadlampGround hands them over: the middle of its nose, its aim, the lamps' height."""
    return [(np.array([x, NOSE_Y, z + HALF_LENGTH]), np.array([0, 0, 1.0]), NOSE_Y) for (x, z) in [OWN] + [c for c, _ in CARS]]


def ground_light(pos, nrm, sky, lamps):
    """Headlamps.hlsl: the light the headlamps throw on the ground, per unit of albedo and of their light."""
    e = np.zeros(pos.shape[:-1])
    for at, fwd, h in lamps:
        up = np.array([0.0, 1.0, 0.0]) - fwd * fwd[1]
        up /= np.linalg.norm(up)
        right = np.cross(up, fwd)
        v = pos - at
        z, x, y = v @ fwd, v @ right, v @ up
        d = np.maximum(np.linalg.norm(v, axis=-1), 1e-3)
        zc = np.maximum(z, 1e-3)
        along = (0.5 + 0.5 * sm(0.5, 9.0, z)) * (1 - sm(15.0, HEADLAMP_REACH, z)) * (z > 0.2) * (z < HEADLAMP_REACH)
        u = np.abs(x) / np.minimum(2.0 + 0.35 * zc, 6.0)
        cut_ = (1 - sm(0.0, 0.06, y / zc)) * sm(-h - 4.0, -h - 2.5, y)
        facing = np.clip(np.sum(nrm * -(v / d[..., None]), -1), 0, None) / np.maximum(h / d, 0.01)
        e += along * np.exp(-1.2 * u * u) * cut_ * np.minimum(facing, 1.5)
    # Only the ground's shaders take it: the road, its lines and the grass, not the cars.
    return np.where(sky | (pos[..., 1] > 0.05), 0.0, e)


def pools(pos, up_facing):
    p = np.zeros(pos.shape[:-1])
    for _, target in masts():
        d = np.sqrt((pos[..., 0] - target[0]) ** 2 + (pos[..., 2] - target[2]) ** 2) / 26.0
        p = np.maximum(p, np.clip(1 - d * d, 0, 1) ** 2)
    return p * up_facing


def flood_beams(cam):
    out = []
    for lens, target in masts():
        reach = np.linalg.norm(target - lens)
        cone = min(max(2 * math.degrees(math.atan(26.0 * 1.1 / reach)), 30), 120)
        if np.linalg.norm(lens - cam) < 400:
            out.append((lens, (target - lens) / reach, reach * 1.6, math.cos(math.radians(cone / 2))))
    out.sort(key=lambda bm: np.linalg.norm(bm[0] - cam))
    return out[:16]


def beam_at(bm, p):
    lens, aim, reach, edge = bm
    v = p - lens
    dd = (v * v).sum(-1)
    c = (v / np.sqrt(dd + 1e-4)[..., None]) @ aim
    cone = sm(edge, edge + (1 - edge) * 0.4, c)
    return np.where(dd < reach * reach, cone * np.clip(1 - dd / (reach * reach), 0, 1) / (dd + 9.0), 0.0)


def mist(cam, d, far, jitter, beams, samples=8):
    lit = np.zeros(d.shape[:-1])
    for bm in beams:
        lens, aim, r, edge = bm
        w0 = cam - lens
        bb = d @ w0
        c = w0 @ w0 - r * r
        disc = bb * bb - c
        root = np.sqrt(np.maximum(disc, 0))
        t0, t1 = np.maximum(-bb - root, 0), np.minimum(-bb + root, far)
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
            rho = MIST_DENSITY * np.exp(-np.maximum(p[..., 1], 0) / MIST_HEIGHT)
            lit += np.where(ok, beam_at(bm, p) * rho * np.exp(-rho * t) * dt, 0)
    return lit


def lamp_fill(scene, sh, n, pos, cam, to_light, main_col, sky_up, sky_eq, heads, smooth=0.45):
    """LampFill.hlsl: what a pool multiplies, as a share of the scene's colour: the night's light alone, unshadowed."""
    sh_n = sky_eq + (sky_up - sky_eq) * np.clip(n[..., 1:2], 0, 1)
    direct = main_col * np.clip(n @ to_light, 0, 1)[..., None]
    open_, shaded = sh_n + direct, sh_n + direct * sh[..., None] + heads
    fill = np.minimum(open_ / np.maximum(shaded, open_ / LAMP_FILL_MOST + 1e-5), LAMP_FILL_MOST)
    view = cam - pos
    view /= np.maximum(np.linalg.norm(view, axis=-1, keepdims=True), 1e-6)
    nov = np.clip((n * view).sum(-1, keepdims=True), 0, 1)
    refl = 2 * nov * n - view
    sh_r = sky_eq + (sky_up - sky_eq) * np.clip(refl[..., 1:2], 0, 1)
    rough = (1 - smooth) ** 2
    sheen = np.minimum(sh_r * (0.04 + (min(smooth + 0.04, 1) - 0.04) * (1 - nov) ** 4) / (rough * rough + 1), scene)
    lit = (scene - sheen) * fill + sheen
    return np.where(scene > 1e-5, lit / np.maximum(scene, 1e-5), fill)


def resample(img, scale):
    from scipy.ndimage import zoom
    if scale >= 0.999: return img
    small = zoom(img, (scale, scale, 1), order=1)
    return zoom(small, (img.shape[0] / small.shape[0], img.shape[1] / small.shape[1], 1), order=1)


# ---------------------------------------------------------------- the particles

def haze_puffs(rng):
    """CarEffects.Exhaust behind every car near the camera, flat out: positions now, size, alpha."""
    out = []
    for (x, z), _ in CARS[:2]:
        rear = np.array([x, 0.32, z - (1.3 + 0.95)])
        for k in range(int(0.8 * 22)):
            tau = (k + rng.random()) / 22.0
            life = rng.uniform(0.45, 0.8)
            if tau >= life: continue
            age = tau / life
            p0 = rear + np.array([rng.uniform(-0.45, 0.45), 0, 0])
            vel = np.array([0, rng.uniform(0.2, 0.6), SPEED * 0.55 - rng.uniform(0.8, 2.0)])
            p = p0 + (vel - np.array([0, 0, SPEED])) * tau
            size = rng.uniform(0.35, 0.55) * (0.7 + 1.5 * age)
            a = np.interp(age, [0, 0.1, 0.4, 1], [0, 1, 0.8, 0])
            out.append(("haze", p, size, a, rng.uniform(0, 2 * math.pi)))
    return out


def smoke_puffs(rng):
    """CarEffects.Puff from the car ahead's front tyres, locked for the last 0.7 s under braking."""
    out = []
    (x, z), _ = CARS[0]
    rate = 38 * (0.45 + 0.55 * min(SPEED / 30, 1))
    for sx in (-0.82, 0.82):
        for sz in (1.35,):
            contact = np.array([x + sx, 0.0, z + sz])
            for k in range(int(0.7 * rate)):
                tau = (k + rng.random()) / rate
                life = rng.uniform(2.2, 3.6)
                if tau >= life: continue
                age = tau / life
                v = rng.normal(size=3)
                p0 = contact + np.array([0, 0.25, 0]) + v / np.linalg.norm(v) * 0.15 * rng.random() ** (1 / 3)
                v0 = np.array([0, 0, SPEED]) * rng.uniform(0.2, 0.4) + np.array([rng.uniform(-0.8, 0.8), rng.uniform(0.4, 1.1), 0])
                drift = np.array([rng.uniform(-0.4, 0.4), rng.uniform(0.25, 0.7), rng.uniform(-0.4, 0.4)])
                p = p0 + v0 * (1 - math.exp(-1.4 * tau)) / 1.4 + drift * tau - np.array([0, 0, SPEED]) * tau
                grow = np.interp(age, [0, 0.3, 1], [0.5, 2.2, 4])
                size = rng.uniform(0.6, 1.1) * grow
                a = np.interp(age, [0, 0.06, 0.35, 1], [0, 0.55, 0.44, 0])
                out.append(("smoke", p, size, a, rng.uniform(0, 2 * math.pi)))
    return out


def project(cam, fwd, up, p):
    right = np.cross(up, fwd)
    v = p - cam
    z = v @ fwd
    t = math.tan(math.radians(FOV) / 2)
    sx = W / 2 + (v @ right) / (z * t) * (H / 2)
    sy = H / 2 - (v @ up) / (z * t) * (H / 2)
    return sx, sy, z


def headlamps_on_smoke(lamps, p):
    """Smoke.shader's HeadlampsOnSmoke: the ground's beam, spreading a little above the lamps' height in the air."""
    total = 0.0
    for at, fwd, h in lamps:
        v = p - at
        z = v @ fwd
        if z <= 0.2 or z >= HEADLAMP_REACH: continue
        up = np.array([0.0, 1.0, 0.0])
        x, y = v @ np.cross(up, fwd), v @ up
        along = (0.5 + 0.5 * float(sm(0.5, 9.0, z))) * (1 - float(sm(15.0, HEADLAMP_REACH, z)))
        u = abs(x) / min(2.0 + 0.35 * z, 6.0)
        total += along * math.exp(-1.2 * u * u) * float((1 - sm(0.0, 0.25, y / z)) * sm(-h - 4.0, -h - 2.5, y))
    return total


def flood_at(bm, p):
    lens, aim, reach, edge = bm
    target = reach * reach / 2.56
    at_target = (1 - 1 / 2.56) / (target + 9.0)
    return min(float(beam_at(bm, p)) / at_target, 1.5)


def draw_particles(rad, copy, puffs, cam, fwd, up, depth, s, level, fixed, light):
    """Draws the puffs far to near over rad, as Smoke.shader and Haze.shader draw them."""
    to_sun, sun_col, sky_up, sky_eq, night_glow, open_, heads, beams, on, lights = light
    puffs = sorted(puffs, key=lambda q: -np.linalg.norm(q[1] - cam))
    t = math.tan(math.radians(FOV) / 2)
    for kind, p, size, a, rot in puffs:
        if kind == "haze" and not LEVELS[level]["haze"]: continue
        sx, sy, z = project(cam, fwd, up, p)
        if z < 0.3: continue
        half = min(size * 0.5 / (z * t) * (H / 2), 0.8 * H / 2)
        x0, x1 = int(max(sx - half * 1.42, 0)), int(min(sx + half * 1.42 + 1, W))
        y0, y1 = int(max(sy - half * 1.42, 0)), int(min(sy + half * 1.42 + 1, H))
        if x0 >= x1 or y0 >= y1: continue
        ys, xs = np.mgrid[y0:y1, x0:x1] + 0.5
        # The quad's own corner coordinates, turned with the particle.
        qx, qy = (xs - sx) / half, -(ys - sy) / half
        c, si = math.cos(rot), math.sin(rot)
        px, py = c * qx + si * qy, -si * qx + c * qy
        r2 = px * px + py * py
        eye = z
        if kind == "haze":
            r = np.clip(1 - np.sqrt(r2), 0, 1)
            puff = r * r
            tt = 10.0
            wx = np.sin(py * 9 + tt * 23) + 0.5 * np.sin(px * 13 - tt * 17)
            wy = np.sin(px * 8 - tt * 19) + 0.5 * np.sin(py * 11 + tt * 29)
            strength = 0.006 * puff * a / max(1.0, eye * 0.15)
            bx = np.clip((xs + wx * strength * W).astype(int), 0, W - 1)
            by = np.clip((ys - wy * strength * H).astype(int), 0, H - 1)
            bent = copy[by, bx]
            still = copy[ys.astype(int), xs.astype(int)]
            k = (np.clip(puff * 2.5, 0, 1) * min(a * 3, 1))[..., None] * (puff > 0)[..., None]
            if fixed: k = k * (1 - on)
            dst = rad[y0:y1, x0:x1]
            if not fixed:
                rad[y0:y1, x0:x1] = bent * k + dst * (1 - k)
            else:
                change = np.clip((bent + 1e-4) / (still + 1e-4), 0.6, 1.6)
                rad[y0:y1, x0:x1] = dst * (1 + (change - 1) * k)
            continue
        ang = np.arctan2(py, px)
        lump = 1 + 0.09 * np.sin(ang * 5 + 1.3) + 0.06 * np.sin(ang * 9 + 2.0)
        rr = np.sqrt(r2) * lump
        body = np.clip(1 - rr, 0, 1)
        body = body * body * (3 - 2 * body)
        to_eye = (cam - p) / np.linalg.norm(cam - p)
        right = np.cross(up, fwd)
        # The billboard faces the camera: its offset from the centre in the world, over the radius.
        off = (right * qx[..., None] + up * qy[..., None])
        sky = sky_up * 0.6 + sky_eq * 0.4 + night_glow
        if fixed and on > 0:
            lamps = lights[0] * headlamps_on_smoke(heads, p) + open_ * sum(lights[1] * flood_at(bm, p) for bm in beams)
            lum = (lamps * SMOKE_TINT) @ LUMA
            sky = sky + lamps / (1 + lum / SMOKE_LAMP_CAP)
        nn = off * 0.8 + to_eye * np.sqrt(np.clip(1 - r2, 0, 1))[..., None]
        nn /= np.maximum(np.linalg.norm(nn, axis=-1, keepdims=True), 1e-6)
        facing = nn @ to_sun
        wrap = np.clip((facing + 0.5) / 1.5, 0, 1)
        through = max(-to_eye @ to_sun, 0) ** 6 * 0.9
        lightv = sun_col * (wrap * 0.85 + through)[..., None] + sky * (0.75 + 0.25 * nn[..., 1:2])
        col = SMOKE_TINT * lightv
        alpha = a * body
        if LEVELS[level]["soft"]:
            alpha = alpha * np.clip((depth[y0:y1, x0:x1] - eye) / 0.6, 0, 1)
        alpha = alpha * min(max((eye - 0.4) / 1.6, 0), 1)
        dst = rad[y0:y1, x0:x1]
        if not fixed:
            rad[y0:y1, x0:x1] = col * alpha[..., None] + dst * (1 - alpha[..., None])
        else:
            shows = np.clip((col @ LUMA) / SMOKE_DARK_SHOWS, 0, 1)
            hide = alpha * (1 + (shows - 1) * on)
            rad[y0:y1, x0:x1] = col * alpha[..., None] + dst * (1 - hide[..., None])
    return rad


def main():
    os.makedirs(out_dir, exist_ok=True)
    s = skyclock.at(hour)
    s["on"] = float(skyclock.sm(0, 0.12, s["night"]))
    s["gain"] = 1.0 + (NIGHT_GAIN - 1.0) * s["night"]
    on, lights_amount = s["on"], s["night"]
    scene(s)
    cam, dirs, fwd, up = camera()
    vl = bpy.context.scene.view_layers[0]
    for attr in ("use_pass_normal", "use_pass_z", "use_pass_position", "use_pass_diffuse_color"):
        setattr(vl, attr, True)
    sc = bpy.context.scene
    sc.render.image_settings.media_type = "MULTI_LAYER_IMAGE"
    sc.render.image_settings.file_format = "OPEN_EXR_MULTILAYER"
    sc.render.image_settings.color_depth = "32"
    tag = f"{hour:g}-{'floods' if floods else 'dark'}"
    path = os.path.join(out_dir, "cycles", tag + ".exr")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    sc.render.filepath = path
    if not (os.environ.get("REUSE") and os.path.exists(path)):
        bpy.ops.render.render(write_still=True)
    L = read_layers(path)
    ambient_scene = L["Combined"][..., :3]
    albedo = L["Diffuse Color"][..., :3]
    pos = L["Position"][..., [0, 2, 1]]
    nrm = L["Normal"][..., [0, 2, 1]]
    sky = (np.linalg.norm(pos - cam, axis=-1) > 15000) | (np.linalg.norm(pos, axis=-1) < 1e-6)
    depth = np.where(sky, 1e7, (pos - cam) @ fwd)

    # The sun or moon, turned so it is ahead and to the right.
    d = s["light_dir"]
    elev = math.asin(max(min(d[1], 1), -1))
    az = math.radians(LIGHT_AZIMUTH)
    to_light = np.array([math.sin(az) * math.cos(elev), math.sin(elev), math.cos(az) * math.cos(elev)])
    i = skyclock.match(s["colour"], s["light"], s["amb_sky"], max(d[1], 0.3)) if d[1] > 0 else 0.0
    main_col = lin(s["colour"]) * i * s["gain"]
    boxes = car_boxes()
    nl = np.clip(nrm @ to_light, 0, 1)
    sky_up, sky_eq = skyclock.ambient(s["amb_sky"]) * s["gain"], skyclock.ambient(s["amb_eq"]) * s["gain"]
    open_up = sky_up + main_col * max(to_light[1], 0)
    lamps = headlamps()
    head_light = lin(HEAD) * HEADLAMP_GROUND * on
    # The headlamps in the ground's own shading (Headlamps.hlsl), part of the opaque scene before and after.
    h_ground = ground_light(pos, nrm, sky, lamps)[..., None] * head_light
    h_up = ground_light(pos, np.broadcast_to([0, 1.0, 0], pos.shape), sky, lamps)[..., None] * head_light
    beams = flood_beams(cam) if floods else []
    g = skyclock.grade(s)
    rng_seed = 7
    for level, cfg in LEVELS.items():
        sh = shadow(pos, sky, to_light, level, boxes)
        opaque = ambient_scene + albedo * (main_col * (nl * sh)[..., None] + h_ground)
        # LampFill for the pools: the open ground's light over the shadowed ground's, facing up.
        fill_up = lamp_fill(opaque, sh, np.broadcast_to([0, 1.0, 0], pos.shape), pos, cam, to_light, main_col, sky_up, sky_eq, h_up)
        m = np.zeros_like(opaque)
        if cfg["mist"] and beams and on > 0.001:
            div = cfg["mist"]
            h, w_ = H // div, W // div
            dd = dirs[div // 2::div, div // 2::div][:h, :w_]
            far = np.minimum(np.where(sky, 1e7, np.linalg.norm(pos - cam, axis=-1))[div // 2::div, div // 2::div][:h, :w_], FOG_REACH)
            lit = mist(cam, dd, far, ign(h, w_), beams)
            mm = np.repeat(np.repeat(lit, div, 0), div, 1)[:H, :W, None] * (lin(FLOOD) * on * BEAM_STRENGTH / (4 * math.pi))
            m[:mm.shape[0], :mm.shape[1]] = mm
        up_facing = (nrm[..., 1] > 0.7) & ~sky & (pos[..., 1] < 0.2)
        pool = pools(pos, up_facing)[..., None] if floods else np.zeros(opaque.shape[:-1] + (1,))
        for name in ("before", "after"):
            fixed = name == "after"
            pf = fill_up if fixed else 1.0
            rad = (opaque + m) * (1 + lin(FLOOD) * POOL_BRIGHTENING * lights_amount * pool * pf) + lin(FLOOD) * POOL_LIFT * lights_amount * pool
            rng = np.random.default_rng(rng_seed)
            puffs = haze_puffs(rng) + (smoke_puffs(rng) if shot == "lockup" else [])
            light = (to_light, main_col, sky_up, sky_eq, lin(FLOOD * 0.3 * on), open_up,
                     lamps if on > 0.001 else [], beams, on, (head_light, lin(FLOOD) * POOL_BRIGHTENING * lights_amount))
            rad = draw_particles(rad, opaque, puffs, cam, fwd, up, depth, sh, level, fixed, light)
            r = resample(rad, cfg["scale"])
            bl = bloom(r, g["exposure"]) if cfg["bloom"] else None
            save(os.path.join(out_dir, f"{shot}-{tag}-{level}-{name}.png"), frame(r, bloom=bl, **g))
    print("rendered", shot, tag, "night", round(s["night"], 3), "lights on", round(on, 3))


main()
