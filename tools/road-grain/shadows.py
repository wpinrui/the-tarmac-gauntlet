"""ScreenShadows.shader, ported line by line, over the bonnet camera's view, to find the dotted band ahead of the car.

The scene: the car (body and cabin, as boxes, with its bonnet sloping to the nose), the flat road and grass under it.
The camera sits where CarCameraSpots and CameraPresets.Bonnet put it: on the centreline just ahead of the windscreen,
a little above the bonnet, 70 degree field of view, tilted 3 degrees down. The depth buffer holds what the camera sees,
the bonnet included. The sun's shadow map holds the road and the car, pushed back by URP's caster biases, cascade by
cascade, its texels snapped to the light's grid. Each pixel then runs the shader's Fragment: SoftShadow (the blocker
search and the filter, turned by the per-pixel noise), the cascade blend, and ContactShadow (the march towards the sun
through the depth buffer, point reads). The result is lit simply: sun times the screen shadow, plus the ambient.

  python3 shadows.py out.png [key=value ...]
    quality=low|medium|high|ultra   hour=15   heading=0 (degrees, the car's yaw; the sun's azimuth is fixed by hour)
    overcast=0..1 (the sun's shadow strength drops as CloudMap.Strength, the contact shadow's does not)
    fix=0|1 (the fixed shader)   frame=0 (the frame index the fixed shader turns its noise by)
    show=lit|screen|contact|soft
"""
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "linear-preview"))
import sky as skyclock  # noqa: E402
from post import lin, frame, save  # noqa: E402

# QualityLevels.Shadows: spread, filter taps, search taps, widest texels, contact reach, steps, far, strength, blend.
# Then the pipeline asset: render scale, shadow distance, cascades and their splits, map size, depth and normal bias,
# and URP's soft shadow kernel radius for its soft shadow quality (Low 1.5, Medium 2.5, High 3.5).
LEVELS = {
    "low": ((0.006, 8, 8, 6.0, 0.3, 6, 40.0, 0.8, 0.0), 0.67, 150, [1.0], 1024, 1.0, 1.0, 1.5),
    "medium": ((0.007, 12, 12, 10.0, 0.35, 8, 60.0, 0.85, 0.1), 0.8, 250, [0.2, 1.0], 2048, 1.0, 1.0, 2.5),
    "high": ((0.008, 16, 16, 16.0, 0.4, 12, 80.0, 0.9, 0.12), 1.0, 500, [0.05, 0.16, 0.4, 1.0], 4096, 0.75, 0.6, 3.5),
    "ultra": ((0.008, 32, 24, 24.0, 0.5, 20, 120.0, 0.9, 0.15), 1.0, 600, [0.045, 0.14, 0.36, 1.0], 4096, 0.6, 0.5, 3.5),
}
DISC = np.array([(-0.94201624, -0.39906216), (0.94558609, -0.76890725), (-0.09418410, -0.92938870),
                 (0.34495938, 0.29387760), (-0.91588581, 0.45771432), (-0.81544232, -0.87912464),
                 (-0.38277543, 0.27676845), (0.97484398, 0.75648379), (0.44323325, -0.97511554),
                 (0.53742981, -0.47373420), (-0.26496911, -0.41893023), (0.79197514, 0.19090188),
                 (-0.24188840, 0.99706507), (-0.81409955, 0.91437590), (0.19984126, 0.78641367),
                 (0.14383161, -0.14100790)])
NEAR = 0.1
# The car, in its own frame (x right, y up, z forward): body and cabin boxes (CarBuilder's box, with a cabin), and the
# bonnet from the windscreen's base down to the nose.
BODY = (np.array([-0.925, 0.25, -2.25]), np.array([0.925, 0.85, 2.25]))
CABIN = (np.array([-0.75, 0.85, -1.7]), np.array([0.75, 1.38, 0.35]))
BONNET = ((0.35, 1.05), (2.25, 0.6))  # (z, y) at the windscreen's base and at the nose
CAMERA = np.array([0.0, 1.25, 0.55])
FOV, TILT = 70.0, -3.0
DEFAULTS = dict(quality="high", hour=15.0, heading=0.0, overcast=0.0, fix=0, frame=0, show="lit", width=1920, height=1080, jx=0.0, jy=0.0, frames=1)


def options():
    o = dict(DEFAULTS)
    for a in sys.argv[2:]:
        k, v = a.split("=", 1)
        o[k] = v if isinstance(DEFAULTS[k], str) else type(DEFAULTS[k])(v)
    return o


class View:
    def __init__(self, w, h):
        self.w, self.h = w, h
        t = math.radians(TILT)
        self.fwd = np.array([0.0, math.sin(t), math.cos(t)])
        self.up = np.array([0.0, math.cos(t), -math.sin(t)])
        self.right = np.array([1.0, 0.0, 0.0])
        self.tan = math.tan(math.radians(FOV) / 2)
        self.aspect = w / h

    def rays(self, px, py):
        sx = (px / self.w * 2 - 1) * self.tan * self.aspect
        sy = (1 - py / self.h * 2) * self.tan
        d = self.fwd + sx[..., None] * self.right + sy[..., None] * self.up
        return d / np.linalg.norm(d, axis=-1, keepdims=True)

    def project(self, p):
        """Screen pixel coordinates (y down) and eye depth."""
        rel = p - CAMERA
        z = rel @ self.fwd
        sx = (rel @ self.right) / (z * self.tan * self.aspect)
        sy = (rel @ self.up) / (z * self.tan)
        return (sx + 1) / 2 * self.w, (1 - sy) / 2 * self.h, z


def box_hit(o, d, lo, hi):
    inv = 1 / np.where(np.abs(d) < 1e-12, 1e-12, d)
    t0, t1 = (lo - o) * inv, (hi - o) * inv
    tn = np.minimum(t0, t1).max(-1)
    tf = np.maximum(t0, t1).min(-1)
    return np.where((tf >= np.maximum(tn, 0)) & (tf > 0), np.maximum(tn, 0), np.inf)


def bonnet_hit(o, d):
    """The bonnet's top: a plane through the windscreen's base and the nose, across the body's width."""
    (z0, y0), (z1, y1) = BONNET
    n = np.array([0.0, z1 - z0, -(y1 - y0)])
    n /= np.linalg.norm(n)
    p0 = np.array([0.0, y0, z0])
    den = d @ n
    t = ((p0 - o) @ n) / np.where(np.abs(den) < 1e-9, 1e-9, den)
    p = o + d * t[..., None]
    ok = (t > 0) & (np.abs(p[..., 0]) <= BODY[1][0]) & (p[..., 2] >= z0) & (p[..., 2] <= z1)
    return np.where(ok, t, np.inf)


def scene_depth(view, jx=0.0, jy=0.0, frames=1):
    """What the camera sees: eye depth per pixel (the depth buffer), its world position, and what it is."""
    ys, xs = np.mgrid[0:view.h, 0:view.w].astype(np.float64) + 0.5
    d = view.rays(xs + jx, ys + jy)
    o = np.broadcast_to(CAMERA, d.shape)
    t_ground = np.where(d[..., 1] < -1e-9, -CAMERA[1] / np.where(d[..., 1] < -1e-9, d[..., 1], -1), np.inf)
    t_car = bonnet_hit(o, d)
    t = np.minimum(t_ground, t_car)
    p = o + d * np.where(np.isfinite(t), t, 0)[..., None]
    depth = np.where(np.isfinite(t), (p - CAMERA) @ view.fwd, np.inf)
    kind = np.where(~np.isfinite(t), 0, np.where(t_car < t_ground, 2, 1))  # 0 sky, 1 ground, 2 car
    if jx or jy:
        # The depth was drawn through jittered rays; the shader rebuilds each pixel's position along its centre's ray.
        dc = view.rays(xs, ys)
        p = CAMERA + dc * (np.where(np.isfinite(depth), depth, 0) / (dc @ view.fwd))[..., None]
    return depth.astype(np.float32), p, kind


class Cascades:
    """URP's cascades for the camera: a culling sphere round each slice of the view, a tile of the shadow map each."""

    def __init__(self, view, distance, splits, size, light):
        self.L = light
        tiles = 1 if len(splits) == 1 else 2
        self.res = size // tiles
        self.spheres = []
        prev = NEAR
        for s in splits:
            far = s * distance
            corners = []
            for z in (prev, far):
                for sx in (-1, 1):
                    for sy in (-1, 1):
                        corners.append(CAMERA + view.fwd * z + view.right * (sx * z * view.tan * view.aspect) + view.up * (sy * z * view.tan))
            corners = np.array(corners)
            best = None
            for c in np.linspace(prev, far, 400):
                centre = CAMERA + view.fwd * c
                r = np.linalg.norm(corners - centre, axis=1).max()
                if best is None or r < best[1]:
                    best = (centre, r)
            self.spheres.append(best)
            prev = far
        up = np.array([0.0, 1.0, 0.0])
        self.lu = up - light * (up @ light)
        self.lu /= np.linalg.norm(self.lu)
        self.lr = np.cross(self.lu, light)
        self.lr /= np.linalg.norm(self.lr)

    def texel(self, c):
        return 2 * self.spheres[c][1] / self.res

    def index(self, p):
        idx = np.full(p.shape[:-1], len(self.spheres))
        for c in range(len(self.spheres) - 1, -1, -1):
            centre, r = self.spheres[c]
            idx = np.where(np.linalg.norm(p - centre, axis=-1) < r, c, idx)
        return idx

    def to_light(self, p):
        return p @ self.lr, p @ self.lu, p @ self.L


class ShadowMap:
    """The road and the car as URP draws them into the shadow map: each surface moved away from the light by the depth
    bias and in along its normal by the normal bias times one minus N.L, both in the cascade's texels times the soft
    shadow kernel radius. A texel holds the caster nearest the sun along the ray through its centre."""

    def __init__(self, cascades, depth_bias, normal_bias, kernel):
        self.cs = cascades
        self.db, self.nb = depth_bias * kernel, normal_bias * kernel
        L = cascades.L
        faces = [(np.array([0.0, 1.0, 0.0]), None)]  # the ground, unbounded
        for lo, hi in (BODY, CABIN):
            for axis in range(3):
                for sign, bound in ((-1, lo), (1, hi)):
                    n = np.zeros(3)
                    n[axis] = sign
                    faces.append((n, (axis, bound[axis], lo, hi)))
        self.faces = faces

    def caster(self, c, u, v):
        """The nearest caster's distance towards the sun at light coords u, v of cascade c."""
        cs, L = self.cs, self.cs.L
        T = cs.texel(c)
        origin = u[..., None] * cs.lr + v[..., None] * cs.lu + L * 1000.0
        d = -L
        best = np.full(u.shape, -np.inf)
        for n, box in self.faces:
            push = self.db * T * L * -1 + n * -(self.nb * T * (1 - max(n @ L, 0)))
            if n @ L <= 0 and box is not None:
                continue  # back faces: URP culls them in the shadow pass by default (Cull Back)
            if box is None:
                den = d @ n
                t = ((push - origin) @ n) / den
                p = origin + d * t[..., None]
                best = np.maximum(best, p @ L)
                continue
            axis, plane, lo, hi = box
            plane_p = np.zeros(3)
            plane_p[axis] = plane
            plane_p = plane_p + push
            den = d @ n
            t = ((plane_p - origin) @ n) / den
            p = origin + d * t[..., None]
            q = p - push
            inside = np.ones(u.shape, bool)
            for a in range(3):
                if a == axis:
                    continue
                inside &= (q[..., a] >= lo[a] - 1e-6) & (q[..., a] <= hi[a] + 1e-6)
            best = np.where(inside, np.maximum(best, p @ L), best)
        return best


def soft_shadow(sm, c, P, spin, soft, strength, fix, N=None):
    """SoftShadow(c, positionWS, spin) for the pixels P, all in cascade c."""
    cs = sm.cs
    spread, taps, search, widest = soft
    T = cs.texel(c)
    u, v, z = cs.to_light(P)
    centre, radius = cs.spheres[c]
    cu, cv, _ = cs.to_light(centre)
    extent = radius - T
    co, s = spin

    def rot(x, y):
        return co * x - s * y, s * x + co * y

    def point(uu, vv):
        uu = np.clip(uu, cu - extent, cu + extent)
        vv = np.clip(vv, cv - extent, cv + extent)
        return sm.caster(c, (np.floor(uu / T) + 0.5) * T, (np.floor(vv / T) + 0.5) * T)

    def compare(uu, vv, zz):
        """SAMPLE_TEXTURE2D_SHADOW: the four nearest texels compared with zz (lit where no nearer caster), blended."""
        uu = np.clip(uu, cu - extent, cu + extent)
        vv = np.clip(vv, cv - extent, cv + extent)
        x0, y0 = np.floor(uu / T - 0.5), np.floor(vv / T - 0.5)
        fx, fy = uu / T - 0.5 - x0, vv / T - 0.5 - y0
        out = 0
        for ax, ay, w in ((0, 0, (1 - fx) * (1 - fy)), (1, 0, fx * (1 - fy)), (0, 1, (1 - fx) * fy), (1, 1, fx * fy)):
            d = sm.caster(c, (x0 + ax + 0.5) * T, (y0 + ay + 0.5) * T)
            out = out + w * (d <= zz + 1e-7)
        return out

    reach = np.clip(spread * 20.0, 2 * T, widest * T)
    gu = gv = np.zeros(u.shape)
    margin = 0.0
    if fix:
        # The receiver's own plane, in the light's frame: how its distance towards the sun changes across the map.
        nl = np.maximum(N @ cs.L, 0.25)
        gu, gv = -(N @ cs.lr) / nl, -(N @ cs.lu) / nl
        margin = (np.abs(gu) + np.abs(gv)) * T
    total = np.zeros(u.shape)
    count = np.zeros(u.shape)
    for i in range(int(search)):
        ox, oy = rot(*(DISC[i & 15] * (1.0 if i < 16 else 0.5)))
        d = point(u + ox * reach, v + oy * reach)
        b = d > z + gu * ox * reach + gv * oy * reach + margin
        total += np.where(b, d, 0)
        count += b
    gap = np.abs(z - total / np.maximum(count, 1))
    rad = np.clip(gap * spread, 1.25 * T, widest * T)
    lit = np.zeros(u.shape)
    for j in range(int(taps)):
        ox, oy = rot(*DISC[j & 15])
        if j >= 16:
            ox, oy = oy * 0.6, -ox * 0.6
        lit += compare(u + ox * rad, v + oy * rad, z + gu * ox * rad + gv * oy * rad + margin)
    out = 1 - strength + strength * (lit / taps)
    return np.where(count > 0, out, 1.0)


def contact_shadow(view, depth, P, L, noise, contact, fix, N=None, slope=None, sun_strength=0.85):
    reach0, steps, far, strength = contact
    steps = int(steps)
    dist = np.linalg.norm(P - CAMERA, axis=-1)
    reach = reach0 * (1 + dist * 0.02)
    hit = np.zeros(dist.shape)
    done = dist > far
    start = P
    if fix:
        # Off the surface by two pixels' width, so the march cannot start inside the ground the pixel shows.
        foot = 2.0 * dist * (2 * view.tan / view.h)
        start = P + N * foot[..., None]
        strength = strength * sun_strength / 0.85
    for i in range(steps):
        t = reach * (i + noise) / steps
        p = start + L * t[..., None]
        px, py, z = view.project(p)
        out = (px < 0) | (px >= view.w) | (py < 0) | (py >= view.h)
        done |= out
        ix = np.clip(np.floor(px).astype(np.int64), 0, view.w - 1)
        iy = np.clip(np.floor(py).astype(np.int64), 0, view.h - 1)
        scene = depth[iy, ix].astype(np.float64)
        behind = z - scene
        bias = 0.01 + 0.0015 * z
        if fix:
            # A pixel's own depth step: what the depth buffer cannot tell apart from the surface itself.
            bias = np.maximum(bias, slope * (z / np.maximum(dist, 1e-3)) ** 2 * 1.5)
        h = ~done & (behind > bias) & (behind < 0.25 + 0.01 * z)
        hit = np.where(h, 1 - i / steps * 0.5, hit)
        done |= h
    fade = 1 - np.clip((dist - far * 0.6) / (far * 0.4), 0, 1)
    fade = fade * fade * (3 - 2 * fade)
    return np.where(dist > far, 1.0, 1 - hit * fade * strength)


def screen_terms(o, cs, sm, view, depth, P, kind, N, slope, noise, L, strength, softq, blend, contactq):
    H, W = depth.shape
    spread, taps, search, widest = softq
    c_reach, c_steps, c_far, c_strength = contactq
    spin = (np.cos(noise * 2 * np.pi), np.sin(noise * 2 * np.pi))
    soft = np.ones((H, W))
    mask = kind > 0
    idx = cs.index(P)
    for c in range(len(cs.spheres)):
        m = mask & (idx == c)
        if not m.any():
            continue
        sh = soft_shadow(sm, c, P[m], (spin[0][m], spin[1][m]), (spread, taps, search, widest), strength, o["fix"], N[m])
        if blend > 0 and c + 1 < len(cs.spheres):
            centre, r = cs.spheres[c]
            edge = np.linalg.norm(P[m] - centre, axis=-1) / r
            t = np.clip((edge - (1 - blend)) / blend, 0, 1)
            nc, nr = cs.spheres[c + 1]
            inside = np.linalg.norm(P[m] - nc, axis=-1) < nr
            sel = (t > 0) & inside
            if sel.any():
                nxt = soft_shadow(sm, c + 1, P[m][sel], (spin[0][m][sel], spin[1][m][sel]), (spread, taps, search, widest), strength, o["fix"], N[m][sel])
                sh[sel] = sh[sel] + (nxt - sh[sel]) * t[sel]
        soft[m] = sh
    contact = np.ones((H, W))
    contact[mask] = contact_shadow(view, depth, P[mask], L, noise[mask], (c_reach, c_steps, c_far, c_strength), o["fix"], N[mask], slope[mask], strength)
    return soft, contact


def main():
    out = sys.argv[1]
    o = options()
    soft_q, scale, distance, splits, size, dbias, nbias, kernel = LEVELS[o["quality"]]
    spread, taps, search, widest, c_reach, c_steps, c_far, c_strength, blend = soft_q
    W, H = int(round(o["width"] * scale)), int(round(o["height"] * scale))
    view = View(W, H)
    s = skyclock.at(o["hour"])
    sun = s["sun_dir"]
    # The car faces `heading` degrees from north; the sun is turned into the car's frame.
    yaw = math.radians(o["heading"])
    east, north = sun[0], sun[2]
    L = np.array([east * math.cos(yaw) - north * math.sin(yaw), sun[1], east * math.sin(yaw) + north * math.cos(yaw)])
    L /= np.linalg.norm(L)

    depth, P, kind = scene_depth(view, o["jx"], o["jy"])
    cs = Cascades(view, distance, splits, size, L)
    sm = ShadowMap(cs, dbias, nbias, kernel)
    strength = 0.85 * (1 - 0.9 * o["overcast"])

    ys, xs = np.mgrid[0:H, 0:W].astype(np.float64) + 0.5
    # The surface's normal from the depth: each axis takes the nearer of its two neighbours, as the shader does.
    def nb(dx, dy):
        return np.roll(np.roll(P, -dy, 0), -dx, 1), np.roll(np.roll(depth, -dy, 0), -dx, 1)
    (pr, dr), (pl, dl), (pd, dd), (pu, du) = nb(1, 0), nb(-1, 0), nb(0, 1), nb(0, -1)
    hx = np.where((np.abs(dr - depth) < np.abs(dl - depth))[..., None], pr - P, P - pl)
    hy = np.where((np.abs(dd - depth) < np.abs(du - depth))[..., None], pd - P, P - pu)
    N = np.cross(hx, hy)
    N /= np.maximum(np.linalg.norm(N, axis=-1, keepdims=True), 1e-9)
    N = np.where(((N * (CAMERA - P)).sum(-1) < 0)[..., None], -N, N)
    slope = np.maximum(np.minimum(np.abs(dr - depth), np.abs(dl - depth)), np.minimum(np.abs(dd - depth), np.abs(du - depth)))
    frames = o["frames"] if o["fix"] else 1
    acc_soft = 0
    acc_contact = 0
    for f in range(frames):
        fr = o["frame"] + f
        shift = 5.588238 * (fr % 64) if o["fix"] else 0.0
        noise = np.modf(52.9829189 * np.modf((xs + shift) * 0.06711056 + (ys + shift) * 0.00583715)[0])[0]
        sf, ct = screen_terms(o, cs, sm, view, depth, P, kind, N, slope, noise, L, strength, (spread, taps, search, widest), blend, (c_reach, c_steps, c_far, c_strength))
        acc_soft = acc_soft + sf
        acc_contact = acc_contact + ct
    soft, contact = acc_soft / frames, acc_contact / frames
    screen = np.minimum(soft, contact)


    if o["show"] != "lit":
        m = {"screen": screen, "contact": contact, "soft": soft}[o["show"]]
        save(out, np.repeat(m[..., None], 3, -1))
    else:
        sun_lin = lin(s["colour"]) * 3.2 * (1 - 0.8 * o["overcast"])
        amb = lin(s["amb_sky"]) * (0.9 + 0.6 * o["overcast"])
        albedo = np.where((kind == 2)[..., None], lin(np.array([0.55, 0.36, 0.30])),
                          np.where((np.abs(P[..., 0]) > 6.5)[..., None], lin(np.array([0.24, 0.36, 0.17])), lin(np.array([0.17, 0.17, 0.18]))))
        n = np.where((kind == 2)[..., None], np.array([0.0, 0.99, -0.15]), np.array([0.0, 1.0, 0.0]))
        NoL = np.clip(n @ L, 0, 1)
        col = albedo * (sun_lin * (NoL * screen)[..., None] + amb)
        col[kind == 0] = lin(s["horizon"]) * 1.4
        save(out, frame(col, **skyclock.grade(s)))
    dist = np.linalg.norm(P - CAMERA, axis=-1)
    for a, b in ((2, 4), (4, 8), (8, 20), (20, 80)):
        r = (kind == 1) & (dist >= a) & (dist < b)
        if r.any():
            print(f"{a}-{b}m contact {contact[r].mean():.3f} soft {soft[r].mean():.3f} min {screen[r].min():.2f}", end="  ")
    print(out.split("/")[-1])


if __name__ == "__main__":
    main()
