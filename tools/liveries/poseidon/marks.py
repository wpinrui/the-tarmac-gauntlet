"""Poseidon Racing's marks as SVG files with every letter drawn as a path, so they bake the
same anywhere: the life-ring logo, the trident, and each line of lettering. Writes them to
out/marks/.

Run: python3 marks.py   (needs fonttools and shapely)
"""
import math
from pathlib import Path

from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont
from shapely.geometry import LineString, Point, Polygon
from shapely.ops import unary_union

HERE = Path(__file__).resolve().parent
OUT = HERE / "out" / "marks"
FONT = {
    "staat": HERE / "font" / "Staatliches-Regular.ttf",
    "alfa": HERE / "font" / "AlfaSlabOne-Regular.ttf",
    "ops": HERE / "font" / "BlackOpsOne-Regular.ttf",
    "barlow": HERE / "font" / "BarlowCondensed-Bold.ttf",
}

TEAL = "#0e8f8c"
TEAL_D = "#07484d"
FOAM = "#eafaf6"
PINK = "#ff3d8b"

_fonts = {}


def glyphs(font, s, size, x, y, spacing=0.0):
    """s as one path d, baseline at y, starting at x. Returns (d, (xmin, ymin, xmax, ymax))."""
    f = _fonts.setdefault(font, TTFont(FONT[font]))
    gs, cmap, hmtx = f.getGlyphSet(), f.getBestCmap(), f["hmtx"]
    k = size / f["head"].unitsPerEm
    pen, bounds = SVGPathPen(gs), BoundsPen(gs)
    cx = x
    for c in s:
        n = cmap[ord(c)]
        t = (k, 0, 0, -k, cx, y)
        gs[n].draw(TransformPen(pen, t))
        gs[n].draw(TransformPen(bounds, t))
        cx += hmtx[n][0] * k + spacing * size
    return pen.getCommands(), bounds.bounds


def ring_d(geom):
    """A shapely polygon (or several) as one SVG path d."""
    polys = getattr(geom, "geoms", [geom])
    d = ""
    for p in polys:
        for ring in [p.exterior, *p.interiors]:
            pts = list(ring.coords)[:-1]
            d += "M" + " L".join(f"{x:.2f},{y:.2f}" for x, y in pts) + " Z "
    return d.strip()


def trident(height=100.0):
    """The trident, point up, as one solid outline: tip at (0, 0), butt at (0, height).
    The three prongs meet the curved yoke and the shaft without any seam."""
    s = height / 100
    w = 7.0 * s                               # prong and shaft width

    def head(cx, tip):
        return Polygon([(cx, tip), (cx + 10 * s, tip + 22 * s), (cx + w / 2, tip + 17 * s),
                        (cx - w / 2, tip + 17 * s), (cx - 10 * s, tip + 22 * s)])

    side = 25 * s
    parts = [head(0, 0), head(-side, 9 * s), head(side, 9 * s)]
    parts.append(LineString([(0, 15 * s), (0, 94 * s)]).buffer(w / 2, cap_style=2))
    # Side prongs and the yoke as one line: down one prong, round the U, up the other.
    arc = [(side * math.cos(a), 38 * s + 13 * s * math.sin(a)) for a in [i * math.pi / 24 for i in range(25)]]
    parts.append(LineString([(side, 24 * s), *arc, (-side, 24 * s)]).buffer(w / 2, cap_style=2, join_style=1))
    parts.append(Polygon([(0, 88 * s), (7 * s, 94 * s), (0, 100 * s), (-7 * s, 94 * s)]))  # the butt
    return unary_union(parts)


def svg(path, vb, body):
    path.write_text(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{" ".join(f"{v:.2f}" for v in vb)}">{body}</svg>')


def words(name, font, s, fill, spacing=0.0, stroke=None):
    """One line of lettering, its viewBox tight round the letters."""
    d, (x0, y0, x1, y1) = glyphs(font, s, 100, 0, 0, spacing)
    pad = stroke[1] if stroke else 0
    edge = (f'<path d="{d}" fill="{stroke[0]}" stroke="{stroke[0]}" stroke-width="{stroke[1] * 2}" '
            f'stroke-linejoin="round"/>' if stroke else "")
    svg(OUT / f"{name}.svg", (x0 - pad, y0 - pad, x1 - x0 + 2 * pad, y1 - y0 + 2 * pad), f'{edge}<path d="{d}" fill="{fill}"/>')


def trident_mark(name, fill):
    t = trident()
    x0, y0, x1, y1 = t.bounds
    svg(OUT / f"{name}.svg", (x0, y0, x1 - x0, y1 - y0), f'<path d="{ring_d(t)}" fill="{fill}"/>')


def lifering():
    """The team logo: a pink and white life ring round the trident on teal, the name on a plate."""
    cx, cy, R, r = 100, 92, 84, 50
    ring = Point(cx, cy).buffer(R, 128).difference(Point(cx, cy).buffer(r, 128))
    bands = []
    for i in range(4):
        a = i * math.pi / 2 + math.pi / 4
        wedge = Polygon([(cx, cy), *[(cx + 200 * math.cos(a + t), cy + 200 * math.sin(a + t)) for t in (-0.33, 0, 0.33)]])
        bands.append(ring.intersection(wedge))
    t = trident(84)
    tx0, ty0, tx1, ty1 = t.bounds
    from shapely.affinity import translate
    t = translate(t, cx, cy - (ty0 + ty1) / 2)
    plate_y = 150
    d, (x0, y0, x1, y1) = glyphs("staat", "POSEIDON", 30, 0, 0, 0.1)
    tw, th = x1 - x0, y1 - y0
    tx, ty = cx - tw / 2 - x0, plate_y + 17 - th / 2 - y0
    body = (f'<circle cx="{cx}" cy="{cy}" r="{R + 2}" fill="{TEAL_D}"/>'
            f'<path d="{ring_d(ring)}" fill="{FOAM}"/>'
            f'<path d="{ring_d(unary_union(bands))}" fill="{PINK}"/>'
            f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="{TEAL}" stroke="{TEAL_D}" stroke-width="3"/>'
            f'<path d="{ring_d(t)}" fill="{FOAM}"/>'
            f'<rect x="{cx - 88}" y="{plate_y}" width="176" height="34" rx="4" fill="{TEAL_D}"/>'
            f'<path transform="translate({tx:.2f} {ty:.2f})" d="{d}" fill="{FOAM}"/>')
    svg(OUT / "logo.svg", (cx - 90, cy - R - 3, 180, plate_y + 34 - (cy - R - 3) + 1), body)


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    lifering()
    for col, hexa in (("pink", PINK), ("foam", FOAM), ("teal", TEAL_D)):
        trident_mark(f"trident-{col}", hexa)
    words("name-alfa", "alfa", "POSEIDON", FOAM, 0.02, (TEAL_D, 4))
    words("name-ops", "ops", "POSEIDON", TEAL_D, 0.02)
    words("sub-pink", "barlow", "FISHING SUPPLIES", PINK, 0.12)
    words("sub-pinkd", "barlow", "FISHING SUPPLIES", "#c4166a", 0.12)
    words("rods", "alfa", "RODS  REELS  BAIT", FOAM, 0.04)
    words("hooked", "ops", "HOOKED ON SPEED", TEAL_D, 0.03)
    print("wrote", OUT)
