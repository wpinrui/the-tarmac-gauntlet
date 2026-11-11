"""Measured rows for the Archer Crescendo, based on the Mercedes-Benz C63 AMG saloon (W204).

Reference: Ivan's 3D model of a 2008 C63 AMG (a .glb, kept out of the repo), given for
measurements only. Ivan wants smooth, simple shapes over close copies, so every outline here
is simplified and every section is smoothed hard.
  - body sections: the outer skin cut across at stations along the car, read as a radius at
    even angles round a centre point, so every station has the same number of points;
  - glass, lamps, grille, intakes, shut lines, mirror and the option spots: outlines of the
    matching panels, seen from the side, front, rear or top, or as 3D loops for the lamps.
The model is scaled so its wheelbase is the published 2765 mm, and sits on the ground one
published tyre radius (235/40 R18) under the wheel centres. Its length then matches the
published 4725 mm within a centimetre and its width the published 1795 mm.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py [path/to/c63.glb]  -> rows.json
"""
import json
import sys
from pathlib import Path

import numpy as np

WHEELBASE = 2.765
LENGTH = 4.725
TYRE_R = round((18 * 25.4 + 2 * 0.40 * 235) / 2000, 4)
RIM_R = round(18 * 25.4 / 2000, 4)
GLB = "/mnt/project-files/uploads/hearth/85ddc293-c6e0-4186-9d09-1e48ef766f97"
ANGLES = 73                    # points round each half section, bottom centre to top centre
STEP = 0.03                    # station spacing along the car

# The model's panels, by mesh name.
TYRES = ["Mesh2_llanta_0", "Mesh71_llanta_0", "Mesh76_llanta_0", "Mesh81_llanta_0"]
PAINT = [f"Mesh{i}_c63color_0" for i in range(6, 20)]
GLASS = ["Mesh67_vidrio_0", "Mesh69_vidrioneg_0", "Mesh49_plasticneg_0"]
FRAMES = ["Mesh24_plasticneg_0", "Mesh42_plasticneg_0", "Mesh43_plasticneg_0",
          "Mesh42_cromo_0", "Mesh43_cromo_0"]
LAMPS = ["Mesh65_c63luz_0", "Mesh60_plasticneg_0", "Mesh61_plasticneg_0"]
SHELL = PAINT + GLASS + FRAMES + LAMPS
GRILLE = ["Mesh20_cromo_0", "Mesh21_cromo_0"]
MIRROR_BOX = ((0.30, 0.62), 0.80, (0.86, 1.06))   # x range, |y| from, z range: kept out of the skin


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
    """Model coordinates (X to the car's left, Y back, Z up) to build space. The overhangs
    stretch a touch so the length is the published one."""

    def __init__(self, m):
        c = np.array([(m[k].reshape(-1, 3).min(0) + m[k].reshape(-1, 3).max(0)) / 2 for k in TYRES])
        yf, yr = c[c[:, 1] < 0, 1].mean(), c[c[:, 1] > 0, 1].mean()
        self.s = WHEELBASE / (yr - yf)
        self.ymid = (yf + yr) / 2
        self.zc = c[:, 2].mean()
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
        return np.stack([x, p[..., 0] * self.s, (p[..., 2] - self.zc) * self.s + TYRE_R], -1)


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


def shell_tris(sp, m):
    tris = sp(np.vstack([m[k] for k in SHELL]))
    c = tris.mean(1)
    (x0, x1), ymin, (z0, z1) = MIRROR_BOX
    mirror = (c[:, 0] > x0) & (c[:, 0] < x1) & (np.abs(c[:, 1]) > ymin) & (c[:, 2] > z0) & (c[:, 2] < z1)
    return tris[~mirror]


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
    Rm = median_filter(Rm, size=(3, 5), mode="nearest")
    Rm = gaussian_filter(Rm, sigma=(1.6, 1.8), mode="nearest")      # smooth simple panels
    ZC = median_filter(ZC, size=5, mode="nearest")
    return [dict(x=round(float(x), 4), zc=round(float(zc), 4), r=r4(r)) for x, zc, r in zip(X, ZC, Rm)]


TAIL_FACE = 0.62     # top of the small flat rear face; above it the tail leans in one plain curve
TAIL_SHOULDER = 0.20  # the lid's back edge rolls down to the face over this length, with no bumper step
TAIL_CORNER = 0.30    # the plan corners round in over this length
TAIL_FACE_W = 0.42    # half width of the flat rear face


def _round_in(x, x_end, run, v_end, v_full):
    s = np.clip((x - x_end) / run, 0, 1)
    return v_end + (v_full - v_end) * (1 - (1 - s) ** 2)


def tail_top(x, full, x_end):
    return _round_in(x, x_end, TAIL_SHOULDER, TAIL_FACE, full)


def tail_width(x, full, x_end):
    return _round_in(x, x_end, TAIL_CORNER, TAIL_FACE_W, full)


def plain_ends(stations, xf, xr, arches, blend=0.12):
    """Ivan wants smooth, plain bumpers. Ahead of xf and behind xr each section is the section at
    xf (or xr) stretched to the smoothed side and plan outline there, so the nose and tail carry
    no grille recess, intake or lamp step of the model. The outline only shrinks towards the ends."""
    from scipy.ndimage import gaussian_filter1d
    th = np.linspace(-np.pi / 2, np.pi / 2, ANGLES)
    st = sorted(stations, key=lambda t: t["x"])
    X = np.array([t["x"] for t in st])
    halves = [np.column_stack([np.asarray(t["r"]) * np.cos(th), t["zc"] + np.asarray(t["r"]) * np.sin(th)]) for t in st]
    top = gaussian_filter1d(np.array([h[:, 1].max() for h in halves]), 2, mode="nearest")
    bot = np.array([h[:, 1].min() for h in halves])
    free = np.array([all(abs(x - wx) > ar + 0.03 for wx, ar in arches) for x in X])
    bot = gaussian_filter1d(np.interp(X, X[free], bot[free]), 2, mode="nearest")
    wid = gaussian_filter1d(np.array([h[:, 0].max() for h in halves]), 2, mode="nearest")
    out = []
    for end, x0 in (("front", xf), ("rear", xr)):
        i0 = int(np.argmin(np.abs(X - x0)))
        idx = range(i0, len(X)) if end == "front" else range(i0, -1, -1)
        ref = halves[i0]
        t0, b0, w0 = ref[:, 1].max(), ref[:, 1].min(), ref[:, 0].max()
        lt, lb, lw = top[i0], bot[i0], wid[i0]
        for i in idx:
            lt, lb, lw = min(lt, top[i]), bot[i], min(lw, wid[i])     # top and width only shrink
            if end == "rear":
                lt, lw = tail_top(X[i], top[i0], X[-1 if end == "front" else 0]), tail_width(X[i], wid[i0], X[0])
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
            rr = np.hypot(mix[:, 0], mix[:, 1] - zc_m)
            out.append((i, zc_m, rr))
    for i, zc, r in out:
        st[i]["zc"], st[i]["r"] = round(float(zc), 4), r4(r)
    return stations


def arches(sp, m, wheel_x):
    """Arch radius round each wheel centre, from the body paint at the sides."""
    v = sp(np.vstack([m[k] for k in PAINT])).reshape(-1, 3)
    out = []
    for wx in wheel_x:
        near = v[(np.abs(v[:, 1]) > 0.7) & (np.abs(v[:, 0] - wx) < 0.6) & (v[:, 2] < TYRE_R + 0.6)]
        d = np.hypot(near[:, 0] - wx, near[:, 2] - TYRE_R)
        out.append((wx, round(float(np.percentile(d, 0.5)), 4)))
    return out


# ---------------------------------------------------------------- the rows
def measure(path):
    from shapely.geometry import box
    from shapely.ops import unary_union
    m = load(path)
    sp = Space(m)
    T = lambda *names: sp(np.vstack([m[k] for k in names]))
    left = lambda t: t[t[:, :, 1].mean(1) > 0]
    wheel_x = [WHEELBASE / 2, -WHEELBASE / 2]
    arch = arches(sp, m, wheel_x)
    stations = sections(sp, m, arch)

    # glass: side (x, z) from the three side panes, screen top (x, y), rear (y, z)
    glass = T("Mesh67_vidrio_0")
    dark = T("Mesh69_vidrioneg_0")
    panes = [c for c in piece(glass, (-1.4, 0.5, 0.9), (0.5, 0.8, 1.4))]
    door_f = [c for c in panes if c[:, :, 0].min() > -0.4][0]
    door_r = [c for c in panes if -1.1 < c[:, :, 0].min() < -0.4][0]
    quarter = [c for c in panes if c[:, :, 0].min() < -1.1][0]
    side = [project(c, 0, 2) for c in (door_f, door_r, quarter)]
    dlo, belt_line = window_line(side + [bridge(side[1], side[0]), bridge(side[2], side[1])])
    b_pillar = [float(side[1].bounds[2]), float(side[0].bounds[0])]
    divider = [float(side[2].bounds[2]), float(side[1].bounds[0])]
    screen = max(piece(dark, (0.0, -0.8, 0.8), (1.0, 0.8, 1.4)), key=len)
    so = plain(project(screen, 0, 1).buffer(-0.006), 0.02)
    centre = so[np.abs(so[:, 1]) < 0.06]
    rear = max(piece(dark, (-2.0, -0.8, 0.9), (-1.0, 0.8, 1.45)), key=len)
    rear_glass = plain(project(rear, 1, 2).buffer(-0.006), 0.02)
    rear_side = np.array(biggest(project(rear, 0, 2)).simplify(0.003).exterior.coords)[:-1]
    stations = plain_ends(stations, float(centre[:, 0].max()) + 0.18, float(rear_side[:, 0].min()) - 0.08, arch)

    # lamps: the head lamp cover and tail lamp lens as 3D loops on the car's left
    head = max(piece(glass, (1.6, 0.4, 0.5), (2.1, 0.8, 0.8)), key=len)
    luz = T("Mesh65_c63luz_0")
    tail = next(c for c in piece(luz, (-2.4, 0.4, 0.6), (-1.9, 0.8, 0.95)) if len(c) > 100)
    luz2 = T("Mesh68_c63luz2_0")
    lenses = []
    for c in piece(luz2, (1.85, 0.45, 0.55), (1.97, 0.72, 0.72)):
        v = c.reshape(-1, 3)
        lenses.append(r4(list((v.min(0) + v.max(0)) / 2) + [float(max(np.ptp(v[:, 1]), np.ptp(v[:, 2])) / 2)]))
    lenses.sort(key=lambda l: -l[1])

    # front, seen from ahead (y, z)
    grille = plain(project(T(*GRILLE), 1, 2).convex_hull, 0.01, 0.003)
    bumper = project(T("Mesh13_c63color_0"), 1, 2)
    holes = [h for p in getattr(bumper, "geoms", [bumper]) for h in p.interiors]
    from shapely.geometry import Polygon
    holes = [Polygon(h) for h in holes if Polygon(h).area > 0.004]
    intakes = [plain(h, 0.015, 0.003) for h in holes]
    centre_in = [i for i in intakes if abs(i[:, 0].mean()) < 0.1]
    side_in = [i for i in intakes if -0.65 < i[:, 0].mean() < -0.3]
    fogv = T("Mesh63_cromo_0").reshape(-1, 3)
    fogv = fogv[fogv[:, 1] > 0]
    fog = [round(float((fogv[:, 1].min() + fogv[:, 1].max()) / 2), 4), round(float((fogv[:, 2].min() + fogv[:, 2].max()) / 2), 4),
           round(float(np.ptp(fogv[:, 2]) / 2), 4)]

    # rear: exhaust tips and the diffuser
    tips = T("Mesh34_cromo_0").reshape(-1, 3)
    tl = tips[tips[:, 1] > 0]
    ys = np.sort(tl[:, 1])
    exhaust = {"x": round(float(tl[:, 0].min()), 4), "y_in": round(float(ys[0]), 4), "y_out": round(float(ys[-1]), 4),
               "z": round(float((tl[:, 2].min() + tl[:, 2].max()) / 2), 4), "h": round(float(np.ptp(tl[:, 2])), 4)}

    # mirror, from its painted cap
    mv = T("Mesh19_c63color_0").reshape(-1, 3)
    (x0, x1), ymin, (z0, z1) = MIRROR_BOX
    mv = mv[(mv[:, 0] > x0) & (mv[:, 0] < x1) & (mv[:, 1] > ymin) & (mv[:, 2] > z0) & (mv[:, 2] < z1)]
    mirror = {"x_back": round(float(mv[:, 0].min()), 4), "x_front": round(float(mv[:, 0].max()), 4),
              "y_out": round(float(mv[:, 1].max()), 4), "z0": round(float(mv[:, 2].min()), 4),
              "z1": round(float(mv[:, 2].max()), 4)}

    # shut lines: rear door from its panel, front door between it and the wing, bonnet, boot lid
    rd = plain(project(left(T("Mesh18_c63color_0")), 0, 2), 0.01, 0.003)
    belt_z = float(np.min(dlo.exterior.coords, axis=0)[1])
    sill_z = float(T("Mesh16_c63color_0")[:, :, 2].max())
    bonnet = plain(project(T("Mesh11_c63color_0"), 0, 1), 0.02, 0.003)
    lid = plain(project(T("Mesh6_c63color_0"), 1, 2), 0.02, 0.003)
    spoiler = T("Mesh9_c63color_0").reshape(-1, 3)

    nm = T("Mesh51_cromo_0").reshape(-1, 3)
    spots = {"lid_z": round(float(nm[:, 2].mean()), 4),
             "bonnet_x": round(float(bonnet[:, 0].max() - 0.16), 4),
             "grille": [0.0, round(float(np.mean([grille[:, 1].min(), grille[:, 1].max()])), 4)],
             "spoiler_z": round(float(spoiler[:, 2].min()), 4)}

    return {
        "name": "ArcherCrescendo",
        "wheel": {"r": TYRE_R, "rim_r": RIM_R, "width": 0.235, "outer_y": round(sp.outer_y, 4),
                  "front_x": wheel_x[0], "rear_x": wheel_x[1], "arch_r": round(max(a for _x, a in arch), 4)},
        "stations": stations,
        "dlo": r4(plain(dlo, 0.02, 0.003)),
        "b_pillar": r4(b_pillar),
        "divider": r4(divider),
        "belt_z": round(belt_z, 4),
        "belt": [round(float(v), 5) for v in belt_line],
        "sill_z": round(sill_z, 4),
        "screen": {"outline": r4(so), "x_base": round(float(centre[:, 0].max()), 4),
                   "x_top": round(float(centre[:, 0].min()), 4)},
        "rear_glass": r4(rear_glass),
        "rear_glass_side": r4(rear_side),
        "head": r4(head),
        "head_lenses": lenses,
        "tail": r4(tail),
        "grille": r4(grille),
        "intake_mid": r4(centre_in[0]) if centre_in else [],
        "intake_side": r4(side_in[0]) if side_in else [],
        "fog": fog,
        "exhaust": exhaust,
        "mirror": mirror,
        "rear_door": r4(rd),
        "bonnet": r4(bonnet),
        "lid": r4(lid),
        "spots": spots,
        # Ivan's picks from the viewer.
        "picks": {"frontbadge": 1, "rearbadge": 1, "name": 1},
    }


if __name__ == "__main__":
    rows = measure(sys.argv[1] if len(sys.argv) > 1 else GLB)
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(rows, indent=1))
    st = rows["stations"]
    print("wrote", out, len(st), "stations, nose", st[0]["x"], "tail", st[-1]["x"],
          "arch", rows["wheel"]["arch_r"], "outer", rows["wheel"]["outer_y"])
