"""
Offline preview of the race's rain and wheel spray, before and after the rain rework, from the TV, onboard and long-lens
cameras. A stand-in pit straight (track, pit wall, garages, trees, cars) is ray cast for colour and depth, then the
weather is drawn over it as WeatherView, RainSheets, Drops.shader and RainSheet.shader draw it: the particle systems are
simulated frame by frame with the same numbers (emission, lifetimes, velocities, drag, size and colour over life), and the
shaders' functions are ported per pixel, with screen derivatives taken from neighbouring pixels.

    python3 tools/rain-preview/preview.py out_dir [shot ...]

Shots: cut, onboard, longlens, spray-tv, spray-onboard. Each writes before and after images at Medium and Ultra, and a
side-by-side sheet. Keep the constants below in step with the C# and the shaders.
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

W, H = 1920, 1080
FPS = 60.0
DT = 1.0 / FPS

# ---- WeatherView / RainSheets constants ----------------------------------------------------------------------------
DROP_LIFE, DROP_SPEED = 1.2, 8.5
MOST_DROPS = 8000
RAIN_BOX = 36.0
FALL_HEIGHT = DROP_LIFE * DROP_SPEED
RAIN_PIXELS, RAIN_PIXELS_AT = 3.0, 12.0
STREAK_W, STREAK_L = 0.0035, 0.45
CELL_H = 3.2 * STREAK_L
SHEET_INNER = 14.0
RADII = {"Low": [24, 80], "Medium": [20, 48, 115], "High": [18, 34, 64, 120], "Ultra": [17, 26, 40, 62, 96, 150]}

# ---- The look of the day: a heavy shower at noon ---------------------------------------------------------------
FOG = np.array([0.58, 0.60, 0.63])
AMBIENT = np.array([0.48, 0.50, 0.54])
FOG_DENSITY = 0.0065
LIGHT = np.clip(FOG * 1.35 + AMBIENT * 0.3, 0, 1)
RAIN_MM = 12.0
WIND = 1.2
WATER = 1.2


def smooth(a, b, x):
    t = min(max((x - a) / (b - a), 0.0), 1.0)
    return t * t * (3 - 2 * t)


# ---- Camera -------------------------------------------------------------------------------------------------------
class Cam:
    def __init__(self, pos, look=None, yaw=0.0, pitch=0.0, fov=60.0):
        self.pos = np.array(pos, float)
        if look is not None:
            d = np.array(look, float) - self.pos
            yaw = math.degrees(math.atan2(d[0], d[2]))
            pitch = math.degrees(math.atan2(d[1], math.hypot(d[0], d[2])))
        self.yaw, self.pitch, self.fov = yaw, pitch, fov
        y, p = math.radians(yaw), math.radians(pitch)
        self.f = np.array([math.sin(y) * math.cos(p), math.sin(p), math.cos(y) * math.cos(p)])
        r = np.cross([0, 1, 0], self.f)
        self.r = r / np.linalg.norm(r)
        self.u = np.cross(self.f, self.r)
        self.focal = 0.5 * H / math.tan(math.radians(fov) * 0.5)

    def rays(self):
        j = (np.arange(W) + 0.5 - W / 2) / self.focal
        i = -(np.arange(H) + 0.5 - H / 2) / self.focal
        x, y = np.meshgrid(j, i)
        d = self.f[None, None, :] + x[..., None] * self.r + y[..., None] * self.u
        return d / np.linalg.norm(d, axis=-1, keepdims=True)

    def view(self, p):
        q = np.asarray(p) - self.pos
        return q @ self.r, q @ self.u, q @ self.f

    def screen(self, p):
        x, y, z = self.view(p)
        return W / 2 + x * self.focal / z, H / 2 - y * self.focal / z, z


def angle(a, b):
    return math.degrees(math.acos(min(1.0, max(-1.0, float(a.f @ b.f)))))


# ---- The stand-in pit straight ---------------------------------------------------------------------------------
def boxes_for(cars):
    b = []
    # Pit wall, with a fence of posts.
    b.append(((-400, 0, 13.0), (400, 1.1, 13.6), (0.55, 0.55, 0.56)))
    # Garages: a long block with coloured fronts and dark open doors.
    fronts = [(0.20, 0.27, 0.55), (0.62, 0.30, 0.42), (0.34, 0.36, 0.38), (0.20, 0.42, 0.40), (0.55, 0.45, 0.20)]
    for k in range(-12, 12):
        x0 = k * 16.0
        c = fronts[k % len(fronts)]
        b.append(((x0, 3.6, 28.0), (x0 + 16, 6.5, 29.0), c))
        b.append(((x0 + 1, 0, 30.0), (x0 + 15, 3.6, 31.0), (0.10, 0.10, 0.11)))
        b.append(((x0 + 15, 0, 28.0), (x0 + 16, 3.6, 29.0), (0.40, 0.40, 0.42)))
    b.append(((-200, 0, 31.0), (200, 9.0, 40.0), (0.40, 0.41, 0.43)))
    b.append(((-200, 6.5, 27.0), (200, 7.0, 31.0), (0.30, 0.30, 0.32)))
    # A gantry over the pit lane.
    b.append(((-60, 6.0, 14.0), (60, 6.4, 15.0), (0.30, 0.30, 0.32)))
    # Trees beyond the grass on the camera's side, and far behind the garages.
    rng = np.random.default_rng(3)
    for k in range(70):
        x = -600 + k * 18 + rng.uniform(-5, 5)
        z = -70 - rng.uniform(0, 30)
        h = rng.uniform(9, 16)
        w = rng.uniform(5, 9)
        b.append(((x - w / 2, 0, z - w / 2), (x + w / 2, h, z + w / 2), (0.16, 0.22, 0.13)))
        x = -600 + k * 18 + rng.uniform(-5, 5)
        z = 70 + rng.uniform(0, 40)
        b.append(((x - w / 2, 0, z - w / 2), (x + w / 2, h + 6, z + w / 2), (0.15, 0.21, 0.12)))
    # Marshal posts along the far edge of the run-off.
    for k in range(-20, 30):
        x = k * 60.0
        b.append(((x, 0, -14), (x + 2, 2.5, -12), (0.80, 0.80, 0.78)))
    for (x, z, col) in cars:
        b += car_boxes(x, z, col)
    return b


def car_boxes(x, z, col):
    """A low sports car pointing along +x: body, cabin, wheels."""
    dark = (0.05, 0.05, 0.05)
    glass = (0.08, 0.09, 0.11)
    return [
        ((x - 2.3, 0.25, z - 0.95), (x + 2.3, 0.95, z + 0.95), col),
        ((x - 1.0, 0.95, z - 0.75), (x + 0.6, 1.25, z + 0.75), glass),
        ((x - 1.8, 0.0, z - 1.0), (x - 1.0, 0.68, z - 0.7), dark),
        ((x - 1.8, 0.0, z + 0.7), (x - 1.0, 0.68, z + 1.0), dark),
        ((x + 1.0, 0.0, z - 1.0), (x + 1.8, 0.68, z - 0.7), dark),
        ((x + 1.0, 0.0, z + 0.7), (x + 1.8, 0.68, z + 1.0), dark),
    ]


def ground_colour(x, z):
    c = np.empty(x.shape + (3,))
    c[:] = (0.17, 0.24, 0.13)
    track = (z > 0) & (z < 12)
    c[track] = (0.20, 0.20, 0.21)
    pit = (z >= 14) & (z < 28)
    c[pit] = (0.26, 0.26, 0.27)
    edge = ((np.abs(z - 0.3) < 0.15) | (np.abs(z - 11.7) < 0.15))
    c[edge] = (0.70, 0.70, 0.70)
    runoff = (z > -12) & (z <= 0)
    c[runoff & ~edge] = (0.19, 0.25, 0.14)
    return c


def render_scene(cam, cars):
    d = cam.rays()
    o = cam.pos
    best = np.full((H, W), np.inf)
    col = np.zeros((H, W, 3))
    nrm = np.zeros((H, W, 3))
    # Ground.
    with np.errstate(divide="ignore", invalid="ignore"):
        tg = -o[1] / d[..., 1]
    hit = (tg > 0) & np.isfinite(tg)
    best[hit] = tg[hit]
    p = o + d * np.where(hit, tg, 0)[..., None]
    col[hit] = ground_colour(p[..., 0], p[..., 2])[hit]
    nrm[hit] = (0, 1, 0)
    inv = 1.0 / np.where(np.abs(d) < 1e-9, 1e-9, d)
    for lo, hi, c in boxes_for(cars):
        lo, hi = np.array(lo, float), np.array(hi, float)
        corners = np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])])
        cx, cy, cz = cam.screen(corners)
        if (cz <= 0.1).all():
            continue
        if (cz > 0.1).all():
            x0, x1 = max(int(cx.min()) - 1, 0), min(int(cx.max()) + 2, W)
            y0, y1 = max(int(cy.min()) - 1, 0), min(int(cy.max()) + 2, H)
            if x0 >= x1 or y0 >= y1:
                continue
        else:
            x0, x1, y0, y1 = 0, W, 0, H
        sl = (slice(y0, y1), slice(x0, x1))
        dd, iv, bb = d[sl], inv[sl], best[sl]
        t1 = (lo - o) * iv
        t2 = (hi - o) * iv
        tmin = np.minimum(t1, t2)
        tmax = np.maximum(t1, t2)
        tn = tmin.max(axis=-1)
        tf = tmax.min(axis=-1)
        h = (tn <= tf) & (tf > 0) & (tn > 0) & (tn < bb)
        if not h.any():
            continue
        bb[h] = tn[h]
        col[sl][h] = c
        axis = tmin.argmax(axis=-1)
        n = np.zeros(h.shape + (3,))
        for a in range(3):
            m = h & (axis == a)
            n[m, a] = -np.sign(dd[m, a])
        nrm[sl][h] = n[h]
    sky = best == np.inf
    shade = 0.62 + 0.38 * np.clip(nrm[..., 1], 0, 1) + 0.12 * np.clip(-nrm[..., 0] * 0.5 - nrm[..., 2] * 0.8, -1, 1)
    img = col * shade[..., None] * (AMBIENT / AMBIENT.mean() * 1.25)
    # Wet track: the sky's sheen on the flat paved ground.
    paved = (nrm[..., 1] > 0.5) & (col[..., 0] < 0.3) & (col[..., 1] < 0.3)
    img[paved] = img[paved] * 0.85 + FOG * 0.12
    zview = best * (d @ cam.f)
    fogi = np.exp(-FOG_DENSITY * np.where(sky, 1e4, zview))
    img = img * fogi[..., None] + FOG * (1 - fogi[..., None])
    up = np.clip(d[..., 1], 0, 1)
    img[sky] = (FOG * (1.02 - 0.1 * up[..., None]))[sky]
    zview[sky] = 1e9
    return np.clip(img, 0, 1), zview, d


# ---- Particles ----------------------------------------------------------------------------------------------------
class Particles:
    def __init__(self, n=0):
        self.pos = np.zeros((0, 3))
        self.vel = np.zeros((0, 3))
        self.vol = np.zeros((0, 3))     # velocity over lifetime, constant per particle
        self.age = np.zeros(0)
        self.life = np.zeros(0)
        self.size = np.zeros(0)
        self.acc = 0.0

    def add(self, pos, vel, vol, life, size, age=None):
        n = len(pos)
        self.pos = np.vstack([self.pos, pos])
        self.vel = np.vstack([self.vel, vel])
        self.vol = np.vstack([self.vol, vol])
        self.life = np.concatenate([self.life, life])
        self.age = np.concatenate([self.age, np.zeros(n) if age is None else age])
        self.size = np.concatenate([self.size, size])

    def clear(self):
        self.__init__()

    def step(self, dt, gravity=0.0, drag=0.0):
        self.age += dt
        keep = self.age < self.life
        for k in ("pos", "vel", "vol", "age", "life", "size"):
            setattr(self, k, getattr(self, k)[keep])
        if drag:
            self.vel *= max(0.0, 1 - drag * dt)
        self.vel[:, 1] -= 9.81 * gravity * dt
        self.pos += (self.vel + self.vol) * dt


RNG = np.random.default_rng(11)


def box_points(n, centre, size):
    return np.asarray(centre) + (RNG.random((n, 3)) - 0.5) * np.asarray(size)


class OldRain:
    """WeatherView before: the rain refills only on a jump over 30 m, from the box's top slab, the frame after the cut."""

    def __init__(self):
        self.p = Particles()
        self.last = None

    def update(self, cam, dt):
        at = cam.pos
        cut = self.last is None or np.sum((at - self.last) ** 2) > 30 * 30
        vel = np.zeros(3) if cut else np.clip((at - self.last) / dt, -90, 90)
        if np.linalg.norm(vel) > 90:
            vel = vel / np.linalg.norm(vel) * 90
        self.last = at.copy()
        self.centre = at + vel * 0.6 * DROP_LIFE + np.array([0, 0.6 * DROP_LIFE * DROP_SPEED, 0])
        alive = min(600 + 450 * RAIN_MM, MOST_DROPS)
        gust = max(WIND - 0.5, 0) * 2.2
        self.drift = np.array([gust, -DROP_SPEED, gust * 0.35])
        if cut:
            self.p.clear()
            self.emit(int(alive))
        self.p.acc += alive / DROP_LIFE * dt
        n = int(self.p.acc)
        self.p.acc -= n
        self.emit(n, spread=dt)

    def emit(self, n, spread=0.0):
        pos = box_points(n, self.centre, (RAIN_BOX, 0.5, RAIN_BOX))
        age = RNG.random(n) * spread
        pos = pos + self.drift * age[:, None]
        self.p.add(pos, np.zeros((n, 3)), np.tile(self.drift, (n, 1)), np.full(n, DROP_LIFE), np.full(n, 0.022), age)

    def step(self, dt):
        self.p.step(dt)


class NewRain(OldRain):
    """WeatherView after: every camera switch is a cut, and a cut fills the whole fall at once, in the same frame."""

    def update(self, cam, dt):
        at = cam.pos
        if self.last is None:
            cut = True
        else:
            jump = 6 + 100 * dt
            cut = (np.sum((at - self.last[0]) ** 2) > jump * jump or angle(cam, self.last[1]) > 25
                   or abs(cam.fov - self.last[1].fov) > 0.25 * self.last[1].fov)
        vel = np.zeros(3) if cut else (at - self.last[0]) / dt
        if np.linalg.norm(vel) > 90:
            vel = vel / np.linalg.norm(vel) * 90
        self.last = (at.copy(), cam)
        self.centre = at + vel * 0.6 * DROP_LIFE + np.array([0, 0.6 * FALL_HEIGHT, 0])
        alive = min(600 + 450 * RAIN_MM, MOST_DROPS)
        gust = max(WIND - 0.5, 0) * 2.2
        self.drift = np.array([gust, -DROP_SPEED, gust * 0.35])
        self.quad = min(max(RAIN_PIXELS * RAIN_PIXELS_AT / cam.focal, STREAK_W), 0.06)
        self.thread = min(2 * STREAK_W / self.quad, 1.0)
        if cut:
            self.p.clear()
            self.emit(int(alive))
            n = len(self.p.age)
            down = RNG.random(n)
            self.p.pos += self.drift * (down * DROP_LIFE)[:, None] * np.array([1, 0, 1]) + np.array([0, -1, 0]) * (down * FALL_HEIGHT)[:, None]
            self.p.life = DROP_LIFE * (1 - down)
        self.p.acc += alive / DROP_LIFE * dt
        n = int(self.p.acc)
        self.p.acc -= n
        self.emit(n, spread=dt)
        self.p.size[:] = self.quad


# ---- Drawing particles as Drops.shader does --------------------------------------------------------------------
def over(img, ys, xs, rgb, a, glow=0.0):
    a = np.clip(a, 0, 1)[..., None]
    img[ys, xs] = img[ys, xs] * (1 - a * (1 - glow)) + rgb * a


def fog_mix(rgb, z):
    f = math.exp(-FOG_DENSITY * z)
    return rgb * f + FOG * (1 - f)


def draw_streaks(img, depth, cam, pos, vel, size, alpha, tint, length_scale, vel_scale, thread=None, glow=0.0,
                 max_size=None):
    """Stretched billboards: a quad `size` wide, size * length_scale + speed * vel_scale long along the velocity."""
    x, y, z = cam.view(pos)
    order = np.argsort(-z)
    for k in order:
        if z[k] < 0.3 or alpha[k] <= 0:
            continue
        v = vel[k]
        sp = np.linalg.norm(v)
        dirv = v / sp if sp > 1e-6 else np.array([0, -1.0, 0])
        L = size[k] * length_scale + sp * vel_scale
        a = pos[k] + dirv * L * 0.5
        b = pos[k] - dirv * L * 0.5
        ax, ay, az = cam.screen(a)
        bx, by, bz = cam.screen(b)
        if az < 0.3 or bz < 0.3:
            continue
        wpx = size[k] * cam.focal / z[k]
        if max_size is not None:
            wpx = min(wpx, max_size * H)
        x0 = int(math.floor(min(ax, bx) - wpx)); x1 = int(math.ceil(max(ax, bx) + wpx)) + 1
        y0 = int(math.floor(min(ay, by) - wpx)); y1 = int(math.ceil(max(ay, by) + wpx)) + 1
        if x1 < 0 or y1 < 0 or x0 >= W or y0 >= H:
            continue
        x0, y0 = max(x0, 0), max(y0, 0)
        x1, y1 = min(x1, W), min(y1, H)
        if x1 - x0 > 900 or y1 - y0 > 900:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        sx, sy = bx - ax, by - ay
        lp = math.hypot(sx, sy)
        if lp < 1e-3:
            sx, sy, lp = 0.0, 1e-3, 1e-3
        dx, dy = gx - ax, gy - ay
        t = (dx * sx + dy * sy) / (lp * lp)
        along = 2 * t - 1
        across = (sx * dy - sy * dx) / lp / max(wpx * 0.5, 1e-4)
        inside = (np.abs(along) <= 1) & (np.abs(across) <= 1) & (depth[y0:y1, x0:x1] > z[k])
        if not inside.any():
            continue
        rot = abs(sx / lp) + abs(sy / lp)
        pxa = 2.0 / max(wpx, 1e-4) * rot
        if thread is None:
            s = np.clip(1 - np.abs(across), 0, 1) * np.clip(1 - np.abs(along), 0, 1)
        else:
            reach = min(max(thread, pxa), 1.0)
            s = min(thread / reach, 1.0) * np.clip(1 - np.abs(across) / reach, 0, 1) * np.clip(1 - np.abs(along), 0, 1)
        rgb = fog_mix(tint, z[k])
        ys, xs = np.nonzero(inside)
        over(img, ys + y0, xs + x0, rgb, alpha[k] * s[inside], glow)


def draw_puffs(img, depth, cam, pos, size, alpha, tint, max_size, glow=0.0):
    x, y, z = cam.view(pos)
    order = np.argsort(-z)
    for k in order:
        if z[k] < 0.3 or alpha[k] <= 0.002:
            continue
        cx, cy, _ = cam.screen(pos[k])
        rpx = min(size[k] * cam.focal / z[k], max_size * H) * 0.5
        x0, x1 = int(cx - rpx), int(cx + rpx) + 1
        y0, y1 = int(cy - rpx), int(cy + rpx) + 1
        if x1 < 0 or y1 < 0 or x0 >= W or y0 >= H:
            continue
        x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, W), min(y1, H)
        gx, gy = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        r = np.clip(1 - np.hypot(gx - cx, gy - cy) / max(rpx, 1e-3), 0, 1)
        s = r * r
        inside = (s > 0) & (depth[y0:y1, x0:x1] > z[k])
        if not inside.any():
            continue
        ys, xs = np.nonzero(inside)
        over(img, ys + y0, xs + x0, fog_mix(tint, z[k]), alpha[k] * s[inside], glow)


# ---- RainSheet.shader ----------------------------------------------------------------------------------------------
M32 = np.uint64(0xFFFFFFFF)


def u32(x):
    return (np.asarray(x).astype(np.int64) & 0xFFFFFFFF).astype(np.uint64)


def hash32(x):
    x = u32(x)
    x ^= x >> np.uint64(16); x = (x * np.uint64(0x7feb352d)) & M32
    x ^= x >> np.uint64(15); x = (x * np.uint64(0x846ca68b)) & M32
    x ^= x >> np.uint64(16)
    return x


def random01(x):
    return (hash32(x) & np.uint64(0xFFFFFF)).astype(np.float64) / 16777216.0


def fwidth(a):
    gy, gx = np.gradient(a)
    return np.abs(gx) + np.abs(gy)


def streaks(u, y, slant, seed, share, cw, ch, fall):
    a = u + slant * y
    b = y + fall
    pu = np.maximum(fwidth(a), 1e-6)
    pv = np.maximum(fwidth(b), 1e-6)
    column = np.floor(a / cw)
    col = (u32(column) + np.uint64(seed) * np.uint64(0x9e3779b9)) & M32
    bv = b / ch + random01(col)
    row = np.floor(bv)
    cell = hash32(col ^ hash32((u32(row) & np.uint64(4095)) + np.uint64(0x51ed27)))
    here = (random01(cell) < share).astype(float)
    centre = 0.15 + 0.7 * random01(cell + np.uint64(1))
    start = random01(cell + np.uint64(2)) * (ch - STREAK_L)
    across = (a / cw - column - centre) * cw
    along = ((bv - row) * ch - start) / STREAK_L
    reach = np.maximum(STREAK_W, pu)
    thread = STREAK_W / reach * np.clip(1 - np.abs(across) / reach, 0, 1)
    sharp = here * thread * np.clip(1 - np.abs(along * 2 - 1), 0, 1)
    mean = share * STREAK_W * STREAK_L * 0.5 / (cw * ch)
    blur = np.clip(np.maximum(pu / cw, 0.5 * pv / STREAK_L) * 2 - 1, 0, 1)
    return sharp + (mean - sharp) * blur


def draw_sheets(img, depth, cam, rays, level, share, fall, drift, tint):
    radii = RADII[level]
    most = MOST_DROPS / (RAIN_BOX * RAIN_BOX * FALL_HEIGHT)
    hd = np.maximum(np.hypot(rays[..., 0], rays[..., 2]), 1e-6)
    cosf = rays @ cam.f
    slant = drift / DROP_SPEED
    for i in reversed(range(len(radii))):
        r = radii[i]
        lo = SHEET_INNER if i == 0 else math.sqrt(radii[i - 1] * r)
        hi = r * 1.6 if i == len(radii) - 1 else math.sqrt(r * radii[i + 1])
        cw = 1.0 / (most * (hi - lo) * CELL_H)
        t = r / hd
        p = cam.pos + rays * t[..., None]
        half = max(r * 1.2, 30.0)
        zv = t * cosf
        ok = (zv < depth) & (np.abs(p[..., 1] - cam.pos[1]) < half)
        o = (p[..., [0, 2]] - cam.pos[[0, 2]]) / r
        w = o ** 2
        w = w ** 2
        w = w / w.sum(axis=-1, keepdims=True)
        rain = w[..., 1] * streaks(p[..., 0], p[..., 1], slant[0], 0, share, cw, CELL_H, fall) \
            + w[..., 0] * streaks(p[..., 2], p[..., 1], slant[1], 1, share, cw, CELL_H, fall)
        a = tint[3] * rain * np.exp(-FOG_DENSITY * zv)
        a = np.where(ok, a, 0.0)
        img[:] = img * (1 - a[..., None]) + tint[:3] * a[..., None]


# ---- Spray --------------------------------------------------------------------------------------------------------
def euler_x(deg, v):
    """Quaternion.Euler(deg, 0, 0) * v in Unity: forward (0,0,1) goes to (0, -sin, cos)."""
    a = math.radians(deg)
    y = v[..., 1] * math.cos(a) - v[..., 2] * math.sin(a)
    z = v[..., 1] * math.sin(a) + v[..., 2] * math.cos(a)
    return np.stack([v[..., 0], y, z], -1)


def car_frame(v):
    """Car along +x: local x (right) is -z in the world, local z (forward) is +x, up is up."""
    return np.stack([v[..., 2], v[..., 1], -v[..., 0]], -1)


class Spray:
    def __init__(self, new):
        self.new = new
        self.drops = Particles()
        self.mist = Particles()

    def update(self, car_pos, speed, dt):
        amount = smooth(0.15, 0.9, WATER)
        standing = min(max((WATER - 0.8) / 1.5, 0), 1)
        rear = car_pos + np.array([-2.2, 0.0, 0.0])
        vcar = np.array([speed, 0.0, 0.0])
        if self.new:
            at = rear + np.array([0, 0.3, 0])
            pace = min(max((speed - 8) / 47, 0), 1)
            mult = (0.5 + 0.7 * pace) * (1 + 0.6 * standing)
            rate = 900 * amount * pace * (1 + 0.5 * standing)
            self.drops.acc += rate * dt
            n = int(self.drops.acc); self.drops.acc -= n
            if n:
                local = (RNG.random((n, 3)) - 0.5) * np.array([1.7, 0.15, 0.3])
                local = euler_x(-150, local)
                d = euler_x(-150, np.tile([0.0, 0.0, 1.0], (n, 1)))
                rnd = RNG.normal(size=(n, 3)); rnd /= np.linalg.norm(rnd, axis=1, keepdims=True)
                d = d * 0.65 + rnd * 0.35
                d /= np.linalg.norm(d, axis=1, keepdims=True)
                spd = RNG.uniform(4, 11, n) * mult
                pos = at + car_frame(local)
                age = RNG.random(n) * dt
                vel = vcar * 0.45 + car_frame(d) * spd[:, None]
                self.drops.add(pos - vcar * age[:, None], vel, np.zeros((n, 3)), RNG.uniform(0.25, 0.5, n),
                               RNG.uniform(0.03, 0.07, n), age)
            mrate = 60 * amount * min(max((speed - 18) / 37, 0), 1) * (1 + 0.5 * standing)
            self.mist.acc += mrate * dt
            n = int(self.mist.acc); self.mist.acc -= n
            if n:
                mat = at + np.array([-0.6, 0.5, 0])
                local = (RNG.random((n, 3)) - 0.5) * np.array([1.8, 0.3, 1.0])
                vol = np.stack([RNG.uniform(-1.6, 1.6, n), RNG.uniform(0.1, 0.6, n), RNG.uniform(-0.5, 0.5, n)], 1)
                self.mist.add(mat + car_frame(local), np.tile(vcar * 0.35, (n, 1)), car_frame(vol),
                              RNG.uniform(0.7, 1.3, n), RNG.uniform(1.2, 2.2, n))
        else:
            at = rear + np.array([0, 0.35, 0])
            rate = 75 * amount * min(max((speed - 10) / 45, 0), 1)
            self.drops.acc += rate * dt
            n = int(self.drops.acc); self.drops.acc -= n
            if n:
                local = (RNG.random((n, 3)) - 0.5) * np.array([1.6, 0.2, 0.6])
                vol = np.stack([RNG.uniform(-1.2, 1.2, n), RNG.uniform(0.6, 1.8, n), RNG.uniform(-1.2, 1.2, n)], 1)
                self.drops.add(at + car_frame(local), np.tile(vcar * 0.3, (n, 1)), vol, RNG.uniform(0.6, 1.1, n),
                               RNG.uniform(0.7, 1.4, n))
            mrate = 9 * amount * min(max((speed - 22) / 40, 0), 1)
            self.mist.acc += mrate * dt
            n = int(self.mist.acc); self.mist.acc -= n
            if n:
                local = (RNG.random((n, 3)) - 0.5) * np.array([2.2, 0.4, 1.5])
                vol = np.stack([RNG.uniform(-0.8, 0.8, n), RNG.uniform(0.2, 0.7, n), RNG.uniform(-0.8, 0.8, n)], 1)
                self.mist.add(at + np.array([0, 0.4, 0]) + car_frame(local), np.tile(vcar * 0.45, (n, 1)), vol,
                              RNG.uniform(1.8, 3.0, n), RNG.uniform(2.5, 4.5, n))

    def step(self, dt):
        if self.new:
            self.drops.step(dt, gravity=1.0, drag=3.5)
            self.mist.step(dt, drag=3.0)
        else:
            self.drops.step(dt)
            self.mist.step(dt, drag=1.6)

    def draw(self, img, depth, cam):
        light = LIGHT
        if self.new:
            t = self.mist.age / self.mist.life
            a = np.interp(t, [0, 0.1, 1], [0, 1, 0])
            size = self.mist.size * np.interp(t, [0, 1], [0.6, 2.2])
            draw_puffs(img, depth, cam, self.mist.pos, size, 0.22 * a, np.clip(light * 1.15, 0, 1), 0.5, glow=0.3)
            t = self.drops.age / self.drops.life
            a = np.interp(t, [0, 0.5, 1], [1, 0.7, 0])
            draw_streaks(img, depth, cam, self.drops.pos, self.drops.vel, self.drops.size, 0.5 * a,
                         np.clip(light * 1.15, 0, 1), 1.0, 0.03, thread=1.0, glow=0.4, max_size=0.012)
        else:
            t = self.mist.age / self.mist.life
            a = np.interp(t, [0, 0.25, 1], [0, 1, 0])
            size = self.mist.size * np.interp(t, [0, 1], [0.7, 1.9])
            draw_puffs(img, depth, cam, self.mist.pos, size, 0.1 * a, light, 0.5)
            t = self.drops.age / self.drops.life
            a = np.interp(t, [0, 0.15, 1], [0, 1, 0])
            size = self.drops.size * np.interp(t, [0, 1], [0.6, 2.2])
            draw_puffs(img, depth, cam, self.drops.pos, size, 0.42 * a, light, 0.6)


# ---- Shots --------------------------------------------------------------------------------------------------------
CAR = (0.90, 0.45, 0.12)


def draw_rain(img, depth, cam, rays, rain, new, level, fall):
    gust = max(WIND - 0.5, 0) * 2.2
    drift = np.array([gust, gust * 0.35])
    if new:
        share = min(600 + 450 * RAIN_MM, MOST_DROPS) / MOST_DROPS
        draw_sheets(img, depth, cam, rays, level, share, fall, drift, np.array([*LIGHT, 0.55]))
        vel = np.tile([gust, -DROP_SPEED, gust * 0.35], (len(rain.p.age), 1))
        draw_streaks(img, depth, cam, rain.p.pos, vel, rain.p.size, np.full(len(rain.p.age), 0.55), LIGHT, 1.0, 0.05,
                     thread=rain.thread)
    else:
        vel = np.tile([gust, -DROP_SPEED, gust * 0.35], (len(rain.p.age), 1))
        draw_streaks(img, depth, cam, rain.p.pos, vel, rain.p.size, np.full(len(rain.p.age), 0.3), LIGHT, 1.0, 0.05)


def run_rain(cams, new, level, frames_to_draw, cars_at, spray_cars=()):
    """Steps the weather through a list of cameras, one a frame; draws the frames asked for."""
    rain = NewRain() if new else OldRain()
    sprays = [Spray(new) for _ in spray_cars]
    out = {}
    fall = 0.0
    prev = None
    for k, cam in enumerate(cams):
        # Before: the rain followed the camera in Update, before the cameras moved in LateUpdate: a frame behind.
        seen = cam if new or prev is None else prev
        rain.update(seen, DT)
        rain.step(DT)
        fall = (fall + DROP_SPEED * DT) % (4096 * CELL_H)
        for s, (x0, z, speed) in zip(sprays, spray_cars):
            s.update(np.array([x0 + speed * k * DT, 0.0, z]), speed, DT)
            s.step(DT)
        prev = cam
        if k in frames_to_draw:
            cars = cars_at(k)
            img, depth, rays = render_scene(cam, cars)
            for s in sprays:
                s.draw(img, depth, cam)
            draw_rain(img, depth, cam, rays, rain, new, level, fall)
            out[k] = np.clip(img, 0, 1)
    return out


def save(img, path):
    Image.fromarray((np.clip(img, 0, 1) ** (1 / 1.0) * 255 + 0.5).astype(np.uint8)).save(path)


def label(img, text):
    im = Image.fromarray((np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8))
    d = ImageDraw.Draw(im)
    try:
        font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 34)
    except OSError:
        font = ImageFont.load_default()
    d.rectangle((0, 0, 20 + d.textlength(text, font=font), 54), fill=(0, 0, 0))
    d.text((10, 8), text, fill=(255, 255, 255), font=font)
    return np.asarray(im).astype(float) / 255


def sheet(tiles, cols, path, scale=0.5):
    small = [np.asarray(Image.fromarray((t * 255).astype(np.uint8)).resize((int(W * scale), int(H * scale)),
                                                                            Image.LANCZOS)) for t in tiles]
    rows = [np.concatenate(small[i:i + cols], axis=1) for i in range(0, len(small), cols)]
    if len(rows[-1]) and rows[-1].shape[1] < rows[0].shape[1]:
        pad = np.zeros((rows[-1].shape[0], rows[0].shape[1] - rows[-1].shape[1], 3), np.uint8)
        rows[-1] = np.concatenate([rows[-1], pad], axis=1)
    Image.fromarray(np.concatenate(rows, axis=0)).save(path)


def shot_cut(out):
    """A TV cut in a downpour: the car passes a trackside camera, then the director cuts to the next one along."""
    tv1 = Cam((-90, 6, -24), look=(-70, 0.6, 6), fov=30)
    cars = lambda k: [(-4 + 0.0 * k, 6.0, CAR), (-30, 4.0, (0.2, 0.3, 0.7))]
    cams = [tv1] * 90 + [Cam((-8, 4.5, -26), look=(0, 0.7, 6), fov=28)] * 80
    cut = 90
    keep = {cut, cut + 18, cut + 36, cut + 72}
    tiles = []
    before = run_rain(cams, False, "Medium", keep, cars)
    for k, name in [(cut, "first frame"), (cut + 18, "0.3 s"), (cut + 36, "0.6 s"), (cut + 72, "1.2 s")]:
        save(before[k], f"{out}/cut-before-{name.replace(' ', '').replace('.', '_')}.png")
        tiles.append(label(before[k], f"Before: {name} after the cut"))
    for level in ("Medium", "Ultra"):
        after = run_rain(cams, True, level, {cut}, cars)
        save(after[cut], f"{out}/cut-after-{level.lower()}-first-frame.png")
        tiles.append(label(after[cut], f"After, {level}: first frame after the cut"))
    sheet(tiles, 2, f"{out}/cut-sheet.png")
    # A cut to a camera 20 m along: before, the rain stayed round the old one.
    near = [Cam((-28, 4.5, -26), look=(-20, 0.7, 6), fov=28)] * 60 + [Cam((-8, 4.5, -26), look=(0, 0.7, 6), fov=28)] * 3
    b = run_rain(near, False, "Medium", {61}, cars)[61]
    a = run_rain(near, True, "Medium", {61}, cars)[61]
    save(b, f"{out}/shortcut-before.png")
    save(a, f"{out}/shortcut-after-medium.png")
    sheet([label(b, "Before: cut 20 m along, next frame"), label(a, "After, Medium: same frame")], 2,
          f"{out}/shortcut-sheet.png")


def shot_ivan(out):
    """Ivan's frame: a TV shot down onto the pit straight, the car side on."""
    cams = [Cam((-2, 7, -22), look=(1, 1.0, 14), fov=36)] * 100
    cars = lambda k: [(1.5, 6.0, CAR)]
    b = run_rain(cams, False, "Medium", {99}, cars)[99]
    save(b, f"{out}/tv-before.png")
    tiles = [label(b, "Before")]
    for level in ("Medium", "Ultra"):
        a = run_rain(cams, True, level, {99}, cars)[99]
        save(a, f"{out}/tv-after-{level.lower()}.png")
        tiles.append(label(a, f"After, {level}"))
    sheet(tiles, 3, f"{out}/tv-sheet.png", scale=0.5)


def onboard_cams(speed, n, z=6.0, back=0.0, fov=70):
    return [Cam((-200 + speed * k * DT - back, 1.15 if back == 0 else 1.6, z), yaw=90, pitch=-2 if back == 0 else -4,
                fov=fov) for k in range(n)]


def shot_onboard(out):
    speed = 60.0
    n = 120
    cams = onboard_cams(speed, n)
    cars = lambda k: [(-200 + speed * k * DT + 28, 4.0, (0.2, 0.3, 0.7)), (-200 + speed * k * DT + 75, 8.0, CAR)]
    b = run_rain(cams, False, "Medium", {n - 1}, cars)[n - 1]
    save(b, f"{out}/onboard-before.png")
    tiles = [label(b, "Before")]
    for level in ("Medium", "Ultra"):
        a = run_rain(cams, True, level, {n - 1}, cars)[n - 1]
        save(a, f"{out}/onboard-after-{level.lower()}.png")
        tiles.append(label(a, f"After, {level}"))
    sheet(tiles, 3, f"{out}/onboard-sheet.png")


def shot_longlens(out):
    cams = [Cam((-15, 7, -62), look=(0, 0.8, 6), fov=7)] * 100
    cars = lambda k: [(0, 6.0, CAR), (-9, 4.0, (0.2, 0.3, 0.7))]
    b = run_rain(cams, False, "Medium", {99}, cars)[99]
    save(b, f"{out}/longlens-before.png")
    tiles = [label(b, "Before")]
    for level in ("Medium", "Ultra"):
        a = run_rain(cams, True, level, {99}, cars)[99]
        save(a, f"{out}/longlens-after-{level.lower()}.png")
        tiles.append(label(a, f"After, {level}"))
    sheet(tiles, 3, f"{out}/longlens-sheet.png")


def shot_spray(out, view):
    speed = 200 / 3.6
    n = 120
    start = -260.0
    car_x = lambda k: start + speed * k * DT
    if view == "tv":
        end = car_x(n - 1)
        cams = [Cam((end - 30, 4.0, -22), look=(car_x(k) - 4, 0.8, 6), fov=24) for k in range(n)]
        cars = lambda k: [(car_x(k), 6.0, CAR)]
    else:
        cams = [Cam((car_x(k) - 16, 1.15, 6.0), yaw=90, pitch=-2, fov=70) for k in range(n)]
        cars = lambda k: [(car_x(k), 6.0, CAR)]
    tiles = []
    for new, level in ((False, "Medium"), (True, "Medium"), (True, "Ultra")):
        img = run_rain(cams, new, level, {n - 1}, cars, spray_cars=[(start, 6.0, speed)])[n - 1]
        name = "before" if not new else f"after-{level.lower()}"
        save(img, f"{out}/spray-{view}-{name}.png")
        tiles.append(label(img, "Before" if not new else f"After, {level}"))
    sheet(tiles, 3, f"{out}/spray-{view}-sheet.png")


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    shots = sys.argv[2:] or ["tv", "cut", "onboard", "longlens", "spray-tv", "spray-onboard"]
    for s in shots:
        {"tv": shot_ivan, "cut": shot_cut, "onboard": shot_onboard, "longlens": shot_longlens,
         "spray-tv": lambda o: shot_spray(o, "tv"), "spray-onboard": lambda o: shot_spray(o, "onboard")}[s](out)
        print("done", s, flush=True)


if __name__ == "__main__":
    main()
