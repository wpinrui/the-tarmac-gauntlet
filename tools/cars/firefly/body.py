"""The Firefly's outer skin, lofted from the traced rows in rows.json, in pure numpy.

Each station holds the values of a narrow rounded hull section: the bottom, the widest point, the
shoulder (the bonnet's or the boot lid's edge, or the belt) and the deck across the top. Over the
cabin the domed greenhouse stands on the shoulder: its glass foot, the roof edge and the crown at
the centre. Along each wing a round wing section is joined to the hull's: from its bottom out to
its widest point, over on an even curve to its crown and in to the hull, so the wing's edge is a
crisp step onto the door. Every value becomes a smooth curve along the car (monotone cubic); at
each x the section is drawn, its corners rounded, and the outline resampled evenly so every ring
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
LOW_KEYS = ("zb", "w", "zw", "tuck", "zs", "ws", "zd")
GH_KEYS = ("zt", "zr", "wr", "wg")
WING_KEYS = ("zt", "w", "wc", "zw", "zb", "tuck")
GH0, GH1 = ROWS["gh"]
N = 224            # points round each ring
CORNER = 0.07      # convex corners round off this much
FILLET = 0.045     # and the hollows where wing, hull and greenhouse meet this much
WING_ROUND = 2.0   # how full the wing's curve from its widest point to its crown is (2 = an ellipse)


def _curves():
    cv = {k: monotone([(s["x"], s[k]) for s in ST]) for k in LOW_KEYS}
    gh = [s for s in ST if "zt" in s]
    cv.update({k: monotone([(s["x"], s[k]) for s in gh]) for k in GH_KEYS})
    return cv


CV = _curves()
WINGS = []
for rows in ROWS["wings"]:
    rows = sorted(rows, key=lambda s: s["x"])
    WINGS.append((rows[0]["x"], rows[-1]["x"], {k: monotone([(s["x"], s[k]) for s in rows]) for k in WING_KEYS}))
NOSE, TAIL = ST[-1]["x"], ST[0]["x"]


def values(x):
    return {k: float(f(x)) for k, f in CV.items()}


def _crown(w, z_edge, z_mid, n=14):
    """Points from (w, z_edge) across to the centre (0, z_mid) on an even parabola."""
    t = np.linspace(0, 1, n)
    return [(w * (1 - u), z_edge + (z_mid - z_edge) * (1 - (1 - u) ** 2)) for u in t]


def lower_half(v):
    zb, w, zw, tk, zs, ws, zd = (v[k] for k in LOW_KEYS)
    h = max(zs - zb, 0.01)
    zs = zb + h
    zw = min(max(zw, zb + min(0.06, 0.3 * h)), zs - min(0.06, 0.3 * h))
    ws = min(ws, w - 0.005)
    knee = (w - 0.25 * (w - ws), zw + 0.7 * (zs - zw))      # the side stays full, then rolls in
    pts = [(0.0, zb), (w - tk, zb), (w - 0.35 * tk, zb + 0.5 * (zw - zb)), (w, zw), knee, (ws, zs)]
    inner = ws - 0.02
    lip = zs + min(max(zd - zs, 0.0), 0.004)
    pts += [(inner, lip)] + _crown(inner, lip, max(zd, lip))[1:]
    return pts


def gh_half(v):
    """The greenhouse: the side glass leaning straight in from its foot to the roof edge, and the
    roof carried on over to the crown on one even curve, leaving the glass at the glass's own lean
    and meeting the crown level, so the dome has no crease at the roof edge."""
    zs, wg, wr, zr, zt = v["zs"], v["wg"], v["wr"], v["zr"], v["zt"]
    wg = min(wg, v["ws"] - 0.004)
    wr = min(wr, wg - 0.002)
    zr = max(zr, zs + 0.004)
    zt = max(zt, zr + 0.001)
    base = zs - 0.08
    u1 = wr - (wg - wr) * (zt - zr) / max(zr - zs, 1e-3)    # the glass's line, carried up to the crown
    u1 = min(max(u1, 0.15 * wr), 0.85 * wr)
    t = np.linspace(0, 1, 20)[:, None]
    roof = (1 - t) ** 2 * np.array([wr, zr]) + 2 * (1 - t) * t * np.array([u1, zt]) + t * t * np.array([0.0, zt])
    return [(0.0, base), (wg, base), (wg, zs)] + [tuple(p) for p in roof]


def wing_half(x, v):
    """The wing's section at x on the car's left, or None where it has ended."""
    for x0, x1, cv in WINGS:
        if not x0 <= x <= x1:
            continue
        zt, w, wc, zw, zb, tk = (float(cv[k](x)) for k in WING_KEYS)
        if zt - zb < 0.03 or w <= wc + 0.01:
            return None
        zw = min(max(zw, zb + 0.4 * (zt - zb) * 0.5), zt - 0.015)
        tk = min(tk, 0.5 * (w - wc))
        inner = min(v["ws"], wc) - 0.03
        z_in = min(v["zs"], zt) - 0.03
        pts = [(inner, zb), (w - tk, zb), (w - 0.4 * tk, zb + 0.45 * (zw - zb)), (w, zw)]
        for a in np.linspace(0, np.pi / 2, 16)[1:]:
            c, s = np.cos(a), np.sin(a)
            pts.append((wc + (w - wc) * c ** (2 / WING_ROUND), zw + (zt - zw) * s ** (2 / WING_ROUND)))
        pts += [(inner, z_in)]
        return Polygon(pts).buffer(0)
    return None


def _poly(half):
    h = np.array(half)
    full = np.vstack([h, (h[-2:0:-1] * [-1, 1])])
    return Polygon(full).buffer(0)


def _mirror(g):
    from shapely.affinity import scale
    return scale(g, -1, 1, origin=(0, 0))


def _round(g):
    b = g.bounds
    r = min(CORNER, 0.3 * (b[3] - b[1]), 0.15 * (b[2] - b[0]))
    g = g.buffer(-r, join_style=1).buffer(r, join_style=1)
    g = g.buffer(FILLET, join_style=1).buffer(-FILLET, join_style=1)
    if g.geom_type != "Polygon":
        g = max(g.geoms, key=lambda q: q.area)
    return Polygon(g.exterior)


@lru_cache(maxsize=4096)
def section_poly(x):
    """The section at x as a shapely polygon in (y, z)."""
    v = values(x)
    parts = [_poly(lower_half(v))]
    if GH0 + 0.004 < x < GH1 - 0.004:
        parts.append(_poly(gh_half(v)))
    wg = wing_half(x, v)
    if wg is not None:
        parts += [wg, _mirror(wg)]
    return _round(unary_union(parts))


def even(ring, n):
    """n points round a closed loop by arc length, starting exactly where its underside crosses
    the centre line, so every ring starts at the same place and the loft never twists."""
    ring = np.asarray(ring, float)
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


def skin(step=0.012):
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
