"""Makes the trackside furniture's textures for the raceday scene.

Usage: python3 -I make_art.py <repo root>

Writes into unity/Assets/Raceday/Resources/Furniture:
  boards.png               the distance boards' faces, 300, 200 and 100 side by side, each 720 by 384
  marshal_overalls.jpg     Team HQ's crew_overalls clothes texture with the blue overalls repainted for marshals
The boards are black numbers on white with a clear margin round them. The overalls are orange.
The numbers are set in D-DIN Bold (D-DIN-Bold.otf beside this script, SIL Open Font License), a free copy of the
DIN 1451 road sign lettering that circuit boards use.
"""
import os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

FACE, INK = (236, 236, 232), (16, 16, 18)
# Each face in pixels. The numbers fill about 440 by 206 of it, leaving a clear margin round them.
FACE_W, FACE_H = 720, 384
OVERALLS = (22, 1.0, 1.7)   # hue, saturation gain, value gain


def boards(root, out):
    font = ImageFont.truetype(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'D-DIN-Bold.otf'), 288)
    im = Image.new('RGB', (FACE_W * 3, FACE_H), FACE)
    d = ImageDraw.Draw(im)
    for i, text in enumerate(['300', '200', '100']):
        x0 = i * FACE_W
        # A thin dark edge round each face, as on a painted aluminium board.
        d.rectangle([x0, 0, x0 + FACE_W - 1, FACE_H - 1], outline=tuple(int(c * 0.55) for c in FACE), width=10)
        l, t, r, b = d.textbbox((0, 0), text, font=font)
        d.text((x0 + FACE_W / 2 - (l + r) / 2, FACE_H / 2 - (t + b) / 2), text, font=font, fill=INK)
    im.save(out)


def overalls(root, out):
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
    hue, sat, val = OVERALLS
    v = np.clip(mx * val, 0, 1)
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
    out = os.path.join(root, 'unity/Assets/Raceday/Resources/Furniture')
    os.makedirs(out, exist_ok=True)
    boards(root, os.path.join(out, 'boards.png'))
    overalls(root, os.path.join(out, 'marshal_overalls.jpg'))
