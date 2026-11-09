"""Lofts the Collier's outer skin from rows.json, in pure numpy."""
import json
from pathlib import Path

import numpy as np
from scipy.interpolate import PchipInterpolator

ROWS = json.loads(Path(__file__).with_name("rows.json").read_text())
KEYS = ("zb", "zt", "crown", "w", "zw", "wb", "zs", "wt")
HALF_N = 36  # points on each half of a section


def chaikin(p, n=3):
    for _ in range(n):
        q = [p[0]]
        for a, b in zip(p[:-1], p[1:]):
            q += [0.75 * a + 0.25 * b, 0.25 * a + 0.75 * b]
        q.append(p[-1])
        p = np.array(q)
    return p


def resample(p, n):
    d = np.r_[0, np.cumsum(np.linalg.norm(np.diff(p, axis=0), axis=1))]
    t = np.linspace(0, d[-1], n)
    return np.column_stack([np.interp(t, d, p[:, 0]), np.interp(t, d, p[:, 1])])


def section(s):
    """Closed (y, z) loop: bottom, side with its widest point, belt, greenhouse, roof crown."""
    s = dict(s)
    s["zs"] = max(s["zs"], s["zb"] + 0.06)
    s["zt"] = max(s["zt"], s["zs"] + 0.01)
    s["zw"] = min(max(s["zw"], s["zb"] + 0.04), s["zs"] - 0.02)
    rb = min(0.08, 0.45 * (s["zw"] - s["zb"]))
    half = np.array([
        (0.0, s["zb"]), (s["w"] - rb, s["zb"]), (s["w"], s["zb"] + rb), (s["w"], s["zw"]),
        (s["wb"], s["zs"]), (s["wt"], s["zt"]), (s["wt"] * 0.5, s["zt"] + s["crown"] * 0.8),
        (0.0, s["zt"] + s["crown"])])
    half = resample(chaikin(half), HALF_N)
    other = half[::-1][1:-1] * np.array([-1, 1])
    return np.vstack([half, other])  # bottom centre, up the left, down the right


def _close(rings):
    m = len(rings[0])
    verts = np.concatenate(rings)
    faces = []
    for i in range(len(rings) - 1):
        a, b = i * m, (i + 1) * m
        for j in range(m):
            k = (j + 1) % m
            faces.append((a + j, b + j, b + k, a + k))
    for idx, ring in ((0, rings[0]), (len(rings) - 1, rings[-1])):
        c = len(verts)
        verts = np.vstack([verts, ring.mean(0)])
        base = idx * m
        for j in range(m):
            k = (j + 1) % m
            faces.append((base + k, base + j, c) if idx == 0 else (base + j, base + k, c))
    return verts, faces


def curves(rows):
    xs = np.array([r["x"] for r in rows])
    return xs, {k: PchipInterpolator(xs, [r[k] for r in rows]) for k in KEYS}


def params_at(x, rows=None):
    xs, c = curves(rows or ROWS["body"])
    return {k: float(c[k](x)) for k in KEYS}


def loft_xs(rows, step=0.02):
    """Ring positions along x, packed tighter at the ends where the sections change fast."""
    xs = np.array([r["x"] for r in rows])
    n = int(np.ceil((xs[-1] - xs[0]) / step)) + 1
    t = (1 - np.cos(np.linspace(0, np.pi, n))) / 2
    return xs[0] + (xs[-1] - xs[0]) * t


def loft(rows, step=0.02):
    xs, c = curves(rows)
    rings = []
    for x in loft_xs(rows, step):
        sec = section({k: float(c[k](x)) for k in KEYS})
        rings.append(np.column_stack([np.full(len(sec), x), sec]))
    return _close(rings)


def extrude_outline(outline_xz, y, half_t):
    o = np.asarray(outline_xz, float)
    n = len(o)
    front = np.column_stack([o[:, 0], np.full(n, y + half_t), o[:, 1]])
    back = np.column_stack([o[:, 0], np.full(n, y - half_t), o[:, 1]])
    verts = np.vstack([front, back])
    faces = [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))]
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, n + i, n + j, j))
    return verts, faces
