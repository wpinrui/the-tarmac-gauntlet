"""Traced rows for the Nusa Carrier, based on the 1992 Proton Saga saloon.

Reference: Ivan's 3D model of a first generation Saga (a .glb, kept out of the repo). It is
rendered orthographically from the side, front, rear and top on a plain background at 500
pixels per model unit, and every row below is picked off those renders like a drawing. Nothing
is taken from its meshes. Points are written in render units read off the renders' grid:
  Y along the car (front negative), Z up, X out to the side (half widths).
Side render: Y = -2.4 + px / 500, Z = 0.85 - py / 500. Front, rear and top renders share the scale.

Scale (Ivan's call: keep the model's proportions): the wheel centres, 2.802 units apart, set one
scale for the whole car from the published 2380 mm wheelbase. The ground sits one published
tyre radius (175/70 R13) under the wheel centres.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py  -> rows.json
"""
import json
from pathlib import Path

import numpy as np
from scipy.interpolate import PchipInterpolator

WHEELBASE = 2.380
TYRE_R = round((13 * 25.4 + 2 * 0.70 * 175) / 2000, 4)    # 175/70 R13
RIM_R = round(13 * 25.4 / 2000, 4)
Y_FRONT, Y_REAR, Z_WHEEL = -1.4325, 1.3695, -0.327      # wheel centres on the side render
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
# Silhouette top along the centre line: nose face, bonnet, windscreen, roof, rear glass, boot, tail.
TOP = [(-2.335, -0.17), (-2.325, -0.14), (-2.30, -0.09), (-2.26, -0.025), (-2.22, 0.048), (-2.19, 0.085),
       (-2.12, 0.096), (-2.0, 0.128), (-1.88, 0.158), (-1.76, 0.186), (-1.6, 0.214), (-1.4, 0.246),
       (-1.2, 0.278), (-1.0, 0.31), (-0.93, 0.32), (-0.88, 0.35), (-0.8, 0.406), (-0.6, 0.542),
       (-0.4, 0.666), (-0.3, 0.722), (-0.24, 0.75), (-0.16, 0.766), (0.0, 0.774), (0.2, 0.786),
       (0.4, 0.794), (0.96, 0.794), (1.04, 0.792), (1.10, 0.788), (1.16, 0.768), (1.2, 0.742),
       (1.3, 0.682), (1.4, 0.618), (1.5, 0.551), (1.6, 0.474), (1.68, 0.408), (1.72, 0.386),
       (1.8, 0.382), (2.0, 0.38), (2.2, 0.376), (2.38, 0.374), (2.405, 0.36), (2.425, 0.32),
       (2.44, 0.20), (2.45, 0.05), (2.47, 0.0), (2.50, -0.04), (2.525, -0.08)]
# Silhouette bottom of the bodywork, carried straight across the arches.
BOTTOM = [(-2.335, -0.225), (-2.30, -0.29), (-2.24, -0.315), (-2.16, -0.36), (-2.10, -0.42),
          (-2.06, -0.452), (-1.88, -0.452), (-0.92, -0.51), (-0.4, -0.498), (0.4, -0.484),
          (0.96, -0.472), (1.8, -0.388), (2.0, -0.362), (2.2, -0.328), (2.32, -0.312),
          (2.36, -0.284), (2.40, -0.21), (2.44, -0.184), (2.48, -0.144), (2.525, -0.10)]
# Half width in plan (top render), mirrors left out.
HALF = [(-2.335, 0.55), (-2.325, 0.70), (-2.30, 0.78), (-2.26, 0.81), (-2.2, 0.818), (-2.0, 0.842),
        (-1.8, 0.862), (-1.7, 0.872), (-1.5, 0.875), (1.6, 0.875), (1.72, 0.888), (2.0, 0.892),
        (2.3, 0.892), (2.40, 0.884), (2.45, 0.85), (2.49, 0.78), (2.51, 0.70), (2.525, 0.60)]
# Height of the widest point, read off the front and rear renders.
WIDEST = [(-2.335, -0.17), (-2.2, -0.15), (-1.85, -0.10), (-1.0, -0.03), (1.6, -0.03), (1.9, -0.02),
          (2.525, -0.05)]
# How far the sill or bumper bottom tucks in from the widest point (front and rear renders).
TUCK = [(-2.335, 0.10), (-1.9, 0.10), (-1.7, 0.035), (1.6, 0.035), (1.85, 0.06), (2.525, 0.06)]
# Shoulder: where the side turns in to the bonnet, the glass or the boot (z), and its half width.
SHOULDER = [(-2.335, -0.20), (-2.30, -0.12), (-2.22, 0.03), (-2.19, 0.06), (-2.12, 0.068),
            (-2.0, 0.098), (-1.6, 0.18), (-1.2, 0.245), (-1.0, 0.275), (-0.92, 0.25), (-0.86, 0.208),
            (0.0, 0.228), (0.96, 0.262), (1.44, 0.276), (1.62, 0.33), (1.74, 0.355), (2.38, 0.355),
            (2.41, 0.33), (2.44, 0.20), (2.45, 0.05), (2.48, -0.02), (2.525, -0.09)]
SHOULDER_IN = [(-2.335, 0.03), (-2.2, 0.05), (-1.8, 0.07), (-1.0, 0.075), (-0.9, 0.055), (1.4, 0.055),
               (1.7, 0.085), (2.4, 0.09), (2.525, 0.05)]
# Deck height at the centre under the greenhouse (hidden), joined to the bonnet and the boot.
DECK = [(-0.95, 0.318), (-0.85, 0.27), (1.55, 0.31), (1.70, 0.383)]

# Greenhouse, from the windscreen's foot to the rear glass's foot.
GH = (-0.95, 1.72)
GH_BASE = 0.792            # the glass's foot in plan, the full length of the cabin
# Roof edge: the A-pillar, the roof's side and the C-pillar, in side (Y, Z) and plan (Y, X).
ROOF_Z = [(-0.95, 0.29), (-0.88, 0.296), (-0.8, 0.33), (-0.72, 0.385), (-0.6, 0.468), (-0.48, 0.55),
          (-0.36, 0.632), (-0.28, 0.69), (-0.22, 0.728), (-0.16, 0.738), (0.0, 0.746), (0.4, 0.764),
          (1.0, 0.764), (1.10, 0.756), (1.16, 0.738), (1.2, 0.712), (1.28, 0.66), (1.4, 0.58),
          (1.52, 0.498), (1.64, 0.408), (1.72, 0.37)]
ROOF_W = [(-0.95, 0.785), (-0.88, 0.765), (-0.6, 0.668), (-0.3, 0.556), (-0.22, 0.53), (0.0, 0.527),
          (1.08, 0.527), (1.20, 0.55), (1.40, 0.60), (1.60, 0.645), (1.72, 0.675)]


def table(rows):
    a = np.array(rows, float)
    return PchipInterpolator(a[:, 0], a[:, 1], extrapolate=True)


def stations():
    """Each station's section values in metres, at every Y any row was picked at."""
    t = {k: table(v) for k, v in dict(top=TOP, bot=BOTTOM, half=HALF, zw=WIDEST, tuck=TUCK, zs=SHOULDER,
                                       sin=SHOULDER_IN, deck=DECK, rz=ROOF_Z, rw=ROOF_W).items()}
    ys = sorted({round(float(y), 3) for rows in (TOP, BOTTOM, HALF, SHOULDER) for y, _ in rows}
                | {round(float(y), 3) for y in np.arange(-2.30, 2.50, 0.04)})
    out = []
    for y in ys:
        f = {k: float(v(y)) for k, v in t.items()}
        top = f["top"]
        in_gh = GH[0] < y < GH[1]
        deck = min(top, f["deck"]) if in_gh else top
        st = dict(x=X(y), zb=Z(f["bot"]), w=H(f["half"]), zw=Z(f["zw"]), tuck=H(f["tuck"]),
                  zs=Z(min(f["zs"], deck - 0.012)), ws=H(f["half"] - f["sin"]), zd=Z(deck))
        if in_gh:
            st.update(zt=Z(top), zr=Z(min(f["rz"], top - 0.004)), wr=H(f["rw"]), wg=H(GH_BASE))
        out.append(st)
    return out


# ---------------------------------------------------------------- glass (side render)
def belt(y):
    return 0.228 + 0.0345 * y


# The side window opening (glass and its black frame), front door to the rear door's quarter light.
DLO = [(-0.83, belt(-0.83)), (-0.76, 0.262), (-0.50, 0.483), (-0.30, 0.653), (-0.235, 0.705),
       (-0.19, 0.728), (-0.10, 0.733), (0.30, 0.745), (0.90, 0.75), (0.93, 0.745), (1.10, 0.578),
       (1.28, 0.385), (1.305, 0.35), (1.31, 0.30), (1.30, belt(1.30)), (0.9, belt(0.9)),
       (0.3, belt(0.3)), (-0.4, belt(-0.4))]
B_PILLAR = (0.30, 0.37)          # its black, front and rear edge
DIVIDER = [(0.835, 0.745), (0.89, belt(0.89))]    # the rear door's slanted frame, centre line, 0.03 wide

# Shut lines (side render).
FRONT_DOOR = [(-0.862, 0.29), (-0.866, 0.278), (-0.904, 0.122), (-0.918, -0.138), (-0.892, -0.294),
              (-0.846, -0.415)]
DOOR_BOTTOM = -0.415
REAR_DOOR = [(0.96, 0.75), (1.20, 0.55), (1.45, 0.355), (1.404, 0.174), (1.352, 0.044),
             (1.28, -0.035), (1.209, -0.067), (1.092, -0.067), (1.0, -0.19), (0.936, -0.385),
             (0.80, -0.392)]
REAR_DOOR_BOTTOM = -0.392
B_SHUT = [(0.335, 0.745), (0.335, 0.30), (0.30, 0.12), (0.28, -0.06), (0.30, -0.392)]

# Windscreen (top render: Y, half width): foot and head, both straight across.
SCREEN = {"y_base": -0.93, "y_top": -0.22, "w_base": 0.705, "w_top": 0.53}
# Rear glass (top render): head and foot.
REAR_GLASS = {"y_top": 1.10, "y_base": 1.70, "w_top": 0.50, "w_base": 0.66}

# ---------------------------------------------------------------- front (front render, left half)
HEAD = [(0.425, 0.072), (0.755, 0.072), (0.785, -0.082), (0.415, -0.082)]
GRILLE = {"w": 0.41, "z0": -0.082, "z1": -0.012, "slats": [-0.03, -0.048, -0.066]}
FRONT_STRIP = {"z0": -0.14, "z1": -0.085, "y_end": -1.85}      # black rubbing strip, round to the arch
INTAKE = {"w": 0.47, "z0": -0.40, "z1": -0.305, "slats": [-0.338, -0.37]}
BONNET = [(-0.93, 0.62), (-0.945, 0.735), (-0.99, 0.785), (-1.2, 0.786), (-2.08, 0.784),
          (-2.15, 0.768), (-2.175, 0.70), (-2.185, 0.40), (-2.19, 0.0)]     # plan, left half, cowl to nose

# ---------------------------------------------------------------- rear (rear render, left half)
TAIL = [(0.32, 0.20), (0.81, 0.20), (0.855, 0.047), (0.32, 0.047)]
TAIL_SIDE = 0.15        # the lamp runs this far forward round the corner onto the side
LID = {"w": 0.675, "z0": 0.205, "y_glass": 1.70}     # boot lid: half width, foot on the rear face
BUMPER_TOP = [(1.64, -0.073), (2.25, -0.04), (2.27, 0.005), (2.45, 0.0)]  # side, then across the back at z 0
VALANCE = {"w": 0.82, "z0": -0.39, "z1": -0.32}
EXHAUST = {"w": -0.48, "z": -0.36, "r": 0.03}       # on the car's right

# ---------------------------------------------------------------- mirror (front and top renders)
MIRROR = {"y0": -0.72, "y1": -0.62, "w0": 0.80, "w1": 0.97, "z0": 0.23, "z1": 0.345}

data = {
    "name": "NusaCarrier",
    "wheel": {"r": TYRE_R, "rim_r": RIM_R, "width": 0.175, "outer_y": H(0.90), "front_x": X(Y_FRONT),
              "rear_x": X(Y_REAR), "arch_r": round(TYRE_R + 0.025, 4)},
    "stations": stations(),
    "gh": [X(GH[1]), X(GH[0])],
    "dlo": side(DLO),
    "b_pillar": [X(B_PILLAR[1]), X(B_PILLAR[0])],
    "divider": side(DIVIDER),
    "front_door": side(FRONT_DOOR),
    "door_bottom": Z(DOOR_BOTTOM),
    "rear_door": side(REAR_DOOR),
    "rear_door_bottom": Z(REAR_DOOR_BOTTOM),
    "b_shut": side(B_SHUT),
    "screen": {"x_base": X(SCREEN["y_base"]), "x_top": X(SCREEN["y_top"]), "w_base": H(SCREEN["w_base"]),
               "w_top": H(SCREEN["w_top"])},
    "rear_glass": {"x_top": X(REAR_GLASS["y_top"]), "x_base": X(REAR_GLASS["y_base"]),
                   "w_top": H(REAR_GLASS["w_top"]), "w_base": H(REAR_GLASS["w_base"])},
    "head": end(HEAD),
    "grille": {"w": H(GRILLE["w"]), "z0": Z(GRILLE["z0"]), "z1": Z(GRILLE["z1"]),
               "slats": [Z(z) for z in GRILLE["slats"]]},
    "front_strip": {"z0": Z(FRONT_STRIP["z0"]), "z1": Z(FRONT_STRIP["z1"]), "x_end": X(FRONT_STRIP["y_end"])},
    "intake": {"w": H(INTAKE["w"]), "z0": Z(INTAKE["z0"]), "z1": Z(INTAKE["z1"]),
               "slats": [Z(z) for z in INTAKE["slats"]]},
    "bonnet": plan(BONNET),
    "tail": end(TAIL),
    "tail_side": H(TAIL_SIDE),
    "lid": {"w": H(LID["w"]), "z0": Z(LID["z0"]), "x_glass": X(LID["y_glass"])},
    "bumper_top": side(BUMPER_TOP),
    "valance": {"w": H(VALANCE["w"]), "z0": Z(VALANCE["z0"]), "z1": Z(VALANCE["z1"])},
    "exhaust": {"y": H(EXHAUST["w"]), "z": Z(EXHAUST["z"]), "r": H(EXHAUST["r"])},
    "mirror": {"x_back": X(MIRROR["y1"]), "x_front": X(MIRROR["y0"]), "y_in": H(MIRROR["w0"]),
               "y_out": H(MIRROR["w1"]), "z0": Z(MIRROR["z0"]), "z1": Z(MIRROR["z1"])},
    "picks": {"frontbadge": 1, "rearbadge": 1, "name": 1},
}
# belt line z = k x + c in build space
_bx = np.array([X(y) for y in (-0.8, 1.2)]); _bz = np.array([Z(belt(y)) for y in (-0.8, 1.2)])
_k = float((_bz[1] - _bz[0]) / (_bx[1] - _bx[0]))
data["belt"] = [round(_k, 6), round(float(_bz[0] - _k * _bx[0]), 4)]

if __name__ == "__main__":
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(data, indent=1))
    st = data["stations"]
    print("wrote", out, len(st), "stations; nose", max(s["x"] for s in st), "tail", min(s["x"] for s in st),
          "roof", max(s.get("zt", 0) for s in st), "half", max(s["w"] for s in st), data["wheel"])
