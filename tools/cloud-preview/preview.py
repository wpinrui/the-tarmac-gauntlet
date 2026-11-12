"""
Offline preview of the race's sky: the volumetric clouds (Volumetrics.shader) over the sky (Sky.shader), or the painted
clouds Low and Medium draw, from a driver's camera. The data (the cloud map, the 3D noise and each sky's colours) comes from
the game's own code through the atmosphere check:

    bash tools/atmosphere-check/run.sh --dump out/cloud-preview/data
    python3 tools/cloud-preview/preview.py out/cloud-preview/data out/cloud-preview [scene ...]

Options, as environment variables: YAW=sun looks towards the sun (else north, or YAW degrees); PAINTED=1 draws the painted
clouds instead; STEPS and FRAMES override the march's steps and the jittered frames averaged.

Every constant of the clouds' look is read from unity/Assets/Rendering/Shaders/CloudLook.hlsl and CloudCover.hlsl, the
files the shader includes, so the two cannot drift apart. The functions below are ports of the shader's: keep them in step.
Everything is lit in linear light, as the game is, then graded and tone mapped as PostEffects does.
"""
import json
import math
import os
import re
import sys
import time

import numpy as np
from PIL import Image

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SHADERS = os.path.join(ROOT, "unity", "Assets", "Rendering", "Shaders")


def constants():
    """Every #define TG_CLOUD_<name> <number> of the shader's includes, by name."""
    c = {}
    for name in ("CloudCover.hlsl", "CloudLook.hlsl"):
        with open(os.path.join(SHADERS, name)) as f:
            for m in re.finditer(r"^#define TG_CLOUD_(\w+)\s+(-?[\d.]+)\s*$", f.read(), re.M):
                c[m.group(1)] = float(m.group(2))
    return c


C = constants()

W, H = int(os.environ.get("W", 960)), int(os.environ.get("H", 540))
FOV = 60.0           # vertical, degrees
EYE = np.array([0.0, 2.0, 0.0])
PITCH = 12.0         # degrees up from level
YAW = os.environ.get("YAW", "0")
PAINTED = os.environ.get("PAINTED", "0") == "1"
STEPS = int(os.environ.get("STEPS", C["ULTRA_STEPS"]))
FRAMES = int(os.environ.get("FRAMES", 4))     # jittered frames averaged, as the game's history does
EXPOSURE = 0.45      # stops, PostEffects.ToneExposure
SKY_CLOUD_SCALE = 0.45  # TimeOfDay.CloudScale

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


def trilinear_wrap(cube, q, channel):
    """Samples the 3D noise (z, y, x, channel) at uvw, wrapping, texel centres at (i + 0.5) / size."""
    n = cube.shape[0]
    f = q * n - 0.5
    i0 = np.floor(f).astype(np.int64)
    t = f - i0
    i0 %= n
    i1 = (i0 + 1) % n
    c = cube[..., channel]
    x0, y0, z0 = i0[:, 0], i0[:, 1], i0[:, 2]
    x1, y1, z1 = i1[:, 0], i1[:, 1], i1[:, 2]
    tx, ty, tz = t[:, 0], t[:, 1], t[:, 2]

    def lerp(a, b, s):
        return a + (b - a) * s

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


def hg(cs, g):
    """Henyey-Greenstein, times 4 pi: 1 for isotropic."""
    k = 1 + g * g - 2 * g * cs
    return (1 - g * g) / (k * np.sqrt(k))


def scatter(cs, g):
    """Sky.shader's Scatter: Henyey-Greenstein scaled to 1 looking straight at the light."""
    k = 1 + g * g - 2 * g * cs
    return (1 - g) ** 3 / (k * np.sqrt(k))


def ign(px, py, frame):
    """Interleaved gradient noise, as Random.hlsl."""
    px = px + 5.588238 * (frame & 63)
    py = py + 5.588238 * (frame & 63)
    return np.mod(52.9829189 * np.mod(0.06711056 * px + 0.00583715 * py, 1.0), 1.0)

# ---------------------------------------------------------------------------------------------------- the clouds


class Clouds:
    """Volumetrics.shader's CloudShape, CloudDensity and TowardsSun."""

    def __init__(self, scene, big, small, n3):
        self.big, self.small, self.n3 = big, small, n3
        self.o = scene["overcast"]
        self.cover = scene["cover"]
        self.tiles = scene["tiles"]
        self.base = C["BASE"] - C["DECK_LOWERS"] * self.o
        self.thick = C["THICKNESS"] + C["DECK_THICKENS"] * self.o
        self.sun = np.array(scene["sunDir"])

    def map_noise(self, x, z):
        """CloudCover.hlsl's TgCloudNoise, with the map at rest."""
        o = self.o
        u, v = x * self.tiles, z * self.tiles
        small = bilinear_wrap(self.small, u * 2.3, v * 2.3)
        small = small + (0.5 - small) * (0.7 * o)
        return bilinear_wrap(self.big, u, v) * 0.7 + small * 0.3

    def noise(self, q, ch):
        """Noise3: the 3D noise, stretched from its 0.25 to 0.7 spread to 0 to 1."""
        return saturate((trilinear_wrap(self.n3, q, ch) - 0.25) / 0.45)

    def shape(self, p):
        """CloudShape: the cloud before the billows and detail eat into it, never less than CloudDensity's. Returns the
        density, and for each point its height in the layer and how far into the far field it is."""
        o = self.o
        h = (p[:, 1] - self.base) / self.thick
        d = np.zeros(len(p))
        far = np.zeros(len(p))
        inside = (h > 0) & (h < 1)
        if not inside.any():
            return d, h, far
        pi, hi = p[inside], h[inside]
        n = self.map_noise(pi[:, 0], pi[:, 2])
        fi = smoothstep(C["FAR_NEAR"], C["FAR"], np.hypot(pi[:, 0] - EYE[0], pi[:, 2] - EYE[2]))
        cov = saturate(C["COVER_FLOOR"] + (n - self.cover) * C["COVER_SLOPE"]) * (1 - C["FAR_THINS"] * fi) * (1 - o) + 0.95 * o
        bigs = self.noise(pi / C["SHAPE_PERIOD"], 0)
        merging = fi > 0
        if merging.any():
            bigs[merging] += (self.noise(pi[merging] / C["FAR_SHAPE_PERIOD"], 0) - bigs[merging]) * fi[merging]
        hf = hi / (1 - 0.65 * fi)
        prof = saturate(hf / 0.06) * (1 - smoothstep(C["TOP_ROUNDS"], 1.0, hf) * (1 - 0.6 * o)) * (hf < 1)
        di = saturate((bigs * prof - (1 - cov)) / np.maximum(cov, 1e-3)) * cov
        if o > 0:
            roll = self.noise(pi / C["DECK_ROLL_PERIOD"], 1)
            lump = smoothstep(0.05, 0.95, 0.75 * self.noise(pi / C["DECK_LUMP_PERIOD"] + 0.37, 0)
                              + 0.25 * self.noise(pi / C["DECK_PATCH_PERIOD"] + 0.61, 1))
            under = 0.04 + 0.22 * (1 - roll)
            thin = C["DECK_THIN"]
            deck = saturate((hi - under) / 0.05) * (1 - smoothstep(0.45, 0.9, hi)) * (thin + (1 - thin) * lump) * C["DECK_DENSITY"]
            di = np.maximum(di * (1 - o), o * deck)
        d[inside] = di
        far[inside] = fi
        return d, h, far

    def density(self, p):
        """CloudDensity: 0 to 1."""
        o = self.o
        d, h, far = self.shape(p)
        live = d > 0
        if live.any():
            pl, hl, fl, dl = p[live], h[live], far[live], d[live]
            billow = self.noise(pl / C["BILLOW_PERIOD"], 0)
            ero = (1 - billow) * C["EROSION"] * (0.4 + hl) * (1 - 0.7 * fl) * (1 - 0.8 * o)
            dl = saturate((dl - ero) / (1 - ero))
            fray = dl > 0
            if fray.any():
                det = self.noise(pl[fray] / C["DETAIL_PERIOD"], 1)
                fr = (1 - det) * C["FRAY"] * (1 - fl[fray]) * (1 - 0.8 * o)
                dl[fray] = saturate((dl[fray] - fr) / (1 - fr))
            d[live] = saturate((dl - C["CUT"]) / (1 - C["CUT"]) * C["DENSITY"])
        return d

    def towards_sun(self, p):
        """TowardsSun: optical depth towards the sun over four steps of growing length."""
        depth = np.zeros(len(p))
        q = p.copy()
        for step in (50.0, 100.0, 200.0, 400.0):
            q = q + self.sun * step
            depth += self.density(q) * step
        return depth * C["EXTINCTION"]


def fine_step(t):
    return np.clip(t * C["STEP_SHARE"], C["STEP_MIN"], C["STEP_MAX"])


def march(clouds, s, d, px, py, cs, frames):
    """The March pass's cloud part for rays that climb: the light the clouds add, and how much of the sky shows."""
    o = clouds.o
    lit_col = np.array(s["cloudLit"])
    sky_light, ground_light = np.array(s["skyLight"]), np.array(s["groundLight"])
    enter = np.maximum((clouds.base - EYE[1]) / d[:, 1], 0)
    leave = np.minimum((clouds.base + clouds.thick - EYE[1]) / d[:, 1], np.minimum(enter + 15000.0, 30000.0))
    # Each bounce of light inside a cloud loses some of the forward glow: thick cloud before the sun is dark, its thin
    # edges silver.
    phase = [0.5 * hg(cs, 0.8 * k) + 0.5 * hg(cs, -0.3 * k) for k in (1.0, 0.5, 0.25)]
    ambient_k = C["AMBIENT"] * (1 + (C["AMBIENT_DECK"] - 1) * o)
    shut = C["SHUT_OUT"]
    ext = C["EXTINCTION"]
    stride = C["STRIDE"]
    # The far clouds sink into the haze: what shows of them fades, and the sky behind shows through instead.
    fade = np.exp(-enter * s["hazeDensity"] / (C["HAZE_REACH"] + (C["DECK_HAZE_REACH"] - C["HAZE_REACH"]) * o))

    def shade(ai, p, r, dt, light, trans):
        od = clouds.towards_sun(p)
        h = saturate((p[:, 1] - clouds.base) / clouds.thick)
        sunlit = (np.exp(-od) * phase[0][ai] + C["SCATTER_ONCE"] * np.exp(-od * 0.25) * phase[1][ai]
                  + C["SCATTER_TWICE"] * np.exp(-od * 0.06) * phase[2][ai])
        powder = 1.0 - np.exp(-r * ext * 200.0)
        direct = lit_col * (C["SUN"] * sunlit * (0.3 + 0.7 * powder))[:, None]
        # Thick cloud above shuts the sky's light out: the undersides of thick cores darken.
        ambient = (ground_light + (sky_light - ground_light) * h[:, None]) * (ambient_k * (shut + (1 - shut) * np.exp(-od * 0.08)))[:, None]
        through = np.exp(-r * ext * dt)
        light[ai] += trans[ai, None] * (direct + ambient) * (1 - through)[:, None]
        trans[ai] *= through

    acc_l = np.zeros((len(d), 3))
    acc_t = np.zeros(len(d))
    for frame in range(frames):
        jitter = ign(px, py, frame * 7 + 1)
        light = np.zeros((len(d), 3))
        trans = np.ones(len(d))
        # Strides through empty air on the cloud's shape; on touching one, back up a stride and walk it in short steps
        # with the full density, until three steps in a row find nothing.
        fine = np.zeros(len(d), bool)
        empty = np.zeros(len(d), np.int32)
        clear = np.ones(len(d), bool)
        tt = enter + fine_step(enter) * stride * jitter
        for _ in range(STEPS):
            alive = (trans > 0.01) & (tt < leave)
            if not alive.any():
                break
            ai = np.nonzero(alive)[0]
            cm = ~fine[ai]
            cr = ai[cm]
            if len(cr):
                c, _, _ = clouds.shape(EYE + d[cr] * tt[cr, None])
                touch = c > 0
                back = cr[touch]
                tt[back] = np.where(clear[back], np.maximum(tt[back] - fine_step(tt[back]) * (stride - 1), enter[back]), tt[back])
                fine[back] = True
                empty[back] = 0
                miss = cr[~touch]
                tt[miss] += fine_step(tt[miss]) * stride
                clear[miss] = True
            fr = ai[~cm]
            if len(fr):
                p = EYE + d[fr] * tt[fr, None]
                step = fine_step(tt[fr])
                rho = clouds.density(p)
                hit = rho > 0.002
                if hit.any():
                    shade(fr[hit], p[hit], rho[hit], step[hit], light, trans)
                empty[fr] = np.where(hit, 0, empty[fr] + 1)
                tt[fr] += step
                gone = fr[empty[fr] >= 3]
                fine[gone] = False
                clear[gone] = False
        acc_l += light * fade[:, None]
        acc_t += 1 - (1 - trans) * fade
    return acc_l / frames, acc_t / frames

# ---------------------------------------------------------------------------------------------------- the sky


def painted(s, d, cs, sh, haze, big, small):
    """Sky.shader's painted clouds (the _SKY_DETAIL path): their colour, and how much of the sky they cover."""
    o = s["overcast"]
    up = d[..., 1]
    cover = s["skyCover"]
    lit_c = np.array(s["cloudLit"])
    shade_c = np.array(s["cloudShade"])
    sun_c = np.array(s["sunColour"])
    above = saturate(s["sunDir"][1] * 20 + 1)
    k = SKY_CLOUD_SCALE * (1 + (0.5 - 1) * o) / (np.maximum(up, 0) * 0.85 + 0.15)
    u, v = d[..., 0] * k, d[..., 2] * k

    def dens(u, v):
        sm = bilinear_wrap(small, u * 2.3, v * 2.3)
        sm = sm + (0.5 - sm) * 0.7 * o
        return bilinear_wrap(big, u, v) * 0.7 + sm * 0.3

    n = dens(u, v)
    fine = bilinear_wrap(small, u * 6.1, v * 6.1)
    n = n + (fine - 0.5) * 0.1 * (1 - 0.6 * o)
    c = smoothstep(cover - 0.2 * o, cover + 0.18 + 0.1 * o, n) * smoothstep(0.0, 0.12, up)
    lit = saturate(0.55 + (n - dens(u + sh[0] * 0.03, v + sh[1] * 0.03)) * 4 + (n - dens(u + sh[0] * 0.08, v + sh[1] * 0.08)) * 2.5)
    lit = lit + (1 - smoothstep(0.3, 0.75, n) - lit) * o
    thick = saturate((n - cover) / 0.35)
    t = (lit * (1 - 0.3 * thick))[..., None]
    cloud = (shade_c + (lit_c - shade_c) * t) * (1 - 0.35 * o * smoothstep(0.3, 0.8, n))[..., None]
    edge = saturate(c * (1 - c) * 4)
    pc = saturate(cs)
    cloud = cloud + sun_c * ((pc ** 8 * edge * 1.4 + pc ** 6 * (1 - c) * 0.6) * above * (1 - o))[..., None]
    hz = np.array(s["haze"])
    cloud = cloud + (hz - cloud) * (haze * 0.8)[..., None]
    return cloud, c


def sky(s, d, cs, big, small):
    """Sky.shader's _SKY_DETAIL path, with the painted clouds when PAINTED is set."""
    zen, hor, haze_c, glow, ground = (np.array(s[k]) for k in ("zenith", "horizon", "haze", "glow", "ground"))
    golden_glow = np.array(s["goldenGlow"])
    sun_c = np.array(s["sunColour"])
    sun = np.array(s["sunDir"])
    deep = s["deep"]
    up = d[..., 1]
    sh = sun[[0, 2]] / (np.linalg.norm(sun[[0, 2]]) + 1e-5)
    dh = d[..., [0, 2]] / (np.linalg.norm(d[..., [0, 2]], axis=-1, keepdims=True) + 1e-5)
    towards = dh @ sh * 0.5 + 0.5
    above = saturate(sun[1] * 20 + 1)
    air = np.exp(-np.maximum(up, 0) * 3.2)
    col = zen + (hor - zen) * air[..., None]
    haze = np.exp(-np.abs(up) * (10.0 + 14.0 * deep))
    band = np.exp(-np.maximum(up, 0) * 6.5)
    col = col + glow * (towards ** 3 * band)[..., None]
    col = col + (haze_c - col) * (haze * 0.85 * (1 - 0.75 * deep))[..., None]
    col = col + (ground - col) * (saturate(-up * 6) * (1 - haze * 0.6))[..., None]
    col = col + golden_glow * (towards ** 4 * np.exp(-np.maximum(up, 0) * 4.0))[..., None]
    sc = scatter(cs, 0.8) * 0.55 + scatter(cs, 0.55) * 0.05
    col = col + sun_c * (sc * (0.55 + 0.45 * air) * above)[..., None]
    a = np.sqrt(np.maximum(2 * (1 - cs), 0))
    rim = saturate(a / 0.0145)
    disc = (a < 0.0145) * above
    disc_col = sun_c * (4 * (1 - 0.45 * rim * rim))[..., None] * (1 + (np.array([1.0, 0.8, 0.65]) - 1) * (rim * rim)[..., None])
    disc_col = disc_col * saturate(up * 25 + 0.4)[..., None]
    if PAINTED:
        cloud, c = painted(s, d, cs, sh, haze, big, small)
        col = col + disc_col * (disc * (1 - c))[..., None]
        col = col + (cloud - col) * c[..., None]
    else:
        col = col + disc_col * disc[..., None]
    return col


def render(scene, big, small, n3):
    s = scene
    sun = np.array(s["sunDir"])
    py, px = np.mgrid[0:H, 0:W].astype(np.float64)
    t = math.tan(math.radians(FOV) / 2)
    sx = (2 * (px + 0.5) / W - 1) * t * W / H
    sy = (1 - 2 * (py + 0.5) / H) * t
    pitch = math.radians(PITCH)
    yaw = math.atan2(sun[0], sun[2]) if YAW == "sun" else math.radians(float(YAW))
    fwd = np.array([math.sin(yaw) * math.cos(pitch), math.sin(pitch), math.cos(yaw) * math.cos(pitch)])
    right = np.array([math.cos(yaw), 0, -math.sin(yaw)])
    up = np.cross(fwd, right)
    d = fwd + sx[..., None] * right + sy[..., None] * up
    d /= np.linalg.norm(d, axis=-1, keepdims=True)
    cs = d @ sun

    img = sky(s, d, cs, big, small)
    show = np.ones((H, W))
    if not PAINTED:
        climbing = (d[..., 1] > 0.01).ravel()
        idx = np.nonzero(climbing)[0]
        light, trans = march(Clouds(s, big, small, n3), s, d.reshape(-1, 3)[idx], px.ravel()[idx], py.ravel()[idx], cs.ravel()[idx], FRAMES)
        flat = img.reshape(-1, 3)
        flat[idx] = flat[idx] * trans[:, None] + light
        show.ravel()[idx] = trans
    # The sun's glare where it shows through, standing in for SunRays and the bloom.
    sun_c = np.array(s["sunColour"])
    ang = np.arccos(np.clip(cs, -1, 1))
    above = saturate(sun[1] * 20 + 1)
    img = img + sun_c * (above * (1 - s["overcast"]) * show * (np.exp(-ang / 0.03) + 0.25 * np.exp(-ang / 0.15)))[..., None]

    # Graded and tone mapped as PostEffects does: exposure, white balance, contrast in log space, saturation, then ACES.
    g = s["grade"]
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
    high = d[..., 1] > math.sin(math.radians(15))
    coverage = float(np.mean(show[high] < 0.5)) if not PAINTED else float("nan")
    return (srgb * 255 + 0.5).astype(np.uint8), coverage


def main():
    data, out = sys.argv[1], sys.argv[2]
    big, small, n3, scenes = load(data)
    wanted = sys.argv[3:]
    for scene in scenes:
        if wanted and scene["name"] not in wanted:
            continue
        start = time.time()
        img, coverage = render(scene, big, small, n3)
        name = scene["name"] + ("-towards-sun" if YAW == "sun" else "") + ("-painted" if PAINTED else "")
        Image.fromarray(img).save(f"{out}/{name}.png")
        print(f"{name}: {coverage:.0%} of the sky above 15 degrees behind cloud, {time.time() - start:.0f} s")


if __name__ == "__main__":
    main()
