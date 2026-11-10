"""Packs Poly Haven glTF props into the small mesh files the Team HQ scene and its viewer both read.

Usage: python3 -I pack_props.py <source folder of <id>/<id>.gltf or .glb> <out folder> <id> [<id> ...]

props.json beside this script can give a prop a height in metres (it is scaled to it and stood on
the origin), a turn in degrees about Y so its front faces +Z, a share of triangles to keep, and a pose for rigged characters: bones turned about world axes at their joints, in
order, then the skin is baked into a still mesh.

For each prop it writes <id>.json (materials and part offsets), <id>_mesh.bytes (the meshes) and its
textures. The mesh stays in glTF's frame (Y up, right-handed, metres), the viewer's frame.
Positions are int16 across the prop's bounds, normals int8, uvs float32, indices uint16 or uint32.
Each material gets a colour map, a normal map and one mask: R metallic, G roughness, B metallic,
A smoothness, so Unity's Standard shader (R and A) and three.js (G and B) read the same file.
"""
import base64, io, json, os, struct, sys
import numpy as np
from PIL import Image

CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}
TEX = 512


def accessor(g, bins, i):
    a = g['accessors'][i]
    bv = g['bufferViews'][a['bufferView']]
    dt = np.dtype(CT[a['componentType']])
    n, c = a['count'], NC[a['type']]
    off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    stride = bv.get('byteStride', 0) or dt.itemsize * c
    raw = bins[bv['buffer']]
    out = np.lib.stride_tricks.as_strided(np.frombuffer(raw, dtype=np.uint8, offset=off),
                                          shape=(n, c * dt.itemsize), strides=(stride, 1)).copy()
    out = out.view(dt).reshape(n, c).astype(np.float64)
    if a.get('normalized'):
        out /= np.iinfo(dt).max
    return out


def skin(g, bins, world, si, at, p, nr):
    """Bakes a skinned primitive into the posed skeleton."""
    sk = g['skins'][si]
    ibm = accessor(g, bins, sk['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1) if 'inverseBindMatrices' in sk \
        else np.tile(np.eye(4), (len(sk['joints']), 1, 1))
    jm = np.stack([world[j] for j in sk['joints']]) @ ibm
    joints = accessor(g, bins, at['JOINTS_0']).astype(int)
    weights = accessor(g, bins, at['WEIGHTS_0'])
    weights /= np.maximum(weights.sum(1, keepdims=True), 1e-9)
    m = np.einsum('vk,vkij->vij', weights, jm[joints])
    p2 = np.einsum('vij,vj->vi', m[:, :3, :3], p) + m[:, :3, 3]
    n2 = np.einsum('vij,vj->vi', np.linalg.inv(m[:, :3, :3]).transpose(0, 2, 1), nr)
    return p2, n2


def node_matrix(n):
    if 'matrix' in n:
        return np.array(n['matrix'], dtype=np.float64).reshape(4, 4).T
    t = np.array(n.get('translation', [0, 0, 0]))
    x, y, z, w = n.get('rotation', [0, 0, 0, 1])
    s = np.array(n.get('scale', [1, 1, 1]))
    r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    m = np.eye(4)
    m[:3, :3] = r * s
    m[:3, 3] = t
    return m


def image_bytes(g, bins, folder, img):
    if 'bufferView' in img:
        bv = g['bufferViews'][img['bufferView']]
        o = bv.get('byteOffset', 0)
        return bins[bv['buffer']][o:o + bv['byteLength']]
    if img['uri'].startswith('data:'):
        return base64.b64decode(img['uri'].split(',', 1)[1])
    return open(os.path.join(folder, img['uri']), 'rb').read()


def textures(g, bins, folder, mat, out, base):
    """Writes the three maps for one material and returns their file names (without folder)."""
    pbr = mat.get('pbrMetallicRoughness', {})

    def load(info):
        if not info:
            return None
        img = g['images'][g['textures'][info['index']]['source']]
        return Image.open(io.BytesIO(image_bytes(g, bins, folder, img)))

    names = {}
    col = load(pbr.get('baseColorTexture'))
    alpha = mat.get('alphaMode', 'OPAQUE') != 'OPAQUE'
    if col is not None:
        col = col.convert('RGBA' if alpha else 'RGB').resize((TEX, TEX), Image.LANCZOS)
        names['col'] = base + '_c' + ('.png' if alpha else '.jpg')
        col.save(os.path.join(out, names['col']), quality=88) if not alpha else col.save(os.path.join(out, names['col']), optimize=True)
    nrm = load(mat.get('normalTexture'))
    if nrm is not None:
        names['nrm'] = base + '_n.jpg'
        nrm.convert('RGB').resize((TEX, TEX), Image.LANCZOS).save(os.path.join(out, names['nrm']), quality=90)
    mr = load(pbr.get('metallicRoughnessTexture'))
    if mr is not None:
        mr = np.asarray(mr.convert('RGB').resize((TEX // 2, TEX // 2), Image.LANCZOS)).astype(np.uint8)
        # glTF scales the map by the material's factors; bake them in so both readers use the map alone.
        rough = (mr[..., 1] * pbr.get('roughnessFactor', 1.0)).round().astype(np.uint8)
        metal = (mr[..., 2] * pbr.get('metallicFactor', 1.0)).round().astype(np.uint8)
        mask = np.stack([metal, rough, metal, 255 - rough], -1)
        names['ms'] = base + '_s.png'
        Image.fromarray(mask, 'RGBA').save(os.path.join(out, names['ms']), optimize=True)
    return names


def load(folder, pid):
    glb = os.path.join(folder, pid + '.glb')
    if os.path.exists(glb):
        data = open(glb, 'rb').read()
        n = struct.unpack_from('<I', data, 12)[0]
        g = json.loads(data[20:20 + n])
        bins = [data[20 + n + 8:]]
        return g, bins
    g = json.load(open(os.path.join(folder, pid + '.gltf')))
    bins = []
    for b in g['buffers']:
        u = b['uri']
        bins.append(base64.b64decode(u.split(',', 1)[1]) if u.startswith('data:') else open(os.path.join(folder, u), 'rb').read())
    return g, bins


def world_matrices(g, pose):
    """Every node's world matrix, with the pose's bone turns applied to each bone and everything under it."""
    parent = {c: i for i, n in enumerate(g['nodes']) for c in n.get('children', [])}
    world = {}

    def get(i):
        if i not in world:
            local = node_matrix(g['nodes'][i])
            world[i] = get(parent[i]) @ local if i in parent else local
        return world[i]

    for i in range(len(g['nodes'])):
        get(i)
    names = {n.get('name'): i for i, n in enumerate(g['nodes'])}

    def subtree(i):
        yield i
        for c in g['nodes'][i].get('children', []):
            yield from subtree(c)

    for bone, axis, deg in pose:
        b = names[bone]
        p = world[b][:3, 3].copy()
        ax = np.array(axis, dtype=np.float64) / np.linalg.norm(axis)
        a = np.radians(deg)
        k = np.array([[0, -ax[2], ax[1]], [ax[2], 0, -ax[0]], [-ax[1], ax[0], 0]])
        r = np.eye(3) + np.sin(a) * k + (1 - np.cos(a)) * k @ k
        d = np.eye(4); d[:3, :3] = r; d[:3, 3] = p - r @ p
        for i in subtree(b):
            world[i] = d @ world[i]
    return world


def pack(src, out, pid):
    """Packs one prop, or with split, each child of the model's first branching node as its own prop."""
    folder = os.path.join(src, pid)
    g, bins = load(folder, pid)
    opts = CONFIG.get(pid, {})
    world = world_matrices(g, opts.get('pose', []))
    roots = g['scenes'][g.get('scene', 0)]['nodes']
    if not opts.get('split'):
        emit(g, bins, folder, out, world, roots, opts.get('name', pid), opts)
        return
    while len(roots) == 1:
        roots = g['nodes'][roots[0]].get('children', [])
    for i, r in enumerate(roots):
        one = dict(opts, height=opts['heights'][i])
        emit(g, bins, folder, out, world, [r], f"{opts.get('name', pid)}_{i + 1}", one)


def decimate(p, nr, uv, idx, keep):
    """Collapses edges until about keep of the triangles are left; each kept vertex keeps its own normal and uv."""
    import fast_simplification
    _, faces, collapses = fast_simplification.simplify(p.astype(np.float32), idx.reshape(-1, 3), 1 - keep, return_collapses=True)
    p2, f2, mapping = fast_simplification.replay_simplification(p.astype(np.float32), idx.reshape(-1, 3), collapses)
    first = np.full(len(p2), -1)
    for old in range(len(mapping) - 1, -1, -1):
        first[mapping[old]] = old
    return p2.astype(np.float64), nr[first], uv[first], f2.ravel().astype(np.int64)


def emit(g, bins, folder, out, world, roots, pid, opts):
    groups = {}

    def walk(ni):
        n = g['nodes'][ni]
        m = world[ni]
        if 'mesh' in n:
            for prim in g['meshes'][n['mesh']]['primitives']:
                at = prim['attributes']
                p = accessor(g, bins, at['POSITION'])
                nr = accessor(g, bins, at['NORMAL']) if 'NORMAL' in at else np.tile([0, 1, 0], (len(p), 1))
                uv = accessor(g, bins, at['TEXCOORD_0']) if 'TEXCOORD_0' in at else np.zeros((len(p), 2))
                idx = accessor(g, bins, prim['indices'])[:, 0].astype(np.int64) if 'indices' in prim else np.arange(len(p))
                if 'skin' in n and 'JOINTS_0' in at:
                    p, nr = skin(g, bins, world, n['skin'], at, p, nr)
                else:
                    p = (np.c_[p, np.ones(len(p))] @ m.T)[:, :3]
                    nr = nr @ np.linalg.inv(m[:3, :3])
                nr /= np.maximum(np.linalg.norm(nr, axis=1, keepdims=True), 1e-9)
                if 'skin' not in n and np.linalg.det(m[:3, :3]) < 0:
                    idx = idx.reshape(-1, 3)[:, [0, 2, 1]].ravel()
                gr = groups.setdefault(prim.get('material', -1), [[], [], [], [], 0])
                gr[0].append(p); gr[1].append(nr); gr[2].append(uv); gr[3].append(idx + gr[4]); gr[4] += len(p)
        for c in n.get('children', []):
            walk(c)

    for ni in roots:
        walk(ni)
    if 'keep' in opts:
        for v in groups.values():
            p, nr, uv, idx = decimate(np.concatenate(v[0]), np.concatenate(v[1]), np.concatenate(v[2]), np.concatenate(v[3]), opts['keep'])
            v[:] = [[p], [nr], [uv], [idx], len(p)]

    if 'turn' in opts:
        a = np.radians(opts['turn'])
        ry = np.array([[np.cos(a), 0, np.sin(a)], [0, 1, 0], [-np.sin(a), 0, np.cos(a)]])
        for v in groups.values():
            v[0] = [q @ ry.T for q in v[0]]
            v[1] = [q @ ry.T for q in v[1]]
    allp = np.concatenate([np.concatenate(v[0]) for v in groups.values()])
    lo, hi = allp.min(0), allp.max(0)
    if 'height' in opts:
        k = opts['height'] / (hi[1] - lo[1])
        shift = np.array([(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2])
        for v in groups.values():
            v[0] = [(q - shift) * k for q in v[0]]
        lo, hi = (lo - shift) * k, (hi - shift) * k
    span = np.maximum(hi - lo, 1e-6)
    buf = bytearray()

    def pad():
        while len(buf) % 4:
            buf.append(0)

    mats, parts = [], []
    for k, (ps, ns, us, ids, count) in groups.items():
        p, nr, uv, idx = np.concatenate(ps), np.concatenate(ns), np.concatenate(us), np.concatenate(ids)
        mat = g['materials'][k] if k >= 0 else {}
        pbr = mat.get('pbrMetallicRoughness', {})
        names = textures(g, bins, folder, mat, out, f'{pid}_{len(mats)}') if k >= 0 else {}
        mats.append({'col': names.get('col', ''), 'nrm': names.get('nrm', ''), 'ms': names.get('ms', ''),
                     'color': pbr.get('baseColorFactor', [1, 1, 1, 1]),
                     'metal': 1.0 if names.get('ms') else pbr.get('metallicFactor', 1.0),
                     'rough': 1.0 if names.get('ms') else pbr.get('roughnessFactor', 1.0),
                     'cutout': mat.get('alphaMode') == 'MASK', 'blend': mat.get('alphaMode') == 'BLEND',
                     'twoSided': bool(mat.get('doubleSided'))})
        q = np.round((p - lo) / span * 65535 - 32768).clip(-32768, 32767).astype('<i2')
        po = len(buf); buf += q.tobytes(); pad()
        no = len(buf); buf += np.round(nr * 127).clip(-127, 127).astype('<i1').tobytes(); pad()
        uo = len(buf); buf += uv.astype('<f4').tobytes()
        wide = count > 65535
        io = len(buf); buf += idx.astype('<u4' if wide else '<u2').tobytes(); pad()
        parts.append({'mat': len(mats) - 1, 'n': int(count), 'i': int(len(idx)), 'pos': po, 'nrm': no, 'uv': uo,
                      'idx': io, 'wide': wide})
    open(os.path.join(out, pid + '_mesh.bytes'), 'wb').write(buf)
    json.dump({'min': lo.round(5).tolist(), 'max': hi.round(5).tolist(), 'mats': mats, 'parts': parts},
              open(os.path.join(out, pid + '.json'), 'w'))
    tris = sum(p['i'] for p in parts) // 3
    print(f'{pid}: {tris} tris, {len(buf) // 1024} KB, size {np.round(hi - lo, 2).tolist()}')


CONFIG = {}
_cfg = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'props.json')
if os.path.exists(_cfg):
    CONFIG = json.load(open(_cfg))


if __name__ == '__main__':
    os.makedirs(sys.argv[2], exist_ok=True)
    for pid in sys.argv[3:]:
        pack(sys.argv[1], sys.argv[2], pid)
