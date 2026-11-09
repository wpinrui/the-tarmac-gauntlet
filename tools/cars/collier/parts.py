"""Separate parts of the Collier: wheels, mirrors, spoiler, small solids."""
import numpy as np

from body import ROWS, _close, extrude_outline

W = ROWS["wheel"]


def lathe(profile_ry, n=64):
    """Revolve a closed (r, y) profile around the y axis. Returns (verts, faces)."""
    prof = np.asarray(profile_ry, float)
    m = len(prof)
    verts = []
    for i in range(n):
        a = 2 * np.pi * i / n
        for r, y in prof:
            verts.append((r * np.cos(a), y, r * np.sin(a)))
    faces = []
    for i in range(n):
        a, b = i * m, ((i + 1) % n) * m
        for j in range(m):
            k = (j + 1) % m
            faces.append((a + j, a + k, b + k, b + j))
    return np.array(verts), faces


def tyre():
    R, w, rim = W["r"], W["width"], W["rim_r"]
    sh = 0.035
    prof = [(rim + 0.004, -w / 2 + 0.01)]
    for t in np.linspace(0, np.pi / 2, 6):
        prof.append((R - sh + sh * np.sin(t), -w / 2 + sh - sh * np.cos(t)))
    for t in np.linspace(np.pi / 2, np.pi, 6):
        prof.append((R - sh + sh * np.sin(t), w / 2 - sh - sh * np.cos(t)))
    prof.append((rim + 0.004, w / 2 - 0.01))
    prof.append((rim - 0.006, w / 2 - 0.016))
    prof.append((rim - 0.006, -w / 2 + 0.016))
    return lathe(prof, 72)


def rim_barrel():
    rim, w = W["rim_r"], W["width"]
    prof = [(0.06, -w / 2 + 0.05), (rim - 0.008, -w / 2 + 0.02), (rim + 0.012, -w / 2 + 0.012),
            (rim + 0.012, w / 2 - 0.004), (rim - 0.004, w / 2 - 0.002), (rim - 0.016, w / 2 - 0.02),
            (rim - 0.016, -w / 2 + 0.06), (0.06, -w / 2 + 0.07)]
    return lathe(prof, 72)


def spokes(n=10):
    """Ten straight spokes, widening towards the rim, dished towards the hub."""
    rim, w = W["rim_r"], W["width"]
    verts, faces = [], []
    for i in range(n):
        a = 2 * np.pi * i / n + np.pi / 2
        d = np.array([np.cos(a), 0, np.sin(a)])
        s = np.array([-np.sin(a), 0, np.cos(a)])
        base = len(verts)
        for r, hw, y in ((0.05, 0.013, w / 2 - 0.034), (rim - 0.006, 0.024, w / 2 - 0.008)):
            for side in (-1, 1):
                for dy in (-0.026, 0.0):
                    verts.append(d * r + s * hw * side + np.array([0, y + dy, 0]))
        b = base
        q = lambda *k: tuple(b + i for i in k)
        faces += [q(1, 3, 7, 5), q(0, 4, 6, 2), q(0, 1, 5, 4), q(2, 6, 7, 3), q(0, 2, 3, 1), q(4, 5, 7, 6)]
    return np.array(verts), faces


def hub():
    """Centre cap: rings along the axle closed with fans, so no point sits on the axis twice."""
    w = W["width"]
    a = np.linspace(0, 2 * np.pi, 40, endpoint=False)
    rings = [np.column_stack([r * np.cos(a), np.full(40, y), r * np.sin(a)])
             for r, y in ((0.068, w / 2 - 0.07), (0.068, w / 2 - 0.036), (0.052, w / 2 - 0.026),
                          (0.025, w / 2 - 0.021))]
    return _close(rings)


def wheel_parts():
    """Every wheel piece at the origin with the outer face towards +y."""
    return {"tyre": (*tyre(), "tyre"), "rim": (*rim_barrel(), "rim"),
            "spokes": (*spokes(), "rim"), "hub": (*hub(), "rim")}


def ellipsoid(cx, cy, cz, rx, ry, rz, cut=0.35, nring=32, nst=14):
    """Ellipsoid along x from its flat cut at -cut*rx to the front tip."""
    rings = []
    t = np.linspace(-cut, 0.97, nst)
    a = np.linspace(0, 2 * np.pi, nring, endpoint=False)
    for ti in t:
        s = np.sqrt(max(1 - ti * ti, 1e-4))
        rings.append(np.column_stack([np.full(nring, cx + rx * ti), cy + ry * s * np.cos(a), cz + rz * s * np.sin(a)]))
    return _close(rings)


def disc(centre, normal, r, depth, n=32):
    """Short closed cylinder pointing out along normal."""
    nrm = np.asarray(normal, float); nrm /= np.linalg.norm(nrm)
    a = np.array([0, 0, 1.0]) if abs(nrm[2]) < 0.9 else np.array([0, 1.0, 0])
    u = np.cross(nrm, a); u /= np.linalg.norm(u)
    v = np.cross(nrm, u)
    ang = np.linspace(0, 2 * np.pi, n, endpoint=False)
    ring = np.array([centre + r * (np.cos(t) * u + np.sin(t) * v) for t in ang])
    return _close([ring - nrm * depth / 2, ring + nrm * depth / 2])


def tube_ring(centre, normal, r0, r1, depth, n=32):
    """Flat ring (washer) pointing out along normal."""
    nrm = np.asarray(normal, float); nrm /= np.linalg.norm(nrm)
    a = np.array([0, 0, 1.0]) if abs(nrm[2]) < 0.9 else np.array([0, 1.0, 0])
    u = np.cross(nrm, a); u /= np.linalg.norm(u)
    v = np.cross(nrm, u)
    ang = np.linspace(0, 2 * np.pi, n, endpoint=False)
    verts = []
    for r in (r1, r0):
        for dd in (-depth / 2, depth / 2):
            verts += [centre + r * (np.cos(t) * u + np.sin(t) * v) + nrm * dd for t in ang]
    verts = np.array(verts)
    # loops: 0 outer back, 1 outer front, 2 inner back, 3 inner front
    L = lambda k, j: k * n + j % n
    faces = []
    for j in range(n):
        faces += [(L(0, j), L(0, j + 1), L(1, j + 1), L(1, j)),
                  (L(1, j), L(1, j + 1), L(3, j + 1), L(3, j)),
                  (L(3, j), L(3, j + 1), L(2, j + 1), L(2, j)),
                  (L(2, j), L(2, j + 1), L(0, j + 1), L(0, j))]
    return verts, faces


def box(x0, x1, y0, y1, z0, z1):
    v = np.array([(x, y, z) for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)])
    f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    return v, f


def spoiler():
    """Roof spoiler: the side outline lofted across, dropping and shortening at the ends."""
    sp = ROWS["spoiler"]
    o = np.asarray(sp["outline"], float)
    h = sp["half"]
    rings = []
    for y in np.linspace(-h, h, 25):
        t = abs(y) / h
        drop = 0.035 * t ** 2 + 0.03 * max(t - 0.85, 0) / 0.15
        k = 1 - 0.25 * max(t - 0.8, 0) / 0.2
        c = o.mean(0)
        ring = c + (o - c) * np.array([k, 1]) - np.array([0, drop])
        rings.append(np.column_stack([ring[:, 0], np.full(len(ring), y), ring[:, 1]]))
    return _close(rings)


def mirror_parts():
    m = ROWS["mirror"]
    out = {}
    for side, sgn in (("l", 1), ("r", -1)):
        y = m["y"] * sgn
        out[f"mirror_{side}"] = (*ellipsoid(m["x"], y, m["z"], m["rx"], m["ry"], m["rz"]), "body")
    return out


def exhaust_parts():
    e = ROWS["exhaust"]
    out = {}
    x0 = ROWS["stations"][0]["x"]
    xs = min(r["x"] for r in ROWS["stations"])
    for i, y in enumerate(e["y"]):
        a = np.linspace(0, 2 * np.pi, 32, endpoint=False)
        ring = np.column_stack([np.zeros(32), y + e["rw"] * np.cos(a), e["z"] + e["rz"] * np.sin(a)])
        r0 = ring + np.array([xs + 0.12, 0, 0]); r1 = ring + np.array([xs - 0.025, 0, 0])
        out[f"exhaust_{i}"] = (*_close([r0, r1]), "chrome")
    return out
