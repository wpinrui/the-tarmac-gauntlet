"""The road just ahead of the onboard camera, worked out per pixel as the game draws it, with each effect that touches it
switched on or off, to find what makes the near road grainy.

The road is a flat plane under the onboard camera (LiveTiming: 1.2 m up, 70 degree field of view) at 3:06 pm, the sun
where SkyClock puts it. Each pixel reads the road's textures as the GPU would: the mip chain, trilinear, eight-tap
anisotropic filtering. Then the Track shader's surface (TrackSurface.hlsl: grain, gloss, patches, polish, the normal
map) is lit as ShadeDielectric lights it: Lambert sun, the full GGX sheen, the ambient light and the environment
reflection. On top, as the effect is on:
  ssao     URP's screen-space ambient occlusion from the depth buffer (Source Depth, as the renderers use it)
  shadow   ScreenShadows.shader's soft shadow of the sun: the blocker search and filter over the road's own shadow map,
           biased as URP biases the casters
  contact  ScreenShadows.shader's contact shadows: the march towards the sun through the depth buffer
  bump     the normal map

Usage: python3 render.py out.png [key=value ...]   e.g. bump=0 ssao=1 quality=ultra heading=200 bump_scale=1
"""
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(ROOT, "tools", "linear-preview"))
import textures  # noqa: E402
import sky as skyclock  # noqa: E402
from post import lin, frame, save  # noqa: E402

DEFAULTS = dict(quality="ultra", bump=1, ssao=1, shadow=1, contact=1, bump_scale=1.0, normal_strength=1.2, heading=215.0,
                hour=15.1, tilt=0.0, width=1920, height=1080, along=0.0, probe="shared", crop=0.0, show="", speckle=0.5, binder=0.78, full=0, eye=1.2, fov=70.0, offset=-1.5)
# Quality levels: QualityLevels.Shadows (spread, filter taps, search taps, widest texels, contact reach, steps, far,
# strength), the cascade's first split (distance x share) and URP's SSAO settings from the renderer.
QUALITY = {
    "high": dict(shadow=(0.008, 16, 16, 16.0, 0.4, 12, 80.0, 0.9), split=500 * 0.05, ssao=(1.5, 0.5, 150.0, 8)),
    "ultra": dict(shadow=(0.008, 32, 24, 24.0, 0.5, 20, 120.0, 0.9), split=600 * 0.045, ssao=(1.5, 0.5, 200.0, 12)),
}
EYE = 1.2
FOV = 70.0
NEAR = 0.3
SHADOW_RES = 2048  # each of the four cascades' tiles in a 4096 shadow map
DEPTH_BIAS, NORMAL_BIAS, KERNEL = 0.75, 0.6, 3.5  # the pipeline assets' biases; URP scales them by the soft kernel
CACHE = os.environ.get("ROAD_CACHE", os.path.join(ROOT, "out", "road-grain"))

DISC = np.array([(-0.94201624, -0.39906216), (0.94558609, -0.76890725), (-0.09418410, -0.92938870),
                 (0.34495938, 0.29387760), (-0.91588581, 0.45771432), (-0.81544232, -0.87912464),
                 (-0.38277543, 0.27676845), (0.97484398, 0.75648379), (0.44323325, -0.97511554),
                 (0.53742981, -0.47373420), (-0.26496911, -0.41893023), (0.79197514, 0.19090188),
                 (-0.24188840, 0.99706507), (-0.81409955, 0.91437590), (0.19984126, 0.78641367),
                 (0.14383161, -0.14100790)])


def args():
    o = dict(DEFAULTS)
    for a in sys.argv[2:]:
        k, v = a.split("=", 1)
        o[k] = type(DEFAULTS[k])(v) if not isinstance(DEFAULTS[k], str) else v
    return o


def mips(tex):
    """A mip chain by 2x2 box averages, as Texture2D.Apply makes it (linear values)."""
    chain = [tex.astype(np.float32)]
    while chain[-1].shape[0] > 1:
        t = chain[-1]
        chain.append(0.25 * (t[0::2, 0::2] + t[1::2, 0::2] + t[0::2, 1::2] + t[1::2, 1::2]))
    return chain


def bilinear(t, u, v):
    """Wrapped bilinear read of t (h, w, c) at texel coords u, v (pixel centres at .5)."""
    h, w = t.shape[:2]
    x, y = u - 0.5, v - 0.5
    x0, y0 = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64)
    fx, fy = (x - x0)[..., None], (y - y0)[..., None]
    x0 %= w
    y0 %= h
    x1, y1 = (x0 + 1) % w, (y0 + 1) % h
    return (t[y0, x0] * (1 - fx) + t[y0, x1] * fx) * (1 - fy) + (t[y1, x0] * (1 - fx) + t[y1, x1] * fx) * fy


def aniso(chain, uv, dx, dy):
    """The GPU's anisotropic read: up to 8 trilinear taps along the footprint's long axis. uv and its screen
    derivatives in tiles; returns the filtered texel."""
    size = chain[0].shape[0]
    ex, ey = dx * size, dy * size
    lx, ly = np.linalg.norm(ex, axis=-1), np.linalg.norm(ey, axis=-1)
    major = np.maximum(lx, ly)
    minor = np.maximum(np.minimum(lx, ly), 1e-6)
    n = np.clip(np.ceil(major / minor), 1, 8)
    lod = np.clip(np.log2(np.maximum(major / n, 1e-6)), 0, len(chain) - 1)
    axis = np.where((lx >= ly)[..., None], ex, ey)
    out = 0
    u0, v0 = uv[..., 0] * size, uv[..., 1] * size
    for i in range(8):
        # Taps past n repeat the centre: the same weight as n taps spread over the footprint.
        use = i < n
        off = np.where(use[..., None], axis * ((i + 0.5) / np.maximum(n, 1) - 0.5)[..., None], 0)
        su, sv = u0 + off[..., 0], v0 + off[..., 1]
        lo = np.floor(lod).astype(np.int64)
        f = (lod - lo)[..., None]
        acc = 0
        for level in range(len(chain)):
            sel = (lo == level) | (lo + 1 == level)
            if not sel.any():
                continue
            scale = 2.0 ** level
            val = bilinear(chain[level], su / scale, sv / scale)
            wgt = np.where((lo == level)[..., None], 1 - f, 0) + np.where((lo + 1 == level)[..., None], f, 0)
            acc = acc + val * wgt
        out = out + acc * use[..., None]
    return out / n[..., None], lod


def main():
    o = args()
    out = sys.argv[1]
    q = QUALITY[o["quality"]]
    W, H = o["width"], o["height"]

    height, stone, grain, patch, polish = textures.load(CACHE)
    rgb, gloss = textures.colour(stone, grain, speckle=o["speckle"], binder=o["binder"])
    nrm = textures.normals(height, o["normal_strength"])
    # The GPU filters an sRGB texture in linear light, a linear one as stored (the normals as 0 to 1 colours).
    surface = mips(np.concatenate([lin(rgb), gloss[..., None], nrm * 0.5 + 0.5], -1))
    macro = mips(np.stack([patch, polish], -1))

    s = skyclock.at(o["hour"])
    sun = s["sun_dir"]
    yaw = math.radians(o["heading"])
    fwd = np.array([math.sin(yaw), 0.0, math.cos(yaw)])
    right = np.array([math.cos(yaw), 0.0, -math.sin(yaw)])
    up = np.array([0.0, 1.0, 0.0])
    # The road may tilt about its direction of travel (camber, crossfall), raising its right edge.
    t = math.radians(o["tilt"])
    road_n = up * math.cos(t) - right * math.sin(t)
    road_r = np.cross(road_n, fwd)
    road_r /= np.linalg.norm(road_r)
    road_r = -road_r if road_r @ right < 0 else road_r
    cam = np.array([0.0, o["eye"], 0.0]) + fwd * o["along"]

    # The lower half of the screen: pixel centres and their rays.
    top = H // 2 + 2
    ys, xs = np.mgrid[top:H, 0:W].astype(np.float64) + 0.5
    tan_v = math.tan(math.radians(o["fov"]) / 2)
    aspect = W / H

    def ray(px, py):
        sx = (px / W * 2 - 1) * tan_v * aspect
        sy = (1 - py / H * 2) * tan_v
        d = fwd[None, None, :] + sx[..., None] * right + sy[..., None] * up
        return d / np.linalg.norm(d, axis=-1, keepdims=True), sx, sy

    def hit(px, py):
        d, _, _ = ray(px, py)
        denom = d @ road_n
        tt = -(cam @ road_n) / np.minimum(denom, -1e-6)
        return cam + d * tt[..., None], tt, d

    P, dist, D = hit(xs, ys)
    Px, _, _ = hit(xs + 1, ys)
    Py, _, _ = hit(xs, ys + 1)
    valid = (D @ road_n) < -1e-4
    eye_depth = (P - cam) @ fwd

    def road_uv(p, metres):
        return np.stack([(p - cam * 0) @ road_r / metres, (p @ fwd) / metres], -1)

    uv = road_uv(P, 4.0)
    duvx, duvy = road_uv(Px, 4.0) - uv, road_uv(Py, 4.0) - uv
    tex, lod = aniso(surface, uv, duvx, duvy)
    albedo = tex[..., 0:3]
    gl = tex[..., 3]
    ts = tex[..., 4:7] * 2 - 1

    # Patches and polish (TrackSurface.hlsl), read in world xz.
    def wread(scale, rot=False, mul=1.0, off=0.0):
        w = np.stack([P[..., 0], P[..., 2]], -1) / scale
        wx = np.stack([Px[..., 0], Px[..., 2]], -1) / scale
        wy = np.stack([Py[..., 0], Py[..., 2]], -1) / scale
        if rot:
            w, wx, wy = (np.stack([a[..., 1], -a[..., 0]], -1) * mul + off for a in (w, wx, wy))
        return aniso(macro, w, wx - w, wy - w)[0]

    polish = wread(60 * 6.0)[..., 1] * 0.7 + wread(60 * 6.0, True, 4.3, 0.17)[..., 1] * 0.3
    pt = (wread(60.0)[..., 0] * 0.65 + wread(60.0, True, 3.7, 0.31)[..., 0] * 0.35) * 2 - 1
    smooth = 0.25 * (0.7 + 0.6 * gl) * (0.75 + 0.55 * polish)
    albedo = albedo * np.power(np.maximum(1 + pt * 0.35, 0), 2.2)[..., None]
    smooth = np.clip(smooth * (1 - pt * 0.35 * 0.5), 0, 1)

    if o["bump"]:
        ts = ts.copy()
        ts[..., 0:2] *= o["bump_scale"]
        ts /= np.linalg.norm(ts, axis=-1, keepdims=True)
        n = ts[..., 0:1] * road_r + ts[..., 1:2] * fwd + ts[..., 2:3] * road_n
    else:
        n = np.broadcast_to(road_n, P.shape).copy()
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    v = -D

    shadow = np.ones(P.shape[:2])
    contact = np.ones(P.shape[:2])
    spread, taps, search, widest, c_reach, c_steps, c_far, c_strength = q["shadow"]
    pixel = np.stack([xs, ys], -1)
    noise = np.modf(52.9829189 * np.modf(pixel @ np.array([0.06711056, 0.00583715]))[0])[0]
    if o["shadow"]:
        shadow = soft_shadow(P, sun, road_n, noise, q, spread, taps, search, widest, cam, fwd)
    if o["contact"]:
        contact = contact_shadow(P, sun, noise, cam, fwd, right, up, tan_v, aspect, W, H, top, eye_depth,
                                 c_reach, int(c_steps), c_far, c_strength)
    sun_vis = np.minimum(shadow, contact)

    ao = np.zeros(P.shape[:2])
    if o["ssao"]:
        ao = ssao(eye_depth, P, cam, fwd, right, up, tan_v, aspect, W, H, top, q["ssao"])
    # URP: the direct light under the occlusion's direct share (0.3), the ambient under all of it.
    direct_ao = 1 - ao * 0.3
    indirect_ao = 1 - ao

    sun_lin = lin(s["colour"]) * 3.2
    amb = lin(s["amb_sky"]) * 0.9
    NoL = np.clip(n @ sun, 0, 1)
    NoV = np.clip((n * v).sum(-1), 1e-4, 1)
    h = sun[None, None, :] + v
    h /= np.linalg.norm(h, axis=-1, keepdims=True)
    NoH = np.clip((n * h).sum(-1), 0, 1)
    LoH = np.clip(h @ sun, 0, 1)
    pr = 1 - smooth
    r = np.maximum(pr * pr, 0.0078125)
    a2 = r * r
    d = NoH * NoH * (a2 - 1) + 1
    Dg = a2 / (d * d)
    vis = 0.5 / (NoL * np.sqrt(NoV * NoV * (1 - a2) + a2) + NoV * np.sqrt(NoL * NoL * (1 - a2) + a2) + 1e-6)
    F = 0.04 + 0.96 * (1 - LoH) ** 5
    spec = np.minimum(Dg * vis * F, 64.0)
    lit = (sun_vis * direct_ao * NoL)[..., None] * sun_lin
    diffuse = albedo * 0.96
    colour = diffuse * lit + spec[..., None] * lit + diffuse * amb * indirect_ao[..., None]
    # The environment's reflection: the shared car probe (the trackside round the watched car) or, when the road's
    # renderer is handed two car probes instead, URP's fallback, the sky.
    refl = v * -1 + 2 * (n * v).sum(-1, keepdims=True) * n
    if o["probe"] == "shared":
        env = environment(s, refl, trackside=True)
    else:
        env = environment(s, refl, trackside=False)
    grazing = np.clip(smooth + 0.04, 0, 1)
    fres = (1 - NoV) ** 4
    env_spec = env * (1 / (a2 + 1))[..., None] * (0.04 + (grazing - 0.04) * fres)[..., None]
    colour = colour + env_spec * indirect_ao[..., None]
    # Fog over the distance.
    fog = np.exp(-s["density"] * dist)[..., None]
    colour = colour * fog + lin(s["fog"]) * 0.9 * (1 - fog)
    colour[~valid] = lin(s["horizon"])
    if o["full"]:
        colour = full_frame(o, s, colour, P, valid, cam, fwd, road_r, road_n, sun, sun_lin, amb, ray, W, H, top, dist)

    g = skyclock.grade(s)
    img = frame(colour, **g)
    if o["show"]:
        m = {"contact": contact, "shadow": shadow, "ao": 1 - ao}[o["show"]]
        img = np.repeat(m[..., None], 3, -1)
    if o["full"]:
        img = bonnet(img, W, H)
    if o["crop"] > 0:
        img = img[: int(img.shape[0] * o["crop"])]
    save(out, img)
    stats = dict(ao_mean=float(ao[valid].mean()), shadow_mean=float(shadow[valid].mean()),
                 contact_mean=float(contact[valid].mean()), shadow_min=float(shadow[valid].min()),
                 contact_min=float(contact[valid].min()), ao_max=float(ao[valid].max()))
    print(out, " ".join(f"{k}={v:.4f}" for k, v in stats.items()))


def full_frame(o, s, road, P, valid, cam, fwd, road_r, road_n, sun, sun_lin, amb, ray, W, H, top, dist):
    """The whole onboard frame: the sky with fair-weather cloud above the horizon, and either side of the 12 m road its
    white edge lines and the grass."""
    across = (P - cam) @ road_r - o["offset"]
    grass_alb = lin(np.array([0.24, 0.36, 0.17]))
    flat = np.clip(sun @ road_n, 0, 1)
    w = 1 + 0.08 * np.sin(P[..., 0] * 3.1) * np.cos(P[..., 2] * 2.3)
    grass = grass_alb * (sun_lin * flat + amb) * w[..., None]
    line = lin(np.array([0.92, 0.92, 0.9])) * (sun_lin * flat + amb) * 0.8
    edge = np.abs(across) > 6.0
    out = np.where(edge[..., None], grass, road)
    stripe = (np.abs(across) > 5.85) & (np.abs(across) <= 6.0)
    out = np.where(stripe[..., None], line, out)
    fog = np.exp(-s["density"] * dist)[..., None]
    out = np.where(valid[..., None], np.where((edge | stripe)[..., None], out * fog + lin(s["fog"]) * 0.9 * (1 - fog), out), out)
    ys, xs = np.mgrid[0:top, 0:W].astype(np.float64) + 0.5
    d, _, _ = ray(xs, ys)
    skyc = lin(skyclock.sky(s, d)) * 1.6
    # Partly cloudy: soft fair-weather cumulus, lit from the sun's side.
    patch, _ = textures.macro(21)
    up = np.maximum(d[..., 1], 0.02)
    cu, cv = d[..., 0] / up * 0.08 + 0.3, d[..., 2] / up * 0.08
    c = bilinear(patch[..., None], cu * 256, cv * 256)[..., 0]
    cover = np.clip((c - 0.52) / 0.12, 0, 1) * np.clip(d[..., 1] * 12, 0, 1)
    cloud = lin(np.array([0.92, 0.93, 0.95])) * (1.5 + 0.4 * (d @ sun))[..., None]
    skyc = skyc * (1 - cover[..., None]) + cloud * cover[..., None]
    return np.concatenate([skyc, out], 0)


def bonnet(img, W, H):
    """The car's bonnet across the bottom of the frame, as the bonnet camera sees it."""
    ys, xs = np.mgrid[0:img.shape[0], 0:W].astype(np.float64)
    off = img.shape[0] - H
    u = (xs - W / 2) / (W * 0.62)
    crest = H * 0.885 - H * 0.04 * np.clip(1 - u * u, 0, 1) + off
    m = ys > crest
    shade = np.clip((ys - crest) / (H * 0.14), 0, 1)
    paint = np.array([0.55, 0.36, 0.30]) * (0.75 + 0.25 * shade)[..., None]
    img = img.copy()
    img[m] = paint[m]
    return img


def environment(s, refl, trackside):
    """A rough reflection's colour: the sky over a blurry cone about the reflection, the trackside below 6 degrees if
    the probe holds it."""
    out = 0
    rng = np.random.default_rng(3)
    for k in range(12):
        j = rng.normal(size=3) * 0.45
        d = refl + j
        d = d / np.linalg.norm(d, axis=-1, keepdims=True)
        c = lin(skyclock.sky(s, d))
        if trackside:
            low = (d[..., 1] < math.sin(math.radians(6)))[..., None]
            c = np.where(low, lin(np.array([0.13, 0.16, 0.10])) * 1.1, c)
        out = out + c
    return out / 12 * 1.0


def cascade_texel(split):
    """Metres per shadow map texel in the first cascade: the sphere round the camera's view out to the split."""
    tan_v = math.tan(math.radians(FOV) / 2)
    hh, hw = split * tan_v, split * tan_v * 16 / 9
    corner = math.hypot(hh, hw)
    # The smallest sphere round the near and far corners: centred on the far plane when that is the larger.
    c = (split * split + corner * corner - (NEAR * NEAR + (NEAR * tan_v) ** 2 * (1 + (16 / 9) ** 2))) / (2 * (split - NEAR))
    c = min(c, split)
    radius = math.hypot(split - c, corner)
    return 2 * radius / SHADOW_RES


def soft_shadow(P, L, road_n, noise, q, spread, taps, search, widest, cam, fwd):
    """ScreenShadows.shader's SoftShadow over the road's own shadow map, for a road with nothing above it but itself.

    The shadow map holds the road plane pushed back by URP's caster biases; a texel's caster is nearer the sun than the
    receiver, and so shadows it, where the texel lies below the receiver by more than that push (worked out exactly for
    a plane, in the light's frame)."""
    texel = cascade_texel(q["split"])
    sin_e = float(L @ road_n)
    depth_bias = DEPTH_BIAS * texel * KERNEL
    normal_bias = NORMAL_BIAS * texel * KERNEL
    b = depth_bias * sin_e + normal_bias * (1 - sin_e)  # how far the caster plane sits below the road
    # The light's frame: its up (the shadow map's v) is the world's up seen from the sun, its right the map's u.
    lu = road_n - L * sin_e
    lu /= np.linalg.norm(lu)
    lr = np.cross(lu, L)
    # Up the map, the road drops towards the sun: a texel's caster lies (offset along lu) * cos(e) higher than the
    # receiver's own. Shadowed where that falls below -b.
    cos_e = float(np.sqrt(max(1 - sin_e * sin_e, 0)))
    pu, pv = P @ lr / texel, P @ lu / texel  # receiver in texels

    def caster_drop(tu, tv):
        """How far below the receiver a texel's caster point lies, m, for texel coords tu, tv (centre-snapped)."""
        return (tv - pv) * texel * cos_e  # positive: the caster is above the receiver along lu

    def shadowed(tu, tv):
        return caster_drop(tu, tv) < -b

    s, c = np.sin(noise * 2 * np.pi), np.cos(noise * 2 * np.pi)
    reach = np.clip(spread * 20.0, 2 * texel, widest * texel)
    count = np.zeros(P.shape[:2])
    gap_sum = np.zeros(P.shape[:2])
    for i in range(int(search)):
        dx, dy = DISC[i & 15] * (1.0 if i < 16 else 0.5)
        ox, oy = c * dx - s * dy, s * dx + c * dy
        tu = np.floor(pu + ox * reach / texel) + 0.5
        tv = np.floor(pv + oy * reach / texel) + 0.5
        drop = caster_drop(tu, tv)
        blocker = drop < -b
        # The caster's depth towards the sun over the receiver's, m.
        gap_sum += np.where(blocker, -(drop + b) / max(sin_e, 1e-3), 0)
        count += blocker
    any_blocker = count > 0
    gap = np.where(any_blocker, gap_sum / np.maximum(count, 1), 0)
    radius = np.clip(gap * spread, 1.25 * texel, widest * texel)
    lit = np.zeros(P.shape[:2])
    for j in range(int(taps)):
        dx, dy = DISC[j & 15]
        ox, oy = c * dx - s * dy, s * dx + c * dy
        if j >= 16:
            ox, oy = oy * 0.6, -ox * 0.6
        tu = pu + ox * radius / texel
        tv = pv + oy * radius / texel
        # Hardware comparison: the four nearest texels compared, then blended.
        x0, y0 = np.floor(tu - 0.5), np.floor(tv - 0.5)
        fx, fy = tu - 0.5 - x0, tv - 0.5 - y0
        acc = 0
        for ax, ay, w in ((0, 0, (1 - fx) * (1 - fy)), (1, 0, fx * (1 - fy)), (0, 1, (1 - fx) * fy), (1, 1, fx * fy)):
            acc = acc + w * (~shadowed(x0 + ax + 0.5, y0 + ay + 0.5))
        lit += acc
    return np.where(any_blocker, lit / taps, 1.0)


def project(p, cam, fwd, right, up, tan_v, aspect, W, H):
    rel = p - cam
    z = rel @ fwd
    sx = (rel @ right) / (z * tan_v * aspect)
    sy = (rel @ up) / (z * tan_v)
    return (sx + 1) / 2 * W, (1 - sy) / 2 * H, z


def contact_shadow(P, L, noise, cam, fwd, right, up, tan_v, aspect, W, H, top, depth, reach0, steps, far, strength):
    """ScreenShadows.shader's ContactShadow, marching through the depth buffer of the road (point reads)."""
    dist = np.linalg.norm(P - cam, axis=-1)
    reach = reach0 * (1 + dist * 0.02)
    hit = np.zeros(P.shape[:2])
    done = np.zeros(P.shape[:2], bool)
    for i in range(steps):
        t = reach * (i + noise) / steps
        p = P + L * t[..., None]
        px, py, z = project(p, cam, fwd, right, up, tan_v, aspect, W, H)
        ix, iy = np.floor(px).astype(np.int64), np.floor(py).astype(np.int64) - top
        inside = (ix >= 0) & (ix < W) & (py >= 0) & (py < H)
        sky = iy < 0
        ixc, iyc = np.clip(ix, 0, W - 1), np.clip(iy, 0, depth.shape[0] - 1)
        scene = np.where(sky, 1e9, depth[iyc, ixc])
        behind = z - scene
        bias = 0.01 + 0.0015 * z
        h = inside & ~done & (behind > bias) & (behind < 0.25 + 0.01 * z)
        hit = np.where(h, 1 - i / steps * 0.5, hit)
        done |= h | ~inside
    fade = 1 - np.clip((dist - far * 0.6) / (far * 0.4), 0, 1)
    fade = fade * fade * (3 - 2 * fade)
    return np.where(dist > far, 1.0, 1 - hit * fade * strength)


def ssao(depth, P, cam, fwd, right, up, tan_v, aspect, W, H, top, settings):
    """URP's SSAO (SSAO.hlsl, blue noise, Source Depth with its normals rebuilt from the depth): occlusion 0 to 1."""
    intensity, radius, falloff, samples = settings
    depth = depth.astype(np.float32)
    h, w = depth.shape
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)

    def pos(ix, iy):
        ixc, iyc = np.clip(ix, 0, w - 1), np.clip(iy, 0, h - 1)
        z = depth[iyc, ixc]
        sx = ((ixc + 0.5) / W * 2 - 1) * tan_v * aspect
        sy = (1 - (iyc + top + 0.5) / H * 2) * tan_v
        return np.stack([sx * z, sy * z, z], -1).astype(np.float32)

    ix, iy = xs.astype(np.int64), ys.astype(np.int64)
    p0 = pos(ix, iy)
    # High quality normals: each axis takes the nearer of its two neighbours.
    l, r_, d_, u_ = pos(ix - 1, iy), pos(ix + 1, iy), pos(ix, iy + 1), pos(ix, iy - 1)
    hx = np.where((np.abs(l[..., 2] - p0[..., 2]) < np.abs(r_[..., 2] - p0[..., 2]))[..., None], p0 - l, r_ - p0)
    hy = np.where((np.abs(d_[..., 2] - p0[..., 2]) < np.abs(u_[..., 2] - p0[..., 2]))[..., None], d_ - p0, p0 - u_)
    n = np.cross(hy, hx)
    n /= np.linalg.norm(n, axis=-1, keepdims=True) + 1e-12
    n = np.where((n[..., 2:3] > 0), -n, n)  # facing the camera
    rng = np.random.default_rng(int(1))
    ao = np.zeros((h, w), np.float32)
    for k in range(int(samples)):
        vec = rng.normal(size=(h, w, 3)).astype(np.float32)
        vec /= np.linalg.norm(vec, axis=-1, keepdims=True)
        scale = rng.random((h, w, 1)).astype(np.float32) ** 2 * 0.9 + 0.1
        vec = vec * scale
        vec = np.where(((vec * n).sum(-1, keepdims=True) < 0), -vec, vec)
        ps = p0 + vec * radius
        sx = ps[..., 0] / (ps[..., 2] * tan_v * aspect)
        sy = ps[..., 1] / (ps[..., 2] * tan_v)
        jx = np.floor((sx + 1) / 2 * W).astype(np.int64)
        jy = np.floor((1 - sy) / 2 * H).astype(np.int64) - top
        q = pos(jx, jy)
        sky = jy < 0
        v2 = q - p0
        a1 = np.maximum((v2 * n).sum(-1) - 0.002 * p0[..., 2], 0)
        a2 = (v2 * v2).sum(-1) + 0.0001
        ao += np.where(sky, 0, a1 / a2)
    fall = np.clip(1 - p0[..., 2] / falloff, 0, 1)
    ao = np.power(np.maximum(ao * radius * intensity * fall / samples, 0), 0.6)
    return np.clip(ao, 0, 1)


if __name__ == "__main__":
    main()
