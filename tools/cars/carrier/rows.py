"""Measured rows for the Nusa Carrier, based on the 1992 Proton Saga saloon.

Reference: Ivan's 3D model of a first generation Saga saloon (a .glb, kept out of the repo),
given for measurements only. Ivan wants smooth, simple shapes over close copies, so every
outline here is simplified and every section is smoothed hard.
  - body sections: the outer skin cut across at stations along the car, read as a radius at
    even angles round a centre point, so every station has the same number of points;
  - glass, lamps, grille, strip, shut lines, mirror and the option spots: outlines of the
    matching meshes, seen from the side, front, rear or top, or as 3D triangles for the lamps.
The model runs long for its width and height, so each axis is scaled on its own: along the
car so the wheelbase is the published 2380 mm, with the overhangs stretched to the published
4280 mm length; across so the body is the published 1655 mm wide; up so the roof stands at
the published 1360 mm with the wheels one published tyre radius (175/70 R13) off the ground.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py [path/to/saga.glb]  -> rows.json
"""
import json
import sys
from pathlib import Path

import numpy as np

WHEELBASE = 2.380
LENGTH = 4.280
WIDTH = 1.655
HEIGHT = 1.360
TYRE_R = round((13 * 25.4 + 2 * 0.70 * 175) / 2000, 4)      # 175/70 R13
RIM_R = round(13 * 25.4 / 2000, 4)
TYRE_W = 0.175
GLB = "/mnt/user-data/uploads/hearth/a5dbcce8-02e0-463a-917e-fdae60831337/a5dbcce8-02e0-463a-917e-fdae60831337.glb"
ANGLES = 73                    # points round each half section, bottom centre to top centre
STEP = 0.03                    # station spacing along the car

TYRES = ["wheel_boot_ok_4_0", "wheel_1_boot_ok_4_0", "wheel_2_boot_ok_4_0", "wheel_3_boot_ok_4_0"]
# The model's meshes, by name, with the pieces kept (by rank in size, biggest first; None: all).
PAINT = [("chassis_001_primary_0", 0, 1, 2, 3, 4, 5), ("bonnet_ok_primary_0", None), ("boot_ok_primary_0", None),
         ("pintudepan_primary_0", 0), ("pintudepan_001_primary_0", 0), ("pintu_primary_0", 0, 2, 4),
         ("pintu_001_primary_0", 0, 2, 4)]
SHELL = PAINT + [("cermin_cermin_0_0", None), ("pintudepan_cermin_0_0", None), ("pintudepan_001_cermin_0_0", None),
                 ("pintu_cermin_0_0", None), ("pintu_001_cermin_0_0", None), ("chassis_pintudepan_4_0", 0),
                 ("lampu_left_front_light_0", None), ("lampu_right_front_light_0", None),
                 ("grill_primary_0", None), ("lampubelakang_lampubelakang_0_0", None),
                 ("lampubelakang_001_right_rear_light_0", None)]


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
        out[o.name] = np.array([[v.co[:] for v in f.verts] for f in bm.faces])
        bm.free()
    return out


class Space:
    """Model coordinates (X to the car's left, Y back, Z up) to build space, each axis scaled
    to its published size. The overhangs stretch so the length is the published one."""

    def __init__(self, m):
        c = np.array([(m[k].reshape(-1, 3).min(0) + m[k].reshape(-1, 3).max(0)) / 2 for k in TYRES])
        yf, yr = c[c[:, 1] < 0, 1].mean(), c[c[:, 1] > 0, 1].mean()
        self.sx = WHEELBASE / (yr - yf)
        self.ymid = (yf + yr) / 2
        self.zc = c[:, 2].mean()
        paint = np.vstack([m[k].reshape(-1, 3) for k, *_ in PAINT])
        self.sy = WIDTH / (2 * np.abs(paint[:, 0]).max())
        self.sz = (HEIGHT - TYRE_R) / (paint[:, 2].max() - self.zc)
        tyre = np.vstack([m[k].reshape(-1, 3) for k in TYRES])
        self.outer_y = float(np.abs(tyre[:, 0]).max() * self.sy)
        x = -(paint[:, 1] - self.ymid) * self.sx
        self.nose, self.tail = float(x.max()), float(x.min())
        over = LENGTH - (self.nose - self.tail)
        f_over, r_over = self.nose - WHEELBASE / 2, -WHEELBASE / 2 - self.tail
        self.kf = 1 + over * f_over / (f_over + r_over) / f_over
        self.kr = 1 + over * r_over / (f_over + r_over) / r_over

    def __call__(self, p):
        p = np.asarray(p, float)
        x = -(p[..., 1] - self.ymid) * self.sx
        h = WHEELBASE / 2
        x = np.where(x > h, h + (x - h) * self.kf, np.where(x < -h, -h + (x + h) * self.kr, x))
        return np.stack([x, p[..., 0] * self.sy, (p[..., 2] - self.zc) * self.sz + TYRE_R], -1)


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


def pick(m, sp, name, *idx):
    """Pieces of one mesh in build space, by their rank in size (biggest first); None: all."""
    t = sp(m[name])
    if idx == (None,) or not idx:
        return t
    cs = sorted(comps(t), key=lambda c: -len(c))
    return np.vstack([cs[i] for i in idx])


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
    hi = gaussian_filter1d(hi, 4, mode="nearest")
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


def shell_tris(sp, m):
    return np.vstack([pick(m, sp, *k) for k in SHELL])


def sections(sp, m, arches):
    from scipy.ndimage import gaussian_filter, median_filter
    tris = shell_tris(sp, m)
    xs_all = tris[:, :, 0]
    nose, tail = xs_all.max(), xs_all.min()
    xs = np.concatenate([[nose - 0.004, nose - 0.012], np.arange(nose - 0.025, tail + 0.02, -STEP),
                         [tail + 0.012, tail + 0.004]])
    th = np.linspace(-np.pi / 2, np.pi / 2, ANGLES)
    in_arch = lambda x: any(abs(x - wx) < ar + 0.012 for wx, ar in arches)
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
            top = TYRE_R + max(ar for _w, ar in arches)
            zz = zc + r * np.sin(th)
            r[(th < 0) & (np.isnan(r) | (zz < top))] = -1
        miss = np.isnan(r) & (th < -0.05)                 # a floor where the model has none
        r[miss] = (lo - zc) / np.sin(th[miss])
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
    # smooth hard along the car, lightly round it: plain panels, crisp edges running lengthwise.
    # The smoothing works on the section points themselves, not on the radii, since the centre
    # height jumps at the wheel arches and would carry the radii up and down with it.
    Y, Z = Rm * np.cos(th), ZC[:, None] + Rm * np.sin(th)
    Y = gaussian_filter(median_filter(Y, size=(5, 3), mode="nearest"), sigma=(2.2, 1.0), mode="nearest")
    Z = gaussian_filter(median_filter(Z, size=(5, 3), mode="nearest"), sigma=(2.2, 1.0), mode="nearest")
    Y[:, 0] = Y[:, -1] = 0.0
    ZC = gaussian_filter(median_filter(ZC, size=5, mode="nearest"), 1.5, mode="nearest")
    for i in range(len(X)):
        full = np.vstack([np.column_stack([Y[i], Z[i]]), np.column_stack([-Y[i], Z[i]])[::-1]])
        r = polar(np.stack([full[:-1], full[1:]], 1), ZC[i], np.clip(th, -np.pi / 2 + 0.01, np.pi / 2 - 0.01))
        Rm[i] = np.where(np.isnan(r), np.hypot(Y[i], Z[i] - ZC[i]), r)
    raw = {"x": xs, "top": zhi, "wid": np.array([np.abs(sg[:, :, 0]).max() for _, sg in cuts])}
    return [dict(x=round(float(x), 4), zc=round(float(zc), 4), r=r4(r)) for x, zc, r in zip(X, ZC, Rm)], raw


def plain_ends(stations, raw, xf, xr, arches, blend=0.10):
    """Plain nose and tail. Ahead of xf and behind xr each section is the section at xf (or xr)
    stretched to the smoothed side and plan outline there, so the ends carry none of the
    model's grille slats, badge or lamp steps. Top and width only shrink towards the ends, so
    the bumpers stand out below the lamp faces as plain shelves."""
    from scipy.ndimage import gaussian_filter1d
    th = np.linspace(-np.pi / 2, np.pi / 2, ANGLES)
    st = sorted(stations, key=lambda t: t["x"])
    X = np.array([t["x"] for t in st])
    halves = [np.column_stack([np.asarray(t["r"]) * np.cos(th), t["zc"] + np.asarray(t["r"]) * np.sin(th)]) for t in st]
    # the side and plan outline from the model's own slices, barely smoothed, so the boot's
    # back edge and the lamp faces stay crisp
    top = gaussian_filter1d(np.interp(X, raw["x"][::-1], raw["top"][::-1]), 0.7, mode="nearest")
    bot = np.array([h[:, 1].min() for h in halves])
    free = np.array([all(abs(x - wx) > ar + 0.03 for wx, ar in arches) for x in X])
    bot = gaussian_filter1d(np.interp(X, X[free], bot[free]), 2, mode="nearest")
    wid = gaussian_filter1d(np.interp(X, raw["x"][::-1], raw["wid"][::-1]), 0.7, mode="nearest")
    out = []
    for end, x0 in (("front", xf), ("rear", xr)):
        i0 = int(np.argmin(np.abs(X - x0)))
        idx = range(i0, len(X)) if end == "front" else range(i0, -1, -1)
        ref = halves[i0]
        t0, b0, w0 = ref[:, 1].max(), ref[:, 1].min(), ref[:, 0].max()
        lt, lw = top[i0], wid[i0]
        for i in idx:
            lt, lb, lw = min(lt, top[i]), bot[i], min(lw, wid[i])
            h = np.column_stack([ref[:, 0] * lw / w0, lb + (ref[:, 1] - b0) * (lt - lb) / (t0 - b0)])
            zc = (lt + lb) / 2
            full = np.vstack([h, (h * [-1, 1])[::-1]])
            segs = np.stack([full[:-1], full[1:]], 1)
            r = polar(segs, zc, np.clip(th, -np.pi / 2 + 0.01, np.pi / 2 - 0.01))
            r = np.where(np.isnan(r), np.nanmax(r), r)
            k = np.clip(abs(X[i] - x0) / blend, 0, 1)
            old = np.column_stack([np.asarray(st[i]["r"]) * np.cos(th), st[i]["zc"] + np.asarray(st[i]["r"]) * np.sin(th)])
            new = np.column_stack([r * np.cos(th), zc + r * np.sin(th)])
            mix = old * (1 - k) + new * k
            zc_m = st[i]["zc"] * (1 - k) + zc * k
            out.append((i, zc_m, np.hypot(mix[:, 0], mix[:, 1] - zc_m)))
    for i, zc, r in out:
        st[i]["zc"], st[i]["r"] = round(float(zc), 4), r4(r)
    return stations


def arches(sp, m, wheel_x):
    """Arch radius round each wheel centre, from the body paint at the sides."""
    v = np.vstack([pick(m, sp, *k) for k in PAINT]).reshape(-1, 3)
    out = []
    for wx in wheel_x:
        near = v[(np.abs(v[:, 1]) > 0.65) & (np.abs(v[:, 0] - wx) < 0.6) & (v[:, 2] < TYRE_R + 0.6)]
        d = np.hypot(near[:, 0] - wx, near[:, 2] - TYRE_R)
        out.append((wx, round(float(np.percentile(d, 0.5)), 4)))
    return out


# ---------------------------------------------------------------- the rows
def box3(t):
    v = t.reshape(-1, 3)
    return r4(v.min(0)), r4(v.max(0))


def measure(path):
    m = load(path)
    sp = Space(m)
    P = lambda name, *i: pick(m, sp, name, *i)
    left = lambda t: t[t[:, :, 1].mean(1) > 0]
    wheel_x = [WHEELBASE / 2, -WHEELBASE / 2]
    arch = arches(sp, m, wheel_x)
    stations, raw = sections(sp, m, arch)

    # glass: the side panes (front door, rear door with its fixed quarter), screen, rear window
    front_pane = biggest(project(P("pintudepan_cermin_0_0"), 0, 2))
    rear_pane = biggest(project(P("pintu_cermin_0_0"), 0, 2))
    dlo, belt_line = window_line([front_pane, rear_pane, bridge(rear_pane, front_pane)])
    b_pillar = [float(rear_pane.bounds[2]), float(front_pane.bounds[0])]
    div = P("pintu_pintudepan_4_0", 3).reshape(-1, 3)
    divider = [float(div[:, 0].min()), float(div[:, 0].max())]
    cs = sorted(comps(sp(m["cermin_cermin_0_0"])), key=lambda c: -c[:, :, 0].mean())
    screen, rear = cs[0], cs[-1]
    so = plain(project(screen, 0, 1), 0.02)
    centre = so[np.abs(so[:, 1]) < 0.06]
    rear_glass = plain(project(rear, 1, 2), 0.02)
    rear_side = np.array(biggest(project(left(rear), 0, 2)).simplify(0.003).exterior.coords)[:-1]

    # lamps, as 3D triangles on the car's left
    head = P("lampu_left_front_light_0")
    tail = np.vstack([left(P("lampubelakang_lampubelakang_0_0")), left(P("lampubelakang_001_right_rear_light_0"))])
    stations = plain_ends(stations, raw, float(screen[:, :, 0].max()) + 0.25, float(tail[:, :, 0].max()) + 0.06, arch)
    amber = left(P("lampubelakang_lampubelakang_0_0", 0, 1))      # the outer corner, amber on the model
    white = left(P("lampubelakang_lampubelakang_0_0", 2, 3))      # the inner end, white on the model

    # front: grille panel and its slots, the badge, the black strip, the slotted intake under it
    gr = P("grill_primary_0").reshape(-1, 3)
    grille = {"y": round(float(gr[:, 1].max()), 4), "z0": round(float(gr[:, 2].min()), 4),
              "z1": round(float(gr[:, 2].max()), 4), "x": round(float(gr[:, 0].max()), 4)}
    gb = P("grill_grill_1_0").reshape(-1, 3)
    grille["badge"] = [round(float(gb[:, 2].min()), 4), round(float(gb[:, 2].max()), 4), round(float(gb[:, 1].max()), 4)]
    st = P("chassis_pintudepan_4_0", 0).reshape(-1, 3)
    strip = {"z0": round(float(st[:, 2].min()), 4), "z1": round(float(st[:, 2].max()), 4),
             "x_back": round(float(st[:, 0].min()), 4)}
    slats = [box3(P("chassis_pintudepan_4_0", i)) for i in (3, 4, 5)]
    lo = np.vstack([np.array(a) for s in slats for a in s])
    intake = {"y": round(float(np.abs(lo[:, 1]).max()), 4), "z0": round(float(lo[:, 2].min()), 4),
              "z1": round(float(lo[:, 2].max()), 4), "n": len(slats)}

    # mirror: the black housing on the left front door
    mv = P("pintudepan_pintudepan_4_0", 0).reshape(-1, 3)
    mirror = {"x_back": round(float(mv[:, 0].min()), 4), "x_front": round(float(mv[:, 0].max()), 4),
              "y_out": round(float(mv[:, 1].max()), 4), "z0": round(float(mv[:, 2].min()), 4),
              "z1": round(float(mv[:, 2].max()), 4)}

    # shut lines: doors (side), bonnet (top), boot lid (rear and side)
    front_door = plain(project(P("pintudepan_primary_0", 0), 0, 2), 0.01, 0.003)
    rear_door = plain(project(np.vstack([P("pintu_primary_0", 0, 2, 4)]), 0, 2), 0.01, 0.003)
    belt_z = float(np.min(np.array(dlo.exterior.coords)[:, 1]))
    bonnet = plain(project(P("bonnet_ok_primary_0"), 0, 1), 0.02, 0.003)
    bt = P("boot_ok_primary_0")
    lid_rear = plain(project(bt, 1, 2), 0.01, 0.003)
    lid_top = plain(project(bt, 0, 1), 0.01, 0.003)
    bv = bt.reshape(-1, 3)

    # the model's own lettering on the lid: "SAGA" on the right, the maker's script on the left
    saga = P("boot_ok_boot_ok_4_0").reshape(-1, 3)
    spots = {"lid_name": [round(float(saga[:, 1].mean()), 4), round(float(saga[:, 2].mean()), 4),
                          round(float(np.ptp(saga[:, 2])), 4)],
             "lid_x": round(float(bv[:, 0].min()), 4)}

    return {
        "name": "NusaCarrier",
        "wheel": {"r": TYRE_R, "rim_r": RIM_R, "width": TYRE_W, "outer_y": round(sp.outer_y, 4),
                  "front_x": wheel_x[0], "rear_x": wheel_x[1], "arch_r": round(max(a for _x, a in arch), 4)},
        "scale": r4([sp.sx, sp.sy, sp.sz]),
        "stations": stations,
        "dlo": r4(plain(dlo, 0.015, 0.003)),
        "b_pillar": r4(b_pillar),
        "divider": r4(divider),
        "belt_z": round(belt_z, 4),
        "belt": [round(float(v), 5) for v in belt_line],
        "screen": {"outline": r4(so), "x_base": round(float(centre[:, 0].max()), 4),
                   "x_top": round(float(centre[:, 0].min()), 4)},
        "rear_glass": r4(rear_glass),
        "rear_glass_side": r4(rear_side),
        "head": r4(head),
        "tail": r4(tail),
        "tail_amber": r4(amber),
        "tail_white": r4(white),
        "grille": grille,
        "strip": strip,
        "intake": intake,
        "mirror": mirror,
        "front_door": r4(front_door),
        "rear_door": r4(rear_door),
        "bonnet": r4(bonnet),
        "lid_rear": r4(lid_rear),
        "lid_top": r4(lid_top),
        "spots": spots,
        # Ivan's picks from the viewer.
        "picks": {"frontbadge": 1, "rearbadge": 1, "name": 1, "wheels": 1},
    }


if __name__ == "__main__":
    rows = measure(sys.argv[1] if len(sys.argv) > 1 else GLB)
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(rows, indent=1))
    st = rows["stations"]
    print("wrote", out, len(st), "stations, nose", st[0]["x"], "tail", st[-1]["x"],
          "arch", rows["wheel"]["arch_r"], "outer", rows["wheel"]["outer_y"], "scale", rows["scale"])
