"""Section rows for the Seorak Cheonma CX-24Si, traced from Ivan's side and front renders.

Side view pixels: wheel centres at x 380 and 1651, ground at y 593.
Scale comes from the published length (4650 mm over 1964 px).
Front view pixels: centre line at x 569, width from the published 1900 mm over 998 px.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.
"""
import json
from pathlib import Path

S_SIDE = 4.65 / 1964
S_FRONT = 1.90 / 998
X_MID = (380 + 1651) / 2
GROUND = 593


def X(px):
    return round((px - X_MID) * S_SIDE, 4)


def Z(py):
    return round((GROUND - py) * S_SIDE, 4)


def YF(px):
    return round(abs(px - 569) * S_FRONT, 4)


def rows(stations):
    """stations: list of (side px, dict of section params already in metres)."""
    return [dict(x=X(px), **p) for px, p in stations]


def rr(yi, yo, zb, zt, rti, rto, rb=0.03):
    """Rounded-rect section: inner and outer y, bottom and top z, top corner radii, bottom radius."""
    return dict(yi=yi, yo=yo, zb=zb, zt=zt, rti=rti, rto=rto, rb=rb)


WHEEL_R = 160 * S_SIDE
WHEEL_X = (X(1651), X(380))

data = {
    "wheel": {"r": round(WHEEL_R, 4), "front_x": WHEEL_X[0], "rear_x": WHEEL_X[1],
              "width": 0.31, "outer_y": 0.93, "rim_r": 0.2286},
    # Front fender pods, left side (mirrored for right).
    "front_pod": rows([
        (1290, rr(0.45, 0.90, 0.10, Z(300), 0.06, 0.10)),
        (1350, rr(0.45, 0.90, 0.10, Z(262), 0.08, 0.14)),
        (1450, rr(0.44, 0.90, 0.10, Z(250), 0.10, 0.16)),
        (1550, rr(0.43, 0.90, 0.10, Z(248), 0.10, 0.16)),
        (1650, rr(0.43, 0.90, 0.10, Z(255), 0.10, 0.16)),
        (1730, rr(0.43, 0.90, 0.10, Z(268), 0.10, 0.16)),
        (1790, rr(0.43, 0.90, 0.10, Z(295), 0.09, 0.14)),
        (1830, rr(0.43, 0.895, 0.10, Z(340), 0.08, 0.12)),
        (1860, rr(0.44, 0.89, 0.10, Z(400), 0.06, 0.08)),
        (1878, rr(0.45, 0.88, 0.10, Z(450), 0.04, 0.05)),
        (1895, rr(0.46, 0.87, 0.10, Z(495), 0.03, 0.03)),
    ]),
    # Central nose and tub, from behind the cockpit to the bull nose.
    "nose": rows([
        (760, rr(-0.45, 0.45, 0.08, 0.60, 0.20, 0.20)),
        (1300, rr(-0.42, 0.42, 0.08, 0.66, 0.24, 0.24)),
        (1460, rr(-0.36, 0.36, 0.08, 0.64, 0.26, 0.26)),
        (1600, rr(-0.28, 0.28, 0.08, 0.53, 0.22, 0.22)),
        (1750, rr(-0.25, 0.25, 0.08, 0.42, 0.20, 0.20)),
        (1850, rr(-0.23, 0.23, 0.08, 0.35, 0.17, 0.17)),
        (1920, rr(-0.20, 0.20, 0.09, 0.30, 0.14, 0.14)),
        (1955, rr(-0.14, 0.14, 0.11, 0.25, 0.09, 0.09)),
    ]),
    # Low deck joining the nose to both pods.
    "deck": rows([
        (1290, rr(-0.88, 0.88, 0.08, 0.24, 0.05, 0.05)),
        (1700, rr(-0.88, 0.88, 0.08, 0.23, 0.05, 0.05)),
        (1890, rr(-0.86, 0.86, 0.09, 0.21, 0.05, 0.05)),
        (1915, rr(-0.84, 0.84, 0.10, 0.18, 0.03, 0.03)),
    ]),
    "splitter": rows([
        (1640, rr(-0.95, 0.95, 0.10, 0.14, 0.01, 0.01, 0.01)),
        (1950, rr(-0.95, 0.95, 0.10, 0.14, 0.01, 0.01, 0.01)),
        (2010, rr(-0.93, 0.93, 0.10, 0.14, 0.015, 0.015, 0.015)),
        (2027, rr(-0.88, 0.88, 0.105, 0.135, 0.012, 0.012, 0.012)),
    ]),
    "canopy": rows([
        (840, rr(-0.30, 0.30, 0.50, Z(185), 0.20, 0.20)),
        (950, rr(-0.31, 0.31, 0.50, Z(152), 0.22, 0.22)),
        (1050, rr(-0.31, 0.31, 0.50, Z(150), 0.22, 0.22)),
        (1150, rr(-0.31, 0.31, 0.50, Z(152), 0.22, 0.22)),
        (1250, rr(-0.31, 0.31, 0.50, Z(175), 0.22, 0.22)),
        (1350, rr(-0.30, 0.30, 0.50, Z(205), 0.21, 0.21)),
        (1420, rr(-0.29, 0.29, 0.50, Z(228), 0.20, 0.20)),
        (1470, rr(-0.27, 0.27, 0.50, Z(245), 0.18, 0.18)),
    ]),
    "scoop": rows([
        (860, rr(-0.06, 0.06, Z(160), Z(130), 0.03, 0.03, 0.01)),
        (1180, rr(-0.06, 0.06, Z(160), Z(118), 0.03, 0.03, 0.01)),
        (1195, rr(-0.06, 0.06, Z(160), Z(122), 0.03, 0.03, 0.01)),
    ]),
    "engine_cover": rows([
        (300, rr(-0.22, 0.22, 0.17, Z(278), 0.14, 0.14)),
        (450, rr(-0.26, 0.26, 0.17, Z(268), 0.16, 0.16)),
        (600, rr(-0.30, 0.30, 0.17, Z(250), 0.18, 0.18)),
        (750, rr(-0.34, 0.34, 0.17, Z(215), 0.20, 0.20)),
        (880, rr(-0.36, 0.36, 0.17, Z(178), 0.22, 0.22)),
    ]),
    # Fin outline in side px, buried into the engine cover along its bottom.
    "fin": {"half_t": 0.012, "outline": [[X(px), Z(py)] for px, py in [
        (300, 140), (880, 134), (880, 230), (600, 280), (330, 300), (300, 300)]]},
    "rear_fender": rows([
        (225, rr(0.45, 0.95, 0.18, Z(330), 0.10, 0.14)),
        (280, rr(0.45, 0.95, 0.18, Z(295), 0.12, 0.17)),
        (350, rr(0.45, 0.95, 0.18, Z(270), 0.12, 0.18)),
        (450, rr(0.45, 0.95, 0.18, Z(260), 0.12, 0.18)),
        (560, rr(0.45, 0.95, 0.18, Z(258), 0.12, 0.18)),
        (650, rr(0.44, 0.95, 0.18, Z(270), 0.12, 0.18)),
        (750, rr(0.42, 0.94, 0.18, Z(300), 0.12, 0.16)),
        (830, rr(0.40, 0.93, 0.18, Z(330), 0.11, 0.14)),
    ]),
    "sidepod": rows([
        (800, rr(0.38, 0.93, 0.18, Z(330), 0.11, 0.14)),
        (1000, rr(0.38, 0.92, 0.18, Z(335), 0.11, 0.13)),
        (1150, rr(0.38, 0.92, 0.18, Z(330), 0.11, 0.13)),
        (1250, rr(0.39, 0.92, 0.18, Z(305), 0.10, 0.12)),
        (1300, rr(0.40, 0.92, 0.18, Z(298), 0.10, 0.12)),
    ]),
    "floor": rows([
        (230, rr(-0.92, 0.92, 0.06, 0.12, 0.01, 0.01, 0.01)),
        (1700, rr(-0.92, 0.92, 0.06, 0.12, 0.01, 0.01, 0.01)),
    ]),
    "rear_box": rows([
        (63, rr(-0.95, 0.95, Z(520), Z(455), 0.03, 0.03, 0.03)),
        (240, rr(-0.95, 0.95, Z(520), Z(455), 0.03, 0.03, 0.03)),
    ]),
    "wing": {"x_le": X(330), "x_te": X(150), "z_le": Z(212), "z_te": Z(186),
             "thick": 0.035, "half_span": 0.90},
    "endplate": {"y": 0.925, "half_t": 0.008, "outline": [[X(px), Z(py)] for px, py in [
        (63, 470), (63, 135), (85, 127), (125, 132), (215, 250), (212, 272),
        (110, 282), (110, 470)]]},
    "pylon": {"half_t": 0.01, "outline": [[X(px), Z(py)] for px, py in [
        (180, 210), (330, 205), (330, 300), (240, 300)]]},
    "diffuser_fins": {"ys": [0.0, 0.42, -0.42], "half_t": 0.008,
                      "outline": [[X(px), Z(py)] for px, py in [
                          (70, 520), (260, 520), (260, 560), (70, 580)]]},
    "mirror": {"x": X(1290), "y": 0.58, "z": Z(236), "rx": 0.06, "ry": 0.045, "rz": 0.032,
               "stalk_r": 0.012, "stalk_z0": Z(285)},
    # Headlights, left pod, as front view pixels mapped onto the pod's front face.
    "head": {"frame_px": [[135, 245], [288, 240], [295, 400], [215, 480], [128, 470]],
             "lamps_px": [[205, 290, 35], [175, 398, 34]], "led_px": [243, 355, 25],
             "face_top": [X(1805), Z(312)], "face_bot": [X(1875), Z(450)],
             "py_top": 235, "py_bot": 480},
    # Windscreen in plan (side px, y metres) and side windows in side px.
    "glass": {"screen_plan": [(1195, 0.25), (1300, 0.26), (1400, 0.23), (1455, 0.19),
                              (1455, -0.19), (1400, -0.23), (1300, -0.26), (1195, -0.25)],
              "side_px": [(995, 222), (1170, 222), (1182, 240), (1182, 296), (995, 296), (988, 280)]},
    "rear_lamps": {"y0": 0.55, "y1": 0.75, "z0": Z(512), "z1": Z(465), "rain_hw": 0.06},
}

data["head"]["yf"] = {"scale": S_FRONT, "centre": 569}

if __name__ == "__main__":
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(data, indent=1))
    print("wrote", out)
