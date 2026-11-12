"""Draws the Team HQ's wear: oil stains, tyre marks, grime at the foot of the walls, dirty patches and scuffed paint.

Usage: python3 -I make_wear.py [out folder, default unity/Assets/TeamHq/Resources/Wear]

Each is an RGBA PNG for the Grime shader, which multiplies what is under it by the RGB, as much as the alpha says.
Every texture is drawn from fixed seeds, so running it again gives the same files.
"""
import os
import sys

import numpy as np
from PIL import Image

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), '..', '..', '..', 'unity', 'Assets', 'TeamHq', 'Resources', 'Wear')


def noise(h, w, cell, rng, wrap_x=False):
    """Smooth value noise with cells of the given size in texels; it repeats across x when wrap_x."""
    gh, gw = h // cell + 2, w // cell + (0 if wrap_x else 2)
    g = rng.random((gh, gw))
    y = np.arange(h) / cell
    x = np.arange(w) / cell
    y0, x0 = y.astype(int), x.astype(int)
    fy, fx = y - y0, x - x0
    fy, fx = fy * fy * (3 - 2 * fy), fx * fx * (3 - 2 * fx)
    x1 = (x0 + 1) % gw if wrap_x else x0 + 1
    x0 = x0 % gw if wrap_x else x0
    a = g[y0][:, x0] * (1 - fx) + g[y0][:, x1] * fx
    b = g[y0 + 1][:, x0] * (1 - fx) + g[y0 + 1][:, x1] * fx
    return a * (1 - fy[:, None]) + b * fy[:, None]


def fbm(h, w, cell, seed, octaves=5, wrap_x=False):
    rng = np.random.default_rng(seed)
    total, amp, norm = np.zeros((h, w)), 1.0, 0.0
    for _ in range(octaves):
        if cell < 1: break
        if wrap_x and w % cell: cell = max(1, w // max(1, round(w / cell)))
        total += noise(h, w, cell, rng, wrap_x) * amp
        norm += amp
        amp *= 0.5
        cell //= 2
    return total / norm


def save(name, tint, alpha):
    h, w = alpha.shape
    rgb = np.broadcast_to(np.asarray(tint, float), (h, w, 3)) if np.ndim(tint) == 1 else tint
    img = np.dstack([rgb, alpha]).clip(0, 1)
    Image.fromarray((img * 255).round().astype(np.uint8), 'RGBA').save(os.path.join(OUT, name + '.png'))


def radial(n, squash=1.0):
    y, x = np.mgrid[0:n, 0:n] / (n - 1) * 2 - 1
    return np.sqrt(x * x + (y / squash) ** 2)


def oil(seed):
    """A pool of old engine oil: dark in the middle, a ragged edge, drips round it."""
    n = 512
    r = radial(n, 0.7)
    edge = fbm(n, n, 128, seed) * 1.1
    body = np.clip((1.0 - r - edge + 0.2) / 0.25, 0, 1)
    rng = np.random.default_rng(seed + 1)
    yy, xx = np.mgrid[0:n, 0:n]
    for _ in range(9):
        cy, cx = rng.normal(n / 2, n / 5, 2)
        rad = rng.uniform(4, 16)
        body = np.maximum(body, np.clip(1 - np.hypot(yy - cy, xx - cx) / rad, 0, 1) ** 0.6)
    mottle = fbm(n, n, 32, seed + 2)
    alpha = body * (0.3 + 0.35 * mottle) * np.clip(1.15 - r, 0, 1)
    return np.array([0.16, 0.13, 0.10]), alpha


def tyre():
    """One wheel's track from the doors into the bay: broken rubber streaks, fading at both ends."""
    h, w = 1024, 128
    along = fbm(h, w, 256, 21) * 0.7 + 0.3
    y = (np.arange(h) + 0.5) / h
    ends = np.clip(y / 0.25, 0, 1) * np.clip((1 - y) / 0.12, 0, 1)
    x = (np.arange(w) + 0.5) / w
    across = np.clip(1 - np.abs(x * 2 - 1) ** 2.5, 0, 1)
    rng = np.random.default_rng(22)
    streaks = np.zeros(w)
    for _ in range(14):
        c, wd = rng.uniform(0.15, 0.85), rng.uniform(0.01, 0.05)
        streaks += rng.uniform(0.3, 1) * np.exp(-((x - c) / wd) ** 2)
    streaks = np.clip(0.35 + streaks * 0.5, 0, 1)
    grain = fbm(h, w, 16, 23)
    alpha = across[None, :] * streaks[None, :] * along * ends[:, None] * (0.6 + 0.4 * grain) * 0.55
    return np.array([0.12, 0.12, 0.12]), alpha


def edge():
    """Dust and grime swept against the foot of a wall: strongest at the wall (top), repeats across."""
    h, w = 128, 1024
    v = 1 - (np.arange(h) + 0.5) / h
    clouds = fbm(h, w, 128, 31, wrap_x=True)
    reach = 0.35 + 0.65 * fbm(1, w, 128, 32, wrap_x=True)[0]
    falloff = np.clip((v[:, None] - (1 - reach[None, :])) / reach[None, :], 0, 1) ** 1.6
    alpha = falloff * (0.45 + 0.55 * clouds) * 0.6
    return np.array([0.36, 0.33, 0.28]), alpha


def patch(seed, strength):
    """A worn, dirty patch where feet and trolleys go: soft and blotchy, fading out to its edge."""
    n = 512
    r = radial(n)
    blot = fbm(n, n, 128, seed)
    fine = fbm(n, n, 16, seed + 1)
    alpha = np.clip((1 - r) * 1.4, 0, 1) ** 1.5 * np.clip(blot * 1.6 - 0.35, 0, 1) * (0.7 + 0.3 * fine) * strength
    return np.array([0.42, 0.39, 0.34]), alpha


def wall():
    """Scuffs and smudges on painted block above the dado: hands, trolleys and drips, densest low down."""
    h, w = 256, 1024
    v = 1 - (np.arange(h) + 0.5) / h  # height up the strip: image rows run down from its top
    low = np.clip(1 - v / 0.85, 0, 1) ** 1.4
    clouds = fbm(h, w, 128, 41, wrap_x=True)
    alpha = low[:, None] * np.clip(clouds * 1.5 - 0.35, 0, 1) * 0.35
    rng = np.random.default_rng(42)
    yy, xx = np.mgrid[0:h, 0:w]
    for _ in range(24):
        cx, cy = rng.uniform(0, w), rng.uniform(h * 0.5, h)
        ln, th = rng.uniform(20, 90), rng.uniform(1.5, 4)
        ang = rng.normal(0, 0.08)
        dx = (xx - cx + w / 2) % w - w / 2
        dy = yy - cy
        along = dx * np.cos(ang) + dy * np.sin(ang)
        off = -dx * np.sin(ang) + dy * np.cos(ang)
        mark = np.exp(-(off / th) ** 2) * np.clip(1 - np.abs(along) / ln, 0, 1)
        alpha = np.maximum(alpha, mark * rng.uniform(0.25, 0.55))
    for _ in range(12):
        cx, top = rng.uniform(0, w), rng.uniform(0.05, 0.6) * h
        ln, th = rng.uniform(0.15, 0.4) * h, rng.uniform(1, 2.5)
        dx = (xx - cx + w / 2) % w - w / 2
        drip = np.exp(-(dx / th) ** 2) * np.clip((top + ln - yy) / ln, 0, 1) * (yy > top)
        alpha = np.maximum(alpha, drip * 0.3)
    return np.array([0.40, 0.38, 0.34]), alpha


def main():
    os.makedirs(OUT, exist_ok=True)
    save('oil1', *oil(1))
    save('oil2', *oil(7))
    save('tyre', *tyre())
    save('edge', *edge())
    save('patch', *patch(51, 0.7))
    save('wall', *wall())


main()
