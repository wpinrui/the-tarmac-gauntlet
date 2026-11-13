"""Saltire Racing on the Tora Daruma: Thistle. Scottish pride on a shoestring, painted by hand in
a lock-up. A cream car with the saltire across the whole roof, run out past the roof's edges so no
cream shows round it, and down the roof rails. Saltire blue brushed along the sills and over the
nose, with dry bristle edges. A thistle grows up out of the blue on each rear door. The team's
shield on the front door, the name in marker, the race number in brushed marker digits.
Independent: no maker's logo.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/ToraDaruma/ToraDaruma.obj out/views
     node ../common/digits.js --font font/PermanentMarker-Regular.ttf --brush 0.5 --out out/digits
     python3 design.py

Daruma landmarks in OBJ metres: z along the car (nose +2.15, tail -2.05), y up, x to the car's left
(half width 0.85). Wheels at z +-1.30, arches up to y 0.70. Front door z 0.88 to -0.24, rear door
-0.24 to -1.22, door bottoms y 0.28, belt y 0.94 (0.99 on the rear door). Roof seen from above
z -1.40 to 0.38 for |x| under 0.62, glass beyond. Rear hatch under the glass y 0.80 to 1.05, the
name badge at y 0.75. Front grille bottom y 0.58.
"""
import base64
import io
import json
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
VIEWS = HERE / "out" / "views"
sys.path.insert(0, str(HERE.parent / "common"))
import livery  # noqa: E402
from livery import Livery, finish  # noqa: E402
from thistle import thistle  # noqa: E402

livery.FONTS.update({"Marker": HERE / "font" / "PermanentMarker-Regular.ttf"})

CREAM = "#e8dfc6"
BLUE = "#0065bd"           # saltire blue
WHITE = "#f4f1ea"
NAVY = "#13233f"           # marker ink: name and number
CREAM_RGB = np.array([0xe8, 0xdf, 0xc6]) / 255
BLUE_RGB = np.array([0x00, 0x65, 0xbd]) / 255
WHITE_RGB = np.array([0xf4, 0xf1, 0xea]) / 255

# Coach enamel from a tin: glossy, a thinner clear coat than a factory's, no metal.
F_PAINT = finish(0.82, 0.6, 0.0)

ROOF = (-1.40, 0.38, 0.62)           # roof seen from above: z from, z to, half width
ROOF_BLEED = (-1.58, 0.50, 0.78)     # the flag's blue runs on under the glass, so no cream rim shows
ARM = 0.22                           # saltire arm width
RAIL_Y = 1.395                       # sides: blue above the side glass, so the flag wraps down
SILL_Y = 0.42                        # blue below this on the sides
NOSE_Y = 0.56                        # blue below this on the front
TAIL_Y = 0.46                        # and on the rear
BONNET_Z = 1.86                      # blue ahead of this on the bonnet
THISTLE = (-0.74, 0.30, 0.64)        # rear door: z of the stem's foot, y, height
SHIELD = (0.66, 0.64, 0.32)          # front door: z, y, height
NAME = (0.06, 0.80, 0.08)           # front door: z, y, cap height
NUMBER = (0.06, 0.54, 0.40, 0.24)    # front door: z, y, box w, h


# --- rasters, in car metres ------------------------------------------------------------------------
def grid(view, meta):
    """Each pixel's (z, x or across, y) in car metres. Sides: (z, -, y). Top: (z, x, -).
    Front and rear: (-, x, y)."""
    f, ppm = meta["views"][view], meta["ppm"]
    w, h = f["px"]
    u = f["u0"] + (np.arange(w) + 0.5) / ppm
    v = f["v0"] + (np.arange(h) + 0.5) / ppm
    U, V = np.meshgrid(u, v)
    if view == "left":
        return -U, None, -V
    if view == "right":
        return U, None, -V
    if view == "top":
        return U, -V, None
    if view == "front":
        return None, U, -V
    return None, -U, -V


def noise2(a, b, ca, cb, seed):
    """Smooth value noise over two coordinates, cells ca by cb metres."""
    rng = np.random.default_rng(seed)
    ga, gb = a / ca, b / cb
    ia, ib = np.floor(ga).astype(int), np.floor(gb).astype(int)
    ta, tb = ga - ia, gb - ib
    ta, tb = ta * ta * (3 - 2 * ta), tb * tb * (3 - 2 * tb)
    oa, ob = ia.min(), ib.min()
    table = rng.random((ia.max() - oa + 2, ib.max() - ob + 2))
    ia, ib = ia - oa, ib - ob
    v00, v10, v01, v11 = table[ia, ib], table[ia + 1, ib], table[ia, ib + 1], table[ia + 1, ib + 1]
    return (v00 * (1 - ta) + v10 * ta) * (1 - tb) + (v01 * (1 - ta) + v11 * ta) * tb


def brushed(depth, along, across, seed, reach=0.05):
    """Coverage of a brushed edge. depth: metres inside the painted side (negative outside).
    along runs with the brush. Bristles leave dry streaks that fade in over reach metres."""
    wobble = (noise2(along, across * 0, 0.18, 1, seed) - 0.5) * 0.016
    d = depth + wobble
    streak = noise2(along, across, 0.35, 0.0045, seed + 1)
    fill = np.clip(d / reach, 0, 1)
    cover = np.clip((streak - (1 - fill) * 0.9) * 4 + fill * 0.6, 0, 1)
    return np.where(d <= 0, 0.0, np.where(d >= reach, 1.0, cover))


def laps(along, across, seed):
    """A roller's laps: soft bands about 23 cm wide, each a touch lighter or darker."""
    band = np.floor(along / 0.23 + noise2(across, across, 0.6, 0.6, seed) * 0.3)
    rng = np.random.default_rng(seed)
    shade = rng.normal(0, 0.018, 256)[(band.astype(int) % 256)]
    fine = (noise2(along, across, 0.02, 0.006, seed + 3) - 0.5) * 0.02
    return shade + fine


def uri(rgba):
    im = Image.fromarray((np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA")
    buf = io.BytesIO()
    im.save(buf, "PNG", optimize=True)
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode()


def paint(view, meta, seed):
    """The view's whole ground: cream with roller laps, the brushed blue, and on the top the flag."""
    z, x, y = grid(view, meta)
    shape = (z if z is not None else x).shape
    blue = np.zeros(shape)
    white = np.zeros(shape)
    if view in ("left", "right"):
        blue = np.maximum(blue, brushed(SILL_Y - y, z, y, seed))
        inside = (z > ROOF_BLEED[0]) & (z < ROOF_BLEED[1])
        blue = np.maximum(blue, inside * brushed(y - RAIL_Y, z, y, seed + 7, reach=0.012))
        along, across = z, y
    elif view == "front":
        blue = brushed(NOSE_Y - y, x, y, seed)
        along, across = x, y
    elif view == "rear":
        blue = brushed(TAIL_Y - y, x, y, seed)
        along, across = x, y
    else:
        blue = brushed(z - BONNET_Z, x, z, seed, reach=0.06)
        z0, z1, hw = ROOF_BLEED
        flag = (z > z0) & (z < z1) & (np.abs(x) < hw)
        blue = np.maximum(blue, flag.astype(float))
        # The X, corner to corner of the roof, brushed along each arm.
        r0, r1, rh = ROOF
        for k, sgn in enumerate((1, -1)):
            p0 = np.array([r0, -sgn * rh])
            p1 = np.array([r1, sgn * rh])
            d = (p1 - p0) / np.linalg.norm(p1 - p0)
            along = (z - p0[0]) * d[0] + (x - p0[1]) * d[1]
            dist = np.abs((z - p0[0]) * -d[1] + (x - p0[1]) * d[0])
            white = np.maximum(white, flag * brushed(ARM / 2 - dist, along, dist, seed + 11 + k, reach=0.03))
        along, across = z, x
    shade = laps(along, across, seed + 20)[..., None]
    col = CREAM_RGB * (1 - blue[..., None]) + BLUE_RGB * blue[..., None]
    col = col * (1 - white[..., None]) + WHITE_RGB * white[..., None]
    col = np.clip(col * (1 + shade), 0, 1)
    return np.concatenate([col, np.ones(shape + (1,))], -1)


# --- marks -------------------------------------------------------------------------------------------
SHIELD_PATH = "M-60,-82.5 H60 V-2.5 C60,42.5 30,67.5 0,82.5 C-30,67.5 -60,42.5 -60,-2.5 Z"   # 120 by 165


def shield(lv, view, p, h, rotate=0):
    """The team's logo: a brushed saltire on a shield, edges rough as if cut out by hand."""
    u, v = lv.uv(view, p)
    s = h / 165
    rough = (f'<filter id="sh-rough" x="-20%" y="-20%" width="140%" height="140%"><feTurbulence type="fractalNoise" '
             f'baseFrequency="0.06" numOctaves="3" seed="4"/><feDisplacementMap in="SourceGraphic" scale="7"/></filter>')
    arm = 'stroke="{c}" stroke-width="22" stroke-linecap="round" filter="url(#sh-rough)"'.format(c=WHITE)
    lv.raw(view, f'<g transform="translate({u:.4f} {v:.4f}) rotate({rotate}) scale({s:.6f})">{rough}'
                 f'<clipPath id="sh-clip"><path d="{SHIELD_PATH}"/></clipPath>'
                 f'<path d="{SHIELD_PATH}" fill="{BLUE}" stroke="{NAVY}" stroke-width="5" filter="url(#sh-rough)"/>'
                 f'<g clip-path="url(#sh-clip)"><line x1="-70" y1="-92.5" x2="70" y2="92.5" {arm}/>'
                 f'<line x1="70" y1="-92.5" x2="-70" y2="92.5" {arm}/></g></g>')


def plant(lv, view):
    """The thistle on the rear door, its bud leaning to the tail on both sides."""
    z, y, h = THISTLE
    u, v = lv.uv(view, Livery._side(view, z, y))
    flip = 1 if view == "left" else -1      # left view: tail to the right
    lv.raw(view, f'<g transform="translate({u:.4f} {v:.4f}) scale({flip * h:.4f} {h:.4f})">{thistle(seed=3)}</g>')


def thistle_livery():
    meta = json.loads((VIEWS / "views.json").read_text())
    lv = Livery(VIEWS / "views.json", CREAM, F_PAINT)
    for k, view in enumerate(meta["views"]):
        lv.image(view, uri(paint(view, meta, 50 + k)))
    for view in ("left", "right"):
        plant(lv, view)
        shield(lv, view, Livery._side(view, SHIELD[0], SHIELD[1]), SHIELD[2])
    lv.side_text("Saltire Racing", NAME[0], NAME[1], NAME[2], NAVY, font="Marker")
    lv.side_number(NUMBER[0], NUMBER[1], NUMBER[2], NUMBER[3], NAVY)
    # Bonnet: the number, read from the nose. Rear: the shield and name over the maker's badge.
    lv.number("top", (0, 0, 1.45), 0.52, 0.30, NAVY, rotate=-90)
    shield(lv, "rear", (0.36, 0.93, 0), 0.20)
    lv.text("rear", "Saltire Racing", (-0.08, 0.93, 0), 0.07, NAVY, font="Marker")
    return lv


OPTIONS = {"thistle": thistle_livery}

if __name__ == "__main__":
    for name, build in OPTIONS.items():
        build().write(HERE / "out" / name)
