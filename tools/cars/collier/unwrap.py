"""Unwraps the reference renders onto the skin's (s, u) map, so parts can be traced where they
really sit on the body: python unwrap.py refdir out.png. Each skin point takes its colour from the
view it faces most. Grid lines every 0.1 m, labels every 0.5 m."""
import sys

import numpy as np
from PIL import Image, ImageDraw

import rows as R
import skin

ref, out = sys.argv[1], sys.argv[2]
PPM = 400
imgs = {k: np.array(Image.open(f"{ref}/{k}.png").convert("RGB")) for k in ("side", "front", "rear", "top")}
umax = skin.UG[-1]
s0, s1 = float(np.nanmin(skin.SG)), float(np.nanmax(skin.SG))
W, H = int((s1 - s0) * PPM), int(umax * PPM)
img = np.full((H, W, 3), 255, np.uint8)
TOPR = [40, 232, 675, 878, 1056]
TOPX = [R.X(60), R.X(255), R.X(750), R.X(900), R.X(1172)]


def inv_x(x):
    if x > R.WB / 2:
        return 960 + (x - R.WB / 2) / (R.S * R.K_OVER)
    if x < -R.WB / 2:
        return 210 - (-R.WB / 2 - x) / (R.S * R.K_OVER)
    return 585 + x / R.S


def px(view, p):
    x, y, z = p
    if view == "side":
        return inv_x(x), 380 - (z - R.TYRE_R) / R.S_Z
    if view == "front":
        return 325.5 + y * 525 / 1.72, np.interp(z, [.05, .35, .60, .81, 1.11], [550, 470, 402, 300, 210])
    if view == "rear":
        return 362 - y * 539 / 1.72, np.interp(z, [0, .42, .57, .75, 1.02, 1.05, 1.41], [600, 470, 395, 358, 268, 255, 120])
    return 312.5 + y * 381 / 1.72, np.interp(x, TOPX, TOPR)


DIRS = {"side": (0, 1, 0), "front": (1, 0, 0), "rear": (-1, 0, 0), "top": (0, 0, 1)}
ss = s0 + (np.arange(W) + 0.5) / PPM
us = umax - (np.arange(H) + 0.5) / PPM
G = skin.grid(us, skin.XG)
P = np.full((H, W, 3), np.nan)
for r, u in enumerate(us):
    k = min(int(u / 0.01), len(skin.UG) - 1)
    row = skin.SG[k]
    ci = np.interp(ss, row, np.arange(len(row)), left=np.nan, right=np.nan)
    ok = ~np.isnan(ci)
    c0 = np.clip(ci[ok].astype(int), 0, len(row) - 2)
    a = (ci[ok] - c0)[:, None]
    P[r, ok] = G[r, c0] * (1 - a) + G[r, c0 + 1] * a
dx = np.gradient(P, axis=1)
du = -np.gradient(P, axis=0)
N = np.cross(du, dx)
N /= np.linalg.norm(N, axis=2, keepdims=True) + 1e-12
for r in range(H):
    for c in range(W):
        p = P[r, c]
        if np.isnan(p[0]) or np.isnan(N[r, c, 0]):
            continue
        v = max(DIRS, key=lambda k: np.dot(N[r, c], DIRS[k]))
        a, b = px(v, p)
        im = imgs[v]
        if 0 <= int(b) < im.shape[0] and 0 <= int(a) < im.shape[1]:
            img[r, c] = im[int(b), int(a)]
out_im = Image.fromarray(img)
d = ImageDraw.Draw(out_im)
for k in range(int(s0 * 10), int(s1 * 10) + 1):
    cx = (k / 10 - s0) * PPM
    d.line([(cx, 0), (cx, H)], fill=(255, 0, 160) if k % 5 == 0 else (0, 170, 255), width=1)
    if k % 5 == 0:
        d.text((cx + 2, 2), f"s{k / 10:g}", fill=(255, 0, 160))
for k in range(0, int(umax * 10) + 1):
    cy = (umax - k / 10) * PPM
    d.line([(0, cy), (W, cy)], fill=(255, 0, 160) if k % 5 == 0 else (0, 170, 255), width=1)
    if k % 5 == 0:
        d.text((2, cy + 2), f"u{k / 10:g}", fill=(255, 0, 160))
out_im.save(out)
print(W, H, umax)
