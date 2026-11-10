"""Raised parts laid onto the body by ray casting: lamps, trim, grille, badge and name."""
import json
from pathlib import Path

import numpy as np
from mathutils import Vector
from mathutils.geometry import tessellate_polygon

HERE = Path(__file__).parent


def _quad(p0, p1, p2, n=6):
    return [tuple((1 - t) ** 2 * np.array(p0) + 2 * (1 - t) * t * np.array(p1) + t * t * np.array(p2))
            for t in np.linspace(0, 1, n + 1)[1:]]


def badge_layers():
    """(pieces, material, layer) for the Kaiser badge; each piece is an outer loop then holes.
    Built from the brand SVG: a ring and a stem, cut by three slits on the right."""
    from shapely.geometry import Point, Polygon, box
    from shapely.ops import unary_union
    t = lambda x, y: (1.15 * x - 23, 1.15 * y - 15)   # the SVG's inner transform
    cx, cy = t(90, 100)
    ring = Point(cx, cy).buffer(52 * 1.15, 96).difference(Point(cx, cy).buffer(26 * 1.15, 64))
    stem = [(128, 48), (154, 48), (154, 124)] + _quad((154, 124), (154, 136), (168, 136))
    stem += [(174, 136), (174, 152), (160, 152)] + _quad((160, 152), (128, 152), (128, 122))
    stem = Polygon([t(*p) for p in stem]).buffer(0)
    shape = unary_union([ring, stem]).buffer(1.5, join_style=1)   # the SVG's 3 px stroke
    for y0, x0 in ((80, 96), (100, 104), (120, 112)):
        shape = shape.difference(box(x0, y0, 220, y0 + 9))
    polys = [shape] if shape.geom_type == "Polygon" else list(shape.geoms)
    pieces = [[list(p.exterior.coords)[:-1]] + [list(i.coords)[:-1] for i in p.interiors] for p in polys]
    return [(pieces, "chrome", 0)]


BADGE_C, BADGE_H = (98.9, 100.0), 122.6   # centre and height of the badge in SVG units


def _densify(loop, max_edge):
    out = []
    n = len(loop)
    for i in range(n):
        p, q = np.asarray(loop[i], float), np.asarray(loop[(i + 1) % n], float)
        k = max(1, int(np.ceil(np.linalg.norm(q - p) / max_edge)))
        out += [p + (q - p) * t for t in np.arange(k) / k]
    return out


def mesh2d(loops, max_edge):
    """Even triangle mesh of a 2D shape (outer loop first, then holes)."""
    import triangle
    from shapely.geometry import Polygon
    verts, segs, holes = [], [], []
    for li, loop in enumerate(loops):
        d = _densify(loop, max_edge)
        b = len(verts)
        verts += d
        segs += [(b + i, b + (i + 1) % len(d)) for i in range(len(d))]
        if li:
            holes.append(Polygon(loop).representative_point().coords[0])
    spec = {"vertices": np.array(verts), "segments": np.array(segs)}
    if holes:
        spec["holes"] = np.array(holes)
    t = triangle.triangulate(spec, f"pq28Ya{max_edge * max_edge * 0.6:.10f}")
    return t["vertices"], t["triangles"]


def project(bvh, o, d, max_back=0.6):
    """Ray onto the body; a ray that misses lands on the nearest surface along it."""
    loc, nrm, _i, _dist = bvh.ray_cast(Vector(o), Vector(d))
    if loc is not None:
        return np.array(loc), np.array(nrm)
    best = None
    for t in np.linspace(0, 13, 521):
        p = o + d * t
        q, n, _i, dist = bvh.find_nearest(Vector(p))
        if q is not None and (best is None or dist < best[0]):
            best = (dist, np.array(q), np.array(n))
    return best[1], best[2]


def raised(bvh, loops, frame, d0, d1, max_edge=0.012, accept=None):
    """Solid from 2D loops (metres in the frame's u, v) laid on the body.
    frame: (origin, u, v, ray direction into the body). Returns (verts, faces) or None."""
    o, u, v, d = (np.asarray(a, float) for a in frame)
    pts2, tris = mesh2d(loops, max_edge)
    tris = [tuple(int(i) for i in t) for t in tris]
    count = {}
    for t in tris:
        for e in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            k = tuple(sorted(e))
            count[k] = count.get(k, 0) + 1
    c2 = np.mean([p for loop in loops for p in loop], axis=0)
    hits = []
    for p in pts2:
        for k in np.linspace(1.0, 0.3, 36):
            q = c2 + (np.asarray(p) - c2) * k   # a ray that misses moves towards the middle
            loc, nrm, _i, _dist = bvh.ray_cast(Vector(o + u * q[0] + v * q[1] - d * 0.5), Vector(d))
            if loc is not None and (accept is None or accept(np.array(loc))):
                break
            loc = None
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


def raised_many(bvh, loops_list, frame, d0, d1, max_edge=0.012):
    out = []
    for loops in loops_list:
        r = raised(bvh, loops, frame, d0, d1, max_edge)
        if r:
            out.append(r)
    return out


def badge(bvh, centre, up, right, into, height, lift=0.0):
    """Kaiser badge centred at centre; up and right are surface directions."""
    s = height / BADGE_H
    out = []
    for loops, mat, layer in badge_layers():
        d0 = 0.0015 + 0.0025 * layer + lift
        d1 = d0 + 0.0025
        for piece in loops:
            m = [[((x - BADGE_C[0]) * s, (BADGE_C[1] - y) * s) for x, y in loop] for loop in piece]
            r = raised(bvh, m, (centre, right, up, into), d0, d1, max_edge=0.005)
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
        r = raised(bvh, loops, (start, along, up, into), 0.0015, 0.0035, max_edge=0.03)
        if r:
            out.append((r[0], r[1], "name"))
    return out, g["width"] * s
