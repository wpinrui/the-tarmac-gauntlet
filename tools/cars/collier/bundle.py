"""Solve the six cameras and the landmark points together.

Gauge: wheel centres sit 2.76 m apart (the silhouette fit's wheelbase at
4.03 m length), level, at tyre-radius height, centred on x = 0.
"""
import sys
import numpy as np
from scipy.optimize import least_squares
import cams as C
from landmarks import LANDMARKS

WHEELBASE = 2.76
WHEEL_Z = 0.345

GUESS = {
    'jet': (1.2, 0.3, 0.95), 'badge_front': (1.95, 0, 0.62), 'headlamp_tip': (1.85, 0.4, 0.62),
    'fog': (1.95, 0.6, 0.35), 'mirror_tip': (0.6, 0.98, 0.95), 'wheel_front': (1.38, 0.75, 0.345),
    'wheel_rear': (-1.38, 0.75, 0.345), 'door_handle': (-0.3, 0.88, 0.85), 'strip_front': (0.75, 0.9, 0.5),
    'strip_rear': (-1.08, 0.9, 0.55), 'taillamp_tip': (-1.95, 0.45, 0.75), 'badge_rear': (-1.97, 0, 0.92),
    'brake_lamp': (-1.75, 0, 1.38), 'exhaust': (-1.95, -0.35, 0.15), 'headlamp_rear_tip': (1.4, 0.75, 0.88),
}

names = list(LANDMARKS)
Y_PRIOR = {'wheel_front': 0.78, 'wheel_rear': 0.78, 'door_handle': 0.88, 'strip_front': 0.89,
           'strip_rear': 0.89, 'mirror_tip': 0.99}
views = C.NAMES


def unpack(x):
    cams = {}
    for i, v in enumerate(views):
        yaw, pitch, roll, tx, ty, tz, ld, ls = x[i * 8:(i + 1) * 8]
        d = np.exp(ld)
        cams[v] = np.array([yaw, pitch, roll, tx, ty, tz, d, np.exp(ls) * d])
    pts = x[len(views) * 8:].reshape(-1, 3)
    return cams, {n: pts[i] for i, n in enumerate(names)}


def pack(cams, pts):
    x = []
    for v in views:
        p = cams[v]
        x += [*p[:6], np.log(p[6]), np.log(p[7] / p[6])]
    for n in names:
        x += list(pts[n])
    return np.array(x, float)


def world(n, side, p):
    kind = LANDMARKS[n][0]
    if kind == 'C':
        return np.array([p[0], 0.0, p[2]])
    if kind == 'F':
        return p
    return np.array([p[0], p[1] if side == 'L' else -p[1], p[2]])


def residuals(x, sizes):
    cams, pts = unpack(x)
    r = []
    for n in names:
        kind, w, obs = LANDMARKS[n]
        for v, sides in obs.items():
            for side, uv in sides.items():
                P = world(n, side, pts[n])[None]
                u, vv, z = C.project(cams[v], P, sizes[v])
                r += [w * (u[0] - uv[0]), w * (vv[0] - uv[1]), 1000.0 * max(0.0, 1.0 - z[0])]
    wf, wr = pts['wheel_front'], pts['wheel_rear']
    g = 200.0
    r += [g * (wf[0] - wr[0] - WHEELBASE), g * (wf[0] + wr[0]),
          g * (wf[2] - WHEEL_Z), g * (wr[2] - WHEEL_Z), g * (wf[1] - wr[1])]
    # lateral positions the silhouettes pin down well: body side 0.88-0.9 m
    for n, y in Y_PRIOR.items():
        r.append(100.0 * (pts[n][1] - y))
    return np.array(r)


def report(x, sizes):
    cams, pts = unpack(x)
    for n in names:
        kind, w, obs = LANDMARKS[n]
        errs = []
        for v, sides in obs.items():
            for side, uv in sides.items():
                P = world(n, side, pts[n])[None]
                u, vv, _ = C.project(cams[v], P, sizes[v])
                errs.append(f'{v[:5]}{side}:{np.hypot(u[0]-uv[0], vv[0]-uv[1]):.1f}')
        print(f'{n:18s}', np.round(pts[n], 3), ' '.join(errs))


if __name__ == '__main__':
    import json
    src, dst = sys.argv[1], sys.argv[2]
    sizes = {k: tuple(v) for k, v in json.load(open('sizes.json')).items()}
    cams = C.load(src)
    x0 = pack(cams, {n: np.array(GUESS[n], float) for n in names})
    nc = len(views) * 8
    # stage 1: points only, cameras from the silhouette fit
    f1 = lambda xp: residuals(np.r_[x0[:nc], xp], sizes)
    r1 = least_squares(f1, x0[nc:], loss='soft_l1', f_scale=4.0)
    x1 = np.r_[x0[:nc], r1.x]
    print('stage 1 rms', np.sqrt(np.mean(r1.fun[:-11] ** 2)))
    report(x1, sizes)
    res = least_squares(residuals, x1, args=(sizes,), loss='soft_l1', f_scale=4.0, max_nfev=20000)
    print('cost', res.cost, 'rms px', np.sqrt(np.mean(res.fun[:-11] ** 2)))
    report(res.x, sizes)
    cams, pts = unpack(res.x)
    C.save(dst, cams)
    json.dump({n: list(map(float, p)) for n, p in pts.items()}, open(dst.replace('.json', '_pts.json'), 'w'), indent=1)
    for v in views:
        print(v, cams[v].round(3))
