"""Lofts the Collier's outer skin from rows.json, in pure numpy."""
import json
from pathlib import Path

import numpy as np
from scipy.interpolate import PchipInterpolator

ROWS = json.loads(Path(__file__).with_name("rows.json").read_text())
KEYS = ("zb", "zt", "w", "zs", "zw", "ks", "kt", "dt")
SEG = 7   # points per segment of the half section


def half_section(p):
    """Control points of the left half section, bottom centre to top centre, in (y, z)."""
    zb, zt, w = p["zb"], p["zt"], p["w"]
    h = zt - zb
    dt = min(p["dt"], 0.25 * h)
    ztop = zt - dt
    zs = min(max(p["zs"], zb + 0.45 * h), ztop - 0.12 * h)
    zw = min(max(p["zw"], zb + 0.18 * h), zs - 0.15 * (zs - zb))
    ws, wt = w * p["ks"], w * p["kt"]
    wt = min(wt, ws - 0.01)
    rb = min(0.06, 0.3 * h, 0.4 * w)
    return np.array([
        (0.0, zb), (0.5 * (w - rb), zb), (w - rb, zb + 0.004), (w - 0.25 * rb, zb + 0.6 * rb),
        (w, zw), (w - 0.10 * (w - ws), 0.5 * (zw + zs)), (w - 0.40 * (w - ws), zw + 0.85 * (zs - zw)), (ws, zs),
        (0.5 * (ws + wt) + 0.1 * (ws - wt), 0.5 * (zs + ztop) + 0.05 * (ztop - zs)), (wt, ztop),
        (0.55 * wt, zt - 0.3 * dt), (0.0, zt)])


def section(p):
    """Closed loop in (y, z), counter-clockwise seen from the front (+x)."""
    c = half_section(p)
    t = np.arange(len(c))
    fy, fz = PchipInterpolator(t, c[:, 0]), PchipInterpolator(t, c[:, 1])
    s = np.linspace(0, len(c) - 1, (len(c) - 1) * SEG + 1)
    left = np.column_stack([fy(s), fz(s)])
    left[0, 0] = left[-1, 0] = 0.0
    right = left[-2:0:-1] * np.array([-1, 1])
    # left side goes up on +y; looking from +x, +y is to the left, so up the left then down the right is clockwise.
    loop = np.concatenate([left, right])
    return loop[::-1]


def curves(rows):
    xs = np.array([r["x"] for r in rows])
    order = np.argsort(xs)
    xs = xs[order]
    rows = [rows[i] for i in order]
    return xs, {k: PchipInterpolator(xs, [r[k] for r in rows]) for k in KEYS}


def loft(rows, step=0.02, inset=0.0, x0=None, x1=None):
    """Loft the stations into a closed shell. inset shrinks every section (for the cabin)."""
    xs, cv = curves(rows)
    a = xs[0] if x0 is None else x0
    b = xs[-1] if x1 is None else x1
    n = max(2, int(np.ceil((b - a) / step)) + 1)
    t = (1 - np.cos(np.linspace(0, np.pi, n))) / 2 if x0 is None else np.linspace(0, 1, n)
    rings = []
    for x in a + (b - a) * t:
        p = {k: float(cv[k](x)) for k in KEYS}
        if inset:
            p["zb"] += inset; p["zt"] -= inset; p["w"] -= inset
            p["zs"] -= inset * 0.5; p["zw"] -= 0
        sec = section(p)
        rings.append(np.column_stack([np.full(len(sec), x), sec[:, 0], sec[:, 1]]))
    return _close(rings)


def _close(rings, mirror=False):
    m = len(rings[0])
    verts = np.concatenate(rings)
    faces = []
    for i in range(len(rings) - 1):
        a, b = i * m, (i + 1) * m
        for j in range(m):
            k = (j + 1) % m
            faces.append((a + j, a + k, b + k, b + j))
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
