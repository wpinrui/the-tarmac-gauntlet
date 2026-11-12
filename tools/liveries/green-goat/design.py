"""Green Goat Racing on the Kaiser Scream: the racing arm of an energy drink empire, out to make
Green Goat's Milk a global protein shake. Overstimulus: neon green, black, milk white, volt
yellow and a hit of magenta. Independent, so no maker's logo. Writes one folder of panels per
option under out/.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/KaiserScream/KaiserScream.obj out/views
     node ../common/digits.js --font font/Bangers.ttf --edge 0.12 --step 0.9 --out out/digits
     python3 marks.py
     python3 design.py

Scream landmarks in OBJ metres: z along the car (nose +2.38, tail -2.23), y up. Front wheel at
z 1.365 (radius 0.33), rear at -1.365 (0.355). Door from z 0.82 to -0.39, y 0.24 to 0.815.
The side intake runs diagonally just behind the door, z -0.46 to -0.71, y 0.36 to 0.72.
"""
import math
import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
MARKS = HERE / "out" / "marks"
sys.path.insert(0, str(HERE.parent / "common"))
from livery import Livery  # noqa: E402

G = "#8cff1a"
GD = "#3f9e0c"
K = "#0a0d08"
M = "#f6f8ee"
Y = "#ffe600"
PK = "#ff2bd6"

DOOR = (0.82, -0.39)
WHEELS = ((1.365, 0.33), (-1.365, 0.355))
# The top edge of the side as the side view sees it, nose to tail.
SHOULDER = [(2.42, 0.45), (2.1, 0.62), (1.79, 0.72), (1.36, 0.78), (0.81, 0.82), (-0.56, 0.87),
            (-1.71, 0.855), (-2.0, 0.78), (-2.19, 0.64), (-2.3, 0.5)]


def shoulder(z):
    pts = SHOULDER
    if z >= pts[0][0]:
        return pts[0][1]
    for (z0, y0), (z1, y1) in zip(pts, pts[1:]):
        if z1 <= z <= z0:
            return y0 + (y1 - y0) * (z0 - z) / (z0 - z1)
    return pts[-1][1]


def disc(ca, cb, r, n=20):
    return [(ca + r * math.cos(2 * math.pi * i / n), cb + r * math.sin(2 * math.pi * i / n)) for i in range(n)]


def rounded(ca, cb, w, h, r, n=6):
    """A rounded rectangle centred on (ca, cb)."""
    out = []
    for qa, qb, a0 in ((1, 1, 0), (-1, 1, 90), (-1, -1, 180), (1, -1, 270)):
        for k in range(n + 1):
            a = math.radians(a0 + 90 * k / n)
            out.append((ca + qa * (w / 2 - r) + r * math.cos(a), cb + qb * (h / 2 - r) + r * math.sin(a)))
    return out


def drips(line, z0, z1, rng, longest, skip=(), short=()):
    """Milk hanging from a (z -> y) line between z0 and z1 (z0 > z1): the poured part above the
    line and round-ended drips below it. skip: z spans with no drips; short: (z, z, length) caps.
    Returns polygons in (z, y)."""
    polys = []
    top = [(z, 1.4) for z in (z0, z1)]
    edge = [(z0 - (z0 - z1) * i / 120, line(z0 - (z0 - z1) * i / 120)) for i in range(121)]
    polys.append([top[0], *edge, top[1]])
    z = z0 - rng.uniform(0.02, 0.1)
    while z > z1 + 0.06:
        w = rng.uniform(0.035, 0.11)
        cz = z - w / 2
        if not any(b <= cz <= a for a, b in skip):
            n = rng.random() ** 1.7 * longest
            for a, b, cap in short:
                if b <= cz <= a:
                    n = min(n, cap)
            if n > 0.025:
                y = line(cz)
                body = [(cz + w / 2, y + 0.01), (cz + w / 2, y - n)]
                body += [(cz + w / 2 * math.cos(math.pi * k / 10), y - n - w / 2 * math.sin(math.pi * k / 10)) for k in range(11)]
                body += [(cz - w / 2, y - n), (cz - w / 2, y + 0.01)]
                # A little neck where the drip leaves the pour.
                polys.append(body)
                polys.append([(cz + w * 0.85, y + 0.005), (cz, y - min(n, 0.06)), (cz - w * 0.85, y + 0.005)])
                if n > 0.12 and rng.random() < 0.3:
                    polys.append(disc(cz, y - n - w * 1.2, w * 0.3, 12))
        z -= w + rng.uniform(0.03, 0.2)
    return polys


def circles(lv, view, pts, fill):
    """Many small discs at once, (point in car metres, radius)."""
    parts = []
    for p, r in pts:
        u, v = lv.uv(view, p)
        parts.append(f'<circle cx="{u:.4f}" cy="{v:.4f}" r="{r:.4f}"/>')
    lv.art[view].append(f'<g fill="{fill}">{"".join(parts)}</g>')


def side_circles(lv, zyr, fill):
    for view in ("left", "right"):
        circles(lv, view, [(lv._side(view, z, y), r) for z, y, r in zyr], fill)


def side_mark_at(lv, name, z, y, h, rotate=0):
    """A mark on both sides, turned rotate degrees with its top leaning to the nose."""
    for view in ("left", "right"):
        lv.mark(view, MARKS / f"{name}.svg", lv._side(view, z, y), h, rotate=-rotate if view == "left" else rotate)


def slash(a, b, w0, w1, bend=0.0, n=24):
    """A tapered stroke from a to b, w0 wide at a and w1 at b, bowed sideways by bend."""
    (za, ya), (zb, yb) = a, b
    dz, dy = zb - za, yb - ya
    ln = math.hypot(dz, dy)
    nz, ny = -dy / ln, dz / ln
    L, R = [], []
    for i in range(n + 1):
        t = i / n
        off = bend * math.sin(math.pi * t)
        cz, cy = za + dz * t + nz * off, ya + dy * t + ny * off
        w = (w0 + (w1 - w0) * t) / 2
        L.append((cz + nz * w, cy + ny * w))
        R.append((cz - nz * w, cy - ny * w))
    return L + R[::-1]


# --- Parts ----------------------------------------------------------------------------------
def milk_sides(lv, door_cap=0.05):
    """Milk poured over the roof, drips down both sides, none crossing a door shut line."""
    rng = random.Random(7)
    line = lambda z: shoulder(z) - 0.06
    walls = [(a + 0.035, a - 0.035) for a in DOOR]
    for poly in drips(line, 2.45, -2.35, rng, 0.3, skip=walls,
                      short=[(0.25, -0.4, door_cap), (0.8, 0.25, door_cap if door_cap < 0.05 else 0.3),
                             (1.75, 0.98, 0.06), (-0.95, -1.75, 0.05)]):
        lv.side_poly(poly, M)


def milk_top(lv, splat_col=G):
    """The top all milk, with splats, clear of the bonnet and roof logos."""
    lv.band_x(-1.2, 1.2, M, views=("top",))
    splat = random.Random(3)
    for _ in range(14):
        z, x, r = splat.uniform(-2.0, 2.2), splat.uniform(-0.75, 0.75), splat.uniform(0.02, 0.06)
        if abs(z - 1.55) < 0.45 or abs(z - 0.1) < 0.35:
            continue
        lv.poly("top", [(x + b, 0, z + a) for a, b in disc(0, 0, r)], splat_col)


def milk_ends(lv):
    """The milk running over the nose and the tail."""
    fr = random.Random(11)
    for view, y0 in (("front", 0.82), ("rear", 0.86)):
        sign = 1 if view == "front" else -1
        for poly in drips(lambda x: y0, 1.1, -1.1, fr, 0.16):
            lv.poly(view, [(sign * x, y, 0) for x, y in poly], M)


def halftone(lv, col, top=True, reach=0.5):
    """Halftone dots rising from the sills (and in from the top's edges), densest at the bottom."""
    s = 0.045
    dots = []
    for j in range(int(1.1 / s) + 1):
        y = 0.04 + j * s
        k = max(0.0, 1 - (y - 0.05) / reach) ** 1.3
        if k < 0.06:
            continue
        for i in range(int(4.9 / s) + 1):
            dots.append((2.45 - i * s - (j % 2) * s / 2, y, k * s * 0.62))
    side_circles(lv, dots, col)
    if top:
        dots = []
        for j in range(int(2.1 / s) + 1):
            x = -1.05 + j * s
            k = max(0.0, (abs(x) - 0.45) / 0.55) ** 1.2
            if k < 0.06:
                continue
            for i in range(int(4.9 / s) + 1):
                dots.append(((x, 0, 2.45 - i * s - (j % 2) * s / 2), min(1, k) * s * 0.62))
        circles(lv, "top", dots, col)
    for view in ("front", "rear"):
        dots = []
        for j in range(int(0.7 / s) + 1):
            y = 0.04 + j * s
            k = max(0.0, 1 - (y - 0.05) / reach) ** 1.3
            for i in range(int(2.1 / s) + 1):
                dots.append(((-1.05 + i * s + (j % 2) * s / 2, y, 0), k * s * 0.62))
        circles(lv, view, [d for d in dots if d[1] > 0.004], col)


def slashes(lv, top=True):
    """Magenta slashes, jumping the door shut lines."""
    for a, b, w in (((1.25, 0.28), (-0.15, 0.78), 0.09), ((0.15, 0.2), (-1.05, 0.62), 0.06), ((2.3, 0.3), (1.75, 0.62), 0.05),
                    ((-1.6, 0.78), (-2.25, 0.35), 0.06)):
        lv.side_poly(slash(a, b, w, 0.012, 0.05), PK)
    if top:
        for a, b, w in (((0.6, 0.9), (-0.4, 0.55), 0.08), ((2.2, -0.75), (1.0, -0.6), 0.06), ((-0.9, -0.85), (-2.1, -0.4), 0.07)):
            lv.poly("top", [(x, 0, z) for z, x in slash(a, b, w, 0.012, 0.06)], PK)


def cartoon_number(lv):
    """The number on a die-cut white plate at the door's front, in the cartoon digits."""
    lv.side_poly(rounded(0.46, 0.55, 0.52, 0.36, 0.05), M)
    lv.side_poly(rounded(0.46, 0.55, 0.48, 0.32, 0.035), K)
    lv.side_poly(rounded(0.46, 0.55, 0.44, 0.28, 0.025), M)
    lv.side_number(0.46, 0.55, 0.4, 0.22, Y, edge=K)


def stickers(lv):
    """The cartoon decals on the sides."""
    side_mark_at(lv, "goat-sticker", -0.1, 0.56, 0.42, 8)
    side_mark_at(lv, "gg-sticker", 0.2, 0.3, 0.16, -3)
    side_mark_at(lv, "burst-30g", 2.0, 0.48, 0.4, -10)
    side_mark_at(lv, "bolt", 0.98, 0.5, 0.42, -15)
    side_mark_at(lv, "bubble", -1.95, 0.5, 0.22, 6)
    side_mark_at(lv, "drop", -1.5, 0.78, 0.13, -12)


def logos_top(lv, team="team", tail="goats-milk", tail_h=0.07, bolts=False):
    lv.mark("top", MARKS / "roundel.svg", (0, 0, 1.55), 0.72, rotate=-90)
    lv.mark("top", MARKS / f"{team}.svg", (0, 0, 0.12), 0.42, rotate=-90)
    lv.mark("top", MARKS / f"{tail}.svg", (0, 0, -1.97), tail_h, rotate=90)
    if bolts:
        lv.mark("top", MARKS / "bolt.svg", (0.62, 0, 2.05), 0.22, rotate=-70)
        lv.mark("top", MARKS / "bolt.svg", (-0.62, 0, 2.05), 0.22, rotate=-110)


# --- Options ----------------------------------------------------------------------------------
def spilt_milk(views):
    """Green, with milk poured over the roof running down every panel in drips. The drink's
    roundel and the number share the door, the name rides the sill."""
    lv = Livery(views, G)
    milk_sides(lv)
    milk_top(lv)
    logos_top(lv)
    milk_ends(lv)
    lv.mark("front", MARKS / "roundel.svg", (0, 0.47, 0), 0.2)
    lv.mark("rear", MARKS / "limit.svg", (0, 0.575, 0), 0.042)
    lv.side_poly(rounded(0.5, 0.53, 0.5, 0.33, 0.04), M)
    lv.side_number(0.5, 0.53, 0.42, 0.24, K)
    side_mark_at(lv, "roundel", -0.06, 0.53, 0.42)
    side_mark_at(lv, "name-black", 0.1, 0.17, 0.085)
    side_mark_at(lv, "team", -1.98, 0.5, 0.22)
    return lv


def overload(views):
    """Black, sticker-bombed: green halftone rising from the sills, magenta slashes across the
    shut lines, and cartoon decals piled over the doors and haunches."""
    lv = Livery(views, K)
    halftone(lv, G)
    slashes(lv)
    cartoon_number(lv)
    stickers(lv)
    logos_top(lv, "team-light", "overstim", 0.09, bolts=True)
    lv.mark("front", MARKS / "goat-sticker.svg", (0, 0.47, 0), 0.22)
    lv.mark("rear", MARKS / "protein.svg", (0, 0.575, 0), 0.06)
    return lv


def milk_riot(views):
    """Overload's black, halftone, slashes and stickers, with Spilt Milk's pour over the roof."""
    lv = Livery(views, K)
    halftone(lv, G, top=False)
    milk_sides(lv, door_cap=0.04)
    slashes(lv, top=False)
    milk_top(lv, PK)
    milk_ends(lv)
    cartoon_number(lv)
    stickers(lv)
    logos_top(lv, "team", "overstim", 0.09, bolts=True)
    lv.mark("front", MARKS / "goat-sticker.svg", (0, 0.47, 0), 0.22)
    lv.mark("rear", MARKS / "protein.svg", (0, 0.575, 0), 0.06)
    return lv


OPTIONS = {"spilt-milk": spilt_milk, "overload": overload, "milk-riot": milk_riot}

if __name__ == "__main__":
    views = HERE / "out" / "views" / "views.json"
    for name, make in OPTIONS.items():
        make(views).write(HERE / "out" / name)
