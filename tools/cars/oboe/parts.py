"""The Oboe's parts: lamps, grille, glass, trim, mirrors, interior, wheels, badge and name.
Every shape and size here is the Oboe's own, from rows.json and Ivan's references.
Runs inside Blender's Python module, called by export.py."""
import json
import sys
from pathlib import Path

import numpy as np
from shapely.affinity import scale as sscale, translate
from shapely.geometry import LineString, Point, Polygon, box
from shapely.ops import unary_union

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "common"))
import blend  # noqa: E402
import geom  # noqa: E402
import body  # noqa: E402

HERE = Path(__file__).resolve().parent
R = body.ROWS
XS, CV = body.curves(R["stations"])
NOSE, TAIL = float(XS[-1]), float(XS[0])
W = R["wheel"]

MATERIALS = {
    # name: (rgb, spec, alpha, emit)
    "body": ((0.62, 0.64, 0.67), 0.75, 1.0, None),
    "trim": ((0.025, 0.025, 0.03), 0.65, 1.0, None),
    "grille": ((0.045, 0.045, 0.05), 0.25, 1.0, None),
    "glass": ((0.07, 0.09, 0.11), 0.92, 0.45, None),
    "head_glass": ((0.92, 0.94, 0.96), 0.95, 0.22, None),
    "tail_glass": ((0.55, 0.03, 0.04), 0.92, 0.55, None),
    "housing": ((0.05, 0.05, 0.055), 0.5, 1.0, None),
    "reflector": ((0.78, 0.79, 0.81), 0.97, 1.0, None),
    "lamp_head": ((0.95, 0.95, 0.92), 0.6, 1.0, (1.0, 0.97, 0.9)),
    "lamp_tail": ((0.5, 0.03, 0.04), 0.5, 1.0, (1.0, 0.06, 0.04)),
    "lamp_brake": ((0.6, 0.04, 0.05), 0.5, 1.0, (1.0, 0.04, 0.03)),
    "chrome": ((0.86, 0.87, 0.89), 0.98, 1.0, None),
    "name": ((0.86, 0.87, 0.89), 0.98, 1.0, None),
    "interior": ((0.09, 0.09, 0.1), 0.15, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.6, 0.62, 0.64), 0.9, 1.0, None),
    "hub": ((0.22, 0.22, 0.24), 0.6, 1.0, None),
}
LAMPS = {"Headlights": ["lamp_head"], "Tail lights": ["lamp_tail"], "Brake lights": ["lamp_brake"]}
GROUPS = {"frontbadge": "Front badge", "rearbadge": "Rear badge", "name": "Name"}


# ---------------------------------------------------------------- frames
def front_fr():
    return blend.frame((NOSE, 0, 0), (0, 1, 0), (0, 0, 1), (-1, 0, 0))


def rear_fr():
    return blend.frame((TAIL, 0, 0), (0, 1, 0), (0, 0, 1), (1, 0, 0))


def side_fr(sgn):
    return blend.frame((0, 0.7 * sgn, 0), (1, 0, 0), (0, 0, 1), (0, -sgn, 0))


def top_fr():
    return blend.frame((0, 0, 1.3), (1, 0, 0), (0, 1, 0), (0, 0, -1))


def poly(pts):
    return Polygon(pts).buffer(0)


def flip(g):
    """Mirror a 2D shape about its first axis (y for front and rear frames)."""
    return sscale(g, -1, 1, origin=(0, 0))


class Wrap:
    """A frame bending round a front or rear corner of the body. In plan it is a rounded
    rectangle: rays run straight in across the face, fan out round a corner of radius rc
    centred at (xc, yc), then run straight in along the side. 2D (u, z): u runs from the
    centre line out round the corner and along the side."""

    def __init__(self, end, sgn, xc, yc, rc, tilt=0.0):
        self.f = 1 if end == "front" else -1
        self.tilt = tilt
        self.sgn, self.xc, self.yc, self.rc = sgn, xc, yc, rc
        self.u1 = yc + rc * np.pi / 2

    def __call__(self, u, z):
        f, s = self.f, self.sgn
        if u <= self.yc:
            o, d, w = (self.xc + f * (self.rc + 0.5), u), (-f, 0.0), 0.0
        elif u >= self.u1:
            x = self.xc - f * (u - self.u1)
            o, d, w = (x, self.yc + self.rc + 0.5), (0.0, -1.0), 1.0
        else:
            t = (u - self.yc) / self.rc
            c, n = np.cos(t), np.sin(t)
            o, d, w = (self.xc + f * c * (self.rc + 0.5), self.yc + n * (self.rc + 0.5)), (-f * c, -n), n
        # round the corner and along the side, rays dip by tilt so the top edge of a lamp is
        # not met at a skim; the start is raised so the ray still reaches height z at the
        # rounded rectangle
        k = np.tan(self.tilt) * w
        return np.array([o[0], o[1] * s, z + 0.5 * k]), np.array([d[0], d[1] * s, -k])

    def to_uv(self, p):
        """The (u, z) whose ray passes through the 3D point p."""
        x, y, z = float(p[0]), abs(float(p[1])), float(p[2])
        f, k = self.f, np.tan(self.tilt)
        dx = f * (x - self.xc)
        if y <= self.yc:
            return y, z
        if dx <= 0:
            u = self.u1 - dx
            run = self.yc + self.rc + 0.5 - y
            return u, z - k * 0.5 + k * run
        t = np.arctan2(y - self.yc, dx)
        u = self.yc + self.rc * t
        run = self.rc + 0.5 - np.hypot(dx, y - self.yc)
        w = np.sin(t)
        return u, z - k * w * 0.5 + k * w * run

    def outline(self, loops):
        """One outline in (u, z) from the lamp's 3D edge loops."""
        u = unary_union([poly([self.to_uv(p) for p in loop]) for loop in loops])
        if u.geom_type != "Polygon":
            u = u.buffer(0.02, join_style=1).buffer(-0.02, join_style=1)
        if u.geom_type != "Polygon":
            u = max(u.geoms, key=lambda g: g.area)
        return u.buffer(-0.002, join_style=1).buffer(0.002, join_style=1)


HEAD_WRAP = dict(end="front", xc=NOSE - 0.36, yc=0.55, rc=0.3, tilt=0.5)
TAIL_WRAP = dict(end="rear", xc=TAIL + 0.36, yc=0.55, rc=0.3, tilt=0.3)


# ---------------------------------------------------------------- lamps and front
def lamp_parts(ref, cutters, out, sgn, which):
    side = "l" if sgn > 0 else "r"
    if which == "head":
        wr = Wrap(sgn=sgn, **HEAD_WRAP)
        shp = wr.outline(R["head"])
        cutters += blend.lay(ref, f"cut_head_{side}", shp, wr, -0.0145, 0.006, "housing", 0.01, along_ray=True)
        out += blend.lay(ref, f"head_housing_{side}", shp.buffer(-0.0015), wr, -0.0135, -0.0105, "housing", 0.01)
        for i, (x, y, z, r) in enumerate(R["head_lenses"]):
            c = Point(*wr.to_uv((x, y, z)))
            keep = shp.buffer(-0.004)
            out += blend.lay(ref, f"lamp_head_{side}{i}", c.buffer(r, 48).intersection(keep), wr, -0.0099, -0.006, "lamp_head", 0.004)
            out += blend.lay(ref, f"head_reflector_{side}{i}", c.buffer(r + 0.011, 48).difference(c.buffer(r + 0.001, 48)).intersection(keep),
                             wr, -0.0099, -0.0082, "reflector", 0.004)
        out += blend.lay(ref, f"head_glass_{side}", shp.buffer(-0.0015), wr, -0.004, -0.0015, "head_glass", 0.01)
    else:
        wr = Wrap(sgn=sgn, **TAIL_WRAP)
        shp = wr.outline(R["tail"])
        cutters += blend.lay(ref, f"cut_tail_{side}", shp, wr, -0.0145, 0.006, "housing", 0.01, along_ray=True)
        out += blend.lay(ref, f"tail_housing_{side}", shp.buffer(-0.0015), wr, -0.0135, -0.0105, "housing", 0.01)
        core = shp.buffer(-0.014)
        zmid = (shp.bounds[1] + shp.bounds[3]) / 2
        out += blend.lay(ref, f"lamp_tail_{side}", core.intersection(box(-5, zmid + 0.005, 5, 5)), wr,
                         -0.0099, -0.007, "lamp_tail", 0.005)
        out += blend.lay(ref, f"lamp_brake_{side}", core.intersection(box(-5, -5, 5, zmid - 0.005)), wr,
                         -0.0099, -0.007, "lamp_brake", 0.005)
        out += blend.lay(ref, f"tail_glass_{side}", shp.buffer(-0.0015), wr, -0.004, -0.0015, "tail_glass", 0.01)


def front_parts(ref, cutters, out):
    fr = front_fr()
    shield = poly(R["shield"])
    out += blend.lay(ref, "shield", shield, fr, -0.001, 0.004, "grille", 0.012)
    out += blend.lay(ref, "shield_bar", poly(R["shield_bar"]), fr, 0.0044, 0.0074, "trim", 0.008)
    out += blend.lay(ref, "chin", poly(R["chin"]), fr, -0.001, 0.004, "trim", 0.008)
    fy, fz, fr_r = R["fog"]
    for sgn, side in ((1, "l"), (-1, "r")):
        g = poly(R["intake"]).convex_hull.buffer(-0.01, join_style=1).buffer(0.01, join_style=1)
        g = g if sgn < 0 else flip(g)
        cutters += blend.lay(ref, f"cut_intake_{side}", g, fr, -0.025, 0.006, "grille", 0.012, along_ray=True)
        out += blend.lay(ref, f"intake_{side}", g.buffer(-0.0015), fr, -0.0225, -0.0195, "grille", 0.012)
        c = Point(fy if sgn < 0 else -fy, fz)
        out += blend.lay(ref, f"fog_ring_{side}", c.buffer(fr_r + 0.008, 40).difference(c.buffer(fr_r + 0.0005, 40)),
                         fr, -0.0189, -0.014, "reflector", 0.004)
        out += blend.lay(ref, f"lamp_head_fog_{side}", c.buffer(fr_r, 40), fr, -0.0189, -0.012, "lamp_head", 0.004)


# ---------------------------------------------------------------- glass and trim
def dlo_shapes():
    dlo = poly(R["dlo"])
    b0, b1 = sorted(R["b_pillar"])
    glass = dlo.buffer(-0.018, join_style=1).difference(box(b0, -1, b1, 3))
    trim = dlo.difference(glass)
    return dlo, glass, trim


def screen_shapes():
    outline = poly(R["screen"]["outline"]).buffer(-0.02, join_style=1).buffer(0.02, join_style=1)
    glass = outline.buffer(-0.026, join_style=1)
    return outline, glass, outline.difference(glass)


def rear_shapes():
    rg = poly(R["rear_glass"])
    rg = rg.intersection(box(-5, -5, 5, rg.bounds[3] - 0.015))    # clear of the spoiler's underside
    glass = rg.buffer(-0.022, join_style=1)
    return rg, glass, rg.difference(glass)


def glass_parts(ref0, cutters, out):
    _d, g, t = dlo_shapes()
    for sgn, side in ((1, "l"), (-1, "r")):
        fr = side_fr(sgn)
        cutters += blend.lay(ref0, f"cut_side_{side}", g, fr, -0.08, 0.006, "trim", along_ray=True)
        out += blend.lay(ref0, f"side_glass_{side}", g.buffer(-0.001), fr, -0.008, -0.004, "glass", 0.02)
        out += blend.lay(ref0, f"window_trim_{side}", t.union(poly(R["sail"])), fr, -0.001, 0.0025, "trim", 0.01)
    _o, g, t = screen_shapes()
    cutters += blend.lay(ref0, "cut_screen", g, top_fr(), -0.08, 0.006, "trim", along_ray=True)
    out += blend.lay(ref0, "screen_glass", g.buffer(-0.001), top_fr(), -0.008, -0.004, "glass", 0.02)
    out += blend.lay(ref0, "screen_trim", t, top_fr(), -0.001, 0.0025, "trim", 0.01)
    _o, g, t = rear_shapes()
    cutters += blend.lay(ref0, "cut_rear", g, rear_fr(), -0.08, 0.004, "trim", along_ray=True)
    out += blend.lay(ref0, "rear_glass", g.buffer(-0.001), rear_fr(), -0.008, -0.004, "glass", 0.02)
    out += blend.lay(ref0, "rear_trim", t, rear_fr(), -0.001, 0.0025, "trim", 0.01)


def grooves(ref):
    """Shut lines as V grooves to cut: doors, bonnet, tailgate."""
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        obs.append(blend.groove(ref, f"g_door_{side}", R["door"], side_fr(sgn)))
    # a closed loop as two open halves that overlap a little, each its own cutter
    b = R["bonnet"]
    h = len(b) // 2
    obs.append(blend.groove(ref, "g_bonnet_a", b[: h + 2], top_fr()))
    obs.append(blend.groove(ref, "g_bonnet_b", b[h:] + b[:2], top_fr()))
    obs.append(blend.groove(ref, "g_tailgate", R["tailgate"], rear_fr()))
    return obs


# ---------------------------------------------------------------- body additions
def spoiler():
    """Roof spoiler: the traced profile carried across, trimmed to the roof's own curve."""
    prof = np.array(R["spoiler"]["profile"])
    h = R["spoiler"]["half"]
    ring = geom.resample(np.vstack([prof, prof[:1]]), 0.006)[:-1]
    rings = [np.column_stack([ring[:, 0], np.full(len(ring), y), ring[:, 1]]) for y in (-h, h)]
    v, f = geom.close_rings(rings)
    sp = blend.make("spoiler", v, f, "body")
    x0 = float(prof[:, 0].max())
    sec = body.section(body.at(CV, x0)) * np.array([1.0, 1.0])
    roof = Polygon(sec).buffer(0.004)
    ring2 = np.array(roof.exterior.coords)[:-1]
    rings = [np.column_stack([np.full(len(ring2), x), ring2[:, 0], ring2[:, 1]]) for x in (x0 + 0.05, prof[:, 0].min() - 0.05)]
    v, f = geom.close_rings(rings)
    blend.boolean(sp, blend.make("roofline", v, f, "body"), "INTERSECT")
    return sp


def mirrors():
    m = R["mirror"]
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        rings = []
        x_mid = m["x_back"] + m["length"] / 2
        y0 = body.y_at(body.curves(body.ROWS["stations"])[1], x_mid, m["z"]) - 0.025   # sunk into the door
        for y in np.linspace(y0, m["y_out"], 18):
            t = max(0.0, (y - m["y_in"]) / (m["y_out"] - m["y_in"]))
            h = m["h_in"] + (m["h_out"] - m["h_in"]) * np.sin(t * np.pi / 2)
            ln = m["length"] * (0.75 + 0.25 * np.sin(t * np.pi / 2))
            if t > 0.8:                               # round off the outer end
                k = np.sqrt(1 - ((t - 0.8) / 0.2) ** 2 * 0.85)
                h, ln = h * k, ln * k
            r = min(h, ln) * 0.45
            sh = box(m["x_back"], m["z"] - h / 2, m["x_back"] + ln, m["z"] + h / 2)
            sh = sh.buffer(-r, join_style=1).buffer(r, join_style=1)
            ring = body._even(np.array(sh.exterior.coords)[:-1], 64)
            rings.append(np.column_stack([ring[:, 0], np.full(64, y * sgn), ring[:, 1]]))
        v, f = geom.close_rings(rings)
        obs.append(blend.make(f"mirror_{side}", v, f, "body"))
    return obs


def mirror_recess():
    m = R["mirror"]
    cut, glass = [], []
    y0, y1 = m["y_in"] + 0.035, m["y_out"] - 0.03
    h = m["h_out"] * 0.72
    for sgn, side in ((1, "l"), (-1, "r")):
        for name, (xa, xb), pad, mat, store in (
                ("cut_mirror", (m["x_back"] - 0.02, m["x_back"] + 0.012), 0.0, "trim", cut),
                ("mirror_glass", (m["x_back"] + 0.006, m["x_back"] + 0.009), 0.0015, "mirror_glass", glass)):
            sh = box(y0 + pad, m["z"] - h / 2 + pad, y1 - pad, m["z"] + h / 2 - pad).buffer(-0.012, join_style=1).buffer(0.012, join_style=1)
            ring = body._even(np.array(sh.exterior.coords)[:-1], 48)
            rings = [np.column_stack([np.full(48, x), ring[:, 0] * sgn, ring[:, 1]]) for x in (xa, xb)]
            v, f = geom.close_rings(rings)
            store.append(blend.make(f"{name}_{side}", v, f, mat))
    return cut, glass


def arches():
    """Arch cutters and the wheel tubs kept out of the cabin."""
    cut, tubs = [], []
    for x in (W["front_x"], W["rear_x"]):
        for sgn in (1, -1):
            v, f = geom.disc((x, 0.83 * sgn, W["r"]), (0, 1, 0), W["arch_r"], 0.56, n=96)
            cut.append(blend.make("arch", v, f, "interior"))
            v, f = geom.disc((x, 0.86 * sgn, W["r"]), (0, 1, 0), W["arch_r"] + 0.035, 0.70, n=96)
            tubs.append(blend.make("tub", v, f, "interior"))
    return cut, tubs


def exhaust():
    e = R["exhaust"]
    cut, pipes = [], []
    for i, y in enumerate(e["y"]):
        x_end = e["x"]
        prof = [(e["r"] - 0.003, 0.0), (e["r"], 0.0), (e["r"], 0.26), (e["r"] - 0.003, 0.26)]
        v, f = geom.lathe(prof, 48, axis="z")
        v = np.column_stack([x_end + v[:, 2], y + v[:, 0], e["z"] + v[:, 1]])
        pipes.append(blend.make(f"exhaust_{i}", v, f, "chrome"))
        v, f = geom.disc((x_end + 0.13, y, e["z"]), (1, 0, 0), e["r"] + 0.004, 0.30)
        cut.append(blend.make("cut_exhaust", v, f, "trim"))
    return cut, pipes


def strips(ref):
    obs = []
    door = LineString(R["door"]).buffer(0.0045)
    g = poly(R["strip"]).difference(door)
    for sgn, side in ((1, "l"), (-1, "r")):
        obs += blend.lay(ref, f"side_strip_{side}", g, side_fr(sgn), 0.0, 0.004, "body", 0.006)
    return obs


def brake3(ref):
    return blend.lay(ref, "lamp_brake_3", poly(R["brake3"]), rear_fr(), 0.0, 0.003, "lamp_brake", 0.004)


# ---------------------------------------------------------------- interior
def _loft(name, x0, x1, clip, n=12, gap=0.0025, mat="interior"):
    rings = []
    for x in np.linspace(x0, x1, n):
        g = body.inside(CV, x, 0.035 + gap).intersection(clip(x))
        if g.geom_type != "Polygon":
            g = max(getattr(g, "geoms", [g]), key=lambda q: q.area)
        ring = body._even(np.array(g.exterior.coords)[:-1], 64)
        rings.append(np.column_stack([np.full(64, x), ring]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def _rbox(name, x0, x1, y0, y1, z0, z1, r=0.03, lean=0.0):
    sh = box(y0, z0, y1, z1).buffer(-r, join_style=1).buffer(r, join_style=1)
    ring = body._even(np.array(sh.exterior.coords)[:-1], 48)
    rings = []
    for x in (x0, x0 + 0.015, x1 - 0.015, x1):
        k = 0.92 if x in (x0, x1) else 1.0
        cy, cz = (y0 + y1) / 2, (z0 + z1) / 2
        rr = np.column_stack([cy + (ring[:, 0] - cy) * k, cz + (ring[:, 1] - cz) * k])
        rings.append(np.column_stack([x - lean * (rr[:, 1] - z0), rr]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, "interior")


def interior(floor):
    x0 = R["screen"]["x_base"] + 0.08
    obs = [_loft("firewall", x0 - 0.010, x0 - 0.004, lambda x: box(-2, floor + 0.002, 2, 3), n=2),
           _loft("dash", x0 - 0.0125, 0.52, lambda x: box(-2, 0.80, 2, 0.80 + 0.19 * (1 - (x0 - x) / 0.6) + 0.03), n=10)]
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, 0.46, 1.2, 0.97) if s > 0 else box(-1.2, 0.46, -0.3, 0.97)).difference(
            body.inside(CV, x, 0.035 + 0.025))
        obs.append(_loft(f"door_card_{side}", -0.37, 0.76, clip, n=12))
        yc = 0.37 * sgn
        obs.append(_rbox(f"seat_base_{side}", -0.36, 0.14, yc - 0.25, yc + 0.25, floor + 0.002, 0.52))
        obs.append(_rbox(f"seat_back_{side}", -0.50, -0.38, yc - 0.24, yc + 0.24, 0.525, 1.15, lean=0.22))
        obs.append(_rbox(f"headrest_{side}", -0.67, -0.57, yc - 0.13, yc + 0.13, 1.18, 1.31, r=0.035, lean=0.1))
    obs.append(_rbox("bench_base", -1.06, -0.62, -0.49, 0.49, floor + 0.002, 0.53))
    obs.append(_rbox("bench_back", -1.20, -1.08, -0.49, 0.49, 0.535, 1.04, lean=0.25))
    xm = R["screen"]["x_top"] - 0.03
    roof_in = body.inside(CV, xm, 0.035).bounds[3]
    top = roof_in - 0.07
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.11, 0.11, top - 0.055, top, r=0.02)
    stem = _rbox("stem", xm - 0.016, xm - 0.004, -0.012, 0.012, top - 0.015, roof_in - 0.0008, r=0.004)
    obs.append(blend.union_all([m, stem], "mirror_rear"))
    return obs


# ---------------------------------------------------------------- wheels
def wheels():
    obs = []
    hw = W["width"] / 2
    yc = W["outer_y"] - hw
    tyre = box(W["rim_r"], -hw, W["r"], hw).buffer(-0.035, join_style=1).buffer(0.035, join_style=1)
    tyre = tyre.union(box(W["rim_r"], -hw + 0.012, W["rim_r"] + 0.03, hw - 0.012))
    tp = body._even(np.array(tyre.exterior.coords)[:-1], 96)
    rim = [(W["rim_r"] - 0.001, -hw + 0.012), (W["rim_r"] - 0.001, hw - 0.01), (W["rim_r"] - 0.012, hw - 0.002),
           (W["rim_r"] - 0.024, hw - 0.006), (W["rim_r"] - 0.024, -hw + 0.012)]
    for x, fb in ((W["front_x"], "f"), (W["rear_x"], "r")):
        for sgn, lr in ((1, "l"), (-1, "r")):
            def place(v, s=sgn):
                v = np.asarray(v, float)
                return np.column_stack([x + v[:, 0], (yc + v[:, 1]) * s if s > 0 else -(yc + v[:, 1]), W["r"] + v[:, 2]])
            v, f = geom.lathe(tp, 96, axis="y")
            obs.append(blend.make(f"wheel_tyre_{fb}{lr}", place(v), f, "tyre"))
            v, f = geom.lathe(rim, 96, axis="y")
            parts = [blend.make("rim", place(v), f, "rim")]
            v, f = geom.disc((0, 0.065, 0), (0, 1, 0), 0.075, 0.05, n=48)
            parts.append(blend.make("hub", place(v), f, "rim"))
            for k in range(5):
                a = 2 * np.pi * k / 5
                ca, sa = np.cos(a), np.sin(a)
                pts = []
                for r, w2, h0, h1 in ((0.06, 0.026, 0.040, 0.088), (W["rim_r"] - 0.018, 0.018, 0.045, 0.098)):
                    for dw in (-w2, w2):
                        for hh in (h0, h1):
                            pts.append((r * ca - dw * sa, hh, r * sa + dw * ca))
                v = np.array(pts)
                f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
                parts.append(blend.make("spoke", place(v), f, "rim"))
            obs.append(blend.union_all(parts, f"wheel_rim_{fb}{lr}"))
    return obs


# ---------------------------------------------------------------- badge and name
def _quad(p0, p1, p2, n=10):
    t = np.linspace(0, 1, n)[1:, None]
    return list(map(tuple, (1 - t) ** 2 * np.array(p0) + 2 * (1 - t) * t * np.array(p1) + t * t * np.array(p2)))


def badge_shape():
    """The Kaiser badge from src/renderer/assets/brands/kaiser/kaiser-badge-chrome.svg, y up,
    centred, 1 unit tall."""
    ring = Point(90, 100).buffer(52, 128).difference(Point(90, 100).buffer(26, 128))
    stem = [(128, 48), (154, 48), (154, 124)] + _quad((154, 124), (154, 136), (168, 136)) + \
        [(174, 136), (174, 152), (160, 152)] + _quad((160, 152), (128, 152), (128, 122))
    g = unary_union([ring, Polygon(stem)]).buffer(1.5, join_style=1)
    from shapely.affinity import affine_transform
    g = affine_transform(g, [1.15, 0, 0, 1.15, -23, -15])
    for x, y in ((96, 80), (104, 100), (112, 120)):
        g = g.difference(box(x, y, x + 110, y + 9))
    g = sscale(g, 1, -1, origin=(0, 0))
    b = g.bounds
    g = translate(g, -(b[0] + b[2]) / 2, -(b[1] + b[3]) / 2)
    return sscale(g, 1 / (b[3] - b[1]), 1 / (b[3] - b[1]), origin=(0, 0))


def name_shape():
    gl = json.loads((HERE / "glyphs.json").read_text())
    polys = [Polygon(p[0], p[1:]) for p in gl["pieces"]]
    g = unary_union(polys)
    b = g.bounds
    g = translate(g, -(b[0] + b[2]) / 2, -gl["cap"] / 2)
    return sscale(g, 1 / gl["cap"], 1 / gl["cap"], origin=(0, 0))


def _flat(ref, g, size, fr, a, b, up, name, d0, d1, mat):
    """Lay shape g (right, up, 1 unit = size) flat on the panel that fr's ray at (a, b) meets."""
    pf = blend.panel_frame(ref, fr, a, b, up)
    return blend.lay(ref, name, sscale(g, size, size, origin=(0, 0)), pf, d0, d1, mat, 0.005)


def options(ref):
    """Every badge and name option, named opt_<group>_<n>_<i>. Each lies flat on its panel."""
    sp = R["spots"]
    bdg, nm = badge_shape(), name_shape()
    up, fwd = (0, 0, 1), (-1, 0, 0)
    out = []

    def add(group, n, objs):
        for i, ob in enumerate(objs):
            ob.name = f"opt_{group}_{n}_{i}"
            out.append(ob)
    # front: 1 on the grille (the reference), 2 on the bonnet
    add("frontbadge", 1, _flat(ref, bdg, 0.08, front_fr(), 0, sp["grille_z"], up, "fb", 0.0045, 0.0075, "chrome"))
    add("frontbadge", 2, _flat(ref, bdg, 0.08, top_fr(), sp["bonnet_x"], 0, fwd, "fb", 0.0, 0.003, "chrome"))
    # rear: 1 tailgate centre (the reference), 2 lower on the tailgate
    add("rearbadge", 1, _flat(ref, bdg, 0.10, rear_fr(), 0, sp["tailgate_z"], up, "rb", 0.0, 0.003, "chrome"))
    add("rearbadge", 2, _flat(ref, bdg, 0.08, rear_fr(), 0, sp["tailgate_low_z"], up, "rb", 0.0, 0.003, "chrome"))
    cap = 0.03
    add("name", 1, _flat(ref, nm, cap, rear_fr(), sp["name_y"], sp["name_z"], up, "nm", 0.0, 0.0025, "name"))
    add("name", 2, _flat(ref, nm, cap, rear_fr(), -sp["name_y"], sp["name_z"], up, "nm", 0.0, 0.0025, "name"))
    q = []
    for sgn in (1, -1):
        q += _flat(ref, nm, cap, side_fr(sgn), sp["quarter_x"], sp["quarter_z"], up, "nm", 0.0, 0.0025, "name")
    add("name", 3, q)
    return out
