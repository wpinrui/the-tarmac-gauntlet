"""Badge and name: raised flat parts laid onto the body by ray casting."""
import json
from pathlib import Path

import numpy as np
from mathutils import Vector
from mathutils.geometry import tessellate_polygon

HERE = Path(__file__).parent


def hex_pts(cx, cy, top, k=1.0):
    """The badge hexagon from the brand SVG, scaled about its centre."""
    pts = [(100, 8), (182, 54), (182, 146), (100, 192), (18, 146), (18, 54)]
    if top != 8:
        r = 100 - top
        pts = [(100 + (x - 100) * r / 92, 100 + (y - 100) * r / 92) for x, y in pts]
    return [(100 + (x - 100) * k, 100 + (y - 100) * k) for x, y in pts]


def badge_layers():
    """(loops in SVG units, material, layer) for the Seorak badge, bottom layer first."""
    navy = [hex_pts(100, 100, 8, 96 / 92)]
    ring = [hex_pts(100, 100, 19.96, 82.04 / 80.04), hex_pts(100, 100, 19.96, 78.04 / 80.04)]
    chevron = [[(100, 46), (162, 146), (134, 146), (100, 92), (66, 146), (38, 146)]]
    red = [[(100, 108), (124, 146), (76, 146)]]
    return [(navy, "badge_navy", 0), (ring, "badge_amber", 1), (chevron, "badge_white", 2), (red, "badge_red", 1)]


def subdivide(verts, tris, max_edge):
    """Split every triangle into four until no edge is longer than max_edge."""
    verts = [np.array(v, float) for v in verts]
    while True:
        longest = max(np.linalg.norm(verts[a] - verts[b]) for t in tris
                      for a, b in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])))
        if longest <= max_edge:
            return verts, tris
        mid = {}

        def m(a, b):
            k = (min(a, b), max(a, b))
            if k not in mid:
                verts.append((verts[a] + verts[b]) / 2)
                mid[k] = len(verts) - 1
            return mid[k]
        new = []
        for a, b, c in tris:
            ab, bc, ca = m(a, b), m(b, c), m(c, a)
            new += [(a, ab, ca), (ab, b, bc), (ca, bc, c), (ab, bc, ca)]
        tris = new


def raised(bvh, loops, frame, d0, d1, max_edge=0.012):
    """Solid from 2D loops (metres in the frame's u, v) laid on the body.
    frame: (origin, u, v, ray direction into the body). Returns (verts, faces) or None."""
    o, u, v, d = (np.asarray(a, float) for a in frame)
    flat = [[(p[0], p[1], 0.0) for p in loop] for loop in loops]
    tris = tessellate_polygon([[Vector(p) for p in loop] for loop in flat])
    pts2 = [p for loop in flat for p in loop]

    def ccw(t):
        a, b, c = (pts2[i] for i in t)
        area = (b[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (b[1] - a[1])
        return tuple(t) if area > 0 else (t[0], t[2], t[1])
    tris = [ccw(t) for t in tris]
    pts2, tris = subdivide(pts2, tris, max_edge)
    # boundary edges of the triangulation become the side walls
    count = {}
    for t in tris:
        for e in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            k = tuple(sorted(e))
            count[k] = count.get(k, 0) + 1
    hits = []
    for p in pts2:
        start = o + u * p[0] + v * p[1] - d * 0.5
        loc, nrm, _i, _dist = bvh.ray_cast(Vector(start), Vector(d))
        if loc is None:
            return None
        hits.append((np.array(loc), np.array(nrm)))
    n = len(pts2)
    verts = [h[0] + h[1] * d0 for h in hits] + [h[0] + h[1] * d1 for h in hits]
    faces = [(t[2], t[1], t[0]) for t in tris] + [(t[0] + n, t[1] + n, t[2] + n) for t in tris]
    for t in tris:
        for a, b in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            if count[tuple(sorted((a, b)))] == 1:
                faces.append((a, b, b + n, a + n))
    verts = np.array(verts)
    c = verts.mean(0)
    w = verts - c
    vol = sum(np.dot(w[f[0]], np.cross(w[f[1]], w[f[2]])) for f in faces if len(f) == 3)
    vol += sum(np.dot(w[f[0]], np.cross(w[f[1]], w[f[2]])) + np.dot(w[f[0]], np.cross(w[f[2]], w[f[3]]))
               for f in faces if len(f) == 4)
    if vol < 0:
        faces = [tuple(reversed(f)) for f in faces]
    return verts, faces


def badge(bvh, centre, up, right, into, height):
    """Seorak badge centred at centre; up and right are surface directions."""
    s = height / 192.0
    out = []
    for loops, mat, layer in badge_layers():
        m = [[((x - 100) * s, (100 - y) * s) for x, y in loop] for loop in loops]
        d0 = 0.0015 + (0.0045 + (layer - 1) * 0.0018 if layer else 0)
        d1 = d0 + (0.003 if layer == 0 else 0.0012)
        r = raised(bvh, m, (centre, right, up, into), d0, d1, max_edge=0.006)
        if r:
            out.append((r[0], r[1], mat))
    return out


def name(bvh, start, along, up, into, cap):
    """Model name in letter outlines; start is the left end of the baseline."""
    g = json.loads((HERE / "glyphs.json").read_text())
    s = cap / g["cap"]
    out = []
    for letter in g["letters"]:
        loops = [[(x * s, y * s) for x, y in c] for c in letter]
        r = raised(bvh, loops, (start, along, up, into), 0.0015, 0.004, max_edge=0.04)
        if r:
            out.append((r[0], r[1], "name"))
    return out, g["width"] * s
