"""Cóndor Racing: five low-fi livery options on a plain cuboid the size of the Liberty Mantle,
and eight mockups of the team's mark, in one viewer.

Run: python3 lowfi.py <out.html>
"""
import base64
import json
import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
L, W, H = 4.869, 1.852, 1.49          # the Mantle's length, body width and height
AX_F, AX_R = 0.98, 3.83               # wheel centres, metres back from the nose
TYRE = 0.34

FONTS = {
    "Titillium": ROOT / "unity/Assets/Raceday/Resources/Fonts/TitilliumWeb-Bold.ttf",
    "Unbounded": ROOT / "tools/cars/oboe/font/Unbounded-ExtraBold.ttf",
}


_CSS = None


def font_css():
    """Both fonts, cut down to the letters a livery uses, so every panel can carry them."""
    global _CSS
    if _CSS is None:
        import io
        from fontTools import subset
        from fontTools.ttLib import TTFont
        rules = []
        for n, path in FONTS.items():
            f = TTFont(path)
            sub = subset.Subsetter()
            sub.populate(text="ABCDEFGHIJKLMNOPQRSTUVWXYZÓ0123456789 ")
            sub.subset(f)
            buf = io.BytesIO()
            f.save(buf)
            rules.append(f"@font-face{{font-family:'{n}';src:url(data:font/ttf;base64,{base64.b64encode(buf.getvalue()).decode()})}}")
        _CSS = "".join(rules)
    return _CSS


# --- the condor ----------------------------------------------------------------------------
def condor_path():
    """A soaring Andean condor seen from above, wingspan 2 (x -1..1), head up (y negative).
    Long straight wings, six splayed fingers at each tip, a short fan tail."""
    def wing(sg):
        pts = [(0.07, -0.06), (0.30, -0.10), (0.55, -0.13), (0.70, -0.14)]
        n = 6
        for i in range(n):
            t = i / (n - 1)
            bx, by = 0.70 + 0.03 * t, -0.13 + 0.31 * t
            ang = math.radians(-22 + 52 * t)
            ln = 0.30 - 0.12 * t
            dx, dy = math.cos(ang), math.sin(ang)
            nx, ny = -dy, dx
            w = 0.024
            pts += [(bx - nx * w * 0.2, by - ny * w * 0.2),
                    (bx + dx * ln * 0.7 - nx * w, by + dy * ln * 0.7 - ny * w),
                    (bx + dx * ln, by + dy * ln),
                    (bx + dx * ln * 0.7 + nx * w, by + dy * ln * 0.7 + ny * w),
                    (bx + nx * w * 0.6 + 0.004, by + ny * w * 0.6 + 0.022)]
        pts += [(0.62, 0.20), (0.45, 0.24), (0.30, 0.27), (0.16, 0.25), (0.08, 0.19)]
        return [(sg * x, y) for x, y in pts]
    r = wing(1)
    l = list(reversed(wing(-1)))
    tail = [(0.08, 0.19), (0.075, 0.34), (0.0, 0.37), (-0.075, 0.34), (-0.08, 0.19)]
    head = [(-0.07, -0.06), (-0.04, -0.13), (-0.025, -0.21), (0.0, -0.24), (0.025, -0.21), (0.04, -0.13)]
    pts = r + tail[1:-1] + l + head
    return "M" + " L".join(f"{x:.4f},{y:.4f}" for x, y in pts) + " Z"


def condor_band():
    """The white band across the upper wing (the condor's secondaries)."""
    def side(sg):
        return [(sg * x, y) for x, y in [(0.12, 0.12), (0.30, 0.15), (0.52, 0.13), (0.62, 0.15), (0.62, 0.20), (0.45, 0.24), (0.30, 0.27), (0.16, 0.25), (0.10, 0.20)]]
    out = []
    for sg in (1, -1):
        p = side(sg)
        out.append("M" + " L".join(f"{x:.4f},{y:.4f}" for x, y in p) + " Z")
    return " ".join(out)


CONDOR = condor_path()
BAND = condor_band()
RUFF = "M-0.06,-0.07 Q0,-0.03 0.06,-0.07 L0.05,-0.10 Q0,-0.075 -0.05,-0.10 Z"


def bird(cx, cy, span, ink, band=None, ruff=None, rotate=0, flip=False):
    s = span / 2
    sx = -s if flip else s
    g = f'<g transform="translate({cx:.4f} {cy:.4f}) rotate({rotate}) scale({sx:.4f} {s:.4f})">'
    g += f'<path d="{CONDOR}" fill="{ink}"/>'
    if band:
        g += f'<path d="{BAND}" fill="{band}"/>'
    if ruff:
        g += f'<path d="{RUFF}" fill="{ruff}"/>'
    return g + "</g>"


# --- faces ---------------------------------------------------------------------------------
SIZE = {"left": (L, H), "right": (L, H), "top": (W, L), "front": (W, H), "rear": (W, H)}


class Paint:
    """Art for the five faces, in metres. Sides take (s, y): s back from the nose, y up."""

    def __init__(self, base):
        self.base = base
        self.el = {f: [] for f in SIZE}

    @staticmethod
    def sxy(face, s, y):
        return (s if face == "left" else L - s, H - y)

    @staticmethod
    def txy(x, s):
        return (x + W / 2, L - s)          # x: + to the car's left; nose at the bottom

    @staticmethod
    def fxy(face, x, y):
        return ((x + W / 2) if face == "front" else (W / 2 - x), H - y)

    @staticmethod
    def d(pts):
        return "M" + " L".join(f"{u:.4f},{v:.4f}" for u, v in pts) + " Z"

    def side(self, pts, fill):
        for f in ("left", "right"):
            self.el[f].append(f'<path d="{self.d([self.sxy(f, s, y) for s, y in pts])}" fill="{fill}"/>')

    def top(self, pts, fill):
        self.el["top"].append(f'<path d="{self.d([self.txy(x, s) for x, s in pts])}" fill="{fill}"/>')

    def ends(self, pts, fill, faces=("front", "rear")):
        for f in faces:
            self.el[f].append(f'<path d="{self.d([self.fxy(f, x, y) for x, y in pts])}" fill="{fill}"/>')

    def raw(self, face, svg):
        self.el[face].append(svg)

    def side_raw(self, fn):
        """fn(face, map) returns SVG for that side, map turning (s, y) into the drawing."""
        for f in ("left", "right"):
            self.el[f].append(fn(f, lambda s, y, f=f: self.sxy(f, s, y)))

    def text(self, face, s, u, v, h, fill, font="Titillium", spacing=0.0, rotate=0):
        self.el[face].append(
            f'<text x="{u:.4f}" y="{v:.4f}" font-family="{font}" font-size="{h / 0.72:.4f}" fill="{fill}" '
            f'letter-spacing="{spacing * h:.4f}" text-anchor="middle" dominant-baseline="central" '
            f'transform="rotate({rotate} {u:.4f} {v:.4f})">{s}</text>')

    def side_text(self, s, at, y, h, fill, **kw):
        for f in ("left", "right"):
            u, v = self.sxy(f, at, y)
            self.text(f, s, u, v, h, fill, **kw)

    def number(self, face, u, v, h, ink, disc=None, r=None):
        if disc:
            self.el[face].append(f'<circle cx="{u:.4f}" cy="{v:.4f}" r="{r:.4f}" fill="{disc}"/>')
        self.text(face, "88", u, v, h, ink)

    def side_number(self, at, y, h, ink, disc=None, r=None):
        for f in ("left", "right"):
            u, v = self.sxy(f, at, y)
            self.number(f, u, v, h, ink, disc, r)

    def svg(self, face, ppm=300):
        w, h = SIZE[face]
        return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{round(w * ppm)}" height="{round(h * ppm)}" '
                f'viewBox="0 0 {w} {h}"><style>{font_css()}</style><rect width="{w}" height="{h}" fill="{self.base}"/>'
                + "".join(self.el[face]) + "</svg>")


# --- palette -------------------------------------------------------------------------------
YEL, BLU, RED = "#FCD116", "#003893", "#CE1126"
BLK, WHT = "#141416", "#F4F2EC"
EMR, EMR2, GOLD = "#0A5C3C", "#13804F", "#D9A521"
CRM, INK = "#EADFC3", "#1A1714"
IND, MAG, ORA, TUR, SUN = "#24164F", "#E0237A", "#F47A1F", "#16B3AE", "#F6C51B"


def bandera():
    """Flag colours. Yellow over the top half, blue then red low down, sweeping up at the tail."""
    p = Paint(YEL)
    p.side([(0, 0), (0, 0.42), (2.6, 0.42), (L, 0.95), (L, 0)], BLU)
    p.side([(0, 0), (0, 0.24), (2.6, 0.24), (L, 0.70), (L, 0)], RED)
    p.ends([(-W, 0), (-W, 0.42), (W, 0.42), (W, 0)], BLU, ("front",))
    p.ends([(-W, 0), (-W, 0.24), (W, 0.24), (W, 0)], RED, ("front",))
    p.ends([(-W, 0), (-W, 0.95), (W, 0.95), (W, 0)], BLU, ("rear",))
    p.ends([(-W, 0), (-W, 0.70), (W, 0.70), (W, 0)], RED, ("rear",))
    for sg in (1, -1):
        p.top([(sg * W / 2, 0), (sg * W / 2, L), (sg * (W / 2 - 0.07), L), (sg * (W / 2 - 0.07), 2.6), (sg * (W / 2 - 0.03), 0)], BLU)
    p.raw("top", bird(*p.txy(0, 0.55), 0.85, BLU))
    p.side_raw(lambda f, m: bird(*m(0.55, 0.92), 0.70, BLU))
    p.side_number(3.05, 0.95, 0.38, BLU)
    p.number("top", *p.txy(0, 2.5), 0.55, BLU)
    p.side_text("CÓNDOR RACING", 2.35, 0.33, 0.09, YEL, spacing=0.12)
    return p


def condor():
    """The bird itself: black, a white ruff where the neck meets the body, white wing feathers."""
    p = Paint(BLK)
    # the ruff: a white collar across the car at the base of the windscreen
    p.top([(-W, 1.55), (W, 1.55), (W, 1.80), (-W, 1.80)], WHT)
    p.side([(1.50, H), (1.75, H), (1.95, 0.2), (1.70, 0.2)], WHT)
    # feathers: long white blades streaming back from the collar along the side
    for i, (y0, y1, ln) in enumerate([(0.86, 0.94, 2.6), (0.70, 0.78, 2.35), (0.54, 0.62, 2.05), (0.38, 0.46, 1.7)]):
        s0 = 1.95 + 0.05 * i
        p.side([(s0, y1), (s0 + ln * 0.8, y1 + 0.02), (s0 + ln, (y0 + y1) / 2 + 0.03), (s0 + ln * 0.8, y0 + 0.01), (s0, y0)], WHT)
    for sg in (1, -1):
        p.top([(sg * 0.55, 2.3), (sg * 0.75, 2.3), (sg * 0.68, L - 0.2), (sg * 0.62, L - 0.2)], WHT)
    p.ends([(-W, 0.78), (W, 0.78), (W, 0.86), (-W, 0.86)], WHT, ("rear",))
    # the red of the male's head, a thin stripe at the nose
    p.ends([(-W, 0.55), (W, 0.55), (W, 0.62), (-W, 0.62)], RED, ("front",))
    p.top([(-W, 0), (W, 0), (W, 0.06), (-W, 0.06)], RED)
    p.raw("top", bird(*p.txy(0, 0.70), 0.95, WHT))
    p.side_number(1.15, 0.62, 0.34, BLK, disc=WHT, r=0.26)
    p.number("top", *p.txy(0, 3.2), 0.5, WHT)
    p.side_text("CÓNDOR", 3.55, 1.12, 0.12, WHT, font="Unbounded", spacing=0.1)
    return p


def esmeralda():
    """Colombian emerald green with a band of Muisca gold along the shoulder."""
    p = Paint(EMR)
    p.side([(0, 0), (0, 0.30), (L, 0.30), (L, 0)], EMR2)
    p.side([(0, 0.88), (L, 0.88), (L, 0.96), (0, 0.96)], GOLD)
    # stepped Muisca fret under the gold band
    def fret(f, m):
        out, s = [], 0.15
        while s < L - 0.3:
            pts = [(s, 0.88), (s + 0.16, 0.88), (s + 0.16, 0.80), (s + 0.08, 0.80), (s + 0.08, 0.84), (s + 0.12, 0.84), (s + 0.12, 0.86), (s + 0.04, 0.86), (s + 0.04, 0.88)]
            out.append(f'<path d="{Paint.d([m(a, b) for a, b in pts])}" fill="{GOLD}"/>')
            s += 0.22
        return "".join(out)
    p.side_raw(fret)
    p.ends([(-W, 0.88), (W, 0.88), (W, 0.96), (-W, 0.96)], GOLD)
    p.ends([(-W, 0), (-W, 0.30), (W, 0.30), (W, 0)], EMR2)
    for sg in (1, -1):
        p.top([(sg * 0.30, 0), (sg * 0.36, 0), (sg * 0.36, L), (sg * 0.30, L)], GOLD)
    # an emerald-cut stone on the bonnet, a gold condor over it
    cx, cy = p.txy(0, 0.62)
    p.raw("top", f'<path d="M{cx - 0.18},{cy - 0.26} L{cx + 0.18},{cy - 0.26} L{cx + 0.26},{cy - 0.18} L{cx + 0.26},{cy + 0.18} '
                 f'L{cx + 0.18},{cy + 0.26} L{cx - 0.18},{cy + 0.26} L{cx - 0.26},{cy + 0.18} L{cx - 0.26},{cy - 0.18} Z" fill="{EMR2}" stroke="{GOLD}" stroke-width="0.025"/>')
    p.raw("top", bird(cx, cy, 0.42, GOLD))
    p.side_number(2.35, 0.60, 0.34, GOLD)
    p.number("top", *p.txy(0, 2.5), 0.5, GOLD)
    p.side_text("CÓNDOR RACING", 3.5, 0.15, 0.08, GOLD, spacing=0.14)
    return p


def vueltiao():
    """The sombrero vueltiao: cream cane with black woven bands of pintas."""
    p = Paint(CRM)

    def weave(s0, s1, y0, y1):
        """A black band holding a row of cream zigzag pintas."""
        def fn(f, m):
            out = [f'<path d="{Paint.d([m(s0, y0), m(s1, y0), m(s1, y1), m(s0, y1)])}" fill="{INK}"/>']
            h = y1 - y0
            step = h * 1.2
            s = s0 + step * 0.2
            while s + step < s1:
                pts = [(s, y0 + h * 0.5), (s + step * 0.5, y0 + h * 0.82), (s + step, y0 + h * 0.5), (s + step * 0.5, y0 + h * 0.18)]
                out.append(f'<path d="{Paint.d([m(a, b) for a, b in pts])}" fill="{CRM}"/>')
                out.append(f'<path d="{Paint.d([m(s + step * 0.5, y0 + h * 0.36), m(s + step * 0.68, y0 + h * 0.5), m(s + step * 0.5, y0 + h * 0.64), m(s + step * 0.32, y0 + h * 0.5)])}" fill="{INK}"/>')
                s += step
            return "".join(out)
        return fn
    p.side_raw(weave(0, L, 0.18, 0.40))
    p.side([(0, 0.44), (L, 0.44), (L, 0.48), (0, 0.48)], INK)
    p.side([(0, 0.95), (L, 0.95), (L, 0.99), (0, 0.99)], INK)
    for f in ("front", "rear"):
        p.ends([(-W, 0.18), (W, 0.18), (W, 0.40), (-W, 0.40)], INK, (f,))
        p.ends([(-W, 0.44), (W, 0.44), (W, 0.48), (-W, 0.48)], INK, (f,))
    # concentric bands on the top, as the hat is woven in rings
    for k, ins in enumerate((0.0, 0.16)):
        t = 0.06 if k else 0.10
        x0, x1, s0, s1 = -W / 2 + ins, W / 2 - ins, ins, L - ins
        for pts in ([(x0, s0), (x1, s0), (x1, s0 + t), (x0, s0 + t)], [(x0, s1 - t), (x1, s1 - t), (x1, s1), (x0, s1)],
                    [(x0, s0), (x0 + t, s0), (x0 + t, s1), (x0, s1)], [(x1 - t, s0), (x1, s0), (x1, s1), (x1 - t, s1)]):
            p.top(pts, INK)
    p.raw("top", bird(*p.txy(0, 0.70), 0.80, INK, band=CRM))
    p.side_number(2.35, 0.70, 0.32, INK)
    p.number("top", *p.txy(0, 2.6), 0.5, INK)
    p.side_text("CÓNDOR", 3.55, 0.72, 0.12, INK, font="Unbounded", spacing=0.1)
    p.side([(0, 1.30), (0.5, 1.30), (0.5, 1.34), (0, 1.34)], RED)
    return p


def carnaval():
    """Barranquilla carnival and Wayuu weaving: deep indigo with bright diamond stripes."""
    p = Paint(IND)
    cols = [MAG, ORA, SUN, TUR]

    def wayuu(f, m):
        out = []
        for i, c in enumerate(cols):
            y0 = 0.16 + i * 0.075
            out.append(f'<path d="{Paint.d([m(0, y0), m(L, y0), m(L, y0 + 0.05), m(0, y0 + 0.05)])}" fill="{c}"/>')
        s, k = 0.1, 0
        while s < L - 0.3:
            c = cols[k % 4]
            out.append(f'<path d="{Paint.d([m(s, 0.75), m(s + 0.14, 0.89), m(s + 0.28, 0.75), m(s + 0.14, 0.61)])}" fill="{c}"/>')
            out.append(f'<path d="{Paint.d([m(s + 0.14, 0.70), m(s + 0.19, 0.75), m(s + 0.14, 0.80), m(s + 0.09, 0.75)])}" fill="{IND}"/>')
            s += 0.30
            k += 1
        return "".join(out)
    p.side_raw(wayuu)
    for f in ("front", "rear"):
        for i, c in enumerate(cols):
            y0 = 0.16 + i * 0.075
            p.ends([(-W, y0), (W, y0), (W, y0 + 0.05), (-W, y0 + 0.05)], c, (f,))
    for i, c in enumerate(cols):
        x = -0.24 + i * 0.16
        p.top([(x, 0), (x + 0.10, 0), (x + 0.10, L), (x, L)], c)
    p.raw("top", bird(*p.txy(0, 0.62), 0.80, WHT))
    p.side_number(2.3, 1.08, 0.30, WHT)
    p.number("top", *p.txy(0, 3.4), 0.5, WHT)
    p.side_text("CÓNDOR RACING", 3.5, 1.08, 0.09, SUN, spacing=0.12)
    return p


OPTIONS = [
    ("Bandera", bandera, "The blue and red sweep up behind the rear door into the C-pillar, so the yellow owns the doors and glasshouse. The mark sits in the front wing behind the arch."),
    ("Cóndor", condor, "The white collar rings the car at the base of the windscreen like the bird's ruff. The feather blades start at the front door shut line and fade out over the rear arch."),
    ("Esmeralda", esmeralda, "The gold fret runs along the shoulder crease just under the side windows. The lighter green fills the sills and bumper skirts."),
    ("Vueltiao", vueltiao, "The woven band wraps the sills and both bumpers below the lamps, so the glasshouse stays clean cream. The roof carries the hat's rings."),
    ("Carnaval", carnaval, "The diamond row rides the shoulder crease and the stripes follow the door bottoms. The roof stripes run from the bonnet over the boot lid."),
]


# --- the eight marks -----------------------------------------------------------------------
def marks():
    css = f"<style>{font_css()}</style>"

    def svg(body, vb="-1.1 -1.1 2.2 2.2"):
        return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{vb}">{css}{body}</svg>'

    def t(s, x, y, h, fill, font="Titillium", spacing=0.0, anchor="middle"):
        return (f'<text x="{x}" y="{y}" font-family="{font}" font-size="{h / 0.72:.4f}" fill="{fill}" letter-spacing="{spacing * h:.4f}" '
                f'text-anchor="{anchor}" dominant-baseline="central">{s}</text>')

    out = []
    # 1 soaring condor over the wordmark
    out.append(svg(bird(0, -0.30, 1.9, BLK, band=WHT, ruff=WHT) + t("CÓNDOR RACING", 0, 0.60, 0.15, BLK, spacing=0.12)))
    # 2 roundel
    out.append(svg(f'<circle r="1.02" fill="{BLK}"/><circle r="0.90" fill="none" stroke="{YEL}" stroke-width="0.05"/>'
                   + bird(0, -0.22, 1.45, WHT) + t("CÓNDOR", 0, 0.50, 0.15, YEL, font="Unbounded", spacing=0.12), "-1.1 -1.1 2.2 2.2"))
    # 3 wordmark, the accent drawn as a red feather over the first O
    ox = -0.73
    out.append(svg(f'<text x="0" y="0.05" font-family="Unbounded" font-size="0.50" text-anchor="middle" '
                   f'fill="{BLK}" dominant-baseline="central">CONDOR</text>'
                   + f'<path d="M{ox - 0.10},-0.33 Q{ox + 0.02},-0.42 {ox + 0.16},-0.56 Q{ox + 0.08},-0.38 {ox - 0.06},-0.28 Z" fill="{RED}"/>'
                   + t("RACING", 0, 0.55, 0.13, BLK, spacing=1.0), "-1.6 -1.1 3.2 2.2"))
    # 4 condor head in profile, white ruff, in a shield
    out.append(svg(
        f'<path d="M-0.62,-0.95 L0.62,-0.95 L0.62,0.25 Q0.62,0.75 0,1.02 Q-0.62,0.75 -0.62,0.25 Z" fill="{BLU}"/>'
        f'<path d="M-0.40,0.55 Q-0.45,0.05 -0.15,-0.28 Q0.02,-0.55 0.28,-0.50 Q0.45,-0.46 0.48,-0.30 L0.30,-0.24 '
        f'Q0.36,-0.12 0.22,-0.06 Q0.10,0.10 0.12,0.30 Q0.15,0.48 0.30,0.55 Z" fill="{BLK}"/>'
        f'<path d="M-0.42,0.55 Q-0.52,0.32 -0.40,0.10 Q-0.30,0.30 -0.20,0.12 Q-0.12,0.32 0.00,0.14 Q0.05,0.35 0.16,0.24 Q0.16,0.45 0.32,0.55 Z" fill="{WHT}"/>'
        f'<circle cx="0.20" cy="-0.36" r="0.035" fill="{YEL}"/>'
        f'<path d="M0.04,-0.48 Q0.12,-0.62 0.26,-0.54 Q0.16,-0.52 0.10,-0.44 Z" fill="{RED}"/>'
        f'<rect x="-0.62" y="0.66" width="1.24" height="0.06" fill="{YEL}"/><rect x="-0.62" y="0.72" width="1.24" height="0.06" fill="{RED}"/>'))
    # 5 Andean peaks, a condor above
    out.append(svg(
        f'<rect x="-1.05" y="-0.75" width="2.1" height="1.5" rx="0.12" fill="{WHT}" stroke="{BLK}" stroke-width="0.05"/>'
        f'<path d="M-1.0,0.70 L-0.45,-0.05 L-0.22,0.22 L0.18,-0.35 L0.55,0.15 L0.70,0.0 L1.0,0.38 L1.0,0.70 Z" fill="{BLU}"/>'
        f'<path d="M0.18,-0.35 L0.30,-0.18 L0.22,-0.14 L0.14,-0.20 L0.06,-0.18 Z" fill="{WHT}"/>'
        + bird(-0.30, -0.42, 0.90, BLK), "-1.1 -0.8 2.2 1.6"))
    # 6 Muisca gold, a stepped angular condor
    st = []
    for sg in (1, -1):
        for i in range(5):
            x0 = 0.10 + i * 0.17
            st.append(f'<rect x="{min(sg * x0, sg * (x0 + 0.15)):.3f}" y="{-0.30 + i * 0.07:.3f}" width="0.15" height="{0.42 - i * 0.05:.3f}" fill="{GOLD}"/>')
    out.append(svg(f'<rect x="-1.05" y="-1.05" width="2.1" height="2.1" rx="0.1" fill="{EMR}"/>' + "".join(st)
                   + f'<rect x="-0.10" y="-0.55" width="0.20" height="1.0" fill="{GOLD}"/>'
                   + f'<rect x="-0.06" y="-0.70" width="0.12" height="0.16" fill="{GOLD}"/>'
                   + f'<path d="M-0.10,0.45 L0.10,0.45 L0.18,0.70 L-0.18,0.70 Z" fill="{GOLD}"/>'
                   + f'<rect x="-0.06" y="-0.40" width="0.12" height="0.05" fill="{EMR}"/>' + t("CÓNDOR", 0, 0.88, 0.12, GOLD, spacing=0.3)))
    # 7 a condor crossing the yellow sun over the red horizon
    out.append(svg(f'<circle cx="0" cy="-0.05" r="0.78" fill="{YEL}"/><rect x="-1.05" y="0.42" width="2.1" height="0.10" fill="{BLU}"/>'
                   f'<rect x="-1.05" y="0.56" width="2.1" height="0.10" fill="{RED}"/>' + bird(0, -0.10, 1.95, BLK, band=WHT)))
    # 8 flag wing: bands of fingers in yellow, blue and red
    fw = []
    for i, c in enumerate((YEL, YEL, BLU, RED)):
        y = -0.36 + i * 0.20
        fw.append(f'<path d="M-0.95,{y:.2f} L0.55,{y - 0.18:.2f} L1.0,{y - 0.24:.2f} L0.62,{y - 0.02:.2f} L-0.95,{y + 0.15:.2f} Z" fill="{c}"/>')
    out.append(svg("".join(fw) + t("CÓNDOR", -0.95, 0.55, 0.20, BLK, font="Unbounded", spacing=0.1, anchor="start"), "-1.1 -0.85 2.2 1.7"))
    return out


PAGE = r"""<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Cóndor Racing options</title>
<style>
:root{--bg:#e8e8ea;--fg:#1b1d21;--chip:#fff;--line:#c8ccd1;--on:#1b1d21;--on-fg:#fff;--card:#f6f6f7}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){--bg:#141619;--fg:#e9ebee;--chip:#22262b;--line:#3a3f46;--on:#e9ebee;--on-fg:#141619;--card:#1d2024;color-scheme:dark}}
:root[data-theme="dark"]{--bg:#141619;--fg:#e9ebee;--chip:#22262b;--line:#3a3f46;--on:#e9ebee;--on-fg:#141619;--card:#1d2024;color-scheme:dark}
*{box-sizing:border-box}html,body{margin:0}
body{background:var(--bg);color:var(--fg);font:500 15px/1.4 system-ui,sans-serif}
#stage{height:62vh;min-height:320px}canvas{display:block;width:100%;height:100%;touch-action:none}
.bar{display:flex;flex-wrap:wrap;justify-content:center;gap:6px;padding:10px 16px}
button{font:inherit;color:var(--fg);background:var(--chip);border:1px solid var(--line);border-radius:999px;padding:7px 14px;cursor:pointer}
button[aria-pressed="true"]{background:var(--on);color:var(--on-fg);border-color:var(--on)}
#note{max-width:720px;margin:0 auto;padding:0 16px 18px;text-align:center}
.marks{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:12px;max-width:960px;margin:0 auto;padding:8px 16px 32px}
.mk{background:#fff;border-radius:12px;padding:14px;display:flex;flex-direction:column;align-items:center;gap:6px;color:#1b1d21}
.mk img{width:100%;aspect-ratio:1.375;object-fit:contain}
.mk b{font-size:20px}
</style>
<div id="stage"></div>
<div class="bar" id="bar"></div>
<p id="note"></p>
<div class="marks" id="marks"></div>
<script src="https://cdn.jsdelivr.net/npm/three@0.147.0/build/three.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/three@0.147.0/examples/js/controls/OrbitControls.js"></script>
<script>
const OPTS = __OPTS__, MARKS = __MARKS__, L = __L__, W = __W__, H = __H__;
const uri = s => 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(s)));
const stage = document.getElementById('stage');
const renderer = new THREE.WebGLRenderer({antialias:true, preserveDrawingBuffer:true});
renderer.setPixelRatio(Math.min(devicePixelRatio, 2)); renderer.outputEncoding = THREE.sRGBEncoding;
stage.appendChild(renderer.domElement);
const scene = new THREE.Scene();
scene.background = new THREE.Color(getComputedStyle(document.body).backgroundColor).convertSRGBToLinear();
const camera = new THREE.PerspectiveCamera(30, 1, 0.05, 100); camera.position.set(6.4, 3.0, 6.0);
const controls = new THREE.OrbitControls(camera, renderer.domElement); controls.target.set(0, 0.6, 0); controls.enableDamping = true;
scene.add(new THREE.HemisphereLight(0xffffff, 0x60656c, 0.6));
const sun = new THREE.DirectionalLight(0xffffff, 0.55); sun.position.set(4, 8, 5); scene.add(sun);
const fill = new THREE.DirectionalLight(0xffffff, 0.35); fill.position.set(-6, 3, -4); scene.add(fill);
const floor = new THREE.Mesh(new THREE.CircleGeometry(9, 64), new THREE.MeshStandardMaterial({color:0xcfd3d8, roughness:1}));
floor.rotation.x = -Math.PI/2; scene.add(floor);
// BoxGeometry faces: +x is the car's left, +y the top, +z the nose.
const ORDER = ['left', 'right', 'top', 'bottom', 'front', 'rear'];
const box = new THREE.Mesh(new THREE.BoxGeometry(W, H, L));
box.position.y = H / 2; scene.add(box);
const mats = OPTS.map(o => ORDER.map(f => {
  if (f === 'bottom') return new THREE.MeshStandardMaterial({color:0x222222});
  const t = new THREE.TextureLoader().load(uri(o.faces[f])); t.encoding = THREE.sRGBEncoding; t.anisotropy = 8;
  return new THREE.MeshStandardMaterial({map:t, roughness:0.35, metalness:0.05});
}));
const bar = document.getElementById('bar'), note = document.getElementById('note');
const btns = OPTS.map((o, i) => { const b = document.createElement('button'); b.textContent = o.name; b.onclick = () => wear(i); bar.appendChild(b); return b; });
function wear(i) { box.material = mats[i]; btns.forEach((b, k) => b.setAttribute('aria-pressed', k === i)); note.textContent = OPTS[i].note; }
wear(0);
const mk = document.getElementById('marks');
MARKS.forEach((s, i) => { const d = document.createElement('div'); d.className = 'mk'; d.innerHTML = `<img src="${uri(s)}" alt=""><b>${i + 1}</b>`; mk.appendChild(d); });
function resize() { const w = stage.clientWidth, h = stage.clientHeight; renderer.setSize(w, h, false); camera.aspect = w / h;
  camera.fov = Math.min(70, 30 * Math.max(1, 1.1 / camera.aspect)); camera.updateProjectionMatrix(); }
new ResizeObserver(resize).observe(stage); resize();
window.view = (p, tg) => { camera.position.set(...p); if (tg) controls.target.set(...tg); controls.update(); };
window.wear = wear;
(function loop(){ controls.update(); renderer.render(scene, camera); requestAnimationFrame(loop); })();
</script>
"""


def main(out):
    opts = []
    for name, fn, note in OPTIONS:
        p = fn()
        opts.append({"name": name, "note": note, "faces": {f: p.svg(f) for f in SIZE}})
    html = (PAGE.replace("__OPTS__", json.dumps(opts)).replace("__MARKS__", json.dumps(marks()))
            .replace("__L__", str(L)).replace("__W__", str(W)).replace("__H__", str(H)))
    Path(out).write_text(html)
    print("wrote", out, Path(out).stat().st_size // 1024, "KB")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else HERE / "lowfi.html")
