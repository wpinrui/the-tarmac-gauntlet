"""Letter outlines for the model name in the Liberty brand font (Yellowtail).

Run: python3 glyphs.py  -> glyphs.json, outlines in em units with the advance per letter.
"""
import json
from pathlib import Path

from fontTools.pens.basePen import BasePen
from fontTools.ttLib import TTFont
from fontTools.ttLib.removeOverlaps import removeOverlaps

FONT = Path(__file__).with_name("font") / "Yellowtail-Regular.ttf"
TEXT = "Collier"


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
    cap = getattr(font["OS/2"], "sCapHeight", 0) or int(font["head"].unitsPerEm * 0.7)
    x = 0
    letters = []
    for ch in text:
        name = cmap[ord(ch)]
        pen = FlatPen(gs)
        gs[name].draw(pen)
        if pen.contours:
            letters.append([[(px + x, py) for px, py in c] for c in pen.contours])
        x += gs[name].width
    # Script letters join up: merge every contour into one outline.
    import pathops
    path = pathops.Path()
    for letter in letters:
        for c in letter:
            path.moveTo(*c[0])
            for p in c[1:]:
                path.lineTo(*p)
            path.close()
    path.simplify(fix_winding=True)
    merged = [[(float(p[0]), float(p[1])) for p in c] for c in _contours(path)]
    return {"text": text, "cap": cap, "width": x, "letters": [merged]}


def _contours(path):
    out, cur = [], []
    for verb, pts in path.segments:
        if verb == "moveTo":
            cur = [pts[0]]
        elif verb == "lineTo":
            cur.append(pts[0])
        elif verb == "closePath":
            if len(cur) > 2:
                out.append(cur)
            cur = []
    return out


if __name__ == "__main__":
    out = Path(__file__).with_name("glyphs.json")
    out.write_text(json.dumps(build()))
    print("wrote", out)
