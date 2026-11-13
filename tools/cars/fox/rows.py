"""Traced rows for the Nusa Fox, based on the 2015 Proton Satria Neo, in its S2000 rally body.

Reference: Ivan's 3D model of a Satria Neo S2000 (a .glb, kept out of the repo). It is rendered
orthographically from the side, front, rear and top on a plain background at 500 pixels per model
unit, and every row below is picked off those renders like a drawing. Nothing is taken from its
meshes. Points are written in the model's units read off the renders' grid:
  Y along the car (front negative), Z up, U out to the side from the centre line (half widths).
Where the model's two sides differ by a few hundredths, the two are averaged.

Scale: the wheel centres, 2.636 units apart, set the scale from the published 2440 mm wheelbase.
Both overhangs stretch by the same factor to the published 3898 mm length. Half widths are set to
the published 1745 mm width over the arches. The ground sits one published tyre radius
(195/55 R15) under the wheel centres.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py  -> rows.json
"""
import json
from pathlib import Path

import numpy as np
from scipy.interpolate import PchipInterpolator

WHEELBASE = 2.440
LENGTH = 3.898
WIDTH = 1.745
TYRE_R = round((15 * 25.4 + 2 * 0.55 * 195) / 2000, 4)     # 195/55 R15
RIM_R = round(15 * 25.4 / 2000, 4)
Y_FRONT, Y_REAR, Z_WHEEL = -1.2163, 1.4197, -0.1605         # wheel centres on the side render
Y_NOSE, Y_TAIL = -2.125, 2.045                              # the body's ends on the side render
HALF_MAX = 0.955                                            # widest half width, over the arches
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
# Over the bonnet the side render's top edge is the bonnet's middle (CENTRE); the wing's top
# (CREST) is read off the side render of the car's outer half, a little lower.
CENTRE = [(-2.125, -0.10), (-2.12, -0.02), (-2.11, 0.02), (-2.09, 0.048), (-2.05, 0.085), (-2.00, 0.138),
          (-1.95, 0.192), (-1.90, 0.228), (-1.85, 0.254), (-1.80, 0.274), (-1.70, 0.306), (-1.60, 0.338),
          (-1.50, 0.364), (-1.40, 0.388), (-1.30, 0.410), (-1.20, 0.430), (-1.10, 0.450), (-1.00, 0.464),
          (-0.92, 0.472), (-0.87, 0.476)]
CREST = [(-2.125, -0.12), (-2.12, -0.06), (-2.10, -0.02), (-2.05, 0.02), (-2.00, 0.07), (-1.95, 0.125),
         (-1.90, 0.18), (-1.85, 0.222), (-1.80, 0.255), (-1.70, 0.292), (-1.60, 0.322), (-1.50, 0.348),
         (-1.40, 0.370), (-1.30, 0.388), (-1.20, 0.404), (-1.10, 0.420), (-1.00, 0.434), (-0.90, 0.444),
         (-0.87, 0.446)]
# Roof and tail along the middle: the windscreen, roof, the hatch glass, the tailgate and the
# bumper's back, down to where it meets the bottom. The rally roof scoop and wing are left out.
TOP = [(-0.87, 0.476), (-0.80, 0.513), (-0.70, 0.565), (-0.60, 0.617), (-0.50, 0.669), (-0.40, 0.721),
       (-0.30, 0.772), (-0.20, 0.822), (-0.10, 0.866), (-0.05, 0.886), (0.00, 0.902), (0.10, 0.924),
       (0.20, 0.940), (0.40, 0.960), (0.60, 0.970), (0.90, 0.972), (1.10, 0.966), (1.30, 0.950),
       (1.45, 0.930), (1.55, 0.902), (1.65, 0.832), (1.75, 0.722), (1.82, 0.640), (1.87, 0.590),
       (1.91, 0.552), (1.94, 0.518), (1.962, 0.482), (1.975, 0.440), (1.978, 0.380), (1.982, 0.280),
       (1.992, 0.200), (2.010, 0.158), (2.030, 0.125), (2.040, 0.080), (2.045, 0.020)]
# Silhouette bottom: the splitter, the sills carried straight across the arches, the bumper's
# underside and its back rising to meet the top.
BOTTOM = [(-2.125, -0.22), (-2.12, -0.33), (-2.11, -0.39), (-2.09, -0.410), (-2.00, -0.414),
          (-1.70, -0.410), (-1.62, -0.400), (-1.40, -0.394), (-1.00, -0.390), (-0.60, -0.378),
          (0.90, -0.376), (1.00, -0.366), (1.40, -0.358), (1.70, -0.346), (1.90, -0.342), (1.96, -0.322),
          (2.00, -0.300), (2.020, -0.260), (2.035, -0.150), (2.042, -0.050), (2.045, -0.010)]
# Half width in plan (top render), mirrors and arch flares left out: the bumpers' wide corners and
# the narrower sides between them.
HALF = [(-2.125, 0.30), (-2.115, 0.40), (-2.10, 0.456), (-2.075, 0.55), (-2.05, 0.62), (-2.00, 0.707),
        (-1.95, 0.76), (-1.90, 0.808), (-1.85, 0.854), (-1.80, 0.895), (-1.76, 0.915), (-1.70, 0.900),
        (-1.60, 0.878), (-1.00, 0.872), (-0.80, 0.862), (-0.60, 0.857), (0.00, 0.862), (0.60, 0.880),
        (1.00, 0.893), (1.50, 0.903), (1.70, 0.910), (1.80, 0.912), (1.85, 0.888), (1.90, 0.838),
        (1.95, 0.763), (2.00, 0.640), (2.030, 0.52), (2.045, 0.40)]
# Arch flares (side and top renders): a round blister over each wheel, centred on it, its radius in
# the side view and its half width.
FLARES = [(Y_FRONT, Z_WHEEL, 0.50, 0.955), (Y_REAR, Z_WHEEL, 0.52, 0.955)]
# The flares and the sides stand full width over a band: its bottom (WIDEST) and top (WIDE_TOP),
# read off the front and rear renders.
WIDEST = [(-2.125, -0.25), (-1.60, -0.25), (-0.90, -0.20), (-0.60, -0.15), (0.80, -0.15), (1.00, -0.25),
          (1.90, -0.25), (2.045, -0.20)]
WIDE_TOP = [(-2.125, 0.00), (-1.90, 0.20), (-1.60, 0.34), (-1.00, 0.34), (-0.85, 0.30), (-0.60, 0.28),
            (0.80, 0.30), (1.00, 0.40), (1.75, 0.42), (1.90, 0.40), (2.045, 0.10)]
# How far the sill or bumper bottom tucks in from the widest point (front and rear renders).
TUCK = [(-2.125, 0.03), (-1.60, 0.03), (-0.80, 0.05), (0.90, 0.05), (1.60, 0.03), (2.045, 0.03)]
# Shoulder along the cabin (the belt) and how far it sits in from the widest point.
# Over the bonnet the shoulder is the wing's top (CREST); behind the cabin it is the quarter's top.
SHOULDER = [(-0.87, 0.452), (-0.70, 0.474), (-0.50, 0.492), (-0.30, 0.500), (0.00, 0.505), (0.50, 0.515),
            (1.00, 0.530), (1.40, 0.550), (1.60, 0.562), (1.75, 0.560), (1.87, 0.548), (1.95, 0.500),
            (2.00, 0.30), (2.045, 0.04)]
SHOULDER_IN = [(-2.125, 0.08), (-1.90, 0.10), (-1.60, 0.12), (-1.00, 0.12), (-0.87, 0.12), (-0.75, 0.10),
               (-0.60, 0.09), (0.80, 0.09), (1.00, 0.10), (1.70, 0.10), (1.90, 0.09), (2.045, 0.07)]
BONNET_END = -0.87        # the bonnet meets the windscreen's foot
# Deck height at the centre under the greenhouse (hidden), joined to the cowl and the tailgate.
DECK = [(-0.87, 0.476), (-0.80, 0.480), (1.80, 0.580), (1.87, 0.590)]

# Greenhouse, from the windscreen's foot to the hatch glass's foot.
GH = (-0.87, 1.87)
# Glass foot in plan along the cabin (top render), and the roof edge in side (Y, Z) and plan (Y, U):
# the A-pillar, the roof's side and the hatch's sides. The side glass leans in steeply.
GLASS_FOOT = [(-0.87, 0.70), (-0.70, 0.735), (-0.40, 0.748), (0.90, 0.755), (1.30, 0.752), (1.60, 0.745),
              (1.87, 0.735)]
ROOF_Z = [(-0.87, 0.474), (-0.70, 0.560), (-0.50, 0.665), (-0.30, 0.770), (-0.15, 0.840), (-0.05, 0.872),
          (0.10, 0.886), (0.50, 0.894), (0.90, 0.892), (1.20, 0.878), (1.40, 0.852), (1.55, 0.805),
          (1.65, 0.730), (1.75, 0.645), (1.87, 0.552)]   # the C-pillar runs down into the shoulder
ROOF_W = [(-0.87, 0.70), (-0.60, 0.665), (-0.30, 0.625), (-0.05, 0.592), (0.30, 0.585), (1.00, 0.585),
          (1.30, 0.595), (1.55, 0.620), (1.75, 0.660), (1.87, 0.690)]


def table(rows):
    a = np.array(rows, float)
    return PchipInterpolator(a[:, 0], a[:, 1], extrapolate=True)


def stations():
    """Each station's section values in metres, at every Y any row was picked at."""
    t = {k: table(v) for k, v in dict(crest=CREST, centre=CENTRE, top=TOP, bot=BOTTOM, half=HALF, zw=WIDEST, zwt=WIDE_TOP,
                                       tuck=TUCK, zs=SHOULDER, sin=SHOULDER_IN, deck=DECK, rz=ROOF_Z,
                                       rw=ROOF_W, wg=GLASS_FOOT).items()}
    ys = sorted({round(float(y), 4) for rows in (CENTRE, TOP, BOTTOM, HALF) for y, _ in rows}
                | {round(float(y), 4) for y in np.arange(-2.10, 2.04, 0.04)})
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
_BELT = table([(-0.62, 0.500), (-0.35, 0.508), (0.00, 0.514), (0.50, 0.527), (1.00, 0.545), (1.40, 0.565),
               (1.62, 0.580)])


def belt(y):
    """The belt line under the side windows, rising gently to the back."""
    return float(_BELT(y))


# The side window opening (glass and its black frame), from the side render: the A-pillar edge a
# straight line, the roof edge one even curve and the quarter window's rear edge a straight line.
DLO = {"a_foot": (-0.60, belt(-0.60)), "a_head": (-0.02, 0.868), "roof": [(-0.02, 0.868), (0.50, 0.878),
       (1.00, 0.864), (1.30, 0.815)], "c_head": (1.38, 0.785), "c_foot": (1.615, 0.590)}
VENT_Y = -0.35                                    # the black corner ahead of the door glass ends here
B_PILLAR = ((0.532, 0.528), (0.520, 0.876), 0.09)  # the black division's foot, head and width

# Door shut line (side render), straight runs and plain arcs.
DOOR_BOTTOM = -0.245                              # the door's bottom edge, one straight line
FRONT_DOOR = [(-0.655, belt(-0.655) - 0.006), (-0.70, 0.25), (-0.685, DOOR_BOTTOM)]   # front edge
REAR_DOOR = [(0.532, 0.528), (0.590, 0.36), (0.585, DOOR_BOTTOM)]   # rear edge, from the division's middle
SHUT_ROUND = 0.04                                 # the door's bottom corners rounded this much
EDGE_ROUND = 0.20                                 # its front and rear edges bow this gently
# Windscreen (top render: Y, half width): its foot and head are even curves, its sides straight.
SCREEN = {"y_base_mid": -0.84, "y_base_edge": -0.72, "w_base": 0.675, "y_top_mid": -0.075, "y_top_edge": -0.03,
          "w_top": 0.575}
# Hatch glass (top render): head and foot.
REAR_GLASS = {"y_top_mid": 1.50, "y_top_edge": 1.47, "w_top": 0.565, "y_base_mid": 1.86, "y_base_edge": 1.84,
              "w_base": 0.62}
# Bonnet shut line. Its front (front render: U, Z) runs across the nose above the grille, meets
# the headlamp's inner top corner and follows the lamp's top edge out. From there (top render: Y,
# half width) it runs back along the wing's inner edge, then bends in to the windscreen's foot,
# inside its corner.
BONNET_NOSE = [(0.0, 0.196), (0.20, 0.196), (0.36, 0.198), (0.43, 0.204), (0.55, 0.229), (0.65, 0.249),
               (0.73, 0.263), (0.795, 0.270)]
BONNET = [(-1.74, 0.80), (-1.60, 0.785), (-1.30, 0.778), (-1.00, 0.772), (-0.86, 0.765), (-0.748, 0.60)]

# ---------------------------------------------------------------- front (front render, left half)
HEAD = [(0.400, 0.155), (0.450, 0.190), (0.550, 0.216), (0.650, 0.236), (0.730, 0.250), (0.790, 0.255),
        (0.805, 0.225), (0.785, 0.170), (0.745, 0.122), (0.650, 0.104), (0.550, 0.100), (0.460, 0.113),
        (0.412, 0.135)]
# The slim upper grille between the headlamps, and the big intake in the bumper below it.
GRILLE = [(0.0, 0.170), (0.285, 0.170), (0.310, 0.158), (0.290, 0.128), (0.250, 0.110), (0.0, 0.110)]
INTAKE = [(0.0, -0.105), (0.300, -0.105), (0.312, -0.120), (0.312, -0.272), (0.296, -0.290), (0.0, -0.290)]
# Fog lamps in the bumper's corners: their recess outline.
FOG = [(0.440, -0.150), (0.600, -0.150), (0.620, -0.170), (0.620, -0.245), (0.455, -0.245)]
SPLITTER = (0.93, -0.414, -0.386)                 # black splitter under the bumper: half width, bottom, top

# ---------------------------------------------------------------- rear (rear render, left half)
# Each tail lamp wraps round the tail's corner onto the side. Its outline on the tail (rear render:
# U, Z), from the inner top corner down and out along the bottom to the corner...
TAIL = [(0.660, 0.490), (0.605, 0.470), (0.580, 0.420), (0.568, 0.335), (0.620, 0.310), (0.780, 0.300)]
# ...then on the side (side render: Y, Z) along the bottom forward to the pointed tip and back
# along the top to the corner...
TAIL_SIDE = [(1.880, 0.330), (1.800, 0.372), (1.720, 0.428), (1.672, 0.462), (1.700, 0.482), (1.760, 0.498),
             (1.860, 0.505)]
# ...and on the tail again along the top back to the start.
TAIL_TOP = [(0.800, 0.488), (0.730, 0.492)]
TAIL_AXIS = (1.80, 0.62)  # the corner's axis in plan (top render: Y, half width), the lamp's rays fan from it
# The dark band low across the bumper's back and the rain light in its middle.
BAND_R = [(0.0, -0.150), (0.640, -0.150), (0.670, -0.165), (0.640, -0.200), (0.0, -0.200)]
RAIN = (0.075, -0.192, -0.158)                    # rain light: half width, bottom, top
# The tailgate's outline on the tail (rear render): across above the bumper, up the lamps' inner
# edges and out round the glass's sides.
HATCH = [(0.0, 0.165), (0.420, 0.165), (0.500, 0.190), (0.555, 0.250), (0.565, 0.330), (0.578, 0.440),
         (0.615, 0.545), (0.640, 0.630), (0.630, 0.740), (0.600, 0.830)]
# Its top edge runs across the roof's end, traced from above (top render: Y, half width) from the
# side's top corner in to the middle.
HATCH_TOP = [(1.50, 0.545), (1.468, 0.40), (1.462, 0.0)]
GATE_Z, GATE_LOW = 0.510, 0.300                   # the tailgate under the glass (the badge), and low on it
EXHAUST = (0.0, -0.290, 0.036)                    # one round tip under the bumper's middle: U, Z, radius
EXHAUST_END = 2.000

# ---------------------------------------------------------------- other parts
MIRROR = {"y0": -0.56, "y1": -0.37, "w0": 0.74, "w1": 0.94, "z0": 0.525, "z1": 0.635}   # at the door's front corner
# Rally wing over the hatch (side and top renders): its section, front to back over the top and
# back under, and its half span.
WING = [(1.470, 0.928), (1.600, 0.962), (1.730, 0.998), (1.738, 0.982), (1.620, 0.946), (1.500, 0.912)]
WING_SPAN = 0.640
# Roof scoop (side, front and top renders): its length along the roof, half width and height.
SCOOP = (-0.08, 0.42, 0.13, 0.026)
# Bonnet vents (top render): one each side, a slanted slot.
VENT = [(-1.20, 0.600), (-1.00, 0.620), (-1.02, 0.672), (-1.17, 0.664)]

data = {
    "name": "NusaFox",
    "wheel": {"r": TYRE_R, "rim_r": RIM_R, "width_f": 0.195, "width_r": 0.195, "outer_y": H(0.935),
              "outer_y_r": H(0.935), "front_x": X(Y_FRONT), "rear_x": X(Y_REAR), "arch_r": round(TYRE_R + 0.04, 4)},
    "stations": stations(),
    "flares": [[X(y), Z(z), L(r), H(w)] for y, z, r, w in FLARES],
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
    "bonnet_nose": end(BONNET_NOSE),
    "bonnet": plan(BONNET),
    "head": end(HEAD),
    "grille": end(GRILLE),
    "intake": end(INTAKE),
    "fog": end(FOG),
    "splitter": [H(SPLITTER[0]), Z(SPLITTER[1]), Z(SPLITTER[2])],
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
    "tail_mid_z": Z(0.400),                       # the tail lamps' middle height on the tail
    "exhaust": [H(EXHAUST[0]), Z(EXHAUST[1]), L(EXHAUST[2])],
    "exhaust_x": X(EXHAUST_END),
    "mirror": {"x_back": X(MIRROR["y1"]), "x_front": X(MIRROR["y0"]), "y_in": H(MIRROR["w0"]),
               "y_out": H(MIRROR["w1"]), "z0": Z(MIRROR["z0"]), "z1": Z(MIRROR["z1"])},
    "wing": side(WING),
    "wing_span": H(WING_SPAN),
    "scoop": [X(SCOOP[0]), X(SCOOP[1]), H(SCOOP[2]), L(SCOOP[3])],
    "vent": plan(VENT),
    "picks": {"frontbadge": 2, "rearbadge": 2, "name": 1, "wing": 1, "scoop": 1},   # Ivan's picks, 2026-11-13
}
# belt line z(x) in build space, sampled for parts to interpolate
_ys = np.linspace(-0.70, 1.65, 48)
data["belt"] = [[X(y), Z(belt(y))] for y in _ys]

if __name__ == "__main__":
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(data, indent=1))
    st = data["stations"]
    print("wrote", out, len(st), "stations; nose", max(s["x"] for s in st), "tail", min(s["x"] for s in st),
          "length", round(max(s["x"] for s in st) - min(s["x"] for s in st), 4),
          "roof", max(s.get("zt", 0) for s in st), "half", max(s["w"] for s in st), data["wheel"])
