"""Section rows for the Liberty Collier, traced from Ivan's side, top, front and rear renders.

Side view (rotated upright, front to the right): wheel centres at x 210 and 960 px, scaled to the
published 2540 mm wheelbase. Overhangs stretch so the car meets the published 4030 mm length.
Heights scale so the roof meets the published 1472 mm, with wheel centres at the tyre radius.
Top view gives the plan width, scaled to the published 1720 mm.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.
"""
import json
from pathlib import Path

import numpy as np

WB = 2.54
S = WB / (960 - 210)
K_OVER = (4.03 - WB) / ((1172 - 960) * S + (210 - 60) * S)
TYRE_R = 0.3105
S_Z = (1.472 - TYRE_R) / (380 - 68)
HALF_W = 0.86


def X(px):
    if px > 960:
        return round(WB / 2 + (px - 960) * S * K_OVER, 4)
    if px < 210:
        return round(-WB / 2 - (210 - px) * S * K_OVER, 4)
    return round((px - 585) * S, 4)


def Z(py):
    return round(TYRE_R + (380 - py) * S_Z, 4)


X_FRONT, X_REAR = X(1172), X(60)


# Front view: centre x 325.5, body width 525 px. Heights matched to side-view features.
def FY(px):
    return round((px - 325.5) * 1.72 / 525, 4)


def FZ(py):
    return round(float(np.interp(py, [210, 300, 402, 470, 550], [1.11, 0.81, 0.60, 0.35, 0.05])), 4)


# Rear view: centre x 362, body width 539 px. Heights matched to side-view features.
def RY(px):
    return round((362 - px) * 1.72 / 539, 4)


def RZ(py):
    return round(float(np.interp(py, [120, 255, 268, 358, 395, 470, 600], [1.41, 1.05, 1.02, 0.75, 0.57, 0.42, 0.0])), 4)


# Top view: front at row 1056, rear at row 40, centre x 312.5, body width 381 px.
# The top render is a perspective view, so rows map to x through features matched with the side view:
# roof glass rear edge, door mirror and windscreen base.
def TX(row):
    return round(float(np.interp(row, [40, 232, 675, 878, 1056], [X(60), X(255), X(750), X(900), X(1172)])), 4)


def TY(px):
    return round((px - 312.5) * 1.72 / 381, 4)

# Plan width ratio from the top view: (top-view row px, ratio of max width). Front at row 1056.
TOP = [(40, 0.30), (46, 0.46), (52, 0.61), (58, 0.73), (64, 0.81), (76, 0.90), (88, 0.94), (100, 0.96),
       (140, 0.985), (200, 1.0), (900, 1.0), (940, 0.99), (980, 0.94), (1000, 0.88), (1012, 0.84),
       (1024, 0.76), (1036, 0.64), (1042, 0.51), (1048, 0.32), (1056, 0.20)]


def plan_w(x):
    row = 1056 - (X_FRONT - x) / ((X_FRONT - X_REAR) / (1056 - 40))
    r = np.interp(row, [t[0] for t in TOP], [t[1] for t in TOP])
    return round(float(r) * HALF_W, 4)


def st(px, top, bot, belt=None, roof=False, wb=None):
    """One station. top and bot are side px of the silhouette; belt is the window sill px."""
    x = X(px)
    w = plan_w(x)
    zt, zb = Z(top), Z(bot)
    if belt is not None:
        zs = Z(belt)
        crown = 0.045 if roof else 0.03
        return dict(x=x, zb=zb, zt=zt - crown, crown=crown, w=w, zw=min(Z(300), zs - 0.15),
                    wb=round(w - (0.07 if wb is None else wb), 4), zs=zs, wt=round(min(0.62, w - 0.2), 4) if roof else round(w - 0.16, 4))
    crown = 0.03
    zs = zt - crown - 0.05
    return dict(x=x, zb=zb, zt=zt - crown, crown=crown, w=w, zw=min(Z(300), zs - 0.05),
                wb=round(w * 0.985, 4), zs=zs, wt=round(w * 0.82, 4))


data = {
    "picks": {},
    "wheel": {"r": TYRE_R, "front_x": X(960), "rear_x": X(210), "width": 0.195,
              "outer_y": 0.835, "rim_r": 0.2032},
    "body": [
        st(60, 318, 351),
        st(68, 292, 381),
        st(80, 269, 394),
        st(92, 228, 398),
        st(104, 197, 402),
        st(118, 186, 405, belt=188),
        st(140, 169, 413, belt=186),
        st(170, 146, 413, belt=184, roof=True),
        st(200, 125, 413, belt=183, roof=True),
        st(230, 104, 413, belt=183, roof=True),
        st(260, 86, 413, belt=183, roof=True),
        st(320, 74, 413, belt=184, roof=True),
        st(410, 66, 413, belt=187, roof=True),
        st(500, 64, 413, belt=190, roof=True),
        st(590, 63, 413, belt=194, roof=True),
        st(650, 70, 413, belt=197, roof=True),
        st(710, 88, 413, belt=200, roof=True),
        st(770, 111, 413, belt=202, roof=True),
        st(830, 136, 413, belt=204, wb=0.055),
        st(880, 157, 413, belt=205, wb=0.03),
        st(910, 166, 413),
        st(950, 178, 413),
        st(1010, 197, 413),
        st(1070, 216, 410),
        st(1112, 240, 405),
        st(1136, 261, 395),
        st(1154, 297, 382),
        st(1166, 330, 361),
        st(1172, 340, 353),
    ],
}

# Glass and openings.
data["glass"] = {
    # side px outlines
    "door_window": [(470, 90), (600, 85), (700, 98), (760, 130), (815, 190), (820, 203), (700, 199),
                    (600, 195), (470, 189)],
    "quarter_window": [(292, 110), (330, 97), (440, 88), (440, 187), (345, 168), (305, 135)],
    # B pillar black applique between the two side windows (side px)
    "b_pillar": [(442, 86), (468, 86), (468, 190), (442, 189)],
    # top view: windscreen and glass roof as one piece (top px x, row)
    "roof": [(190, 232), (312, 226), (434, 232), (438, 700), (478, 822), (312, 878), (146, 822), (186, 700)],
    # rear view
    "rear": [(208, 130), (362, 126), (476, 128), (520, 175), (556, 248), (362, 254), (166, 250), (180, 190)],
}
data["cabin"] = {"x0": X(150), "x1": X(860), "zb": 0.30, "inset": 0.035}
data["lamps"] = {
    # front px, left in the image is the car's right; mirrored for the left
    "head": [(92, 262), (118, 256), (142, 272), (165, 300), (183, 335), (190, 368), (178, 385),
             (150, 380), (120, 362), (100, 332), (88, 298)],
    "head_units": [("lamp_head", 115, 312, 22), ("lamp_head", 146, 344, 14), ("lamp_indicator", 165, 368, 9)],
    "fog": [(160, 485, 19)],
    # rear px, left in the image is the car's left
    "tail": [(100, 282), (118, 268), (140, 272), (160, 290), (178, 318), (186, 352), (165, 358),
             (130, 352), (100, 340), (97, 310)],
    "tail_units": [("lamp_tail", 125, 298, 13), ("lamp_brake", 152, 322, 13), ("lamp_indicator", 118, 327, 9),
                   ("lamp_reverse", 166, 344, 8)],
    "repeater": {"x": X(856), "z": Z(292), "rx": 0.032, "rz": 0.012},
}
# Wrap-round lamps, traced on the unwrapped skin map from unwrap.py: (s, u) in metres, left side.
data["lamps"]["head_su"] = [(1.40, 1.60), (1.62, 1.615), (1.85, 1.575), (2.04, 1.50), (2.175, 1.40), (2.275, 1.30),
                            (2.35, 1.175), (2.39, 1.04), (2.35, 0.995), (2.225, 1.035), (2.10, 1.125), (2.0, 1.2),
                            (1.85, 1.31), (1.70, 1.41), (1.55, 1.50), (1.45, 1.565)]
data["lamps"]["head_su_units"] = [("lamp_head", 2.125, 1.20, 0.045), ("lamp_head", 2.25, 1.06, 0.035),
                                  ("lamp_indicator", 1.95, 1.40, 0.025)]
data["lamps"]["tail_su"] = [(-1.419, 1.612), (-1.5, 1.622), (-1.6, 1.606), (-1.675, 1.569), (-1.787, 1.5),
                            (-1.875, 1.437), (-1.95, 1.362), (-1.994, 1.275), (-1.987, 1.2), (-1.9, 1.165),
                            (-1.8, 1.19), (-1.75, 1.25), (-1.712, 1.35), (-1.675, 1.41), (-1.575, 1.487),
                            (-1.475, 1.562)]
data["lamps"]["tail_su_units"] = [("lamp_tail", -1.6, 1.553, 0.035), ("lamp_brake", -1.85, 1.32, 0.05),
                                  ("lamp_indicator", -1.72, 1.46, 0.028), ("lamp_reverse", -1.95, 1.25, 0.025)]
# High-level brake lamp over the rear window, rear px.
data["lamps"]["high_brake"] = [(305, 120), (419, 120), (419, 129), (305, 129)]
data["grille_upper"] = [(280, 328), (372, 328), (366, 355), (345, 370), (305, 370), (285, 355)]
data["grille_lower"] = [(100, 402), (550, 402), (552, 420), (540, 450), (500, 470), (150, 470), (110, 450), (98, 420)]
data["rear_strip"] = [(100, 398), (360, 396), (605, 393), (610, 465), (360, 467), (100, 470)]
data["shut_lines"] = {
    # side px, rays across the car
    "door": [(838, 206), (852, 300), (846, 405), (640, 410), (452, 408), (436, 300), (440, 190)],
    # rear px
    "tailgate": [(200, 122), (530, 122), (556, 250), (520, 362), (470, 374), (250, 374), (205, 362), (168, 250)],
    # front px: bonnet front edge
    "bonnet": [(95, 252), (200, 300), (325, 312), (450, 300), (556, 252)],
}
data["mirror"] = {"x": TX(675), "y": 0.86, "z": Z(184), "rx": 0.06, "ry": 0.085, "rz": 0.055,
                  "root_y": 0.74, "root_z": 0.95}

if __name__ == "__main__":
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(data, indent=1))
    print("wrote", out, "length", X_FRONT - X_REAR)
