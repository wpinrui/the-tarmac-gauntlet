"""Shared geometry primitives in pure numpy. No shapes, sizes or designs live here."""
import numpy as np
from scipy.interpolate import PchipInterpolator


def monotone(points):
    """Monotone cubic through (t, value) pairs: a smooth curve with no overshoot."""
    t, v = zip(*sorted(points))
    return PchipInterpolator(np.array(t, float), np.array(v, float), extrapolate=True)


def close_rings(rings, cap=True):
    """Join equal-length rings into a closed solid: quads between rings, a fan over each end.
    Returns (verts, faces)."""
    m = len(rings[0])
    verts = [np.asarray(r, float) for r in rings]
    faces = []
    for i in range(len(rings) - 1):
        a, b = i * m, (i + 1) * m
        for j in range(m):
            k = (j + 1) % m
            faces.append((a + j, a + k, b + k, b + j))
    verts = np.concatenate(verts)
    if cap:
        for idx, flip in ((0, True), (len(rings) - 1, False)):
            c = len(verts)
            verts = np.vstack([verts, np.asarray(rings[idx]).mean(0)])
            base = idx * m
            for j in range(m):
                k = (j + 1) % m
                faces.append((base + k, base + j, c) if flip else (base + j, base + k, c))
    return verts, faces


def lathe(profile, n=72, axis="y"):
    """Spin a closed (r, h) profile round an axis. Returns (verts, faces)."""
    prof = np.asarray(profile, float)
    m = len(prof)
    verts = []
    for i in range(n):
        a = 2 * np.pi * i / n
        for r, h in prof:
            if axis == "y":
                verts.append((r * np.cos(a), h, r * np.sin(a)))
            else:
                verts.append((r * np.cos(a), r * np.sin(a), h))
    faces = []
    for i in range(n):
        a, b = i * m, ((i + 1) % n) * m
        for j in range(m):
            k = (j + 1) % m
            faces.append((a + j, a + k, b + k, b + j))
    return np.array(verts), faces


def catmull_closed(pts, n=8):
    """Smooth closed curve through picked points."""
    p = np.asarray(pts, float)
    m = len(p)
    out = []
    for i in range(m):
        p0, p1, p2, p3 = p[i - 1], p[i], p[(i + 1) % m], p[(i + 2) % m]
        for t in np.linspace(0, 1, n, endpoint=False):
            out.append(0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    return np.array(out)


def catmull_open(pts, n=8):
    """Smooth open curve through picked points, ends kept."""
    p = np.asarray(pts, float)
    p = np.vstack([2 * p[0] - p[1], p, 2 * p[-1] - p[-2]])
    out = []
    for i in range(1, len(p) - 2):
        p0, p1, p2, p3 = p[i - 1], p[i], p[i + 1], p[i + 2]
        for t in np.linspace(0, 1, n, endpoint=False):
            out.append(0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    out.append(p[-2])
    return np.array(out)


def resample(path, step):
    """Even spacing along an open polyline."""
    p = np.asarray(path, float)
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(p, axis=0), axis=1))])
    n = max(2, int(np.ceil(s[-1] / step)) + 1)
    t = np.linspace(0, s[-1], n)
    return np.column_stack([np.interp(t, s, p[:, k]) for k in range(p.shape[1])])


def box(x0, x1, y0, y1, z0, z1):
    v = np.array([(x, y, z) for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)], float)
    f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    return v, f


def disc(centre, normal, r, depth, n=40):
    """Short closed cylinder along normal."""
    nrm = np.asarray(normal, float); nrm /= np.linalg.norm(nrm)
    a = np.array([0, 0, 1.0]) if abs(nrm[2]) < 0.9 else np.array([0, 1.0, 0])
    u = np.cross(nrm, a); u /= np.linalg.norm(u)
    v = np.cross(nrm, u)
    ang = np.linspace(0, 2 * np.pi, n, endpoint=False)
    ring = np.array([np.asarray(centre) + r * (np.cos(t) * u + np.sin(t) * v) for t in ang])
    return close_rings([ring - nrm * depth / 2, ring + nrm * depth / 2])


def mirror_y(verts, faces):
    """Mirror a solid across y = 0, keeping it facing outward."""
    v = np.asarray(verts, float) * np.array([1, -1, 1])
    return v, [tuple(f[::-1]) for f in faces]
