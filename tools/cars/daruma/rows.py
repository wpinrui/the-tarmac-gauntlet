"""Traced rows for the Tora Daruma, based on the 2008 Nissan Latio Sport (the five-door hatch).

Reference: Ivan's 3D model of a C11 hatch (a .glb, kept out of the repo). It is rendered
orthographically from the side, front, rear and top on a plain background at 30000 pixels per
model unit, each material one flat colour, and every row below is picked off those renders like a
drawing. Nothing is taken from its meshes. Points are written in render pixels:
  side render: px along the car (nose left), py down;
  top render: px along the car as the side, u out from the centre line;
  front and rear renders: u out from the centre line, py as the side render.

Scale: the wheel centres, 782.5 px apart, set the scale from the published 2600 mm wheelbase.
Both overhangs stretch by one factor to the published 4205 mm length. The ground sits one
published tyre radius (185/65 R15) under the wheel centres. Half widths are set so the widest
body point is the published 1695 mm. Heights keep the model's proportions.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py  -> rows.json
"""
import json
from pathlib import Path

import numpy as np
from scipy.interpolate import PchipInterpolator

WHEELBASE, LENGTH, WIDTH = 2.600, 4.205, 1.695
TYRE_R = round((15 * 25.4 + 2 * 0.65 * 185) / 2000, 4)    # 185/65 R15
RIM_R = round(15 * 25.4 / 2000, 4)
PX_WF, PX_WR, PY_W = 312.5, 1095.0, 417.5                  # wheel centres on the side render
PX_NOSE, PX_TAIL = 43.5, 1332.5                            # the body's ends
HALF_MAX = 254.0                                            # widest half width, top render
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


def side(pts):
    return [[X(x), Z(z)] for x, z in pts]


def end(pts):
    """Front or rear render points (u, py), the car's left half."""
    return [[H(u), Z(z)] for u, z in pts]


def plan(pts):
    return [[X(x), H(u)] for x, u in pts]


# ---------------------------------------------------------------- body rows (side and top renders)
# Silhouette top along the middle: nose, bonnet, windscreen, roof, spoiler lip, rear glass,
# tailgate, bumper and the tail's face.
TOP = [(43.5, 352), (45, 349), (48, 346), (52, 343), (60, 340), (70, 324), (80, 311), (90, 297), (100, 283),
       (110, 272), (120, 265), (130, 260), (150, 251), (170, 243), (200, 234), (230, 227), (260, 221),
       (290, 216), (320, 213), (350, 210), (380, 194), (420, 172), (460, 149), (500, 128), (540, 109),
       (580, 91), (600, 83), (620, 78), (660, 70), (700, 65), (740, 61), (780, 60), (850, 60), (900, 62),
       (960, 65), (1000, 68), (1060, 74), (1100, 78), (1140, 84), (1160, 88), (1168, 93), (1176, 103),
       (1200, 130), (1230, 165), (1250, 189), (1262, 200), (1279, 220), (1292, 240), (1300, 262),
       (1304, 285), (1308, 305), (1314, 320), (1321, 330), (1328, 338), (1332.5, 346)]
# Silhouette bottom, carried straight across the arches.
BOTTOM = [(43.5, 434), (45, 440), (46.5, 443), (50, 446), (60, 453), (100, 454), (150, 455), (200, 457),
          (430, 463), (985, 463), (1180, 463), (1220, 460), (1250, 457), (1272, 455), (1290, 444),
          (1300, 442), (1310, 438), (1318, 433), (1324, 425), (1328, 414), (1331, 400), (1332.5, 386)]
# Half width in plan (top render), mirrors left out.
HALF = [(43.5, 60), (46, 75), (50, 90), (55, 103), (60, 113), (64, 121), (66, 127), (68, 133), (70, 139),
        (84, 174), (104, 209), (124, 228), (144, 237), (164, 242), (184, 246.5), (204, 249), (224, 251), (244, 252), (264, 253), (324, 254), (424, 253), (444, 252),
        (524, 252), (684, 253), (864, 253), (924, 252.5), (1004, 254), (1144, 254), (1164, 253),
        (1184, 251), (1204, 249), (1224, 244), (1244, 238), (1264, 235), (1284, 227), (1290, 223),
        (1296, 217), (1302, 206), (1308, 191), (1314, 169), (1320, 141), (1326, 99), (1329, 77),
        (1332.5, 30)]
# Height of the widest point (front and rear renders).
WIDEST = [(43.5, 380), (70, 362), (100, 346), (200, 330), (420, 325), (1000, 325), (1200, 335), (1332.5, 385)]
# How far the sill or bumper bottom tucks in from the widest point (front and rear renders).
TUCK = [(43.5, 8), (200, 12), (430, 18), (990, 18), (1200, 14), (1332.5, 10)]
# Shoulder: where the side turns in to the bonnet, the glass or the tailgate (py), and how far in
# from the widest point it sits (u). Along the cabin it is the belt; the tail clips it under the top.
SHOULDER = [(43.5, 357), (60, 345), (70, 330), (80, 317), (90, 304), (100, 293), (150, 263), (200, 248), (260, 236), (300, 230),
            (345, 226), (420, 233), (1072, 212), (1200, 208), (1250, 205), (1280, 228), (1300, 270), (1320, 335),
            (1332.5, 352)]
SHOULDER_IN = [(43.5, 30), (70, 40), (100, 38), (150, 34), (200, 32), (345, 30), (420, 8), (1072, 8), (1180, 9),
               (1250, 14), (1300, 22), (1332.5, 20)]
# Deck height at the middle under the greenhouse (hidden), joined to the bonnet and the tailgate.
DECK = [(345, 211), (380, 220), (1220, 214), (1262, 200), (1280, 220), (1300, 262)]

# Greenhouse, from the windscreen's foot down past the rear glass's foot, where the D-pillars
# fade into the tailgate.
GH = (350, 1300)
# Glass foot (u) along the cabin, and the roof edge in side (px, py) and plan (px, u): the
# A-pillar, the roof's side and the D-pillar.
GLASS_FOOT = [(350, 214), (420, 236), (560, 240), (1000, 239), (1100, 237), (1200, 228), (1262, 222), (1300, 214)]
ROOF_Z = [(350, 212), (418, 206), (495, 157), (536, 135), (603, 105), (653, 91), (682, 85), (750, 78),
          (819, 78), (915, 84), (984, 92), (1063, 103), (1110, 108), (1150, 112), (1180, 128), (1210, 155),
          (1240, 183), (1262, 199), (1280, 221), (1300, 262)]
ROOF_W = [(350, 205), (412, 204), (500, 197), (600, 187), (700, 181), (900, 179), (1050, 180),
          (1150, 185), (1200, 194), (1262, 210), (1300, 212)]


def table(rows):
    a = np.array(rows, float)
    return PchipInterpolator(a[:, 0], a[:, 1], extrapolate=True)


def stations():
    """Each station's section values in metres, at every px any row was picked at."""
    t = {k: table(v) for k, v in dict(top=TOP, bot=BOTTOM, half=HALF, zw=WIDEST, tuck=TUCK, zs=SHOULDER,
                                       sin=SHOULDER_IN, deck=DECK, rz=ROOF_Z, rw=ROOF_W, wg=GLASS_FOOT).items()}
    xs = sorted({float(x) for rows in (TOP, BOTTOM, HALF, SHOULDER) for x, _ in rows}
                | {float(x) for x in np.arange(50, 1330, 12)})
    out = []
    for px in xs:
        f = {k: float(v(px)) for k, v in t.items()}
        top = f["top"]
        in_gh = GH[0] < px < GH[1]
        deck = max(top, f["deck"]) if in_gh else top          # py grows downward
        st = dict(x=X(px), zb=Z(f["bot"]), w=H(f["half"]), zw=Z(f["zw"]), tuck=H(f["tuck"]),
                  zs=Z(max(f["zs"], deck + 4)), ws=H(f["half"] - f["sin"]), zd=Z(deck))
        if in_gh:
            st.update(zt=Z(top), zr=Z(max(f["rz"], top + 1)), wr=H(f["rw"]), wg=H(f["wg"]))
        out.append(st)
    return out


# ---------------------------------------------------------------- glass (side render)
def belt(px):
    """The belt line: one straight rise from the front door to the quarter glass."""
    return 233.0 - (px - 420.0) * 21.0 / 652.0


# The side window opening (glass and its black frame): the front quarter light, both doors' glass
# and the rear quarter glass, up the A-pillar, along the roof and down the D-pillar.
DLO = [(412, 226), (418, 210), (455, 184), (495, 158), (536, 136), (603, 107), (653, 93), (682, 87),
       (750, 81), (819, 81), (915, 87), (984, 95), (1063, 107), (1084, 113), (1090, 123), (1090, 132),
       (1072, belt(1072)), (900, belt(900)), (700, belt(700)), (500, belt(500)), (420, belt(420))]
B_PILLAR = (779, 803)                     # its black, front and rear edge
QUARTER = [(497, 155), (494, belt(494))]  # the front door's slim post behind the quarter light
DIVIDER = [(980, 96), (980, belt(980))]   # the rear door's post before the quarter glass

# Shut lines (side render), straight runs and one arc round the rear arch.
DOOR_BOTTOM = 430                          # both doors' bottom edge, one straight line
FRONT_DOOR = [(444, 228), (437, 300), (451, DOOR_BOTTOM)]   # front edge: down the wing's back
B_SHUT = [(777, 225), (775, DOOR_BOTTOM)]  # down the middle of the B-pillar's black
REAR_DOOR = [(1073, 212), (1047, 307)]     # rear edge from the window's corner, then round the arch
REAR_ARCH_R = 120                           # the arc's radius about the rear wheel centre (px)
SHUT_ROUND = 12                             # corners rounded this much (px)

# Windscreen (top render: px, u): its foot and head are even curves, its sides straight.
SCREEN = {"x_base_mid": 350, "x_base_edge": 386, "w_base": 201, "x_top_mid": 581, "x_top_edge": 603, "w_top": 176}
# Rear glass (top render): head and foot.
REAR_GLASS = {"x_top_mid": 1158, "x_top_edge": 1146, "w_top": 170, "x_base_mid": 1263, "x_base_edge": 1205,
              "w_base": 188}
# Bonnet shut line (top render: px, u): from the windscreen's foot inboard of its corner, out to
# the bonnet's back corner, forward along the wing and the headlamp's inner edge, across the nose
# above the grille.
BONNET = [(378, 186), (366, 203), (348, 209), (310, 213), (260, 207), (205, 195), (170, 180), (135, 162),
          (110, 148), (92, 127), (80, 90), (73, 45), (70, 0)]

# ---------------------------------------------------------------- front (front render, left half)
HEAD = [(192.5, 326), (125.5, 330), (155.5, 294), (174.5, 274), (195.5, 258), (218.5, 264), (227.5, 271),
        (237.5, 283), (239.5, 289), (237.5, 304), (229.5, 314), (220.5, 320)]
HEAD_SIDE = [(95, 330), (92, 329), (94, 325), (118, 298), (140, 282), (175, 264), (203, 258), (215, 262),
             (222, 271), (230, 283), (230, 287), (226, 290), (203, 300), (156, 316)]
HEAD_CORNER = (130, 213)    # where the lamp turns from the front to the side (top render: px, u)
HEAD_DIAG = [(512, 326), (448, 330), (448, 329), (467, 310), (485, 296), (500, 286), (526, 274), (550, 264),
             (574, 258), (594, 262), (607, 268), (622, 285), (621, 290), (599, 302), (556, 318)]
# The upper grille: a band under the bonnet's front edge, its outer end cut along the lamp.
GRILLE_UP = [(0, 295), (148, 290), (124, 331), (30, 337), (0, 337)]
GRILLE_UP_SLATS = [304, 312, 320]
# The lower grille in the bumper: straight sided, its outer end angled in.
GRILLE_LOW = [(0, 380), (128, 380), (131, 393), (117, 418), (0, 420)]
GRILLE_LOW_SLATS = [392, 406]
GRILLE_LOW_BARS = [0, 42, 85]
# The fog lamp pockets, and the lamp in each.
FOG = [(151.5, 420), (168.5, 376), (215.5, 375), (207.5, 406), (199.5, 414), (187.5, 418)]
FOG_LAMP = (192.5, 396, 13)

# ---------------------------------------------------------------- rear (rear render, left half)
TAIL = [(137.5, 288), (205.5, 284), (224.5, 278), (230.5, 272), (232.5, 254), (230.5, 236), (226.5, 219),
        (219.5, 212), (212.5, 212), (192.5, 224), (160.5, 259), (136.5, 286)]
TAIL_SIDE = [(1287, 288), (1260, 282), (1244, 276), (1130, 220), (1128, 215), (1130, 212), (1199, 212),
             (1217, 214), (1243, 222), (1254, 230), (1270, 247), (1282, 267)]
TAIL_CORNER = (1258, 219)   # where the lamp turns from the back to the side (top render: px, u)
# Each lamp wraps round a corner, so its outline is traced once more on a render looking at that
# corner from 45 degrees (front-left or rear-left, see DIAG), and the lamp is that outline pushed
# along the view. One outline means one lamp, with no seam where the end and side meet.
TAIL_DIAG = [(1105, 288), (1031, 282), (1011, 276), (968, 248), (940, 226), (932, 218), (932, 216), (936, 212),
             (987, 212), (1005, 214), (1039, 226), (1072, 250), (1088, 265)]
STOP = [(0, 88), (48, 88), (52, 91), (50, 96), (45, 100), (0, 100)]     # high stop lamp over the glass
# Tailgate shut line (rear render): across the roof at the spoiler's front (top render px), down
# beside the rear glass, along the lamp's inner edge, and across the bumper top.
LID_ROOF = 1125
LID = [(162, 95), (189.5, 185), (204.5, 215), (160.5, 259), (134.5, 285), (104.5, 322), (64.5, 331.5),
       (0, 331.5)]

# ---------------------------------------------------------------- side
SILL = (425, 985, 433)        # black sill strip: from, to (px), its top (py), down to the bottom
MIRROR = {"x0": 469, "x1": 509, "u0": 233, "u1": 292, "z0": 228, "z1": 188}

# ---------------------------------------------------------------- 45 degree renders
# Reference render space: the front at -Y, car-left at +X, PPU pixels per unit. A 45 degree render
# looks along d; its pixel (a, b) is the line C + (a - w/2)/PPU right + (h/2 - b)/PPU up + t d.
PPU = 30000
RX0, RY0, RZ1 = -0.0115, -0.0235, 0.0155
DIAG = {"front": {"d": (-0.7071068, 0.7071068, 0.0), "right": (0.7071068, 0.7071068, 0.0)},
        "rear": {"d": (-0.7071068, -0.7071068, 0.0), "right": (-0.7071068, 0.7071068, 0.0)}}
DIAG_C, DIAG_W, DIAG_H = (0.0, -0.0005, 0.007), 1560, 510


def build_of_ref(p):
    """A reference-space point in build space. The overhang scale holds on past the wheel line, so
    the map stays one affine map and a lamp's straight prism stays straight."""
    px, py = (p[1] - RY0) * PPU, (RZ1 - p[2]) * PPU
    front = px < (PX_WF + PX_WR) / 2
    x = WHEELBASE / 2 + (PX_WF - px) * S * K if front else -WHEELBASE / 2 - (px - PX_WR) * S * K
    return [round(float(x), 4), round(float(p[0] * PPU * SH), 4), round(float(TYRE_R + (PY_W - py) * S), 4)]


def diag_prism(pts, view, corner, reach=0.004, shrink=0.0):
    """The lamp outline pts (45 degree render px) as a prism round the corner (top render px, u):
    each point's line from reach before the corner to reach past it, in build space."""
    from shapely.geometry import Polygon
    g = Polygon(pts)
    if shrink:
        g = g.buffer(-shrink, join_style=2)
    pts = list(g.exterior.coords)[:-1]
    v = DIAG[view]
    d, r, c = np.array(v["d"]), np.array(v["right"]), np.array(DIAG_C)
    k = np.array([corner[1] / PPU, RY0 + corner[0] / PPU, 0.0])
    tc = float((k - c) @ d)
    out = []
    for t in (tc - reach, tc + reach):
        out.append([build_of_ref(c + (a - DIAG_W / 2) / PPU * r + np.array([0, 0, (DIAG_H / 2 - b) / PPU]) + t * d)
                    for a, b in pts])
    return out


data = {
    "name": "ToraDaruma",
    "wheel": {"r": TYRE_R, "rim_r": RIM_R, "width": 0.185, "outer_y": H(251.5), "front_x": X(PX_WF),
              "rear_x": X(PX_WR), "arch_r": round(TYRE_R + 0.042, 4)},
    "stations": stations(),
    "gh": [X(GH[1]), X(GH[0])],
    "dlo": side(DLO),
    "b_pillar": [X(B_PILLAR[1]), X(B_PILLAR[0])],
    "quarter": side(QUARTER),
    "divider": side(DIVIDER),
    "front_door": side(FRONT_DOOR),
    "door_bottom": Z(DOOR_BOTTOM),
    "b_shut": side(B_SHUT),
    "rear_door": side(REAR_DOOR),
    "rear_arch_r": round(REAR_ARCH_R * S, 4),
    "shut_round": round(SHUT_ROUND * S, 4),
    "screen": {"x_base_mid": X(SCREEN["x_base_mid"]), "x_base_edge": X(SCREEN["x_base_edge"]),
               "w_base": H(SCREEN["w_base"]), "x_top_mid": X(SCREEN["x_top_mid"]),
               "x_top_edge": X(SCREEN["x_top_edge"]), "w_top": H(SCREEN["w_top"])},
    "rear_glass": {"x_top_mid": X(REAR_GLASS["x_top_mid"]), "x_top_edge": X(REAR_GLASS["x_top_edge"]),
                   "w_top": H(REAR_GLASS["w_top"]), "x_base_mid": X(REAR_GLASS["x_base_mid"]),
                   "x_base_edge": X(REAR_GLASS["x_base_edge"]), "w_base": H(REAR_GLASS["w_base"])},
    "bonnet": plan(BONNET),
    "head": end(HEAD), "head_side": side(HEAD_SIDE), "head_corner": plan([HEAD_CORNER])[0],
    "head_prism": diag_prism(HEAD_DIAG, "front", HEAD_CORNER),
    "head_lens": diag_prism(HEAD_DIAG, "front", HEAD_CORNER, shrink=0.6),
    "grille_up": end(GRILLE_UP), "grille_up_slats": [Z(z) for z in GRILLE_UP_SLATS],
    "grille_low": end(GRILLE_LOW), "grille_low_slats": [Z(z) for z in GRILLE_LOW_SLATS],
    "grille_low_bars": [H(u) for u in GRILLE_LOW_BARS],
    "fog": end(FOG), "fog_lamp": [H(FOG_LAMP[0]), Z(FOG_LAMP[1]), H(FOG_LAMP[2])],
    "tail": end(TAIL), "tail_side": side(TAIL_SIDE), "tail_corner": plan([TAIL_CORNER])[0],
    "tail_prism": diag_prism(TAIL_DIAG, "rear", TAIL_CORNER),
    "tail_lens": diag_prism(TAIL_DIAG, "rear", TAIL_CORNER, shrink=0.6),
    "stop": end(STOP),
    "lid_roof": X(LID_ROOF), "lid": end(LID),
    "sill": [X(SILL[0]), X(SILL[1]), Z(SILL[2])],
    "mirror": {"x_back": X(MIRROR["x1"]), "x_front": X(MIRROR["x0"]), "y_in": H(MIRROR["u0"]),
               "y_out": H(MIRROR["u1"]), "z0": Z(MIRROR["z0"]), "z1": Z(MIRROR["z1"])},
    "picks": {"frontbadge": 2, "rearbadge": 2, "name": 4},
}
# belt line z = k x + c in build space
_bx = np.array([X(p) for p in (420, 1072)]); _bz = np.array([Z(belt(p)) for p in (420, 1072)])
_k = float((_bz[1] - _bz[0]) / (_bx[1] - _bx[0]))
data["belt"] = [round(_k, 6), round(float(_bz[0] - _k * _bx[0]), 4)]

if __name__ == "__main__":
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(data, indent=1))
    st = data["stations"]
    print("wrote", out, len(st), "stations; nose", max(s["x"] for s in st), "tail", min(s["x"] for s in st),
          "length", round(max(s["x"] for s in st) - min(s["x"] for s in st), 4),
          "roof", max(s.get("zt", 0) for s in st), "half", max(s["w"] for s in st), data["wheel"])
