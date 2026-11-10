"""The Oboe's outer skin, lofted from the measured sections in rows.json, in pure numpy.

Each station holds a centre height zc and a radius at even angles round (0, zc), from the
bottom centre round the left side to the top centre. Each radius and the centre height
become a smooth curve along the car (monotone cubic), and the skin is lofted through them.
"""
import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "common"))
from geom import close_rings, monotone  # noqa: E402

ROWS = json.loads(Path(__file__).with_name("rows.json").read_text())
N = len(ROWS["stations"][0]["r"])
TH = np.linspace(-np.pi / 2, np.pi / 2, N)


def half(p):
    """Left half section, (y, z) from bottom centre to top centre."""
    r = np.asarray(p["r"])
    out = np.column_stack([r * np.cos(TH), p["zc"] + r * np.sin(TH)])
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
    cv = {"zc": monotone(list(zip(xs, [r["zc"] for r in st])))}
    R = np.array([r["r"] for r in st])
    cv["r"] = [monotone(list(zip(xs, R[:, j]))) for j in range(N)]
    return xs, cv


def at(cv, x):
    return {"zc": float(cv["zc"](x)), "r": [max(float(f(x)), 0.002) for f in cv["r"]]}


def skin(step=0.016):
    """The outer skin as one closed solid."""
    xs, cv = curves(ROWS["stations"])
    n = int(np.ceil((xs[-1] - xs[0]) / step)) + 1
    t = (1 - np.cos(np.linspace(0, np.pi, n))) / 2       # closer rings at the nose and tail
    rings = []
    for x in xs[0] + (xs[-1] - xs[0]) * t:
        sec = section(at(cv, x))
        rings.append(np.column_stack([np.full(len(sec), x), sec[:, 0], sec[:, 1]]))
    return close_rings(rings)


def inside(cv, x, inset):
    """The section at x shrunk by inset, also kept inset from the sections just ahead and behind,
    so the wall keeps its thickness where the body slopes along x."""
    from shapely.geometry import Polygon
    out = None
    for dx in (-inset, -inset / 2, 0, inset / 2, inset):
        p = Polygon(section(at(cv, x + dx))).buffer(0).buffer(-inset)
        out = p if out is None else out.intersection(p)
    if out.geom_type != "Polygon":
        out = max(getattr(out, "geoms", [out]), key=lambda g: g.area)
    return out


def y_at(cv, x, z):
    """Outer half width of the skin at x and height z."""
    h = half(at(cv, x))
    best = 0.0
    for (y0, z0), (y1, z1) in zip(h[:-1], h[1:]):
        if (z0 - z) * (z1 - z) <= 0 and z0 != z1:
            best = max(best, y0 + (y1 - y0) * (z - z0) / (z1 - z0))
    return best


def cavity(x0, x1, floor, inset=0.035, n=40):
    """The inside of the body between x0 and x1, above floor, as a closed solid to cut away."""
    from shapely.geometry import box
    from shapely.geometry.polygon import orient
    _xs, cv = curves(ROWS["stations"])
    rings = []
    for x in np.linspace(x0, x1, n):
        inner = inside(cv, x, inset).intersection(box(-3, floor, 3, 5))
        if inner.geom_type != "Polygon" or inner.area < 1e-3:
            continue
        ring = np.array(orient(inner, 1).exterior.coords)[:-1]
        ring = _even(ring, 72)
        rings.append(np.column_stack([np.full(len(ring), x), ring]))
    return close_rings(rings)


def _even(ring, n):
    """n points round a closed loop by arc length, starting at its lowest middle point."""
    k = int(np.argmin(np.abs(ring[:, 0]) + 10 * (ring[:, 1] - ring[:, 1].min())))
    ring = np.roll(ring, -k, axis=0)
    ring = np.vstack([ring, ring[:1]])
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(ring, axis=0), axis=1))])
    t = np.linspace(0, s[-1], n, endpoint=False)
    return np.column_stack([np.interp(t, s, ring[:, 0]), np.interp(t, s, ring[:, 1])])
