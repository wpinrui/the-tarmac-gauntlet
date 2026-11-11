"""Makes the trackside furniture's textures for the raceday scene.

Usage: python3 -I make_art.py <repo root> [board style] [overalls colour]

Writes into unity/Assets/Raceday/Resources/Furniture:
  boards.png               the distance boards' faces, 300, 200 and 100 side by side, each 512 by 384
  marshal_overalls.jpg     Team HQ's crew_overalls clothes texture with the blue overalls repainted for marshals
Board styles: white (black numbers on white), black (white on black), yellow (black on yellow).
Overalls colours: orange, white.
The numbers are set in Titillium Web Bold, the font the race graphics use.
"""
import os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

BOARDS = {'white': ((236, 236, 232), (16, 16, 18)), 'black': ((22, 22, 24), (238, 238, 234)), 'yellow': ((240, 196, 24), (16, 16, 18))}
OVERALLS = {'orange': (22, 1.0, 1.7), 'white': (None, 0.0, 1.9)}


def boards(root, style, out):
    face, ink = BOARDS[style]
    font = ImageFont.truetype(os.path.join(root, 'unity/Assets/Raceday/Resources/Fonts/TitilliumWeb-Bold.ttf'), 300)
    im = Image.new('RGB', (1536, 384), face)
    d = ImageDraw.Draw(im)
    for i, text in enumerate(['300', '200', '100']):
        x0 = i * 512
        # A thin dark edge round each face, as on a painted aluminium board.
        d.rectangle([x0, 0, x0 + 511, 383], outline=tuple(int(c * 0.55) for c in face), width=10)
        l, t, r, b = d.textbbox((0, 0), text, font=font)
        d.text((x0 + 256 - (l + r) / 2, 192 - (t + b) / 2), text, font=font, fill=ink)
    im.save(out)


def overalls(root, colour, out):
    src = os.path.join(root, 'unity/Assets/TeamHq/Resources/Crew/crew_overalls_t0_c.jpg')
    p = np.asarray(Image.open(src).convert('RGB'), dtype=np.float64) / 255
    mx, mn = p.max(-1), p.min(-1)
    d = mx - mn
    dd = np.where(d > 0, d, 1)
    r, g, b = p[..., 0], p[..., 1], p[..., 2]
    h = np.where(mx == r, ((g - b) / dd) % 6, np.where(mx == g, (b - r) / dd + 2, (r - g) / dd + 4)) * 60
    s = np.where(mx > 0, d / np.where(mx > 0, mx, 1), 0)
    # The overalls are the blue-grey cloth, and the brown waistcoat over them (in the texture's lower left), so the
    # marshal is one colour; skin, helmet, boots and badges keep their own.
    hgt, wid = mx.shape
    yy, xx = np.mgrid[0:hgt, 0:wid]
    vest = (xx < wid * 0.69) & (yy > hgt * 0.58) & (h < 50) & (s > 0.15) & (mx > 0.12)
    cloth = ((h > 110) & (h < 270) & (s > 0.03)) | vest
    hue, sat, val = OVERALLS[colour]
    v = np.clip(mx * val, 0, 1)
    if hue is None:
        out_rgb = np.repeat(np.clip(0.18 + v * 0.8, 0, 1)[..., None], 3, -1)
    else:
        sv = np.clip(0.75 + s * sat, 0, 0.95)
        c = v * sv
        hh = hue / 60
        x = c * (1 - abs(hh % 2 - 1))
        m = v - c
        out_rgb = np.stack([c + m, x + m, m], -1)   # hue between 0 and 60: red, then green rising
    res = np.where(cloth[..., None], out_rgb, p)
    Image.fromarray((res * 255).clip(0, 255).astype(np.uint8)).save(out, quality=88)


if __name__ == '__main__':
    root = sys.argv[1]
    style = sys.argv[2] if len(sys.argv) > 2 else 'white'
    colour = sys.argv[3] if len(sys.argv) > 3 else 'orange'
    out = os.path.join(root, 'unity/Assets/Raceday/Resources/Furniture')
    os.makedirs(out, exist_ok=True)
    boards(root, style, os.path.join(out, 'boards.png'))
    overalls(root, colour, os.path.join(out, 'marshal_overalls.jpg'))
