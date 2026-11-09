"""Alternate between silhouettes and landmarks until the cameras agree.

Each round: carve a hull from the other five views, move one camera to match
both its silhouette and its landmarks, then re-solve the landmark points.
"""
import sys
import json
import numpy as np
from scipy import ndimage as ndi
from scipy.optimize import minimize, least_squares
import cams as C
from hull import Grid, load_masks, carve
from fit import surface, render, small, unpack as move
from landmarks import LANDMARKS
import bundle as B

LAMBDA = float(__import__("os").environ.get("LAMBDA", "0.0005"))  # IoU units per squared pixel of landmark error


def view_obs(v, pts):
    P, uv, w = [], [], []
    for n, (kind, wt, obs) in LANDMARKS.items():
        for side, q in obs.get(v, {}).items():
            P.append(B.world(n, side, pts[n]))
            uv.append(q)
            w.append(wt)
    return np.array(P), np.array(uv, float), np.array(w)


MIN_DIST = {'side': 20.0}


def fit_cam(params, S, mask, P, uv, w, iters=3000, min_dist=0.0):
    h, wd = mask.shape
    target = small(mask)

    def loss(x):
        p = move(params, x)
        img = render(p, S, (wd, h))
        iou = (img & target).sum() / max((img | target).sum(), 1)
        u, v, z = C.project(p, P, (wd, h))
        e = np.mean(w * ((u - uv[:, 0]) ** 2 + (v - uv[:, 1]) ** 2))
        return (1 - iou) + LAMBDA * e + max(0.0, min_dist - p[6])

    r = minimize(loss, np.zeros(8), method='Powell', options={'maxfev': iters, 'xtol': 1e-3, 'ftol': 1e-6})
    return move(params, r.x), r.fun


def solve_points(cams, pts, sizes):
    x0 = B.pack(cams, pts)
    nc = len(B.views) * 8
    f = lambda xp: B.residuals(np.r_[x0[:nc], xp], sizes)
    r = least_squares(f, x0[nc:], loss='soft_l1', f_scale=4.0)
    _, out = B.unpack(np.r_[x0[:nc], r.x])
    return out, np.sqrt(np.mean(r.fun[:-11] ** 2))


if __name__ == '__main__':
    src, dst, rounds = sys.argv[1], sys.argv[2], int(sys.argv[3])
    sizes = {k: tuple(v) for k, v in json.load(open('sizes.json')).items()}
    cams = C.load(src)
    pts = {k: np.array(v) for k, v in json.load(open(src.replace('.json', '_pts.json'))).items()}
    masks = load_masks(C.NAMES)
    loose = {n: ndi.binary_dilation(m, iterations=4) for n, m in masks.items()}
    g = Grid(0.02, lo=(-2.6, -1.3, -0.3), hi=(2.6, 1.3, 1.8))
    for rnd in range(rounds):
        for v in C.NAMES:
            others = {k: c for k, c in cams.items() if k != v}
            vox = carve(g, others, loose)
            vox = vox & vox[:, ::-1, :]
            S = g.points(np.argwhere(surface(vox)))
            P, uv, w = view_obs(v, pts)
            cams[v], l = fit_cam(cams[v], S, masks[v], P, uv, w, min_dist=MIN_DIST.get(v, 0.0))
            print(rnd, v, round(l, 4), flush=True)
        pts, rms = solve_points(cams, pts, sizes)
        print('round', rnd, 'landmark rms', round(rms, 2), flush=True)
        C.save(dst, cams)
        json.dump({n: list(map(float, p)) for n, p in pts.items()}, open(dst.replace('.json', '_pts.json'), 'w'), indent=1)
