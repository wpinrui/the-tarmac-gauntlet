"""Measured rows for the Tora Hanabi, based on the 2001 Honda S2000.

Reference: Ivan's 3D model of a 2001 S2000 (a .glb, kept out of the repo). Its renders and
measurements replace a drawing. Every row here is measured from it, then set to the 2001's
published sizes:
  - wheelbase 2400 mm sets the scale, from the wheel centres;
  - the overhangs stretch so the length is the published 4135 mm;
  - half widths are set to the published 1750 mm body width;
  - tyres 205/55 R16 front and 225/50 R16 rear on the published tracks, 1470 and 1510 mm;
  - the ground sits one tyre radius under the wheel centres.
Body sections are the outer skin cut across at stations along the car, read as a radius at
even angles round a centre point. Over the cockpit the skin is closed flat at the door tops;
the opening is cut later. The roof is measured on its own, as sections above the belt.
Glass, lamps, grille, shut lines, mirrors and the option spots are outlines of the matching
meshes, seen from the side, front, rear or top, or as 3D loops.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py [path/to/s2000.glb]  -> rows.json
"""
import json
import sys
from pathlib import Path

import numpy as np

WHEELBASE = 2.400
LENGTH = 4.135
WIDTH = 1.750
TRACK = {"front": 1.470, "rear": 1.510}
TYRE = {"front": (205, 55), "rear": (225, 50)}
RIM_IN = 16
GLB = "/mnt/user-data/uploads/hearth/3363c18b-b62c-4a19-9cd4-063d7db0f1b4/3363c18b-b62c-4a19-9cd4-063d7db0f1b4.glb"
ANGLES = 73                    # points round each half section, bottom centre to top centre
STEP = 0.03                    # station spacing along the car


def tyre_r(w, ar):
    return round((RIM_IN * 25.4 + 2 * w * ar / 100) / 2000, 4)


TYRE_R = round((tyre_r(*TYRE["front"]) + tyre_r(*TYRE["rear"])) / 2, 4)
RIM_R = round(RIM_IN * 25.4 / 2000, 4)


# ---------------------------------------------------------------- loading
def load(path):
    """Triangles of the model grouped by mesh and material, as {group: (n, 3, 3)}."""
    import bpy
    import bmesh
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    out = {}
    for o in bpy.context.scene.objects:
        if o.type != "MESH" or o.name == "Icosphere":
            continue
        bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        mats = [m.name if m else "" for m in o.data.materials]
        part = o.name.split("_Geo")[0].replace("h:", "")
        for f in bm.faces:
            mat = mats[f.material_index] if mats else ""
            if o.name.startswith("polySurface"):
                key = "wheel:" + mat.split("TNRRims_91A18Na")[-1].split("2001")[-1].replace("_Material1", "")
            else:
                key = part + ":" + mat.replace("hHonda_S2000TNR0_2001", "").replace("_Material1", "")
            out.setdefault(key, []).append([v.co[:] for v in f.verts])
        bm.free()
    return {k: np.array(v) for k, v in out.items()}


class Space:
    """Model coordinates (X left, Y back, Z up, about 1:100) to build space at published sizes."""

    def __init__(self, m):
        rim = m["wheel:TNR_Rim91A"].reshape(-1, 3)
        front, rear = rim[rim[:, 1] < 0], rim[rim[:, 1] > 0]
        yf = (front[:, 1].min() + front[:, 1].max()) / 2
        yr = (rear[:, 1].min() + rear[:, 1].max()) / 2
        self.s = WHEELBASE / (yr - yf)
        self.ymid = (yf + yr) / 2
        self.zc = (rim[:, 2].min() + rim[:, 2].max()) / 2
        self.kx = self.ky = 1.0
        paint = self.raw(m["Kit0_Paint:PaintTNR"]).reshape(-1, 3)
        body = paint[~((paint[:, 2] > 0.8) & (np.abs(paint[:, 1]) > 0.8))]          # no mirrors
        over = paint[:, 0].max() - paint[:, 0].min() - WHEELBASE
        self.kx = (LENGTH - WHEELBASE) / over
        self.ky = WIDTH / 2 / np.abs(body[:, 1]).max()

    def raw(self, p):
        p = np.asarray(p, float)
        return np.stack([-(p[..., 1] - self.ymid) * self.s, p[..., 0] * self.s,
                         (p[..., 2] - self.zc) * self.s + TYRE_R], -1)

    def __call__(self, p):
        q = self.raw(p)
        x, h = q[..., 0], WHEELBASE / 2
        q[..., 0] = np.where(x > h, h + (x - h) * self.kx, np.where(x < -h, -h + (x + h) * self.kx, x))
        q[..., 1] *= self.ky
        return q


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
    keys = [tuple(np.round(p, 5)) for p in v]
    index = {}
    for k in keys:
        index.setdefault(k, len(index))
    pts = np.array(list(index))
    idx = np.array([index[k] for k in keys]).reshape(-1, 3)
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


def slice_x(tris, x):
    """Segments where the plane at x cuts the triangles, in (y, z)."""
    d = tris[:, :, 0] - x
    cut = (d.min(1) < 0) & (d.max(1) > 0)
    t, dd = tris[cut], d[cut]
    pts = []
    for i, j in ((0, 1), (1, 2), (2, 0)):
        a, b = dd[:, i], dd[:, j]
        m = (a * b) < 0
        k = np.where(m, a / np.where(m, a - b, 1), 0)[:, None]
        p = t[:, i, 1:] + (t[:, j, 1:] - t[:, i, 1:]) * k
        pts.append(np.where(m[:, None], p, np.nan))
    pts = np.stack(pts, 1)
    segs = [row[~np.isnan(row[:, 0])][:2] for row in pts]
    segs = [s for s in segs if len(s) == 2]
    return np.array(segs) if segs else np.zeros((0, 2, 2))


def polar(segs, zc, th):
    """Farthest hit along each ray from (0, zc) at angles th, on the half y >= 0."""
    s = segs.copy(); s[:, :, 0] = np.abs(s[:, :, 0])
    p, e = s[:, 0], s[:, 1] - s[:, 0]
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


# ---------------------------------------------------------------- groups
PAINT = ["Kit0_Paint:PaintTNR", "Hood0a_Paint:PaintTNR", "Wing0a_Paint:PaintTNR"]
GLASS = "Kit0_Window:Window"
SOFT = "Kit0_Coloured:Coloured"


def mirror_mask(t):
    c = t.mean(1)
    return (c[:, 2] > 0.9) & (np.abs(c[:, 1]) > 0.7) & (c[:, 0] > -0.15) & (c[:, 0] < 0.3)


def lens_mask(t):
    """Head and tail lamp lenses among the glass."""
    c = t.mean(1)
    return (c[:, 0] > 1.45) | (c[:, 0] < -1.62)


def cabin_glass_mask(t):
    c = t.mean(1)
    return (c[:, 0] < 0.75) & (c[:, 0] > -1.4) & (c[:, 2] > 0.84)


def front_holes(paint):
    """The grille and intake openings in the front bumper, as 3D loops."""
    front = paint[paint.mean(1)[:, 0] > 1.7]
    return [l for l in loops3d(front) if np.ptp(l[:, 1]) < 1.0 and np.ptp(l[:, 2]) > 0.05 and l[:, 2].mean() < 0.5]


def patch(loop):
    """A fan of triangles closing a hole flush."""
    c = loop.mean(0)
    return np.array([[c, loop[i], loop[(i + 1) % len(loop)]] for i in range(len(loop))])


def plate_fill(T):
    """A flat face across the rear plate recess, a little short of the bumper around it."""
    p = T("Kit0_ManufacturerPlate:ManufacturerPlateA").reshape(-1, 3)
    y0, y1 = p[:, 1].min() - 0.045, p[:, 1].max() + 0.045
    z0, z1 = p[:, 2].min() - 0.02, p[:, 2].max() + 0.015
    x = p[:, 0].min() - 0.035
    q = np.array([[x, y0, z0], [x, y1, z0], [x, y1, z1], [x, y0, z1]])
    return np.array([[q[0], q[1], q[2]], [q[0], q[2], q[3]]])


# ---------------------------------------------------------------- body sections
def sections(T, arches, open_x):
    paint = T(*PAINT)
    paint = paint[~mirror_mask(paint)]
    g = T(GLASS)
    # the openings and the plate are closed flush, so the skin has no dents there
    fills = [patch(l) for l in front_holes(paint)] + [plate_fill(T)]
    tris = np.vstack([paint, g[lens_mask(g)]] + fills)
    xs_all = tris[:, :, 0]
    nose, tail = xs_all.max(), xs_all.min()
    xs = np.concatenate([[nose - 0.004, nose - 0.012], np.arange(nose - 0.025, tail + 0.02, -STEP),
                         [tail + 0.012, tail + 0.004]])
    th = np.linspace(-np.pi / 2, np.pi / 2, ANGLES)
    in_arch = lambda x: any(abs(x - wx) < ar + 0.02 for wx, ar in arches)
    cuts = [(x, slice_x(tris, x)) for x in xs]
    cuts = [(x, sg) for x, sg in cuts if len(sg) >= 3]
    xs = np.array([x for x, _ in cuts])
    zlo = np.array([sg[:, :, 1].min() for _, sg in cuts])
    zhi = np.array([sg[:, :, 1].max() for _, sg in cuts])
    ok = np.array([not in_arch(x) for x in xs])
    zlo = np.interp(-xs, -xs[ok], zlo[ok])              # the arches have no floor of their own
    zcs = (zlo + zhi) / 2
    top = TYRE_R + max(ar for _w, ar in arches)
    rows = []
    for x, (_x, segs), zc, lo, hi in zip(xs, cuts, zcs, zlo, zhi):
        r = polar(segs, zc, np.clip(th, -np.pi / 2 + 0.01, np.pi / 2 - 0.01))
        if in_arch(x):
            # the side is open round the wheel: fill the lower half from the stations either side
            zz = zc + r * np.sin(th)
            r[(th < 0) & (np.isnan(r) | (zz < top))] = -1
        floor = np.isnan(r) & (th < -0.05)
        r[floor] = (lo - zc) / np.sin(th[floor])
        if open_x[0] < x < open_x[1]:                    # open cockpit: closed flat at the door tops
            ymax = np.nanmax(r * np.cos(th))
            lid = np.isnan(r) & (th > 0.05)
            r[lid] = np.minimum((hi - zc) / np.sin(th[lid]), ymax / np.cos(th[lid]))
        rows.append([x, zc, r])
    X = np.array([row[0] for row in rows]); ZC = np.array([row[1] for row in rows])
    Rm = np.array([row[2] for row in rows])
    Rm[Rm < 0] = np.nan
    for j in range(ANGLES):                       # fill across the arches and any gaps
        col = Rm[:, j]; good = ~np.isnan(col)
        Rm[:, j] = np.interp(-X, -X[good], col[good])
    k = 6                                         # the top and bottom centre lie flat
    for j in list(range(k)) + list(range(ANGLES - k, ANGLES)):
        ref = k if j < k else ANGLES - 1 - k
        z = ZC + Rm[:, ref] * np.sin(th[ref])
        Rm[:, j] = (z - ZC) / np.sin(th[j])
    from scipy.ndimage import gaussian_filter, median_filter
    Rm = median_filter(Rm, size=(3, 5), mode="nearest")
    Rm = gaussian_filter(Rm, sigma=(1.0, 1.2), mode="nearest")
    # the bumpers carry recesses and lips the skin should not copy: smooth them harder
    ends = (X > X.max() - 0.3) | (X < X.min() + 0.35)
    soft = gaussian_filter(Rm, sigma=(2.0, 3.0), mode="nearest")
    Rm[ends] = soft[ends]
    ZC = median_filter(ZC, size=5, mode="nearest")
    return [dict(x=round(float(x), 4), zc=round(float(zc), 4), r=r4(r)) for x, zc, r in zip(X, ZC, Rm)]


def arch_radii(T, wheel_x):
    """Arch radius round each wheel centre, from the body paint at the sides."""
    v = T(*PAINT).reshape(-1, 3)
    out = []
    for wx in wheel_x:
        near = v[(np.abs(v[:, 1]) > 0.6) & (np.abs(v[:, 0] - wx) < 0.6) & (v[:, 2] < TYRE_R + 0.6)]
        d = np.hypot(near[:, 0] - wx, near[:, 2] - TYRE_R)
        out.append((wx, round(float(np.percentile(d, 0.5)), 4)))
    return out


# ---------------------------------------------------------------- roof
def roof_sections(T, belt):
    """The soft top and its glass above the belt, as upper half sections round (0, belt)."""
    soft = T(SOFT)
    c = soft.mean(1)
    soft = soft[(c[:, 2] > belt - 0.02) & (c[:, 0] < 0.1) & (c[:, 0] > -1.5) & (np.abs(c[:, 1]) < 0.8)]
    g = T(GLASS)
    tris = np.vstack([soft, g[cabin_glass_mask(g)]])
    x0, x1 = tris[:, :, 0].min(), tris[:, :, 0].max()
    th = np.linspace(0.02, np.pi / 2 - 0.01, 37)
    out = []
    for x in np.arange(x1 - 0.01, x0, -0.025):
        sg = slice_x(tris, x)
        if len(sg) < 3:
            continue
        r = polar(sg, belt, th)
        if np.isnan(r).mean() > 0.3:
            continue
        out.append(dict(x=round(float(x), 4), r=r4(r)))
    return out, round(float(x0), 4), round(float(x1), 4)


def plane_shape(tris):
    """A flat outline of a gently curved sheet: its best fit plane (origin, u, v, outward
    normal) and its outline in that plane."""
    from shapely.geometry import Polygon
    from shapely.ops import unary_union
    v = tris.reshape(-1, 3)
    o = v.mean(0)
    _s, _w, vt = np.linalg.svd(v - o, full_matrices=False)
    n = vt[2]
    if np.dot(n, o - np.array([0, 0, 0.5])) < 0:          # outward, away from the car's middle
        n = -n
    u = np.cross([0, 0, 1.0], n); u /= np.linalg.norm(u)
    w = np.cross(n, u)
    flat = [Polygon(np.column_stack([(t - o) @ u, (t - o) @ w])) for t in tris]
    g = unary_union([p.buffer(2e-4) for p in flat if p.area > 1e-9])
    return {"o": r4(o), "u": r4(u), "v": r4(w), "n": r4(n), "outline": r4(outline(g, 0.002))}


# The door shut line, picked by eye along the gaps of the left door in a side view of the
# model's paint coloured by facing: from the top front, down, along the sill, up the back.
DOOR = [(0.24, 0.895), (0.31, 0.84), (0.33, 0.76), (0.335, 0.6), (0.333, 0.4), (0.33, 0.33),
        (0.31, 0.305), (0.0, 0.307), (-0.3, 0.31), (-0.6, 0.315), (-0.64, 0.33), (-0.68, 0.42),
        (-0.73, 0.58), (-0.77, 0.72), (-0.79, 0.8), (-0.785, 0.86), (-0.76, 0.9)]


def measure(path):
    from shapely.geometry import box
    from shapely.ops import unary_union
    m = load(path)
    sp = Space(m)
    T = lambda *names: sp(np.vstack([m[k] for k in names]))
    left = lambda t: t[t[:, :, 1].mean(1) > 0]
    wheel_x = [WHEELBASE / 2, -WHEELBASE / 2]
    arch = arch_radii(T, wheel_x)
    g = T(GLASS)
    cg = g[cabin_glass_mask(g)].reshape(-1, 3)
    stations = sections(T, arch, (float(cg[:, 0].min()) - 0.1, float(cg[:, 0].max())))

    paint = T(*PAINT)
    paint = paint[~mirror_mask(paint)]
    pv = paint.reshape(-1, 3)
    glass = T(GLASS)
    cab = glass[cabin_glass_mask(glass)]

    # windscreen: the glass ahead of the door glass; side glass: the door glass
    c = cab.mean(1)
    screen_t = cab[(np.abs(c[:, 1]) < 0.66) & (c[:, 0] > 0.0)]
    side_t = left(cab[(np.abs(c[:, 1]) > 0.6) & (c[:, 0] < 0.25) & (c[:, 0] > -1.0)])
    rear_t = cab[c[:, 0] < -0.95]
    screen = outline(project(screen_t, 0, 1))
    sv = screen_t.reshape(-1, 3)
    centre = sv[np.abs(sv[:, 1]) < 0.05]
    side = outline(project(side_t, 0, 2))
    rear_g = outline(project(rear_t, 1, 2))

    # belt: the door top, along the cockpit
    tops = []
    for x in np.arange(-0.5, 0.4, 0.02):
        q = pv[(np.abs(pv[:, 0] - x) < 0.01) & (np.abs(pv[:, 1]) > 0.6)]
        if len(q):
            tops.append(q[:, 2].max())
    door_top = np.median(tops)
    roof, roof_x0, roof_x1 = roof_sections(T, float(door_top))

    # cockpit opening, seen from above: inside the door tops and deck
    cockpit = []
    for x in np.arange(0.45, -1.3, -0.05):
        band = pv[(np.abs(pv[:, 0] - x) < 0.03) & (pv[:, 2] > door_top - 0.07)]
        if len(band) == 0:
            continue
        cockpit.append([round(float(x), 4), round(float(np.abs(band[:, 1]).min()), 4)])

    # lamps, each a flat outline in its own plane, on the car's left
    inner = T("Kit0_WindowInside:orange_glass")
    head = plane_shape(left(glass[glass.mean(1)[:, 0] > 1.45]))
    head_amber = plane_shape(left(inner[inner.mean(1)[:, 0] > 1.3]))
    tail = plane_shape(left(glass[glass.mean(1)[:, 0] < -1.62]))
    tail_amber = plane_shape(left(inner[inner.mean(1)[:, 0] < -1.6]))
    tail_red = plane_shape(left(T("Kit0_WindowInside:red_glass")))
    marker = plane_shape(left(T("Kit0_Window:orange_glass")))
    brake3 = outline(project(T("Kit0_Window:red_glass"), 1, 2), 0.002)

    # front openings: holes in the bumper's paint, seen from ahead (y, z)
    holes = front_holes(paint)
    grille = max(holes, key=lambda l: np.ptp(l[:, 1]))
    intake = [l for l in holes if l[:, 1].mean() > 0.3][0]

    # mirror: half width slices of the housing, out from the door
    mt = T(*PAINT)
    mt = left(mt[mirror_mask(mt)]).reshape(-1, 3)
    mirror = []
    for y in np.arange(mt[:, 1].min() + 0.005, mt[:, 1].max() - 0.004, 0.012):
        q = mt[np.abs(mt[:, 1] - y) < 0.008]
        if len(q) > 4:
            mirror.append(r4([y, q[:, 0].min(), q[:, 0].max(), q[:, 2].min(), q[:, 2].max()]))

    # panels: bonnet and boot lid seen from above
    bonnet = outline(project(T("Hood0a_Paint:PaintTNR"), 0, 1), 0.002)
    boot = outline(project(T("Wing0a_Paint:PaintTNR"), 0, 1), 0.002)

    # exhaust tips: the two round ends under the rear bumper
    ex = T(SOFT)
    ex = ex[(ex.mean(1)[:, 0] < -1.95) & (ex.mean(1)[:, 2] < 0.35)].reshape(-1, 3)
    tips = ex[np.abs(ex[:, 1]) > 0.38]
    tl = tips[tips[:, 1] > 0]
    exhaust = {"x": round(float(tips[:, 0].min()), 4), "y": round(float((tl[:, 1].min() + tl[:, 1].max()) / 2), 4),
               "z": round(float((tl[:, 2].min() + tl[:, 2].max()) / 2), 4),
               "r": round(float(np.ptp(tl[:, 2]) / 2), 4)}

    # option spots, from the model's own badges
    bd = T("Kit0_Badge:BadgeA")
    bc = bd.mean(1)
    fb = bd[bc[:, 0] > 1.5].reshape(-1, 3)
    rb = bd[bc[:, 0] < -1.5].reshape(-1, 3)
    sb = left(bd[np.abs(bc[:, 0]) < 1.5]).reshape(-1, 3)
    mid = lambda a: round(float((a.min() + a.max()) / 2), 4)
    spots = {"front": [mid(fb[:, 1]), mid(fb[:, 2])], "rear": [mid(rb[:, 1]), mid(rb[:, 2])],
             "side": [mid(sb[:, 0]), mid(sb[:, 2])], "side_h": round(float(np.ptp(sb[:, 2])), 4),
             "bonnet_x": round(float(bonnet[:, 0].max() - 0.12), 4),
             "rear_low_z": round(float(T("Kit0_ManufacturerPlate:ManufacturerPlateA")[:, :, 2].max() + 0.04), 4),
             "boot_right": [round(float(-0.45), 4), round(float(spots_z(T)), 4)]}

    return {
        "name": "ToraHanabi",
        "wheel": {"r": TYRE_R, "rim_r": RIM_R,
                  "front": {"x": wheel_x[0], "width": TYRE["front"][0] / 1000, "track": TRACK["front"]},
                  "rear": {"x": wheel_x[1], "width": TYRE["rear"][0] / 1000, "track": TRACK["rear"]},
                  "arch_r": [a for _x, a in arch]},
        "stations": stations,
        "belt": round(float(door_top), 4),
        "roof": {"x0": roof_x0, "x1": roof_x1, "sections": roof},
        "cockpit": cockpit,
        "screen": {"outline": r4(screen), "x_base": round(float(centre[:, 0].max()), 4),
                   "x_top": round(float(centre[:, 0].min()), 4)},
        "side_glass": r4(side),
        "rear_glass": r4(rear_g),
        "head": head, "head_amber": head_amber,
        "tail": tail, "tail_amber": tail_amber, "tail_red": tail_red,
        "marker": marker, "brake3": r4(brake3),
        "grille": r4(grille[:, 1:]), "intake": r4(intake[:, 1:]),
        "mirror": mirror,
        "door": DOOR,
        "bonnet": r4(bonnet), "boot": r4(boot),
        "exhaust": exhaust,
        "spots": spots,
        # Ivan's picks from the viewer.
        "picks": {"roof": 1, "frontbadge": 1, "rearbadge": 1, "name": 2},
    }


def spots_z(T):
    """Height of the boot lid's back face, between the brake light and the lid's lower edge."""
    w = T("Wing0a_Paint:PaintTNR").reshape(-1, 3)
    back = w[w[:, 0] < w[:, 0].min() + 0.03]
    return (back[:, 2].min() + back[:, 2].max()) / 2


if __name__ == "__main__":
    rows = measure(sys.argv[1] if len(sys.argv) > 1 else GLB)
    out = Path(__file__).with_name("rows.json")
    out.write_text(json.dumps(rows, indent=1))
    st = rows["stations"]
    print("wrote", out, len(st), "stations, nose", st[0]["x"], "tail", st[-1]["x"],
          "arches", rows["wheel"]["arch_r"], "belt", rows["belt"])
