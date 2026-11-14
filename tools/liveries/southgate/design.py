"""Southgate Motorsport on the Seorak Hwaeum: a suburban Kenyan team in gloss black and gold, a
gold savanna horizon with acacias and a setting sun. Independent, so no maker's logo. Writes one
folder of panels per option under out/, and the team's logo under out/logos/.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/SeorakHwaeum/SeorakHwaeum.obj out/views
     node ../common/digits.js --font fonts/Cinzel-Bold.ttf --out out/digits
     python3 bonnet.py
     python3 design.py
     ./game.sh

Side art is given as (z, y) in the car's metres: z runs nose (+2.26) to tail (-2.06), y up from the
ground. Landmarks on the Hwaeum: wheels at z +-1.325 (radius 0.34, arch tops at y 0.70); front
door z 0.88 to -0.23, rear door -0.23 to -1.16, sill y 0.29 to the window line at 0.96 (front)
rising to 1.03 (rear); bonnet z 1.06 to the nose; roof z -1.41 to 0.26, x within 0.5.
Rear: tailgate y 0.66 to 1.19 with the maker's badge and script at y 0.98, bumper below.
"""
import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "common"))
import livery  # noqa: E402
from livery import Livery  # noqa: E402

livery.FONTS["Cinzel"] = HERE / "fonts" / "Cinzel-Bold.ttf"
livery.FONTS["Barlow"] = HERE / "fonts" / "BarlowCondensed-Bold.ttf"
LOGOS = HERE / "out" / "logos"

BLACK = "#0b0b0c"      # gloss black body
GOLD = "#c9a24a"       # lines, trees, MOTORSPORT
GOLD_L = "#e8c872"     # SOUTHGATE, the sun, numbers
GOLD_D = "#8f6f2a"     # the sun's rays, the dusk in the logo
DUSK = "#16140f"       # the logo's sky

SIDES = ("left", "right")
HORIZON = 0.72                       # just over the arch tops, so it runs unbroken nose to tail
NOSE, TAIL = 2.30, -2.10


def side_pt(view, z, y):
    return Livery._side(view, z, y)


def line(lv, z0, y0, z1, y1, w, fill):
    """A straight band along both sides from (z0, y0) to (z1, y1), w metres tall."""
    lv.side_poly([(z0, y0 - w / 2), (z1, y1 - w / 2), (z1, y1 + w / 2), (z0, y0 + w / 2)], fill)


def acacia(z, base, h):
    """A flat-topped acacia as (z, y) polygons: trunk, three limbs, two canopy layers."""
    def limb(z0, y0, z1, y1, w):
        dz, dy = z1 - z0, y1 - y0
        n = math.hypot(dz, dy)
        oz, oy = -dy / n * w / 2, dz / n * w / 2
        return [(z0 + oz, y0 + oy), (z1 + oz, y1 + oy), (z1 - oz, y1 - oy), (z0 - oz, y0 - oy)]

    def ellipse(cz, cy, rz, ry):
        return [(cz + rz * math.cos(a * math.pi / 16), cy + ry * math.sin(a * math.pi / 16)) for a in range(32)]

    fork = (z - h * 0.04, base + h * 0.55)
    out = [limb(z, base, *fork, h * 0.06)]
    for dz, dy in ((0.42, 0.86), (-0.38, 0.88), (-0.1, 0.9)):
        out.append(limb(*fork, z + dz * h, base + dy * h, h * 0.035))
    out.append(ellipse(z, base + h * 0.93, h * 0.72, h * 0.1))
    out.append(ellipse(z - h * 0.25, base + h * 1.0, h * 0.4, h * 0.07))
    return out


def trees(lv, spots, fill, base=HORIZON):
    for z, h in spots:
        for part in acacia(z, base, h):
            lv.side_poly(part, fill)


def flat_trees(lv, view, spots, base, fill):
    """Acacias across the front or rear view, x across the car."""
    for x, h in spots:
        for part in acacia(x, base, h):
            lv.poly(view, [(px, py, 0) for px, py in part], fill)


def sun(lv, z, y, r, rays):
    """A setting sun on the horizon at z, and its rays, both sides."""
    r0, r1 = rays
    for i in range(13):
        a = math.pi * (i + 0.5) / 13
        w = 0.035
        pts = [(z + math.cos(a - w) * r0, y + math.sin(a - w) * r0), (z + math.cos(a - w * 0.6) * r1, y + math.sin(a - w * 0.6) * r1),
               (z + math.cos(a + w * 0.6) * r1, y + math.sin(a + w * 0.6) * r1), (z + math.cos(a + w) * r0, y + math.sin(a + w) * r0)]
        lv.side_poly(pts, GOLD_D)
    lv.side_poly([(z + r * math.cos(math.pi * k / 48), y + r * math.sin(math.pi * k / 48)) for k in range(49)], GOLD_L)


def wordmark(lv, z, y, h):
    lv.side_text("SOUTHGATE", z, y, h, GOLD_L, spacing=0.12, font="Cinzel")
    lv.side_text("MOTORSPORT", z, y - h * 0.95, h * 0.36, GOLD, spacing=0.6, font="Barlow")


def logo():
    """Logo 5: an acacia roundel at sunset in a gold ring, SOUTHGATE over MOTORSPORT under it."""
    def tree(x, base, h):
        limbs = "".join(f'<line x1="{x + h * 0.04}" y1="{base - h * 0.55}" x2="{x + dx * h}" y2="{base + dy * h}" stroke-width="{h * 0.035}"/>'
                        for dx, dy in ((-0.42, -0.86), (0.38, -0.88), (0.1, -0.9)))
        return (f'<g stroke="{GOLD}" stroke-linecap="round" fill="{GOLD}"><line x1="{x}" y1="{base}" x2="{x + h * 0.04}" y2="{base - h * 0.55}" stroke-width="{h * 0.06}"/>'
                f'{limbs}<ellipse cx="{x}" cy="{base - h * 0.93}" rx="{h * 0.72}" ry="{h * 0.1}" stroke="none"/>'
                f'<ellipse cx="{x + h * 0.25}" cy="{base - h}" rx="{h * 0.4}" ry="{h * 0.07}" stroke="none"/></g>')

    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 212">'
            '<clipPath id="sg-ring"><circle cx="100" cy="82" r="72"/></clipPath>'
            f'<circle cx="100" cy="82" r="72" fill="{DUSK}"/><g clip-path="url(#sg-ring)">'
            f'<circle cx="100" cy="108" r="44" fill="{GOLD_D}"/><rect x="20" y="108" width="160" height="60" fill="{BLACK}"/>'
            f'{tree(80, 108, 58)}{tree(136, 108, 36)}</g>'
            f'<circle cx="100" cy="82" r="72" fill="none" stroke="{GOLD}" stroke-width="3.5"/>'
            f'<rect x="28" y="106.5" width="144" height="3" fill="{GOLD}"/>'
            f'<text x="100" y="176" text-anchor="middle" dominant-baseline="central" font-family="Cinzel" font-size="22" '
            f'letter-spacing="2" fill="{GOLD_L}">SOUTHGATE</text>'
            f'<text x="100" y="200" text-anchor="middle" dominant-baseline="central" font-family="Barlow" font-size="13" '
            f'letter-spacing="6" fill="{GOLD}">MOTORSPORT</text></svg>')


def common(lv):
    """What both takes share: the gold pinstripe under the windows, the sill line, the horizon
    across the front and rear, the logo on the roof."""
    line(lv, NOSE, 0.868, TAIL, 0.990, 0.010, GOLD_L)
    line(lv, NOSE, 0.30, TAIL, 0.30, 0.012, GOLD)
    lv.band_x(-1.1, 1.1, GOLD, views=("front",), y=(0.493, 0.507))
    lv.band_x(-1.1, 1.1, GOLD, views=("rear",), y=(0.613, 0.627))
    flat_trees(lv, "rear", [(0.66, 0.15), (-0.7, 0.11)], 0.62, GOLD)
    lv.text("rear", "SOUTHGATE", (0, 0.79, 0), 0.07, GOLD_L, spacing=0.12, font="Cinzel")
    for s in (1, -1):
        lv.poly("top", [(s * 0.46, 0, -1.38), (s * 0.48, 0, -1.38), (s * 0.48, 0, 0.22), (s * 0.46, 0, 0.22)], GOLD)
    lv.mark("top", LOGOS / "logo.svg", (0, 0, -0.58), 0.78, rotate=-90)


def dusk_option(views):
    """Savanna as shown: a gold horizon nose to tail, acacias on it, the sun setting on the rear
    door with the race number in it, SOUTHGATE on the front door below the horizon."""
    lv = Livery(views, BLACK)
    sun(lv, -0.70, HORIZON, 0.25, (0.29, 0.35))
    line(lv, NOSE, HORIZON, TAIL, HORIZON, 0.014, GOLD)
    trees(lv, [(1.02, 0.14), (-0.2, 0.16), (-1.3, 0.19), (-1.78, 0.13)], GOLD)
    wordmark(lv, 0.33, 0.53, 0.11)
    common(lv)
    lv.side_number(-0.70, 0.83, 0.30, 0.15, BLACK)
    return lv


def badge_option(views):
    """Savanna with logo 5 as a door badge: the roundel on the front door over the horizon, a
    longer row of acacias along the rear, the race number on the rear door."""
    lv = Livery(views, BLACK)
    line(lv, NOSE, HORIZON, TAIL, HORIZON, 0.014, GOLD)
    trees(lv, [(1.05, 0.13), (-0.28, 0.12), (-1.25, 0.18), (-1.55, 0.12), (-1.85, 0.15)], GOLD)
    for view in SIDES:
        lv.mark(view, LOGOS / "logo.svg", side_pt(view, 0.33, 0.6), 0.58)
    common(lv)
    lv.side_number(-0.70, 0.52, 0.42, 0.22, GOLD_L)
    return lv


# --- bonnet centrepieces -------------------------------------------------------------------
# Each piece is one illustration from bonnet.py, 1.0 m across by 0.8 m along the bonnet,
# upright for someone at the nose.
BONNET = HERE / "out" / "bonnet"
BONNETS = ("lion", "elephant", "crane", "giraffes")


def bonnet(lv, name):
    lv.mark("top", BONNET / f"{name}.svg", (0, 0, 1.56), 0.8, rotate=-90)


OPTIONS = {"dusk": dusk_option, "badge": badge_option}

if __name__ == "__main__":
    LOGOS.mkdir(parents=True, exist_ok=True)
    (LOGOS / "logo.svg").write_text(logo())
    views = HERE / "out" / "views" / "views.json"
    for name, make in OPTIONS.items():
        make(views).write(HERE / "out" / name)
    # Ivan's pick: Dusk with the elephant on the bonnet. game.sh bakes it into the game.
    lv = dusk_option(views)
    bonnet(lv, "elephant")
    lv.write(HERE / "out" / "savanna")
    # The Dusk take with each bonnet centrepiece, for Ivan to pick from.
    for name in BONNETS:
        lv = dusk_option(views)
        bonnet(lv, name)
        lv.write(HERE / "out" / f"dusk-{name}")
