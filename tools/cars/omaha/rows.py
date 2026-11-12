"""Traced rows for the Liberty Omaha, based on the Peugeot 106 (3-door Rallye).

Reference: Ivan's 3D model of a 106 Rallye (a .glb, kept out of the repo). It is rendered
orthographically from the side, front, rear and top on a plain background at 400 pixels per model
unit, and every row below is picked off those renders like a drawing. Nothing is taken from its
meshes. Points are written in the model's units read off the renders' grid:
  Y along the car (front negative), Z up, U out to the side from the centre line (half widths).

Scale: the wheel centres, 2.3105 units apart, set the scale from the published 2385 mm wheelbase.
Both overhangs stretch by the same factor to the published 3678 mm length. Half widths are set to
the published 1594 mm width. The ground sits one published tyre radius (175/60 R14) under the
wheel centres.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py  -> rows.json
"""
import json
from pathlib import Path

import numpy as np
from scipy.interpolate import PchipInterpolator

WHEELBASE = 2.385
LENGTH = 3.678
WIDTH = 1.594
TYRE_R = round((14 * 25.4 + 2 * 0.60 * 175) / 2000, 4)     # 175/60 R14
RIM_R = round(14 * 25.4 / 2000, 4)
Y_FRONT, Y_REAR, Z_WHEEL = -1.0885, 1.222, 0.3085           # wheel centres on the side render
Y_NOSE, Y_TAIL = -1.7925, 1.7825                            # the body's ends on the side render
HALF_MAX = 0.795                                            # widest half width on the top render
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
    """A length along the car or up it, in model units, to metres."""
    return round(float(d * S), 4)


def side(pts):
    return [[X(y), Z(z)] for y, z in pts]


def end(pts):
    """Front or rear view points (half width, Z), the car's left half."""
    return [[H(w), Z(z)] for w, z in pts]


def plan(pts):
    return [[X(y), H(w)] for y, w in pts]


# ---------------------------------------------------------------- body rows (side and top renders)
# Over the bonnet the side render's top edge is the wing's top; the bonnet's middle is CENTRE,
# a little crowned above it.
CREST = [(-1.7925, 0.50), (-1.785, 0.54), (-1.765, 0.585), (-1.74, 0.625), (-1.71, 0.656), (-1.69, 0.676),
         (-1.67, 0.69), (-1.65, 0.70), (-1.60, 0.722), (-1.55, 0.740), (-1.50, 0.757), (-1.45, 0.772),
         (-1.40, 0.785), (-1.30, 0.805), (-1.20, 0.823), (-1.10, 0.840), (-1.00, 0.857), (-0.90, 0.873),
         (-0.865, 0.877)]
CENTRE = [(-1.7925, 0.50), (-1.785, 0.54), (-1.765, 0.585), (-1.74, 0.627), (-1.71, 0.662), (-1.69, 0.684),
          (-1.67, 0.699), (-1.65, 0.710), (-1.60, 0.733), (-1.55, 0.752), (-1.50, 0.769), (-1.45, 0.784),
          (-1.40, 0.797), (-1.30, 0.817), (-1.20, 0.834), (-1.10, 0.850), (-1.00, 0.865), (-0.90, 0.877),
          (-0.865, 0.879)]
# Roof and tail along the middle: the windscreen, roof, the hatch glass, the tailgate and the
# bumper's top.
TOP = [(-0.865, 0.879), (-0.80, 0.907), (-0.70, 0.977), (-0.60, 1.045), (-0.50, 1.112), (-0.40, 1.177),
       (-0.30, 1.242), (-0.25, 1.274), (-0.20, 1.295), (-0.15, 1.310), (-0.10, 1.319), (0.00, 1.328),
       (0.10, 1.334), (0.20, 1.338), (0.35, 1.342), (0.50, 1.344), (0.65, 1.342), (0.80, 1.336),
       (0.90, 1.328), (1.00, 1.320), (1.10, 1.307), (1.15, 1.292), (1.20, 1.270), (1.25, 1.232),
       (1.30, 1.195), (1.35, 1.152), (1.40, 1.112), (1.45, 1.070), (1.50, 1.027), (1.55, 0.982),
       (1.60, 0.935), (1.64, 0.900), (1.67, 0.870), (1.69, 0.845), (1.71, 0.800), (1.725, 0.750),
       (1.735, 0.700), (1.742, 0.650), (1.755, 0.622), (1.77, 0.605), (1.7825, 0.580)]
# Silhouette bottom: the bumper's chin, the sills carried straight across the arches, the tail.
BOTTOM = [(-1.7925, 0.45), (-1.785, 0.40), (-1.765, 0.33), (-1.75, 0.255), (-1.72, 0.227), (-1.60, 0.215),
          (-1.40, 0.206), (-1.00, 0.200), (-0.50, 0.197), (0.00, 0.212), (0.50, 0.217), (0.90, 0.217),
          (1.10, 0.235), (1.30, 0.255), (1.50, 0.272), (1.65, 0.280), (1.70, 0.310), (1.74, 0.330),
          (1.76, 0.440), (1.7825, 0.540)]
# Half width in plan (top render), mirrors and tyres left out.
HALF = [(-1.7925, 0.24), (-1.79, 0.287), (-1.77, 0.390), (-1.75, 0.455), (-1.73, 0.510), (-1.71, 0.560),
        (-1.69, 0.600), (-1.67, 0.632), (-1.65, 0.657), (-1.63, 0.677), (-1.61, 0.695), (-1.59, 0.710),
        (-1.55, 0.730), (-1.50, 0.744), (-1.45, 0.752), (-1.40, 0.760), (-1.30, 0.772), (-1.20, 0.789),
        (-1.10, 0.794), (-0.90, 0.793), (-0.75, 0.788), (-0.60, 0.785), (0.80, 0.786), (0.95, 0.794),
        (1.05, 0.793), (1.15, 0.790), (1.25, 0.787), (1.35, 0.782), (1.45, 0.777), (1.50, 0.772),
        (1.55, 0.756), (1.60, 0.740), (1.64, 0.713), (1.68, 0.677), (1.70, 0.650), (1.72, 0.595),
        (1.74, 0.500), (1.76, 0.390), (1.775, 0.270), (1.7825, 0.170)]
# Height of the widest point, read off the front and rear renders.
WIDEST = [(-1.7925, 0.48), (-1.70, 0.52), (-1.50, 0.57), (1.50, 0.57), (1.70, 0.55), (1.7825, 0.56)]
# How far the sill or bumper bottom tucks in from the widest point (front and rear renders).
TUCK = [(-1.7925, 0.03), (-1.0, 0.033), (1.0, 0.033), (1.7825, 0.03)]
# Shoulder along the cabin (the belt) and how far it sits in from the widest point.
# Over the bonnet the shoulder is the wing's top.
SHOULDER = [(-0.865, 0.833), (-0.60, 0.830), (-0.40, 0.835), (0.00, 0.850), (0.40, 0.865), (0.80, 0.880),
            (1.05, 0.890), (1.30, 0.892), (1.50, 0.888), (1.63, 0.880), (1.70, 0.82), (1.7825, 0.57)]
SHOULDER_IN = [(-1.7925, 0.06), (-1.60, 0.07), (-1.20, 0.085), (-0.865, 0.082), (-0.60, 0.074),
               (0.80, 0.074), (1.20, 0.078), (1.50, 0.08), (1.7825, 0.06)]
BONNET_END = -0.865       # the bonnet meets the windscreen's foot
# Deck height at the centre under the greenhouse (hidden), joined to the cowl and the tailgate.
DECK = [(-0.865, 0.879), (-0.80, 0.880), (1.58, 0.900), (1.63, 0.900)]

# Greenhouse, from the windscreen's foot to the hatch glass's foot.
GH = (-0.865, 1.63)
# Glass foot in plan along the cabin (top render), and the roof edge in side (Y, Z) and plan (Y, U):
# the A-pillar, the roof's side and the hatch's sides.
GLASS_FOOT = [(-0.865, 0.66), (-0.70, 0.70), (-0.40, 0.712), (0.90, 0.712), (1.20, 0.708), (1.45, 0.70),
              (1.63, 0.68)]
ROOF_Z = [(-0.865, 0.875), (-0.70, 0.960), (-0.50, 1.070), (-0.30, 1.180), (-0.20, 1.240), (-0.10, 1.275),
          (0.00, 1.288), (0.60, 1.300), (1.00, 1.292), (1.10, 1.280), (1.20, 1.250), (1.30, 1.180),
          (1.40, 1.095), (1.50, 1.008), (1.63, 0.890)]
ROOF_W = [(-0.865, 0.64), (-0.70, 0.645), (-0.50, 0.60), (-0.30, 0.555), (-0.15, 0.530), (0.00, 0.528),
          (0.60, 0.528), (1.00, 0.530), (1.20, 0.550), (1.40, 0.580), (1.63, 0.610)]


def table(rows):
    a = np.array(rows, float)
    return PchipInterpolator(a[:, 0], a[:, 1], extrapolate=True)


def stations():
    """Each station's section values in metres, at every Y any row was picked at."""
    t = {k: table(v) for k, v in dict(crest=CREST, centre=CENTRE, top=TOP, bot=BOTTOM, half=HALF, zw=WIDEST,
                                       tuck=TUCK, zs=SHOULDER, sin=SHOULDER_IN, deck=DECK, rz=ROOF_Z,
                                       rw=ROOF_W, wg=GLASS_FOOT).items()}
    ys = sorted({round(float(y), 4) for rows in (CREST, TOP, BOTTOM, HALF) for y, _ in rows}
                | {round(float(y), 4) for y in np.arange(-1.78, 1.78, 0.04)})
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
            zs = min(f["zs"], deck - 0.012)
        else:
            top = f["top"]
            deck = top                                    # the tailgate: the middle is the top
            zs = min(f["zs"], top - 0.012)
        st = dict(x=X(y), zb=Z(f["bot"]), w=H(f["half"]), zw=Z(f["zw"]), tuck=H(f["tuck"]),
                  zs=Z(zs), ws=H(f["half"] - f["sin"]), zd=Z(deck))
        if in_gh:
            st.update(zt=Z(top), zr=Z(min(f["rz"], top - 0.006)), wr=H(f["rw"]), wg=H(f["wg"]))
        out.append(st)
    return out


# ---------------------------------------------------------------- glass (side render)
_BELT = table([(-0.70, 0.830), (-0.40, 0.835), (0.00, 0.850), (0.40, 0.865), (0.80, 0.880), (1.05, 0.890),
               (1.20, 0.895)])


def belt(y):
    """The belt line under the side windows, rising gently to the back."""
    return float(_BELT(y))


# The side window opening (glass and its black frame), from the side render: the A-pillar edge,
# the roof edge and the quarter window's slanted rear edge, all straight or one even curve.
DLO = {"a_foot": (-0.62, belt(-0.62)), "a_head": (-0.07, 1.262), "roof": [(-0.07, 1.262), (0.40, 1.272),
       (0.96, 1.252)], "c_head": (0.99, 1.236), "c_foot": (1.135, 0.905)}
VENT_Y = -0.43                                    # the black corner ahead of the door glass ends here
B_PILLAR = ((0.458, 0.866), (0.451, 1.278), 0.11)   # the black division's foot, head and width

# Door shut line (side render), straight runs and plain arcs.
DOOR_BOTTOM = 0.288                               # the door's bottom edge, one straight line
FRONT_DOOR = [(-0.684, belt(-0.684) - 0.006), (-0.689, 0.50), (-0.662, DOOR_BOTTOM)]   # front edge
REAR_DOOR = [(0.458, 0.866), (0.448, 0.48), (0.42, DOOR_BOTTOM)]   # rear edge, up the division's middle
SHUT_ROUND = 0.03                                 # the door's bottom corners rounded this much
EDGE_ROUND = 0.25                                 # its front and rear edges bow this gently
# Windscreen (top render: Y, half width): its foot and head are even curves, its sides straight.
SCREEN = {"y_base_mid": -0.865, "y_base_edge": -0.72, "w_base": 0.645, "y_top_mid": -0.232, "y_top_edge": -0.19,
          "w_top": 0.50}
# Hatch glass (top render): head and foot.
REAR_GLASS = {"y_top_mid": 1.195, "y_top_edge": 1.215, "w_top": 0.46, "y_base_mid": 1.63, "y_base_edge": 1.60,
              "w_base": 0.605}
# Bonnet shut line (top render: Y, half width): across the nose over the grille, back along the
# headlamps' top edge and the wings' inner edge, then in to the windscreen's foot inside its corners.
BONNET = [(-1.700, 0.0), (-1.702, 0.20), (-1.700, 0.34), (-1.665, 0.43), (-1.60, 0.53), (-1.53, 0.615),
          (-1.47, 0.668), (-1.40, 0.672), (-1.10, 0.668), (-0.85, 0.668), (-0.77, 0.662), (-0.74, 0.630),
          (-0.745, 0.60)]

# ---------------------------------------------------------------- front (front render, left half)
HEAD = [(0.359, 0.616), (0.42, 0.645), (0.52, 0.674), (0.62, 0.692), (0.67, 0.697), (0.714, 0.692),
        (0.729, 0.64), (0.734, 0.567), (0.60, 0.566), (0.375, 0.567), (0.362, 0.59)]
# The slim grille band between the headlamps.
GRILLE = [(0.0, 0.632), (0.30, 0.632), (0.342, 0.620), (0.347, 0.592), (0.33, 0.583), (0.0, 0.583)]
# The black rubbing strip across the bumper, wrapping round its corners.
STRIP_F = (0.266, 0.436, 0.507)                   # inner end, bottom and top (it runs out to the corner)
STRIP_F_END = -1.394                              # where it ends along the side (side render)
FOG = (0.5625, 0.34, 0.048)                       # round fog lamp: centre and radius
# Two slots in the bumper's lower half, their outer ends cut on the slant.
SLOT_UP = [(0.0, 0.397), (0.44, 0.397), (0.448, 0.385), (0.43, 0.356), (0.0, 0.356)]
SLOT_LO = [(0.0, 0.319), (0.418, 0.319), (0.427, 0.308), (0.40, 0.277), (0.0, 0.277)]

# ---------------------------------------------------------------- rear (rear render, left half)
TAIL = [(0.732, 0.626), (0.406, 0.626), (0.451, 0.712), (0.50, 0.745), (0.602, 0.765), (0.70, 0.765),
        (0.719, 0.752), (0.726, 0.70)]
STRIP_R = (0.0, 0.497, 0.580)                     # across the whole tail: inner end, bottom and top
STRIP_R_END = 1.497                               # where it ends along the side (side render)
SLOT_R = [(0.0, 0.42), (0.47, 0.42), (0.482, 0.408), (0.45, 0.37), (0.0, 0.37)]
RAIN = (0.06, 0.378, 0.412)                       # rain light in the slot: half width, bottom, top
# The tailgate's outline on the tail: across above the bumper, up the lamps' inner edges, out
# round the glass and over the top under the spoiler.
HATCH = [(0.0, 0.636), (0.398, 0.636), (0.446, 0.716), (0.60, 0.79), (0.637, 0.86), (0.635, 0.95),
         (0.59, 1.10), (0.53, 1.22), (0.49, 1.272), (0.0, 1.282)]
GATE_Z, GATE_LOW = 0.765, 0.675                   # the tailgate's middle under the glass, and low on it
EXHAUST = (-0.42, 0.262, 0.03)                    # one round tip under the bumper's right: U, Z, radius
EXHAUST_END = 1.745

# ---------------------------------------------------------------- other parts
MIRROR = {"y0": -0.56, "y1": -0.36, "w0": 0.74, "w1": 0.913, "z0": 0.857, "z1": 0.965}
# Roof spoiler over the hatch (side and top renders): its section from the roof's end out to its
# lip and back under to the glass, and its half span.
SPOILER = [(1.12, 1.306), (1.30, 1.293), (1.302, 1.258), (1.24, 1.205), (1.17, 1.235)]
SPOILER_SPAN = 0.544

data = {
    "name": "LibertyOmaha",
    "wheel": {"r": TYRE_R, "rim_r": RIM_R, "width_f": 0.175, "width_r": 0.175, "outer_y": H(0.777),
              "outer_y_r": H(0.777), "front_x": X(Y_FRONT), "rear_x": X(Y_REAR), "arch_r": L(0.315)},
    "stations": stations(),
    "gh": [X(GH[1]), X(GH[0])],
    "dlo": {"a_foot": side([DLO["a_foot"]])[0], "a_head": side([DLO["a_head"]])[0], "roof": side(DLO["roof"]),
            "c_head": side([DLO["c_head"]])[0], "c_foot": side([DLO["c_foot"]])[0]},
    "vent_x": X(VENT_Y),
    "b_pillar": {"foot": side([B_PILLAR[0]])[0], "head": side([B_PILLAR[1]])[0], "w": L(B_PILLAR[2])},
    "front_door": side(FRONT_DOOR),
    "door_bottom": Z(DOOR_BOTTOM),
    "rear_door": side(REAR_DOOR),
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
    "grille": end(GRILLE),
    "strip_f": [H(STRIP_F[0]), Z(STRIP_F[1]), Z(STRIP_F[2])],
    "strip_side_f": X(STRIP_F_END),
    "strip_side_r": X(STRIP_R_END),
    "fog": [H(FOG[0]), Z(FOG[1]), L(FOG[2])],
    "slot_up": end(SLOT_UP),
    "slot_lo": end(SLOT_LO),
    "tail": end(TAIL),
    "strip_r": [H(STRIP_R[0]), Z(STRIP_R[1]), Z(STRIP_R[2])],
    "slot_r": end(SLOT_R),
    "rain": [H(RAIN[0]), Z(RAIN[1]), Z(RAIN[2])],
    "hatch": end(HATCH),
    "gate_z": Z(GATE_Z),
    "gate_low": Z(GATE_LOW),
    "exhaust": [H(EXHAUST[0]), Z(EXHAUST[1]), L(EXHAUST[2])],
    "exhaust_x": X(EXHAUST_END),
    "mirror": {"x_back": X(MIRROR["y1"]), "x_front": X(MIRROR["y0"]), "y_in": H(MIRROR["w0"]),
               "y_out": H(MIRROR["w1"]), "z0": Z(MIRROR["z0"]), "z1": Z(MIRROR["z1"])},
    "spoiler": side(SPOILER),
    "spoiler_span": H(SPOILER_SPAN),
    "picks": {"frontbadge": 1, "rearbadge": 1, "name": 1, "spoiler": 1},     # Ivan's picks, 2026-11-12
}
# belt line z(x) in build space, sampled for parts to interpolate
_ys = np.linspace(-0.75, 1.20, 40)
data["belt"] = [[X(y), Z(belt(y))] for y in _ys]

if __name__ == "__main__":
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(data, indent=1))
    st = data["stations"]
    print("wrote", out, len(st), "stations; nose", max(s["x"] for s in st), "tail", min(s["x"] for s in st),
          "length", round(max(s["x"] for s in st) - min(s["x"] for s in st), 4),
          "roof", max(s.get("zt", 0) for s in st), "half", max(s["w"] for s in st), data["wheel"])
