"""Traced rows for the Kaiser Oboe, based on the 2009 VW Golf GTI 3-door (Mk5).

References (Ivan's): side, front, top, front three-quarter, high front and rear renders.
Published: wheelbase 2578 mm, length 4216 mm, width 1759 mm, height 1466 mm, 225/45 R17.

Side render, mirrored so the nose is on the left. It is a perspective shot from nearer the
rear: the rear wheel reads 140 px across and the front 130, so each column gets its own scale
before use. Wheel centres (252, 463) and (1272, 482); nose x 40, tail x 1515.
Front render: face centre x 459, half width 400 px. Rear render: centre x 513, half width 359 px.
Top render: half width 262 px.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py  -> rows.json
"""
import json
from pathlib import Path

import numpy as np

WHEELBASE, LENGTH, WIDTH, HEIGHT = 2.578, 4.216, 1.759, 1.466
TYRE_R = round((17 * 25.4 + 2 * 0.45 * 225) / 2000, 4)
RIM_R = round(17 * 25.4 / 2000, 4)
HALF_W = WIDTH / 2

# ---------------------------------------------------------------- side view mapping
NOSE, TAIL, WF, WR = 40, 1515, 252, 1272
WF_Y, WR_Y = 463, 482
GROW = (140 / 130 - 1) / (WR - WF)          # per px, how much nearer the camera a column is


def scale(px):
    return 1 + GROW * (np.asarray(px, float) - WF)


def even(px):
    """Side px with the perspective removed."""
    return WF + np.log(scale(px)) / GROW


def axle_y(px):
    return WF_Y + (px - WF) * (WR_Y - WF_Y) / (WR - WF)


def _x_lookup():
    """px -> x. The wheelbase and the overhangs each get their own stretch, blended over a
    short run so no kink lands in a body line; solved so wheelbase and length are exact."""
    px = np.arange(NOSE - 4, TAIL + 4.5, 0.5)
    e = even(px)
    ef, er = float(even(WF)), float(even(WR))
    blend = 1 / (1 + np.exp(-(ef - e) / 20)) + 1 / (1 + np.exp(-(e - er) / 20))
    de = np.gradient(e)
    run = lambda w: np.concatenate([[0], np.cumsum(((w * de)[1:] + (w * de)[:-1]) / 2)])
    inner, outer = run(1 - blend), run(blend)
    at = lambda p: int(round((p - px[0]) * 2))
    eq = np.array([[inner[at(WR)] - inner[at(WF)], outer[at(WR)] - outer[at(WF)]],
                   [inner[at(TAIL)] - inner[at(NOSE)], outer[at(TAIL)] - outer[at(NOSE)]]])
    ki, ko = np.linalg.solve(eq, [WHEELBASE, LENGTH])
    x = -(ki * inner + ko * outer)
    return px, x - (x[at(WF)] + x[at(WR)]) / 2


_PX, _X = _x_lookup()
ROOF_PX, ROOF_PY = 1040, 43
DZ = (HEIGHT - TYRE_R) / ((axle_y(ROOF_PX) - ROOF_PY) / scale(ROOF_PX))   # metres per px


def X(px):
    return round(float(np.interp(px, _PX, _X)), 4)


def Z(px, py):
    return round(float(TYRE_R + (axle_y(px) - py) / scale(px) * DZ), 4)


def side(pts):
    return [[X(px), Z(px, py)] for px, py in pts]


# ---------------------------------------------------------------- front, rear, top mappings
F_C, F_S = 459, HALF_W / 400
F_TO_SIDE = [(210, 205), (283, 283), (377, 350), (470, 420), (600, 491), (640, 505)]
R_C, R_S = 513, HALF_W / 359
R_TO_SIDE = [(88, 50), (113, 86), (250, 186), (262, 198), (357, 292), (487, 398), (640, 515)]
T_S = HALF_W / 262
T_TO_SIDE = [(88, 40), (505, 465), (1346, 1515)]


def _lerp(table, v):
    a, b = zip(*table)
    return float(np.interp(v, a, b))


def front(pts):
    """Front render px -> (y, z); image right is the car's left (+y)."""
    return [[round((px - F_C) * F_S, 4), Z(60, _lerp(F_TO_SIDE, py))] for px, py in pts]


def rear(pts):
    """Rear render px -> (y, z); image left is the car's left (+y)."""
    return [[round((R_C - px) * R_S, 4), Z(1490, _lerp(R_TO_SIDE, py))] for px, py in pts]


def half_loop(pts, c):
    """A trace from the centre line round one half, closed by its mirror about c."""
    return pts + [(2 * c - p, q) for p, q in pts[-2:0:-1]]


def across(pts, c):
    """An open path from one side to the centre line, carried on to the other side."""
    return pts + [(2 * c - p, q) for p, q in pts[-2::-1]]


# ---------------------------------------------------------------- body profiles (side px)
TOP = [(40, 391), (44, 365), (48, 358), (52, 352), (60, 344), (64, 336), (72, 323), (80, 313),
       (100, 297), (120, 283), (140, 276), (160, 270), (200, 258), (260, 244), (300, 235),
       (340, 226), (380, 215), (405, 206), (440, 185), (480, 162), (520, 140), (560, 119),
       (600, 101), (640, 86), (680, 74), (720, 65), (760, 59), (800, 54), (840, 50), (880, 47),
       (920, 45), (980, 44), (1040, 43), (1100, 44), (1160, 46), (1220, 50), (1280, 54),
       (1320, 57), (1340, 62), (1360, 80), (1380, 109), (1400, 130), (1420, 151), (1440, 175),
       (1460, 198), (1472, 222), (1482, 252), (1492, 300), (1500, 340), (1508, 368), (1515, 400)]
BOTTOM = [(40, 410), (44, 440), (48, 466), (56, 479), (70, 490), (100, 498), (130, 502),
          (400, 519), (700, 523), (1100, 530), (1420, 522), (1460, 513), (1480, 499),
          (1500, 474), (1510, 450), (1515, 425)]
# Belt and shoulder: where the vertical side ends and the top (bonnet, glass, tailgate) begins.
BELT = [(40, 352), (60, 328), (100, 302), (140, 291), (200, 274), (300, 254), (400, 242),
        (490, 239), (700, 228), (1000, 209), (1197, 192), (1260, 196), (1300, 202), (1350, 212),
        (1400, 232), (1440, 258), (1470, 290), (1495, 340), (1515, 430)]
WIDEST = [(40, 410), (100, 418), (300, 402), (1100, 396), (1400, 405), (1515, 415)]
# Half widths: nose and tail from the front and rear renders (the top render's perspective
# narrows them), the middle from the top render.
_NOSE_W = [(40, 0.16), (44, 0.44), (50, 0.62), (60, 0.74), (75, 0.805), (95, 0.845), (125, 0.868),
           (160, 0.876)]
_TOP_W = [(208, 233), (238, 244), (268, 257), (298, 262), (330, 264), (370, 263.5), (430, 260),
          (600, 259), (700, 261.5), (800, 262), (900, 262), (1000, 259.5), (1100, 260.5)]
_TAIL_W = [(1180, 0.879), (1260, 0.877), (1330, 0.873), (1390, 0.868), (1440, 0.862),
           (1465, 0.85), (1485, 0.82), (1500, 0.775), (1508, 0.71), (1513, 0.57), (1515, 0.32)]
HALF = (_NOSE_W + [(_lerp(T_TO_SIDE, t), h * T_S) for t, h in _TOP_W if 200 < _lerp(T_TO_SIDE, t) < 1150]
        + _TAIL_W)
# Section shape along the car: belt width as a share of the half width, the top's squareness,
# the greenhouse's lean in, and the lower corner's squareness.
K_BELT = [(40, 0.97), (120, 0.985), (380, 0.985), (480, 0.95), (1200, 0.935), (1380, 0.955),
          (1515, 0.95)]
N_TOP = [(40, 4.5), (380, 5.0), (470, 3.8), (1230, 3.8), (1330, 4.4), (1515, 4.0)]
LEAN = [(40, 0.0), (400, 0.0), (480, 0.20), (1220, 0.20), (1340, 0.10), (1515, 0.0)]
N_LOW = [(40, 3.0), (200, 3.6), (1300, 3.6), (1515, 3.0)]


def stations():
    pxs = sorted({round(p, 1) for t in (TOP, BOTTOM, HALF, BELT) for p, _ in t})
    out = []
    for px in pxs:
        out.append(dict(x=X(px), zb=Z(px, _lerp(BOTTOM, px)), zt=Z(px, _lerp(TOP, px)),
                        zs=Z(px, _lerp(BELT, px)), zw=Z(px, _lerp(WIDEST, px)),
                        w=round(max(_lerp(HALF, px), 0.03), 4), kb=_lerp(K_BELT, px),
                        nt=_lerp(N_TOP, px), lean=_lerp(LEAN, px), nb=_lerp(N_LOW, px)))
    return out


# ---------------------------------------------------------------- the rows
rows = {
    "name": "KaiserOboe",
    "wheel": {"r": TYRE_R, "rim_r": RIM_R, "width": 0.225, "outer_y": 0.865,
              "front_x": X(WF), "rear_x": X(WR), "arch_r": round(TYRE_R + 0.03, 4)},
    "stations": stations(),
    # Side glass opening, outer edge of its black surround (side px).
    "dlo": side([(493, 237), (490, 221), (505, 204), (560, 169), (641, 125), (720, 98), (797, 82),
                 (870, 71), (937, 64), (1010, 61), (1076, 60), (1130, 62), (1163, 66), (1192, 76),
                 (1210, 91), (1224, 113), (1229, 133), (1227, 153), (1216, 172), (1197, 187),
                 (1100, 198), (1000, 207), (850, 215), (728, 223), (600, 230)]),
    "b_pillar": [X(915), X(942)],
    # Black sail where the mirror meets the door, front corner of the side glass (side px).
    "sail": side([(489, 240), (487, 222), (501, 205), (538, 200), (543, 240)]),
    # Windscreen: centre of its base, its top edge, and how wide the body-coloured A-pillar is.
    "screen": {"x_base": X(408), "x_top": X(792), "pillar": 0.065},
    "rear_glass": rear(half_loop([(513, 108), (300, 110), (283, 118), (263, 160), (246, 205),
                                  (233, 240), (241, 253), (513, 255)], R_C)),
    # Headlight: front render px (car's right, image left) and where it wraps onto the side.
    "head": front([(105, 285), (130, 278), (160, 280), (190, 290), (215, 305), (235, 325),
                   (245, 345), (250, 362), (240, 374), (200, 377), (160, 374), (120, 368), (95, 360),
                   (88, 350), (90, 330), (96, 305)]),
    "head_side": side([(60, 349), (66, 338), (82, 322), (99, 300), (117, 282), (112, 300),
                       (101, 326), (92, 342), (75, 350)]),
    "head_lenses": [[round((145 - F_C) * F_S, 4), Z(60, _lerp(F_TO_SIDE, 322)), 0.048],
                    [round((203 - F_C) * F_S, 4), Z(60, _lerp(F_TO_SIDE, 332)), 0.041]],
    # Black grille shield (front px, centre line round the car's right half).
    "shield": front(half_loop([(459, 322), (350, 322), (256, 324), (258, 360), (262, 400), (278, 467),
                               (287, 494), (296, 531), (305, 553), (322, 562), (459, 563)], F_C)),
    "shield_bar": front(half_loop([(459, 404), (298, 404), (290, 412), (296, 470), (306, 476),
                                   (459, 476)], F_C)),
    "intake": front([(75, 448), (130, 452), (200, 463), (245, 470), (251, 499), (258, 529),
                     (250, 548), (200, 553), (130, 548), (89, 540), (80, 499), (72, 465)]),
    "fog": front([(114, 504)])[0] + [round(17 * F_S, 4)],
    "chin": front(half_loop([(459, 574), (200, 574), (95, 571), (74, 580), (88, 598), (200, 604),
                             (459, 605)], F_C)),
    # Tail light: rear render px (car's left, image left) and where it wraps onto the side.
    "tail": rear([(198, 266), (215, 262), (240, 264), (270, 270), (300, 281), (316, 295),
                  (322, 315), (318, 338), (305, 354), (280, 357), (240, 357), (200, 357),
                  (184, 350), (180, 330), (182, 300), (188, 280)]),
    "tail_side": side([(1428, 205), (1466, 206), (1479, 236), (1480, 266), (1479, 287),
                       (1412, 291), (1421, 250)]),
    "brake3": rear([(440, 98), (586, 98), (586, 106), (440, 106)]),
    "spoiler": {"profile": side([(1282, 52), (1320, 48), (1350, 51), (1371, 61), (1369, 70),
                                 (1346, 84), (1320, 74), (1290, 62)]), "half": 0.60},
    "rear_lower": {"z": Z(1480, 398), "x": X(1425)},
    "exhaust": {"y": [round((R_C - 232) * R_S, 4), round((R_C - 282) * R_S, 4)],
                "z": Z(1490, _lerp(R_TO_SIDE, 600)), "r": 0.046},
    "strip": side([(440, 384), (1095, 367), (1095, 375), (440, 392)]),
    "mirror": {"x_back": X(490), "length": 0.13, "y_in": 0.80, "y_out": 0.975,
               "z": Z(465, 206), "h_in": 0.085, "h_out": 0.125},
    # Shut lines.
    "door": side([(926, 213), (922, 300), (919, 400), (915, 480), (896, 507), (470, 510),
                  (440, 501), (424, 440), (420, 360), (424, 290), (440, 251), (478, 239)]),
    "bonnet_front": front([(141, 280), (160, 281), (200, 288), (235, 312), (258, 321), (300, 322),
                           (459, 321), (618, 322), (660, 321), (683, 312), (718, 288), (758, 281),
                           (777, 280)]),
    "bonnet_side_y": 0.69,
    "tailgate": rear(across([(230, 245), (233, 253), (255, 263), (255, 357), (262, 400),
                             (276, 432), (300, 438), (513, 440)], R_C)),
    "head_back": X(140),
    "tail_front": X(1408),
    # Option spots for the badge and the name.
    "spots": {"grille_z": Z(60, _lerp(F_TO_SIDE, 343)), "bonnet_x": X(190),
              "tailgate_z": Z(1490, _lerp(R_TO_SIDE, 315)), "tailgate_low_z": Z(1490, _lerp(R_TO_SIDE, 410)),
              "name_y": round((R_C - 300) * R_S, 4), "name_z": Z(1490, _lerp(R_TO_SIDE, 383)),
              "quarter_x": X(1180), "quarter_z": Z(1180, 300)},
    # Ivan's picks from the viewer.
    "picks": {"frontbadge": 1, "rearbadge": 1, "name": 1},
}

if __name__ == "__main__":
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(rows, indent=1))
    print("wrote", out, "nose", X(NOSE), "tail", X(TAIL), "roof", Z(ROOF_PX, ROOF_PY),
          "lamp z", rows["tail"][1][1], rows["tail"][10][1])
