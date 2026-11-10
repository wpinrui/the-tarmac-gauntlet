"""Measured rows for the Kaiser Oboe, based on the 2009 VW Golf GTI 3-door (Mk5).

Reference: Ivan's 3D model of the Golf GTI (a .glb, kept out of the repo). Its renders and
measurements replace a drawing. Every row here is measured from it:
  - body sections: the outer skin cut across at stations along the car, read as a radius at
    even angles round a centre point, so every station has the same number of points;
  - glass, lamps, grille, shut lines, mirror, spoiler and the option spots: outlines of the
    matching panels, seen from the side, front, rear or top, or as 3D loops for the lamps.
The model is scaled so its wheelbase is the published 2578 mm, and sits on the ground one
published tyre radius (225/45 R17) under the wheel centres.
Build space: metres, x forward from mid wheelbase, y left, z up from the ground.

Run: python3 rows.py [path/to/golf.glb]  -> rows.json
"""
import json
import sys
from pathlib import Path

import numpy as np

WHEELBASE = 2.578
TYRE_R = round((17 * 25.4 + 2 * 0.45 * 225) / 2000, 4)
RIM_R = round(17 * 25.4 / 2000, 4)
GLB = "/mnt/project-files/vw_golf_v_gti_grand_turismo_5.glb"
ANGLES = 73                    # points round each half section, bottom centre to top centre
STEP = 0.03                    # station spacing along the car

# The model's panels, by mesh name.
TYRES = ["Tubo_Nuovo_0", "Tubo.2_Nuovo_0", "Tubo.3_Nuovo_0", "Tubo.4_Nuovo_0"]
PAINT = ["w177_body_w177_paint_0", "w177_body_w177_roof_0", "Poligono_w177_paint_0",
         "w177_tailgate_a_w177_paint_0", "w177_fender_FL_w177_paint_0", "w177_fender_FR_w177_paint_0",
         "w177_door_FL_w177_paint_0", "Poligono.4_w177_paint_0", "w177_paint_door_L_w177_paint_0",
         "w177_paint_door_R_w177_paint_0", "w177_paint_w177_paint_0", "w177_bumper_gti_w177_paint_0",
         "w177_bumper_GTI_R_w177_paint_0"]
GLASS = ["w177_windshield_vivace_glass_0", "w177_doorglass_FL_vivace_glass_0", "w177_doorglass_FR_vivace_glass_0",
         "w177_sideglass_RL_vivace_glass_0", "w177_sideglass_RR_vivace_glass_0", "w177_tailgateglass_vivace_glass_0"]
LAMPS = ["w177_headlightglass_R_vivace_glass_0", "w177_headlightglass_L_vivace_glass_0",
         "w177_taillight_L_vivace_glass_0", "w177_taillight_R_vivace_glass_0",
         "w177_tailgate_a_lightglass_L_vivace_glass_0", "w177_tailgate_a_lightglass_R_vivace_glass_0"]
BLACK = ["w177_bumper_gti_etk800.1_0", "w177_bumper_gti1_etk800.1_0", "w177_bumper_GTI_R3_Nuovo.8_0"]
SHELL = PAINT + GLASS + LAMPS + BLACK


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
    """Model coordinates (X right of the car's left, Y back, Z up) to build space."""

    def __init__(self, m):
        c = {k: (m[k].reshape(-1, 3).min(0) + m[k].reshape(-1, 3).max(0)) / 2 for k in TYRES}
        front = [v for v in c.values() if v[1] < 0]
        rear = [v for v in c.values() if v[1] > 0]
        yf, yr = np.mean([v[1] for v in front]), np.mean([v[1] for v in rear])
        self.s = WHEELBASE / (yr - yf)
        self.ymid = (yf + yr) / 2
        self.zc = np.mean([v[2] for v in c.values()])
        tyre = np.vstack([m[k].reshape(-1, 3) for k in TYRES])
        self.outer_y = float(np.abs(tyre[:, 0]).max() * self.s)

    def __call__(self, p):
        p = np.asarray(p, float)
        return np.stack([-(p[..., 1] - self.ymid) * self.s, p[..., 0] * self.s,
                         (p[..., 2] - self.zc) * self.s + TYRE_R], -1)


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
    if g.geom_type != "Polygon":
        g = max(g.geoms, key=lambda p: p.area)
    from shapely.geometry import Polygon
    return np.array(Polygon(g.exterior).simplify(simplify).exterior.coords)[:-1]


def loops3d(tris):
    """Boundary loops of a triangle sheet, in 3D, longest first."""
    v = tris.reshape(-1, 3)
    key = {tuple(np.round(p, 5)): None for p in v}
    for i, k in enumerate(key):
        key[k] = i
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
    pts = np.stack(pts, 1)                                   # (n, 3, 2), two of three valid
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
    for i, a in enumerate(th):
        d = np.array([np.cos(a), np.sin(a)])
        o = np.array([0.0, zc])
        den = d[0] * e[:, 1] - d[1] * e[:, 0]
        ok = np.abs(den) > 1e-12
        w = p - o
        t = np.where(ok, (w[:, 0] * e[:, 1] - w[:, 1] * e[:, 0]) / np.where(ok, den, 1), -1)
        u = np.where(ok, (w[:, 0] * d[1] - w[:, 1] * d[0]) / np.where(ok, den, 1), -1)
        hit = ok & (t > 0) & (u >= 0) & (u <= 1)
        if hit.any():
            r[i] = t[hit].max()
    return r


def sections(sp, m, arches):
    tris = sp(np.vstack([m[k] for k in SHELL]))
    # the roof spoiler is its own part, so keep it out of the skin
    tris = tris[~spoiler_mask(sp, m, tris)]
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
        r[miss] = (lo - zc) / np.sin(th[miss])
        rows.append([x, zc, r])
    X = np.array([row[0] for row in rows]); ZC = np.array([row[1] for row in rows])
    Rm = np.array([row[2] for row in rows])
    Rm[Rm < 0] = np.nan
    for j in range(ANGLES):                       # fill across the arches and any gaps
        col = Rm[:, j]; ok = ~np.isnan(col)
        if ok.sum() >= 2:
            Rm[:, j] = np.interp(X, X[ok][::-1], col[ok][::-1]) if X[0] > X[-1] else np.interp(X, X[ok], col[ok])
    # the top and bottom centre lie flat: no spike or notch where the halves meet
    k = 6
    for j in list(range(k)) + list(range(ANGLES - k, ANGLES)):
        ref = k if j < k else ANGLES - 1 - k
        z = ZC + Rm[:, ref] * np.sin(th[ref])
        Rm[:, j] = (z - ZC) / np.sin(th[j])
    from scipy.ndimage import median_filter
    Rm = median_filter(Rm, size=(3, 5), mode="nearest")
    from scipy.ndimage import gaussian_filter
    Rm = gaussian_filter(Rm, sigma=(1.0, 1.2), mode="nearest")      # smooth panels, no ripples
    ZC = median_filter(ZC, size=5, mode="nearest")
    return [dict(x=round(float(x), 4), zc=round(float(zc), 4), r=r4(r)) for x, zc, r in zip(X, ZC, Rm)]



def spoiler_mask(sp, m, tris):
    """Which triangles stand above the rear glass in side view: the roof spoiler."""
    g = sp(m["w177_tailgateglass_vivace_glass_0"]).reshape(-1, 3)
    top = g[np.argmax(g[:, 2])]; bot = g[np.argmin(g[:, 2])]
    c = tris.mean(1)
    line = top[2] + (c[:, 0] - top[0]) * (bot[2] - top[2]) / (bot[0] - top[0])
    return (c[:, 0] < top[0] + 0.03) & (c[:, 2] > line + 0.012) & (c[:, 2] > top[2] - 0.12)


def spoiler_tris(sp, m):
    t = sp(np.vstack([m[k] for k in ("w177_body_w177_paint_0", "w177_tailgate_a_w177_paint_0",
                                     "w177_body_w177_roof_0")]))
    return t[spoiler_mask(sp, m, t)]


def arches(sp, m, wheel_x):
    """Arch radius round each wheel centre, from the body paint at the sides."""
    v = sp(np.vstack([m[k] for k in PAINT + BLACK])).reshape(-1, 3)
    out = []
    for wx in wheel_x:
        near = v[(np.abs(v[:, 1]) > 0.6) & (np.abs(v[:, 0] - wx) < 0.6) & (v[:, 2] < TYRE_R + 0.6)]
        d = np.hypot(near[:, 0] - wx, near[:, 2] - TYRE_R)
        out.append((wx, round(float(np.percentile(d, 0.5)), 4)))
    return out


# ---------------------------------------------------------------- the rows
def plain_tail(stations, length=0.22):
    """Ivan wants no plate bulge under the tailgate: over the last stretch each section is the
    section where it starts, shrunk to the measured size, so the tail rounds off plainly."""
    st = sorted(stations, key=lambda t: t["x"])
    x_end = st[0]["x"]
    ref = min((t for t in st if t["x"] >= x_end + length), key=lambda t: t["x"])
    r_ref = np.asarray(ref["r"])
    for t in st:
        if t["x"] < ref["x"]:
            k = float(np.mean(t["r"]) / np.mean(r_ref))
            t["r"] = [round(float(v), 4) for v in r_ref * k]
    return stations


def measure(path):
    from shapely.geometry import MultiPoint, Polygon, box
    from shapely.ops import unary_union
    m = load(path)
    sp = Space(m)
    T = lambda *names: sp(np.vstack([m[k] for k in names]))
    left = lambda t: t[t[:, :, 1].mean(1) > 0]
    right = lambda t: t[t[:, :, 1].mean(1) < 0]
    wheel_x = [WHEELBASE / 2, -WHEELBASE / 2]
    arch = arches(sp, m, wheel_x)
    stations = plain_tail(sections(sp, m, arch))

    # glass: side (x, z), screen top (x, y), rear (y, z)
    door_g = project(left(T("w177_doorglass_FL_vivace_glass_0")), 0, 2)
    quarter_g = project(left(T("w177_sideglass_RL_vivace_glass_0")), 0, 2)
    dlo = unary_union([door_g, quarter_g]).buffer(0.09, join_style=2).buffer(-0.09, join_style=2)
    mid_z = (dlo.bounds[1] + dlo.bounds[3]) / 2
    band = box(-5, mid_z - 0.05, 5, mid_z + 0.05)
    b_pillar = [door_g.intersection(band).bounds[0] + 0.03, quarter_g.intersection(band).bounds[2] - 0.03]
    screen = project(T("w177_windshield_vivace_glass_0"), 0, 1).buffer(-0.008)
    so = outline(screen)
    centre = so[np.abs(so[:, 1]) < 0.05]
    rear_glass = outline(project(T("w177_tailgateglass_vivace_glass_0"), 1, 2).buffer(-0.006))

    # lamps as 3D loops on the car's left; the lenses as centre and radius
    head = [r4(even_loop(l, 0.01)) for l in loops3d(left(T("w177_headlightglass_L_vivace_glass_0")))[:1]]
    tail = [r4(even_loop(l, 0.01)) for k in ("w177_taillight_L_vivace_glass_0", "w177_tailgate_a_lightglass_L_vivace_glass_0")
            for l in loops3d(T(k))[:1]]
    lenses = []
    for k in ("w177_headlight_L_w177_lowbeam_0", "w177_headlight_L_w177_highbeam_0"):
        v = T(k).reshape(-1, 3)
        c = (v.min(0) + v.max(0)) / 2
        lenses.append(r4(list(c) + [max(v[:, 1].max() - v[:, 1].min(), v[:, 2].max() - v[:, 2].min()) / 2 + 0.008]))

    # front, seen from ahead (y, z), car's right where a side is meant
    black = right(T("w177_bumper_gti_etk800.1_0"))
    blk = project(black, 1, 2).buffer(0.012).buffer(-0.012)
    pieces = list(getattr(blk, "geoms", [blk]))
    mid = [p for p in pieces if p.centroid.x > -0.4 and p.area > 1e-3]
    side = [p for p in pieces if p.centroid.x <= -0.4 and p.area > 1e-3]
    shield_half = unary_union(mid).convex_hull.intersection(box(-1, 0, 0, 3))
    shield = unary_union([shield_half, Polygon([(-y, z) for y, z in shield_half.exterior.coords])]).buffer(0.002)
    intake = unary_union(side).buffer(0.004)
    plate = project(T("w177_bumper_gti_licenseplate-52-11_0"), 1, 2).envelope.buffer(0.012, join_style=2)
    chin = project(T("w177_bumper_gti1_etk800.1_0"), 1, 2).buffer(0.01).buffer(-0.01)
    fogs = project(right(T("w177_bumper_gti_vivace_glass_0")), 1, 2)
    fg = max(getattr(fogs, "geoms", [fogs]), key=lambda p: p.area)
    fog = [round(fg.centroid.x, 4), round(fg.centroid.y, 4), round(np.sqrt(fg.area / np.pi), 4)]

    # rear
    rl = T("w177_bumper_GTI_R3_Nuovo.8_0").reshape(-1, 3)
    ctr = rl[np.abs(rl[:, 1]) < 0.3]
    sidepart = rl[np.abs(rl[:, 1]) > 0.8]
    rear_lower = {"z": round(float(ctr[:, 2].max()), 4), "x": round(float(sidepart[:, 0].max()), 4)}
    tips = T("w177_bumper_GTI_R_vivace.1_0").reshape(-1, 3)
    ys = np.sort(tips[:, 1]); half = (ys[0] + ys[-1]) / 2
    ex_r = round(float((tips[:, 2].max() - tips[:, 2].min()) / 2), 4)
    exhaust = {"x": round(float(tips[:, 0].min()), 4), "y": [round(float((ys[0] + half) / 2), 4), round(float((half + ys[-1]) / 2), 4)],
               "z": round(float((tips[:, 2].max() + tips[:, 2].min()) / 2), 4), "r": ex_r}

    # spoiler: side profile and half width; third brake light on its back face
    st = spoiler_tris(sp, m)
    prof = outline(project(st, 0, 2).buffer(0.006).buffer(-0.006), 0.004)
    back = prof[prof[:, 0] < prof[:, 0].min() + 0.03]
    bz0, bz1 = back[:, 1].min(), back[:, 1].max()
    bm = (bz0 + bz1) / 2
    brake3 = [[0.17, bm + 0.009], [-0.17, bm + 0.009], [-0.17, bm - 0.009], [0.17, bm - 0.009]]

    # side trim and mirror
    strip = outline(project(left(T("w177_paint_door_L_w177_paint_0", "w177_paint_w177_paint_0")), 0, 2)
                    .buffer(0.004).buffer(-0.004))
    mv = left(T("w177_mirror_L_paint_w177_paint_0")).reshape(-1, 3)
    mirror = {"x_back": round(float(mv[:, 0].min()), 4), "length": round(float(np.ptp(mv[:, 0])), 4),
              "y_in": round(float(mv[:, 1].min()), 4), "y_out": round(float(mv[:, 1].max()), 4),
              "z": round(float((mv[:, 2].min() + mv[:, 2].max()) / 2), 4),
              "h_in": round(float(np.ptp(mv[:, 2]) * 0.6), 4), "h_out": round(float(np.ptp(mv[:, 2])), 4)}
    sail = outline(project(left(T("w177_mirror_L_paint_Nuovo.8_0")), 0, 2).convex_hull.intersection(dlo.buffer(0.03)))

    # shut lines
    door_o = outline(project(left(T("w177_door_FL_w177_paint_0")), 0, 2), 0.002)
    keep = np.array([not dlo.buffer(0.012).contains(MultiPoint([p]).geoms[0]) for p in door_o])
    k0 = int(np.argmax(~keep))                          # start the open path just after the glass
    rolled = np.roll(door_o, -k0, axis=0); kr = np.roll(keep, -k0)
    door = rolled[kr]
    bonnet = outline(project(T("Poligono_w177_paint_0"), 0, 1), 0.002)
    tg = outline(project(T("w177_tailgate_a_w177_paint_0"), 1, 2), 0.002)
    top_cut = rear_glass[:, 1].max() - 0.01
    kt = tg[:, 1] < top_cut
    k0 = int(np.argmax(~kt))
    tailgate = np.roll(tg, -k0, axis=0)[np.roll(kt, -k0)]

    # option spots
    vw_f = T("w177_bumper_gti_vivace.1_0").reshape(-1, 3)
    vw_r = T("w177_tailgate_a_etk800.1_0").reshape(-1, 3)
    gti = T("w177_lettering_GTI_etk800.1_0").reshape(-1, 3)
    bon = T("Poligono_w177_paint_0").reshape(-1, 3)
    spots = {"grille_z": round(float(np.median(vw_f[:, 2])), 4) if len(vw_f) else 0.65,
             "bonnet_x": round(float(bon[:, 0].max() - 0.2), 4),
             "tailgate_z": round(float((vw_r[:, 2].min() + vw_r[:, 2].max()) / 2), 4),
             "tailgate_low_z": round(float((gti[:, 2].min() + gti[:, 2].max()) / 2), 4),
             "name_y": round(float((gti[:, 1].min() + gti[:, 1].max()) / 2), 4),
             "name_z": round(float((gti[:, 2].min() + gti[:, 2].max()) / 2), 4),
             "quarter_x": round(float((b_pillar[1] + wheel_x[1] + arch[1][1]) / 2), 4),
             "quarter_z": round(float(strip[:, 1].max() + 0.12), 4)}

    return {
        "name": "KaiserOboe",
        "wheel": {"r": TYRE_R, "rim_r": RIM_R, "width": 0.225, "outer_y": round(sp.outer_y, 4),
                  "front_x": wheel_x[0], "rear_x": wheel_x[1], "arch_r": round(max(a for _x, a in arch), 4)},
        "stations": stations,
        "dlo": r4(outline(dlo)),
        "b_pillar": r4(b_pillar),
        "sail": r4(sail),
        "screen": {"outline": r4(so), "x_base": round(float(centre[:, 0].max()), 4),
                   "x_top": round(float(centre[:, 0].min()), 4)},
        "rear_glass": r4(rear_glass),
        "head": head,
        "head_lenses": lenses,
        "shield": r4(outline(shield)),
        "shield_bar": r4(outline(plate)),
        "intake": r4(outline(intake)),
        "fog": fog,
        "chin": r4(outline(chin)),
        "tail": tail,
        "brake3": r4(brake3),
        "spoiler": {"profile": r4(prof), "half": round(float(np.abs(st[:, :, 1]).max()), 4)},
        "rear_lower": rear_lower,
        "exhaust": exhaust,
        "strip": r4(strip),
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
