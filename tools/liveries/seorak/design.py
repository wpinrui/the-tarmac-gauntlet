"""Team Seorak on the Seorak Cheonma CX-24Si: pearl white and Seorak navy, Korean executive.
Writes one folder of panels per option under out/.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/CheonmaCX24Si/CheonmaCX24Si.obj out/views
     python3 lockup.py
     python3 design.py
"""
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
MARKS = HERE / "out" / "marks"
sys.path.insert(0, str(HERE.parent / "common"))
from livery import Livery  # noqa: E402

PEARL = "#eef0f2"
NAVY = "#1c2a3a"
GOLD = "#d9a443"
RED = "#c63d2a"  # the A's core

# Team stack on the door under the window, number on the front fender.
STACK_Z, STACK_Y, STACK_H = -0.03, 0.425, 0.25
NUM_Z, NUM_Y, NUM_W, NUM_H = 0.80, 0.34, 0.44, 0.24


def marks(lv):
    """Fender and nose numbers, the team stack on the door and 설악 on the nose."""
    lv.side_number(NUM_Z, NUM_Y, NUM_W, NUM_H, NAVY)
    lv.number("top", (0, 0, 1.45), 0.46, 0.28, NAVY, rotate=-90)  # reads from the nose
    for view in ("left", "right"):
        lv.mark(view, MARKS / "team-seorak-stack-navy.svg", lv._side(view, STACK_Z, STACK_Y), STACK_H)
    lv.mark("top", MARKS / "seorak-hangul-only-navy.svg", (0, 0, 2.02), 0.17, rotate=-90)  # nose tip, under the number


RIDGE = [(2.5, 0.20), (1.95, 0.25), (1.60, 0.20), (1.20, 0.24), (0.85, 0.19), (0.45, 0.23), (0.05, 0.19),
         (-0.30, 0.25), (-0.52, 0.27), (-0.70, 0.50), (-0.82, 0.44), (-1.05, 0.80), (-1.30, 0.58),
         (-1.48, 0.66), (-1.80, 0.44), (-2.05, 0.50), (-2.5, 0.38)]


# Seorak's A from the wordmark (units: base 88 wide, 72 tall), laid on the rear deck as the base
# layer: its base spans the car at A_BASE_Z, its point meets the windscreen at A_APEX_Z.
A_OUTER = [(-44, 0), (0, 72), (44, 0), (22.15, 0), (0, 32.1), (-22.15, 0)]
A_CORE = [(0, 30.24), (16, 0), (-16, 0)]
A_BASE_Z, A_APEX_Z, A_HALF = -0.876, 0.424, 0.98  # point on the windscreen's top edge


def seorak_a(lv):
    """The A across the whole width of the top, point forward."""
    def place(pts):
        return [(x / 44 * A_HALF, 0, A_BASE_Z + y / 72 * (A_APEX_Z - A_BASE_Z)) for x, y in pts]
    # Carry the base a little past the body's edges so it reaches the sides cleanly.
    over = 0.10 / (A_APEX_Z - A_BASE_Z) * 72
    outer = [(-44 - 44 / 72 * over, -over), (0, 72), (44 + 44 / 72 * over, -over),
             (22.15 + 22.15 / 32.1 * over, -over), (0, 32.1), (-22.15 - 22.15 / 32.1 * over, -over)]
    lv.poly("top", place(outer), NAVY)
    lv.poly("top", place(A_CORE), RED)


def ridgeline(views):
    """A navy ridge of peaks, low along the sill and rising to its tallest over the rear haunch,
    traced in gold."""
    lv = Livery(views, PEARL)
    gold = [(z, y + 0.018) for z, y in RIDGE]
    lv.side_poly(gold + [(-2.5, -0.1), (2.5, -0.1)], GOLD)
    lv.side_poly(RIDGE + [(-2.5, -0.1), (2.5, -0.1)], NAVY)
    lv.band_x(-1.2, 1.2, NAVY, views=("front",), y=(-0.1, 0.20))
    lv.band_x(-1.2, 1.2, GOLD, views=("front",), y=(0.20, 0.218))
    seorak_a(lv)
    lv.band_x(-1.2, 1.2, NAVY, views=("rear",), y=(-0.1, 0.38))
    lv.band_x(-1.2, 1.2, GOLD, views=("rear",), y=(0.38, 0.398))
    marks(lv)
    return lv


OPTIONS = {"ridgeline": ridgeline}

if __name__ == "__main__":
    views = HERE / "out" / "views" / "views.json"
    for name, make in OPTIONS.items():
        make(views).write(HERE / "out" / name)
