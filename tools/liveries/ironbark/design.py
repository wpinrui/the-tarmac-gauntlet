"""Ironbark Racing on the Liberty Collier: green and gold, the flag, and the tree-ring logo.
Independent, so no maker's logo. Writes one folder of panels per option under out/.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/LibertyCollier/LibertyCollier.obj out/views
     python3 marks.py
     python3 design.py

Side art is given as (z, y) in the car's metres: z runs nose (+2.08) to tail (-1.98), y up from
the ground. Landmarks on the Collier: front wheel at z 1.27, rear at -1.27 (radius 0.34, arch
tops at y 0.71); door from z 0.87 to -0.44, y 0.19 to the window line at 0.95; bonnet z 0.87
to 2.0; roof z -1.25 to 0.1.
"""
import math
import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
MARKS = HERE / "out" / "marks"
sys.path.insert(0, str(HERE.parent / "common"))
from livery import Livery  # noqa: E402

GREEN = "#0f4d2c"
GOLD = "#f2b51c"
WATTLE = "#ffd24a"
CREAM = "#f3ead2"

DOOR_Z, DOOR_W, DOOR_H = 0.22, 0.5, 0.28
ROOF_Z = -0.55


def bezier(pts, n=40):
    """Points along a cubic Bezier chain: p0 c0 c1 p1 c2 c3 p2 ..."""
    out = []
    for i in range(0, len(pts) - 1, 3):
        p0, c0, c1, p1 = pts[i:i + 4]
        for k in range(n + (1 if i + 4 >= len(pts) else 0)):
            t = k / n
            out.append(tuple((1 - t) ** 3 * a + 3 * (1 - t) ** 2 * t * b + 3 * (1 - t) * t ** 2 * c + t ** 3 * d
                             for a, b, c, d in zip(p0, c0, c1, p1)))
    return out


def ribbon(centre, w0, w1):
    """A band along a centreline, w0 wide at the start tapering to w1 at the end."""
    top, bot = [], []
    n = len(centre)
    for i, (z, y) in enumerate(centre):
        a, b = centre[max(i - 1, 0)], centre[min(i + 1, n - 1)]
        dz, dy = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(dz, dy)
        nz, ny = -dy / ln, dz / ln
        h = (w0 + (w1 - w0) * i / (n - 1)) / 2
        top.append((z + nz * h, y + ny * h))
        bot.append((z - nz * h, y - ny * h))
    return top + bot[::-1]


def circle(z, y, r, n=14):
    return [(z + math.cos(a) * r, y + math.sin(a) * r) for a in (i / n * 2 * math.pi for i in range(n))]


def star(cx, cy, r, n, inner=0.47):
    """A star with a point toward +second axis."""
    return [(cx + math.cos(a) * (r * inner if i % 2 else r), cy - math.sin(a) * (r * inner if i % 2 else r))
            for i, a in ((i, -math.pi / 2 + i * math.pi / n) for i in range(n * 2))]


def flags(lv, z, y, height):
    """The flag with its hoist toward the nose on both sides."""
    lv.mark("left", MARKS / "flag.svg", lv._side("left", z, y), height)
    lv.mark("right", MARKS / "flag-mirror.svg", lv._side("right", z, y), height)


def numbers(lv, ink, door_y, roof=True):
    lv.side_number(DOOR_Z, door_y, DOOR_W, DOOR_H, ink)
    if roof:
        lv.number("top", (0, 0, ROOF_Z), 0.5, 0.32, ink, rotate=180)


# The gold sweep: low on the front bumper, under the door number, up to the rear pillar.
SWEEP = bezier([(2.12, 0.40), (1.3, 0.40), (0.6, 0.44), (-0.1, 0.55),
                (-0.7, 0.66), (-1.05, 0.82), (-1.32, 1.04)])


def green_and_gold(views):
    lv = Livery(views, GREEN)
    lv.side_poly(ribbon(SWEEP, 0.16, 0.07), GOLD)
    lv.side_poly(ribbon([(z, y - 0.13) for z, y in SWEEP[:int(len(SWEEP) * 0.8)]], 0.025, 0.012), GOLD)
    rnd = random.Random(3)
    for _ in range(26):  # wattle blossom where the sweep ends, on the rear pillar
        a, d = rnd.random() * 2 * math.pi, rnd.random() * 0.13
        lv.side_poly(circle(-1.42 + math.cos(a) * d, 1.12 + math.sin(a) * d * 0.8, 0.018 + rnd.random() * 0.02),
                     WATTLE if rnd.random() < 0.5 else GOLD)
    for x0, x1 in ((0.26, 0.38), (-0.38, -0.26)):  # twin stripes over the bonnet, roof and tailgate
        lv.band_x(x0, x1, GOLD)
    for view in ("left", "right"):
        lv.mark(view, MARKS / "logo-light.svg", lv._side(view, DOOR_Z, 0.32), 0.17)
    lv.mark("top", MARKS / "logo-light.svg", (0, 0, 1.55), 0.2, rotate=-90)
    lv.mark("rear", MARKS / "logo-light.svg", (0.0, 0.66, 0), 0.13)
    lv.side_mark(MARKS / "tagline-gold.svg", -0.15, 0.11, 0.045)
    flags(lv, 1.22, 0.83, 0.11)
    numbers(lv, CREAM, 0.79)
    return lv


# The split: green ahead of a line from behind the front wheel up through the B-pillar, gold
# behind it.
def split_y(z):
    return (1.4 - z) / 1.54


def southern_cross(views):
    lv = Livery(views, GREEN)
    z0, z1 = 1.4, 1.4 - 1.54 * 2.0
    lv.side_poly([(z0, 0.0), (z1, 2.0), (-2.4, 2.0), (-2.4, -0.1), (z0 + 0.15, -0.1)], GOLD)
    lv.side_poly([(z0 - 0.09, 0.0), (z1 - 0.09, 2.0), (z1 - 0.12, 2.0), (z0 - 0.12, 0.0)], GREEN)
    lv.band_x(-2, 2, GOLD, views=("rear",))
    lv.side_poly(star(0.62, 0.78, 0.1, 7), GOLD)  # the Commonwealth star on the door's green
    # The Southern Cross over the roof, Alpha toward the windscreen, in the flag's arrangement.
    for fx, fy, r, n in ((0.75, 0.167, 0.11, 7), (0.625, 0.45, 0.11, 7), (0.873, 0.4, 0.11, 7),
                         (0.75, 0.833, 0.11, 7), (0.81, 0.56, 0.06, 5)):
        x, z = (fx - 0.75) * 3.33, -0.575 + (fy - 0.5) * 1.667
        lv.poly("top", [(x + dx, 0, z - dz) for dx, dz in star(0, 0, r, n)], GOLD)  # points to the tail
    for view in ("left", "right"):
        lv.mark(view, MARKS / "logo-dark.svg", lv._side(view, 0.0, 0.29), 0.16)
    lv.mark("top", MARKS / "logo-light.svg", (0, 0, 1.55), 0.2, rotate=-90)
    lv.mark("rear", MARKS / "logo-dark.svg", (0.0, 0.66, 0), 0.13)
    lv.side_mark(MARKS / "tagline-green.svg", -1.0, 0.11, 0.045)
    flags(lv, -0.7, 0.86, 0.11)
    lv.side_number(0.02, 0.5, DOOR_W, DOOR_H, GREEN)
    return lv


OPTIONS = {"green-and-gold": green_and_gold, "southern-cross": southern_cross}

if __name__ == "__main__":
    views = HERE / "out" / "views" / "views.json"
    for name, make in OPTIONS.items():
        make(views).write(HERE / "out" / name)
