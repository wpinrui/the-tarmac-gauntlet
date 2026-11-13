"""Synthesises the podium's own sounds into unity/Assets/Raceday/Resources/Podium: the champagne corks, the spray's
fizz and the confetti cannon. Nothing is downloaded, so there is no licence to track. The crowd below cheers with
the race ambience's own crowd recordings (tools/ambience/build.py). Needs ffmpeg and numpy. Run from the repo root:
    python3 tools/podium/sounds.py
"""
import os
import subprocess

import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "unity", "Assets", "Raceday", "Resources", "Podium")
SR = 44100


def filt(x, chain):
    """Runs mono audio through an ffmpeg filter chain."""
    p = subprocess.run(["ffmpeg", "-v", "error", "-f", "f32le", "-ac", "1", "-ar", str(SR), "-i", "-",
                        "-af", chain, "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
                       input=x.astype(np.float32).tobytes(), capture_output=True, check=True).stdout
    return np.frombuffer(p, np.float32).copy()


def t_of(seconds):
    return np.arange(int(seconds * SR)) / SR


def place(out, x, at):
    """Adds x into out starting at `at` seconds, clipped to out's end."""
    i = int(at * SR)
    n = min(len(x), len(out) - i)
    if n > 0:
        out[i:i + n] += x[:n]


def write(name, x, peak_db):
    """Scales to a peak of `peak_db` dBFS and writes Ogg Vorbis."""
    x = x * (10 ** (peak_db / 20) / (np.abs(x).max() + 1e-9))
    path = os.path.join(OUT, name + ".ogg")
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ac", "1", "-ar", str(SR), "-i", "-",
                    "-c:a", "libvorbis", "-q:a", "5", path], input=x.astype(np.float32).tobytes(), check=True)
    print(f"{name}.ogg {len(x) / SR:4.2f}s")


def cork(rng, pitch):
    """A cork leaving the neck: a sharp click, the neck's hollow pop falling in pitch, then the gas rushing out."""
    t = t_of(0.9)
    click = rng.standard_normal(len(t)) * np.exp(-t / 0.0015)
    click = filt(click, "highpass=f=1500")
    f = pitch * (1.0 + 0.6 * np.exp(-t / 0.012))
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.035) * (1 - np.exp(-t / 0.0008))
    thump = np.sin(2 * np.pi * 95 * t) * np.exp(-t / 0.05) * 0.5
    gas = filt(rng.standard_normal(len(t)), "highpass=f=1800,lowpass=f=9000")
    gas *= (1 - np.exp(-t / 0.02)) * np.exp(-t / 0.18) * 0.22
    x = click * 0.8 + body + thump + gas
    return filt(x, "afade=t=out:st=0.7:d=0.2")


def fizz(rng, length=6.0, fade=1.0):
    """The champagne's spray, looping: a hiss that surges with the thumb on the neck, full of bursting bubbles."""
    n = int((length + fade) * SR)
    t = np.arange(n) / SR
    hiss = filt(rng.standard_normal(n), "highpass=f=1200,lowpass=f=10000")
    # The thumb working the neck: the stream surges and slackens.
    surge = np.zeros(n)
    for k, (hz, depth) in enumerate([(0.7, 0.3), (1.9, 0.18), (3.3, 0.1)]):
        surge += depth * np.sin(2 * np.pi * hz * t + rng.uniform(0, 6.28))
    hiss *= 0.6 + surge
    # Bubbles and droplets: thousands of tiny decaying pings.
    pops = np.zeros(n)
    for _ in range(int(900 * (length + fade))):
        at = rng.uniform(0, length + fade)
        f = rng.uniform(1800, 7000)
        d = rng.uniform(0.002, 0.008)
        g = t_of(d * 6)
        place(pops, np.sin(2 * np.pi * f * g) * np.exp(-g / d) * rng.uniform(0.05, 0.35), at)
    # Splashes as the stream hits overalls and the floor.
    splash = filt(rng.standard_normal(n), "bandpass=f=900:w=1400")
    env = np.zeros(n)
    for _ in range(int(6 * (length + fade))):
        g = t_of(0.25)
        place(env, np.exp(-g / 0.06) * rng.uniform(0.2, 0.7), rng.uniform(0, length + fade))
    x = hiss + pops + splash * env * 0.6
    # Seamless: the tail past the end crossfades into the start.
    m, f = int(length * SR), int(fade * SR)
    w = np.linspace(0, np.pi / 2, f)
    out = x[:m].copy()
    out[:f] = x[:f] * np.sin(w) + x[m:m + f] * np.cos(w)
    return out


def confetti(rng):
    """A confetti cannon: a low boom and a crack, then paper fluttering down for a few seconds."""
    t = t_of(5.0)
    f = 48 + 40 * np.exp(-t / 0.03)
    boom = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.16)
    crack = filt(rng.standard_normal(len(t)) * np.exp(-t / 0.006), "highpass=f=600")
    # Thousands of paper flakes, each a short soft rustle; dense at first, thinning as they land.
    paper = np.zeros(len(t))
    grain = t_of(0.018)
    for _ in range(2200):
        at = rng.exponential(1.0) + 0.05
        if at > 4.8:
            continue
        g = rng.standard_normal(len(grain)) * np.sin(np.pi * grain / grain[-1]) ** 2 * rng.uniform(0.05, 1) ** 2
        place(paper, g, at)
    paper = filt(paper, "highpass=f=2500,lowpass=f=11000")
    paper *= 1 - np.exp(-t / 0.08)
    x = boom * 1.0 + crack * 0.7 + paper * 0.2
    return filt(x, "afade=t=out:st=4.2:d=0.8")


def main():
    os.makedirs(OUT, exist_ok=True)
    rng = np.random.default_rng(449)
    for i, pitch in enumerate([520, 460, 580]):
        write(f"cork-{i + 1}", cork(rng, pitch), -3)
    write("fizz", fizz(rng), -8)
    write("confetti", confetti(rng), -2)


if __name__ == "__main__":
    main()
