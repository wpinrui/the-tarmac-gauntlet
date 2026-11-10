"""Turns Poly Haven textures (CC0) into the building's tidy, painted surfaces.

Usage: python3 -I make_textures.py <folder of <id>/Diffuse.jpg, nor_gl.jpg, arm.jpg> <out folder>

Each surface keeps the photo's relief (normal map) and wear pattern, recoloured to the approved look:
the colour is the tint, varied by the photo's own light and dark at the given contrast. The mask uses
the same channels as the props: R metallic, G roughness, B metallic, A smoothness.
"""
import os, sys
import numpy as np
from PIL import Image

# key: (source, size, tint or None to keep the photo's colour, contrast or colour scale, roughness scale, roughness offset, metallic)
SURFACES = {
    'floor':     ('hangar_concrete_floor', 1024, 0x8b908f, 0.22, 0.35, 0.05, 0.0),   # grey epoxy
    'wall':      ('concrete_block_wall', 1024, 0xe3e3df, 0.10, 0.6, 0.35, 0.0),      # painted block
    'dado':      ('concrete_block_wall', 1024, 0x3a3e3d, 0.12, 0.5, 0.3, 0.0),
    'ceiling':   ('corrugated_iron_02', 1024, 0xb4b8b6, 0.10, 0.5, 0.25, 0.6),       # roof decking
    'slab':      ('plastered_wall', 512, 0xc6c9c4, 0.25, 0.6, 0.3, 0.0),
    'partition': ('plastered_wall', 512, 0xebeae5, 0.15, 0.6, 0.3, 0.0),
    'walnut':    ('smoked_walnut_veneer', 512, None, 0.42, 0.5, 0.2, 0.0),          # dark walnut
    'carpet':    ('dirty_carpet', 512, 0x4d4f50, 0.5, 1.0, 0.0, 0.0),
    'parquet':   ('wood_floor', 1024, None, 0.55, 0.5, 0.15, 0.0),
    'tread':     ('metal_plate', 512, 0x3b3f40, 0.35, 0.5, 0.2, 0.8),
    'door':      ('painted_metal_shutter', 512, 0x6f7876, 0.2, 0.6, 0.2, 0.4),
}


def make(src, out, key, spec):
    sid, size, tint, contrast, rs, ro, metal = spec
    d = os.path.join(src, sid)
    col = np.asarray(Image.open(os.path.join(d, 'Diffuse.jpg')).convert('RGB').resize((size, size), Image.LANCZOS)).astype(np.float64) / 255
    if tint is None:
        res = col * contrast  # darken, keeping the grain's own colour
    else:
        lum = col @ [0.2126, 0.7152, 0.0722]
        lum = lum / lum.mean()
        t = np.array([(tint >> 16) & 255, (tint >> 8) & 255, tint & 255]) / 255
        res = t[None, None, :] * (1 + contrast * (lum[..., None] - 1))
    Image.fromarray((res.clip(0, 1) * 255).round().astype(np.uint8)).save(os.path.join(out, key + '_c.jpg'), quality=88)
    Image.open(os.path.join(d, 'nor_gl.jpg')).convert('RGB').resize((size, size), Image.LANCZOS).save(os.path.join(out, key + '_n.jpg'), quality=90)
    arm = np.asarray(Image.open(os.path.join(d, 'arm.jpg')).convert('RGB').resize((size // 2, size // 2), Image.LANCZOS)).astype(np.float64) / 255
    rough = (arm[..., 1] * rs + ro).clip(0.02, 1)
    m = np.full_like(rough, metal)
    mask = np.stack([m, rough, m, 1 - rough], -1)
    Image.fromarray((mask * 255).round().astype(np.uint8), 'RGBA').save(os.path.join(out, key + '_s.png'), optimize=True)
    print(key, sid)


if __name__ == '__main__':
    os.makedirs(sys.argv[2], exist_ok=True)
    for k, v in SURFACES.items():
        make(sys.argv[1], sys.argv[2], k, v)
