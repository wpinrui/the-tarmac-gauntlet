"""Team Archer's marks as SVG files with every letter drawn as a path, so they bake the same
anywhere. Three team logos (Ivan's picks 1, 3 and 6 from the mockups), each in ink for papaya
paint. Writes them to out/marks/.

Run: python3 marks.py   (needs fonttools)
"""
import math
import re
from pathlib import Path

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

HERE = Path(__file__).resolve().parent
OUT = HERE / "out" / "marks"
BRAND = HERE.parents[2] / "src" / "renderer" / "assets" / "brands" / "archer"
SAIRA = HERE / "font" / "SairaCondensed-Bold.ttf"
BARLOW = HERE / "font" / "BarlowCondensed-Bold.ttf"
RACING = HERE / "font" / "RacingSansOne-Regular.ttf"

INK = "#141414"
WHITE = "#f7f5f0"
RED = "#d4202c"

_fonts = {}


def _font(path):
    f = _fonts.setdefault(path, TTFont(path))
    return f, f.getGlyphSet(), f.getBestCmap(), f["hmtx"], f["head"].unitsPerEm


def words(font, s, size, x, y, fill, spacing=0.0, anchor="middle"):
    """s as one path at font size `size`, baseline y. spacing: extra gap per letter in units of
    size. Returns (svg, width)."""
    _, gs, cmap, hmtx, upm = _font(font)
    k = size / upm
    names = [cmap[ord(c)] for c in s]
    adv = [hmtx[n][0] * k + spacing * size for n in names]
    width = sum(adv) - spacing * size
    pen = SVGPathPen(gs)
    cx = x - {"middle": width / 2, "start": 0, "end": width}[anchor]
    for n, a in zip(names, adv):
        gs[n].draw(TransformPen(pen, (k, 0, 0, -k, cx, y)))
        cx += a
    return f'<path d="{pen.getCommands()}" fill="{fill}"/>', width


def arc_words(font, s, size, cx, cy, r, fill, spacing=0.0, top=True):
    """s round a circle of radius r (its baseline), centred on the top or the bottom, upright."""
    _, gs, cmap, hmtx, upm = _font(font)
    k = size / upm
    names = [cmap[ord(c)] for c in s]
    adv = [hmtx[n][0] * k + spacing * size for n in names]
    total = sum(adv) - spacing * size
    pen = SVGPathPen(gs)
    pos = -total / 2
    for n, a in zip(names, adv):
        mid = pos + hmtx[n][0] * k / 2
        ang = mid / r                       # radians from the top (or bottom) centre
        if top:
            t = -math.pi / 2 + ang
            px, py, rot = cx + r * math.cos(t), cy + r * math.sin(t), ang
        else:
            t = math.pi / 2 - ang
            px, py, rot = cx + r * math.cos(t), cy + r * math.sin(t), -ang
        c, s_ = math.cos(rot), math.sin(rot)
        ox = -hmtx[n][0] * k / 2           # glyph's own centre on its baseline
        # glyph units -> rotate about (px, py); font y is up, so flip.
        m = (k * c, k * s_, k * s_, -k * c, px + ox * c, py + ox * s_)
        gs[n].draw(TransformPen(pen, m))
        pos += a
    return f'<path d="{pen.getCommands()}" fill="{fill}"/>'


def badge(x, y, size):
    """The Archer badge, size units square, top-left at x, y."""
    src = (BRAND / "archer-badge-colour.svg").read_text()
    inner = re.search(r"<svg[^>]*>(.*)</svg>", re.sub(r"<metadata>.*?</metadata>", "", src, flags=re.S), re.S).group(1)
    return f'<g transform="translate({x} {y}) scale({size / 200})">{inner}</g>'


def svg(w, h, body):
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}">{"".join(body)}</svg>'


def logo1():
    """The badge beside TEAM over ARCHER."""
    a, w1 = words(SAIRA, "TEAM", 40, 112, 50, INK, anchor="start")
    b, w2 = words(SAIRA, "ARCHER", 40, 112, 92, INK, anchor="start")
    return svg(112 + max(w1, w2) + 2, 104, [badge(0, 2, 100), a, b])


def logo3():
    """The roundel: TEAM ARCHER over the top, PERFORMANCE DIVISION under, the badge inside."""
    c, R = 100, 96
    body = [f'<circle cx="{c}" cy="{c}" r="{R}" fill="{WHITE}" stroke="{INK}" stroke-width="5"/>',
            f'<circle cx="{c}" cy="{c}" r="58" fill="none" stroke="{INK}" stroke-width="2.5"/>',
            arc_words(BARLOW, "TEAM ARCHER", 21, c, c, 70, INK, spacing=0.18, top=True),
            arc_words(BARLOW, "PERFORMANCE DIVISION", 15, c, c, 80, INK, spacing=0.12, top=False),
            badge(c - 40, c - 42, 80)]
    return svg(200, 200, body)


def logo6():
    """Team Archer in a racing script over the badge's red arrow."""
    a, w = words(RACING, "Team Archer", 50, 0, 50, INK, anchor="start")
    shaft_w = w * 0.8
    arrow = (f'<rect x="0" y="66" width="{shaft_w:.1f}" height="6" fill="{RED}"/>'
             f'<polygon points="{shaft_w - 2:.1f},58 {shaft_w + 22:.1f},69 {shaft_w - 2:.1f},80" fill="{RED}"/>')
    return svg(max(w, shaft_w + 22) + 2, 84, [a, arrow])


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for name, make in {"logo1": logo1, "logo3": logo3, "logo6": logo6}.items():
        (OUT / f"{name}.svg").write_text(make())
    print("wrote", OUT)
