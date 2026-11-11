"""The Hanabi's parts: lamps, openings, glass, roof options, mirrors, shut lines, interior,
wheels, badge and name. Every shape and size here is the Hanabi's own, from rows.json and
Ivan's S2000 model. Runs inside Blender's Python module, called by export.py."""
import json
import sys
from pathlib import Path

import numpy as np
from shapely.affinity import scale as sscale, translate
from shapely.geometry import LineString, Point, Polygon, box
from shapely.ops import unary_union

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "common"))
import blend  # noqa: E402
import geom  # noqa: E402
import body  # noqa: E402

HERE = Path(__file__).resolve().parent
R = body.ROWS
NOSE, TAIL = body.NOSE, body.TAIL
W = R["wheel"]
BELT = R["belt"]

MATERIALS = {
    # name: (rgb, spec, alpha, emit)
    "body": ((0.97, 0.56, 0.05), 0.75, 1.0, None),
    "trim": ((0.025, 0.025, 0.03), 0.6, 1.0, None),
    "grille": ((0.04, 0.04, 0.045), 0.3, 1.0, None),
    "softtop": ((0.07, 0.07, 0.075), 0.08, 1.0, None),
    "glass": ((0.07, 0.09, 0.11), 0.92, 0.45, None),
    "head_glass": ((0.9, 0.93, 0.96), 0.95, 0.25, None),
    "tail_glass": ((0.35, 0.05, 0.06), 0.92, 0.45, None),
    "housing": ((0.045, 0.045, 0.05), 0.5, 1.0, None),
    "reflector": ((0.78, 0.79, 0.81), 0.97, 1.0, None),
    "amber": ((1.0, 0.5, 0.08), 0.85, 0.9, None),
    "lamp_head": ((0.95, 0.95, 0.92), 0.6, 1.0, (1.0, 0.97, 0.9)),
    "lamp_tail": ((0.55, 0.04, 0.05), 0.5, 1.0, (1.0, 0.06, 0.04)),
    "lamp_brake": ((0.62, 0.04, 0.05), 0.5, 1.0, (1.0, 0.04, 0.03)),
    "chrome": ((0.86, 0.87, 0.89), 0.98, 1.0, None),
    "badge_orange": ((0.93, 0.42, 0.12), 0.7, 1.0, None),
    "badge_black": ((0.09, 0.075, 0.06), 0.7, 1.0, None),
    "badge_cream": ((0.95, 0.91, 0.85), 0.7, 1.0, None),
    "interior": ((0.09, 0.09, 0.1), 0.15, 1.0, None),
    "seat": ((0.12, 0.12, 0.13), 0.12, 1.0, None),
    "mirror_glass": ((0.62, 0.67, 0.72), 0.98, 1.0, None),
    "tyre": ((0.06, 0.06, 0.065), 0.1, 1.0, None),
    "rim": ((0.66, 0.67, 0.69), 0.9, 1.0, None),
}
LAMPS = {"Headlights": ["lamp_head"], "Tail lights": ["lamp_tail"], "Brake lights": ["lamp_brake"]}
GROUPS = {"roof": "Roof", "frontbadge": "Front badge", "rearbadge": "Rear badge", "name": "Name"}


# ---------------------------------------------------------------- frames
def front_fr():
    return blend.frame((NOSE, 0, 0), (0, 1, 0), (0, 0, 1), (-1, 0, 0))


def rear_fr():
    return blend.frame((TAIL, 0, 0), (0, 1, 0), (0, 0, 1), (1, 0, 0))


def side_fr(sgn):
    return blend.frame((0, 0.7 * sgn, 0), (1, 0, 0), (0, 0, 1), (0, -sgn, 0))


def top_fr():
    return blend.frame((0, 0, 1.6), (1, 0, 0), (0, 1, 0), (0, 0, -1))


def plane_fr(p, sgn=1):
    """The frame of a lamp's own plane; sgn -1 mirrors it to the car's right."""
    k = np.array([1, sgn, 1.0])
    o, u, v, n = (np.asarray(p[a]) * k for a in ("o", "u", "v", "n"))
    return blend.frame(o, u, v, -n)


def poly(pts):
    return Polygon(pts).buffer(0)


def smooth2d(g, r):
    """Round off small notches and spurs."""
    return g.buffer(r, join_style=1).buffer(-2 * r, join_style=1).buffer(r, join_style=1)


def in_plane(src, dst):
    """src's outline carried into dst's plane coordinates."""
    o, u, v = (np.asarray(src[a]) for a in ("o", "u", "v"))
    O, U, V = (np.asarray(dst[a]) for a in ("o", "u", "v"))
    pts = [o + u * a + v * b for a, b in src["outline"]]
    return poly([((p - O) @ U, (p - O) @ V) for p in pts])


def to_plane(p3, dst):
    O, U, V = (np.asarray(dst[a]) for a in ("o", "u", "v"))
    p = np.asarray(p3) - O
    return float(p @ U), float(p @ V)


# ---------------------------------------------------------------- lamps
GAP = 0.0015      # in-plane clearance between neighbouring pieces


def apart(pieces):
    """Each shape less the ones before it, kept GAP clear, so laid pieces never touch."""
    out, taken = [], None
    for g in pieces:
        g = g if taken is None else g.difference(taken.buffer(GAP))
        out.append(g)
        taken = g if taken is None else taken.union(g)
    return out


def headlamps(ref, cutters, out):
    h = R["head"]
    shp = smooth2d(poly(h["outline"]), 0.004)
    amber = in_plane(R["head_amber"], h).intersection(shp.buffer(-0.006))
    rest = shp.buffer(-0.008).difference(amber.buffer(0.01))
    # one projector, set in the widest part of the lamp ahead of the amber corner
    c = rest.buffer(-0.02).centroid if not rest.buffer(-0.02).is_empty else rest.centroid
    rad = min(0.034, max(0.018, 0.9 * rest.buffer(0).exterior.distance(c)))
    ring = c.buffer(rad + 0.012, 48).difference(c.buffer(rad + 0.002, 48)).intersection(rest)
    amber, lamp, ring = apart([amber, c.buffer(rad, 48).intersection(rest), ring])
    for sgn, side in ((1, "l"), (-1, "r")):
        fr = plane_fr(h, sgn)
        cutters += blend.lay(ref, f"cut_head_{side}", shp, fr, -0.016, 0.006, "housing", 0.008, along_ray=True)
        out += blend.lay(ref, f"head_back_{side}", shp.buffer(-0.001), fr, -0.0150, -0.0140, "housing", 0.008, along_ray=True)
        out += blend.lay(ref, f"head_amber_{side}", amber, fr, -0.0130, -0.010, "amber", 0.006, along_ray=True)
        out += blend.lay(ref, f"head_reflector_{side}", ring, fr, -0.0130, -0.0110, "reflector", 0.004, along_ray=True)
        out += blend.lay(ref, f"lamp_head_{side}", lamp, fr, -0.0130, -0.0095, "lamp_head", 0.004, along_ray=True)
        out += blend.lay(ref, f"head_glass_{side}", shp.buffer(-0.002), fr, -0.0045, -0.0018, "head_glass", 0.008, along_ray=True)


def taillamps(ref, cutters, out):
    t = R["tail"]
    shp = smooth2d(poly(t["outline"]), 0.003)
    inner = shp.buffer(-0.006)
    red = in_plane(R["tail_red"], t).intersection(inner)
    amber = in_plane(R["tail_amber"], t).intersection(inner)
    # the round lamp inboard of the amber one: the brake light, same size, our own placement
    a3 = np.asarray(R["tail_amber"]["o"])
    ac = Point(*to_plane(a3 + np.array([0, -0.14, 0]), t))
    ar = np.sqrt(amber.area / np.pi)
    brake = ac.buffer(ar, 48).intersection(inner)
    rings = [d.buffer(0.009, join_style=1).difference(d.buffer(0.0015, join_style=1)).intersection(inner) for d in (amber, brake)]
    amber, brake, ring_a, ring_b, red = apart([amber, brake, *rings, red])
    for sgn, side in ((1, "l"), (-1, "r")):
        fr = plane_fr(t, sgn)
        cutters += blend.lay(ref, f"cut_tail_{side}", shp, fr, -0.016, 0.006, "housing", 0.008, along_ray=True)
        out += blend.lay(ref, f"tail_back_{side}", shp.buffer(-0.001), fr, -0.0150, -0.0140, "housing", 0.008, along_ray=True)
        out += blend.lay(ref, f"lamp_tail_{side}", red, fr, -0.0130, -0.0098, "lamp_tail", 0.005, along_ray=True)
        out += blend.lay(ref, f"tail_amber_{side}", amber, fr, -0.0130, -0.0098, "amber", 0.004, along_ray=True)
        for nm, ring in (("ring_a", ring_a), ("ring_b", ring_b)):
            out += blend.lay(ref, f"tail_{nm}_{side}", ring, fr, -0.0130, -0.0110, "reflector", 0.004, along_ray=True)
        out += blend.lay(ref, f"lamp_brake_{side}", brake, fr, -0.0130, -0.0098, "lamp_brake", 0.004, along_ray=True)
        out += blend.lay(ref, f"tail_glass_{side}", shp.buffer(-0.002), fr, -0.0045, -0.0018, "tail_glass", 0.008, along_ray=True)


def small_lamps(ref, out):
    """Side markers on the front wings and the brake light strip on the boot lid."""
    m = R["marker"]
    shp = smooth2d(poly(m["outline"]), 0.002)
    for sgn, side in ((1, "l"), (-1, "r")):
        out += blend.lay(ref, f"marker_{side}", shp, plane_fr(m, sgn), 0.0006, 0.0032, "amber", 0.004)
    strip = smooth2d(poly(R["brake3"]), 0.002)
    out += blend.lay(ref, "lamp_brake_high", strip, rear_fr(), 0.0006, 0.0034, "lamp_brake", 0.006)


# ---------------------------------------------------------------- front openings
def openings(ref, cutters):
    """The grille and the two side intakes, as cutters; their shapes in (y, z) for painting."""
    fr = front_fr()
    grille = smooth2d(poly(R["grille"]), 0.006)
    intake = smooth2d(poly(R["intake"]), 0.004)
    shapes = [grille, intake, sscale(intake, -1, 1, origin=(0, 0))]
    for i, g in enumerate(shapes):
        cutters += blend.lay(ref, f"cut_open_{i}", g, fr, -0.07, 0.008, "grille", 0.01, along_ray=True)
    return shapes


def grille_parts(ref, shapes):
    """Slats across the grille and a mesh in each intake, set back in the openings."""
    fr = front_fr()
    out = []
    g = shapes[0].buffer(-0.003)
    z0, z1 = g.bounds[1], g.bounds[3]
    n = 4
    for i in range(1, n):
        z = z0 + (z1 - z0) * i / n
        out += blend.lay(ref, f"grille_slat_{i}", g.intersection(box(-2, z - 0.006, 2, z + 0.006)), fr,
                         -0.034, -0.022, "grille", 0.006, along_ray=True)
    for i, s in enumerate(shapes):
        out += blend.lay(ref, f"grille_back_{i}", s.buffer(-0.001), fr, -0.06, -0.052, "grille", 0.01, along_ray=True)
    return out


# ---------------------------------------------------------------- roof and glass
def envelope():
    """The windscreen and roof's outer surface as a solid, used only to lay glass and trim on."""
    xs, rcv = body.roof_curves()
    v, f = body.loft(lambda x: body.roof_section(rcv, x, BELT), xs[0], xs[-1], 0.02)
    return blend.make("envelope", v, f, "trim"), xs, rcv


SCREEN_FRAME = 0.035     # frame width round the glass, along the surface
SCREEN_BAND = 0.012      # how far the frame laps over the glass
SCREEN_FOOT = 0.56       # the screen runs on to here, under the cowl, and is trimmed by the body


def _arc(rcv, x, n=400):
    """The roof surface's left half section at x, top centre first: (y, z) points and the
    distance along the surface from the top centre."""
    th = np.linspace(0.02, np.pi / 2 - 0.01, len(rcv))
    r = np.array([max(float(f(x)), 0.01) for f in rcv])
    t = np.linspace(np.pi / 2, 0.02, n)
    rr = np.interp(t, th, r)
    pts = np.column_stack([rr * np.cos(t), BELT + rr * np.sin(t)])
    pts[0, 0] = 0.0
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(pts, axis=0), axis=1))])
    return pts, s


def _glass_s(rcv):
    """Distance along the surface from the top centre to the glass's side edge, by x."""
    sc = poly(R["screen"]["outline"]).convex_hull
    x_top = R["screen"]["x_top"]
    x_hi = sc.bounds[2] - 0.005

    def S(x):
        xc = min(max(x, x_top), x_hi)
        cut = sc.intersection(LineString([(xc, -2), (xc, 2)]))
        w = cut.bounds[3] if not cut.is_empty else 0.3
        pts, s = _arc(rcv, x)
        w = min(w, pts[:, 0].max() - SCREEN_FRAME - 0.02)
        k = int(np.argmax(pts[:, 0] >= w)) if (pts[:, 0] >= w).any() else len(s) - 1
        return float(s[k])
    return S


class _Surface:
    """The roof surface over the screen, mapped by (t, s): t the distance along the centre line
    from the screen's top (forward positive), s the signed distance along the section from the
    centre. Both are lengths on the surface, so offsets in (t, s) come out even all round."""

    def __init__(self, rcv, x0, x1):
        from scipy.interpolate import RegularGridInterpolator as RGI
        self.xg = np.arange(x0, x1, 0.002)
        sg = np.linspace(0.0, 0.9, 451)
        Y = np.zeros((len(self.xg), len(sg))); Z = np.zeros_like(Y)
        for i, x in enumerate(self.xg):
            pts, s = _arc(rcv, x)
            Y[i] = np.interp(sg, s, pts[:, 0]); Z[i] = np.interp(sg, s, pts[:, 1])
        self.fy, self.fz = RGI((self.xg, sg), Y), RGI((self.xg, sg), Z)
        c = np.column_stack([self.xg, Z[:, 0]])
        tg = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(c, axis=0), axis=1))])
        self.tg = tg - np.interp(R["screen"]["x_top"], self.xg, tg)

    def x(self, t):
        return np.interp(t, self.tg, self.xg)

    def t(self, x):
        return np.interp(x, self.xg, self.tg)

    def point(self, t, s):
        x = self.x(t)
        q = np.column_stack([x, np.minimum(np.abs(s), 0.9)])
        return np.column_stack([x, np.sign(s) * self.fy(q), self.fz(q)])

    def at(self, t, s, d):
        """Points d off the surface along its normal, outward."""
        h = 0.002
        p = self.point(t, s)
        n = np.cross(self.point(t + h, s) - self.point(t - h, s), self.point(t, s + h) - self.point(t, s - h))
        n /= np.linalg.norm(n, axis=1, keepdims=True)
        n *= np.sign(n[:, 2])[:, None]
        return p + np.asarray(d)[:, None] * n


def _glass_outline(rcv, srf):
    """The glass in (t, s): the screen's outline, kept where the roof surface stands clear of
    the body, with smooth rounded corners."""
    from scipy.ndimage import gaussian_filter1d
    G = _glass_s(rcv)
    edge = np.array(_cover_edge(0.0, 0.0, 0.006))
    x_top = R["screen"]["x_top"]
    xs = np.arange(x_top, float(edge[-1, 0]), 0.005)
    h = []
    for x in xs:
        pts, s = _arc(rcv, x)
        h.append(min(G(x), float(np.interp(np.interp(x, edge[:, 0], edge[:, 1]), pts[:, 0], s))) - 0.004)
    h = np.array(h)
    k = int(np.argmax(h)); stop = k + int(np.argmax(h[k:] < 0.15)) if (h[k:] < 0.15).any() else len(h)
    xs, h = xs[:stop], gaussian_filter1d(h[:stop], 2.0, mode="nearest")
    t = srf.t(xs)
    g = Polygon([(a, -b) for a, b in zip(t, h)] + [(a, b) for a, b in zip(t[::-1], h[::-1])]).buffer(0)
    return smooth2d(g, 0.01).buffer(-0.05, join_style=1).buffer(0.05, join_style=1)


def _loop(g, n):
    """n even points round g's outline, counter clockwise, with outward normals, in (t, s)."""
    from shapely.geometry.polygon import orient
    from scipy.ndimage import gaussian_filter1d
    p = body.even(np.array(orient(g, 1).exterior.coords)[:-1], n)
    tan = gaussian_filter1d(np.roll(p, -1, 0) - np.roll(p, 1, 0), 2.0, axis=0, mode="wrap")
    nrm = np.column_stack([tan[:, 1], -tan[:, 0]])
    return p, nrm / np.linalg.norm(nrm, axis=1, keepdims=True)


def _top_z(x, y):
    """Height of the plain skin's top at (x, y), or None off its edge."""
    h = body.half(body.at(body.CV, x))
    top = h[h[:, 1] > BELT - 0.1]
    top = top[np.argsort(top[:, 0])]
    y = abs(y)
    if not len(top) or y > top[-1, 0] - 0.01:
        return None
    return float(np.interp(y, top[:, 0], top[:, 1]))


def screen_parts(rcv):
    """Windscreen: the surround (A-pillars, header and the rail along the glass foot) as one
    smooth body-colour solid, swept round the glass and lapping over its edge, to join into
    the body; and the glass, seated under the lap."""
    x_top = R["screen"]["x_top"]
    srf = _Surface(rcv, x_top - 0.15, SCREEN_FOOT + 0.1)
    g = _glass_outline(rcv, srf)
    p, nrm = _loop(g, 480)
    F, L = SCREEN_FRAME, SCREEN_BAND
    # the surround's section: (offset out from the glass edge, height off the surface), a
    # rounded top lapping the glass, the underside reaching down into the body
    top = [(o, 0.0015 + 0.0115 * np.sqrt(max(0.0, 1 - ((o - (F - L) / 2) / ((F + L) / 2)) ** 2)) ** 0.8)
           for o in np.linspace(-L, F, 15)]
    top[0] = (-L, 0.0015 + 0.002); top[-1] = (F, -0.004)
    prof = [(-L, 0.0015)] + top + [(F, -0.03), (GAP, -0.03), (GAP, 0.0015)]
    rings = []
    for o, d in prof:
        q = p + o * nrm
        rings.append(srf.at(q[:, 0], q[:, 1], np.full(len(q), d)))
    # near the body the underside drops onto it, so the surround stands on the body as one
    # piece, and its outer side flares out in plan as it drops, meeting the body in a fillet;
    # well above the body (header, upper pillars) it keeps its own depth
    from scipy.ndimage import gaussian_filter1d
    jo, ji = len(prof) - 3, len(prof) - 2
    q2 = p + (F + 0.01) * nrm
    dirn = np.nan_to_num(srf.at(q2[:, 0], q2[:, 1], np.full(len(q2), -0.03))[:, :2] - rings[jo][:, :2])
    dirn /= np.maximum(np.linalg.norm(dirn, axis=1, keepdims=True), 1e-9)
    w = np.zeros(len(p))
    for i, (x, y, z) in enumerate(rings[jo]):
        zb = _top_z(x, y)
        if zb is not None:
            w[i] = np.clip((0.20 - (z - zb)) / 0.08, 0.0, 1.0)
    w = gaussian_filter1d(w, 6.0, mode="wrap")
    for j, flare in ((jo, 0.045), (ji, 0.0)):
        pts = rings[j].copy()
        for i in range(len(p)):
            if w[i] < 1e-3:
                continue
            x, y, z = pts[i]
            x2, y2 = np.array([x, y]) + flare * w[i] * dirn[i]
            zb = _top_z(x2, y2)
            if zb is None:
                x2, y2 = x, y; zb = _top_z(x, y)
            if zb is not None:
                pts[i] = (x2, y2, (1 - w[i]) * z + w[i] * min(z, zb - 0.015))
        # smooth the dropped underside along the loop, so its edge runs clean
        # (the inner side by height only: smoothing it in plan would pull its corners into the glass)
        k = np.clip(w * 4, 0, 1)[:, None]
        cols = slice(0, 3) if flare else slice(2, 3)
        pts[:, cols] = pts[:, cols] * (1 - k) + gaussian_filter1d(pts[:, cols], 3.0, axis=0, mode="wrap") * k
        rings[j] = pts
    # the fillet: points from the outer edge down to the flared foot, dropping first, then out
    a, b = rings[jo - 1], rings[jo]
    fil = []
    for k in (0.2, 0.4, 0.6, 0.8):
        e_xy, e_z = k ** 2, 1 - (1 - k) ** 2
        fil.append(np.column_stack([a[:, :2] + (b[:, :2] - a[:, :2]) * e_xy, a[:, 2] + (b[:, 2] - a[:, 2]) * e_z]))
    rings = rings[:jo] + fil + rings[jo:]
    m = len(rings)
    verts = rings
    verts = np.stack(verts, axis=1).reshape(-1, 3)          # loop point i, section point j -> i * m + j
    n = len(p)
    f = [(i * m + j, ((i + 1) % n) * m + j, ((i + 1) % n) * m + (j + 1) % m, i * m + (j + 1) % m)
         for i in range(n) for j in range(m)]
    frame = blend.make("screen_frame", verts, f, "body")

    # the glass: rings shrinking to the middle, top and bottom sheets and the edge
    c = np.array(g.centroid.coords[0])
    K = 12
    top, bot = [], []
    for k in range(K):
        q = c + (p - c) * (1 - k / K)
        top.append(srf.at(q[:, 0], q[:, 1], np.zeros(n)))
        bot.append(srf.at(q[:, 0], q[:, 1], np.full(n, -0.004)))
    ct = srf.at(np.array([c[0]]), np.array([c[1]]), np.zeros(1))
    cb = srf.at(np.array([c[0]]), np.array([c[1]]), np.full(1, -0.004))
    v = np.vstack(top + bot + [ct, cb])
    T, B, CT, CB = 0, K * n, 2 * K * n, 2 * K * n + 1
    f = []
    for base, flip, ctr in ((T, False, CT), (B, True, CB)):
        for k in range(K - 1):
            for i in range(n):
                q = (base + k * n + i, base + k * n + (i + 1) % n, base + (k + 1) * n + (i + 1) % n, base + (k + 1) * n + i)
                f.append(q[::-1] if flip else q)
        for i in range(n):
            q = (base + (K - 1) * n + i, base + (K - 1) * n + (i + 1) % n, ctr)
            f.append(q[::-1] if flip else q)
    for i in range(n):
        f.append((T + i, B + i, B + (i + 1) % n, T + (i + 1) % n))
    glass = blend.make("screen_glass", v, f, "glass")
    return frame, glass


def bpy_new_mesh(name):
    import bpy
    return bpy.data.meshes.new(name)


def stand_off(ob, *bodies, d=0.003):
    """Cut ob back to stand d clear of each body."""
    for b in bodies:
        blend.boolean(ob, blend.inflate(b, d), "DIFFERENCE")


def grown_skin(x0, x1, d=0.003, step=0.01):
    """The plain skin between x0 and x1 grown by d, lofted from its sections: a clean solid
    to cut a part back with, so it stands just off the body."""
    from shapely.geometry import Polygon as P
    rings = []
    for x in np.arange(x0, x1 + step / 2, step):
        g = P(body.section(body.at(body.CV, x))).buffer(0).buffer(d, join_style=1)
        ring = body.even(np.array(g.exterior.coords)[:-1], 160)
        rings.append(np.column_stack([np.full(160, x), ring]))
    v, f = geom.close_rings(rings)
    return blend.make("grown_skin", v, f, "trim")


def off_skin(ob, d=0.003):
    """Cut ob back to stand d clear of the plain skin."""
    xs = [v.co.x for v in ob.data.vertices]
    # stations off the round numbers parts end on, so no ring lands on a part's end face
    x0 = max(np.floor((min(xs) - 0.05) / 0.01) * 0.01 + 0.0047, TAIL + 0.002)
    x1 = min(max(xs) + 0.05, NOSE - 0.002)
    blend.boolean(ob, grown_skin(x0, x1, d), "DIFFERENCE")


def _prism(shape, z0, z1):
    """shape (in plan, x and y) stood up from z0 to z1 as a closed solid. The ends are proper
    triangulations, so a shape that is far from convex still makes a sound solid."""
    import bmesh
    from shapely.geometry.polygon import orient
    shape = shape.intersection(box(-3, -2, 3, 2))
    me = bpy_new_mesh("prism")
    bm = bmesh.new()
    for g in getattr(shape, "geoms", [shape]):
        loop = geom.resample(np.array(orient(g, 1).exterior.coords), 0.01)[:-1]
        vs = [bm.verts.new((float(x), float(y), z0)) for x, y in loop]
        face = bm.faces.new(vs)
        ext = bmesh.ops.extrude_face_region(bm, geom=[face])
        for v in ext["geom"]:
            if isinstance(v, bmesh.types.BMVert):
                v.co.z = z1
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    return blend.adopt("prism", me, "trim")


def side_glass_shape():
    g = smooth2d(poly(R["side_glass"]), 0.015)
    return g.buffer(-0.004)


def rear_glass_shape():
    return smooth2d(poly(R["rear_glass"]), 0.015).buffer(-0.004)


def roof_shell(xs, rcv, plain, mat):
    """The top as a shell 18 mm thick, behind the windscreen, standing on the body."""
    x_front = R["screen"]["x_top"] - SCREEN_FRAME - 0.004     # just behind the frame
    x0 = float(xs[0])
    v, f = body.loft(lambda x: body.roof_section(rcv, x, BELT, drop=0.12), x0, x_front, 0.02,
                  centre=lambda x: (0.0, BELT), even=True)
    outer = blend.make("roof_outer", v, f, mat)
    v, f = body.loft(lambda x: body.roof_section(rcv, x, BELT, drop=0.2, grow=-0.018), x0 - 0.05, x_front + 0.05, 0.02,
                  centre=lambda x: (0.0, BELT), even=True)
    blend.boolean(outer, blend.make("roof_inner", v, f, mat), "DIFFERENCE")
    off_skin(outer)
    return outer


def roof_options(env_bvh, xs, rcv, plain):
    """1 soft top (the reference), 2 roof down, 3 body-colour hardtop. Returns {n: [objects]}."""
    sg, rg = side_glass_shape(), rear_glass_shape()
    opts = {}
    for n, mat in ((1, "softtop"), (3, "body")):
        top = roof_shell(xs, rcv, plain, mat)
        cuts = []
        for sgn, side in ((1, "l"), (-1, "r")):
            cuts += blend.lay(env_bvh, f"cut_rs_{side}", sg, side_fr(sgn), -0.05, 0.01, "trim", 0.01, along_ray=True)
        cuts += blend.lay(env_bvh, "cut_rr", rg, rear_fr(), -0.05, 0.01, "trim", 0.01, along_ray=True)
        for c in cuts:
            blend.boolean(top, c, "DIFFERENCE")
        objs = [top]
        for sgn, side in ((1, "l"), (-1, "r")):
            objs += blend.lay(env_bvh, f"roof_side_glass_{side}", sg.buffer(-0.002), side_fr(sgn), -0.012, -0.008, "glass", 0.02, along_ray=True)
            if n == 3:
                band = sg.buffer(0.012).difference(sg.buffer(-0.002)).intersection(box(-5, BELT + 0.02, 5, 5))
                objs += blend.lay(env_bvh, f"roof_side_band_{side}", band, side_fr(sgn), 0.0015, 0.0035, "trim", 0.008)
        objs += blend.lay(env_bvh, "roof_rear_glass", rg.buffer(-0.002), rear_fr(), -0.012, -0.008, "glass", 0.02, along_ray=True)
        opts[n] = objs
    cover = boot_cover()
    off_skin(cover)
    opts[2] = [cover]
    return opts


def boot_cover():
    """Roof down: the folded top under a low padded cover behind the seats."""
    x0, x1 = -1.34, COCKPIT_X[0] + 0.02
    rings = []
    for x in np.linspace(x0, x1, 14):
        t = (x - x0) / (x1 - x0)
        k = np.sin(np.pi * t) ** 0.35
        hw = 0.62 * (0.92 + 0.08 * k)
        top = body.z_top(x) + 0.045 * k + 0.004
        base = body.z_top(x) - 0.03
        rr = min(0.03, (top - base) / 2.2)
        sh = box(-hw, base, hw, top).buffer(-rr, join_style=1).buffer(rr, join_style=1)
        ring = body.even(np.array(sh.exterior.coords)[:-1], 64)
        rings.append(np.column_stack([np.full(64, x), ring]))
    v, f = geom.close_rings(rings)
    return blend.make("boot_cover", v, f, "softtop")


# ---------------------------------------------------------------- cockpit
COCKPIT_X = (-1.12, 0.45)
COCKPIT_FLOOR = 0.30


def cockpit_half(x):
    """Half width of the opening at x, from the door tops and deck, rounded at the back."""
    pts = np.array(R["cockpit"])
    y = float(np.interp(x, pts[::-1, 0], pts[::-1, 1])) - 0.012
    y = min(y, 0.705)
    back = x - COCKPIT_X[0]
    if back < 0.12:
        y -= 0.12 - np.sqrt(max(0.0, 0.12 ** 2 - (0.12 - back) ** 2))
    front = COCKPIT_X[1] - x
    if front < 0.2:
        y = min(y, 0.56 + 0.145 * front / 0.2)
    return max(y, 0.2)


def screen_cover(extra=0.0, inset=0.01, thresh=0.006):
    """In plan: behind the screen's top, and ahead of it where the roof surface stands clear
    of the body. The cockpit opening keeps inside it, so its front edge hides under the glass."""
    side = _cover_edge(extra, inset, thresh)
    x_top = R["screen"]["x_top"]
    return Polygon([(-5, -5), (x_top, -5)] + [(x, -y) for x, y in side] + [(x, y) for x, y in side[::-1]]
                   + [(x_top, 5), (-5, 5)]).buffer(0)


def _cover_edge(extra=0.0, inset=0.01, thresh=0.006):
    """(x, half width) ahead of the screen's top where the roof surface stands thresh clear of
    the body, out to extra beyond the glass edge, less inset."""
    xs, rcv = body.roof_curves()
    G = _glass_s(rcv)
    x_top = R["screen"]["x_top"]
    edge = []
    for x in np.arange(x_top, SCREEN_FOOT, 0.005):
        pts, s = _arc(rcv, x)
        yg = float(np.interp(G(x) + extra, s, pts[:, 0])) - inset
        h = body.half(body.at(body.CV, x))
        top = h[h[:, 1] > BELT - 0.1]                       # the body's top surface, centre outwards
        ok = 0.0
        for y in np.arange(0.0, yg, 0.005):
            z_env = np.interp(y, pts[::-1, 0] if pts[-1, 0] < pts[0, 0] else pts[:, 0], pts[::-1, 1] if pts[-1, 0] < pts[0, 0] else pts[:, 1])
            z_body = np.interp(y, top[::-1, 0], top[::-1, 1])
            if z_env < z_body + thresh:
                break
            ok = y
        if ok < 0.05:
            break
        edge.append((x, ok - inset))
    return edge


def cockpit_cutters():
    """The cockpit as two cutters: the room inside the body below the door tops, and the
    opening through the door tops and deck seen from above."""
    from shapely.geometry.polygon import orient
    rings = []
    for x in np.linspace(COCKPIT_X[0], COCKPIT_X[1], 40):
        g = body.inside(x, 0.035).intersection(box(-3, COCKPIT_FLOOR, 3, BELT - 0.05))
        if g.geom_type != "Polygon":
            g = max(g.geoms, key=lambda q: q.area)
        ring = body.even(np.array(orient(g, 1).exterior.coords)[:-1], 96)
        rings.append(np.column_stack([np.full(96, x), ring]))
    v, f = geom.close_rings(rings)
    room = blend.make("cockpit_room", v, f, "interior")
    xs = np.linspace(COCKPIT_X[0], COCKPIT_X[1], 80)
    plan = Polygon([(x, cockpit_half(x)) for x in xs] + [(x, -cockpit_half(x)) for x in xs[::-1]])
    plan = orient(plan.intersection(screen_cover()).buffer(0), 1)
    loop = geom.resample(np.array(plan.exterior.coords), 0.01)[:-1]
    rings = [np.column_stack([loop, np.full(len(loop), z)]) for z in (BELT - 0.09, 2.0)]
    v, f = geom.close_rings(rings)
    return [room, blend.make("cockpit_opening", v, f, "interior")]


def arches():
    """Arch cutters and the wheel tubs kept out of the cockpit."""
    cut, tubs = [], []
    for (ax, ar) in ((W["front"]["x"], W["arch_r"][0]), (W["rear"]["x"], W["arch_r"][1])):
        for sgn in (1, -1):
            v, f = geom.disc((ax, 0.84 * sgn, W["r"]), (0, 1, 0), ar, 0.56, n=96)
            cut.append(blend.make("arch", v, f, "interior"))
            v, f = geom.disc((ax, 0.84 * sgn, W["r"]), (0, 1, 0), ar + 0.035, 0.74, n=96)
            tubs.append(blend.make("tub", v, f, "interior"))
    return cut, tubs


# ---------------------------------------------------------------- shut lines
def grooves(ref):
    obs = []
    door = [(x, min(z, BELT - 0.012)) for x, z in R["door"]]       # stop short of the door top's curl
    for sgn, side in ((1, "l"), (-1, "r")):
        obs.append(blend.groove(ref, f"g_door_{side}", door, side_fr(sgn)))
    # the traced outlines carry the hinge tabs: leave them out
    bonnet = [p for p in R["bonnet"] if p[0] > 0.385]
    boot = [p for p in R["boot"] if p[0] < -1.39]
    for name, pts in (("bonnet", bonnet), ("boot", boot)):
        g = smooth2d(poly(pts), 0.012).buffer(-0.004, join_style=1)
        ring = geom.resample(np.array(g.exterior.coords), 0.01)[:-1]
        # two U shaped halves, split where the line crosses the centre line, overlapping a little
        mid = np.where(np.abs(ring[:, 1]) < 0.02)[0]
        a = mid[np.argmax(ring[mid, 0])]
        b = mid[np.argmin(ring[mid, 0])]
        ring = np.roll(ring, -a, axis=0)
        b = (b - a) % len(ring)
        obs.append(blend.groove(ref, f"g_{name}_a", ring[: b + 3], top_fr()))
        # the second half a hair narrower and shallower, so where they overlap its walls lie
        # inside the first half's groove instead of on top of its walls
        obs.append(blend.groove(ref, f"g_{name}_b", np.vstack([ring[b - 2:], ring[:3]]), top_fr(),
                                width=0.0056, depth=0.0055))
    return obs


# ---------------------------------------------------------------- driver's door
DOOR_WALL = 0.09      # the door takes the cockpit wall this deep
DOOR_TOP = BELT - 0.08     # above this the door top is a solid ledge to the cockpit's edge
DOOR_IN = 0.69        # the ledge is taken in to here, past the cockpit's edge at 0.705
DOOR_GAP = 0.0015     # half the gap round the door, inside the shut line's groove
DOOR_OPEN = 65.0      # degrees the door swings out


def door_outline():
    """The left door seen from the side: inside its shut line, up over its top and the mirror
    on its front top corner."""
    d = np.array(R["door"])
    top = max(m[4] for m in R["mirror"]) + 0.03
    return unary_union([poly(d), box(d[-1][0], DOOR_TOP, d[0][0], top)])


def _door_top(grow):
    """The door top and mirror, straight through from the cockpit's edge outwards."""
    g = door_outline().intersection(box(-5, DOOR_TOP, 5, 5)).buffer(grow, join_style=2)
    ring = np.array(g.exterior.coords)[:-1]
    rings = [np.column_stack([ring[:, 0], np.full(len(ring), y), ring[:, 1]]) for y in (DOOR_IN - grow, 1.4)]
    v, f = geom.close_rings(rings)
    return [blend.make("door_top", v, f, "interior")]


def door_hinge():
    """The hinge axis up the door's front edge, outside the skin, and the open angle. Turning
    about an axis pointing up, a negative angle swings the door's back edge out."""
    d = np.array(R["door"])
    xf = float(d[:, 0].max()) + 0.004
    z0, z1 = float(d[:, 1].min()), float(d[:, 1].max())
    y = max(body.y_at(xf - 0.01, z) for z in np.linspace(z0 + 0.02, BELT - 0.02, 20)) + 0.003
    return (xf, y, z0), (xf, y, z1), -DOOR_OPEN


def door(ref, shell):
    """Cut the left door free of the body; its card and mirror glass go with it. Returns the
    door's pieces."""
    o = door_outline()
    regions = [(o.intersection(box(-5, -1, 5, DOOR_TOP + 0.01)), DOOR_WALL)]
    roles = {"mirror_glass_l": "mirror", "door_card_l": "card"}
    return blend.free_door(ref, side_fr(1), regions, DOOR_GAP, shell, [], roles, o, solids=_door_top)


# ---------------------------------------------------------------- mirrors and exhaust
def _mirror_rows():
    """The measured slices, smoothed along the arm so neighbouring slices do not jump."""
    rows = np.array(R["mirror"], float)
    sm = rows.copy()
    for j in range(1, 5):
        col = np.pad(rows[:, j], 1, mode="edge")
        sm[:, j] = (col[:-2] + 2 * col[1:-1] + col[2:]) / 4
    sm[:, 1] = np.polyval(np.polyfit(rows[:, 0], rows[:, 1], 1), rows[:, 0])   # the back face is flat
    for j in (3, 4):                                    # top and bottom run smoothly to the tip
        sm[:, j] = np.polyval(np.polyfit(rows[:, 0], rows[:, j], 2), rows[:, 0])
    return sm


def _mirror_shape(x0, x1, z0, z1):
    """A slice of the housing: flat at the back where the glass sits, rounded to the front."""
    d, h, zc = x1 - x0, (z1 - z0) / 2, (z0 + z1) / 2
    t = np.linspace(-np.pi / 2, np.pi / 2, 49)
    k = 2 / 2.6                                       # a superellipse, fuller than an ellipse
    front = np.column_stack([x0 + d * np.sign(np.cos(t)) * np.abs(np.cos(t)) ** k,
                             zc + h * np.sign(np.sin(t)) * np.abs(np.sin(t)) ** k])
    sh = Polygon(np.vstack([[x0, zc - h], front, [x0, zc + h]])).buffer(0)
    r = min(0.012, h / 2.5, d / 3)
    return sh.buffer(-r, join_style=1).buffer(r, join_style=1)


def _rrect(x0, x1, z0, z1, r, n):
    """n points round a rounded rectangle by arc length, from its bottom middle, anticlockwise."""
    pts = [((x0 + x1) / 2, z0)]
    for cx, cz, a0 in ((x1 - r, z0 + r, -np.pi / 2), (x1 - r, z1 - r, 0), (x0 + r, z1 - r, np.pi / 2), (x0 + r, z0 + r, np.pi)):
        for a in np.linspace(a0, a0 + np.pi / 2, 9):
            pts.append((cx + r * np.cos(a), cz + r * np.sin(a)))
    pts.append(pts[0])
    pts = np.array(pts)
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(pts, axis=0), axis=1))])
    t = np.linspace(0, s[-1], n, endpoint=False)
    return np.column_stack([np.interp(t, s, pts[:, 0]), np.interp(t, s, pts[:, 1])])


def _mirror_rings(sgn, inset=0.0, x_lo=None, x_hi=None, y_from=None):
    rows = [r for r in _mirror_rows() if y_from is None or r[0] >= y_from]
    rings = []
    for y, x0, x1, z0, z1 in rows:
        sh = _mirror_shape(x0, x1, z0, z1)
        if inset:
            sh = sh.buffer(-inset, join_style=1)
        if x_lo is not None:            # a slab from x_lo behind the back face to x_hi into it
            zb = sh.bounds
            ring = _rrect(x0 + x_lo, x0 + x_hi, zb[1], zb[3], min(0.004, (x_hi - x_lo) / 3), 64)
            rings.append(np.column_stack([ring[:, 0], np.full(64, y * sgn), ring[:, 1]]))
            continue
        if sh.geom_type != "Polygon":
            sh = max(sh.geoms, key=lambda g: g.area)
        c = np.array(sh.centroid.coords[0])           # start every ring at its own bottom middle
        ring = body.even(np.array(sh.exterior.coords)[:-1] - c, 64) + c
        rings.append(np.column_stack([ring[:, 0], np.full(64, y * sgn), ring[:, 1]]))
    return rings


MIRROR_FOOT = 0.84


MIRROR_Y0 = 0.778       # the housing starts here; inboard of it only the arm


def mirrors():
    """Each housing lofted through the measured slices, rounded at both ends, on a short arm
    down to the door's front top corner."""
    obs = []
    for sgn, side in ((1, "l"), (-1, "r")):
        rings = _mirror_rings(sgn, y_from=MIRROR_Y0)
        ends = []
        for ring, dy in ((rings[0], -0.008), (rings[-1], 0.006)):
            c = ring.mean(0)
            e = c + (ring - c) * 0.6
            e[:, 1] = ring[0, 1] + dy * sgn
            ends.append(e)
        rings = [ends[0]] + rings + [ends[1]]
        if sgn < 0:
            rings = rings[::-1]
        v, f = geom.close_rings(rings)
        housing = blend.make(f"mirror_{side}", v, f, "body")
        y0, x0, x1, z0, z1 = _mirror_rows()[1]
        xa = x0 + 0.35 * (x1 - x0)
        za, ya = BELT - 0.012, 0.705               # inside the door's top edge
        path = geom.catmull_open([(xa, ya * sgn, za), (xa, (ya + 0.035) * sgn, za + 0.022),
                                  (xa + 0.005, (y0 + 0.02) * sgn, z0 + 0.03)], 6)
        blend.boolean(housing, _tube("arm", path, 0.012, "body"), "UNION")
        obs.append(housing)
    return obs


def mirror_recess():
    """A shallow recess in each housing's back face, with the glass in it."""
    y_from = R["mirror"][0][0] + 0.03
    cut, glass = [], []
    for sgn, side in ((1, "l"), (-1, "r")):
        for name, x_lo, x_hi, inset, out, mat in (("cut_mirror", -0.03, 0.008, 0.008, cut, "trim"),
                                                   ("mirror_glass", 0.002, 0.005, 0.012, glass, "mirror_glass")):
            rings = _mirror_rings(sgn, inset, x_lo, x_hi, y_from)[:-1]
            if mat == "mirror_glass":
                rings[0][:, 1] += 0.004 * sgn
                rings[-1][:, 1] -= 0.004 * sgn
            if sgn < 0:
                rings = rings[::-1]
            v, f = geom.close_rings(rings)
            out.append(blend.make(f"{name}_{side}", v, f, mat))
    return cut, glass


def _skin_x(y, z):
    """The rearmost x where the skin reaches out to (y, z)."""
    for x in np.arange(TAIL, TAIL + 0.5, 0.004):
        if body.y_at(x, z) >= abs(y):
            return float(x)
    return TAIL


def exhaust():
    e = R["exhaust"]
    cut, pipes = [], []
    x_end = _skin_x(e["y"] + e["r"], e["z"]) - 0.025       # the tips stand just proud of the bumper
    e = dict(e, x=x_end)
    for sgn, side in ((1, "l"), (-1, "r")):
        y = e["y"] * sgn
        prof = [(e["r"] - 0.004, 0.0), (e["r"], 0.0), (e["r"], 0.25), (e["r"] - 0.004, 0.25)]
        v, f = geom.lathe(prof, 48, axis="z")
        v = np.column_stack([e["x"] + v[:, 2], y + v[:, 0], e["z"] + v[:, 1]])
        pipes.append(blend.make(f"exhaust_{side}", v, f, "chrome"))
        v, f = geom.disc((e["x"] + 0.13, y, e["z"]), (1, 0, 0), e["r"] + 0.003, 0.3)
        cut.append(blend.make("cut_exhaust", v, f, "trim"))
    return cut, pipes


# ---------------------------------------------------------------- interior
def _rbox(name, x0, x1, y0, y1, z0, z1, r=0.03, lean=0.0, mat="seat"):
    sh = box(y0, z0, y1, z1).buffer(-r, join_style=1).buffer(r, join_style=1)
    ring = body.even(np.array(sh.exterior.coords)[:-1], 48)
    rings = []
    cy, cz = (y0 + y1) / 2, (z0 + z1) / 2
    for x in (x0, x0 + 0.012, x1 - 0.012, x1):
        k = 0.93 if x in (x0, x1) else 1.0
        rr = np.column_stack([cy + (ring[:, 0] - cy) * k, cz + (ring[:, 1] - cz) * k])
        rings.append(np.column_stack([x - lean * (rr[:, 1] - z0), rr]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def _wall(name, x0, x1, clip, n=12, mat="interior"):
    """A panel following the inside of the body between x0 and x1, cut to clip(x)."""
    rings = []
    for x in np.linspace(x0, x1, n):
        g = body.inside(x, 0.035 + 0.006).intersection(clip(x))
        if g.geom_type != "Polygon":
            g = max(getattr(g, "geoms", [g]), key=lambda q: q.area)
        ring = body.even(np.array(g.exterior.coords)[:-1], 64)
        rings.append(np.column_stack([np.full(64, x), ring]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def _tube(name, path, r, mat):
    pts = geom.resample(path, 0.02)
    rings = []
    for i, p in enumerate(pts):
        t = pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]
        t /= np.linalg.norm(t)
        a = np.cross(t, [1.0, 0, 0] if abs(t[0]) < 0.7 else [0, 0, 1.0]); a /= np.linalg.norm(a)
        b = np.cross(t, a)
        ang = np.linspace(0, 2 * np.pi, 20, endpoint=False)
        rings.append(np.array([p + r * (np.cos(q) * a + np.sin(q) * b) for q in ang]))
    v, f = geom.close_rings(rings)
    return blend.make(name, v, f, mat)


def interior():
    x_front, x_back = COCKPIT_X[1] - 0.004, COCKPIT_X[0] + 0.004
    fl = COCKPIT_FLOOR + 0.002
    top = BELT - 0.055                                  # the room's top is BELT - 0.05
    obs = [_wall("firewall", x_front - 0.008, x_front, lambda x: box(-1, fl, 1, top), n=2),
           _wall("bulkhead", x_back, x_back + 0.008, lambda x: box(-1, fl, 1, top), n=2),
           _wall("dash", 0.12, x_front - 0.0085, lambda x: box(-1, 0.62 + 0.12 * (x - 0.12) / 0.33, 1, top), n=10)]
    for sgn, side in ((1, "l"), (-1, "r")):
        clip = lambda x, s=sgn: (box(0.3, 0.42, 1.2, top - 0.015) if s > 0 else box(-1.2, 0.42, -0.3, top - 0.015)).intersection(
            body.inside(x, 0.035 + 0.009)).difference(body.inside(x, 0.035 + 0.036))
        obs.append(_wall(f"door_card_{side}", -0.72, 0.28, clip, n=12))
    # steering wheel behind the dash ahead of the left seat, the driver's, on a column into the dash
    yc = 0.33
    ang = np.linspace(0, 2 * np.pi, 41)
    ring = [(0.02 + 0.05 * np.sin(a), yc + 0.17 * np.cos(a), 0.74 + 0.16 * np.sin(a)) for a in ang]
    wheel = _tube("steering_wheel", np.array(ring), 0.016, "trim")
    col = _tube("column", np.array([(0.0, yc, 0.735), (0.2, yc, 0.70)]), 0.025, "trim")
    hub = _tube("hub", np.array([(-0.005, yc, 0.74), (0.02, yc, 0.735)]), 0.06, "trim")
    for ob in (wheel, hub):
        blend.boolean(col, ob, "UNION")
    obs.append(col)
    cards = [o for o in obs if o.name.startswith("door_card")]      # the driver's swings with its door
    cabin = blend.union_all([o for o in obs if o not in cards], "cabin")
    seats = []
    for sgn, side in ((1, "l"), (-1, "r")):
        yc = 0.33 * sgn
        seat = [_rbox(f"seat_base_{side}", -0.62, -0.12, yc - 0.23, yc + 0.23, fl, 0.45),
                _rbox(f"seat_back_{side}", -0.76, -0.64, yc - 0.23, yc + 0.23, 0.44, 1.0, lean=0.2),
                _rbox("wing", -0.78, -0.62, yc + 0.17, yc + 0.25, 0.5, 0.95, r=0.03, lean=0.2),
                _rbox("wing", -0.78, -0.62, yc - 0.25, yc - 0.17, 0.5, 0.95, r=0.03, lean=0.2)]
        seats.append(blend.union_all(seat, f"seat_{side}"))
        # roll hoop just behind the seat, its legs down to the floor
        hx = -0.93
        path = [(hx, yc - 0.17, fl + 0.001), (hx, yc - 0.17, 0.80), (hx, yc - 0.155, 1.06), (hx, yc - 0.11, 1.12),
                (hx, yc, 1.135), (hx, yc + 0.11, 1.12), (hx, yc + 0.155, 1.06), (hx, yc + 0.17, 0.80), (hx, yc + 0.17, fl + 0.001)]
        seats.append(_tube(f"hoop_{side}", geom.catmull_open(path, 6), 0.021, "trim"))   # trimmed to the floor in export
    return cabin, seats + cards


def rear_view_mirror(env_bvh):
    xm = R["screen"]["x_top"] + 0.06
    loc, _n = blend.cast(env_bvh, top_fr(), xm, 0.0)
    top = float(loc[2]) - 0.012 if loc is not None else 1.2
    m = _rbox("mirror_rear", xm - 0.02, xm, -0.11, 0.11, top - 0.11, top - 0.05, r=0.02, mat="interior")
    stem = _rbox("stem", xm - 0.014, xm - 0.006, -0.01, 0.01, top - 0.06, top + 0.004, r=0.004, mat="interior")
    return blend.union_all([m, stem], "mirror_rear")


# ---------------------------------------------------------------- wheels
def wheels():
    obs = []
    for ax, key, fb in ((W["front"]["x"], "front", "f"), (W["rear"]["x"], "rear", "r")):
        w = W[key]["width"]
        hw = w / 2
        yc = W[key]["track"] / 2
        tyre = box(W["rim_r"], -hw, W["r"], hw).buffer(-0.03, join_style=1).buffer(0.03, join_style=1)
        tyre = tyre.union(box(W["rim_r"], -hw + 0.012, W["rim_r"] + 0.03, hw - 0.012))
        tp = body.even(np.array(tyre.exterior.coords)[:-1], 96)
        rim = [(W["rim_r"] - 0.001, -hw + 0.012), (W["rim_r"] - 0.001, hw - 0.012), (W["rim_r"] - 0.014, hw - 0.004),
               (W["rim_r"] - 0.026, hw - 0.01), (W["rim_r"] - 0.026, -hw + 0.012)]
        for sgn, lr in ((1, "l"), (-1, "r")):
            def place(v, s=sgn):
                v = np.asarray(v, float)
                return np.column_stack([ax + v[:, 0], s * (yc + v[:, 1]), W["r"] + v[:, 2]])
            v, f = geom.lathe(tp, 96, axis="y")
            obs.append(blend.make(f"wheel_tyre_{fb}{lr}", place(v), f, "tyre"))
            v, f = geom.lathe(rim, 96, axis="y")
            parts = [blend.make("rim", place(v), f, "rim")]
            face = hw - 0.03
            v, f = geom.disc((0, face - 0.02, 0), (0, 1, 0), 0.07, 0.045, n=48)
            parts.append(blend.make("hub", place(v), f, "rim"))
            for k in range(5):
                a = 2 * np.pi * k / 5
                ca, sa = np.cos(a), np.sin(a)
                pts = []
                # a spoke that narrows and leans outward towards the rim
                for r, w2, h0, h1 in ((0.05, 0.03, face - 0.04, face), (W["rim_r"] - 0.02, 0.02, face - 0.03, face - 0.012)):
                    for dw in (-w2, w2):
                        for hh in (h0, h1):
                            pts.append((r * ca - dw * sa, hh, r * sa + dw * ca))
                f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
                parts.append(blend.make("spoke", place(np.array(pts)), f, "rim"))
            obs.append(blend.union_all(parts, f"wheel_rim_{fb}{lr}"))
    return obs


# ---------------------------------------------------------------- badge and name
BADGE_SVG = HERE.parent.parent.parent / "src/renderer/assets/brands/tora/tora-badge-colour.svg"


def badge_pieces():
    """The Tora badge, full colour with a chrome rim: [(shape, material)], flat, centred, 1 unit
    tall, y up. Later shapes in the drawing cover earlier ones, so each colour is what shows."""
    import re
    svg = BADGE_SVG.read_text()
    colours = {"#16130f": "badge_black", "#ee6a1f": "badge_orange", "#f3e9d8": "badge_cream"}
    shapes = []
    # walk the drawing in order, tracking the mirror groups
    pos, mirror = 0, []
    for tok in re.finditer(r'<g transform="translate\(200 0\) scale\(-1 1\)">|</g>|<polygon points="([^"]+)" fill="([^"]+)"/>', svg):
        t = tok.group(0)
        if t.startswith("<g"):
            mirror.append(True)
        elif t == "</g>":
            if mirror:
                mirror.pop()
        else:
            pts = [tuple(map(float, p.split(","))) for p in tok.group(1).split()]
            if mirror:
                pts = [(200 - x, y) for x, y in pts]
            shapes.append((Polygon(pts).buffer(0), colours[tok.group(2)]))
    whole = unary_union([s for s, _m in shapes])
    b = whole.bounds
    k = 1 / (b[3] - b[1])
    fix = lambda g: sscale(translate(sscale(g, 1, -1, origin=(0, 0)), -(b[0] + b[2]) / 2, (b[1] + b[3]) / 2), k, k, origin=(0, 0))
    out, covered = [], None
    for s, m in reversed(shapes):
        vis = s if covered is None else s.difference(covered)
        covered = s if covered is None else covered.union(s)
        if not vis.is_empty:
            out.append((fix(vis), m))
    outline = fix(whole)
    return outline, out


def name_shape():
    gl = json.loads((HERE / "glyphs.json").read_text())
    g = unary_union([Polygon(p[0], p[1:]) for p in gl["pieces"]])
    b = g.bounds
    g = translate(g, -(b[0] + b[2]) / 2, -gl["cap"] / 2)
    return sscale(g, 1 / gl["cap"], 1 / gl["cap"], origin=(0, 0))


def _flat(ref, g, size, fr, a, b, up, name, d0, d1, mat, step=0.004):
    """Lay shape g (right, up, 1 unit = size) flat on the panel that fr's ray at (a, b) meets."""
    pf = blend.panel_frame(ref, fr, a, b, up)
    return blend.lay(ref, name, sscale(g, size, size, origin=(0, 0)), pf, d0, d1, mat, step)


def badge(ref, size, fr, a, b, up, name):
    """Chrome rim under the colour face; each colour a little apart so no faces touch."""
    outline, pieces = badge_pieces()
    rim = 0.07
    objs = _flat(ref, outline.buffer(rim, join_style=2), size, fr, a, b, up, name + "_rim", 0.0012, 0.0030, "chrome", 0.003)
    for i, (g, m) in enumerate(pieces):
        g = g.buffer(-0.3 * 0.001 / size)
        if g.is_empty:
            continue
        objs += _flat(ref, g, size, fr, a, b, up, f"{name}_{i}", 0.0036, 0.0046, m, 0.002)
    return objs


def options(ref):
    """Badge and name options, named opt_<group>_<n>_<i>. Each lies flat on its panel."""
    sp = R["spots"]
    nm = name_shape()
    up, fwd = (0, 0, 1), (-1, 0, 0)
    out = []

    def add(group, n, objs):
        for i, ob in enumerate(objs):
            ob.name = f"opt_{group}_{n}_{i}"
            out.append(ob)
    # front: 1 on the nose above the grille (the reference), 2 on the bonnet's front edge
    add("frontbadge", 1, badge(ref, 0.06, front_fr(), sp["front"][0], sp["front"][1], up, "fb"))
    add("frontbadge", 2, badge(ref, 0.06, top_fr(), sp["bonnet_x"], 0, fwd, "fb"))
    # rear: 1 middle of the boot lid (the reference), 2 lower, above the plate recess
    add("rearbadge", 1, badge(ref, 0.06, rear_fr(), 0, sp["rear"][1], up, "rb"))
    add("rearbadge", 2, badge(ref, 0.06, rear_fr(), 0, sp["rear_low_z"], up, "rb"))
    cap = 0.028
    q = []
    for sgn in (1, -1):
        q += _flat(ref, nm, cap, side_fr(sgn), sp["side"][0], sp["side"][1], up, "nm", 0.0012, 0.0025, "chrome")
    add("name", 1, q)
    add("name", 2, _flat(ref, nm, cap, rear_fr(), -0.2, sp["rear"][1] - 0.045, up, "nm", 0.0012, 0.0025, "chrome"))
    return out
