"""The Fox's outer skin, lofted from the traced rows in rows.json, in pure numpy.

Each station holds the values of a hatchback section with flared arches: the bottom, the widest
point, the shoulder, and the deck across the top. Over the arches the flare stands out from the
side and rolls in to the shoulder; over each wheel a round flare stands out from it. Over the bonnet the shoulder is the wing's top
and the deck the bonnet's crowned middle; behind the cabin the deck is the tailgate's top. Over
the cabin a tall greenhouse stands on the shoulder: its
glass foot, the roof edge (A-pillar, roof side or C-pillar) and the crown at the centre. Every
value becomes a smooth curve along the car (monotone cubic); at each x the section is drawn,
its corners rounded, the greenhouse joined on, and the outline resampled evenly so every ring
has the same points.
"""
import json
import sys
from functools import lru_cache
from pathlib import Path

import numpy as np
from shapely.geometry import Polygon, box
from shapely.geometry.polygon import orient
from shapely.ops import unary_union

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "common"))
from geom import close_rings, monotone  # noqa: E402

ROWS = json.loads(Path(__file__).with_name("rows.json").read_text())
ST = sorted(ROWS["stations"], key=lambda s: s["x"])
LOW_KEYS = ("zb", "w", "zw", "zwt", "tuck", "zs", "ws", "zd")
GH_KEYS = ("zt", "zr", "wr", "wg")
GH0, GH1 = ROWS["gh"]
N = 192            # points round each ring
CORNER = 0.028     # convex corners round off this much
FILLET = 0.012     # and the cowl and the greenhouse's foot this much


def _curves():
    cv = {k: monotone([(s["x"], s[k]) for s in ST]) for k in LOW_KEYS}
    gh = [s for s in ST if "zt" in s]
    cv.update({k: monotone([(s["x"], s[k]) for s in gh]) for k in GH_KEYS})
    return cv


CV = _curves()
NOSE, TAIL = ST[-1]["x"], ST[0]["x"]


def values(x):
    return {k: float(f(x)) for k, f in CV.items()}


def _crown(w, z_edge, z_mid, n=14):
    """Points from (w, z_edge) across to the centre (0, z_mid) on an even parabola."""
    t = np.linspace(0, 1, n)
    return [(w * (1 - u), z_edge + (z_mid - z_edge) * (1 - (1 - u) ** 2)) for u in t]


def lower_half(v):
    zb, w, zw, zt, tk, zs, ws, zd = (v[k] for k in LOW_KEYS)
    h = max(zs - zb, 0.01)
    zs = zb + h
    lo, hi = zb + min(0.06, 0.3 * h), zs - min(0.06, 0.3 * h)
    zw = min(max(zw, lo), hi)
    zt = min(max(zt, zw), hi)
    ws = min(ws, w - 0.005)
    knee = (w - 0.30 * (w - ws), zt + 0.62 * (zs - zt))     # the band stays full, then rolls in
    pts = [(0.0, zb), (w - tk, zb), (w - 0.35 * tk, zb + 0.5 * (zw - zb)), (w, zw)]
    pts += ([(w, zt)] if zt > zw + 0.004 else []) + [knee, (ws, zs)]
    inner = ws - 0.02
    lip = zs + 0.004 if zd >= zs else zs
    pts += [(inner, lip)] + _crown(inner, lip, zd)[1:]
    return pts


def gh_half(v):
    zs, wg, wr, zr, zt = v["zs"], v["wg"], v["wr"], v["zr"], v["zt"]
    wg = min(wg, v["ws"] - 0.004)
    wr = min(wr, wg - 0.002)
    zr = max(zr, zs + 0.004)
    zt = max(zt, zr + 0.001)
    base = zs - 0.08
    return [(0.0, base), (wg, base), (wg, zs)] + _crown(wr, zr, zt)


def _poly(half):
    h = np.array(half)
    full = np.vstack([h, (h[-2:0:-1] * [-1, 1])])
    return Polygon(full).buffer(0)


def _round(g):
    b = g.bounds
    r = min(CORNER, 0.3 * (b[3] - b[1]), 0.15 * (b[2] - b[0]))
    g = g.buffer(-r, join_style=1).buffer(r, join_style=1)
    g = g.buffer(FILLET, join_style=1).buffer(-FILLET, join_style=1)
    if g.geom_type != "Polygon":
        g = max(g.geoms, key=lambda q: q.area)
    return Polygon(g.exterior)


FLARE_R = 0.035    # the flare's edges round off this much
FLARE_EDGE = 0.18  # over this far at its front and back the flare eases out to the side


def flare(x, v):
    """The arch flare's section at x, if any: full width from the bottom up to the blister's
    edge, a circle round the wheel centre in side view."""
    out = []
    for xc, zc, r, wf in ROWS["flares"]:
        dx = abs(x - xc)
        if dx >= r - 0.005:
            continue
        top = zc + np.sqrt(r * r - dx * dx)
        k = min(1.0, (r - dx) / FLARE_EDGE)
        w = v["w"] + (wf - v["w"]) * (3 * k * k - 2 * k ** 3)
        if w <= v["w"] + 0.002 or top <= v["zb"] + 0.05:
            continue
        g = box(-w, v["zb"], w, top)
        out.append(g.buffer(-FLARE_R, join_style=1).buffer(FLARE_R, join_style=1))
    return out


@lru_cache(maxsize=4096)
def section_poly(x):
    """The section at x as a shapely polygon in (y, z)."""
    v = values(x)
    g = _poly(lower_half(v))
    fl = flare(x, v)
    if fl:
        g = unary_union([g] + fl)
    if GH0 + 0.004 < x < GH1 - 0.004:
        gh = _poly(gh_half(v))
        g = unary_union([g, gh])
    return _round(g)


def even(ring, n):
    """n points round a closed loop by arc length, starting at its lowest middle point."""
    ring = np.asarray(ring, float)
    # start exactly where the outline crosses the centre line lowest, so every ring starts at
    # the same place and neighbouring rings never twist against each other
    nxt = np.roll(ring, -1, axis=0)
    cross = np.nonzero((ring[:, 0] <= 0) & (nxt[:, 0] > 0) | (ring[:, 0] >= 0) & (nxt[:, 0] < 0))[0]
    if len(cross):
        zc = []
        for i in cross:
            t = ring[i, 0] / (ring[i, 0] - nxt[i, 0]) if ring[i, 0] != nxt[i, 0] else 0.0
            zc.append((ring[i, 1] + t * (nxt[i, 1] - ring[i, 1]), i, t))
        _z, i, t = min(zc)
        start = ring[i] + t * (nxt[i] - ring[i])
        ring = np.vstack([[start], np.roll(ring, -(i + 1), axis=0)])
    else:
        k = int(np.argmin(np.abs(ring[:, 0]) + 10 * (ring[:, 1] - ring[:, 1].min())))
        ring = np.roll(ring, -k, axis=0)
    ring = np.vstack([ring, ring[:1]])
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(ring, axis=0), axis=1))])
    t = np.linspace(0, s[-1], n, endpoint=False)
    return np.column_stack([np.interp(t, s, ring[:, 0]), np.interp(t, s, ring[:, 1])])


def section(x, n=N):
    """The ring at x, anticlockwise seen from the front, n points."""
    g = orient(section_poly(round(float(x), 5)), 1)
    return even(np.array(g.exterior.coords)[:-1], n)


def skin(step=0.014):
    """The outer skin as one closed solid, rings closer at the nose and tail."""
    n = int(np.ceil((NOSE - TAIL) / step)) + 1
    t = (1 - np.cos(np.linspace(0, np.pi, n))) / 2
    rings = []
    for x in TAIL + (NOSE - TAIL) * t:
        sec = section(x)
        rings.append(np.column_stack([np.full(len(sec), x), sec[:, 0], sec[:, 1]]))
    return close_rings(rings)


def inside(x, inset):
    """The section at x shrunk by inset, also kept inset from the sections just ahead and behind."""
    out = None
    for dx in (-inset, 0, inset):
        xx = min(max(x + dx, TAIL), NOSE)
        p = section_poly(round(float(xx), 5)).buffer(-inset)
        out = p if out is None else out.intersection(p)
    if out.geom_type != "Polygon":
        out = max(getattr(out, "geoms", [out]), key=lambda g: g.area)
    return out


def y_at(x, z):
    """Outer half width of the skin at x and height z."""
    g = section_poly(round(float(x), 5))
    hit = g.intersection(box(0, z - 1e-4, 5, z + 1e-4))
    return float(hit.bounds[2]) if not hit.is_empty else 0.0


def z_top(x, y=0.0):
    """Height of the skin's top at x, y."""
    g = section_poly(round(float(x), 5))
    hit = g.intersection(box(y - 1e-4, -1, y + 1e-4, 5))
    return float(hit.bounds[3]) if not hit.is_empty else 0.0


def cavity(x0, x1, floor, inset=0.035, n=40, tubs=()):
    """The inside of the body between x0 and x1, above floor, as a closed solid to cut away.
    tubs: wheel tubs (x, z, r, y_in) kept out of it, each a round drum outboard of y_in."""
    rings = []
    for x in np.linspace(x0, x1, n):
        inner = inside(x, inset).intersection(box(-3, floor, 3, 5))
        for xc, zc, r, y_in in tubs:
            if abs(x - xc) < r:
                top = zc + np.sqrt(r * r - (x - xc) ** 2)
                inner = inner.difference(box(y_in, -1, 3, top)).difference(box(-3, -1, -y_in, top))
        if inner.geom_type != "Polygon":
            inner = max(getattr(inner, "geoms", [inner]), key=lambda g: g.area)
        if inner.area < 1e-3:
            continue
        ring = even(np.array(orient(inner, 1).exterior.coords)[:-1], 96)
        rings.append(np.column_stack([np.full(len(ring), x), ring]))
    return close_rings(rings)
