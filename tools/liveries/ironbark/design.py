"""Ironbark Racing on the Liberty Collier: green and gold, the flag, and the tree-ring logo.
Independent, so no maker's logo. Writes one folder of panels per option under out/.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/LibertyCollier/LibertyCollier.obj out/views
     python3 marks.py
     python3 design.py
     ./game.sh

Side art is given as (z, y) in the car's metres: z runs nose (+2.08) to tail (-1.98), y up from
the ground. Landmarks on the Collier: front wheel at z 1.27, rear at -1.27 (radius 0.34, arch
tops at y 0.71); door from z 0.87 to -0.44, y 0.19 to the window line at 0.95; bonnet z 0.87
to 2.0; roof z -1.25 to 0.1.
"""
import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
MARKS = HERE / "out" / "marks"
MODEL = HERE.parents[2] / "unity" / "Assets" / "Art" / "Vehicles" / "LibertyCollier" / "LibertyCollier.obj"
sys.path.insert(0, str(HERE.parent / "common"))
from livery import Livery  # noqa: E402

GREEN = "#0f4d2c"
GOLD = "#f2b51c"


def body_tris(_cache=[]):
    """The model's paint triangles."""
    if not _cache:
        verts, mat = [], None
        for ln in MODEL.read_text().splitlines():
            t = ln.split()
            if not t:
                continue
            if t[0] == "v":
                verts.append(tuple(map(float, t[1:4])))
            elif t[0] == "usemtl":
                mat = t[1]
            elif t[0] == "f" and mat == "body":
                ids = [verts[int(k.split("/")[0]) - 1] for k in t[1:]]
                _cache += [(ids[0], ids[i], ids[i + 1]) for i in range(1, len(ids) - 1)]
    return _cache


def surface(axis, a, b, pick):
    """Where a ray along one axis meets the paint. axis 2 (z) takes (x, y), axis 0 (x) takes
    (z, y); pick chooses among the hits (min z for the tailgate, max x for the car's left)."""
    i, j = (0, 1) if axis == 2 else (2, 1)
    hits = []
    for p, q, r in body_tris():
        d = (q[i] - p[i]) * (r[j] - p[j]) - (r[i] - p[i]) * (q[j] - p[j])
        if abs(d) < 1e-12:
            continue
        u = ((a - p[i]) * (r[j] - p[j]) - (r[i] - p[i]) * (b - p[j])) / d
        v = ((q[i] - p[i]) * (b - p[j]) - (a - p[i]) * (q[j] - p[j])) / d
        if u >= 0 and v >= 0 and u + v <= 1:
            hits.append(p[axis] + u * (q[axis] - p[axis]) + v * (r[axis] - p[axis]))
    return pick(hits)


def on_top(lv, pts, fill, side=None):
    """Art on a curved panel shows in the top view too (and side art near the nose in the front
    view), blended in by the shader: draw it there as well, where it lands on the surface, or
    that view's paint bleeds into it. pts are rear
    view (x, y), or side view (z, y) when side is set. Edges are split so the outline follows
    the curve."""
    pts = [(p[0] + (q[0] - p[0]) * k / 8, p[1] + (q[1] - p[1]) * k / 8)
           for p, q in zip(pts, pts[1:] + pts[:1]) for k in range(8)]
    if side:
        for sign in (1, -1):
            xs = [sign * surface(0, z, y, max) for z, y in pts]
            lv.poly("top", [(x, 0, z) for x, (z, y) in zip(xs, pts)], fill)
            lv.poly("front", [(x, y, 0) for x, (z, y) in zip(xs, pts)], fill)
    else:
        lv.poly("top", [(x, 0, surface(2, x, y, min)) for x, y in pts], fill)


def star(cx, cy, r, n, inner=0.47):
    """A star with a point toward +second axis."""
    return [(cx + math.cos(a) * (r * inner if i % 2 else r), cy - math.sin(a) * (r * inner if i % 2 else r))
            for i, a in ((i, -math.pi / 2 + i * math.pi / n) for i in range(n * 2))]


def flags(lv, z, y, height):
    """The flag with its hoist toward the nose on both sides."""
    lv.mark("left", MARKS / "flag.svg", lv._side("left", z, y), height)
    lv.mark("right", MARKS / "flag-mirror.svg", lv._side("right", z, y), height)


# The Southern Cross as the flag places it: (x across the fly, y down it, radius on the roof, points).
CROSS = ((0.75, 0.167, 0.11, 7), (0.625, 0.45, 0.11, 7), (0.873, 0.4, 0.11, 7), (0.75, 0.833, 0.11, 7), (0.81, 0.56, 0.06, 5))


def southern_cross(slant):
    """slant: degrees the big side logo tilts, positive running down toward the tail."""
    def make(views):
        lv = Livery(views, GREEN)
        z0, z1 = 1.4, 1.4 - 1.54 * 2.0
        lv.side_poly([(z0, 0.0), (z1, 2.0), (-2.4, 2.0), (-2.4, -0.1), (z0 + 0.15, -0.1)], GOLD)
        lv.side_poly([(z0 - 0.09, 0.0), (z1 - 0.09, 2.0), (z1 - 0.12, 2.0), (z0 - 0.12, 0.0)], GREEN)
        lv.band_x(-2, 2, GOLD, views=("rear",))
        wing = star(1.2, 0.8, 0.065, 7)  # the Commonwealth star on the front wing
        lv.side_poly(wing, GOLD)
        on_top(lv, wing, GOLD, side=True)
        # The roof rail stays green to the end of the roof, so the roof's edge has no gold blur.
        lv.side_poly([(2.4, 1.24), (-1.3, 1.24), (-1.3, 2.0), (2.4, 2.0)], GREEN)
        # The Southern Cross over the roof, Alpha toward the windscreen, in the flag's arrangement.
        for fx, fy, r, n in CROSS:
            x, z = (fx - 0.75) * 3.33, -0.575 + (fy - 0.5) * 1.667
            lv.poly("top", [(x + dx, 0, z - dz) for dx, dz in star(0, 0, r, n)], GOLD)  # points to the tail
        lv.mark("top", MARKS / "logo-light.svg", (0, 0, 1.5), 0.34, rotate=-90)
        # Behind the roof the top view is gold too, so the tailgate's top edge blends gold into gold.
        lv.poly("top", [(-2, 0, -1.3), (2, 0, -1.3), (2, 0, -3), (-2, 0, -3)], GOLD)
        # The tailgate answers the roof: the Southern Cross in green on the gold, upright as on the
        # flag, beside the maker's badge and clear of the script.
        for fx, fy, r, n in CROSS:
            x, y = 0.25 - (fx - 0.75) * 0.9, 0.76 - (fy - 0.5) * 0.45
            pts = [(x - dx, y + dy) for dx, dy in star(0, 0, r * 0.4, n)]
            lv.poly("rear", [(px, py, 0) for px, py in pts], GREEN)
            on_top(lv, pts, GREEN)
        lv.mark("rear", MARKS / "url-green.svg", (0.0, 0.36, 0), 0.075)  # in the plate recess
        # Gold number on the green at the front of the door, the flag at the door's foot and the
        # logo big from the door's back half over the rear quarter.
        lv.side_number(0.53, 0.82, 0.46, 0.2, GOLD)  # 100 still fits the door's green
        flags(lv, 0.45, 0.3, 0.11)
        z, y, h = {12: (-0.36, 0.46, 0.36), -18: (-0.28, 0.56, 0.41)}[slant]
        for view, sign in (("left", 1), ("right", -1)):
            lv.mark(view, MARKS / "logo-dark.svg", lv._side(view, z, y), h, rotate=sign * slant)
        return lv
    return make


OPTIONS = {"southern-cross": southern_cross(-18)}

if __name__ == "__main__":
    views = HERE / "out" / "views" / "views.json"
    for name, make in OPTIONS.items():
        make(views).write(HERE / "out" / name)
