"""Liberty badge and the Collier name, built flat and laid onto the shell.

The badge is the B4 hexagon-and-bell mark from the brand sheet. Lettering
comes from the font outlines. Each part is drawn in a 2D plane, extruded a
couple of millimetres, then every vertex is dropped onto the body along the
local normal so the part hugs the curved panel.
"""
from pathlib import Path
import numpy as np
import trimesh
from shapely.geometry import Polygon, LineString, Point
from shapely.ops import unary_union
from fontTools.ttLib import TTFont
from fontTools.pens.recordingPen import RecordingPen
import cams as C

HERE = Path(__file__).parent
FONTS = {'script': HERE / 'font/Yellowtail-Regular.ttf', 'block': HERE / 'font/Archivo-ExtraBold.ttf'}


def qbez(p0, p1, p2, n=10):
    t = np.linspace(0, 1, n + 1)[1:, None]
    return (1 - t) ** 2 * p0 + 2 * (1 - t) * t * p1 + t ** 2 * p2


def cbez(p0, p1, p2, p3, n=12):
    t = np.linspace(0, 1, n + 1)[1:, None]
    return (1 - t) ** 3 * p0 + 3 * (1 - t) ** 2 * t * p1 + 3 * (1 - t) * t ** 2 * p2 + t ** 3 * p3


def hexagon(r, cx=100, cy=100):
    a = np.radians(np.arange(6) * 60 - 90)
    return Polygon(np.c_[cx + r * np.cos(a), cy + r * np.sin(a)])


def bell():
    P = lambda x, y: np.array([x, y], float)
    pts = [P(74, 70)]
    pts += list(qbez(P(74, 70), P(74, 52), P(100, 52)))
    pts += list(qbez(P(100, 52), P(126, 52), P(126, 70)))
    pts += [P(130, 116)]
    pts += list(qbez(P(130, 116), P(132, 134), P(150, 140)))
    pts += [P(150, 148), P(50, 148), P(50, 140)]
    pts += list(qbez(P(50, 140), P(68, 134), P(70, 116)))
    return Polygon(pts)


def badge_layers(finish):
    """[(shapely geometry in SVG units, material, layer)] bottom first."""
    crack = LineString([(104, 56), (97, 80), (106, 98), (99, 122), (104, 136)]).buffer(1.75, cap_style=1, join_style=1)
    bar = Polygon([(92, 44), (108, 44), (108, 52), (92, 52)])
    gold = unary_union([bell(), bar, Point(100, 157).buffer(8, 24)])
    if finish == 'colour':
        return [(hexagon(94), 'badge_red', 0), (hexagon(80), 'badge_navy', 1), (gold.difference(crack), 'badge_gold', 2)]
    return [(hexagon(94), 'chrome', 0), (hexagon(80), 'badge_dark', 1), (gold.difference(crack), 'chrome', 2)]


def glyph_shapes(text, font):
    """Text outline as shapely geometry, in font units, y up."""
    f = TTFont(str(FONTS[font]))
    gs = f.getGlyphSet()
    cmap = f.getBestCmap()
    hmtx = f['hmtx']
    x = 0
    polys = []
    for ch in text:
        name = cmap[ord(ch)]
        pen = RecordingPen()
        gs[name].draw(pen)
        rings, cur = [], []
        for op, args in pen.value:
            if op == 'moveTo':
                cur = [np.array(args[0], float)]
            elif op == 'lineTo':
                cur.append(np.array(args[0], float))
            elif op == 'qCurveTo':
                pts = [np.array(a, float) for a in args]
                p0 = cur[-1]
                # implied on-curve points between consecutive off-curve points
                for i in range(len(pts) - 1):
                    c = pts[i]
                    e = pts[i + 1] if i == len(pts) - 2 else (pts[i] + pts[i + 1]) / 2
                    cur += list(qbez(p0, c, e, 6))
                    p0 = e
            elif op == 'curveTo':
                p1, p2, p3 = [np.array(a, float) for a in args]
                cur += list(cbez(cur[-1], p1, p2, p3, 8))
            elif op in ('closePath', 'endPath'):
                if len(cur) > 2:
                    rings.append(np.array(cur) + [x, 0])
                cur = []
        # even-odd: union of outer rings minus holes, by area sign
        g = None
        for r in rings:
            p = Polygon(r).buffer(0)
            g = p if g is None else g.symmetric_difference(p)
        if g is not None:
            polys.append(g)
        x += hmtx[name][0]
    return unary_union(polys)


def extrude(geom, h, z0=0.0):
    parts = geom.geoms if hasattr(geom, 'geoms') else [geom]
    out = []
    for p in parts:
        if p.area <= 0:
            continue
        m = trimesh.creation.extrude_polygon(p, h)
        m.apply_translation([0, 0, z0])
        out.append(m)
    return trimesh.util.concatenate(out) if out else None


def lay_on(shell, flat, origin, normal, up, size_scale):
    """Map a flat part (x right, y up, z out, in its own units) onto the shell.
    origin: surface point. normal: outward. up: world direction that should read as up."""
    n = normal / np.linalg.norm(normal)
    u = up - (up @ n) * n
    u /= np.linalg.norm(u)
    r = np.cross(u, n)
    V = flat.vertices.copy()
    P = origin + size_scale * (V[:, 0:1] * r + V[:, 1:2] * u)
    # drop onto the surface along -n, keep the part's own thickness on top
    locs, ri, ti = shell.ray.intersects_location(P + 0.15 * n, np.repeat(-n[None], len(P), 0), multiple_hits=False)
    base = P.copy()
    base[ri] = locs
    out = base + V[:, 2:3] * n
    m = flat.copy()
    m.vertices = out
    return m


def anchor(shell, cams, view, uv, size):
    """Surface point and normal under a pixel of a reference view."""
    o = C.centre(cams[view])
    d = C.ray_dirs(cams[view], np.array([uv], float), size)[0]
    locs, ri, ti = shell.ray.intersects_location(o[None], d[None], multiple_hits=False)
    p = locs[0]
    # average normal over a small patch for a steadier frame
    near = np.linalg.norm(shell.triangles_center - p, axis=1) < 0.04
    nrm = shell.face_normals[near].mean(0)
    return p, nrm / np.linalg.norm(nrm)


def badge(shell, origin, normal, height=0.085, finish='colour'):
    """dict material -> mesh, badge centred on origin."""
    out = {}
    s = height / 188.0
    for geom, mat, layer in badge_layers(finish):
        # SVG y runs down: flip, centre on (100, 100)
        g = trimesh.Trimesh()
        from shapely import affinity
        gg = affinity.scale(affinity.translate(geom, -100, -100), 1, -1, origin=(0, 0))
        m = extrude(gg, 0.0015 / s, z0=(0.001 + 0.0009 * layer) / s)
        m.vertices[:, 2] *= s
        m = lay_on(shell, m, origin, normal, np.array([0, 0, 1.0]), s)
        out.setdefault(mat, []).append(m)
    return {k: trimesh.util.concatenate(v) for k, v in out.items()}


def name(shell, origin, normal, font='script', height=0.05, text='Collier'):
    g = glyph_shapes(text if font == 'script' else text.upper(), font)
    minx, miny, maxx, maxy = g.bounds
    from shapely import affinity
    g = affinity.translate(g, -(minx + maxx) / 2, -(miny + maxy) / 2)
    s = height / (maxy - miny)
    m = extrude(g, 0.002 / s, z0=0.001 / s)
    m.vertices[:, 2] *= s
    return {'chrome': lay_on(shell, m, origin, normal, np.array([0, 0, 1.0]), s)}
