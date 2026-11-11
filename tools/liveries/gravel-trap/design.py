"""Gravel Trap Racing on the Liberty Collier: a scrappy club team, rally dirty and proud of it.
Writes one folder of panels per option under out/.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/LibertyCollier/LibertyCollier.obj out/views
     python3 design.py
"""
import math
import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "common"))
import livery  # noqa: E402
from livery import Livery  # noqa: E402

livery.FONTS["Marker"] = HERE / "fonts" / "PermanentMarker-Regular.ttf"
livery.FONTS["Caveat"] = HERE / "fonts" / "Caveat.ttf"
LOGO = HERE / "logo.svg"
BOOT_LINE = "oh no we did it again"

INK = "#1d1b17"
WHITE = "#f6f3ea"
MUD = ["#5b4630", "#7a6040", "#3f3123", "#8a7556"]

# Collier landmarks in OBJ metres: z along the car (+ nose), y up. Read off the true-scale views.
FRONT_AXLE, REAR_AXLE, WHEEL_R = 1.268, -1.27, 0.34
# Bonnet seen from above (z, x) and from the front (x, y).
BONNET_TOP = [(0.87, 0.66), (1.38, 0.66), (1.55, 0.60), (1.70, 0.47), (1.80, 0.25), (1.82, 0.0),
              (1.80, -0.25), (1.70, -0.47), (1.55, -0.60), (1.38, -0.66), (0.87, -0.66)]
BONNET_FRONT = [(-0.68, 1.06), (0.68, 1.06), (0.64, 0.85), (0.45, 0.75), (-0.45, 0.75), (-0.64, 0.85)]
# Boot lid below the rear window (x, y).
BOOT_LID = [(0.60, 1.01), (-0.60, 1.01), (-0.50, 0.46), (0.50, 0.46)]

# Door: team logo above, race number below. Bonnet number reads from the nose.
LOGO_Z, LOGO_Y, LOGO_H = 0.22, 0.73, 0.32
NUM_Z, NUM_Y, NUM_W, NUM_H = 0.22, 0.40, 0.40, 0.26
BONNET_NUM = (0.0, 0, 1.30)


def one_side(lv, view, zy, fill):
    lv.poly(view, [lv._side(view, z, y) for z, y in zy], fill)


def dot(lv, view, p, r, fill, opacity=1.0):
    u, v = lv.uv(view, p)
    lv.art[view].append(f'<circle cx="{u:.4f}" cy="{v:.4f}" r="{r:.4f}" fill="{fill}" opacity="{opacity:.2f}"/>')


def plate(lv, z, y, w, h, fill, views=("left", "right")):
    c = 0.035
    z0, z1, y0, y1 = z - w / 2, z + w / 2, y - h / 2, y + h / 2
    pts = [(z0 + c, y0), (z1 - c, y0), (z1, y0 + c), (z1, y1 - c), (z1 - c, y1), (z0 + c, y1), (z0, y1 - c), (z0, y0 + c)]
    for view in views:
        one_side(lv, view, pts, fill)


def mud(lv, seed, amount=1.0):
    """Dirt kicked up off the tyres: dense on the sills, fanning back and up behind each wheel,
    thinner on the bumpers. Each side gets its own spray."""
    for view in ("left", "right"):
        r = random.Random(seed + (0 if view == "left" else 1))
        for _ in range(int(900 * amount)):  # sills
            t = r.random() ** 2.2
            dot(lv, view, lv._side(view, r.uniform(-2.0, 2.1), 0.13 + t * 0.32), 0.004 + r.random() * 0.012 * (1 - t),
                r.choice(MUD), 0.45 + 0.5 * (1 - t))
        for axle in (FRONT_AXLE, REAR_AXLE):  # flicks thrown back off the tread
            for _ in range(int(260 * amount)):
                a, d = r.uniform(0.1, 1.2), WHEEL_R + r.random() ** 0.7 * 0.65
                z, y = axle - math.cos(a) * d, 0.30 + math.sin(a) * d * 0.55
                dot(lv, view, lv._side(view, z, y), 0.003 + r.random() * 0.008, r.choice(MUD), 0.55)
    for view, sign in (("front", 1), ("rear", -1)):
        r = random.Random(seed + 7 + sign)
        for _ in range(int(500 * amount)):
            t = r.random() ** 2
            dot(lv, view, (r.uniform(-0.95, 0.95), 0.12 + t * 0.3, 0), 0.004 + r.random() * 0.01 * (1 - t), r.choice(MUD), 0.4 + 0.5 * (1 - t))


def marks(lv, door_ink=None, plate_fill=WHITE, num_ink=INK, bonnet_ink=WHITE):
    lv.side_mark(LOGO, LOGO_Z, LOGO_Y, LOGO_H, door_ink)
    plate(lv, NUM_Z, NUM_Y, NUM_W + 0.1, NUM_H + 0.08, plate_fill)
    lv.side_number(NUM_Z, NUM_Y, NUM_W, NUM_H, num_ink)
    lv.number("top", BONNET_NUM, 0.46, 0.30, bonnet_ink, rotate=-90)


def debris_flag(views):
    """Marshal's slippery-surface flag: yellow, red stripes from the door's rear edge to the tail,
    black bonnet."""
    Y, R, K = "#f2c200", "#d62718", "#151412"
    lv = Livery(views, Y)
    for i in range(7):
        z1 = -0.50 - i * 0.24
        lv.side_poly([(z1, -0.1), (z1 - 0.12, -0.1), (z1 - 0.12, 1.7), (z1, 1.7)], R)
        lv.poly("top", [(-1.2, 0, z1), (1.2, 0, z1), (1.2, 0, z1 - 0.12), (-1.2, 0, z1 - 0.12)], R)
    for i in range(8):
        x = -0.90 + i * 0.24
        lv.poly("rear", [(x, 0.47, 0), (x + 0.12, 0.47, 0), (x + 0.12, 1.7, 0), (x, 1.7, 0)], R)
    lv.poly("top", [(x, 0, z) for z, x in BONNET_TOP], K)
    lv.poly("front", [(x, y, 0) for x, y in BONNET_FRONT], K)
    lv.poly("rear", [(x, y, 0) for x, y in BOOT_LID], Y)  # stripes stop at the boot lid
    lv.mark("rear", LOGO, (0, 0.72, 0), 0.24)
    lv.text("rear", BOOT_LINE, (0, 0.565, 0), 0.042, INK, font="Marker")
    mud(lv, 11)
    marks(lv)
    return lv


OPTIONS = {"debris-flag": debris_flag}

if __name__ == "__main__":
    views = HERE / "out" / "views" / "views.json"
    for name, make in OPTIONS.items():
        make(views).write(HERE / "out" / name)
