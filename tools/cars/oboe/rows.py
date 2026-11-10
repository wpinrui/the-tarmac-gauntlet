"""Section rows for the Kaiser Oboe (2009 VW Golf GTI 3-door), traced from Ivan's references.

Side view: Ivan's side render mirrored so the nose is on the left. Wheel centres at x 252 and
1272 (y 463 and 482), car x 40 to 1515. The render is a perspective shot from nearer the rear,
so each column gets its own scale s(px), set by the two wheel sizes, before it is used.
The wheel centres set the scale to the published 2578 mm wheelbase; the overhangs stretch to the
published 4216 mm length. Heights scale to the published 1466 mm with the ground one wheel radius
(225/45 R17) under the wheel centres. Half widths come from the top view, set to the published
1759 mm width. The rear shapes come from Ivan's rear photo, measured across its near half.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.
"""
import json
from pathlib import Path

import numpy as np

WHEELBASE, LENGTH, WIDTH, HEIGHT = 2.578, 4.216, 1.759, 1.466
TYRE_R = round((17 * 25.4 + 2 * 0.45 * 225) / 2000, 4)   # 225/45 R17
RIM_R = round(17 * 25.4 / 2000, 4)

# Side view (mirrored): front of the car is on the left.
PX_FRONT, PX_REAR, PX_WF, PX_WR = 40, 1515, 252, 1272
PY_WF, PY_WR = 463, 482
K = (140 / 130 - 1) / (PX_WR - PX_WF)   # the rear wheel reads 140 px across, the front 130


def s_of(px):
    return 1 + K * (px - PX_WF)


def flat(px):
    """Side px with the perspective taken out: distance along the car at one scale."""
    return PX_WF + np.log(s_of(np.asarray(px, float))) / K


def yc(px):
    return PY_WF + (px - PX_WF) * (PY_WR - PY_WF) / (PX_WR - PX_WF)


def _x_table():
    """px -> x. Inside the wheelbase one scale, outside another, blended smoothly so no
    kink lands in a body line. Solved so the wheelbase and length come out exact."""
    px = np.arange(PX_FRONT - 5, PX_REAR + 6, 0.5)
    f = flat(px)
    fwf, fwr = flat(PX_WF), flat(PX_WR)
    sig = lambda t: 1 / (1 + np.exp(-t))
    outer = sig((fwf - f) / 25) + sig((f - fwr) / 25)   # 1 in the overhangs
    df = np.gradient(f)

    def integ(s):
        g = s * df
        return np.concatenate([[0], np.cumsum((g[1:] + g[:-1]) / 2)])
    a, b = integ(1 - outer), integ(outer)
    i = lambda p: int(round((p - px[0]) * 2))
    A = np.array([[a[i(PX_WR)] - a[i(PX_WF)], b[i(PX_WR)] - b[i(PX_WF)]],
                  [a[i(PX_REAR)] - a[i(PX_FRONT)], b[i(PX_REAR)] - b[i(PX_FRONT)]]])
    si, so = np.linalg.solve(A, [WHEELBASE, LENGTH])
    x = -(si * a + so * b)
    x -= (x[i(PX_WF)] + x[i(PX_WR)]) / 2
    return px, x


_PX, _XS = _x_table()
PX_ROOF, PY_ROOF = 1040, 43
SZ = (HEIGHT - TYRE_R) / ((yc(PX_ROOF) - PY_ROOF) / s_of(PX_ROOF))


def X(px):
    return round(float(np.interp(px, _PX, _XS)), 4)


def Z(px, py):
    return round(TYRE_R + (yc(px) - py) / s_of(px) * SZ, 4)


# Top view: half width px -> metres; top view x maps onto side px through nose, mirror, tail.
S_TOP = WIDTH / 2 / 262.0
_TOP_X = [(88, 40), (505, 465), (1346, 1515)]


def T2S(t):
    a, b = zip(*_TOP_X)
    return float(np.interp(t, a, b))


# Front view: face centre x 459, half width 400 px; its y maps onto side view y at the nose.
S_FRONT = WIDTH / 2 / 400.0
_FRONT_Y = [(210, 205), (283, 283), (377, 350), (470, 420), (600, 491), (640, 505)]


def FY(px):
    return round((px - 459) * S_FRONT, 4)


def FZ(py):
    a, b = zip(*_FRONT_Y)
    return Z(60, float(np.interp(py, a, b, left=b[0] + (py - a[0]), right=b[-1] + (py - a[-1]) * 0.7)))


# Rear photo: badge at x 2884; its near half measured to the body corner at x 1930.
S_REAR = 0.80 / (2884 - 1930)
_REAR_Y = [(450, 57), (553, 86), (905, 186), (940, 198), (1215, 292), (1395, 345), (1590, 398),
           (1900, 500)]


def RY(px):
    return round((2884 - px) * S_REAR, 4)


def RZ(py):
    a, b = zip(*_REAR_Y)
    return Z(1490, float(np.interp(py, a, b)))


# Profiles along the side view, picked by hand on the side and top views.
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
_HALF_T = [(88, 36), (95, 75), (103, 108), (118, 160), (133, 184), (148, 200), (163, 212),
           (178, 221), (193, 228), (208, 233), (238, 244), (268, 257), (298, 262), (330, 264),
           (370, 263.5), (430, 260), (600, 259), (700, 261.5), (800, 262), (900, 262),
           (1000, 259.5), (1100, 260.5), (1153, 258.5), (1183, 254), (1213, 245.5), (1243, 239.5),
           (1273, 229), (1288, 220.5), (1303, 204.5), (1318, 176.5), (1333, 113.5), (1346, 30)]
# The top view's nose and tail read narrow in its perspective; the front and rear views set them.
_NOSE = [(40, 0.15), (44, 0.42), (50, 0.60), (60, 0.72), (75, 0.79), (95, 0.835), (125, 0.862),
         (160, 0.874)]
_TAIL = [(1180, 0.879), (1260, 0.877), (1330, 0.873), (1390, 0.868), (1440, 0.862), (1465, 0.85), (1485, 0.82), (1500, 0.77), (1508, 0.70), (1513, 0.56),
         (1515, 0.30)]
HALF = ([(px, w / S_TOP) for px, w in _NOSE]
        + [(round(T2S(t), 1), h) for t, h in _HALF_T if 200 < T2S(t) < 1150] + [(px, w / S_TOP) for px, w in _TAIL])
# Shoulder: where the side turns in to the bonnet, the glass or the tailgate.
SHOULDER = [(40, 345), (60, 318), (100, 296), (120, 290), (160, 282), (200, 272), (260, 260),
            (330, 250), (400, 241), (489, 237), (600, 233), (800, 225), (1000, 213),
            (1150, 201), (1220, 196), (1300, 202), (1350, 212), (1400, 232), (1440, 258),
            (1470, 290), (1495, 340), (1515, 430)]
WIDEST = [(40, 410), (100, 420), (300, 400), (1100, 395), (1400, 405), (1515, 415)]
K_SHOULDER = [(40, 0.94), (100, 0.975), (160, 0.975), (330, 0.96), (420, 0.92), (500, 0.90),
              (1200, 0.90), (1360, 0.94), (1450, 0.955), (1515, 0.93)]
K_TOP = [(40, 0.80), (100, 0.84), (250, 0.84), (405, 0.82), (760, 0.70), (1100, 0.70),
         (1250, 0.68), (1350, 0.74), (1420, 0.78), (1515, 0.62)]
CROWN = [(40, 0.01), (100, 0.025), (405, 0.05), (760, 0.045), (1150, 0.04), (1300, 0.05),
         (1515, 0.03)]


def _at(table, px):
    a, b = zip(*table)
    return float(np.interp(px, a, b))


def stations():
    pxs = sorted({p for t in (TOP, BOTTOM, HALF, SHOULDER) for p, _ in t})
    out = []
    for px in pxs:
        w = round(_at(HALF, px) * S_TOP, 4)
        zb, zt = Z(px, _at(BOTTOM, px)), Z(px, _at(TOP, px))
        out.append(dict(x=X(px), zb=zb, zt=zt, w=max(w, 0.02),
                        zs=Z(px, _at(SHOULDER, px)), zw=Z(px, _at(WIDEST, px)),
                        ks=_at(K_SHOULDER, px), kt=_at(K_TOP, px), dt=_at(CROWN, px)))
    return out


def side(pts):
    return [[X(px), Z(px, py)] for px, py in pts]


def front(pts):
    return [[FY(px), FZ(py)] for px, py in pts]


def rear(pts):
    return [[RY(px), RZ(py)] for px, py in pts]


def mirror_x(pts, c):
    """Close a left-half trace into a full loop about centre c."""
    return pts + [(2 * c - p, q) for p, q in pts[-2:0:-1]]


data = {
    "wheel": {"r": TYRE_R, "rim_r": RIM_R, "front_x": X(PX_WF), "rear_x": X(PX_WR),
              "width": 0.225, "outer_y": 0.865, "arch_r": round(TYRE_R + 0.035, 4)},
    "stations": stations(),
    # Side glass opening (the black surround's outer edge) in side px.
    "dlo": side([(493, 236), (490, 220), (505, 203), (560, 168), (641, 124), (720, 97),
                 (797, 81), (870, 70), (937, 63), (1010, 60), (1076, 59), (1130, 61),
                 (1163, 65), (1192, 75), (1210, 90), (1224, 112), (1229, 132), (1227, 152),
                 (1216, 171), (1197, 186), (1100, 197), (1000, 206), (850, 214), (728, 222),
                 (600, 229)]),
    "b_pillar": side([(915, 55), (942, 55), (942, 228), (915, 228)]),
    # Windscreen: top edge, base at the centre and at the A-pillar foot, in side px.
    "screen": {"x_top": X(790), "x_base": X(408), "x_side": X(470)},
    # Rear glass: height range from the photo, rear view.
    # Tail light where it wraps onto the side, side px.
    "tail_side": side([(1428, 202), (1468, 205), (1479, 236), (1480, 266), (1479, 287),
                       (1410, 292), (1420, 250)]),
    "rear_glass": rear([(2060, 556), (2060, 905)]),
    # Headlight, front view px, car's right; mirrored for the left.
    "head": front([(105, 285), (130, 278), (160, 280), (190, 290), (215, 305), (235, 325),
                   (245, 345), (250, 362), (240, 374), (200, 377), (160, 374), (120, 368),
                   (95, 360), (88, 350), (90, 330), (96, 305)]),
    # Projectors inside the headlight: centre and radius in front px (our own layout).
    "projectors": [[FY(145), FZ(322), round(19 * S_FRONT, 4)], [FY(203), FZ(332), round(16 * S_FRONT, 4)]],
    # The black shield round the grille, front view px, left half to the centre line.
    "mouth": front([(459, 322), (350, 322), (256, 324), (258, 360), (262, 400), (278, 467),
                    (287, 494), (296, 531), (305, 553), (322, 562), (459, 563)]),
    "slats": [],
    # Gloss black panel across the shield between the upper and lower grilles.
    "panel": front([(459, 404), (298, 404), (290, 412), (296, 470), (306, 476), (459, 476)]),
    # Side intake round each fog lamp, car's right.
    "intake": front([(75, 448), (130, 452), (200, 463), (245, 470), (251, 499), (258, 529),
                     (250, 548), (200, 553), (130, 548), (89, 540), (80, 499), (72, 465)]),
    "fog": {"c": [FY(114), FZ(504)], "r": round(17 * S_FRONT, 4)},
    "chin": front([(459, 574), (200, 574), (95, 571), (74, 580), (88, 598), (200, 604),
                   (459, 605)]),
    # Tail light, rear photo px, near side; mirrored for the other.
    "tail": rear([(2085, 940), (2200, 938), (2303, 946), (2380, 962), (2432, 989), (2490, 1030),
                  (2513, 1077), (2500, 1130), (2465, 1166), (2410, 1188), (2352, 1198),
                  (2200, 1206), (2050, 1211), (1948, 1214), (1965, 1174), (2013, 1077),
                  (2061, 981)]),
    "exhaust": {"y": [RY(2385), RY(2445)], "z": RZ(1860), "rw": 0.037, "rz": 0.037},
    "spoiler": {"outline": side([(1285, 52), (1320, 49), (1350, 52), (1370, 62), (1368, 70),
                                 (1345, 84), (1320, 74), (1290, 62)]), "half": 0.62},
    "strip": side([(440, 383), (1095, 366), (1095, 375), (440, 392)]),
    # Door mirror: housing from its flat glass face forward, sail plate on the door in side px.
    "mirror": {"x_rear": X(490), "depth": 0.12, "y_in": 0.80, "y_out": 0.975, "z": Z(465, 208),
               "h_in": 0.085, "h_out": 0.13,
               "sail": side([(493, 238), (490, 222), (503, 206), (540, 200), (545, 238)])},
    # Shut lines in side px (door), front view (bonnet front), rear photo (tailgate).
    "door_shut": side([(926, 212), (922, 300), (919, 400), (915, 480), (896, 506), (470, 509),
                       (440, 500), (424, 440), (420, 360), (424, 290), (440, 250), (478, 238)]),
    "bonnet_front": front([(141, 280), (160, 281), (200, 288), (235, 312), (258, 321), (300, 322),
                           (459, 321), (618, 322), (660, 321), (683, 312), (718, 288),
                           (758, 281), (777, 280)]),
    "tailgate_shut": rear(mirror_x([(2080, 925), (2290, 944), (2275, 1080), (2260, 1200),
                                    (2300, 1340), (2390, 1395), (2884, 1398)], 2884)),
    "bonnet": {"tip_y": 0.70, "hb": 0.68},
    # Black lower rear bumper: below this height, behind this point (side px), wrapping the corner.
    "rear_lower": {"z": Z(1480, 398), "x": X(1425)},
}

data["head_back"] = X(138)    # rear tip of the headlight in the side view
data["tail_front"] = X(1410)  # front tip of the tail light in the side view
z_sp = Z(1368, 66)
data["brake3"] = [[0.14, z_sp - 0.008], [-0.14, z_sp - 0.008], [-0.14, z_sp + 0.006], [0.14, z_sp + 0.006]]
data["opts"] = {"nose_z": FZ(343), "bonnet_x": X(190), "rear_z1": RZ(1050), "rear_z2": RZ(1300),
                "name_y": 0.25, "name_z": RZ(1330), "quarter_x": X(1180), "quarter_z": Z(1180, 300)}

# Ivan's picks from the viewer.
data["picks"] = {"frontbadge": 1, "rearbadge": 1, "name": 1}

if __name__ == "__main__":
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(data, indent=1))
    print("wrote", out, "front x", data["wheel"]["front_x"], "rear x", data["wheel"]["rear_x"],
          "nose", X(PX_FRONT), "tail", X(PX_REAR), "roof", Z(PX_ROOF, PY_ROOF),
          "sill", Z(700, 523), "SZ", round(SZ, 5))
