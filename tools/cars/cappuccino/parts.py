"""Kaiser Cappuccino parts, built in Blender: the body cut into its pieces, glass, lamps,
grille, wheels, interior, mirrors, badges and the name."""
import json
import math
from pathlib import Path

import numpy as np
import bpy
from mathutils import Matrix  # noqa: E402
from shapely.geometry import Point, Polygon, box as sbox
from shapely import affinity
from shapely.ops import unary_union

import blend as B
import body
import rows
from geom import close_rings, lathe

HERE = Path(__file__).resolve().parent
R = body.R
AX_F, AX_R, AX_Z = R["axles"]["front"], R["axles"]["rear"], R["axles"]["z"]
TYRE_R, TYRE_W, RIM_R = R["tyre"]["r"], R["tyre"]["w"], R["tyre"]["rim_r"]
TRACK = 0.775                       # wheel centre plane, half track
SKIN = 0.022                        # panel thickness round the cabin
GAP = 0.0025                        # half the shut line gap round each opening panel
FLOOR = float(rows.z_of(0.03)) + 0.13   # cabin floor, above the sills
X, Z, W = rows.x_of, rows.z_of, rows.w_of
DOORS = {}
OPTION_GROUPS = {"rim": "Rims", "badgef": "Front badge", "badger": "Rear badge", "name": "Name"}


# ---------------------------------------------------------------- helpers

def prism(name, poly, axis, a0, a1, mat="interior", step=0.05):
    """Extrude a 2D polygon. axis "y": poly is (x, z), from y = a0 to a1. axis "z": (x, y).
    axis "x": (y, z)."""
    pts, tris = B.triangulate(poly, step)
    n = len(pts)

    def put(p, a):
        u, v = p
        return {"y": (u, a, v), "z": (u, v, a), "x": (a, u, v)}[axis]
    verts = [put(p, a0) for p in pts] + [put(p, a1) for p in pts]
    count = {}
    for t in tris:
        for e in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            count[tuple(sorted(e))] = count.get(tuple(sorted(e)), 0) + 1
    faces = [(t[2], t[1], t[0]) for t in tris] + [(t[0] + n, t[1] + n, t[2] + n) for t in tris]
    for t in tris:
        for a, b in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            if count[tuple(sorted((a, b)))] == 1:
                faces.append((a, b, b + n, a + n))
    return B.make(name, verts, faces, mat)


def side_frame(sign):
    """Rays inward from the left (sign 1) or right (-1) side; 2D is (x, z)."""
    return B.frame((0, 2.0 * sign, 0), (1, 0, 0), (0, 0, 1), (0, -sign, 0))


TOP = B.frame((0, 0, 3.0), (1, 0, 0), (0, 1, 0), (0, 0, -1))
FRONT = B.frame((4.0, 0, 0), (0, 1, 0), (0, 0, 1), (-1, 0, 0))
REAR = B.frame((-4.0, 0, 0), (0, 1, 0), (0, 0, 1), (1, 0, 0))


def frame_on(bvh, loops, fr, centre=None):
    """A frame lying along the mean normal of the skin under 3D outline points, centred on
    them, and those outlines in its 2D coordinates. loops: lists of 2D points in fr."""
    pts3, nrm = [], []
    for loop in loops:
        for p in loop:
            loc, n = B.cast(bvh, fr, *p)
            if loc is not None:
                pts3.append(loc); nrm.append(n)
    pts3 = np.array(pts3)
    n = np.mean(nrm, axis=0); n /= np.linalg.norm(n)
    c = pts3.mean(0) if centre is None else centre
    o, u, v, _d = fr
    u2 = u - n * u.dot(n); u2 /= np.linalg.norm(u2)
    v2 = np.cross(n, u2)
    if v2.dot(v) < 0:
        v2 = -v2
    f = B.frame(c, u2, v2, -n)
    out = []
    for loop in loops:
        q = []
        for p in loop:
            loc, _n = B.cast(bvh, fr, *p)
            if loc is not None:
                q.append(((loc - c).dot(u2), (loc - c).dot(v2)))
        out.append(q)
    return f, out


def dense(poly, step=0.01):
    """Outline points of a polygon, every step."""
    p = np.array(poly.exterior.coords)
    from geom import resample
    return resample(p, step)[:-1]


def window_frame(bvh, poly, fr):
    """Frame along a window's own normal, and the window outline in it."""
    f, (out,) = frame_on(bvh, [dense(poly)], fr)
    return f, Polygon(out).buffer(0)


def ellipsoid(name, c, r, mat, n=24, m=14):
    rings = []
    for i in range(1, m):
        t = math.pi * i / m
        ring = [(c[0] + r[0] * math.sin(t) * math.cos(a), c[1] + r[1] * math.sin(t) * math.sin(a),
                 c[2] + r[2] * math.cos(t)) for a in np.linspace(0, 2 * math.pi, n, endpoint=False)]
        rings.append(ring)
    v, f = close_rings(rings, centres=[(c[0], c[1], c[2] + r[2]), (c[0], c[1], c[2] - r[2])])
    return B.make(name, v, f, mat)


def boxob(name, x0, x1, y0, y1, z0, z1, mat):
    from geom import box
    v, f = box(x0, x1, y0, y1, z0, z1)
    return B.make(name, v, f, mat)


def half_to_full(pts):
    """Outline traced on one side (w, z) of the centre, mirrored to a whole closed shape."""
    p = list(pts)
    return Polygon(p + [(-w, z) for w, z in p[::-1]]).buffer(0)


# ---------------------------------------------------------------- body

def _volume(ob):
    import bmesh
    bm = bmesh.new(); bm.from_mesh(ob.data)
    v = bm.calc_volume(signed=True)
    bm.free()
    return v


_exact = B.boolean


def safe_boolean(target, other, op, tolerant=False):
    """Blender's exact boolean can quietly lose a large shell or skip a cut. Check the result's
    volume against what the operation allows, and run it again in the hole tolerant mode, then
    with the cutter nudged a hair, when it is wrong."""
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    v0, vo = _volume(target), _volume(other)
    dg = bpy.context.evaluated_depsgraph_get()
    touch = bool(BVHTree.FromObject(target, dg).overlap(BVHTree.FromObject(other, dg)))
    keep_t = target.data.copy()
    keep_o = other.data.copy()
    tries = [(tolerant, 0.0), (True, 0.0), (False, 0.0004), (True, 0.0007)]
    for i, (tol, nudge) in enumerate(tries):
        if i:
            target.data = keep_t.copy()
            other = bpy.data.objects.new(other_name, keep_o.copy())
            bpy.context.scene.collection.objects.link(other)
            other.data.transform(__import__("mathutils").Matrix.Translation((nudge, nudge * 0.7, nudge * 0.4)))
        other_name = other.name
        _exact(target, other, op, tol)
        v1 = _volume(target)
        if op == "DIFFERENCE":
            ok = v0 - vo - 1e-6 <= v1 <= v0 + 1e-6 and (not touch or len(target.data.polygons) > 0)
        elif op == "INTERSECT":
            ok = -1e-6 <= v1 <= min(v0, vo) + 1e-6 and (not touch or v1 > 1e-7)
        else:
            ok = v1 >= max(v0, vo) - 1e-6
        if ok:
            return
        print("boolean retry", op, target.name, other_name, round(v0, 5), round(vo, 5), "->", round(v1, 5))
    print("boolean failed", op, target.name, other_name)


B.boolean = safe_boolean


def cut(target, other, op="DIFFERENCE"):
    B.boolean(target, other, op)


def outer():
    v, f = close_rings(body.skin())
    return B.make("body", v, f, "body")


def arch_tub(name, axle_x, grow, y0):
    return [prism(f"{name}_{s}", body.arch(axle_x).buffer(grow, join_style=2), "y", y0 * s, 1.5 * s)
            for s in (1, -1)]


def cabin(ob):
    """The hollow inside the cabin: the skin pushed in by SKIN, kept between the firewall and
    the tail, above the floor, and clear of the wheel tubs."""
    x0, x1 = R["screen"]["base"] + 0.04, float(X(1.66))
    v, f = close_rings(body.inner_skin(x0, x1, SKIN))
    inner = B.make("cabin", v, f, "interior")
    keep = prism("cabin_box", sbox(x1 - 1, FLOOR, x0 + 1, 3.0), "y", -1.2, 1.2)
    B.boolean(inner, keep, "INTERSECT")
    r = R["arch_r"] + 0.05
    for ax in (AX_F, AX_R):
        for s in (1, -1):
            v, f = lathe([(r, 0.40 * s), (r, 1.5 * s), (0.01, 1.5 * s), (0.01, 0.40 * s)], n=48, axis="y")
            t = B.make("tub", v, f, "interior")
            t.data.transform(Matrix.Translation((ax, 0, AX_Z)))
            B.boolean(inner, t, "DIFFERENCE")
    inner.name = "cabin"
    return inner


def windscreen():
    """Windscreen outline on the top view (x, y): an even curve for the base and the top,
    straight sides."""
    xb0, xbc, xt0, xtc = X(-0.88), X(-0.82), X(-0.15), X(-0.19)
    wb, wt = R["screen"]["w_base"], R["screen"]["w_top"]
    base = [(xbc + (xb0 - xbc) * (1 - (y / wb) ** 2), y) for y in np.linspace(-wb, wb, 30)]
    top = [(xtc + (xt0 - xtc) * (1 - (y / wt) ** 2), y) for y in np.linspace(wt, -wt, 30)]
    return Polygon(base + top).buffer(0)


def hatch_glass():
    """Rear glass on the rear view (y, z): even curves top and bottom, straight sides."""
    wt, wb = R["hatch"]["w_top"], R["hatch"]["w_base"]
    zt0, ztc, zb0, zbc = Z(0.918), Z(0.905), Z(0.715), Z(0.71)
    top = [(y, ztc + (zt0 - ztc) * (1 - (y / wt) ** 2)) for y in np.linspace(-wt, wt, 30)]
    bot = [(y, zbc + (zb0 - zbc) * (1 - (y / wb) ** 2)) for y in np.linspace(wb, -wb, 30)]
    return Polygon(top + bot).buffer(0)


def bonnet_outline(bvh):
    """Bonnet shut line on the top view (x, y): from the windscreen's base, inboard of its
    corners, out to the bonnet's rear corners, forward along the wings and over the lamps,
    across the nose."""
    half = [(-0.80, .50), (-0.86, .60), (-0.93, .685), (-1.10, .695), (-1.30, .695), (-1.40, .688),
            (-1.48, .655), (-1.56, .60), (-1.64, .52), (-1.70, .42), (-1.74, .30), (-1.765, .15), (-1.772, 0)]
    pts = [(float(X(s)), float(W(w))) for s, w in half]
    poly = Polygon(pts + [(x, -y) for x, y in pts[-2::-1]]).buffer(0)
    # the front edge crosses the nose above the grille, so the grille stays on the bumper
    zg = max(z for _w, z in R["grille"]) + 0.025
    edge = []
    for y in np.linspace(-1.0, 1.0, 81):
        loc, _n = B.cast(bvh, FRONT, y, zg)
        if loc is not None:
            edge.append((loc[0] - 0.012, y))
    keep = Polygon([(-9, edge[0][1] - 1)] + [(edge[0][0], edge[0][1] - 1)] + edge
                   + [(edge[-1][0], edge[-1][1] + 1), (-9, edge[-1][1] + 1)])
    return poly.intersection(keep).buffer(0)


def hatch_outline():
    """Hatch shut line on the rear view (y, z): below the roof's rear edge, down the sides of
    the glass, straight on down under the tail lamps, which cover it, and across above the
    bumper. One smooth closed curve through these points."""
    from geom import catmull_closed
    half = [(0.0, .958), (0.30, .956), (0.46, .948), (0.515, .925), (0.535, .86), (0.532, .77), (0.522, .70),
            (0.512, .60), (0.505, .50), (0.497, .47), (0.475, .452), (0.42, .45), (0.0, .45)]
    pts = [(float(W(w)), float(Z(z))) for w, z in half]
    loop = pts + [(-y, z) for y, z in pts[-2:0:-1]]
    return Polygon(catmull_closed(loop, 8)).buffer(0)


def paint_jambs(bvh, ob, outline, sign, depth=True):
    """Paint the cut faces round a door opening in body colour: interior faces near the door
    outline seen from the side, whose normal does not point across the car. depth: also keep
    only faces between the outer skin and the hollow behind it, so the door's own inner wall
    stays interior."""
    if "body" not in [m.name for m in ob.data.materials]:
        ob.data.materials.append(bpy.data.materials["body"])
    names = [m.name for m in ob.data.materials]
    k, ki = names.index("body"), names.index("interior") if "interior" in names else -1
    r = 0.02 if depth else 0.012
    ring = outline.buffer(r).difference(outline.buffer(-r))
    fr = side_frame(sign)
    for p in ob.data.polygons:
        c = p.center
        if p.material_index != ki or c[1] * sign < 0.3 or abs(p.normal[1]) > 0.75 \
                or not ring.contains(Point(c[0], c[2])):
            continue
        if not depth:
            p.material_index = k
            continue
        loc, _n = B.cast(bvh, fr, c[0], c[2])
        if loc is not None and abs(loc[1]) - abs(c[1]) < SKIN * 0.8:
            p.material_index = k


def body_pieces(opts):
    ob = outer()
    ref = ob.copy(); ref.data = ob.data.copy(); ref.name = "ref"
    bpy.context.scene.collection.objects.link(ref)
    bvh = B.tree(ref)
    for t in arch_tub("arch", AX_F, 0.0, 0.60) + arch_tub("archr", AX_R, 0.0, 0.60):
        B.boolean(ob, t, "DIFFERENCE")
    cab = cabin(ob)
    keep = cab.copy(); keep.data = cab.data.copy(); keep.name = "cab_keep"
    bpy.context.scene.collection.objects.link(keep)
    B.boolean(ob, cab, "DIFFERENCE")

    # glass outlines: each window's own frame, along its own normal
    sw = body.side_windows()
    wins = [("screen", windscreen(), TOP), ("hatch", hatch_glass(), REAR)]
    for s, tag in ((1, "l"), (-1, "r")):
        wins += [(f"door_{tag}", sw["door"], side_frame(s)), (f"quarter_{tag}", sw["quarter"], side_frame(s))]
    frames = {name: window_frame(bvh, poly, fr) for name, poly, fr in wins}
    cutter = lambda name: B.cutter(bvh, f"cut_{name}", [(frames[name][1], 0.07)], frames[name][0], step=0.025)
    for name in ("screen", "hatch"):
        cut(ob, cutter(name))

    # B pillar: black between the door glass and the quarter glass, split down its middle by the door
    pillars = []
    bf, br = R["b_pillar"]
    for s, tag in ((1, "l"), (-1, "r")):
        zb = max(z for xx, z in sw["door"].exterior.coords if abs(xx - bf) < 0.03)
        strip = sbox(br - 0.01, float(body.CURVES["belt"]["z"](br)), bf + 0.01, zb + 0.012)
        strip = strip.difference(sw["door"].buffer(0.004)).difference(sw["quarter"].buffer(0.004))
        pillars += B.lay(bvh, f"pillar_{tag}", strip, side_frame(s), -0.003, 0.0025, "trim")
    for name, (f, p2) in frames.items():
        B.lay(bvh, f"glass_{name}", p2.buffer(0.004), f, -0.011, -0.006, "glass", 0.03)
        B.lay(bvh, f"band_{name}", p2.buffer(0.013, join_style=1).difference(p2.buffer(-0.006)),
              f, -0.003, 0.0025, "trim")

    interior(keep, opts)
    mirrors(bvh)

    # doors: cut free along the shut line, then the side glass openings in the door and the body
    for s, tag in ((1, "l"), (-1, "r")):
        out = body.door_outline()
        name = f"door_{tag}"
        roles = {f"glass_door_{tag}": "glass", f"band_door_{tag}": "band", f"mirror_{tag}": "mirror",
                 f"mirror_glass_{tag}": "mirror_glass", f"card_{tag}": "card"}
        pil = [p for p in pillars if p.name.startswith(f"pillar_{tag}")]
        free = B.free_door(bvh, side_frame(s), [(out, 0.11)], GAP, ob, pil, roles, out, name=name, step=0.012)
        cut(free[0], cutter(f"door_{tag}"))
        cut(ob, cutter(f"quarter_{tag}"))
        paint_jambs(bvh, free[0], out, s)
        paint_jambs(bvh, ob, out, s, depth=False)
        f = R["door"]
        xf = max(p[0] for p in f) - 0.03
        yw = float(body.CURVES["side"]["w"](xf)) - 0.03
        DOORS[name] = ((xf, s * yw, 0.40), (xf - 0.04, s * (yw - 0.07), 1.10), -s * 66.0)

    # bonnet and hatch: panels cut free along their shut lines
    for nm, poly, fr in (("bonnet", bonnet_outline(bvh), TOP), ("hatch", hatch_outline(), REAR)):
        inner = B.cutter(bvh, f"{nm}_in", [(poly, 0.03)], fr, grow=-GAP, step=0.025)
        outr = B.cutter(bvh, f"{nm}_out", [(poly, 0.03)], fr, grow=GAP, step=0.025)
        B.split_off(ob, inner, outr, nm)
    bpy.data.objects.remove(keep)
    return ob, ref, bvh


# ---------------------------------------------------------------- interior

def interior(cab, opts):
    """Firewall, dash, floor, seats, steering wheel on the left, rear seat, door cards, mirror.
    Each is clipped to the cabin's hollow."""
    x_fw = R["screen"]["base"] + 0.04
    x_seat = float(X(0.12))
    parts = []
    parts.append(boxob("firewall", x_fw - 0.02, x_fw + 0.01, -0.9, 0.9, 0.30, 1.2, "interior"))
    parts.append(boxob("dash", x_fw - 0.42, x_fw + 0.01, -0.9, 0.9, 0.78, 1.04, "interior"))
    parts.append(boxob("floor", -2.2, x_fw + 0.01, -0.9, 0.9, FLOOR - 0.04, FLOOR + 0.04, "interior"))
    for s in (1, -1):
        y = 0.37 * s
        parts.append(boxob(f"seat_base_{s}", x_seat - 0.28, x_seat + 0.24, y - 0.24, y + 0.24, FLOOR + 0.03, FLOOR + 0.18, "seat"))
        back = boxob(f"seat_back_{s}", x_seat - 0.34, x_seat - 0.22, y - 0.24, y + 0.24, FLOOR + 0.16, FLOOR + 0.81, "seat")
        B.swing([back], (x_seat - 0.28, y - 1, FLOOR + 0.16), (x_seat - 0.28, y + 1, FLOOR + 0.16), -14)
        parts.append(back)
    parts.append(boxob("rear_seat", float(X(0.95)), float(X(0.62)), -0.62, 0.62, FLOOR + 0.03, FLOOR + 0.21, "seat"))
    rb = boxob("rear_back", float(X(1.05)), float(X(0.93)), -0.62, 0.62, FLOOR + 0.16, FLOOR + 0.68, "seat")
    B.swing([rb], (float(X(0.99)), -1, FLOOR + 0.16), (float(X(0.99)), 1, FLOOR + 0.16), -10)
    parts.append(rb)
    for p in parts:
        clip = cab.copy(); clip.data = cab.data.copy()
        bpy.context.scene.collection.objects.link(clip)
        B.boolean(p, clip, "INTERSECT")
    # steering wheel: a ring on the left, tilted towards the driver, on a column to the dash
    y = 0.37
    c = np.array([x_fw - 0.62, y, 0.98])
    ring = [(0.185 + 0.016 * math.cos(a), 0.016 * math.sin(a)) for a in np.linspace(0, 2 * math.pi, 12, endpoint=False)]
    v, f = lathe(ring, n=40, axis="y")
    sw = B.make("wheel_steer", v, f, "interior")
    from mathutils import Matrix
    sw.data.transform(Matrix.Rotation(math.radians(90), 4, "Z"))
    sw.data.transform(Matrix.Rotation(math.radians(-24), 4, "Y"))
    sw.data.transform(Matrix.Translation(tuple(c)))
    sw.name = "steering"
    # three spokes from the hub out to the rim: left, right and down, in the wheel's plane
    for k, (y0, y1, z0, z1) in enumerate(((0.05, 0.18, -0.014, 0.014), (-0.18, -0.05, -0.014, 0.014),
                                          (-0.014, 0.014, -0.18, -0.05))):
        sp = boxob(f"steering_spoke_{k}", -0.008, 0.008, y0, y1, z0, z1, "interior")
        sp.data.transform(Matrix.Rotation(math.radians(-24), 4, "Y"))
        sp.data.transform(Matrix.Translation(tuple(c)))
    col = boxob("column", c[0] + 0.01, x_fw - 0.38, y - 0.03, y + 0.03, c[2] - 0.04, c[2] + 0.02, "interior")
    hub = boxob("steering_hub", c[0] - 0.02, c[0] + 0.03, y - 0.06, y + 0.06, c[2] - 0.05, c[2] + 0.05, "interior")
    B.swing([hub], tuple(c - [0, 1, 0]), tuple(c + [0, 1, 0]), -24)
    # interior mirror under the windscreen's top
    xt = float(X(-0.22))
    zt = float(body.CURVES["crown"]["z"](xt)) - SKIN - 0.075
    boxob("rear_view", xt - 0.03, xt + 0.01, -0.11, 0.11, zt, zt + 0.05, "interior")
    boxob("rear_view_stem", xt - 0.01, xt + 0.01, -0.01, 0.01, zt + 0.05, zt + 0.08, "interior")
    # door cards on the inside of each door, laid on the hollow's wall
    cb = B.tree(cab)
    out = body.door_outline()
    zb = float(body.CURVES["belt"]["z"](0.0))
    card = out.intersection(sbox(-3, 0.40, 3, zb - 0.03)).buffer(-0.05)
    for s, tag in ((1, "l"), (-1, "r")):
        fr = lambda a, b, s=s: (np.array([a, 0.0, b]), np.array([0.0, s, 0.0]))
        B.lay(cb, f"card_{tag}", card, fr, -0.014, -0.001, "interior", 0.02)


# ---------------------------------------------------------------- mirrors

def mirrors(bvh):
    for s, tag in ((1, "l"), (-1, "r")):
        x, z = float(X(-0.60)), float(Z(0.70))
        loc, _n = B.cast(bvh, side_frame(s), x, z)
        y0 = abs(loc[1])
        c = (x - 0.02, s * (y0 + 0.12), z + 0.04)
        house = ellipsoid(f"mirror_{tag}", c, (0.055, 0.11, 0.065), "body")
        arm = boxob(f"mirror_arm_{tag}", x - 0.04, x + 0.03, min(s * (y0 - 0.03), s * (y0 + 0.06)),
                    max(s * (y0 - 0.03), s * (y0 + 0.06)), z - 0.01, z + 0.03, "body")
        B.boolean(house, arm, "UNION")
        g = ellipsoid(f"mirror_glass_{tag}", (c[0] - 0.05, c[1], c[2]), (0.012, 0.095, 0.052), "mirror")


# ---------------------------------------------------------------- lamps, grille

def quarter(sign, front):
    d = np.array([-1.0 if front else 1.0, -0.75 * sign, -0.25]); d /= np.linalg.norm(d)
    u = np.cross(d, [0, 0, 1.0]); u /= np.linalg.norm(u)
    v = np.cross(u, d)
    return B.frame(-d * 4 + [0, 0, 0.6], u, v, d)


def _open_chain(poly, drop):
    """The outline of poly as one open chain, with the points drop(p) removed: the run of
    kept points, started just after the dropped stretch."""
    pts = np.array(poly.exterior.coords)[:-1]
    if poly.exterior.is_ccw:
        pts = pts[::-1]
    gone = np.array([drop(q) for q in pts])
    k = int(np.argmax(gone))
    pts, gone = np.roll(pts, -k, axis=0), np.roll(gone, -k)
    j = int(np.argmin(gone))
    return [q for q in pts[j:] if not drop(q)]


def lamp_region(bvh, side_pts, face_pts, sign, front):
    """A lamp traced on two views, carried onto the skin and seen from the quarter, where it
    wraps the corner. The side trace is kept where the body faces sideways, the front or rear
    trace where it faces that way, and the two are joined into one outline."""
    f = FRONT if front else REAR
    fr_q = quarter(sign, front)
    xc = float(X(-1.62)) if front else float(X(1.58))
    side = Polygon(dense(Polygon(side_pts).buffer(0), 0.006)).buffer(0)
    zmid = side.centroid.y
    wc = abs(B.cast(bvh, side_frame(sign), xc, zmid)[0][1]) - 0.004
    behind = (lambda q: q[0] > xc) if front else (lambda q: q[0] < xc)
    outer = lambda q: abs(q[0]) > wc
    chains = []
    for pts, fr, drop in ((_open_chain(side, behind), side_frame(sign), None),
                          (_open_chain(Polygon(dense(Polygon(face_pts).buffer(0), 0.006)).buffer(0), outer), f, None)):
        q = []
        for p in pts:
            loc, _n = B.cast(bvh, fr, *p)
            if loc is not None:
                o, u, v, d = fr_q
                q.append(((loc - o).dot(u), (loc - o).dot(v)))
        chains.append(np.array(q))
    a, c = chains
    # join the chains whichever way round gives one clean outline, not a bow tie
    joins = [Polygon(np.vstack([a, cc])) for cc in (c, c[::-1])]
    poly = max(joins, key=lambda g: (g.is_valid, g.buffer(0).area))
    # the two traces meet with small steps; the hull of both reads as one smooth lamp
    return fr_q, Polygon(dense(poly.buffer(0).convex_hull, 0.006))


def lamps(bvh):
    hs, hf = np.array(R["head_side"]), np.array(R["head_front"])
    ts, tr = np.array(R["tail_side"]), np.array(R["tail_rear"])
    for s, tag in ((1, "l"), (-1, "r")):
        # headlamp: black housing, two round lamps of our own, a chrome bezel between
        face = [(s * y, z) for y, z in hf]
        fr, reg = lamp_region(bvh, hs, face, s, True)
        B.lay(bvh, f"head_housing_{tag}", reg, fr, -0.002, 0.006, "lamp_housing", 0.012)
        inner = reg.buffer(-0.012)
        cx = np.array(inner.centroid.coords[0])
        (minx, miny, maxx, maxy) = inner.bounds
        r = min(0.032, (maxy - miny) / 2 - 0.004)
        span = maxx - minx
        for k, t in enumerate((0.30, 0.62)):
            p = Point(minx + span * t, cx[1]).buffer(r, 32).intersection(inner)
            if p.area > 1e-5:
                B.lay(bvh, f"lamp_head_{tag}" + (f"_{k}" if k else ""), p, fr, 0.0055, 0.010, "lamp_head", 0.005)
        # tail lamp: red tail lamp body with a brake band along its top
        face = [(s * y, z) for y, z in tr]
        fr, reg = lamp_region(bvh, ts, face, s, False)
        B.lay(bvh, f"tail_housing_{tag}", reg, fr, -0.002, 0.005, "lamp_housing", 0.012)
        inner = reg.buffer(-0.007)
        (minx, miny, maxx, maxy) = inner.bounds
        top = inner.intersection(sbox(minx - 1, miny + (maxy - miny) * 0.62, maxx + 1, maxy + 1))
        low = inner.difference(top.buffer(0.004))
        B.lay(bvh, f"lamp_brake_{tag}", top, fr, 0.0045, 0.009, "lamp_brake", 0.01)
        B.lay(bvh, f"lamp_tail_{tag}", low, fr, 0.0045, 0.008, "lamp_tail", 0.012)
        # rain light in the rear bumper slots
        rain = Polygon([(s * y, z) for y, z in R["rain"]]).buffer(0)
        B.lay(bvh, f"lamp_rain_{tag}", rain, REAR, -0.002, 0.005, "lamp_rain", 0.004)


def grille(bvh):
    hf = R["head_front"]
    lamp = unary_union([Polygon([(s * y, z) for y, z in hf]).buffer(0.004) for s in (1, -1)])
    band = half_to_full(R["grille"]).difference(lamp)
    B.lay(bvh, "grille", band, FRONT, -0.003, 0.004, "trim", 0.012)
    zs = [z for _w, z in R["grille"]]
    z0, z1 = min(zs), max(zs)
    z = (z0 + z1) / 2
    slat = sbox(-W(0.38), z - 0.003, W(0.38), z + 0.003)
    B.lay(bvh, "grille_slat", slat, FRONT, 0.0035, 0.007, "lamp_housing", 0.006)
    intake = half_to_full(R["intake"])
    B.lay(bvh, "intake", intake, FRONT, -0.003, 0.004, "trim", 0.025)
    zs = [z for _w, z in R["intake"]]
    z0, z1 = min(zs), max(zs)
    for k, t in enumerate((0.3, 0.5, 0.7)):
        z = z0 + (z1 - z0) * t
        slat = sbox(-W(0.62), z - 0.005, W(0.62), z + 0.005).intersection(intake.buffer(-0.02))
        B.lay(bvh, f"intake_slat_{k}", slat, FRONT, 0.0035, 0.009, "trim", 0.008)


# ---------------------------------------------------------------- wheels

def spoke_design(k):
    """Spoke outlines in the wheel's plane (u, v), centred. 1: the reference car's five broad
    spokes. 2: ten slim spokes in pairs. 3: a plain disc with seven round holes."""
    rr = RIM_R - 0.012
    hub = Point(0, 0).buffer(0.075, 40)
    if k == 3:
        disc = Point(0, 0).buffer(rr, 64)
        holes = [Point(0.13 * math.cos(a), 0.13 * math.sin(a)).buffer(0.034, 24)
                 for a in np.linspace(0, 2 * math.pi, 7, endpoint=False)]
        return disc.difference(unary_union(holes))
    shapes = [hub]
    if k == 1:
        for a in np.linspace(0, 2 * math.pi, 5, endpoint=False):
            sp = Polygon([(0.05, -0.034), (rr, -0.052), (rr, 0.052), (0.05, 0.034)])
            shapes.append(affinity.rotate(sp, a, origin=(0, 0), use_radians=True))
    else:
        for a in np.linspace(0, 2 * math.pi, 5, endpoint=False):
            for off in (-0.13, 0.13):
                sp = Polygon([(0.05, -0.012), (rr, -0.016), (rr, 0.016), (0.05, 0.012)])
                shapes.append(affinity.rotate(sp, a + off, origin=(0, 0), use_radians=True))
    return unary_union(shapes)


def wheels(opts, game):
    hw, r0, r1 = TYRE_W / 2, RIM_R - 0.006, TYRE_R
    prof = [(r0, -hw + 0.01)] + [(r1 - 0.03, -hw), (r1 - 0.008, -hw + 0.012), (r1, -hw + 0.04), (r1, hw - 0.04),
                                 (r1 - 0.008, hw - 0.012), (r1 - 0.03, hw)] + [(r0, hw - 0.01)]
    barrel = [(RIM_R - 0.022, -hw + 0.012), (RIM_R + 0.004, -hw + 0.006), (RIM_R + 0.004, hw - 0.004),
              (RIM_R - 0.022, hw - 0.004)]
    picks = [opts["rim"]] if game else [1, 2, 3]
    from mathutils import Matrix
    for corner, x, s in (("fl", AX_F, 1), ("fr", AX_F, -1), ("rl", AX_R, 1), ("rr", AX_R, -1)):
        y = s * TRACK
        place = lambda ob: ob.data.transform(Matrix.Translation((x, y, AX_Z)))
        v, f = lathe(prof, n=64, axis="y")
        place(B.make(f"wheel_tyre_{corner}", v, f, "rubber"))
        v, f = lathe(barrel, n=64, axis="y")
        place(B.make(f"wheel_rim_{corner}", v, f, "rim"))
        disc = Point(0, 0).buffer(RIM_R - 0.045, 48)
        yd = -s * 0.01
        ob = prism(f"wheel_disc_{corner}", disc, "y", yd - 0.012, yd + 0.012, "brake")
        place(ob)
        face = s * (hw - 0.03)
        for k in picks:
            pref = "" if game else f"opt_rim_{k}_"
            sp = spoke_design(k)
            ob = prism(f"{pref}wheel_spokes_{corner}", sp, "y", face - 0.022 * s, face, "rim", 0.04)
            place(ob)
            cap = Point(0, 0).buffer(0.035, 32)
            ob = prism(f"{pref}wheel_cap_{corner}", cap, "y", face - 0.003 * s, face + 0.006 * s, "badge", 0.01)
            place(ob)


# ---------------------------------------------------------------- badges and name

def badge_shape(size):
    """The Kaiser badge from src/renderer/assets/brands/kaiser/kaiser-badge-colour.svg: a ring
    and a stem cut by three slits, scaled to size (its height), centred, y up."""
    ring = Point(90, 100).buffer(52, 64).difference(Point(90, 100).buffer(26, 64))
    q = lambda p0, c, p1, n=12: [tuple((1 - t) ** 2 * np.array(p0) + 2 * (1 - t) * t * np.array(c) + t * t * np.array(p1))
                                 for t in np.linspace(0, 1, n)]
    stem = Polygon([(128, 48), (154, 48), (154, 124)] + q((154, 124), (154, 136), (168, 136)) + [(174, 136), (174, 152), (160, 152)]
                   + q((160, 152), (128, 152), (128, 122)))
    g = unary_union([ring, stem])
    g = affinity.affine_transform(g, [1.15, 0, 0, 1.15, -23, -15])
    for y0 in (80, 100, 120):
        g = g.difference(sbox(96 + (y0 - 80) * 0.4, y0, 230, y0 + 9))
    g = affinity.scale(g, 1, -1, origin=(0, 0))
    minx, miny, maxx, maxy = g.bounds
    g = affinity.translate(g, -(minx + maxx) / 2, -(miny + maxy) / 2)
    return affinity.scale(g, size / (maxy - miny), size / (maxy - miny), origin=(0, 0))


def name_shape(height):
    data = json.loads((HERE / "glyphs.json").read_text())
    polys = [Polygon(p[0], p[1:]).buffer(0) for p in data["pieces"]]
    g = unary_union(polys)
    k = height / data["cap"]
    g = affinity.scale(g, k, k, origin=(0, 0))
    minx, miny, maxx, maxy = g.bounds
    return affinity.translate(g, -(minx + maxx) / 2, -(miny + maxy) / 2)


def flat(bvh, name, shape, fr, a, b, up, mat, d0, d1, step):
    """Lay a flat shape on its panel along the panel's normal at its centre."""
    pf = B.panel_frame(bvh, fr, a, b, up)
    return B.lay(bvh, name, shape, pf, d0, d1, mat, step)


def badges(bvh, opts, game):
    bf = {1: (0.0, float(Z(0.482))),    # reference: on the nose above the grille
          2: (0.0, float(Z(0.408)))}    # in the grille band
    br = {1: (0.0, float(Z(0.585))),    # reference: centre of the hatch, between the lamps
          2: (0.0, float(Z(0.50)))}     # lower on the hatch
    nm = {1: (float(W(0.30)), float(Z(0.575))),    # reference: left of the hatch, beside the lamp
          2: (-float(W(0.30)), float(Z(0.575))),   # right of the hatch
          3: (0.0, float(Z(0.50)))}                # centred low on the hatch
    for grp, places, fr, shape, mat, d in (
            ("badgef", bf, FRONT, badge_shape(0.075), "badge", (0.0005, 0.006)),
            ("badger", br, REAR, badge_shape(0.075), "badge", (0.0005, 0.006)),
            ("name", nm, REAR, name_shape(0.016), "badge", (0.0005, 0.003))):
        for k, (a, b) in places.items():
            if game and opts.get(grp, 1) != k:
                continue
            pref = "" if game else f"opt_{grp}_{k}_"
            flat(bvh, f"{pref}{grp}", shape, fr, a, b, (0, 0, 1), mat, d[0] - 0.001, d[1], 0.003)


def build(opts, game):
    ob, ref, bvh = body_pieces(opts)
    lamps(bvh)
    grille(bvh)
    wheels(opts, game)
    badges(bvh, opts, game)
    bpy.data.objects.remove(ref)
    for o in list(bpy.context.scene.objects):
        if o.type == "MESH" and not o.data.polygons:
            bpy.data.objects.remove(o)
