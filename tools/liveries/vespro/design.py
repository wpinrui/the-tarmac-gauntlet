"""Vespro Corse on the Archer Jet Black: an Italian GT privateer powerhouse. Independent: no maker's
logo. Wine red with a cream nose and cream sills, thick twin cream stripes over the roof and boot, a
cream roundel for the number on each door and the team's motto on each front wing. The team's mark,
big across the bonnet and on the tail: its name in a script hand beside a bell tower (vespro is the
evening bell), underlined, a thin Italian tricolore under that.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/ArcherJetBlack/ArcherJetBlack.obj out/views
     node ../common/digits.js --font font/BodoniModa-Bold.ttf --out out/digits
     python3 design.py

Jet Black landmarks in OBJ metres: z along the car (nose +2.31, tail -2.44), y up, x to the car's
left. Wheels at z +1.34 and -1.34, radius 0.34, arches up to y 0.70. The door z +0.27 to -0.94,
y 0.35 to the glass at 0.90; it swings out on a hinge at its front edge. Side vent z 0.76 to 0.44,
y 0.30 to 0.62. Shoulder y 0.85 over the front wing. Windscreen base z 0.44 seen from above; the
bonnet's shut line runs at |x| 0.70. Rear: boot lid edge y 0.81, maker's badge y 0.68, plate
recess y 0.25 to 0.37. Front: grille y 0.27 to 0.53, headlamps up to y 0.72.
"""
import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
VIEWS = HERE / "out" / "views" / "views.json"
MARKS = HERE / "out" / "marks"
TG = HERE / "tg.svg"
sys.path.insert(0, str(HERE.parent / "common"))
import livery  # noqa: E402
from livery import Livery, finish  # noqa: E402

livery.FONTS.update({"GreatVibes": HERE / "font" / "GreatVibes-Regular.ttf"})

WINE = "#5e1224"
CREAM = "#efe6d2"

F_PAINT = finish(0.88, 0.85, 0.25)   # deep gloss with a little flake

NOSE = (2.0, 0.95, 1.72, 0.33)     # cream nose: from the shoulder at z 2.0 down to the front arch's leading edge
NOSE_FRONT_Y = 0.84                 # front view: cream below this, just under the mirrors
NOSE_TOP_Z = 1.92                   # top view: cream ahead of this, so the nose's slope is cream from both views
SILL_Y, PIN_Y, PIN_W = 0.30, 0.335, 0.018
STRIPE = (0.04, 0.24)              # thick twin stripes, each this far off the centre line
DOOR = (-0.36, 0.60, 0.20)         # door roundel: z, y, radius
MOTTO = (0.62, 0.745, 0.057)       # front wing above the side vent: z, y, height
TG_SIDE = (-0.35, 0.17, 0.07)      # on the cream sill under the door: z, y, height
BONNET_SCRIPT = (1.15, 0.50)       # the mark big across the bonnet: z, height
SCREEN_Z = 0.30                    # just behind the windscreen base, so the stripes start under the glass


# --- the team's mark ---------------------------------------------------------------------------------
def tower(ink, ground):
    """The bell tower, 140 units tall, its foot centred on the origin."""
    return (f'<g fill="{ink}"><rect x="-14" y="-110" width="28" height="110"/><path d="M-18,-110 L0,-140 L18,-110Z"/>'
            f'<rect x="-20" y="-114" width="40" height="6"/></g>'
            f'<g fill="{ground}"><rect x="-7" y="-100" width="5" height="12"/><rect x="2" y="-100" width="5" height="12"/>'
            f'<rect x="-7" y="-70" width="14" height="10"/></g>')


def logo(ink, ground):
    """The bell tower, then the name in script, underlined, a thin Italian tricolore under that. ground cuts the tower's windows."""
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="-14 -90 316 108">'
            f'<g transform="translate(0 6) scale(0.62)">{tower(ink, ground)}</g>'
            f'<text x="30" y="-6" font-family="GreatVibes" font-size="58" fill="{ink}">Vespro Corse</text>'
            f'<rect x="67" y="8" width="190" height="2.6" fill="{ink}"/>'
            + "".join(f'<rect x="{67 + i * 190 / 3:.2f}" y="13" width="{190 / 3 + 0.05:.2f}" height="2.2" fill="{c}"/>'
                      for i, c in enumerate(("#009246", "#ffffff", "#ce2b37")))
            + '</svg>')


def write_marks():
    MARKS.mkdir(parents=True, exist_ok=True)
    (MARKS / "logo-cream.svg").write_text(logo(CREAM, WINE))


# --- the livery ------------------------------------------------------------------------------------------
def circle(cz, cy, r, n=72):
    return [(cz + r * math.cos(t), cy + r * math.sin(t)) for t in (i / n * math.tau for i in range(n))]


def roundel(lv, view, p, r, rotate=0):
    """A cream number roundel with a fine wine ring, r metres across its radius, centred on p."""
    u, v = lv.uv(view, p)
    lv.raw(view, f'<circle cx="{u:.4f}" cy="{v:.4f}" r="{r:.4f}" fill="{CREAM}"/>'
                 f'<circle cx="{u:.4f}" cy="{v:.4f}" r="{r - r * 0.11:.4f}" fill="none" stroke="{WINE}" stroke-width="{r * 0.035:.4f}"/>')
    lv.number(view, p, r * 1.5, r * 0.95, WINE, rotate=rotate)


def side_roundel(lv, z, y, r):
    for view in ("left", "right"):
        roundel(lv, view, Livery._side(view, z, y), r)


def base():
    """Wine with the cream nose, cream sills, and the thick twin stripes from under the windscreen to the boot lid's edge."""
    write_marks()
    lv = Livery(VIEWS, WINE, F_PAINT)
    # Cream nose: a swept edge from the shoulder down to the front wheel arch.
    z_top, y_top, z_arch, y_arch = NOSE
    edge = [(z_top - (z_top - z_arch) * t ** 1.6, y_top - (y_top - y_arch) * t) for t in (i / 24 for i in range(25))]
    lv.side_poly([(3, 2), (z_top, 2)] + edge + [(z_arch, -0.1), (3, -0.1)], CREAM)
    lv.poly("top", [(-2, 0, 3), (2, 0, 3), (2, 0, NOSE_TOP_Z), (-2, 0, NOSE_TOP_Z)], CREAM)
    lv.band_x(-2, 2, CREAM, views=("front",), y=(-0.1, NOSE_FRONT_Y))
    # Cream sills and a pinstripe over them, carried round the rear bumper.
    lv.side_poly([(3, SILL_Y), (-3, SILL_Y), (-3, -0.1), (3, -0.1)], CREAM)
    lv.side_poly([(z_arch, PIN_Y + PIN_W / 2), (-3, PIN_Y + PIN_W / 2), (-3, PIN_Y - PIN_W / 2), (z_arch, PIN_Y - PIN_W / 2)], CREAM)
    lv.band_x(-2, 2, CREAM, views=("rear",), y=(-0.1, SILL_Y))
    lv.band_x(-2, 2, CREAM, views=("rear",), y=(PIN_Y - PIN_W / 2, PIN_Y + PIN_W / 2))
    # Thick twin stripes over the roof, down to the boot lid's edge.
    for s in (1, -1):
        lv.band_x(s * STRIPE[0], s * STRIPE[1], CREAM, views=("top",), z=(-3, SCREEN_Z))
        lv.band_x(s * STRIPE[0], s * STRIPE[1], CREAM, views=("rear",), y=(0.81, 2))
    lv.side_text("Senza fretta, senza sosta", *MOTTO, CREAM, font="GreatVibes")
    lv.side_mark(TG, *TG_SIDE, ink=WINE)
    lv.mark("rear", MARKS / "logo-cream.svg", (0, 0.53, 0), 0.25)
    lv.mark("rear", TG, (0, 0.18, 0), 0.07, ink=WINE)
    return lv



def bonnet_script():
    """The mark big across the bonnet, read from the nose; the number on each door."""
    lv = base()
    lv.mark("top", MARKS / "logo-cream.svg", (0, 0, BONNET_SCRIPT[0]), BONNET_SCRIPT[1], rotate=-90)
    side_roundel(lv, *DOOR)
    return lv



OPTIONS = {"bonnet-script": bonnet_script}

if __name__ == "__main__":
    for name, build in OPTIONS.items():
        build().write(HERE / "out" / name)
