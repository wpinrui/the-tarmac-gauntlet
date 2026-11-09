"""Assemble the car and write a GLB with one named mesh per material.

python3 build.py out.glb
"""
import sys
import numpy as np
import trimesh
import cams as C
import shell as SH
import features as FE
import parts as PA
import badges as BA
from decals import decal

CAMS = 'cams_f.json'

# feature -> (material, lift in metres)
FEATURES = {
    ('side', 'glass'): ('glass', 0.004), ('side', 'strip'): ('trim', 0.006),
    ('three_quarter_front', 'headlamp'): ('headlamp', 0.005),
    ('three_quarter_rear', 'taillamp'): ('taillamp', 0.005), ('front', 'grille'): ('grille', 0.004),
    ('front', 'fog'): ('fog', 0.008), ('front', 'fog_ring'): ('chrome', 0.010), ('front', 'bars'): ('chrome', 0.009), ('front', 'lip'): ('trim', 0.004),
    ('rear', 'glass'): ('glass', 0.004),
    ('rear', 'strip'): ('trim', 0.005), ('top', 'glass'): ('glass', 0.004),
}


FACING = {('front', 'headlamp'): 0.35, ('rear', 'taillamp'): 0.35, ('side', 'headlamp'): 0.3,
          ('side', 'taillamp'): 0.3}


def liner(body):
    """Split the faces the arch cut made into their own dark material."""
    c = body.triangles_center
    inside = np.zeros(len(c), bool)
    for sx in (1, -1):
        d = np.hypot(c[:, 0] - sx * SH.P.WHEEL_X, c[:, 2] - SH.P.WHEEL_Z)
        inside |= (d < 0.382) & (np.abs(c[:, 1]) > 0.49)
    return body.submesh([np.nonzero(~inside)[0]], append=True), body.submesh([np.nonzero(inside)[0]], append=True)


def build():
    cams = C.load(CAMS)
    body = SH.arches(SH.loft())
    paint, well = liner(body)
    groups = {'paint': [paint], 'liner': [well]}
    masks = {v: f()[1] for v, f in FE.VIEWS.items()}
    for (view, feat), (mat, lift) in FEATURES.items():
        d = decal(body, cams, view, masks[view][feat], lift=lift, min_facing=FACING.get((view, feat), 0.0))
        groups.setdefault(mat, []).append(d)
    for k, m in PA.wheels().items():
        groups.setdefault(k, []).append(m)
    for k, m in PA.mirrors().items():
        groups.setdefault(k, []).append(m)
    groups['paint'].append(PA.spoiler())
    for k, m in PA.exhaust().items():
        groups.setdefault(k, []).append(m)
    out = {k: trimesh.util.concatenate(v) for k, v in groups.items()}
    out.update(options(body, cams))
    return out


SIZES = {v: tuple(reversed(f()[0].shape[:2])) for v, f in FE.VIEWS.items()} if False else None

# where the options sit: (view, pixel) in the references
BADGE_FRONT = {'nose': ('front', (412, 368)), 'grille': ('front', (412, 470))}
BADGE_REAR = {'tailgate': ('rear', (487, 264)), 'low': ('rear', (487, 362))}
NAME_POS = {'left': ('rear', (242, 384)), 'right': ('rear', (733, 384)), 'centre': ('rear', (487, 366)),
            'sides': ('side', (395, 335))}


def centre_anchor(body, x_dir, p):
    """Re-find a centre-line point by shooting along X at y = 0."""
    o = np.array([p[0] + 1.0 * x_dir, 0.0, p[2]])
    locs, ri, ti = body.ray.intersects_location(o[None], np.array([[-x_dir, 0, 0]]), multiple_hits=False)
    q = locs[0]
    near = np.linalg.norm(body.triangles_center - q, axis=1) < 0.04
    n = body.face_normals[near].mean(0)
    n[1] = 0
    return q, n / np.linalg.norm(n)


def options(body, cams):
    """Every badge and name choice. dict 'opt|group|choice|variant|material' -> mesh."""
    from masks import load
    size = {v: load(v).shape[1::-1] for v in ('front', 'rear', 'side')}
    out = {}
    for pos, (view, uv) in BADGE_FRONT.items():
        p, n = BA.anchor(body, cams, view, uv, size[view])
        p, n = centre_anchor(body, 1, p)
        lift = 0.010 if pos == 'grille' else 0.0
        for fin in ('colour', 'chrome'):
            for mat, m in BA.badge(body, p + lift * n, n, 0.085, fin).items():
                out[f'opt|badgeF|{pos}|{fin}|{mat}'] = m
    for pos, (view, uv) in BADGE_REAR.items():
        p, n = BA.anchor(body, cams, view, uv, size[view])
        p, n = centre_anchor(body, -1, p)
        for fin in ('colour', 'chrome'):
            for mat, m in BA.badge(body, p, n, 0.075, fin).items():
                out[f'opt|badgeR|{pos}|{fin}|{mat}'] = m
    for pos, (view, uv) in NAME_POS.items():
        p, n = BA.anchor(body, cams, view, uv, size[view])
        if pos == 'centre':
            p, n = centre_anchor(body, -1, p)
        for font, h in (('script', 0.05), ('block', 0.024)):
            parts = [BA.name(body, p, n, font, h)]
            if pos == 'sides':
                q, k = p * [1, -1, 1], n * [1, -1, 1]
                parts.append(BA.name(body, q, k, font, h))
            for mat in parts[0]:
                out[f'opt|name|{pos}|{font}|{mat}'] = trimesh.util.concatenate([d[mat] for d in parts])
    return out


def export_glb(groups, path):
    scene = trimesh.Scene()
    for k, m in groups.items():
        scene.add_geometry(m, node_name=k, geom_name=k)
    scene.export(path)


if __name__ == '__main__':
    g = build()
    for k, m in g.items():
        print(f'{k:10s} {len(m.faces):7d}')
    export_glb(g, sys.argv[1])
