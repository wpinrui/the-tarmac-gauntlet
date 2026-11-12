"""Bakes the Team HQ's lighting with Blender's Cycles, run as a Python module (pip install bpy).

Reads the building as tools/team-hq/geometry-check writes it (`run.sh bake scene.json`) and writes into
unity/Assets/TeamHq/Resources/Lighting:

- hq_lightmap.exr: the light on every lit surface, on the layout HqLightmapLayout gives it. The sun stays a live light
  in the game, so only its bounce is baked; the lamps and the daylight through the windows are baked whole.
- probe_<name>.exr: what each reflection probe sees, a latitude-longitude image in Unity's layout.
- bake.json: the building signature the bake fits, and the room's ambient light for whatever moves.

Usage: python3 bake.py scene.json [--quick]
"""
import json
import math
import os
import re
import struct
import sys

import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
HQ = os.path.join(ROOT, "unity", "Assets", "TeamHq")
RES = os.path.join(HQ, "Resources")
OUT = os.path.join(RES, "Lighting")
QUICK = "--quick" in sys.argv
SCENE = json.load(open(sys.argv[1]))

# Cycles units against Unity's: a white surface shows a Unity light's intensity where Cycles needs pi times that for a
# sun and 4 pi squared for a point (checked by baking one plane under each).
SUN_SCALE = math.pi
# HqLightingView's flat ambient sky colour, the fill the live room is lit by.
FLAT_SKY = [0.62, 0.65, 0.68]
POINT_SCALE = 4 * math.pi ** 2


def to_linear(c):
    c = np.asarray(c, dtype=float)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def b(v):
    """Unity's frame (left-handed, Y up) to Blender's (right-handed, Z up): the same room, axes renamed."""
    return (v[0], v[2], v[1])


def hexcol(h):
    v = int(h, 16)
    return [((v >> 16) & 255) / 255, ((v >> 8) & 255) / 255, (v & 255) / 255]


def surface_looks():
    """Each part's colour, glow and finish, read from HqMaterials.cs so the bake never drifts from the game."""
    src = open(os.path.join(HQ, "Scripts", "HqMaterials.cs")).read()
    looks = {}
    for key, kind, args in re.findall(r'case "(\w+)": return (Make|Emissive)\(key, ([^;]*)\);', src):
        hexes = re.findall(r"Hex\(0x([0-9a-fA-F]{6})\)(?:\s*\*\s*([\d.]+)f)?", args)
        nums = [float(n) for n in re.findall(r"(?<![\w.])(\d*\.?\d+)f", re.sub(r"Hex\(0x[0-9a-fA-F]+\)\s*(\*\s*[\d.]+f)?", "", args))]
        look = {"albedo": to_linear(hexcol(hexes[0][0])) if hexes else np.ones(3), "smooth": 0.5, "metal": 0.0, "glow": None}
        if kind == "Make":
            if nums: look["smooth"] = nums[0]
            if len(nums) > 1: look["metal"] = nums[1]
        else:
            g = hexes[1] if len(hexes) > 1 else ("ffffff", None)
            # Emissive(key, colour, glow, smoothness): Color.white has no Hex.
            look["glow"] = to_linear(np.array(hexcol(g[0])) * float(g[1] or 1))
            if "Color.white" in args: look["albedo"] = np.ones(3)
            look["smooth"] = nums[-1] if len(nums) > (1 if g[1] else 0) else 0.3
        looks[key] = look
    return looks


LOOKS = surface_looks()


def node_material(name, albedo=None, tex=None, uv="uv", glow=None, smooth=0.5, metal=0.0, kind="solid", alpha_tex=False):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    if kind == "glow":
        e = nt.nodes.new("ShaderNodeEmission")
        e.inputs["Color"].default_value = (*glow, 1)
        e.name = "glow"
        nt.links.new(e.outputs[0], out.inputs[0])
        return m
    if kind == "glass":
        t = nt.nodes.new("ShaderNodeBsdfTransparent")
        g = nt.nodes.new("ShaderNodeBsdfGlossy")
        g.inputs["Roughness"].default_value = 0.05
        mix = nt.nodes.new("ShaderNodeMixShader")
        mix.inputs[0].default_value = 0.12
        nt.links.new(t.outputs[0], mix.inputs[1])
        nt.links.new(g.outputs[0], mix.inputs[2])
        nt.links.new(mix.outputs[0], out.inputs[0])
        return m
    p = nt.nodes.new("ShaderNodeBsdfPrincipled")
    p.name = "bsdf"
    p.inputs["Roughness"].default_value = max(0.05, 1 - smooth)
    p.inputs["Metallic"].default_value = metal
    colour = (*(albedo if albedo is not None else np.ones(3)), 1)
    p.inputs["Base Color"].default_value = colour
    if tex:
        i = nt.nodes.new("ShaderNodeTexImage")
        i.image = bpy.data.images.load(tex, check_existing=True)
        u = nt.nodes.new("ShaderNodeUVMap")
        u.uv_map = uv
        nt.links.new(u.outputs[0], i.inputs[0])
        mul = nt.nodes.new("ShaderNodeMix")
        mul.data_type = "RGBA"
        mul.blend_type = "MULTIPLY"
        mul.inputs[0].default_value = 1
        nt.links.new(i.outputs["Color"], mul.inputs[6])
        mul.inputs[7].default_value = colour
        nt.links.new(mul.outputs[2], p.inputs["Base Color"])
        if alpha_tex: nt.links.new(i.outputs["Alpha"], p.inputs["Alpha"])
    if glow is not None:
        p.inputs["Emission Color"].default_value = (*glow, 1)
        p.inputs["Emission Strength"].default_value = 1
    nt.links.new(p.outputs[0], out.inputs[0])
    return m


def mesh_object(name, verts, tris, normals=None, uvs=None):
    me = bpy.data.meshes.new(name)
    # Unity's front faces wind clockwise; with the axes renamed they would face away in Blender, so reverse them.
    me.from_pydata([b(v) for v in verts], [], [tuple(t[::-1]) for t in tris])
    for layer, data in (uvs or {}).items():
        uv = me.uv_layers.new(name=layer)
        loops = np.array([li for poly in me.polygons for li in poly.loop_indices])
        vidx = np.array([me.loops[li].vertex_index for li in loops])
        flat = np.zeros(len(me.loops) * 2)
        flat.reshape(-1, 2)[loops] = np.asarray(data)[vidx]
        uv.data.foreach_set("uv", flat)
    if normals is not None:
        me.normals_split_custom_set_from_vertices([b(n) for n in normals])
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    world = bpy.data.worlds.new("outside")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[1].default_value = 0
    sc.world = world
    sc.cycles.max_bounces = 6
    sc.cycles.diffuse_bounces = 4
    sc.cycles.glossy_bounces = 2
    sc.cycles.transparent_max_bounces = 8
    return sc


def building(sc):
    """The building's parts: lit ones get the lightmap image to bake into."""
    lit, glows, lamps = [], [], []
    for part in SCENE["parts"]:
        key = part["key"]
        if key == "blob": continue
        v = np.array(part["v"]).reshape(-1, 3)
        t = np.array(part["t"]).reshape(-1, 3)
        uv = np.array(part["uv"]).reshape(-1, 2)
        uvs = {"uv": uv}
        if part["uv2"]: uvs["lm"] = np.array(part["uv2"]).reshape(-1, 2)
        ob = mesh_object(key, v, t, None, uvs)
        look = LOOKS.get(key, {"albedo": np.ones(3) * 0.5, "smooth": 0.5, "metal": 0, "glow": None})
        tex = os.path.join(RES, "Textures", key + "_c.jpg")
        if key.startswith("photo"): tex = os.path.join(RES, "Textures", key + ".jpg")
        if key == "glass":
            ob.data.materials.append(node_material(key, kind="glass"))
            ob.visible_shadow = False
        elif key in ("window", "screen", "lamp"):
            ob.data.materials.append(node_material(key, look["albedo"], glow=look["glow"], kind="glow"))
            ob.visible_shadow = False
            (lamps if key == "lamp" else glows).append(ob)
        else:
            ob.data.materials.append(node_material(key, look["albedo"], tex if os.path.exists(tex) else None,
                                                   smooth=look["smooth"], metal=look["metal"], glow=None))
        if part["uv2"]: lit.append(ob)
    return lit, glows, lamps


def props(sc):
    pdir = os.path.join(RES, "Props")
    cache = {}
    for pid, x, y, z, yaw, scale in SCENE["props"]:
        if pid not in cache:
            info = json.load(open(os.path.join(pdir, pid + ".json")))
            data = open(os.path.join(pdir, pid + "_mesh.bytes"), "rb").read()
            lo = np.array(info["min"]); span = np.maximum(np.array(info["max"]) - lo, 1e-6)
            pieces = []
            for k, p in enumerate(info["parts"]):
                n = p["n"]
                q = np.frombuffer(data, dtype="<i2", count=n * 3, offset=p["pos"]).reshape(-1, 3).astype(float)
                pos = lo + (q + 32768) / 65535 * span
                pos[:, 2] *= -1
                uv = np.frombuffer(data, dtype="<f4", count=n * 2, offset=p["uv"]).reshape(-1, 2).astype(float).copy()
                uv[:, 1] = 1 - uv[:, 1]
                idx = np.frombuffer(data, dtype="<u4" if p["wide"] else "<u2", count=p["i"], offset=p["idx"]).astype(int).reshape(-1, 3)
                idx = idx[:, [0, 2, 1]]
                mat = info["mats"][p["mat"]]
                col = mat["col"]
                tex = os.path.join(pdir, col) if col else None
                colour = to_linear(mat["color"][:3])
                pieces.append((pos, idx, uv, node_material(f"{pid}/{k}", colour, tex, alpha_tex=mat["cutout"] or mat["blend"])))
            cache[pid] = pieces
        a = math.radians(yaw)
        for k, (pos, idx, uv, m) in enumerate(cache[pid]):
            p = pos * scale
            w = np.stack([x + p[:, 0] * math.cos(a) + p[:, 2] * math.sin(a), y + p[:, 1], z - p[:, 0] * math.sin(a) + p[:, 2] * math.cos(a)], 1)
            ob = mesh_object(f"{pid}.{k}", w, idx, None, {"uv": uv})
            ob.data.materials.append(m)


def lights(sc):
    d = SCENE["sun"]
    sun = bpy.data.lights.new("sun", "SUN")
    sun.color = tuple(to_linear(d[3:6]))
    sun.energy = d[6] * SUN_SCALE
    sun.angle = math.radians(1.0)
    so = bpy.data.objects.new("sun", sun)
    so.rotation_mode = "QUATERNION"
    so.rotation_quaternion = Vector(b(d[0:3])).to_track_quat("-Z", "Y")
    sc.collection.objects.link(so)
    points = []
    for i, (x, y, z, intensity, r, g, bl) in enumerate(SCENE["lights"]):
        l = bpy.data.lights.new(f"lamp{i}", "POINT")
        l.color = tuple(to_linear([r, g, bl]))
        l.energy = intensity * POINT_SCALE
        l.shadow_soft_size = 0.12
        o = bpy.data.objects.new(f"lamp{i}", l)
        o.location = b((x, y, z))
        sc.collection.objects.link(o)
        points.append(l)
    return sun, points


def glow_strength(obs, s):
    for ob in obs:
        ob.active_material.node_tree.nodes["glow"].inputs["Strength"].default_value = s


def bake_lightmap(sc, lit, glows, lamps, sun, points):
    W, H = SCENE["width"], SCENE["height"]
    img = bpy.data.images.new("lightmap", W, H, float_buffer=True)
    for ob in lit:
        ob.data.uv_layers.active = ob.data.uv_layers["lm"]
        nt = ob.active_material.node_tree
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = img
        nt.nodes.active = n
        ob.select_set(True)
    bpy.context.view_layer.objects.active = lit[0]
    # The lamp discs only show the lamps: their light is the point lights'.
    for ob in lamps: ob.visible_diffuse = ob.visible_glossy = False
    bk = sc.render.bake
    bk.use_pass_color = False
    bk.margin = 2
    bk.margin_type = "EXTEND"
    sc.cycles.samples = 16 if QUICK else 512

    def run(direct):
        bk.use_pass_direct = direct
        bk.use_pass_indirect = True
        bpy.ops.object.bake(type="DIFFUSE", use_clear=True)
        return np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)[:, :, :3].copy()

    # The sun is live in the game: bake only its bounce.
    keep = sun.energy
    for p in points: p.energy, p["e"] = 0, p.energy
    glow_strength(glows, 0)
    bounce = run(False)
    # Then the lamps, whole.
    sun.energy = 0
    for p in points: p.energy = p["e"]
    lamp = run(True)
    # Then the daylight through the windows, whole.
    for p in points: p.energy = 0
    glow_strength(glows, 1)
    day = run(True)
    sun.energy = keep
    for p in points: p.energy = p["e"]
    for ob in lit: ob.select_set(False)

    # The live room is filled by a flat ambient light. The daylight takes its place, so it is scaled to leave the
    # floor as bright on average as the flat fill and the live lamps leave it.
    floor = next(np.array(p["uv2"]).reshape(-1, 2) for p in SCENE["parts"] if p["key"] == "floor")
    (u0, v0), (u1, v1) = floor.min(0), floor.max(0)
    area = (slice(int(v0 * H) + 1, int(v1 * H) - 1), slice(int(u0 * W) + 1, int(u1 * W) - 1))
    fill = to_linear(FLAT_SKY).mean()
    daylight = (fill + lamp[area].mean() - (bounce + lamp)[area].mean()) / max(day[area].mean(), 1e-6)
    daylight = max(daylight, 1.0)
    print(f"daylight x{daylight:.2f}")
    glow_strength(glows, daylight)
    return bounce + lamp + day * daylight


def render_probe(sc, probe, lamps):
    for ob in lamps: ob.visible_diffuse = ob.visible_glossy = True
    cam = bpy.data.cameras.new("probe")
    cam.type = "PANO"
    cam.panorama_type = "EQUIRECTANGULAR"
    co = bpy.data.objects.new("probe", cam)
    co.location = b(probe["capture"])
    # Looking along Unity's +Z.
    co.rotation_euler = (math.pi / 2, 0, 0)
    sc.collection.objects.link(co)
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = (256, 128) if QUICK else (1024, 512)
    sc.render.resolution_percentage = 100
    sc.cycles.samples = 16 if QUICK else 256
    sc.cycles.use_denoising = not QUICK
    sc.view_settings.view_transform = "Standard"
    sc.render.image_settings.file_format = "OPEN_EXR"
    sc.render.image_settings.color_depth = "16"
    path = os.path.join(OUT, f"probe_{probe['name']}.exr")
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    px = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)
    bpy.data.images.remove(img)
    # Blender puts the camera's forward at the middle; Unity puts +X there, with +Z a quarter to the left.
    px = np.roll(px, -px.shape[1] // 4, axis=1)
    save_exr(path, px)
    bpy.data.objects.remove(co)
    return px


def save_exr(path, px):
    h, w = px.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, float_buffer=True, alpha=True)
    img.pixels.foreach_set(np.ascontiguousarray(px, dtype=np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "OPEN_EXR"
    sc = bpy.context.scene
    sc.render.image_settings.file_format = "OPEN_EXR"
    sc.render.image_settings.color_depth = "16"
    sc.render.image_settings.exr_codec = "ZIP"
    img.save_render(path, scene=sc)
    bpy.data.images.remove(img)


def ambient(px):
    """Irradiance over pi from a probe image for normals up, level and down: what a white surface there shows."""
    h, w = px.shape[:2]
    theta = (np.arange(h)[::-1] + 0.5) / h * math.pi  # row 0 is the bottom: theta from the top
    phi = (np.arange(w) + 0.5) / w * 2 * math.pi
    st, ct = np.sin(theta)[:, None], np.cos(theta)[:, None]
    dirs = np.stack([st * np.cos(phi), np.broadcast_to(ct, (h, w)), st * np.sin(phi)], -1)
    dw = (math.pi / h) * (2 * math.pi / w) * st
    rgb = px[:, :, :3]

    def irr(n):
        c = np.clip((dirs * n).sum(-1), 0, None) * dw
        return (rgb * c[:, :, None]).sum((0, 1)) / math.pi

    level = sum(irr(np.array(n)) for n in ([1, 0, 0], [-1, 0, 0], [0, 0, 1], [0, 0, -1])) / 4
    return {"sky": irr(np.array([0, 1, 0])).tolist(), "equator": level.tolist(), "ground": irr(np.array([0, -1, 0])).tolist()}


def main():
    os.makedirs(OUT, exist_ok=True)
    sc = reset()
    lit, glows, lamps = building(sc)
    props(sc)
    sun, points = lights(sc)
    lm = bake_lightmap(sc, lit, glows, lamps, sun, points)
    save_exr(os.path.join(OUT, "hq_lightmap.exr"), np.concatenate([lm, np.ones(lm.shape[:2] + (1,), np.float32)], -1))
    out = {"signature": SCENE["signature"], "ambient": None}
    for probe in SCENE["probes"]:
        px = render_probe(sc, probe, lamps)
        if probe["name"] == "garage": out["ambient"] = ambient(px)
    json.dump(out, open(os.path.join(OUT, "bake.json"), "w"), indent=1)
    print("baked", SCENE["signature"])


if __name__ == "__main__":
    main()
