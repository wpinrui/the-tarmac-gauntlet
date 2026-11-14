"""Cóndor Racing on the Liberty Mantle: Ivan's two picks, Vueltiao and Carnaval, drawn as five
panels over the car's true-scale views, with the team's mark (mockup 1, the soaring condor).

Run: python3 design.py   -> out/<design>/ (panels) and marks/*.svg
"""
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "common"))
from livery import Livery  # noqa: E402
from lowfi import BLK, CRM, IND, INK, MAG, ORA, SUN, TUR, WHT, bird  # noqa: E402

VIEWS = HERE / "out" / "views" / "views.json"
MARKS = HERE / "marks"

# The Mantle's lines, in OBJ metres (z towards the nose, y up), read off its side view.
NOSE, TAIL = 2.50, -2.50
SILL = 0.20                       # the paint's lowest band starts here, under the door bottoms
FRONT_DOOR = (1.00, -0.19)        # z of its front and rear shut lines
REAR_DOOR = (-0.19, -1.23)


def belt(z):
    """The bottom of the side windows, rising towards the tail."""
    return 0.98 + (1.0 - z) * 0.044


def logo(path, ink, band, text):
    """Mockup 1, cropped tight: the soaring condor over CÓNDOR RACING."""
    path.parent.mkdir(exist_ok=True)
    h = 0.15
    path.write_text(
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="-1.0 -0.62 2.0 1.34">'
        + bird(0, -0.30, 1.9, ink, band=band, ruff=band)
        + f'<text x="0" y="0.60" font-family="Titillium" font-size="{h / 0.72:.4f}" fill="{text}" '
          f'letter-spacing="{0.12 * h:.4f}" text-anchor="middle" dominant-baseline="central">CÓNDOR RACING</text></svg>')
    return path


def belt_line(lv, bend, t=0.025):
    """The shoulder pinstripe: along the belt from the tail, then angled down from bend (z) to
    end in the top of the front wheel arch, where the arch cuts it off."""
    pts = [(TAIL, belt(TAIL) - 0.07), (bend, belt(bend) - 0.07), (1.42, 0.60)]
    top, bot = [], []
    for i, (z, y) in enumerate(pts):
        a = pts[max(i - 1, 0)]
        b = pts[min(i + 1, len(pts) - 1)]
        dz, dy = b[0] - a[0], b[1] - a[1]
        n = (dz * dz + dy * dy) ** 0.5
        nz, ny = -dy / n, dz / n          # unit normal
        if 0 < i < len(pts) - 1:          # mitre at the bend
            d1 = (z - a[0], y - a[1]); d2 = (b[0] - z, b[1] - y)
            n1 = (-d1[1], d1[0]); n2 = (-d2[1], d2[0])
            l1 = (n1[0] ** 2 + n1[1] ** 2) ** 0.5; l2 = (n2[0] ** 2 + n2[1] ** 2) ** 0.5
            m = (n1[0] / l1 + n2[0] / l2, n1[1] / l1 + n2[1] / l2)
            ml = (m[0] ** 2 + m[1] ** 2) ** 0.5
            cos = (m[0] * n1[0] / l1 + m[1] * n1[1] / l1) / ml
            nz, ny = m[0] / ml / cos, m[1] / ml / cos
        top.append((z + nz * t / 2, y + ny * t / 2))
        bot.append((z - nz * t / 2, y - ny * t / 2))
    lv.side_poly(top + bot[::-1], INK)


def side_band(lv, y0, y1, fill, z0=NOSE, z1=TAIL):
    lv.side_poly([(z0, y0), (z1, y0), (z1, y1), (z0, y1)], fill)


def end_band(lv, y0, y1, fill):
    for view in ("front", "rear"):
        lv.poly(view, [(-2, y0, 0), (2, y0, 0), (2, y1, 0), (-2, y1, 0)], fill)


def diamond(lv, view_pts, cz, cy, r, fill):
    """A diamond on both sides, r metres from centre to tip."""
    lv.side_poly([(cz + r, cy), (cz, cy + r), (cz - r, cy), (cz, cy - r)], fill)


def end_diamond(lv, cx, cy, r, fill):
    for view in ("front", "rear"):
        lv.poly(view, [(cx + r, cy, 0), (cx, cy + r, 0), (cx - r, cy, 0), (cx, cy - r, 0)], fill)


def vueltiao(bend=1.15):
    """The sombrero vueltiao: cream cane with a black woven band of pintas round the sills."""
    dark = logo(MARKS / "condor-dark.svg", BLK, CRM, BLK)
    lv = Livery(VIEWS, CRM)
    y0, y1 = SILL, 0.42
    h = y1 - y0
    step = h * 1.2
    # the woven band: black, holding a row of cream diamonds, each with a black eye
    side_band(lv, y0, y1, INK)
    end_band(lv, y0, y1, INK)
    z = NOSE
    while z > TAIL:
        diamond(lv, None, z, y0 + h / 2, h * 0.32, CRM)
        diamond(lv, None, z, y0 + h / 2, h * 0.12, INK)
        z -= step
    x = 0.0
    while x < 1.1:
        for sx in ({x, -x}):
            end_diamond(lv, sx, y0 + h / 2, h * 0.32, CRM)
            end_diamond(lv, sx, y0 + h / 2, h * 0.12, INK)
        x += step
    # pinstripes: one over the band, one along the belt
    side_band(lv, 0.46, 0.485, INK)
    end_band(lv, 0.46, 0.485, INK)
    belt_line(lv, bend)
    # the roof carries the hat's rings
    for inset, t in ((0.04, 0.06), (0.14, 0.035)):
        x0, z0, z1 = 0.66 - inset, 0.33 - inset, -1.26 + inset
        for pts in ([(x0, z0), (-x0, z0), (-x0, z0 - t), (x0, z0 - t)], [(x0, z1 + t), (-x0, z1 + t), (-x0, z1), (x0, z1)],
                    [(x0, z0), (x0 - t, z0), (x0 - t, z1), (x0, z1)], [(-x0 + t, z0), (-x0, z0), (-x0, z1), (-x0 + t, z1)]):
            lv.poly("top", [(a, 0, b) for a, b in pts], INK)
    # the mark on the bonnet and the rear doors; the number on the front doors and the roof
    lv.mark("top", dark, (0, 0, 1.80), 0.62, rotate=-90)
    lv.side_mark(dark, -0.72, 0.70, 0.34)
    lv.side_number(0.42, 0.70, 0.62, 0.30, INK)
    lv.number("top", (0, 0, -0.47), 0.95, 0.46, INK, rotate=-90)
    return lv


def carnaval():
    """Barranquilla carnival and Wayuu weaving: deep indigo, four bright stripes low down and a
    row of diamonds along the shoulder."""
    light = logo(MARKS / "condor-light.svg", WHT, IND, WHT)
    lv = Livery(VIEWS, IND)
    cols = [MAG, ORA, SUN, TUR]
    for i, c in enumerate(cols):
        y = SILL + i * 0.075
        side_band(lv, y, y + 0.05, c)
        end_band(lv, y, y + 0.05, c)
    # the diamond row, leaving room for the number and the mark under it
    z, k = 1.62, 0                 # from behind the headlamp to the tail lamp
    while z > -1.92:
        cy = belt(z) - 0.15
        diamond(lv, None, z, cy, 0.10, cols[k % 4])
        diamond(lv, None, z, cy, 0.035, IND)
        z -= 0.24
        k += 1
    # stripes over the top, nose to tail
    for i, c in enumerate(cols):
        x = -0.24 + i * 0.16
        lv.poly("top", [(x, 0, NOSE), (x + 0.10, 0, NOSE), (x + 0.10, 0, TAIL), (x, 0, TAIL)], c)
    # indigo plates for the bonnet mark and the roof number, so they read over the stripes
    for zc, hz, hx in ((1.80, 0.36, 0.52), (-0.47, 0.30, 0.56)):
        lv.poly("top", [(hx, 0, zc + hz), (-hx, 0, zc + hz), (-hx, 0, zc - hz), (hx, 0, zc - hz)], IND)
    lv.mark("top", light, (0, 0, 1.80), 0.62, rotate=-90)
    lv.side_mark(light, -0.72, 0.60, 0.28)
    lv.side_number(0.42, 0.60, 0.62, 0.24, WHT)
    lv.number("top", (0, 0, -0.47), 0.95, 0.46, WHT, rotate=-90)
    return lv


DESIGNS = {"vueltiao": vueltiao, "carnaval": carnaval,
           "vueltiao-door": lambda: vueltiao(0.80)}

if __name__ == "__main__":
    for name, fn in DESIGNS.items():
        fn().write(HERE / "out" / name)
