"""Tora GT Works on the Tora Giga: Team Tora Racing's folded facets in greyscale, with the
team's own logo (a TORA tab over a red GT block and a black WORKS block) in place of the
seal and the tiger head.

Run: python3 design.py   (after views.js has drawn out/views)
"""
import math
import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "common"))
import livery  # noqa: E402
from livery import Livery  # noqa: E402

VIEWS = HERE / "out" / "views" / "views.json"

SHADES = ["#d9d9d9", "#bdbdbd", "#a3a3a3", "#8a8a8a", "#707070", "#575757"]
BLACK = "#16171a"
SHADE = "rgba(22,23,26,0.5)"  # side streaks darken the facets under them
WHITE = "#f5f5f3"
RED = "#d7261e"
BASE = "#a3a3a3"
NAME = "TORA GT WORKS"
livery.FONTS["Saira"] = HERE / "font" / "SairaExtraCondensed-ExtraBold.ttf"

# The logo in its own units: 200 wide (before the lean) and 94 tall, centred on (0, 0).
LOGO = f"""<g transform="skewX(-12) translate(0 15)">
<rect x="-100" y="-62" width="84" height="30" fill="{WHITE}"/>
<text x="-58" y="-46" font-family="Saira" font-size="28" fill="{BLACK}" text-anchor="middle" dominant-baseline="central" textLength="60" lengthAdjust="spacingAndGlyphs">TORA</text>
<rect x="-100" y="-28" width="66" height="60" fill="{RED}"/>
<text x="-67" y="3" font-family="Saira" font-size="58" fill="{WHITE}" text-anchor="middle" dominant-baseline="central" textLength="42" lengthAdjust="spacingAndGlyphs">GT</text>
<rect x="-30" y="-28" width="130" height="60" fill="{BLACK}"/>
<text x="35" y="3" font-family="Saira" font-size="58" fill="{WHITE}" text-anchor="middle" dominant-baseline="central" textLength="106" lengthAdjust="spacingAndGlyphs">WORKS</text>
</g>"""


def logo(lv, view, p, height, rotate=0):
    u, v = lv.uv(view, p)
    lv.raw(view, f'<g transform="translate({u:.4f} {v:.4f}) rotate({rotate}) scale({height / 94:.6f})">{LOGO}</g>')


def side_logo(lv, z, y, height):
    for view in ("left", "right"):
        logo(lv, view, lv._side(view, z, y), height)


def facets(a0, a1, b0, b1, cell, tilt, bias, seed):
    """Triangles over a rectangle in (a, b), each with a grey. bias(a, b) is 0 for light, 1 for dark."""
    r = random.Random(seed)
    na, nb = math.ceil((a1 - a0) / cell) + 3, math.ceil((b1 - b0) / cell) + 3
    g = [[(a0 + (i - 1) * cell + (r.random() - 0.5) * cell * 0.8 + (j - 1) * cell * tilt - tilt * cell * nb / 2,
           b0 + (j - 1) * cell + (r.random() - 0.5) * cell * 0.8) for i in range(na + int(abs(tilt) * nb) + 1)] for j in range(nb)]
    out = []

    def tri(p, q, s):
        ca, cb = (p[0] + q[0] + s[0]) / 3, (p[1] + q[1] + s[1]) / 3
        t = max(0.0, min(0.999, bias(ca, cb) + (r.random() - 0.5) * 0.75))
        out.append(([p, q, s], SHADES[int(t * 6)]))

    for j in range(nb - 1):
        for i in range(len(g[j]) - 1):
            A, B, C, D = g[j][i], g[j][i + 1], g[j + 1][i + 1], g[j + 1][i]
            if r.random() < 0.5:
                tri(A, B, C); tri(A, C, D)
            else:
                tri(A, B, D); tri(B, C, D)
    return out


def streak(a0, b0, a1, b1, w):
    """A bold blade from (a0, b0), w wide at its root, tapering to a point at (a1, b1)."""
    da, db = a1 - a0, b1 - b0
    n = math.hypot(da, db)
    pa, pb = -db / n * w / 2, da / n * w / 2
    return [(a0 + pa, b0 + pb), (a0 + da * 0.55 + pa * 0.7, b0 + db * 0.55 + pb * 0.7), (a1, b1), (a0 - pa, b0 - pb)]


def top(lv, pts, fill):
    lv.poly("top", [(x, 0, z) for x, z in pts], fill)


def end(lv, view, pts, fill):
    lv.poly(view, [(x, y, 0) for x, y in pts], fill)


def team_name(lv, view, p, height):
    u, v = lv.uv(view, p)
    lv.raw(view, f'<text transform="translate({u:.4f} {v:.4f}) skewX(-12)" font-family="Saira" font-size="{height / 0.7:.4f}" '
                 f'letter-spacing="{0.03 * height:.4f}" fill="{WHITE}" text-anchor="middle" dominant-baseline="central">{NAME}</text>')


def folded():
    lv = Livery(VIEWS, BASE)
    wave = lambda a, b: 0.5 + 0.4 * math.sin(a * 1.6 + b * 2)
    for pts, c in facets(-2.5, 2.5, -0.05, 1.45, 0.55, 0.6, wave, 71):
        lv.side_poly(pts, c)
    for pts, c in facets(-1.1, 1.1, -2.5, 2.5, 0.55, 0.6, lambda x, z: wave(z, x), 72):
        top(lv, pts, c)
    for pts, c in facets(-1.1, 1.1, -0.05, 1.45, 0.55, 0.6, lambda x, y: 0.25, 73):
        end(lv, "front", pts, c)
    for pts, c in facets(-1.1, 1.1, -0.05, 1.45, 0.55, 0.6, lambda x, y: 0.8, 74):
        end(lv, "rear", pts, c)
    for s in (streak(2.3, 0.8, -2.2, 0.98, 0.22), streak(-2.45, 0.42, -0.6, 0.33, 0.14)):
        lv.side_poly(s, SHADE)
    for x in (-0.15, 0.15):
        end(lv, "front", streak(x * 5, 0.62, x * 2, 0.9, 0.2), BLACK)
    lv.side_number(0.1, 0.6, 0.74, 0.42, BLACK, edge=WHITE)
    side_logo(lv, -0.9, 0.68, 0.2)   # where the tiger head was
    side_logo(lv, 0.92, 0.8, 0.14)   # where the seal was
    # Bonnet and roof read from in front of the car.
    lv.number("top", (0, 0, -0.4), 0.8, 0.44, BLACK, rotate=-90, edge=WHITE)
    logo(lv, "top", (0, 0, 1.45), 0.36, rotate=-90)
    for view in ("left", "right"):
        team_name(lv, view, lv._side(view, 0.16, 0.28), 0.09)
    logo(lv, "rear", (0, 0.58, 0), 0.16)
    return lv


if __name__ == "__main__":
    folded().write(HERE / "out" / "folded")
