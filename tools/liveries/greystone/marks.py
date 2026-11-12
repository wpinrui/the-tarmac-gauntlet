"""Greystone Archer's logo as an SVG file with every letter drawn as a path, so it bakes the same
anywhere. The Archer A rebuilt in grey stone blocks with the papaya arrow through it, and
GREYSTONE ARCHER curved under it (Ivan's pick 7 from the mockups), and the stone A alone for
the bonnet. Writes out/marks/logo7.svg and out/marks/badge.svg.

Run: python3 marks.py   (needs fonttools)
"""
import math
from pathlib import Path

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

HERE = Path(__file__).resolve().parent
OUT = HERE / "out" / "marks"
CINZEL = HERE / "font" / "Cinzel-ExtraBold.ttf"

PAPAYA = "#ff7f1e"
STONE = "#8d8f8c"
MORTAR = "#f7f5f0"
INK = "#141414"

_fonts = {}


def _font(path):
    f = _fonts.setdefault(path, TTFont(path))
    return f.getGlyphSet(), f.getBestCmap(), f["hmtx"], f["head"].unitsPerEm


def arc_words(font, s, size, cx, cy, r, fill, spacing=0.0):
    """s under a circle of radius r (its baseline), centred on the bottom, upright and reading
    left to right, so the line smiles."""
    gs, cmap, hmtx, upm = _font(font)
    k = size / upm
    names = [cmap[ord(c)] for c in s]
    adv = [hmtx[n][0] * k + spacing * size for n in names]
    total = sum(adv) - spacing * size
    pen = SVGPathPen(gs)
    pos = -total / 2
    for n, a in zip(names, adv):
        ang = (pos + hmtx[n][0] * k / 2) / r      # radians from the bottom centre, + to the right
        t = math.pi / 2 - ang
        px, py, rot = cx + r * math.cos(t), cy + r * math.sin(t), -ang
        c, s_ = math.cos(rot), math.sin(rot)
        ox = -hmtx[n][0] * k / 2
        gs[n].draw(TransformPen(pen, (k * c, k * s_, k * s_, -k * c, px + ox * c, py + ox * s_)))
        pos += a
    return f'<path d="{pen.getCommands()}" fill="{fill}"/>'


A = "M100 18 L176 180 L136 180 L100 100 L64 180 L24 180 Z"   # the Archer badge's A


def stone_a():
    """The stone A with the papaya arrow through it, viewBox 0 0 200 200 like the Archer badge."""
    courses = "M0 52 H200 M0 84 H200 M0 116 H200 M0 148 H200"
    joints = "M96 18 V52 M90 52 V84 M110 52 V84 M78 84 V116 M122 84 V116 M64 116 V148 M136 116 V148 M48 148 V180 M150 148 V180"
    return [
        f'<defs><clipPath id="ga-a"><path d="{A}"/></clipPath></defs>',
        f'<path d="{A}" fill="{STONE}"/>',
        f'<path d="{courses} {joints}" stroke="{MORTAR}" stroke-width="4" fill="none" clip-path="url(#ga-a)"/>',
        f'<rect x="22" y="136" width="140" height="13" fill="{PAPAYA}"/>',
        f'<polygon points="158,126 186,142.5 158,159" fill="{PAPAYA}"/>',
        f'<polygon points="10,126 28,126 34,136 16,136" fill="{PAPAYA}"/>',
        f'<polygon points="10,159 28,159 34,149 16,149" fill="{PAPAYA}"/>',
    ]


def logo7(ink=INK):
    """The stone A over GREYSTONE ARCHER. ink: the lettering's colour."""
    body = stone_a() + [arc_words(CINZEL, "GREYSTONE ARCHER", 25, 100, 0, 228, ink, spacing=0.06)]
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="-42 12 284 222">{"".join(body)}</svg>'


def badge():
    """The stone A alone, for the bonnet in place of the Archer badge."""
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">{"".join(stone_a())}</svg>'


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "logo7.svg").write_text(logo7())
    (OUT / "badge.svg").write_text(badge())
    print("wrote", OUT)
