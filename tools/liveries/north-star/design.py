"""North Star Racing on the Kaiser Oboe: winter wilderness, the shield logo and the pole star.
Independent, so no maker's logo. Writes one folder of panels per option under out/.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/KaiserOboe/KaiserOboe.obj out/views
     python3 marks.py
     python3 design.py
     ./game.sh

Side art is given as (z, y) in the car's metres: z runs nose (+2.19) to tail (-2.03), y up from
the ground. Landmarks on the Oboe: front wheel at z 1.29, rear at -1.29 (radius 0.32, arch tops
at y 0.71); door from z 0.84 to -0.45, y 0.27 to the window line at 0.89; rubbing strip at y 0.53;
bonnet z 1.19 to the nose; windscreen z 0.29 to 1.19; roof z -1.6 to 0.29, x within 0.6.
Rear: tailgate y 0.63 to 1.03, bumper below.
"""
import math
import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
MARKS = HERE / "out" / "marks"
sys.path.insert(0, str(HERE.parent / "common"))
from livery import Livery  # noqa: E402

SNOW = "#f2f6f8"
FROST = "#dceef4"
ICE = "#a9d6e5"
GLACIER = "#5f9fc0"
NIGHT = "#0b1b33"
NIGHT_D = "#060f1e"
PINE = "#1f4a3a"
PINE_D = "#10291f"
GOLD = "#f2c14e"
AUR_G = "#39e6a0"
AUR_T = "#26c6c6"
AUR_V = "#8a5cf6"

FA, RA = 1.29, -1.29          # wheel centres, z
SIDES = ("left", "right")

# Shared placement: number on the door, logo over the rear quarter, both clear of the arches.
NUM_Z, NUM_Y, NUM_W, NUM_H = 0.42, 0.72, 0.46, 0.22
LOGO_Z, LOGO_Y, LOGO_H = -0.80, 0.60, 0.46
TOP_NUM = (0, 0, 1.68)        # on the bonnet, reading from the nose


# --- drawing helpers ---------------------------------------------------------------------
def raw(lv, view, svg):
    lv.art[view].append(svg)


def defs(lv, svg):
    for view in lv.views:
        raw(lv, view, f"<defs>{svg}</defs>")


def vgrad(gid, stops):
    """A gradient running down whatever it fills: stops are (offset, colour, opacity)."""
    s = "".join(f'<stop offset="{o}" stop-color="{c}" stop-opacity="{a}"/>' for o, c, a in stops)
    return f'<linearGradient id="{gid}" x1="0" y1="0" x2="0" y2="1">{s}</linearGradient>'


def side_pt(view, z, y):
    return Livery._side(view, z, y)


def pine(z, base, h):
    """One pine tree as three tiers and a trunk, (z, y) polygons."""
    hw = h * 0.2
    out = []
    for t, b, w in ((0, 0.42, 0.55), (0.22, 0.68, 0.8), (0.42, 0.92, 1.0)):
        top, bot = base + h - t * h, base + h - b * h
        out.append([(z, top), (z - hw * w, bot), (z + hw * w, bot)])
    out.append([(z - h * 0.03, base), (z + h * 0.03, base), (z + h * 0.03, base + h * 0.1), (z - h * 0.03, base + h * 0.1)])
    return out


def treeline(lv, base, hmin, hmax, col, step, seed, clear=None, span=(-2.3, 2.4)):
    """Pines along both sides. clear: (z0, z1, factor) lowers the trees there."""
    r = random.Random(seed)
    z = span[1]
    while z > span[0]:
        h = hmin + r.random() * (hmax - hmin)
        if clear and clear[0] < z < clear[1]:
            h *= clear[2]
        for tier in pine(z, base, h):
            lv.side_poly(tier, col)
        z -= step * (0.7 + r.random() * 0.6)


def flat_trees(lv, view, base, hmin, hmax, col, step, seed, width=1.1):
    """Pines across the front or rear view, x across the car."""
    r = random.Random(seed)
    x = -width
    while x < width:
        h = hmin + r.random() * (hmax - hmin)
        for tier in pine(x, base, h):
            lv.poly(view, [(px, py, 0) for px, py in tier], col)
        x += step * (0.7 + r.random() * 0.6)


def star4(cx, cy, r, k=0.18, turn=0.0):
    pts = []
    for i in range(8):
        a = math.radians(turn) - math.pi / 2 + i * math.pi / 4
        rad = r * k if i % 2 else r
        pts.append((cx + math.cos(a) * rad, cy - math.sin(a) * rad))
    return pts


def dots(lv, view, pts_uv, r, fill):
    """Small round stars, given in the view's own drawing units."""
    raw(lv, view, "".join(f'<circle cx="{u:.4f}" cy="{v:.4f}" r="{s:.4f}" fill="{fill}" fill-opacity="{a:.2f}"/>'
                          for (u, v, s, a) in pts_uv))


def starfield(lv, view, n, seed, vmin=None, vmax=None):
    f = lv.views[view]
    r = random.Random(seed)
    v0, v1 = vmin if vmin is not None else f["v0"], vmax if vmax is not None else f["v1"]
    pts = [(f["u0"] + r.random() * (f["u1"] - f["u0"]), v0 + r.random() * (v1 - v0), 0.004 + r.random() * 0.008,
            0.35 + r.random() * 0.6) for _ in range(n)]
    dots(lv, view, pts, 0, SNOW)


def fill_view(lv, view, fill):
    f = lv.views[view]
    raw(lv, view, f'<rect x="{f["u0"]}" y="{f["v0"]}" width="{f["u1"] - f["u0"]}" height="{f["v1"] - f["v0"]}" fill="{fill}"/>')


def logos(lv, light=True):
    name = "logo-light.svg" if light else "logo-dark.svg"
    for view in SIDES:
        lv.mark(view, MARKS / name, side_pt(view, LOGO_Z, LOGO_Y), LOGO_H)


def numbers(lv, ink, top_ink):
    lv.side_number(NUM_Z, NUM_Y, NUM_W, NUM_H, ink)
    lv.number("top", TOP_NUM, 0.46, 0.28, top_ink, rotate=-90)


# --- Treeline ------------------------------------------------------------------------------
def treeline_option(views):
    """Snow white, a pine forest up from the sills with a clearing for the number, the ice
    pinstripe under the window line and the gold pole star on the C-pillar."""
    lv = Livery(views, SNOW)
    lv.side_poly([(2.4, 0.835), (-2.4, 0.835), (-2.4, 0.85), (2.4, 0.85)], ICE)
    treeline(lv, 0.14, 0.34, 0.62, PINE, 0.1, 7, clear=(0.08, 0.78, 0.42))
    treeline(lv, 0.10, 0.18, 0.36, PINE_D, 0.085, 9, clear=(0.08, 0.78, 0.7))
    lv.side_poly([(2.4, -0.1), (-2.4, -0.1), (-2.4, 0.2), (2.4, 0.2)], PINE_D)
    for view in SIDES:
        lv.mark(view, MARKS / "compass-gold.svg", side_pt(view, -1.62, 1.14), 0.24)
    logos(lv)
    # Front and rear: the same forest across the bumpers.
    flat_trees(lv, "front", 0.14, 0.14, 0.3, PINE, 0.085, 4)
    flat_trees(lv, "rear", 0.14, 0.16, 0.34, PINE, 0.085, 5)
    lv.band_x(-1.1, 1.1, PINE_D, views=("front", "rear"), y=(-0.1, 0.2))
    lv.mark("rear", MARKS / "tagline-pine.svg", (0, 0.555, 0), 0.05)
    # Top: pine rails along the roof, the compass star big on the roof.
    for s in (1, -1):
        lv.poly("top", [(s * 0.66, 0, -1.7), (s * 0.72, 0, -1.7), (s * 0.72, 0, 0.3), (s * 0.66, 0, 0.3)], PINE)
    lv.mark("top", MARKS / "compass-pine.svg", (0, 0, -0.65), 0.95, rotate=-90)
    numbers(lv, PINE, PINE)
    return lv


# --- Aurora --------------------------------------------------------------------------------
AUR_GRADS = "".join(vgrad(f"aur{k}", [(0, AUR_T, 0.9 * a), (0.18, AUR_G, a), (1, AUR_G, 0)]) for k, a in enumerate((0.45, 0.65, 0.85)))
AUR_GRADS += "".join(vgrad(f"aurv{k}", [(0, AUR_V, a), (1, AUR_V, 0)]) for k, a in enumerate((0.4, 0.6, 0.8)))


def curtain(lv, top, length, grad, seed, span=(-2.3, 2.3), width=0.013):
    """Aurora rays hanging down the sides from top(z), each a thin strip fading downward."""
    r = random.Random(seed)
    z = span[1]
    while z > span[0]:
        y0 = top(z)
        ln = length * (0.6 + 0.3 * math.sin(z * 6.1 + seed) + r.random() * 0.25)
        lv.side_poly([(z, y0), (z - width, y0), (z - width, y0 - ln), (z, y0 - ln)], f"url(#{grad}{r.randrange(3)})")
        z -= width * 0.95


def aurora_option(views):
    """Polar night with the northern lights hanging from the window line and fading before the
    sills, stars above, the gold compass star on the bonnet."""
    lv = Livery(views, NIGHT)
    defs(lv, AUR_GRADS)
    for i, view in enumerate(SIDES):
        starfield(lv, view, 160, 11 + i, vmin=-1.45, vmax=-0.3)
    curtain(lv, lambda z: 0.86 - 0.05 * math.sin(z * 1.7) - 0.03 * (1 - z / 2.2), 0.5, "aur", 5)
    curtain(lv, lambda z: 0.9 + 0.04 * math.sin(z * 2.3 + 1), 0.3, "aurv", 8, span=(-2.3, 0.2))
    lv.side_poly([(2.4, -0.1), (-2.4, -0.1), (-2.4, 0.2), (2.4, 0.2)], NIGHT_D)
    logos(lv)
    # Top: stars, a green ribbon over the roof, the compass star on the bonnet ahead of the number.
    starfield(lv, "top", 120, 21)
    for k, (x0, w, op) in enumerate(((-0.55, 0.22, 0.55), (-0.2, 0.14, 0.4))):
        pts = [(x0 + 0.25 * math.sin(z * 1.4 + k), 0, z) for z in [i / 10 - 1.8 for i in range(22)]]
        pts += [(x + w, 0, z) for x, _, z in reversed(pts)]
        raw(lv, "top", '<path d="' + " ".join(f"{'M' if i == 0 else 'L'}{u:.4f},{v:.4f}" for i, (u, v) in enumerate(lv.uv("top", p) for p in pts))
            + f' Z" fill="{AUR_G if k == 0 else AUR_V}" fill-opacity="{op}"/>')
    lv.mark("top", MARKS / "compass-gold.svg", (0, 0, 1.98), 0.2, rotate=-90)
    # Front and rear: stars over a dark bumper, the tagline on the rear bumper.
    for view, seed in (("front", 31), ("rear", 32)):
        starfield(lv, view, 50, seed)
    lv.band_x(-1.1, 1.1, NIGHT_D, views=("front", "rear"), y=(-0.1, 0.2))
    lv.mark("rear", MARKS / "tagline-snow.svg", (0, 0.555, 0), 0.05)
    numbers(lv, SNOW, SNOW)
    return lv


# --- Frozen Lake ---------------------------------------------------------------------------
def cracks(lv, view, seed, n, area):
    """Frost cracks as branching white lines, in the view's drawing units."""
    r = random.Random(seed)
    u0, u1, v0, v1 = area
    out = []

    def walk(u, v, a, steps, w, depth):
        pts = [(u, v)]
        for _ in range(steps):
            a += (r.random() - 0.5) * 0.9
            u += math.cos(a) * 0.05
            v += math.sin(a) * 0.05
            pts.append((u, v))
            if depth < 2 and r.random() < 0.12:
                walk(u, v, a + (1 if r.random() < 0.5 else -1) * (0.6 + r.random() * 0.6), steps // 3, w * 0.6, depth + 1)
        out.append(f'<path d="M{" L".join(f"{p:.4f},{q:.4f}" for p, q in pts)}" fill="none" stroke="{SNOW}" '
                   f'stroke-opacity="0.85" stroke-width="{w:.4f}" stroke-linecap="round" stroke-linejoin="round"/>')

    for _ in range(n):
        walk(u0 + r.random() * (u1 - u0), v0 + r.random() * (v1 - v0), r.random() * 6.28, 14 + r.randrange(20), 0.007 + r.random() * 0.007, 0)
    raw(lv, view, "".join(out))


def drift_top(z):
    arch = max(math.exp(-((z - FA) / 0.42) ** 2), math.exp(-((z - RA) / 0.42) ** 2))
    return 0.36 + 0.16 * arch + 0.025 * math.sin(z * 11)


def frozen_option(views):
    """Lake ice: pale at the window line, glacier blue at the sills, white frost cracks, and
    snow drifting up the lower body and highest round the wheel arches."""
    lv = Livery(views, ICE)
    defs(lv, vgrad("ice", [(0, FROST, 1), (0.55, ICE, 1), (1, GLACIER, 1)]) + vgrad("icetop", [(0, ICE, 1), (1, ICE, 1)]))
    for view in lv.views:
        fill_view(lv, view, "url(#icetop)" if view == "top" else "url(#ice)")
    for i, view in enumerate(lv.views):
        f = lv.views[view]
        cracks(lv, view, 41 + i, 12 if view in SIDES else 6, (f["u0"], f["u1"], f["v0"], f["v1"]))
    zs = [2.4 - k * 0.02 for k in range(241)]
    lv.side_poly([(z, drift_top(z)) for z in zs] + [(-2.4, -0.1), (2.4, -0.1)], SNOW)
    for view in ("front", "rear"):
        xs = [-1.1 + k * 0.02 for k in range(111)]
        lv.poly(view, [(x, 0.3 + 0.025 * math.sin(x * 9 + (view == "rear")), 0) for x in xs] + [(1.1, -0.1, 0), (-1.1, -0.1, 0)], SNOW)
    logos(lv, light=False)
    lv.mark("top", MARKS / "compass-night.svg", (0, 0, -0.65), 0.95, rotate=-90)
    lv.mark("rear", MARKS / "tagline-night.svg", (0, 0.555, 0), 0.05)
    numbers(lv, NIGHT, NIGHT)
    return lv


OPTIONS = {"treeline": treeline_option, "aurora": aurora_option, "frozen-lake": frozen_option}

if __name__ == "__main__":
    views = HERE / "out" / "views" / "views.json"
    for name, make in OPTIONS.items():
        make(views).write(HERE / "out" / name)
