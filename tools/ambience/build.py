"""Builds the race ambience clips in unity/Assets/Raceday/Resources/Ambience from public-domain field recordings.

Every source is public domain (CC0, or the Public Domain Mark on Radio Aporee's archive.org uploads). The script
downloads each one into out/ambience/raw, cuts the parts used, makes the beds loop seamlessly, levels everything
and writes Ogg Vorbis. Needs ffmpeg and numpy. Run from the repo root:
    python3 tools/ambience/build.py
"""
import os
import subprocess
import sys
import urllib.request

import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RAW = os.path.join(ROOT, "out", "ambience", "raw")
OUT = os.path.join(ROOT, "unity", "Assets", "Raceday", "Resources", "Ambience")
SR = 44100
IA = "https://archive.org/download/"
OGA = "https://opengameart.org/sites/default/files/"

# name: (url, page, licence, what it is)
SOURCES = {
    "stadium": (IA + "aporee_65154_75249/football20240903162614.flac", "https://archive.org/details/aporee_65154_75249",
                "Public Domain Mark", "Second division football crowd at TUIL Stadium, Tromsdalen, Norway"),
    "marathon": (IA + "aporee_54419_62244/AntwerpNightMarathonfulllengthaudio.flac", "https://archive.org/details/aporee_54419_62244",
                 "Public Domain Mark", "Crowds cheering the Antwerp Night Marathon, Belgium"),
    "station": (IA + "aporee_51225_58462/stat20201005215245.flac", "https://archive.org/details/aporee_51225_58462",
                "Public Domain Mark", "Train announcements at Cantu Asnago station, Italy"),
    "rain-light": (OGA + "amb_rain_loop_1.wav", "https://opengameart.org/content/amb-rain-loop-1", "CC0", "Steady rain"),
    "rain-heavy": (IA + "aporee_46405_52712/ZOOM0052.flac", "https://archive.org/details/aporee_46405_52712",
                   "Public Domain Mark", "Sheltering in trees in heavy rain, Heaton Park, Newcastle, UK"),
    "storm-melle": (IA + "aporee_36892_42275/Thunderstorm20170616145820.mp3", "https://archive.org/details/aporee_36892_42275",
                    "Public Domain Mark", "Thunderstorm, Melle, Germany"),
    "storm-bologna": (IA + "aporee_60932_70046/Temporale.flac", "https://archive.org/details/aporee_60932_70046",
                      "Public Domain Mark", "Thunderstorm, Bologna, Italy"),
    "storm-nl": (IA + "aporee_44701_50804/190604001.mp3", "https://archive.org/details/aporee_44701_50804",
                 "Public Domain Mark", "Rain and thunder, Heerhugowaard, Netherlands"),
    "birds": (OGA + "park_ambience_birds.wav", "https://opengameart.org/content/park-ambiences", "CC0", "Park birds"),
    "crickets": (IA + "aporee_70552_82256/FranceMoulinDeCostLeDerbouxNightCricketsStereoBlumlein.flac",
                 "https://archive.org/details/aporee_70552_82256", "Public Domain Mark", "Crickets at night near Le Derboux, France"),
}


def fetch(name):
    url = SOURCES[name][0]
    path = os.path.join(RAW, name + os.path.splitext(url)[1])
    if not os.path.exists(path):
        print("downloading", url)
        urllib.request.urlretrieve(url, path)
    return path


def load(name, start=0.0, length=None, channels=2):
    """A piece of a source as float32 (frames, channels) at SR."""
    args = ["ffmpeg", "-v", "error", "-ss", str(start)]
    if length is not None:
        args += ["-t", str(length)]
    args += ["-i", fetch(name), "-ac", str(channels), "-ar", str(SR), "-f", "f32le", "-"]
    raw = subprocess.run(args, capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.float32).reshape(-1, channels).copy()


def filt(x, chain):
    """Runs audio through an ffmpeg filter chain."""
    ch = x.shape[1]
    p = subprocess.run(["ffmpeg", "-v", "error", "-f", "f32le", "-ac", str(ch), "-ar", str(SR), "-i", "-",
                        "-af", chain, "-ac", str(ch), "-ar", str(SR), "-f", "f32le", "-"],
                       input=x.astype(np.float32).tobytes(), capture_output=True, check=True).stdout
    return np.frombuffer(p, np.float32).reshape(-1, ch).copy()


def rms_db(x):
    return 20 * np.log10(np.sqrt(np.mean(x ** 2)) + 1e-9)


def tonal(x):
    """How far the strongest narrow peak between 300 Hz and 5 kHz stands above its neighbours, dB. A car horn, a whistle
    or a chant stands 13 dB or more above; a crowd's hubbub stays under."""
    m = x.mean(axis=1)
    n = 4096
    frames = [m[i:i + n] * np.hanning(n) for i in range(0, len(m) - n, n // 2)]
    db = 10 * np.log10(np.mean([np.abs(np.fft.rfft(f)) ** 2 for f in frames], axis=0) + 1e-12)
    f = np.fft.rfftfreq(n, 1 / SR)
    band = (f > 300) & (f < 5000)
    pad = np.pad(db, 25, mode="edge")
    med = np.median(np.lib.stride_tricks.sliding_window_view(pad, 51), axis=1)
    return float((db - med)[band].max())


def quiet_chunks(x, chunk, keep, above=None):
    """Chunks of the recording, chunk seconds long, the steadiest `keep` share of them: those nearest the median level
    (or, with above, the loudest ones), so a bed carries no sudden events. Chunks with a horn, whistle or chant in them
    are left out."""
    n = int(chunk * SR)
    parts = [x[i:i + n] for i in range(0, len(x) - n + 1, n)]
    parts = [p for p in parts if tonal(p) < 13]
    levels = np.array([rms_db(p) for p in parts])
    if above:
        order = np.argsort(-levels)
    else:
        order = np.argsort(np.abs(levels - np.median(levels)) + np.maximum(0, levels - np.median(levels)) * 2)
    picked = sorted(order[:max(1, int(len(parts) * keep))])
    return [parts[i] for i in picked]


def splice(parts, fade=0.6):
    """Joins pieces with equal-power crossfades."""
    f = int(fade * SR)
    t = np.linspace(0, np.pi / 2, f)[:, None]
    out = parts[0]
    for p in parts[1:]:
        out = np.concatenate([out[:-f], out[-f:] * np.cos(t) + p[:f] * np.sin(t), p[f:]])
    return out


def loop(x, length, fade=2.0):
    """A seamless loop `length` seconds long: the tail past the end is crossfaded into the start."""
    n, f = int(length * SR), int(fade * SR)
    assert len(x) >= n + f, (len(x) / SR, length + fade)
    t = np.linspace(0, np.pi / 2, f)[:, None]
    out = x[:n].copy()
    out[:f] = x[:f] * np.sin(t) + x[n:n + f] * np.cos(t)
    return out


def best_window(name, length, start=0.0, end=None, skip=()):
    """The loudest stretch of a recording `length` seconds long with no horn or whistle in it, as a start time."""
    x = load(name, start, None if end is None else end - start)
    n = int(4 * SR)
    chunks = [x[i:i + n] for i in range(0, len(x) - n + 1, n)]
    level = [rms_db(c) if tonal(c) < 13 else -200 for c in chunks]
    k = int(length // 4)
    best, at = -1e9, 0
    for i in range(len(chunks) - k + 1):
        t = start + i * 4
        if any(a - length < t < b for a, b in skip):
            continue
        score = min(level[i:i + k]) + np.mean(level[i:i + k])
        if score > best:
            best, at = score, t
    return at


def shot(x, fade_in=0.4, fade_out=2.5):
    a, b = int(fade_in * SR), int(fade_out * SR)
    x = x.copy()
    x[:a] *= np.linspace(0, 1, a)[:, None]
    x[-b:] *= (np.linspace(1, 0, b) ** 2)[:, None]
    return x


def loudness(x):
    p = subprocess.run(["ffmpeg", "-v", "info", "-nostats", "-f", "f32le", "-ac", str(x.shape[1]), "-ar", str(SR), "-i", "-",
                        "-af", "ebur128", "-f", "null", "-"], input=x.astype(np.float32).tobytes(), capture_output=True).stderr
    for line in reversed(p.decode().splitlines()):
        if line.strip().startswith("I:"):
            return float(line.split()[1])
    raise RuntimeError("no loudness")


def write(name, x, lufs):
    """Levels to `lufs` (keeping peaks under -1 dBFS) and writes Ogg Vorbis."""
    x = x * 10 ** ((lufs - loudness(x)) / 20)
    if np.abs(x).max() > 0.89:
        # A few raindrops or claps on the mic stand far above the rest: a fast limiter takes them down to the level.
        x = filt(x, "alimiter=limit=0.89:attack=2:release=60:level=disabled")
    path = os.path.join(OUT, name + ".ogg")
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ac", str(x.shape[1]), "-ar", str(SR), "-i", "-",
                    "-c:a", "libvorbis", "-q:a", "4", path], input=x.astype(np.float32).tobytes(), check=True)
    print(f"{name}.ogg {len(x) / SR:5.1f}s {loudness(x):6.1f} LUFS")


# ---------------------------------------------------------------- the clips

# The stadium recording's dictaphone whines at 3.65 kHz.
STADIUM = "highpass=f=90,equalizer=f=3646:t=q:w=6:g=-30,equalizer=f=3630:t=q:w=4:g=-15"

def crowd():
    # The bed: the murmur of a crowd sitting through a match, steadiest stretches of two recordings, layered so it reads
    # as a big stand rather than a few voices. A little air taken off the top, as it reaches you across the track.
    stadium = splice(quiet_chunks(filt(load("stadium", 0, 352), STADIUM), 4, 0.6))
    marathon = load("marathon", 0, 200)   # before the horns at 210 s
    marathon = splice(quiet_chunks(filt(marathon, "highpass=f=110"), 4, 0.55))
    n = min(len(stadium), len(marathon), int(62 * SR))
    calm = stadium[:n] / 10 ** (rms_db(stadium[:n]) / 20) + 0.8 * marathon[:n] / 10 ** (rms_db(marathon[:n]) / 20)
    calm = filt(calm, "lowpass=f=7000")
    write("crowd-calm", loop(calm, 60).mean(axis=1, keepdims=True), -24)

    # The swell: the marathon crowd cheering and clapping runners past, its loudest stretches.
    lively = load("marathon", 300, 500)
    lively = splice(quiet_chunks(filt(lively, "highpass=f=110,lowpass=f=8000"), 4, 0.35, above=True))
    write("crowd-lively", loop(lively, min(48, len(lively) / SR - 2.5)).mean(axis=1, keepdims=True), -22)

    # Roars for the start, the finish and overtakes in front of a stand: the home side's goal, and the two loudest
    # horn-free stretches of the marathon crowd.
    write("crowd-roar-1", shot(filt(load("stadium", 135.5, 24), STADIUM + ",lowpass=f=7500"), 0.3, 6).mean(axis=1, keepdims=True), -19)
    a = best_window("marathon", 16, 290)
    b = best_window("marathon", 16, 290, skip=[(a - 4, a + 20)])
    print("marathon roars at", a, b)
    for i, t in enumerate((a, b)):
        roar = shot(filt(load("marathon", t, 16), "highpass=f=110,lowpass=f=8000"), 0.8, 5)
        write(f"crowd-roar-{i + 2}", roar.mean(axis=1, keepdims=True), -19)


def pa():
    # Station announcements from a horn speaker, made wordless: the band a horn carries, the consonants cut,
    # then the slapback of a run of speakers down the straight and the echo off the stands.
    chain = ("highpass=f=380,lowpass=f=1500,lowpass=f=1500,"
             "aecho=0.85:0.75:95|210|360|540:0.55|0.4|0.3|0.2,"
             "aecho=0.8:0.6:900|1400:0.25|0.15,highpass=f=300,lowpass=f=2200")
    for i, (a, b) in enumerate([(27.5, 49.5), (51.5, 74.5), (295.5, 334.5)]):
        x = load("station", a, b - a, 1)
        x = np.concatenate([x, np.zeros((int(2.5 * SR), 1), np.float32)])
        write(f"pa-{i + 1}", shot(filt(x, chain), 0.05, 2.5), -24)


def rain():
    write("rain-light", loop(filt(load("rain-light", 0, 88.5), "highpass=f=120"), 84), -26)
    write("rain-heavy", loop(filt(load("rain-heavy", 8, 128), "highpass=f=140"), 120, 3), -22)


def thunder():
    picks = [("storm-melle", 34.5, 14), ("storm-melle", 104.5, 13), ("storm-melle", 246.5, 16),
             ("storm-melle", 552.5, 15), ("storm-bologna", 123.5, 22), ("storm-nl", 654.5, 30)]
    for i, (name, a, n) in enumerate(picks):
        write(f"thunder-{i + 1}", shot(filt(load(name, a, n), "highpass=f=28"), 0.6, 5), -20)


def countryside():
    write("country-day", loop(filt(load("birds", 30, 95), "highpass=f=250,equalizer=f=444:t=q:w=6:g=-24"), 90, 3), -30)
    write("country-night", loop(filt(load("crickets", 20, 95), "highpass=f=250"), 90, 3), -32)


if __name__ == "__main__":
    os.makedirs(RAW, exist_ok=True)
    os.makedirs(OUT, exist_ok=True)
    jobs = {"crowd": crowd, "pa": pa, "rain": rain, "thunder": thunder, "countryside": countryside}
    for name in sys.argv[1:] or jobs:
        jobs[name]()
