"""Measures the leaves' bounds in each frame of the packed impostor atlases, so each tree's quad can be cut down to
them (Resources/Landscape/Impostor.shader draws the quad only that big). Writes crop.bytes: per kind, per frame
(frame x + frame y * frames, frame y counted up from the bottom of the image, as texture coordinates count), four bytes,
the frame's left, bottom, right and top in 255ths of the frame, with a margin for the leaves the smaller mipmaps
spread outwards.
Usage: python3 -I frames.py <Resources/Landscape folder> name [name ...]"""
import os, sys
import numpy as np
from PIL import Image

FRAMES = 8
MARGIN = 12   # texels of a 256-texel frame: about what mipmap 3, the shadows' outline, spreads the leaves


def crop(alpha, frames=FRAMES):
    size = alpha.shape[0] // frames
    out = []
    for fy in range(frames):
        for fx in range(frames):
            row = frames - 1 - fy   # image rows count down from the top
            a = alpha[row * size:(row + 1) * size, fx * size:(fx + 1) * size]
            ys, xs = np.where(a.any(1))[0], np.where(a.any(0))[0]
            if not len(ys):
                out += [0, 0, 0, 0]
                continue
            m = MARGIN * size / 256
            left, right = (xs[0] - m) / size, (xs[-1] + 1 + m) / size
            bottom, top = 1 - (ys[-1] + 1 + m) / size, 1 - (ys[0] - m) / size
            out += [int(np.floor(np.clip(left, 0, 1) * 255)), int(np.floor(np.clip(bottom, 0, 1) * 255)),
                    int(np.ceil(np.clip(right, 0, 1) * 255)), int(np.ceil(np.clip(top, 0, 1) * 255))]
    return out


folder, *names = sys.argv[1:]
data = []
for n in names:
    alpha = np.asarray(Image.open(os.path.join(folder, f'{n}_colour.png')).convert('RGBA'))[..., 3] > 127
    c = crop(alpha)
    area = np.mean([(c[i + 2] - c[i]) * (c[i + 3] - c[i + 1]) / 255 ** 2 for i in range(0, len(c), 4)])
    print(f'{n}: quads cut to {area:.0%} of their area on average')
    data += c
open(os.path.join(folder, 'crop.bytes'), 'wb').write(bytes(data))
