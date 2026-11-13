"""Flatbed Legends on the Kaiser Firefly: Tow Yard. Arrive on a flatbed, leave on a flatbed, so the
car is painted like the truck that keeps bringing it home. Safety yellow with a black greenhouse
and black sills, hazard chevrons on the rear wings and across the tail, the name in shaded slab
letters like a tow company's door, and the team's hazard badge. Independent: no maker's logo.

Two layouts of it, as options:
  letters  the name on the door in two shaded lines, the number under it, chevrons on the rear wings.
  badge    the hazard badge large on the door with the number in its plate, the name on the rear wing.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/KaiserFirefly/KaiserFirefly.obj out/views
     node ../common/digits.js --font font/AlfaSlabOne-Regular.ttf --out out/digits
     python3 design.py

Firefly landmarks in OBJ metres: z along the car (nose +2.09, tail -1.99), y up, x to the car's left
(half width 0.89). Wheels at z +-1.26, radius 0.33. Door z 0.80 to -0.33, door bottom y 0.24, belt
(side glass foot) y 0.93. Greenhouse seen from above z -1.33 to 1.21 for |x| under 0.78. Bonnet
ahead of z 1.06. Rear: glass foot y 1.11, boot lid y 0.69 to 0.98.
"""
import base64
import io
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
VIEWS = HERE / "out" / "views"
sys.path.insert(0, str(HERE.parent / "common"))
import livery  # noqa: E402
from livery import Livery  # noqa: E402

livery.FONTS.update({"Slab": HERE / "font" / "AlfaSlabOne-Regular.ttf"})

YELLOW = "#f2b705"         # safety yellow
INK = "#16161a"            # black
RED = "#b3202a"            # the letters' drop shade
YELLOW_RGB = np.array([0xf2, 0xb7, 0x05]) / 255
INK_RGB = np.array([0x16, 0x16, 0x1a]) / 255

BELT_Y = 0.93              # sides: black above the side glass's foot
SILL_Y = 0.27              # and below this, all round
GLASS = (-1.40, 1.24, 0.80, 0.22)   # top: black greenhouse, z from, z to, half width, corner radius
DOOR = (0.80, -0.33)
WING_Z = -0.42             # sides: chevrons behind this (the door's rear shut line is at -0.33)
TAIL_TOP = 1.10            # rear: chevrons up to the rear glass's foot
DECK_Z = -1.46             # top: chevrons on the tail deck behind this
BAND = 0.12                # chevron stripe width


# --- rasters, in car metres ------------------------------------------------------------------------
def grid(view, meta):
    """Each pixel's (z, x, y) in car metres; the coordinate the view looks along is None."""
    f, ppm = meta["views"][view], meta["ppm"]
    w, h = f["px"]
    u = f["u0"] + (np.arange(w) + 0.5) / ppm
    v = f["v0"] + (np.arange(h) + 0.5) / ppm
    U, V = np.meshgrid(u, v)
    return {"left": (-U, None, -V), "right": (U, None, -V), "top": (U, -V, None),
            "front": (None, U, -V), "rear": (None, -U, -V)}[view]


def stripes(t):
    """Hazard stripes across t (metres), anti-aliased: 1 on the black stripes."""
    k = t / BAND
    d = np.abs((k % 2) - 1) - 0.5           # >0 on one stripe, <0 on the next
    return np.clip(d * BAND * 400 + 0.5, 0, 1)


def soft(d, px=0.004):
    return np.clip(d / px + 0.5, 0, 1)


def rounded_box(z, x, z0, z1, hw, r):
    """Inside a rounded rectangle in plan: signed depth in metres (positive inside)."""
    cz, hz = (z0 + z1) / 2, (z1 - z0) / 2
    qz, qx = np.abs(z - cz) - (hz - r), np.abs(x) - (hw - r)
    out = np.hypot(np.maximum(qz, 0), np.maximum(qx, 0)) + np.minimum(np.maximum(qz, qx), 0) - r
    return -out


def ground(view, meta, wing_chevrons):
    """Yellow, the black greenhouse and sills, and the chevrons."""
    z, x, y = grid(view, meta)
    shape = (z if z is not None else x).shape
    black = np.zeros(shape)
    if view in ("left", "right"):
        black = np.maximum(soft(y - BELT_Y), soft(SILL_Y - y))
        if wing_chevrons:
            band = soft(WING_Z - z) * soft(y - SILL_Y) * soft(BELT_Y - y)
            black = np.maximum(black, band * stripes(y + z))
    elif view == "top":
        z0, z1, hw, r = GLASS
        black = soft(rounded_box(z, x, z0, z1, hw, r))
        black = np.maximum(black, soft(DECK_Z - z) * stripes(np.abs(x) - z))
    elif view == "front":
        black = soft(SILL_Y - y)
    else:
        black = np.maximum(soft(SILL_Y - y), soft(y - TAIL_TOP) * 0)
        black = np.maximum(black, soft(TAIL_TOP - y) * soft(y - SILL_Y) * stripes(y + np.abs(x)))
    col = YELLOW_RGB * (1 - black[..., None]) + INK_RGB * black[..., None]
    return np.concatenate([col, np.ones(shape + (1,))], -1)


def uri(rgba):
    im = Image.fromarray((np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA")
    buf = io.BytesIO()
    im.save(buf, "PNG", optimize=True)
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode()


# --- marks -------------------------------------------------------------------------------------------
def badge_svg(plate=True):
    """The team's logo: a hazard-striped badge with a yellow plate in the middle. plate=True writes
    F/L in it; False leaves it clear for the race number."""
    stripes_ = "".join(f'<path d="M{-40 + i * 40} 0 h20 l-120 120 h-20 Z" fill="{INK}"/>' for i in range(10))
    letters = (f'<text x="90" y="76" font-family="Slab" font-size="34" fill="{INK}" text-anchor="middle">F/L</text>'
               if plate else "")
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 180 120">'
            f'<defs><clipPath id="hz"><rect width="180" height="120" rx="10"/></clipPath></defs>'
            f'<g clip-path="url(#hz)"><rect width="180" height="120" fill="{YELLOW}"/>{stripes_}</g>'
            f'<rect x="2" y="2" width="176" height="116" rx="9" fill="none" stroke="{INK}" stroke-width="4"/>'
            f'<rect x="28" y="36" width="124" height="48" rx="6" fill="{YELLOW}" stroke="{INK}" stroke-width="4"/>'
            f'{letters}</svg>')


def write_marks():
    out = HERE / "out" / "marks"
    out.mkdir(parents=True, exist_ok=True)
    (out / "badge.svg").write_text(badge_svg(True))
    (out / "badge-blank.svg").write_text(badge_svg(False))
    return out


def shaded(lv, view, s, p, h, spacing=0.04):
    """Slab letters with a red shade dropped down and back, like a tow company's door."""
    d = h * 0.07
    u, v = lv.uv(view, p)
    for (du, dv), fill in (((d, d), RED), ((0, 0), INK)):
        lv.raw(view, f'<text x="{u + du:.4f}" y="{v + dv:.4f}" font-family="Slab" font-size="{h / 0.72:.4f}" '
                     f'letter-spacing="{spacing * h:.4f}" fill="{fill}" text-anchor="middle" '
                     f'dominant-baseline="central">{s}</text>')


def common(lv, wing_chevrons):
    meta = json.loads((VIEWS / "views.json").read_text())
    for view in meta["views"]:
        lv.image(view, uri(ground(view, meta, wing_chevrons)))
    # Bonnet: the number, read from the nose. Tail: the name on a yellow plate in the chevrons, under the boot lid.
    lv.number("top", (0, 0, 1.58), 0.50, 0.28, INK, rotate=-90)
    u, v = lv.uv("rear", (0, 0.48, 0))
    lv.raw("rear", f'<rect x="{u - 0.30:.4f}" y="{v - 0.13:.4f}" width="0.60" height="0.26" rx="0.03" '
                   f'fill="{YELLOW}" stroke="{INK}" stroke-width="0.02"/>')
    shaded(lv, "rear", "FLATBED", (0, 0.535, 0), 0.075)
    shaded(lv, "rear", "LEGENDS", (0, 0.425, 0), 0.075)


def letters_livery():
    lv = Livery(VIEWS / "views.json", YELLOW)
    marks = write_marks()
    common(lv, wing_chevrons=True)
    mid = (DOOR[0] + DOOR[1]) / 2
    for view in ("left", "right"):
        shaded(lv, view, "FLATBED", Livery._side(view, mid, 0.79), 0.12)
        shaded(lv, view, "LEGENDS", Livery._side(view, mid, 0.61), 0.12)
        lv.mark(view, marks / "badge.svg", Livery._side(view, 1.28, 0.81), 0.17)
    lv.side_number(mid, 0.40, 0.44, 0.24, INK)
    return lv


def badge_livery():
    lv = Livery(VIEWS / "views.json", YELLOW)
    marks = write_marks()
    common(lv, wing_chevrons=False)
    mid = (DOOR[0] + DOOR[1]) / 2
    for view in ("left", "right"):
        lv.mark(view, marks / "badge-blank.svg", Livery._side(view, mid, 0.58), 0.56)
        shaded(lv, view, "FLATBED", Livery._side(view, -0.95, 0.86), 0.085)
        shaded(lv, view, "LEGENDS", Livery._side(view, -0.95, 0.73), 0.085)
    # The number sits in the badge's plate: 124 by 48 of the badge's 180 by 120.
    lv.side_number(mid, 0.58, 0.56 * 180 / 120 * 124 / 180 - 0.05, 0.56 * 48 / 120 - 0.05, INK)
    return lv


OPTIONS = {"letters": letters_livery, "badge": badge_livery}

if __name__ == "__main__":
    for name, build in OPTIONS.items():
        build().write(HERE / "out" / name)
