# The pit stop and collision sounds: real CC0 recordings, cut into takes, layered and levelled.
# Downloads its sources once to out/race-sfx/src, writes the game's takes to unity/Assets/Raceday/Resources/RaceSfx/<bank>/
# as mono Ogg Vorbis, and writes MP3 previews and two demo mixes to out/race-sfx/preview/.
# Run from the repo root: python3 tools/race-sfx/build.py   (needs numpy and ffmpeg)
import os, subprocess, sys, urllib.request
import numpy as np

SR = 48000
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SRC = os.path.join(ROOT, 'out', 'race-sfx', 'src')
PREVIEW = os.path.join(ROOT, 'out', 'race-sfx', 'preview')
GAME = os.path.join(ROOT, 'unity', 'Assets', 'Raceday', 'Resources', 'RaceSfx')
rng = np.random.default_rng(7)

BSB = 'https://bigsoundbank.com/UPLOAD/ogg/{}.ogg'   # Joseph Sardin, BigSoundBank, CC0
OGA = 'https://opengameart.org/sites/default/files/{}'  # OpenGameArt, each pack CC0
ARCHIVES = {
    'scrapes.zip': OGA.format('scrapes.zip'),
}
SOURCES = {
    'impact-wrench-1': BSB.format('0964'), 'impact-wrench-2': BSB.format('0965'),
    'air-leak': BSB.format('1486'),
    'pneumatic-cylinder-1': BSB.format('1491'), 'pneumatic-cylinder-2': BSB.format('1492'), 'pneumatic-cylinder-3': BSB.format('1493'),
    'service-station': BSB.format('0524'),
    'car-trunk-1': BSB.format('1512'), 'car-trunk-2': BSB.format('1525'), 'car-door-3': BSB.format('1524'), 'car-door-4': BSB.format('1526'),
    'metal-trash-can': BSB.format('1266'), 'crushed-cans': BSB.format('0118'), 'grinding-metal-gate': BSB.format('0683'),
    **{f'sheet-steel-{i}': BSB.format(f'{1770 + i}') for i in range(1, 9)},
    'lightclunk1': OGA.format('lightclunk1.wav'), 'lightclunk2': OGA.format('lightclunk2.wav'), 'clank1': OGA.format('clank1.wav'),
    'thud2': OGA.format('thud2.wav'), 'thud3': OGA.format('thud3.wav'),
    'crash': OGA.format('qubodup-crash.ogg'),
}


# ---------------------------------------------------------------- sources

def fetch():
    os.makedirs(SRC, exist_ok=True)
    for name, url in {**SOURCES, **ARCHIVES}.items():
        path = os.path.join(SRC, name if name.endswith('.zip') else name + os.path.splitext(url)[1])
        if not os.path.exists(path):
            print('downloading', url)
            urllib.request.urlretrieve(url, path)
    z = os.path.join(SRC, 'scrapes')
    if not os.path.isdir(z):
        subprocess.run(['unzip', '-q', '-o', os.path.join(SRC, 'scrapes.zip'), '-d', z], check=True)


def load(name):
    if name.startswith('scrape-'):
        path = os.path.join(SRC, 'scrapes', name + '.wav')
    else:
        path = os.path.join(SRC, name + os.path.splitext(SOURCES[name])[1])
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', path, '-ac', '1', '-ar', str(SR), '-f', 'f32le', '-'],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.float32).astype(np.float64)


# ---------------------------------------------------------------- tools

def env_db(x, hop=0.005):
    h = int(SR * hop); n = max(len(x) // h, 1)
    r = np.sqrt(np.mean(x[:n * h].reshape(n, h) ** 2, axis=1) + 1e-12)
    return 20 * np.log10(r), h


def bursts(x, thr=-30, gap=0.08):
    """(start, end) in seconds of each burst louder than thr dB under the loudest moment."""
    db, h = env_db(x, 0.01); pk = db.max(); on = db > pk + thr
    g = int(gap / 0.01); out = []; i = 0
    while i < len(on):
        if on[i]:
            j = i
            while j < len(on) and on[j:j + g].any(): j += 1
            out.append((i * 0.01, j * 0.01)); i = j
        else: i += 1
    return out


def cut(x, a, b=None, pre=0.012, fade=0.004, tail=-42):
    """From just before a, to where the sound after b has died to tail dB under its peak, with short fades."""
    i = max(int((a - pre) * SR), 0)
    y = x[i:] if b is None else x[i:int(b * SR) + SR]
    db, h = env_db(y); pk = db.max()
    k = len(db) - 1
    start = int(np.argmax(db)) if b is None else int((b - a + pre) * SR) // h
    for j in range(min(start, len(db) - 1), len(db)):
        if db[j] < pk + tail: k = j; break
    y = y[:(k + 1) * h].copy()
    f = int(fade * SR); y[:f] *= np.linspace(0, 1, f)
    fo = min(int(0.03 * SR), len(y) // 3); y[-fo:] *= np.linspace(1, 0, fo) ** 2
    return y


def snap(y, lead=0.0015):
    """Starts a one-shot right at its attack, so it sounds the instant the game plays it: lead s before the first
    sample at a tenth of its peak, faded in over a millisecond."""
    i = max(int(np.argmax(np.abs(y) > 0.1 * np.abs(y).max())) - int(lead * SR), 0)
    y = y[i:].copy(); f = int(0.001 * SR); y[:f] *= np.linspace(0, 1, f)
    return y


def filt(x, cut_hz, hp=False, order=2):
    n = len(x); pad = int(0.05 * SR)
    X = np.fft.rfft(np.concatenate([x, np.zeros(pad)])); f = np.fft.rfftfreq(n + pad, 1 / SR) + 1e-9
    r = (f / cut_hz) ** order
    return np.fft.irfft(X * (r / (1 + r) if hp else 1 / (1 + r)), n + pad)[:n]


def pitch(x, ratio):
    """Plays x ratio times as fast: lower and longer below 1."""
    t = np.arange(0, len(x) - 1, ratio)
    return np.interp(t, np.arange(len(x)), x)


def mix(*parts):
    """Layers (signal, offset s, gain) into one."""
    n = max(int(o * SR) + len(s) for s, o, g in parts)
    out = np.zeros(n)
    for s, o, g in parts:
        i = int(o * SR); out[i:i + len(s)] += s * g
    return out


def peak(x, db=-1.0):
    return x * (10 ** (db / 20) / (np.abs(x).max() + 1e-9))


def rms_to(x, db):
    return x * (10 ** (db / 20) / (np.sqrt(np.mean(x ** 2)) + 1e-9))


def limit(x, db=-1.0):
    """Soft-clips the odd peak instead of turning the whole thing down."""
    c = 10 ** (db / 20)
    return np.tanh(x / c) * c


def decay(n, t60):
    return 10 ** (-3 * np.arange(n) / (t60 * SR))


def loop(x, length, xfade=0.35):
    """A seamless loop length s long from x: its end crossfaded into its start."""
    n = int(length * SR); f = int(xfade * SR)
    y = x[:n + f].copy()
    w = np.sin(np.linspace(0, np.pi / 2, f)) ** 2
    y[:f] = y[:f] * w + y[n:n + f] * (1 - w)
    return y[:n]


def grains(sources, length, density, size=(0.18, 0.4)):
    """A seamless loop of overlapping Hann windowed grains drawn from the sources, wrapped round its end."""
    n = int(length * SR); out = np.zeros(n)
    count = int(length * density)
    for k in range(count):
        s = sources[rng.integers(len(sources))]
        g = int(rng.uniform(*size) * SR)
        if len(s) <= g: continue
        i = rng.integers(0, len(s) - g)
        grain = s[i:i + g]
        grain = grain / (np.sqrt(np.mean(grain ** 2)) + 1e-9) * np.hanning(g) * rng.uniform(0.7, 1.0)
        at = int(k / count * n + rng.uniform(-0.3, 0.3) * n / count) % n
        idx = (at + np.arange(g)) % n
        np.add.at(out, idx, grain)
    return out


# ---------------------------------------------------------------- the banks

def build():
    banks = {}

    # Wheel guns: every burst of two real air impact wrenches, as recorded.
    gun = []
    for name in ('impact-wrench-1', 'impact-wrench-2'):
        x = load(name)
        for a, b in bursts(x):
            gun.append(peak(filt(cut(x, a, b, tail=-36), 120, hp=True)))
    banks['gun'] = gun

    # Air lines: short breaths from a recorded air leak, the hiss as a gun lets go or a line vents.
    air = load('air-leak')
    banks['air'] = []
    for k in range(6):
        d = rng.uniform(0.18, 0.42); i = int(rng.uniform(1, 20) * SR)
        y = air[i:i + int(d * SR)].copy()
        y *= np.minimum(np.arange(len(y)) / (0.006 * SR), 1) * decay(len(y), d * 0.9)
        banks['air'].append(peak(filt(y, 1200, hp=True)))

    # The air jacks firing: real pneumatic cylinders.
    banks['jack-up'] = [peak(filt(cut(load(n), 0.1, None, tail=-40), 80, hp=True)) for n in
                        ('pneumatic-cylinder-1', 'pneumatic-cylinder-2', 'pneumatic-cylinder-3')]

    # The car dropping off the jacks: a car's own boot and doors slamming, slowed for the weight of a whole car landing
    # on its tyres, with the jacks venting their air.
    slams = [('car-trunk-1', 6.61), ('car-trunk-2', 8.09), ('car-door-3', 2.43), ('car-door-4', 3.30)]
    banks['jack-drop'] = []
    for k, (name, at) in enumerate(slams):
        body = filt(pitch(cut(load(name), at, at + 0.12), 0.6 + 0.04 * k), 1400)
        i = int(rng.uniform(1, 18) * SR); vent = air[i:i + int(0.5 * SR)].copy()
        vent *= np.minimum(np.arange(len(vent)) / (0.004 * SR), 1) * decay(len(vent), 0.45)
        banks['jack-drop'].append(peak(mix((filt(vent, 1500, hp=True), 0, 0.22), (peak(body), 0.02, 1.0))))

    # The fuel rig: the nozzle locking on and off, from recorded mechanical clunks and a real service station nozzle.
    station = load('service-station')
    clunks = [cut(load(n), 0.0, None, pre=0, tail=-40) for n in ('lightclunk1', 'lightclunk2', 'clank1')]
    clunks += [cut(station, 0.44, 0.61), cut(station, 2.19, 2.37)]
    banks['fuel-clunk'] = [peak(filt(c, 90, hp=True)) for c in clunks]
    # The fuel itself running in: the steady pour from the same service station, looped.
    banks['fuel-flow'] = [rms_to(loop(filt(station[int(20 * SR):int(30 * SR)], 70, hp=True), 6.0), -16)]

    # Light hits: cars' own doors and boots, and small metal hits.
    light = [cut(load('car-door-3'), 2.43, 2.58), cut(load('car-door-4'), 3.30, 3.50), cut(load('car-trunk-1'), 6.61, 6.75),
             cut(load('car-trunk-2'), 8.09, 8.24)]
    can = load('metal-trash-can')
    light += [cut(can, 3.64, 3.95), cut(can, 8.61, 8.91)]
    banks['hit-light'] = [peak(filt(h, 60, hp=True)) for h in light]

    # Heavy hits: falling sheet steel for the panels, a car's boot slam slowed for the body's weight, and crushed cans
    # for the crumple.
    cans = load('crushed-cans')
    crunch = [cut(cans, 0.62, 0.82), cut(cans, 2.79, 2.98), cut(cans, 5.40, 5.69)]
    trunk = [cut(load('car-trunk-1'), 6.61, 6.75), cut(load('car-trunk-2'), 8.09, 8.24)]
    banks['hit-heavy'] = []
    for i in range(1, 9):
        steel = load(f'sheet-steel-{i}'); a, b = bursts(steel)[0]
        steel = cut(steel, a, b, tail=-40)
        body = filt(pitch(trunk[i % 2], rng.uniform(0.55, 0.65)), 1800)
        y = mix((peak(body), 0, 1.0), (peak(steel), 0.004, 0.8), (peak(crunch[i % 3]), 0.01, rng.uniform(0.35, 0.55)))
        banks['hit-heavy'].append(peak(filt(y, 45, hp=True)))
    banks['hit-heavy'].append(peak(cut(load('crash'), 0, None, pre=0, tail=-40)))

    # Tyre walls: dull, soft thuds with no ring.
    soft = [load('thud2'), load('thud3')] + [pitch(t, 0.5) for t in trunk]
    banks['hit-soft'] = [peak(filt(cut(s, 0, None, pre=0, tail=-40), 900)) for s in soft]

    # Scraping along a barrier: seamless loops of grains from recorded metal scrapes and a grinding gate.
    sources = [load(f'scrape-{i}') for i in range(1, 9)]
    gate = load('grinding-metal-gate')
    sources += [cut(gate, a, b) for a, b in [(2.33, 3.26), (6.94, 8.09), (18.97, 20.34), (20.92, 21.99)]]
    sources = [filt(s, 150, hp=True) for s in sources]
    banks['scrape'] = [limit(rms_to(grains(sources, 3.0, 14), -16)) for _ in range(3)]
    return banks


# ---------------------------------------------------------------- output

def write(y, path, fmt):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    pcm = np.clip(y, -1, 1).astype(np.float32).tobytes()
    codec = ['-c:a', 'libvorbis', '-q:a', '5'] if fmt == 'ogg' else ['-c:a', 'libmp3lame', '-b:a', '128k']
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-f', 'f32le', '-ar', str(SR), '-ac', '1', '-i', '-', *codec, path],
                   input=pcm, check=True)


FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
# Short takes decompress on load, so playing one costs no decoding; each is mono, placed in 3D by the game.
AUDIO_META = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 0.6
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {{}}
  forceToMono: 1
  normalize: 0
  preloadAudioData: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def meta(path, template):
    """A Unity .meta beside path, its guid fixed by the path so rebuilds keep it."""
    import uuid
    rel = os.path.relpath(path, ROOT).replace(os.sep, '/')
    with open(path + '.meta', 'w', newline='\n') as f:
        f.write(template.format(guid=uuid.uuid5(uuid.NAMESPACE_URL, 'tarmac-gauntlet/' + rel).hex))


def demos(b):
    """A pit stop and a crash, mixed the way the game times them, for the listening page."""
    pick = lambda k, i: b[k][i % len(b[k])]
    flow = np.tile(b['fuel-flow'][0], 2)[:int(2.3 * SR)]
    flow *= np.minimum(np.arange(len(flow)) / (0.08 * SR), 1) * np.minimum((len(flow) - np.arange(len(flow))) / (0.12 * SR), 1)
    parts = [(pick('jack-up', 0), 0.0, 0.8), (pick('fuel-clunk', 0), 0.35, 0.6), (flow, 0.42, 0.55)]
    for w, t in enumerate((0.25, 0.31, 0.4, 0.47)):
        parts += [(pick('gun', w), t, 0.9 - 0.12 * (w % 2)), (pick('air', w), t + 0.42, 0.25)]
    for w, t in enumerate((1.55, 1.62, 1.7, 1.79)):
        parts += [(pick('gun', w + 5), t, 0.9 - 0.12 * (w % 2)), (pick('air', w + 2), t + 0.45, 0.25)]
    parts += [(pick('fuel-clunk', 3), 2.72, 0.6), (pick('jack-drop', 1), 2.9, 0.95)]
    stop = mix(*parts)
    scrape = np.tile(b['scrape'][0], 2)[:int(2.4 * SR)]
    scrape *= np.minimum(np.arange(len(scrape)) / (0.05 * SR), 1) * np.linspace(1, 0.2, len(scrape))
    crash = mix((pick('hit-light', 0), 0.0, 0.35), (pick('hit-light', 3), 0.9, 0.6), (pick('hit-heavy', 2), 2.0, 1.0),
                (scrape, 2.05, 0.5), (pick('hit-soft', 0), 5.2, 0.9))
    return {'demo-pit-stop': peak(stop, -1), 'demo-crash': peak(crash, -1)}


LOOPS = ('fuel-flow', 'scrape')


def main():
    fetch()
    banks = build()
    for bank, takes in banks.items():
        if bank not in LOOPS: takes = banks[bank] = [snap(t) for t in takes]
        d = os.path.join(GAME, bank)
        for f in (os.listdir(d) if os.path.isdir(d) else []):
            if f.endswith('.ogg') or f.endswith('.ogg.meta'): os.remove(os.path.join(d, f))
        for i, y in enumerate(takes, 1):
            take = os.path.join(d, f'{bank}-{i}.ogg')
            write(y, take, 'ogg')
            meta(take, AUDIO_META)
            write(y, os.path.join(PREVIEW, bank, f'{bank}-{i}.mp3'), 'mp3')
        meta(d, FOLDER_META)
        print(f'{bank}: {len(takes)} takes, {sum(len(t) for t in takes) / SR:.1f}s')
    meta(GAME, FOLDER_META)
    for name, y in demos(banks).items():
        write(y, os.path.join(PREVIEW, name + '.mp3'), 'mp3')


if __name__ == '__main__':
    main()
