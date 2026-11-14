"""Sip Vanessa Racing on the Archer Tachyon: two liveries for Ivan to pick from.

Happy Hour: hot pink with a cream beer-foam band along the sills that kicks up over the side
intake, the lips logo on the bonnet and doors, a cherry pair and an eggplant on the rear haunches.
Lace & Lager: bubblegum pink, black lace along the sills and nose, a black garter band with a
bow across the bonnet, the lips logo on the doors.

Independent: no maker's logo.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/ArcherTachyon/ArcherTachyon.obj out/views
     python3 design.py
     ./preview.sh <page.html>

Tachyon landmarks in OBJ metres: z along the car (nose +2.39, tail -2.12), y up, x to the car's
left. Axles at z +-1.335. Door from z 1.07 to -0.41 at the belt (0.76 up) and to 0.12 at the
sill (0.13 up). Side intake z -0.28 to -0.84, y 0.31 to 0.76. Bonnet z 1.35 to 2.30 for |x| under
0.6; windscreen z 0.51 to 1.33; roof z -0.40 to 0.50 for |x| under 0.5; engine glass z -1.43 to
-0.62 for |x| under 0.37.
"""
import math
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "common"))
import livery  # noqa: E402
from livery import Livery  # noqa: E402

FONT = HERE / "font"
livery.FONTS.update({"Pacifico": FONT / "Pacifico-Regular.ttf", "Bebas": FONT / "BebasNeue-Regular.ttf",
                     "Abril": FONT / "AbrilFatface-Regular.ttf"})
VIEWS = HERE / "out" / "views" / "views.json"

PINK = "#ff4fa3"
BUBBLE = "#ff8fc8"
CREAM = "#fff3e2"
PLUM = "#3d0d2e"
LIP = "#c4124f"
LIP_HI = "#ee4a7f"
BLACK = "#17121a"
CHERRY = "#d0103a"
LEAF = "#2f8a3c"
AUBERGINE = "#5a2580"
AUB_HI = "#8a4fb8"
AMBER = "#f2a531"
AMBER_DK = "#c27a12"
FOAM = "#fffaf0"

# ---------------------------------------------------------------- marks, in unit coordinates
LIPS = ('<path d="M-1,0 C-0.7,-0.35 -0.35,-0.55 -0.12,-0.42 Q0,-0.3 0.12,-0.42 C0.35,-0.55 0.7,-0.35 1,0 '
        'C0.5,-0.06 -0.5,-0.06 -1,0 Z" fill="{c}"/>'
        '<path d="M-1,0.04 C-0.5,0.1 0.5,0.1 1,0.04 C0.7,0.55 -0.7,0.55 -1,0.04 Z" fill="{c}"/>'
        '<path d="M-0.55,-0.22 Q-0.35,-0.32 -0.2,-0.3 M0.25,-0.3 Q0.4,-0.3 0.55,-0.2 M-0.4,0.28 Q0,0.36 0.4,0.28 '
        'M-0.15,0.18 L-0.12,0.38 M0.12,0.18 L0.15,0.38" fill="none" stroke="{h}" stroke-width="0.035" stroke-linecap="round"/>')


def lips(c=LIP, h=LIP_HI):
    return LIPS.format(c=c, h=h)


def cherries():
    return (f'<path d="M-0.38,0.25 Q-0.2,-0.4 0.1,-0.7 M0.38,0.3 Q0.25,-0.3 0.1,-0.7" fill="none" stroke="{LEAF}" '
            f'stroke-width="0.07" stroke-linecap="round"/>'
            f'<ellipse cx="0.32" cy="-0.72" rx="0.24" ry="0.1" transform="rotate(-23 0.32 -0.72)" fill="{LEAF}"/>'
            f'<circle cx="-0.4" cy="0.42" r="0.3" fill="{CHERRY}"/><circle cx="0.4" cy="0.47" r="0.3" fill="{CHERRY}"/>'
            '<circle cx="-0.5" cy="0.33" r="0.06" fill="#ffffff" fill-opacity="0.7"/>'
            '<circle cx="0.3" cy="0.38" r="0.06" fill="#ffffff" fill-opacity="0.7"/>')


def eggplant():
    return (f'<path d="M-0.05,-0.45 C0.35,-0.5 0.5,0.1 0.38,0.55 C0.25,1.0 -0.25,1.0 -0.35,0.6 '
            f'C-0.45,0.2 -0.35,-0.25 -0.05,-0.45 Z" fill="{AUBERGINE}"/>'
            f'<path d="M-0.18,0.0 C-0.25,0.3 -0.2,0.55 -0.1,0.7" fill="none" stroke="{AUB_HI}" stroke-width="0.07" '
            f'stroke-linecap="round"/>'
            f'<path d="M-0.3,-0.4 L-0.05,-0.62 L0.05,-0.42 L0.3,-0.5 L0.15,-0.3 L-0.05,-0.25 Z" fill="{LEAF}"/>'
            f'<path d="M-0.02,-0.55 Q0.0,-0.8 0.12,-0.9" fill="none" stroke="{LEAF}" stroke-width="0.07" '
            f'stroke-linecap="round"/>')


def beer():
    """A beer mug: amber glass, a foam head spilling over, a handle on the right."""
    return (f'<path d="M0.42,-0.25 h0.22 a0.18,0.18 0 0 1 0.18,0.18 v0.36 a0.18,0.18 0 0 1 -0.18,0.18 h-0.22" fill="none" '
            f'stroke="{AMBER_DK}" stroke-width="0.12"/>'
            f'<rect x="-0.5" y="-0.45" width="0.95" height="1.2" rx="0.1" fill="{AMBER}"/>'
            f'<path d="M-0.25,-0.2 v0.75 M0.0,-0.2 v0.75 M0.22,-0.2 v0.75" stroke="{AMBER_DK}" stroke-width="0.06" '
            f'stroke-linecap="round"/>'
            f'<path d="M-0.58,-0.4 a0.2,0.2 0 0 1 0.12,-0.36 a0.25,0.25 0 0 1 0.45,-0.08 a0.25,0.25 0 0 1 0.45,0.04 '
            f'a0.2,0.2 0 0 1 0.1,0.4 v0.12 a0.1,0.1 0 0 1 -0.2,0 a0.1,0.1 0 0 0 -0.2,0 v0.08 a0.1,0.1 0 0 1 -0.2,0 '
            f'v-0.1 a0.1,0.1 0 0 0 -0.2,0 a0.1,0.1 0 0 1 -0.2,0 Z" fill="{FOAM}"/>')


def outlined(svg, width=0.09):
    """Art with a white outline round its silhouette, width in the art's own units."""
    return f'<g filter="url(#wo{round(width * 1000)})">{svg}</g>'


def logo(script=CREAM, edge=PLUM, lip=LIP, hi=LIP_HI):
    """The team logo: a big lipstick print with the name written across its lower lip.
    Unit: the lips are 2.6 wide; the whole mark is about 2.6 wide and 1.5 tall."""
    return (f'<g transform="translate(0 -0.22) scale(1.3)" filter="url(#wo70)">{lips(lip, hi)}</g>'
            f'<text x="0" y="0.42" font-family="Pacifico" font-size="0.62" text-anchor="middle" dominant-baseline="central" '
            f'fill="{script}" stroke="{edge}" stroke-width="0.07" paint-order="stroke" stroke-linejoin="round">Sip Vanessa</text>')


_filters = {}


def white_outline(lv, view, width):
    """Defines the white-outline filter for this width on a view, once."""
    key = (id(lv), view, width)
    if key not in _filters:
        _filters[key] = True
        lv.raw(view, f'<defs><filter id="wo{width}" x="-50%" y="-50%" width="200%" height="200%" color-interpolation-filters="sRGB">'
                     f'<feMorphology in="SourceAlpha" operator="dilate" radius="{width / 1000}" result="d"/>'
                     f'<feFlood flood-color="#ffffff"/><feComposite in2="d" operator="in" result="o"/>'
                     f'<feMerge><feMergeNode in="o"/><feMergeNode in="SourceGraphic"/></feMerge></filter></defs>')


def put(lv, view, p, size, svg, rotate=0, flip=False):
    """Unit-coordinate art, size metres per unit, centred on p (car metres). Art that asks for a
    white outline (filter wo<n>) gets the filter defined on that view."""
    for w in set(re.findall(r"url\(#wo(\d+)\)", svg)):
        white_outline(lv, view, int(w))
    u, v = lv.uv(view, p)
    sx = -size if flip else size
    lv.raw(view, f'<g transform="translate({u:.4f} {v:.4f}) rotate({rotate}) scale({sx:.4f} {size:.4f})">{svg}</g>')


def side_put(lv, z, y, size, svg, rotate=0):
    for view in ("left", "right"):
        put(lv, view, lv._side(view, z, y), size, svg, rotate)


def scallop_edge(z0, z1, y_at, r, step):
    """Points along a scalloped edge from z0 to z1 (z falling), bumps of radius r every step."""
    pts = []
    n = int(round((z0 - z1) / step))
    for i in range(n):
        a = z0 - i * step
        for k in range(9):
            t = k / 8
            z = a - t * step
            pts.append((z, y_at(z) + r * math.sin(math.pi * t)))
    pts.append((z1, y_at(z1)))
    return pts


# ---------------------------------------------------------------- Happy Hour
def happy_hour():
    lv = Livery(VIEWS, PINK)

    # The foam band: along the sills at 0.30, kicking up over the intake to 0.56 behind it.
    def foam_y(z):
        if z > -0.25:
            return 0.30
        if z > -0.95:
            return 0.30 + (-0.25 - z) / 0.70 * 0.26
        return 0.56
    top = scallop_edge(2.5, -2.25, foam_y, 0.035, 0.125)
    lv.side_poly(top + [(-2.25, -0.2), (2.5, -0.2)], CREAM)
    lv.side_text("CRAFT BEER &amp; BAR FOOD", 0.2, 0.205, 0.075, PLUM, spacing=0.12, font="Bebas")

    side_put(lv, 0.14, 0.54, 0.21, logo())
    lv.side_number(0.84, 0.54, 0.26, 0.2, PLUM)
    side_put(lv, 0.84, 0.54, 0.155, f'<circle r="1" fill="{CREAM}"/>')
    # The wink: a cherry pair, an eggplant and a beer, in a row on the rear haunch above the arch.
    side_put(lv, -1.0, 0.78, 0.085, outlined(cherries()))
    side_put(lv, -1.3, 0.78, 0.085, outlined(eggplant()), rotate=28)
    side_put(lv, -1.6, 0.76, 0.08, outlined(beer()))
    side_put(lv, 1.98, 0.42, 0.075, outlined(beer()))

    # Top: the logo on the bonnet, read from the nose; the number on the roof.
    put(lv, "top", (0, 0, 1.70), 0.21, logo(), rotate=-90)
    lv.number("top", (0, 0, 0.04), 0.42, 0.3, PLUM, rotate=-90)
    put(lv, "top", (0, 0, 0.04), 0.33, '<circle r="1" fill="#fff3e2"/>')
    put(lv, "top", (0.62, 0, -0.95), 0.15, outlined(cherries()), rotate=-90)
    put(lv, "top", (-0.62, 0, -0.95), 0.15, outlined(eggplant()), rotate=-90)
    for x in (0.62, -0.62):
        put(lv, "top", (x, 0, -1.4), 0.14, outlined(beer()), rotate=-90)

    # Ends: the foam runs round the lower corners; a kiss on the nose, the slogan on the tail.
    for view in ("front", "rear"):
        lv.poly(view, [(-1.2, -0.1, 0), (1.2, -0.1, 0), (1.2, 0.30, 0), (-1.2, 0.30, 0)], CREAM)
    put(lv, "front", (0.0, 0.62, 0), 0.07, outlined(lips(), 0.12))
    lv.text("rear", "CRAFT BEER &amp; BAR FOOD", (0, 0.585, 0), 0.085, CREAM, spacing=0.1, font="Bebas")
    put(lv, "rear", (-0.62, 0.42, 0), 0.08, outlined(lips(), 0.12), rotate=-12)
    return lv


# ---------------------------------------------------------------- Lace & Lager
def lace_side(lv, y, r=0.045):
    step = 2 * r
    for view in ("left", "right"):
        u_lo = min(lv.uv(view, lv._side(view, z, y))[0] for z in (2.5, -2.3))
        u_hi = max(lv.uv(view, lv._side(view, z, y))[0] for z in (2.5, -2.3))
        _, v = lv.uv(view, lv._side(view, 0, y))
        lace_row(lv, view, u_lo, u_hi, v, r)


def lace_row(lv, view, u0, u1, v, r, up=False):
    """A black lace trim: a thin band with scallops hanging off it and a hole in each scallop."""
    d = -1 if up else 1
    parts = [f'<rect x="{u0:.4f}" y="{v - (0.02 if up else 0):.4f}" width="{u1 - u0:.4f}" height="0.02" fill="{BLACK}"/>']
    u = u0
    while u < u1:
        cy = v + d * 0.02
        parts.append(f'<path d="M{u:.4f},{cy:.4f} A{r},{r} 0 0 {0 if up else 1} {u + 2 * r:.4f},{cy:.4f} Z" fill="{BLACK}"/>')
        parts.append(f'<circle cx="{u + r:.4f}" cy="{cy + d * r * 0.45:.4f}" r="{r * 0.22:.4f}" fill="{BUBBLE}"/>')
        u += 2 * r
    lv.raw(view, "".join(parts))


def lace_lager():
    lv = Livery(VIEWS, BUBBLE)
    lace_side(lv, 0.27)
    side_put(lv, 0.16, 0.57, 0.2, logo(script=BLACK, edge=BUBBLE, lip=BLACK, hi="#4a3a52"))
    lv.side_text("artisan bar food", 0.16, 0.34, 0.055, BLACK, font="Abril")
    lv.side_number(0.84, 0.47, 0.26, 0.2, BUBBLE)
    for view in ("left", "right"):
        put(lv, view, lv._side(view, 0.84, 0.47), 0.15, f'<rect x="-1" y="-0.8" width="2" height="1.6" rx="0.25" fill="{BLACK}"/>')
    # The garter: a black band across the bonnet with lace both edges and a pink bow.
    z0, z1 = 1.70, 1.90
    lv.poly("top", [(1.2, 0, z0), (-1.2, 0, z0), (-1.2, 0, z1), (1.2, 0, z1)], BLACK)
    # lace rows run across the car, along v in the top view
    for zz in (z0, z1):
        u, _ = lv.uv("top", (0, 0, zz))
        g = []
        r = 0.035
        vv = -1.1
        d = -1 if zz == z0 else 1
        while vv < 1.1:
            g.append(f'<path d="M{u:.4f},{vv:.4f} A{r},{r} 0 0 {1 if d < 0 else 0} {u:.4f},{vv + 2 * r:.4f} Z" fill="{BLACK}"/>')
            g.append(f'<circle cx="{u + d * r * 0.45:.4f}" cy="{vv + r:.4f}" r="{r * 0.22:.4f}" fill="{BUBBLE}"/>')
            vv += 2 * r
        lv.raw("top", "".join(g))
    put(lv, "top", (0, 0, 1.80), 0.17,
        f'<path d="M0,0 C-0.6,-0.7 -1.1,-0.2 -1,0 C-1.1,0.3 -0.6,0.7 0,0 C0.6,-0.7 1.1,-0.2 1,0 C1.1,0.3 0.6,0.7 0,0 Z" fill="{PINK}"/>'
        f'<circle r="0.18" fill="#e8247f"/>', rotate=-90)
    put(lv, "top", (0, 0, 0.04), 0.2, f'<rect x="-1" y="-0.8" width="2" height="1.6" rx="0.25" fill="{BLACK}"/>', rotate=-90)
    lv.number("top", (0, 0, 0.04), 0.36, 0.26, BUBBLE, rotate=-90)
    put(lv, "top", (0, 0, -1.85), 0.16, logo(script=BLACK, edge=BUBBLE, lip=BLACK, hi="#4a3a52"), rotate=-90)
    # Ends: lace along the nose lip and the tail's lower edge.
    for view in ("front", "rear"):
        u0, _ = lv.uv(view, (1.1 if view == "rear" else -1.1, 0, 0))
        u1, v = lv.uv(view, (-1.1 if view == "rear" else 1.1, 0.42, 0))
        lace_row(lv, view, min(u0, u1), max(u0, u1), v, 0.04)
    put(lv, "front", (0.0, 0.62, 0), 0.07, lips(BLACK, "#4a3a52"))
    lv.text("rear", "artisan bar food", (0, 0.6, 0), 0.055, BLACK, font="Abril")
    return lv


if __name__ == "__main__":
    happy_hour().write(HERE / "out" / "happy-hour")
    lace_lager().write(HERE / "out" / "lace-lager")
