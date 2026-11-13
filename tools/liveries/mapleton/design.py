"""Mapleton Autosport on the Nusa Fox: a love letter to Canada. Soft, warm and a little
sentimental, with fine detail kept small enough to find only up close. Independent: no maker's
logo. The team's roundel (a maple leaf in a red ring) and its roadside town sign.

Two options:
  cottage  Sage green over a still lake along the sills: a pine shoreline, ripples, a canoe, a loon
           and the town sign on the shore. The roof is a contour map of a lake.
  flannel  Warm ivory over a washed-out red buffalo check, edged with a cream stitch line, like a
           favourite shirt. A check maple leaf stitched on the roof, the roundel as a sewn patch.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/NusaFox/NusaFox.obj out/views
     node ../common/digits.js --font font/Fraunces-SemiBold.ttf --out out/digits
     python3 design.py

Fox landmarks in OBJ metres: z along the car (nose +2.11, tail -1.84), y up, x to the car's left
(half width 0.87 without mirrors). Wheels at z +1.23 and -1.22, radius 0.33, arches up to y 0.64.
The one door z 0.74 to -0.45, its bottom y 0.23, belt y 0.93 at the front rising to 1.0 over the
rear quarter. Rear quarter above the arch z -0.5 to -1.7, y 0.67 to 1.0. Roof seen from above
z -1.27 to 0.13 for |x| under 0.53, windscreen to z 0.86, bonnet beyond. Rear hatch under the
glass y 0.61 to 1.02, the maker's badge at y 0.82. Front grille y 0.6.
"""
import json
import math
import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
VIEWS = HERE / "out" / "views"
sys.path.insert(0, str(HERE.parent / "common"))
import livery  # noqa: E402
from livery import Livery, finish  # noqa: E402

livery.FONTS.update({"Fraunces": HERE / "font" / "Fraunces-SemiBold.ttf", "Caveat": HERE / "font" / "Caveat-Bold.ttf"})

# Shared: the team's marks.
MAPLE = "#a8392d"          # maple red
IVORY = "#efe4cf"          # warm ivory
BARK = "#4a3328"           # dark brown ink
PINE = "#4d6350"           # pine green
# Cottage.
SAGE = "#7f977b"
LAKE = "#2e4a55"
SHORE = "#3c5243"          # the pines, a shade off pine so they sit back
# Flannel.
FLANNEL = "#9a3c33"
CHAR = "#2c2422"

F_PAINT = finish(0.86, 0.8, 0.0)     # a factory gloss, no metal

TAIL, NOSE = -1.84, 2.12
SHORE_Y = 0.33                       # sides: lake below, pines rise from here
NUMBER = (0.15, 0.56, 0.40, 0.24)    # door: z, y, box w, h
NAME = (0.15, 0.80, 0.062)           # door: z, y, cap height
ROUNDEL = (-1.02, 0.83, 0.27)        # rear quarter: z, y, height

LEAF = [(50, 5), (56, 20), (64, 16), (61, 40), (75, 26), (78, 34), (92, 31), (87, 46), (95, 50), (73, 66), (76, 76),
        (54, 72), (54, 95), (46, 95), (46, 72), (24, 76), (27, 66), (5, 50), (13, 46), (8, 31), (22, 34), (25, 26),
        (39, 40), (36, 16), (44, 20)]
VEINS = [(50, 8), (88, 33), (12, 33), (78, 62), (22, 62)]
LEAF_D = "M" + " L".join(f"{x - 50},{y - 50}" for x, y in LEAF) + " Z"   # 90 tall, centred


def f(x):
    return f"{x:.4f}"


def side_uv(lv, view, z, y):
    return lv.uv(view, Livery._side(view, z, y))


def nose_dir(view):
    """+1 when the nose is towards +u in this side view, so shapes can face the nose on both sides."""
    return 1 if view == "right" else -1


# --- the team's marks ---------------------------------------------------------------------------------
_ids = iter(range(10 ** 6))


def roundel(lv, view, u, v, h, rotate=0):
    """Logo 1: a maple leaf in a red ring, the name round the top. 192 units across."""
    k = next(_ids)
    s = h / 192
    lv.raw(view, f'<g transform="translate({f(u)} {f(v)}) rotate({rotate}) scale({s:.6f})">'
                 f'<defs><path id="ring{k}" d="M-82,0 A82,82 0 0 1 82,0"/></defs>'
                 f'<circle r="96" fill="{MAPLE}"/><circle r="62" fill="{IVORY}"/>'
                 f'<path transform="translate(0 2) scale(1.02)" d="{LEAF_D}" fill="{MAPLE}"/>'
                 f'<text font-family="Fraunces" font-size="15" letter-spacing="2.2" fill="{IVORY}">'
                 f'<textPath href="#ring{k}" startOffset="50%" text-anchor="middle">MAPLETON AUTOSPORT</textPath></text></g>')


def town_sign(lv, view, u, v, h, post=0.0):
    """Logo 3: the roadside sign, 220 by 110 units, on a post post metres long under it."""
    s = h / 110
    lv.raw(view, f'<g transform="translate({f(u)} {f(v)})">'
                 + (f'<rect x="{f(-0.004)}" y="{f(h / 2 - 0.01)}" width="0.008" height="{f(post + 0.01)}" fill="{BARK}"/>' if post else "")
                 + f'<g transform="scale({s:.6f}) translate(-110 -55)">'
                 f'<rect width="220" height="110" rx="10" fill="{PINE}"/><rect x="8" y="8" width="204" height="94" rx="6" fill="none" stroke="{IVORY}" stroke-width="3"/>'
                 f'<text x="110" y="36" font-family="Fraunces" font-size="15" letter-spacing="3" fill="{IVORY}" text-anchor="middle">WELCOME TO</text>'
                 f'<text x="110" y="74" font-family="Fraunces" font-size="38" fill="{IVORY}" text-anchor="middle">Mapleton</text>'
                 f'<path transform="translate(186 34) scale(0.18)" d="{LEAF_D}" fill="{MAPLE}"/>'
                 f'<text x="110" y="93" font-family="Fraunces" font-size="12" letter-spacing="5" fill="{IVORY}" text-anchor="middle">AUTOSPORT</text></g></g>')


def leaf(u, v, h, fill="none", stroke="none", sw=0.0, rotate=0, veins=None, dash=None):
    """The maple leaf, h metres tall, centred on (u, v). dash: (on, off) metres, for a stitch."""
    s = h / 90
    dashes = f' stroke-dasharray="{dash[0] / s:.3f} {dash[1] / s:.3f}" stroke-linecap="round"' if dash else ""
    out = (f'<g transform="translate({f(u)} {f(v)}) rotate({rotate}) scale({s:.6f})">'
           f'<path d="{LEAF_D}" fill="{fill}" stroke="{stroke}" stroke-width="{sw / s:.3f}" stroke-linejoin="round"{dashes}/>')
    if veins:
        out += "".join(f'<path d="M0,22 Q{(x - 50) * 0.4:.1f},{(y - 50 + 22) / 2:.1f} {(x - 50) * 0.9:.1f},{(y - 50) * 0.9:.1f}" '
                       f'fill="none" stroke="{veins}" stroke-width="{sw * 0.7 / s:.3f}" stroke-linecap="round"/>' for x, y in VEINS)
    return out + "</g>"


# --- cottage country ------------------------------------------------------------------------------------
def pines(r, z0, z1, max_h):
    """A shoreline of pines nose to tail: (z, height) for each tree."""
    out, z = [], z0
    while z < z1:
        out.append((z, max_h * (0.35 + 0.65 * r.random() ** 0.8)))
        z += 0.03 + r.random() * 0.045
    return out


def pine_svg(u, v, h, col):
    """One pine, three tiers, foot at (u, v)."""
    w = h * 0.3
    tiers = []
    for t in range(3):
        base = v - h * 0.27 * t
        tw = w * (1 - t * 0.24)
        tiers.append(f"M{f(u - tw)},{f(base)} L{f(u)},{f(base - h * 0.5)} L{f(u + tw)},{f(base)} Z")
    return (f'<path d="{" ".join(tiers)}" fill="{col}"/>'
            f'<rect x="{f(u - 0.004)}" y="{f(v - 0.01)}" width="0.008" height="0.02" fill="{col}"/>')


def ripples(r, u0, u1, v0, v1, col):
    """Short level strokes on still water, thinning out with depth."""
    out = []
    v = v0 + 0.012
    while v < v1:
        u = u0 + r.random() * 0.1
        while u < u1:
            ln = 0.03 + r.random() * 0.1
            out.append(f'<line x1="{f(u)}" y1="{f(v)}" x2="{f(u + ln)}" y2="{f(v)}" stroke="{col}" stroke-width="0.004" '
                       f'stroke-linecap="round" opacity="{0.15 + r.random() * 0.3:.2f}"/>')
            u += ln + 0.1 + r.random() * 0.2
        v += 0.02
    return "".join(out)


def canoe(u, v, ln, d):
    """A cedar-strip canoe with one paddler, ln metres long, bow towards d."""
    s = ln
    return (f'<g transform="translate({f(u)} {f(v)}) scale({f(d * s)} {f(s)})">'
            f'<path d="M-0.5,-0.08 Q0,0.1 0.5,-0.08 Q0,0.02 -0.5,-0.08 Z" fill="{IVORY}"/>'
            f'<rect x="-0.06" y="-0.26" width="0.07" height="0.2" fill="{BARK}"/><circle cx="-0.025" cy="-0.3" r="0.045" fill="{BARK}"/>'
            f'<line x1="0.12" y1="-0.3" x2="0.02" y2="0.06" stroke="{BARK}" stroke-width="0.022"/></g>')


def loon(u, v, ln, d):
    """A common loon on the water, ln metres long, facing d."""
    return (f'<g transform="translate({f(u)} {f(v)}) scale({f(d * ln)} {f(ln)})" fill="#15201f">'
            f'<ellipse rx="0.5" ry="0.15"/><path d="M0.3,-0.05 Q0.42,-0.33 0.54,-0.35 L0.47,-0.22 Z"/>'
            f'<circle cx="0.52" cy="-0.35" r="0.07"/><path d="M0.58,-0.37 L0.74,-0.34 L0.58,-0.32 Z"/>'
            f'<path d="M-0.3,-0.06 L0.2,-0.06" stroke="{IVORY}" stroke-width="0.03" stroke-dasharray="0.04 0.04"/></g>')


def lake_contours(cz, cx, rz, rx, n, col, seed):
    """Top view: a contour map round a lake, the inner rings filled as water. u = z, v = -x."""
    r = random.Random(seed)
    ph = [r.random() * 7 for _ in range(3)]
    rings = []
    for k in range(1, n + 1):
        s = k / n
        pts = []
        for i in range(126):
            a = i / 126 * math.tau
            w = 1 + 0.18 * math.sin(a * 3 + ph[0] + k * 0.15) + 0.1 * math.sin(a * 5 + ph[1]) + 0.06 * math.sin(a * 2 + ph[2])
            pts.append((cz + math.cos(a) * rz * s * w, -cx + math.sin(a) * rx * s * w))
        d = "M" + " L".join(f"{f(a)},{f(b)}" for a, b in pts) + " Z"
        fill = LAKE if k <= 2 else "none"
        rings.append(f'<path d="{d}" fill="{fill}" fill-opacity="{0.55 if k == 1 else 0.3}" stroke="{col}" stroke-width="0.004"/>')
    return "".join(reversed(rings))


def cottage_livery():
    meta = json.loads((VIEWS / "views.json").read_text())
    lv = Livery(VIEWS / "views.json", SAGE, F_PAINT)
    r = random.Random(7)
    trees = pines(r, TAIL - 0.05, NOSE + 0.05, 0.24)
    for view in ("left", "right"):
        d = nose_dir(view)
        vw = meta["views"][view]
        _, vs = side_uv(lv, view, 0, SHORE_Y)
        lv.raw(view, f'<rect x="{f(vw["u0"])}" y="{f(vs)}" width="{f(vw["u1"] - vw["u0"])}" height="{f(vw["v1"] - vs)}" fill="{LAKE}"/>')
        lv.raw(view, ripples(random.Random(11), vw["u0"], vw["u1"], vs, vw["v1"], IVORY))
        lv.raw(view, "".join(pine_svg(*side_uv(lv, view, z, SHORE_Y + 0.004), h, SHORE) for z, h in trees))
        # The town sign on the shore ahead of the front wheel, the canoe under the door, a loon behind it.
        u, v = side_uv(lv, view, 1.78, SHORE_Y + 0.12)
        town_sign(lv, view, u, v, 0.075, post=0.08)
        lv.raw(view, canoe(*side_uv(lv, view, 0.25, 0.2), 0.36, d))
        lv.raw(view, loon(*side_uv(lv, view, -0.62, 0.17), 0.11, d))
        # A cream pinstripe under the glass, rising with the belt.
        a, b = side_uv(lv, view, 0.74, 0.885), side_uv(lv, view, -1.75, 0.975)
        lv.raw(view, f'<line x1="{f(a[0])}" y1="{f(a[1])}" x2="{f(b[0])}" y2="{f(b[1])}" stroke="{IVORY}" stroke-width="0.007"/>')
        roundel(lv, view, *side_uv(lv, view, ROUNDEL[0], ROUNDEL[1]), ROUNDEL[2])
    lv.side_text("Mapleton Autosport", NAME[0], NAME[1], NAME[2], IVORY, font="Fraunces")
    lv.side_number(NUMBER[0], NUMBER[1], NUMBER[2], NUMBER[3], IVORY)
    # Roof: the lake map. Bonnet: the number, read from the nose.
    lv.raw("top", lake_contours(-0.57, 0, 0.6, 0.46, 9, IVORY, 5))
    lv.number("top", (0, 0, 1.5), 0.52, 0.30, IVORY, rotate=-90)
    # Front and rear: the shoreline carried round the bumpers.
    for view, base, h in (("front", 0.36, 0.13), ("rear", 0.36, 0.16)):
        vw = meta["views"][view]
        vb = -base
        lv.raw(view, f'<rect x="{f(vw["u0"])}" y="{f(vb)}" width="{f(vw["u1"] - vw["u0"])}" height="{f(vw["v1"] - vb)}" fill="{LAKE}"/>')
        lv.raw(view, ripples(random.Random(13), vw["u0"], vw["u1"], vb, vw["v1"], IVORY))
        rr = random.Random(17)
        lv.raw(view, "".join(pine_svg(u, vb + 0.004, hh, SHORE) for u, hh in pines(rr, vw["u0"], vw["u1"], h)))
    lv.text("rear", "Mapleton Autosport", (0, 0.70, 0), 0.045, IVORY, font="Fraunces")
    return lv


# --- flannel -------------------------------------------------------------------------------------------
CHECK_Y = 0.42                       # sides: check below this
CELL = 0.11                          # buffalo check cell, metres


def check_pattern(pid, rotate=0):
    """A washed buffalo check: dark bands both ways, darker where they cross, cream lint across it."""
    c = CELL
    lint = "".join(f'<rect x="{f(random.Random(i).random() * 2 * c)}" y="{f(random.Random(i + 99).random() * 2 * c)}" '
                   f'width="0.012" height="0.0025" fill="{IVORY}" opacity="0.12"/>' for i in range(30))
    return (f'<pattern id="{pid}" width="{f(2 * c)}" height="{f(2 * c)}" patternUnits="userSpaceOnUse" patternTransform="rotate({rotate})">'
            f'<rect width="{f(2 * c)}" height="{f(2 * c)}" fill="{FLANNEL}"/>'
            f'<rect width="{f(c)}" height="{f(2 * c)}" fill="{CHAR}" opacity="0.45"/>'
            f'<rect width="{f(2 * c)}" height="{f(c)}" fill="{CHAR}" opacity="0.45"/>{lint}</pattern>')


def stitch(u0, u1, v, col=IVORY, sw=0.006):
    return (f'<line x1="{f(u0)}" y1="{f(v)}" x2="{f(u1)}" y2="{f(v)}" stroke="{col}" stroke-width="{sw}" '
            f'stroke-dasharray="0.022 0.014" stroke-linecap="round"/>')


def hem(lv, view, top, pid):
    """The check from top down to the ground on a side, front or rear view, with its stitch and a brown pinstripe."""
    vw = meta_views[view]
    v = lv.uv(view, (0, top, 0))[1] if view in ("front", "rear") else side_uv(lv, view, 0, top)[1]
    w = vw["u1"] - vw["u0"]
    lv.raw(view, f'<defs>{check_pattern(pid)}</defs><rect x="{f(vw["u0"])}" y="{f(v)}" width="{f(w)}" height="{f(vw["v1"] - v)}" fill="url(#{pid})"/>'
                 + stitch(vw["u0"], vw["u1"], v + 0.03)
                 + f'<line x1="{f(vw["u0"])}" y1="{f(v - 0.012)}" x2="{f(vw["u1"])}" y2="{f(v - 0.012)}" stroke="{BARK}" stroke-width="0.008"/>')


def patch(inner, u, v, h, w=None):
    """A sewn patch: an ivory ground with a stitched edge round whatever inner draws, in metres."""
    w = w or h
    return (f'<g transform="translate({f(u)} {f(v)})"><rect x="{f(-w / 2)}" y="{f(-h / 2)}" width="{f(w)}" height="{f(h)}" rx="{f(h * 0.18)}" fill="{IVORY}"/>'
            f'<rect x="{f(-w / 2 + 0.012)}" y="{f(-h / 2 + 0.012)}" width="{f(w - 0.024)}" height="{f(h - 0.024)}" rx="{f(h * 0.14)}" fill="none" '
            f'stroke="{FLANNEL}" stroke-width="0.005" stroke-dasharray="0.014 0.009"/>{inner}</g>')


meta_views = {}


def flannel_livery():
    meta = json.loads((VIEWS / "views.json").read_text())
    meta_views.update(meta["views"])
    lv = Livery(VIEWS / "views.json", IVORY, F_PAINT)
    for view in ("left", "right"):
        hem(lv, view, CHECK_Y, f"chk-{view}")
        u, v = side_uv(lv, view, ROUNDEL[0], ROUNDEL[1])
        lv.raw(view, patch("", u, v, ROUNDEL[2] + 0.05, ROUNDEL[2] + 0.05))
        roundel(lv, view, u, v, ROUNDEL[2])
    lv.side_text("Mapleton Autosport", NAME[0], NAME[1], NAME[2] * 1.35, BARK, font="Caveat")
    lv.side_number(NUMBER[0], NUMBER[1], NUMBER[2], NUMBER[3], BARK)
    hem(lv, "front", 0.40, "chk-front")
    hem(lv, "rear", 0.42, "chk-rear")
    # Roof: a big maple leaf cut from the check and stitched on. Bonnet: the number.
    s = 1.0 / 90
    lv.raw("top", f'<defs>{check_pattern("chk-top", 45)}<clipPath id="leaf-top"><path transform="translate(-0.57 0) rotate(90) scale({s:.6f})" d="{LEAF_D}"/></clipPath></defs>'
                  f'<rect x="-1.2" y="-0.6" width="1.3" height="1.2" fill="url(#chk-top)" clip-path="url(#leaf-top)"/>'
                  + leaf(-0.57, 0, 0.94, stroke=IVORY, sw=0.006, rotate=90, dash=(0.022, 0.014)))
    lv.number("top", (0, 0, 1.5), 0.52, 0.30, BARK, rotate=-90)
    # Rear: the town sign as a patch on the hatch beside the maker's badge, the name under it.
    u, v = lv.uv("rear", (0.3, 0.82, 0))
    lv.raw("rear", patch("", u, v, 0.16, 0.28))
    town_sign(lv, "rear", u, v, 0.12)
    lv.text("rear", "Mapleton Autosport", (0, 0.68, 0), 0.06, BARK, font="Caveat")
    return lv


OPTIONS = {"cottage": cottage_livery, "flannel": flannel_livery}

if __name__ == "__main__":
    for name, build in OPTIONS.items():
        build().write(HERE / "out" / name)
