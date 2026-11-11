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
RED = "#c63d2a"  # the badge's core

# Side art keeps out of the rear haunch (z -1.7 to -0.55): the vent channels behind the cockpit
# have walls facing inward that take the side views, and anything drawn there shows on them
# back to front. Flat colour is fine there; marks and numbers are not.
# Team stack on the door under the window, number on the front fender.
STACK_Z, STACK_Y, STACK_H = -0.03, 0.425, 0.25
NUM_Z, NUM_Y, NUM_W, NUM_H = 0.80, 0.38, 0.44, 0.26


def common(lv, rear_ink):
    """Marks every option shares: fender and nose numbers, the team stack on the door, the
    team line across the rear deck and 설악 on the engine cover."""
    lv.side_number(NUM_Z, NUM_Y, NUM_W, NUM_H, NAVY)
    lv.number("top", (0, 0, 1.45), 0.46, 0.28, NAVY, rotate=-90)  # reads from the nose
    for view in ("left", "right"):
        lv.mark(view, MARKS / "team-seorak-stack-navy.svg", lv._side(view, STACK_Z, STACK_Y), STACK_H)
    lv.mark("top", MARKS / "team-seorak-navy.svg", (0, 0, -1.95), 0.07, rotate=90)  # rear deck, reads from behind
    lv.mark("top", MARKS / f"seorak-hangul-{rear_ink}.svg", (0, 0, -0.86), 0.21, rotate=90)  # engine cover


def summit(views):
    """The badge's peak on the rear haunch, its legs landing either side of the rear arch, with
    the badge's red core between them."""
    lv = Livery(views, PEARL)
    apex, base, t = (-1.05, 0.86), 0.06, 0.15
    front, back = -0.45, -1.65
    outer = [(front, base), apex, (back, base)]
    inner = [(back + t * 1.5, base), (apex[0], apex[1] - t * 1.6), (front - t * 1.5, base)]
    lv.side_poly(outer + inner, NAVY)
    lv.side_poly([(apex[0], 0.40), (apex[0] + 0.25, base), (apex[0] - 0.25, base)], RED)
    # A navy spine edged in gold, nose to engine cover, broken for the nose number.
    for z in ((-0.62, 1.15), (1.75, 9)):
        lv.band_x(-0.12, 0.12, NAVY, views=("top",), z=z)
        lv.band_x(-0.145, -0.125, GOLD, views=("top",), z=z)
        lv.band_x(0.125, 0.145, GOLD, views=("top",), z=z)
    lv.band_x(-0.12, 0.12, NAVY, views=("front",))
    lv.band_x(-0.145, -0.125, GOLD, views=("front",))
    lv.band_x(0.125, 0.145, GOLD, views=("front",))
    common(lv, "navy")
    return lv


RIDGE = [(2.5, 0.20), (1.95, 0.25), (1.60, 0.20), (1.20, 0.24), (0.85, 0.19), (0.45, 0.23), (0.05, 0.19),
         (-0.30, 0.25), (-0.52, 0.27), (-0.70, 0.50), (-0.82, 0.44), (-1.05, 0.80), (-1.30, 0.58),
         (-1.48, 0.66), (-1.80, 0.44), (-2.05, 0.50), (-2.5, 0.38)]


def ridgeline(views):
    """A navy ridge of peaks, low along the sill and rising to its tallest over the rear haunch,
    traced in gold."""
    lv = Livery(views, PEARL)
    gold = [(z, y + 0.018) for z, y in RIDGE]
    lv.side_poly(gold + [(-2.5, -0.1), (2.5, -0.1)], GOLD)
    lv.side_poly(RIDGE + [(-2.5, -0.1), (2.5, -0.1)], NAVY)
    lv.band_x(-1.2, 1.2, NAVY, views=("front",), y=(-0.1, 0.20))
    lv.band_x(-1.2, 1.2, GOLD, views=("front",), y=(0.20, 0.218))
    lv.band_x(-1.2, 1.2, NAVY, views=("rear",), y=(-0.1, 0.38))
    lv.band_x(-1.2, 1.2, GOLD, views=("rear",), y=(0.38, 0.398))
    common(lv, "navy")
    return lv


OPTIONS = {"summit": summit, "ridgeline": ridgeline}

if __name__ == "__main__":
    views = HERE / "out" / "views" / "views.json"
    for name, make in OPTIONS.items():
        make(views).write(HERE / "out" / name)
