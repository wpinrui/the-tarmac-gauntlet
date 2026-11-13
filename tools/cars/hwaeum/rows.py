"""Traced rows for the Seorak Hwaeum, based on the 2011 Kia cee'd five-door hatchback.

Reference: Ivan's 3D model of a 2011 cee'd 1.6 CRDi (a .glb, kept out of the repo). Its paint is
turned green so the red tail lamps stand apart, and it is rendered orthographically from the side,
front, rear and top on a white background at 500 pixels per unit, plus one side render of the car's
outer half only (for the wing's crest). Every row below is picked off those renders like a drawing.
Nothing is taken from its meshes. Points are written in the renders' units read off their grid:
  Y along the car (front negative), Z up, U out to the side from the centre line (half widths).

Scale: the wheel centres, 2.647 units apart, set the scale from the published 2650 mm wheelbase.
Both overhangs stretch by the same factor to the published 4260 mm length. Half widths are set to
the published 1790 mm width. The ground sits one published tyre radius (205/55 R16) under the
wheel centres.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py  -> rows.json
"""
import json
from pathlib import Path

import numpy as np
from scipy.interpolate import PchipInterpolator

WHEELBASE = 2.650
LENGTH = 4.260
WIDTH = 1.790
TYRE_R = round((16 * 25.4 + 2 * 0.55 * 205) / 2000, 4)     # 205/55 R16
RIM_R = round(16 * 25.4 / 2000, 4)
Y_FRONT, Y_REAR, Z_WHEEL = -1.2852, 1.3618, 0.316           # wheel centres on the side render
Y_NOSE, Y_TAIL = -2.200, 2.076                              # the body's ends on the side render
HALF_MAX = 0.888                                            # widest half width, mirrors left out
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
# front face up to the windscreen's foot; the wing's top (CREST) is read off the outer half's render.
CENTRE = [(-2.200, 0.420), (-2.186, 0.500), (-2.152, 0.580), (-2.116, 0.660), (-2.066, 0.740), (-2.018, 0.780),
          (-1.946, 0.820), (-1.850, 0.860), (-1.720, 0.900), (-1.544, 0.940), (-1.336, 0.980), (-1.170, 0.995)]
CREST = [(-2.200, 0.400), (-2.150, 0.430), (-2.100, 0.470), (-2.050, 0.540), (-2.000, 0.656), (-1.950, 0.736),
         (-1.900, 0.770), (-1.850, 0.800), (-1.800, 0.832), (-1.700, 0.862), (-1.600, 0.888), (-1.500, 0.914),
         (-1.400, 0.936), (-1.300, 0.958), (-1.200, 0.978), (-1.170, 0.982)]
# Roof and tail along the middle: the windscreen, the roof, the hatch glass, the tailgate and the
# bumper's back, down to where it meets the bottom. The roof spoiler over the glass is left out.
TOP = [(-1.170, 0.995), (-1.100, 1.005), (-1.000, 1.035), (-0.900, 1.084), (-0.800, 1.134), (-0.700, 1.184),
       (-0.600, 1.236), (-0.500, 1.288), (-0.400, 1.332), (-0.300, 1.374), (-0.200, 1.414), (-0.100, 1.438),
       (0.000, 1.456), (0.200, 1.476), (0.400, 1.484), (0.600, 1.488), (0.800, 1.482), (1.000, 1.472),
       (1.200, 1.458), (1.400, 1.440), (1.500, 1.426), (1.550, 1.414), (1.600, 1.388), (1.650, 1.342),
       (1.700, 1.296), (1.750, 1.248), (1.800, 1.196), (1.850, 1.148), (1.880, 1.115), (1.920, 1.075),
       (1.960, 1.020), (1.990, 0.960), (2.003, 0.880), (2.008, 0.780), (2.024, 0.680), (2.062, 0.620),
       (2.073, 0.560), (2.076, 0.500)]
# Silhouette bottom: the front bumper's foot, the sills carried straight across the arches, the
# rear bumper's underside and its back rising to meet the top.
BOTTOM = [(-2.200, 0.400), (-2.192, 0.300), (-2.180, 0.220), (-2.100, 0.195), (-1.900, 0.182), (-1.700, 0.178),
          (-1.000, 0.180), (-0.600, 0.184), (0.000, 0.190), (0.600, 0.196), (1.200, 0.200), (1.660, 0.220),
          (1.930, 0.255), (2.000, 0.270), (2.040, 0.285), (2.058, 0.300), (2.068, 0.400), (2.076, 0.480)]
# Half width in plan (top render), the mirrors and door handles left out.
HALF = [(-2.200, 0.300), (-2.150, 0.380), (-2.100, 0.494), (-2.050, 0.586), (-2.000, 0.662), (-1.950, 0.714),
        (-1.900, 0.756), (-1.850, 0.786), (-1.800, 0.808), (-1.700, 0.844), (-1.600, 0.864), (-1.500, 0.880),
        (-1.300, 0.882), (-1.100, 0.888), (-0.900, 0.877), (-0.700, 0.868), (-0.400, 0.878), (0.000, 0.884),
        (0.500, 0.874), (0.900, 0.862), (1.100, 0.882), (1.400, 0.880), (1.600, 0.876), (1.700, 0.856),
        (1.800, 0.814), (1.900, 0.756), (1.950, 0.702), (2.000, 0.602), (2.050, 0.408), (2.076, 0.300)]
FLARES = []           # the cee'd has no arch flares
# The sides stand full width over a band: its bottom (WIDEST) and top (WIDE_TOP), read off the
# front and rear renders.
WIDEST = [(-2.200, 0.300), (-1.700, 0.300), (-1.000, 0.300), (0.950, 0.320), (1.600, 0.300), (2.076, 0.350)]
WIDE_TOP = [(-2.200, 0.550), (-2.000, 0.650), (-1.700, 0.750), (-1.000, 0.800), (0.950, 0.820), (1.600, 0.850),
            (1.900, 0.850), (2.076, 0.600)]
# How far the sill or bumper bottom tucks in from the widest point (front and rear renders).
TUCK = [(-2.200, 0.050), (-1.600, 0.040), (-0.800, 0.050), (0.900, 0.050), (1.600, 0.040), (2.076, 0.050)]
# Shoulder along the cabin (the belt) and how far it sits in from the widest point.
# Over the bonnet the shoulder is the wing's top (CREST); behind the cabin it is the quarter's top.
SHOULDER = [(-1.170, 0.982), (-1.000, 0.972), (-0.800, 0.958), (-0.680, 0.950), (-0.300, 0.968), (0.250, 0.993),
            (0.700, 1.022), (1.080, 1.052), (1.400, 1.072), (1.700, 1.078), (1.850, 1.068), (1.950, 1.000),
            (2.000, 0.850), (2.076, 0.500)]
SHOULDER_IN = [(-2.200, 0.060), (-1.600, 0.060), (-1.000, 0.070), (-0.700, 0.080), (1.000, 0.080), (1.700, 0.080),
               (2.076, 0.060)]
BONNET_END = -1.170       # the bonnet meets the windscreen's foot
# Deck height at the centre under the greenhouse (hidden), joined to the cowl and the tailgate.
DECK = [(-1.170, 0.995), (-1.100, 1.000), (1.800, 1.100), (1.880, 1.115)]

# Greenhouse, from the windscreen's foot to the hatch glass's foot.
GH = (-1.170, 1.880)
# Glass foot in plan along the cabin (top render), and the roof edge in side (Y, Z) and plan (Y, U):
# the A-pillar, the roof's side and the hatch's sides.
GLASS_FOOT = [(-1.170, 0.700), (-1.000, 0.740), (-0.700, 0.790), (-0.300, 0.795), (0.500, 0.790), (1.000, 0.780),
              (1.400, 0.750), (1.700, 0.700), (1.880, 0.640)]
ROOF_Z = [(-1.170, 0.990), (-1.000, 1.010), (-0.800, 1.110), (-0.600, 1.210), (-0.400, 1.300), (-0.200, 1.370),
          (-0.050, 1.410), (0.200, 1.430), (0.600, 1.440), (1.000, 1.430), (1.300, 1.410), (1.500, 1.385),
          (1.600, 1.340), (1.700, 1.270), (1.800, 1.190), (1.880, 1.120)]
ROOF_W = [(-1.170, 0.700), (-1.000, 0.700), (-0.600, 0.640), (-0.250, 0.565), (0.000, 0.545), (0.500, 0.535),
          (1.000, 0.535), (1.300, 0.540), (1.500, 0.560), (1.700, 0.600), (1.880, 0.630)]


def table(rows):
    a = np.array(rows, float)
    return PchipInterpolator(a[:, 0], a[:, 1], extrapolate=True)


def stations():
    """Each station's section values in metres, at every Y any row was picked at."""
    t = {k: table(v) for k, v in dict(crest=CREST, centre=CENTRE, top=TOP, bot=BOTTOM, half=HALF, zw=WIDEST, zwt=WIDE_TOP,
                                       tuck=TUCK, zs=SHOULDER, sin=SHOULDER_IN, deck=DECK, rz=ROOF_Z,
                                       rw=ROOF_W, wg=GLASS_FOOT).items()}
    ys = sorted({round(float(y), 4) for rows in (CENTRE, TOP, BOTTOM, HALF) for y, _ in rows}
                | {round(float(y), 4) for y in np.arange(-2.18, 2.07, 0.04)})
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
        st = dict(x=X(y), zb=Z(f["bot"]), w=H(f["half"]), zw=Z(f["zw"]), zwt=Z(f["zwt"]), tuck=H(f["tuck"]),
                  zs=Z(zs), ws=H(f["half"] - f["sin"]), zd=Z(deck))
        if in_gh:
            st.update(zt=Z(top), zr=Z(min(f["rz"], top - 0.006)), wr=H(f["rw"]), wg=H(f["wg"]))
        out.append(st)
    return out


# ---------------------------------------------------------------- glass (side render)
_BELT = table([(-0.80, 0.950), (-0.70, 0.955), (-0.30, 0.975), (0.25, 1.000), (0.70, 1.028), (1.08, 1.058),
               (1.25, 1.070)])


def belt(y):
    """The belt line under the side windows, rising evenly to the back."""
    return float(_BELT(y))


# The side window opening (glass and its black frame), from the side render: the A-pillar edge a
# straight line, the roof edge one even curve and the quarter window's rear edge a straight line.
DLO = {"a_foot": (-0.70, belt(-0.70)), "a_head": (-0.14, 1.395), "roof": [(-0.14, 1.395), (0.30, 1.410),
       (0.70, 1.410), (1.00, 1.400), (1.30, 1.355)], "c_head": (1.34, 1.340), "c_foot": (1.17, belt(1.17))}
VENT_Y = -0.45                                     # the black corner ahead of the door glass ends here
B_PILLAR = ((0.280, belt(0.280)), (0.340, 1.400), 0.12)    # the black division's foot, head and width
C_POST = ((1.075, belt(1.075)), (1.020, 1.400), 0.05)      # the rear door glass's rear post

# Door shut lines (side render), straight runs and plain arcs. Only the front doors open; the rear
# doors are shut lines.
DOOR_BOTTOM = 0.290                               # the doors' bottom edge, one straight line
FRONT_DOOR = [(-0.790, belt(-0.790) - 0.006), (-0.848, 0.700), (-0.835, DOOR_BOTTOM)]   # front edge
REAR_DOOR = [(0.280, belt(0.280)), (0.262, 0.620), (0.255, DOOR_BOTTOM)]   # rear edge, from the division's middle
# The rear door's rear edge: from the window's rear foot down round the front of the rear arch.
BACK_DOOR = [(1.190, belt(1.190) - 0.004), (1.235, 0.910), (1.200, 0.770), (1.100, 0.590), (1.030, 0.460),
             (0.970, 0.350), (0.900, 0.305)]
SHUT_ROUND = 0.05                                 # the doors' bottom corners rounded this much
EDGE_ROUND = 0.20                                 # their front and rear edges bow this gently
# Windscreen (top render: Y, half width): its foot and head are even curves, its sides straight.
SCREEN = {"y_base_mid": -1.17, "y_base_edge": -1.00, "w_base": 0.700, "y_top_mid": -0.26, "y_top_edge": -0.22,
          "w_top": 0.560}
# Hatch glass (top render): head and foot.
REAR_GLASS = {"y_top_mid": 1.52, "y_top_edge": 1.50, "w_top": 0.550, "y_base_mid": 1.88, "y_base_edge": 1.78,
              "w_base": 0.630}
# Bonnet shut line. Its front (front render: U, Z) runs across the nose above the grille, rises to
# the headlamp's top edge and follows it out. From there (top render: Y, half width) it runs back
# along the wing's inner edge, then bends in to the windscreen's foot, inside its corner.
BONNET_NOSE = [(0.0, 0.735), (0.30, 0.735), (0.45, 0.728), (0.53, 0.712), (0.60, 0.775), (0.70, 0.812),
               (0.78, 0.815), (0.82, 0.800)]
BONNET = [(-1.62, 0.800), (-1.50, 0.787), (-1.30, 0.778), (-1.10, 0.768), (-1.045, 0.760), (-1.025, 0.640)]

# ---------------------------------------------------------------- front (front render, left half)
HEAD = [(0.470, 0.612), (0.520, 0.690), (0.600, 0.760), (0.700, 0.798), (0.780, 0.800), (0.810, 0.750),
        (0.800, 0.670), (0.770, 0.620), (0.700, 0.612), (0.600, 0.610)]
# The grille between the headlamps and the wide intake in the bumper below it.
GRILLE = [(0.0, 0.712), (0.200, 0.715), (0.440, 0.716), (0.455, 0.700), (0.410, 0.620), (0.300, 0.585),
          (0.100, 0.565), (0.0, 0.560)]
INTAKE = [(0.0, 0.440), (0.390, 0.440), (0.440, 0.380), (0.490, 0.290), (0.470, 0.255), (0.0, 0.255)]
# Fog lamps in the bumper's corners: their recess outline.
FOG = [(0.580, 0.425), (0.730, 0.430), (0.750, 0.400), (0.740, 0.300), (0.620, 0.300), (0.570, 0.350)]

# ---------------------------------------------------------------- rear (rear render, left half)
# Each tail lamp wraps round the tail's corner onto the side. Its outline on the tail (rear render:
# U, Z), from the inner top corner down and out along the bottom to the corner...
TAIL = [(0.620, 1.050), (0.600, 1.000), (0.570, 0.920), (0.535, 0.790), (0.600, 0.783), (0.700, 0.785),
        (0.780, 0.800)]
# ...then on the side (side render: Y, Z) along the bottom forward to the pointed tip and back
# along the top to the corner...
TAIL_SIDE = [(1.960, 0.800), (1.860, 0.855), (1.760, 0.910), (1.660, 0.950), (1.700, 1.000), (1.735, 1.045),
             (1.800, 1.052), (1.880, 1.052)]
# ...and on the tail again along the top back to the start.
TAIL_TOP = [(0.760, 1.052), (0.680, 1.053)]
TAIL_AXIS = (1.72, 0.52)  # the corner's axis in plan (top render: Y, half width), the lamp's rays fan from it
# The dark band low across the bumper's back and the rain light in its middle.
BAND_R = [(0.0, 0.300), (0.600, 0.300), (0.650, 0.320), (0.620, 0.380), (0.0, 0.380)]
RAIN = (0.070, 0.318, 0.362)                      # rain light: half width, bottom, top
# The tailgate's outline on the tail (rear render): across above the bumper, up the lamps' inner
# edges and out round the glass's sides.
HATCH = [(0.0, 0.646), (0.400, 0.648), (0.470, 0.690), (0.510, 0.750), (0.535, 0.790), (0.570, 0.920),
         (0.600, 1.000), (0.625, 1.060), (0.630, 1.150), (0.600, 1.280), (0.560, 1.350)]
# Its top edge runs across the roof's end, traced from above (top render: Y, half width) from the
# side's top corner in to the middle.
HATCH_TOP = [(1.500, 0.560), (1.450, 0.450), (1.442, 0.0)]
GATE_Z, GATE_LOW = 0.980, 0.720                   # the tailgate between the lamps (the badge), and low on it
EXHAUST = (-0.450, 0.300, 0.032)                  # one round tip under the bumper's right: U, Z, radius
EXHAUST_END = 2.040

# ---------------------------------------------------------------- other parts
MIRROR = {"y0": -0.69, "y1": -0.49, "w0": 0.80, "w1": 0.99, "z0": 0.990, "z1": 1.110}   # at the door's front corner

data = {
    "name": "SeorakHwaeum",
    "wheel": {"r": TYRE_R, "rim_r": RIM_R, "width_f": 0.205, "width_r": 0.205, "outer_y": H(0.865),
              "outer_y_r": H(0.865), "front_x": X(Y_FRONT), "rear_x": X(Y_REAR), "arch_r": round(TYRE_R + 0.05, 4)},
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
    "shut_round": L(SHUT_ROUND),
    "edge_round": L(EDGE_ROUND),
    "screen": {"x_base_mid": X(SCREEN["y_base_mid"]), "x_base_edge": X(SCREEN["y_base_edge"]),
               "w_base": H(SCREEN["w_base"]), "x_top_mid": X(SCREEN["y_top_mid"]),
               "x_top_edge": X(SCREEN["y_top_edge"]), "w_top": H(SCREEN["w_top"])},
    "rear_glass": {"x_top_mid": X(REAR_GLASS["y_top_mid"]), "x_top_edge": X(REAR_GLASS["y_top_edge"]),
                   "w_top": H(REAR_GLASS["w_top"]), "x_base_mid": X(REAR_GLASS["y_base_mid"]),
                   "x_base_edge": X(REAR_GLASS["y_base_edge"]), "w_base": H(REAR_GLASS["w_base"])},
    "bonnet_nose": end(BONNET_NOSE),
    "bonnet": plan(BONNET),
    "head": end(HEAD),
    "grille": end(GRILLE),
    "intake": end(INTAKE),
    "fog": end(FOG),
    "tail": end(TAIL),
    "tail_side": side(TAIL_SIDE),
    "tail_top": end(TAIL_TOP),
    "tail_axis": plan([TAIL_AXIS])[0],
    "band_r": end(BAND_R),
    "rain": [H(RAIN[0]), Z(RAIN[1]), Z(RAIN[2])],
    "hatch": end(HATCH),
    "hatch_top": plan(HATCH_TOP),
    "gate_z": Z(GATE_Z),
    "gate_low": Z(GATE_LOW),
    "tail_mid_z": Z(0.920),                       # the tail lamps' middle height on the tail
    "exhaust": [H(EXHAUST[0]), Z(EXHAUST[1]), L(EXHAUST[2])],
    "exhaust_x": X(EXHAUST_END),
    "mirror": {"x_back": X(MIRROR["y1"]), "x_front": X(MIRROR["y0"]), "y_in": H(MIRROR["w0"]),
               "y_out": H(MIRROR["w1"]), "z0": Z(MIRROR["z0"]), "z1": Z(MIRROR["z1"])},
    "picks": {"frontbadge": 1, "rearbadge": 1, "name": 1},   # placeholders until Ivan picks in the viewer
}
# belt line z(x) in build space, sampled for parts to interpolate
_ys = np.linspace(-0.80, 1.25, 48)
data["belt"] = [[X(y), Z(belt(y))] for y in _ys]

if __name__ == "__main__":
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(data, indent=1))
    st = data["stations"]
    print("wrote", out, len(st), "stations; nose", max(s["x"] for s in st), "tail", min(s["x"] for s in st),
          "length", round(max(s["x"] for s in st) - min(s["x"] for s in st), 4),
          "roof", max(s.get("zt", 0) for s in st), "half", max(s["w"] for s in st), data["wheel"])
