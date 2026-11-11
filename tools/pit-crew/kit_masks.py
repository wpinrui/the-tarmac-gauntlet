"""Writes the pit crews' kit masks from the crew_suit body's own clothing textures.

Usage: python3 -I tools/pit-crew/kit_masks.py <repo root>

The suit is navy cloth with red panels. Each mask marks them, so the race's crew kit shader can paint them
in a team's colours: red is the navy cloth, green the red panels. The masks are soft at the edges, as the
JPEG colours blend there, and half the textures' size: the crew are never close enough to need more.
"""
import os, sys
import numpy as np
from PIL import Image, ImageFilter

SRC = 'unity/Assets/TeamHq/Resources/Crew'
OUT = 'unity/Assets/Raceday/Resources/PitCrew'
PARTS = {'crew_suit_t12_c.jpg': 'kit_top.png', 'crew_suit_t8_c.jpg': 'kit_bottom.png'}


def ramp(x, lo, hi):
    return np.clip((x - lo) / (hi - lo), 0, 1)


def mask(path):
    im = Image.open(path).convert('RGB')
    hsv = np.asarray(im.convert('HSV')).astype(np.float64)
    h, s, v = hsv[..., 0] * 360 / 255, hsv[..., 1] / 255, hsv[..., 2] / 255
    red_hue = np.maximum(ramp(-np.minimum(h, 360 - h), -30, -18), 0)          # 1 within 18 degrees of red, 0 past 30
    panel = red_hue * ramp(s, 0.3, 0.45)
    navy_hue = ramp(h, 180, 195) * (1 - ramp(h, 260, 275))
    cloth = navy_hue * ramp(s, 0.08, 0.14)
    out = np.zeros(h.shape + (3,), np.uint8)
    out[..., 0] = (cloth * 255).round()
    out[..., 1] = (panel * 255).round()
    img = Image.fromarray(out).filter(ImageFilter.GaussianBlur(1.0))
    return img.resize((im.width // 2, im.height // 2), Image.LANCZOS)


def main(root):
    for src, dst in PARTS.items():
        mask(os.path.join(root, SRC, src)).save(os.path.join(root, OUT, dst))
        print('wrote', dst)


if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else '.')
