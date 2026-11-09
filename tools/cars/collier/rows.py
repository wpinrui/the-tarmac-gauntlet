"""Section rows for the Liberty Collier (2008 Peugeot 207 3-door), traced from Ivan's renders.

Side view pixels: wheel centres at x 270 and 1241, wheel centre height y 432.5, car x 40 to 1436.
The wheel centres set the scale to the published 2540 mm wheelbase; the overhangs stretch to
the published 4030 mm length. Heights scale to the published 1472 mm with the ground one
wheel radius (205/45 R17) under the wheel centres. Half widths come from the top view,
set to the published 1720 mm width.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.
"""
import json
from pathlib import Path

import numpy as np

WHEELBASE, LENGTH, WIDTH, HEIGHT = 2.54, 4.03, 1.72, 1.472
TYRE_R = round((17 * 25.4 + 2 * 0.45 * 205) / 2000, 4)   # 205/45 R17
RIM_R = round(17 * 25.4 / 2000, 4)

# Side view: front of the car is on the left.
PX_FRONT, PX_REAR, PX_WF, PX_WR = 40, 1436, 270, 1241
PY_WHEEL, PY_ROOF = 432.5, 59


def _x_table():
    """px -> x. Inside the wheelbase one scale, outside another, blended smoothly so no
    kink lands in a body line. Solved so the wheelbase and length come out exact."""
    px = np.arange(PX_FRONT - 5, PX_REAR + 6, 0.5)
    sig = lambda t: 1 / (1 + np.exp(-t))
    outer = sig((PX_WF - px) / 25) + sig((px - PX_WR) / 25)   # 1 in the overhangs
    def integ(s):
        return np.concatenate([[0], np.cumsum((s[1:] + s[:-1]) / 2 * 0.5)])
    a, b = integ(1 - outer), integ(outer)   # x = -(Si*a + So*b) + c
    i = lambda p: int(round((p - px[0]) * 2))
    # wheelbase and length equations in Si, So
    A = np.array([[a[i(PX_WR)] - a[i(PX_WF)], b[i(PX_WR)] - b[i(PX_WF)]],
                  [a[i(PX_REAR)] - a[i(PX_FRONT)], b[i(PX_REAR)] - b[i(PX_FRONT)]]])
    si, so = np.linalg.solve(A, [WHEELBASE, LENGTH])
    x = -(si * a + so * b)
    x -= (x[i(PX_WF)] + x[i(PX_WR)]) / 2
    return px, x


_PX, _XS = _x_table()
SZ = (HEIGHT - TYRE_R) / (PY_WHEEL - PY_ROOF)


def X(px):
    return round(float(np.interp(px, _PX, _XS)), 4)


def Z(py):
    return round(TYRE_R + (PY_WHEEL - py) * SZ, 4)


S_TOP = WIDTH / 2 / 284.0           # top view half width px -> metres


def W(px_half):
    return round(px_half * S_TOP, 4)


# Front view: centre x 413, half width 337 px at the bumper; its y maps onto side view y.
S_FRONT = WIDTH / 2 / 337.0


def FY(px):
    return round((px - 413) * S_FRONT, 4)


def FZ(py):
    return Z(59 + (py - 52) * (505 - 59) / (600 - 52))


# Rear view: centre x 490, half width 449.5 px; perspective, so y maps through features.
S_REAR = WIDTH / 2 / 449.5 * 0.95
_REAR_Y = [(42, 81), (230, 222), (398, 310), (458, 385), (540, 450), (610, 500), (660, 540)]


def RY(px):
    return round((490 - px) * S_REAR, 4)


def RZ(py):
    a, b = zip(*_REAR_Y)
    return Z(float(np.interp(py, a, b)))


# Profiles along the side view, picked by hand on the side and top views.
TOP = [(40, 376), (44, 362), (50, 352), (60, 344), (80, 326), (100, 302), (120, 283), (140, 272),
       (160, 265), (180, 258), (200, 250), (240, 235), (280, 222), (320, 213), (360, 207),
       (395, 200), (420, 185), (460, 164), (500, 146), (540, 128), (580, 111), (620, 96),
       (660, 81), (700, 71), (740, 64), (800, 60), (860, 59), (920, 60), (980, 64), (1040, 69),
       (1100, 75), (1150, 81), (1190, 89), (1215, 100), (1250, 128), (1290, 163), (1330, 200),
       (1350, 214), (1365, 226), (1378, 246), (1388, 275), (1395, 308), (1402, 332),
       (1415, 350), (1425, 368), (1432, 395), (1436, 430)]
BOTTOM = [(40, 440), (46, 468), (55, 490), (70, 502), (100, 510), (160, 510), (400, 506),
          (1100, 506), (1380, 506), (1405, 500), (1420, 490), (1430, 476), (1436, 455)]
# The tail reads narrow in the perspective top view, so the rear view sets its width.
HALF = [(40, 22), (45, 60), (50, 96), (60, 154), (70, 182), (80, 198), (100, 222), (120, 242),
        (150, 260), (180, 268), (220, 275), (270, 281), (320, 282), (380, 282), (500, 282),
        (700, 282), (900, 283), (1100, 283.5), (1200, 285), (1250, 284), (1300, 280.5),
        (1350, 278), (1380, 268), (1400, 245), (1415, 210), (1425, 165), (1432, 105), (1436, 30)]
# Shoulder: where the side turns in to the bonnet, the glass or the tailgate.
SHOULDER = [(40, 400), (60, 370), (100, 318), (150, 283), (200, 263), (260, 246), (320, 236),
            (380, 226), (410, 228), (445, 245), (560, 246), (920, 225), (1100, 213),
            (1200, 207), (1300, 213), (1350, 226), (1380, 262), (1400, 340), (1420, 378),
            (1436, 440)]
# Height of the widest point, the shoulder width and the crown width as fractions of the half width.
WIDEST = [(40, 412), (100, 420), (300, 405), (1100, 400), (1400, 420), (1436, 448)]
K_SHOULDER = [(40, 0.88), (100, 0.95), (160, 0.97), (330, 0.96), (420, 0.90), (500, 0.88), (1200, 0.88), (1360, 0.92),
              (1436, 0.86)]
K_TOP = [(40, 0.50), (100, 0.62), (250, 0.70), (395, 0.80), (720, 0.667), (1100, 0.66),
         (1215, 0.63), (1350, 0.72), (1400, 0.76), (1436, 0.60)]
CROWN = [(40, 0.02), (100, 0.06), (395, 0.07), (720, 0.045), (1150, 0.04), (1300, 0.05),
         (1436, 0.03)]


def _at(table, px):
    a, b = zip(*table)
    return float(np.interp(px, a, b))


def stations():
    pxs = sorted({p for t in (TOP, BOTTOM, HALF, SHOULDER) for p, _ in t})
    out = []
    for px in pxs:
        w = W(_at(HALF, px))
        zb, zt = Z(_at(BOTTOM, px)), Z(_at(TOP, px))
        out.append(dict(x=X(px), zb=zb, zt=zt, w=max(w, 0.02),
                        zs=Z(_at(SHOULDER, px)), zw=Z(_at(WIDEST, px)),
                        ks=_at(K_SHOULDER, px), kt=_at(K_TOP, px), dt=_at(CROWN, px)))
    return out


def side(pts):
    return [[X(px), Z(py)] for px, py in pts]


def front(pts):
    return [[FY(px), FZ(py)] for px, py in pts]


def rear(pts):
    return [[RY(px), RZ(py)] for px, py in pts]


data = {
    "wheel": {"r": TYRE_R, "rim_r": RIM_R, "front_x": X(PX_WF), "rear_x": X(PX_WR),
              "width": 0.205, "outer_y": 0.835, "arch_r": round(TYRE_R + 0.035, 4)},
    "stations": stations(),
    # Side glass opening (the black surround's outer edge) in side px.
    "dlo": side([(452, 238), (447, 224), (464, 211), (510, 183), (572, 146), (634, 120),
                 (696, 104), (755, 89), (817, 84), (930, 88), (1005, 99), (1067, 103),
                 (1130, 112), (1146, 128), (1117, 162), (1067, 194), (1011, 214), (924, 226),
                 (680, 232)]),
    "b_pillar": side([(898, 80), (935, 80), (935, 240), (898, 240)]),
    # Windscreen surround in plan: (side px, y metres).
    # Windscreen: top edge, base at the centre and at the A-pillar foot, in side px.
    "screen": {"x_top": X(712), "x_base": X(401), "x_side": X(440)},
    # Rear glass surround, rear view px.
    "rear_glass": rear([(190, 84), (790, 84), (812, 160), (806, 196), (760, 214), (220, 214),
                        (174, 196), (168, 160)]),
    # Headlight, front view px, car's right; mirrored for the left.
    "head": front([(130, 265), (160, 293), (180, 313), (207, 343), (230, 367), (247, 390),
                   (252, 400), (217, 390), (180, 382), (140, 373), (110, 363), (93, 350),
                   (90, 327), (92, 312), (98, 300), (110, 283), (120, 272)]),
    # Mouth of the front bumper, front view px, left half to the centre line.
    "mouth": front([(413, 437), (300, 437), (200, 439), (140, 441), (110, 445), (100, 455),
                    (102, 470), (110, 484), (126, 494), (150, 495), (185, 486), (215, 486),
                    (232, 498), (238, 525), (252, 541), (290, 546), (413, 546)]),
    "slats": [[FZ(py), FY(x0)] for py, x0 in [(433, 205), (452, 170), (488, 240), (501, 242),
                                              (514, 246), (527, 252)]],
    "fog": {"c": [FY(202), FZ(510)], "r": round(26 * S_FRONT, 4)},
    "chin": front([(413, 578), (200, 578), (130, 576), (118, 584), (135, 594), (200, 598),
                   (413, 599)]),
    # Tail light, rear view px, car's right; mirrored for the left.
    "tail": rear([(80, 226), (100, 232), (118, 250), (140, 290), (160, 340), (182, 398),
                  (150, 392), (110, 378), (70, 368), (48, 350), (45, 320), (52, 280),
                  (62, 245)]),
    "rear_band": rear([(490, 458), (78, 458), (66, 474), (68, 526), (86, 540), (490, 540)]),
    "exhaust": {"y": [RY(652), RY(716)], "z": RZ(606), "rw": 0.045, "rz": 0.03},
    # Third brake light: centred across the car, in the middle of the spoiler's rear face.
    "brake3": [[RY(px), Z(py)] for px, py in [(433, 83), (547, 83), (547, 88), (433, 88)]],
    "spoiler": {"outline": side([(1120, 74), (1170, 74), (1215, 76), (1238, 81), (1234, 90),
                                 (1205, 106), (1170, 98), (1125, 86)]), "half": 0.60},
    "strip": side([(437, 378), (1100, 352), (1100, 360), (437, 387)]),
    # Door mirror: housing from its flat glass face forward, sail plate on the door in side px.
    "mirror": {"x_rear": X(560), "depth": 0.11, "y_in": 0.75, "y_out": 0.94, "z": Z(212),
               "h_in": 0.075, "h_out": 0.115,
               "sail": side([(452, 240), (447, 224), (470, 206), (560, 206), (566, 240)])},
    # Shut lines in side px (door) and plan (bonnet), rear view (tailgate).
    "door_shut": side([(925, 226), (921, 300), (918, 400), (912, 470), (880, 474), (470, 474),
                       (448, 468), (430, 420), (418, 340), (424, 280), (440, 240)]),
    "bonnet_front": front([(196, 336), (300, 331), (413, 333), (526, 331), (630, 336)]),
    "tailgate_shut": rear([(166, 214), (196, 300), (222, 400), (236, 444), (744, 444),
                           (758, 400), (784, 300), (814, 214)]),
}

data["head_back"] = X(232)    # rear tip of the headlight in the side view
data["tail_front"] = X(1288)  # front tip of the tail light in the side view
data["opts"] = {"nose_z": FZ(372), "bonnet_x": X(170), "rear_z1": RZ(262), "rear_z2": RZ(350),
                "name_y": round(RY(262) - 0.06, 4),  # clear of the tailgate gap
                "name_z": RZ(393), "quarter_x": X(1060), "quarter_z": Z(300)}

# Ivan's picks from the viewer.
data["picks"] = {"frontbadge": 1, "rearbadge": 1, "name": 2}

if __name__ == "__main__":
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(data, indent=1))
    print("wrote", out, "front x", data["wheel"]["front_x"], "rear x", data["wheel"]["rear_x"],
          "nose", X(PX_FRONT), "tail", X(PX_REAR), "roof", Z(PY_ROOF), "sill", Z(506))
