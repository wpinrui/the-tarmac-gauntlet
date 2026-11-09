"""Cast image-space feature masks onto the shell as thin conforming panels.

Each mask is triangulated in pixel space, every vertex is shot along its
camera ray onto the shell, and the panel is lifted a few millimetres off the
surface. The car's left half is kept and mirrored, so every panel is
symmetric.
"""
import numpy as np
import trimesh
from scipy.spatial import Delaunay
from skimage import measure
import cams as C

LIFT = 0.004


def mask_mesh(mask, step=5, tol=0.6):
    """2D triangulation covering a mask, edges following its outline."""
    pad = np.pad(mask, 1).astype(float)
    pts = []
    for c in measure.find_contours(pad, 0.5):
        c = measure.approximate_polygon(c, tol)
        # densify long edges
        for a, b in zip(c[:-1], c[1:]):
            n = max(1, int(np.ceil(np.linalg.norm(b - a) / step)))
            for t in np.arange(n) / n:
                pts.append(a + (b - a) * t)
    vv, uu = np.mgrid[0:mask.shape[0]:step, 0:mask.shape[1]:step]
    inner = mask[vv, uu]
    from scipy import ndimage as ndi
    core = ndi.binary_erosion(mask, iterations=2)
    inner = core[vv, uu]
    pts = np.array(pts) - 1  # undo pad, (row, col)
    grid = np.c_[vv[inner], uu[inner]]
    P = np.vstack([pts, grid]) if len(pts) else grid
    P = np.unique(np.round(P, 2), axis=0)
    tri = Delaunay(P)
    cen = P[tri.simplices].mean(1)
    r = np.clip(np.round(cen[:, 0]).astype(int), 0, mask.shape[0] - 1)
    c = np.clip(np.round(cen[:, 1]).astype(int), 0, mask.shape[1] - 1)
    keep = mask[r, c]
    uv = P[:, ::-1]  # (u, v)
    return uv, tri.simplices[keep]


def cast(shell, params, size, uv, faces, lift=LIFT, min_facing=0.0):
    origin = C.centre(params)
    dirs = C.ray_dirs(params, uv, size)
    locs, ray_idx, tri_idx = shell.ray.intersects_location(np.repeat(origin[None], len(uv), 0), dirs,
                                                          multiple_hits=False)
    hit = np.full((len(uv), 3), np.nan)
    hit[ray_idx] = locs
    nrm = np.full((len(uv), 3), np.nan)
    nrm[ray_idx] = shell.face_normals[tri_idx]
    good = ~np.isnan(hit[:, 0])
    if min_facing > 0:
        facing = np.full(len(uv), -1.0)
        facing[ray_idx] = -(shell.face_normals[tri_idx] * dirs[ray_idx]).sum(1)
        good &= facing >= min_facing
    V = hit + lift * nrm
    F = faces[good[faces].all(1)]
    m = trimesh.Trimesh(np.nan_to_num(V), F, process=False)
    m.remove_unreferenced_vertices()
    return m


def symmetric(m):
    """Keep the y >= 0 half (cut cleanly) and mirror it."""
    if len(m.faces) == 0:
        return m
    half = trimesh.intersections.slice_mesh_plane(m, plane_normal=[0, 1, 0], plane_origin=[0, 0, 0])
    if half is None or len(half.faces) == 0:
        return m
    mir = half.copy()
    mir.vertices[:, 1] *= -1
    mir.faces = mir.faces[:, ::-1]
    return trimesh.util.concatenate([half, mir])


def decal(shell, cams, view, mask, lift=LIFT, step=5, min_facing=0.0):
    size = mask.shape[::-1]
    uv, faces = mask_mesh(mask, step=step)
    m = cast(shell, cams[view], size, uv, faces, lift, min_facing)
    if view == 'side':
        mir = m.copy()
        mir.vertices[:, 1] *= -1
        mir.faces = mir.faces[:, ::-1]
        return trimesh.util.concatenate([m, mir])
    return symmetric(m)
