"""Packs baked impostor atlases for Unity: colour bled a few pixels past the leaf edges (so mipmaps
have no dark fringe) and saved as a 256-colour PNG; normals halved and saved as JPEG.
Usage: python3 -I pack.py <bake out folder> <Resources/Landscape folder> name [name ...]"""
import json, os, shutil, sys
import numpy as np
from PIL import Image


def bleed(rgba, steps=6):
    a = rgba[..., 3] > 127
    rgb = rgba[..., :3].astype(np.float32)
    have = a.copy()
    for _ in range(steps):
        acc = np.zeros_like(rgb); cnt = np.zeros(have.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            h = np.roll(have, (dy, dx), (0, 1)); c = np.roll(rgb, (dy, dx), (0, 1))
            acc += c * h[..., None]; cnt += h
        new = (~have) & (cnt > 0)
        rgb[new] = acc[new] / cnt[new][:, None]
        have |= new
    rgb[~have] = rgb[a].mean(0) if a.any() else 0
    out = np.dstack([rgb.clip(0, 255).astype(np.uint8), np.where(a, 255, 0).astype(np.uint8)])
    return out


src, dst, *names = sys.argv[1:]
os.makedirs(dst, exist_ok=True)
for n in names:
    c = np.array(Image.open(os.path.join(src, f'{n}_colour.png')).convert('RGBA'))
    Image.fromarray(bleed(c)).quantize(256, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE).save(os.path.join(dst, f'{n}_colour.png'), optimize=True)
    nm = Image.open(os.path.join(src, f'{n}_normal.png')).convert('RGBA')
    flat = Image.new('RGBA', nm.size, (128, 128, 255, 255)); flat.alpha_composite(nm)
    flat.convert('RGB').resize((nm.width // 2, nm.height // 2), Image.LANCZOS).save(os.path.join(dst, f'{n}_normal.jpg'), quality=88)
    shutil.copy(os.path.join(src, f'{n}.json'), os.path.join(dst, f'{n}.json'))
    print(n, os.path.getsize(os.path.join(dst, f'{n}_colour.png')) // 1024, 'KB', os.path.getsize(os.path.join(dst, f'{n}_normal.jpg')) // 1024, 'KB')
