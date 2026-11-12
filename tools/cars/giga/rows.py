"""Traced rows for the Tora Giga, based on the Nissan GT-R (R35) coupe.

Reference: Ivan's 3D model of a 2014 GT-R (a .glb, kept out of the repo). It is rendered
orthographically from the side, front, rear and top on a plain background at 20 pixels per model
unit, and every row below is picked off those renders like a drawing. Nothing is taken from its
meshes. Points are written in the model's units read off the renders' grid:
  Y along the car (front negative), Z up, U out to the side from the centre line (half widths).
Side render: Y = -60.58 + px / 20, Z = 54.26 - py / 20. Front, rear and top renders share the scale.

Scale: the wheel centres, 102.70 units apart, set the scale from the published 2780 mm wheelbase.
The model then measures the published 1895 mm wide; its body is 4634 mm long against the
published 4670 mm, so both overhangs stretch by the same factor to the published length.
The ground sits one published tyre radius (255/40 R20 front, 285/35 R20 rear) under the wheel centres.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py  -> rows.json
"""
import json
from pathlib import Path

import numpy as np
from scipy.interpolate import PchipInterpolator

WHEELBASE = 2.780
LENGTH = 4.670
TYRE_F = round((20 * 25.4 + 2 * 0.40 * 255) / 2000, 4)    # 255/40 R20
TYRE_RR = round((20 * 25.4 + 2 * 0.35 * 285) / 2000, 4)   # 285/35 R20
TYRE_R = round((TYRE_F + TYRE_RR) / 2, 4)
RIM_R = round(20 * 25.4 / 2000, 4)
Y_FRONT, Y_REAR, Z_WHEEL = -20.83, 81.87, 15.146          # wheel centres on the side render
Y_NOSE, Y_TAIL = -53.8, 117.4                             # the body's ends on the side render
S = WHEELBASE / (Y_REAR - Y_FRONT)
K = (LENGTH - WHEELBASE) / (((Y_FRONT - Y_NOSE) + (Y_TAIL - Y_REAR)) * S)
X_F, X_R = WHEELBASE / 2, -WHEELBASE / 2


def X(y):
    """Build x of a render Y: the wheelbase at scale, the overhangs stretched by K."""
    y = float(y)
    if y < Y_FRONT:
        return round(X_F + (Y_FRONT - y) * S * K, 4)
    if y > Y_REAR:
        return round(X_R - (y - Y_REAR) * S * K, 4)
    return round(X_F - (y - Y_FRONT) * S, 4)


def Z(z):
    return round(float((z - Z_WHEEL) * S + TYRE_R), 4)


def H(w):
    return round(float(w * S), 4)


def side(pts):
    return [[X(y), Z(z)] for y, z in pts]


def end(pts):
    """Front or rear view points (half width, Z), the car's left half."""
    return [[H(w), Z(z)] for w, z in pts]


def plan(pts):
    return [[X(y), H(w)] for y, w in pts]


# ---------------------------------------------------------------- body rows (side and top renders)
# Over the bonnet the side render's top edge is the wing crest; the bonnet's middle is CENTRE.
CREST = [(-53.8, 19.0), (-53.5, 21.0), (-52.8, 22.6), (-52.0, 24.6), (-51.0, 26.2), (-50.0, 27.05),
         (-48.0, 28.55), (-46.0, 29.5), (-44.0, 30.35), (-40.0, 31.8), (-36.0, 32.95), (-30.0, 34.45),
         (-24.0, 35.5), (-20.0, 36.0), (-16.0, 36.3), (-12.0, 36.45), (-8.0, 36.55), (-5.0, 36.6)]
CENTRE = [(-53.8, 18.6), (-53.5, 20.6), (-52.8, 22.2), (-52.0, 24.0), (-51.0, 25.4), (-50.0, 26.1),
          (-48.0, 27.4), (-46.0, 28.5), (-44.0, 29.4), (-40.0, 31.0), (-36.0, 32.3), (-30.0, 34.0),
          (-24.0, 35.3), (-20.0, 36.1), (-16.0, 36.7), (-12.0, 37.15), (-8.0, 37.55), (-5.0, 37.65)]
# Roof and tail along the middle: the windscreen, roof, rear glass, the short deck under the
# wing and the tail's rounded top.
TOP = [(-5.0, 37.65), (-2.0, 37.9), (0.0, 38.25), (2.0, 39.35), (4.0, 40.45), (6.0, 41.5), (8.0, 42.55),
       (10.0, 43.6), (12.0, 44.65), (14.0, 45.65), (16.0, 46.65), (18.0, 47.6), (20.0, 48.5), (22.0, 49.35),
       (24.0, 50.1), (26.0, 50.65), (28.0, 51.1), (30.0, 51.45), (32.0, 51.75), (34.0, 51.95), (36.0, 52.05),
       (40.0, 52.15), (44.0, 52.05), (48.0, 51.9), (52.0, 51.55), (56.0, 51.1), (60.0, 50.55), (64.0, 49.85),
       (68.0, 49.1), (72.0, 48.15), (76.0, 47.1), (80.0, 45.9), (84.0, 44.6), (88.0, 43.1), (92.0, 41.5),
       (95.0, 40.4), (100.7, 39.4), (106.2, 38.8), (110.1, 38.2), (112.9, 36.0), (115.1, 32.7),
       (116.5, 27.7), (117.05, 22.5), (117.4, 19.9)]
# Silhouette bottom: the splitter, the sills carried straight across the arches, the diffuser.
BOTTOM = [(-53.8, 10.5), (-53.4, 8.2), (-52.0, 7.4), (-35.0, 7.3), (-20.0, 7.0), (0.0, 6.8), (47.0, 6.8),
          (60.0, 6.9), (90.0, 7.0), (100.7, 7.7), (106.2, 9.9), (110.7, 12.1), (115.9, 14.9), (117.4, 17.0)]
# Half width in plan (top render), mirrors and tyres left out.
HALF = [(-53.8, 26.0), (-53.3, 26.82), (-52.5, 27.87), (-51.5, 29.05), (-50.0, 30.48), (-48.0, 31.75),
        (-46.0, 32.62), (-44.0, 33.22), (-42.0, 33.52), (-38.0, 33.78), (-34.0, 34.02), (-30.0, 34.22),
        (-14.0, 34.33), (-6.0, 34.22), (-2.0, 33.93), (2.0, 33.58), (6.0, 33.22), (10.0, 33.02),
        (18.0, 33.1), (26.0, 33.28), (34.0, 33.52), (42.0, 33.78), (50.0, 34.02), (58.0, 34.38),
        (66.0, 34.88), (74.0, 35.02), (90.0, 35.02), (94.0, 34.88), (98.0, 34.52), (102.0, 33.83),
        (106.0, 33.02), (110.0, 32.08), (111.0, 30.75), (112.0, 27.78), (113.0, 25.58), (114.0, 23.12),
        (115.0, 20.0), (116.0, 16.42), (116.5, 13.68), (117.0, 9.88), (117.4, 6.4)]
# Height of the widest point, read off the front and rear renders.
WIDEST = [(-53.8, 16.0), (-45.0, 18.0), (-30.0, 22.0), (-20.0, 24.0), (-5.0, 25.0), (20.0, 25.0),
          (45.0, 26.0), (70.0, 27.0), (90.0, 27.0), (105.0, 25.0), (117.4, 22.0)]
# How far the sill or bumper bottom tucks in from the widest point (front and rear renders).
TUCK = [(-53.8, 1.5), (-40.0, 1.2), (-20.0, 1.0), (10.0, 1.6), (40.0, 1.6), (70.0, 1.2), (100.0, 1.2),
        (117.4, 1.5)]
# Shoulder along the cabin and the rear haunch (z), and how far it sits in from the widest point.
# Over the bonnet the shoulder is the wing crest.
SHOULDER = [(-5.0, 36.6), (10.0, 36.4), (30.0, 36.8), (50.0, 38.0), (60.0, 38.6), (70.0, 39.0),
            (80.0, 39.2), (90.0, 39.3), (100.0, 39.1), (108.0, 38.4), (112.0, 36.5), (115.0, 32.5),
            (117.4, 25.0)]
SHOULDER_IN = [(-53.8, 6.0), (-48.0, 5.0), (-40.0, 4.6), (-20.0, 4.3), (-5.0, 3.6), (5.0, 1.6),
               (40.0, 1.4), (60.0, 1.8), (75.0, 3.2), (90.0, 4.0), (105.0, 4.2), (112.0, 4.6), (117.4, 5.0)]
BONNET_END = -5.0         # the bonnet meets the windscreen's foot
# Deck height at the centre under the greenhouse (hidden), joined to the cowl and the boot.
DECK = [(-5.0, 37.65), (-3.0, 37.6), (93.0, 40.6), (95.0, 40.4)]

# Greenhouse, from the windscreen's foot to the rear glass's foot.
GH = (-5.0, 95.0)
# Glass foot in plan along the cabin (top render), and the roof edge in side (Y, Z) and plan (Y, U):
# the A-pillar, the roof's side and the C-pillar.
GLASS_FOOT = [(-5.0, 27.0), (0.0, 28.0), (10.0, 29.0), (30.0, 29.2), (50.0, 29.3), (65.0, 29.0),
              (75.0, 28.4), (85.0, 27.0), (95.0, 24.0)]
ROOF_Z = [(-5.0, 37.4), (0.0, 37.6), (5.0, 39.6), (10.0, 41.6), (15.0, 43.8), (20.0, 46.0), (25.0, 48.0),
          (30.0, 49.6), (35.0, 50.4), (40.0, 50.6), (50.0, 50.3), (56.0, 49.8), (62.0, 48.9), (66.0, 48.0),
          (72.0, 46.5), (78.0, 45.0), (84.0, 43.4), (90.0, 41.6), (95.0, 40.3)]
ROOF_W = [(-5.0, 26.6), (0.0, 26.5), (10.0, 24.4), (20.0, 21.6), (28.0, 19.6), (40.0, 18.4), (55.0, 18.4),
          (63.0, 18.6), (75.0, 19.5), (85.0, 20.4), (95.0, 21.6)]


def table(rows):
    a = np.array(rows, float)
    return PchipInterpolator(a[:, 0], a[:, 1], extrapolate=True)


def stations():
    """Each station's section values in metres, at every Y any row was picked at."""
    t = {k: table(v) for k, v in dict(crest=CREST, centre=CENTRE, top=TOP, bot=BOTTOM, half=HALF, zw=WIDEST,
                                       tuck=TUCK, zs=SHOULDER, sin=SHOULDER_IN, deck=DECK, rz=ROOF_Z,
                                       rw=ROOF_W, wg=GLASS_FOOT).items()}
    ys = sorted({round(float(y), 3) for rows in (CREST, TOP, BOTTOM, HALF) for y, _ in rows}
                | {round(float(y), 3) for y in np.arange(-53.0, 117.0, 1.5)})
    ys = [y for y in ys if Y_NOSE <= y <= Y_TAIL]
    out = []
    for y in ys:
        f = {k: float(v(y)) for k, v in t.items()}
        in_gh = GH[0] < y < GH[1]
        if y <= BONNET_END:
            zs, deck = f["crest"], f["centre"]           # the crest is the shoulder, the middle is the bonnet
            top = max(zs, deck)
        elif in_gh:
            top = f["top"]
            deck = min(top, f["deck"])
            zs = min(f["zs"], deck - 0.3)
        else:
            top = f["top"]
            deck = top                                    # the boot: the middle is the top
            zs = min(f["zs"], top - 0.3)
        st = dict(x=X(y), zb=Z(f["bot"]), w=H(f["half"]), zw=Z(f["zw"]), tuck=H(f["tuck"]),
                  zs=Z(zs), ws=H(f["half"] - f["sin"]), zd=Z(deck))
        if in_gh:
            st.update(zt=Z(top), zr=Z(min(f["rz"], top - 0.15)), wr=H(f["rw"]), wg=H(f["wg"]))
        out.append(st)
    return out


# ---------------------------------------------------------------- glass (side render)
def belt(y):
    """The belt line: nearly level along the door, rising to the quarter window."""
    return 36.35 + 0.012 * (y - 10.0) + 0.00045 * max(y - 10.0, 0.0) ** 2


# The side window opening (glass and its black frame): the door glass and the fixed quarter light
# behind it, whose tail kicks down to a point under the C-pillar.
DLO = [(10.0, belt(10.0)), (14.75, 39.5), (21.0, 43.7), (27.7, 47.6), (31.8, 48.75), (40.0, 48.55),
       (47.0, 48.0), (54.3, 47.2), (60.0, 45.9), (66.8, 44.3), (68.6, 43.0), (71.0, 39.9),
       (70.4, belt(70.4)), (60.0, belt(60.0)), (45.0, belt(45.0)), (30.0, belt(30.0)), (20.0, belt(20.0))]
B_PILLAR = ((51.9, 38.0), (54.6, 47.4), 0.8)     # the slanted division's foot, head and width

# Shut lines (side render), straight runs and plain arcs.
DOOR_BOTTOM = 10.2                                # the door's bottom edge, one straight line
FRONT_DOOR = [(9.6, belt(9.6) - 0.15), (6.6, 31.5), (6.7, 19.0), (9.0, DOOR_BOTTOM)]   # front edge
REAR_DOOR = [(51.95, 38.05), (54.8, 34.8), (44.4, DOOR_BOTTOM)]   # rear edge: down, then slanting to the sill
SHUT_ROUND = 1.2                                  # small corners rounded this much (units)
REAR_ROUND = 6.0                                  # the rear edge's bend, one wide arc
FRONT_ROUND = 6.0                                 # the front edge's two bends
# The door's rear edge meets the belt at the foot of the slanted division between the door glass
# and the quarter light, so the shut line runs up that division's middle.
# Windscreen (top render: Y, half width): its foot and head are even curves, its sides straight.
SCREEN = {"y_base_mid": -5.0, "y_base_edge": 1.6, "w_base": 26.6, "y_top_mid": 25.6, "y_top_edge": 28.6,
          "w_top": 19.4}
# Rear glass (top render): head and foot.
REAR_GLASS = {"y_top_mid": 66.0, "y_top_edge": 63.6, "w_top": 18.4, "y_base_mid": 95.0, "y_base_edge": 91.6,
              "w_base": 20.8}
# Bonnet shut line (top render: Y, half width): across the nose above the grille, back along the
# lamps' inner edge and the wings' inner edge, then in to the windscreen's foot inside its corners.
BONNET = [(-47.7, 0.0), (-47.45, 8.0), (-47.0, 14.0), (-46.5, 19.0), (-45.8, 21.4), (-36.0, 24.0),
          (-28.6, 27.4), (-24.0, 27.6), (-18.5, 27.9), (-11.0, 28.25), (-3.5, 28.45), (-0.5, 28.2),
          (1.2, 26.9), (1.0, 25.0)]

# ---------------------------------------------------------------- front (front render, left half)
HEAD = [(20.9, 26.0), (22.67, 28.5), (25.0, 30.83), (27.17, 32.0), (30.83, 32.4), (31.17, 32.0),
        (30.67, 29.33), (30.0, 26.67), (29.6, 25.17), (29.5, 24.0), (25.67, 23.83), (22.17, 23.93),
        (21.17, 24.83)]
# The upper grille: a slim band with its outer end cut on the slant.
GRILLE_UP = [(0.0, 25.1), (13.6, 25.1), (15.0, 23.9), (14.6, 23.4), (0.0, 23.4)]
# The main mouth under the body-coloured bar: straight sided, its top corners swept up.
GRILLE = [(0.0, 21.4), (12.0, 21.45), (14.5, 21.8), (16.6, 21.6), (17.9, 20.0), (18.1, 12.0), (17.6, 10.9),
          (16.5, 10.6), (0.0, 10.6)]
GRILLE_SLATS = [18.6, 16.0, 13.4]
# The daytime running lamp in the bumper's corner: a slim slot.
DRL = [(24.0, 15.4), (25.0, 15.9), (28.0, 16.5), (30.5, 16.65), (30.6, 16.1), (28.0, 15.4), (25.0, 15.0)]
SPLITTER = 10.3                                   # top of the black lip across the nose
SPLITTER_W = 32.0                                 # its half width, clear of the front arches

# ---------------------------------------------------------------- rear (rear render, left half)
TAIL_RINGS = [(25.4, 33.15, 3.6), (17.75, 32.4, 2.9)]     # two round lamps each side: centre and radius
# The boot lid's outline on the tail: straight across above the inner lamps, then up their
# inboard side to the deck, where it runs forward to the rear glass.
LID = {"w": 20.2, "z0": 33.3, "z_side": 36.2, "w_low": 15.0}
# The black lower bumper and diffuser, its top edge stepping down at the corners. Its foot stays
# above the tail's underside, so no ray runs on under the car into the rear wheels.
VALANCE = [(0.0, 17.0), (15.0, 16.8), (20.0, 17.15), (28.0, 17.1), (31.0, 16.6), (32.4, 15.4), (32.8, 13.0),
           (32.4, 11.0), (31.4, 10.4), (0.0, 10.4)]
EXHAUSTS = [(26.1, 13.4, 2.25), (21.1, 13.25, 2.25)]      # four round tips: two each side
EXHAUST_END = 116.4                                        # where the tips end (side render)
RAIN = (4.5, 11.25, 12.0)                                   # rain light: half width, bottom, top

# ---------------------------------------------------------------- other parts
MIRROR = {"y0": 15.6, "y1": 22.7, "w0": 30.4, "w1": 36.6, "z0": 36.4, "z1": 40.8}
# Vent on each front wing behind the arch (side render): a short bar on top and a long slanted slot.
GILL = [(-2.7, 29.6), (5.7, 29.9), (5.6, 28.9), (-0.2, 28.6), (6.4, 15.4), (5.9, 14.9), (-1.6, 27.6),
        (-2.9, 28.7)]
SKIRT = 9.8                                        # top of the black side skirt
# Rear wing on the deck (side, rear and top renders): blade chord, height and half span.
WING = {"y0": 98.5, "y1": 114.5, "z0": 40.4, "z1": 42.6, "span": 27.0, "post_w": 18.0}

data = {
    "name": "ToraGiga",
    "wheel": {"r": TYRE_R, "rim_r": RIM_R, "width_f": 0.255, "width_r": 0.285, "outer_y": H(34.3),
              "outer_y_r": H(34.9), "front_x": X(Y_FRONT), "rear_x": X(Y_REAR), "arch_r": round(TYRE_R + 0.03, 4)},
    "stations": stations(),
    "gh": [X(GH[1]), X(GH[0])],
    "dlo": side(DLO),
    "b_pillar": {"foot": side([B_PILLAR[0]])[0], "head": side([B_PILLAR[1]])[0], "w": H(B_PILLAR[2])},
    "front_door": side(FRONT_DOOR),
    "door_bottom": Z(DOOR_BOTTOM),
    "rear_door": side(REAR_DOOR),
    "shut_round": H(SHUT_ROUND),
    "rear_round": H(REAR_ROUND),
    "front_round": H(FRONT_ROUND),
    "screen": {"x_base_mid": X(SCREEN["y_base_mid"]), "x_base_edge": X(SCREEN["y_base_edge"]),
               "w_base": H(SCREEN["w_base"]), "x_top_mid": X(SCREEN["y_top_mid"]),
               "x_top_edge": X(SCREEN["y_top_edge"]), "w_top": H(SCREEN["w_top"])},
    "rear_glass": {"x_top_mid": X(REAR_GLASS["y_top_mid"]), "x_top_edge": X(REAR_GLASS["y_top_edge"]),
                   "w_top": H(REAR_GLASS["w_top"]), "x_base_mid": X(REAR_GLASS["y_base_mid"]),
                   "x_base_edge": X(REAR_GLASS["y_base_edge"]), "w_base": H(REAR_GLASS["w_base"])},
    "bonnet": plan(BONNET),
    "head": end(HEAD),
    "grille_up": end(GRILLE_UP),
    "grille": end(GRILLE),
    "grille_slats": [Z(z) for z in GRILLE_SLATS],
    "drl": end(DRL),
    "splitter": Z(SPLITTER),
    "splitter_w": H(SPLITTER_W),
    "tail_rings": [[H(u), Z(z), H(r)] for u, z, r in TAIL_RINGS],
    "lid": {"w": H(LID["w"]), "z0": Z(LID["z0"]), "z_side": Z(LID["z_side"]), "w_low": H(LID["w_low"]),
            "x_glass": X(REAR_GLASS["y_base_mid"])},
    "valance": end(VALANCE),
    "exhausts": [[H(u), Z(z), H(r)] for u, z, r in EXHAUSTS],
    "exhaust_x": X(EXHAUST_END),
    "rain": [H(RAIN[0]), Z(RAIN[1]), Z(RAIN[2])],
    "mirror": {"x_back": X(MIRROR["y1"]), "x_front": X(MIRROR["y0"]), "y_in": H(MIRROR["w0"]),
               "y_out": H(MIRROR["w1"]), "z0": Z(MIRROR["z0"]), "z1": Z(MIRROR["z1"])},
    "gill": side(GILL),
    "skirt": Z(SKIRT),
    "wing": {"x_front": X(WING["y0"]), "x_back": X(WING["y1"]), "z0": Z(WING["z0"]), "z1": Z(WING["z1"]),
             "span": H(WING["span"]), "post_w": H(WING["post_w"])},
    "picks": {"frontbadge": 2, "rearbadge": 2, "name": 1, "wing": 1},     # Ivan's picks, 2026-11-12
}
# belt line z(x) in build space, sampled for parts to interpolate
_ys = np.linspace(0.0, 75.0, 31)
data["belt"] = [[X(y), Z(belt(y))] for y in _ys]

if __name__ == "__main__":
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(data, indent=1))
    st = data["stations"]
    print("wrote", out, len(st), "stations; nose", max(s["x"] for s in st), "tail", min(s["x"] for s in st),
          "length", round(max(s["x"] for s in st) - min(s["x"] for s in st), 4),
          "roof", max(s.get("zt", 0) for s in st), "half", max(s["w"] for s in st), data["wheel"])
