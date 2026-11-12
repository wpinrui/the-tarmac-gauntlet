"""Greystone Archer on the Archer Crescendo: a satellite trying too hard to look like the works
team. Team Archer's colours turned round (white with papaya, not papaya with white), twin papaya
stripes edged in black, the team's stone A huge on the bonnet, Archer along the rear doors, and a stone wall from the
sills down. Writes one folder of panels per option under out/.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/ArcherCrescendo/ArcherCrescendo.obj out/views
     python3 marks.py
     python3 design.py

Landmarks on the Crescendo, in OBJ metres (z along the car, nose +2.23 to tail -2.50, y up):
wheels at z +-1.38 (radius 0.32, arch 0.36); front door z 0.60 to -0.41, rear door to -1.40,
door bottoms at y 0.32 and the belt line near 0.95; body bottom at y 0.16 between the wheels;
bonnet shut line near z 0.85, bonnet clear of the headlamps for |x| under 0.70.
"""
import math
import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
MARKS = HERE / "out" / "marks"
BRAND = HERE.parents[2] / "src" / "renderer" / "assets" / "brands" / "archer"
sys.path.insert(0, str(HERE.parent / "common"))
from livery import Livery  # noqa: E402

WHITE = "#f7f5f0"
PAPAYA = "#ff7f1e"
BLACK = "#141414"
# Grey stone, each block one of these shades so the wall reads as stone, not paint.
STONES = ("#8d8f8c", "#9a9c98", "#80827f", "#a6a7a3")

# Twin stripes nose to tail: papaya from 0.035 to 0.235 off the centre line, edged in black out to 0.01 and 0.26.
STRIPE = (0.035, 0.235)
EDGE = (0.010, 0.260)
# Front door roundel and the number inside it.
ROUND_Z, ROUND_Y, ROUND_R, RING = 0.08, 0.70, 0.21, 0.025
NUM_W, NUM_H = 0.30, 0.20
# Archer along the rear door, the team logo on the front wing behind the arch.
WORD_Z, WORD_Y, WORD_H = -0.84, 0.74, 0.14
LOGO_Z, LOGO_Y, LOGO_H = 0.82, 0.72, 0.24
# The team's stone A across the bonnet, its top towards the windscreen.
BADGE_Z, BADGE_H = 1.40, 0.92
# A wall in white mortar from the ground up past the bottoms of the doors.
WALL_TOP, JOINT = 0.45, 0.012
BRICK = (0.21, 0.065)    # papaya brick, length and height
STONE = (0.30, 0.085)    # grey stone blocks


def stripes(lv):
    for (a, b), col in ((EDGE, BLACK), (STRIPE, PAPAYA)):
        for s in (1, -1):
            lv.band_x(s * a, s * b, col)


def circle(z, y, r):
    return [(z + r * math.cos(t), y + r * math.sin(t)) for t in (i / 96 * 2 * math.pi for i in range(96))]


def roundel(lv):
    lv.side_poly(circle(ROUND_Z, ROUND_Y, ROUND_R + RING), BLACK)
    lv.side_poly(circle(ROUND_Z, ROUND_Y, ROUND_R), PAPAYA)
    lv.side_number(ROUND_Z, ROUND_Y, NUM_W, NUM_H, WHITE)


def wall(lv, block, shades):
    """Blocks (length, height) in running bond, laid from WALL_TOP down, on every view but
    the top. Each block takes one of shades at random, the same on every bake."""
    rng = random.Random(7)
    bl, bh = block
    for view, f in lv.views.items():
        if view == "top":
            continue
        u0, u1, v1 = f["u0"], f["u1"], f["v1"]
        top = -WALL_TOP                      # v runs down: v = -y
        out = [f'<rect x="{u0:.4f}" y="{top:.4f}" width="{u1 - u0:.4f}" height="{v1 - top:.4f}" fill="{WHITE}"/>']
        row, v = 0, top + JOINT
        while v < v1:
            shift = (row % 2) * (bl + JOINT) / 2
            u = u0 - bl + shift
            while u < u1:
                out.append(f'<rect x="{u:.4f}" y="{v:.4f}" width="{bl:.4f}" height="{bh:.4f}" fill="{rng.choice(shades)}"/>')
                u += bl + JOINT
            v += bh + JOINT
            row += 1
        lv.raw(view, "".join(out))


def more_archer(views, block, shades):
    lv = Livery(views, WHITE)
    stripes(lv)
    wall(lv, block, shades)
    roundel(lv)
    lv.mark("top", MARKS / "badge.svg", (0, 0, BADGE_Z), BADGE_H, rotate=-90)
    lv.side_mark(BRAND / "archer-wordmark-colour.svg", WORD_Z, WORD_Y, WORD_H)
    lv.side_mark(MARKS / "logo7.svg", LOGO_Z, LOGO_Y, LOGO_H)
    return lv


OPTIONS = {
    "stone": (STONE, STONES),     # Ivan's pick
    "brick": (BRICK, (PAPAYA,)),
}

if __name__ == "__main__":
    views = HERE / "out" / "views" / "views.json"
    for name, wall_kind in OPTIONS.items():
        more_archer(views, *wall_kind).write(HERE / "out" / name)
