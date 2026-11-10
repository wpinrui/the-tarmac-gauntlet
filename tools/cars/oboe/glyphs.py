"""Letter outlines for the model name in the Kaiser brand font (Unbounded ExtraBold).

Run: python3 glyphs.py  -> glyphs.json, outlines in em units with the advance per letter.
"""
import json
from pathlib import Path

from fontTools.pens.basePen import BasePen
from fontTools.ttLib import TTFont
from fontTools.ttLib.removeOverlaps import removeOverlaps

FONT = Path(__file__).with_name("font") / "Unbounded-ExtraBold.ttf"
TEXT = "Oboe"


class FlatPen(BasePen):
    """Collects contours as polylines, flattening curves."""

    def __init__(self, glyphset, steps=6):
        super().__init__(glyphset)
        self.contours, self.cur, self.steps = [], [], steps

    def _moveTo(self, p):
        self.cur = [p]

    def _lineTo(self, p):
        self.cur.append(p)

    def _curveToOne(self, p1, p2, p3):
        p0 = self.cur[-1]
        for i in range(1, self.steps + 1):
            t = i / self.steps
            a = (1 - t) ** 3; b = 3 * (1 - t) ** 2 * t; c = 3 * (1 - t) * t * t; d = t ** 3
            self.cur.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))

    def _qCurveToOne(self, p1, p2):
        p0 = self.cur[-1]
        for i in range(1, self.steps + 1):
            t = i / self.steps
            a = (1 - t) ** 2; b = 2 * (1 - t) * t; c = t * t
            self.cur.append((a * p0[0] + b * p1[0] + c * p2[0], a * p0[1] + b * p1[1] + c * p2[1]))

    def _closePath(self):
        if len(self.cur) > 2:
            if self.cur[0] == self.cur[-1]:
                self.cur.pop()
            self.contours.append(self.cur)
        self.cur = []

    _endPath = _closePath


def build(text=TEXT):
    font = TTFont(FONT)
    removeOverlaps(font)
    gs = font.getGlyphSet()
    cmap = font.getBestCmap()
    from fontTools.pens.boundsPen import BoundsPen
    bp = BoundsPen(gs)
    gs[cmap[ord(text[0])]].draw(bp)
    cap = bp.bounds[3]  # height of the capital, the font has no cap height
    x = 0
    letters = []
    for ch in text:
        name = cmap[ord(ch)]
        pen = FlatPen(gs)
        gs[name].draw(pen)
        if pen.contours:
            letters.append([[(px + x, py) for px, py in c] for c in pen.contours])
        x += gs[name].width
    from shapely.geometry import Polygon
    from shapely.ops import unary_union
    shapes = []
    for letter in letters:
        g = None
        for c in letter:
            p = Polygon(c).buffer(0)
            g = p if g is None else g.symmetric_difference(p)
        shapes.append(g)
    whole = unary_union(shapes)
    polys = [whole] if whole.geom_type == "Polygon" else list(whole.geoms)
    # one entry per solid piece: outer loop first, then its holes
    pieces = [[list(p.exterior.coords)[:-1]] + [list(i.coords)[:-1] for i in p.interiors] for p in polys]
    return {"text": text, "cap": cap, "width": x, "letters": pieces}


if __name__ == "__main__":
    out = Path(__file__).with_name("glyphs.json")
    out.write_text(json.dumps(build()))
    print("wrote", out)
