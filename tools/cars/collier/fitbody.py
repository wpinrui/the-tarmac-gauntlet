"""Fit every reference camera to the finished body shell.

The shell is fixed in the side-blueprint frame, so each camera is fitted on
its own against its silhouette (mirrors cut off), with simple tyres added.
"""
import sys
import numpy as np
import trimesh
import cams as C
import profiles as P
from body import body_masks
from fit import fit_one, iou

TYRE_W = 0.22
TRACK_Y = 0.76


def tyres():
    out = []
    for sx in (1, -1):
        for sy in (1, -1):
            c = trimesh.creation.cylinder(radius=P.TYRE_R, height=TYRE_W, sections=64)
            c.apply_transform(trimesh.transformations.rotation_matrix(np.pi / 2, [1, 0, 0]))
            c.apply_translation([sx * P.WHEEL_X, sy * TRACK_Y, P.WHEEL_Z])
            out.append(c)
    return out


def points(shell, n=150000):
    m = trimesh.util.concatenate([shell] + tyres())
    pts, _ = trimesh.sample.sample_surface_even(m, n)
    return pts


if __name__ == '__main__':
    shell = trimesh.load(sys.argv[1], force='mesh')
    out = sys.argv[2]
    starts = [C.load(f) for f in sys.argv[3:]]
    S = points(shell)
    masks = body_masks()
    cams = {}
    for n in C.NAMES:
        best = None
        for st in starts:
            for p0 in [st[n]]:
                q, s = fit_one(p0.copy(), S, masks[n], iters=5000)
                if best is None or s > best[1]:
                    best = (q, s)
        cams[n] = best[0]
        print(n, round(best[1], 4), best[0].round(3), flush=True)
    C.save(out, cams)
