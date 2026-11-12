"""The game's post-processing and colour maths, shared by the gamma and linear preview renders.

URP's post stack works in linear light in both colour spaces: in Gamma it decodes the frame from sRGB first. So a render
is turned into a frame the same way for both, and only the light that reaches the post stack differs.
"""
import numpy as np

LUMA = np.array([0.2126, 0.7152, 0.0722])


def lin(c):
    """sRGB to linear, as Unity's Color.linear (values over 1 follow the same curve)."""
    c = np.asarray(c, dtype=np.float64)
    a = np.abs(c)
    return np.sign(c) * np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)


def enc(c):
    """Linear to sRGB."""
    c = np.maximum(np.asarray(c, dtype=np.float64), 0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def aces(x):
    """ACES RRT and sRGB ODT, Stephen Hill's fit: close to URP's ACES tonemapping."""
    m1 = np.array([[0.59719, 0.35458, 0.04823], [0.07600, 0.90834, 0.01566], [0.02840, 0.13383, 0.83777]])
    m2 = np.array([[1.60475, -0.53108, -0.07367], [-0.10208, 1.10813, -0.00605], [-0.00327, -0.07276, 1.07602]])
    v = x @ m1.T
    v = (v * (v + 0.0245786) - 0.000090537) / (v * (0.983729 * v + 0.4329510) + 0.238081)
    return np.clip(v @ m2.T, 0, 1)


def frame(radiance, exposure=0.45, contrast=0.0, saturation=0.0, temperature=0.0, tint=0.0, bloom=None):
    """URP's post stack on linear radiance: exposure, white balance, contrast, saturation, then ACES; returns sRGB 0-1."""
    x = np.maximum(radiance, 0) * 2 ** exposure
    if bloom is not None: x = x + bloom
    t, g = temperature / 100, tint / 100
    x = x * np.array([1 + 0.12 * t + 0.03 * g, 1 - 0.06 * g, 1 - 0.12 * t + 0.03 * g])
    if contrast:
        lc = (np.log2(np.maximum(x, 1e-6)) + 9.72) / 17.52
        lc = (lc - 0.4135884) * (1 + contrast / 100) + 0.4135884
        x = 2 ** (lc * 17.52 - 9.72)
    if saturation:
        y = (x @ LUMA)[..., None]
        x = np.maximum(y + (x - y) * (1 + saturation / 100), 0)
    return enc(aces(x))


def bloom(radiance, exposure=0.45, threshold=1.2, intensity=0.5, scatter=0.65):
    """A rough stand-in for URP's bloom: the light over the threshold, spread wide."""
    from scipy.ndimage import gaussian_filter
    x = np.maximum(radiance, 0) * 2 ** exposure
    knee = np.maximum(x.max(-1, keepdims=True) - threshold, 0) / np.maximum(x.max(-1, keepdims=True), 1e-6)
    hot = x * knee
    h, w = x.shape[:2]
    out = np.zeros_like(x)
    for s, k in ((0.004, 0.4), (0.012, 0.3), (0.03, 0.2), (0.07, 0.1 + 0.2 * scatter)):
        out += k * np.stack([gaussian_filter(hot[..., c], s * w) for c in range(3)], -1)
    return out * intensity


def save(path, srgb):
    from PIL import Image
    Image.fromarray((np.clip(srgb, 0, 1) * 255 + 0.5).astype(np.uint8)).save(path)
