"""Team Archer on the Archer Tachyon: papaya, a white door roundel with a black number, and twin
white stripes edged in black from nose to tail. Writes one folder of panels per option and team
logo under out/.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/ArcherTachyon/ArcherTachyon.obj out/views
     python3 marks.py
     python3 design.py

Side art is given as (z, y) in the car's metres: z runs nose (+2.39) to tail (-2.12), y up from
the ground. Landmarks on the Tachyon: front wheel at z 1.34, rear at -1.34 (arch tops near y
0.72); door from z 1.05 to -0.18 at mid height, y 0.26 to the window at 0.80; side intake z -0.3
to -0.9, y 0.31 to 0.77; body bottom at y 0.13 between the wheels.
"""
import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
MARKS = HERE / "out" / "marks"
WORDMARK = HERE.parents[2] / "src" / "renderer" / "assets" / "brands" / "archer" / "archer-wordmark-colour.svg"
sys.path.insert(0, str(HERE.parent / "common"))
from livery import Livery  # noqa: E402

PAPAYA = "#ff7f1e"
WHITE = "#f7f5f0"
BLACK = "#141414"

# Door roundel and the number inside it.
ROUND_Z, ROUND_Y, ROUND_R = 0.44, 0.53, 0.21
NUM_W, NUM_H = 0.30, 0.20
# Archer on the sill between the wheels, the team logo on the haunch over the side intake.
WORD_Z, WORD_Y, WORD_H = 0.45, 0.20, 0.09
LOGO_Z, LOGO_Y = -1.30, 0.79  # on the haunch: clear of the rear arch, below where it turns to the roof
LOGO_H = {"logo1": 0.13}
# Twin stripes: white from 0.035 to 0.235 off the centre line, black edge out to 0.01 and 0.26.
STRIPE = (0.035, 0.235)
EDGE = (0.010, 0.260)
SILL_Y, PIN = 0.30, 0.012


def stripes(lv):
    for (a, b), col in ((EDGE, BLACK), (STRIPE, WHITE)):
        for s in (1, -1):
            lv.band_x(s * a, s * b, col)


def roundel(lv):
    pts = [(ROUND_Z + ROUND_R * math.cos(t), ROUND_Y + ROUND_R * math.sin(t))
           for t in (i / 72 * 2 * math.pi for i in range(72))]
    lv.side_poly(pts, WHITE)
    lv.side_number(ROUND_Z, ROUND_Y, NUM_W, NUM_H, BLACK)


def branding(lv, logo, word_ink):
    lv.side_mark(WORDMARK, WORD_Z, WORD_Y, WORD_H, ink=word_ink)
    lv.side_mark(MARKS / f"{logo}.svg", LOGO_Z, LOGO_Y, LOGO_H[logo])


def clubman(views, logo):
    lv = Livery(views, PAPAYA)
    stripes(lv)
    roundel(lv)
    branding(lv, logo, BLACK)
    return lv


def black_sills(views, logo):
    """Black below the crease that runs from the front arch into the side intake, a white pinline on top.
    The base is black, so the sill stays black where it turns under the car."""
    lv = Livery(views, BLACK)
    lv.band_x(-3, 3, PAPAYA)
    lv.side_poly([(3, 2), (-3, 2), (-3, -0.1), (3, -0.1)], PAPAYA)
    lv.side_poly([(3, SILL_Y), (-3, SILL_Y), (-3, -0.1), (3, -0.1)], BLACK)
    lv.side_poly([(3, SILL_Y + PIN), (-3, SILL_Y + PIN), (-3, SILL_Y), (3, SILL_Y)], WHITE)
    lv.band_x(-1.2, 1.2, BLACK, views=("front", "rear"), y=(-0.1, SILL_Y))
    lv.band_x(-1.2, 1.2, WHITE, views=("front", "rear"), y=(SILL_Y, SILL_Y + PIN))
    stripes(lv)
    roundel(lv)
    branding(lv, logo, WHITE)
    return lv


OPTIONS = {"clubman": clubman, "black-sills": black_sills}
LOGOS = ("logo1",)

if __name__ == "__main__":
    views = HERE / "out" / "views" / "views.json"
    for name, make in OPTIONS.items():
        for logo in LOGOS:
            make(views, logo).write(HERE / "out" / f"{name}-{logo}")
