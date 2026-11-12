"""Poseidon's head in profile, black-figure style: a light silhouette with the details cut in
dark, his hair streaming back into curling waves. Faces +x (the nose of the car).
Units: 400 by 300. Returns SVG body markup for one fill and one cut colour."""
import math


def spiral(cx, cy, r0, turns, sign=1, n=60):
    """An inward spiral as path d."""
    pts = []
    for i in range(n + 1):
        t = i / n
        a = sign * t * turns * 2 * math.pi
        r = r0 * (1 - 0.85 * t)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return "M" + " L".join(f"{x:.1f},{y:.1f}" for x, y in pts)


def wave(x, y, s):
    """One breaking wave crest curling forward (+x) with its spiral, base on y, s tall."""
    return (f"M{x - 1.6 * s:.1f},{y:.1f} C{x - 1.1 * s:.1f},{y - 0.2 * s:.1f} {x - 0.6 * s:.1f},{y - 1.05 * s:.1f} "
            f"{x + 0.15 * s:.1f},{y - 1.0 * s:.1f} C{x + 0.75 * s:.1f},{y - 0.95 * s:.1f} {x + 0.8 * s:.1f},{y - 0.35 * s:.1f} "
            f"{x + 0.35 * s:.1f},{y - 0.3 * s:.1f} C{x + 0.05 * s:.1f},{y - 0.28 * s:.1f} {x + 0.05 * s:.1f},{y - 0.6 * s:.1f} "
            f"{x + 0.3 * s:.1f},{y - 0.6 * s:.1f} C{x + 0.1 * s:.1f},{y - 0.1 * s:.1f} {x + 0.2 * s:.1f},{y:.1f} {x + 0.5 * s:.1f},{y:.1f} Z")


def head(fill, cut):
    sil = ("M226,62 C246,50 266,52 276,66 C284,78 286,92 292,104 L318,150 C320,155 316,158 310,158 "
           "L300,159 C303,164 306,168 302,172 C299,175 296,176 300,180 C304,186 300,192 294,196 "
           # the beard, curling forward to a point
           "C302,214 306,236 296,262 C292,270 280,272 270,264 C252,270 230,268 214,258 "
           # neck and the back of the hair, streaming toward the tail
           "C206,272 196,284 182,290 C170,262 160,246 140,236 "
           "C120,232 96,238 70,236 C52,234 34,226 20,214 C46,214 66,206 80,196 "
           "C58,192 40,180 30,164 C56,168 82,164 100,154 C82,146 68,132 62,116 "
           "C86,124 112,124 132,114 C126,98 128,80 140,68 C160,54 196,56 226,62 Z")
    out = [f'<path d="{sil}" fill="{fill}"/>']
    # Wave crests breaking off the hair's tips.
    for x, y, s in ((66, 236, 40), (78, 196, 36), (96, 154, 34), (128, 116, 30)):
        out.append(f'<path d="{wave(x - 40, y + 6, s)}" fill="{fill}"/>')
    lines = []
    # The diadem: three points, the trident's echo.
    out.append(f'<path d="M196,62 L206,30 L216,58 L230,18 L242,56 L254,34 L258,62 Z" fill="{fill}"/>')
    lines.append("M190,66 C214,58 240,58 262,66")
    # Eye, brow and ear.
    lines.append("M274,104 C282,100 290,102 294,108 C288,112 280,112 274,104 Z")
    lines.append("M266,92 C278,86 290,88 298,96")
    lines.append("M220,118 C206,118 204,146 222,148")
    # Lips and the moustache sweeping back into the beard.
    lines.append("M300,172 C290,170 278,172 268,180")
    lines.append("M298,162 C286,164 272,170 262,184 C254,196 246,198 236,196")
    # Hair: long strands running back from the brow into each wave.
    for y0, y1, x1 in ((80, 132, 132), (96, 154, 100), (112, 196, 82), (128, 236, 70)):
        lines.append(f"M{230 - (y0 - 80) * 0.6:.0f},{y0} C200,{y0 + 6} {x1 + 60},{y1 - 30} {x1},{y1}")
    # Beard: rows of curls.
    for row, (y, xs) in enumerate(((210, (232, 254, 276)), (232, (226, 250, 274)), (252, (238, 262)))):
        for x in xs:
            lines.append(spiral(x, y, 9, 1.4, 1 if row % 2 else -1))
    out.append(f'<path d="{" ".join(lines)}" fill="none" stroke="{cut}" stroke-width="5" stroke-linecap="round" stroke-linejoin="round"/>')
    # The spirals in the wave crests.
    for x, y, s in ((66, 236, 40), (78, 196, 36), (96, 154, 34), (128, 116, 30)):
        out.append(f'<path d="{spiral(x - 40 + 0.33 * s, y + 6 - 0.55 * s, s * 0.28, 1.2)}" fill="none" stroke="{cut}" stroke-width="4" stroke-linecap="round"/>')
    return "".join(out)
