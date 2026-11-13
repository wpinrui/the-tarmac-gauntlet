"""Steelworks Motorsport on the Seorak Hoeori: Steel Town. An industrial town's pride project,
painted with the town itself: a black skyline of smokestacks, a blast furnace, cranes, a mine
headframe and workers' cottages standing on the sills against a molten sky, smoke trailing back.
STEELWORKS rides above the town in cream, and the team's maker's plate sits on the bonnet.
Independent: no maker's logo or colours.

Oxblood over an orange sunset, with the team's website on the tail.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/SeorakHoeori/SeorakHoeori.obj out/views
     node ../common/digits.js --font font/BigShouldersDisplay-Black.ttf --edge 0.06 --out out/digits
     python3 design.py

Hoeori landmarks in OBJ metres: z along the car (nose +2.16, tail -2.14), y up, x to the car's left
(half width 0.89). Wheels at z +-1.28, centre y 0.31, radius 0.35. Door z 0.68 to -0.63, y 0.24 to
the side glass's foot at 0.90. From above: roof z -0.91 to 0.30, bonnet ahead of z 0.90, boot lid
behind z -1.68. Rear: boot lid y 0.64 to 0.95, the panel between the lamps at y 0.50.
"""
import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
VIEWS = HERE / "out" / "views"
sys.path.insert(0, str(HERE.parent / "common"))
import livery  # noqa: E402
from livery import Livery  # noqa: E402

livery.FONTS.update({"Shoulders": HERE / "font" / "BigShouldersDisplay-Black.ttf"})

OXBLOOD = "#5e1d18"
SOOT = "#120c0b"           # the town, and the sills
CREAM = "#efe6d2"
EMBER = "#ff7a1a"
GOLD = "#ffcc3d"
STEEL = "#8f969d"          # the maker's plate
SMOKE = "#2a1512"

SILL_Y = 0.20              # solid soot below this, all round
NAME = (0.0, 0.53, 0.22)   # STEELWORKS on the doors: z, y, cap height
NUMBER = (-0.12, 0.775, 0.42, 0.2)  # z, y, w, h: on the door, under the side glass
# An invented address: .za takes no new names of its own (only under co.za, org.za and the like), so
# it can't be a real site.
URL = "steelworks.za"
URL_Y = 0.56              # tail: centre of the website, clear of the diffuser below 0.46
FLAG = (-1.18, 0.80, 0.13)  # the South African flag on each rear quarter, above the arch: z, y, width


# --- the town ------------------------------------------------------------------------------------------
# Each building as (kind, width, height above the ground line), in metres, laid from the nose back.
# The doors keep to low terraces and sheds, so the name stands clear over them.
SIDE_TOWN = [("shed", 0.20, 0.03), ("house", 0.14, 0.08), ("headframe", 0.30, 0.40), ("twin", 0.35, 0.34),
             ("saw", 0.36, 0.13), ("house", 0.14, 0.07), ("house", 0.15, 0.10), ("house", 0.13, 0.08),
             ("saw", 0.44, 0.12), ("house", 0.14, 0.09), ("house", 0.15, 0.07), ("house", 0.14, 0.10),
             ("furnace", 0.44, 0.56), ("stack", 0.14, 0.66), ("tank", 0.24, 0.24), ("shed", 0.16, 0.12),
             ("crane", 0.36, 0.50), ("shed", 0.63, 0.0)]
# The nose and tail corners curve hard round into the end views, where a building would smear, so
# the town stays low there and the bumpers carry only the sky and the sill.


def building(kind, z, w, h, base):
    """One building standing on y=base, its front at z, running back w. Returns its polygons (in (z, y))
    and the tops of its smokestacks."""
    b, e = base, z - w
    if kind == "house":
        return [[(z, b), (z, b + h), (z - w / 2, b + h + 0.05), (e, b + h), (e, b)],
                [(z - w * 0.7, b + h), (z - w * 0.7, b + h + 0.06), (z - w * 0.82, b + h + 0.06), (z - w * 0.82, b + h)]], []
    if kind == "shed":
        return [[(z, b), (z, b + h), (e, b + h), (e, b)]], []
    if kind == "saw":  # a factory's sawtooth roof
        n, pts = 3, [(z, b)]
        for i in range(n):
            z0 = z - w * i / n
            pts += [(z0, b + h * 0.6), (z0, b + h), (z0 - w / n, b + h * 0.6)]
        return [pts + [(e, b)]], []
    if kind == "stack":
        return [[(z, b), (z - w * 0.18, b + h), (e + w * 0.18, b + h), (e, b)]], [(z - w / 2, b + h)]
    if kind == "twin":
        s = w * 0.36
        return ([[(z, b), (z, b + h * 0.3), (e, b + h * 0.3), (e, b)],
                 [(z - 0.02, b), (z - 0.03, b + h), (z - 0.03 - s * 0.7, b + h), (z - 0.02 - s, b)],
                 [(e + 0.02 + s, b), (e + 0.03 + s * 0.7, b + h * 0.85), (e + 0.03, b + h * 0.85), (e + 0.02, b)]],
                [(z - 0.03 - s * 0.35, b + h), (e + 0.03 + s * 0.35, b + h * 0.85)])
    if kind == "furnace":  # a blast furnace: a stout tower with its stove and a pipe over
        return ([[(z, b), (z, b + h * 0.35), (e, b + h * 0.35), (e, b)],
                 [(z - w * 0.3, b), (z - w * 0.34, b + h * 0.8), (z - w * 0.4, b + h * 0.86), (z - w * 0.4, b + h),
                  (z - w * 0.6, b + h), (z - w * 0.6, b + h * 0.86), (z - w * 0.66, b + h * 0.8), (z - w * 0.7, b)],
                 [(z - w * 0.05, b), (z - w * 0.05, b + h * 0.6), (z - w * 0.05 - 0.02, b + h * 0.68), (z - w * 0.22, b + h * 0.68),
                  (z - w * 0.24, b + h * 0.6), (z - w * 0.24, b)],
                 [(z - w * 0.15, b + h * 0.66), (z - w * 0.15, b + h * 0.92), (z - w * 0.45, b + h * 0.92),
                  (z - w * 0.45, b + h * 0.88), (z - w * 0.19, b + h * 0.88), (z - w * 0.19, b + h * 0.66)]],
                [(z - w / 2, b + h)])
    if kind == "tank":
        return [[(z, b), (z, b + h * 0.8), (z - w * 0.2, b + h), (e + w * 0.2, b + h), (e, b + h * 0.8), (e, b)]], []
    if kind == "headframe":  # a mine's winding tower: two legs, the wheel at the top
        t = 0.025
        return [[(z, b), (z - w * 0.45, b + h), (z - w * 0.45 - t * 1.5, b + h), (z - t * 1.5, b)],
                [(e, b), (z - w * 0.55, b + h), (z - w * 0.55 + t * 1.5, b + h), (e + t * 1.5, b)],
                [(z - w * 0.35, b + h), (z - w * 0.35, b + h + 0.03), (z - w * 0.65, b + h + 0.03), (z - w * 0.65, b + h)],
                [(z - w * 0.3, b + h * 0.5), (z - w * 0.7, b + h * 0.5), (z - w * 0.7, b + h * 0.5 + t), (z - w * 0.3, b + h * 0.5 + t)],
                [(z - w * 0.5 + 0.045 * math.cos(a / 8 * 6.2832), b + h + 0.06 + 0.045 * math.sin(a / 8 * 6.2832))
                 for a in range(8)]], []
    if kind == "crane":  # a tower crane, jib pointing at the nose
        return [[(z - w * 0.7, b), (z - w * 0.7, b + h), (z - w * 0.76, b + h), (z - w * 0.76, b)],
                [(z, b + h * 0.9), (z, b + h * 0.96), (e, b + h * 0.96), (e, b + h * 0.9)],
                [(e, b + h * 0.96), (e + 0.06, b + h * 0.96), (e + 0.06, b + h * 0.82), (e, b + h * 0.82)],
                [(z - 0.03, b + h * 0.9), (z - 0.035, b + h * 0.7), (z - 0.025, b + h * 0.7)],
                [(z - 0.06, b + h * 0.7), (z, b + h * 0.7), (z, b + h * 0.64), (z - 0.06, b + h * 0.64)]], []
    raise ValueError(kind)


def town(z0, row, base):
    polys, stacks, z = [], [], z0
    for kind, w, h in row:
        p, s = building(kind, z, w, h, base)
        polys += p
        stacks += s
        z -= w
    return polys, stacks, z


def side_town(lv, ink, smoke_ink):
    base = SILL_Y + 0.04
    polys, stacks, _ = town(2.3, SIDE_TOWN, base)
    lv.side_poly([(2.4, base + 0.01), (-2.4, base + 0.01), (-2.4, -0.2), (2.4, -0.2)], ink)
    for p in polys:
        lv.side_poly(p, ink)
    for z, y in stacks:
        for i in range(6):  # the smoke drifts back as the car goes
            for view in ("left", "right"):
                u, v = lv.uv(view, Livery._side(view, z - 0.05 - i * 0.075, y + 0.03 + i * 0.014))
                r = 0.028 + i * 0.011
                lv.raw(view, f'<ellipse cx="{u:.4f}" cy="{v:.4f}" rx="{r * 1.5:.4f}" ry="{r:.4f}" fill="{smoke_ink}" '
                             f'opacity="{0.5 - i * 0.07:.2f}"/>')


def sky(lv, view, top, stops):
    """A vertical glow from y=top (clear) down to the sill, on one view."""
    f = lv.views[view]
    gid = f"sky_{view}"
    v0, v1 = -top, -SILL_Y
    st = "".join(f'<stop offset="{o}" stop-color="{c}" stop-opacity="{a}"/>' for o, c, a in stops)
    lv.raw(view, f'<defs><linearGradient id="{gid}" gradientUnits="userSpaceOnUse" x1="0" y1="{v0}" x2="0" y2="{v1}">'
                 f'{st}</linearGradient></defs><rect x="{f["u0"]}" y="{v0}" width="{f["u1"] - f["u0"]}" '
                 f'height="{v1 - v0}" fill="url(#{gid})"/>')


def sill(lv, ink):
    for view in ("left", "right", "front", "rear"):
        f = lv.views[view]
        lv.raw(view, f'<rect x="{f["u0"]}" y="{-SILL_Y}" width="{f["u1"] - f["u0"]}" height="{SILL_Y + 0.1}" fill="{ink}"/>')


# --- marks -----------------------------------------------------------------------------------------------
def plate_svg():
    """The team's logo: a riveted oval maker's plate."""
    rivets = "".join(f'<circle cx="{cx}" cy="{cy}" r="5" fill="{SOOT}"/>' for cx, cy in ((20, 100), (320, 100), (170, 13), (170, 187)))
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 340 200">'
            f'<ellipse cx="170" cy="100" rx="167" ry="97" fill="{STEEL}" stroke="{SOOT}" stroke-width="6"/>'
            f'<ellipse cx="170" cy="100" rx="147" ry="80" fill="none" stroke="{SOOT}" stroke-width="3"/>{rivets}'
            f'<text x="170" y="112" font-family="Shoulders" font-size="54" letter-spacing="2" fill="{SOOT}" '
            f'text-anchor="middle">STEELWORKS</text>'
            f'<text x="170" y="146" font-family="Shoulders" font-size="20" letter-spacing="8" fill="#9b3a22" '
            f'text-anchor="middle">MOTORSPORT</text></svg>')


def flag_svg():
    """South Africa's flag, 3:2."""
    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 90 60">'
            '<rect width="90" height="30" fill="#de3831"/><rect y="30" width="90" height="30" fill="#002395"/>'
            '<path d="M0 0 L45 30 L0 60 Z" fill="#fff"/><path d="M0 0 H18 L48 20 H90 V40 H48 L18 60 H0 Z" fill="#fff"/>'
            '<path d="M0 6 L36 30 L0 54 Z M0 0 H8 L46 24 H90 V36 H46 L8 60 H0 Z" fill="#007a4d"/>'
            '<path d="M0 12 L27 30 L0 48 Z" fill="#ffb612"/><path d="M0 18 L18 30 L0 42 Z" fill="#000"/></svg>')


def write_marks():
    out = HERE / "out" / "marks"
    out.mkdir(parents=True, exist_ok=True)
    (out / "plate.svg").write_text(plate_svg())
    (out / "flag.svg").write_text(flag_svg())
    return out


def name(lv, view, p, h, fill, shadow, text="STEELWORKS"):
    """Lettering in Big Shoulders, with a hard drop shadow down and back."""
    u, v = lv.uv(view, p)
    d = h * 0.06
    back = -d if view == "right" else d
    for du, dv, ink in ((back, d, shadow), (0, 0, fill)):
        lv.raw(view, f'<text x="{u + du:.4f}" y="{v + dv:.4f}" font-family="Shoulders" font-size="{h / 0.72:.4f}" '
                     f'letter-spacing="{0.03 * h:.4f}" fill="{ink}" text-anchor="middle" dominant-baseline="central">{text}</text>')


# --- the two takes -----------------------------------------------------------------------------------
def steel_town():
    lv = Livery(VIEWS / "views.json", OXBLOOD)
    marks = write_marks()
    for view in ("left", "right", "front", "rear"):
        sky(lv, view, 0.68, [(0, EMBER, 0), (0.5, EMBER, 1), (1, GOLD, 1)])
    side_town(lv, SOOT, SMOKE)
    sill(lv, SOOT)
    z, y, h = NAME
    for view in ("left", "right"):
        name(lv, view, Livery._side(view, z, y), h, CREAM, SOOT)
        lv.mark(view, marks / "flag.svg", Livery._side(view, FLAG[0], FLAG[1]), FLAG[2] * 2 / 3)
    lv.side_number(NUMBER[0], NUMBER[1], NUMBER[2], NUMBER[3], CREAM, edge=SOOT)
    # Bonnet: the maker's plate, read from the nose. Roof: the number, read from the nose.
    lv.mark("top", marks / "plate.svg", (0, 0, 1.55), 0.56, rotate=-90)
    lv.number("top", (0, 0, -0.32), 0.62, 0.36, CREAM, rotate=-90, edge=SOOT)
    # Tail: the team's website between the lamps, above the bumper's lower edge.
    name(lv, "rear", (0, URL_Y, 0), 0.075, CREAM, SOOT, URL)
    return lv


if __name__ == "__main__":
    steel_town().write(HERE / "out" / "steeltown")
