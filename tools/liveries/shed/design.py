"""Shed Monozukuri Co. on the Liberty Omaha: a Japanese independent that builds its car in an
actual garden shed, with a bonsai grower's patience. Calm, precise, natural. The team's red
小屋 seal is the logo. Independent, so no maker's logo. Writes one folder of panels per option
under out/.

  hinoki    washi cream over a planed hinoki lower body, split by one sumi pinstripe
  kintsugi  deep pine green, mended in gold from the lower shut lines

Both share one decal plan, so nothing fights the seal: ものづくり on the front wing, the seal and
the number on the door, the wordmark on the rear quarter, a joiner's rule along the pinstripe.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/LibertyOmaha/LibertyOmaha.obj out/views
     node ../common/digits.js --font font/ShipporiMincho-ExtraBold.ttf --out out/digits
     python3 design.py

Omaha landmarks in OBJ metres: z along the car (nose +1.92, tail -1.77), y up, x to the car's left
(half width 0.84). Wheels at z +-1.19, radius 0.28, arches 0.33. Door z 0.78 to -0.40, bottom
y 0.26, belt y 0.83. Bonnet z 0.78 to 1.83, |x| under 0.65. Roof z 0.25 to -0.95.
The fonts are cut down to the glyphs used here (fontTools pyftsubset).
"""
import base64
import io
import json
import math
import random
import sys
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
VIEWS = HERE / "out" / "views"
sys.path.insert(0, str(HERE.parent / "common"))
import livery  # noqa: E402
from livery import Livery, finish  # noqa: E402

livery.FONTS.update({
    "Mincho": HERE / "font" / "ShipporiMincho-ExtraBold.ttf",
    "Brush": HERE / "font" / "YujiSyuku-Regular.ttf",
    "Gothic": HERE / "font" / "ZenKakuGothicNew-Bold.ttf",
})

WASHI = "#ece3cf"
SUMI = "#2b2b2b"
SHU = "#c8372d"           # the seal's red
PINE = "#2f4a3a"
GOLD = "#c9a24e"
HINOKI = np.array([0xd8, 0xb5, 0x80]) / 255
HINOKI_LATE = np.array([0xa6, 0x77, 0x42]) / 255   # the darker latewood lines

# Finishes, as shares of the livery shader's smoothness, clear coat and metal.
F_PAINT = finish(0.9, 1.0, 0.0)
F_PINE = finish(0.9, 1.0, 0.5)
F_WOOD = (0.45, 0.25, 0.0)
F_GOLD = finish(0.9, 0.7, 1.0)

LINE_Y = 0.52             # the pinstripe, along the door's crease
LINE_W = 0.012
SEAL = (0.55, 0.67, 0.22)           # door: z, y, size
NUMBER = (0.0, 0.67, 0.40, 0.22)    # door: z, y, box w, h
CRAFT = (1.22, 0.72, 0.085)         # front wing: ものづくり
WORD = (-0.98, 0.72, 0.034)         # rear quarter: wordmark cap height


# --- drawing helpers, in car metres -------------------------------------------------------------
def side(view, z, y):
    return Livery._side(view, z, y)


def kanji(lv, view, s, p, size, fill, font="Mincho", rotate=0, spacing=0.0):
    """Japanese text, size metres per character (the em)."""
    lv.text(view, s, p, size * 0.72, fill, rotate=rotate, spacing=spacing, font=font)


def seal(lv, view, p, size, rotate=0):
    """The team's logo: a red seal with 小屋 cut out in washi, stacked."""
    u, v = lv.uv(view, p)
    s = size
    lv.raw(view, f'<g transform="translate({u:.4f} {v:.4f}) rotate({rotate})">'
                 f'<rect x="{-s / 2:.4f}" y="{-s / 2:.4f}" width="{s:.4f}" height="{s:.4f}" rx="{s * 0.07:.4f}" fill="{SHU}"/>'
                 f'<rect x="{-s * 0.415:.4f}" y="{-s * 0.415:.4f}" width="{s * 0.83:.4f}" height="{s * 0.83:.4f}" rx="{s * 0.03:.4f}" '
                 f'fill="none" stroke="{WASHI}" stroke-width="{s * 0.035:.4f}"/>'
                 f'<text x="0" y="{-s * 0.19:.4f}" font-family="Mincho" font-size="{s * 0.36:.4f}" fill="{WASHI}" text-anchor="middle" dominant-baseline="central">小</text>'
                 f'<text x="0" y="{s * 0.19:.4f}" font-family="Mincho" font-size="{s * 0.36:.4f}" fill="{WASHI}" text-anchor="middle" dominant-baseline="central">屋</text>'
                 '</g>')


def stroke(lv, view, pts, width, colour, fin=None):
    """A stroked line through car points; fin also draws it on the finish layer."""
    d = " ".join(f"{'M' if i == 0 else 'L'}{u:.4f},{v:.4f}" for i, (u, v) in enumerate(lv.uv(view, p) for p in pts))
    lv.raw(view, f'<path d="{d}" fill="none" stroke="{colour}" stroke-width="{width:.4f}" stroke-linecap="round" stroke-linejoin="round"/>')
    if fin:
        lv.raw(view, f'<path d="{d}" fill="none" stroke="{fin}" stroke-width="{width:.4f}" stroke-linecap="round" stroke-linejoin="round"/>', finish=True)


def rule(lv, colour, fin=None):
    """The pinstripe round the car at LINE_Y, with a joiner's rule hanging from it on the sides:
    a tick every 5 cm, longer every 25, longest every metre from the nose."""
    for view in lv.views:
        if view == "top":
            continue
        f = lv.views[view]
        lv.raw(view, f'<rect x="{f["u0"]:.4f}" y="{-LINE_Y - LINE_W / 2:.4f}" width="{f["u1"] - f["u0"]:.4f}" height="{LINE_W}" fill="{colour}"/>')
        if fin:
            lv.raw(view, f'<rect x="{f["u0"]:.4f}" y="{-LINE_Y - LINE_W / 2:.4f}" width="{f["u1"] - f["u0"]:.4f}" height="{LINE_W}" fill="{fin}"/>', finish=True)
    for view in ("left", "right"):
        for k in range(0, 71):
            z = 1.75 - k * 0.05
            n = round((1.75 - z) * 100)
            ln = 0.055 if n % 100 == 0 else 0.035 if n % 25 == 0 else 0.018
            stroke(lv, view, [side(view, z, LINE_Y), side(view, z, LINE_Y - ln)], 0.0045, colour, fin)


def decals(lv, ink, word_ink):
    """The shared decal plan."""
    for view in ("left", "right"):
        kanji(lv, view, "ものづくり", side(view, CRAFT[0], CRAFT[1]), CRAFT[2], ink, font="Brush")
        seal(lv, view, side(view, SEAL[0], SEAL[1]), SEAL[2])
        lv.text(view, "SHED MONOZUKURI CO.", side(view, WORD[0], WORD[1]), WORD[2], word_ink, spacing=0.35, font="Gothic")
    lv.side_number(NUMBER[0], NUMBER[1], NUMBER[2], NUMBER[3], ink)
    # Bonnet: the seal, its top to the windscreen. Roof: the number, read from the nose.
    seal(lv, "top", (0, 0, 1.32), 0.34, rotate=-90)
    lv.number("top", (0, 0, -0.38), 0.52, 0.30, ink, rotate=-90)
    # The front stays plain: its bumper slots would cut a seal in two. Rear: the wordmark under the glass, over the name badge.
    lv.text("rear", "SHED MONOZUKURI CO.", (0, 0.822, 0), 0.03, word_ink, spacing=0.35, font="Gothic")


# --- hinoki ---------------------------------------------------------------------------------------
def grid(view, meta):
    """Each pixel's (along, across, y) in car metres: along runs nose to tail on the sides and top,
    across the car on the front and rear, so the grain runs the long way on every panel."""
    f, ppm = meta["views"][view], meta["ppm"]
    w, h = f["px"]
    u = f["u0"] + (np.arange(w) + 0.5) / ppm
    v = f["v0"] + (np.arange(h) + 0.5) / ppm
    U, V = np.meshgrid(u, v)
    if view in ("left", "right"):
        return (-U if view == "left" else U), -V, -V
    if view == "top":
        return U, -V, None
    return U, -V, -V


def noise1(x, cell, seed):
    rng = np.random.default_rng(seed)
    g = x / cell
    i = np.floor(g).astype(int)
    t = g - i
    o = i.min()
    vals = rng.random(i.max() - o + 2)
    s = t * t * (3 - 2 * t)
    return vals[i - o] * (1 - s) + vals[i - o + 1] * s


def wood(along, across, seed):
    """Quarter-sawn hinoki: straight, close latewood lines that drift a little, and soft colour
    running down the boards. Boards 0.12 m wide, each its own shade, meeting in a fine dark joint."""
    board = np.floor(across / 0.12)
    rng = np.random.default_rng(seed)
    shades = rng.normal(0, 0.035, 64)
    shade = shades[(board.astype(int) % 64)]
    drift = (noise1(along * 0.7 + board * 3.1, 0.9, seed + 1) - 0.5) * 0.03
    phase = (across + drift) / 0.0075
    late = np.clip((np.abs(np.sin(phase * math.pi)) - 0.80) * 5, 0, 1)
    late *= 0.5 + 0.5 * noise1(along + board * 7.7, 0.35, seed + 2)
    t = np.clip(late * 0.75, 0, 1)
    col = HINOKI[None, None] * (1 - t)[..., None] + HINOKI_LATE[None, None] * t[..., None]
    col = col * (1 + shade)[..., None]
    joint = np.abs(across / 0.12 - np.round(across / 0.12)) * 0.12 < 0.0012
    col[joint] = col[joint] * 0.72
    return np.clip(col, 0, 1)


def uri(rgba):
    im = Image.fromarray((np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA")
    buf = io.BytesIO()
    im.save(buf, "PNG", optimize=True)
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode()


def hinoki():
    meta = json.loads((VIEWS / "views.json").read_text())
    lv = Livery(VIEWS / "views.json", WASHI, F_PAINT)
    for k, view in enumerate(meta["views"]):
        along, across, y = grid(view, meta)
        # Below the line on the sides, front and rear; the whole bonnet on the top.
        mask = (along > 0.78) & (np.abs(across) < 0.70) if view == "top" else (y < LINE_Y)
        col = wood(along, across, 40 + k)
        a = mask.astype(float)[..., None]
        lv.image(view, uri(np.concatenate([col, a], -1)))
        fin = np.empty(col.shape)
        fin[:] = F_WOOD
        lv.image(view, uri(np.concatenate([fin, a], -1)), finish=True)
    # The bonnet's wood ends in a sumi line at its shut line.
    lv.poly("top", [(0.72, 0, 0.77), (-0.72, 0, 0.77), (-0.72, 0, 0.79), (0.72, 0, 0.79)], SUMI)
    rule(lv, SUMI)
    decals(lv, SUMI, SUMI)
    return lv


# --- kintsugi -------------------------------------------------------------------------------------
def seam(rng, start, heading, length, step=0.035, wander=0.55):
    """A gold repair: a crack that wanders about a heading, with short branches. Points in the
    view's own 2D car coordinates. Returns [main, *branches]."""
    x, y = start
    a = heading
    main, branches = [(x, y)], []
    for _ in range(int(length / step)):
        a += rng.uniform(-wander, wander)
        a = a * 0.7 + heading * 0.3
        x, y = x + math.cos(a) * step, y + math.sin(a) * step
        main.append((x, y))
        if rng.random() < 0.13:
            b = a + rng.choice((-1, 1)) * rng.uniform(0.6, 1.1)
            bx, by, br = x, y, [(x, y)]
            for _ in range(rng.randint(3, 7)):
                b += rng.uniform(-0.4, 0.4)
                bx, by = bx + math.cos(b) * step, by + math.sin(b) * step
                br.append((bx, by))
            branches.append(br)
    return [main, *branches]


# Seams, all below the pinstripe on the sides: (z, y) start, heading in radians in (z, y), length.
SIDE_SEAMS = [((0.78, 0.36), 0.15, 0.55, 3), ((0.78, 0.30), -0.35, 0.30, 4),
              ((-0.40, 0.38), math.pi - 0.1, 0.42, 5), ((0.30, 0.26), 1.35, 0.18, 6)]
# Bonnet, from its shut line forward: (z, x) start, heading, length.
TOP_SEAMS = [((0.80, 0.28), 0.25, 0.75, 7), ((0.80, -0.36), -0.15, 0.55, 8)]


def kintsugi():
    lv = Livery(VIEWS / "views.json", PINE, F_PINE)
    gold = lambda view, pts, w: stroke(lv, view, pts, w, GOLD, F_GOLD)
    for (z, y), hd, ln, sd in SIDE_SEAMS:
        for view in ("left", "right"):
            for i, line in enumerate(seam(random.Random(sd), (z, y), hd, ln)):
                gold(view, [side(view, a, b) for a, b in line], 0.010 if i == 0 else 0.005)
    for (z, x), hd, ln, sd in TOP_SEAMS:
        for i, line in enumerate(seam(random.Random(sd), (z, x), hd, ln)):
            gold("top", [(b, 0, a) for a, b in line], 0.010 if i == 0 else 0.005)
    for i, line in enumerate(seam(random.Random(9), (-0.55, 0.30), 0.2, 0.45)):
        gold("rear", [(a, b, 0) for a, b in line], 0.009 if i == 0 else 0.005)
    rule(lv, GOLD, F_GOLD)
    decals(lv, GOLD, GOLD)
    return lv


OPTIONS = {"hinoki": hinoki, "kintsugi": kintsugi}

if __name__ == "__main__":
    for name, build in OPTIONS.items():
        build().write(HERE / "out" / name)
