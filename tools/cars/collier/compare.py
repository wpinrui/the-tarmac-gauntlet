"""Reference next to render for every view, in one sheet."""
import sys
from PIL import Image
import cams as C
rend, out = sys.argv[1], sys.argv[2]
names = sys.argv[3].split(',') if len(sys.argv) > 3 else C.NAMES
rows = []
for n in names:
    a = Image.open(f'ref/{n}.png').convert('RGB')
    b = Image.open(f'{rend}/{n}.png').convert('RGB').resize(a.size)
    r = Image.new('RGB', (a.width * 2 + 10, a.height), (255, 255, 255))
    r.paste(a, (0, 0)); r.paste(b, (a.width + 10, 0))
    k = 900 / r.width
    rows.append(r.resize((900, int(r.height * k))))
H = sum(r.height for r in rows)
sheet = Image.new('RGB', (900, H), (255, 255, 255))
y = 0
for r in rows:
    sheet.paste(r, (0, y)); y += r.height
sheet.save(out)
