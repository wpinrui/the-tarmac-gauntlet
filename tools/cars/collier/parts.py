"""Separate parts: wheels, mirrors, roof spoiler, exhaust, grille bars."""
import numpy as np
import trimesh
from trimesh.transformations import rotation_matrix
import profiles as P

TRACK_Y = 0.755      # wheel centre plane, either side
TYRE_W = 0.215
RIM_R = 0.266
TYRE_R = P.TYRE_R


def lathe(profile, sections=72):
    """Revolve (r, y) points about the Y axis; returns a mesh in the XZ plane."""
    prof = np.asarray(profile, float)
    a = np.linspace(0, 2 * np.pi, sections, endpoint=False)
    V = []
    for t in a:
        V.append(np.c_[prof[:, 0] * np.cos(t), prof[:, 1], prof[:, 0] * np.sin(t)])
    V = np.vstack(V)
    n = len(prof)
    F = []
    for i in range(sections):
        i1 = (i + 1) % sections
        for j in range(n - 1):
            a0, a1, b0, b1 = i * n + j, i * n + j + 1, i1 * n + j, i1 * n + j + 1
            F += [(a0, b0, b1), (a0, b1, a1)]
    m = trimesh.Trimesh(V, F, process=True)
    m.fix_normals()
    return m


def tyre():
    r, w, rr = TYRE_R, TYRE_W / 2, RIM_R
    prof = [(rr, -w * 0.92), (rr + 0.012, -w), (r - 0.03, -w * 1.02), (r - 0.008, -w * 0.9), (r, -w * 0.6),
            (r, w * 0.6), (r - 0.008, w * 0.9), (r - 0.03, w * 1.02), (rr + 0.012, w), (rr, w * 0.92),
            (rr - 0.004, w * 0.92), (rr - 0.004, -w * 0.92), (rr, -w * 0.92)]
    return lathe(prof, 96)


def rim_parts():
    """Rim face at local +Y (outboard). Returns dict name -> mesh."""
    w = TYRE_W / 2
    out = {}
    lip = lathe([(RIM_R - 0.006, w * 0.80), (RIM_R + 0.004, w * 0.86), (RIM_R + 0.004, w * 0.93),
                 (RIM_R - 0.022, w * 0.93), (RIM_R - 0.022, w * 0.70), (RIM_R - 0.006, w * 0.80)], 96)
    barrel = lathe([(RIM_R - 0.012, w * 0.75), (RIM_R - 0.012, -w * 0.9), (RIM_R - 0.03, -w * 0.9),
                    (RIM_R - 0.03, w * 0.70)], 64)
    hub = lathe([(0.0, w * 0.86), (0.05, w * 0.86), (0.075, w * 0.80), (0.085, w * 0.55), (0.0, w * 0.55)], 48)
    spokes = []
    n = 10
    for i in range(n):
        t = 2 * np.pi * i / n
        L = RIM_R - 0.02 - 0.07
        # tapered spoke: wide at the rim, narrow at the hub, dished inwards
        s = trimesh.creation.box(extents=[L, 0.024, 0.026])
        v = s.vertices
        frac = (v[:, 0] + L / 2) / L
        v[:, 2] *= 0.75 + 0.6 * frac
        v[:, 1] += w * 0.80 - 0.03 * (1 - frac) * 0 - 0.012 * frac
        v[:, 0] += 0.07 + L / 2
        s.vertices = v
        s.apply_transform(rotation_matrix(t, [0, 1, 0]))
        spokes.append(s)
    nuts = []
    for i in range(4):
        t = 2 * np.pi * i / 4 + np.pi / 4
        c = trimesh.creation.cylinder(radius=0.011, height=0.02, sections=6)
        c.apply_transform(rotation_matrix(np.pi / 2, [1, 0, 0]))
        c.apply_translation([0.045 * np.cos(t), w * 0.88, 0.045 * np.sin(t)])
        nuts.append(c)
    cap = trimesh.creation.cylinder(radius=0.028, height=0.012, sections=32)
    cap.apply_transform(rotation_matrix(np.pi / 2, [1, 0, 0]))
    cap.apply_translation([0, w * 0.9, 0])
    disc = lathe([(0.17, -w * 0.1), (0.17, w * 0.2), (0.07, w * 0.2), (0.07, -w * 0.1)], 48)
    out['rim'] = trimesh.util.concatenate([lip, hub] + spokes)
    out['rim_inner'] = barrel
    out['nuts'] = trimesh.util.concatenate(nuts + [cap])
    out['brake'] = disc
    return out


def wheels():
    """All four wheels placed. dict material -> mesh."""
    t = tyre()
    rp = rim_parts()
    parts = {'tyre': [], 'rim': [], 'rim_inner': [], 'nuts': [], 'brake': []}
    for sx in (1, -1):
        for sy in (1, -1):
            M = np.eye(4)
            if sy < 0:
                M = rotation_matrix(np.pi, [0, 0, 1])
            M = trimesh.transformations.translation_matrix([sx * P.WHEEL_X, sy * TRACK_Y, P.WHEEL_Z]) @ M
            for k, m in [('tyre', t)] + list(rp.items()):
                c = m.copy()
                c.apply_transform(M)
                parts[k].append(c)
    return {k: trimesh.util.concatenate(v) for k, v in parts.items()}


def ellipsoid(c, r, sub=3):
    s = trimesh.creation.icosphere(subdivisions=sub)
    s.vertices *= r
    s.vertices += c
    return s


def mirrors():
    """Door mirrors: body-colour cap, black base, mirror glass. dict material -> mesh."""
    out = {'paint': [], 'trim': [], 'mirror': []}
    for sy in (1, -1):
        cap = trimesh.creation.icosphere(subdivisions=4)
        v = cap.vertices
        v = np.sign(v) * np.abs(v) ** 0.6  # squarer, superellipsoid-like
        v *= [0.085, 0.105, 0.060]
        # nose tapers forward and down, the back is cut flat for the glass
        fwd = np.clip(v[:, 0] / 0.085, 0, 1)
        v[:, 2] *= 1 - 0.25 * fwd
        v[:, 1] *= 1 - 0.15 * fwd
        v[:, 0] = np.maximum(v[:, 0], -0.04)
        cap.vertices = v
        cap.apply_translation([0.66, 0.895, 0.985])
        glass = trimesh.creation.box(extents=[0.004, 0.18, 0.10])
        glass.apply_translation([0.66 - 0.041, 0.895, 0.985])
        base = trimesh.creation.box(extents=[0.10, 0.12, 0.035])
        base.apply_translation([0.665, 0.80, 0.935])
        for k, m in (('paint', cap), ('mirror', glass), ('trim', base)):
            if sy < 0:
                m = m.copy()
                m.vertices[:, 1] *= -1
                m.faces = m.faces[:, ::-1]
            out[k].append(m)
    return {k: trimesh.util.concatenate(v) for k, v in out.items()}


def spoiler():
    """Roof spoiler: an airfoil lofted across the car with rounded tips."""
    half = 0.57
    ys = np.linspace(-half, half, 41)
    chord = lambda y: 0.25 - 0.03 * (np.abs(y) / half) ** 4
    secs = []
    for y in ys:
        k = np.sqrt(max(1 - (abs(y) / (half + 0.004)) ** 8, 0.05))
        c = chord(y)
        t = np.linspace(0, np.pi, 14)
        xs = -1.13 - c * (1 - np.cos(t)) / 2
        top = 1.372 - 0.012 * ((xs + 1.13) / -c) ** 1.5
        th = 0.032 * k * np.sin(t) ** 0.7 + 0.004
        up = np.c_[xs, np.full_like(xs, y), top]
        lo = np.c_[xs, np.full_like(xs, y), top - th]
        ring = np.vstack([up, lo[::-1][1:-1]])
        secs.append(ring)
    n = len(secs[0])
    V = np.vstack(secs)
    F = []
    for i in range(len(secs) - 1):
        for j in range(n):
            j1 = (j + 1) % n
            a, b, c, d = i * n + j, i * n + j1, (i + 1) * n + j1, (i + 1) * n + j
            F += [(a, b, c), (a, c, d)]
    for i, flip in ((0, False), (len(secs) - 1, True)):
        cidx = len(V)
        V = np.vstack([V, secs[i].mean(0)])
        for j in range(n):
            f = (i * n + j, i * n + (j + 1) % n, cidx)
            F.append(f[::-1] if flip else f)
    m = trimesh.Trimesh(V, F, process=True)
    m.fix_normals()
    return m


def exhaust():
    """Twin oval tips under the rear bumper, car's right."""
    tips = []
    for y in (-0.275, -0.36):
        c = trimesh.creation.cylinder(radius=1.0, height=0.16, sections=32)
        c.apply_transform(rotation_matrix(np.pi / 2, [0, 1, 0]))
        c.vertices[:, 1] *= 0.040
        c.vertices[:, 2] *= 0.028
        c.apply_translation([-1.88, y, 0.165])
        inner = trimesh.creation.cylinder(radius=1.0, height=0.02, sections=32)
        inner.apply_transform(rotation_matrix(np.pi / 2, [0, 1, 0]))
        inner.vertices[:, 1] *= 0.033
        inner.vertices[:, 2] *= 0.021
        inner.apply_translation([-1.955, y, 0.165])
        tips.append((c, inner))
    return {'chrome': trimesh.util.concatenate([t[0] for t in tips]),
            'soot': trimesh.util.concatenate([t[1] for t in tips])}
