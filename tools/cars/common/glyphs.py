"""Shared primitive: letter outlines of a text in a font, as flat loops in em units."""
from fontTools.pens.basePen import BasePen
from fontTools.pens.boundsPen import BoundsPen
from fontTools.ttLib import TTFont
from fontTools.ttLib.removeOverlaps import removeOverlaps
from shapely.geometry import Polygon
from shapely.ops import unary_union


class _Flatten(BasePen):
    def __init__(self, glyphset, steps=8):
        super().__init__(glyphset)
        self.loops, self.cur, self.steps = [], [], steps

    def _moveTo(self, p):
        self.cur = [p]

    def _lineTo(self, p):
        self.cur.append(p)

    def _curveToOne(self, a, b, c):
        p = self.cur[-1]
        for i in range(1, self.steps + 1):
            t = i / self.steps
            k = ((1 - t) ** 3, 3 * (1 - t) ** 2 * t, 3 * (1 - t) * t * t, t ** 3)
            self.cur.append(tuple(k[0] * p[j] + k[1] * a[j] + k[2] * b[j] + k[3] * c[j] for j in (0, 1)))

    def _qCurveToOne(self, a, b):
        p = self.cur[-1]
        for i in range(1, self.steps + 1):
            t = i / self.steps
            k = ((1 - t) ** 2, 2 * (1 - t) * t, t * t)
            self.cur.append(tuple(k[0] * p[j] + k[1] * a[j] + k[2] * b[j] for j in (0, 1)))

    def _closePath(self):
        if len(self.cur) > 2:
            if self.cur[0] == self.cur[-1]:
                self.cur.pop()
            self.loops.append(self.cur)
        self.cur = []

    _endPath = _closePath


def outlines(font_path, text):
    """{'cap': height of the first letter, 'width': advance, 'pieces': [[outer, holes...], ...]}"""
    font = TTFont(font_path)
    removeOverlaps(font)
    gs = font.getGlyphSet()
    cmap = font.getBestCmap()
    bp = BoundsPen(gs)
    gs[cmap[ord(text[0])]].draw(bp)
    x = 0
    shapes = []
    for ch in text:
        g = gs[cmap[ord(ch)]]
        pen = _Flatten(gs)
        g.draw(pen)
        shape = None
        for loop in pen.loops:
            p = Polygon([(px + x, py) for px, py in loop]).buffer(0)
            shape = p if shape is None else shape.symmetric_difference(p)
        if shape is not None:
            shapes.append(shape)
        x += g.width
    whole = unary_union(shapes)
    polys = [whole] if whole.geom_type == "Polygon" else list(whole.geoms)
    pieces = [[list(p.exterior.coords)[:-1]] + [list(i.coords)[:-1] for i in p.interiors] for p in polys]
    return {"text": text, "cap": bp.bounds[3], "width": x, "pieces": pieces}
