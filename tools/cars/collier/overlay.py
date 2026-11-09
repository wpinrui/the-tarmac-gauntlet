"""Overlay the carved hull's projection on each reference silhouette.
Red: reference only. Green: hull only. Yellow: both."""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage as ndi
import cams as C
from hull import Grid, load_masks, carve
from fit import surface, render

cams = C.load(sys.argv[1])
out = sys.argv[2]
masks = load_masks(C.NAMES)
loose = {n: ndi.binary_dilation(m, iterations=3) for n, m in masks.items()}
g = Grid(0.01)
vox = carve(g, cams, loose)
vox = vox & vox[:, ::-1, :]
np.save(out + '/vox.npy', vox)
P = g.points(np.argwhere(surface(vox)))
tiles = []
for n in C.NAMES:
    m = masks[n]
    h, w = m.shape
    img = render(cams[n], P, (w, h), k=1, grow=2)
    rgb = np.zeros((h, w, 3), np.uint8)
    rgb[..., 0] = m * 255
    rgb[..., 1] = img * 255
    print(n, round((img & m).sum() / (img | m).sum(), 4))
    tiles.append(Image.fromarray(rgb))
W = 1600
rows = []
y = 0
canvas = Image.new('RGB', (W * 2, 2100))
for i, t in enumerate(tiles):
    canvas.paste(t, ((i % 2) * W, (i // 2) * 1000))
canvas.resize((W, 1050)).save(out + '/overlay.png')
