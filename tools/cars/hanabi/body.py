"""The Hanabi's outer skin and roof, lofted from the measured sections in rows.json, in pure numpy.

Each body station holds a centre height zc and a radius at even angles round (0, zc), from
the bottom centre round the left side to the top centre. Each radius and the centre height
become a smooth curve along the car (monotone cubic), and the skin is lofted through them.
Roof stations hold radii round (0, belt) over the upper quarter only.
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
    zc = np.array([r["zc"] for r in st], float)
    # the nose only falls towards its tip: where a measured section's top rises above the one
    # behind it, lower the section
    for i in range(1, len(st)):
        if xs[i] > xs.max() - 0.35:
            excess = (zc[i] + st[i]["r"][-1]) - (zc[i - 1] + st[i - 1]["r"][-1])
            if excess > 0:
                zc[i] -= excess
    cv = {"zc": monotone(list(zip(xs, zc)))}
    R = np.array([r["r"] for r in st], float)
    # round the nose: the grille's filled top edge leaves a ragged lip across the angles there
    nose = xs > xs.max() - 0.35
    from scipy.ndimage import gaussian_filter
    k = int(np.argmax(nose)) - 4                       # a few stations behind, so the blend is smooth
    R[k:] = gaussian_filter(R[k:], (2.0, 1.5), mode="nearest")
    cv["r"] = [monotone(list(zip(xs, R[:, j]))) for j in range(R.shape[1])]
    return xs, cv


def at(cv, x):
    return {"zc": float(cv["zc"](x)), "r": [max(float(f(x)), 0.002) for f in cv["r"]]}


XS, CV = curves(ROWS["stations"])
NOSE, TAIL = float(XS[-1]), float(XS[0])


def loft(sec_at, x0, x1, step, centre=None, even=False):
    """Rings along x, closed by a fan at each end, round centre(x) as (y, z) when given.
    The rings close up towards the ends unless even."""
    n = int(np.ceil((x1 - x0) / step)) + 1
    t = np.linspace(0, 1, n) if even else (1 - np.cos(np.linspace(0, np.pi, n))) / 2
    rings = []
    for x in x0 + (x1 - x0) * t:
        sec = sec_at(x)
        rings.append(np.column_stack([np.full(len(sec), x), sec[:, 0], sec[:, 1]]))
    ends = None if centre is None else [np.array([x, *centre(x)]) for x in (x0, x1)]
    return close_rings(rings, centres=ends)


END_ROLL = 0.025      # the blunt ends roll over within this distance instead of a sharp edge


def skin(step=0.016):
    """The outer skin as one closed solid. Within END_ROLL of each end the section is inset on
    a quarter circle, so the bumper faces meet the sides on a rounded edge."""
    from shapely.geometry import Polygon
    from shapely.geometry.polygon import orient
    n = len(section(at(CV, NOSE)))

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
        ring = even(np.array(orient(g, 1).exterior.coords)[:-1], n)   # anticlockwise, as section()
        return ring
    return loft(sec_at, TAIL, NOSE, step)


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


def cavity(x0, x1, floor, clip, inset=0.035, n=40):
    """The inside of the body between x0 and x1, above floor and inside clip(x) (a shapely shape
    in (y, z)), as a closed solid to cut away."""
    from shapely.geometry import box
    from shapely.geometry.polygon import orient
    rings = []
    for x in np.linspace(x0, x1, n):
        inner = inside(x, inset).intersection(box(-3, floor, 3, 5)).intersection(clip(x))
        if inner.geom_type != "Polygon":
            inner = max(getattr(inner, "geoms", [inner]), key=lambda g: g.area)
        if inner.area < 1e-3:
            continue
        ring = even(np.array(orient(inner, 1).exterior.coords)[:-1], 96)
        rings.append(np.column_stack([np.full(len(ring), x), ring]))
    return close_rings(rings)


# ---------------------------------------------------------------- roof


def roof_curves():
    st = sorted(ROWS["roof"]["sections"], key=lambda r: r["x"])
    xs = np.array([r["x"] for r in st])
    R = np.array([r["r"] for r in st], float)
    for j in range(R.shape[1]):                 # fill misses along the car
        col = R[:, j]; ok = ~np.isnan(col)
        R[:, j] = np.interp(xs, xs[ok], col[ok])
    from scipy.ndimage import gaussian_filter, median_filter
    R = gaussian_filter(median_filter(R, size=(3, 3), mode="nearest"), (1.0, 0.7), mode="nearest")
    return xs, [monotone(list(zip(xs, R[:, j]))) for j in range(R.shape[1])]


def roof_section(rcv, x, belt, drop=0.06, grow=0.0):
    """Closed (y, z) loop of the roof at x, from below the belt to the top, grown by grow."""
    th = np.linspace(0.02, np.pi / 2 - 0.01, len(rcv))
    r = np.array([max(float(f(x)), 0.01) for f in rcv]) + grow
    left = np.column_stack([r * np.cos(th), belt + r * np.sin(th)])
    left = np.vstack([[left[0, 0], belt - drop], left, [0.0, left[-1, 1]]])
    right = left[::-1] * np.array([-1, 1])
    return np.vstack([left, right[1:-1]])
