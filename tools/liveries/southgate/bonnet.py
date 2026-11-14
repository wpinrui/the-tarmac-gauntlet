"""Southgate Motorsport's bonnet centrepieces: Kenyan animals, each a full illustration drawn as one SVG in a
1100 by 880 box (1.1 m across the bonnet, 0.88 m along it), upright for someone at the nose.
design.py lays the chosen one on the bonnet. Writes them to out/bonnet/.

Run: python3 bonnet.py
"""
import math
import random
from pathlib import Path

HERE = Path(__file__).resolve().parent
OUT = HERE / "out" / "bonnet"

BLACK = "#0b0b0c"
GOLD = "#c9a24a"
GOLD_L = "#e8c872"
GOLD_D = "#8f6f2a"
RED = "#b5121b"
GREEN = "#006b3f"
BONE = "#f4f1e8"
BEAD_B = "#1f5fbf"
BEAD_O = "#e2701e"
W, H = 1100, 880


def svg(body, defs=""):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}">'
            f'<defs>{defs}</defs>{"".join(body)}</svg>')


def path(pts, fill, close=True, extra=""):
    d = "M" + " L".join(f"{x:.1f},{y:.1f}" for x, y in pts) + (" Z" if close else "")
    return f'<path d="{d}" fill="{fill}" {extra}/>'


def poly_line(pts, stroke, w, extra=""):
    d = "M" + " L".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    return f'<path d="{d}" fill="none" stroke="{stroke}" stroke-width="{w}" stroke-linecap="round" stroke-linejoin="round" {extra}/>'


def circle(cx, cy, r, fill, extra=""):
    return f'<circle cx="{cx:.1f}" cy="{cy:.1f}" r="{r:.1f}" fill="{fill}" {extra}/>'


def arc_pts(cx, cy, r, a0, a1, n=64):
    return [(cx + r * math.cos(a0 + (a1 - a0) * k / n), cy + r * math.sin(a0 + (a1 - a0) * k / n)) for k in range(n + 1)]


# --- shared pieces ----------------------------------------------------------------------------
def bezier(p0, p1, p2, p3, n=60):
    out = []
    for k in range(n + 1):
        t = k / n
        u = 1 - t
        out.append((u ** 3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t ** 3 * p3[0],
                    u ** 3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t ** 3 * p3[1]))
    return out


def ribbon(pts, w0, w1):
    """A band along pts, w0 wide at the start tapering to w1 at the end, as one outline."""
    left, right = [], []
    for k, (x, y) in enumerate(pts):
        a, b = pts[max(k - 1, 0)], pts[min(k + 1, len(pts) - 1)]
        dx, dy = b[0] - a[0], b[1] - a[1]
        n = math.hypot(dx, dy) or 1
        w = (w0 + (w1 - w0) * k / (len(pts) - 1)) / 2
        left.append((x - dy / n * w, y + dx / n * w))
        right.append((x + dy / n * w, y - dx / n * w))
    return left + right[::-1]


def place(pts, x, y, rot=0.0, k=1.0, flip=False):
    """Local points moved to (x, y), turned rot degrees and scaled; flip mirrors them first."""
    c, s = math.cos(math.radians(rot)), math.sin(math.radians(rot))
    out = []
    for px, py in pts:
        px = -px if flip else px
        out.append((x + (px * c - py * s) * k, y + (px * s + py * c) * k))
    return out


def cells(x0, y0, x1, y1, size, gap, fill, seed):
    """A reticulated giraffe's coat: a jittered grid of rounded patches with gaps between."""
    r = random.Random(seed)
    out = []
    rows = int((y1 - y0) / (size * 0.86)) + 2
    cols = int((x1 - x0) / size) + 2
    pts = {}
    for j in range(rows + 1):
        for i in range(cols + 1):
            pts[i, j] = (x0 + (i + (0.5 if j % 2 else 0)) * size + (r.random() - 0.5) * size * 0.35,
                         y0 + j * size * 0.86 + (r.random() - 0.5) * size * 0.3)
    for j in range(rows):
        for i in range(cols):
            p = [pts[i, j], pts[i + 1, j], pts[i + 1, j + 1], pts[i, j + 1]]
            cx, cy = sum(a for a, _ in p) / 4, sum(b for _, b in p) / 4
            shrink = [(cx + (a - cx) * (1 - gap), cy + (b - cy) * (1 - gap)) for a, b in p]
            out.append(path(shrink, fill, extra=f'stroke="{fill}" stroke-width="{size * 0.12:.1f}" stroke-linejoin="round"'))
    return "".join(out)


# --- 1. Lion -----------------------------------------------------------------------------------
def lion():
    """A lion's head from the front in faceted gold: a two-layer mane of blades, the face built
    from planes, black features."""
    cx, cy = 550, 430
    r = random.Random(9)
    body = []
    for layer, (r0, r1, n, fill, jit) in enumerate(((190, 410, 44, GOLD_D, 60), (170, 330, 36, GOLD, 40), (160, 255, 28, GOLD_L, 25))):
        off = layer * 0.07
        for i in range(n):
            a = 2 * math.pi * i / n + off - math.pi / 2
            if layer == 0 and math.sin(a) > 0.55:  # leave the chin clear under the mane
                continue
            w = math.pi / n * 0.95
            rr1 = r1 + (r.random() - 0.5) * jit
            body.append(path([(cx + math.cos(a - w) * r0, cy + math.sin(a - w) * r0 * 1.05),
                              (cx + math.cos(a + w * 0.3) * rr1, cy + math.sin(a + w * 0.3) * rr1 * 1.05),
                              (cx + math.cos(a + w) * r0, cy + math.sin(a + w) * r0 * 1.05)], fill, extra=f'stroke="{BLACK}" stroke-width="3"'))
    # Ears.
    for s in (-1, 1):
        body.append(path([(cx + s * 120, cy - 170), (cx + s * 190, cy - 230), (cx + s * 200, cy - 150)], GOLD, extra=f'stroke="{BLACK}" stroke-width="4"'))
        body.append(path([(cx + s * 135, cy - 172), (cx + s * 182, cy - 212), (cx + s * 188, cy - 160)], BLACK))
    face = [(0, -205), (95, -185), (160, -110), (175, -20), (150, 70), (100, 150), (40, 195), (0, 205)]
    full = [(cx + x, cy + y) for x, y in face] + [(cx - x, cy + y) for x, y in reversed(face)]
    body.append(path(full, GOLD_L, extra=f'stroke="{BLACK}" stroke-width="5"'))
    # Facets: shaded planes on the brow, cheeks and muzzle.
    for s in (-1, 1):
        f = lambda pts: [(cx + s * x, cy + y) for x, y in pts]
        body.append(path(f([(0, -205), (95, -185), (70, -95), (0, -120)]), GOLD))
        body.append(path(f([(160, -110), (175, -20), (110, 10), (100, -60)]), GOLD))
        body.append(path(f([(150, 70), (100, 150), (75, 95), (110, 10), (175, -20)]), GOLD_D))
        body.append(path(f([(40, 195), (0, 205), (0, 160), (55, 140)]), GOLD_D))
        body.append(path(f([(0, -120), (70, -95), (100, -60), (52, -35), (0, -55)]), GOLD_L, extra=f'stroke="{GOLD_D}" stroke-width="2"'))
        # Eye with its brow.
        body.append(path(f([(38, -32), (70, -48), (106, -40), (80, -22)]), BLACK))
        body.append(circle(cx + s * 76, cy - 36, 7, GOLD_L))
        body.append(poly_line(f([(30, -60), (75, -78), (118, -62)]), BLACK, 6))
        # Bridge of the nose and the line down to the muzzle.
        body.append(poly_line(f([(38, -32), (30, 40)]), BLACK, 4))
        body.append(poly_line(f([(30, 40), (60, 90), (110, 100)]), BLACK, 4))
        # Whisker pads and dots.
        body.append(f'<ellipse cx="{cx + s * 42:.1f}" cy="{cy + 112}" rx="44" ry="30" fill="{GOLD_L}" stroke="{BLACK}" stroke-width="4"/>')
        for dx, dy in ((30, 104), (48, 98), (62, 112), (40, 122), (56, 128)):
            body.append(circle(cx + s * dx, cy + dy, 3.5, BLACK))
    body.append(path([(cx - 42, cy + 40), (cx + 42, cy + 40), (cx + 18, cy + 80), (cx - 18, cy + 80)], BLACK))
    body.append(poly_line([(cx, cy + 80), (cx, cy + 112)], BLACK, 4))
    body.append(poly_line([(cx - 30, cy + 150), (cx, cy + 140), (cx + 30, cy + 150)], BLACK, 5))
    return svg(body)


# --- 2. Elephant ---------------------------------------------------------------------------------
def elephant():
    """An elephant's head from the front in faceted gold: ears spread wide and veined, a creased
    trunk curling at the tip, long tusks sweeping out and up."""
    cx, cy = 550, 380
    body = []
    edge = f'stroke="{BLACK}" stroke-width="5" stroke-linejoin="round"'
    for s in (-1, 1):
        f = lambda pts: [(cx + s * x, cy + y) for x, y in pts]
        ear = [(105, -200), (230, -250), (390, -255), (500, -190), (530, -60), (490, 80), (420, 190), (360, 300), (330, 350), (290, 260), (210, 150), (150, 60)]
        body.append(path(f(ear), GOLD_D, extra=edge))
        body.append(path(f([(140, -170), (240, -215), (380, -220), (470, -165), (495, -60), (455, 60), (390, 160), (335, 255), (250, 140), (175, 40)]), GOLD, extra=f'stroke="{GOLD_D}" stroke-width="3"'))
        # Veins branching across the ear.
        for (x0, y0), (x1, y1), (x2, y2) in (((170, -60), (300, -120), (440, -150)), ((170, -20), (310, -10), (470, -40)),
                                            ((180, 30), (300, 90), (430, 110)), ((200, 80), (280, 170), (350, 240))):
            body.append(poly_line(f(bezier((x0, y0), (x1, y1), (x1, y1), (x2, y2), 20)), GOLD_D, 6))
            body.append(poly_line(f(bezier((x1, y1), (x1 + 40, y1 - 30), (x1 + 60, y1 - 40), (x1 + 90, y1 - 70), 12)), GOLD_D, 3.5))
            body.append(poly_line(f(bezier((x1, y1), (x1 + 40, y1 + 30), (x1 + 70, y1 + 40), (x1 + 100, y1 + 70), 12)), GOLD_D, 3.5))
    head = [(0, -290), (70, -280), (130, -235), (160, -150), (165, -60), (150, 30), (120, 100), (80, 150)]
    body.append(path([(cx + x, cy + y) for x, y in head] + [(cx - x, cy + y) for x, y in reversed(head)], GOLD_L, extra=edge))
    for s in (-1, 1):
        f = lambda pts: [(cx + s * x, cy + y) for x, y in pts]
        body.append(path(f([(0, -290), (70, -280), (130, -235), (60, -200), (0, -215)]), GOLD))
        body.append(path(f([(130, -235), (160, -150), (165, -60), (120, -40), (60, -200)]), GOLD))
        body.append(path(f([(165, -60), (150, 30), (120, 100), (80, 150), (60, 60), (120, -40)]), GOLD_D, extra=f'stroke="{BLACK}" stroke-width="2"'))
        body.append(path(f([(78, -55), (104, -68), (128, -60), (104, -48)]), BLACK))
        for k in range(3):
            body.append(poly_line(f([(70, -80 - k * 12), (105, -95 - k * 14), (138, -82 - k * 12)]), GOLD_D, 3))
        # Tusk: from under the cheek, out, down and up again.
        tusk = ribbon(bezier((70, 120), (150, 230), (250, 300), (300, 210), 40), 46, 6)
        body.append(path(f(tusk), BONE, extra=f'stroke="{BLACK}" stroke-width="4"'))
        body.append(poly_line(f(bezier((76, 132), (150, 225), (240, 285), (290, 215), 30)), GOLD_L, 5))
    # Trunk: down the middle, creased, curling at the tip.
    trunk_c = bezier((0, 60), (0, 260), (-30, 420), (60, 440), 50)
    body.append(path([(cx + x, cy + y) for x, y in ribbon(trunk_c, 150, 52)], GOLD_L, extra=edge))
    for k in range(4, 46, 3):
        (x0, y0), (x1, y1) = trunk_c[k - 1], trunk_c[k + 1]
        dx, dy = x1 - x0, y1 - y0
        n = math.hypot(dx, dy)
        w = (150 + (52 - 150) * k / 50) / 2 * 0.85
        nx, ny = -dy / n * w, dx / n * w
        x, y = trunk_c[k]
        body.append(f'<path d="M{cx + x + nx:.1f},{cy + y + ny:.1f} Q{cx + x + dx * 0.8:.1f},{cy + y + dy * 0.8 + 6:.1f} {cx + x - nx:.1f},{cy + y - ny:.1f}" fill="none" stroke="{GOLD_D}" stroke-width="3.5"/>')
    return svg(body)


# --- 3. Crowned crane ----------------------------------------------------------------------------
def feather(x, y, ang, length, width, fill):
    a = math.radians(ang)
    ux, uy = math.cos(a), math.sin(a)
    nx, ny = -uy, ux
    tip = (x + ux * length, y + uy * length)
    return (f'<path d="M{x + nx * width * 0.35:.1f},{y + ny * width * 0.35:.1f} '
            f'Q{x + ux * length * 0.6 + nx * width:.1f},{y + uy * length * 0.6 + ny * width:.1f} {tip[0]:.1f},{tip[1]:.1f} '
            f'Q{x + ux * length * 0.6 - nx * width:.1f},{y + uy * length * 0.6 - ny * width:.1f} {x - nx * width * 0.35:.1f},{y - ny * width * 0.35:.1f} Z" '
            f'fill="{fill}" stroke="{BLACK}" stroke-width="2.5"/>'
            f'<path d="M{x:.1f},{y:.1f} L{x + ux * length * 0.9:.1f},{y + uy * length * 0.9:.1f}" stroke="{BLACK}" stroke-width="1.5" opacity=".6"/>')


def crane():
    """A grey crowned crane in flight, wings spread right across the bonnet: fingered primaries,
    rows of coverts, the gold bristle crown fanned out, black cap, white cheek, red wattle."""
    cx, cy = 550, 500
    body = []
    for s in (-1, 1):
        # Primaries off the wrist, longest at the tip; secondaries along the arm; coverts on top.
        for k in range(10):
            t = k / 9
            x, y = cx + s * (290 + t * 110), cy - 150 + t * 40
            ang = (-20 + 75 * t) if s > 0 else (200 - 75 * t)
            body.append(feather(x, y, ang, 110 + 70 * (1 - abs(t - 0.35)), 20, GOLD_D if k % 2 else "#7a5e22"))
        for k in range(14):
            t = k / 13
            x, y = cx + s * (60 + t * 260), cy - 70 - t * 80
            body.append(feather(x, y, 90 - s * (10 + 35 * t), 170 - 30 * t, 22, GOLD))
        for row, (ln, col) in enumerate(((95, GOLD_L), (60, BONE))):
            for k in range(16):
                t = k / 15
                x, y = cx + s * (50 + t * 330), cy - 85 - t * 90 + row * 6
                body.append(feather(x, y, 90 - s * (12 + 40 * t), ln, 18, col))
        body.append(path(ribbon(bezier((cx + s * 30, cy - 80), (cx + s * 160, cy - 140), (cx + s * 300, cy - 180), (cx + s * 420, cy - 120), 30), 34, 14), GOLD_L, extra=f'stroke="{BLACK}" stroke-width="3"'))
    # Tail and trailing legs.
    for k in range(9):
        body.append(feather(cx, cy + 150, 90 + (k - 4) * 6, 150, 20, GOLD_D if k % 2 else GOLD))
    for s in (-1, 1):
        body.append(poly_line([(cx + s * 12, cy + 170), (cx + s * 18, cy + 400)], GOLD_D, 7))
        body.append(poly_line([(cx + s * 18, cy + 400), (cx + s * 30, cy + 430)], GOLD_D, 5))
    body.append(f'<ellipse cx="{cx}" cy="{cy + 20}" rx="62" ry="175" fill="{GOLD_D}" stroke="{BLACK}" stroke-width="4"/>')
    body.append(f'<ellipse cx="{cx}" cy="{cy - 30}" rx="40" ry="110" fill="{GOLD}"/>')
    # Neck, head and crown, up towards the windscreen.
    body.append(path(ribbon(bezier((cx, cy - 120), (cx, cy - 200), (cx, cy - 250), (cx, cy - 300), 20), 46, 30), GOLD_D, extra=f'stroke="{BLACK}" stroke-width="3"'))
    hy = cy - 320
    for k in range(46):
        a = math.radians(-165 + 150 * k / 45)
        ln = 95 + 25 * math.sin(k * 1.7) + 15 * math.cos(k * 0.6)
        x1, y1 = cx + math.cos(a) * ln, hy - 30 + math.sin(a) * ln
        body.append(poly_line([(cx, hy - 30), (x1, y1)], GOLD_L, 2.2))
        body.append(circle(x1, y1, 4, GOLD_L))
    body.append(path([(cx - 9, hy - 40), (cx + 9, hy - 40), (cx + 4, hy - 110), (cx - 4, hy - 110)], "#2a2a2a", extra=f'stroke="{GOLD_D}" stroke-width="2"'))
    body.append(f'<ellipse cx="{cx}" cy="{hy}" rx="32" ry="40" fill="{BLACK}" stroke="{GOLD}" stroke-width="2"/>')
    for s in (-1, 1):
        body.append(f'<ellipse cx="{cx + s * 20}" cy="{hy + 4}" rx="13" ry="20" fill="{BONE}"/>')
        body.append(f'<ellipse cx="{cx + s * 12}" cy="{hy + 26}" rx="7" ry="11" fill="{RED}"/>')
        body.append(circle(cx + s * 14, hy - 10, 4, GOLD_L))
    return svg(body)


# --- 4. Giraffes ---------------------------------------------------------------------------------
GIRAFFE_HEAD = [(-10, -32), (8, -48), (40, -54), (80, -50), (122, -38), (160, -18), (174, 2), (168, 20), (148, 28), (110, 26), (70, 32), (30, 42), (0, 38), (-14, 18)]


def giraffes():
    """Two reticulated giraffes, necks rising from the nose and curving in so their heads meet,
    gold patches with dark lines between, ossicones, ears and manes."""
    cx = 550
    body = []
    defs = []
    for s in (-1, 1):
        flip = s < 0          # X mirrors by s: s -1 is the right giraffe, which faces left
        X = lambda x: cx + s * x
        rot = 14 * (-1 if flip else 1)
        at = (X(-300), 250)
        P = lambda pts: place(pts, at[0], at[1], rot=rot, k=1.5, flip=flip)
        neck_c = bezier((X(-330), 900), (X(-480), 560), (X(-430), 330), (X(-290), 262), 50)
        neck = ribbon(neck_c, 230, 120)
        head = P(GIRAFFE_HEAD)
        for ox, oy in ((22, -48), (44, -52)):
            body.append(path(P([(ox - 7, oy), (ox + 7, oy), (ox + 5, oy - 44), (ox - 5, oy - 44)]), GOLD_D, extra=f'stroke="{BLACK}" stroke-width="3"'))
            tx, ty = P([(ox, oy - 48)])[0]
            body.append(circle(tx, ty, 14, "#2a2a2a", f'stroke="{GOLD}" stroke-width="3"'))
        body.append(path(P([(0, -30), (-70, -62), (-80, -52), (-20, -18)]), GOLD, extra=f'stroke="{BLACK}" stroke-width="3"'))
        cid = f"g{s + 1}"
        defs.append(f'<clipPath id="{cid}">{path(neck, "#000")}{path(head, "#000")}</clipPath>')
        body.append(path(neck, GOLD_D) + path(head, GOLD_D))
        body.append(f'<g clip-path="url(#{cid})">{cells(cx - 600, 100, cx + 600, 920, 72, 0.24, GOLD, 3 + s)}</g>')
        body.append(path(P([(112, -34), (160, -18), (174, 2), (168, 20), (148, 28), (112, 26)]), GOLD_L))
        # Mane down the back of the neck, the outer edge.
        outer = ribbon(neck_c, 230, 120)[:51] if flip else ribbon(neck_c, 230, 120)[51:][::-1]
        body.append(path(ribbon(outer[4:48], 26, 14), "#3a2e14", extra=f'stroke="{BLACK}" stroke-width="2"'))
        body.append(path(neck, "none", extra=f'stroke="{BLACK}" stroke-width="5"'))
        # The head again over the neck's outline, so the neck line stops at the jaw.
        defs.append(f'<clipPath id="{cid}h">{path(head, "#000")}</clipPath>')
        body.append(path(head, GOLD_D))
        body.append(f'<g clip-path="url(#{cid}h)">{cells(cx - 600, 100, cx + 600, 920, 72, 0.24, GOLD, 3 + s)}</g>')
        body.append(path(P([(112, -34), (160, -18), (174, 2), (168, 20), (148, 28), (112, 26)]), GOLD_L))
        body.append(path(head, "none", extra=f'stroke="{BLACK}" stroke-width="5"'))
        body.append(path(P([(64, -24), (82, -32), (98, -24), (82, -18)]), BLACK))
        body.append(poly_line(P([(60, -36), (82, -42), (102, -34)]), BLACK, 3))
        nx, ny = P([(158, -2)])[0]
        body.append(f'<ellipse cx="{nx:.1f}" cy="{ny:.1f}" rx="8" ry="5" fill="{BLACK}"/>')
        body.append(poly_line(P([(150, 16), (168, 18)]), BLACK, 3))
    return svg(body, "".join(defs))


PIECES = {"lion": lion, "elephant": elephant, "crane": crane, "giraffes": giraffes}

if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for name, make in PIECES.items():
        (OUT / f"{name}.svg").write_text(make())
    print("wrote", len(PIECES), "bonnet pieces to", OUT)
