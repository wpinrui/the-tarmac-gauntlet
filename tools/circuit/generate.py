"""Builds the circuit data the Unity raceday scene reads.

Run:  python3 tools/circuit/generate.py
Writes unity/Assets/Raceday/Resources/Tracks/circuit.json.

The lap is a list of straights and constant-radius arcs. Two straights have their
lengths solved so the loop closes exactly. Headings are compass degrees and the
lap runs clockwise. World axes in the output: x east, y up, z north (metres).
"""
import json
import math
import os

STEP = 2.0  # metres between output samples

# ('S', length | 'A' | 'B', elevation at end)
# ('C', radius, target heading, 'L' | 'R', elevation at end, corner id)
# Corner ids are build-order ids; they are renumbered from the start line below.
SEGMENTS = [
    ('S', 350, 100),                                   # chicane exit to hairpin
    ('C', 16, 190, 'R', 99, 'hairpin'),
    ('S', 450, 50),
    ('C', 180, 160, 'L', 56, 'compression'), ('C', 150, 200, 'R', 72, 'compression'),
    ('C', 200, 125, 'L', 88, 'compression'),
    ('S', 90, 91),
    ('C', 100, 90, 'L', 93, 'esses1'), ('C', 100, 160, 'R', 94, 'esses1'),
    ('S', 60, 95),
    ('C', 100, 90, 'L', 96, 'esses2'), ('C', 100, 125, 'R', 97, 'esses2'),
    ('S', 1050, 104),                                  # long straight
    ('C', 40, 205, 'R', 103, 'top1'), ('S', 70, 101), ('C', 45, 150, 'L', 98, 'top2'),
    ('S', 450, 82),
    ('C', 70, 250, 'R', 72, 'far1'),
    ('S', 350, 62),
    ('C', 35, 345, 'R', 58, 'far2'),
    ('S', 500, 46),
    ('C', 140, 305, 'L', 40, 'doubleleft'), ('S', 90, 34), ('C', 110, 265, 'L', 28, 'doubleleft'),
    ('S', 'B', 22),
    ('C', 90, 350, 'R', 22, 'lastcorner'),
    ('S', 'A', 35),                                    # pit straight, under 2% climb
    ('C', 260, 20, 'R', 44, 'kink'),
    ('S', 450, 82),
    ('C', 22, 85, 'R', 85, 'chicane'), ('C', 22, 15, 'L', 87, 'chicane'), ('C', 45, 20, 'R', 88, 'chicane'),
]
START_HEADING = 20.0
ORDER = ['kink', 'chicane', 'hairpin', 'compression', 'esses1', 'esses2',
         'top1', 'top2', 'far1', 'far2', 'doubleleft', 'lastcorner']
OFF_CAMBER = {'compression'}

ROAD = 12.0
GRID_ROAD = 15.0          # start straight and grid, through the first corner
CARS = 100                # garages
GRID_SLOTS = 30           # painted grid slots, staggered 1x1, pole on the left
GRID_PITCH = 8.0          # metres of grid per car
GRID_SLOT_WIDTH = 2.7
GRID_SLOT_LENGTH = 3.0    # side lines (no official figure found)
GRID_LANE = 3.5           # slot centres either side of the track centre
PIT_LANE = 12.0
FAST_LANE = 3.5
LANE_OFFSET = 30.0        # pit lane centre from track centre
GARAGE_DEPTH = 16.0
START_TO_STRAIGHT_END = 451.0  # start line position on the pit straight


def walk(a, b):
    x = y = 0.0
    h = math.radians(START_HEADING)
    pts = [(x, y, h)]
    segs = []
    for s in SEGMENTS:
        if s[0] == 'S':
            length = {'A': a, 'B': b}.get(s[1], s[1])
            dh = 0.0
        else:
            r, target, d = s[1], s[2], s[3]
            delta = (target - math.degrees(h)) % 360
            if d == 'L':
                delta -= 360
            dh = math.radians(delta)
            length = r * abs(dh)
        n = max(1, int(math.ceil(length / 0.5)))
        for _ in range(n):
            h += dh / n / 2
            x += length / n * math.sin(h)
            y += length / n * math.cos(h)
            h += dh / n / 2
            pts.append((x, y, h))
        segs.append({'kind': s[0], 'len': length, 'elev': s[2] if s[0] == 'S' else s[4],
                     'id': None if s[0] == 'S' else s[5], 'radius': None if s[0] == 'S' else s[1],
                     'turn': math.degrees(dh)})
    return x, y, pts, segs


def solve():
    x0, y0, _, _ = walk(0, 0)
    xa, ya, _, _ = walk(1, 0)
    xb, yb, _, _ = walk(0, 1)
    ax, ay, bx, by = xa - x0, ya - y0, xb - x0, yb - y0
    det = ax * by - ay * bx
    return (-x0 * by + y0 * bx) / det, (-ax * y0 + ay * x0) / det


def smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def build_terrain(np, P, E, W, cross, lane, lane_d, step, garages, spacing=8.0, margin=700.0):
    """Ground heights on a regular grid. Flat with the track out to the runoff,
    then easing into rolling hills further away."""
    import base64
    pts = np.concatenate([P, np.array([[p[0], p[1]] for p in lane]),
                          np.array([[g['x'], g['z']] for g in garages])])
    x0, z0 = pts[:, 0].min() - margin, pts[:, 1].min() - margin
    nx = int((pts[:, 0].max() + margin - x0) / spacing) + 1
    nz = int((pts[:, 1].max() + margin - z0) / spacing) + 1
    gx = x0 + np.arange(nx) * spacing
    gz = z0 + np.arange(nz) * spacing
    GX, GZ = np.meshgrid(gx, gz)
    flat = np.stack([GX.ravel(), GZ.ravel()], axis=1)
    # Surfaces the ground has to follow: the track, a second stretch of track where the lap
    # doubles back on itself (hairpins, parallel straights), the pit lane and the garages.
    # Each one's height at a ground node is its own surface extended outwards at the runoff's
    # 1.5% fall, measured at its nearest point (so a steep road does not drag the ground down
    # from further along its own length). Between surfaces the ground blends from one to the
    # other, weighted heavily towards the nearest, so it slopes like an embankment.
    T = np.asarray(P)[:, :2]
    TH = np.asarray(E)
    CF = np.asarray(cross)
    HALF = np.asarray(W) / 2
    tg = np.roll(T, -1, axis=0) - np.roll(T, 1, axis=0)
    tg /= np.linalg.norm(tg, axis=1)[:, None]
    LEFT = np.stack([-tg[:, 1], tg[:, 0]], axis=1)
    n_track = len(T)
    L = np.array([[p[0], p[1]] for p in lane])
    lk = (np.round(np.asarray(lane_d) / step).astype(int)) % n_track
    LH = TH[lk] + CF[lk] * HALF[lk]          # the lane is level with the track's left edge
    lane_half = PIT_LANE / 2 + 1.2
    G = np.array([[g['x'], g['z']] for g in garages])
    GH = np.array([g['y'] for g in garages])
    garage_half = 12.0     # reaches the garage corners
    fall = 0.015

    def track_plane(c, k):
        lat = ((c - T[k]) * LEFT[k]).sum(axis=1)
        half = HALF[k]
        return (TH[k] + CF[k] * np.clip(lat, -half, half) - fall * np.clip(np.abs(lat) - half, 0, None),
                np.sqrt(((c - T[k]) ** 2).sum(axis=1)) - half)

    near_d = np.empty(len(flat))
    near_h = np.empty(len(flat))
    far = np.empty(len(flat))
    for i in range(0, len(flat), 2048):
        c = flat[i:i + 2048]
        rows = np.arange(len(c))
        cands = []
        dt2 = ((c[:, None, :] - T[None, :, :]) ** 2).sum(axis=2)
        k1 = dt2.argmin(axis=1)
        cands.append(track_plane(c, k1))
        gap = np.abs(np.arange(n_track)[None, :] - k1[:, None])
        gap = np.minimum(gap, n_track - gap)
        k2 = np.where(gap > 50, dt2, np.inf).argmin(axis=1)
        cands.append(track_plane(c, k2))
        for S, SH, half in ((L, LH, lane_half), (G, GH, garage_half)):
            d2 = ((c[:, None, :] - S[None, :, :]) ** 2).sum(axis=2)
            k = d2.argmin(axis=1)
            beyond = np.sqrt(d2[rows, k]) - half
            cands.append((SH[k] - fall * np.clip(beyond, 0, None), beyond))
        H = np.stack([q[0] for q in cands], axis=1)
        B = np.stack([q[1] for q in cands], axis=1)
        near_d[i:i + 2048] = np.sqrt(dt2[rows, k1])
        w = 1.0 / (np.clip(B, 0, None) + 1.0) ** 4
        blend = (w * H).sum(axis=1) / w.sum(axis=1)
        # Never above the edge of a road within a grid cell of it, so no ground shows through it
        # or its verge.
        close = np.where(B < 10.0, H + fall * np.clip(B, 0, None), np.inf).min(axis=1)
        near_h[i:i + 2048] = np.minimum(blend, close)
        wf = 1.0 / (dt2[:, ::2] + 150.0 ** 2)
        far[i:i + 2048] = (wf * TH[None, ::2]).sum(axis=1) / wf.sum(axis=1)
    flat_zone = 60.0
    blend = np.clip((near_d - flat_zone) / 220.0, 0, 1)
    blend = blend * blend * (3 - 2 * blend)
    hills = (9 * np.sin(flat[:, 0] / 210 + 1.3) * np.cos(flat[:, 1] / 260 - 0.4)
             + 5 * np.sin(flat[:, 0] / 97 - flat[:, 1] / 131 + 2.1)
             + 3 * np.cos(flat[:, 0] / 53 + flat[:, 1] / 71))
    reach = np.clip((near_d - flat_zone) / 600.0, 0, 1)
    # Close to the track the ground sits just under the road and runoff planes. Runoff and
    # barriers are laid on the ground itself, so they follow it wherever it blends.
    under = near_h - 0.15
    h = under * (1 - blend) + (far + hills * (0.4 + 1.6 * reach)) * blend
    lo = float(h.min()) - 1.0
    q = np.clip(np.round((h - lo) * 100), 0, 65535).astype('<u2')
    return {'originX': round(float(x0), 2), 'originZ': round(float(z0), 2), 'spacing': spacing,
            'countX': nx, 'countZ': nz, 'baseHeight': round(lo, 2), 'heightsCm': base64.b64encode(q.tobytes()).decode()}


def main():
    a, b = solve()
    _, _, raw, segs = walk(a, b)
    lap = sum(s['len'] for s in segs)

    # Segment boundaries along the lap.
    d = 0.0
    for s in segs:
        s['s'], s['e'] = d, d + s['len']
        d += s['len']

    # Resample raw points (0.5 m apart) every STEP metres.
    rd = [0.0]
    for i in range(1, len(raw)):
        rd.append(rd[-1] + math.hypot(raw[i][0] - raw[i - 1][0], raw[i][1] - raw[i - 1][1]))
    n = int(round(lap / STEP))
    step = lap / n
    X, Y, H = [], [], []
    j = 0
    for k in range(n):
        dd = k * step
        while j < len(rd) - 2 and rd[j + 1] < dd:
            j += 1
        t = (dd - rd[j]) / max(1e-9, rd[j + 1] - rd[j])
        X.append(raw[j][0] + (raw[j + 1][0] - raw[j][0]) * t)
        Y.append(raw[j][1] + (raw[j + 1][1] - raw[j][1]) * t)
        H.append(raw[j][2] + (raw[j + 1][2] - raw[j][2]) * t)

    # Elevation: piecewise linear between segment ends, then smoothed twice.
    keys = [(0.0, segs[-1]['elev'])] + [(s['e'], s['elev']) for s in segs]

    def lin(dd):
        for k in range(1, len(keys)):
            if dd <= keys[k][0] + 1e-9:
                d0, e0 = keys[k - 1]
                d1, e1 = keys[k]
                return e0 + (e1 - e0) * (dd - d0) / max(1e-9, d1 - d0)
        return keys[-1][1]

    E = [lin(k * step) for k in range(n)]
    for half in (40, 40):
        w = int(half / step)
        pref = [0.0]
        ext = E[-w:] + E + E[:w]
        for v in ext:
            pref.append(pref[-1] + v)
        E = [(pref[k + 2 * w + 1] - pref[k]) / (2 * w + 1) for k in range(n)]

    # Corners in lap order from the start line, numbered from 1.
    corners = {}
    for s in segs:
        if s['kind'] == 'C':
            c = corners.setdefault(s['id'], {'s': s['s'], 'e': s['e'], 'radius': s['radius'], 'arcs': []})
            c['e'] = s['e']
            c['radius'] = min(c['radius'], s['radius'])
            c['arcs'].append({'s': s['s'], 'e': s['e'], 'dir': 1 if s['turn'] > 0 else -1, 'radius': s['radius']})

    pit_straight = next(s for s in segs if s['kind'] == 'S' and abs(s['len'] - a) < 1e-6)
    b_straight = next(s for s in segs if s['kind'] == 'S' and abs(s['len'] - b) < 1e-6)
    start = pit_straight['e'] - START_TO_STRAIGHT_END
    shift = int(round(start / step))
    start = shift * step

    def nd(v):
        return (v - start) % lap

    X = X[shift:] + X[:shift]
    Y = Y[shift:] + Y[:shift]
    H = H[shift:] + H[:shift]
    E = E[shift:] + E[:shift]

    out_corners = []
    for num, cid in enumerate(ORDER, 1):
        c = corners[cid]
        out_corners.append({
            'number': num, 's': round(nd(c['s']), 2), 'e': round(nd(c['e']), 2),
            'radius': c['radius'], 'offCamber': cid in OFF_CAMBER,
            'arcs': [{'s': round(nd(x['s']), 2), 'e': round(nd(x['e']), 2), 'dir': x['dir'], 'radius': x['radius']}
                     for x in c['arcs']],
        })

    # Turn direction per sample (+1 right, -1 left, 0 straight) for crossfall.
    turn = [0] * n
    for c in out_corners:
        for arc in c['arcs']:
            k0, k1 = int(arc['s'] / step), int(arc['e'] / step)
            for k in range(k0, k1 + 1):
                turn[k % n] = arc['dir']

    # Crossfall: rise of the left edge over the right edge, per metre of width.
    # Straights fall 1.5% to the right; corners bank 3% into the turn; off-camber
    # corners lean 4% the other way. Blended over 40 m.
    target = []
    off = [False] * n
    for c in out_corners:
        if c['offCamber']:
            for k in range(int(c['s'] / step) - 10, int(c['e'] / step) + 10):
                off[k % n] = True
    for k in range(n):
        if turn[k] == 0:
            target.append(0.015)
        elif off[k]:
            target.append(-0.04 if turn[k] > 0 else 0.04)
        else:
            target.append(0.03 if turn[k] > 0 else -0.03)
    w = int(20 / step)
    cross = [sum(target[(k + i) % n] for i in range(-w, w + 1)) / (2 * w + 1) for k in range(n)]

    # Width: 15 m from the back of the grid through the first corner's exit.
    grid_len = GRID_SLOTS * GRID_PITCH
    first = out_corners[0]
    wide0, wide1 = (-grid_len - 40.0) % lap, first['e'] + 30.0
    width = []
    for k in range(n):
        dd = k * step
        dist_in = (dd - wide0) % lap
        span = (wide1 - wide0) % lap
        if dist_in <= span:
            t = min(smoothstep(dist_in / 60.0), smoothstep((span - dist_in) / 60.0))
        else:
            t = 0.0
        width.append(ROAD + (GRID_ROAD - ROAD) * t)

    # Pit lane: leaves after the double left, runs on the left (outside) of the
    # last corner and the pit straight, bypasses the chicane and rejoins after
    # the hairpin.
    def frame(dd):
        k = int(round(dd / step)) % n
        k0, k1 = (k - 1) % n, (k + 1) % n
        tx, ty = X[k1] - X[k0], Y[k1] - Y[k0]
        m = math.hypot(tx, ty)
        return k, tx / m, ty / m

    def left(dd, o):
        k, tx, ty = frame(dd)
        return [X[k] - ty * o, Y[k] + tx * o, E[k]]

    lane_in = nd(b_straight['s']) + 38
    garages_s = nd(b_straight['s']) + 188
    garages_e = nd(pit_straight['e']) - 121
    if garages_e < garages_s:
        garages_e += lap
    chicane = out_corners[1]
    hairpin = out_corners[2]
    bypass_s = chicane['s'] - 20 + lap
    bypass_e = chicane['e'] + 40 + lap
    lane_out = hairpin['e'] + 200 + lap
    p0, p1 = left(bypass_s, LANE_OFFSET), left(bypass_e, LANE_OFFSET)
    lane = []
    lane_d = []
    dd = lane_in
    while dd <= lane_out + 1e-6:
        o = LANE_OFFSET * smoothstep((dd - lane_in) / 300) * smoothstep((lane_out - dd) / 300)
        if bypass_s < dd < bypass_e:
            t = (dd - bypass_s) / (bypass_e - bypass_s)
            lane.append([p0[i] + (p1[i] - p0[i]) * t for i in range(3)])
        else:
            lane.append(left(dd, o))
        lane_d.append(dd % lap)
        dd += step

    # Garages on the far side of the lane, evenly spaced by length.
    gl_off = LANE_OFFSET + PIT_LANE / 2 + 1 + GARAGE_DEPTH / 2
    line = [left(garages_s + i * 1.0, gl_off) for i in range(int(garages_e - garages_s) + 1)]
    acc = [0.0]
    for i in range(1, len(line)):
        acc.append(acc[-1] + math.hypot(line[i][0] - line[i - 1][0], line[i][1] - line[i - 1][1]))
    bay = acc[-1] / CARS
    garages = []
    i = 0
    for g in range(CARS):
        target_len = (g + 0.5) * bay
        while acc[i] < target_len:
            i += 1
        p, q, r = line[i], line[min(i + 1, len(line) - 1)], line[max(i - 1, 0)]
        heading = math.degrees(math.atan2(q[0] - r[0], q[1] - r[1]))
        # Garage floors are level with the pit lane, which is level with the track's left edge.
        kg = int(round((garages_s + i * 1.0) / step)) % n
        floor_y = E[kg] + cross[kg] * width[kg] / 2
        garages.append({'x': round(p[0], 2), 'z': round(p[1], 2), 'y': round(floor_y, 2), 'heading': round(heading, 2)})

    # --- Clearance: how far each side can spread before meeting another part of
    # the circuit (another stretch of track, the pit lane or the garages).
    import numpy as np
    P = np.array([[X[k], Y[k]] for k in range(n)])
    Tg = np.zeros_like(P)
    Tg[:, 0] = np.roll(P[:, 0], -1) - np.roll(P[:, 0], 1)
    Tg[:, 1] = np.roll(P[:, 1], -1) - np.roll(P[:, 1], 1)
    Tg /= np.linalg.norm(Tg, axis=1)[:, None]
    Lft = np.stack([-Tg[:, 1], Tg[:, 0]], axis=1)          # left normal
    W = np.array(width)
    lane_xy = np.array([[p[0], p[1]] for p in lane])
    gar_xy = np.array([[g['x'], g['z']] for g in garages])
    obstacles = [(P, W / 2, True), (lane_xy, np.full(len(lane_xy), PIT_LANE / 2 + 1.0), False),
                 (gar_xy, np.full(len(gar_xy), GARAGE_DEPTH / 2 + 2.0), False)]
    idx = np.arange(n)
    clear = {}
    for side, sign in (('left', 1.0), ('right', -1.0)):
        best = np.full(n, 400.0)
        nrm = Lft * sign
        for pts, rad, is_track in obstacles:
            for k0 in range(0, n, 256):
                ks = idx[k0:k0 + 256]
                v = pts[None, :, :] - P[ks][:, None, :]
                lat = np.einsum('ijk,ik->ij', v, nrm[ks])
                along = np.einsum('ijk,ik->ij', v, Tg[ks])
                ok = (lat > 0) & (np.abs(along) < lat * 0.6 + 10)
                if is_track:
                    gap = np.abs(ks[:, None] - idx[None, :])
                    gap = np.minimum(gap, n - gap) * step
                    ok &= gap > 250
                dist = np.where(ok, lat - rad[None, :], 1e9)
                best[ks] = np.minimum(best[ks], dist.min(axis=1))
        # Track meets track: share the gap. Anything else: keep 3 m short of it.
        clear[side] = np.maximum(0.0, best - W / 2)
    # Gaps between two stretches of track are shared down the middle.
    for side in ('left', 'right'):
        clear[side] = np.minimum(clear[side], 400.0)

    terrain = build_terrain(np, P, np.array(E), W, cross, lane, lane_d, step, garages)

    def r2(v):
        return round(v, 3)

    data = {
        'lapLength': round(lap, 2),
        'step': round(step, 6),
        'centre': [r2(v) for k in range(n) for v in (X[k], E[k], Y[k])],
        'width': [round(v, 2) for v in width],
        'clearLeft': [round(float(v), 1) for v in clear['left']],
        'clearRight': [round(float(v), 1) for v in clear['right']],
        'crossfall': [round(v, 4) for v in cross],
        'corners': [{k: v for k, v in c.items()} for c in out_corners],
        'grid': {
            'slotWidth': GRID_SLOT_WIDTH, 'slotLength': GRID_SLOT_LENGTH, 'yellowLength': 4.0,
            'slots': [{'d': round((-2.0 - i * GRID_PITCH) % lap, 2), 'lateral': GRID_LANE if i % 2 == 0 else -GRID_LANE}
                      for i in range(GRID_SLOTS)],
        },
        'safetyCarLines': [round((lane_in - 60) % lap, 2), round((lane_out + 40) % lap, 2)],
        'terrain': terrain,
        'pit': {
            'side': 'left',
            'laneWidth': PIT_LANE,
            'fastLane': FAST_LANE,
            'limiterKph': 100,
            'entry': round(lane_in, 2),
            'exit': round(lane_out % lap, 2),
            'limitStart': round(garages_s - 60, 2),
            'limitEnd': round((garages_e + 60) % lap, 2),
            'garageDepth': GARAGE_DEPTH,
            'garageWidth': round(bay, 3),
            'lane': [r2(v) for p in lane for v in (p[0], p[2], p[1])],
            'laneTrackDistance': [round(v, 2) for v in lane_d],
            'garages': garages,
        },
    }
    here = os.path.dirname(os.path.abspath(__file__))
    path = os.path.join(here, '..', '..', 'unity', 'Assets', 'Raceday', 'Resources', 'Tracks', 'circuit.json')
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w') as f:
        json.dump(data, f, separators=(',', ':'))
    grades = [(E[(k + 5) % n] - E[k]) / (5 * step) for k in range(n)]
    sk = int(round(nd(pit_straight['s']) / step)) + 25
    ek = int(round(nd(pit_straight['e']) / step)) - 25
    if ek < sk:
        ek += n
    straight_grade = max(abs(grades[k % n]) for k in range(sk, ek))
    print(f'lap {lap:.0f} m, A {a:.0f}, B {b:.0f}, elevation {min(E):.0f}..{max(E):.0f} m, '
          f'grade {min(grades)*100:.1f}%..{max(grades)*100:.1f}%, start straight max {straight_grade*100:.1f}%, '
          f'garage bay {bay:.1f} m')


if __name__ == '__main__':
    main()
