"""Measured rows for the Feretti Vortice, based on the Ferrari 458 Italia.

Reference: Ivan's 3D model of a 2013 458 Italia (a .glb, kept out of the repo), given for
measurements only. Ivan wants simple, clean, smooth shapes over close copies, so every outline
here is simplified and every section is smoothed hard.
  - body sections: the outer skin cut across at stations along the car, read as a radius at
    even angles round a centre point, so every station has the same number of points;
  - glass, lamps, intakes, shut lines, mirror and exhaust: outlines of the matching meshes,
    seen from the side, front, rear or top, or as 3D loops for the head lamps.
The model is scaled so its wheelbase is the published 2650 mm, with the front axle one
published front tyre radius (235/35 R20) off the ground. Its length then matches the
published 4527 mm within a centimetre and its width the published 1937 mm.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py [path/to/458.glb]  -> rows.json
"""
import json
import sys
from pathlib import Path

import numpy as np

WHEELBASE = 2.650
LENGTH = 4.527
WIDTH = 1.937
TYRE_F = round((20 * 25.4 + 2 * 0.35 * 235) / 2000, 4)     # 235/35 R20
TYRE_R = round((20 * 25.4 + 2 * 0.35 * 295) / 2000, 4)     # 295/35 R20
RIM_R = round(20 * 25.4 / 2000, 4)
GLB = "/mnt/project-files/uploads/ref/ferrari458.glb"
ANGLES = 73                    # points round each half section, bottom centre to top centre
STEP = 0.03                    # station spacing along the car

# The model's panels, by mesh name.
TYRES_F = ["Object_4", "Object_115"]
TYRES_R = ["Object_9", "Object_14"]
TYRES = TYRES_F + TYRES_R
PAINT = ["Object_63", "Object_19", "Object_47", "Object_89", "Object_93", "Object_69"]
MIRRORS = ["Object_29", "Object_67"]
SCREEN, SIDE_GLASS, REAR_GLASS = "Object_31", ["Object_21", "Object_55"], "Object_103"
HEAD_GLASS = ["Object_105", "Object_113"]
SHELL = PAINT + [SCREEN, REAR_GLASS] + SIDE_GLASS + HEAD_GLASS


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


def shell_tris(sp, m):
    return sp(np.vstack([m[k] for k in SHELL]))      # the mirrors are separate meshes


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


TAIL_PINCH = 0.06     # the tail face is this much narrower than the body ahead of it
TAIL_DROP = 0.02      # and its top edge this much lower
TAIL_REF = 0.50       # the tail keeps the section this far ahead of its end


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
    free = np.array([all(abs(x - wx) > ar + 0.03 for wx, _wz, ar in arches) for x in X])
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
            if end == "front":
                lt, lb, lw = min(lt, top[i]), bot[i], min(lw, wid[i])     # top and width only shrink
            else:
                # the tail: plan and top fall away only near the end, so the back stands upright
                t = (x0 - X[i]) / (x0 - X.min())
                lt, lb, lw = t0 - TAIL_DROP * t ** 2, bot[i], w0 * (1 - TAIL_PINCH * t ** 3)
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


def measure(path):
    m = load(path)
    sp = Space(m)
    T = lambda *names: sp(np.vstack([m[k] for k in names]))
    left = lambda t: t[t[:, :, 1].mean(1) > 0]
    wheels = [(WHEELBASE / 2, TYRE_F), (-WHEELBASE / 2, TYRE_R)]
    arch = arches(sp, m, wheels)
    stations = sections(sp, m, arch)

    # glass: side pane (x, z), screen top (x, y), engine cover glass top (x, y)
    pane = left(T(*SIDE_GLASS))
    dlo = biggest(project(pane, 0, 2))
    screen = T(SCREEN)
    so = plain(project(screen, 0, 1).buffer(-0.006), 0.02)
    centre = so[np.abs(so[:, 1]) < 0.06]
    rear = T(REAR_GLASS)
    rear_glass = plain(project(rear, 0, 1), 0.02)
    rear_side = np.array(biggest(project(rear, 0, 2)).simplify(0.003).exterior.coords)[:-1]
    tail = T(*PAINT)[:, :, 0].min()
    stations = plain_ends(stations, float(centre[:, 0].max()) + 0.55, float(tail) + TAIL_REF, arch)

    # lamps: head lamp glass as a 3D loop on the left; tail lamps are round
    head = left(T(*HEAD_GLASS))
    tail_lamp = disc(T("Object_49"))

    # front, seen from ahead (y, z): the lower intake
    intake = outline(T("Object_81"), 1, 2, 0.015)
    badge_f = disc(T("Object_65"))

    # rear: exhaust tips, the black panels under the lamps, the diffuser
    tv = T("Object_27")
    tips = []
    for y0 in (-1, 0, 1):                             # three tips side by side
        c = tv[np.abs(tv[:, :, 1].mean(1) - y0 * 0.104) < 0.052]
        tips.append(disc(c))
    panel = [outline(c, 1, 2, 0.01) for c in comps(left(T("Object_45"))) if len(c) > 30 and c[:, :, 0].mean() < -1.8]
    diffuser = outline(T("Object_59"), 1, 2, 0.02)

    # mirror, from its painted cap
    mv = T("Object_29").reshape(-1, 3)
    mirror = {"x_back": round(float(mv[:, 0].min()), 4), "x_front": round(float(mv[:, 0].max()), 4),
              "y_out": round(float(mv[:, 1].max()), 4), "z0": round(float(mv[:, 2].min()), 4),
              "z1": round(float(mv[:, 2].max()), 4)}

    # shut lines: the door (side), the bonnet (top), the engine cover (top)
    door = outline(T("Object_93"), 0, 2, 0.01)
    bonnet = outline(T("Object_69"), 0, 1, 0.02)
    engine = outline(T("Object_61"), 0, 1, 0.02)

    return {
        "name": "FerettiVortice",
        "wheel": {"r_f": TYRE_F, "r_r": TYRE_R, "rim_r": RIM_R, "width_f": 0.235, "width_r": 0.295,
                  "outer_y": round(sp.outer_y, 4), "front_x": wheels[0][0], "rear_x": wheels[1][0],
                  "arch_f": arch[0][2], "arch_r": arch[1][2]},
        "stations": stations,
        "dlo": r4(plain(dlo, 0.02, 0.003)),
        "screen": {"outline": r4(so), "x_base": round(float(centre[:, 0].max()), 4),
                   "x_top": round(float(centre[:, 0].min()), 4)},
        "rear_glass": r4(rear_glass),
        "rear_glass_side": r4(rear_side),
        "head": r4(head),
        "tail_lamp": tail_lamp,
        "intake": r4(intake),
        "badge_front": badge_f,
        "exhaust": tips,
        "rear_panels": [r4(p) for p in panel],
        "diffuser": r4(diffuser),
        "mirror": mirror,
        "door": r4(door),
        "bonnet": r4(bonnet),
        "engine_cover": r4(engine),
        "picks": {},
    }


if __name__ == "__main__":
    rows = measure(sys.argv[1] if len(sys.argv) > 1 else GLB)
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(rows, indent=1))
    st = rows["stations"]
    print("wrote", out, len(st), "stations, nose", st[0]["x"], "tail", st[-1]["x"], rows["wheel"])
