"""Ironbark Racing's marks as SVG files with every letter drawn as a path, so they bake the
same anywhere: the tree-ring logo (light for green paint, dark for gold), the team's web address and the
Australian flag. Writes them to out/marks/.

Run: python3 marks.py   (needs fonttools)
"""
import math
import random
from pathlib import Path

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

HERE = Path(__file__).resolve().parent
OUT = HERE / "out" / "marks"
ZILLA = HERE / "font" / "ZillaSlab-Bold.ttf"
BARLOW = HERE / "font" / "BarlowCondensed-Bold.ttf"

GREEN = "#0f4d2c"
GOLD = "#f2b51c"
CREAM = "#f3ead2"
INK = "#1a1410"
BARK = "#4a2a1a"
TIMBER = "#c98b4b"
GRAIN = "#7a4524"
NAVY, RED, WHITE = "#012169", "#c8102e", "#ffffff"

_fonts = {}


def words(font, s, size, x, y, fill, spacing=0.0, anchor="middle"):
    """s as one path, cap height about size*0.7, baseline at y. spacing: extra gap per letter
    in units of size. Returns (svg, width)."""
    f = _fonts.setdefault(font, TTFont(font))
    gs, cmap, hmtx = f.getGlyphSet(), f.getBestCmap(), f["hmtx"]
    k = size / f["head"].unitsPerEm
    names = [cmap[ord(c)] for c in s]
    adv = [hmtx[n][0] * k + spacing * size for n in names]
    width = sum(adv) - spacing * size
    pen = SVGPathPen(gs)
    cx = x - {"middle": width / 2, "start": 0, "end": width}[anchor]
    for n, a in zip(names, adv):
        gs[n].draw(TransformPen(pen, (k, 0, 0, -k, cx, y)))
        cx += a
    return f'<path d="{pen.getCommands()}" fill="{fill}"/>', width


def star(cx, cy, r, n, fill, inner=0.47):
    pts = []
    for i in range(n * 2):
        a = -math.pi / 2 + i * math.pi / n
        rad = r * inner if i % 2 else r
        pts.append(f"{cx + math.cos(a) * rad:.2f},{cy + math.sin(a) * rad:.2f}")
    return f'<path d="M{" L".join(pts)} Z" fill="{fill}"/>'


def rings(cx, cy, R, seed=5):
    """An ironbark log end: bark, timber, growth rings and checks."""
    rnd = random.Random(seed)
    out = [f'<circle cx="{cx}" cy="{cy}" r="{R}" fill="{BARK}"/>', f'<circle cx="{cx}" cy="{cy}" r="{R * 0.88}" fill="{TIMBER}"/>']
    for k in range(1, 10):
        pts = []
        for i in range(48):
            a = i / 48 * 2 * math.pi
            rad = R * 0.88 * k / 10 * (1 + 0.05 * math.sin(3 * a + k) + 0.025 * (rnd.random() - 0.5))
            pts.append(f"{cx + math.cos(a) * rad:.2f},{cy + math.sin(a) * rad:.2f}")
        out.append(f'<path d="M{" L".join(pts)} Z" fill="none" stroke="{GRAIN}" stroke-width="{2.4 if k % 3 == 0 else 1.3}"/>')
    for _ in range(5):
        a = rnd.random() * 2 * math.pi
        out.append(f'<path d="M{cx:.2f},{cy:.2f} L{cx + math.cos(a) * R * 0.8:.2f},{cy + math.sin(a) * R * 0.8:.2f}" stroke="{BARK}" stroke-width="2"/>')
    return "".join(out)


def logo(word_fill, racing_fill):
    """Logo 3: the log end beside IRONBARK, a gold rule and RACING."""
    body = [rings(70, 70, 62)]
    x0 = 150
    a, w = words(ZILLA, "IRONBARK", 40, x0, 78, word_fill, anchor="start")
    b, _ = words(BARLOW, "RACING", 19, x0 + w / 2, 118, racing_fill, spacing=0.42)
    body += [a, f'<rect x="{x0}" y="90" width="{w:.1f}" height="4" fill="{GOLD}"/>', b]
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {x0 + w + 4:.1f} 140">{"".join(body)}</svg>'


def url(fill):
    a, w = words(BARLOW, "ironbark-supplies.au", 40, 0, 32, fill, spacing=0.04, anchor="start")
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w:.1f} 44">{a}</svg>'


def flag(mirror=False):
    """The Australian flag, 2:1. mirror puts the hoist on the right, for the car's right side."""
    w, h = 200, 100
    cw, ch = w / 2, h / 2
    s = [f'<rect width="{w}" height="{h}" fill="{NAVY}"/>',
         f'<clipPath id="ib-flag"><rect width="{cw}" height="{ch}"/></clipPath><g clip-path="url(#ib-flag)">',
         f'<path d="M0,0 L{cw},{ch} M{cw},0 L0,{ch}" stroke="{WHITE}" stroke-width="{ch * 0.2}"/>',
         f'<path d="M0,0 L{cw},{ch} M{cw},0 L0,{ch}" stroke="{RED}" stroke-width="{ch * 0.067}"/>',
         f'<rect x="{cw / 2 - ch / 6}" width="{ch / 3}" height="{ch}" fill="{WHITE}"/><rect y="{ch / 2 - ch / 6}" width="{cw}" height="{ch / 3}" fill="{WHITE}"/>',
         f'<rect x="{cw / 2 - ch / 10}" width="{ch / 5}" height="{ch}" fill="{RED}"/><rect y="{ch / 2 - ch / 10}" width="{cw}" height="{ch / 5}" fill="{RED}"/></g>',
         star(w / 4, h * 0.75, w * 0.075, 7, WHITE)]
    s += [star(fx * w, fy * h, 0.035 * w, 7, WHITE) for fx, fy in ((0.75, 0.167), (0.625, 0.45), (0.873, 0.4), (0.75, 0.833))]
    s.append(star(0.81 * w, 0.56 * h, 0.02 * w, 5, WHITE, 0.45))
    inner = "".join(s)
    if mirror:
        inner = f'<g transform="translate({w} 0) scale(-1 1)">{inner}</g>'
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}">{inner}</svg>'


MARKS = {
    "logo-light": lambda: logo(CREAM, GOLD),
    "logo-dark": lambda: logo(GREEN, INK),
    "url-green": lambda: url(GREEN),
    "flag": lambda: flag(),
    "flag-mirror": lambda: flag(True),
}

if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for name, make in MARKS.items():
        (OUT / f"{name}.svg").write_text(make())
    print("wrote", len(MARKS), "marks to", OUT)
