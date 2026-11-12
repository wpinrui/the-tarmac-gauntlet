"""The road's textures as TrackTextures.cs makes them (MakeTarmac, TarmacColour, TarmacNormals, Macro), in numpy.

The stones are laid in the same order with the same sizes, heights and shades, from Python's random numbers rather than
System.Random's, so the grain matches the game's in every respect but the exact place of each stone.
"""
import math
import os
import random

import numpy as np

SIZE = 1024
MACRO = 256


def _hash(x, y, seed):
    h = (x.astype(np.int64) * 374761393 + y.astype(np.int64) * 668265263 + seed * 1442695041) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    h ^= h >> 16
    return (h & 0xFFFFFF) / float(0xFFFFFF) * 2 - 1


def fbm(size, cells, octaves, seed):
    y, x = np.mgrid[0:size, 0:size]
    total, weight, norm = np.zeros((size, size)), 0.5, 0.0
    for o in range(octaves):
        fx, fy = (x + 0.5) * cells / size, (y + 0.5) * cells / size
        x0, y0 = np.floor(fx).astype(np.int64), np.floor(fy).astype(np.int64)
        tx, ty = fx - x0, fy - y0
        tx, ty = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)
        s = seed * 31 + o
        a, b = _hash(x0 % cells, y0 % cells, s), _hash((x0 + 1) % cells, y0 % cells, s)
        c, d = _hash(x0 % cells, (y0 + 1) % cells, s), _hash((x0 + 1) % cells, (y0 + 1) % cells, s)
        total += weight * ((a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty)
        norm += weight
        weight *= 0.5
        cells *= 2
    return total / norm


def tarmac(seed=1):
    s = SIZE
    rng = random.Random(seed)
    height = 0.25 * fbm(s, 16, 4, seed)
    grain = np.array([rng.random() * 2 - 1 for _ in range(s * s)]).reshape(s, s)
    stone = np.zeros((s, s))
    h_flat, st_flat = height.ravel().tolist(), stone.ravel().tolist()
    for _ in range(s * s // 6):
        cx, cy = rng.random() * s, rng.random() * s
        pick = rng.random()
        r = 0.6 + pick * pick * 1.6
        top = 0.35 + rng.random() * 0.5
        light = 0.35 + rng.random() * 0.65
        reach = math.ceil(r)
        icx, icy = int(cx), int(cy)
        for dy in range(-reach, reach + 1):
            fy = icy + dy + 0.5 - cy
            row = ((icy + dy) % s) * s
            for dx in range(-reach, reach + 1):
                fx = icx + dx + 0.5 - cx
                q = (fx * fx + fy * fy) / (r * r)
                if q >= 1:
                    continue
                i = row + (icx + dx) % s
                h = h_flat[i] * 0.3 + top * math.sqrt(1 - q)
                if h <= h_flat[i]:
                    continue
                h_flat[i] = h
                st_flat[i] = light
    return np.array(h_flat).reshape(s, s), np.array(st_flat).reshape(s, s), grain


def colour(stone, grain, average=(0.17, 0.17, 0.18), speckle=0.5, binder=0.78):
    shade = (1 + grain * 0.04) * np.where(stone > 0, 1 + stone * speckle, binder)
    k = shade / shade.mean()
    rgb = np.clip(np.array(average)[None, None, :] * k[..., None], 0, 1)
    gloss = np.where(stone > 0, 0.5 + stone * 0.35, 0.35)
    return rgb, gloss


def normals(height, strength=1.2):
    dx = np.roll(height, -1, 1) - np.roll(height, 1, 1)
    dy = np.roll(height, -1, 0) - np.roll(height, 1, 0)
    n = np.stack([-dx * strength * 0.5, -dy * strength * 0.5, np.ones_like(dx)], -1)
    return n / np.linalg.norm(n, axis=-1, keepdims=True)


def macro(seed=8):
    s = MACRO
    broad = fbm(s, 4, 4, seed)
    relaid = fbm(s, 6, 2, seed + 11)
    patch = broad * 0.7 + np.where(relaid > 0.35, 0.12, np.where(relaid < -0.4, -0.1, 0))
    polish = fbm(s, 3, 3, seed + 51)
    return np.clip(0.5 + patch * 0.5, 0, 1), np.clip(0.5 + polish * 0.8, 0, 1)


def load(cache):
    """height, stone, grain, macro patch and polish, made once and kept in the cache folder."""
    path = os.path.join(cache, "tarmac.npz")
    if os.path.exists(path):
        z = np.load(path)
        return z["height"], z["stone"], z["grain"], z["patch"], z["polish"]
    height, stone, grain = tarmac(1)
    patch, polish = macro(8)
    os.makedirs(cache, exist_ok=True)
    np.savez_compressed(path, height=height, stone=stone, grain=grain, patch=patch, polish=polish)
    return height, stone, grain, patch, polish
