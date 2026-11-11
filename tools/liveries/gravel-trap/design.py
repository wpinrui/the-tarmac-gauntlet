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
BUMPER_LINE = "oh no we did it again"

INK = "#1d1b17"
WHITE = "#f6f3ea"
MUD = ["#5b4630", "#7a6040", "#3f3123", "#8a7556"]

# Collier landmarks in OBJ metres: z along the car (+ nose), y up. Read off the true-scale views.
FRONT_AXLE, REAR_AXLE, WHEEL_R = 1.268, -1.27, 0.34
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


def mud_patch(lv, view, centre, radii, seed, fill="#4a3826"):
    """A rough patch of mud with soft edges: a ragged blob, blurred, with clots and spatter
    round it. centre and radii are (u, v) in the view's own metres."""
    r = random.Random(seed)
    cu, cv = centre
    ru, rv = radii
    waves = [(k, r.uniform(0.04, 0.16), r.uniform(0, 6.3)) for k in (2, 3, 5, 7, 11)]
    pts = []
    for i in range(96):
        a = i / 96 * 2 * math.pi
        k = 1 + sum(amp * math.sin(n * a + ph) for n, amp, ph in waves)
        pts.append((cu + math.cos(a) * ru * k, cv + math.sin(a) * rv * k))
    d = "M" + " L".join(f"{u:.4f},{v:.4f}" for u, v in pts) + " Z"
    blur = 0.03 * min(ru, rv) / 0.3
    art = lv.art[view]
    art.append(f'<defs><filter id="soft-{view}-{seed}" x="-30%" y="-30%" width="160%" height="160%">'
               f'<feGaussianBlur stdDeviation="{blur:.4f}"/></filter></defs>')
    blobs = [f'<path d="{d}"/>']
    for _ in range(14):  # clots breaking off the edge
        a = r.uniform(0, 2 * math.pi)
        k = r.uniform(0.85, 1.25)
        blobs.append(f'<ellipse cx="{cu + math.cos(a) * ru * k:.4f}" cy="{cv + math.sin(a) * rv * k:.4f}" '
                     f'rx="{ru * r.uniform(0.08, 0.22):.4f}" ry="{rv * r.uniform(0.08, 0.22):.4f}"/>')
    art.append(f'<g fill="{fill}" opacity="0.92" filter="url(#soft-{view}-{seed})">{"".join(blobs)}</g>')
    for _ in range(160):  # spatter thrown past the edge
        a = r.uniform(0, 2 * math.pi)
        k = 1.0 + r.random() ** 1.5 * 0.6
        art.append(f'<circle cx="{cu + math.cos(a) * ru * k:.4f}" cy="{cv + math.sin(a) * rv * k:.4f}" '
                   f'r="{0.003 + r.random() * 0.012 * (1.6 - k):.4f}" fill="{r.choice(MUD)}" opacity="0.8"/>')


def marks(lv, door_ink=None, plate_fill=WHITE, num_ink=INK, bonnet_ink=WHITE):
    lv.side_mark(LOGO, LOGO_Z, LOGO_Y, LOGO_H, door_ink)
    plate(lv, NUM_Z, NUM_Y, NUM_W + 0.1, NUM_H + 0.08, plate_fill)
    lv.side_number(NUM_Z, NUM_Y, NUM_W, NUM_H, num_ink)
    lv.number("top", BONNET_NUM, 0.46, 0.30, bonnet_ink, rotate=-90)


def debris_flag(views):
    """Marshal's slippery-surface flag: yellow, red stripes from the door's rear edge to the tail,
    a clear boot lid, mud on the bonnet."""
    Y, R = "#f2c200", "#d62718"
    lv = Livery(views, Y)
    for i in range(7):
        z1 = -0.50 - i * 0.24
        lv.side_poly([(z1, -0.1), (z1 - 0.12, -0.1), (z1 - 0.12, 1.7), (z1, 1.7)], R)
        lv.poly("top", [(-1.2, 0, z1), (1.2, 0, z1), (1.2, 0, z1 - 0.12), (-1.2, 0, z1 - 0.12)], R)
    for i in range(8):
        x = -0.90 + i * 0.24
        lv.poly("rear", [(x, 0.47, 0), (x + 0.12, 0.47, 0), (x + 0.12, 1.7, 0), (x, 1.7, 0)], R)
    mud_patch(lv, "top", lv.uv("top", (0.0, 0, 1.33)), (0.40, 0.50), 31)  # on the bonnet
    mud_patch(lv, "front", lv.uv("front", (0.0, 0.90, 0)), (0.52, 0.11), 32)
    lv.poly("rear", [(x, y, 0) for x, y in BOOT_LID], Y)  # stripes stop at the boot lid
    lv.mark("rear", LOGO, (0.10, 0.68, 0), 0.30)  # nudged off the Collier script
    # Across the rear bumper's plate recess (x -0.81 to 0.81, y 0.25 to 0.47), padded each side.
    lv.text("rear", BUMPER_LINE, (0, 0.36, 0), 0.075, INK, font="Marker")
    mud(lv, 11)
    marks(lv)
    return lv


OPTIONS = {"debris-flag": debris_flag}

if __name__ == "__main__":
    views = HERE / "out" / "views" / "views.json"
    for name, make in OPTIONS.items():
        make(views).write(HERE / "out" / name)
