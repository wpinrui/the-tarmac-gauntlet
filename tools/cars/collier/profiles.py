"""Profile curves along the car, in metres.

Frame: X forward, Y to the car's left, Z up, origin on the ground at mid
wheelbase. The side render is the master blueprint: 4.03 m over 1396 px.
Top and bottom edges and the plan width are read off the silhouettes; the
rest are knots picked from the references.
"""
import numpy as np
from scipy.interpolate import PchipInterpolator
from scipy.ndimage import gaussian_filter1d
from masks import silhouette

LENGTH = 4.03
S_SIDE = LENGTH / 1396
U_MID = 758.0          # mid wheelbase, side render
NOSE_X = (U_MID - 40) * S_SIDE
TAIL_X = (U_MID - 1436) * S_SIDE
WHEEL_X = (U_MID - 270) * S_SIDE  # +- this
TYRE_R = 122 * S_SIDE
WHEEL_Z = 120 * S_SIDE  # centre height, from the side render (v 432 above ground 552)


def v_ground(u):
    return 552 + (u - 270) * 5 / 976


def side_x(u):
    return (U_MID - u) * S_SIDE


def side_z(u, v):
    return (v_ground(u) - v) * S_SIDE


def knots(pairs):
    pairs = sorted(pairs)
    x, y = zip(*pairs)
    f = PchipInterpolator(x, y, extrapolate=True)
    return lambda q: f(np.clip(q, x[0], x[-1]))


def _side_edges():
    m = silhouette('side')
    us = np.arange(40, 1437)
    top, bot = [], []
    for u in us:
        vs = np.nonzero(m[:, u])[0]
        top.append(side_z(u, vs.min()))
        bot.append(side_z(u, vs.max()))
    return side_x(us), np.array(top), np.array(bot)


_xs, _top, _bot = _side_edges()
_xs, _top, _bot = _xs[::-1], _top[::-1], _bot[::-1]  # ascending x

# the spoiler sits on top of the roof's rear edge; take it out of the roof line
_spoiler = (_xs > -1.42) & (_xs < -1.05)
_roof_out = knots([(-1.46, 1.19), (-1.42, 1.235), (-1.36, 1.30), (-1.30, 1.335), (-1.20, 1.36),
                   (-1.10, 1.375), (-1.05, 1.381)])
_top = np.where(_spoiler, _roof_out(_xs), _top)
# underside: between the arches the sill line, wheels removed
_floor = knots([(-1.96, 0.20), (-1.91, 0.19), (-1.85, 0.16), (-1.78, 0.145), (-1.0, 0.14),
                (1.0, 0.14), (1.75, 0.12), (1.95, 0.115), (2.02, 0.14), (2.073, 0.30)])
_bot = np.where((np.abs(_xs) > 0.95) & (np.abs(_xs) < 1.80), _floor(_xs), _bot)
_bot = np.maximum(_bot, _floor(_xs) - 0.01)

_top_s = gaussian_filter1d(_top, 10.0)
_bot_s = gaussian_filter1d(_bot, 8.0)
zt = lambda x: np.interp(x, _xs, _top_s)
zb = lambda x: np.interp(x, _xs, _bot_s)


def _plan():
    from body import body_masks
    m = body_masks()['top']
    s = LENGTH / (1304 - 61)
    us = np.arange(61, 1305)
    hw = []
    for u in us:
        vs = np.nonzero(m[:, u])[0]
        hw.append((vs.max() - vs.min()) / 2 * s * 0.96 if len(vs) else 0.0)
    x = (us - 1304) * s + NOSE_X
    hw = np.array(hw)
    mid = (x > -1.4) & (x < 1.3)
    hw[mid] = np.minimum(hw[mid], 0.882)
    return x, gaussian_filter1d(hw, 12.0)


_px, _pw = _plan()
w = lambda x: np.interp(x, _px, _pw)

# beltline (top of the shoulder, where the glass starts)
z_belt = knots([(-1.96, 0.70), (-1.85, 0.92), (-1.75, 0.975), (-1.45, 1.0), (-1.1, 1.0), (-0.8, 0.975),
                (-0.5, 0.935), (0.0, 0.91), (0.5, 0.875), (0.9, 0.86), (1.0, 0.86)])
# top edge of the side glass, which is where the roof edge sits
z_roof_edge = knots([(-1.80, 0.95), (-1.65, 1.07), (-1.45, 1.22), (-1.30, 1.30), (-1.10, 1.335), (-0.8, 1.345),
                     (-0.3, 1.372), (0.1, 1.345), (0.4, 1.255), (0.6, 1.17), (0.86, 1.00), (0.95, 0.92)])
w_roof = knots([(-1.85, 0.50), (-1.6, 0.53), (-1.35, 0.56), (-1.0, 0.585), (-0.5, 0.60), (0.1, 0.60),
                (0.5, 0.63), (0.85, 0.68), (1.0, 0.70)])
# fender crest ahead of the windscreen
z_fender = knots([(0.85, 0.86), (1.2, 0.87), (1.47, 0.865), (1.7, 0.80), (1.9, 0.68), (2.07, 0.45)])
# greenhouse presence: 1 behind the A-pillar foot, 0 over the bonnet
greenhouse = knots([(-2.1, 1.0), (0.84, 1.0), (1.0, 0.0), (2.2, 0.0)])
z_max_w = knots([(-1.96, 0.42), (-1.7, 0.62), (-1.0, 0.62), (0.0, 0.58), (1.2, 0.60), (1.8, 0.52), (2.07, 0.38)])
