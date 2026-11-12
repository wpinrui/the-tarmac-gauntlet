"""Traced rows for the Seorak Hoeori, based on the Subaru BRZ coupe.

Reference: Ivan's 3D model of a 2013 BRZ (a .glb, kept out of the repo). It is rendered
orthographically from the side, front, rear and top on a plain background at 500 pixels per
model unit, and every row below is picked off those renders like a drawing. Nothing is taken
from its meshes. Points are written in render units read off the renders' grid:
  Y along the car (front negative), Z up from the ground, X out to the side (half widths).
Side render: Y = -2.3 + px / 500, Z = 1.5 - py / 500. Front, rear and top renders share the scale.

Scale: the wheel centres, 2.572 units apart, set the scale from the published 2570 mm wheelbase.
The model then measures the published 4235 mm long and 1775 mm wide, so nothing is stretched.
The ground sits one published tyre radius (215/45 R17) under the wheel centres.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py  -> rows.json
"""
import json
from pathlib import Path

import numpy as np
from scipy.interpolate import PchipInterpolator

WHEELBASE = 2.570
TYRE_R = round((17 * 25.4 + 2 * 0.45 * 215) / 2000, 4)    # 215/45 R17
RIM_R = round(17 * 25.4 / 2000, 4)
Y_FRONT, Y_REAR, Z_WHEEL = -1.271, 1.301, 0.315          # wheel centres on the side render
S = WHEELBASE / (Y_REAR - Y_FRONT)
Y_MID = (Y_FRONT + Y_REAR) / 2


def X(y):
    return round(float(-(y - Y_MID) * S), 4)


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
# Silhouette top: the nose, the wing crests over the bonnet, windscreen, roof, rear glass, the
# short deck and the tail. Over the bonnet the side render shows the wing crests, not the middle.
TOP = [(-2.112, 0.50), (-2.106, 0.55), (-2.09, 0.578), (-2.06, 0.604), (-2.0, 0.630), (-1.9, 0.672),
       (-1.8, 0.706), (-1.7, 0.736), (-1.6, 0.764), (-1.5, 0.788), (-1.4, 0.808), (-1.3, 0.826),
       (-1.2, 0.842), (-1.1, 0.856), (-1.0, 0.866), (-0.90, 0.870), (-0.84, 0.872), (-0.78, 0.882),
       (-0.72, 0.912), (-0.6, 0.980), (-0.5, 1.038), (-0.4, 1.092), (-0.3, 1.140), (-0.2, 1.184),
       (-0.1, 1.218), (0.0, 1.238), (0.2, 1.256), (0.4, 1.260), (0.6, 1.250), (0.7, 1.242),
       (0.8, 1.228), (0.9, 1.207), (1.0, 1.184), (1.2, 1.128), (1.4, 1.066), (1.55, 1.015),
       (1.62, 0.990), (1.70, 0.966), (1.80, 0.950), (1.90, 0.938), (1.97, 0.932), (2.01, 0.925),
       (2.035, 0.900), (2.042, 0.860), (2.046, 0.800), (2.052, 0.700), (2.075, 0.640), (2.10, 0.590),
       (2.115, 0.520), (2.12, 0.450)]
# Silhouette bottom: the bumper lip, the sills carried straight across the arches, the diffuser.
BOTTOM = [(-2.112, 0.30), (-2.105, 0.22), (-2.09, 0.175), (-2.05, 0.155), (-1.9, 0.150), (-1.0, 0.148),
          (0.0, 0.150), (1.0, 0.152), (1.6, 0.165), (1.8, 0.178), (1.92, 0.205), (2.0, 0.225),
          (2.06, 0.250), (2.10, 0.300), (2.12, 0.400)]
# Half width in plan (top render), mirrors and tyres left out.
HALF = [(-2.112, 0.36), (-2.10, 0.42), (-2.08, 0.444), (-2.04, 0.506), (-2.0, 0.565), (-1.96, 0.620),
        (-1.92, 0.674), (-1.88, 0.725), (-1.84, 0.776), (-1.80, 0.818), (-1.76, 0.838), (-1.72, 0.849),
        (-1.64, 0.867), (-1.56, 0.880), (-1.48, 0.886), (-1.0, 0.886), (-0.9, 0.882), (-0.8, 0.877),
        (-0.4, 0.879), (0.0, 0.879), (0.3, 0.873), (0.6, 0.867), (0.8, 0.877), (1.0, 0.885),
        (1.2, 0.888), (1.5, 0.888), (1.6, 0.881), (1.68, 0.869), (1.76, 0.837), (1.84, 0.801),
        (1.92, 0.739), (2.0, 0.657), (2.04, 0.603), (2.08, 0.533), (2.105, 0.46), (2.12, 0.38)]
# Height of the widest point, read off the front and rear renders.
WIDEST = [(-2.112, 0.40), (-1.8, 0.45), (-1.0, 0.52), (0.0, 0.47), (1.0, 0.56), (1.7, 0.56), (2.12, 0.45)]
# How far the sill or bumper bottom tucks in from the widest point (front and rear renders).
TUCK = [(-2.112, 0.06), (-1.8, 0.05), (-1.0, 0.07), (0.0, 0.08), (1.0, 0.07), (1.8, 0.06), (2.12, 0.08)]
# Shoulder: the wing crest over the bonnet, the belt along the cabin, the haunch over the rear
# wheel (z), and how far it sits in from the widest point (front and rear renders).
SHOULDER = [(-2.112, 0.47), (-2.09, 0.55), (-2.0, 0.622), (-1.8, 0.700), (-1.6, 0.758), (-1.4, 0.802),
            (-1.2, 0.836), (-1.0, 0.860), (-0.88, 0.864), (-0.6, 0.868), (0.0, 0.880), (0.6, 0.893),
            (0.9, 0.902), (1.3, 0.905), (1.7, 0.900), (1.9, 0.892), (2.0, 0.884), (2.03, 0.862),
            (2.06, 0.66), (2.10, 0.57), (2.12, 0.44)]
SHOULDER_IN = [(-2.112, 0.13), (-2.0, 0.15), (-1.8, 0.16), (-1.4, 0.16), (-1.0, 0.14), (-0.88, 0.10),
               (-0.7, 0.085), (0.6, 0.085), (1.0, 0.07), (1.6, 0.065), (1.8, 0.08), (2.0, 0.10), (2.12, 0.10)]
# How far the bonnet's middle sits under the wing crests (front render).
DIP = [(-2.112, 0.0), (-2.0, 0.008), (-1.8, 0.022), (-1.5, 0.028), (-1.2, 0.022), (-1.0, 0.010), (-0.90, 0.0)]
BONNET_END = -0.90        # the bonnet's dip runs out at the cowl
# Deck height at the centre under the greenhouse (hidden), joined to the cowl and the boot.
DECK = [(-0.90, 0.870), (-0.80, 0.866), (1.55, 0.95), (1.68, 0.968)]

# Greenhouse, from the windscreen's foot to the rear glass's foot.
GH = (-0.90, 1.68)
# Glass foot in plan along the cabin (top render), and the roof edge in side (Y, Z) and plan (Y, X):
# the A-pillar, the roof's side and the C-pillar.
GLASS_FOOT = [(-0.90, 0.70), (-0.75, 0.755), (-0.5, 0.770), (0.0, 0.775), (0.6, 0.778), (1.2, 0.770),
              (1.68, 0.72)]
ROOF_Z = [(-0.90, 0.866), (-0.80, 0.870), (-0.65, 0.885), (-0.5, 0.955), (-0.4, 1.010), (-0.3, 1.068),
          (-0.2, 1.120), (-0.1, 1.166), (0.0, 1.196), (0.2, 1.212), (0.5, 1.212), (0.7, 1.204),
          (0.9, 1.172), (1.0, 1.154), (1.2, 1.102), (1.4, 1.042), (1.55, 0.996), (1.68, 0.962)]
ROOF_W = [(-0.90, 0.70), (-0.75, 0.665), (-0.5, 0.620), (-0.3, 0.578), (0.0, 0.562), (0.4, 0.560),
          (0.7, 0.566), (1.0, 0.586), (1.3, 0.622), (1.68, 0.690)]


def table(rows):
    a = np.array(rows, float)
    return PchipInterpolator(a[:, 0], a[:, 1], extrapolate=True)


def stations():
    """Each station's section values in metres, at every Y any row was picked at."""
    t = {k: table(v) for k, v in dict(top=TOP, bot=BOTTOM, half=HALF, zw=WIDEST, tuck=TUCK, zs=SHOULDER,
                                       sin=SHOULDER_IN, dip=DIP, deck=DECK, rz=ROOF_Z, rw=ROOF_W,
                                       wg=GLASS_FOOT).items()}
    ys = sorted({round(float(y), 3) for rows in (TOP, BOTTOM, HALF, SHOULDER) for y, _ in rows}
                | {round(float(y), 3) for y in np.arange(-2.08, 2.10, 0.04)})
    out = []
    for y in ys:
        f = {k: float(v(y)) for k, v in t.items()}
        top = f["top"]
        in_gh = GH[0] < y < GH[1]
        if y <= BONNET_END:
            zs, deck = top, top - max(f["dip"], 0.0)          # the crest is the top, the middle dips
        elif in_gh:
            deck = min(top, f["deck"])
            zs = min(f["zs"], deck - 0.012)
        else:
            deck = top                                         # the boot: the middle is the top
            zs = min(f["zs"], top - 0.012)
        st = dict(x=X(y), zb=Z(f["bot"]), w=H(f["half"]), zw=Z(f["zw"]), tuck=H(f["tuck"]),
                  zs=Z(zs), ws=H(f["half"] - f["sin"]), zd=Z(deck))
        if in_gh:
            st.update(zt=Z(top), zr=Z(min(f["rz"], top - 0.004)), wr=H(f["rw"]), wg=H(f["wg"]))
        out.append(st)
    return out


# ---------------------------------------------------------------- glass (side render)
def belt(y):
    return 0.872 + 0.0175 * (y + 0.6)


# The side window opening (glass and its black frame): the door glass and the quarter light
# behind it, whose foot kicks up to a point under the C-pillar.
DLO = [(-0.62, belt(-0.62)), (-0.5, 0.935), (-0.4, 0.995), (-0.3, 1.055), (-0.2, 1.110), (-0.1, 1.155),
       (0.0, 1.180), (0.2, 1.195), (0.4, 1.195), (0.5, 1.186), (0.6, 1.166), (0.75, 1.116), (0.9, 1.068),
       (1.0, 1.032), (1.05, 1.006), (1.062, 0.990), (1.0, 0.974), (0.9, 0.950), (0.75, 0.915),
       (0.66, belt(0.66)), (0.0, belt(0.0))]
B_PILLAR = (0.60, 0.66)          # its black, front and rear edge

# Shut lines (side render), straight runs and plain arcs.
DOOR_BOTTOM = 0.245                                   # the door's bottom edge, one straight line
FRONT_DOOR = [(-0.62, 0.862), (-0.665, DOOR_BOTTOM)]  # front edge: from the window's corner, straight down
REAR_DOOR = [(0.63, belt(0.63)), (0.685, 0.60), (0.40, DOOR_BOTTOM)]   # rear edge: down, then slanting to the sill
SHUT_ROUND = 0.03                                     # small corners rounded this much
REAR_ROUND = 0.30                                     # the rear edge's two bends, one wide arc each
# Windscreen (top render: Y, half width): foot and head, inside the pillars.
SCREEN = {"y_base": -0.86, "y_top": -0.30, "w_base": 0.66, "w_top": 0.50}
# Rear glass (top render): head and foot.
REAR_GLASS = {"y_top": 0.93, "y_base": 1.64, "w_top": 0.50, "w_base": 0.58}
# Bonnet shut line (top render, half widths): across the nose above the grille, back along the
# lamps' top edge and the wings' inner edge, then in to the windscreen's foot inside its corners.
BONNET = [(-1.965, 0.0), (-1.955, 0.20), (-1.925, 0.36), (-1.875, 0.47), (-1.80, 0.58), (-1.72, 0.66),
          (-1.55, 0.712), (-1.2, 0.735), (-0.98, 0.735), (-0.90, 0.69), (-0.88, 0.60)]

# ---------------------------------------------------------------- front (front render, left half)
HEAD = [(0.692, 0.535), (0.720, 0.539), (0.740, 0.549), (0.771, 0.586), (0.777, 0.618), (0.771, 0.652),
        (0.755, 0.680), (0.736, 0.697), (0.698, 0.702), (0.640, 0.684), (0.578, 0.663), (0.500, 0.636),
        (0.440, 0.613), (0.425, 0.594), (0.417, 0.570), (0.418, 0.557), (0.500, 0.559), (0.588, 0.561)]
# The grille mouth: a wide hexagon, flat top and bottom, its sides stepping in.
GRILLE = [(0.0, 0.503), (0.43, 0.500), (0.475, 0.488), (0.503, 0.446), (0.478, 0.388), (0.447, 0.362),
          (0.386, 0.258), (0.355, 0.233), (0.30, 0.224), (0.0, 0.222)]
GRILLE_BAR = 0.452                                   # the body-coloured bar across the mouth's top
GRILLE_SLATS = [0.345, 0.300, 0.265]
FOG = {"box": (0.565, 0.235, 0.782, 0.392), "lamp": (0.700, 0.300, 0.038)}
MARKER = {"w0": 0.79, "w1": 0.86, "z0": 0.54, "z1": 0.565}      # amber side marker at the bumper's corner

# ---------------------------------------------------------------- rear (rear render, left half)
TAIL = [(0.771, 0.746), (0.769, 0.788), (0.755, 0.824), (0.726, 0.843), (0.600, 0.843), (0.470, 0.839),
        (0.448, 0.833), (0.443, 0.818), (0.466, 0.784), (0.490, 0.745), (0.580, 0.724), (0.628, 0.690),
        (0.654, 0.679), (0.690, 0.677), (0.730, 0.689), (0.755, 0.710)]
LID = {"w": 0.445, "z0": 0.632, "r": 0.08, "y_glass": 1.66}
# The black lower bumper and diffuser, its top edge stepping down at the sides.
VALANCE = [(0.0, 0.463), (0.41, 0.463), (0.50, 0.457), (0.55, 0.431), (0.60, 0.354), (0.65, 0.225),
           (0.655, 0.185), (0.0, 0.185)]
EXHAUST = {"w": 0.40, "z": 0.300, "r": 0.046}
RAIN = [(0.0, 0.338), (0.045, 0.338), (0.0, 0.300)]    # the rain light: a small triangle in the diffuser

# ---------------------------------------------------------------- other parts
MIRROR = {"y0": -0.36, "y1": -0.19, "w0": 0.82, "w1": 0.975, "z0": 0.875, "z1": 0.99}
# Gill on the front wing behind the arch (side render), a slim slanted vent.
GILL = [(-0.985, 0.812), (-0.95, 0.828), (-0.73, 0.832), (-0.72, 0.818), (-0.74, 0.806), (-0.95, 0.796)]
# Rear wing on the boot (side and rear renders): blade chord, height and half span.
WING = {"y0": 1.72, "y1": 2.01, "z0": 0.950, "z1": 0.975, "span": 0.70, "post_w": 0.47}

data = {
    "name": "SeorakHoeori",
    "wheel": {"r": TYRE_R, "rim_r": RIM_R, "width": 0.215, "outer_y": H(0.885), "front_x": X(Y_FRONT),
              "rear_x": X(Y_REAR), "arch_r": round(TYRE_R + 0.042, 4)},
    "stations": stations(),
    "gh": [X(GH[1]), X(GH[0])],
    "dlo": side(DLO),
    "b_pillar": [X(B_PILLAR[1]), X(B_PILLAR[0])],
    "front_door": side(FRONT_DOOR),
    "door_bottom": Z(DOOR_BOTTOM),
    "rear_door": side(REAR_DOOR),
    "b_shut": X(sum(B_PILLAR) / 2),
    "shut_round": H(SHUT_ROUND),
    "rear_round": H(REAR_ROUND),
    "screen": {"x_base": X(SCREEN["y_base"]), "x_top": X(SCREEN["y_top"]), "w_base": H(SCREEN["w_base"]),
               "w_top": H(SCREEN["w_top"])},
    "rear_glass": {"x_top": X(REAR_GLASS["y_top"]), "x_base": X(REAR_GLASS["y_base"]),
                   "w_top": H(REAR_GLASS["w_top"]), "w_base": H(REAR_GLASS["w_base"])},
    "bonnet": plan(BONNET),
    "head": end(HEAD),
    "grille": end(GRILLE),
    "grille_bar": Z(GRILLE_BAR),
    "grille_slats": [Z(z) for z in GRILLE_SLATS],
    "fog": {"box": [H(FOG["box"][0]), Z(FOG["box"][1]), H(FOG["box"][2]), Z(FOG["box"][3])],
            "lamp": [H(FOG["lamp"][0]), Z(FOG["lamp"][1]), H(FOG["lamp"][2])]},
    "marker": {"w0": H(MARKER["w0"]), "w1": H(MARKER["w1"]), "z0": Z(MARKER["z0"]), "z1": Z(MARKER["z1"])},
    "tail": end(TAIL),
    "lid": {"w": H(LID["w"]), "z0": Z(LID["z0"]), "r": H(LID["r"]), "x_glass": X(LID["y_glass"])},
    "valance": end(VALANCE),
    "exhaust": {"w": H(EXHAUST["w"]), "z": Z(EXHAUST["z"]), "r": H(EXHAUST["r"])},
    "rain": end(RAIN),
    "mirror": {"x_back": X(MIRROR["y1"]), "x_front": X(MIRROR["y0"]), "y_in": H(MIRROR["w0"]),
               "y_out": H(MIRROR["w1"]), "z0": Z(MIRROR["z0"]), "z1": Z(MIRROR["z1"])},
    "gill": side(GILL),
    "wing": {"x_front": X(WING["y0"]), "x_back": X(WING["y1"]), "z0": Z(WING["z0"]), "z1": Z(WING["z1"]),
             "span": H(WING["span"]), "post_w": H(WING["post_w"])},
    "picks": {"frontbadge": 1, "rearbadge": 1, "name": 1, "wing": 1},
}
# belt line z = k x + c in build space
_bx = np.array([X(y) for y in (-0.6, 0.6)]); _bz = np.array([Z(belt(y)) for y in (-0.6, 0.6)])
_k = float((_bz[1] - _bz[0]) / (_bx[1] - _bx[0]))
data["belt"] = [round(_k, 6), round(float(_bz[0] - _k * _bx[0]), 4)]

if __name__ == "__main__":
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(data, indent=1))
    st = data["stations"]
    print("wrote", out, len(st), "stations; nose", max(s["x"] for s in st), "tail", min(s["x"] for s in st),
          "roof", max(s.get("zt", 0) for s in st), "half", max(s["w"] for s in st), data["wheel"])
