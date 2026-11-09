"""Body silhouette (green) over the reference silhouette (red), per view."""
import sys
import numpy as np
import trimesh
from PIL import Image
import cams as C
from body import body_masks
from fit import render
from fitbody import points

shell = trimesh.load(sys.argv[1], force='mesh')
cams = C.load(sys.argv[2])
out = sys.argv[3]
S = points(shell, 400000)
masks = body_masks()
tiles = []
for n in C.NAMES:
    m = masks[n]
    h, w = m.shape
    img = render(cams[n], S, (w, h), k=1, grow=1)
    rgb = np.full((h, w, 3), 40, np.uint8)
    rgb[m & ~img] = (255, 60, 60)
    rgb[img & ~m] = (60, 255, 60)
    rgb[img & m] = (200, 200, 200)
    print(n, round((img & m).sum() / (img | m).sum(), 4))
    tiles.append(Image.fromarray(rgb))
W = 1600
canvas = Image.new('RGB', (W * 2, 3000), (0, 0, 0))
y = [0, 0]
for i, t in enumerate(tiles):
    col = i % 2
    canvas.paste(t, (col * W, y[col]))
    y[col] += t.height + 10
canvas = canvas.crop((0, 0, 2 * W, max(y)))
canvas.resize((canvas.width // 2, canvas.height // 2)).save(out)
