# The menus' UI sounds. Tonal cues are synthesised here, in E so they sit together; the tactile clicks are Kenney's
# CC0 Interface Sounds (https://opengameart.org/content/interface-sounds), softened and levelled here.
# python3 tools/ui-sounds/ui-sounds.py <kenney Audio folder> <out folder>   (needs numpy and ffmpeg)
# Writes 44.1 kHz mono 16-bit WAVs named by cue, as the game loads them from Resources/UiSounds.
import numpy as np, wave, sys, os, subprocess
SR = 44100
rng = np.random.default_rng(7)
KENNEY, OUT = sys.argv[1], sys.argv[2]
os.makedirs(OUT, exist_ok=True)

def midi(m): return 440 * 2 ** ((m - 69) / 12)
def t_of(sec): return np.arange(int(sec * SR)) / SR
def pad(x, sec): return np.concatenate([x, np.zeros(max(0, int(sec * SR) - len(x)))])[: int(sec * SR)]

def filt(x, lo=None, hi=None, order=2):
    """Gentle Butterworth-shaped high and low cuts, done in the frequency domain."""
    X = np.fft.rfft(x); f = np.fft.rfftfreq(len(x), 1 / SR) + 1e-9; g = np.ones_like(f)
    if lo: g *= 1 / np.sqrt(1 + (lo / f) ** (2 * order))
    if hi: g *= 1 / np.sqrt(1 + (f / hi) ** (2 * order))
    return np.fft.irfft(X * g, len(x))

def sweep_noise(sec, f0, f1, q=1.2):
    """Noise through a band that glides from f0 to f1, in short overlapping blocks."""
    n = int(sec * SR); noise = rng.standard_normal(n + 4096); out = np.zeros(n + 4096)
    hop, size = 512, 2048; win = np.hanning(size)
    for i in range(0, n, hop):
        p = i / n; fc = f0 * (f1 / f0) ** p
        seg = noise[i:i + size] * win
        X = np.fft.rfft(seg); f = np.fft.rfftfreq(size, 1 / SR) + 1e-9
        bw = fc / q; g = 1 / (1 + ((f - fc) / (bw / 2)) ** 2)
        out[i:i + size] += np.fft.irfft(X * g, size)
    return out[:n] / 3

def room(x, sec=0.45, mix=0.14, bright=5000):
    """A small, soft room: exponentially fading noise as the impulse."""
    t = t_of(sec); ir = rng.standard_normal(len(t)) * np.exp(-t * 6.9 / sec); ir = filt(ir, 200, bright)
    ir /= np.sqrt((ir ** 2).sum())
    wet = np.fft.irfft(np.fft.rfft(np.pad(x, (0, len(ir)))) * np.fft.rfft(np.pad(ir, (0, len(x)))), len(x) + len(ir))
    dry = np.pad(x, (0, len(ir)))
    return dry * (1 - mix) + wet * mix * 2.2

def glass(m, sec, decay, vel=1.0, shimmer=0.22):
    """A frosted-glass bell: the note, a faint inharmonic partial that dies fast, and a soft attack."""
    t = t_of(sec); f = midi(m)
    x = np.sin(2 * np.pi * f * t) * np.exp(-t * decay)
    x += shimmer * np.sin(2 * np.pi * f * 2.76 * t) * np.exp(-t * decay * 3)
    x += 0.06 * np.sin(2 * np.pi * f * 5.4 * t) * np.exp(-t * decay * 6)
    return x * np.minimum(1, t / 0.0025) * vel

def tap(sec=0.03, lo=1500, hi=6000, decay=260):
    t = t_of(sec); return filt(rng.standard_normal(len(t)), lo, hi) * np.exp(-t * decay)

def thump(f0=110, sec=0.25, decay=18):
    t = t_of(sec); ph = 2 * np.pi * np.cumsum(f0 * (1 + 0.6 * np.exp(-t * 40))) / SR
    return np.sin(ph) * np.exp(-t * decay) * np.minimum(1, t / 0.003)

def mix(*xs):
    """Sounds of any lengths laid over each other from the start."""
    out = np.zeros(max(len(x) for x in xs))
    for x in xs: out[: len(x)] += x
    return out

def place(parts, sec):
    out = np.zeros(int(sec * SR))
    for start, x in parts:
        i = int(start * SR); j = min(len(out), i + len(x)); out[i:j] += x[: j - i]
    return out

def fade_tail(x, ms=8):
    n = int(ms / 1000 * SR); x = x.copy(); x[-n:] *= np.linspace(1, 0, n); return x

def kenney(name):
    raw = subprocess.run(['ffmpeg', '-v', 'quiet', '-i', os.path.join(KENNEY, name + '.ogg'), '-ac', '1', '-ar', str(SR), '-f', 'f32le', '-'],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.float32).astype(np.float64)

def trim(x, floor_db=-60):
    a = np.abs(x); on = np.where(a > a.max() * 10 ** (floor_db / 20))[0]
    return x[max(0, on[0] - 16): on[-1] + 1]

LIFT = 6  # dB on every cue's peak below, so the set sits right under the -18 LUFS menu music

def write(name, x, peak_db):
    x = fade_tail(trim(x) if len(x) > 64 else x)
    x = x / np.abs(x).max() * 10 ** ((peak_db + LIFT) / 20)
    pcm = (np.clip(x, -1, 1) * 32767).astype('<i2')
    with wave.open(os.path.join(OUT, name + '.wav'), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())

E4, Gs4, B4, Cs5, E5, Fs5, Gs5, B5, E6 = 64, 68, 71, 73, 76, 78, 80, 83, 88

# ---- touch: hover, click, steps and the slider (quiet, short, felt more than heard)
write('hover', room(mix(glass(B5 + 12, 0.06, 90, shimmer=0.1) * 0.5, tap(0.012, 3000, 9000, 500) * 0.25), 0.2, 0.08), -30)
write('click', room(filt(kenney('click_002'), 400, 8000), 0.25, 0.08), -17)
write('step', room(filt(kenney('tick_004'), 500, 8000), 0.25, 0.08), -21)
write('slider', filt(tap(0.02, 1800, 7000, 400), 1500), -26)
write('toggle-on', room(filt(kenney('toggle_002'), 400, 9000), 0.25, 0.08), -17)
write('toggle-off', room(filt(kenney('toggle_001'), 300, 7000), 0.25, 0.08), -18)

# ---- moving about: back, dropdowns, panels and the camera's glide
write('back', room(place([(0, mix(glass(E5, 0.12, 40), tap() * 0.3)), (0.045, glass(B4, 0.2, 30) * 0.8)], 0.3)), -17)
write('dropdown-open', room(mix(place([(0, tap(0.02, 2500, 8000) * 0.4), (0, glass(Gs5, 0.14, 38) * 0.7)], 0.18),
                         pad(sweep_noise(0.12, 1500, 5000) * np.hanning(int(0.12 * SR)) * 0.25, 0.18))), -21)
write('dropdown-close', room(mix(place([(0, tap(0.02, 2000, 7000) * 0.4), (0, glass(Cs5, 0.12, 45) * 0.7)], 0.16),
                          pad(sweep_noise(0.1, 4000, 1400) * np.hanning(int(0.1 * SR)) * 0.25, 0.16))), -22)
env = np.sin(np.pi * np.linspace(0, 1, int(0.5 * SR))) ** 2.2
write('glide', room(filt(sweep_noise(0.5, 380, 1700, 0.9), 150, 6000) * env, 0.5, 0.1), -24)
env = np.sin(np.pi * np.linspace(0, 1, int(0.22 * SR))) ** 2
write('panel', room(filt(sweep_noise(0.22, 1200, 4200, 1.0), 400, 9000) * env, 0.35, 0.1), -26)

# ---- outcomes: confirm, the big four and the error
write('confirm', room(place([(0, mix(tap(0.03, 1200, 5000) * 0.35, glass(E5, 0.3, 14) * 0.8, glass(B5, 0.3, 18) * 0.35))], 0.4)), -14)
# Race: a lift onto the grid. A low thump, then a fifth-and-ninth bell chord that rings out.
race = place([(0, thump(82, 0.3, 12) * 0.9), (0, tap(0.04, 900, 4000) * 0.4),
              (0.02, glass(E4, 0.9, 5) * 0.55 + glass(B4, 0.9, 6) * 0.45 + glass(Fs5, 0.9, 7) * 0.3 + glass(Gs5, 0.9, 7) * 0.25)], 1.0)
write('race', room(race, 0.7, 0.18), -10)
# Go: committed. A short rising air into a bright E chord with a deeper thump; it plays on through the loading screen.
rise_env = np.linspace(0, 1, int(0.28 * SR)) ** 2
go = place([(0, filt(sweep_noise(0.28, 500, 4500, 1.1), 300, 9000) * rise_env * 0.35),
            (0.28, thump(65, 0.4, 9)), (0.28, tap(0.04, 1500, 6000) * 0.5),
            (0.28, glass(E5, 1.1, 4) * 0.5 + glass(B5, 1.1, 5) * 0.35 + glass(E6, 1.1, 6) * 0.25 + glass(Gs5, 1.1, 5) * 0.3)], 1.4)
write('go', room(go, 0.9, 0.2), -9)
# Hire: a handshake. Two warm notes up a fourth, B to E.
hire = place([(0, mix(tap(0.03, 1200, 5000) * 0.3, glass(B4, 0.5, 9) * 0.7)), (0.085, mix(glass(E5, 0.7, 7) * 0.8, glass(Gs5, 0.7, 9) * 0.25))], 0.8)
write('hire', room(hire, 0.6, 0.16), -12)
# Buying a car: keys handed over. Two bright small bells over a soft low body; money changes hands without a till.
keys = sum(place([(0.07 + k * 0.022, tap(0.02, 5000, 12000, 300) * (0.25 - 0.05 * k))], 0.2) for k in range(4))
buy = place([(0, thump(98, 0.25, 16) * 0.6), (0, glass(Gs5, 0.6, 9) * 0.6), (0.06, glass(B5, 0.6, 9) * 0.5), (0.06, glass(E6, 0.6, 11) * 0.3), (0, pad(keys, 0.3))], 0.8)
write('buy', room(buy, 0.6, 0.16), -12)
# Error: a soft, low double bump a semitone apart, never a buzzer.
def bump(m, sec=0.11):
    t = t_of(sec); x = np.sin(2 * np.pi * midi(m) * t) + 0.25 * np.sin(2 * np.pi * midi(m) * 2 * t)
    return np.tanh(x * 1.4) * np.exp(-t * 28) * np.minimum(1, t / 0.004)
write('error', room(filt(place([(0, bump(47)), (0.09, bump(46) * 0.85)], 0.3), 60, 2500), 0.3, 0.1), -15)
print('wrote', len(os.listdir(OUT)), 'cues to', OUT)
