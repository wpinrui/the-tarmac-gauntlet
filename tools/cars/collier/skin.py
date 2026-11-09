"""Parts that lie on the body skin, built in the skin's own surface coordinates.

u is arc length up the left half of a section, from the bottom centre. s is arc length along the
skin at constant u, from x = 0, so the nose and tail faces get their true size. Points evaluate on
the same rings the body mesh uses, so a patch follows the mesh exactly and wraps round corners that
no single view ray could reach. Outlines are traced on the unwrapped map that unwrap.py draws.
"""
import numpy as np

import body

ROWS = body.ROWS["body"]
XS = body.loft_xs(ROWS)
_xs, _c = body.curves(ROWS)
HALVES = [body.section({k: float(_c[k](x)) for k in body.KEYS})[:body.HALF_N] for x in XS]
LENS = [np.r_[0, np.cumsum(np.linalg.norm(np.diff(h, axis=0), axis=1))] for h in HALVES]


def _ring_at(i, f):
    """Point at arc fraction f (0..1) up the left half of ring i, as (y, z)."""
    h, L = HALVES[i], LENS[i]
    s = np.clip(f, 0, 1) * L[-1]
    return np.array([np.interp(s, L, h[:, 0]), np.interp(s, L, h[:, 1])])


def _bracket(x):
    i = int(np.clip(np.searchsorted(XS, x) - 1, 0, len(XS) - 2))
    a = (x - XS[i]) / (XS[i + 1] - XS[i])
    return i, float(np.clip(a, 0, 1))


def half_len(x):
    i, a = _bracket(x)
    return LENS[i][-1] * (1 - a) + LENS[i + 1][-1] * a


def point(x, u):
    """Skin point at (x, u) on the left side."""
    i, a = _bracket(x)
    f = u / half_len(x)
    yz = _ring_at(i, f) * (1 - a) + _ring_at(i + 1, f) * a
    return np.array([x, yz[0], yz[1]])


def normal(x, u, h=0.004):
    dx = point(x + h, u) - point(x - h, u)
    du = point(x, u + h) - point(x, u - h)
    n = np.cross(du, dx)
    return n / (np.linalg.norm(n) + 1e-12)


def grid(us, xs):
    """Skin points for every (u, x) pair: array (len(us), len(xs), 3); nan above the section top."""
    P = np.full((len(us), len(xs), 3), np.nan)
    fs = np.linspace(0, 1, 800)
    us = np.asarray(us)
    for c, x in enumerate(xs):
        i, a = _bracket(x)
        ring = sum(np.column_stack([np.interp(fs * LENS[j][-1], LENS[j], HALVES[j][:, 0]),
                                    np.interp(fs * LENS[j][-1], LENS[j], HALVES[j][:, 1])]) * w
                   for j, w in ((i, 1 - a), (i + 1, a)))
        hl = half_len(x)
        ok = us <= hl
        f = us[ok] / hl
        P[ok, c] = np.column_stack([np.full(ok.sum(), x), np.interp(f, fs, ring[:, 0]), np.interp(f, fs, ring[:, 1])])
    return P


XG = np.linspace(XS[0], XS[-1], 2400)
UG = np.arange(0, max(L[-1] for L in LENS) + 0.01, 0.01)
_P = grid(UG, XG)
_d = np.linalg.norm(np.diff(_P, axis=1), axis=2)
SG = np.concatenate([np.zeros((len(UG), 1)), np.nancumsum(_d, axis=1)], 1)
SG -= np.array([np.interp(0.0, XG, row) for row in SG])[:, None]


def x_of(s, u):
    """x at surface position (s, u)."""
    k = float(np.clip(u / 0.01, 0, len(UG) - 2))
    i, a = int(k), k - int(k)
    return float(np.interp(s, SG[i], XG) * (1 - a) + np.interp(s, SG[i + 1], XG) * a)


def s_of(x, u):
    k = float(np.clip(u / 0.01, 0, len(UG) - 2))
    i, a = int(k), k - int(k)
    return float(np.interp(x, XG, SG[i]) * (1 - a) + np.interp(x, XG, SG[i + 1]) * a)


def to_su(p):
    x, u = to_xu(p)
    return np.array([s_of(x, u), u])


def to_xu(p):
    """(x, u) of a skin point; y is taken as its absolute value."""
    x = float(p[0])
    i, a = _bracket(x)
    n = 400
    fs = np.linspace(0, 1, n)
    pts = np.array([_ring_at(i, f) * (1 - a) + _ring_at(i + 1, f) * a for f in fs])
    k = int(np.argmin(np.linalg.norm(pts - [abs(p[1]), p[2]], axis=1)))
    return np.array([x, fs[k] * half_len(x)])


def smooth_loop(loop, n=2):
    loop = np.asarray(loop, float)
    for _ in range(n):
        nxt = np.roll(loop, -1, axis=0)
        loop = np.stack([0.75 * loop + 0.25 * nxt, 0.25 * loop + 0.75 * nxt], 1).reshape(-1, 2)
    return loop


def point_su(s, u):
    return point(x_of(s, u), u)


def normal_su(s, u):
    x = x_of(s, u)
    return normal(x, u)


def _surface(rings, depth, side):
    pts = []
    for ring in rings:
        for s, u in ring:
            x = x_of(s, u)
            p = point(x, u) + normal(x, u) * depth
            pts.append(p * [1, side, 1])
    return np.array(pts).reshape(len(rings), len(rings[0]), 3)


def patch(loop_su, off, thick, side=1, scale=1.0, levels=12):
    """Closed slab on the skin inside an (s, u) outline: (verts, faces) with faces facing out."""
    from export import sheet
    loop = np.asarray(loop_su, float)
    cen = loop.mean(0)
    loop = cen + (loop - cen) * scale
    rings = [cen + (loop - cen) * k for k in np.linspace(1.0, 0.02, levels)]
    v, f = sheet([_surface(rings, d, side) for d in (off, off + thick)])
    return v, _orient(v, f, side)


def ring(loop_su, off, thick, side=1, outer=1.08):
    """Bezel between the outline and the outline scaled by outer."""
    loop = np.asarray(loop_su, float)
    cen = loop.mean(0)
    L = [_surface([cen + (loop - cen) * k for k in (outer, 1.0)], d, side) for d in (off, off + thick)]
    m = len(loop)
    verts = np.concatenate([L[0].reshape(-1, 3), L[1].reshape(-1, 3)])
    faces = []
    for j in range(m):
        k = (j + 1) % m
        o0, i0, o1, i1 = j, m + j, 2 * m + j, 3 * m + j
        ok, ik, o1k, i1k = k, m + k, 2 * m + k, 3 * m + k
        faces += [(o0, ok, ik, i0), (o1, i1, i1k, o1k), (o0, o1, o1k, ok), (i0, ik, i1k, i1)]
    return verts, _orient(verts, faces, side)


def _orient(v, f, side):
    """Flip all faces if the signed volume is negative."""
    vol = 0.0
    c = v.mean(0)
    for t in f:
        a = v[t[0]] - c
        for j in range(1, len(t) - 1):
            vol += np.dot(a, np.cross(v[t[j]] - c, v[t[j + 1]] - c))
    return f if vol > 0 else [tuple(t[::-1]) for t in f]
