"""Silhouette masks from the reference renders.

Background is flat grey (221). Anything connected to the border that is close
to it is background; everything else, holes filled, is car.
"""
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

REF = __file__.rsplit('/', 1)[0] + '/ref/'


def load(name):
    return np.asarray(Image.open(REF + name + '.png').convert('RGB')).astype(np.int16)


def silhouette(name, tol=5):
    a = load(name)
    near = np.abs(a - 221).max(2) <= tol
    lab, _ = ndi.label(near)
    border = set(np.unique(np.r_[lab[0], lab[-1], lab[:, 0], lab[:, -1]])) - {0}
    bg = np.isin(lab, list(border))
    car = ndi.binary_fill_holes(~bg)
    car = ndi.binary_opening(car, iterations=1)
    return car


if __name__ == '__main__':
    import sys
    out = sys.argv[1]
    for n in ['side', 'top', 'front', 'rear']:
        m = silhouette(n)
        Image.fromarray((m * 255).astype(np.uint8)).save(f'{out}/mask_{n}.png')
        ys, xs = np.nonzero(m)
        print(n, xs.min(), xs.max(), ys.min(), ys.max())
