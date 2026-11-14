"""Kaiser Cappuccino, after the 2017 VW Scirocco. Section rows traced off orthographic renders
of the model Ivan supplied (side, front, rear, top on a plain background, 500 px per render
unit). Points are read in render units: s along the car (front -), z up, w half width.
Writes rows.json in build space: metres, x forward from mid wheelbase, y left, z up.

Published: wheelbase 2578 mm, length 4256 mm, width 1810 mm. Tyres 235/50 R18 (Ivan: no low
profile tyres, but filling the arches)."""
import json
from pathlib import Path

import numpy as np

WHEELBASE, LENGTH, WIDTH = 2.578, 4.256, 1.810
TYRE_W, RIM_D, PROFILE = 0.235, 18 * 0.0254, 0.50
TYRE_R = RIM_D / 2 + PROFILE * TYRE_W

# wheel centres on the side render
S_FRONT, S_REAR, Z_HUB = -1.0345, 1.1875, 0.123
S_TIP_F, S_TIP_R = -1.817, 1.823
W_REF = 0.769                       # widest half width on the top render

SCALE = WHEELBASE / (S_REAR - S_FRONT)
S_MID = (S_FRONT + S_REAR) / 2
OVERHANG = (LENGTH - WHEELBASE) / ((S_FRONT - S_TIP_F + S_TIP_R - S_REAR) * SCALE)
WSCALE = WIDTH / 2 / W_REF


def x_of(s):
    """Render s to build x, with the overhangs stretched to the published length."""
    s = np.asarray(s, float)
    x = -(s - S_MID) * SCALE
    xf, xr = -(S_FRONT - S_MID) * SCALE, -(S_REAR - S_MID) * SCALE
    return np.where(x > xf, xf + (x - xf) * OVERHANG, np.where(x < xr, xr + (x - xr) * OVERHANG, x))


def z_of(z):
    """Ground sits one tyre radius under the wheel centres."""
    return (np.asarray(z, float) - Z_HUB) * SCALE + TYRE_R


def w_of(w):
    return np.asarray(w, float) * WSCALE


# Section rows, bottom to top. Each is {"z": [(s, z)], "w": [(s, w)]}.
ROWS = {
    "keel": {
        "z": [(-1.817, .20), (-1.81, .14), (-1.79, .08), (-1.76, .05), (-1.68, .04), (-1.5, .032), (-1.3, .03),
              (0, .03), (1.3, .035), (1.45, .05), (1.6, .08), (1.7, .10), (1.78, .14), (1.81, .22), (1.823, .30)],
        "w": [(-1.817, .04), (-1.81, .15), (-1.79, .30), (-1.75, .44), (-1.69, .53), (-1.6, .62), (-1.5, .67),
              (-1.38, .70), (-1.1, .71), (1.3, .71), (1.5, .70), (1.6, .69), (1.72, .63), (1.77, .53), (1.80, .38),
              (1.815, .25), (1.823, .06)],
    },
    "side": {   # widest line
        "z": [(-1.817, .26), (-1.8, .27), (-1.7, .30), (-1.3, .32), (0, .33), (1.3, .36), (1.7, .38), (1.823, .36)],
        "w": [(-1.817, .06), (-1.812, .20), (-1.795, .40), (-1.75, .52), (-1.69, .60), (-1.60, .70), (-1.50, .75),
              (-1.38, .768), (-1.2, .769), (1.4, .769), (1.5, .765), (1.6, .755), (1.66, .74), (1.72, .70),
              (1.77, .60), (1.80, .45), (1.815, .30), (1.823, .08)],
    },
    "shoulder": {
        "z": [(-1.817, .30), (-1.80, .36), (-1.75, .42), (-1.6, .49), (-1.3, .565), (-0.9, .625), (-0.6, .645),
              (0, .655), (0.6, .67), (1.0, .69), (1.3, .70), (1.5, .69), (1.65, .64), (1.75, .56), (1.80, .48),
              (1.823, .38)],
        "w": [(-1.817, .05), (-1.80, .26), (-1.75, .45), (-1.65, .58), (-1.5, .66), (-1.3, .70), (-0.9, .715),
              (-0.4, .715), (0.4, .72), (1.0, .74), (1.3, .745), (1.5, .73), (1.65, .66), (1.75, .52), (1.80, .36),
              (1.823, .06)],
    },
    "belt": {   # glass base along the side, the bonnet's outer band ahead of it
        "z": [(-1.817, .32), (-1.80, .385), (-1.75, .44), (-1.6, .515), (-1.3, .59), (-1.0, .655), (-0.86, .69),
              (-0.66, .685), (0.0, .70), (0.5, .722), (1.0, .755), (1.1, .77), (1.2, .78), (1.4, .78), (1.55, .76),
              (1.65, .72), (1.72, .66), (1.77, .58), (1.80, .50), (1.815, .43), (1.823, .36)],
        "w": [(-1.817, .03), (-1.80, .18), (-1.75, .36), (-1.65, .48), (-1.5, .56), (-1.3, .61), (-1.0, .65),
              (-0.86, .67), (-0.66, .70), (0.0, .665), (0.5, .625), (1.0, .58), (1.1, .575), (1.2, .58), (1.4, .59),
              (1.55, .58), (1.65, .55), (1.72, .48), (1.77, .40), (1.80, .30), (1.815, .18), (1.823, .04)],
    },
    "cant": {   # roof edge and A pillar, the bonnet crown's edge ahead of it
        "z": [(-1.817, .325), (-1.81, .36), (-1.79, .395), (-1.76, .425), (-1.68, .49), (-1.6, .53), (-1.44, .582),
              (-1.28, .621), (-1.12, .654), (-0.96, .686), (-0.86, .705), (-0.80, .727), (-0.64, .805), (-0.48, .885),
              (-0.32, .955), (-0.16, 1.0), (0.0, 1.015), (0.3, 1.02), (0.8, 1.005), (1.05, .99), (1.2, .975),
              (1.36, .955), (1.45, .94), (1.55, .89), (1.62, .84), (1.68, .78), (1.74, .69), (1.78, .61),
              (1.805, .515), (1.82, .42), (1.823, .34)],
        "w": [(-1.817, .02), (-1.81, .12), (-1.79, .20), (-1.75, .27), (-1.68, .32), (-1.5, .38), (-1.2, .44),
              (-1.0, .50), (-0.86, .58), (-0.80, .60), (-0.6, .595), (-0.4, .58), (-0.2, .56), (0.0, .55),
              (0.5, .535), (1.0, .52), (1.3, .50), (1.45, .48), (1.6, .44), (1.7, .38), (1.77, .30), (1.81, .18),
              (1.823, .03)],
    },
    "crown": {  # centreline top
        "z": [(-1.817, .33), (-1.81, .36), (-1.79, .40), (-1.76, .43), (-1.68, .495), (-1.60, .535), (-1.44, .588),
              (-1.28, .628), (-1.12, .662), (-0.96, .694), (-0.86, .712), (-0.80, .74), (-0.64, .82), (-0.48, .905),
              (-0.32, .977), (-0.16, 1.027), (0.0, 1.048), (0.24, 1.062), (0.40, 1.062), (0.64, 1.054),
              (0.80, 1.042), (1.04, 1.022), (1.20, .997), (1.36, .972), (1.45, .955), (1.55, .90), (1.62, .85),
              (1.68, .79), (1.74, .70), (1.78, .62), (1.805, .52), (1.82, .42), (1.823, .34)],
    },
}

# Side window opening (daylight opening) on the side render: (s, z), front point first.
DLO = [(-0.66, .69), (-0.52, .76), (-0.40, .855), (-0.25, .925), (-0.10, .962), (0.10, .968), (0.30, .968),
       (0.55, .962), (0.80, .945), (0.98, .925), (1.07, .905), (1.10, .87), (1.09, .80), (1.06, .775),
       (0.50, .728), (0.0, .705)]
B_PILLAR = (0.47, 0.55)             # black B pillar between door glass and quarter glass

# Door shut line on the side render: front edge, bottom, rear edge. The top runs along the
# roof edge above the glass.
DOOR = [(-0.665, .135), (-0.672, .40), (-0.676, .66)]
DOOR_REAR = [(0.505, .67), (0.50, .40), (0.495, .135)]

# Wheel arch openings: radius about each wheel centre, render units.
ARCH_R = 0.345

# Windscreen and rear glass, on the top render: (s, w) along the edge, base first.
SCREEN = {"base": -0.84, "top": -0.13, "w_base": 0.58, "w_top": 0.505}
HATCH = {"top": 1.43, "base": 1.70, "w_top": 0.44, "w_base": 0.47}

# Lamps, traced off zoomed side, front and rear renders.
# Headlamp outline on the side render (s, z), front end first, over the top and back.
HEAD_SIDE = [(-1.78, .42), (-1.76, .452), (-1.72, .462), (-1.68, .472), (-1.64, .48), (-1.60, .488), (-1.56, .494),
             (-1.50, .50), (-1.44, .503), (-1.40, .50), (-1.37, .495), (-1.355, .48), (-1.365, .468), (-1.38, .455),
             (-1.40, .442), (-1.44, .425), (-1.50, .412), (-1.56, .403), (-1.62, .398), (-1.70, .398), (-1.76, .40)]
# Headlamp outline on the front render (w, z), inner end first, over the top and back.
HEAD_FRONT = [(0.40, .445), (0.45, .462), (0.50, .478), (0.55, .490), (0.60, .499), (0.65, .504), (0.68, .501),
              (0.70, .492), (0.715, .475), (0.722, .455), (0.72, .435), (0.71, .415), (0.69, .397), (0.66, .388),
              (0.60, .384), (0.55, .383), (0.50, .385), (0.45, .388), (0.40, .393)]
# Tail lamp outline on the side render (s, z), front end first, over the top and back.
TAIL_SIDE = [(1.345, .63), (1.36, .648), (1.40, .65), (1.45, .648), (1.50, .645), (1.55, .638), (1.60, .628),
             (1.64, .612), (1.66, .59), (1.67, .565), (1.66, .545), (1.64, .53), (1.60, .523), (1.55, .53),
             (1.50, .54), (1.45, .555), (1.40, .575), (1.37, .60)]
# Tail lamp outline on the rear render (w, z), inner tip first, over the top and back.
TAIL_REAR = [(0.40, .535), (0.42, .57), (0.45, .60), (0.50, .625), (0.55, .638), (0.60, .645), (0.65, .643),
             (0.68, .635), (0.70, .62), (0.715, .60), (0.72, .57), (0.715, .545), (0.70, .525), (0.65, .515),
             (0.60, .51), (0.55, .512), (0.50, .515), (0.45, .52), (0.41, .528)]

# Upper grille band (centre to the headlamp's inner end) and lower intake on the front render (w, z).
GRILLE = [(0.0, .442), (0.40, .446), (0.40, .378), (0.0, .375)]
INTAKE = [(0.0, .255), (0.30, .258), (0.62, .262), (0.66, .258), (0.69, .22), (0.70, .17), (0.69, .14),
          (0.66, .128), (0.60, .115), (0.40, .10), (0.20, .10), (0.0, .10)]
# Rear bumper slots (w, z), lit as the rain light.
RAIN = [(0.50, .20), (0.67, .20), (0.68, .19), (0.66, .18), (0.52, .18), (0.50, .19)]

# Ivan's picks from the viewer, filled in once he answers.
PICKS = {}


def main():
    out = {"ref": {"scale": SCALE, "overhang": OVERHANG, "wscale": WSCALE},
           "tyre": {"r": TYRE_R, "w": TYRE_W, "rim_r": RIM_D / 2},
           "axles": {"front": float(x_of(S_FRONT)), "rear": float(x_of(S_REAR)), "z": float(z_of(Z_HUB))},
           "rows": {}}
    for name, row in ROWS.items():
        z = row["z"]
        out["rows"][name] = {"z": [[float(x_of(s)), float(z_of(v))] for s, v in z]}
        if "w" in row:
            out["rows"][name]["w"] = [[float(x_of(s)), float(w_of(v))] for s, v in row["w"]]
    side = lambda pts: [[float(x_of(s)), float(z_of(v))] for s, v in pts]
    face = lambda pts: [[float(w_of(w)), float(z_of(v))] for w, v in pts]
    out["dlo"] = side(DLO)
    out["b_pillar"] = [float(x_of(s)) for s in B_PILLAR]
    out["door"] = side(DOOR)
    out["door_rear"] = side(DOOR_REAR)
    out["arch_r"] = ARCH_R * SCALE
    out["screen"] = {"base": float(x_of(SCREEN["base"])), "top": float(x_of(SCREEN["top"])),
                     "w_base": float(w_of(SCREEN["w_base"])), "w_top": float(w_of(SCREEN["w_top"]))}
    out["hatch"] = {"top": float(x_of(HATCH["top"])), "base": float(x_of(HATCH["base"])),
                    "w_top": float(w_of(HATCH["w_top"])), "w_base": float(w_of(HATCH["w_base"]))}
    out["head_side"], out["tail_side"] = side(HEAD_SIDE), side(TAIL_SIDE)
    out["head_front"], out["tail_rear"] = face(HEAD_FRONT), face(TAIL_REAR)
    out["grille"], out["intake"], out["rain"] = face(GRILLE), face(INTAKE), face(RAIN)
    out["picks"] = PICKS
    Path(__file__).with_name("rows.json").write_text(json.dumps(out, indent=1))
    print("scale", round(SCALE, 4), "overhang", round(OVERHANG, 4), "length",
          round(float(x_of(S_TIP_F) - x_of(S_TIP_R)), 3), "roof", round(float(z_of(1.062)), 3))


if __name__ == "__main__":
    main()
