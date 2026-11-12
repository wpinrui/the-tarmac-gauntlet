"""Dockyard Racing on the Liberty Fantasy: harbour-town mechanics, a yellow shop-floor car covered
in tools, with the team's local diner on the tailgate. Writes one folder of panels per option
under out/, and the logos it draws with under out/logos/.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/LibertyFantasy/LibertyFantasy.obj out/views
     node ../common/digits.js --font fonts/Shrikhand-Regular.ttf --edge 0.09 --step 0.8 --out out/digits
     python3 design.py
"""
import math
import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "common"))
import livery  # noqa: E402
from livery import Livery  # noqa: E402

livery.FONTS["Alfa"] = HERE / "fonts" / "AlfaSlabOne-Regular.ttf"
livery.FONTS["Barlow"] = HERE / "fonts" / "BarlowCondensed-Bold.ttf"
livery.FONTS["Pacifico"] = HERE / "fonts" / "Pacifico-Regular.ttf"
LOGOS = HERE / "out" / "logos"

YELLOW = "#ffcc00"
TOOL = "#ffe680"       # the yellow, 25% lighter
NAVY = "#152a4a"
CREAM = "#fff4d6"
INK = "#111214"
RED = "#c92a22"
CHALK = "#22302b"
WHITE = "#ffffff"

# Fantasy landmarks in OBJ metres: z along the car (+ nose), y up. Read off the true-scale views.
NOSE, TAIL = 2.04, -1.92
FRONT_AXLE, REAR_AXLE = 1.2445, -1.2445
# Door: 0.85 to -0.50 along the car, sill 0.24 to belt line 0.90. The number fills its middle.
NUM_Z, NUM_Y, NUM_W, NUM_H = 0.18, 0.57, 0.66, 0.36
# Bonnet: shut line at z 1.0, nose at 2.04, |x| under 0.7 clear of the headlamps.
BONNET = (0.0, 0, 1.52)
# Tailgate skin under the rear glass: y 0.68 to 0.98, |x| 0.55 at the top narrowing to 0.46.
# The badge (y 0.92) and the Fantasy script (x 0.37, y 0.86) sit on it.
TAILGATE = (0.0, 0.83, 0)
# Roof between the windscreen header and the spoiler: z -1.40 to 0.20, |x| under 0.52.
ROOF = (0.0, 0, -0.60)
# Rear bumper between the reflectors: y 0.50 to 0.66, |x| under 0.62.
BUMPER = (0.0, 0.58, 0)


# ---------------------------------------------------------------- tools
# Each tool in a 100-unit box centred on 0. Holes are painted in the colour underneath.
def _tool(name, fill, hole):
    if name == "spanner":
        return (f'<g transform="rotate(-40)" fill="{fill}"><rect x="-28" y="-6" width="56" height="12"/><circle cx="-34" cy="0" r="15"/>'
                f'<circle cx="36" cy="0" r="12"/><rect x="-52" y="-6.5" width="18" height="13" fill="{hole}"/><circle cx="36" cy="0" r="6" fill="{hole}"/></g>')
    if name == "hammer":
        return (f'<g transform="rotate(34)" fill="{fill}"><rect x="-5" y="-18" width="10" height="62"/><rect x="-26" y="-36" width="44" height="20" rx="4"/>'
                f'<path d="M18,-36 Q34,-34 30,-18 L18,-16Z"/></g>')
    if name == "screwdriver":
        return (f'<g transform="rotate(46)" fill="{fill}"><rect x="-9" y="-46" width="18" height="40" rx="7"/><rect x="-3" y="-8" width="6" height="44"/>'
                f'<path d="M-5,36 L5,36 L2,46 L-2,46Z"/><rect x="-4.5" y="-40" width="2.5" height="28" fill="{hole}"/><rect x="2" y="-40" width="2.5" height="28" fill="{hole}"/></g>')
    if name == "nut":
        pts = " ".join(f"{30 * math.cos(i * math.pi / 3):.2f},{30 * math.sin(i * math.pi / 3):.2f}" for i in range(6))
        return f'<g fill="{fill}"><polygon points="{pts}"/><circle r="13" fill="{hole}"/></g>'
    if name == "gear":
        pts = []
        for i in range(16):
            a, r = i * math.pi / 8, (26 if i % 2 else 34)
            pts += [f"{math.cos(a - 0.12) * r:.2f},{math.sin(a - 0.12) * r:.2f}", f"{math.cos(a + 0.12) * r:.2f},{math.sin(a + 0.12) * r:.2f}"]
        return f'<g fill="{fill}"><polygon points="{" ".join(pts)}"/><circle r="11" fill="{hole}"/></g>'
    if name == "bolt":
        pts = " ".join(f"{-24 + 16 * math.cos(i * math.pi / 3 + math.pi / 6):.2f},{16 * math.sin(i * math.pi / 3 + math.pi / 6):.2f}" for i in range(6))
        threads = "".join(f'<rect x="{x}" y="-9" width="2.5" height="4" fill="{hole}"/><rect x="{x + 4}" y="5" width="2.5" height="4" fill="{hole}"/>' for x in range(-6, 38, 8))
        return f'<g transform="rotate(-28)" fill="{fill}"><polygon points="{pts}"/><rect x="-12" y="-8" width="52" height="16"/>{threads}</g>'
    raise ValueError(name)


TOOLS = ["spanner", "hammer", "screwdriver", "nut", "gear", "bolt"]


def tools(lv, view, size=0.21, gap=0.36, seed=1, keep=lambda u, v: True, fill=TOOL, hole=YELLOW):
    """Polka dots of tools over one view: a staggered grid, each dot a random tool at a random
    turn. keep(u, v) says whether a dot at that drawing point is laid."""
    f = lv.views[view]
    r = random.Random(f"{seed}-{view}")
    dy = gap * math.sqrt(3) / 2
    out = []
    j = 0
    v = f["v0"] - gap / 2
    while v < f["v1"] + gap:
        u = f["u0"] - gap / 2 + (gap / 2 if j % 2 else 0)
        while u < f["u1"] + gap:
            name, turn = r.choice(TOOLS), r.uniform(0, 360)
            if keep(u, v):
                out.append(f'<g transform="translate({u:.4f} {v:.4f}) rotate({turn:.1f}) scale({size / 100:.5f})">{_tool(name, fill, hole)}</g>')
            u += gap
        v += dy
        j += 1
    lv.art[view].append("".join(out))


# ---------------------------------------------------------------- logos
def _anchor(cx, cy, s, col, lw=9, edge=None):
    """The tattoo anchor, s units tall, ring at the top. edge: a sticker outline round it."""
    k = s / 100

    def body(c, w):
        return (f'<g transform="translate({cx} {cy}) scale({k})" fill="none" stroke="{c}" stroke-width="{w}" stroke-linecap="round" stroke-linejoin="round">'
                f'<path d="M0,-28 V28 M-16,-15 H16"/><circle cx="0" cy="-35" r="7"/><path d="M-26,6 A26,26 0 0,0 26,6"/>'
                f'<path d="M-30,14 L-26,4 L-18,10Z M30,14 L26,4 L18,10Z" fill="{c}"/></g>')
    return (body(edge, lw + 10) if edge else "") + body(col, lw)


def team_logo(layout):
    """Logo 7's wordmark with logo 4's anchor, as a sticker: a cream edge cut round it all."""
    word = (f'<text x="150" y="128" text-anchor="middle" font-family="Alfa" font-size="46" fill="{NAVY}" '
            f'stroke="{CREAM}" stroke-width="9" stroke-linejoin="round" paint-order="stroke">DOCKYARD</text>')
    bar = (f'<rect x="30" y="140" width="240" height="28" rx="3" fill="{NAVY}" stroke="{CREAM}" stroke-width="5"/>'
           f'<text x="150" y="155" text-anchor="middle" dominant-baseline="central" font-family="Barlow" font-size="22" '
           f'letter-spacing="13" fill="{YELLOW}">RACING</text>')
    if layout == "hang":   # the anchor hangs off a line where 7's hook was, flukes on the Y
        art = (f'<line x1="214" y1="0" x2="214" y2="18" stroke="{CREAM}" stroke-width="8"/><line x1="214" y1="0" x2="214" y2="18" stroke="{NAVY}" stroke-width="3"/>'
               + _anchor(214, 60, 96, NAVY, 9, CREAM) + word + bar)
        return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 300 175">{art}</svg>'
    if layout == "beside":
        art = (_anchor(42, 104, 140, NAVY, 10, CREAM)
               + word.replace('x="150"', 'x="204"').replace('font-size="46"', 'font-size="38"')
               + bar.replace('x="30"', 'x="104"').replace('width="240"', 'width="200"').replace('x="150"', 'x="204"').replace('letter-spacing="13"', 'letter-spacing="10"'))
        return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="-14 22 340 153">{art}</svg>'
    if layout == "behind":  # a big anchor with the wordmark across its shank
        art = (_anchor(150, 110, 230, NAVY, 9, CREAM) + word.replace('y="128"', 'y="104"')
               + bar.replace('y="140"', 'y="116"').replace('y="155"', 'y="131"'))
        return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 300 200">{art}</svg>'
    raise ValueError(layout)


def _plank(x, y, w, h, wood, grain, edge):
    return (f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" fill="{wood}" stroke="{edge}" stroke-width="4"/>'
            f'<path d="M{x + 12},{y + h * 0.3} q{w * 0.3},-6 {w * 0.6},0 t{w * 0.34},0 M{x + 18},{y + h * 0.74} q{w * 0.25},5 {w * 0.5},0 t{w * 0.42},0" '
            f'stroke="{grain}" stroke-width="2" fill="none"/>')


def diner_logo(scheme):
    """Logo 1, the Driftwood Diner plank, in three colourings that read better than the first."""
    s = {
        # bleached plank, navy script edged in cream, DINER cream on a navy tag
        "bleached": dict(wood="#dcc7a2", grain="#b89c74", edge=NAVY, script=NAVY, script_edge=CREAM, tag=NAVY, tag_ink=CREAM),
        # dark plank, cream script, red DINER
        "dark": dict(wood="#6e4529", grain="#8f6142", edge="#3e2615", script=CREAM, script_edge="#3e2615", tag="#3e2615", tag_ink="#e8392c"),
        # mid plank, white script edged navy, DINER white on a red tag
        "tagged": dict(wood="#b88555", grain="#94653b", edge=NAVY, script=WHITE, script_edge=NAVY, tag="#c92a22", tag_ink=WHITE),
    }[scheme]
    art = (_plank(10, 10, 280, 104, s["wood"], s["grain"], s["edge"])
           + f'<text x="150" y="68" text-anchor="middle" font-family="Pacifico" font-size="52" fill="{s["script"]}" '
             f'stroke="{s["script_edge"]}" stroke-width="7" stroke-linejoin="round" paint-order="stroke">Driftwood</text>'
           + f'<rect x="100" y="96" width="100" height="30" rx="4" fill="{s["tag"]}"/>'
           + f'<text x="150" y="111" text-anchor="middle" dominant-baseline="central" font-family="Barlow" font-size="22" letter-spacing="7" fill="{s["tag_ink"]}">DINER</text>')
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 300 130">{art}</svg>'


def logo_file(name, svg):
    LOGOS.mkdir(parents=True, exist_ok=True)
    p = LOGOS / f"{name}.svg"
    p.write_text(svg)
    return p


def _inner(svg):
    """An SVG's viewBox and contents, to nest it in another."""
    vb = svg.split('viewBox="')[1].split('"')[0]
    return vb, svg.split(">", 1)[1].rsplit("</svg>", 1)[0]


def _cup(x, y, s, col):
    k = s / 100
    return (f'<g transform="translate({x} {y}) scale({k})"><path d="M-30,-10 h60 v30 a30,30 0 0,1 -60,0z" fill="{col}"/>'
            f'<path d="M30,0 a14,14 0 0,1 0,28" fill="none" stroke="{col}" stroke-width="7"/>'
            f'<path d="M-12,-20 q-8,-12 0,-24 t0,-24 M8,-20 q-8,-12 0,-24 t0,-24" fill="none" stroke="{col}" stroke-width="5" stroke-linecap="round"/></g>')


def _burger(x, y, s, bun, filling):
    k = s / 100
    return (f'<g transform="translate({x} {y}) scale({k})"><path d="M-34,-6 a34,26 0 0,1 68,0z" fill="{bun}"/>'
            f'<rect x="-36" y="-2" width="72" height="9" rx="4" fill="{filling[0]}"/><rect x="-38" y="9" width="76" height="12" rx="6" fill="{filling[1]}"/>'
            f'<path d="M-34,25 h68 a6,6 0 0,1 -6,10 h-56 a6,6 0 0,1 -6,-10z" fill="{bun}"/></g>')


def diner_ad(style):
    """The tailgate as an advert for the Driftwood Diner: a board cut to the tailgate's shape,
    the plank in the middle under the badge, what they serve either side. Units are millimetres,
    1100 by 300, centred on TAILGATE."""
    vb, plank = _inner(diner_logo("dark"))
    board = "M20,12 L1080,12 L1010,292 L90,292Z"
    if style == "billboard":
        ground, edge, ink, accent = CREAM, NAVY, NAVY, RED
        left = _cup(240, 178, 76, NAVY)
        right = _burger(870, 135, 100, "#e2a24a", ("#3f8f3a", "#6b3a22"))
        lines_l = ("BOTTOMLESS", "COFFEE")
        lines_r = ("ALL-DAY", "BREAKFAST")
        foot = "PIER 9  HARBOUR ROAD"
    else:  # chalkboard: the specials board on the counter
        ground, edge, ink, accent = CHALK, "#8a5a34", CREAM, YELLOW
        left = right = ""
        lines_l = ("FRY-UP  6.50", "FISH & CHIPS  8.00")
        lines_r = ("PIT CREW PIE  5.00", "COFFEE  FREE REFILLS")
        foot = "OPEN 6AM TILL LATE"
    def block(x, lines, y0, size):
        return "".join(f'<text x="{x}" y="{y0 + i * size * 1.1}" text-anchor="middle" font-family="Barlow" font-size="{size}" '
                       f'letter-spacing="{size * 0.08:.1f}" fill="{ink}">{t.replace("&", "&amp;")}</text>' for i, t in enumerate(lines))
    if style == "billboard":
        sides = block(240, lines_l, 232, 26) + block(870, lines_r, 214, 30)
    else:
        sides = block(240, lines_l, 190, 26) + block(870, lines_r, 160, 28)
        sides += f'<text x="270" y="86" text-anchor="middle" font-family="Pacifico" font-size="40" fill="{accent}">Today</text>'
        sides += f'<text x="870" y="96" text-anchor="middle" font-family="Pacifico" font-size="40" fill="{accent}">Specials</text>'
    art = (f'<path d="{board}" fill="{ground}" stroke="{edge}" stroke-width="12" stroke-linejoin="round"/>'
           f'<path d="M44,28 L1056,28 L994,276 L106,276Z" fill="none" stroke="{accent}" stroke-width="3"/>'
           + left + right + sides
           + f'<svg x="375" y="98" width="350" height="146" viewBox="{vb}">{plank}</svg>'
           + f'<text x="550" y="266" text-anchor="middle" dominant-baseline="auto" font-family="Barlow" font-size="22" letter-spacing="6" fill="{accent}">{foot}</text>')
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1100 300">{art}</svg>'


def bumper_strip(style):
    """A strip across the rear bumper finishing the advert, 1240 by 110 mm."""
    ground, ink = (NAVY, CREAM) if style == "billboard" else (CHALK, CREAM)
    line = "EAT LIKE A MECHANIC" if style == "billboard" else "PIER 9  HARBOUR ROAD"
    art = (f'<rect x="0" y="0" width="1240" height="110" rx="14" fill="{ground}"/>'
           f'<text x="620" y="56" text-anchor="middle" dominant-baseline="central" font-family="Alfa" font-size="58" letter-spacing="6" fill="{ink}">{line}</text>'
           + "".join(f'<path d="M{x},55 l10,-22 10,22 -10,22z" fill="{YELLOW}"/>' for x in (40, 1180)))
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1240 110">{art}</svg>'


# ---------------------------------------------------------------- roof cranes
# Each drawn 1600 by 1000 mm with the nose to the right, laid flat on the roof.
def _outline(art, w=14):
    """A decal cut with a cream edge: the art drawn fat in cream under itself."""
    fat = art.replace(f'fill="{NAVY}"', f'fill="{CREAM}" stroke="{CREAM}" stroke-width="{w}" stroke-linejoin="round"')
    fat = fat.replace(f'stroke="{NAVY}"', f'stroke="{CREAM}"')
    return f'<g>{fat}</g><g>{art}</g>'


def _truss(x0, x1, y, h, col, n):
    """A lattice beam from x0 to x1, h tall, centred on y, with n bays."""
    d = (x1 - x0) / n
    zig = " ".join(f"M{x0 + i * d:.0f},{y + (h / 2 if i % 2 else -h / 2):.0f} L{x0 + (i + 1) * d:.0f},{y + (-h / 2 if i % 2 else h / 2):.0f}" for i in range(n))
    return (f'<rect x="{x0}" y="{y - h / 2 - 7}" width="{x1 - x0}" height="14" fill="{col}"/>'
            f'<rect x="{x0}" y="{y + h / 2 - 7}" width="{x1 - x0}" height="14" fill="{col}"/>'
            f'<path d="{zig}" stroke="{col}" stroke-width="9" fill="none"/>')


def _hook(x, y, s, col):
    k = s / 100
    return (f'<g transform="translate({x} {y}) scale({k})"><rect x="-14" y="-40" width="28" height="22" rx="4" fill="{col}"/>'
            f'<path d="M0,-18 V8 A18,18 0 1,1 -18,26" fill="none" stroke="{col}" stroke-width="10" stroke-linecap="round"/></g>')


def crane(kind):
    if kind == "gantry":   # a quayside container crane, side on, its boom reaching for the nose
        art = (_truss(120, 1560, 300, 70, NAVY, 18)
               + f'<path d="M420,300 L540,80 L660,300 M540,80 L1500,265 M540,80 L180,280" stroke="{NAVY}" stroke-width="16" fill="none"/>'
               + f'<path d="M500,335 L440,900 L490,900 L560,335Z M820,335 L880,900 L830,900 L760,335Z" fill="{NAVY}"/>'
               + f'<rect x="440" y="560" width="440" height="34" fill="{NAVY}"/><rect x="380" y="890" width="560" height="26" fill="{NAVY}"/>'
               + f'<rect x="1160" y="330" width="80" height="50" fill="{NAVY}"/><line x1="1200" y1="380" x2="1200" y2="600" stroke="{NAVY}" stroke-width="5"/>'
               + _hook(1200, 640, 120, NAVY))
    elif kind == "jib":    # a lattice jib down the middle of the roof, the hook hanging off the back
        art = _truss(330, 1560, 500, 200, NAVY, 12) + _hook(250, 500, 300, NAVY).replace("translate(250 500)", "translate(250 500) rotate(90)")
    elif kind == "skyline":  # three cranes along the harbour, standing on the car's right edge
        def tower(x, h, reach, back):
            top = 940 - h
            return (_truss(x - 25, x + 25, 940 - h / 2, 50, NAVY, 1).replace(f'x="{x - 25}"', f'x="{x - 25}"')
                    + f'<rect x="{x - 26}" y="{top}" width="52" height="{h}" fill="none" stroke="{NAVY}" stroke-width="12"/>'
                    + f'<path d="' + " ".join(f"M{x - 26},{top + i * 52} L{x + 26},{top + (i + 1) * 52}" for i in range(int(h / 52))) + f'" stroke="{NAVY}" stroke-width="7"/>'
                    + _truss(x - back, x + reach, top - 25, 50, NAVY, int((reach + back) / 55))
                    + f'<path d="M{x},{top - 50} L{x},{top - 150} M{x},{top - 150} L{x + reach * 0.7},{top - 50} M{x},{top - 150} L{x - back},{top - 50}" stroke="{NAVY}" stroke-width="8"/>'
                    + f'<rect x="{x - back}" y="{top}" width="70" height="60" fill="{NAVY}"/>'
                    + f'<line x1="{x + reach * 0.8}" y1="{top}" x2="{x + reach * 0.8}" y2="{top + h * 0.45}" stroke="{NAVY}" stroke-width="5"/>'
                    + _hook(x + reach * 0.8, top + h * 0.45 + 30, 70, NAVY))
        art = tower(300, 560, 360, 160) + tower(820, 760, 520, 200) + tower(1360, 460, 200, 140)
    elif kind == "hook":   # a cable from the windscreen to the spoiler, the hook on the end
        art = (f'<line x1="1580" y1="500" x2="420" y2="500" stroke="{NAVY}" stroke-width="16"/>'
               + _hook(400, 500, 380, NAVY).replace("translate(400 500)", "translate(400 500) rotate(90)"))
    else:
        raise ValueError(kind)
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1600 1000">{_outline(art)}</svg>'


# ---------------------------------------------------------------- the livery
def shop_floor(views, ad="billboard", logo="beside", roof=None):
    """Yellow all over, tools all over; the diner's advert across the back."""
    lv = Livery(views, YELLOW)
    for view in lv.views:
        tools(lv, view, seed=3)
    lv.side_number(NUM_Z, NUM_Y, NUM_W, NUM_H, WHITE, edge=INK)
    svg = team_logo(logo)
    vb = [float(x) for x in svg.split('viewBox="')[1].split('"')[0].split()]
    # 1.0 m across the bonnet, whatever the layout's shape, reading from in front of the car
    lv.mark("top", logo_file(f"team-{logo}", svg), BONNET, 1.0 * vb[3] / vb[2], rotate=-90)
    lv.mark("rear", logo_file(f"ad-{ad}", diner_ad(ad)), TAILGATE, 0.30)
    lv.mark("rear", logo_file(f"bumper-{ad}", bumper_strip(ad)), BUMPER, 0.11)
    if roof:
        lv.mark("top", logo_file(f"roof-{roof}", crane(roof)), ROOF, 1.0)
    return lv


OPTIONS = {
    "billboard": lambda v: shop_floor(v, "billboard"),
    "gantry": lambda v: shop_floor(v, "billboard", roof="gantry"),
    "jib": lambda v: shop_floor(v, "billboard", roof="jib"),
    "skyline": lambda v: shop_floor(v, "billboard", roof="skyline"),
    "hook": lambda v: shop_floor(v, "billboard", roof="hook"),
    "chalkboard": lambda v: shop_floor(v, "chalkboard"),
}

if __name__ == "__main__":
    views = HERE / "out" / "views" / "views.json"
    only = sys.argv[1:]
    for name, make in OPTIONS.items():
        if not only or name in only:
            make(views).write(HERE / "out" / name)
