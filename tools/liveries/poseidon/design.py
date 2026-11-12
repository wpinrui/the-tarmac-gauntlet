"""Poseidon Racing on the Tora Hanabi: teal with pink, fish scales, the trident and the life-ring
logo, with a little of the owner's fishing shop. Independent, so no maker's logo. Writes one
folder of panels per option under out/.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/ToraHanabi/ToraHanabi.obj out/views
     python3 marks.py
     python3 design.py
     ./game.sh

Side art is given as (z, y) in the car's metres: z runs nose (+2.02) to tail (-2.11), y up from
the ground. Landmarks on the Hanabi: front wheel at z 1.2, rear at -1.2 (radius 0.31, arch
tops at y 0.69); door from z 0.33 to -0.80, y 0.31 to its top at 0.88; the side's top edge
falls from y 0.88 at the door to 0.53 at the nose and 0.70 at the tail; bonnet z 0.4 to 1.9;
rear deck z -0.9 to -2.0.
"""
import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
MARKS = HERE / "out" / "marks"
sys.path.insert(0, str(HERE.parent / "common"))
from livery import Livery  # noqa: E402

TEAL = "#0e8f8c"
TEAL_D = "#07484d"
TEAL_DD = "#04292d"
SCALE = "#13a19d"
SCALE_EDGE = "#0a6b6b"
FOAM = "#eafaf6"
PINK = "#ff3d8b"
PINK_D = "#c4166a"


def smooth(pts, n=10):
    """A Catmull-Rom curve through pts."""
    out = []
    ext = [pts[0], *pts, pts[-1]]
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1:i + 3]
        for k in range(n):
            t = k / n
            out.append(tuple(0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t ** 3)
                             for a, b, c, d in zip(p0, p1, p2, p3)))
    out.append(pts[-1])
    return out


def strip(line, width):
    """A band width tall centred on a (z, y) line."""
    return [(z, y + width / 2) for z, y in line] + [(z, y - width / 2) for z, y in reversed(line)]


def disc(ca, cb, r, n=28):
    return [(ca + r * math.cos(2 * math.pi * i / n), cb + r * math.sin(2 * math.pi * i / n)) for i in range(n)]


def scale_field(a0, a1, b0, b1, r=0.085, bulge=-1):
    """Fish scales over a rectangle in (a, b): overlapping discs, each column laid over the one
    behind it, so each shows a crescent toward +bulge along a. Yields (edge, face) shapes."""
    cols = [a0 + i * r for i in range(int((a1 - a0) / r) + 3)]
    cols.sort(key=lambda a: a * bulge, reverse=True)
    for a in cols:
        off = r * 0.9 if round((a - a0) / r) % 2 else 0
        b = b0 - 2 * r + off
        while b < b1 + 2 * r:
            yield disc(a, b, r), disc(a, b, r * 0.84)
            b += 1.8 * r


# The pink pinstripe along the side's top, clear of the wheel arches.
SHOULDER = smooth([(2.06, 0.50), (1.7, 0.62), (1.25, 0.74), (0.8, 0.80), (0.2, 0.82), (-1.2, 0.84),
                   (-1.75, 0.83), (-2.12, 0.66)])


def scale_mail(z0=-0.05):
    """Plain teal at the front, scales from z0 back, a pink pinstripe nose to tail."""
    def make(views):
        lv = Livery(views, TEAL)
        # Scales: whole scales only, so the front edge is scalloped like a fish's.
        for edge, face in scale_field(-2.3, z0, 0.15, 1.05):
            lv.side_poly(edge, SCALE_EDGE)
            lv.side_poly(face, SCALE)
        for edge, face in scale_field(-2.3, z0, -1.0, 1.0):
            lv.poly("top", [(-b, 0, a) for a, b in edge], SCALE_EDGE)
            lv.poly("top", [(-b, 0, a) for a, b in face], SCALE)
        for edge, face in scale_field(-0.1, 1.2, -1.0, 1.0, bulge=-1):  # rear: free edges point down
            lv.poly("rear", [(b, a, 0) for a, b in edge], SCALE_EDGE)
            lv.poly("rear", [(b, a, 0) for a, b in face], SCALE)
        # Dark sill under the doors, pinstripe on the shoulder.
        lv.side_poly([(2.3, 0.0), (2.3, 0.24), (-2.3, 0.24), (-2.3, 0.0)], TEAL_DD)
        lv.band_x(-2, 2, TEAL_DD, views=("front", "rear"), y=(-0.1, 0.24))
        lv.side_poly(strip(SHOULDER, 0.035), PINK)
        lv.band_x(-2, 2, PINK, views=("rear",), y=(0.645, 0.68))
        # Name on the door's front half and the front wing, the shop under it.
        lv.side_mark(MARKS / "name-alfa.svg", 0.40, 0.62, 0.135)
        lv.side_mark(MARKS / "sub-pink.svg", 0.40, 0.49, 0.05)
        # Number on a foam plate on the door's back half, over the scales.
        lv.side_poly([(-0.18, 0.47), (-0.18, 0.75), (-0.72, 0.75), (-0.72, 0.47)], FOAM)
        lv.side_number(-0.45, 0.61, 0.46, 0.2, TEAL_DD)
        # Trident down the bonnet, prongs to the nose; logo on the rear deck.
        lv.mark("top", MARKS / "trident-pink.svg", (0, 0, 1.12), 1.05, rotate=90)
        lv.mark("top", MARKS / "logo.svg", (0, 0, -1.5), 0.5, rotate=90)
        # Shop line on the tail.
        lv.mark("rear", MARKS / "rods.svg", (0, 0.44, 0), 0.07)
        lv.mark("rear", MARKS / "sub-pink.svg", (0, 0.35, 0), 0.04)
        return lv
    return make


def waterline():
    """A boat's hull: foam topsides, teal below a waterline that rises to bow and stern, a pink
    boot stripe on it and scales under the water."""
    def wl(z):
        return 0.42 + 0.06 * ((z + 0.04) / 2.08) ** 2

    def make(views):
        lv = Livery(views, FOAM)
        line = [(z / 20, wl(z / 20)) for z in range(-44, 45)]
        lv.side_poly(line + [(2.3, -0.1), (-2.3, -0.1)], TEAL)
        for edge, face in scale_field(-2.3, 2.3, -0.1, 0.6, bulge=-1):
            lv.side_poly(edge, SCALE_EDGE)
            lv.side_poly(face, SCALE)
        lv.side_poly(line + [(2.3, 2.0), (-2.3, 2.0)], FOAM)  # the scales stop at the water
        lv.side_poly(strip(line, 0.06), PINK)
        bow, stern = wl(2.02), wl(-2.11)
        lv.band_x(-2, 2, TEAL, views=("front",), y=(-0.1, bow))
        lv.band_x(-2, 2, TEAL, views=("rear",), y=(-0.1, stern))
        lv.band_x(-2, 2, PINK, views=("front",), y=(bow - 0.03, bow + 0.03))
        lv.band_x(-2, 2, PINK, views=("rear",), y=(stern - 0.03, stern + 0.03))
        # Name amidships, the number forward on the wing like a boat's bow number.
        lv.side_mark(MARKS / "name-ops.svg", -0.32, 0.73, 0.13)
        lv.side_mark(MARKS / "sub-pinkd.svg", -0.32, 0.61, 0.045)
        lv.side_number(0.6, 0.72, 0.46, 0.2, TEAL_D)
        # Teal trident down the bonnet, logo on the rear deck.
        lv.mark("top", MARKS / "trident-teal.svg", (0, 0, 1.12), 1.05, rotate=90)
        lv.mark("top", MARKS / "logo.svg", (0, 0, -1.5), 0.5, rotate=90)
        lv.mark("rear", MARKS / "hooked.svg", (0, 0.58, 0), 0.06)
        return lv
    return make


OPTIONS = {"scale-mail": scale_mail(), "waterline": waterline()}

if __name__ == "__main__":
    views = HERE / "out" / "views" / "views.json"
    for name, make in OPTIONS.items():
        make(views).write(HERE / "out" / name)
