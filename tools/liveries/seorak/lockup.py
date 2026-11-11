"""Team Seorak lockups: TEAM set in the Seorak brand font (Unbounded ExtraBold, the wordmark's
own font at the wordmark's size and spacing) followed by Seorak's wordmark, the badge stacked
over that line, and 설악 in Black Han Sans over the line. Writes out/marks/*.svg.

Run: python3 lockup.py
"""
import re
from pathlib import Path

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
HANGUL = HERE / "font" / "BlackHanSans-Seorak.ttf"  # subset to 설악팀, SIL OFL 1.1
FONT = ROOT / "tools" / "cars" / "cheonma" / "font" / "Unbounded-ExtraBold.ttf"
BRAND = ROOT / "src" / "renderer" / "assets" / "brands" / "seorak"
NAVY, WHITE, GOLD = "#1c2a3a", "#ffffff", "#d9a443"
SIZE = 100  # the wordmark is the font at 100 units: caps 75 tall, baseline at 0


def word(text, x0=0.0, font_path=FONT, size=SIZE, track=0.0):
    """Outline of text at size, baseline at y 0, starting at x0. Returns (path d, end x)."""
    font = TTFont(font_path)
    gs, cmap, k = font.getGlyphSet(), font.getBestCmap(), size / font["head"].unitsPerEm
    d, x = [], x0
    for ch in text:
        g = gs[cmap[ord(ch)]]
        pen = SVGPathPen(gs)
        g.draw(TransformPen(pen, (k, 0, 0, -k, x, 0)))
        d.append(pen.getCommands())
        x += g.width * k + track * size
    return " ".join(d), x


def inner(svg, ink):
    body = re.search(r"<svg[^>]*>(.*)</svg>", svg, re.S).group(1)
    return body.replace(f'fill="{NAVY}"', f'fill="{ink}"')


def viewbox(svg):
    return [float(v) for v in re.search(r'viewBox="([^"]+)"', svg).group(1).split()]


def line(ink):
    """TEAM, a word space, then the wordmark, all on one baseline. Returns (svg body, width)."""
    wm = (BRAND / "seorak-wordmark-colour.svg").read_text()
    team, end = word("TEAM")
    space = TTFont(FONT)["hmtx"]["space"][0] * SIZE / 1000
    shift = end + space * 1.4
    vb = viewbox(wm)
    body = f'<path d="{team}" fill="{ink}"/><g transform="translate({shift:.2f} 0)">{inner(wm, ink)}</g>'
    return body, shift + vb[0] + vb[2]


def write(name, w, h, y0, body):
    out = HERE / "out" / "marks"
    out.mkdir(parents=True, exist_ok=True)
    (out / name).write_text(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 {y0:.2f} {w:.2f} {h:.2f}" '
                            f'width="{w:.2f}" height="{h:.2f}">{body}</svg>\n')


if __name__ == "__main__":
    badge = (BRAND / "seorak-badge-colour.svg").read_text()
    bvb = viewbox(badge)
    for tag, ink in (("navy", NAVY), ("white", WHITE)):
        body, w = line(ink)
        write(f"team-seorak-{tag}.svg", w + 4, 82, -78, body)
        # Badge centred over the line, its height 1.9 times the caps, a half-cap gap between.
        bh = 75 * 1.9
        bw = bh * bvb[2] / bvb[3]
        top = -75 - 37 - bh
        stack = (f'<svg x="{(w - bw) / 2:.2f}" y="{top:.2f}" width="{bw:.2f}" height="{bh:.2f}" '
                 f'viewBox="{" ".join(map(str, bvb))}">{inner(badge, NAVY)}</svg>{body}')
        write(f"team-seorak-stack-{tag}.svg", w + 4, -top + 4, top - 2, stack)
        # 설악 with the line under it in gold at a quarter of its cap height, both centred.
        han, hw = word("설악", font_path=HANGUL, size=380, track=0.04)
        hw -= 0.04 * 380
        scale = 0.36
        lw = w * scale
        width = max(hw, lw)
        body2 = (f'<path d="{han}" fill="{ink}" transform="translate({(width - hw) / 2:.2f} 0)"/>'
                 f'<g transform="translate({(width - lw) / 2:.2f} {75 * scale + 60:.2f}) scale({scale})">{line(GOLD)[0]}</g>')
        write(f"seorak-hangul-{tag}.svg", width + 4, 292 + 75 * scale + 70, -292, body2)
        write(f"seorak-hangul-only-{tag}.svg", hw + 4, 296, -292, f'<path d="{han}" fill="{ink}"/>')
    print("wrote", HERE / "out" / "marks")
