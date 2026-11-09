"""Carve a voxel hull from the reference silhouettes."""
import numpy as np
from scipy import ndimage as ndi
from masks import silhouette
import cams as C

L, H = 4.03, 1.452  # overall length and height, metres


class Grid:
    def __init__(self, step, lo=(-2.2, -1.05, 0.0), hi=(2.2, 1.05, 1.6)):
        self.step = step
        self.lo = np.array(lo)
        self.shape = tuple(int(round((h - l) / step)) + 1 for l, h in zip(lo, hi))

    def points(self, idx=None):
        if idx is None:
            idx = np.indices(self.shape).reshape(3, -1).T
        return self.lo + idx * self.step


def load_masks(names, tol=0):
    out = {}
    for n in names:
        m = silhouette(n)
        if tol > 0:
            m = ndi.binary_dilation(m, iterations=tol)
        out[n] = m
    return out


def inside(params, mask, P):
    h, w = mask.shape
    u, v, _ = C.project(params, P, (w, h))
    ui = np.round(u).astype(int)
    vi = np.round(v).astype(int)
    ok = (ui >= 0) & (ui < w) & (vi >= 0) & (vi < h)
    res = np.zeros(len(P), bool)
    res[ok] = mask[vi[ok], ui[ok]]
    return res


def carve(grid, cams, masks, P=None):
    if P is None:
        P = grid.points()
    keep = np.ones(len(P), bool)
    for n, params in cams.items():
        idx = np.nonzero(keep)[0]
        keep[idx] = inside(params, masks[n], P[idx])
    return keep.reshape(grid.shape)


def ortho_start():
    """Rough near-orthographic cameras from the silhouette bounding boxes."""
    D = 200.0
    cams = {}
    # side: nose at image left, x 40..1436, ground at v 561
    s = L / (1436 - 40)
    u0, v0 = 1504 / 2, 616 / 2
    cams['side'] = [90, 0, 0, (738 - u0) * s, 0, (561 - v0) * s, D, D / s]
    # top: nose at image right, x 61..1304; body edges v 80..648
    s = L / (1304 - 61)
    u0, v0 = 1338 / 2, 774 / 2
    cams['top'] = [-90, 89.5, 0, (u0 - 682) * s, (364 - v0) * s, 0.7, D, D / s]
    # front: body x 76..750, y 51..604
    s = H / (604 - 51)
    u0, v0 = 805 / 2, 676 / 2
    cams['front'] = [0, 0, 0, 0, (u0 - 413) * s, (604 - v0) * s, D, D / s]
    s = H / (655 - 40)
    u0, v0 = 1012 / 2, 715 / 2
    cams['rear'] = [180, 0, 0, 0, (491 - u0) * s, (655 - v0) * s, D, D / s]
    return {k: np.array(v, float) for k, v in cams.items()}
