"""Lofts the outer skin pieces of the Cheonma from rows.json, in pure numpy."""
import json
from pathlib import Path

import numpy as np
from scipy.interpolate import PchipInterpolator

ROWS = json.loads(Path(__file__).with_name("rows.json").read_text())
KEYS = ("yi", "yo", "zb", "zt", "rti", "rto", "rb")
ARC = 8      # points per corner
SIDE = 4     # points per straight side


def _line(a, b, n):
    t = np.linspace(0, 1, n, endpoint=False)[:, None]
    return a + (b - a) * t


def _arc(c, r, a0, a1, n):
    t = np.linspace(a0, a1, n, endpoint=False)
    return np.stack([c[0] + r * np.cos(t), c[1] + r * np.sin(t)], 1)


def section(p):
    """Closed rounded-rect loop in (y, z), counter-clockwise seen from the front."""
    yi, yo, zb, zt = p["yi"], p["yo"], p["zb"], p["zt"]
    w, h = yo - yi, zt - zb
    lim = 0.499 * min(w, h)
    rb, rto, rti = (min(max(p[k], 1e-3), lim) for k in ("rb", "rto", "rti"))
    rto = min(rto, h - rb - 1e-3)
    rti = min(rti, h - rb - 1e-3)
    pts = [
        _line(np.array([yi + rb, zb]), np.array([yo - rb, zb]), SIDE),
        _arc((yo - rb, zb + rb), rb, -np.pi / 2, 0, ARC),
        _line(np.array([yo, zb + rb]), np.array([yo, zt - rto]), SIDE),
        _arc((yo - rto, zt - rto), rto, 0, np.pi / 2, ARC),
        _line(np.array([yo - rto, zt]), np.array([yi + rti, zt]), SIDE),
        _arc((yi + rti, zt - rti), rti, np.pi / 2, np.pi, ARC),
        _line(np.array([yi, zt - rti]), np.array([yi, zb + rb]), SIDE),
        _arc((yi + rb, zb + rb), rb, np.pi, 1.5 * np.pi, ARC),
    ]
    return np.concatenate(pts)


def loft(rows, step=0.03, mirror=False):
    """Loft rounded-rect rows along x into a closed shell. Returns (verts, faces)."""
    xs = np.array([r["x"] for r in rows])
    order = np.argsort(xs)
    xs = xs[order]
    rows = [rows[i] for i in order]
    curves = {k: PchipInterpolator(xs, [r[k] for r in rows]) for k in KEYS}
    n = max(2, int(np.ceil((xs[-1] - xs[0]) / step)) + 1)
    # Cluster stations near both ends so rounded ends stay smooth.
    t = (1 - np.cos(np.linspace(0, np.pi, n))) / 2
    samples = xs[0] + (xs[-1] - xs[0]) * t
    rings = []
    for x in samples:
        sec = section({k: float(curves[k](x)) for k in KEYS})
        rings.append(np.column_stack([np.full(len(sec), x), sec[:, 0], sec[:, 1]]))
    return _close(rings, mirror)


def _close(rings, mirror=False):
    m = len(rings[0])
    verts = np.concatenate(rings)
    faces = []
    for i in range(len(rings) - 1):
        a, b = i * m, (i + 1) * m
        for j in range(m):
            k = (j + 1) % m
            faces.append((a + j, a + k, b + k, b + j))
    # Caps: fan to the ring centroid.
    for idx, ring in ((0, rings[0]), (len(rings) - 1, rings[-1])):
        c = len(verts)
        verts = np.vstack([verts, ring.mean(0)])
        base = idx * m
        for j in range(m):
            k = (j + 1) % m
            faces.append((base + k, base + j, c) if idx == 0 else (base + j, base + k, c))
    if mirror:
        verts = verts * np.array([1, -1, 1])
        faces = [f[::-1] for f in faces]
    return verts, faces


def extrude_outline(outline_xz, y, half_t):
    """Flat plate from a side outline (x, z), centred on y with half thickness."""
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


def pieces():
    """Body pieces to be joined into one shell: name -> (verts, faces)."""
    out = {}
    for name in ("front_pod", "rear_fender", "sidepod"):
        out[name + "_l"] = loft(ROWS[name])
        out[name + "_r"] = loft(ROWS[name], mirror=True)
    for name in ("nose", "deck", "engine_cover", "canopy"):
        out[name] = loft(ROWS[name])
    fin = ROWS["fin"]
    out["fin"] = extrude_outline(fin["outline"], 0.0, fin["half_t"])
    return out


def trim_pieces():
    """Separate pieces in their own materials: name -> (verts, faces, material)."""
    out = {
        "splitter": (*loft(ROWS["splitter"], step=0.05), "carbon"),
        "floor": (*loft(ROWS["floor"], step=0.2), "carbon"),
        "rear_box": (*loft(ROWS["rear_box"], step=0.05), "body"),
        "scoop": (*loft(ROWS["scoop"], step=0.05), "body"),
    }
    return out
