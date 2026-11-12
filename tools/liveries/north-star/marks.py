"""North Star Racing's marks as SVG files with every letter drawn as a path, so they bake the
same anywhere: the shield logo (aurora, compass star, NORTH STAR inside, RACING under it), the
compass star alone and the tagline. Writes them to out/marks/.

Run: python3 marks.py   (needs fonttools)
"""
import math
from pathlib import Path

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

HERE = Path(__file__).resolve().parent
OUT = HERE / "out" / "marks"
JOSEFIN = HERE / "font" / "JosefinSans-Bold.ttf"
BARLOW = HERE / "font" / "BarlowCondensed-Bold.ttf"

NIGHT = "#0b1b33"
SNOW = "#f2f6f8"
ICE = "#a9d6e5"
GOLD = "#f2c14e"
PINE = "#1f4a3a"
AUR_G = "#39e6a0"
AUR_V = "#8a5cf6"

TAGLINE = "STEADY AS THE POLE STAR"

_fonts = {}


def words(font, s, size, x, y, fill, spacing=0.0, anchor="middle", width=None):
    """s as one path, baseline at y. spacing: extra gap per letter in units of size. width
    squeezes the line to that width when it would run wider. Returns (svg, width)."""
    f = _fonts.setdefault(font, TTFont(font))
    gs, cmap, hmtx = f.getGlyphSet(), f.getBestCmap(), f["hmtx"]
    k = size / f["head"].unitsPerEm
    names = [cmap[ord(c)] for c in s]
    adv = [hmtx[n][0] * k + spacing * size for n in names]
    full = sum(adv) - spacing * size
    sx = min(1.0, width / full) if width else 1.0
    pen = SVGPathPen(gs)
    cx = x - {"middle": full * sx / 2, "start": 0, "end": full * sx}[anchor]
    for n, a in zip(names, adv):
        gs[n].draw(TransformPen(pen, (k * sx, 0, 0, -k, cx, y)))
        cx += a * sx
    return f'<path d="{pen.getCommands()}" fill="{fill}"/>', full * sx


def star4(cx, cy, r, fill, k=0.18, turn=0.0):
    """A four point star, r to each tip, the waist k*r. turn in degrees."""
    pts = []
    for i in range(8):
        a = math.radians(turn) - math.pi / 2 + i * math.pi / 4
        rad = r * k if i % 2 else r
        pts.append(f"{cx + math.cos(a) * rad:.2f},{cy + math.sin(a) * rad:.2f}")
    return f'<path d="M{" L".join(pts)} Z" fill="{fill}"/>'


def compass(cx, cy, r, big, mid, core):
    """Logo 4's star: a four point star, a smaller one turned 45 degrees behind it, a gold core."""
    return star4(cx, cy, r * 0.55, mid, 0.25, 45) + star4(cx, cy, r, big) + star4(cx, cy, r * 0.23, core, 0.3)


SHIELD = "M100,6 L186,28 L186,118 Q186,190 100,234 Q14,190 14,118 L14,28 Z"
SHIELD_IN = "M100,12.2 L180,32.7 L180,118 Q180,186 100,227.2 Q20,186 20,118 L20,32.7 Z"


def logo(racing_fill):
    """Logo 6 as Ivan asked: the shield with a thin inner outline like logo 2's ring, the
    aurora behind logo 4's compass star, NORTH STAR inside the shield and RACING under it."""
    name, _ = words(JOSEFIN, "NORTH STAR", 22, 100, 170, SNOW, spacing=0.1, width=112)
    racing, _ = words(BARLOW, "RACING", 26, 100, 272, racing_fill, spacing=0.45)
    body = [f'<clipPath id="ns-shield"><path d="{SHIELD}"/></clipPath>',
            f'<path d="{SHIELD}" fill="{NIGHT}"/>',
            '<g clip-path="url(#ns-shield)">',
            f'<path d="M0,112 C60,64 120,112 200,76 L200,104 C120,140 60,100 0,140 Z" fill="{AUR_G}" opacity=".55"/>',
            f'<path d="M0,86 C60,44 120,90 200,52 L200,74 C130,110 60,70 0,106 Z" fill="{AUR_V}" opacity=".5"/>',
            "</g>",
            f'<path d="{SHIELD_IN}" fill="none" stroke="{ICE}" stroke-width="2.6"/>',
            compass(100, 84, 44, SNOW, ICE, GOLD),
            name, racing]
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 282">{"".join(body)}</svg>'


def compass_mark(big, mid, core):
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">{compass(50, 50, 49, big, mid, core)}</svg>'


def tagline(fill):
    a, w = words(JOSEFIN, TAGLINE, 40, 0, 40, fill, spacing=0.12, anchor="start")
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w:.1f} 44">{a}</svg>'


MARKS = {
    "logo-light": lambda: logo(SNOW),
    "logo-dark": lambda: logo(NIGHT),
    "compass-gold": lambda: compass_mark(GOLD, ICE, SNOW),
    "compass-pine": lambda: compass_mark(PINE, ICE, GOLD),
    "compass-night": lambda: compass_mark(NIGHT, SNOW, GOLD),
    "tagline-pine": lambda: tagline(PINE),
    "tagline-snow": lambda: tagline(SNOW),
    "tagline-night": lambda: tagline(NIGHT),
}

if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for name, make in MARKS.items():
        (OUT / f"{name}.svg").write_text(make())
    print("wrote", len(MARKS), "marks to", OUT)
