"""The Oboe's outer skin, lofted from rows.json in pure numpy.

Each cross-section has three parts, left half drawn bottom centre to top centre:
  lower: a squarish quarter from the floor centre out to the widest point,
  side:  an S from the widest point in to the belt, vertical at both ends,
  top:   a squarish quarter from the belt up to the centre line, leaning in on the greenhouse.
"""
import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "common"))
from geom import close_rings, monotone  # noqa: E402

ROWS = json.loads(Path(__file__).with_name("rows.json").read_text())
KEYS = ("zb", "zw", "zs", "zt", "w", "kb", "nt", "lean", "nb")
N_LOW, N_SIDE, N_TOP = 14, 10, 28


def _quarter(n, e):
    """Superellipse quarter from angle 0 to 90 degrees: (cos, sin) shaped by exponent e."""
    a = np.linspace(0, np.pi / 2, n)
    c, s = np.cos(a), np.sin(a)
    return np.sign(c) * np.abs(c) ** (2 / e), np.sign(s) * np.abs(s) ** (2 / e)


def half(p):
    """Left half section, (y, z) from bottom centre to top centre."""
    zb, zt, w = p["zb"], p["zt"], p["w"]
    h = zt - zb
    zw = np.clip(p["zw"], zb + 0.12 * h, zb + 0.6 * h)
    zs = np.clip(p["zs"], zw + 0.08 * h, zt - 0.06 * h)
    ws = w * p["kb"]
    # lower: centre (0, zw), radii (w, zw - zb), from the bottom centre round to (w, zw)
    c, s = _quarter(N_LOW, p["nb"])
    low = np.column_stack([w * c[::-1], zw - (zw - zb) * s[::-1]])
    # side: an S from (w, zw) to (ws, zs)
    t = np.linspace(0, 1, N_SIDE + 1)[1:]
    st = t * t * (3 - 2 * t)
    mid = np.column_stack([w + (ws - w) * st, zw + (zs - zw) * t])
    # top: centre (0, zs), radii (ws, zt - zs), leaning in as it rises
    c, s = _quarter(N_TOP, p["nt"])
    c, s = c[1:], s[1:]
    lean = 1 - p["lean"] * s ** 1.5
    top = np.column_stack([ws * c * lean, zs + (zt - zs) * s])
    out = np.vstack([low, mid, top])
    out[0, 0] = out[-1, 0] = 0.0
    return out


def section(p):
    """Closed loop in (y, z), anticlockwise seen from the front."""
    left = half(p)
    right = left[-2:0:-1] * np.array([-1, 1])
    return np.vstack([left, right])[::-1]


def curves(stations):
    st = sorted(stations, key=lambda r: r["x"])
    xs = np.array([r["x"] for r in st])
    return xs, {k: monotone(list(zip(xs, [r[k] for r in st]))) for k in KEYS}


def at(cv, x):
    return {k: float(cv[k](x)) for k in KEYS}


def skin(step=0.015):
    """The outer skin as one closed solid."""
    xs, cv = curves(ROWS["stations"])
    n = int(np.ceil((xs[-1] - xs[0]) / step)) + 1
    t = (1 - np.cos(np.linspace(0, np.pi, n))) / 2       # closer rings at the nose and tail
    rings = []
    for x in xs[0] + (xs[-1] - xs[0]) * t:
        sec = section(at(cv, x))
        rings.append(np.column_stack([np.full(len(sec), x), sec[:, 0], sec[:, 1]]))
    return close_rings(rings)


def cavity(x0, x1, floor, inset=0.035, n=40):
    """The inside of the body between x0 and x1, above floor, as a closed solid to cut away."""
    from shapely.geometry.polygon import orient
    _xs, cv = curves(ROWS["stations"])
    rings = []
    from shapely.geometry import box
    for x in np.linspace(x0, x1, n):
        inner = inside(cv, x, inset).intersection(box(-3, floor, 3, 5))
        if inner.geom_type != "Polygon" or inner.area < 1e-3:
            continue
        ring = np.array(orient(inner, 1).exterior.coords)[:-1]
        ring = _even(ring, 72)
        rings.append(np.column_stack([np.full(len(ring), x), ring]))
    return close_rings(rings)


def inside(cv, x, inset):
    """The section at x shrunk by inset, also kept inset from the sections just ahead and behind,
    so the wall keeps its thickness where the body slopes along x."""
    from shapely.geometry import Polygon
    out = None
    for dx in (-inset, -inset / 2, 0, inset / 2, inset):
        p = Polygon(section(at(cv, x + dx))).buffer(-inset)
        out = p if out is None else out.intersection(p)
    if out.geom_type != "Polygon":
        out = max(getattr(out, "geoms", [out]), key=lambda g: g.area)
    return out


def y_at(cv, x, z):
    """Outer half width of the skin at x and height z."""
    h = half(at(cv, x))
    return float(np.interp(z, h[:, 1], h[:, 0]))


def _even(ring, n):
    """n points round a closed loop by arc length, starting at its lowest middle point."""
    k = int(np.argmin(np.abs(ring[:, 0]) + 10 * (ring[:, 1] - ring[:, 1].min())))
    ring = np.roll(ring, -k, axis=0)
    ring = np.vstack([ring, ring[:1]])
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(ring, axis=0), axis=1))])
    t = np.linspace(0, s[-1], n, endpoint=False)
    return np.column_stack([np.interp(t, s, ring[:, 0]), np.interp(t, s, ring[:, 1])])
