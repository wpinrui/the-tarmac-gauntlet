"""Fix scale and origin of the fitted cameras.

The silhouette fit leaves scale and position free. Scale the world so the
carved hull is L long, then put the origin on the ground at mid wheelbase,
using the wheel centres picked in the side view.
"""
import sys
import numpy as np
from scipy import ndimage as ndi
import cams as C
from hull import Grid, load_masks, carve, L

# wheel centres and tyre radius in the side render, picked by eye
SIDE_WHEELS = [(270.0, 432.0), (1246.0, 433.0)]
SIDE_TYRE_R = 122.0


def transform(cams, s, o):
    out = {}
    for n, p in cams.items():
        q = p.copy()
        q[3:6] = (p[3:6] - o) * s
        q[6] = p[6] * s
        out[n] = q
    return out


def ray_plane_y(params, uv, size, y):
    c = C.centre(params)
    d = C.ray_dirs(params, np.array([uv], float), size)[0]
    t = (y - c[1]) / d[1]
    return c + t * d


if __name__ == '__main__':
    src, dst = sys.argv[1], sys.argv[2]
    cams = C.load(src)
    masks = load_masks(C.NAMES)
    loose = {n: ndi.binary_dilation(m, iterations=3) for n, m in masks.items()}
    g = Grid(0.01, lo=(-3, -1.5, -0.5), hi=(3, 1.5, 2.2))
    vox = carve(g, cams, loose)
    idx = np.argwhere(vox)
    P = g.points(idx)
    x0, x1 = P[:, 0].min(), P[:, 0].max()
    s = L / (x1 - x0)
    cams = transform(cams, s, np.zeros(3))
    P = P * s
    half = P[:, 1].max()
    size = masks['side'].shape[::-1]
    w = [ray_plane_y(cams['side'], uv, size, half - 0.03) for uv in SIDE_WHEELS]
    # tyre radius: project a vertical offset at the wheel centre
    u, v, _ = C.project(cams['side'], np.array(w), size)
    _, v2, _ = C.project(cams['side'], np.array(w) + [0, 0, 0.1], size)
    px_per_m = np.mean((v - v2) / 0.1)
    r = SIDE_TYRE_R / px_per_m
    zc = np.mean([p[2] for p in w])
    o = np.array([(w[0][0] + w[1][0]) / 2, 0.0, zc - r])
    cams = transform(cams, 1.0, o)
    C.save(dst, cams)
    print('scale', s, 'wheelbase', w[0][0] - w[1][0], 'tyre r', r, 'wheel z', zc - o[2])
    print('front wheel', w[0] - o, 'rear wheel', w[1] - o)
    print('half width', half, 'height', P[:, 2].max() - o[2], 'bottom', P[:, 2].min() - o[2])
