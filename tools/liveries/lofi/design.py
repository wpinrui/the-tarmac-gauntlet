"""Lo-fi Racing on the Tora Giga: Side A. The car is a cassette tape from the closed Taichung tape
factory the team works out of. A gloss black shell; a cream paper label across the doors with a red
header (SIDE A on the left, SIDE B on the right), the team name hand-written in blue marker and a tape
window with its reels; the J-card on the bonnet; the spine across the tail. Cluttered on purpose:
screws, moulded shell lettering, grip ridges, write-protect tabs, shop stickers, a barcode and a
tracklist. Chinese all over, as the factory printed it: A面, seal stamps, the shop's 特價 sticker,
masking-tape notes in brush pen and 低傳真賽車 large on the spine. The race number is written on
masking tape on the label in the same marker. Independent: no maker's logo.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/ToraGiga/ToraGiga.obj out/views
     node ../common/digits.js --font font/PermanentMarker-Regular.ttf --out out/digits
     python3 design.py

Giga landmarks in OBJ metres: z along the car (nose +2.33, tail -2.40), y up, x to the car's left
(half width 1.02). Wheels at z +-1.39, centre y 0.36, arch radius 0.39. Door from z 0.65 to -0.64,
y 0.22 up to the belt line near 0.93. Bonnet from z 0.95 to 2.15 for |x| under 0.75; roof from
z 0.10 to -0.90 for |x| under 0.50. The tail's flat panel spans y 0.41 to 0.74.
"""
import math
import random
import sys
from pathlib import Path

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

HERE = Path(__file__).resolve().parent
FONT = HERE / "font"
VIEWS = HERE / "out" / "views" / "views.json"
sys.path.insert(0, str(HERE.parent / "common"))
from livery import Livery, finish  # noqa: E402

SHELL = "#111013"      # gloss black cassette shell
MOULD = "#46424c"      # lettering and ridges moulded into the shell
LABEL = "#efe6cf"      # paper label
RED = "#e8553a"        # label header
MARKER = "#2a3a8c"     # blue marker
RULE = "#c9bfa7"       # label writing lines
TAPE = "#4a2e22"       # magnetic tape
GLASS = "#2e2b33"      # tape window
STEEL = "#9a97a0"      # screw heads

F_SHELL = finish(0.9, 1.0, 0.15)
F_PAPER = finish(0.35, 0.15, 0.0)

MARKER_F = FONT / "PermanentMarker-Regular.ttf"
BLACK_F = FONT / "ArchivoBlack-Regular.ttf"
MONO_F = FONT / "SpaceMono-Bold.ttf"
COND_F = FONT / "BarlowCondensed-Bold.ttf"
CJK_F = FONT / "NotoSansTC-subset.ttf"     # print, cut to the characters the livery uses
KAI_F = FONT / "LXGWWenKaiTC-subset.ttf"   # hand-written, cut the same

# The door label: z from front to back, y from bottom to top, the red header at its top.
LAB = (0.95, -0.95, 0.36, 0.88)
HEAD = 0.08
NUM = (-0.36, 0.70, 0.30, 0.17)            # side number box: z, y, w, h
WIN = (0.02, 0.50, 0.92, 0.18)             # tape window: z, y, w, h

_fonts = {}


def _font(path):
    if path not in _fonts:
        f = TTFont(path)
        gs, cmap = f.getGlyphSet(), f.getBestCmap()
        cap = getattr(f["OS/2"], "sCapHeight", 0) or f["head"].unitsPerEm * 0.72
        if path in (CJK_F, KAI_F):
            cap = f["head"].unitsPerEm * 0.88
        _fonts[path] = (gs, cmap, f["hmtx"], cap)
    return _fonts[path]


def words(font, s, u, v, height, fill, rotate=0.0, spacing=0.0, anchor="middle", width=None):
    """s as one SVG path, capitals height tall, centred on (u, v) in panel units, upright unless
    rotated (degrees, clockwise). width squeezes it to fit."""
    gs, cmap, hmtx, cap = _font(font)
    k = height / cap
    names = [cmap.get(ord(c), cmap.get(ord(" "))) for c in s]
    total = sum(hmtx[n][0] * k for n in names) + spacing * height * (len(names) - 1)
    sx = min(1.0, width / total) if width else 1.0
    x0 = {"middle": -total / 2, "start": 0.0, "end": -total}[anchor]
    pen = SVGPathPen(gs)
    x = x0
    for n in names:
        gs[n].draw(TransformPen(pen, (k, 0, 0, -k, x, height / 2)))
        x += hmtx[n][0] * k + spacing * height
    return (f'<path transform="translate({u:.4f} {v:.4f}) rotate({rotate}) scale({sx:.4f} 1)" '
            f'd="{pen.getCommands()}" fill="{fill}"/>')


# --- cassette parts, all in panel units around (u, v) --------------------------------------
def rrect(u, v, w, h, r, fill, extra=""):
    return f'<rect x="{u - w / 2:.4f}" y="{v - h / 2:.4f}" width="{w:.4f}" height="{h:.4f}" rx="{r:.4f}" fill="{fill}" {extra}/>'


def screw(u, v, r=0.022):
    return (f'<circle cx="{u:.4f}" cy="{v:.4f}" r="{r * 1.35:.4f}" fill="#111013"/>'
            f'<circle cx="{u:.4f}" cy="{v:.4f}" r="{r:.4f}" fill="{STEEL}"/>'
            f'<path d="M{u - r * 0.65:.4f},{v:.4f}H{u + r * 0.65:.4f}M{u:.4f},{v - r * 0.65:.4f}V{v + r * 0.65:.4f}" '
            f'stroke="#3a373e" stroke-width="{r * 0.28:.4f}"/>')


def reel(u, v, r, pack):
    out = [f'<circle cx="{u:.4f}" cy="{v:.4f}" r="{pack:.4f}" fill="{TAPE}"/>']
    for i in range(1, 6):
        rr = r * 0.5 + (pack - r * 0.5) * i / 6
        out.append(f'<circle cx="{u:.4f}" cy="{v:.4f}" r="{rr:.4f}" fill="none" stroke="#6b4636" stroke-width="{r * 0.03:.4f}"/>')
    out.append(f'<circle cx="{u:.4f}" cy="{v:.4f}" r="{r * 0.5:.4f}" fill="#f3efe6"/>')
    out.append(f'<circle cx="{u:.4f}" cy="{v:.4f}" r="{r * 0.3:.4f}" fill="{SHELL}"/>')
    for i in range(6):
        a = i * 60
        out.append(f'<rect x="{r * 0.17:.4f}" y="{-r * 0.05:.4f}" width="{r * 0.15:.4f}" height="{r * 0.1:.4f}" fill="#f3efe6" '
                   f'transform="translate({u:.4f} {v:.4f}) rotate({a})"/>')
    return "".join(out)


def tape_window(u, v, w, h, full=0.7, empty=0.25, rotate=0):
    """The window between the reels, the tape run between them, one reel fuller than the other."""
    r = h * 0.42
    dx = w / 2 - h * 0.5
    out = [f'<g transform="rotate({rotate} {u:.4f} {v:.4f})">',
           rrect(u, v, w, h, h * 0.5, SHELL),
           rrect(u, v, w - h * 0.16, h * 0.84, h * 0.42, GLASS),
           f'<path d="M{u - dx:.4f},{v + r * (0.5 + full * 0.5):.4f}L{u + dx:.4f},{v + r * (0.5 + empty * 0.5):.4f}" stroke="{TAPE}" stroke-width="{h * 0.04:.4f}"/>',
           reel(u - dx, v, r, r * (0.5 + full * 0.5)), reel(u + dx, v, r, r * (0.5 + empty * 0.5)),
           rrect(u, v, w * 0.2, h * 0.6, h * 0.05, "#ffffff", 'opacity="0.07"'),
           "</g>"]
    return "".join(out)


def lines(u0, u1, v0, n, gap):
    return "".join(f'<path d="M{u0:.4f},{v0 + i * gap:.4f}H{u1:.4f}" stroke="{RULE}" stroke-width="0.004"/>' for i in range(n))


def ridges(u0, u1, v0, v1, step=0.022):
    return "".join(f'<rect x="{u:.4f}" y="{v0:.4f}" width="{step * 0.45:.4f}" height="{v1 - v0:.4f}" rx="{step * 0.2:.4f}" fill="{MOULD}"/>'
                   for u in frange(u0, u1, step))


def frange(a, b, step):
    n = int(abs(b - a) / step)
    s = step if b > a else -step
    return [a + i * s for i in range(n + 1)]


def arrow(u, v, size, fill, point=1):
    """A play triangle, pointing along +u (point=1) or -u (point=-1)."""
    return (f'<path d="M{u - point * size / 2:.4f},{v - size / 2:.4f}L{u + point * size / 2:.4f},{v:.4f}'
            f'L{u - point * size / 2:.4f},{v + size / 2:.4f}Z" fill="{fill}"/>')


def barcode(u, v, w, h, seed):
    rng = random.Random(seed)
    out = [rrect(u, v, w + 0.02, h + 0.03, 0.004, "#fbf8f0")]
    x = u - w / 2
    while x < u + w / 2 - 0.004:
        bw = rng.choice((0.0015, 0.003, 0.0045))
        out.append(f'<rect x="{x:.4f}" y="{v - h / 2:.4f}" width="{bw:.4f}" height="{h * 0.82:.4f}" fill="#111"/>')
        x += bw + rng.choice((0.0015, 0.003))
    out.append(words(MONO_F, "4 710328 650917", u, v + h * 0.45, h * 0.12, "#111"))
    return "".join(out)


def masking(u, v, w, h, rotate, text=None, size=None, font=MARKER_F):
    """A torn strip of masking tape, written on in marker."""
    rng = random.Random(round(u * 1000 + v * 7))
    n = 7
    top = [(u - w / 2 + w * i / n, v - h / 2 + rng.uniform(-0.004, 0.004)) for i in range(n + 1)]
    bot = [(u + w / 2 - w * i / n, v + h / 2 + rng.uniform(-0.004, 0.004)) for i in range(n + 1)]
    left = (u - w / 2 - 0.006, v + rng.uniform(-0.01, 0.01))
    right = (u + w / 2 + 0.006, v + rng.uniform(-0.01, 0.01))
    pts = top + [right] + bot + [left]
    d = "M" + " L".join(f"{x:.4f},{y:.4f}" for x, y in pts) + "Z"
    out = f'<g transform="rotate({rotate} {u:.4f} {v:.4f})"><path d="{d}" fill="#e6d7a8" opacity="0.95"/>'
    if text:
        out += words(font, text, u, v, size or h * 0.45, MARKER, width=w * 0.9)
    return out + "</g>"


def ribbon(pts, wid):
    """Tape pulled out of the shell: one brown strip through the points, smooth."""
    d = f"M{pts[0][0]:.4f},{pts[0][1]:.4f}"
    for i in range(1, len(pts) - 2, 3):
        d += "C" + " ".join(f"{x:.4f},{y:.4f}" for x, y in pts[i:i + 3])
    return (f'<path d="{d}" fill="none" stroke="{TAPE}" stroke-width="{wid:.4f}" stroke-linecap="round"/>'
            f'<path d="{d}" fill="none" stroke="#8a5a40" stroke-width="{wid * 0.25:.4f}" stroke-linecap="round" opacity="0.6"/>')


def price(u, v, rotate):
    """A shop's price sticker: special offer."""
    return (f'<g transform="rotate({rotate} {u:.4f} {v:.4f})">' + rrect(u, v, 0.15, 0.10, 0.008, "#fdfaf2", f'stroke="{RED}" stroke-width="0.006"')
            + words(CJK_F, "特價", u, v - 0.022, 0.03, RED) + words(BLACK_F, "NT$ 99", u, v + 0.022, 0.026, RED) + "</g>")


def seal(u, v, h, rotate=0, text="低傳真"):
    """A red seal stamp, the characters in white in a row, a little worn."""
    w = h * (len(text) * 0.9 + 0.3)
    rng = random.Random(len(text) + round(u * 100))
    specks = "".join(f'<circle cx="{u + rng.uniform(-w / 2, w / 2):.4f}" cy="{v + rng.uniform(-h / 2, h / 2):.4f}" r="{h * rng.uniform(0.02, 0.05):.4f}" fill="#f3ead6" opacity="0.7"/>' for _ in range(10))
    return (f'<g transform="rotate({rotate} {u:.4f} {v:.4f})">' + rrect(u, v, w, h, h * 0.08, "#c8202f")
            + rrect(u, v, w - h * 0.12, h * 0.88, h * 0.05, "none", 'stroke="#f3ead6" stroke-width="' + f'{h * 0.03:.4f}"')
            + words(CJK_F, text, u, v, h * 0.62, "#f3ead6", spacing=0.1) + specks + "</g>")


def rec(u, v, r=0.045):
    return (f'<circle cx="{u:.4f}" cy="{v:.4f}" r="{r:.4f}" fill="{RED}"/>'
            + words(BLACK_F, "REC", u, v, r * 0.55, LABEL))


def logo(u, v, h, rotate=0):
    """Logo 1: the cassette, LO-FI RACING on its label. Its own box is 300 by 190; h is its height."""
    s = h / 190
    g = [rrect(0, 0, 300, 190, 16, SHELL), rrect(0, -30, 250, 110, 6, LABEL),
         f'<rect x="-125" y="-85" width="250" height="26" fill="{RED}"/>',
         words(BLACK_F, "LO-FI RACING", 0, -72, 12, LABEL),
         rrect(0, -8, 150, 48, 24, SHELL), reel(-50, -8, 17, 17), reel(50, -8, 17, 17),
         f'<path d="M-90 95 L-70 60 L70 60 L90 95Z" fill="#2c2930"/>',
         "".join(f'<rect x="{x - 6}" y="70" width="12" height="14" fill="#050506"/>' for x in (-60, -30, 0, 30, 60)),
         rrect(0, 0, 300, 190, 16, "none", f'stroke="{MOULD}" stroke-width="4"')]
    return f'<g transform="translate({u:.4f} {v:.4f}) rotate({rotate}) scale({s:.6f})">{"".join(g)}</g>'


TRACKS = ["01  slow in, fast out", "02  rain on the pit wall", "03  3am setup change", "04  tyre hiss", "05  apex (demo)"]


# --- the five views ---------------------------------------------------------------------------
def sides(lv):
    z0, z1, y0, y1 = LAB
    for view in ("left", "right"):
        s = 1.0 if view == "left" else -1.0

        def P(z, y):
            return lv.uv(view, (s, y, z))

        fwd = -1 if view == "left" else 1          # +u towards the nose on this view
        a = [P(z0, y1), P(z1, y0)]
        lu, lv0 = min(a[0][0], a[1][0]), a[0][1]
        lw, lh = abs(a[1][0] - a[0][0]), a[1][1] - a[0][1]
        cu = lu + lw / 2
        out = []
        # shell: grip ridges fore and aft of the arches, moulded lettering along the sill
        for zz0, zz1, ya, yb in ((2.12, 1.86, 0.56, 0.36), (-1.86, -2.18, 0.68, 0.42)):
            (ua, va), (ub, vb) = P(zz0, ya), P(zz1, yb)
            out.append(ridges(min(ua, ub), max(ua, ub), va, vb))
        for z, y in ((2.05, 0.30), (-1.90, 0.32), (-1.40, 0.96), (0.95, 0.27), (-0.95, 0.27)):
            out.append(screw(*P(z, y)))
        u, v = P(0.0, 0.29)
        out.append(words(MONO_F, "MADE IN TAIWAN", u, v, 0.024, MOULD, spacing=0.25))
        u, v = P(-0.45, 0.29)
        out.append(words(CJK_F, "台中製造", u, v, 0.036, MOULD))
        for z in (0.62, 0.48):
            out.append(arrow(*P(z, 0.29), 0.03, MOULD, -fwd))
        u, v = P(0.45, 0.29)
        out.append(words(MONO_F, "PLAY", u + 0.07 * fwd * -1, v, 0.022, MOULD))
        # write-protect tabs on the rear quarter, and its grade lettering
        for z in (-1.70, -1.84):
            out.append(rrect(*P(z, 0.86), 0.07, 0.05, 0.006, "#050506"))
        u, v = P(-1.30, 0.93)
        out.append(words(MONO_F, "NORMAL BIAS  120µs EQ", u, v, 0.018, MOULD, spacing=0.1))
        u, v = P(1.25, 0.86)
        out.append(words(COND_F, "HIGH OUTPUT  LOW NOISE", u, v, 0.03, MOULD, spacing=0.12))
        out.append(masking(*P(1.62, 0.82), 0.18, 0.065, 6 * fwd, "重播", 0.038, KAI_F))
        out.append(words(CJK_F, "台灣製造  卡式錄音帶", *P(1.20, 0.80), 0.026, MOULD, spacing=0.2))
        out.append(masking(*P(-1.22, 0.82), 0.36, 0.06, -4 * fwd, "DO NOT ERASE!!"))
        out.append(masking(*P(-1.10, 0.885), 0.22, 0.055, 3 * fwd, "請勿消除", 0.032, KAI_F))
        out.append(rec(*P(-2.10, 0.86), 0.035))
        # the label
        out.append(rrect(cu, lv0 + lh / 2, lw, lh, 0.03, LABEL))
        out.append(f'<path d="M{lu:.4f},{lv0 + HEAD:.4f}V{lv0 + 0.03:.4f}Q{lu:.4f},{lv0:.4f} {lu + 0.03:.4f},{lv0:.4f}H{lu + lw - 0.03:.4f}'
                   f'Q{lu + lw:.4f},{lv0:.4f} {lu + lw:.4f},{lv0 + 0.03:.4f}V{lv0 + HEAD:.4f}Z" fill="{RED}"/>')
        hv = lv0 + HEAD / 2
        u, _ = P(0.72, 0)
        out.append(words(BLACK_F, "SIDE A" if view == "left" else "SIDE B", u, hv, 0.042, LABEL))
        u, _ = P(0.47, 0)
        out.append(words(BLACK_F, "A" if view == "left" else "B", u - 0.022, hv, 0.042, LABEL))
        out.append(words(CJK_F, "面", u + 0.022, hv, 0.042, LABEL))
        u, _ = P(0.02, 0)
        out.append(words(MONO_F, "TYPE I  NORMAL POSITION  60 MIN", u, hv, 0.02, LABEL, spacing=0.12))
        u, _ = P(-0.68, 0)
        out.append(words(BLACK_F, "LO-FI RACING", u, hv, 0.036, LABEL))
        out.append(lines(lu + 0.04, lu + lw - 0.04, lv0 + HEAD + 0.07, 4, 0.07))
        # hand-written: the name, the side letter and the tracklist
        u, v = P(0.30, 0.755)
        out.append(words(MARKER_F, "lo-fi racing", u, v, 0.06, MARKER, rotate=-2))
        u, v = P(0.82, 0.60)
        out.append(words(MARKER_F, "A" if view == "left" else "B", u, v, 0.13, RED, rotate=-6))
        u, v = P(0.82, 0.60)
        out.append(f'<circle cx="{u:.4f}" cy="{v:.4f}" r="0.095" fill="none" stroke="{RED}" stroke-width="0.008"/>')
        out.append(seal(*P(0.80, 0.752), 0.05, -3 * fwd))
        out.append(words(KAI_F, "第一輯  好聽", *P(0.30, 0.685), 0.034, MARKER, rotate=-2))
        for i, t in enumerate(TRACKS):
            u, v = P(-0.81, 0.76 - i * 0.05)
            out.append(words(MARKER_F, t, u, v, 0.016, MARKER, width=0.24))
        out.append(barcode(*P(-0.78, 0.50), 0.18, 0.05, 3))
        # the window, the logo on the front of the label
        out.append(tape_window(*P(WIN[0], WIN[1]), WIN[2], WIN[3]))
        # tape chewed out of the shell, trailing back along the sill
        out.append(ribbon([P(*q) for q in ((-0.30, 0.42), (-0.36, 0.30), (-0.20, 0.25), (-0.05, 0.31), (0.10, 0.37), (-0.10, 0.40),
                                           (-0.30, 0.33), (-0.50, 0.24), (-0.70, 0.27), (-0.85, 0.33), (-1.0, 0.36), (-0.95, 0.24),
                                           (-0.80, 0.24))], 0.016))
        out.append(logo(*P(0.80, 0.42), 0.09))
        # stickers over the edges, and the label's own screws
        out.append(rec(*P(0.96, 0.86), 0.04))
        out.append(price(*P(-0.86, 0.40), -8 if view == "left" else 8))
        for z, y in ((0.92, 0.39), (-0.92, 0.84)):
            out.append(screw(*P(z, y), 0.018))
        lv.raw(view, "".join(out))
        # the label is paper: matte
        lv.raw(view, rrect(cu, lv0 + lh / 2, lw, lh, 0.03, F_PAPER), finish=True)
        nu, nv = P(NUM[0], NUM[1])
        lv.raw(view, masking(nu, nv, NUM[2] + 0.07, NUM[3] + 0.03, -3 if view == "left" else 3))
        lv.number(view, (s, NUM[1], NUM[0]), NUM[2], NUM[3], MARKER, rotate=-3 if view == "left" else 3)


def top(lv):
    out, fin = [], []

    def T(z, x):
        return lv.uv("top", (x, 0, z))

    # the J-card on the bonnet, reading from the nose: header towards the windscreen
    zc, zw, xw = 1.52, 0.95, 1.06       # centre, length along the car, width across
    u, v = T(zc, 0)
    out.append(rrect(u, v, zw, xw, 0.03, LABEL))
    fin.append(rrect(u, v, zw, xw, 0.03, F_PAPER))
    hu, _ = T(zc - zw / 2 + 0.05, 0)
    out.append(rrect(hu, v, 0.10, xw, 0.0, RED))
    out.append(words(BLACK_F, "SIDE A", hu, v - 0.30, 0.05, LABEL, rotate=-90))
    out.append(words(BLACK_F, "LO-FI RACING", hu, v + 0.25, 0.045, LABEL, rotate=-90))
    for i in range(5):
        lu, _ = T(zc - zw / 2 + 0.19 + i * 0.08, 0)
        out.append(f'<path d="M{lu:.4f},{v - xw / 2 + 0.05:.4f}V{v + xw / 2 - 0.05:.4f}" stroke="{RULE}" stroke-width="0.004"/>')
    mu, _ = T(zc - 0.18, 0)
    out.append(words(MARKER_F, "lo-fi racing", mu, v, 0.10, MARKER, rotate=-92))
    wu, _ = T(zc + 0.22, 0)
    out.append(tape_window(wu, v, 0.70, 0.22, rotate=-90))
    for dz in (-zw / 2 + 0.03, zw / 2 - 0.03):
        for dx in (-xw / 2 + 0.03, xw / 2 - 0.03):
            out.append(screw(*T(zc + dz, dx), 0.018))
    out.append(rec(*T(zc + zw / 2 - 0.02, -0.40), 0.05))
    out.append(seal(*T(zc - 0.02, 0.36), 0.075, -90))
    out.append(words(KAI_F, "低傳真賽車  第一輯", *T(zc - 0.06, -0.12), 0.05, MARKER, rotate=-92))
    # tracklist down the bonnet's edge
    for i, t in enumerate(TRACKS):
        tu, tv = T(1.25 + i * 0.065, 0.66)
        out.append(words(MARKER_F, t, tu, tv, 0.024, LABEL, rotate=-90, width=0.4, anchor="middle"))
    # grip ridges on the wings, beside the bonnet
    for x in (0.86, -0.86):
        (ua, va), (ub, vb) = T(1.15, x - 0.05), T(1.68, x + 0.05)
        out.append(f'<g transform="rotate(90 {(ua + ub) / 2:.4f} {(va + vb) / 2:.4f})">'
                   + ridges((ua + ub) / 2 - 0.05, (ua + ub) / 2 + 0.05, (va + vb) / 2 - 0.26, (va + vb) / 2 + 0.26) + "</g>")
    # the roof: a paper patch with the number, upright from the nose
    ru, rv = T(-0.38, 0)
    out.append(rrect(ru, rv, 0.62, 0.74, 0.03, LABEL))
    fin.append(rrect(ru, rv, 0.62, 0.74, 0.03, F_PAPER))
    out.append(rrect(ru + 0.24, rv, 0.08, 0.74, 0.0, RED))
    out.append(words(BLACK_F, "SIDE A", ru + 0.24, rv, 0.04, LABEL, rotate=-90))
    for dz in (-0.27, 0.27):
        for dx in (-0.33, 0.33):
            out.append(screw(ru + dz, rv + dx, 0.016))
    out.append(price(ru - 0.25, rv + 0.28, -80))
    out.append(masking(*T(1.06, 0.46), 0.26, 0.06, -86, "SIDE A, PLAY LOUD"))
    out.append(masking(*T(-2.07, 0), 0.40, 0.065, -92, "be kind, rewind"))
    out.append(masking(*T(-0.38, -0.30), 0.24, 0.055, -88, "demo"))
    # the boot lid: moulded lettering
    bu, bv = T(-1.80, 0)
    out.append(words(CJK_F, "低傳真錄音帶", bu, bv, 0.05, MOULD, rotate=-90))
    lv.raw("top", "".join(out))
    lv.raw("top", "".join(fin), finish=True)
    lv.number("top", (0, 0, -0.42), 0.46, 0.28, MARKER, rotate=-90)


def front(lv):
    def F(x, y):
        return lv.uv("front", (x, y, 2.4))

    out = []
    u, v = F(0, 0.71)
    out.append(words(BLACK_F, "LO-FI RACING", u, v, 0.04, LABEL, spacing=0.08))
    for s in (1, -1):
        out.append(screw(*F(s * 0.80, 0.52), 0.02))
        out.append(screw(*F(s * 0.80, 0.24), 0.02))
        (ua, va), (ub, vb) = F(s * 0.86, 0.33), F(s * 0.66, 0.24)
        out.append(ridges(min(ua, ub), max(ua, ub), va, vb, 0.02))
    out.append(words(CJK_F, "請勿觸摸磁帶", *F(0, 0.66), 0.022, MOULD, spacing=0.3))
    out.append(seal(*F(-0.62, 0.40), 0.05))
    out.append(masking(*F(0.74, 0.29), 0.20, 0.06, -5, "play loud"))
    lv.raw("front", "".join(out))


def rear(lv):
    def R(x, y):
        return lv.uv("rear", (x, y, -2.5))

    out, fin = [], []
    # the spine: a paper strip across the tail panel, red end on the car's left
    u, v = R(0, 0.55)
    w, h = 1.66, 0.19
    out.append(rrect(u, v, w, h, 0.02, LABEL))
    fin.append(rrect(u, v, w, h, 0.02, F_PAPER))
    eu, _ = R(0.74, 0)
    out.append(rrect(eu, v, 0.18, h, 0.0, RED))
    out.append(words(BLACK_F, "A", eu, v, 0.11, LABEL))
    mu, _ = R(0.34, 0)
    out.append(words(MARKER_F, "lo-fi racing", mu, v - 0.035, 0.05, MARKER, width=0.5))
    out.append(words(KAI_F, "第一輯  倒帶重播", mu, v + 0.045, 0.04, MARKER, width=0.5))
    cu, _ = R(-0.36, 0)
    out.append(words(CJK_F, "低傳真賽車", cu, v, 0.12, RED, spacing=0.05, width=0.82))
    out.append(barcode(*R(-0.65, 0.68), 0.16, 0.05, 9))
    out.append(words(MONO_F, "MADE IN TAICHUNG, TAIWAN", *R(0, 0.43), 0.02, MOULD, spacing=0.2))
    for s in (1, -1):
        out.append(screw(*R(s * 0.88, 0.70), 0.02))
        out.append(screw(*R(s * 0.88, 0.42), 0.02))
    out.append(rec(*R(0.55, 0.69), 0.035))
    out.append(logo(*R(0.30, 0.70), 0.06))
    out.append(masking(*R(-0.30, 0.70), 0.22, 0.05, 4, "倒帶", 0.032, KAI_F))
    out.append(seal(*R(0.02, 0.69), 0.05))
    out.append(words(CJK_F, "台中製造", *R(0.62, 0.43), 0.024, MOULD, spacing=0.2))
    lv.raw("rear", "".join(out))
    lv.raw("rear", "".join(fin), finish=True)


def side_a(views):
    lv = Livery(views, SHELL, F_SHELL)
    sides(lv)
    top(lv)
    front(lv)
    rear(lv)
    return lv


if __name__ == "__main__":
    side_a(VIEWS).write(HERE / "out" / "side-a")
