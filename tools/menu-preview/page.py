"""Builds one standalone HTML page per screen from MenuPreview's output, for screenshots and checks.

Usage: python3 page.py <MenuPreview output folder> <Menus.uss> <unity Assets folder>
Writes <output>/pages/<screen>.html and copies the images and fonts they use under <output>/pages/assets/.
"""
import os
import shutil
import sys

from uss2css import convert

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))

# The scene behind each screen: the mockup backdrops shot in the Team HQ and at the circuit.
BACKDROPS = {
    "hub": "garage-backdrop.jpg", "main-menu": "garage-backdrop.jpg", "track": "track-backdrop.jpg",
    "car-workshop": "workshop-backdrop.jpg", "new-car-dealer": "newdealer-backdrop.jpg",
    "second-hand-dealer": "secondhand-backdrop.jpg", "crew": "crew-backdrop.jpg", "drivers": "roster-backdrop.jpg",
    "finances": "roster-backdrop.jpg", "race-history": "roster-backdrop.jpg", "standings": "roster-backdrop.jpg",
    "scouting-report": "roster-backdrop.jpg",
}

FONTS = """
@font-face{font-family:'Titillium Web';font-weight:600;src:url(assets/fonts/TitilliumWeb-SemiBold.ttf)}
@font-face{font-family:'Titillium Web';font-weight:700;src:url(assets/fonts/TitilliumWeb-Bold.ttf)}
@font-face{font-family:'Host Grotesk';font-weight:400;src:url(assets/fonts/HostGrotesk-Regular.ttf)}
@font-face{font-family:'Host Grotesk';font-weight:500;src:url(assets/fonts/HostGrotesk-Medium.ttf)}
"""


def build(out, uss, assets):
    pages = os.path.join(out, "pages")
    os.makedirs(os.path.join(pages, "assets", "fonts"), exist_ok=True)
    css = FONTS + convert(open(uss, encoding="utf-8").read())
    for f in ["Raceday/Resources/Fonts/TitilliumWeb-SemiBold.ttf", "Raceday/Resources/Fonts/TitilliumWeb-Bold.ttf",
              "Management/Resources/Fonts/HostGrotesk-Regular.ttf", "Management/Resources/Fonts/HostGrotesk-Medium.ttf"]:
        shutil.copy(os.path.join(assets, f), os.path.join(pages, "assets", "fonts"))
    for img in open(os.path.join(out, "images.txt")).read().split():
        dest = os.path.join(pages, "assets", os.path.relpath(img, assets))
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        shutil.copy(img, dest)
    for line in open(os.path.join(out, "shots.txt")).read().splitlines():
        name, shot = line.split()
        back = BACKDROPS.get(shot, "garage-backdrop.jpg")
        shutil.copy(os.path.join(REPO, "mockups", back), os.path.join(pages, "assets", back))
        body = open(os.path.join(out, name + ".html"), encoding="utf-8").read()
        html = (f"<!doctype html><meta charset=utf-8><style>{css}\nbody{{margin:0;background:#000}}"
                f".stage{{background:url(assets/{back}) center/cover}}</style>"
                f"<div class=stage>{body}</div>")
        open(os.path.join(pages, name + ".html"), "w", encoding="utf-8").write(html)


if __name__ == "__main__":
    build(*sys.argv[1:4])
