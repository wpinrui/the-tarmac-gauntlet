"""Lays beams.py's renders out as before and after pairs: one sheet per shot and hour, a row per quality level.

Usage: python sheet.py out_dir
"""
import glob
import os
import sys

from PIL import Image, ImageDraw, ImageFont

out = sys.argv[1]
LEVELS = ("low", "medium", "high", "ultra")
font = ImageFont.load_default(size=22)
tags = sorted({os.path.basename(p).rsplit("-", 2)[0] for p in glob.glob(os.path.join(out, "*-*-before.png"))})
for tag in tags:
    first = Image.open(os.path.join(out, f"{tag}-low-before.png"))
    w, h = first.width // 2, first.height // 2
    pad = 34
    sheet = Image.new("RGB", (w * 2 + 12, (h + pad) * len(LEVELS)), (18, 18, 20))
    d = ImageDraw.Draw(sheet)
    for i, level in enumerate(LEVELS):
        y = i * (h + pad)
        for j, name in enumerate(("before", "after")):
            im = Image.open(os.path.join(out, f"{tag}-{level}-{name}.png")).resize((w, h), Image.LANCZOS)
            sheet.paste(im, (j * (w + 12), y + pad))
            d.text((j * (w + 12) + 6, y + 6), f"{level.title()}  {name}", fill=(230, 230, 230), font=font)
    sheet.save(os.path.join(out, f"sheet-{tag}.png"))
    print("sheet", tag)
