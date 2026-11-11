"""Shared primitive: a livery drawn as five SVG panels over a car's true-scale views.

Shapes are given in the car's own metres (game OBJ axes: +X car's left, +Y up, +Z nose) and
land on the right panel. Side art is drawn once and lands on both sides, front to the nose,
with text that reads the right way on each. Number boxes mark where the game draws the race
number. Holds no colours or designs.
"""
import base64
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
FONT_DIR = HERE.parents[2] / "unity" / "Assets" / "Raceday" / "Resources" / "Fonts"
FONTS = {"Titillium": FONT_DIR / "TitilliumWeb-Bold.ttf"}


def _font_css():
    rules = []
    for name, path in FONTS.items():
        data = base64.b64encode(path.read_bytes()).decode()
        rules.append(f"@font-face{{font-family:'{name}';src:url(data:font/ttf;base64,{data})}}")
    return "".join(rules)


class Livery:
    def __init__(self, views_json, base):
        meta = json.loads(Path(views_json).read_text())
        self.ppm = meta["ppm"]
        self.views = meta["views"]
        self.base = base
        self.art = {v: [] for v in self.views}
        self.numbers = []

    # --- mapping -------------------------------------------------------------------------
    def uv(self, view, p):
        f = self.views[view]
        return (sum(a * b for a, b in zip(f["u"], p)), sum(a * b for a, b in zip(f["v"], p)))

    @staticmethod
    def _side(view, z, y):
        return (1.0 if view == "left" else -1.0, y, z)

    # --- drawing on one view, in car metres ---------------------------------------------
    def poly(self, view, pts, fill):
        d = " ".join(f"{'M' if i == 0 else 'L'}{u:.4f},{v:.4f}" for i, (u, v) in enumerate(self.uv(view, p) for p in pts))
        self.art[view].append(f'<path d="{d} Z" fill="{fill}"/>')

    def text(self, view, s, p, height, fill, rotate=0, spacing=0.0):
        u, v = self.uv(view, p)
        self.art[view].append(
            f'<text x="{u:.4f}" y="{v:.4f}" font-family="Titillium" font-size="{height / 0.72:.4f}" '
            f'letter-spacing="{spacing * height:.4f}" fill="{fill}" text-anchor="middle" dominant-baseline="central" '
            f'transform="rotate({rotate} {u:.4f} {v:.4f})">{s}</text>')

    def number(self, view, centre, w, h, ink, rotate=0):
        """Where the game draws the race number: a box w by h metres, upright in the drawing
        unless rotated (degrees, clockwise)."""
        u, v = self.uv(view, centre)
        self.numbers.append({"view": view, "u": round(u, 4), "v": round(v, 4), "w": w, "h": h, "ink": ink, "rotate": rotate})

    # --- helpers for art that repeats across views ----------------------------------------
    def side_poly(self, zy, fill):
        for view in ("left", "right"):
            self.poly(view, [self._side(view, z, y) for z, y in zy], fill)

    def side_text(self, s, z, y, height, fill, spacing=0.0):
        for view in ("left", "right"):
            self.text(view, s, self._side(view, z, y), height, fill, spacing=spacing)

    def side_number(self, z, y, w, h, ink):
        for view in ("left", "right"):
            self.number(view, self._side(view, z, y), w, h, ink)

    def band_x(self, x0, x1, fill, views=("top", "front", "rear"), z=(-9, 9), y=(-9, 9)):
        """A band between two widths, nose to tail over the chosen views."""
        for view in views:
            if view == "top":
                self.poly(view, [(x0, 0, z[0]), (x1, 0, z[0]), (x1, 0, z[1]), (x0, 0, z[1])], fill)
            else:
                self.poly(view, [(x0, y[0], 0), (x1, y[0], 0), (x1, y[1], 0), (x0, y[1], 0)], fill)

    # --- output ----------------------------------------------------------------------------
    def svg(self, view, sample=None):
        """The panel as SVG. sample: a number to draw in the boxes, for previews only."""
        f = self.views[view]
        u0, v0, w, h = f["u0"], f["v0"], f["u1"] - f["u0"], f["v1"] - f["v0"]
        out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{f["px"][0]}" height="{f["px"][1]}" '
               f'viewBox="{u0:.4f} {v0:.4f} {w:.4f} {h:.4f}"><style>{_font_css()}</style>',
               f'<rect x="{u0}" y="{v0}" width="{w}" height="{h}" fill="{self.base}"/>', *self.art[view]]
        if sample is not None:
            for n in self.numbers:
                if n["view"] == view:
                    out.append(f'<text x="{n["u"]}" y="{n["v"]}" font-family="Titillium" font-size="{n["h"] / 0.72:.4f}" '
                               f'fill="{n["ink"]}" text-anchor="middle" dominant-baseline="central" '
                               f'transform="rotate({n["rotate"]} {n["u"]} {n["v"]})">{sample}</text>')
        out.append("</svg>")
        return "".join(out)

    def write(self, out_dir, sample=None):
        out = Path(out_dir)
        out.mkdir(parents=True, exist_ok=True)
        for view in self.views:
            (out / f"{view}.svg").write_text(self.svg(view, sample))
        (out / "numbers.json").write_text(json.dumps({"base": self.base, "numbers": self.numbers}, indent=1))
        print("wrote", out)
