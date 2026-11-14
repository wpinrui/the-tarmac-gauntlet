# The warning chimes that play as an issue hits your car, alongside the HUD's flash (FailureBanner).
# Synthesised in E like the menus' UI sounds (tools/ui-sounds), from the same frosted-glass bell.
# python3 tools/issue-chimes/issue-chimes.py <out folder>   (needs numpy)
# Writes 44.1 kHz mono 16-bit WAVs: issue-minor (a medium or minor issue, amber) and issue-major (red).
import numpy as np, wave, sys, os
SR = 44100
rng = np.random.default_rng(11)
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)

def midi(m): return 440 * 2 ** ((m - 69) / 12)
def t_of(sec): return np.arange(int(sec * SR)) / SR

def filt(x, lo=None, hi=None, order=2):
    X = np.fft.rfft(x); f = np.fft.rfftfreq(len(x), 1 / SR) + 1e-9; g = np.ones_like(f)
    if lo: g *= 1 / np.sqrt(1 + (lo / f) ** (2 * order))
    if hi: g *= 1 / np.sqrt(1 + (f / hi) ** (2 * order))
    return np.fft.irfft(X * g, len(x))

def room(x, sec=0.45, mix=0.14, bright=5000):
    t = t_of(sec); ir = rng.standard_normal(len(t)) * np.exp(-t * 6.9 / sec); ir = filt(ir, 200, bright)
    ir /= np.sqrt((ir ** 2).sum())
    wet = np.fft.irfft(np.fft.rfft(np.pad(x, (0, len(ir)))) * np.fft.rfft(np.pad(ir, (0, len(x)))), len(x) + len(ir))
    return np.pad(x, (0, len(ir))) * (1 - mix) + wet * mix * 2.2

def glass(m, sec, decay, vel=1.0, shimmer=0.22):
    t = t_of(sec); f = midi(m)
    x = np.sin(2 * np.pi * f * t) * np.exp(-t * decay)
    x += shimmer * np.sin(2 * np.pi * f * 2.76 * t) * np.exp(-t * decay * 3)
    x += 0.06 * np.sin(2 * np.pi * f * 5.4 * t) * np.exp(-t * decay * 6)
    return x * np.minimum(1, t / 0.0025) * vel

def place(parts, sec):
    out = np.zeros(int(sec * SR))
    for at, x in parts:
        i = int(at * SR); n = min(len(x), len(out) - i)
        out[i:i + n] += x[:n]
    return out

def write(name, x, peak_db):
    x = x / np.max(np.abs(x)) * 10 ** (peak_db / 20)
    with wave.open(os.path.join(OUT, name + '.wav'), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype('<i2').tobytes())

E5, Cs5, B4, Gs4, E6 = 76, 73, 71, 68, 88
# Minor or medium: two notes falling a third, E to C sharp. Heard, not alarming.
write('issue-minor', room(place([(0, glass(E5, 0.35, 12) * 0.8), (0.12, glass(Cs5, 0.5, 9) * 0.75)], 0.7)), -12)
# Major: the same fall, twice and quicker, over a bright octave, so it cuts through the engines.
major = place([(0, glass(E5, 0.25, 16) + glass(E6, 0.25, 22) * 0.3), (0.09, glass(Cs5, 0.25, 16)),
               (0.22, glass(E5, 0.25, 16) + glass(E6, 0.25, 22) * 0.3), (0.31, glass(Cs5, 0.5, 9))], 0.9)
write('issue-major', room(major), -9)
print('wrote issue-minor and issue-major to', OUT)
