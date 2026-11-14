"""Team Tora Racing on the Tora Giga: cubist facets in shades of orange, bold black streaks,
the Tora badge outlined in white and the 虎 seal. Two candidates, Shatter and Folded.

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
BADGE = HERE / "marks" / "tora-badge-outline.svg"
SEAL = HERE / "marks" / "seal.svg"

SHADES = ["#ffb04a", "#f8913a", "#ee6a1f", "#e05a1a", "#c94b14", "#a83c10"]
BLACK = "#16130f"
SHADE = "rgba(22,19,15,0.5)"  # side streaks darken the facets under them
WHITE = "#f6f3ee"
BASE = "#ee6a1f"
NAME = "TEAM TORA RACING"
livery.FONTS["Saira"] = HERE / "font" / "SairaExtraCondensed-ExtraBold.ttf"


def facets(a0, a1, b0, b1, cell, tilt, bias, seed):
    """Triangles over a rectangle in (a, b), each with an orange shade. bias(a, b) is 0 for light, 1 for dark."""
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


def side(lv, pts, fill):
    lv.side_poly(pts, fill)


def top(lv, pts, fill):
    """pts in (x, z)."""
    lv.poly("top", [(x, 0, z) for x, z in pts], fill)


def end(lv, view, pts, fill):
    """pts in (x, y) on the front or rear."""
    lv.poly(view, [(x, y, 0) for x, y in pts], fill)


def rect(c0, c1, w, h):
    return [(c0 - w / 2, c1 - h / 2), (c0 + w / 2, c1 - h / 2), (c0 + w / 2, c1 + h / 2), (c0 - w / 2, c1 + h / 2)]


def door_number(lv, z, y, w, h):
    """The race number in black outlined in white (the livery's own digits), on both sides."""
    lv.side_number(z, y, w, h, BLACK, edge=WHITE)


def roof_number(lv, z):
    # Reads from in front of the car: the panel's width runs across the car.
    lv.number("top", (0, 0, z), 0.8, 0.44, BLACK, rotate=-90, edge=WHITE)


def team_name(lv, view, p, height):
    """The team name in logo 5's lettering: Saira Extra Condensed, leaning forward 12 degrees."""
    u, v = lv.uv(view, p)
    lv.raw(view, f'<text transform="translate({u:.4f} {v:.4f}) skewX(-12)" font-family="Saira" font-size="{height / 0.7:.4f}" '
                 f'letter-spacing="{0.03 * height:.4f}" fill="{WHITE}" text-anchor="middle" dominant-baseline="central">{NAME}</text>')


def badge_top(lv, z, h):
    lv.mark("top", BADGE, (0, 0, z), h, rotate=-90)


def fill_views(lv, cell, tilt, side_bias, top_bias, seed):
    for pts, c in facets(-2.5, 2.5, -0.05, 1.45, cell, tilt, side_bias, seed):
        side(lv, pts, c)
    for pts, c in facets(-1.1, 1.1, -2.5, 2.5, cell, tilt, lambda x, z: top_bias(z, x), seed + 1):
        top(lv, pts, c)
    for pts, c in facets(-1.1, 1.1, -0.05, 1.45, cell, tilt, lambda x, y: 0.25, seed + 2):
        end(lv, "front", pts, c)
    for pts, c in facets(-1.1, 1.1, -0.05, 1.45, cell, tilt, lambda x, y: 0.8, seed + 3):
        end(lv, "rear", pts, c)


def shatter():
    lv = Livery(VIEWS, BASE)
    nose = lambda z, _: 0.5 - z / 5  # light at the nose, dark at the tail
    fill_views(lv, 0.3, 0.0, nose, nose, 11)
    for s in (streak(-2.45, 1.0, 1.7, 0.74, 0.2), streak(-2.45, 0.5, 0.5, 0.36, 0.15), streak(2.45, 0.86, 0.7, 0.8, 0.1)):
        side(lv, s, SHADE)
    for x in (-0.72, 0.72):
        top(lv, streak(x, -2.45, x * 0.9, 0.3, 0.18), BLACK)
    for x in (-0.9, 0.9):
        end(lv, "front", streak(x, 1.0, x * 0.25, 0.78, 0.14), BLACK)
    for x in (-1.05, 1.05):
        end(lv, "rear", streak(x, 0.92, 0.0, 0.78, 0.16), BLACK)
    door_number(lv, 0.0, 0.58, 0.66, 0.36)
    roof_number(lv, -0.4)
    badge_top(lv, 1.5, 0.72)
    lv.side_mark(SEAL, -0.87, 0.6, 0.3)
    return lv


def folded():
    lv = Livery(VIEWS, BASE)
    fill_views(lv, 0.55, 0.6, lambda z, y: 0.5 + 0.4 * math.sin(z * 1.6 + y * 2), lambda z, x: 0.5 + 0.4 * math.sin(z * 1.6 + x * 2), 71)
    for s in (streak(2.3, 0.8, -2.2, 0.98, 0.22), streak(-2.45, 0.42, -0.6, 0.33, 0.14)):
        side(lv, s, SHADE)
    for x in (-0.15, 0.15):
        end(lv, "front", streak(x * 5, 0.62, x * 2, 0.9, 0.2), BLACK)
    door_number(lv, 0.1, 0.6, 0.74, 0.42)
    lv.side_mark(BADGE, -0.86, 0.68, 0.32)
    lv.side_mark(SEAL, 0.84, 0.8, 0.24)
    roof_number(lv, -0.4)
    badge_top(lv, 1.45, 0.7)
    for view in ("left", "right"):
        team_name(lv, view, lv._side(view, 0.16, 0.28), 0.09)
    team_name(lv, "rear", (0, 0.58, 0), 0.08)
    return lv


if __name__ == "__main__":
    for name, make in (("folded", folded),):
        make().write(HERE / "out" / name)
