"""Dockyard Racing on the Liberty Fantasy: harbour-town mechanics, a yellow shop-floor car covered
in tools, with the team's local diner on the tailgate. Writes one folder of panels per option
under out/, and the logos it draws with under out/logos/.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/LibertyFantasy/LibertyFantasy.obj out/views
     node ../common/digits.js --font fonts/Shrikhand-Regular.ttf --edge 0.09 --out out/digits
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
TOOL = "#ffdb4d"       # the yellow, 15% lighter
NAVY = "#152a4a"
CREAM = "#fff4d6"
INK = "#111214"
WHITE = "#ffffff"

# Fantasy landmarks in OBJ metres: z along the car (+ nose), y up. Read off the true-scale views.
NOSE, TAIL = 2.04, -1.92
FRONT_AXLE, REAR_AXLE = 1.2445, -1.2445
# Door: 0.85 to -0.50 along the car, sill 0.24 to belt line 0.90. The number fills its middle.
NUM_Z, NUM_Y, NUM_W, NUM_H = 0.18, 0.57, 0.66, 0.36
# Bonnet: shut line at z 1.0, nose at 2.04, |x| under 0.7 clear of the headlamps.
BONNET = (0.0, 0, 1.52)
# Tailgate skin under the rear glass and badge: y 0.68 to 0.88, |x| under 0.3 clear of the script.
TAILGATE = (0.0, 0.775, 0)


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
               + word.replace('x="150"', 'x="186"').replace('font-size="46"', 'font-size="40"')
               + bar.replace('x="30"', 'x="86"').replace('width="240"', 'width="200"').replace('x="150"', 'x="186"').replace('letter-spacing="13"', 'letter-spacing="10"'))
        return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 30 300 145">{art}</svg>'
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
        # dark plank, cream script, yellow DINER
        "dark": dict(wood="#6e4529", grain="#8f6142", edge="#3e2615", script=CREAM, script_edge="#3e2615", tag="#3e2615", tag_ink=YELLOW),
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


# ---------------------------------------------------------------- shared marks
def marks(lv, logo="hang", diner="bleached"):
    lv.side_number(NUM_Z, NUM_Y, NUM_W, NUM_H, WHITE, edge=INK)
    svg = team_logo(logo)
    vb = [float(x) for x in svg.split('viewBox="')[1].split('"')[0].split()]
    # 1.0 m across the bonnet, whatever the layout's shape
    lv.mark("top", logo_file(f"team-{logo}", svg), BONNET, 1.0 * vb[3] / vb[2], rotate=90)
    lv.mark("rear", logo_file(f"diner-{diner}", diner_logo(diner)), TAILGATE, 0.2)


def shop_floor(views, logo="hang", diner="bleached"):
    """Yellow all over, tools all over."""
    lv = Livery(views, YELLOW)
    for view in lv.views:
        tools(lv, view, seed=3)
    marks(lv, logo, diner)
    return lv


def waterline(views, logo="hang", diner="bleached"):
    """A harbour tug: navy hull under a white boot stripe that runs along the sills and rises
    into each bumper, a cream rope above it, tools on the yellow topsides only."""
    lv = Livery(views, YELLOW)
    mid = (NOSE + TAIL) / 2
    wl = lambda z: 0.36 + 0.14 * ((z - mid) / ((NOSE - TAIL) / 2)) ** 4   # sill height, rising at the ends
    end_wl = 0.50                                                               # across the bumpers
    for view in ("left", "right"):
        tools(lv, view, seed=7, keep=lambda u, v, view=view: -v > wl(u if view == "right" else -u) + 0.09)
    tools(lv, "top", seed=7)
    for view in ("front", "rear"):
        tools(lv, view, seed=7, keep=lambda u, v: -v > end_wl + 0.09)
    zs = [TAIL - 0.2 + i * (NOSE - TAIL + 0.4) / 80 for i in range(81)]
    lv.side_poly([(z, wl(z)) for z in zs] + [(zs[-1], -0.2), (zs[0], -0.2)], NAVY)
    lv.side_poly([(z, wl(z) + 0.035) for z in zs] + [(z, wl(z) - 0.005) for z in reversed(zs)], WHITE)
    for view in ("front", "rear"):
        lv.poly(view, [(-1.2, end_wl, 0), (1.2, end_wl, 0), (1.2, -0.2, 0), (-1.2, -0.2, 0)], NAVY)
        lv.poly(view, [(-1.2, end_wl + 0.035, 0), (1.2, end_wl + 0.035, 0), (1.2, end_wl - 0.005, 0), (-1.2, end_wl - 0.005, 0)], WHITE)
    rope(lv, wl, end_wl)
    marks(lv, logo, diner)
    return lv


def rope(lv, wl, end_wl, lay=0.04):
    """A twisted cream rope laid just above the boot stripe, all round the car."""
    def strands(view, pts):
        out = []
        for (u0, v0), (u1, v1) in zip(pts, pts[1:]):
            out.append(f'<path d="M{u0:.4f},{v0 + 0.012:.4f} L{u1:.4f},{v1 - 0.012:.4f}" stroke="{CREAM}" stroke-width="0.012" stroke-linecap="round"/>')
        lv.art[view].append("".join(out))
    n = int((NOSE - TAIL + 0.4) / lay)
    for view in ("left", "right"):
        strands(view, [lv.uv(view, lv._side(view, TAIL - 0.2 + i * lay, wl(TAIL - 0.2 + i * lay) + 0.06)) for i in range(n + 1)])
    for view in ("front", "rear"):
        strands(view, [lv.uv(view, (-1.2 + i * lay, end_wl + 0.06, 0)) for i in range(int(2.4 / lay) + 1)])


OPTIONS = {
    "shop-floor": lambda v: shop_floor(v),
    "waterline": lambda v: waterline(v),
    "shop-floor-beside": lambda v: shop_floor(v, logo="beside"),
    "shop-floor-behind": lambda v: shop_floor(v, logo="behind"),
    "shop-floor-dark": lambda v: shop_floor(v, diner="dark"),
    "shop-floor-tagged": lambda v: shop_floor(v, diner="tagged"),
}

if __name__ == "__main__":
    views = HERE / "out" / "views" / "views.json"
    only = sys.argv[1:]
    for name, make in OPTIONS.items():
        if not only or name in only:
            make(views).write(HERE / "out" / name)
