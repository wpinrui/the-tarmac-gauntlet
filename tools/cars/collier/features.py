"""Paint features read off the references as image-space masks.

Each feature is a mask in one reference view. decals.py casts it onto the
shell from that view's fitted camera. Plates, makers' badges and lettering
are left out on purpose: those areas stay body colour.
"""
import numpy as np
from scipy import ndimage as ndi
from skimage.draw import polygon as fill_poly, disk
from masks import load, silhouette


def hsv(a):
    a = a.astype(float) / 255
    mx, mn = a.max(2), a.min(2)
    v = mx
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    d = np.maximum(mx - mn, 1e-6)
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    return h, s, v


def box(shape, u0, v0, u1, v1):
    m = np.zeros(shape, bool)
    m[v0:v1, u0:u1] = True
    return m


def poly(shape, pts):
    m = np.zeros(shape, bool)
    rr, cc = fill_poly([p[1] for p in pts], [p[0] for p in pts], shape)
    m[rr, cc] = True
    return m


def clean(m, close=2, open_=1, min_area=40):
    if close:
        m = ndi.binary_closing(m, iterations=close)
    m = ndi.binary_fill_holes(m)
    if open_:
        m = ndi.binary_opening(m, iterations=open_)
    lab, n = ndi.label(m)
    if n:
        sizes = ndi.sum(m, lab, range(1, n + 1))
        keep = np.isin(lab, 1 + np.nonzero(sizes >= min_area)[0])
        m = m & keep
    return m


def paint_masks(a):
    h, s, v = hsv(a)
    orange = (h > 15) & (h < 45) & (s > 0.35) & (v > 0.35)
    red = ((h < 12) | (h > 340)) & (s > 0.35)
    dark = (v < 0.35) & ~orange
    light = (v > 0.6) & (s < 0.15)
    return orange, red, dark, light


def side():
    a = load('side')
    sil = silhouette('side')
    orange, red, dark, light = paint_masks(a)
    sh = sil.shape
    out = {}
    green = poly(sh, [(425, 205), (560, 120), (690, 82), (920, 74), (1120, 92), (1150, 120), (1120, 200),
                      (930, 222), (560, 252), (430, 262)])
    mirror = poly(sh, [(485, 175), (585, 175), (585, 285), (485, 285)])
    g = sil & green & ~orange & ~mirror
    g = g | (sil & mirror & green & ~orange & ndi.binary_dilation(g & ~mirror, iterations=0))
    out['glass'] = clean(g, close=3, open_=2, min_area=500)
    out['strip'] = clean(sil & dark & box(sh, 425, 345, 1110, 400), close=1, min_area=200)
    out['taillamp'] = clean(sil & (red | dark) & poly(sh, [(1280, 215), (1340, 215), (1395, 260),
                                                          (1395, 320), (1360, 300)]), min_area=200)
    out['headlamp'] = clean(sil & ~orange & poly(sh, [(75, 315), (120, 260), (200, 245), (262, 245),
                                                      (250, 268), (160, 300), (95, 320)]), min_area=100)
    return a, out


def front():
    a = load('front')
    sil = silhouette('front')
    orange, red, dark, light = paint_masks(a)
    sh = sil.shape
    out = {}
    lamps = poly(sh, [(60, 330), (95, 262), (140, 262), (262, 405), (230, 410), (120, 392)])
    lamps |= poly(sh, [(745, 330), (712, 262), (668, 262), (560, 410), (590, 410), (700, 392)])
    out['headlamp'] = clean(sil & ~orange & lamps, close=3, min_area=300)
    mouth = poly(sh, [(130, 412), (690, 412), (705, 445), (690, 470), (660, 520), (650, 555), (560, 560),
                      (300, 560), (170, 555), (160, 520), (130, 470), (118, 440)])
    plate = box(sh, 288, 538, 535, 600)
    fog = np.zeros(sh, bool)
    lens = np.zeros(sh, bool)
    for c in ((200, 505), (622, 512)):
        rr, cc = disk((c[1], c[0]), 33, shape=sh)
        fog[rr, cc] = True
        rr, cc = disk((c[1], c[0]), 23, shape=sh)
        lens[rr, cc] = True
    out['fog'] = lens
    out['fog_ring'] = fog & ~lens
    bars = np.zeros(sh, bool)
    for v, u0, u1 in ((430, 205, 610), (489, 238, 580), (506, 238, 586), (523, 238, 586), (540, 250, 570)):
        bars[v - 2:v + 2, u0:u1] = True
    out['bars'] = bars & mouth & ~fog
    m = sil & mouth & ~orange & ~plate & ~fog
    out['grille'] = clean(m, close=4, min_area=500)
    out['lip'] = clean(sil & dark & box(sh, 90, 575, 720, 610) & ~plate, close=2, min_area=200)
    return a, out


def rear():
    a = load('rear')
    sil = silhouette('rear')
    orange, red, dark, light = paint_masks(a)
    sh = sil.shape
    out = {}
    lamps = poly(sh, [(55, 300), (100, 252), (170, 262), (225, 405), (180, 400), (70, 360)])
    lamps |= poly(sh, [(930, 300), (885, 252), (815, 262), (760, 405), (805, 400), (915, 360)])
    out['taillamp'] = clean(sil & ~orange & lamps, close=3, min_area=300)
    win = poly(sh, [(150, 230), (215, 110), (300, 85), (700, 85), (790, 110), (840, 230), (700, 245), (300, 245)])
    out['glass'] = clean(sil & ~orange & win, close=4, min_area=1000)
    out['strip'] = clean(sil & dark & box(sh, 55, 482, 925, 568), close=2, min_area=500)
    return a, out


def top():
    a = load('top')
    sil = silhouette('top')
    orange, red, dark, light = paint_masks(a)
    sh = sil.shape
    out = {}
    ws = poly(sh, [(762, 150), (1010, 128), (1075, 160), (1100, 300), (1100, 450), (1075, 590), (1010, 620),
                   (762, 600)])
    out['glass'] = clean(sil & ~orange & ws, close=3, min_area=1000)
    return a, out


def three_quarter_front():
    a = load('three_quarter_front')
    sil = silhouette('three_quarter_front')
    orange, red, dark, light = paint_masks(a)
    sh = sil.shape
    lamp = poly(sh, [(500, 618), (600, 560), (700, 520), (800, 490), (900, 440), (975, 428), (982, 445), (950, 480),
                     (900, 520), (850, 545), (800, 570), (700, 595), (600, 612), (520, 624)])
    return a, {'headlamp': clean(sil & ~orange & lamp, close=3, min_area=500)}


def three_quarter_rear():
    a = load('three_quarter_rear')
    sil = silhouette('three_quarter_rear')
    orange, red, dark, light = paint_masks(a)
    sh = sil.shape
    lamp = poly(sh, [(483, 250), (500, 234), (560, 234), (620, 248), (662, 285), (702, 340), (728, 390), (690, 387),
                     (600, 372), (540, 347), (498, 300), (483, 270)])
    return a, {'taillamp': clean(sil & ~orange & lamp, close=3, min_area=500)}


VIEWS = {'side': side, 'front': front, 'rear': rear, 'top': top,
         'three_quarter_front': three_quarter_front, 'three_quarter_rear': three_quarter_rear}

COLOURS = {'glass': (40, 160, 255), 'strip': (255, 0, 255), 'taillamp': (255, 40, 40), 'headlamp': (255, 255, 0),
           'grille': (0, 255, 120), 'chrome': (255, 255, 255), 'fog': (0, 255, 255), 'fog_ring': (200, 200, 200), 'bars': (255, 255, 255), 'lip': (140, 80, 255)}

if __name__ == '__main__':
    import sys
    from PIL import Image
    for n, f in VIEWS.items():
        a, out = f()
        img = (a * 0.45).astype(np.uint8)
        for k, m in out.items():
            img[m] = COLOURS[k]
        Image.fromarray(img).save(f'{sys.argv[1]}/feat_{n}.png')
