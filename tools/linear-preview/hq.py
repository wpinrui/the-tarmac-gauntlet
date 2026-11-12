"""Renders the Team HQ as the game draws it with the bake on, in Gamma and in Linear colour space.

Builds the room with tools/team-hq/bake/bake.py's own scene code, then lets Cycles work out only what the game works
out live: the sun's direct light and shadows. Everything else comes from the committed bake (the lightmap on the
building, the baked ambient on props and cars) and each surface's own colour and glow, written out as AOVs. The two
colour spaces then differ only in how they add those up:

- Gamma: the shader adds light to sRGB colours, and URP's post stack decodes the sum: lin(srgb(albedo) * light).
- Linear: albedo * light in linear light.

Usage: python3 hq.py scene.json out_dir shot [sun_gamma sun_linear lamp_scale]
"""
import json
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
scene_json, out_dir, shot = sys.argv[1], sys.argv[2], sys.argv[3]
SUN_G = float(sys.argv[4]) if len(sys.argv) > 4 else 1.25
SUN_L = float(sys.argv[5]) if len(sys.argv) > 5 else 1.25
LM_SCALE = float(sys.argv[6]) if len(sys.argv) > 6 else 1.0
# HqLighting.Exposure: the Linear HQ's grade, on top of the post stack's own.
EXPOSURE_L = float(sys.argv[7]) if len(sys.argv) > 7 else -0.3
sys.argv = [sys.argv[0], scene_json]
sys.path.insert(0, os.path.join(ROOT, "tools", "team-hq", "bake"))
import bake  # noqa: E402
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

sys.path.insert(0, HERE)
from post import lin, enc, frame, bloom, save  # noqa: E402

HQ = os.path.join(ROOT, "unity", "Assets", "TeamHq")
BAKE = json.load(open(os.path.join(HQ, "Resources", "Lighting", "bake.json")))
AMB = {k: np.array(v) for k, v in BAKE["ambient"].items()}


def aov(nt, name, socket=None, value=None):
    o = nt.nodes.new("ShaderNodeOutputAOV")
    o.aov_name = name
    if socket is not None: nt.links.new(socket, o.inputs["Color"])
    elif value is not None: o.inputs["Color"].default_value = (*value, 1)
    return o


def ambient_nodes(nt, colours):
    """Unity's trilight ambient by the surface's normal: sky above, equator level, ground below."""
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(geo.outputs["Normal"], sep.inputs[0])
    up = nt.nodes.new("ShaderNodeMath"); up.operation = "MAXIMUM"; up.inputs[1].default_value = 0
    nt.links.new(sep.outputs[2], up.inputs[0])
    dn = nt.nodes.new("ShaderNodeMath"); dn.operation = "MINIMUM"; dn.inputs[1].default_value = 0
    nt.links.new(sep.outputs[2], dn.inputs[0])
    neg = nt.nodes.new("ShaderNodeMath"); neg.operation = "MULTIPLY"; neg.inputs[1].default_value = -1
    nt.links.new(dn.outputs[0], neg.inputs[0])
    m1 = nt.nodes.new("ShaderNodeMix"); m1.data_type = "RGBA"
    nt.links.new(up.outputs[0], m1.inputs[0])
    m1.inputs[6].default_value = (*colours["equator"], 1); m1.inputs[7].default_value = (*colours["sky"], 1)
    m2 = nt.nodes.new("ShaderNodeMix"); m2.data_type = "RGBA"
    nt.links.new(neg.outputs[0], m2.inputs[0])
    nt.links.new(m1.outputs[2], m2.inputs[6]); m2.inputs[7].default_value = (*colours["ground"], 1)
    return m2.outputs[2]


def dress(lightmap):
    """Adds the AOVs to every material: albedo, the light each colour space shows, and glow."""
    amb_l = {k: v * LM_SCALE for k, v in AMB.items()}
    # Gamma: HqLightingView sets the baked ambient as sRGB colours, which Gamma uses as they are.
    amb_g = {k: enc(v) for k, v in AMB.items()}
    for ob in bpy.data.objects:
        if ob.type != "MESH" or not ob.data.materials: continue
        lit = "lm" in ob.data.uv_layers
        for m in ob.data.materials:
            if m is None or m.get("dressed"): continue
            m["dressed"] = True
            nt = m.node_tree
            glow = nt.nodes.get("glow")
            if glow is not None:
                c = np.array(glow.inputs["Color"].default_value[:3])
                aov(nt, "alb", value=(0, 0, 0)); aov(nt, "lmg", value=(0, 0, 0)); aov(nt, "lml", value=(0, 0, 0))
                aov(nt, "emg", value=enc(c)); aov(nt, "eml", value=c)
                glow.inputs["Strength"].default_value = 0
                continue
            bsdf = nt.nodes.get("bsdf") or next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
            if bsdf is None: continue
            base = bsdf.inputs["Base Color"]
            if base.links: aov(nt, "alb", socket=base.links[0].from_socket)
            else: aov(nt, "alb", value=base.default_value[:3])
            aov(nt, "emg", value=(0, 0, 0)); aov(nt, "eml", value=(0, 0, 0))
            if lit:
                t = nt.nodes.new("ShaderNodeTexImage"); t.image = lightmap; t.interpolation = "Linear"
                u = nt.nodes.new("ShaderNodeUVMap"); u.uv_map = "lm"
                nt.links.new(u.outputs[0], t.inputs[0])
                aov(nt, "lmg", socket=t.outputs["Color"])
                s = nt.nodes.new("ShaderNodeVectorMath"); s.operation = "SCALE"; s.inputs["Scale"].default_value = LM_SCALE
                nt.links.new(t.outputs["Color"], s.inputs[0])
                aov(nt, "lml", socket=s.outputs[0])
            else:
                aov(nt, "lmg", socket=ambient_nodes(nt, amb_g))
                aov(nt, "lml", socket=ambient_nodes(nt, amb_l))


def cars():
    """The demo cars in the bays, as HqScene puts them, with their MTL colours."""
    looks = ["LibertyCollier", "CheonmaCX24Si", None, "LibertyCollier"]
    for x, name in zip([-8, -3, 2, 7], looks):
        if name is None: continue
        path = os.path.join(ROOT, "unity", "Assets", "Art", "Vehicles", name, name + ".obj")
        before = set(bpy.data.objects)
        bpy.ops.wm.obj_import(filepath=path)
        for ob in set(bpy.data.objects) - before:
            ob.location = bake.b((x, 0, -1.5))
            for m in ob.data.materials:
                bsdf = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
                if bsdf is None: continue
                # Unity reads an MTL's Kd as an sRGB colour.
                c = np.array(bsdf.inputs["Base Color"].default_value[:3])
                bsdf.inputs["Base Color"].default_value = (*lin(c), 1)
                bsdf.inputs["Roughness"].default_value = 0.25
                if "glass" in m.name: bsdf.inputs["Alpha"].default_value = 0.45


def camera(sc, shot_id):
    presets = json.load(open(os.path.join(HQ, "Resources", "CameraPresets.json")))["presets"]
    p = next(q for q in presets if q["id"] == shot_id)
    pos = Vector(bake.b((p["pos"][0], p["pos"][1], -p["pos"][2])))
    target = Vector(bake.b((p["target"][0], p["target"][1], -p["target"][2])))
    cam = bpy.data.cameras.new("cam")
    cam.sensor_fit = "VERTICAL"
    cam.angle_y = math.radians(p["fov"])
    co = bpy.data.objects.new("cam", cam)
    co.location = pos
    co.rotation_mode = "QUATERNION"
    co.rotation_quaternion = (target - pos).to_track_quat("-Z", "Y")
    sc.collection.objects.link(co)
    sc.camera = co


def cull(cam_pos):
    """Unity draws front faces only: faces turned away from the camera stay in the scene for shadows but hide from it."""
    import bmesh
    for ob in [o for o in bpy.data.objects if o.type == "MESH"]:
        me = ob.data
        mw = ob.matrix_world
        back = [((mw @ p.center) - cam_pos).dot((mw.to_3x3() @ p.normal)) > 0 for p in me.polygons]
        if not any(back): continue
        hidden = ob.copy(); hidden.data = me.copy()
        bpy.context.scene.collection.objects.link(hidden)
        hidden.visible_camera = False
        for target, drop in ((ob, back), (hidden, [not b for b in back])):
            bm = bmesh.new(); bm.from_mesh(target.data); bm.faces.ensure_lookup_table()
            bmesh.ops.delete(bm, geom=[f for f, d in zip(bm.faces, drop) if d], context="FACES")
            bm.to_mesh(target.data); bm.free()


def read_layers(path):
    """Every pass in a multilayer EXR, by name, as an RGB array."""
    import OpenEXR
    out = {}
    with OpenEXR.File(path, separate_channels=True) as f:
        for part in f.parts:
            chans = {k.rpartition(".")[2]: v.pixels for k, v in part.channels.items()}
            name = next(iter(part.channels)).rpartition(".")[0].rpartition(".")[2]
            out[name] = np.stack([chans[c] for c in "RGB"], -1).astype(np.float64)
    return out


def main():
    os.makedirs(out_dir, exist_ok=True)
    sc = bake.reset()
    lit, glows, lamps = bake.building(sc)
    bake.props(sc)
    cars()
    sun, points = bake.lights(sc)
    for p in points: p.energy = 0
    sun.color = (1, 1, 1)
    sun.energy = math.pi  # a white sun of intensity 1: scaled per colour space below
    lm = bpy.data.images.load(os.path.join(HQ, "Resources", "Lighting", "hq_lightmap.exr"))
    lm.colorspace_settings.name = "Non-Color"
    dress(lm)
    vl = sc.view_layers[0]
    for name in ("alb", "lmg", "lml", "emg", "eml"):
        a = vl.aovs.add(); a.name = name; a.type = "COLOR"
    vl.use_pass_diffuse_direct = True
    vl.use_pass_glossy_direct = True
    vl.use_pass_glossy_color = True
    sc.cycles.max_bounces = 0
    sc.cycles.samples = int(os.environ.get("SAMPLES", "48"))
    sc.cycles.use_denoising = False
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    sc.render.film_transparent = False
    sc.render.image_settings.media_type = "MULTI_LAYER_IMAGE"
    sc.render.image_settings.file_format = "OPEN_EXR_MULTILAYER"
    sc.render.image_settings.color_depth = "32"
    camera(sc, shot)
    cull(sc.camera.location)
    path = os.path.join(out_dir, f"hq-{shot}.exr")
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    L = read_layers(path)
    def get(name): return L[name]
    alb, lmg, lml, emg, eml = (get(n) for n in ("alb", "lmg", "lml", "emg", "eml"))
    sun_irr = get("Diffuse Direct")
    gloss = get("Glossy Direct") * get("Glossy Color")
    colour = np.array([1, 0.96, 0.89])
    # Gamma: everything added up on sRGB numbers, then decoded by the post stack.
    v_g = enc(alb) * (lmg + sun_irr * SUN_G * colour) + emg + enc(gloss * SUN_G)
    r_g = lin(v_g)
    r_l = alb * (lml + sun_irr * SUN_L * lin(colour)) + eml + gloss * SUN_L * lin(colour)
    np.save(os.path.join(out_dir, f"hq-{shot}-gamma.npy"), r_g.astype(np.float32))
    np.save(os.path.join(out_dir, f"hq-{shot}-linear.npy"), r_l.astype(np.float32))
    for tag, r, ev in (("gamma", r_g, 0.45), ("linear", r_l, 0.45 + EXPOSURE_L)):
        save(os.path.join(out_dir, f"hq-{shot}-{tag}.png"), frame(r, exposure=ev, bloom=bloom(r, ev)))
    print("rendered", shot)


main()
