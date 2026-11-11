"""Measured rows for the Liberty Fantasy, based on the 2009 Ford Fiesta Zetec S 3-door (Mk7).

Reference: Ivan's 3D model of the Fiesta Zetec S (a .glb, kept out of the repo). Its
measurements replace a drawing. Every row here is measured from it:
  - body sections: the outer skin cut across at stations along the car, read as a radius at
    even angles round a centre point, so every station has the same number of points;
  - glass, lamps, grilles, shut lines, mirror, spoiler and the option spots: outlines of the
    matching panels, seen from the side, front, rear or top, or as 3D loops for the lamps.
The model is scaled so its wheelbase is the published 2489 mm, and sits on the ground one
published tyre radius (195/45 R16) under the wheel centres. Its length then sits near the
published 3950 mm and its width near the published 1722 mm.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py [path/to/fiesta.glb]  -> rows.json
"""
import json
import sys
from pathlib import Path

import numpy as np

WHEELBASE = 2.489
LENGTH = 3.950
TYRE_R = round((16 * 25.4 + 2 * 0.45 * 195) / 2000, 4)      # 195/45 R16
RIM_R = round(16 * 25.4 / 2000, 4)
TYRE_W = 0.195
GLB = "/mnt/user-data/uploads/hearth/c75c35c8-7960-4b5b-8420-b9fdf50c88a5/c75c35c8-7960-4b5b-8420-b9fdf50c88a5.glb"
ANGLES = 73                    # points round each half section, bottom centre to top centre
STEP = 0.03                    # station spacing along the car

# The model's meshes hold every part of one material; its parts are their connected pieces.
PAINT, BLACK, GLASS, LAMP, CHROME = "Mesh12_FIESTA_0", "Mesh9_FIESTA_plat_0", "Mesh7_vidrios_0", "Mesh4_FIESTA_luz_0", "Mesh6_cromo_0"
TYRES = ["Mesh14_llanta_0", "Mesh19_llanta_0", "Mesh21_llanta_0", "Mesh23_llanta_0"]


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
    """Model coordinates (X to the car's left, Y back, Z up) to build space."""

    def __init__(self, m):
        c = {k: (m[k].reshape(-1, 3).min(0) + m[k].reshape(-1, 3).max(0)) / 2 for k in TYRES}
        yf = np.mean([v[1] for v in c.values() if v[1] < 0])
        yr = np.mean([v[1] for v in c.values() if v[1] > 0])
        self.s = WHEELBASE / (yr - yf)
        self.ymid = (yf + yr) / 2
        self.zc = np.mean([v[2] for v in c.values()])
        tyre = np.vstack([m[k].reshape(-1, 3) for k in TYRES])
        self.outer_y = float(np.abs(tyre[:, 0]).max() * self.s)
        # the overhangs stretch a touch so the painted length is the published one
        x = -(m[PAINT].reshape(-1, 3)[:, 1] - self.ymid) * self.s
        nose, tail = float(x.max()), float(x.min())
        h = WHEELBASE / 2
        f_over, r_over = nose - h, -h - tail
        over = LENGTH - (nose - tail)
        self.kf = 1 + over / (f_over + r_over)
        self.kr = self.kf

    def __call__(self, p):
        p = np.asarray(p, float)
        x = -(p[..., 1] - self.ymid) * self.s
        h = WHEELBASE / 2
        x = np.where(x > h, h + (x - h) * self.kf, np.where(x < -h, -h + (x + h) * self.kr, x))
        return np.stack([x, p[..., 0] * self.s, (p[..., 2] - self.zc) * self.s + TYRE_R], -1)


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


class Parts:
    """The model's parts in build space, found by where they sit."""

    def __init__(self, m, sp):
        self.p = {k: [sp(c) for c in comps(t)] for k, t in m.items()}

    def find(self, mesh, lo, hi, n_min=1):
        """Every piece of mesh whose bounds lie inside lo..hi (x, y, z), joined."""
        out = [c for c in self.p[mesh] if len(c) >= n_min
               and np.all(c.reshape(-1, 3).min(0) > np.asarray(lo) - 1e-3)
               and np.all(c.reshape(-1, 3).max(0) < np.asarray(hi) + 1e-3)]
        if not out:
            raise ValueError(f"nothing in {mesh} inside {lo}..{hi}")
        return np.vstack(out)

    def biggest(self, mesh, k=0):
        return sorted(self.p[mesh], key=len, reverse=True)[k]


# ---------------------------------------------------------------- 2D helpers
def r4(a):
    return np.round(np.asarray(a, float), 4).tolist()


def project(tris, a, b):
    """Union of triangles seen along the third axis, as a shapely shape in (axis a, axis b)."""
    from shapely.geometry import Polygon
    from shapely.ops import unary_union
    polys = [Polygon(t[:, [a, b]]) for t in tris]
    return unary_union([p.buffer(1e-4) for p in polys if p.area > 1e-9])


def outline(g, simplify=0.003):
    """The biggest piece's outer edge, holes filled."""
    from shapely.geometry import Polygon
    if g.geom_type != "Polygon":
        g = max(g.geoms, key=lambda p: p.area)
    return np.array(Polygon(g.exterior).simplify(simplify).exterior.coords)[:-1]


def loops3d(tris):
    """Boundary loops of a triangle sheet, in 3D, longest first."""
    v = tris.reshape(-1, 3)
    key = {}
    for p in v:
        key.setdefault(tuple(np.round(p, 5)), len(key))
    pts = np.array(list(key.keys()))
    idx = np.array([key[tuple(np.round(p, 5))] for p in v]).reshape(-1, 3)
    count = {}
    for t in idx:
        for a, b in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            e = (min(a, b), max(a, b))
            count[e] = count.get(e, 0) + 1
    nxt = {}
    for (a, b), n in count.items():
        if n == 1:
            nxt.setdefault(a, []).append(b); nxt.setdefault(b, []).append(a)
    seen, loops = set(), []
    for start in nxt:
        if start in seen:
            continue
        loop, prev, cur = [start], None, start
        seen.add(start)
        while True:
            options = [n for n in nxt[cur] if n != prev and n not in seen]
            if not options:
                break
            prev, cur = cur, options[0]
            loop.append(cur); seen.add(cur)
        if len(loop) > 8:
            loops.append(pts[loop])
    return sorted(loops, key=len, reverse=True)


def even_loop(p, step):
    p = np.vstack([p, p[:1]])
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(p, axis=0), axis=1))])
    t = np.arange(0, s[-1], step)
    return np.column_stack([np.interp(t, s, p[:, k]) for k in range(p.shape[1])])


def open_path(ring, keep):
    """The run of a closed ring's points where keep holds, as one open path."""
    k0 = int(np.argmax(~keep))
    rolled, kr = np.roll(ring, -k0, axis=0), np.roll(keep, -k0)
    return rolled[kr]


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


def sections(tris, arches):
    from scipy.ndimage import gaussian_filter, median_filter
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
            # the side is open round the wheel: fill the lower half from the stations either side
            top = TYRE_R + max(ar for _w, ar in arches)
            zz = zc + r * np.sin(th)
            r[(th < 0) & (np.isnan(r) | (zz < top))] = -1
        miss = np.isnan(r) & (th < -0.05)                 # a floor where the model has none
        wmax = np.nanmax(np.where(r > 0, r * np.cos(th), np.nan)) if np.any(r > 0) else 0.5
        r[miss] = np.minimum((lo - zc) / np.sin(th[miss]), wmax / np.cos(th[miss]))
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
    Rm = gaussian_filter(Rm, sigma=(1.2, 1.4), mode="nearest")      # the model is coarse: smooth it
    ZC = median_filter(ZC, size=5, mode="nearest")
    return [dict(x=round(float(x), 4), zc=round(float(zc), 4), r=r4(r)) for x, zc, r in zip(X, ZC, Rm)]


def arches(paint, wheel_x):
    """Arch radius round each wheel centre, from the body paint at the sides."""
    v = paint.reshape(-1, 3)
    out = []
    for wx in wheel_x:
        near = v[(np.abs(v[:, 1]) > 0.6) & (np.abs(v[:, 0] - wx) < 0.6) & (v[:, 2] < TYRE_R + 0.6)]
        d = np.hypot(near[:, 0] - wx, near[:, 2] - TYRE_R)
        out.append((wx, round(float(np.percentile(d, 0.5)), 4)))
    return out


def spoiler_mask(rear_glass, tris):
    """Which triangles stand above the rear glass's top edge, seen from the side: the roof spoiler."""
    g = rear_glass.reshape(-1, 3)
    top = g[np.argmax(g[:, 2])]; bot = g[np.argmin(g[:, 2])]
    c = tris.mean(1)
    line = top[2] + (c[:, 0] - top[0]) * (bot[2] - top[2]) / (bot[0] - top[0])
    return (c[:, 0] < top[0] + 0.03) & (c[:, 2] > line + 0.012) & (c[:, 2] > top[2] - 0.12)


# ---------------------------------------------------------------- the rows
def measure(path):
    from shapely.geometry import MultiPoint, box
    from shapely.ops import unary_union
    m = load(path)
    sp = Space(m)
    P = Parts(m, sp)
    left = lambda t: t[t[:, :, 1].mean(1) > 0]
    BIG = 9.0
    wheel_x = [WHEELBASE / 2, -WHEELBASE / 2]

    # the painted panels
    body_p = P.biggest(PAINT)
    tailgate_p = P.find(PAINT, (-BIG, -0.7, 0.6), (-1.2, 0.7, 1.45), 500)
    bonnet_p = P.find(PAINT, (0.9, -BIG, 0.6), (BIG, BIG, 1.0), 200)
    door_l = P.find(PAINT, (-0.8, 0.6, 0.1), (0.9, BIG, 1.0), 200)
    door_r = P.find(PAINT, (-0.8, -BIG, 0.1), (0.9, -0.6, 1.0), 200)
    roof_p = P.find(PAINT, (-1.5, -0.6, 1.25), (0.3, 0.6, BIG), 100)
    paint = np.vstack([body_p, tailgate_p, bonnet_p, door_l, door_r, roof_p])

    # glass
    rear_g = P.find(GLASS, (-BIG, -BIG, 0.9), (-1.4, BIG, BIG), 100)
    quarter_g = P.find(GLASS, (-1.5, 0.5, 0.9), (-0.5, BIG, BIG), 50)
    door_g = P.find(GLASS, (-0.7, 0.5, 0.8), (0.7, BIG, BIG), 20)
    tri_g = P.find(GLASS, (0.5, 0.5, 0.8), (1.0, BIG, 1.1), 20)
    screens = [c for c in P.p[GLASS] if c[:, :, 0].min() > 0.1 and c[:, :, 0].max() < 1.4 and
               np.ptp(c[:, :, 1]) > 1.0]
    screen_g = max(screens, key=lambda c: np.ptp(c[:, :, 1]))
    head_g = P.find(GLASS, (1.0, -BIG, 0.5), (BIG, BIG, 0.9), 50)
    glass_all = np.vstack([rear_g, quarter_g, P.find(GLASS, (-1.5, -BIG, 0.9), (-0.5, -0.5, BIG), 50),
                           door_g, P.find(GLASS, (-0.7, -BIG, 0.8), (0.7, -0.5, BIG), 20),
                           tri_g, P.find(GLASS, (0.5, -BIG, 0.8), (1.0, -0.5, 1.1), 20), screen_g, head_g])

    # lamps and black parts
    tail_l = P.find(LAMP, (-BIG, -BIG, 0.75), (-1.3, BIG, 1.25), 300)
    brake3 = P.find(LAMP, (-BIG, -0.3, 1.25), (-1.6, 0.3, BIG), 10)
    rear_lower = P.find(BLACK, (-BIG, -BIG, 0.1), (-1.5, BIG, 0.55), 300)
    front_lower = P.find(BLACK, (1.8, -BIG, 0.15), (BIG, BIG, 0.6), 300)
    chin = P.find(BLACK, (1.4, -BIG, 0.0), (BIG, BIG, 0.25), 60)
    dlo_trim = P.find(BLACK, (-1.5, -BIG, 0.75), (1.1, BIG, BIG), 200)
    rear_frame = P.find(BLACK, (-BIG, -BIG, 0.95), (-1.5, BIG, 1.35), 100)
    tris = np.vstack([paint, glass_all, tail_l, rear_lower, front_lower, chin, dlo_trim, rear_frame])
    spoil = spoiler_mask(rear_g, tris)
    arch = arches(paint, wheel_x)
    stations = sections(tris[~spoil], arch)

    # glass: side (x, z), screen top (x, y), rear (y, z)
    dg = project(left(door_g), 0, 2)
    qg = project(left(quarter_g), 0, 2)
    tg = project(left(tri_g), 0, 2)
    dlo = unary_union([dg, qg, tg]).buffer(0.09, join_style=2).buffer(-0.09, join_style=2)
    mid_z = (dlo.bounds[1] + dlo.bounds[3]) / 2
    band = box(-5, mid_z - 0.05, 5, mid_z + 0.05)
    b_pillar = [dg.intersection(band).bounds[0], qg.intersection(band).bounds[2]]
    a_post = [tg.intersection(band).bounds[0], dg.intersection(band).bounds[2]]     # the slim bar ahead of the door glass
    screen = project(screen_g, 0, 1).buffer(-0.006)
    so = outline(screen)
    centre = so[np.abs(so[:, 1]) < 0.05]
    rear_glass = outline(project(rear_g, 1, 2).buffer(-0.004))

    # lamps as 3D loops on the car's left
    head = [r4(even_loop(l, 0.01)) for l in loops3d(left(head_g))[:1]]
    tail = [r4(even_loop(l, 0.01)) for l in loops3d(left(tail_l))[:1]]

    # front, seen from ahead (y, z)
    grille_lower = outline(project(front_lower, 1, 2).buffer(0.004).buffer(-0.004), 0.003)
    slot = P.find(BLACK, (1.8, -0.4, 0.6), (BIG, 0.4, 0.72), 30)
    grille_upper = outline(project(slot, 1, 2).buffer(0.004).buffer(-0.004), 0.002)
    chin_o = outline(project(chin, 1, 2).buffer(0.01).buffer(-0.01))
    fog_b = P.find(BLACK, (1.5, -BIG, 0.25), (BIG, -0.5, 0.48), 60)
    fb = project(fog_b, 1, 2)
    fog = [round(fb.centroid.x, 4), round(fb.centroid.y, 4), round(float(np.ptp(fog_b[:, :, 2])) / 2, 4)]

    # rear: the lower diffuser, the exhaust, the reflectors, the third brake light
    rl = rear_lower.reshape(-1, 3)
    diffuser = outline(project(rear_lower, 1, 2).buffer(0.004).buffer(-0.004), 0.003)
    pipes = P.find(BLACK, (-BIG, -0.5, 0.15), (-1.5, -0.2, 0.35), 20).reshape(-1, 3)
    ys = np.sort(pipes[:, 1]); half = (ys[0] + ys[-1]) / 2
    exhaust = {"x": round(float(pipes[:, 0].min()), 4),
               "y": [round(float((ys[0] + half) / 2), 4), round(float((half + ys[-1]) / 2), 4)],
               "z": round(float((pipes[:, 2].max() + pipes[:, 2].min()) / 2), 4),
               "r": round(float(np.ptp(pipes[:, 2]) / 2 - 0.004), 4)}
    refl = P.find(LAMP, (-BIG, 0.3, 0.38), (-1.5, BIG, 0.5), 5)
    reflector = outline(project(refl, 1, 2), 0.002)
    b3 = brake3.reshape(-1, 3)

    # the spoiler: side profile and half width
    st = tris[spoil]
    prof = outline(project(st, 0, 2).buffer(0.008).buffer(-0.008), 0.004)

    # mirror: housing on its base, seen whole
    mv = left(np.vstack([P.find(PAINT, (0.3, 0.7, 0.75), (1.0, BIG, 1.1), 20),
                         P.find(BLACK, (0.3, 0.7, 0.75), (1.0, BIG, 1.1), 100)])).reshape(-1, 3)
    mirror = {"x_back": round(float(mv[:, 0].min()), 4), "x_front": round(float(mv[:, 0].max()), 4),
              "y_in": round(float(mv[:, 1].min()), 4), "y_out": round(float(mv[:, 1].max()), 4),
              "z0": round(float(mv[:, 2].min()), 4), "z1": round(float(mv[:, 2].max()), 4)}

    # shut lines
    door_o = outline(project(left(door_l), 0, 2), 0.002)
    keep = np.array([not dlo.buffer(0.012).contains(MultiPoint([p]).geoms[0]) for p in door_o])
    door = open_path(door_o, keep)
    bonnet = outline(project(bonnet_p, 0, 1), 0.002)
    tgo = outline(project(tailgate_p[~spoiler_mask(rear_g, tailgate_p)], 1, 2), 0.002)
    tailgate = open_path(tgo, tgo[:, 1] < rear_glass[:, 1].max() - 0.01)

    # option spots: the model's own badge places, the tailgate's flat face, the quarter panel
    bf = P.find(CHROME, (1.8, -0.2, 0.55), (BIG, 0.2, 0.75), 10).reshape(-1, 3)
    br = P.find(CHROME, (-BIG, -0.2, 0.8), (-1.6, 0.2, 1.0), 10).reshape(-1, 3)
    spots = {"grille_z": round(float(bf[:, 2].mean()), 4),
             "bonnet_x": round(float(bonnet_p[:, :, 0].max() - 0.16), 4),
             "tailgate_z": round(float(br[:, 2].mean()), 4),
             "tailgate_low_z": round(float(br[:, 2].mean() - 0.17), 4),
             "name_y": 0.36,
             "name_z": round(float(br[:, 2].mean() - 0.06), 4),
             "quarter_x": round(float((b_pillar[1] + wheel_x[1] + arch[1][1]) / 2), 4),
             "quarter_z": round(float(dlo.bounds[1] - 0.12), 4)}

    return {
        "name": "LibertyFantasy",
        "wheel": {"r": TYRE_R, "rim_r": RIM_R, "width": TYRE_W, "outer_y": round(sp.outer_y, 4),
                  "front_x": wheel_x[0], "rear_x": wheel_x[1], "arch_r": round(max(a for _x, a in arch), 4)},
        "stations": stations,
        "dlo": r4(outline(dlo)),
        "b_pillar": r4(b_pillar),
        "a_post": r4(a_post),
        "screen": {"outline": r4(so), "x_base": round(float(centre[:, 0].max()), 4),
                   "x_top": round(float(centre[:, 0].min()), 4)},
        "rear_glass": r4(rear_glass),
        "rear_glass3d": r4(even_loop(loops3d(rear_g)[0], 0.01)),
        "door_glass": r4(outline(dg, 0.002)), "quarter_glass": r4(outline(qg, 0.002)),
        "tri_glass": r4(outline(tg, 0.002)),
        "head": head,
        "grille_upper": r4(grille_upper),
        "grille_lower": r4(grille_lower),
        "chin": r4(chin_o),
        "fog": fog,
        "tail": tail,
        "brake3": {"x": round(float(b3[:, 0].min()), 4), "z": round(float(b3[:, 2].mean()), 4),
                   "half": round(float(b3[:, 1].max()), 4), "h": round(float(np.ptp(b3[:, 2])), 4)},
        "spoiler": {"profile": r4(prof), "half": round(float(np.abs(st[:, :, 1]).max()), 4)},
        "diffuser": r4(diffuser),
        "rear_lower_x": round(float(rl[:, 0].min()), 4),
        "reflector": r4(reflector),
        "exhaust": exhaust,
        "mirror": mirror,
        "door": r4(door),
        "bonnet": r4(bonnet),
        "tailgate": r4(tailgate),
        "spots": spots,
        # Ivan's picks from the viewer.
        "picks": {"frontbadge": 1, "rearbadge": 1, "name": 2},
    }


if __name__ == "__main__":
    rows = measure(sys.argv[1] if len(sys.argv) > 1 else GLB)
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(rows, indent=1))
    st = rows["stations"]
    print("wrote", out, len(st), "stations, nose", st[0]["x"], "tail", st[-1]["x"],
          "arch", rows["wheel"]["arch_r"], "outer", rows["wheel"]["outer_y"])
