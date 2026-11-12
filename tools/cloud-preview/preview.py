"""
Offline preview of the race's volumetric clouds: the same cloud map, 3D noise, density, lighting and march as the
Volumetrics shader, drawn from a driver's camera for a few skies. The data (the noise and each sky's colours) comes from
the game's own code through the atmosphere check:

    bash tools/atmosphere-check/run.sh --dump out/cloud-preview/data
    python3 tools/cloud-preview/preview.py out/cloud-preview/data out/cloud-preview [scene ...]

Everything is lit in linear light, as the game is, then tone mapped with ACES and the game's exposure.
Keep this file and the March pass of Volumetrics.shader in step: CloudDensity, TowardsSun and the march below are ports.
"""
import json
import math
import sys
import time

import numpy as np
from PIL import Image

W, H = 960, 540
FOV = 60.0           # vertical, degrees
EYE = np.array([0.0, 2.0, 0.0])
PITCH = float(__import__('os').environ.get('PITCH', 12.0))  # degrees up from level
YAW = __import__('os').environ.get('YAW', '0')  # degrees, 0 looks north (+z), 180 towards a southern sun
STEPS = int(__import__("os").environ.get("STEPS", 48))
FRAMES = int(__import__("os").environ.get("FRAMES", 4))
EXPOSURE = 0.45      # stops, PostEffects.ToneExposure
NOISE_PERIOD = 1800.0
EXTINCTION = 0.04    # per m at full density
P = dict(cov0=0.35, covk=2.5, bigp=3600.0, midp=900.0, detp=300.0, top0=0.2, ero=0.35, fray=0.1, dens=3.0, ms1=0.6, ms2=0.3, amb=0.5, hazek=8.0, stepk=0.01, stepmin=40.0, stepmax=200.0, coarse=3.0, cut=0.06, glare=1.0, deep=1.0, far0=4000.0, far1=16000.0)
import os
for _kv in os.environ.get('P', '').split(','):
    if '=' in _kv:
        _k, _v = _kv.split('=')
        P[_k] = float(_v)
W = int(os.environ.get('W', W)); H = int(os.environ.get('H', H))

# ---------------------------------------------------------------------------------------------------- data


def load(data):
    size = 256
    raw = np.fromfile(f"{data}/map.f32", dtype=np.float32)
    big = raw[: size * size].reshape(size, size)
    small = raw[size * size:].reshape(size, size)
    n3 = np.fromfile(f"{data}/noise3d.u8", dtype=np.uint8).reshape(64, 64, 64, 2).astype(np.float32) / 255.0
    with open(f"{data}/scenes.json") as f:
        scenes = json.load(f)
    return big, small, n3, scenes


def bilinear_wrap(field, u, v):
    """Samples a square field at uv (texture space, wrapping), filtered as the GPU does: texel centres at (i + 0.5) / size."""
    n = field.shape[0]
    fx = u * n - 0.5
    fy = v * n - 0.5
    x0 = np.floor(fx).astype(np.int64)
    y0 = np.floor(fy).astype(np.int64)
    tx = fx - x0
    ty = fy - y0
    x0 %= n
    y0 %= n
    x1 = (x0 + 1) % n
    y1 = (y0 + 1) % n
    a = field[y0, x0]
    b = field[y0, x1]
    c = field[y1, x0]
    d = field[y1, x1]
    return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty


def trilinear_wrap(cube, u, v, w, channel):
    """Samples the 3D noise (z, y, x, channel) at uvw, wrapping, texel centres at (i + 0.5) / size."""
    n = cube.shape[0]
    fx, fy, fz = u * n - 0.5, v * n - 0.5, w * n - 0.5
    x0, y0, z0 = np.floor(fx).astype(np.int64), np.floor(fy).astype(np.int64), np.floor(fz).astype(np.int64)
    tx, ty, tz = fx - x0, fy - y0, fz - z0
    x0 %= n
    y0 %= n
    z0 %= n
    x1, y1, z1 = (x0 + 1) % n, (y0 + 1) % n, (z0 + 1) % n
    c = cube[..., channel]

    def lerp(a, b, t):
        return a + (b - a) * t

    c00 = lerp(c[z0, y0, x0], c[z0, y0, x1], tx)
    c10 = lerp(c[z0, y1, x0], c[z0, y1, x1], tx)
    c01 = lerp(c[z1, y0, x0], c[z1, y0, x1], tx)
    c11 = lerp(c[z1, y1, x0], c[z1, y1, x1], tx)
    return lerp(lerp(c00, c10, ty), lerp(c01, c11, ty), tz)


def saturate(x):
    return np.clip(x, 0.0, 1.0)


def smoothstep(a, b, x):
    t = saturate((x - a) / (b - a))
    return t * t * (3 - 2 * t)

# ---------------------------------------------------------------------------------------------------- the clouds


class Sky:
    def __init__(self, scene, big, small, n3):
        self.s = scene
        self.big, self.small, self.n3 = big, small, n3
        self.o = scene["overcast"]
        self.cover = scene["cover"]
        self.tiles = scene["tiles"]
        self.base = 1500.0 - 300.0 * self.o
        self.thick = P.get('thick', 900.0) + 600.0 * self.o
        self.sun = np.array(scene["sunDir"])

    def map_noise(self, x, z):
        """CloudCover.hlsl TgCloudNoise, no drift."""
        o = self.o
        u, v = x * self.tiles, z * self.tiles
        small = bilinear_wrap(self.small, u * 2.3, v * 2.3)
        small = small + (0.5 - small) * (0.7 * o)
        return bilinear_wrap(self.big, u, v) * 0.7 + small * 0.3

    def density(self, p, cheap=False):
        """CloudDensity: 0 to 1. The 3D noise makes the clouds; the map only says how much of the sky they may take."""
        x, y, z = p[..., 0], p[..., 1], p[..., 2]
        o = self.o
        h = (y - self.base) / self.thick
        out = np.zeros_like(y)
        inside = (h > 0) & (h < 1)
        if not inside.any():
            return out
        xi, yi, zi, hi = x[inside], y[inside], z[inside], h[inside]
        n = self.map_noise(xi, zi)
        # Coverage: soft, so the 3D noise draws each cloud's edge. A fair day leaves most of the sky clear.
        cov = saturate(P["cov0"] + (n - self.cover) * P["covk"]) * (1 - o) + o * 0.95
        q = np.stack([xi, yi, zi], -1)
        # Far off, clouds merge into fewer, larger, flatter shapes: seen edge on, their small ones would only be grain.
        far = smoothstep(P["far0"], P["far1"], np.hypot(xi - EYE[0], zi - EYE[2]))
        big = self.noise(q / (P["bigp"] * (1 + 2.5 * far)[:, None]), 0)
        hf = hi / (1 - 0.65 * far)
        # Fair-weather cumulus: a flat base, then narrowing to rounded tops.
        prof = saturate(hf / 0.06) * (1.0 - smoothstep(P["top0"], 1.0, hf) * (1 - o * 0.6)) * (hf < 1)
        shape = big * prof
        d = saturate((shape - (1.0 - cov)) / np.maximum(cov, 1e-3)) * cov
        if o > 0:
            # Under an overcast the layer closes into one deck: its underside rolls up and down, it is thick in lumps
            # and thin in patches the light comes through.
            roll = self.noise(q / 2400.0, 1)
            lump = smoothstep(0.1, 0.9, 0.6 * self.noise(q / 5000.0 + 0.37, 0) + 0.4 * self.noise(q / 2200.0 + 0.61, 1))
            under = 0.04 + 0.22 * (1 - roll)
            deck = saturate((hi - under) / 0.05) * (1 - smoothstep(0.45, 0.9, hi)) * (0.25 + 0.75 * lump) * 0.45
            d = np.maximum(d * (1 - o), o * deck)
        if cheap:
            # The shape before the billows and detail eat into it: never less than the full density, so empty air
            # here is empty air there.
            out[inside] = d
            return out
        live = d > 0
        if live.any():
            ql = q[live]
            hh = hi[live]
            billow = self.noise(ql / P["midp"], 0)
            dl = d[live]
            fl = far[live]
            ero = (1 - billow) * P["ero"] * (0.4 + hh) * (1 - 0.7 * fl) * (1 - 0.8 * o)
            dl = saturate((dl - ero) / (1 - ero))
            fray = dl > 0
            if fray.any():
                det = self.noise(ql[fray] / P["detp"], 1)
                e = dl[fray]
                fr = (1 - det) * P["fray"] * (1 - fl[fray]) * (1 - 0.8 * o)
                dl[fray] = saturate((e - fr) / (1 - fr))
            d[live] = dl
        # Thin wisps between the clouds read as grain from the ground; clear them.
        out[inside] = saturate((d - P["cut"]) / (1 - P["cut"]) * P["dens"])
        return out

    def noise(self, q, ch):
        """The 3D noise, stretched from its 0.2 to 0.72 spread to 0 to 1."""
        r = trilinear_wrap(self.n3, q[:, 0], q[:, 1], q[:, 2], ch)
        return saturate((r - 0.25) / 0.45)

    def towards_sun(self, p):
        """Optical depth towards the sun over 4 steps of growing length."""
        depth = np.zeros(p.shape[0])
        q = p.copy()
        for step in (50.0, 100.0, 200.0, 400.0):
            q = q + self.sun * step
            depth += self.density(q) * step
        return depth * EXTINCTION


def hg(cs, g):
    k = 1 + g * g - 2 * g * cs
    return (1 - g * g) / (k * np.sqrt(k))     # x 4 pi: 1 for isotropic


def ign(px, py, frame):
    """Interleaved gradient noise, as Random.hlsl."""
    px = px + 5.588238 * (frame & 63)
    py = py + 5.588238 * (frame & 63)
    return np.mod(52.9829189 * np.mod(0.06711056 * px + 0.00583715 * py, 1.0), 1.0)


def render(sky, frames=FRAMES):
    s = sky.s
    col = lambda k: np.array(s[k])
    zen, hor, haze, glow, sun_col = col("zenith"), col("horizon"), col("haze"), col("glow"), col("sun")
    lit_col = col("cloudLit")
    # Proposed sky palette for the volumetric sky (DEEP=0 draws the game's sky as it is): a deeper blue overhead, the haze
    # a thin band in the sky's own colour.
    deep = P["deep"] * (1 - sky.o)
    zen = zen * (1 - deep * np.array([0.55, 0.4, 0.15]))
    haze_band = 1 - 0.5 * deep
    hor = hor * (1 - deep * np.array([0.22, 0.12, 0.03]))
    # The low sun: warm orange-gold light on the clouds, dusky mauve shade, the sky warm towards it.
    e = s["elevation"]
    gold = deep * smoothstep(-2, 3, e) * (1 - smoothstep(6, 18, e))
    lit_col = lit_col * (1 + gold * (np.array([1.15, 0.78, 0.45]) - 1))
    mauve = 1 + gold * (np.array([0.95, 0.78, 0.9]) - 1)
    # Camera looking north, pitched up.
    py, px = np.mgrid[0:H, 0:W].astype(np.float64)
    t = math.tan(math.radians(FOV) / 2)
    sx = (2 * (px + 0.5) / W - 1) * t * W / H
    sy = (1 - 2 * (py + 0.5) / H) * t
    pitch = math.radians(PITCH)
    sun = sky.sun
    yaw = math.atan2(sun[0], sun[2]) if YAW == 'sun' else math.radians(float(YAW))
    fwd = np.array([math.sin(yaw) * math.cos(pitch), math.sin(pitch), math.cos(yaw) * math.cos(pitch)])
    right = np.array([math.cos(yaw), 0, -math.sin(yaw)])
    up = np.cross(fwd, right)
    d = fwd + sx[..., None] * right + sy[..., None] * up
    d /= np.linalg.norm(d, axis=-1, keepdims=True)
    dy = d[..., 1]
    sun = sky.sun
    cs = d @ sun

    # The sky behind, as Sky.shader's detailed path draws it without its painted clouds.
    air = np.exp(-np.maximum(dy, 0) * 3.2)[..., None]
    bg = zen + (hor - zen) * air
    towards = (np.einsum("ij,j->i", d[..., [0, 2]].reshape(-1, 2), sun[[0, 2]] / (np.linalg.norm(sun[[0, 2]]) + 1e-6)).reshape(H, W) * 0.5 + 0.5)
    band = np.exp(-np.maximum(dy, 0) * 6.5)
    bg = bg + glow * (towards ** 3 * band)[..., None]
    hz = np.exp(-np.abs(dy) * (10.0 + 14.0 * deep))[..., None]
    bg = bg + (haze - bg) * hz * 0.85 * haze_band
    bg = bg + glow * (gold * 0.8 * towards ** 4 * np.exp(-np.maximum(dy, 0) * 4.0))[..., None]
    scatter = ((1 - 0.8) ** 3 / (1 + 0.64 - 1.6 * cs) ** 1.5) * 0.55 + ((1 - 0.55) ** 3 / (1 + 0.3025 - 1.1 * cs) ** 1.5) * 0.05
    above = saturate(sun[1] * 20 + 1)
    bg = bg + sun_col / max(np.max(sun_col), 1e-3) * min(np.max(sun_col), 1.25) * (scatter * (0.55 + 0.45 * air[..., 0]) * above)[..., None]
    disc = (np.arccos(np.clip(cs, -1, 1)) < 0.0145) * above
    bg = bg + sun_col * 30.0 * (1 - sky.o) * disc[..., None]
    ground = np.array(s["ground"]) * 0.8
    bg = np.where((dy < 0)[..., None], ground + (haze - ground) * np.exp(dy * 40)[..., None] * 0.8, bg)

    # The march.
    climbing = dy > 0.01
    idx = np.nonzero(climbing.ravel())[0]
    bg_idx = bg.reshape(-1, 3)[idx]
    dd = d.reshape(-1, 3)[idx]
    enter = np.maximum((sky.base - EYE[1]) / dd[:, 1], 0)
    leave = np.minimum((sky.base + sky.thick - EYE[1]) / dd[:, 1], np.minimum(enter + 15000.0, 30000.0))
    css = cs.ravel()[idx]
    pxs, pys = px.ravel()[idx], py.ravel()[idx]
    acc_l = np.zeros((len(idx), 3))
    acc_t = np.zeros(len(idx))
    # Each bounce of light inside a cloud loses some of the forward glow: thick cloud before the sun is dark, its thin
    # edges silver.
    phase = [0.5 * hg(css, 0.8 * k) + 0.5 * hg(css, -0.3 * k) for k in (1.0, 0.5, 0.25)]
    # Light from the sky dome above, and from the sunlit ground below.
    amb_hi = zen * 0.7 + hor * 0.3
    amb_lo = amb_hi * 0.4 + np.array(s["ground"]) * sun_col * max(sun[1], 0.0) * 0.15
    adaptive = os.environ.get("MARCH", "adaptive") == "adaptive"

    def fine_step(t):
        return np.clip(t * P["stepk"], P["stepmin"], P["stepmax"])

    def shade(ai, p, r, dt, light, trans):
        od = sky.towards_sun(p)
        h = saturate((p[:, 1] - sky.base) / sky.thick)
        # Sun light through the cloud, with the light scattered on many times inside it lifting the shade.
        sunlit = (np.exp(-od) * phase[0][ai] + P['ms1'] * np.exp(-od * 0.25) * phase[1][ai]
                  + P['ms2'] * np.exp(-od * 0.06) * phase[2][ai])
        powder = 1.0 - np.exp(-r * EXTINCTION * 200.0)
        direct = lit_col * (sunlit * (0.3 + 0.7 * powder))[:, None]
        # Thick cloud above shuts out the sky's light too: the undersides of thick cores darken.
        ambient = (amb_lo + (amb_hi - amb_lo) * h[:, None]) * mauve * (P['amb'] * (0.3 + 0.7 * np.exp(-od * 0.08)))[:, None]
        radiance = direct + ambient
        through = np.exp(-r * EXTINCTION * dt)
        light[ai] += trans[ai, None] * radiance * (1 - through)[:, None]
        trans[ai] *= through

    for frame in range(frames):
        jitter = ign(pxs, pys, frame * 7 + 1)
        light = np.zeros((len(idx), 3))
        trans = np.ones(len(idx))
        if not adaptive:
            dt = (leave - enter) / STEPS
            tt = enter + dt * jitter
            for _ in range(STEPS):
                alive = (trans > 0.01) & (tt < leave)
                if not alive.any():
                    break
                ai = np.nonzero(alive)[0]
                p = EYE + dd[ai] * tt[ai, None]
                rho = sky.density(p)
                hit = rho > 0.002
                if hit.any():
                    shade(ai[hit], p[hit], rho[hit], dt[ai[hit]], light, trans)
                tt = tt + dt
        else:
            # Long strides through empty air on the cheap shape; on touching cloud, back up a stride and walk it in
            # short steps with the full density, until a few steps in a row find nothing.
            fine = np.zeros(len(idx), bool)
            empty = np.zeros(len(idx), np.int32)
            clear = np.ones(len(idx), bool)   # the last stride was in empty air, so a touch backs up
            tt = enter + fine_step(enter) * P["coarse"] * jitter
            for _ in range(STEPS):
                alive = (trans > 0.01) & (tt < leave)
                if not alive.any():
                    break
                ai = np.nonzero(alive)[0]
                p = EYE + dd[ai] * tt[ai, None]
                step = fine_step(tt[ai])
                cm = ~fine[ai]
                cr = ai[cm]
                if len(cr):
                    c = sky.density(p[cm], cheap=True)
                    touch = c > 0
                    back = cr[touch]
                    tt[back] = np.where(clear[back], np.maximum(tt[back] - step[cm][touch] * (P["coarse"] - 1), enter[back]), tt[back])
                    fine[back] = True
                    empty[back] = 0
                    miss = cr[~touch]
                    tt[miss] += fine_step(tt[miss]) * P["coarse"]
                    clear[miss] = True
                # Rays that strode this time take their short steps from the next.
                fr = ai[~cm]
                if len(fr):
                    pf = EYE + dd[fr] * tt[fr, None]
                    sf = fine_step(tt[fr])
                    rho = sky.density(pf)
                    hit = rho > 0.002
                    if hit.any():
                        shade(fr[hit], pf[hit], rho[hit], sf[hit], light, trans)
                    empty[fr] = np.where(hit, 0, empty[fr] + 1)
                    tt[fr] += sf
                    gone = fr[empty[fr] >= 3]
                    fine[gone] = False
                    clear[gone] = False
        # Far clouds sink into the haze: the air between is lit the haze's colour.
        fade = np.exp(-enter * 3.912 / (s["hazeDistance"] * P["hazek"]))
        light = light * fade[:, None] + bg_idx * ((1 - trans) * (1 - fade))[:, None]
        acc_l += light
        acc_t += trans
    acc_l /= frames
    acc_t /= frames

    img = bg.reshape(-1, 3).copy()
    img[idx] = img[idx] * acc_t[:, None] + acc_l
    img = img.reshape(H, W, 3)
    # The sun's glare, as SunRays draws it: only where the sun shows through.
    ang = np.arccos(np.clip(cs, -1, 1))
    see = np.ones(H * W)
    see[idx] = acc_t
    see = see.reshape(H, W)
    img = img + sun_col * (above * see * (P["glare"] * np.exp(-ang / 0.03) + 0.25 * P["glare"] * np.exp(-ang / 0.15)))[..., None]
    # Graded and tone mapped as PostEffects does: exposure, white balance, contrast in log space, saturation, then ACES.
    g = s.get("grade", [0, 0, 0, 0, 0])
    x = img * (2.0 ** (EXPOSURE + g[0]))
    warm = g[3] / 100.0
    x = x * np.array([1 + 0.3 * warm, 1 + 0.03 * g[4] / 100.0, 1 - 0.3 * warm])
    logc = np.log10(5.555556 * np.maximum(x, 0) + 0.047996) * 0.244161 + 0.386036
    mid = 0.4135884
    logc = (logc - mid) * (1 + g[1] / 100.0) + mid
    x = np.maximum((10 ** ((logc - 0.386036) / 0.244161) - 0.047996) / 5.555556, 0)
    lum = x @ np.array([0.2126, 0.7152, 0.0722])
    x = np.maximum(lum[..., None] + (x - lum[..., None]) * (1 + g[2] / 100.0), 0)
    x = np.clip((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), 0, 1)
    srgb = np.where(x <= 0.0031308, 12.92 * x, 1.055 * np.power(x, 1 / 2.4) - 0.055)
    # Cover of the sky more than 15 degrees up, where the clouds are not just the far field seen edge on.
    high = dy.ravel()[idx] > math.sin(math.radians(15))
    coverage = float(np.mean(acc_t[high] < 0.5)) if high.any() else 0.0
    return (srgb * 255 + 0.5).astype(np.uint8), coverage


def main():
    data, out = sys.argv[1], sys.argv[2]
    big, small, n3, scenes = load(data)
    wanted = sys.argv[3:]
    for scene in scenes:
        if wanted and scene["name"] not in wanted:
            continue
        start = time.time()
        img, coverage = render(Sky(scene, big, small, n3))
        name = scene['name'] + ('-towards-sun' if YAW == 'sun' else '')
        Image.fromarray(img).save(f"{out}/{name}.png")
        print(f"{name}: {coverage:.0%} of the sky above 15 degrees behind cloud, {time.time() - start:.0f} s")


if __name__ == "__main__":
    main()
