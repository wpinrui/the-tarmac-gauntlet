"""Duct Tape Dynamics on the Nusa Carrier: an American shitbox, olive green once, now about 85% rust.
The race number is brushed on in white by hand, a little crooked and not where it is on the other
side. A hole in the bonnet is patched with black gaffer tape. Independent, so no maker's logo.
The rust, paint and tape are rasters drawn here in the car's own metres; the finish layer makes the
rust and tape matte and leaves the last of the olive paint a tired gloss.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/NusaCarrier/NusaCarrier.obj out/views
     node ../common/digits.js --font font/CaveatBrush-Regular.ttf --brush 0.7 --out out/digits
     python3 design.py

Carrier landmarks in OBJ metres: z along the car (nose +1.96, tail -2.17), y up, x to the car's left
(half width 0.83). Wheels at z 1.25 and -1.33, radius 0.31. Front door from z 0.70 to -0.31,
y 0.23 to 0.78. Bonnet from z 0.75 to 1.90, x within 0.68.
"""
import base64
import io
import math
import random
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
VIEWS = HERE / "out" / "views"
sys.path.insert(0, str(HERE.parent / "common"))
import livery as lvmod  # noqa: E402
from livery import Livery, finish  # noqa: E402

lvmod.FONTS["CaveatBrush"] = HERE / "font" / "CaveatBrush-Regular.ttf"

OLIVE = np.array([0x48, 0x4e, 0x29]) / 255
OLIVE_SUN = np.array([0x62, 0x66, 0x42]) / 255
PRIMER = np.array([0x8f, 0x8a, 0x78]) / 255
RUST = [np.array(c) / 255 for c in ([0x35, 0x1b, 0x0e], [0x62, 0x30, 0x16], [0x86, 0x45, 0x1e], [0xa0, 0x5c, 0x28], [0x22, 0x14, 0x0c])]
TAPE = np.array([0x17, 0x17, 0x18]) / 255
INK = "#efece3"
BASE = "#4a2512"

WHEELS = ((1.25, 0.31), (-1.33, 0.31))
PAINT_SHARE = 0.15
# Finishes, as shares of the livery shader's smoothness, clear coat and metal.
F_RUST = (0.12, 0.0, 0.0)
F_PAINT = (0.55, 0.3, 0.0)
F_PRIMER = (0.25, 0.0, 0.0)
F_TAPE = (0.38, 0.0, 0.0)


# --- noise ---------------------------------------------------------------------------------------
def value_noise(a, b, cell, seed):
    """Smooth value noise in 0..1 at points (a, b), metres, one random value per cell corner."""
    rng = np.random.default_rng(seed)
    ga, gb = a / cell, b / cell
    a0, b0 = np.floor(ga).astype(int), np.floor(gb).astype(int)
    fa, fb = ga - a0, gb - b0
    oa, ob = a0.min(), b0.min()
    grid = rng.random((a0.max() - oa + 2, b0.max() - ob + 2))
    i, j = a0 - oa, b0 - ob
    sa, sb = fa * fa * (3 - 2 * fa), fb * fb * (3 - 2 * fb)
    top = grid[i, j] * (1 - sa) + grid[i + 1, j] * sa
    bot = grid[i, j + 1] * (1 - sa) + grid[i + 1, j + 1] * sa
    return top * (1 - sb) + bot * sb


def fbm(a, b, cell, seed, octaves=5, gain=0.5):
    total, amp, norm = 0.0, 1.0, 0.0
    for k in range(octaves):
        total = total + amp * value_noise(a, b, cell / 2 ** k, seed + 101 * k)
        norm += amp
        amp *= gain
    return total / norm


# --- one view's rasters --------------------------------------------------------------------------
def grid(view, meta):
    """Each pixel's car coordinates on the view: (a, b) for the noise, and y for height."""
    f, ppm = meta["views"][view], meta["ppm"]
    w, h = f["px"]
    u = f["u0"] + (np.arange(w) + 0.5) / ppm
    v = f["v0"] + (np.arange(h) + 0.5) / ppm
    U, V = np.meshgrid(u, v)
    if view in ("left", "right"):
        z = -U if view == "left" else U
        return z, -V, -V
    if view == "top":
        return U, -V, None
    return (U if view == "front" else -U), -V, -V


def body_mask(view):
    im = np.asarray(Image.open(VIEWS / f"{view}.png").convert("RGBA")).astype(int)
    return (im[..., 3] > 0) & (abs(im[..., 0] - 236) < 6) & (abs(im[..., 2] - 241) < 6)


def survival(view, a, b, y, seed):
    """How likely the paint is to have survived here: noise, raised up high and on the roof,
    lowered at the sills, the wheel arches and the panel edges where water sits."""
    n = fbm(a, b, 0.55, seed, 6, 0.55)
    if view in ("left", "right"):
        n = n + 0.35 * np.clip((y - 0.45) / 0.45, -1, 1)
        for wz, r in WHEELS:
            d = np.hypot(a - wz, y - r)
            n = n - 0.35 * np.clip(1 - (d - r) / 0.18, 0, 1)
    elif view == "top":
        n = n + 0.15 * np.clip(1 - np.abs(b) / 0.5, 0, 1) + 0.12 * np.clip(1 - np.abs(a + 0.35) / 0.9, 0, 1)
    else:
        n = n + 0.3 * np.clip((y - 0.6) / 0.4, -1, 1)
    return n


def rust_colour(a, b, y, seed):
    """Rust: dark brown through orange, scabbed and pitted, heavier towards the ground."""
    n1 = fbm(a, b, 0.25, seed + 1, 6, 0.6)
    n2 = fbm(a, b, 0.06, seed + 2, 4, 0.6)
    n3 = fbm(a * 3.0, b * 0.25, 0.08, seed + 3, 3)       # streaks running down
    pits = value_noise(a, b, 0.006, seed + 4)
    t = np.clip(n1 * 1.6 - 0.3 + (n2 - 0.5) * 0.6, 0, 1)
    col = RUST[0][None, None] * (1 - t)[..., None] + RUST[1][None, None] * t[..., None]
    hot = np.clip((n2 - 0.55) * 4, 0, 1) * np.clip((n1 - 0.4) * 3, 0, 1)
    col = col * (1 - hot)[..., None] + RUST[2][None, None] * hot[..., None]
    flake = np.clip((value_noise(a, b, 0.012, seed + 5) - 0.84) * 4, 0, 1) * 0.7
    col = col * (1 - flake)[..., None] + RUST[3][None, None] * flake[..., None]
    pit = np.clip((pits - 0.82) * 6, 0, 1)
    col = col * (1 - pit)[..., None] + RUST[4][None, None] * pit[..., None]
    if y is not None:
        dark = np.clip((0.45 - y) / 0.35, 0, 1) * 0.35 + (n3 - 0.5) * 0.25
        col = col * (1 - np.clip(dark, -0.2, 0.5))[..., None]
    return np.clip(col, 0, 1)


def paint_colour(view, a, b, seed):
    n = fbm(a, b, 0.3, seed + 7, 5)
    chalk = np.clip((n - 0.3) * 0.9 + (0.3 if view == "top" else 0.0), 0, 1)
    col = OLIVE[None, None] * (1 - chalk)[..., None] + OLIVE_SUN[None, None] * chalk[..., None]
    speck = np.clip((value_noise(a, b, 0.01, seed + 8) - 0.85) * 7, 0, 1)   # rust bubbling through
    return col * (1 - speck)[..., None] + RUST[2][None, None] * speck[..., None], speck


def paint_view(view, meta, seed):
    a, b, y = grid(view, meta)
    body = body_mask(view)
    s = survival(view, a, b, y, seed)
    share = {"top": 0.17, "left": PAINT_SHARE, "right": PAINT_SHARE, "front": 0.1, "rear": 0.1}[view]
    cut = np.quantile(s[body], 1 - share) if body.any() else 1.0
    # Paint where the survival clears the cut; a ragged rim of primer where it peels.
    rag = (fbm(a, b, 0.02, seed + 9, 3) - 0.5) * 0.05
    edge = s - cut + rag
    paint = edge > 0.012
    primer = (edge > 0) & ~paint
    col = rust_colour(a, b, y, seed)
    pc, speck = paint_colour(view, a, b, seed)
    col[paint] = pc[paint]
    col[primer] = PRIMER * (0.85 + 0.3 * fbm(a, b, 0.02, seed + 10, 2))[primer][..., None]
    fin = np.empty(col.shape)
    fin[:] = F_RUST
    rough = fbm(a, b, 0.05, seed + 11, 3)
    fin[..., 0] = F_RUST[0] * (0.6 + 0.8 * rough)
    fin[paint] = F_PAINT
    fin[paint & (speck > 0.5)] = F_RUST
    fin[primer] = F_PRIMER
    return col, fin, a, b


# --- the tape ------------------------------------------------------------------------------------
# Strips over the bonnet hole, top view: centre (z, x), length, turn in degrees.
# Laid side by side along the car, overlapping, then two across the ends to hold them down.
PATCH = (1.32, -0.16)
STRIPS = [(0.01, -0.085, 0.36, 2), (-0.01, -0.044, 0.40, -3), (0.02, -0.002, 0.38, 1), (0.0, 0.04, 0.41, 4),
          (-0.015, 0.081, 0.35, -2), (0.17, 0.0, 0.30, 93), (-0.16, -0.01, 0.31, 85)]
TAPE_W = 0.05


def tape(meta, col, fin, seed):
    f, ppm = meta["views"]["top"], meta["ppm"]
    w, h = f["px"]
    ss = 3
    rnd = random.Random(seed)
    mask = Image.new("L", (w * ss, h * ss), 0)
    crease = Image.new("L", (w * ss, h * ss), 0)
    md, cd = ImageDraw.Draw(mask), ImageDraw.Draw(crease)

    def px(z, x):  # top view: u = z, v = -x
        return ((z - f["u0"]) * ppm * ss, (-x - f["v0"]) * ppm * ss)

    for dz, dx, length, turn in STRIPS:
        cz, cx = PATCH[0] + dz, PATCH[1] + dx
        t = math.radians(turn)
        ax, ay = math.cos(t), math.sin(t)          # along the strip, in (z, x)
        nx, ny = -ay, ax
        half, hw = length / 2, TAPE_W / 2
        # Torn ends: a zigzag across each end.
        pts = []
        for end in (1, -1):
            steps = 7
            for k in range(steps + 1):
                s = -hw + 2 * hw * k / steps
                s = s if end == 1 else -s
                jag = rnd.uniform(-0.008, 0.008)
                pts.append((cz + ax * (end * half + jag) + nx * s, cx + ay * (end * half + jag) + ny * s))
        md.polygon([px(z, x) for z, x in pts], fill=255)
        # Creases across the tape, and a few wrinkles along it.
        for _ in range(rnd.randint(4, 7)):
            p = rnd.uniform(-half * 0.9, half * 0.9)
            tilt = rnd.uniform(-0.5, 0.5)
            z0, x0 = cz + ax * p + nx * -hw, cx + ay * p + ny * -hw
            z1, x1 = cz + ax * (p + tilt * TAPE_W) + nx * hw, cx + ay * (p + tilt * TAPE_W) + ny * hw
            cd.line([px(z0, x0), px(z1, x1)], fill=rnd.randint(90, 160), width=ss * 2)
        for _ in range(2):
            o = rnd.uniform(-hw * 0.6, hw * 0.6)
            p0, p1 = rnd.uniform(-half, 0), rnd.uniform(0, half)
            cd.line([px(cz + ax * p0 + nx * o, cx + ay * p0 + ny * o), px(cz + ax * p1 + nx * o, cx + ay * p1 + ny * o)], fill=70, width=ss)
    m = np.asarray(mask.resize((w, h), Image.LANCZOS)).astype(float) / 255
    c = np.asarray(crease.filter(ImageFilter.GaussianBlur(ss)).resize((w, h), Image.LANCZOS)).astype(float) / 255
    c = c * m
    a, b, _ = grid("top", meta)
    weave = (value_noise(a, b, 0.0025, seed + 20) - 0.5) * 0.04
    tc = np.clip(TAPE[None, None] + (c * 0.12 + weave)[..., None], 0, 1)
    col[:] = col * (1 - m)[..., None] + tc * m[..., None]
    tf = np.array(F_TAPE)[None, None] * (1 - 0.4 * c)[..., None]
    fin[:] = fin * (1 - m)[..., None] + tf * m[..., None]


# --- output --------------------------------------------------------------------------------------
def uri(rgb):
    im = Image.fromarray((np.clip(rgb, 0, 1) * 255 + 0.5).astype(np.uint8), "RGB")
    buf = io.BytesIO()
    im.save(buf, "PNG", optimize=True)
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode()


def build(name, team_name=False):
    import json
    meta = json.loads((VIEWS / "views.json").read_text())
    lv = Livery(VIEWS / "views.json", BASE, finish(*F_RUST))
    for k, view in enumerate(meta["views"]):
        col, fin, _, _ = paint_view(view, meta, 1000 + 37 * k)
        if view == "top":
            tape(meta, col, fin, 77)
        lv.image(view, uri(col))
        lv.image(view, uri(fin), finish=True)
    # The number, brushed on the front doors by hand: not level, and not where it is on the other side.
    lv.number("left", (1, 0.50, 0.22), 0.62, 0.30, INK, rotate=-5)
    lv.number("right", (-1, 0.47, 0.12), 0.62, 0.29, INK, rotate=3)
    if team_name:
        lv.side_text("Duct Tape Dynamics", -0.72, 0.56, 0.085, INK, font="CaveatBrush")
    lv.write(HERE / "out" / name)


if __name__ == "__main__":
    build("rust")
    build("rust-name", team_name=True)  # the name brushed on the rear doors
