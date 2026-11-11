"""Rows of the Feretti F-256V8T, traced from sections of Ivan's F2008 model.

The model is scaled so its wheel centres sit at the published wheelbase (3135 mm). Each row
below was picked off the model's sections at that station (slice plots made from the GLB, never
from perspective pictures). Build space: x forward from mid wheelbase, y left, z up from the
ground, metres.

Run: python3 rows.py  -> rows.json
"""
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
GLB = "/mnt/project-files/uploads/ref/ferrari_f2008.glb"   # Ivan's model, kept out of the repo

WHEELBASE = 3.135
LENGTH = 4.545
WIDTH = 1.800

# centre body: x, half width at the shoulder wu, bottom zb, shoulder zs, half width at the top
# wt, top zt. Between shoulder and top the sides lean in, so the engine cover reads as a tent.
CENTRE = [
    (2.44, 0.030, 0.285, 0.315, 0.022, 0.330),
    (2.38, 0.060, 0.235, 0.350, 0.048, 0.380),
    (2.30, 0.075, 0.215, 0.370, 0.060, 0.405),
    (2.15, 0.085, 0.225, 0.405, 0.070, 0.450),
    (2.00, 0.095, 0.245, 0.450, 0.080, 0.500),
    (1.85, 0.105, 0.250, 0.480, 0.090, 0.530),
    (1.70, 0.115, 0.260, 0.505, 0.100, 0.560),
    (1.55, 0.130, 0.265, 0.535, 0.110, 0.595),
    (1.40, 0.145, 0.255, 0.560, 0.120, 0.620),
    (1.25, 0.170, 0.200, 0.580, 0.140, 0.640),
    (1.10, 0.190, 0.130, 0.590, 0.160, 0.650),
    (0.95, 0.210, 0.080, 0.600, 0.180, 0.660),
    (0.80, 0.230, 0.060, 0.600, 0.200, 0.660),
    (0.65, 0.250, 0.055, 0.585, 0.220, 0.635),
    (0.50, 0.260, 0.055, 0.580, 0.230, 0.620),
    (0.20, 0.270, 0.055, 0.575, 0.240, 0.615),
    (-0.05, 0.280, 0.055, 0.570, 0.240, 0.640),
    (-0.20, 0.295, 0.055, 0.560, 0.090, 0.900),
    (-0.30, 0.300, 0.055, 0.540, 0.050, 0.950),
    (-0.45, 0.295, 0.055, 0.500, 0.030, 0.945),
    (-0.60, 0.270, 0.055, 0.475, 0.025, 0.925),
    (-0.75, 0.240, 0.060, 0.450, 0.022, 0.895),
    (-0.90, 0.205, 0.070, 0.425, 0.022, 0.840),
    (-1.05, 0.175, 0.090, 0.405, 0.025, 0.740),
    (-1.20, 0.150, 0.150, 0.420, 0.060, 0.580),
    (-1.35, 0.128, 0.240, 0.450, 0.095, 0.525),
    (-1.50, 0.115, 0.255, 0.440, 0.090, 0.500),
    (-1.65, 0.095, 0.280, 0.420, 0.075, 0.460),
    (-1.78, 0.050, 0.300, 0.385, 0.040, 0.405),
]

# sidepods, left side (mirrored for the right): x, inner y, outer y, bottom, top, corner radius
POD = [
    (0.44, 0.15, 0.660, 0.140, 0.450, 0.060),
    (0.38, 0.15, 0.690, 0.110, 0.490, 0.075),
    (0.25, 0.15, 0.710, 0.095, 0.515, 0.090),
    (0.05, 0.15, 0.720, 0.085, 0.530, 0.110),
    (-0.15, 0.15, 0.705, 0.085, 0.515, 0.120),
    (-0.35, 0.15, 0.650, 0.085, 0.455, 0.120),
    (-0.55, 0.15, 0.545, 0.085, 0.395, 0.115),
    (-0.75, 0.15, 0.420, 0.085, 0.335, 0.100),
    (-0.95, 0.15, 0.300, 0.085, 0.280, 0.080),
]

# floor, left half in plan (x, y), and its height
FLOOR = {"z0": 0.035, "z1": 0.055,
         "plan": [(0.98, 0.0), (0.98, 0.22), (0.80, 0.26), (0.55, 0.62), (0.42, 0.70), (-0.40, 0.70),
                  (-0.70, 0.60), (-1.10, 0.48), (-1.45, 0.46), (-1.45, 0.0)]}
# diffuser ramp behind the floor: x0 -> x1, rising to z1, half width
DIFFUSER = {"x0": -1.45, "x1": -1.82, "z1": 0.24, "w": 0.46}

# wings: front main plane and flap (chord line le -> te in x, z at the tips and the middle),
# endplates; rear upper two elements, lower beam wing, endplates
FRONT_WING = {"span": 0.700, "mid": 0.13,
              "elements": [{"le": 2.47, "te": 2.20, "z_tip": 0.085, "z_mid": 0.175, "t": 0.022},
                           {"le": 2.22, "te": 2.02, "z_tip": 0.150, "z_mid": 0.215, "t": 0.016}],
              "plate": {"x0": 1.96, "x1": 2.47, "z0": 0.040, "z1": 0.300, "t": 0.012}}
REAR_WING = {"span": 0.475,
             "elements": [{"le": -1.66, "te": -1.90, "z": 0.700, "t": 0.030, "lift": 0.050},
                          {"le": -1.88, "te": -2.04, "z": 0.775, "t": 0.022, "lift": 0.035}],
             "beam": {"le": -1.62, "te": -1.78, "z": 0.400, "t": 0.022, "span": 0.40},
             "plate": {"x0": -1.62, "x1": -2.06, "z0": 0.330, "z1": 0.830, "t": 0.012}}

WHEELS = {"front_x": WHEELBASE / 2, "rear_x": -WHEELBASE / 2, "r": 0.312, "rim_r": 0.185,
          "front_y": 0.704, "front_w": 0.300, "rear_y": 0.690, "rear_w": 0.380}

COCKPIT = {"x0": -0.12, "x1": 0.58, "w": 0.215, "back_w": 0.17}      # opening in plan
DRIVER = {"helmet": (0.10, 0.0, 0.700), "r": 0.135}
AIRBOX = {"x": -0.20, "z0": 0.700, "z1": 0.890, "w": 0.075}           # intake above the helmet
MIRROR = {"x": 0.52, "y": 0.50, "z": 0.640}

PICKS = {}

if __name__ == "__main__":
    out = {"name": "FerettiF256V8T", "centre": CENTRE, "pod": POD, "floor": FLOOR, "diffuser": DIFFUSER,
           "front_wing": FRONT_WING, "rear_wing": REAR_WING, "wheels": WHEELS, "cockpit": COCKPIT,
           "driver": DRIVER, "airbox": AIRBOX, "mirror": MIRROR, "picks": PICKS}
    (HERE / "rows.json").write_text(json.dumps(out, indent=1))
    print("wrote rows.json")
