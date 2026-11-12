"""Green Goat Racing's marks as SVG files with every letter drawn as a path, so they bake the
same anywhere: the Green Goat roundel (the drink's mark), the team's milk-splash logo, and the
cartoon decals. Writes them to out/marks/.

Run: python3 marks.py   (needs fonttools)
"""
import math
from pathlib import Path

from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

HERE = Path(__file__).resolve().parent
OUT = HERE / "out" / "marks"
FONT = {
    "bangers": HERE / "font" / "Bangers.ttf",
    "rubik": HERE / "font" / "RubikMonoOne.ttf",
    "archivo": HERE / "font" / "ArchivoBlack.ttf",
}

G = "#8cff1a"
GD = "#3f9e0c"
GDD = "#173d06"
K = "#0a0d08"
M = "#f6f8ee"
Y = "#ffe600"
PK = "#ff2bd6"

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


def centred(font, s, size, cx, cy, spacing=0.0, width=None):
    """s centred on (cx, cy), squeezed to width if wider. Returns a transform and d."""
    d, (x0, y0, x1, y1) = glyphs(font, s, size, 0, 0, spacing)
    k = min(1.0, width / (x1 - x0)) if width else 1.0
    return f'translate({cx - (x0 + x1) / 2 * k:.2f} {cy - (y0 + y1) / 2:.2f}) scale({k:.4f} 1)', d


def svg(name, vb, body):
    OUT.joinpath(f"{name}.svg").write_text(
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{" ".join(f"{v:.2f}" for v in vb)}">{body}</svg>')


def edged(d, fill, edges=(), transform=""):
    """A path with outlines behind it: edges are (colour, width), widest first."""
    t = f' transform="{transform}"' if transform else ""
    out = "".join(f'<path{t} d="{d}" fill="{c}" stroke="{c}" stroke-width="{w}" stroke-linejoin="round"/>' for c, w in edges)
    return out + f'<path{t} d="{d}" fill="{fill}"/>'


def mirror(d):
    """Path d (absolute M/L/C/Q/Z in a 100 box) mirrored left to right."""
    out, nums = [], d.replace(",", " ").split()
    i = 0
    while i < len(nums):
        t = nums[i]
        if t[0].isalpha():
            out.append(t[0])
            rest = t[1:]
            if rest:
                nums.insert(i + 1, rest)
            i += 1
            continue
        out.append(f"{100 - float(t):.1f},{nums[i + 1]}")
        i += 2
    return " ".join(out)


HORN = "M38,30 C30,6 6,2 3,28 C12,14 27,16 33,37 Z"
EAR = "M34,38 L12,47 L34,50 Z"
EYE = "M36,44 L47,49 L37,53 Z"


def goat(head, eye, horn=None, x=0, y=0, s=100):
    """The goat's head, front on, in an s box at (x, y). Horizontal-bar pupils."""
    horn = horn or head
    return (f'<g transform="translate({x} {y}) scale({s / 100})">'
            f'<path d="{HORN} {mirror(HORN)}" fill="{horn}"/><path d="{EAR} {mirror(EAR)}" fill="{head}"/>'
            f'<path d="M33,30 L67,30 L62,72 Q50,94 38,72 Z" fill="{head}"/><path d="M44,86 L50,100 L56,86 Z" fill="{head}"/>'
            f'<path d="{EYE} {mirror(EYE)}" fill="{eye}"/>'
            f'<rect x="38.5" y="48" width="6" height="2" fill="{head}"/><rect x="55.5" y="48" width="6" height="2" fill="{head}"/>'
            f'<circle cx="45" cy="78" r="2.2" fill="{eye}"/><circle cx="55" cy="78" r="2.2" fill="{eye}"/></g>')


def goat_outline(x, y, s, col, w):
    """The goat's silhouette in one colour, grown by w, for a sticker's edge."""
    return (f'<g transform="translate({x} {y}) scale({s / 100})" fill="{col}" stroke="{col}" stroke-width="{w * 100 / s}" '
            f'stroke-linejoin="round"><path d="{HORN} {mirror(HORN)}"/><path d="{EAR} {mirror(EAR)}"/>'
            f'<path d="M33,30 L67,30 L62,72 Q50,94 38,72 Z"/><path d="M44,86 L50,100 L56,86 Z"/></g>')


def star(cx, cy, r0, r1, n, rot=0.0):
    pts = []
    for i in range(n * 2):
        a = rot + i * math.pi / n
        r = r1 if i % 2 == 0 else r0
        pts.append(f"{cx + math.cos(a) * r:.2f},{cy + math.sin(a) * r:.2f}")
    return "M" + " L".join(pts) + " Z"


SPLASH = ("M60,130 Q70,90 84,118 Q92,60 110,112 Q122,40 140,108 Q150,30 160,108 Q178,40 190,112 "
          "Q208,60 216,118 Q230,90 240,130 Q150,150 60,130 Z")
SPLASH_DROPS = ((96, 58, 7), (208, 50, 6), (150, 18, 8))


def splash(fill, transform=""):
    """The milk-splash crown, in a 300 x 150 box centred on x 150, with its flying drops."""
    t = f' transform="{transform}"' if transform else ""
    return (f'<g{t} fill="{fill}"><path d="{SPLASH}"/>'
            + "".join(f'<circle cx="{x}" cy="{y}" r="{r}"/>' for x, y, r in SPLASH_DROPS) + "</g>")


def roundel():
    """The drink's mark: the goat on a black disc with green rays, a milk splash at its chin."""
    cx, cy = 100, 100
    rays = "".join(f'<path d="M{cx},{cy} L{cx + 90 * math.cos(a - 0.11):.2f},{cy + 90 * math.sin(a - 0.11):.2f} '
                   f'L{cx + 90 * math.cos(a + 0.11):.2f},{cy + 90 * math.sin(a + 0.11):.2f} Z" fill="{GDD}"/>'
                   for a in [i * math.pi / 9 for i in range(18)])
    tr, d = centred("bangers", "GREEN GOAT", 30, cx, 178, 0.04, 150)
    body = (f'<circle cx="{cx}" cy="{cy}" r="96" fill="{K}"/><circle cx="{cx}" cy="{cy}" r="92" fill="{G}"/>'
            f'<circle cx="{cx}" cy="{cy}" r="80" fill="{K}"/>'
            f'<clipPath id="disc"><circle cx="{cx}" cy="{cy}" r="80"/></clipPath><g clip-path="url(#disc)">{rays}'
            f'{splash(M, "translate(100 136) scale(0.62) translate(-150 -100)")}</g>'
            + goat(G, K, x=42, y=30, s=116)
            + f'<rect x="28" y="160" width="144" height="36" rx="6" fill="{K}"/>'
            + edged(d, M, transform=tr))
    svg("roundel", (2, 2, 196, 196), body)


def team_logo():
    """The team's logo: a green milk splash over GREEN GOAT and RACING."""
    tr1, d1 = centred("rubik", "GREEN GOAT", 26, 150, 160, 0.0, 250)
    tr2, d2 = centred("archivo", "R A C I N G", 15, 150, 188, 0.1, 170)
    body = (f'{splash(G)}'
            f'<path transform="{tr1}" d="{d1}" fill="{K}"/><path transform="{tr2}" d="{d2}" fill="{GD}"/>')
    svg("team", (40, 6, 220, 192), body)
    # The same, light, for dark paint.
    svg("team-light", (40, 6, 220, 192), body.replace(f'fill="{K}"', f'fill="{M}"'))


def words(name, font, s, fill, spacing=0.0, edges=()):
    """One line of lettering, its viewBox tight round the letters and their edges."""
    d, (x0, y0, x1, y1) = glyphs(font, s, 100, 0, 0, spacing)
    pad = max([w / 2 for _, w in edges], default=0)
    svg(name, (x0 - pad, y0 - pad, x1 - x0 + 2 * pad, y1 - y0 + 2 * pad), edged(d, fill, edges))


STICKER = ((M, 26), (K, 12))  # white die-cut edge round a black outline


def decals():
    """Cartoon stickers: thick black outline, white die-cut edge."""
    # A volt burst shouting 30G.
    b = star(100, 100, 62, 92, 14, 0.1)
    tr, d = centred("bangers", "30G!", 66, 100, 102, 0.02, 120)
    svg("burst-30g", (-12, -12, 224, 224), edged(b, Y, STICKER) + edged(d, K, transform=tr))
    # A speech bubble going BAAA!
    bub = "M20,20 Q20,4 36,4 L204,4 Q220,4 220,20 L220,84 Q220,100 204,100 L90,100 L52,132 L62,100 L36,100 Q20,100 20,84 Z"
    tr, d = centred("bangers", "BAAAA!!", 64, 120, 54, 0.03, 180)
    svg("bubble", (6, -10, 228, 156), edged(bub, M, STICKER) + edged(d, PK, ((K, 8),), tr))
    # A lightning bolt.
    bolt = "M40,0 L120,0 L84,92 L138,92 L18,250 L52,132 L0,132 Z"
    svg("bolt", (-16, -16, 170, 282), edged(bolt, Y, STICKER))
    # The goat, as a sticker.
    svg("goat-sticker", (-14, -14, 128, 128), goat_outline(0, 0, 100, M, 26) + goat_outline(0, 0, 100, K, 12) + goat(G, K, K))
    # A milk drop with a goat face's eyes.
    drop = "M60,0 C60,0 110,70 110,105 A50,50 0 0,1 10,105 C10,70 60,0 60,0 Z"
    svg("drop", (-14, -14, 148, 184), edged(drop, M, STICKER)
        + f'<path d="M34,100 L52,108 L35,115 Z M86,100 L68,108 L85,115 Z" fill="{K}"/>'
        + f'<path d="M44,132 Q60,146 76,132" fill="none" stroke="{K}" stroke-width="6" stroke-linecap="round"/>')
    # Lettering stickers.
    words("gg-sticker", "bangers", "GREEN GOAT", M, 0.03, ((M, 22), (K, 12)))
    words("more", "bangers", "MORE! MORE! MORE!", G, 0.03, STICKER)
    words("overstim", "bangers", "OVERSTIMULATED", PK, 0.03, STICKER)
    words("protein", "bangers", "GOAT'S MILK PROTEIN", Y, 0.03, STICKER)


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    roundel()
    team_logo()
    decals()
    words("name-black", "rubik", "GREEN GOAT", K)
    words("name-light", "rubik", "GREEN GOAT", M)
    words("goats-milk", "rubik", "GOAT'S MILK", G)
    words("limit", "rubik", "MILK THE LIMIT", K)
    print("wrote", OUT)
