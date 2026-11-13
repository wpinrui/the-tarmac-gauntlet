"""Traced rows for the Kaiser Firefly, based on the 1998 Volkswagen New Beetle.

Reference: Ivan's 3D model of a 1998 New Beetle (a .glb, kept out of the repo). It is rendered
orthographically from the side, front, rear and top on a plain background at 30000 pixels per
model unit, each material one flat colour (and once more shaded by the surface's facing, so creases
and shut lines show), and every row below is picked off those renders like a drawing. Nothing is
taken from its meshes. Points are written in render pixels:
  side render: px along the car (nose left), py down;
  top render: px along the car as the side, u out from the centre line;
  front and rear renders: u out from the centre line (the car's left), py as the side render.
The round head and tail lamps are traced once more on renders looking straight at each lamp
(LAMP_VIEWS), and each lamp is that outline pushed along the view.

Scale: the wheel centres, 757.5 px apart, set the scale from the published 2508 mm wheelbase.
Both overhangs stretch by one factor to the published 4081 mm length. The ground sits one
published tyre radius (205/55 R16) under the wheel centres. Half widths are set so the widest
body point is the published 1724 mm. Heights keep the model's proportions.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

The body is a narrow hull (bumpers, bonnet, doors, boot and the domed greenhouse) with the four
round wings added onto its sides: each wing is its own section, joined to the hull's at every
station, so its edge shows as a crisp step down onto the door.

Run: python3 rows.py  -> rows.json
"""
import json
from pathlib import Path

import numpy as np
from scipy.interpolate import PchipInterpolator

WHEELBASE, LENGTH, WIDTH = 2.508, 4.081, 1.724
TYRE_R = round((16 * 25.4 + 2 * 0.55 * 205) / 2000, 4)    # 205/55 R16
RIM_R = round(16 * 25.4 / 2000, 4)
PX_WF, PX_WR, PY_W = 272.0, 1029.5, 466.5                  # wheel centres on the side render
PX_NOSE, PX_TAIL = 24.0, 1248.0                            # the body's ends
HALF_MAX = 258.5                                            # widest half width, top render
S = WHEELBASE / (PX_WR - PX_WF)
K = (LENGTH - WHEELBASE) / ((PX_WF - PX_NOSE + PX_TAIL - PX_WR) * S)
SH = WIDTH / 2 / HALF_MAX


def X(px):
    """Build x of a side px: the wheelbase at scale, the overhangs stretched by K."""
    px = float(px)
    if px < PX_WF:
        return round(WHEELBASE / 2 + (PX_WF - px) * S * K, 4)
    if px > PX_WR:
        return round(-WHEELBASE / 2 - (px - PX_WR) * S * K, 4)
    return round(WHEELBASE / 2 - (px - PX_WF) * S, 4)


def Z(py):
    return round(float(TYRE_R + (PY_W - py) * S), 4)


def H(u):
    return round(float(u * SH), 4)


def L(d):
    return round(float(d * S), 4)


def side(pts):
    return [[X(x), Z(z)] for x, z in pts]


def end(pts):
    return [[H(u), Z(z)] for u, z in pts]


def plan(pts):
    return [[X(x), H(u)] for x, u in pts]


# ---------------------------------------------------------------- hull rows
# Silhouette top along the middle: bumper, bonnet, windscreen, the domed roof, the rear glass,
# the boot and the rear bumper's face.
TOP = [(24, 436), (28, 419), (32, 415), (40, 412), (48, 405), (56, 389), (60, 384), (70, 365), (80, 350),
       (90, 341), (100, 334), (120, 321), (140, 311), (160, 303), (180, 296), (200, 291), (220, 287), (240, 283),
       (260, 281), (280, 276), (300, 269), (320, 254), (340, 239), (360, 224), (380, 210), (400, 196), (420, 183),
       (440, 172), (460, 161), (480, 152), (500, 143), (520, 137), (540, 131), (560, 127), (580, 123), (600, 120),
       (620, 118), (640, 116), (660, 115), (680, 114), (720, 114), (740, 115), (760, 116), (780, 119), (800, 121),
       (820, 124), (840, 128), (860, 133), (880, 139), (900, 145), (920, 152), (940, 161), (960, 171), (980, 182),
       (1000, 194), (1020, 206), (1040, 219), (1060, 233), (1080, 247), (1100, 260), (1120, 272), (1140, 288),
       (1160, 306), (1180, 333), (1196, 355), (1204, 361), (1212, 369), (1220, 380), (1228, 386), (1236, 393),
       (1240, 400), (1244, 413), (1248, 425)]
# Silhouette bottom, carried straight across the arches.
BOTTOM = [(24, 456), (28, 464), (36, 470), (44, 491), (52, 512), (60, 515), (100, 519), (180, 520), (360, 525),
          (860, 522), (1120, 507), (1160, 504), (1180, 503), (1196, 495), (1212, 490), (1228, 488), (1236, 476),
          (1240, 464), (1244, 451), (1248, 442)]
# The hull's half width (top render): the bumpers' corners, the body under the wings, the doors.
HALF = [(24, 12), (30, 90), (40, 140), (56, 179), (72, 201), (88, 216), (104, 228), (120, 237), (136, 243),
        (152, 246), (170, 246), (200, 240), (260, 236), (330, 238), (380, 243), (420, 245), (460, 245),
        (520, 246), (600, 249), (700, 250), (760, 250), (800, 249), (840, 247), (870, 244), (900, 238),
        (1000, 236), (1080, 238), (1112, 245), (1128, 248), (1144, 246), (1160, 240), (1176, 231), (1192, 218),
        (1208, 201), (1224, 172), (1240, 110), (1248, 40)]
# Height of the hull's widest point, and how far its bottom tucks in from it (front and rear renders).
WIDEST = [(24, 446), (60, 440), (150, 430), (300, 400), (450, 385), (850, 385), (1100, 420), (1248, 440)]
TUCK = [(24, 6), (100, 18), (200, 22), (450, 20), (850, 20), (1150, 18), (1248, 8)]
# Shoulder: where the side turns in to the top. Over the bonnet it is the bonnet's edge, along the
# cabin the belt, over the boot the boot lid's edge. Height (side render) and half width (top render).
SHOULDER = [(24, 438), (40, 420), (60, 392), (72, 373), (85, 357), (100, 348), (130, 331), (160, 318),
            (200, 309), (250, 304), (290, 303), (335, 301), (380, 294), (440, 288), (970, 281), (1000, 281),
            (1040, 279), (1080, 276), (1110, 274), (1140, 292), (1170, 320), (1196, 352), (1220, 385),
            (1248, 430)]
SHOULDER_W = [(24, 8), (40, 60), (60, 80), (75, 90), (85, 118), (100, 138), (130, 158), (170, 172), (220, 184),
              (280, 200), (335, 208), (380, 222), (440, 232), (700, 233), (950, 230), (1000, 225), (1040, 205),
              (1080, 180), (1110, 156), (1140, 150), (1170, 140), (1196, 118), (1220, 90), (1248, 20)]
# Deck height at the middle under the greenhouse (hidden), joined to the cowl and the boot.
DECK = [(286, 282), (400, 284), (1000, 282), (1110, 262)]

# Greenhouse, from the windscreen's foot to past the rear glass's foot, where the C-pillars fade
# into the boot lid. Glass foot (u) along the cabin, and the roof edge in side (px, py) and plan
# (px, u): the A-pillar, the roof's side and the C-pillar.
GH = (286, 1110)
GLASS_FOOT = [(286, 199), (335, 206), (380, 218), (440, 227), (700, 228), (950, 225), (1000, 219), (1040, 200),
              (1080, 175), (1110, 151)]
ROOF_Z = [(286, 282), (335, 279), (380, 240), (437, 191), (470, 163), (500, 165), (540, 165), (600, 155),
          (650, 149), (700, 146), (740, 146), (780, 152), (830, 165), (880, 185), (930, 211), (985, 247),
          (1040, 265), (1110, 272)]
ROOF_W = [(286, 198), (335, 198), (380, 186), (437, 178), (470, 179), (500, 181), (540, 185), (600, 188),
          (740, 188), (850, 186), (950, 180), (1040, 165), (1110, 152)]

# ---------------------------------------------------------------- wings
# Each wing at stations along the car: its top (side render, the crease where it meets the bonnet,
# the door or the boot), its outer half width (top render), the half width of its crown, the height
# of its widest point (front and rear renders) and its bottom. Where the top falls to the bottom the
# wing ends, so its edge runs down on the slant like the reference's.
FRONT_WING = [  # px, top, out, crown, widest, bottom
    (45, 430, 140, 108, 445, 512), (60, 400, 180, 138, 430, 514), (80, 365, 205, 162, 410, 515),
    (100, 345, 224, 178, 395, 516), (130, 328, 241, 190, 385, 516), (160, 316, 250, 196, 378, 516),
    (200, 306, 256, 202, 375, 505), (250, 300, 258, 208, 375, 508), (300, 301, 258, 214, 375, 512),
    (330, 306, 258, 218, 378, 515), (355, 320, 257, 223, 383, 519), (380, 345, 256, 228, 393, 522),
    (400, 380, 256, 232, 410, 524), (415, 420, 256, 235, 440, 525), (425, 470, 256, 237, 480, 525),
    (432, 524, 256, 238, 510, 525)]
REAR_WING = [
    (858, 522, 246, 236, 520, 522), (862, 480, 250, 236, 470, 522), (870, 430, 253, 236, 420, 522),
    (885, 390, 256, 236, 385, 522), (905, 360, 257, 232, 372, 521), (930, 333, 258, 226, 370, 520),
    (960, 312, 258, 220, 370, 518), (1000, 298, 258, 212, 370, 515), (1040, 293, 258, 205, 372, 511),
    (1080, 298, 257, 200, 375, 508), (1110, 308, 254, 196, 378, 506), (1140, 322, 247, 192, 382, 505),
    (1170, 340, 234, 185, 390, 503), (1196, 358, 215, 172, 400, 495), (1215, 375, 195, 150, 410, 490),
    (1228, 430, 160, 130, 450, 488)]
WING_TUCK = 16      # px the wing's bottom rolls under from its widest point


def table(rows):
    a = np.array(rows, float)
    return PchipInterpolator(a[:, 0], a[:, 1], extrapolate=True)


def wing_stations(rows):
    a = np.array(rows, float)
    xs = sorted(set(a[:, 0]) | set(np.arange(a[0, 0], a[-1, 0], 8.0)))
    cols = [PchipInterpolator(a[:, 0], a[:, k]) for k in range(1, 6)]
    return [dict(x=X(px), zt=Z(cols[0](px)), w=H(cols[1](px)), wc=H(cols[2](px)), zw=Z(cols[3](px)),
                 zb=Z(cols[4](px)), tuck=H(WING_TUCK)) for px in xs]


def stations():
    """Each station's hull values in metres, at every px any row was picked at."""
    t = {k: table(v) for k, v in dict(top=TOP, bot=BOTTOM, half=HALF, zw=WIDEST, tuck=TUCK, zs=SHOULDER,
                                       ws=SHOULDER_W, deck=DECK, rz=ROOF_Z, rw=ROOF_W, wg=GLASS_FOOT).items()}
    xs = sorted({float(x) for rows in (TOP, BOTTOM, HALF, SHOULDER) for x, _ in rows}
                | {float(x) for x in np.arange(30, 1245, 12)})
    out = []
    for px in xs:
        f = {k: float(v(px)) for k, v in t.items()}
        top = f["top"]
        in_gh = GH[0] < px < GH[1]
        deck = max(top, f["deck"]) if in_gh else top          # py grows downward
        st = dict(x=X(px), zb=Z(f["bot"]), w=H(f["half"]), zw=Z(f["zw"]), tuck=H(f["tuck"]),
                  zs=Z(max(f["zs"], deck + 3)), ws=H(f["ws"]), zd=Z(deck))
        if in_gh:
            st.update(zt=Z(top), zr=Z(max(f["rz"], top + 1)), wr=H(f["rw"]), wg=H(f["wg"]))
        out.append(st)
    return out


# ---------------------------------------------------------------- glass (side render)
def belt(px):
    """The belt line: one straight rise from the front of the door to the quarter glass's end."""
    return 287.0 - (px - 440.0) * 6.0 / 530.0


# The side window opening (glass and its black frame): one arch from the door's front corner over
# to the quarter glass's tail, and the belt.
DLO = [(437, belt(437)), (437, 228), (460, 212), (500, 190), (550, 170), (600, 157), (650, 150), (700, 147),
       (740, 148), (780, 156), (830, 172), (880, 194), (930, 222), (965, 248), (985, 272), (980, belt(980))]
B_PILLAR = (732, 772)                       # its black, front and rear edge
DOOR_BOTTOM = 490                           # the door's bottom edge, one straight line
FRONT_DOOR = [(412, 288), (410, 385), (415, 420), (425, 470), (430, DOOR_BOTTOM)]   # front edge, then the wing's
REAR_DOOR = [(752, 282), (752, DOOR_BOTTOM)]                 # rear edge, down the B-pillar's middle
SHUT_ROUND = 10                             # the door's bottom corners rounded this much (px)

# Windscreen and rear glass in plan (top render: px, u): foot and head are even curves, sides straight.
SCREEN = {"x_base_mid": 285, "x_base_edge": 335, "w_base": 199, "x_top_mid": 458, "x_top_edge": 482, "w_top": 160}
REAR_GLASS = {"x_top_mid": 866, "x_top_edge": 885, "w_top": 132, "x_base_mid": 1042, "x_base_edge": 1018,
              "w_base": 138}
# Bonnet shut line: in plan from the windscreen's foot inboard of its corner, out to the bonnet's
# back corner and forward along the wing's inner edge; then, seen from ahead, over the lamps' inner
# side and across the nose under the bonnet's round front.
BONNET_PLAN = [(326, 182), (330, 207), (280, 200), (220, 184), (170, 172), (130, 159)]
BONNET_NOSE = [(148, 335), (133, 355), (103, 372), (50, 380), (0, 382)]
# Hatch shut line: across the roof at the rear glass's head (side px), and seen from behind, down
# the C-pillar beside the glass, in under the tail lamps and across over the bumper.
LID_ROOF = 860
LID = [(156, 140), (159, 200), (161, 260), (163, 292), (150, 305), (136, 314), (111, 335), (86, 350), (61, 356),
       (0, 357)]

# ---------------------------------------------------------------- front (front render, left half)
INDICATOR = (180, 427, 230, 450)            # the amber lamp in the bumper under each headlamp
GRILLE = [(0, 457), (160, 457), (176, 465), (184, 490), (181, 516), (0, 519)]   # the bumper's lower intake
GRILLE_BARS = [485, 500]
FOG = (126, 492, 15)                        # round fog lamp in the intake: centre and radius

# ---------------------------------------------------------------- rear (rear render, left half)
STOP = [(0, 267), (46, 267), (48, 271), (46, 275), (0, 275)]   # high stop lamp under the rear glass
RAIN = (153, 472, 12, 20)                   # round lamp low in the bumper: centre, radius, its pocket's

# ---------------------------------------------------------------- side
MIRROR = {"x0": 398, "x1": 448, "u0": 225, "u1": 266, "z0": 263, "z1": 230}

# ---------------------------------------------------------------- lamps
# Reference render space: the front at -Y, car-left at +X, PPU pixels per unit. A lamp view looks
# along d; its pixel (a, b) is the line C + (a - w/2)/PPU right + (h/2 - b)/PPU up + t d.
PPU = 30000
REF_C = (0.0, -0.0028761, 0.0095454)        # the renders' centre
SIDE_W, SIDE_H = 1273, 587
LAMP_VIEWS = {
    "head": {"d": (-0.5773503, 0.5773503, -0.5773503), "right": (0.7071068, 0.7071068, 0.0),
             "up": (-0.4082483, 0.4082483, 0.8164966)},
    "tail": {"d": (-0.5773503, -0.5773503, -0.5773503), "right": (-0.7071068, 0.7071068, 0.0),
             "up": (-0.4082483, -0.4082483, 0.8164966)},
}
LAMP_W, LAMP_H = 1380, 1200
# The lenses' outlines on those renders, and the lamp's middle (front or rear render u, py and side px).
HEAD = [(462.0, 974.5), (452.8, 973.5), (443.8, 971.9), (436.0, 966.5), (430.5, 958.8), (428.5, 949.9),
        (428.7, 940.8), (432.0, 932.0), (437.4, 924.1), (444.2, 917.3), (452.0, 911.9), (460.4, 907.9),
        (469.4, 905.5), (479.0, 905.5), (488.0, 907.5), (495.5, 913.5), (501.0, 921.2), (503.4, 930.1),
        (502.1, 939.6), (499.2, 948.6), (494.0, 956.5), (487.6, 963.2), (479.5, 968.4), (470.8, 971.9)]
HEAD_MID = (193.5, 360, 127)
TAIL = [(947.0, 965.5), (937.2, 965.5), (928.0, 964.0), (919.2, 960.3), (911.2, 954.7), (904.3, 947.8),
        (900.0, 939.5), (896.5, 930.4), (895.5, 921.1), (897.1, 911.9), (902.1, 903.6), (909.7, 897.5),
        (918.7, 894.5), (928.0, 893.5), (936.9, 895.7), (945.9, 899.3), (954.1, 904.6), (961.0, 911.5),
        (966.1, 919.7), (969.4, 928.9), (970.3, 938.2), (967.9, 947.4), (963.3, 955.7), (955.9, 961.9)]
TAIL_MID = (182.5, 347, 1160)


def build_of_ref(p):
    """A reference-space point in build space. The overhang scale holds on past the wheel line, so
    the map stays one affine map each end and a lamp's straight prism stays straight."""
    px = (p[1] - REF_C[1]) * PPU + SIDE_W / 2
    py = SIDE_H / 2 - (p[2] - REF_C[2]) * PPU
    front = px < (PX_WF + PX_WR) / 2
    x = WHEELBASE / 2 + (PX_WF - px) * S * K if front else -WHEELBASE / 2 - (px - PX_WR) * S * K
    return [round(float(x), 4), round(float(p[0] * PPU * SH), 4), round(float(TYRE_R + (PY_W - py) * S), 4)]


def ref_of_mid(mid):
    u, py, px = mid
    return np.array([u / PPU, REF_C[1] + (px - SIDE_W / 2) / PPU, REF_C[2] + (SIDE_H / 2 - py) / PPU])


def lamp_prism(pts, view, mid, reach=0.0012, shrink=0.0):
    """The lamp outline pts (lamp view px) as a prism through the lamp's middle: each point's line
    from reach before the middle to reach past it, in build space."""
    from shapely.geometry import Polygon
    g = Polygon(pts)
    if shrink:
        g = g.buffer(-shrink, join_style=1)
    pts = list(g.exterior.coords)[:-1]
    v = LAMP_VIEWS[view]
    d, r, up, c = np.array(v["d"]), np.array(v["right"]), np.array(v["up"]), np.array(REF_C)
    tc = float((ref_of_mid(mid) - c) @ d)
    return [[build_of_ref(c + (a - LAMP_W / 2) / PPU * r + (LAMP_H / 2 - b) / PPU * up + t * d) for a, b in pts]
            for t in (tc - reach, tc + reach)]


data = {
    "name": "KaiserFirefly",
    "wheel": {"r": TYRE_R, "rim_r": RIM_R, "width": 0.205, "outer_y": H(250), "front_x": X(PX_WF),
              "rear_x": X(PX_WR), "arch_r": L(102)},
    "stations": stations(),
    "wings": [wing_stations(FRONT_WING), wing_stations(REAR_WING)],
    "gh": [X(GH[1]), X(GH[0])],
    "dlo": side(DLO),
    "b_pillar": [X(B_PILLAR[1]), X(B_PILLAR[0])],
    "front_door": side(FRONT_DOOR),
    "door_bottom": Z(DOOR_BOTTOM),
    "rear_door": side(REAR_DOOR),
    "shut_round": L(SHUT_ROUND),
    "screen": {"x_base_mid": X(SCREEN["x_base_mid"]), "x_base_edge": X(SCREEN["x_base_edge"]),
               "w_base": H(SCREEN["w_base"]), "x_top_mid": X(SCREEN["x_top_mid"]),
               "x_top_edge": X(SCREEN["x_top_edge"]), "w_top": H(SCREEN["w_top"])},
    "rear_glass": {"x_top_mid": X(REAR_GLASS["x_top_mid"]), "x_top_edge": X(REAR_GLASS["x_top_edge"]),
                   "w_top": H(REAR_GLASS["w_top"]), "x_base_mid": X(REAR_GLASS["x_base_mid"]),
                   "x_base_edge": X(REAR_GLASS["x_base_edge"]), "w_base": H(REAR_GLASS["w_base"])},
    "bonnet_plan": plan(BONNET_PLAN), "bonnet_nose": end(BONNET_NOSE),
    "lid_roof": X(LID_ROOF), "lid": end(LID),
    "head_prism": lamp_prism(HEAD, "head", HEAD_MID),
    "head_lens": lamp_prism(HEAD, "head", HEAD_MID, shrink=0.6),
    "tail_prism": lamp_prism(TAIL, "tail", TAIL_MID),
    "tail_lens": lamp_prism(TAIL, "tail", TAIL_MID, shrink=0.6),
    "head_mid": end([HEAD_MID[:2]])[0] + [X(HEAD_MID[2])],
    "tail_mid": end([TAIL_MID[:2]])[0] + [X(TAIL_MID[2])],
    "indicator": [H(INDICATOR[0]), Z(INDICATOR[3]), H(INDICATOR[2]), Z(INDICATOR[1])],
    "grille": end(GRILLE), "grille_bars": [Z(z) for z in GRILLE_BARS],
    "fog": [H(FOG[0]), Z(FOG[1]), L(FOG[2])],
    "stop": end(STOP),
    "rain": [H(RAIN[0]), Z(RAIN[1]), L(RAIN[2]), L(RAIN[3])],
    "mirror": {"x_back": X(MIRROR["x1"]), "x_front": X(MIRROR["x0"]), "y_in": H(MIRROR["u0"]),
               "y_out": H(MIRROR["u1"]), "z0": Z(MIRROR["z0"]), "z1": Z(MIRROR["z1"])},
    "picks": {"frontbadge": 1, "rearbadge": 1, "name": 1},
}
_bx = np.array([X(p) for p in (440, 970)]); _bz = np.array([Z(belt(p)) for p in (440, 970)])
_k = float((_bz[1] - _bz[0]) / (_bx[1] - _bx[0]))
data["belt"] = [round(_k, 6), round(float(_bz[0] - _k * _bx[0]), 4)]

if __name__ == "__main__":
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(data, indent=1))
    st = data["stations"]
    print("wrote", out, len(st), "stations; nose", max(s["x"] for s in st), "tail", min(s["x"] for s in st),
          "length", round(max(s["x"] for s in st) - min(s["x"] for s in st), 4),
          "roof", max(s.get("zt", 0) for s in st), "half", max(max(w["w"] for w in g) for g in data["wings"]),
          data["wheel"])
