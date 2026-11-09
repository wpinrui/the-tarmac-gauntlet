"""Renders the built model flat-shaded in each reference view's pixel frame and blends it over the
reference: python overlay.py obj refdir outdir. Writes ov_<view>.png (blend) and mo_<view>.png (model)."""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

import rows as R

obj, ref, out = map(Path, sys.argv[1:4])
kd, cur = {}, None
for line in obj.with_suffix(".mtl").read_text().splitlines():
    if line.startswith("newmtl"):
        cur = line.split()[1]
    elif line.startswith("Kd") and cur:
        kd[cur] = np.array([float(t) for t in line.split()[1:4]])
V, F, M, mat = [], [], [], None
for line in obj.read_text().splitlines():
    if line.startswith("v "):
        V.append([float(t) for t in line.split()[1:4]])
    elif line.startswith("usemtl"):
        mat = line.split()[1]
    elif line.startswith("f "):
        idx = [int(t.split("/")[0]) - 1 for t in line.split()[1:]]
        for i in range(1, len(idx) - 1):
            F.append((idx[0], idx[i], idx[i + 1]))
            M.append(mat)
V, F = np.array(V), np.array(F)
B = np.stack([V[:, 0], -V[:, 2], V[:, 1]], 1)  # build space
x, y, z = B.T
T = B[F]
N = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0])
N /= np.linalg.norm(N, axis=1, keepdims=True) + 1e-12
C = T.mean(1)
col = np.array([kd.get(m, np.ones(3)) for m in M])


def inv_x(x):
    hi = 960 + (x - R.WB / 2) / (R.S * R.K_OVER)
    lo = 210 - (-R.WB / 2 - x) / (R.S * R.K_OVER)
    return np.where(x > R.WB / 2, hi, np.where(x < -R.WB / 2, lo, 585 + x / R.S))


def inv_interp(z, ps, zs):
    return np.interp(z, zs[::-1], ps[::-1])


# (u, v, view direction toward the viewer, depth key: larger = nearer)
views = {
    "side": (inv_x(x), 380 - (z - R.TYRE_R) / R.S_Z, np.array([0, -1, 0])),
    "front": (325.5 + y * 525 / 1.72, inv_interp(z, [210, 300, 402, 470, 550], [1.11, .81, .60, .35, .05]), np.array([1, 0, 0])),
    "rear": (362 - y * 539 / 1.72, inv_interp(z, [120, 255, 268, 358, 395, 470, 600], [1.41, 1.05, 1.02, .75, .57, .42, 0]), np.array([-1, 0, 0])),
    "top": (312.5 + y * 381 / 1.72, 1056 - (R.X_FRONT - x) * 1016 / 4.03, np.array([0, 0, 1])),
}
light = np.array([0.3, -0.4, 0.85]); light /= np.linalg.norm(light)
for name, (u, v, d) in views.items():
    img = Image.open(ref / f"{name}.png").convert("RGB")
    m = Image.new("RGB", img.size, (230, 230, 230))
    dr = ImageDraw.Draw(m)
    P = np.stack([u, v], 1)
    face = N @ d > 0
    order = np.argsort(C @ d)
    shade = 0.45 + 0.55 * np.clip(np.abs(N @ light), 0, 1)
    for i in order:
        if not face[i]:
            continue
        c = tuple(int(255 * min(1, k)) for k in col[i] ** (1 / 2.2) * shade[i])
        dr.polygon([tuple(P[j]) for j in F[i]], fill=c)
    m.save(out / f"mo_{name}.png")
    Image.blend(img, m, 0.5).save(out / f"ov_{name}.png")
    print(name)
