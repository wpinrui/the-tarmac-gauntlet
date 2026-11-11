"""The F-256V8T's body pieces lofted from rows.json, in pure numpy and shapely.

Centre body: each station is a tapered section (straight sides up to the shoulder, leaning in
to the top) with its corners rounded. Sidepods: rounded boxes beside it. Each traced value
becomes a smooth curve along the car (monotone cubic), and the skin is lofted through them.
"""
import json
import sys
from pathlib import Path

import numpy as np
from shapely.geometry import Polygon, box
from shapely.geometry.polygon import orient

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "common"))
from geom import close_rings, monotone  # noqa: E402

ROWS = json.loads(Path(__file__).with_name("rows.json").read_text())
N = 96          # points round each section
TIP_ROUND = 0.6  # share of the shoulder to top rise rounded off
WAIST = 0.40     # where the leaning sides turn up: share of the way from shoulder to top


def _curves(rows):
    rows = sorted(rows, key=lambda r: r[0])
    xs = [r[0] for r in rows]
    return xs[0], xs[-1], [monotone(list(zip(xs, [r[k] for r in rows]))) for k in range(1, len(rows[0]))]


def even(ring, n):
    """n points round a closed loop by arc length, starting where it crosses the middle at the
    bottom, so every station starts at the same spot and the skin never twists."""
    ring = np.asarray(ring, float)
    nxt = np.roll(ring, -1, axis=0)
    best = None
    for i, (p, q) in enumerate(zip(ring, nxt)):
        if (p[0] - ring[:, 0].mean()) * (q[0] - ring[:, 0].mean()) <= 0 and p[0] != q[0]:
            t = (ring[:, 0].mean() - p[0]) / (q[0] - p[0])
            z = p[1] + (q[1] - p[1]) * t
            if best is None or z < best[0]:
                best = (z, i, p + (q - p) * t)
    _z, i, c = best
    ring = np.vstack([c, ring[i + 1:], ring[:i + 1], c])
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(ring, axis=0), axis=1))])
    t = np.linspace(0, s[-1], n, endpoint=False)
    return np.column_stack([np.interp(t, s, ring[:, 0]), np.interp(t, s, ring[:, 1])])


C_X0, C_X1, C_CV = _curves(ROWS["centre"])
P_X0, P_X1, P_CV = _curves(ROWS["pod"])


def rounded(corners, radii, rounds=3):
    """A closed loop through polygon corners with each corner rounded to about its radius.
    Built the same way at every station (same points in the same order), so lofted stations
    line up and the skin stays smooth."""
    c = np.asarray(corners, float)
    m = len(c)
    pts = []
    for i in range(m):
        p, prev, nxt = c[i], c[i - 1], c[(i + 1) % m]
        a, b = prev - p, nxt - p
        ra = min(radii[i], 0.45 * np.linalg.norm(a)); rb = min(radii[i], 0.45 * np.linalg.norm(b))
        pts += [p + a / np.linalg.norm(a) * ra, p, p + b / np.linalg.norm(b) * rb]
    pts = np.array(pts)
    for _ in range(rounds):
        q = np.roll(pts, -1, axis=0)
        pts = np.stack([0.75 * pts + 0.25 * q, 0.25 * pts + 0.75 * q], axis=1).reshape(-1, 2)
    return pts


def centre_ring(x):
    wu, zb, zs, wt, zt = (float(f(x)) for f in C_CV)
    r = max(0.004, min(0.35 * wu, 0.3 * (zs - zb), 0.06))
    rt = max(0.004, min(0.9 * wt, TIP_ROUND * (zt - zs), 0.05))
    wm, zm = wt + (wu - wt) * WAIST, zs + (zt - zs) * WAIST       # the sides curve in, so a tall
    corners = [(wu, zb), (wu, zs), (wm, zm), (wt, zt),             # cover rises as a slim fin
               (-wt, zt), (-wm, zm), (-wu, zs), (-wu, zb)]
    radii = [r, 0.04, 0.06, rt, rt, 0.06, 0.04, r]
    return rounded(corners, radii)


def centre_shape(x):
    return orient(Polygon(centre_ring(x)).buffer(0), 1)


def pod_shape(x, sgn=1):
    yi, yo, z0, z1, r = (float(f(x)) for f in P_CV)
    r = min(r, 0.45 * (z1 - z0), 0.45 * (yo - yi))
    g = box(yi, z0, yo, z1).buffer(-r, join_style=1).buffer(r, join_style=1)
    if sgn < 0:
        g = Polygon(np.array(g.exterior.coords) * [-1, 1])
    return orient(g, 1)


def _loft(shape, x0, x1, step, ease=0.0, ring=None):
    n = int(np.ceil((x1 - x0) / step)) + 1
    t = np.linspace(0, 1, n)
    if ease:
        t = (1 - np.cos(np.pi * t)) / 2 * ease + t * (1 - ease)
    rings = []
    for x in x0 + (x1 - x0) * t:
        r = ring(x) if ring else even(np.array(shape(x).exterior.coords)[:-1], N)
        rings.append(np.column_stack([np.full(len(r), x), r]))
    return close_rings(rings)


def centre(step=0.015):
    """Each station's points come straight from its corners, so point k means the same spot on
    every station and the skin cannot twist where the cover narrows into the airbox."""
    return _loft(None, C_X0, C_X1, step, ease=0.6, ring=centre_ring)


def pod(sgn, step=0.03):
    return _loft(lambda x: pod_shape(x, sgn), P_X0, P_X1, step)
