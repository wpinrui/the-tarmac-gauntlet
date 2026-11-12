"""The race sky at an hour, as SkyClock.cs, SkyGrade.cs and Sky.shader work it out, and LinearLight.cs's matching."""
import math

import numpy as np

from post import lin, LUMA

LOOK = dict(DZ=(0.26, 0.47, 0.76), DH=(0.78, 0.84, 0.89), TZ=(0.10, 0.16, 0.36), TH=(0.55, 0.45, 0.48),
            NZ=(0.02, 0.035, 0.08), NH=(0.05, 0.07, 0.12), SS=(1, 0.48, 0.18))
LOOK = {k: np.array(v) for k, v in LOOK.items()}
MOON = np.array([0.62, 0.72, 1.0])
GROUND_TINT = np.array([0.36, 0.36, 0.24])


def sm(a, b, x):
    t = min(max((x - a) / (b - a), 0.0), 1.0)
    return t * t * (3 - 2 * t)


def direction(hour, dec):
    """Unity frame: x east, y up, z north."""
    h = (hour - 12.3) * 15 * math.pi / 180
    phi, d = math.radians(50), math.radians(dec)
    east = -math.cos(d) * math.sin(h)
    north = math.sin(d) * math.cos(phi) - math.cos(d) * math.cos(h) * math.sin(phi)
    up = math.sin(d) * math.sin(phi) + math.cos(d) * math.cos(h) * math.cos(phi)
    return np.array([east, up, north])


def sun_colour(e):
    a, b, c, d = map(np.array, [(1, .42, .18), (1, .68, .42), (1, .88, .72), (1, .97, .92)])
    if e < 5: return a + (b - a) * min(max(e / 5, 0), 1)
    if e < 15: return b + (c - b) * (e - 5) / 10
    return c + (d - c) * min(max((e - 15) / 25, 0), 1)


def at(hour):
    s = {}
    s["sun_dir"] = direction(hour, -12)
    s["moon_dir"] = direction(hour - 12, 12)
    e = math.degrees(math.asin(max(min(s["sun_dir"][1], 1), -1)))
    me = math.degrees(math.asin(max(min(s["moon_dir"][1], 1), -1)))
    s["e"] = e
    sun = 1.25 * sm(-1.5, 4, e) * (0.35 + 0.65 * sm(0, 35, e))
    moon = 0.18 * (1 - sm(-8, -2, e)) * sm(-2, 8, me)
    if sun > 0 or e > -2:
        s["light_dir"], s["light"], s["colour"] = s["sun_dir"], sun, sun_colour(e)
    else:
        s["light_dir"], s["light"], s["colour"] = s["moon_dir"], moon, MOON
    tw, day = sm(-16, -5, e), sm(-5, 12, e)
    mix = lambda a, b, t: a + (b - a) * t
    s["zenith"] = mix(mix(LOOK["NZ"], LOOK["TZ"], tw), LOOK["DZ"], day)
    s["horizon"] = mix(mix(LOOK["NH"], LOOK["TH"], tw), LOOK["DH"], day)
    s["glow"] = LOOK["SS"] * (sm(-8, -1, e) * (1 - sm(4, 20, e)))
    s["amb_sky"] = s["zenith"] * 0.4 + s["horizon"] * 0.6
    s["amb_eq"] = s["horizon"] * 0.72 + s["glow"] * 0.25
    s["amb_ground"] = s["horizon"] * GROUND_TINT
    s["fog"] = s["horizon"] * 0.95 + s["glow"] * 0.15
    s["night"] = 1 - sm(-12, -4, e)
    s["haze"] = 6000 * (1 - 0.45 * misty(hour))
    s["density"] = 3.912 / s["haze"]
    return s


def misty(hour):
    d = ((hour - 7.6 + 12) % 24 - 12) / 2.5
    return math.exp(-d * d)


def golden(e):
    """SkyGrade.Golden under a clear sky."""
    return sm(-2, 3, e) * (1 - sm(6, 18, e))


def grade(s, night_exposure=None):
    e = s["e"]
    golden_ = golden(e)
    blue = sm(-8, -3, e) * (1 - sm(-1, 2, e))
    night = s["night"]
    return dict(exposure=0.45 + (EXPOSURE_NIGHT if night_exposure is None else night_exposure) * night, contrast=8 + 4 * golden_ + 6 * night,
                saturation=6 + 10 * golden_ - 6 * blue - 22 * night, temperature=14 * golden_ - 10 * blue - 8 * night,
                tint=3 * golden_ - 2 * blue)


# SkyGrade's night exposure: 1, and 0.25 while the game drew in Gamma.
EXPOSURE_NIGHT = 1.0
EXPOSURE_NIGHT_GAMMA = 0.25
ALBEDO = 0.35  # LinearLight.Albedo


def ambient(c):
    """LinearLight.Ambient, as linear light."""
    return lin(np.asarray(c) * ALBEDO) / lin(ALBEDO)


def match(colour, gamma_intensity, ambient, facing):
    """LinearLight.Match."""
    if gamma_intensity <= 0: return 0.0
    facing = min(max(facing, 0.05), 1)
    a = lin(np.asarray(ambient) * ALBEDO) @ LUMA
    want = (lin((np.asarray(colour) * gamma_intensity * facing + ambient) * ALBEDO) @ LUMA - a) / lin(ALBEDO)
    per = facing * (lin(colour) @ LUMA)
    return max(want, 0) / per if per > 1e-6 else 0.0


def sky(s, dirs):
    """Sky.shader without _SKY_DETAIL or clouds, in sRGB numbers (as Gamma drew it): dirs is (..., 3) in Unity's frame."""
    up = dirs[..., 1]
    air = np.exp(-np.maximum(up, 0) * 3.2)[..., None]
    col = s["zenith"] + (s["horizon"] - s["zenith"]) * air
    h = 1 - np.clip(up, 0, 1)
    dh = dirs[..., [0, 2]]
    dh = dh / np.maximum(np.linalg.norm(dh, axis=-1, keepdims=True), 1e-5)
    sh = s["sun_dir"][[0, 2]] / max(np.linalg.norm(s["sun_dir"][[0, 2]]), 1e-5)
    towards = ((dh @ sh) * 0.5 + 0.5)[..., None]
    band = (h ** 6)[..., None]
    haze = np.exp(-np.abs(up) * 10)[..., None]
    col = col + s["glow"] * towards ** 3 * band
    col = col + (s["fog"] - col) * haze * 0.85
    col = col + (s["amb_ground"] - col) * (np.clip(-up * 6, 0, 1)[..., None] * (1 - haze * 0.6))
    cs = dirs @ s["sun_dir"]
    above = min(max(s["sun_dir"][1] * 20 + 1, 0), 1)
    sun_c = s["colour"] if s["e"] > -2 else np.zeros(3)
    col = col + sun_c * ((np.clip(cs, 0, 1) ** 12 * 0.18 + np.clip(cs, 0, 1) ** 400 * 0.6) * above)[..., None]
    disc = np.clip((cs - 0.99985) / (0.99992 - 0.99985), 0, 1) * above
    col = col + sun_c * 3.0 * disc[..., None]
    cm = dirs @ s["moon_dir"]
    moon = np.clip((cm - 0.9999) / 0.00004, 0, 1) * min(max(s["moon_dir"][1] * 20, 0), 1)
    col = col + np.array([0.85, 0.88, 0.95]) * (moon * s["night"])[..., None]
    return col
