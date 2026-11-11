"""The F-256V8T's parts: openings, floor and diffuser, wings, wheels, suspension, mirrors,
steering wheel, the driver, badge and name. Every size is the car's own, from rows.json, kept
plain and smooth. Runs inside Blender's Python module, called by export.py."""
import json
import re
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from shapely import affinity
from shapely.affinity import scale as sscale, translate
from shapely.geometry import Point, Polygon, box
from shapely.ops import unary_union

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "common"))
import blend  # noqa: E402
import geom  # noqa: E402
import body  # noqa: E402

HERE = Path(__file__).resolve().parent
R = body.ROWS
W = R["wheels"]

MATERIALS = {
    # name: (rgb, spec, alpha, emit)
    "body": ((0.98, 0.58, 0.04), 0.75, 1.0, None),
    "carbon": ((0.045, 0.045, 0.05), 0.6, 1.0, None),
    "interior": ((0.09, 0.09, 0.1), 0.15, 1.0, None),
    "grille": ((0.03, 0.03, 0.035), 0.3, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.32, 0.33, 0.35), 0.9, 1.0, None),
    "chrome": ((0.86, 0.87, 0.89), 0.98, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "lamp_rain": ((0.5, 0.03, 0.04), 0.5, 1.0, (1.0, 0.05, 0.03)),
    "helmet": ((0.93, 0.93, 0.92), 0.8, 1.0, None),
    "visor": ((0.05, 0.06, 0.08), 0.95, 1.0, None),
    "suit": ((0.75, 0.06, 0.06), 0.3, 1.0, None),
    "badge_gold": ((0.79, 0.64, 0.15), 0.8, 1.0, None),
    "badge_red": ((0.78, 0.06, 0.18), 0.6, 1.0, None),
    "badge_black": ((0.08, 0.08, 0.08), 0.6, 1.0, None),
    "badge_cream": ((0.96, 0.93, 0.89), 0.6, 1.0, None),
    "badge_green": ((0.0, 0.54, 0.29), 0.6, 1.0, None),
}
LAMPS = {"Rain light": ["lamp_rain"]}
SHOW = {"Driver": ["driver"]}
GROUPS = {"frontbadge": "Front badge", "rearbadge": "Rear badge", "name": "Name"}


# ---------------------------------------------------------------- helpers
def soft(g, r):
    return g.buffer(-r, join_style=1).buffer(r, join_style=1)


def ring2(g, n=64):
    return body.even(np.array(g.exterior.coords)[:-1], n)


def extrude(name, g, axis, a0, a1, mat, n=64):
    """A flat shape pushed along an axis: g in (y, z) for x, (x, z) for y, (x, y) for z."""
    p = ring2(g, n)
    rings = []
    for a in (a0, a1):
        c = np.full(n, a)
        rings.append({"x": np.column_stack([c, p[:, 0], p[:, 1]]),
                      "y": np.column_stack([p[:, 0], c, p[:, 1]]),
                      "z": np.column_stack([p[:, 0], p[:, 1], c])}[axis])
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def bar(name, p0, p1, w, t, mat="carbon", n=24):
    """A flat strut from p0 to p1: w wide across, t thick, its wide face kept level."""
    p0, p1 = np.asarray(p0, float), np.asarray(p1, float)
    a = (p1 - p0) / np.linalg.norm(p1 - p0)
    side = np.cross(a, (0, 0, 1.0))
    if np.linalg.norm(side) < 0.2:
        side = np.cross(a, (1.0, 0, 0))
    side /= np.linalg.norm(side)
    up = np.cross(side, a)
    sec = ring2(soft(box(-w / 2, -t / 2, w / 2, t / 2), t * 0.45), n)
    rings = [p + np.outer(sec[:, 0], side) + np.outer(sec[:, 1], up) for p in (p0, p1)]
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def ellipsoid(name, c, r, mat, seg=48, rings=24):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    for v in bm.verts:
        v.co.x, v.co.y, v.co.z = c[0] + v.co.x * r[0], c[1] + v.co.y * r[1], c[2] + v.co.z * r[2]
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    return blend.adopt(name, me, mat)


def merge(obs, name):
    """Join objects into one, keeping each piece's material (no boolean)."""
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    bpy.ops.object.join()
    obs[0].name = name
    obs[0].data.name = name
    return obs[0]


def mirrored(fn):
    """Call fn(sgn, side) for both sides and return every object."""
    out = []
    for sgn, side in ((1, "l"), (-1, "r")):
        out += fn(sgn, side)
    return out


# ---------------------------------------------------------------- body
def shell():
    """The centre body with both sidepods joined on."""
    v, f = body.centre()
    sh = blend.make("body", v, f, "body")
    for sgn in (1, -1):
        v, f = body.pod(sgn)
        blend.boolean(sh, blend.make("pod", v, f, "body"), "UNION")
    return sh


def cockpit_cut():
    c = R["cockpit"]
    x0, x1, w, bw = c["x0"], c["x1"], c["w"], c["back_w"]
    plan = Polygon([(x0, -bw), (x0 + 0.42, -w), (x1 - 0.08, -w + 0.015), (x1, -0.12),
                    (x1, 0.12), (x1 - 0.08, w - 0.015), (x0 + 0.42, w), (x0, bw)])
    return extrude("cut_cockpit", soft(plan, 0.05), "z", 0.30, 1.2, "interior")


AIRBOX_FACE = (-0.22, 0.745)    # the airbox's upright face starts here (x) above this height (z)


def inlet_cuts():
    """Sidepod mouths and the airbox mouth above the driver's head."""
    obs = []
    for sgn in (1, -1):
        face = soft(box(0.30, 0.18, 0.62, 0.41), 0.06)
        if sgn < 0:
            face = sscale(face, -1, 1, origin=(0, 0))
        obs.append(extrude("cut_pod", face, "x", 0.25, 0.60, "grille"))
    obs.append(extrude("cut_hoop", box(-0.3, AIRBOX_FACE[1], 0.3, 1.3), "x", AIRBOX_FACE[0], 0.1, "body", 8))
    a = R["airbox"]
    face = soft(box(-a["w"] * 0.7, AIRBOX_FACE[1] + 0.025, a["w"] * 0.7, a["z1"] - 0.015), 0.04)
    obs.append(extrude("cut_airbox", face, "x", AIRBOX_FACE[0] - 0.25, AIRBOX_FACE[0] + 0.02, "grille"))
    return obs


def floor():
    """Flat floor plate, the diffuser ramp behind it and its three fences."""
    fl, d = R["floor"], R["diffuser"]
    half = Polygon(fl["plan"])
    plan = half.union(sscale(half, 1, -1, origin=(0, 0))).buffer(0)
    obs = [extrude("floor", soft(plan, 0.02), "z", fl["z0"], fl["z1"] + 0.01, "carbon", 128)]
    rings = []
    for t in np.linspace(0, 1, 12):
        x = d["x0"] + (d["x1"] - d["x0"]) * t
        zb = fl["z0"] + (d["z1"] - fl["z0"]) * t * t * (3 - 2 * t) ** 0.5
        sec = ring2(soft(box(-d["w"], zb, d["w"], zb + 0.02), 0.008), 48)
        rings.append(np.column_stack([np.full(48, x), sec]))
    v, f = geom.close_rings(rings)
    obs.append(blend.make("diffuser", v, f, "carbon"))
    fence = Polygon([(d["x0"] - 0.03, fl["z0"] + 0.01), (d["x1"], fl["z0"] + 0.01), (d["x1"], d["z1"] + 0.01)])
    for y in (-0.23, 0.0, 0.23):
        obs.append(extrude("fence", fence, "y", y - 0.004, y + 0.004, "carbon", 24))
    return merge(obs, "floor")


def rain_light():
    x = R["centre"][-1][0]
    face = soft(box(-0.035, 0.325, 0.035, 0.36), 0.012)
    return extrude("rain_light", face, "x", x - 0.012, x + 0.01, "lamp_rain", 32)


# ---------------------------------------------------------------- wings
def airfoil(le, te, z, t, lift, n=24):
    """Closed (x, z) section, leading edge le to trailing edge te, te raised by lift."""
    s = (1 - np.cos(np.linspace(0, np.pi, n))) / 2
    yt = 5 * t * (0.2969 * np.sqrt(s) - 0.126 * s - 0.3516 * s ** 2 + 0.2843 * s ** 3 - 0.1036 * s ** 4)
    zc = z + lift * s + 0.25 * t * np.sin(np.pi * s)
    x = le + (te - le) * s
    upper = np.column_stack([x, zc + yt])[::-1]
    lower = np.column_stack([x, zc - yt])[1:-1]
    return np.vstack([upper, lower])


def element(name, e, span, zfn, lift, mat="body"):
    sec = airfoil(e["le"], e["te"], 0.0, e["t"], lift)
    rings = []
    for y in np.linspace(-span, span, 41):
        rings.append(np.column_stack([sec[:, 0], np.full(len(sec), y), sec[:, 1] + zfn(y)]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def plate(name, p, y, mat="body"):
    g = soft(box(p["x1"], p["z0"], p["x0"], p["z1"]) if p["x1"] < p["x0"] else box(p["x0"], p["z0"], p["x1"], p["z1"]), 0.03)
    return extrude(name, g, "y", y - p["t"] / 2, y + p["t"] / 2, mat)


def front_wing():
    fw = R["front_wing"]
    span, mid = fw["span"], fw["mid"]
    obs = []
    lifts = (0.035, 0.07)
    for i, e in enumerate(fw["elements"]):
        def zfn(y, e=e):
            s = np.clip((abs(y) - mid) / 0.15, 0, 1)
            s = s * s * (3 - 2 * s)
            return e["z_mid"] + (e["z_tip"] - e["z_mid"]) * s
        obs.append(element(f"fw_{i}", e, span + 0.003, zfn, lifts[i]))
    for sgn in (1, -1):
        obs.append(plate("fw_plate", fw["plate"], sgn * (span + fw["plate"]["t"] / 2 - 0.002)))
        g = Polygon([(2.18, 0.18), (2.40, 0.20), (2.36, 0.30), (2.20, 0.30)])
        obs.append(extrude("fw_pylon", soft(g, 0.01), "y", sgn * 0.055 - 0.005, sgn * 0.055 + 0.005, "carbon", 32))
    return merge(obs, "front_wing")


def rear_wing():
    rw = R["rear_wing"]
    span = rw["span"]
    obs = [element(f"rw_{i}", e, span + 0.003, lambda y, e=e: e["z"], e["lift"]) for i, e in enumerate(rw["elements"])]
    b = rw["beam"]
    obs.append(element("rw_beam", b, b["span"], lambda y: b["z"], 0.02))
    for sgn in (1, -1):
        obs.append(plate("rw_plate", rw["plate"], sgn * (span + rw["plate"]["t"] / 2 - 0.002)))
    pil = Polygon([(-1.70, 0.36), (-1.80, 0.36), (-1.86, 0.73), (-1.74, 0.73)])
    obs.append(extrude("rw_pillar", soft(pil, 0.01), "y", -0.006, 0.006, "carbon", 32))
    return merge(obs, "rear_wing")


# ---------------------------------------------------------------- wheels and suspension
def wheels():
    """13 inch rims with twelve slim spokes in tall, smooth tyres. Wider at the back."""
    obs = []
    rr, wr = W["rim_r"], W["r"]
    for x, fb, yc, width in ((W["front_x"], "f", W["front_y"], W["front_w"]), (W["rear_x"], "r", W["rear_y"], W["rear_w"])):
        hw = width / 2
        tyre = soft(box(rr, -hw, wr, hw), 0.05).union(box(rr, -hw + 0.012, rr + 0.04, hw - 0.012))
        tp = body.even(np.array(tyre.exterior.coords)[:-1], 96)
        rim = [(rr - 0.001, -hw + 0.012), (rr - 0.001, hw - 0.01), (rr - 0.012, hw - 0.002),
               (rr - 0.022, hw - 0.006), (rr - 0.022, -hw + 0.012)]
        for sgn, lr in ((1, "l"), (-1, "r")):
            def place(v, s=sgn, x=x, yc=yc):
                v = np.asarray(v, float)
                return np.column_stack([x + v[:, 0], (yc + v[:, 1]) * s, wr + v[:, 2]])
            v, f = geom.lathe(tp, 96, axis="y")
            obs.append(blend.make(f"wheel_tyre_{fb}{lr}", place(v), f, "tyre"))
            v, f = geom.lathe(rim, 96, axis="y")
            parts = [blend.make("rim", place(v), f, "rim")]
            v, f = geom.disc((0, hw - 0.06, 0), (0, 1, 0), 0.06, 0.05, n=48)
            parts.append(blend.make("hub", place(v), f, "rim"))
            for k in range(12):
                a = 2 * np.pi * k / 12
                ca, sa = np.cos(a), np.sin(a)
                pts = []
                for r, w2, h0, h1 in ((0.05, 0.009, hw - 0.08, hw - 0.04), (rr - 0.015, 0.008, hw - 0.06, hw - 0.025)):
                    for dw in (-w2, w2):
                        for hh in (h0, h1):
                            pts.append((r * ca - dw * sa, hh, r * sa + dw * ca))
                f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
                parts.append(blend.make("spoke", place(np.array(pts)), f, "rim"))
            obs.append(blend.union_all(parts, f"wheel_rim_{fb}{lr}"))
    return obs


def suspension():
    """Per corner: an upright in the wheel, upper and lower wishbones, a pushrod."""
    obs = []
    wr = W["r"]
    for x, yc, w, fb in ((W["front_x"], W["front_y"], W["front_w"], "f"), (W["rear_x"], W["rear_y"], W["rear_w"], "r")):
        yo = yc - w / 2 + 0.05            # the outer ends sit inside the rim
        if fb == "f":
            inner = ((x - 0.16, x + 0.14), 0.07, 0.47, 0.30)       # x pair, y, upper z, lower z
            push = (x - 0.10, 0.06, 0.52)
        else:
            inner = ((x + 0.20, x - 0.08), 0.07, 0.42, 0.28)
            push = (x + 0.12, 0.06, 0.44)
        (xa, xb), yi, zu, zl = inner
        for sgn, lr in ((1, "l"), (-1, "r")):
            parts = [extrude("upright", soft(box(x - 0.035, wr - 0.13, x + 0.035, wr + 0.13), 0.02), "y",
                             sgn * (yo - 0.01) - 0.015, sgn * (yo - 0.01) + 0.015, "carbon", 32)]
            for zo, zi in ((wr + 0.11, zu), (wr - 0.12, zl)):
                for xi in (xa, xb):
                    parts.append(bar("arm", (x, sgn * yo, zo), (xi, sgn * yi, zi), 0.034, 0.012))
            parts.append(bar("push", (x, sgn * (yo - 0.02), wr - 0.11), (push[0], sgn * push[1], push[2]), 0.022, 0.022))
            obs.append(merge(parts, f"suspension_{fb}{lr}"))
    return obs


# ---------------------------------------------------------------- cockpit
def mirrors():
    m = R["mirror"]
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        yc = m["y"] * sgn
        face = soft(box(yc - 0.07, m["z"] - 0.032, yc + 0.07, m["z"] + 0.032), 0.028)
        house = extrude("mirror", face, "x", m["x"] - 0.03, m["x"] + 0.04, "body")
        glass = extrude("glass", soft(box(yc - 0.06, m["z"] - 0.023, yc + 0.06, m["z"] + 0.023), 0.02),
                        "x", m["x"] - 0.033, m["x"] - 0.029, "mirror_glass")
        stalk = bar("stalk", (m["x"] + 0.01, sgn * 0.22, m["z"] - 0.07), (m["x"] + 0.01, yc - 0.03 * sgn, m["z"] - 0.01), 0.03, 0.012)
        obs.append(merge([house, glass, stalk], f"mirror_{side}"))
    return obs


def steering():
    face = soft(box(-0.13, 0.50, 0.13, 0.60), 0.035)
    wheel = extrude("steering", face, "x", 0.40, 0.42, "carbon", 48)
    col = bar("column", (0.41, 0, 0.55), (0.56, 0, 0.50), 0.03, 0.03)
    return merge([wheel, col], "steering")


def driver():
    """Helmet with its visor, shoulders and arms, as one object so he can go in one step."""
    d = R["driver"]
    hx, _hy, hz = d["helmet"]
    r = d["r"]
    helmet = ellipsoid("helmet", (hx, 0, hz), (r * 1.08, r * 0.92, r), "helmet", 64, 32)
    vz0, vz1, a = hz - 0.03, hz + 0.035, np.radians(58)
    blend.split_by_planes(helmet, [((0, 0, vz0), (0, 0, 1)), ((0, 0, vz1), (0, 0, 1)),
                                   ((hx, 0, 0), (-np.sin(a), np.cos(a), 0)), ((hx, 0, 0), (np.sin(a), np.cos(a), 0))])
    blend.paint(helmet, "visor", lambda x, y, z: vz0 < z < vz1 and x - hx > abs(y) / np.tan(a))
    torso = ellipsoid("torso", (hx - 0.12, 0, hz - 0.17), (0.14, 0.2, 0.1), "suit")
    arms = [bar("arm", (hx - 0.08, sgn * 0.17, hz - 0.15), (0.40, sgn * 0.11, 0.55), 0.06, 0.06, "suit")
            for sgn in (1, -1)]
    return merge([helmet, torso] + arms, "driver")


# ---------------------------------------------------------------- badge and name
BRAND = HERE.parent.parent.parent / "src/renderer/assets/brands/feretti"


def _transform(g, t):
    """Apply an SVG transform attribute to a shapely shape."""
    for name, args in re.findall(r"(\w+)\(([^)]*)\)", t or "")[::-1]:
        a = [float(v) for v in re.split(r"[ ,]+", args.strip())]
        if name == "translate":
            g = translate(g, a[0], a[1] if len(a) > 1 else 0)
        elif name == "scale":
            g = sscale(g, a[0], a[1] if len(a) > 1 else a[0], origin=(0, 0))
        elif name == "rotate":
            g = affinity.rotate(g, a[0], origin=(a[1], a[2]) if len(a) > 2 else (0, 0))
    return g


def _path(d):
    """M, L, Q and Z path data as a polygon (curves sampled)."""
    pts, cur = [], (0.0, 0.0)
    for cmd, args in re.findall(r"([MLQZ])([^MLQZ]*)", d):
        v = [float(t) for t in re.findall(r"-?[\d.]+", args)]
        if cmd in "ML":
            for i in range(0, len(v), 2):
                cur = (v[i], v[i + 1]); pts.append(cur)
        elif cmd == "Q":
            for i in range(0, len(v), 4):
                c, e = np.array(v[i:i + 2]), np.array(v[i + 2:i + 4])
                for t in np.linspace(0, 1, 12)[1:]:
                    p = (1 - t) ** 2 * np.array(cur) + 2 * (1 - t) * t * c + t ** 2 * e
                    pts.append(tuple(p))
                cur = tuple(e)
    return Polygon(pts).buffer(0)


def _svg_shapes(name):
    """Every filled or stroked shape in a brand SVG, in drawing order: [(shape, fill)]. A stroke
    becomes its own ring, named by the stroke colour."""
    svg = re.sub(r"<(metadata|defs)\b.*?</\1>", "", (BRAND / name).read_text(), flags=re.S)
    out, stack = [], [""]
    for m in re.finditer(r"<(/?)(\w+)([^>]*?)(/?)>", svg):
        close, tag, attrs, selfclose = m.groups()
        a = dict(re.findall(r'([\w-]+)="([^"]*)"', attrs))
        if tag == "g":
            if close:
                stack.pop()
            elif not selfclose:
                stack.append(stack[-1] + " " + a.get("transform", ""))
            continue
        if close or tag == "svg":
            continue
        if tag == "ellipse":
            g = sscale(Point(0, 0).buffer(1, 32), float(a["rx"]), float(a["ry"]), origin=(0, 0))
            g = translate(g, float(a["cx"]), float(a["cy"]))
        elif tag == "circle":
            g = translate(Point(0, 0).buffer(float(a["r"]), 96), float(a["cx"]), float(a["cy"]))
        elif tag == "rect":
            x, y, w, h = (float(a[k]) for k in ("x", "y", "width", "height"))
            g = box(x, y, x + w, y + h)
            if "rx" in a:
                g = soft(g, float(a["rx"]) * 0.999)
        elif tag == "path":
            g = _path(a["d"])
        else:
            continue
        g = _transform(_transform(g, a.get("transform")), stack[-1])
        stroke = float(a.get("stroke-width", 0) or 0)
        if a.get("fill", "") != "none":
            out.append((g, a.get("fill", "")))
        if stroke and a.get("stroke"):
            if tag == "circle":
                ring = g.buffer(stroke / 2).difference(g.buffer(-stroke / 2))
            else:
                ring = g.boundary.buffer(stroke / 2)
            out.append((ring, a["stroke"]))
    return out


def _fit(shapes, ref):
    """Flip y up and scale so ref is 1 unit tall, centred."""
    b = ref.bounds
    k = 1 / (b[3] - b[1])
    return [sscale(translate(sscale(g, 1, -1, origin=(0, 0)), -(b[0] + b[2]) / 2, (b[1] + b[3]) / 2), k, k, origin=(0, 0))
            for g in shapes]


def _layered(shapes):
    """Painter's order to flat pieces: each shape keeps only what later shapes leave uncovered."""
    out = []
    for i, (g, f) in enumerate(shapes):
        over = unary_union([q for q, _f in shapes[i + 1:]]) if i + 1 < len(shapes) else None
        g = g.difference(over.buffer(0.6)) if over is not None else g
        if not g.is_empty and g.area > 0.5:
            out.append((g, f))
    return out


def badge_chrome():
    """The Feretti badge from feretti-badge-chrome.svg: laurels, ring, tower and bar in chrome,
    with the tower's door and window cut through."""
    sh = _svg_shapes("feretti-badge-chrome.svg")
    chrome = unary_union([g for g, f in sh if f.startswith("url")])
    dark = unary_union([g for g, f in sh if not f.startswith("url")])
    whole = chrome.difference(dark)
    return _fit([whole], unary_union([g for g, _f in sh]))[0].simplify(0.003)


COLOURS = {"#c9a227": "badge_gold", "#c8102e": "badge_red", "#141414": "badge_black",
           "#f4eee3": "badge_cream", "#00894b": "badge_green"}


def badge_colour():
    """The Feretti badge from feretti-badge-colour.svg: [(shape, material)], one flat piece per
    colour, none overlapping."""
    sh = _svg_shapes("feretti-badge-colour.svg")
    pieces = _layered(sh)
    ref = unary_union([g for g, _f in sh])
    shapes = _fit([g for g, _f in pieces], ref)
    by = {}
    for g, (_q, f) in zip(shapes, pieces):
        mat = COLOURS[f.lower()]
        by[mat] = by.get(mat, Polygon()).union(g)
    return [(g.simplify(0.003).buffer(-0.004), m) for m, g in by.items()]     # the colours stand just apart




def name_shape():
    gl = json.loads((HERE / "glyphs.json").read_text())
    g = unary_union([Polygon(p[0], p[1:]) for p in gl["pieces"]])
    b = g.bounds
    g = translate(g, -(b[0] + b[2]) / 2, -gl["cap"] / 2)
    g = sscale(g, 1 / gl["cap"], 1 / gl["cap"], origin=(0, 0))
    return g.buffer(0.02, 8).simplify(0.004)    # the brand's wordmark carries a heavier stroke than the bare font


def side_fr(sgn):
    return blend.frame((0, 1.0 * sgn, 0), (1, 0, 0), (0, 0, 1), (0, -sgn, 0))


def top_fr():
    return blend.frame((0, 0, 1.3), (1, 0, 0), (0, 1, 0), (0, 0, -1))


def _flat(ref, g, size, fr, a, b, up, name, d0, d1, mat, step=0.004):
    """Lay shape g (right, up, 1 unit = size) flat on the panel that fr's ray at (a, b) meets."""
    pf = blend.panel_frame(ref, fr, a, b, up)
    return blend.lay(ref, name, sscale(g, size, size, origin=(0, 0)), pf, d0, d1, mat, step)


BADGE_T = 0.006
NAME_T = 0.003


def badge(ref, style, size, fr, a, b, up, name):
    if style == "chrome":
        return _flat(ref, badge_chrome(), size, fr, a, b, up, name, 0.0012, BADGE_T, "chrome", 0.003)
    obs = []
    for i, (g, m) in enumerate(badge_colour()):
        obs += _flat(ref, g, size, fr, a, b, up, f"{name}_{i}", 0.0012, BADGE_T, m, 0.003)
    return obs


def options(ref):
    """Badge and name options, named opt_<group>_<n>_<i>. Each lies flat on its panel.
    Odd numbers are the chrome badge, even the colour badge, at the same spot."""
    nm = name_shape()
    up, back = (0, 0, 1), (-1, 0, 0)     # a nose badge reads from ahead: its top faces the driver
    rw = R["rear_wing"]["plate"]
    px = (rw["x0"] + rw["x1"]) / 2
    out = []

    def add(group, n, objs):
        for i, ob in enumerate(objs):
            ob.name = f"opt_{group}_{n}_{i}"
            out.append(ob)
    for k, style in ((0, "chrome"), (1, "colour")):
        # front: on top of the nose, or on each side of the tub ahead of the sidepods
        add("frontbadge", 1 + k, badge(ref, style, 0.07, top_fr(), 1.95, 0, back, "fb"))
        add("frontbadge", 3 + k, mirrored(lambda s, _l: badge(ref, style, 0.08, side_fr(s), 0.80, 0.42, up, "fb")))
        # rear: on the outside of each rear wing endplate, or on each side of the engine cover fin
        add("rearbadge", 1 + k, mirrored(lambda s, _l: badge(ref, style, 0.10, side_fr(s), px, 0.70, up, "rb")))
        add("rearbadge", 3 + k, mirrored(lambda s, _l: badge(ref, style, 0.07, side_fr(s), -0.70, 0.72, up, "rb")))
    add("name", 1, mirrored(lambda s, _l: _flat(ref, nm, 0.06, side_fr(s), -0.05, 0.33, up, "nm", 0.0012, NAME_T, "chrome")))
    add("name", 2, mirrored(lambda s, _l: _flat(ref, nm, 0.04, side_fr(s), px, 0.58, up, "nm", 0.0012, NAME_T, "chrome")))
    add("name", 3, mirrored(lambda s, _l: _flat(ref, nm, 0.045, side_fr(s), -0.98, 0.60, up, "nm", 0.0012, NAME_T, "chrome")))
    return out
