"""The Scottish thistle, hand painted: a spiny green head under a flared purple crown, on a winged
stem with deeply lobed, spine-tipped leaves, and a closed bud on a side shoot.

thistle(seed) returns an SVG group in metres for a thistle 1 m tall: origin at the foot of the
stem, up is -y. Scale and place it with a transform. Every shape is jittered a little, as a
signwriter's brush would leave it.
"""
import math
import random

# Greens: shadow, body, light. Purples: shadow, body, bright, tips.
G_DARK, G_MID, G_LIGHT, G_PALE = "#22381c", "#3f6630", "#6f9a48", "#a9c77e"
P_DARK, P_MID, P_BRIGHT, P_TIP = "#3d1457", "#6e2a90", "#9d4fc0", "#d7a6ec"


def _f(x):
    return f"{x:.4f}"


def _path(pts, close=True):
    return "M" + " L".join(f"{_f(x)},{_f(y)}" for x, y in pts) + (" Z" if close else "")


def _smooth(pts, close=True):
    """Catmull-Rom through the points, as cubic Beziers."""
    n = len(pts)
    d = f"M{_f(pts[0][0])},{_f(pts[0][1])}"
    rng = range(n) if close else range(n - 1)
    for i in rng:
        p0 = pts[(i - 1) % n] if close or i > 0 else pts[i]
        p1, p2 = pts[i], pts[(i + 1) % n]
        p3 = pts[(i + 2) % n] if close or i + 2 < n else p2
        c1 = (p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6)
        c2 = (p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6)
        d += f" C{_f(c1[0])},{_f(c1[1])} {_f(c2[0])},{_f(c2[1])} {_f(p2[0])},{_f(p2[1])}"
    return d + (" Z" if close else "")


def _bez(p0, p1, p2, t):
    a = (1 - t) ** 2
    b = 2 * (1 - t) * t
    c = t * t
    return (a * p0[0] + b * p1[0] + c * p2[0], a * p0[1] + b * p1[1] + c * p2[1])


def _bez_dir(p0, p1, p2, t):
    dx = 2 * (1 - t) * (p1[0] - p0[0]) + 2 * t * (p2[0] - p1[0])
    dy = 2 * (1 - t) * (p1[1] - p0[1]) + 2 * t * (p2[1] - p1[1])
    n = math.hypot(dx, dy) or 1
    return dx / n, dy / n


def stem(r, p0, p1, p2, w0, w1, out):
    """A tapered stem along a quadratic curve, with ragged spiny wings down both edges."""
    left, right = [], []
    N = 40
    for i in range(N + 1):
        t = i / N
        x, y = _bez(p0, p1, p2, t)
        dx, dy = _bez_dir(p0, p1, p2, t)
        nx, ny = -dy, dx
        w = w0 + (w1 - w0) * t
        # The wing: a saw edge, a tooth every few steps.
        tooth = (0.010 if i % 4 == 2 else 0.0) * (1 - t * 0.5)
        left.append((x + nx * (w / 2 + tooth), y + ny * (w / 2 + tooth)))
        right.append((x - nx * (w / 2 + tooth), y - ny * (w / 2 + tooth)))
    out.append(f'<path d="{_path(left + right[::-1])}" fill="{G_MID}"/>')
    # A light rib down one side.
    rib = [_bez(p0, p1, p2, i / N) for i in range(N + 1)]
    rib = [(x + 0.004, y) for x, y in rib]
    out.append(f'<path d="{_path(rib, False)}" fill="none" stroke="{G_LIGHT}" stroke-width="0.006" stroke-linecap="round" opacity=".8"/>')


def leaf(r, base, angle, length, width, flip, out):
    """A deeply lobed thistle leaf from base along angle (radians, 0 = right, -pi/2 = up),
    curling up at the tip. Each lobe ends in a long pale spine. flip mirrors the curl."""
    # Midrib: a gentle arc.
    ca, sa = math.cos(angle), math.sin(angle)
    curl = -0.35 * flip
    rib = []
    for i in range(25):
        t = i / 24
        a = angle + curl * t * t
        if i == 0:
            x, y = base
        else:
            x, y = rib[-1]
            x += math.cos(a) * length / 24
            y += math.sin(a) * length / 24
        rib.append((x, y))
    upper, lower, spines = [], [], []
    lobes = 5
    for side, edge in ((1, upper), (-1, lower)):
        for i in range(25):
            t = i / 24
            x, y = rib[i]
            j = min(i, 23)
            dx, dy = rib[j + 1][0] - rib[j][0], rib[j + 1][1] - rib[j][1]
            n = math.hypot(dx, dy)
            nx, ny = -dy / n * side, dx / n * side
            env = math.sin(math.pi * (0.04 + 0.96 * t)) ** 0.6 * width * (1 - 0.3 * t)
            # Lobes: deep cuts between points that sweep forward.
            ph = (t * lobes + (0.0 if side > 0 else 0.5)) % 1
            lobe = 0.25 + 0.75 * (1 - abs(ph - 0.75) / 0.75) ** 2.2
            w = env * lobe * (0.92 + r.random() * 0.16)
            fx, fy = dx / n, dy / n
            px, py = x + nx * w + fx * w * 0.35, y + ny * w + fy * w * 0.35
            edge.append((px, py))
            if abs(ph - 0.75) < 0.5 / 24 * lobes and 0.1 < t < 0.97:
                sl = 0.035 + r.random() * 0.02
                spines.append(((px, py), (px + (nx * 0.7 + fx * 0.7) * sl, py + (ny * 0.7 + fy * 0.7) * sl)))
    shape = upper + [rib[-1]] + lower[::-1]
    out.append(f'<path d="{_path(shape)}" fill="{G_MID}" stroke="{G_DARK}" stroke-width="0.004" stroke-linejoin="round"/>')
    # Shade the lower half, highlight the upper, as paint laid on with two loads of the brush.
    out.append(f'<path d="{_path(rib + lower[::-1])}" fill="{G_DARK}" opacity=".35"/>')
    hl = [(x + (ux - x) * 0.45, y + (uy - y) * 0.45) for (x, y), (ux, uy) in zip(rib, upper)]
    out.append(f'<path d="{_path(rib + hl[::-1])}" fill="{G_LIGHT}" opacity=".45"/>')
    out.append(f'<path d="{_path(rib, False)}" fill="none" stroke="{G_PALE}" stroke-width="0.007" stroke-linecap="round"/>')
    # Veins out to each lobe.
    for i in range(2, 24, 5):
        for edge in (upper, lower):
            k = min(24, i + 2)
            out.append(f'<path d="M{_f(rib[i][0])},{_f(rib[i][1])} L{_f(edge[k][0] * 0.8 + rib[k][0] * 0.2)},{_f(edge[k][1] * 0.8 + rib[k][1] * 0.2)}" '
                       f'stroke="{G_PALE}" stroke-width="0.0035" stroke-linecap="round" opacity=".7"/>')
    for (a, b) in spines:
        out.append(f'<path d="M{_f(a[0])},{_f(a[1])} L{_f(b[0])},{_f(b[1])}" stroke="{G_PALE}" stroke-width="0.004" stroke-linecap="round"/>')
    tip = rib[-1]
    dx, dy = rib[-1][0] - rib[-2][0], rib[-1][1] - rib[-2][1]
    n = math.hypot(dx, dy)
    out.append(f'<path d="M{_f(tip[0])},{_f(tip[1])} L{_f(tip[0] + dx / n * 0.05)},{_f(tip[1] + dy / n * 0.05)}" stroke="{G_PALE}" stroke-width="0.004" stroke-linecap="round"/>')


def bract(cx, cy, w, h, lean, r, fill, out, spine=True):
    """One scale of the head: a pointed leaf shape rising from (cx, cy), tip up and out by lean,
    ending in a spine that flicks outward."""
    tx, ty = cx + lean * h, cy - h
    pts = [(cx, cy + h * 0.08), (cx - w * 0.38, cy - h * 0.12), (cx - w / 2 + lean * h * 0.35, cy - h * 0.42), (tx, ty),
           (cx + w / 2 + lean * h * 0.35, cy - h * 0.42), (cx + w * 0.38, cy - h * 0.12)]
    out.append(f'<path d="{_path(pts)}" fill="{fill}" stroke="{G_DARK}" stroke-width="0.0035" stroke-linejoin="round"/>')
    out.append(f'<path d="M{_f(cx)},{_f(cy - h * 0.1)} Q{_f(cx + lean * h * 0.3)},{_f(cy - h * 0.5)} {_f(tx)},{_f(ty)}" '
               f'fill="none" stroke="{G_PALE}" stroke-width="0.003" opacity=".8"/>')
    if spine:
        sl = h * (0.45 + r.random() * 0.25)
        a = math.atan2(-1, lean * 2.2)
        out.append(f'<path d="M{_f(tx)},{_f(ty)} L{_f(tx + math.cos(a) * sl)},{_f(ty + math.sin(a) * sl)}" '
                   f'stroke="{G_PALE}" stroke-width="0.0035" stroke-linecap="round"/>')


def head(r, cx, cy, s, out, crown=True):
    """A thistle head with its base at (cx, cy), s the head's width. The involucre is a swollen
    cup of overlapping bracts; the crown is a flared brush of purple florets above it."""
    W, Hh = s, s * 0.95
    top = cy - Hh
    # Crown first, so the cup's top bracts lap over its foot.
    if crown:
        layers = [(P_DARK, 1.0, 70, 0.0105), (P_MID, 0.93, 80, 0.0085), (P_BRIGHT, 0.82, 70, 0.0065), (P_TIP, 0.62, 40, 0.004)]
        for col, reach, n, w in layers:
            for i in range(n):
                t = (i + r.random()) / n
                a = -math.pi / 2 + (t - 0.5) * math.radians(130)
                # Florets fan out from a line across the cup's mouth.
                bx = cx + (t - 0.5) * W * 0.55
                by = top + Hh * 0.08
                ln = s * (0.95 + r.random() * 0.35) * reach * (1 - 0.25 * abs(t - 0.5) * 2)
                bend = (t - 0.5) * 0.35
                ex, ey = bx + math.cos(a) * ln, by + math.sin(a) * ln
                mx, my = bx + math.cos(a - bend) * ln * 0.55, by + math.sin(a - bend) * ln * 0.55
                out.append(f'<path d="M{_f(bx)},{_f(by)} Q{_f(mx)},{_f(my)} {_f(ex)},{_f(ey)}" fill="none" stroke="{col}" '
                           f'stroke-width="{_f(w * (0.8 + r.random() * 0.4))}" stroke-linecap="round"/>')
                if col == P_TIP and r.random() < 0.7:
                    out.append(f'<circle cx="{_f(ex)}" cy="{_f(ey)}" r="{_f(w * 0.9)}" fill="#f1d8fb"/>')
    # The cup: a swollen ball, narrow at the neck.
    cup = []
    for i in range(36):
        a = i / 36 * 2 * math.pi
        x = math.sin(a) * W / 2
        y = -math.cos(a) * Hh / 2
        if y > 0:
            x *= 1 - 0.45 * (y / (Hh / 2)) ** 1.6
        cup.append((cx + x, cy - Hh / 2 + y))
    out.append(f'<path d="{_smooth(cup)}" fill="{G_MID}" stroke="{G_DARK}" stroke-width="0.004"/>')
    # Rows of bracts, bottom to top, each row offset; outer ones lean out.
    rows = 7
    # Top row first: each lower, outer scale laps over the foot of the one above.
    for k in reversed(range(rows)):
        t = k / (rows - 1)
        yy = cy - Hh * (0.06 + 0.80 * t)
        half = (W / 2) * math.sin(math.pi * (0.22 + 0.66 * t))
        n = 7
        for j in range(n):
            u = ((j + (0.5 if k % 2 else 0.0)) / (n - 1)) * 2 - 1
            if abs(u) > 1.02:
                continue
            # Round the ball: scales near the rim are seen edge on, so narrower.
            bx = cx + math.sin(u * math.pi / 2) * half
            bw = W * 0.2 * (1 - 0.45 * abs(u))
            bh = Hh * (0.36 + 0.08 * t) * (1 - 0.15 * abs(u))
            fill = G_LIGHT if (abs(u) < 0.5 and r.random() < 0.45) else G_MID
            bract(bx, yy, bw, bh, u * (0.25 + 0.9 * t), r, fill, out, spine=k >= 1)
    # The ruff: long spiny bracts flaring out sideways under the crown.
    for side in (-1, 1):
        for j in range(3):
            a = math.radians(-25 - j * 20) if side > 0 else math.pi - math.radians(-25 - j * 20)
            x0, y0 = cx + side * W * 0.36, top + Hh * 0.18 + j * 0.01
            ln = s * (0.32 - j * 0.05)
            x1, y1 = x0 + math.cos(a) * ln, y0 + math.sin(a) * ln
            out.append(f'<path d="M{_f(x0)},{_f(y0)} Q{_f((x0 + x1) / 2)},{_f(y0 - 0.005)} {_f(x1)},{_f(y1)}" '
                       f'stroke="{G_PALE}" stroke-width="0.004" stroke-linecap="round" fill="none"/>')


def bud(r, cx, cy, s, out):
    """A closed bud: the cup alone with a peep of purple at its tip."""
    out.append(f'<path d="M{_f(cx - s * 0.12)},{_f(cy - s * 0.9)} Q{_f(cx)},{_f(cy - s * 1.25)} {_f(cx + s * 0.12)},{_f(cy - s * 0.9)} Z" fill="{P_MID}"/>')
    for i in range(7):
        a = -math.pi / 2 + (i / 6 - 0.5) * 0.9
        out.append(f'<path d="M{_f(cx)},{_f(cy - s * 0.88)} L{_f(cx + math.cos(a) * s * 0.3)},{_f(cy - s * 0.88 + math.sin(a) * s * 0.3)}" '
                   f'stroke="{P_BRIGHT}" stroke-width="0.006" stroke-linecap="round"/>')
    head(r, cx, cy, s, out, crown=False)


def thistle(seed=1, roughen=True):
    """The whole plant, 1 m tall, foot of the stem at the origin."""
    r = random.Random(seed)
    out = []
    # Side shoot to the bud, behind the main stem.
    stem(r, (0.0, -0.30), (0.10, -0.50), (0.20, -0.60), 0.022, 0.014, out)
    # Main stem, a slight lean.
    stem(r, (0.0, 0.0), (-0.03, -0.35), (0.02, -0.66), 0.036, 0.022, out)
    # Leaves, alternating up the stem, the lowest the longest.
    leaf(r, (0.0, -0.08), math.radians(-160), 0.42, 0.10, -1, out)
    leaf(r, (0.0, -0.14), math.radians(-25), 0.38, 0.095, 1, out)
    leaf(r, (-0.01, -0.36), math.radians(-150), 0.30, 0.08, -1, out)
    leaf(r, (0.06, -0.46), math.radians(-15), 0.22, 0.06, 1, out)
    bud(r, 0.20, -0.58, 0.12, out)
    head(r, 0.02, -0.64, 0.26, out)
    body = "".join(out)
    if roughen:
        # A slight wobble on every edge, the hand of the painter.
        return (f'<filter id="thistle-hand" x="-10%" y="-10%" width="120%" height="120%"><feTurbulence type="fractalNoise" '
                f'baseFrequency="60" numOctaves="2" seed="{seed}"/><feDisplacementMap in="SourceGraphic" scale="0.006"/></filter>'
                f'<g filter="url(#thistle-hand)">{body}</g>')
    return f"<g>{body}</g>"


if __name__ == "__main__":
    import sys
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="1000" height="1000" viewBox="-0.5 -1.15 1 1.2">'
           f'<rect x="-0.5" y="-1.15" width="1" height="1.2" fill="#e8dfc6"/>{thistle()}</svg>')
    open(sys.argv[1] if len(sys.argv) > 1 else "thistle.svg", "w").write(svg)
