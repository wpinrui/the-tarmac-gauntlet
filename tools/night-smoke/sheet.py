"""Lays render.py's frames out as before and after pairs: one sheet per shot, hour and floodlights, Medium over Ultra.

Usage: python3 sheet.py out_dir
"""
import glob
import os
import sys

from PIL import Image, ImageDraw, ImageFont

out = sys.argv[1]
font = ImageFont.load_default(size=22)
tags = sorted({os.path.basename(p).rsplit("-", 2)[0] for p in glob.glob(os.path.join(out, "*-before.png"))})
for tag in tags:
    first = Image.open(os.path.join(out, f"{tag}-medium-before.png"))
    w, h = first.width // 2, first.height // 2
    pad = 34
    sheet = Image.new("RGB", (w * 2 + 12, (h + pad) * 2), (18, 18, 20))
    d = ImageDraw.Draw(sheet)
    for i, level in enumerate(("medium", "ultra")):
        y = i * (h + pad)
        for j, name in enumerate(("before", "after")):
            im = Image.open(os.path.join(out, f"{tag}-{level}-{name}.png")).resize((w, h), Image.LANCZOS)
            sheet.paste(im, (j * (w + 12), y + pad))
            d.text((j * (w + 12) + 6, y + 6), f"{level.title()}  {name}", fill=(230, 230, 230), font=font)
    sheet.save(os.path.join(out, f"sheet-{tag}.png"))
    print("sheet", tag)
