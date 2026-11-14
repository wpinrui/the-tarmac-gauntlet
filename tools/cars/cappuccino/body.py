"""Kaiser Cappuccino outer skin, lofted from rows.json in pure numpy, and the 2D outlines the
skin is cut along (windows, doors, bonnet, hatch, arches). Build space: metres, x forward from
mid wheelbase, y left, z up."""
import json
from pathlib import Path

import numpy as np
from shapely.geometry import Polygon

import sys
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "common"))
from geom import catmull_closed, monotone  # noqa: E402

R = json.loads(Path(__file__).with_name("rows.json").read_text())
ORDER = ["keel", "side", "shoulder", "belt", "cant"]
N_RING = 128
CURVES = {name: {k: monotone(v) for k, v in row.items()} for name, row in R["rows"].items()}
X_FRONT = max(p[0] for p in R["rows"]["crown"]["z"])
X_REAR = min(p[0] for p in R["rows"]["crown"]["z"])


def stations(n=260):
    """Stations from nose to tail, packed close at both ends so the rounded ends stay smooth."""
    t = np.linspace(0, 1, n)
    t = 0.5 - 0.5 * np.cos(np.pi * t)
    t = 0.6 * t + 0.4 * np.linspace(0, 1, n)
    return X_FRONT + (X_REAR - X_FRONT) * t


def section(x):
    """Closed ring of the section at x, from the bottom centre round the left side and back."""
    c = {k: (float(CURVES[k]["w"](x)), float(CURVES[k]["z"](x))) for k in ORDER}
    zt = float(CURVES["crown"]["z"](x))
    zk = c["keel"][1]
    half = [(0.0, zk)] + [c[k] for k in ORDER] + [(0.0, zt)]
    left = [(w, z) for w, z in half]
    right = [(-w, z) for w, z in half[-2:0:-1]]
    pts = np.array(left + right)
    loop = catmull_closed(pts, 12)
    # even spacing round the ring, starting at the bottom centre
    d = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(np.vstack([loop, loop[:1]]), axis=0), axis=1))])
    t = np.linspace(0, d[-1], N_RING, endpoint=False)
    closed = np.vstack([loop, loop[:1]])
    return np.column_stack([np.interp(t, d, closed[:, 0]), np.interp(t, d, closed[:, 1])])


def skin():
    """Rings of the outer skin: list of (N_RING, 3) arrays, nose first."""
    rings = []
    for x in stations():
        s = section(x)
        rings.append(np.column_stack([np.full(len(s), x), s[:, 0], s[:, 1]]))
    return rings


def inner_skin(x_front, x_rear, d, n=120):
    """Rings of the skin pushed in by d, between two stations: the hollow behind the panels."""
    rings = []
    for x in np.linspace(x_front, x_rear, n):
        p = Polygon(section(x)).buffer(-d, join_style=1)
        if p.geom_type != "Polygon":
            p = max(p.geoms, key=lambda g: g.area)
        loop = np.array(p.exterior.coords)[:-1]
        if Polygon(loop).exterior.is_ccw != Polygon(section(x)).exterior.is_ccw:
            loop = loop[::-1]
        start = int(np.argmin(np.abs(loop[:, 0]) + 10 * (loop[:, 1] > loop[:, 1].mean())))
        loop = np.roll(loop, -start, axis=0)
        closed = np.vstack([loop, loop[:1]])
        dd = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(closed, axis=0), axis=1))])
        t = np.linspace(0, dd[-1], N_RING, endpoint=False)
        r = np.column_stack([np.interp(t, dd, closed[:, 0]), np.interp(t, dd, closed[:, 1])])
        rings.append(np.column_stack([np.full(N_RING, x), r]))
    return rings


# ---------------------------------------------------------------- outlines

def even_curve(pts, deg=2):
    """One even curve through traced points (a low order fit), for a window's long edge."""
    p = np.asarray(pts, float)
    k = np.polyfit(p[:, 0], p[:, 1], deg)
    xs = np.linspace(p[:, 0].min(), p[:, 0].max(), 40)
    return np.column_stack([xs, np.polyval(k, xs)])


def side_windows():
    """Door glass and quarter glass outlines on the side (x, z). Each long edge is one even
    curve, each short side a straight line."""
    d = np.array(R["dlo"])
    i_rear = int(np.argmin(d[:, 0]))
    up = even_curve(d[1: i_rear], 3)
    lo = even_curve(np.vstack([d[i_rear + 1:], d[:1]]), 2)
    fz = lambda c, x: float(np.interp(x, c[:, 0], c[:, 1]))
    x_tip, z_tip = float(d[0, 0]), float(d[0, 1])
    x_end = max(up[:, 0].min(), lo[:, 0].min())
    bf, br = R["b_pillar"]          # front and rear edges of the B pillar (bf > br)

    def piece(x0, x1, tip=None):
        top = [(x, fz(up, x)) for x in np.linspace(x0, x1, 40) if fz(up, x) > fz(lo, x) + 0.03]
        bot = [(x, fz(lo, x)) for x in np.linspace(top[-1][0] if tip else x1, x0, 40)]
        return Polygon(top + ([tip] if tip else []) + bot).buffer(0)

    return {"door": piece(bf, x_tip - 0.04, (x_tip, z_tip)), "quarter": piece(x_end + 0.01, br)}


def door_outline():
    """Door shut line on the side (x, z): front edge, along the bottom, rear edge, and over
    the top along the middle of the A pillar, the roof edge and the B pillar."""
    f, r = R["door"], R["door_rear"]
    zc = CURVES["cant"]["z"]
    bf, br = R["b_pillar"]
    top = [(x, float(zc(x)) - 0.018) for x in np.linspace(f[-1][0], (bf + br) / 2, 50)]
    return Polygon([tuple(p) for p in f] + top + [tuple(p) for p in r]).buffer(0)


def arch(axle_x, n=18):
    """Wheel arch opening on the side: an arc stepping round the wheel centre at even angles,
    closed below the body."""
    r = R["arch_r"]
    zc = R["axles"]["z"]
    a = np.linspace(np.radians(-12), np.radians(192), n)
    pts = [(axle_x + r * np.cos(t), zc + r * np.sin(t)) for t in a]
    pts += [(pts[-1][0], -0.2), (pts[0][0], -0.2)]
    return Polygon(pts)


if __name__ == "__main__":
    rings = skin()
    print("rings", len(rings), "x", X_FRONT, X_REAR)
    print("door", door_outline().bounds)
    print({k: v.bounds for k, v in side_windows().items()})
