"""Measured rows for the Kaiser Scream, based on the 2005 Porsche Carrera GT.

Reference: Ivan's 3D model of the 2000 Carrera GT concept (a .glb, kept out of the repo), given
for measurements only. Its shapes are taken and scaled to the 2005 car's published sizes, and
every outline is simplified and every section smoothed hard.
  - body sections: the outer skin cut across at stations along the car, read as a radius at
    even angles round a centre point, so every station has the same number of points;
  - lamps, intakes, shut lines, glass, mirror and exhaust: outlines of the matching meshes,
    seen from the side, front, rear or top, or as 3D loops for the head lamps.
The model sits rolled a few degrees, so it is first levelled on its four wheel centres. It is then
scaled so its wheelbase is the published 2730 mm, with each axle one published tyre radius
(265/35 R19 front, 335/30 R20 rear) off the ground, and its overhangs stretch to the published
4613 mm length. The published width is 1921 mm.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py [path/to/carrera_gt.glb]  -> rows.json
"""
import json
import sys
from pathlib import Path

import numpy as np

WHEELBASE = 2.730
LENGTH = 4.613
WIDTH = 1.921
HEIGHT = 1.166
TYRE_F = round((19 * 25.4 + 2 * 0.35 * 265) / 2000, 4)     # 265/35 R19
TYRE_R = round((20 * 25.4 + 2 * 0.30 * 335) / 2000, 4)     # 335/30 R20
RIM_F = round(19 * 25.4 / 2000, 4)
RIM_R = round(20 * 25.4 / 2000, 4)
GLB = "/mnt/project-files/uploads/ref/porsche_carrera_gt.glb"
ANGLES = 73                    # points round each half section, bottom centre to top centre
STEP = 0.03                    # station spacing along the car
PAINT = "Porsche_Carrera_GT_Concept_2000_Replica_By_Alex_Ka"


# ---------------------------------------------------------------- loading
def load(path):
    """Triangles of every mesh, keyed by its material's name."""
    import bpy
    import bmesh
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    out = {}
    for o in bpy.context.scene.objects:
        if o.type != "MESH" or not o.data.materials:
            continue
        bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        tris = np.array([[v.co[:] for v in f.verts] for f in bm.faces])
        name = o.data.materials[0].name
        out[name] = np.vstack([out[name], tris]) if name in out else tris
        bm.free()
    return out


class Space:
    """Model coordinates (X to the car's left, Y back, Z up, rolled a few degrees) to build
    space. The four wheel centres level it; the overhangs stretch so the length is the
    published one."""

    def __init__(self, m):
        c = []
        for t in comps(m["tireSIDE"]):
            v = np.unique(np.round(t.reshape(-1, 3), 5), axis=0)
            c.append(v.mean(0))
        c = np.array(c)
        pair = lambda sel: c[sel].mean(0)
        front, rear = c[:, 1] < 0, c[:, 1] > 0
        lf, rf = pair(front & (c[:, 0] > 0)), pair(front & (c[:, 0] < 0))
        lr, rr = pair(rear & (c[:, 0] > 0)), pair(rear & (c[:, 0] < 0))
        fm, rm = (lf + rf) / 2, (lr + rr) / 2
        left = (lf - rf + lr - rr); left /= np.linalg.norm(left)
        fwd = fm - rm; fwd -= left * (fwd @ left)
        wb = np.linalg.norm(fwd); fwd /= wb
        up = np.cross(fwd, left)
        self.s = WHEELBASE / wb
        # pitch so each axle stands one published tyre radius up
        a = np.arcsin((TYRE_F - TYRE_R) / WHEELBASE)
        f2 = fwd * np.cos(a) + up * np.sin(a)
        self.up = np.cross(f2, left); self.fwd = f2; self.left = left
        self.o = (fm + rm) / 2
        self.z0 = (TYRE_F + TYRE_R) / 2
        paint = m[PAINT].reshape(-1, 3)
        x = (paint - self.o) @ self.fwd * self.s
        self.nose, self.tail = float(x.max()), float(x.min())
        over = LENGTH - (self.nose - self.tail)
        h = WHEELBASE / 2
        f_over, r_over = self.nose - h, -h - self.tail
        self.kf = 1 + over * f_over / (f_over + r_over) / f_over
        self.kr = 1 + over * r_over / (f_over + r_over) / r_over
        self.ky = 1.0
        skin = max(comps(m[PAINT]), key=len).reshape(-1, 3)      # no mirrors
        y = np.abs((skin - self.o) @ self.left * self.s)
        self.ky = WIDTH / 2 / float(y.max())       # the concept is a touch wider than the 2005 car
        tyre = self(m["tire"].reshape(-1, 3))
        self.outer_y = float(np.abs(tyre[:, 1]).max())

    def __call__(self, p):
        p = np.asarray(p, float) - self.o
        x = (p @ self.fwd) * self.s
        h = WHEELBASE / 2
        x = np.where(x > h, h + (x - h) * self.kf, np.where(x < -h, -h + (x + h) * self.kr, x))
        return np.stack([x, (p @ self.left) * self.s * self.ky, (p @ self.up) * self.s + self.z0], -1)


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




# ---------------------------------------------------------------- body sections
def shell(sp, m):
    """The painted skin and the windscreen: body, nose, tail, engine deck, doors and bonnet.
    No fairings behind the seats: the hard top takes their place. No mirrors and no wing: they are built on their own."""
    cs = sorted(comps(sp(m[PAINT])), key=lambda c: -len(c))
    diffuser = max(comps(sp(m["diffuzer"])), key=len)
    return np.vstack([cs[i] for i in SKIN] + [sp(m["material"]), diffuser])


SKIN = (0, 1, 2, 3, 6, 7, 8, 9)     # pieces of the paint mesh, by size (6: the wing, flush with the deck)
DECK = (-1.75, -0.30)      # the engine deck and the cabin's back: open grilles and the fairings
                           # behind the seats in the model, a plain deck under the hard top here
DECK_Y = 0.62              # out to this half width
BRIDGE = [(-0.80, -0.38, 0.28, 0.78)]     # (x0, x1, z0, z1): recesses the skin spans over
HUMP = (0.20, 0.80, 0.74, 0.70)           # (x0, x1, z0, y0): the model's mirror plinth, cut back to a straight
                                          # shoulder above z0 and outboard of y0


def sections(tris, arches):
    """Each station's centre height and radius at even angles, smoothed hard."""
    from scipy.ndimage import gaussian_filter, median_filter
    xs_all = tris[:, :, 0]
    nose, tail = xs_all.max(), xs_all.min()
    xs = np.concatenate([[nose - 0.004, nose - 0.012], np.arange(nose - 0.025, tail + 0.02, -STEP),
                         [tail + 0.012, tail + 0.004]])
    th = np.linspace(-np.pi / 2, np.pi / 2, ANGLES)
    in_arch = lambda x: any(abs(x - wx) < ar + 0.04 for wx, _wz, ar in arches)
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
            top = next(wz + ar for w, wz, ar in arches if abs(x - w) < ar + 0.04)
            zz = zc + r * np.sin(th)
            r[(th < 0) & (np.isnan(r) | (zz < top))] = -1
        miss = np.isnan(r) & (th < -0.05)                 # a floor where the model has none
        wmax = np.nanmax(np.where(r > 0, r * np.cos(th), np.nan)) if np.any(r > 0) else 0.5
        r[miss] = np.minimum((lo - zc) / np.sin(th[miss]), wmax / np.cos(th[miss]))   # no wider than the section
        rows.append([x, zc, r])
    X = np.array([row[0] for row in rows]); ZC = np.array([row[1] for row in rows])
    Rm = np.array([row[2] for row in rows])
    Rm[Rm < 0] = np.nan
    for x0, x1, z0, z1 in BRIDGE:                 # the side intake's recess is cut later
        zz = ZC[:, None] + Rm * np.sin(th)[None, :]
        Rm[(X[:, None] > x0) & (X[:, None] < x1) & (zz > z0) & (zz < z1) & (th[None, :] > -1.2)] = np.nan
    for i in np.where((X > DECK[0]) & (X < DECK[1]))[0]:   # the deck runs plain over its grilles
        y, z = Rm[i] * np.cos(th), ZC[i] + Rm[i] * np.sin(th)
        up = th > 0.2
        edge = up & (y > DECK_Y) & ~np.isnan(y)
        if not edge.any():
            continue
        je = int(np.where(edge)[0].max())                  # the deck's edge: innermost crown ray
        ye, ze = y[je], z[je]
        mid = up & (y < 0.06) & ~np.isnan(y)
        zm = float(np.median(z[mid])) if mid.any() else ze
        k = (ze - zm) / ye
        over = np.arange(ANGLES) > je
        Rm[i, over] = (zm - ZC[i]) / (np.sin(th[over]) - k * np.cos(th[over]))
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
    x0, x1, z0, y0 = HUMP                              # the shoulder runs straight past the mirror plinth
    i0, i1 = int(np.argmin(np.abs(X - x0))), int(np.argmin(np.abs(X - x1)))
    up = th > 0
    def prof(i):
        z, y = ZC[i] + Rm[i, up] * np.sin(th[up]), Rm[i, up] * np.cos(th[up])
        o = np.argsort(z)
        return z[o], y[o]
    (za, ya), (zb, yb) = prof(i0), prof(i1)
    for i in range(min(i0, i1) + 1, max(i0, i1)):
        a = (X[i] - X[i0]) / (X[i1] - X[i0])
        for j in np.where(up)[0]:
            z, y = ZC[i] + Rm[i, j] * np.sin(th[j]), Rm[i, j] * np.cos(th[j])
            if z < z0 or np.isnan(y) or y <= y0:
                continue
            lim = max(y0, (1 - a) * np.interp(z, za, ya) + a * np.interp(z, zb, yb))
            if y > lim:
                Rm[i, j] *= lim / y
    Rm = median_filter(Rm, size=(3, 5), mode="nearest")
    Rm = gaussian_filter(Rm, sigma=(1.6, 1.8), mode="nearest")      # smooth simple panels
    ZC = median_filter(ZC, size=5, mode="nearest")
    (xf, _zf, af), (xr, _zr, ar) = sorted(arches, key=lambda a: -a[0])
    i0, i1 = int(np.argmin(np.abs(X - (xf - af)))), int(np.argmin(np.abs(X - (xr + ar))))
    low = th < 0                                       # the sill's bottom runs straight between the arches
    zs = gaussian_filter(ZC, 1.5, mode="nearest")      # the centre heights body.py lofts with
    za, zb = (np.nanmin(zs[i] + Rm[i, low] * np.sin(th[low])) for i in (i0, i1))
    for i in range(i0 + 1, i1):
        zl = za + (zb - za) * (X[i] - X[i0]) / (X[i1] - X[i0])
        z = zs[i] + Rm[i] * np.sin(th)
        under = low & (z < zl)
        Rm[i, under] = (zl - zs[i]) / np.sin(th[under])
    return [dict(x=round(float(x), 4), zc=round(float(zc), 4), r=r4(r)) for x, zc, r in zip(X, ZC, Rm)]


def arches(sp, m, wheels):
    """Arch radius round each wheel centre (x, z): the body paint's nearest reach in even steps
    round the arch's top half, at least a tyre's gap clear of the published tyre."""
    v = shell(sp, m).reshape(-1, 3)
    out = []
    for wx, wz in wheels:
        near = v[(np.abs(v[:, 1]) > 0.8) & (np.abs(v[:, 0] - wx) < 0.6)]
        d = np.hypot(near[:, 0] - wx, near[:, 2] - wz)
        a = np.degrees(np.arctan2(near[:, 2] - wz, near[:, 0] - wx))
        steps = [d[(a >= b) & (a < b + 15)].min() for b in range(0, 180, 15) if ((a >= b) & (a < b + 15)).any()]
        out.append((wx, wz, round(max(float(np.median(steps)), wz + ARCH_GAP), 4)))
    return out


ARCH_GAP = 0.035     # the arch clears the tyre by at least this much


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




def left(t):
    return t[t[:, :, 1].mean(1) > 0]


def loop3(tris):
    """A 3D loop round a nearly flat part: its outline on its best plane, lifted back to 3D."""
    from shapely.geometry import Polygon
    from shapely.ops import unary_union
    p = tris.reshape(-1, 3)
    c = p.mean(0)
    _u, _s, vt = np.linalg.svd(p - c)
    u, v = vt[0], vt[1]
    to2 = lambda q: np.column_stack([(q - c) @ u, (q - c) @ v])
    g = unary_union([Polygon(to2(t)).buffer(1e-4) for t in tris])
    ring2 = np.array(biggest(g).simplify(0.002).exterior.coords)[:-1]
    return r4(c + ring2[:, :1] * u + ring2[:, 1:] * v)


def measure(path):
    m = load(path)
    sp = Space(m)
    B = {k: sp(v) for k, v in m.items()}
    paint = sorted(comps(B[PAINT]), key=lambda c: -len(c))
    pieces = lambda k: sorted(comps(B[k]), key=lambda c: -len(c))
    wheels = [(WHEELBASE / 2, TYRE_F), (-WHEELBASE / 2, TYRE_R)]
    arch = arches(sp, m, wheels)
    stations = sections(shell(sp, m), arch)

    # the windscreen, seen from above, and where its middle meets the cowl and the header
    screen = B["material"]
    sv = screen.reshape(-1, 3)
    centre = sv[np.abs(sv[:, 1]) < 0.1]
    so = plain(project(screen, 0, 1).buffer(-0.004), 0.02)

    # side: the door, the intake behind it, the repeater on the wing
    door = biggest(project(left(paint[7]), 0, 2))
    intake = biggest(project(left(B["grill1"])[left(B["grill1"])[:, :, 0].mean(1) < 0], 0, 2))
    repeater = biggest(project(left(B["turnsignalS"]), 0, 2))

    # front: the head lamp glass in 3D, its amber strip, the three intakes and the fog lamps
    head = left(B["headlightglass"])
    amber = left(B["turnsignalF"])
    fb = pieces("frontbumperplastic")
    intake_mid = biggest(project(fb[0], 1, 2))
    intake_side = biggest(project(left(np.vstack(fb[1:])), 1, 2))
    fog = disc(left(B["foglight"]))
    bonnet = biggest(project(paint[9], 0, 1))

    # rear: the tail lamps, the black panel round the exhausts, the diffuser below, the exhausts
    tail = biggest(project(left(B["brakelight"])[left(B["brakelight"])[:, :, 0].mean(1) < -1], 1, 2))
    dz = pieces("diffuzer")
    panel = project(np.vstack([B["grillrear"], dz[1], dz[2], dz[4]]), 1, 2)
    panel = biggest(panel.buffer(0.01).buffer(-0.01))
    diffuser = biggest(project(dz[0][dz[0][:, :, 0].mean(1) < -2.0], 1, 2))
    pipes = [disc(c) for c in pieces("exhaustchrome") if c[:, :, 1].mean() > 0]

    # top: the engine deck lid, its two grilles
    deck = biggest(project(paint[3], 0, 1))
    grilles = [biggest(project(c, 0, 1)) for c in pieces("ENGINEgrill")[:2]]
    grille = [g for g in grilles if g.centroid.y > 0][0]

    # the door mirror: its housing and glass on the left
    mv = left(paint[4]).reshape(-1, 3)
    gv = left(B["sidemirrors"]).reshape(-1, 3)
    mirror = {"x_back": round(float(gv[:, 0].min()), 4), "x_front": round(float(mv[:, 0].max()), 4),
              "y_in": round(float(mv[:, 1].min()), 4), "y_out": round(float(mv[:, 1].max()), 4),
              "z0": round(float(mv[:, 2].min()), 4), "z1": round(float(mv[:, 2].max()), 4),
              "glass_z0": round(float(gv[:, 2].min()), 4), "glass_z1": round(float(gv[:, 2].max()), 4),
              "glass_y0": round(float(gv[:, 1].min()), 4), "glass_y1": round(float(gv[:, 1].max()), 4)}

    return {
        "name": "KaiserScream",
        "wheel": {"r_f": TYRE_F, "r_r": TYRE_R, "rim_f": RIM_F, "rim_r": RIM_R, "width_f": 0.265,
                  "width_r": 0.335, "outer_y": round(sp.outer_y, 4), "front_x": wheels[0][0],
                  "rear_x": wheels[1][0], "arch_f": arch[0][2], "arch_r": arch[1][2]},
        "stations": stations,
        "screen": {"outline": r4(so), "x_base": round(float(centre[:, 0].max()), 4),
                   "x_top": round(float(centre[:, 0].min()), 4)},
        "door": ring(door), "side_intake": ring(intake), "repeater": ring(repeater),
        "head": r4(head), "head_amber": r4(amber), "intake_mid": ring(intake_mid),
        "intake_side": ring(intake_side), "fog": fog, "bonnet": ring(bonnet),
        "tail": ring(tail), "rear_panel": ring(panel), "diffuser": ring(diffuser), "exhaust": pipes,
        "deck": ring(deck), "grille": ring(grille),
        "mirror": mirror,
        "picks": {"frontbadge": 1, "rearbadge": 1, "name": 1},
    }


if __name__ == "__main__":
    rows = measure(sys.argv[1] if len(sys.argv) > 1 else GLB)
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(rows, indent=1))
    st = rows["stations"]
    print("wrote", out, len(st), "stations, nose", st[0]["x"], "tail", st[-1]["x"], rows["wheel"])
