"""Measured rows for the Archer Jet Black, based on the Mercedes SLS AMG.

Reference: Ivan's 3D model of a 2010 SLS AMG (a .glb, kept out of the repo), given for
measurements only. Simple, clean, smooth shapes come before close copies, so every outline
here is simplified and every section is smoothed.
  - body sections: the outer skin cut across at stations along the car, read as a radius at
    even angles round a centre point, so every station has the same number of points;
  - glass, lamps, grille, vents, shut lines, mirror and exhaust: outlines of the matching
    meshes, seen from the side, front, rear or top, or as 3D loops for the lamps.
The model is scaled so its wheelbase is the published 2680 mm, with each axle one published
tyre radius (265/35 R19 front, 295/30 R20 rear) off the ground. The overhangs stretch to the
published 4638 mm length; the width then sits near the published 1939 mm.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py [path/to/sls.glb]  -> rows.json
"""
import json
import sys
from pathlib import Path

import numpy as np

WHEELBASE = 2.680
LENGTH = 4.638
WIDTH = 1.939
TYRE_F = round((19 * 25.4 + 2 * 0.35 * 265) / 2000, 4)     # 265/35 R19
TYRE_R = round((20 * 25.4 + 2 * 0.30 * 295) / 2000, 4)     # 295/30 R20
RIM_F = round(19 * 25.4 / 2000, 4)
RIM_R = round(20 * 25.4 / 2000, 4)
GLB = "/mnt/project-files/uploads/ref/mercedes_sls_amg.glb"
ANGLES = 73                    # points round each half section, bottom centre to top centre
STEP = 0.03                    # station spacing along the car

# The model's meshes, by material (one mesh holds every panel of one material), and the
# pieces of each that make the outer shell, by their rank in size.
TYRES = ["tire"]
PAINT = ["paint"]
SHELL = [("paint", 0, 1, 2, 3, 4, 5, 8, 9, 10, 11), ("glasses", 0, 1, 2, 3), ("grill_2", 0, 3), ("black", 4)]


# ---------------------------------------------------------------- loading
def load(path):
    import bpy
    import bmesh
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    out = {}
    for o in bpy.context.scene.objects:
        if o.type != "MESH":
            continue
        bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        tris = np.array([[v.co[:] for v in f.verts] for f in bm.faces])
        name = o.data.materials[0].name if o.data.materials else o.name
        out[name] = np.vstack([out[name], tris]) if name in out else tris
        bm.free()
    return out


class Space:
    """Model coordinates (X to the car's left, Y back, Z up) to build space. The overhangs
    stretch a touch so the length is the published one."""

    def __init__(self, m):
        tv = m[TYRES[0]].reshape(-1, 3)
        mid = (tv.min(0) + tv.max(0)) / 2
        c = []
        for sx in (-1, 1):
            for sy in (-1, 1):
                q = tv[(np.sign(tv[:, 0] - mid[0]) == sx) & (np.sign(tv[:, 1] - mid[1]) == sy)]
                c.append((q.min(0) + q.max(0)) / 2)
        c = np.array(c)
        yf, yr = c[c[:, 1] < 0, 1].mean(), c[c[:, 1] > 0, 1].mean()
        self.s = WHEELBASE / (yr - yf)
        self.ymid = (yf + yr) / 2
        self.zc = c[c[:, 1] < 0, 2].mean()          # the front axle sits one front tyre radius up
        tyre = np.vstack([m[k].reshape(-1, 3) for k in TYRES])
        self.outer_y = float(np.abs(tyre[:, 0]).max() * self.s)
        paint = np.vstack([m[k].reshape(-1, 3) for k in PAINT])
        x = -(paint[:, 1] - self.ymid) * self.s
        self.nose, self.tail = float(x.max()), float(x.min())
        over = LENGTH - (self.nose - self.tail)
        f_over, r_over = self.nose - WHEELBASE / 2, -WHEELBASE / 2 - self.tail
        self.kf = 1 + over * f_over / (f_over + r_over) / f_over
        self.kr = 1 + over * r_over / (f_over + r_over) / r_over

    def __call__(self, p):
        p = np.asarray(p, float)
        x = -(p[..., 1] - self.ymid) * self.s
        h = WHEELBASE / 2
        x = np.where(x > h, h + (x - h) * self.kf, np.where(x < -h, -h + (x + h) * self.kr, x))
        return np.stack([x, p[..., 0] * self.s, (p[..., 2] - self.zc) * self.s + TYRE_F], -1)


# ---------------------------------------------------------------- 2D helpers
def r4(a):
    return np.round(np.asarray(a, float), 4).tolist()


def project(tris, a, b):
    """Union of triangles seen along the third axis, as a shapely shape in (axis a, axis b)."""
    from shapely.geometry import Polygon
    from shapely.ops import unary_union
    polys = [Polygon(t[:, [a, b]]) for t in tris]
    return unary_union([p.buffer(1e-4) for p in polys if p.area > 1e-9])


def biggest(g):
    from shapely.geometry import Polygon
    if g.geom_type != "Polygon":
        g = max(g.geoms, key=lambda p: p.area)
    return Polygon(g.exterior)


def plain(g, r=0.03, tol=0.004):
    """A simple smooth outline: holes filled, small kinks rounded off, few points."""
    g = biggest(g).buffer(r, join_style=1).buffer(-2 * r, join_style=1).buffer(r, join_style=1)
    g = biggest(g).simplify(tol)
    return np.array(g.exterior.coords)[:-1]


def comps(tris):
    """Connected pieces of a triangle soup."""
    v = tris.reshape(-1, 3)
    key = {}
    idx = np.array([key.setdefault(tuple(np.round(p, 6)), len(key)) for p in v]).reshape(-1, 3)
    par = list(range(len(key)))

    def f(a):
        while par[a] != a:
            par[a] = par[par[a]]; a = par[a]
        return a
    for a, b, c in idx:
        par[f(a)] = f(b); par[f(b)] = f(c)
    roots = np.array([f(i[0]) for i in idx])
    return [tris[roots == r] for r in np.unique(roots)]


def piece(tris, lo, hi):
    """The connected piece whose bounds lie inside lo..hi (build space)."""
    for c in comps(tris):
        v = c.reshape(-1, 3)
        if np.all(v.min(0) > np.asarray(lo) - 1e-3) and np.all(v.max(0) < np.asarray(hi) + 1e-3):
            yield c


def window_line(panes):
    """One smooth window opening round the side panes: a straight belt line under them and a
    smooth top over them, meeting at the front and rear tips."""
    from scipy.ndimage import gaussian_filter1d
    from shapely.geometry import LineString, Polygon
    from shapely.ops import unary_union
    u = unary_union(panes)
    x0, x1 = u.bounds[0], u.bounds[2]
    xs = np.linspace(x0 + 0.002, x1 - 0.002, 200)
    lo, hi = [], []
    for x in xs:
        cut = u.intersection(LineString([(x, -5), (x, 5)]))
        b = cut.bounds if not cut.is_empty else (x, np.nan, x, np.nan)
        lo.append(b[1]); hi.append(b[3])
    lo, hi = np.array(lo), np.array(hi)
    ok = ~np.isnan(hi)
    hi = np.interp(xs, xs[ok], hi[ok]); lo = np.interp(xs, xs[ok], lo[ok])
    mid = (xs > x0 + 0.25) & (xs < x1 - 0.12)
    k = np.polyfit(xs[mid], lo[mid], 1)
    belt = np.polyval(k, xs)
    hi = gaussian_filter1d(hi, 8, mode="nearest")
    hi = np.maximum(hi, belt)
    top = np.column_stack([xs, hi])[::-1]
    bottom = np.column_stack([xs, belt])
    return Polygon(np.vstack([bottom, top])).buffer(0).simplify(0.002), k


def bridge(a, b):
    """The window opening across the pillar between pane a (behind) and pane b (ahead)."""
    from shapely.geometry import box
    x0, x1 = a.bounds[2] - 0.01, b.bounds[0] + 0.01
    strip = lambda g, x: g.intersection(box(x - 0.08, -5, x + 0.08, 5)).bounds
    za, zb = strip(a, a.bounds[2]), strip(b, b.bounds[0])
    return box(x0, max(za[1], zb[1]), x1, min(za[3], zb[3]) - 0.01)


# ---------------------------------------------------------------- body sections
def slice_x(tris, x):
    """Segments where the plane at x cuts the triangles, in (y, z)."""
    d = tris[:, :, 0] - x
    sgn = np.sign(d)
    cut = (sgn.min(1) < 0) & (sgn.max(1) > 0)
    t = tris[cut]; dd = d[cut]
    pts = []
    for i, j in ((0, 1), (1, 2), (2, 0)):
        a, b = dd[:, i], dd[:, j]
        m = (a * b) < 0
        k = np.where(m, a / np.where(m, a - b, 1), 0)[:, None]
        p = t[:, i, 1:] + (t[:, j, 1:] - t[:, i, 1:]) * k
        pts.append(np.where(m[:, None], p, np.nan))
    pts = np.stack(pts, 1)
    segs = []
    for row in pts:
        ok = row[~np.isnan(row[:, 0])]
        if len(ok) >= 2:
            segs.append(ok[:2])
    return np.array(segs) if segs else np.zeros((0, 2, 2))


def polar(segs, zc, th):
    """Farthest hit along each ray from (0, zc) at angles th, on the half y >= 0."""
    s = segs.copy(); s[:, :, 0] = np.abs(s[:, :, 0])
    p, q = s[:, 0], s[:, 1]
    e = q - p
    r = np.full(len(th), np.nan)
    o = np.array([0.0, zc])
    w = p - o
    for i, a in enumerate(th):
        d = np.array([np.cos(a), np.sin(a)])
        den = d[0] * e[:, 1] - d[1] * e[:, 0]
        ok = np.abs(den) > 1e-12
        t = np.where(ok, (w[:, 0] * e[:, 1] - w[:, 1] * e[:, 0]) / np.where(ok, den, 1), -1)
        u = np.where(ok, (w[:, 0] * d[1] - w[:, 1] * d[0]) / np.where(ok, den, 1), -1)
        hit = ok & (t > 0) & (u >= 0) & (u <= 1)
        if hit.any():
            r[i] = t[hit].max()
    return r


def pick(m, name, *idx, sp=None):
    """Pieces of one material's mesh in build space, by their rank in size (biggest first)."""
    cs = sorted(comps(sp(m[name])), key=lambda c: -len(c))
    return np.vstack([cs[i] for i in idx])


def shell_tris(sp, m):
    """The painted skin and the glass: no mirrors, no airbrake (it is built on its own)."""
    return np.vstack([pick(m, *k, sp=sp) for k in SHELL])


def sections(sp, m, arches):
    from scipy.ndimage import gaussian_filter, median_filter
    tris = shell_tris(sp, m)
    xs_all = tris[:, :, 0]
    nose, tail = xs_all.max(), xs_all.min()
    xs = np.concatenate([[nose - 0.004, nose - 0.012], np.arange(nose - 0.025, tail + 0.02, -STEP),
                         [tail + 0.012, tail + 0.004]])
    th = np.linspace(-np.pi / 2, np.pi / 2, ANGLES)
    in_arch = lambda x: any(abs(x - wx) < ar + 0.012 for wx, _wz, ar in arches)
    cuts = [(x, slice_x(tris, x)) for x in xs]
    cuts = [(x, sg) for x, sg in cuts if len(sg) >= 3]
    xs = np.array([x for x, _ in cuts])
    zlo = np.array([sg[:, :, 1].min() for _, sg in cuts])
    zhi = np.array([sg[:, :, 1].max() for _, sg in cuts])
    ok = np.array([not in_arch(x) for x in xs])
    zlo = np.interp(-xs, -xs[ok], zlo[ok])              # the arches have no floor of their own
    zcs = (zlo + zhi) / 2
    rows = []
    for x, (_x, segs), zc, lo in zip(xs, cuts, zcs, zlo):
        r = polar(segs, zc, np.clip(th, -np.pi / 2 + 0.01, np.pi / 2 - 0.01))
        if in_arch(x):
            top = next(wz + ar for w, wz, ar in arches if abs(x - w) < ar + 0.012)
            zz = zc + r * np.sin(th)
            r[(th < 0) & (np.isnan(r) | (zz < top))] = -1
        miss = np.isnan(r) & (th < -0.05)                 # a floor where the model has none
        wmax = np.nanmax(np.where(r > 0, r * np.cos(th), np.nan)) if np.any(r > 0) else 0.5
        r[miss] = np.minimum((lo - zc) / np.sin(th[miss]), wmax / np.cos(th[miss]))   # no wider than the section
        rows.append([x, zc, r])
    X = np.array([row[0] for row in rows]); ZC = np.array([row[1] for row in rows])
    Rm = np.array([row[2] for row in rows])
    Rm[Rm < 0] = np.nan
    for j in range(ANGLES):                       # fill across the arches and any gaps
        col = Rm[:, j]; good = ~np.isnan(col)
        if good.sum() >= 2:
            Rm[:, j] = np.interp(X[::-1], X[good][::-1], col[good][::-1])[::-1]
    # the top and bottom centre lie flat: no spike or notch where the halves meet
    k = 6
    for j in list(range(k)) + list(range(ANGLES - k, ANGLES)):
        ref = k if j < k else ANGLES - 1 - k
        z = ZC + Rm[:, ref] * np.sin(th[ref])
        Rm[:, j] = (z - ZC) / np.sin(th[j])
    Rm = median_filter(Rm, size=(3, 5), mode="nearest")
    Rm = gaussian_filter(Rm, sigma=(1.6, 1.8), mode="nearest")      # smooth simple panels
    ZC = median_filter(ZC, size=5, mode="nearest")
    return [dict(x=round(float(x), 4), zc=round(float(zc), 4), r=r4(r)) for x, zc, r in zip(X, ZC, Rm)]


NOSE_FROM = 2.02     # ahead of here the nose is rebuilt plain, over the grille
NOSE_LIFT = 0.03     # its bottom rises this much by the tip


def plain_nose(stations, xf=NOSE_FROM):
    """The model's grille and its star stand out of the nose and would leave a knob in its
    sections. Ahead of xf each section is the section at xf stretched to a smooth top line, a
    near level bottom and the measured plan width, so the nose runs down to one blunt face."""
    from scipy.ndimage import gaussian_filter1d
    th = np.linspace(-np.pi / 2, np.pi / 2, ANGLES)
    st = sorted(stations, key=lambda t: t["x"])
    X = np.array([t["x"] for t in st])
    halves = [np.column_stack([np.asarray(t["r"]) * np.cos(th), t["zc"] + np.asarray(t["r"]) * np.sin(th)]) for t in st]
    i0 = int(np.argmin(np.abs(X - xf)))
    ref = halves[i0]
    t0, b0, w0 = ref[:, 1].max(), ref[:, 1].min(), ref[:, 0].max()
    xn = X.max()
    tops = np.array([h[:, 1].max() for h in halves])
    near = X > xn - 0.03
    t1 = float(tops[near].mean())
    wid = gaussian_filter1d(np.array([h[:, 0].max() for h in halves]), 2, mode="nearest")
    lw = w0
    for i in range(i0, len(X)):
        t = max(0.0, (X[i] - X[i0]) / (xn - X[i0]))
        lt = t0 + (t1 - t0) * t ** 1.6                  # one even curve down to the tip
        lb = b0 + NOSE_LIFT * t ** 3
        lw = min(lw, wid[i])
        h = np.column_stack([ref[:, 0] * lw / w0, lb + (ref[:, 1] - b0) * (lt - lb) / (t0 - b0)])
        zc = (lt + lb) / 2
        full = np.vstack([h, (h * [-1, 1])[::-1]])
        segs = np.stack([full[:-1], full[1:]], 1)
        r = polar(segs, zc, np.clip(th, -np.pi / 2 + 0.01, np.pi / 2 - 0.01))
        r = np.where(np.isnan(r), np.nanmax(r), r)
        st[i]["zc"], st[i]["r"] = round(float(zc), 4), r4(r)
    return stations


TAIL_FROM = -1.95    # behind here the tail's underside is rebuilt plain
TAIL_RISE = 0.07     # and rises this much by the end, so the bumper carries the diffuser
TAIL_CUT = 0.09      # the measured tail's last stretch, tapering to a point, is dropped


def plain_tail(stations, xr=TAIL_FROM):
    """The model's tail has no floor of its own under the bumper, and its last few centimetres
    taper to a point round a plate recess. Behind xr each section's lower half is stretched down
    to an even underside, rising gently to the end. The pointed last stretch is dropped and the
    rear overhang stretched back to its full length, so the tail ends in one blunt face."""
    th = np.linspace(-np.pi / 2, np.pi / 2, ANGLES)
    st = sorted(stations, key=lambda t: t["x"])
    X = np.array([t["x"] for t in st])
    i0 = int(np.argmin(np.abs(X - xr)))
    half = lambda t: np.column_stack([np.asarray(t["r"]) * np.cos(th), t["zc"] + np.asarray(t["r"]) * np.sin(th)])
    b0 = half(st[i0])[:, 1].min()
    xt = X.min()
    for i in range(0, i0):
        h = half(st[i])
        zc, lo = st[i]["zc"], h[:, 1].min()
        t = (xr - X[i]) / (xr - xt)
        target = min(lo, b0 + TAIL_RISE * t ** 2)
        if target >= lo - 1e-4 or zc - lo < 1e-3:
            continue
        k = (zc - target) / (zc - lo)
        low = h[:, 1] < zc
        h[low, 1] = zc - (zc - h[low, 1]) * k
        full = np.vstack([h, (h * [-1, 1])[::-1]])
        segs = np.stack([full[:-1], full[1:]], 1)
        r = polar(segs, zc, np.clip(th, -np.pi / 2 + 0.01, np.pi / 2 - 0.01))
        st[i]["r"] = r4(np.where(np.isnan(r), np.nanmax(r), r))
    keep = [t for t in st if t["x"] >= xt + TAIL_CUT]
    xa = -WHEELBASE / 2
    k = (xt - xa) / (keep[0]["x"] - xa)
    for t in keep:
        if t["x"] < xa:
            t["x"] = round(xa + (t["x"] - xa) * k, 4)
    return sorted(keep, key=lambda t: -t["x"])

def arches(sp, m, wheels):
    """Arch radius round each wheel centre (x, z), from the body paint at the sides."""
    v = sp(np.vstack([m[k] for k in PAINT])).reshape(-1, 3)
    out = []
    for wx, wz in wheels:
        near = v[(np.abs(v[:, 1]) > 0.7) & (np.abs(v[:, 0] - wx) < 0.6) & (v[:, 2] < wz + 0.6)]
        d = np.hypot(near[:, 0] - wx, near[:, 2] - wz)
        out.append((wx, wz, round(float(np.percentile(d, 0.5)), 4)))
    return out


# ---------------------------------------------------------------- the rows
def outline(tris, a, b, r=0.02, tol=0.003):
    return plain(project(tris, a, b), r, tol)


def disc(tris):
    """Centre and radius of a round part: [x, y, z, r]."""
    v = tris.reshape(-1, 3)
    c = (v.min(0) + v.max(0)) / 2
    return r4(list(c) + [float(max(np.ptp(v[:, 1]), np.ptp(v[:, 2])) / 2)])


def open_edges(tris):
    """Edges used by one triangle only: the panel's own borders, so its shut lines and openings."""
    from collections import Counter
    key = lambda p: tuple(np.round(p, 5))
    e = Counter()
    for t in tris:
        for i, j in ((0, 1), (1, 2), (2, 0)):
            e[tuple(sorted((key(t[i]), key(t[j]))))] += 1
    return np.array([ab for ab, n in e.items() if n == 1])


def faces(edges, a, b):
    """The flat regions the edges enclose, seen along the third axis, biggest first."""
    from shapely.geometry import MultiLineString
    from shapely.ops import polygonize, unary_union
    lines = unary_union(MultiLineString([e[:, [a, b]] for e in edges]))
    return sorted(polygonize(lines), key=lambda p: -p.area)


def find(polys, x, y):
    """The smallest face holding the point (x, y)."""
    from shapely.geometry import Point
    hits = [p for p in polys if p.contains(Point(x, y))]
    return min(hits, key=lambda p: p.area)


def piece_at(g, x, y):
    from shapely.geometry import Point
    for q in getattr(g, "geoms", [g]):
        if q.contains(Point(x, y)):
            return q
    raise ValueError("no piece there")


def ring(g, tol=0.002):
    return r4(np.array(biggest(g).simplify(tol).exterior.coords)[:-1])


def measure(path):
    m = load(path)
    sp = Space(m)
    P = lambda name, *i: pick(m, name, *i, sp=sp)
    left = lambda t: t[t[:, :, 1].mean(1) > 0]
    wheels = [(WHEELBASE / 2, TYRE_F), (-WHEELBASE / 2, TYRE_R)]
    arch = arches(sp, m, wheels)
    stations = plain_tail(plain_nose(sections(sp, m, arch)))

    # glass: the door window (left), the windscreen, the rear window
    door_glass = biggest(project(P("glasses", 3), 0, 2))
    screen = P("glasses", 1)
    so = plain(project(screen, 0, 1).buffer(-0.004), 0.02)
    sv = screen.reshape(-1, 3)
    sc = sv[np.abs(sv[:, 1]) < 0.1]
    back = P("glasses", 0)
    bo = plain(project(back, 0, 1).buffer(-0.004), 0.02)
    bv = back.reshape(-1, 3)
    bc = bv[np.abs(bv[:, 1]) < 0.1]

    # the panels: the left door seen from the side (the reference's gull wing door, kept below
    # the window), the bonnet and the boot lid from above
    door = project(P("paint", 4), 0, 2)
    bonnet = project(P("paint", 8), 0, 1)
    boot = project(P("paint", 5), 0, 1)

    # front: the grille and its blade, the outer intakes and the low slot, the head lamps in 3D
    grille = project(P("grill_2", 3), 1, 2).union(project(P("grill_2", 0), 1, 2))
    blade = project(P("grill_2", 7), 1, 2)
    intake = project(P("grill_2", 1), 1, 2)
    slot = project(P("grill_2", 4, 6), 1, 2)
    head = P("tail_gls", 3)
    badge_f = disc(P("chrome", 5))
    nose_star = disc(P("chrome", 6))

    # side: the vent behind the front wheel and its fins; the bonnet vents from above
    vent = project(P("black", 0), 0, 2)
    hood_vent = project(P("grill", 5), 0, 1)

    # rear: the tail lamps in 3D, the exhaust tips (left pair), the star, the spoiler in plan
    tail = P("tail_gls", 1)
    tips = project(P("chrome", 2), 1, 2)
    badge_r = disc(P("chrome", 0))
    spoiler = project(P("paint", 9), 0, 1)
    third = sp(m["break"]).reshape(-1, 3)

    mv = left(P("paint", 7)).reshape(-1, 3)
    mirror = {"x_back": round(float(mv[:, 0].min()), 4), "x_front": round(float(mv[:, 0].max()), 4),
              "y_in": round(float(mv[:, 1].min()), 4), "y_out": round(float(mv[:, 1].max()), 4),
              "z0": round(float(mv[:, 2].min()), 4), "z1": round(float(mv[:, 2].max()), 4)}

    return {
        "name": "ArcherJetBlack",
        "wheel": {"r_f": TYRE_F, "r_r": TYRE_R, "rim_f": RIM_F, "rim_r": RIM_R, "width_f": 0.265,
                  "width_r": 0.295, "outer_y": round(sp.outer_y, 4), "front_x": wheels[0][0],
                  "rear_x": wheels[1][0], "arch_f": arch[0][2], "arch_r": arch[1][2]},
        "stations": stations,
        "door_glass": ring(door_glass),
        "screen": {"outline": r4(so), "x_base": round(float(sc[:, 0].max()), 4),
                   "x_top": round(float(sc[:, 0].min()), 4)},
        "rear_glass": {"outline": r4(bo), "x_base": round(float(bc[:, 0].min()), 4),
                       "x_top": round(float(bc[:, 0].max()), 4)},
        "door": ring(door), "bonnet": ring(bonnet), "boot": ring(boot),
        "grille": ring(grille), "blade": ring(blade), "intake": ring(intake), "slot": ring(slot),
        "head": r4(head), "badge_front": badge_f, "nose_star": nose_star,
        "vent": ring(vent), "vent_fin": ring(project(P("chrome", 12), 0, 2)),
        "hood_vent": ring(hood_vent),
        "tail": r4(tail), "exhaust": ring(tips), "badge_rear": badge_r, "spoiler": ring(spoiler),
        "third_brake": r4([third[:, 0].mean(), third[:, 1].min(), third[:, 1].max(), third[:, 2].min(), third[:, 2].max()]),
        "mirror": mirror,
        "picks": {"frontbadge": 1, "rearbadge": 1, "name": 1},
    }

if __name__ == "__main__":
    rows = measure(sys.argv[1] if len(sys.argv) > 1 else GLB)
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(rows, indent=1))
    st = rows["stations"]
    print("wrote", out, len(st), "stations, nose", st[0]["x"], "tail", st[-1]["x"], rows["wheel"])
