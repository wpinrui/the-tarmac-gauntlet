"""Packs the rigged, animated crew bodies into the files the Team HQ scene reads.

Usage: python3 -I pack_crew.py <folder with crew_overalls.glb, crew_suit.glb, crew_vest.glb> <out folder>

The bodies come from the crew animation pipeline (Mixamo clips retargeted onto each body in Blender) and are
the same files the Team HQ viewer plays. Unlike the props, these files are converted into Unity's frame here
(Z flipped: positions z -> -z, rotations (x, y, z, w) -> (-x, -y, z, w)), because only Unity reads them.

For each body it writes:
  <id>.json        nodes (name, parent, rest pose), skin joints and inverse binds, materials, mesh parts, clips
  <id>_mesh.bytes  per part: float32 positions, normals, uvs, uint8x4 joints, float32x4 weights, uint32 indices
  <id>_anim.bytes  per clip, per frame: int16x4 rotation of every animated node, then float32x3 position of
                   every node whose position moves or
                   differs from its rest pose (the others keep their rest position)
  textures         colour and normal maps (at most 1024 px), and the clothing colourways
The coffee cart, desk and stool the crew use are ordinary props: Poly Haven's CoffeeCart_01, metal_office_desk
and folding_wooden_stool, packed by ../props/pack_props.py as coffee_cart, desk and stool at their own size.

Colourways repaint the clothes and keep skin as it is, exactly as the viewer does: texels on triangles
skinned to the head, neck, arms or hands with a skin-like colour are left alone.
"""
import io, json, os, re, struct, sys
import numpy as np
from PIL import Image, ImageDraw

BODIES = ['crew_overalls', 'crew_suit', 'crew_vest']
# Hue shift, saturation and value multipliers, and a tint hue and strength for grey cloth. 0 is the body's own look.
COLOURS = [[0, 1, 1, 0, 0], [150, 1.1, .9, 200, .18], [-110, 1, 1, 20, .22], [60, .8, .8, 120, .15], [200, 1.2, 1.05, 330, .2], [-40, .6, .7, 40, .1]]
SKIN_BONE = re.compile(r'Head|Neck|Arm|Hand|Thumb|Index|Middle|Ring|Pinky')
SUIT_CLOTH = re.compile(r'Outfit_Top|Outfit_Bottom|Headwear')
MAX_TEX = 1024
CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}
FLIP = np.diag([1.0, 1.0, -1.0, 1.0])


def load(path):
    d = open(path, 'rb').read()
    n = struct.unpack('<I', d[12:16])[0]
    g = json.loads(d[20:20 + n])
    return g, d[20 + n + 8:]


def accessor(g, b, i):
    a = g['accessors'][i]
    bv = g['bufferViews'][a['bufferView']]
    dt = np.dtype(CT[a['componentType']])
    n, c = a['count'], NC[a['type']]
    off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    stride = bv.get('byteStride', 0) or dt.itemsize * c
    out = np.lib.stride_tricks.as_strided(np.frombuffer(b, dtype=np.uint8, offset=off), shape=(n, c * dt.itemsize), strides=(stride, 1)).copy()
    out = out.view(dt).reshape(n, c).astype(np.float64)
    if a.get('normalized'):
        out /= np.iinfo(dt).max
    return out


def image(g, b, ti):
    bv = g['bufferViews'][g['images'][g['textures'][ti]['source']]['bufferView']]
    o = bv.get('byteOffset', 0)
    im = Image.open(io.BytesIO(b[o:o + bv['byteLength']])).convert('RGB')
    if max(im.size) > MAX_TEX:
        k = MAX_TEX / max(im.size)
        im = im.resize((max(1, round(im.size[0] * k)), max(1, round(im.size[1] * k))), Image.LANCZOS)
    return im


def pos(v):
    return [float(v[0]), float(v[1]), -float(v[2])]


def rot(q):
    return [-float(q[0]), -float(q[1]), float(q[2]), float(q[3])]


def rgb2hsv(p):
    r, g, b = p[..., 0], p[..., 1], p[..., 2]
    mx, mn = p.max(-1), p.min(-1)
    d = mx - mn
    h = np.zeros_like(mx)
    nz = d > 0
    dd = np.where(nz, d, 1)
    hr = ((g - b) / dd) % 6
    hg = (b - r) / dd + 2
    hb = (r - g) / dd + 4
    h = np.where(mx == r, hr, np.where(mx == g, hg, hb)) * 60
    h = np.where(nz, h, 0)
    h = np.where(h < 0, h + 360, h)
    s = np.where(mx > 0, d / np.where(mx > 0, mx, 1), 0)
    return h, s, mx


def hsv2rgb(h, s, v):
    h = ((h % 360) + 360) % 360
    c = v * s
    x = c * (1 - np.abs((h / 60) % 2 - 1))
    m = v - c
    z = np.zeros_like(h)
    sec = np.floor(h / 60).astype(int).clip(0, 5)
    rgb = np.stack([np.choose(sec, [c, x, z, z, x, c]), np.choose(sec, [x, c, c, x, z, z]), np.choose(sec, [z, z, x, c, c, x])], -1)
    return rgb + m[..., None]


def recolour(im, mask, cw):
    dh, sm, vm, th, ts = cw
    p = np.asarray(im, dtype=np.float64) / 255
    h, s, v = rgb2hsv(p)
    keep = (mask > 127) & (h < 38) & (s > .15) & (s < .8) & (v > .15) if mask is not None else np.zeros_like(h, dtype=bool)
    grey = s < .15
    h2 = np.where(grey, th, h + dh)
    s2 = np.where(grey, s + ts * (1 - s) * v, np.minimum(1, s * sm))
    out = hsv2rgb(h2, s2, np.minimum(1, v * vm))
    out = np.where(keep[..., None], p, out)
    return Image.fromarray((out * 255).clip(0, 255).astype(np.uint8))


def pack(src, out, bid):
    g, b = load(os.path.join(src, bid + '.glb'))
    nodes = g['nodes']
    root = g['scenes'][0]['nodes'][0]
    parent = {}
    for i, n in enumerate(nodes):
        for c in n.get('children', []):
            parent[c] = i
    # every node under the body's root, parents first
    order, stack = [], [root]
    while stack:
        i = stack.pop(0)
        order.append(i)
        stack.extend(nodes[i].get('children', []))
    idx = {n: k for k, n in enumerate(order)}
    jnodes = []
    for k, i in enumerate(order):
        n = nodes[i]
        jnodes.append({'name': n.get('name', 'node%d' % i), 'parent': idx.get(parent.get(i), -1),
                       't': pos(n.get('translation', [0, 0, 0])), 'r': rot(n.get('rotation', [0, 0, 0, 1])), 's': [float(x) for x in n.get('scale', [1, 1, 1])]})
    sk = g['skins'][0]
    joints = [idx[j] for j in sk['joints']]
    ibm = accessor(g, b, sk['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1)
    ibm = np.einsum('ij,njk,kl->nil', FLIP, ibm, FLIP)
    skinj = np.array([bool(SKIN_BONE.search(nodes[j].get('name', ''))) for j in sk['joints']])

    # materials, and the textures they read
    mats, texfile = [], {}
    def tex(ti, suffix):
        if ti not in texfile:
            name = '%s_t%d_%s' % (bid, ti, suffix)
            image(g, b, ti).save(os.path.join(out, name + '.jpg'), quality=88)
            texfile[ti] = name
        return texfile[ti]
    for mi, m in enumerate(g['materials']):
        pbr = m.get('pbrMetallicRoughness', {})
        col = pbr.get('baseColorTexture', {}).get('index')
        nrm = m.get('normalTexture', {}).get('index')
        mats.append({'name': m.get('name', ''), 'col': tex(col, 'c') if col is not None else '', 'nrm': tex(nrm, 'n') if nrm is not None else '',
                     'color': [float(x) for x in pbr.get('baseColorFactor', [1, 1, 1, 1])],
                     'metal': float(pbr.get('metallicFactor', 1)), 'rough': float(pbr.get('roughnessFactor', 1)), 'colours': []})

    # mesh parts, and per material the uv triangles of skin for the colourways
    data = bytearray()
    parts, skin_tris = [], {}
    for i in order:
        n = nodes[i]
        if 'mesh' not in n:
            continue
        for pr in g['meshes'][n['mesh']]['primitives']:
            at = pr['attributes']
            p = accessor(g, b, at['POSITION'])
            nr = accessor(g, b, at['NORMAL'])
            uv = accessor(g, b, at['TEXCOORD_0'])
            jt = accessor(g, b, at['JOINTS_0']).astype(int)
            wt = accessor(g, b, at['WEIGHTS_0'])
            wt /= np.maximum(wt.sum(1, keepdims=True), 1e-9)
            ix = accessor(g, b, pr['indices']).astype(np.int64).ravel() if 'indices' in pr else np.arange(len(p))
            mi = pr.get('material', 0)
            sk_v = (wt * skinj[jt]).sum(1) >= .5
            t = ix.reshape(-1, 3)
            skin_tris.setdefault(mi, []).append(uv[t[sk_v[t].all(1)]])
            part = {'mat': mi, 'n': len(p), 'i': len(ix)}
            p = p.copy(); p[:, 2] *= -1
            nr = nr.copy(); nr[:, 2] *= -1
            t2 = t[:, [0, 2, 1]].ravel()
            for key, arr in (('pos', p.astype('<f4')), ('nrm', nr.astype('<f4')), ('uv', np.stack([uv[:, 0], 1 - uv[:, 1]], 1).astype('<f4')),
                             ('joints', jt.astype(np.uint8)), ('weights', wt.astype('<f4')), ('idx', t2.astype('<u4'))):
                part[key] = len(data)
                data += arr.tobytes()
            parts.append(part)
    open(os.path.join(out, bid + '_mesh.bytes'), 'wb').write(bytes(data))

    # colourways
    suit = bid == 'crew_suit'
    for mi, m in enumerate(g['materials']):
        col = m.get('pbrMetallicRoughness', {}).get('baseColorTexture', {}).get('index')
        names = ['']
        for k in range(1, len(COLOURS)):
            if col is None or (suit and not SUIT_CLOTH.search(m.get('name', ''))):
                names.append('')
                continue
            im = image(g, b, col)
            w, h = im.size
            mask = None
            if not suit:
                mk = Image.new('L', (w, h), 0)
                dr = ImageDraw.Draw(mk)
                for tris in skin_tris.get(mi, []):
                    for tr in tris:
                        pts = [(float(u * w), float(v * h)) for u, v in tr]
                        dr.polygon(pts, fill=255, outline=255, width=2)
                mask = np.asarray(mk)
            name = '%s_m%d_k%d_c' % (bid, mi, k)
            recolour(im, mask, COLOURS[k]).save(os.path.join(out, name + '.jpg'), quality=88)
            names.append(name)
        mats[mi]['colours'] = names

    # clips: rotations of every animated node, positions of the nodes whose position moves
    anim = bytearray()
    clips = []
    for a in g['animations']:
        ch = {}
        times = None
        for c in a['channels']:
            ni = c['target']['node']
            if ni not in idx:
                continue
            s = a['samplers'][c['sampler']]
            tm = accessor(g, b, s['input']).ravel()
            times = tm if times is None or len(tm) > len(times) else times
            ch[(idx[ni], c['target']['path'])] = (tm, accessor(g, b, s['output']))
        fps = round((len(times) - 1) / (times[-1] - times[0])) if len(times) > 1 else 24
        frames = len(times)
        def sample(tm, v):
            out = np.empty((frames, v.shape[1]))
            for k in range(v.shape[1]):
                out[:, k] = np.interp(times, tm, v[:, k])
            return out
        rnodes = sorted(n for (n, path) in ch if path == 'rotation')
        pnodes = []
        for (n, path), (tm, v) in sorted(ch.items()):
            rest = np.array(nodes[order[n]].get('translation', [0, 0, 0]))
            if path == 'translation' and (np.ptp(v, 0).max() > 1e-4 or np.abs(v.mean(0) - rest).max() > 1e-4):
                pnodes.append(n)
        off = len(anim)
        R = np.stack([sample(*ch[(n, 'rotation')]) for n in rnodes], 1) if rnodes else np.zeros((frames, 0, 4))
        R /= np.maximum(np.linalg.norm(R, axis=2, keepdims=True), 1e-9)
        R = R * np.array([-1, -1, 1, 1])
        # keep each node's quaternions on one side so neighbouring frames interpolate the short way
        for f in range(1, frames):
            flip = (R[f] * R[f - 1]).sum(1) < 0
            R[f][flip] *= -1
        anim += np.round(R * 32767).astype('<i2').tobytes()
        P = np.stack([sample(*ch[(n, 'translation')]) for n in pnodes], 1) if pnodes else np.zeros((frames, 0, 3))
        P = P * np.array([1, 1, -1])
        poff = len(anim)
        anim += P.astype('<f4').tobytes()
        clips.append({'name': a['name'], 'frames': frames, 'fps': fps, 'rot': rnodes, 'pos': pnodes, 'off': off, 'posOff': poff})
    open(os.path.join(out, bid + '_anim.bytes'), 'wb').write(bytes(anim))

    info = {'nodes': jnodes, 'joints': joints, 'ibm': ibm.reshape(-1).round(7).tolist(), 'mats': mats, 'parts': parts, 'clips': clips}
    with open(os.path.join(out, bid + '.json'), 'w') as f:
        json.dump(info, f, separators=(',', ':'))
    print(bid, len(order), 'nodes', len(joints), 'joints', len(parts), 'parts', len(clips), 'clips', len(data) // 1024, 'KB mesh', len(anim) // 1024, 'KB anim')


if __name__ == '__main__':
    os.makedirs(sys.argv[2], exist_ok=True)
    for bid in BODIES:
        pack(sys.argv[1], sys.argv[2], bid)
