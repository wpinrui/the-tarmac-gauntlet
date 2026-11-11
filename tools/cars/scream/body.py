"""The Scream's outer skin, lofted from the measured sections in rows.json, in pure numpy.

Each station holds a centre height zc and a radius at even angles round (0, zc), from the
bottom centre round the left side to the top centre. Each radius and the centre height become
a smooth curve along the car (monotone cubic), and the skin is lofted through them. The nose
and tail are smoothed harder than the middle, so the bumpers read as plain rounded forms.
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
END_SMOOTH = 0.45     # the last stretch of nose and tail is smoothed harder
END_ROLL = 0.03       # the blunt ends roll over within this distance instead of a sharp edge


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
    from scipy.ndimage import gaussian_filter
    st = sorted(stations, key=lambda r: r["x"])
    xs = np.array([r["x"] for r in st])
    zc = np.array([r["zc"] for r in st], float)
    R = np.array([r["r"] for r in st], float)
    soft = gaussian_filter(R, (2.5, 2.0), mode="nearest")
    w = np.clip(np.maximum(xs - (xs.max() - END_SMOOTH), (xs.min() + END_SMOOTH) - xs) / 0.15, 0, 1)[:, None]
    R = R * (1 - w) + soft * w
    cv = {"zc": monotone(list(zip(xs, gaussian_filter(zc, 1.5, mode="nearest"))))}
    cv["r"] = [monotone(list(zip(xs, R[:, j]))) for j in range(N)]
    return xs, cv


def at(cv, x):
    return {"zc": float(cv["zc"](x)), "r": [max(float(f(x)), 0.002) for f in cv["r"]]}


XS, CV = curves(ROWS["stations"])
NOSE, TAIL = float(XS[-1]), float(XS[0])


def skin(step=0.016):
    """The outer skin as one closed solid. Within END_ROLL of each end the section is inset on
    a quarter circle, so the bumper faces meet the sides on a rounded edge."""
    from shapely.geometry import Polygon
    from shapely.geometry.polygon import orient
    n_pts = len(section(at(CV, NOSE)))

    def sec_at(x):
        sec = section(at(CV, x))
        over = max(x - (NOSE - END_ROLL), (TAIL + END_ROLL) - x, 0.0)
        if over <= 0:
            return sec
        d = END_ROLL * (1 - np.sqrt(max(1 - (over / END_ROLL) ** 2, 0.0)))
        g = Polygon(sec).buffer(0).buffer(-min(d, 0.9 * END_ROLL), join_style=1)
        if g.is_empty:
            return sec
        if g.geom_type != "Polygon":
            g = max(g.geoms, key=lambda q: q.area)
        return even(np.array(orient(g, 1).exterior.coords)[:-1], n_pts)

    n = int(np.ceil((NOSE - TAIL) / step)) + 1
    t = (1 - np.cos(np.linspace(0, np.pi, n))) / 2       # closer rings at the nose and tail
    rings = []
    for x in TAIL + (NOSE - TAIL) * t:
        sec = sec_at(x)
        rings.append(np.column_stack([np.full(len(sec), x), sec[:, 0], sec[:, 1]]))
    return close_rings(rings)


def inside(x, inset):
    """The section at x shrunk by inset, also kept inset from the sections just ahead and behind,
    so the wall keeps its thickness where the body slopes along x."""
    from shapely.geometry import Polygon
    out = None
    for dx in (-inset, -inset / 2, 0, inset / 2, inset):
        p = Polygon(section(at(CV, x + dx))).buffer(0).buffer(-inset)
        out = p if out is None else out.intersection(p)
    if out.geom_type != "Polygon":
        out = max(getattr(out, "geoms", [out]), key=lambda g: g.area)
    return out


def y_at(x, z):
    """Outer half width of the skin at x and height z."""
    h = half(at(CV, x))
    best = 0.0
    for (y0, z0), (y1, z1) in zip(h[:-1], h[1:]):
        if (z0 - z) * (z1 - z) <= 0 and z0 != z1:
            best = max(best, y0 + (y1 - y0) * (z - z0) / (z1 - z0))
    return best


def z_top(x):
    """Height of the skin's top centre at x."""
    return float(half(at(CV, x))[-1, 1])


def even(ring, n):
    """n points round a closed loop by arc length, starting at its lowest middle point."""
    k = int(np.argmin(np.abs(ring[:, 0]) + 10 * (ring[:, 1] - ring[:, 1].min())))
    ring = np.roll(ring, -k, axis=0)
    ring = np.vstack([ring, ring[:1]])
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(ring, axis=0), axis=1))])
    t = np.linspace(0, s[-1], n, endpoint=False)
    return np.column_stack([np.interp(t, s, ring[:, 0]), np.interp(t, s, ring[:, 1])])


def cavity(x0, x1, floor, inset=0.035, n=40):
    """The inside of the body between x0 and x1, above floor, as a closed solid to cut away."""
    from shapely.geometry import box
    from shapely.geometry.polygon import orient
    rings = []
    for x in np.linspace(x0, x1, n):
        inner = inside(x, inset).intersection(box(-3, floor, 3, 5))
        if inner.geom_type != "Polygon":
            inner = max(getattr(inner, "geoms", [inner]), key=lambda g: g.area)
        if inner.area < 1e-3:
            continue
        ring = even(np.array(orient(inner, 1).exterior.coords)[:-1], 72)
        rings.append(np.column_stack([np.full(len(ring), x), ring]))
    return close_rings(rings)
