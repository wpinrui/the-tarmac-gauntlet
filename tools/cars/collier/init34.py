"""Grid-search a starting camera for the three-quarter views, then fit."""
import sys
import numpy as np
from scipy import ndimage as ndi
import cams as C
from hull import Grid, load_masks, carve
from fit import surface, render, small, fit_one

out = sys.argv[1]
cams = C.load(out)
masks = load_masks(C.NAMES)
loose = {n: ndi.binary_dilation(m, iterations=3) for n, m in masks.items()}
g = Grid(0.02)
vox = carve(g, {n: cams[n] for n in C.NAMES[:4]}, loose)
vox = vox & vox[:, ::-1, :]
P = g.points(np.argwhere(surface(vox)))
ctr = P.mean(0)
for name, yaws in [('three_quarter_front', range(15, 80, 5)), ('three_quarter_rear', range(110, 175, 5))]:
    m = masks[name]
    h, w = m.shape
    t = small(m)
    ys, xs = np.nonzero(m)
    mc = np.array([xs.mean(), ys.mean()])
    best = None
    for yaw in yaws:
        for pitch in range(0, 35, 5):
            for dist in (5.0, 8.0, 15.0):
                p = np.array([yaw, pitch, 0, *ctr, dist, 1000.0])
                u, v, _ = C.project(p, P, (w, h))
                p[7] *= (xs.max() - xs.min()) / (u.max() - u.min())
                u, v, _ = C.project(p, P, (w, h))
                # shift target so centroids match
                du = (mc[0] - u.mean()) / p[7] * dist
                dv = (mc[1] - v.mean()) / p[7] * dist
                right, up, _ = C.rot(yaw, pitch, 0)
                p[3:6] = ctr - du * right + dv * up
                img = render(p, P, (w, h))
                s = (img & t).sum() / (img | t).sum()
                if best is None or s > best[0]:
                    best = (s, p)
    print(name, 'grid', round(best[0], 3), best[1].round(2))
    cams[name], s = fit_one(best[1], P, m, iters=6000)
    print(name, 'fit', round(s, 3), cams[name].round(3))
C.save(out, cams)
