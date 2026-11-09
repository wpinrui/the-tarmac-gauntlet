"""Body shell as a smooth visual hull.

Every reference view gives a signed distance field: how far a point's
projection is outside that view's silhouette, in metres at the point's depth.
The body is the smooth intersection of all six, so the creases where two
silhouettes meet come out rounded instead of sharp.
"""
import numpy as np
from scipy import ndimage as ndi
from skimage import measure
import trimesh
import cams as C
from masks import silhouette
from hull import Grid

STEP = 0.01
WHEEL_INNER_Y = 0.52  # inboard of this the side outline keeps the body under the arches


def poly_cut(mask, poly):
    """Clear the inside of a polygon from a mask."""
    from skimage.draw import polygon
    rr, cc = polygon([p[1] for p in poly], [p[0] for p in poly], mask.shape)
    mask = mask.copy()
    mask[rr, cc] = False
    return mask


def body_masks(wheels_px=None):
    """Silhouettes with the mirrors taken off, plus a side outline without
    the wheels for the outboard part of the body."""
    m = {n: silhouette(n) for n in C.NAMES}
    m['front'] = poly_cut(m['front'], [(0, 170), (168, 176), (126, 248), (0, 270)])
    m['front'] = poly_cut(m['front'], [(805, 170), (655, 176), (698, 248), (805, 270)])
    m['top'] = poly_cut(m['top'], [(720, 0), (920, 0), (920, 90), (720, 90)])
    m['top'] = poly_cut(m['top'], [(720, 662), (920, 662), (920, 774), (720, 774)])
    return m


def signed_px(mask):
    """Signed distance to the silhouette edge in pixels, negative inside."""
    inside = ndi.distance_transform_edt(mask)
    outside = ndi.distance_transform_edt(~mask)
    return np.where(mask, -(inside - 0.5), outside - 0.5).astype(np.float32)


def sample(img, u, v):
    return ndi.map_coordinates(img, [v, u], order=1, mode='nearest')


def view_field(params, sd, P):
    h, w = sd.shape
    u, v, z = C.project(params, P, (w, h))
    d = sample(sd, u, v)
    # outside the image counts as outside by the distance to the frame
    du = np.maximum(np.maximum(-u, u - (w - 1)), 0)
    dv = np.maximum(np.maximum(-v, v - (h - 1)), 0)
    d = d + np.hypot(du, dv)
    return d * z / params[7]


def smax(a, b, k):
    h = np.clip(k - np.abs(a - b), 0, None) / np.maximum(k, 1e-6)
    return np.maximum(a, b) + h * h * k * 0.25


def mesh(field, g, level=0.0):
    pad = np.pad(field, 1, constant_values=field.max())
    v, f, n, _ = measure.marching_cubes(pad, level, spacing=(g.step,) * 3)
    v = v - g.step + g.lo
    m = trimesh.Trimesh(v, f[:, ::-1], process=True)
    if m.volume < 0:
        m.invert()
    return m


SIDE_WHEELS_PX = [(270.0, 432.0), (1246.0, 433.0)]
ARCH_R_PX = 131.0


def fields(cams, g, k=0.04):
    """Smooth-hull field on grid g, metres, negative inside."""
    from skimage.draw import disk
    m = body_masks()
    sd = {n: signed_px(m[n]) for n in C.NAMES}
    outboard = m['side'].copy()
    for (u, v) in SIDE_WHEELS_PX:
        rr, cc = disk((v, u), ARCH_R_PX, shape=outboard.shape)
        outboard[rr, cc] = False
    sd_out = signed_px(outboard)
    nx, ny, nz = g.shape
    F = np.empty(g.shape, np.float32)
    ys = g.lo[1] + np.arange(ny) * g.step
    zs = g.lo[2] + np.arange(nz) * g.step
    Yg, Zg = np.meshgrid(ys, zs, indexing='ij')
    for i in range(nx):
        x = g.lo[0] + i * g.step
        P = np.c_[np.full(Yg.size, x), Yg.ravel(), Zg.ravel()]
        f = None
        for n in C.NAMES:
            d = view_field(cams[n], sd[n], P)
            if n == 'side':
                d_out = view_field(cams[n], sd_out, P)
                w = np.clip((np.abs(P[:, 1]) - WHEEL_INNER_Y) / 0.03, 0, 1)
                d = np.maximum(d, w * d_out + (1 - w) * d) if False else (1 - w) * d + w * np.maximum(d, d_out)
            f = d if f is None else smax(f, d, k)
        F[i] = f.reshape(ny, nz)
    return F


def round_ends(F, g, n_of_x, k=0.03):
    """Carve each x section toward a superellipse that fits its bounding box.
    n large leaves the section alone; n near 2 rounds the corners hard."""
    out = F.copy()
    ys = g.lo[1] + np.arange(g.shape[1]) * g.step
    zs = g.lo[2] + np.arange(g.shape[2]) * g.step
    Y, Z = np.meshgrid(ys, zs, indexing='ij')
    for i in range(g.shape[0]):
        x = g.lo[0] + i * g.step
        n = n_of_x(x)
        if n >= 12:
            continue
        sec = F[i] < 0
        if not sec.any():
            continue
        jj, kk = np.nonzero(sec)
        w = ys[jj].max() + 0.5 * g.step
        z0, z1 = zs[kk].min() - 0.5 * g.step, zs[kk].max() + 0.5 * g.step
        zc, h = (z0 + z1) / 2, (z1 - z0) / 2
        r = (np.abs(Y / w) ** n + np.abs((Z - zc) / h) ** n) ** (1 / n)
        d = (r - 1) * min(w, h)
        out[i] = smax(F[i], d, k)
    return out
