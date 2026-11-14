"""Traced rows for the Liberty Mantle, based on the 2013 Ford Fusion saloon (sold in Europe as the
2014 Mk5 Mondeo).

Reference: Ivan's 3D model of a Fusion (a .glb, kept out of the repo). Its paint is turned green so
the red tail lamps stand apart, and it is rendered orthographically from the side, front, rear and
top on a white background at 12 pixels per unit, plus one side render of the car's outer half only
(for the wing's crest). Every row below is picked off those renders like a drawing. Nothing is
taken from its meshes. Points are written in the renders' units read off their grid:
  Y along the car (front negative), Z up, U out to the side from the centre line (half widths).

Scale: the wheel centres, 116.18 units apart, set the scale from the published 2850 mm wheelbase.
Both overhangs stretch by the same factor to the published 4869 mm length. Half widths are set to
the published 1852 mm width. The ground sits one published tyre radius (235/45 R18, the Titanium's)
under the wheel centres.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py  -> rows.json
"""
import json
from pathlib import Path

import numpy as np
from scipy.interpolate import PchipInterpolator

WHEELBASE = 2.850
LENGTH = 4.869
WIDTH = 1.852
TYRE_R = round((18 * 25.4 + 2 * 0.45 * 235) / 2000, 4)     # 235/45 R18
RIM_R = round(18 * 25.4 / 2000, 4)
Y_FRONT, Y_REAR, Z_WHEEL = -58.19, 57.99, -11.266          # wheel centres on the side render
Y_NOSE, Y_TAIL = -94.84, 98.70                              # the body's ends on the side render
HALF_MAX = 37.67                                            # widest half width, mirrors left out
S = WHEELBASE / (Y_REAR - Y_FRONT)
K = (LENGTH - WHEELBASE) / (((Y_FRONT - Y_NOSE) + (Y_TAIL - Y_REAR)) * S)
SW = WIDTH / 2 / HALF_MAX
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
    return round(float(w * SW), 4)


def L(d):
    """A length along the car or up it, in render units, to metres."""
    return round(float(d * S), 4)


def side(pts):
    return [[X(y), Z(z)] for y, z in pts]


def end(pts):
    """Front or rear view points (half width, Z), the car's left half."""
    return [[H(w), Z(z)] for w, z in pts]


def plan(pts):
    return [[X(y), H(w)] for y, w in pts]


# ---------------------------------------------------------------- body rows (side and top renders)
# Over the bonnet the side render's top edge is the bonnet's middle (CENTRE), from the nose's
# foremost point up to the windscreen's foot; the wing's top (CREST) is read off the outer half's
# render.
CENTRE = [(-94.84, -4.0), (-94.60, 0.0), (-94.20, 3.0), (-93.60, 5.0), (-92.80, 6.5), (-91.80, 7.6),
          (-89.50, 9.0), (-86.60, 10.5), (-83.10, 12.0), (-78.90, 13.5), (-73.70, 15.0), (-66.70, 16.5),
          (-60.00, 17.5), (-55.00, 18.0), (-50.00, 18.2)]
CREST = [(-94.84, -4.0), (-94.00, 2.0), (-92.50, 5.5), (-90.50, 7.0), (-87.00, 8.0), (-84.00, 8.8),
         (-81.00, 9.9), (-78.00, 11.0), (-75.00, 12.0), (-72.00, 13.0), (-69.00, 14.0), (-66.00, 14.8),
         (-60.00, 15.9), (-54.00, 16.5), (-50.00, 16.9)]
# Roof and tail along the middle: the windscreen, the roof, the long rear glass, the short boot
# deck, the boot lid's back and the bumper's back, down to the bumper's rearmost point.
TOP = [(-50.00, 18.2), (-48.00, 18.3), (-46.00, 18.9), (-44.00, 19.4), (-40.00, 21.4), (-36.00, 23.4),
       (-30.00, 26.3), (-24.00, 28.9), (-20.00, 30.5), (-16.00, 31.9), (-12.00, 32.9), (-8.00, 33.8),
       (-4.00, 34.4), (0.00, 34.9), (4.00, 35.3), (8.00, 35.5), (12.00, 35.7), (14.00, 35.75),
       (18.00, 35.7), (22.00, 35.6), (26.00, 35.35), (30.00, 35.1), (34.00, 34.7), (38.00, 34.1),
       (42.00, 33.5), (46.00, 32.7), (50.00, 31.9), (54.00, 31.0), (58.00, 30.0), (62.00, 29.0),
       (66.00, 27.9), (70.00, 26.5), (74.00, 25.2), (78.00, 23.8), (82.00, 22.5), (86.00, 21.3),
       (90.00, 20.1), (92.00, 19.7), (93.50, 19.3), (94.40, 18.5), (94.80, 17.0), (95.00, 12.0),
       (95.20, 9.0), (95.60, 7.5), (96.50, 6.0), (97.60, 3.5), (98.30, 0.0), (98.70, -4.5)]
# Silhouette bottom: the front bumper's foot, the sills carried straight across the arches, the
# rear bumper's underside and its back rising to meet the top.
BOTTOM = [(-94.84, -4.0), (-94.70, -7.0), (-94.40, -11.0), (-93.70, -13.5), (-92.00, -14.6), (-88.80, -15.0),
          (-84.00, -15.5), (-80.00, -16.0), (-72.00, -16.7), (-60.00, -17.4), (-44.00, -18.0), (0.00, -17.3),
          (40.00, -17.0), (60.00, -16.3), (72.00, -15.6), (80.00, -15.1), (88.00, -14.1), (94.00, -13.1),
          (96.30, -10.6), (97.60, -8.5), (98.30, -6.5), (98.70, -4.5)]
# Half width in plan (top render), the mirrors left out.
HALF = [(-94.84, 17.0), (-94.00, 20.5), (-93.00, 22.3), (-90.00, 28.2), (-87.00, 31.7), (-84.00, 33.7),
        (-81.00, 35.4), (-78.00, 36.2), (-75.00, 37.0), (-72.00, 37.4), (-69.00, 37.6), (-66.00, 37.67),
        (-46.00, 37.67), (-42.00, 37.5), (-39.00, 36.9), (-34.00, 36.67), (36.00, 36.75), (42.00, 36.95),
        (45.00, 37.3), (48.00, 37.5), (52.00, 37.62), (56.00, 37.67), (66.00, 37.67), (69.00, 37.6),
        (72.00, 37.3), (75.00, 36.8), (78.00, 35.8), (81.00, 34.8), (84.00, 33.9), (87.00, 32.6),
        (90.00, 30.8), (93.00, 28.4), (96.00, 24.8), (98.00, 21.5), (98.70, 18.0)]
FLARES = []           # the arches stand only a hair proud, so the plan half width carries them
# The sides stand full width over a band: its bottom (WIDEST) and top (WIDE_TOP), read off the
# front and rear renders.
WIDEST = [(-94.84, -3.0), (-60.00, -3.0), (0.00, -2.0), (60.00, -3.0), (98.70, -3.0)]
WIDE_TOP = [(-94.84, 2.0), (-85.00, 5.0), (-70.00, 6.5), (0.00, 7.5), (60.00, 8.0), (90.00, 8.0), (98.70, 4.0)]
# How far the sill or bumper bottom tucks in from the widest point (front and rear renders).
TUCK = [(-94.84, 1.5), (-60.00, 1.5), (0.00, 1.6), (60.00, 1.5), (98.70, 2.0)]
# Shoulder along the cabin (the belt) and how far it sits in from the widest point.
# Over the bonnet the shoulder is the wing's top (CREST); behind the cabin it is the boot deck's
# edge.
SHOULDER = [(-50.00, 16.8), (-44.00, 15.6), (-38.00, 15.1), (-20.00, 15.7), (0.00, 16.5), (20.00, 17.3),
            (40.00, 18.0), (56.00, 18.7), (64.00, 19.1), (72.00, 19.5), (80.00, 19.8), (88.00, 19.9),
            (92.00, 19.6), (94.00, 19.0), (98.70, 17.0)]
SHOULDER_IN = [(-94.84, 2.0), (-70.00, 2.2), (-50.00, 2.6), (-40.00, 3.6), (0.00, 3.6), (50.00, 4.0),
               (64.00, 4.6), (90.00, 4.6), (98.70, 3.0)]
BONNET_END = -50.0        # the bonnet meets the windscreen's foot
# Deck height at the centre under the greenhouse (hidden), joined to the cowl and the boot deck.
DECK = [(-50.00, 18.2), (-48.00, 18.3), (82.00, 20.9), (84.00, 21.0)]

# Greenhouse, from the windscreen's foot to the rear glass's foot.
GH = (-50.0, 84.0)
# Glass foot in plan along the cabin (top render), and the roof edge in side (Y, Z) and plan (Y, U):
# the A-pillar, the roof's side and the C-pillar along the rear glass's sides.
GLASS_FOOT = [(-50.00, 28.0), (-46.00, 29.6), (-40.00, 31.2), (-36.00, 32.6), (20.00, 32.4), (40.00, 32.0),
              (48.00, 31.8), (56.00, 31.3), (60.00, 30.5), (64.00, 30.0), (72.00, 28.5), (80.00, 26.6),
              (84.00, 25.6)]
ROOF_Z = [(-50.00, 17.6), (-46.00, 18.2), (-38.00, 20.0), (-34.00, 21.6), (-30.00, 23.5), (-26.00, 25.4),
          (-22.00, 27.3), (-18.00, 29.0), (-14.00, 30.6), (-10.00, 31.7), (-6.00, 32.6), (-2.00, 33.2),
          (2.00, 33.6), (6.00, 33.9), (10.00, 34.1), (14.00, 34.2), (18.00, 34.2), (22.00, 34.0),
          (26.00, 33.6), (30.00, 33.1), (34.00, 32.5), (38.00, 31.8), (42.00, 31.0), (46.00, 30.1),
          (50.00, 29.3), (54.00, 28.8), (58.00, 27.8), (62.00, 26.7), (66.00, 25.6), (70.00, 24.3),
          (74.00, 23.0), (78.00, 21.7), (82.00, 20.8), (84.00, 20.6)]
ROOF_W = [(-50.00, 27.0), (-44.00, 29.0), (-36.00, 29.8), (-28.00, 29.0), (-20.00, 28.0), (-14.00, 27.6),
          (-8.00, 27.4), (0.00, 27.3), (20.00, 27.3), (40.00, 27.6), (48.00, 27.8), (54.00, 27.0),
          (60.00, 25.8), (70.00, 25.2), (84.00, 24.8)]


def table(rows):
    a = np.array(rows, float)
    return PchipInterpolator(a[:, 0], a[:, 1], extrapolate=True)


def stations():
    """Each station's section values in metres, at every Y any row was picked at."""
    t = {k: table(v) for k, v in dict(crest=CREST, centre=CENTRE, top=TOP, bot=BOTTOM, half=HALF, zw=WIDEST, zwt=WIDE_TOP,
                                       tuck=TUCK, zs=SHOULDER, sin=SHOULDER_IN, deck=DECK, rz=ROOF_Z,
                                       rw=ROOF_W, wg=GLASS_FOOT).items()}
    ys = sorted({round(float(y), 4) for rows in (CENTRE, TOP, BOTTOM, HALF) for y, _ in rows}
                | {round(float(y), 4) for y in np.arange(-94.0, 98.0, 1.6)})
    ys = [y for y in ys if Y_NOSE <= y <= Y_TAIL]
    out = []
    for y in ys:
        f = {k: float(v(y)) for k, v in t.items()}
        in_gh = GH[0] < y < GH[1]
        if y <= BONNET_END:
            zs, deck = f["crest"], f["centre"]           # the wing top is the shoulder, the middle is the bonnet
            top = max(zs, deck)
        elif in_gh:
            top = f["top"]
            deck = min(top, f["deck"])
            zs = min(f["zs"], deck - 0.5)
        else:
            top = f["top"]
            deck = top                                    # the boot: the middle is the top
            zs = min(f["zs"], top - 0.5)
        st = dict(x=X(y), zb=Z(f["bot"]), w=H(f["half"]), zw=Z(f["zw"]), zwt=Z(f["zwt"]), tuck=H(f["tuck"]),
                  zs=Z(zs), ws=H(f["half"] - f["sin"]), zd=Z(deck))
        if in_gh:
            st.update(zt=Z(top), zr=Z(min(f["rz"], top - 0.25)), wr=H(f["rw"]), wg=H(f["wg"]))
        out.append(st)
    return out


# ---------------------------------------------------------------- glass (side render)
def belt(y):
    """The belt line under the side windows: one straight line rising gently to the back."""
    return 16.5 + 0.0385 * y


# The side window opening (glass and its black frame), from the side render: the A-pillar edge a
# straight line from the black sail's tip, the roof edge one even curve and the quarter window's
# pointed rear end a straight line.
DLO = {"a_foot": (-40.0, belt(-40.0)), "a_head": (-14.0, 29.0),
       "roof": [(-14.0, 29.0), (-10.0, 30.2), (-6.0, 31.2), (-2.0, 31.8), (2.0, 32.2), (10.0, 32.7), (18.0, 32.8),
                (26.0, 32.2), (34.0, 31.1), (42.0, 29.6), (50.0, 27.5), (56.0, 25.7), (61.5, 23.6)],
       "c_head": (61.5, 23.6), "c_foot": (56.5, belt(56.5))}
VENT_Y = -22.6                                     # the black sail ahead of the door glass ends here
B_PILLAR = ((9.8, belt(9.8)), (12.6, 32.6), 10.0)  # the black division's foot, head and width
C_POST = ((44.5, belt(44.5)), (42.0, 29.8), 6.0)   # the rear door glass's rear post

# Door shut lines (side render), straight runs and plain arcs. Only the front doors open; the rear
# doors are shut lines.
DOOR_BOTTOM = -13.0                               # the doors' bottom edge, one straight line
FRONT_DOOR = [(-40.2, belt(-40.2) - 0.3), (-37.2, 2.0), (-36.7, DOOR_BOTTOM)]   # front edge
REAR_DOOR = [(9.8, belt(9.8)), (7.6, 0.0), (6.8, DOOR_BOTTOM)]   # rear edge, from the division's middle
# The rear door's rear edge: one straight run from the window's foot down to the rear arch, then
# one circle round the arch about the wheel centre (its radius), down to the doors' bottom line.
BACK_DOOR = [(50.0, belt(50.0) - 0.2), (46.4, 1.9)]
BACK_ARCH_R = 17.5
SHUT_ROUND = 2.0                                  # the doors' bottom corners rounded this much
EDGE_ROUND = 8.0                                  # their front and rear edges bow this gently
# Windscreen (top render: Y, half width): its foot and head are even curves, its sides straight.
SCREEN = {"y_base_mid": -49.9, "y_base_edge": -45.0, "w_base": 29.9, "y_top_mid": -14.5, "y_top_edge": -15.0,
          "w_top": 25.6}
# Rear glass (top render): head and foot. Its foot's corners are cut back, so the foot is one deep curve.
REAR_GLASS = {"y_top_mid": 51.5, "y_top_edge": 52.5, "w_top": 24.6, "y_base_mid": 84.8, "y_base_edge": 77.0,
              "w_base": 24.9}
# Bonnet shut line, all traced from above (top render: Y, half width): from the nose's middle above
# the grille, out along the headlamps' top edges to the wing, back along the wing's inner edge, then
# bending in to the windscreen's foot, inside its corner.
BONNET = [(-89.6, 0.0), (-89.3, 10.0), (-88.6, 17.0), (-87.6, 22.5), (-86.0, 26.8), (-84.0, 29.6), (-80.0, 32.1),
          (-76.0, 33.5), (-72.0, 34.3), (-66.0, 34.6), (-58.0, 34.5), (-52.0, 34.0), (-48.6, 32.6), (-47.0, 28.0)]

# ---------------------------------------------------------------- front (front render, left half)
# Each headlamp sweeps from the grille's top corner round the nose's corner onto the side. Its
# outline on the nose (front render: U, Z), from the inner tip along the bottom...
HEAD = [(20.6, 6.5), (20.2, 5.5), (21.3, 4.3), (24.0, 3.8), (28.0, 3.65)]
# ...then on the side (side render: Y, Z) back along the bottom to the rear tip and forward along
# the top...
HEAD_SIDE = [(-89.5, 3.55), (-85.0, 3.6), (-79.5, 3.9), (-75.6, 4.55), (-73.2, 5.6), (-71.3, 6.8), (-70.2, 7.9),
             (-70.2, 8.9), (-71.2, 9.6), (-73.0, 9.75), (-77.9, 9.2), (-84.4, 8.2), (-88.5, 7.4)]
# ...and on the nose again along the top back to the inner tip.
HEAD_TOP = [(27.0, 7.6), (23.0, 7.0)]
HEAD_AXIS = (-78.0, 22.0)  # the corner's axis in plan (top render: Y, half width), the lamp's rays fan from it
# The grille between the headlamps: a wide rounded trapezoid, widest just above its middle.
GRILLE = [(0.0, 6.6), (10.0, 6.5), (14.0, 6.0), (17.0, 5.0), (19.5, 3.3), (20.7, 1.2), (20.0, -0.2), (18.0, -1.2),
          (12.0, -2.1), (0.0, -2.4)]
# The bumper's lower intake across the middle, and the side intakes in its corners.
INTAKE = [(0.0, -7.6), (18.5, -7.6), (19.4, -9.0), (20.5, -11.0), (20.2, -11.6), (0.0, -11.6)]
SIDE_INTAKE = [(21.6, -7.7), (33.2, -6.9), (34.4, -11.0), (34.0, -11.5), (25.4, -11.5)]

# ---------------------------------------------------------------- rear (rear render, left half)
# Each tail lamp wraps round the tail's corner onto the side. Its outline on the tail (rear render:
# U, Z), from the top down round the inner tip and out along the bottom to the corner...
TAIL = [(18.0, 15.9), (16.2, 15.2), (14.2, 14.6), (13.0, 13.8), (13.8, 12.6), (16.0, 12.0), (20.0, 11.5),
        (23.5, 11.2)]
# ...then on the side (side render: Y, Z) along the bottom forward to the pointed tip and back
# along the top to the corner...
TAIL_SIDE = [(91.0, 10.5), (86.0, 10.45), (83.0, 10.6), (80.5, 11.0), (79.1, 12.0), (78.4, 13.2), (79.2, 14.0),
             (82.8, 15.0), (86.6, 16.0), (89.5, 16.7), (91.5, 17.1)]
# ...and on the tail again along the top back to the start.
TAIL_TOP = [(23.5, 17.0), (21.0, 16.6)]
TAIL_AXIS = (86.0, 18.0)  # the corner's axis in plan (top render: Y, half width), the lamp's rays fan from it
# The dark band low across the bumper's back and the rain light in its middle.
BAND_R = [(0.0, -9.4), (29.0, -9.4), (28.0, -7.8), (25.8, -6.2), (19.5, -5.2), (0.0, -5.0)]
RAIN = (3.5, -8.6, -6.0)                          # rain light: half width, bottom, top
# The boot lid's outline on the tail (rear render): across above the bumper, then straight up at
# the rear glass's width to the deck's edge, cutting through the tail lamps.
LID = [(0.0, 4.8), (23.4, 4.8), (24.9, 6.3), (24.9, 18.4)]
LID_W = 24.9
# Its front edge runs across the deck just behind the rear glass, traced from above (top render:
# Y, half width) from the side's top corner forward at the same width, then in to the middle.
LID_TOP = [(88.0, 24.9), (79.4, 24.9), (78.6, 24.0), (80.2, 20.0), (82.2, 15.0), (85.2, 8.0), (86.0, 0.0)]
GATE_Z, GATE_LOW = 14.6, 9.6                      # the lid between the lamps (the badge), and low in its recess

# ---------------------------------------------------------------- other parts
MIRROR = {"y0": -31.0, "y1": -23.5, "w0": 36.0, "w1": 42.8, "z0": 17.2, "z1": 21.6}   # on the black sail

data = {
    "name": "LibertyMantle",
    "wheel": {"r": TYRE_R, "rim_r": RIM_R, "width_f": 0.235, "width_r": 0.235, "outer_y": H(36.59),
              "outer_y_r": H(36.59), "front_x": X(Y_FRONT), "rear_x": X(Y_REAR), "arch_r": round(TYRE_R + 0.05, 4)},
    "stations": stations(),
    "flares": [[X(y), Z(z), L(r), H(w)] for y, z, r, w in FLARES],
    "gh": [X(GH[1]), X(GH[0])],
    "dlo": {"a_foot": side([DLO["a_foot"]])[0], "a_head": side([DLO["a_head"]])[0], "roof": side(DLO["roof"]),
            "c_head": side([DLO["c_head"]])[0], "c_foot": side([DLO["c_foot"]])[0]},
    "vent_x": X(VENT_Y),
    "b_pillar": {"foot": side([B_PILLAR[0]])[0], "head": side([B_PILLAR[1]])[0], "w": L(B_PILLAR[2])},
    "c_post": {"foot": side([C_POST[0]])[0], "head": side([C_POST[1]])[0], "w": L(C_POST[2])},
    "front_door": side(FRONT_DOOR),
    "door_bottom": Z(DOOR_BOTTOM),
    "rear_door": side(REAR_DOOR),
    "back_door": side(BACK_DOOR),
    "back_arch_r": L(BACK_ARCH_R),
    "shut_round": L(SHUT_ROUND),
    "edge_round": L(EDGE_ROUND),
    "screen": {"x_base_mid": X(SCREEN["y_base_mid"]), "x_base_edge": X(SCREEN["y_base_edge"]),
               "w_base": H(SCREEN["w_base"]), "x_top_mid": X(SCREEN["y_top_mid"]),
               "x_top_edge": X(SCREEN["y_top_edge"]), "w_top": H(SCREEN["w_top"])},
    "rear_glass": {"x_top_mid": X(REAR_GLASS["y_top_mid"]), "x_top_edge": X(REAR_GLASS["y_top_edge"]),
                   "w_top": H(REAR_GLASS["w_top"]), "x_base_mid": X(REAR_GLASS["y_base_mid"]),
                   "x_base_edge": X(REAR_GLASS["y_base_edge"]), "w_base": H(REAR_GLASS["w_base"])},
    "bonnet": plan(BONNET),
    "head": end(HEAD),
    "head_side": side(HEAD_SIDE),
    "head_top": end(HEAD_TOP),
    "head_axis": plan([HEAD_AXIS])[0],
    "grille": end(GRILLE),
    "intake": end(INTAKE),
    "side_intake": end(SIDE_INTAKE),
    "tail": end(TAIL),
    "tail_side": side(TAIL_SIDE),
    "tail_top": end(TAIL_TOP),
    "tail_axis": plan([TAIL_AXIS])[0],
    "band_r": end(BAND_R),
    "rain": [H(RAIN[0]), Z(RAIN[1]), Z(RAIN[2])],
    "lid": end(LID),
    "lid_w": H(LID_W),
    "lid_top": plan(LID_TOP),
    "gate_z": Z(GATE_Z),
    "gate_low": Z(GATE_LOW),
    "mirror": {"x_back": X(MIRROR["y1"]), "x_front": X(MIRROR["y0"]), "y_in": H(MIRROR["w0"]),
               "y_out": H(MIRROR["w1"]), "z0": Z(MIRROR["z0"]), "z1": Z(MIRROR["z1"])},
    "picks": {"frontbadge": 1, "rearbadge": 1, "name": 1},   # Ivan's picks, 2026-11-14
}
# belt line z(x) in build space, sampled for parts to interpolate
_ys = np.linspace(-42.0, 58.0, 48)
data["belt"] = [[X(y), Z(belt(y))] for y in _ys]

if __name__ == "__main__":
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(data, indent=1))
    st = data["stations"]
    print("wrote", out, len(st), "stations; nose", max(s["x"] for s in st), "tail", min(s["x"] for s in st),
          "length", round(max(s["x"] for s in st) - min(s["x"] for s in st), 4),
          "roof", max(s.get("zt", 0) for s in st), "half", max(s["w"] for s in st), data["wheel"])
