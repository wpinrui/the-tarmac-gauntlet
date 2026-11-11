"""Packs the race drivers' helmet from "Racing helmet" by MizuCorp (Sketchfab, CC BY 4.0, credited in
unity/Assets/Raceday/Resources/PitCrew/CREDITS.md), fitted over crew_suit's head. The game draws it on the head
bone of every driver.

Usage: python3 -I tools/pit-crew/helmet.py <racing_helmet.glb> <crew_suit.glb>
Needs numpy, trimesh, rtree and fast-simplification.

Writes unity/Assets/Raceday/Resources/PitCrew/helmet.json: positions in the head bone's frame, already in
Unity's frame (z flipped, as tools/team-hq/crew/pack_crew.py packs the body), and triangles for three parts:
shell (painted in the team's colour), visor (turned down shut), and lip (the liner, vents and visor pivots, all dark). The
helmet's own ground plane is left out, and each part is simplified to keep the stills light. It then checks
that every vertex of the head lies inside the liner. Drivers are drawn without the body's cap and glasses.
"""
import json, os, struct, sys
import numpy as np
import trimesh
import fast_simplification

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '../../unity/Assets/Raceday/Resources/PitCrew/helmet.json')

# The source's materials, by the part each goes to. Its ground plane (Material.005) is dropped.
PARTS = {'Material': 'shell', 'Material.003': 'visor', 'Material.004': 'lip', 'metalser': 'lip', 'dershat': 'lip'}
KEEP = {'shell': 0.12, 'visor': 0.25, 'lip': 0.06}   # the share of each part's triangles kept
SMALL = 600                                          # parts under this many triangles are kept whole
# The source has its visor flipped up. It turns down this far about its pivots (in the source's scene: the
# helmet faces -x, +y up) to shut over the eye port.
VISOR_SHUT = 28
PIVOT = np.array([-0.12, 1.65])

# Placed in the head bone's frame as the body is drawn in glTF: +y up the neck, +z out of the face, metres.
LENGTH = 0.32           # the shell, front to back
CENTRE = np.array([0.0, 0.096, 0.02])    # where the middle of the liner sits
INSIDE = 0.97           # the head may reach this share of the way to the liner


def load(path):
    b = open(path, 'rb').read()
    n = struct.unpack('<I', b[12:16])[0]
    return json.loads(b[20:20 + n]), b[28 + n:]


def accessor(g, b, i):
    a = g['accessors'][i]
    v = g['bufferViews'][a['bufferView']]
    k = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}[a['type']]
    dt = np.dtype({5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}[a['componentType']])
    off = v.get('byteOffset', 0) + a.get('byteOffset', 0)
    stride = v.get('byteStride', 0) or dt.itemsize * k
    raw = np.frombuffer(b, np.uint8, (a['count'] - 1) * stride + dt.itemsize * k, off)
    rows = np.lib.stride_tricks.as_strided(raw, (a['count'], dt.itemsize * k), (stride, 1)).copy()
    x = rows.view(dt)
    return x.reshape(-1, k) if k > 1 else x.ravel()


def source(path):
    """The helmet's triangles by part, in its scene's frame, as {part: (vertices, faces)}."""
    g, b = load(path)
    out = {}

    def walk(i, M):
        n = g['nodes'][i]
        M = M @ (np.array(n['matrix']).reshape(4, 4).T if 'matrix' in n else np.eye(4))
        for p in g['meshes'][n['mesh']]['primitives'] if 'mesh' in n else []:
            part = PARTS.get(g['materials'][p['material']]['name'])
            if part:
                P = accessor(g, b, p['attributes']['POSITION'])
                out.setdefault(part, []).append(((np.c_[P, np.ones(len(P))] @ M.T)[:, :3], accessor(g, b, p['indices']).reshape(-1, 3)))
        for c in n.get('children', []):
            walk(c, M)

    for r in g['scenes'][0]['nodes']:
        walk(r, np.eye(4))
    parts = {k: trimesh.util.concatenate([trimesh.Trimesh(v, f, process=False) for v, f in ms]) for k, ms in out.items()}
    parts['visor'] = shut(parts['visor'])
    return parts


def shut(visor):
    t = np.radians(VISOR_SHUT)
    v = visor.vertices.copy()
    xy = v[:, :2] - PIVOT
    v[:, 0] = xy[:, 0] * np.cos(t) - xy[:, 1] * np.sin(t) + PIVOT[0]
    v[:, 1] = xy[:, 0] * np.sin(t) + xy[:, 1] * np.cos(t) + PIVOT[1]
    return trimesh.Trimesh(v, visor.faces, process=False)


def simplify(m, keep):
    m = trimesh.Trimesh(m.vertices, m.faces)   # welds the seams so the simplifier sees one surface
    if len(m.faces) < SMALL:
        return m
    v, f = fast_simplification.simplify(m.vertices.astype(np.float32), m.faces.astype(np.int32), 1 - keep)
    return trimesh.Trimesh(v, f)


def place(parts):
    """Turns the helmet to face +z (it faces -x in its scene) and scales it so the shell is LENGTH long."""
    turn = np.array([[0, 0, 1], [0, 1, 0], [-1, 0, 0]], float)   # a quarter turn about y: -x goes to +z
    pts = parts['lip'].vertices @ turn.T
    shell = parts['shell'].vertices @ turn.T
    scale = LENGTH / (shell[:, 2].max() - shell[:, 2].min())
    mid = (pts.min(0) + pts.max(0)) / 2
    return {k: trimesh.Trimesh((m.vertices @ turn.T - mid) * scale + CENTRE, m.faces, process=False) for k, m in parts.items()}


def head(glb):
    """The head's vertices in the head bone's frame: what sits above the neck."""
    g, b = load(glb)
    skin = g['skins'][0]
    names = [g['nodes'][j]['name'] for j in skin['joints']]
    ibm = accessor(g, b, skin['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1)
    h = names.index('Head')
    pts = []
    for n in g['nodes']:
        if 'mesh' not in n or n['name'] not in ('Wolf3D_Head', 'Wolf3D_Teeth', 'EyeLeft', 'EyeRight'):
            continue
        for p in g['meshes'][n['mesh']]['primitives']:
            P = accessor(g, b, p['attributes']['POSITION'])
            J = accessor(g, b, p['attributes']['JOINTS_0'])
            W = accessor(g, b, p['attributes']['WEIGHTS_0'])
            on = ((J == h) * W).sum(1) > 0.5
            pts.append((np.c_[P[on], np.ones(on.sum())] @ ibm[h].T)[:, :3])
    return np.concatenate(pts)


def check(parts, pts):
    """Each head vertex's share of the way from the liner's middle out to the liner, along the same line."""
    liner = parts['lip']
    open_below = liner.bounds[0][1] + 0.03   # the neck runs on out of the opening
    pts = pts[pts[:, 1] > open_below]
    d = pts - CENTRE
    r = np.linalg.norm(d, axis=1)
    hits, rays, _ = liner.ray.intersects_location(np.repeat([CENTRE], len(pts), 0), d / r[:, None], multiple_hits=False)
    reach = np.full(len(pts), np.inf)
    reach[rays] = np.linalg.norm(hits - CENTRE, axis=1)
    share = r / reach
    worst = float(share.max())
    print(f'head: {len(pts)} vertices, furthest {worst:.3f} of the way to the liner')
    print('ok: all inside the liner' if worst < INSIDE else 'FAIL: the head pokes through')
    return worst < INSIDE


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    parts = place({k: simplify(m, KEEP[k]) for k, m in source(sys.argv[1]).items()})
    verts, out = [], {}
    for k in ('shell', 'visor', 'lip'):
        base = sum(len(v) for v in verts)
        verts.append(parts[k].vertices)
        # To Unity's frame: z flipped, which also flips every triangle's winding.
        out[k] = [int(x) for x in (parts[k].faces[:, [0, 2, 1]] + base).reshape(-1)]
    u = np.concatenate(verts) * np.array([1, 1, -1])
    res = {'positions': [round(float(x), 5) for x in u.reshape(-1)]}
    res.update(out)
    with open(OUT, 'w') as f:
        json.dump(res, f, separators=(',', ':'))
    print(f'{len(u)} vertices, {sum(len(t) for t in out.values()) // 3} triangles -> {os.path.relpath(OUT)}')
    if not check(parts, head(sys.argv[2])):
        sys.exit(1)


if __name__ == '__main__':
    main()
