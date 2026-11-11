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
TAGLINE = "frequent visitors to the run-off area"

INK = "#1d1b17"
WHITE = "#f6f3ea"
MUD = ["#5b4630", "#7a6040", "#3f3123", "#8a7556"]

# Collier landmarks in OBJ metres: z along the car (+ nose), y up. Read off the true-scale views.
FRONT_AXLE, REAR_AXLE, WHEEL_R = 1.268, -1.27, 0.34
# The door, as its own mesh outlines it, frame included.
DOOR = [(-0.436, 0.95), (-0.414, 1.383), (-0.25, 1.391), (-0.096, 1.391), (0.188, 1.354), (0.28, 1.32),
        (0.427, 1.248), (0.721, 1.088), (0.812, 1.019), (0.83, 0.925), (0.854, 0.783), (0.869, 0.596),
        (0.838, 0.347), (0.792, 0.199), (0.736, 0.18), (-0.321, 0.18), (-0.402, 0.193), (-0.418, 0.41)]
# Front wing: door front edge forward to the headlamp, down past the arch.
WING = [(0.83, 1.0), (0.854, 0.783), (0.869, 0.596), (0.838, 0.347), (0.792, 0.13), (1.62, 0.13),
        (1.62, 0.68), (1.50, 0.76), (1.39, 0.86), (1.20, 1.06)]
# Rear quarter: door rear edge back to the tail lamp and bumper.
QUARTER = [(-0.42, 0.19), (-0.43, 0.96), (-1.0, 0.99), (-1.47, 0.99), (-1.607, 0.677), (-1.75, 0.47),
           (-1.75, 0.13), (-0.42, 0.13)]
# Bonnet seen from above (z, x) and from the front (x, y).
BONNET_TOP = [(0.87, 0.66), (1.38, 0.66), (1.55, 0.60), (1.70, 0.47), (1.80, 0.25), (1.82, 0.0),
              (1.80, -0.25), (1.70, -0.47), (1.55, -0.60), (1.38, -0.66), (0.87, -0.66)]
BONNET_FRONT = [(-0.68, 1.06), (0.68, 1.06), (0.64, 0.85), (0.45, 0.75), (-0.45, 0.75), (-0.64, 0.85)]
# Tailgate below the rear window (x, y).
TAILGATE = [(0.595, 1.01), (-0.595, 1.01), (-0.47, 0.515), (0.47, 0.515)]

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
    lv.text("rear", TAGLINE, (0, 0.37, 0), 0.06, INK, font="Caveat")
    mud(lv, 11)
    marks(lv)
    return lv


def spare_parts(views):
    """A white car rebuilt from whatever was in the shed: blue doors, red-oxide bonnet, a primer
    wing on the left and a primer quarter on the right, tape on the knocked corners, the crew's
    names on the back."""
    Wt, P, B, OX, T, TE = "#f2efe6", "#8e8d84", "#5b86b0", "#9a4a34", "#b9bab4", "#8f908a"
    lv = Livery(views, Wt)
    lv.side_poly(DOOR, B)
    one_side(lv, "left", WING, P)
    one_side(lv, "right", QUARTER, P)
    lv.poly("top", [(x, 0, z) for z, x in BONNET_TOP], OX)
    lv.poly("front", [(x, y, 0) for x, y in BONNET_FRONT], OX)
    lv.poly("rear", [(x, y, 0) for x, y in TAILGATE], P)
    lv.text("rear", TAGLINE, (0, 0.60, 0), 0.05, INK, font="Marker")

    def tape(view, cx, cy, ang, length=0.36, w=0.075):
        c, s = math.cos(math.radians(ang)), math.sin(math.radians(ang))
        pts = [(cx + dx * c - dy * s, cy + dx * s + dy * c) for dx, dy in
               ((-length / 2, -w / 2), (length / 2, -w / 2), (length / 2, w / 2), (-length / 2, w / 2))]
        edge = [(cx + dx * c - dy * s, cy + dx * s + dy * c) for dx, dy in
                ((-length / 2, -w / 2 - 0.006), (length / 2, -w / 2 - 0.006), (length / 2, w / 2 + 0.006), (-length / 2, w / 2 + 0.006))]
        if view in ("left", "right"):
            one_side(lv, view, edge, TE)
            one_side(lv, view, pts, T)
        else:
            lv.poly(view, [(u, v, 0) for u, v in edge], TE)
            lv.poly(view, [(u, v, 0) for u, v in pts], T)

    for view in ("left", "right"):
        tape(view, 1.93, 0.36, 50)
        tape(view, -1.86, 0.33, -45)
    tape("front", 0.78, 0.33, -55)
    tape("front", -0.78, 0.33, 55)
    tape("rear", -0.78, 0.33, -55)
    crew = [("Dee", -0.80, 0.80), ("Big Sal", -1.15, 0.66), ("Moss", -0.75, 0.52), ("Jojo", -1.25, 0.86)]
    for view, ink in (("left", "#2d4a6e"), ("right", INK)):
        for name, z, y in crew:
            lv.text(view, name, lv._side(view, z, y), 0.055, ink, rotate=-6, font="Caveat")
    mud(lv, 21, 0.8)
    marks(lv, door_ink=WHITE)
    return lv


OPTIONS = {"debris-flag": debris_flag, "spare-parts": spare_parts}

if __name__ == "__main__":
    views = HERE / "out" / "views" / "views.json"
    for name, make in OPTIONS.items():
        make(views).write(HERE / "out" / name)
