"""Fit a pinhole camera to every reference view, carving as we go.

Loop: carve the hull with the current cameras, then move each camera so the
hull's projection matches that view's silhouette, then carve again.
"""
import sys
import numpy as np
from scipy import ndimage as ndi
from scipy.optimize import minimize
import cams as C
from hull import Grid, load_masks, carve, ortho_start

K = 4  # fit at 1/K resolution


def surface(vox):
    return vox & ~ndi.binary_erosion(vox)


def render(params, P, size, k=K, grow=1):
    w, h = size
    u, v, z = C.project(params, P, size)
    wk, hk = (w + k - 1) // k, (h + k - 1) // k
    ui = np.round(u / k).astype(int)
    vi = np.round(v / k).astype(int)
    ok = (ui >= 0) & (ui < wk) & (vi >= 0) & (vi < hk) & (z > 0)
    img = np.zeros((hk, wk), bool)
    img[vi[ok], ui[ok]] = True
    if grow:
        img = ndi.binary_dilation(img, iterations=grow)
    return ndi.binary_fill_holes(img)


def small(mask, k=K):
    h, w = mask.shape
    hk, wk = (h + k - 1) // k, (w + k - 1) // k
    pad = np.zeros((hk * k, wk * k), bool)
    pad[:h, :w] = mask
    return pad.reshape(hk, k, wk, k).mean((1, 3)) > 0.5


STEP = np.array([1, 1, 1, 0.01, 0.01, 0.01, 0.1, 0.01])


def unpack(params, x):
    """x moves angles, target, log distance and log image scale."""
    p = params.copy()
    p[:6] += x[:6] * STEP[:6]
    scale = params[7] / params[6] * np.exp(x[7] * STEP[7])
    p[6] = params[6] * np.exp(x[6] * STEP[6])
    p[7] = scale * p[6]
    return p


def fit_one(params, P, mask, iters=4000):
    h, w = mask.shape
    target = small(mask)

    def loss(x):
        img = render(unpack(params, x), P, (w, h))
        return 1 - (img & target).sum() / max((img | target).sum(), 1)

    r = minimize(loss, np.zeros(8), method='Powell',
                 options={'maxfev': iters, 'xtol': 1e-3, 'ftol': 1e-5})
    return unpack(params, r.x), 1 - r.fun


def iou(params, P, mask):
    h, w = mask.shape
    img = render(params, P, (w, h))
    t = small(mask)
    return (img & t).sum() / (img | t).sum()


if __name__ == '__main__':
    out = sys.argv[1]
    names = sys.argv[2].split(',') if len(sys.argv) > 2 else C.NAMES[:4]
    rounds = int(sys.argv[3]) if len(sys.argv) > 3 else 3
    try:
        cams = C.load(out)
    except FileNotFoundError:
        cams = ortho_start()
    masks = load_masks(C.NAMES, tol=0)
    loose = {n: ndi.binary_dilation(m, iterations=3) for n, m in masks.items()}
    g = Grid(0.02)
    for rnd in range(rounds):
        use = {n: cams[n] for n in names if n in cams}
        vox = carve(g, use, loose)
        vox = vox & vox[:, ::-1, :]  # the car is symmetric
        P = g.points(np.argwhere(surface(vox)))
        print('round', rnd, 'voxels', vox.sum(), flush=True)
        for n in names:
            if n not in cams:
                continue
            before = iou(cams[n], P, masks[n])
            cams[n], after = fit_one(cams[n], P, masks[n])
            print(f'  {n}: {before:.3f} -> {after:.3f}', [round(x, 3) for x in cams[n]], flush=True)
        C.save(out, cams)
