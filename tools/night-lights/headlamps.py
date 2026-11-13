"""Renders Ivan's night frames with the cars' headlamps as the race draws them, before and after the headlamp fix.

The real headlamp lights (NightLights: spot lights that light only the cars, just ahead of each bumper at 24, dimmed for
a car close in front) are rendered by Cycles with URP's inverse square falloff. The headlamps' light on the ground is the
ground's own shading (Headlamps.hlsl): it is added to the opaque pass in each surface's own colour, from every car, and
the floodlights' pools then brighten it as they brighten the rest. Before is that light as it first went in, at full
strength from every car with nothing to stop it adding up; after, the cars' light saturates at 1.3 times one car's and a
car under the floodlights throws a share of it. The floodlights' beams in the mist are as beams.py draws them, and the
post stack follows.

Usage: [HOUR=22] python headlamps.py out_dir frame       frame is "field" (19:06, floodlights on, twenty cars down the
       straight), "alongside" (19:06, floodlights on, a car alongside), "dark" (20:43, no circuit lights, one car) or
       "pack" (20:42, no circuit lights, cars close ahead); REUSE=1 keeps renders
"""
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
out_dir, frame_name = sys.argv[1], sys.argv[2]
HOURS = {"field": 19.1, "alongside": 19.1, "dark": 20.72, "pack": 20.7}
hour = float(os.environ.get("HOUR", HOURS[frame_name]))
TAG = frame_name if "HOUR" not in os.environ else f"{frame_name}-{os.environ['HOUR']}"
sys.argv = [sys.argv[0], out_dir, "straight", str(hour)]
sys.path.insert(0, HERE)
import beams  # noqa: E402
from beams import bpy, Vector, b, lin, frame, bloom, save, LUMA, skyclock  # noqa: E402

W, H = beams.W, beams.H
HEAD = beams.HEAD

# NightLights and LightFalloff.
HEAD_RANGE, HEAD_CONE, HEAD_LIGHTS = 70.0, 55.0, 15.0
HEAD_AFTER, HEAD_AHEAD, HEAD_NEAREST = 24.0, 0.15, 2.0
CAR_HALF_LENGTH, CAR_HALF_WIDTH = 2.2, 0.9
# CarLamps: the headlamps' middle and the bumper, from the car's middle, m.
NOSE_Y, NOSE_Z, FRONT_Z = 0.65, 1.8, 2.3
# Headlamps.hlsl and NightLook: the light the headlamps throw on the ground, and how far.
GROUND_LIGHT, GROUND_REACH = 5.0, 75.0
GROUND_UNDER_FLOODS, GROUND_SATURATE = 0.01, 1.466

# Each frame: floodlights on, the camera (position, a point it looks at), the other cars (x, z of their middles, colour),
# and which cars carry a real headlamp light (the nearest the camera: 0 is our own).
FIELD_COLOURS = [(0.85, 0.72, 0.12), (0.2, 0.6, 0.2), (0.7, 0.1, 0.1), (0.85, 0.85, 0.85), (0.2, 0.35, 0.8),
                 (0.85, 0.2, 0.75), (0.95, 0.5, 0.1), (0.1, 0.1, 0.12)]
FIELD = [(x, z, FIELD_COLOURS[i % len(FIELD_COLOURS)]) for i, (x, z) in enumerate(
    [(-2.2, 10.0), (2.6, -9.0), (1.8, 24.0), (-3.0, 33.0), (2.4, 47.0), (-1.0, 60.0), (3.2, 72.0), (-2.6, 86.0),
     (0.8, 101.0), (-3.4, 118.0), (2.2, 137.0), (-1.4, 158.0), (-2.8, -22.0), (1.0, -34.0), (3.0, -48.0),
     (-1.8, -61.0), (2.0, -77.0), (-3.0, -92.0), (0.6, -110.0)])]
FRAMES = {
    "field": (True, ((0.0, 1.15, -2.9), (0.0, 0.5, 7.1)), FIELD, [0, 1, 2]),
    "alongside": (True, ((0.0, 1.05, -2.7), (0.0, 0.45, 7.3)),
                  [(-2.9, 0.6, (0.85, 0.72, 0.12)), (2.0, 32.0, (0.2, 0.6, 0.2)), (-1.5, 60.0, (0.7, 0.1, 0.1))], [0, 1]),
    "dark": (False, ((0.0, 1.05, -2.7), (0.0, 0.45, 7.3)),
             [(-1.8, 110.0, (0.7, 0.1, 0.1)), (1.5, 140.0, (0.85, 0.72, 0.12))], [0]),
    "pack": (False, ((0.0, 1.0, 2.5), (0.0, 0.55, 12.5)),
             [(-2.3, 11.0, (0.85, 0.85, 0.85)), (0.6, 28.0, (0.85, 0.2, 0.75)), (1.8, 36.0, (0.85, 0.72, 0.12))],
             [0, 1, 2]),
}
floods, (cam_pos, cam_at), others, lit = FRAMES[frame_name]
OWN = (0.0, 0.0, (0.55, 0.12, 0.05))
# Every car's middle: our own is FRONT_Z behind the origin, where its nose is.
MIDDLES = [(OWN[0], -FRONT_Z, OWN[2])] + others


def at_share(d, rng):
    """LightFalloff.At: the share of its intensity a URP light gives at this distance."""
    d2 = max(d * d, 1e-4)
    f = d2 / (rng * rng)
    return min(max(1 - f * f, 0), 1) ** 2 / d2


def nearest(own, at, forward, cars):
    """NightLights.Nearest: how near the closest other car in front of a headlamp light is, m."""
    near = HEAD_RANGE
    edge = math.cos(math.radians(HEAD_CONE * 0.5 + 25))
    for i, (x, z, _) in enumerate(cars):
        if i == own: continue
        axis = np.array([0, 0, CAR_HALF_LENGTH])
        start = np.array([x, 0, z]) - axis
        k = min(max(np.dot(at - start, axis) / (2 * CAR_HALF_LENGTH ** 2), 0), 1)
        v = start + axis * 2 * k + np.array([0, 0.6, 0]) - at
        d = np.linalg.norm(v)
        if d >= near + CAR_HALF_WIDTH or np.dot(v, forward) < edge * d: continue
        near = min(near, max(d - CAR_HALF_WIDTH, 0.5))
    return near


def car(name, x, z, colour, coll, own=False):
    paint = beams.material(name, colour, 0.3 if not own else 0.45)
    parts = []
    if own:
        # Only the front of our own car shows: a sports car's bonnet, low between the wings that carry the headlamps.
        parts.append(beams.box((x, 0.32, z + 1.3), (1.1, 0.5, 2.0), paint))
        for sx in (-0.75, 0.75): parts.append(beams.box((x + sx, 0.33, z + 1.3), (0.4, 0.66, 2.0), paint))
    else:
        parts.append(beams.box((x, 0.5, z), (1.9, 0.75, 4.4), paint))
        parts.append(beams.box((x, 1.1, z - 0.3), (1.5, 0.45, 2.2), paint))
        tail = beams.material(name + " tail", (0.32, 0.03, 0.03), 0.4, emit=(1, 0.06, 0.03), strength=0.9)
        for sx in (-0.6, 0.6): parts.append(beams.box((x + sx, 0.72, z - 2.21), (0.4, 0.12, 0.02), tail))
    for p in parts:
        for c in p.users_collection: c.objects.unlink(p)
        coll.objects.link(p)


def head_light(name, at, intensity, coll):
    l = bpy.data.lights.new(name, "SPOT")
    # Unity's intensity I lights a face-on surface d away with I / d^2; Cycles needs 4 pi^2 W for that.
    l.energy = 4 * math.pi ** 2 * intensity
    l.color = tuple(lin(HEAD))
    l.spot_size = math.radians(HEAD_CONE)
    l.spot_blend = 0.5
    l.shadow_soft_size = 0.03
    l.use_shadow = False  # NightLights' headlamp lights cast no shadows
    o = bpy.data.objects.new(name, l)
    o.location = b(at)
    o.rotation_mode = "QUATERNION"
    aim = np.array([0, -math.sin(math.radians(4)), math.cos(math.radians(4))])
    o.rotation_quaternion = Vector(b(aim)).to_track_quat("-Z", "Y")
    bpy.context.scene.collection.objects.link(o)
    o.light_linking.receiver_collection = coll


def build(s, which):
    sc = beams.scene(s)  # the straight's road, grass and masts; its own car and other cars are replaced below
    for ob in list(bpy.data.objects):
        if ob.type == "MESH" and (ob.data.materials and ob.data.materials[0].name.startswith(("bonnet", "other car", "paint", "tail"))):
            bpy.data.objects.remove(ob)
        elif ob.type == "EMPTY":
            bpy.data.objects.remove(ob)
    if not floods:
        for ob in list(bpy.data.objects):
            if ob.type == "MESH" and ob.data.materials and ob.data.materials[0].name == "lens":
                ob.data.materials[0].node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 0
    cars = bpy.data.collections.new("cars")
    bpy.context.scene.collection.children.link(cars)
    allcars = [OWN] + others
    for i, (x, z, colour) in enumerate(allcars):
        car(f"car{i}", x, z - (FRONT_Z if i == 0 else 0), colour, cars, own=(i == 0))
    # Our own car's middle is FRONT_Z behind the beam's start at the origin.
    middles = MIDDLES
    forward = np.array([0, -math.sin(math.radians(4)), math.cos(math.radians(4))])
    for i in lit:
        x, z, _ = middles[i]
        at = np.array([x, NOSE_Y, z + FRONT_Z + HEAD_AHEAD])
        near = nearest(i, at, forward, middles)
        share = at_share(near, HEAD_RANGE)
        scale = min(1.0, HEAD_NEAREST * at_share(HEAD_LIGHTS, HEAD_RANGE) / share) if share > 0 else 1.0
        print(f"car {i}: nearest car in front {near:.1f} m, light at {scale:.2f} of {HEAD_AFTER}")
        head_light(f"head{i}", at, HEAD_AFTER * scale, cars)


def camera():
    cam = bpy.data.cameras.new("cam")
    cam.sensor_fit = "VERTICAL"
    fov = 46.0
    cam.angle_y = math.radians(fov)
    cam.clip_end = 20000
    co = bpy.data.objects.new("cam", cam)
    co.location = b(cam_pos)
    co.rotation_mode = "QUATERNION"
    co.rotation_quaternion = (Vector(b(cam_at)) - Vector(b(cam_pos))).to_track_quat("-Z", "Y")
    bpy.context.scene.collection.objects.link(co)
    bpy.context.scene.camera = co
    q = co.rotation_quaternion
    ys, xs = np.mgrid[0:H, 0:W]
    t = math.tan(math.radians(fov) / 2)
    v = np.stack([(xs + 0.5 - W / 2) / (H / 2) * t, -(ys + 0.5 - H / 2) / (H / 2) * t, -np.ones((H, W))], -1)
    dirs = v @ np.array(q.to_matrix()).T
    dirs /= np.linalg.norm(dirs, axis=-1, keepdims=True)
    return np.array(cam_pos, dtype=np.float64), dirs[..., [0, 2, 1]]


def render(s, which):
    build(s, which)
    cam, dirs = camera()
    sc = bpy.context.scene
    vl = sc.view_layers[0]
    for attr in ("use_pass_normal", "use_pass_z", "use_pass_position", "use_pass_diffuse_color"):
        setattr(vl, attr, True)
    sc.render.image_settings.media_type = "MULTI_LAYER_IMAGE"
    sc.render.image_settings.file_format = "OPEN_EXR_MULTILAYER"
    sc.render.image_settings.color_depth = "32"
    path = os.path.join(out_dir, f"{TAG}-{which}.exr")
    sc.render.filepath = path
    if not (os.environ.get("REUSE") and os.path.exists(path)):
        bpy.ops.render.render(write_still=True)
    return cam, dirs, beams.read_layers(path)


def ground_light(pos, nrm, sky, lamps, new):
    """Headlamps.hlsl: the light the cars' headlamps throw on the ground ahead, per unit of albedo. Each lamp is (where,
    heading, height, share of its light); before, every share was 1, a slope could take half as much again and the sum
    had no limit."""
    sm = beams.sm
    e = np.zeros(pos.shape[:-1])
    for at, fwd, h, share in lamps:
        up = np.array([0.0, 1.0, 0.0]) - fwd * fwd[1]
        up /= np.linalg.norm(up)
        right = np.cross(up, fwd)
        v = pos - at
        z = v @ fwd
        x = v @ right
        y = v @ up
        d = np.maximum(np.linalg.norm(v, axis=-1), 1e-3)
        zc = np.maximum(z, 1e-3)
        along = (0.5 + 0.5 * sm(0.5, 9.0, z)) * (1 - sm(15.0, GROUND_REACH, z)) * (z > 0.2)
        u = np.abs(x) / np.minimum(2.0 + 0.35 * zc, 6.0)
        side = np.exp(-1.2 * u * u)
        cut = (1 - sm(0.0, 0.06, y / zc)) * sm(-h - 4.0, -h - 2.5, y)
        facing = np.clip(np.sum(nrm * -(v / d[..., None]), -1), 0, None) / np.maximum(h / d, 0.01)
        e += along * side * cut * np.minimum(facing, 1.0 if new else 1.5) * (share if new else 1.0)
    if new:
        e = (1 - np.exp(-GROUND_SATURATE * e)) / (1 - math.exp(-GROUND_SATURATE))
    # Only the ground's shaders take it: the road, its lines and the grass, not the cars.
    return np.where(sky | (pos[..., 1] > 0.05), 0.0, e)


def main():
    os.makedirs(out_dir, exist_ok=True)
    s = skyclock.at(hour)
    ht, ft = lin(HEAD), lin(beams.FLOOD)
    g = skyclock.grade(s)
    on = skyclock.sm(0, 0.12, s["night"])
    rads = {}
    cam, dirs, L = render(s, "scene")
    opaque = L["Combined"][..., :3]
    pos = L["Position"][..., [0, 2, 1]]
    nrm = L["Normal"][..., [0, 2, 1]]
    albedo = L["Diffuse Color"][..., :3]
    sky = (np.linalg.norm(pos - cam, axis=-1) > 15000) | (np.linalg.norm(pos, axis=-1) < 1e-6)
    dist = np.where(sky, 1e7, np.linalg.norm(pos - cam, axis=-1))
    up = (nrm[..., 1] > 0.7) & ~sky & (pos[..., 1] < 0.2)
    p = beams.pools(pos, up, beams.flat)[..., None] * (on if floods else 0.0)
    # NightLook.HeadlampDark, and HeadlampGround's share for a car whose beam falls in the floodlights' pools: the
    # straight's masts light all of it.
    dark = skyclock.sm(4.0, 16.0, -s["e"])
    share = GROUND_UNDER_FLOODS if floods else 1.0
    fwd = np.array([0.0, 0.0, 1.0])
    for which in ("before", "after"):
        new = which == "after"
        lamps = [(np.array([x, NOSE_Y, z + FRONT_Z]), fwd, NOSE_Y, share) for x, z, _ in MIDDLES]
        e = ground_light(pos, nrm, sky, lamps, new)[..., None]
        lit_ground = opaque + albedo * ht * (GROUND_LIGHT * on * (dark if new else 1.0)) * e
        rad = lit_ground * (1 + ft * beams.POOL_BRIGHTENING * p) + ft * beams.POOL_LIFT * p
        rads[which] = (rad, dirs, dist, cam)
    beam_list = beams.mast_beams(rads["before"][3], beams.flat) if floods else []
    for level, (scale, has_bloom, div, steps) in beams.LEVELS.items():
        for which in ("before", "after"):
            rad, dirs, dist, cam = rads[which]
            r = beams.resample(rad, scale)
            if div and beam_list:
                h, w_ = H // div, W // div
                d = dirs[div // 2::div, div // 2::div][:h, :w_]
                reach = np.minimum(dist[div // 2::div, div // 2::div][:h, :w_], beams.FOG_REACH)
                jit = beams.ign(h, w_, 7)
                lit_ = beams.mist_new(cam, d, reach, jit, beam_list)
                m = np.repeat(np.repeat(lit_, div, 0), div, 1)[:H, :W, None] * (ft * on * beams.BEAM_STRENGTH / (4 * math.pi))
                r = r + np.pad(m, ((0, H - m.shape[0]), (0, W - m.shape[1]), (0, 0)))
            bl = bloom(r, g["exposure"]) if has_bloom else None
            grade = g if has_bloom else dict(exposure=g["exposure"])
            save(os.path.join(out_dir, f"{TAG}-{level}-{which}.png"), frame(r, bloom=bl, **grade))
    print("rendered", TAG)


main()
