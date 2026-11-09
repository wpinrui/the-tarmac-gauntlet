"""Raised and separate parts of the Cheonma: wheels, wing, mirrors, lamps."""
import json
from pathlib import Path

import numpy as np

from body import ROWS, extrude_outline, _close

W = ROWS["wheel"]


def lathe(profile_ry, n=48):
    """Revolve a closed (r, y) profile around the y axis. Returns (verts, faces)."""
    prof = np.asarray(profile_ry, float)
    m = len(prof)
    verts = []
    for i in range(n):
        a = 2 * np.pi * i / n
        for r, y in prof:
            verts.append((r * np.cos(a), y, r * np.sin(a)))
    faces = []
    for i in range(n):
        a, b = i * m, ((i + 1) % n) * m
        for j in range(m):
            k = (j + 1) % m
            faces.append((a + j, a + k, b + k, b + j))
    return np.array(verts), faces


def tyre():
    R, w, rim = W["r"], W["width"], W["rim_r"]
    sh = 0.05
    prof = [(rim, -w / 2)]
    for t in np.linspace(0, np.pi / 2, 6):
        prof.append((R - sh + sh * np.sin(t), -w / 2 + sh - sh * np.cos(t)))
    for t in np.linspace(np.pi / 2, np.pi, 6):
        prof.append((R - sh + sh * np.sin(t), w / 2 - sh - sh * np.cos(t)))
    prof.append((rim, w / 2))
    return lathe(prof)


def rim_barrel():
    rim, w = W["rim_r"], W["width"]
    prof = [(0.07, -w / 2 + 0.04), (rim - 0.001, -w / 2 + 0.01), (rim - 0.001, w / 2 - 0.01),
            (rim - 0.012, w / 2 - 0.02), (rim - 0.012, -w / 2 + 0.06), (0.07, -w / 2 + 0.06)]
    return lathe(prof)


def hub():
    w = W["width"]
    prof = [(0.0, w / 2 - 0.06), (0.075, w / 2 - 0.06), (0.075, w / 2 - 0.03),
            (0.045, w / 2 - 0.015), (0.0, w / 2 - 0.015)]
    return lathe(prof, 24)


def spokes(n=12):
    """Straight spokes from hub to rim on the outer face."""
    rim, w = W["rim_r"], W["width"]
    verts, faces = [], []
    y0, y1 = w / 2 - 0.05, w / 2 - 0.025
    for i in range(n):
        a = 2 * np.pi * i / n
        d = np.array([np.cos(a), 0, np.sin(a)])
        s = np.array([-np.sin(a), 0, np.cos(a)])
        quad = []
        for r, hw in ((0.06, 0.016), (rim - 0.005, 0.009)):
            for side in (-1, 1):
                quad.append(d * r + s * hw * side)
        base = len(verts)
        for y in (y0, y1):
            for q in quad:
                verts.append(q + np.array([0, y, 0]))
        # box: 0 1 (inner) 2 3 (outer) at y0, +4 at y1
        b = base
        faces += [(b, b + 2, b + 3, b + 1), (b + 4, b + 5, b + 7, b + 6),
                  (b, b + 4, b + 6, b + 2), (b + 1, b + 3, b + 7, b + 5),
                  (b, b + 1, b + 5, b + 4), (b + 2, b + 6, b + 7, b + 3)]
    return np.array(verts), faces


def wheel_parts():
    """Every wheel piece at the origin with the outer face towards +y."""
    return {"tyre": (*tyre(), "tyre"), "rim": (*rim_barrel(), "rim"),
            "spokes": (*spokes(), "rim"), "hub": (*hub(), "hub")}


def airfoil(x_le, z_le, x_te, z_te, thick, n=14):
    c = np.array([x_te - x_le, z_te - z_le])
    L = np.linalg.norm(c)
    u = c / L
    nrm = np.array([-u[1], u[0]])
    t = (1 - np.cos(np.linspace(0, np.pi, n))) / 2
    half = 5 * thick * (0.2969 * np.sqrt(t) - 0.126 * t - 0.3516 * t ** 2 + 0.2843 * t ** 3 - 0.1036 * t ** 4)
    half = np.maximum(half, 0.002)
    camber = 0.06 * L * 4 * t * (1 - t)
    top = [np.array([x_le, z_le]) + u * L * ti + nrm * (cb + h) for ti, h, cb in zip(t, half, camber)]
    bot = [np.array([x_le, z_le]) + u * L * ti + nrm * (cb - h) for ti, h, cb in zip(t, half, camber)]
    return np.array(top + bot[::-1][1:-1])


def wing_parts():
    w = ROWS["wing"]
    prof = airfoil(w["x_le"], w["z_le"], w["x_te"], w["z_te"], w["thick"] / (w["x_le"] - w["x_te"]) * 1.0 * (w["x_le"] - w["x_te"]))
    out = {"wing": (*extrude_outline(prof, 0.0, w["half_span"]), "body")}
    ep = ROWS["endplate"]
    out["endplate_l"] = (*extrude_outline(ep["outline"], ep["y"], ep["half_t"]), "body")
    out["endplate_r"] = (*extrude_outline(ep["outline"], -ep["y"], ep["half_t"]), "body")
    py = ROWS["pylon"]
    out["pylon"] = (*extrude_outline(py["outline"], 0.0, py["half_t"]), "body")
    df = ROWS["diffuser_fins"]
    for i, y in enumerate(df["ys"]):
        out[f"diffuser_fin_{i}"] = (*extrude_outline(df["outline"], y, df["half_t"]), "carbon")
    return out


def ellipsoid_shell(cx, cy, cz, rx, ry, rz, cut=0.45, nring=24, nst=10):
    """Front half and a bit of an ellipsoid along x, cut flat at the back.
    Returns the shell and the back face loop for the glass."""
    rings = []
    t = np.linspace(-cut, 1.0, nst)
    for ti in t[:-1]:
        x = cx + rx * ti
        s = np.sqrt(max(1 - ti * ti, 1e-4))
        a = np.linspace(0, 2 * np.pi, nring, endpoint=False)
        rings.append(np.column_stack([np.full(nring, x), cy + ry * s * np.cos(a), cz + rz * s * np.sin(a)]))
    # small last ring near the tip
    a = np.linspace(0, 2 * np.pi, nring, endpoint=False)
    rings.append(np.column_stack([np.full(nring, cx + rx * 0.995), cy + ry * 0.1 * np.cos(a), cz + rz * 0.1 * np.sin(a)]))
    return _close(rings)


def mirror_parts():
    m = ROWS["mirror"]
    out = {}
    for side, sgn in (("l", 1), ("r", -1)):
        y = m["y"] * sgn
        out[f"mirror_{side}"] = (*ellipsoid_shell(m["x"], y, m["z"], m["rx"], m["ry"], m["rz"]), "body")
        # recessed glass on the flat back
        xb = m["x"] - m["rx"] * 0.45 + 0.005
        s = np.sqrt(1 - 0.45 ** 2) * 0.80
        a = np.linspace(0, 2 * np.pi, 24, endpoint=False)
        ring0 = np.column_stack([np.full(24, xb - 0.0045), y + m["ry"] * s * np.cos(a), m["z"] + m["rz"] * s * np.sin(a)])
        ring1 = ring0 + np.array([0.004, 0, 0])
        out[f"mirror_glass_{side}"] = (*_close([ring0, ring1]), "mirror_glass")
        # stalk
        a = np.linspace(0, 2 * np.pi, 12, endpoint=False)
        r = m["stalk_r"]
        ra = np.column_stack([m["x"] + r * np.cos(a), y + r * np.sin(a), np.full(12, m["stalk_z0"])])
        rb = ra.copy(); rb[:, 2] = m["z"]
        out[f"mirror_stalk_{side}"] = (*_close([ra, rb]), "body")
    return out


def disc(centre, normal, r, depth, n=24):
    """Short closed cylinder lying on a surface, pointing out along normal."""
    nrm = np.asarray(normal, float); nrm /= np.linalg.norm(nrm)
    a = np.array([0, 0, 1.0]) if abs(nrm[2]) < 0.9 else np.array([0, 1.0, 0])
    u = np.cross(nrm, a); u /= np.linalg.norm(u)
    v = np.cross(nrm, u)
    ang = np.linspace(0, 2 * np.pi, n, endpoint=False)
    ring = np.array([centre + r * (np.cos(t) * u + np.sin(t) * v) for t in ang])
    return _close([ring - nrm * depth / 2, ring + nrm * depth / 2])


def box(x0, x1, y0, y1, z0, z1):
    v = np.array([(x, y, z) for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)])
    f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    return v, f


def rear_lamp_parts():
    r = ROWS["rear_lamps"]
    x_back = ROWS["rear_box"][0]["x"]
    out = {}
    zm = (r["z0"] + r["z1"]) / 2
    for side, s in (("l", 1), ("r", -1)):
        y0, y1 = sorted((r["y0"] * s, r["y1"] * s))
        out[f"rear_housing_{side}"] = (*box(x_back - 0.012, x_back + 0.01, y0, y1, r["z0"], r["z1"]), "housing")
        yc = (y0 + y1) / 2
        out[f"lamp_tail_{side}"] = (*disc(np.array([x_back - 0.0165, yc + 0.05 * s, zm]), (-1, 0, 0), 0.026, 0.008), "lamp_tail")
        out[f"lamp_brake_{side}"] = (*disc(np.array([x_back - 0.0165, yc - 0.05 * s, zm]), (-1, 0, 0), 0.026, 0.008), "lamp_brake")
    hw = r["rain_hw"]
    out["rain_housing"] = (*box(x_back - 0.012, x_back + 0.01, -hw, hw, r["z0"] + 0.01, r["z1"] - 0.005), "housing")
    out["lamp_rain"] = (*box(x_back - 0.0185, x_back - 0.0125, -hw + 0.012, hw - 0.012, r["z0"] + 0.02, r["z1"] - 0.015), "lamp_rain")
    return out
