"""Pinhole cameras for the reference renders.

World frame while building: X forward, Y to the car's left, Z up, metres,
origin on the ground at mid wheelbase.

A camera is 8 numbers: yaw, pitch, roll (degrees), target x, y, z (metres),
distance (metres) and focal length (pixels). The target lands on the image
centre. Yaw 0 looks along -X (at the nose), yaw 90 looks along -Y (at the
car's left side).
"""
import json
import numpy as np

NAMES = ['side', 'top', 'front', 'rear', 'three_quarter_front', 'three_quarter_rear']


def rot(yaw, pitch, roll):
    y, p, r = np.radians([yaw, pitch, roll])
    # forward: from camera toward target
    fwd = np.array([-np.cos(p) * np.cos(y), -np.cos(p) * np.sin(y), -np.sin(p)])
    up0 = np.array([0.0, 0.0, 1.0])
    if abs(fwd @ up0) > 0.999:
        up0 = np.array([-np.cos(y), -np.sin(y), 0.0])
    right = np.cross(fwd, up0)
    right /= np.linalg.norm(right)
    up = np.cross(right, fwd)
    c, s = np.cos(r), np.sin(r)
    right, up = c * right + s * up, -s * right + c * up
    return right, up, fwd


def project(params, P, size):
    """P: (N,3) world points. Returns (u, v, depth) in pixels."""
    yaw, pitch, roll, tx, ty, tz, dist, f = params
    right, up, fwd = rot(yaw, pitch, roll)
    T = np.array([tx, ty, tz])
    C = T - dist * fwd
    d = P - C
    zc = d @ fwd
    u = size[0] / 2 + f * (d @ right) / zc
    v = size[1] / 2 - f * (d @ up) / zc
    return u, v, zc


def ray_dirs(params, uv, size):
    yaw, pitch, roll, tx, ty, tz, dist, f = params
    right, up, fwd = rot(yaw, pitch, roll)
    x = (uv[:, 0] - size[0] / 2) / f
    y = -(uv[:, 1] - size[1] / 2) / f
    d = fwd + x[:, None] * right + y[:, None] * up
    return d / np.linalg.norm(d, axis=1, keepdims=True)


def centre(params):
    yaw, pitch, roll, tx, ty, tz, dist, f = params
    _, _, fwd = rot(yaw, pitch, roll)
    return np.array([tx, ty, tz]) - dist * fwd


def save(path, cams):
    with open(path, 'w') as fh:
        json.dump({k: [round(float(x), 5) for x in v] for k, v in cams.items()}, fh, indent=1)


def load(path):
    with open(path) as fh:
        return {k: np.array(v) for k, v in json.load(fh).items()}
