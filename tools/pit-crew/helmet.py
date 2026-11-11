"""Builds the race drivers' helmet: a closed-face shell with a visor across the eyes and a dark lip round the
opening, sized to fit over crew_suit's head and cap. The game draws it on the head bone of every driver.

Usage: python3 -I tools/pit-crew/helmet.py [crew_suit.glb]

Writes unity/Assets/Raceday/Resources/PitCrew/helmet.json: positions in the head bone's frame, already in
Unity's frame (z flipped, as tools/team-hq/crew/pack_crew.py packs the body), and triangles for three parts:
shell, visor and lip. Normals are left to the loader. With the body's .glb given, it also checks that every
vertex of the head lies inside the shell. Drivers are drawn without the body's cap and glasses.
"""
import json, math, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '../../unity/Assets/Raceday/Resources/PitCrew/helmet.json')

# In the head bone's frame as the body is drawn in glTF: +y up the neck, +z out of the face, metres.
CENTRE = np.array([0.0, 0.095, 0.012])
RX, RY_UP, RY_DOWN, RZ_FRONT, RZ_BACK = 0.138, 0.162, 0.17, 0.166, 0.152
CHIN, NAPE = -0.07, -0.045         # the shell's lowest point under the chin and over the neck
VISOR_SIDE = math.radians(64)      # the visor reaches this far round from the front, each way
VISOR_LOW, VISOR_HIGH = -0.042, 0.04   # the visor's bottom and top edge, m from the centre's height
VISOR_OUT = 0.004                  # the visor stands this far proud of the shell
LIP = 0.018                        # the lip turns in this far round the opening
AROUND, UP = 56, 22


def surface(theta, phi):
    """A point on the shell: theta round from the front, phi up from the centre's height."""
    ry = RY_UP if phi >= 0 else RY_DOWN
    rz = RZ_FRONT if math.cos(theta) >= 0 else RZ_BACK
    return CENTRE + np.array([RX * math.cos(phi) * math.sin(theta), ry * math.sin(phi), rz * math.cos(phi) * math.cos(theta)])


def bottom(theta):
    """The lowest phi of the shell at theta: low under the chin, higher over the neck."""
    f = (1 + math.cos(theta)) / 2
    y = NAPE + (CHIN - NAPE) * f ** 2
    return math.asin(max(-1.0, (y - CENTRE[1]) / RY_DOWN))


def build():
    verts, parts = [], {'shell': [], 'visor': [], 'lip': []}

    def quad(part, a, b, c, d):
        base = len(verts)
        verts.extend([a, b, c, d])
        # Wound so the face points out, in glTF's frame: (a, b, c), (a, c, d) counter-clockwise from outside.
        parts[part] += [base, base + 1, base + 2, base, base + 2, base + 3]

    for i in range(AROUND):
        t0, t1 = 2 * math.pi * i / AROUND - math.pi, 2 * math.pi * (i + 1) / AROUND - math.pi
        b0, b1 = bottom(t0), bottom(t1)
        for j in range(UP):
            u0, u1 = j / UP, (j + 1) / UP
            p00, p01 = b0 + (math.pi / 2 - b0) * u0, b0 + (math.pi / 2 - b0) * u1
            p10, p11 = b1 + (math.pi / 2 - b1) * u0, b1 + (math.pi / 2 - b1) * u1
            tm = (t0 + t1) / 2
            ym = (surface(tm, (p00 + p01) / 2)[1] - CENTRE[1])
            visor = abs(tm) < VISOR_SIDE and VISOR_LOW < ym < VISOR_HIGH
            a, b, c, d = surface(t0, p00), surface(t1, p10), surface(t1, p11), surface(t0, p01)
            if visor:
                a, b, c, d = [v + (v - CENTRE) / np.linalg.norm(v - CENTRE) * VISOR_OUT for v in (a, b, c, d)]
            quad('visor' if visor else 'shell', a, b, c, d)
        # The lip: from the opening's edge in towards the neck.
        e0, e1 = surface(t0, b0), surface(t1, b1)
        def inner(e):
            r = e - np.array([CENTRE[0], e[1], CENTRE[2]])
            return e - r / np.linalg.norm(r) * LIP + np.array([0, 0.004, 0])
        quad('lip', e1, e0, inner(e0), inner(e1))
    return np.array(verts), parts


def inside(points):
    """How far out of the shell each point is, as a share of the shell's radius there (under 1 is inside)."""
    d = points - CENTRE
    ry = np.where(d[:, 1] >= 0, RY_UP, RY_DOWN)
    rz = np.where(d[:, 2] >= 0, RZ_FRONT, RZ_BACK)
    return np.sqrt((d[:, 0] / RX) ** 2 + (d[:, 1] / ry) ** 2 + (d[:, 2] / rz) ** 2)


def check(glb):
    sys.path.insert(0, os.path.join(HERE, '../team-hq/crew'))
    import pack_crew as pc
    g, b = pc.load(glb)
    skin = g['skins'][0]
    names = [g['nodes'][j]['name'] for j in skin['joints']]
    ibm = pc.accessor(g, b, skin['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1)
    head = names.index('Head')
    worst = 0.0
    for n in g['nodes']:
        if 'mesh' not in n or n['name'] not in ('Wolf3D_Head', 'Wolf3D_Teeth', 'EyeLeft', 'EyeRight'):
            continue
        for p in g['meshes'][n['mesh']]['primitives']:
            P = pc.accessor(g, b, p['attributes']['POSITION'])
            J = pc.accessor(g, b, p['attributes']['JOINTS_0'])
            W = pc.accessor(g, b, p['attributes']['WEIGHTS_0'])
            on = ((J == head) * W).sum(1) > 0.5
            Q = (np.c_[P[on], np.ones(on.sum())] @ ibm[head].T)[:, :3]
            # Only what sits above the opening needs covering: the neck runs on out of it.
            Q = Q[Q[:, 1] > NAPE + 0.01]
            if len(Q):
                worst = max(worst, float(inside(Q).max()))
                print(f'{n["name"]}: {len(Q)} vertices, furthest {inside(Q).max():.3f} of the way out')
    print('ok: all inside the shell' if worst < 0.97 else f'FAIL: something reaches {worst:.3f} of the way out')
    return worst < 0.97


def main():
    verts, parts = build()
    # To Unity's frame: z flipped, which also flips every triangle's winding.
    u = verts * np.array([1, 1, -1])
    out = {'positions': [round(float(x), 5) for x in u.reshape(-1)]}
    for k, tris in parts.items():
        t = np.array(tris).reshape(-1, 3)[:, [0, 2, 1]]
        out[k] = [int(x) for x in t.reshape(-1)]
    with open(OUT, 'w') as f:
        json.dump(out, f, separators=(',', ':'))
    print(f'{len(verts)} vertices, {sum(len(t) for t in parts.values()) // 3} triangles -> {os.path.relpath(OUT)}')
    if len(sys.argv) > 1 and not check(sys.argv[1]):
        sys.exit(1)


if __name__ == '__main__':
    main()
