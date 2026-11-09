"""Body shell lofted from cross-sections.

Each station along X gets a closed section: a periodic cubic B-spline through
a mirrored ring of control points built from the profile curves. Stations are
joined into one watertight tube with caps at the nose and tail.
"""
import numpy as np
import trimesh
import profiles as P

RING = 160  # samples around a section


def smooth01(t):
    t = np.clip(t, 0, 1)
    return t * t * (3 - 2 * t)


NOSE_Z, TAIL_Z = 0.42, 0.48
CAP = 0.07


def ends(x):
    """Elliptical closure over the last CAP metres at each end."""
    for tip, z0, sgn in ((P.NOSE_X, NOSE_Z, 1), (P.TAIL_X, TAIL_Z, -1)):
        d = (tip - x) * sgn
        if d < CAP:
            t = max(d, 0.0) / CAP
            return np.sqrt(max(1 - (1 - t) ** 2, 0.0)), z0
    return 1.0, None


def control_points(x):
    zb, zt, w = float(P.zb(x)), float(P.zt(x)), float(P.w(x))
    f, z0 = ends(x)
    if z0 is not None:
        w *= f
        zt = z0 + (zt - z0) * f
        zb = z0 + (zb - z0) * f
    g = float(P.greenhouse(x))
    h = max(zt - zb, 1e-3)
    end = min(1.0, w / 0.86)  # sections shrink at the ends

    # lower body
    z_m = min(float(P.z_max_w(x)), zb + 0.62 * h)
    z_m = max(z_m, zb + 0.3 * h)
    # shoulder / glass base
    belt = float(P.z_belt(x)) if g > 0 else float(P.z_fender(x))
    belt_bonnet = float(P.z_fender(x))
    z_g = (1 - g) * belt_bonnet + g * float(P.z_belt(x))
    z_g = min(z_g, zb + 0.85 * h)
    z_g = max(z_g, z_m + 0.05 * h)
    z_sh = z_g - 0.05 * min(1.0, h / 0.6)
    z_sh = max(z_sh, z_m + 0.02 * h)

    inset = float(np.interp(x, [-1.9, -1.5, -1.1, 0.6, 1.0], [0.012, 0.03, 0.06, 0.06, 0.05]))
    w_gl_house = w - inset * end
    w_gl_bonnet = w - 0.05 * end
    w_g = (1 - g) * w_gl_bonnet + g * w_gl_house

    # upper control points: roof edge and crown, or bonnet edge and crown
    z_re = float(P.z_roof_edge(x))
    z6_house, w6_house = min(max(z_re, z_g + 0.02), zt - 0.01), float(P.w_roof(x)) * end
    z6_bonnet, w6_bonnet = z_g + 0.65 * (zt - z_g), w * 0.55
    z6 = (1 - g) * z6_bonnet + g * z6_house
    w6 = (1 - g) * w6_bonnet + g * w6_house
    rc = min(0.05, 0.3 * (zt - z6)) if zt > z6 else 0.0
    half = [
        (0.0, zb), (0.6 * w, zb), (0.9 * w, zb + 0.02 * end),
        (w - 0.01 * end, zb + 0.11 * h), (w, z_m), (w - 0.012 * end, z_sh),
        (w_g, z_g), (w6, z6), (w6 - 1.2 * rc, z6 + 0.8 * rc), (0.5 * w6, zt - 0.012 * (zt - z6 > 0.02)),
        (0.0, zt),
    ]
    return half


def ring_from_half(half):
    """Mirror the half section into a closed control ring (y, z)."""
    pts = list(half) + [(-y, z) for (y, z) in half[-2:0:-1]]
    return np.array(pts)


def bspline_closed(ctrl, n):
    m = len(ctrl)
    t = np.linspace(0, m, n, endpoint=False)
    i = np.floor(t).astype(int)
    u = t - i
    b0 = (1 - u) ** 3 / 6
    b1 = (3 * u ** 3 - 6 * u ** 2 + 4) / 6
    b2 = (-3 * u ** 3 + 3 * u ** 2 + 3 * u + 1) / 6
    b3 = u ** 3 / 6
    p = (b0[:, None] * ctrl[(i - 1) % m] + b1[:, None] * ctrl[i % m]
         + b2[:, None] * ctrl[(i + 1) % m] + b3[:, None] * ctrl[(i + 2) % m])
    return p


def catmull_closed(pts, n, alpha=0.5):
    """Closed centripetal Catmull-Rom through pts, n samples spread by arc length."""
    m = len(pts)
    dense = []
    for i in range(m):
        p0, p1, p2, p3 = pts[(i - 1) % m], pts[i], pts[(i + 1) % m], pts[(i + 2) % m]
        t0 = 0.0
        t1 = t0 + max(np.linalg.norm(p1 - p0), 1e-6) ** alpha
        t2 = t1 + max(np.linalg.norm(p2 - p1), 1e-6) ** alpha
        t3 = t2 + max(np.linalg.norm(p3 - p2), 1e-6) ** alpha
        t = np.linspace(t1, t2, 24, endpoint=False)[:, None]
        a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1
        a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2
        a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3
        b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2
        b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3
        dense.append((t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2)
    d = np.vstack(dense)
    return d


def resample(d, n):
    """n points along a closed polyline, starting at its first point."""
    seg = np.linalg.norm(np.roll(d, -1, 0) - d, axis=1)
    s = np.r_[0, np.cumsum(seg)]
    t = np.linspace(0, s[-1], n, endpoint=False)
    dd = np.vstack([d, d[:1]])
    return np.c_[np.interp(t, s, dd[:, 0]), np.interp(t, s, dd[:, 1])]


def section(x, n=RING):
    ctrl = ring_from_half(control_points(x))
    return resample(catmull_closed(ctrl, n), n)


def stations():
    a, b = P.TAIL_X + 0.0005, P.NOSE_X - 0.0005
    t = np.linspace(0, 1, 420)
    # pack stations toward both ends where the shape turns fast
    s = 0.5 - 0.5 * np.cos(np.pi * t)
    s = 0.6 * s + 0.4 * t
    return a + (b - a) * s


def loft():
    xs = stations()
    rings = []
    for x in xs:
        yz = section(x)
        rings.append(np.c_[np.full(len(yz), x), yz])
    V = np.vstack(rings)
    n = RING
    F = []
    for k in range(len(xs) - 1):
        a0, b0 = k * n, (k + 1) * n
        for j in range(n):
            j1 = (j + 1) % n
            F.append((a0 + j, a0 + j1, b0 + j1))
            F.append((a0 + j, b0 + j1, b0 + j))
    # caps
    for k, flip in ((0, True), (len(xs) - 1, False)):
        c = rings[k].mean(0)
        ci = len(V)
        V = np.vstack([V, c])
        base = k * n
        for j in range(n):
            j1 = (j + 1) % n
            f = (base + j, base + j1, ci)
            F.append(f[::-1] if not flip else f)
    m = trimesh.Trimesh(V, np.array(F), process=True)
    trimesh.repair.fix_normals(m)
    trimesh.smoothing.filter_taubin(m, lamb=0.5, nu=-0.53, iterations=12)
    return m


def arches(m, r=0.378, y_in=0.50):
    """Cut the wheel arches with cylinders across the outer body."""
    import manifold3d as mf
    out = trimesh.Trimesh(m.vertices, m.faces)
    cuts = []
    for sx in (1, -1):
        for sy in (1, -1):
            c = trimesh.creation.cylinder(radius=r, height=0.6, sections=96)
            c.apply_transform(trimesh.transformations.rotation_matrix(np.pi / 2, [1, 0, 0]))
            c.apply_translation([sx * P.WHEEL_X, sy * (y_in + 0.3), P.WHEEL_Z])
            cuts.append(c)
    return trimesh.boolean.difference([out] + cuts, engine='manifold')


if __name__ == '__main__':
    import sys
    m = loft()
    print(len(m.faces), m.is_watertight, m.bounds)
    m = arches(m)
    print(len(m.faces), m.is_watertight)
    m.export(sys.argv[1])
