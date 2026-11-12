"""Shoots a menu screen over an HQ render as UI Toolkit draws it in Gamma or in Linear colour space.

Chromium blends in sRGB, as UI Toolkit did in Gamma. For Linear every colour on the page, every image and the backdrop
are first turned into linear light, so Chromium's blending, glass blur included, adds up linear light as Unity's does.
The shot is then encoded back to sRGB. Chromium keeps 8 bits a channel, which bands the darkest shades in linear light,
so it is shot again four times as bright: where that shot agrees with the first, nothing bright was clipped and its
finer steps are used.

Usage: python3 menu.py html_dir uss assets_dir page backdrop.npy gamma|linear out.png [exposure]
html_dir is MenuPreview's output (tools/menu-preview), backdrop.npy an hq.py render.
"""
import os
import re
import shutil
import subprocess
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "menu-preview"))
from post import lin, enc, frame, bloom  # noqa: E402

html_dir, uss, assets, page, backdrop, mode, out = sys.argv[1:8]
exposure = float(sys.argv[8]) if len(sys.argv) > 8 else 0.45

SHOOT = os.path.join(HERE, "..", "menu-preview", "shoot.js")
PAGE = os.path.join(HERE, "..", "menu-preview", "page.py")


def linear_css(text, gain):
    """Every rgb(), rgba() and hex colour in the page, in linear light, times gain."""
    def ch(v):
        return round(min(float(lin(np.array(float(v) / 255))) * gain, 1.0) * 255, 3)

    def rgb(m):
        parts = [p.strip() for p in m.group(2).split(",")]
        return f"{m.group(1)}({', '.join(str(ch(p)) for p in parts[:3])}{', ' + parts[3] if len(parts) > 3 else ''})"

    def hexc(m):
        h = m.group(1)
        if len(h) in (3, 4): h = "".join(c * 2 for c in h)
        vals = [int(h[i:i + 2], 16) for i in range(0, len(h), 2)]
        out = [ch(v) for v in vals[:3]] + ([vals[3] / 255] if len(vals) > 3 else [1])
        return f"rgba({out[0]}, {out[1]}, {out[2]}, {out[3]:.4f})"

    text = re.sub(r"(rgba?)\(([^)]*)\)", rgb, text)
    return re.sub(r"#([0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3,4})\b(?![-\w])", hexc, text)


def linear_images(folder, gain):
    for root, _, files in os.walk(folder):
        for f in files:
            if f.lower().endswith((".png", ".jpg", ".jpeg")):
                path = os.path.join(root, f)
                im = Image.open(path)
                mode = "RGBA" if "A" in im.getbands() else "RGB"
                a = np.asarray(im.convert(mode), np.float64) / 255
                a[..., :3] = np.minimum(lin(a[..., :3]) * gain, 1)
                Image.fromarray((a * 255 + 0.5).astype(np.uint8), mode).save(path if f.endswith(".png") else path + ".png")
                if not f.endswith(".png"): os.replace(path + ".png", path)


def shoot(work, gain=1):
    pages = os.path.join(work, "pages")
    sub = os.path.join(work, "only")
    shutil.rmtree(sub, ignore_errors=True)
    shutil.copytree(pages, sub, ignore=shutil.ignore_patterns("*.html"))
    shutil.copy(os.path.join(pages, page + ".html"), sub)
    if mode == "linear":
        linear_images(sub, gain)
        path = os.path.join(sub, page + ".html")
        html = linear_css(open(path, encoding="utf-8").read(), gain)
        open(path, "w", encoding="utf-8").write(html)
    png = os.path.join(work, "png")
    subprocess.run(["node", SHOOT, sub, png, "1"], check=True)
    return np.asarray(Image.open(os.path.join(png, page + ".png")).convert("RGB"), np.float64) / 255


def main():
    work = out + ".work"
    shutil.rmtree(work, ignore_errors=True)
    os.makedirs(work)
    shutil.copytree(html_dir, os.path.join(work, "html"))
    subprocess.run([sys.executable, PAGE, os.path.join(work, "html"), uss, assets], check=True)
    os.rename(os.path.join(work, "html", "pages"), os.path.join(work, "pages"))
    r = np.load(backdrop).astype(np.float64)
    scene = frame(r, exposure=exposure, bloom=bloom(r, exposure))
    Image.fromarray((np.clip(scene, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(work, "pages", "scene.png"))
    path = os.path.join(work, "pages", page + ".html")
    html = open(path, encoding="utf-8").read()
    open(path, "w", encoding="utf-8").write(re.sub(r"\.stage\{background:url\([^)]*\) center/cover\}",
                                                  ".stage{background:url(scene.png) center/cover}", html))
    img = shoot(work)
    if mode == "linear":
        fine = shoot(work, 4) / 4
        img = enc(np.where(np.abs(fine - img) <= 1.5 / 255, fine, img))
    Image.fromarray((np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8)).save(out)
    if not os.environ.get("KEEP"): shutil.rmtree(work)
    print("shot", out)


main()
