// Bakes the race-number digits 0 to 9 for the livery shader: one row of cells, each digit drawn
// from the cell's left edge, cap height filling the cell. Writes Digits.png and Digits.json (each
// digit's advance as a fraction of its height).
//
// The shared digits are white on clear, in Titillium, for every livery without its own. A livery
// can bake its own in another font with an outline: the outline is black and the face white, so
// the shader colours the face in the box's ink and the outline in its edge. Each digit is then
// centred in one shared advance, so the outlines of neighbours never overlap.
//
// A livery's digits can also be brushed on: --brush streaks the face with dry bristle gaps and thins
// the paint, so what is under the number shows through a little.
//
// Run: node digits.js                                              (the shared digits)
//      node digits.js --font <ttf> --edge 0.09 --step 0.8 --out <dir>   (a livery's own; edge in digit
//      heights, step how far apart digits sit as a share of their width, so they can overlap)
//      node digits.js --font <ttf> --brush 0.6 --out <dir>          (brushed, 0 to 1)
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const args = process.argv.slice(2);
const flag = (k, d) => { const i = args.indexOf(k); return i < 0 ? d : args[i + 1]; };
const OUT = flag('--out', path.join(__dirname, '../../../unity/Assets/Raceday/Resources/Livery'));
const FONT = flag('--font', path.join(__dirname, '../../../unity/Assets/Raceday/Resources/Fonts/TitilliumWeb-Bold.ttf'));
const EDGE = Number(flag('--edge', 0));
const STEP = Number(flag('--step', 1));
const BRUSH = Number(flag('--brush', 0));
const H = 192;   // digit height in pixels
const PAD = 8;   // clear border round each digit, so mip levels keep digits apart

function bake(font, H, PAD, edge, brush) {
  return new Promise(async done => {
    const face = new FontFace('Num', `url(${font})`); await face.load(); document.fonts.add(face);
    const cv = document.createElement('canvas'), g = cv.getContext('2d');
    const probe = px => { g.font = `${px}px Num`; return g.measureText('0').actualBoundingBoxAscent; };
    const px = H / probe(100) * 100;   // font size whose digits are H tall
    g.font = `${px}px Num`;
    const e = edge * H;                // outline width beyond the face, pixels
    const digits = [...'0123456789'];
    const adv = digits.map(d => g.measureText(d).width);
    // Outlined digits are centred by their ink, which in a slanted face runs past the advance.
    const ink = digits.map(d => { const m = g.measureText(d); return [m.actualBoundingBoxLeft, m.actualBoundingBoxRight]; });
    const slot = Math.ceil(Math.max(...ink.map(([l, r]) => l + r)) + 2 * e + 4);
    const tall = H + 2 * Math.ceil(e);  // the outline is part of the digit: the box holds it whole
    const cell = (e ? slot : Math.ceil(Math.max(...adv))) + 2 * PAD;
    cv.width = cell * 10; cv.height = tall + 2 * PAD;
    g.font = `${px}px Num`; g.textBaseline = 'alphabetic'; g.lineJoin = 'round';
    digits.forEach((d, i) => {
      const x = e ? i * cell + PAD + (slot - ink[i][0] - ink[i][1]) / 2 + ink[i][0] : i * cell + PAD, y = PAD + (tall + H) / 2;
      if (e) { g.strokeStyle = '#000000'; g.lineWidth = 2 * e; g.strokeText(d, x, y); }
      g.fillStyle = '#ffffff'; g.fillText(d, x, y);
    });
    if (brush) {
      // Bristle gaps: thin streaks along a slanted stroke, broken up along their length, and paint
      // that is thinner in places. Seeded, so the digits bake the same every time.
      let seed = 7;
      const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
      const lane = Array.from({ length: 4096 }, rnd), along = Array.from({ length: 4096 }, rnd);
      const smooth = (tab, x) => { const i = Math.floor(x), f = x - i, a = tab[((i % 4096) + 4096) % 4096], b = tab[(((i + 1) % 4096) + 4096) % 4096]; return a + (b - a) * f * f * (3 - 2 * f); };
      const img = g.getImageData(0, 0, cv.width, cv.height), d = img.data;
      const c = Math.cos(1.2), s = Math.sin(1.2);
      for (let y = 0; y < cv.height; y++) for (let x = 0; x < cv.width; x++) {
        const k = (y * cv.width + x) * 4; if (!d[k + 3]) continue;
        const w = x * c + y * s, l = -x * s + y * c;
        const gap = Math.max(0, smooth(lane, w / 3) - 0.7) * 3.3 * Math.max(0, smooth(along, l / 30 + w * 0.37) * 2 - 0.6);
        const thin = 0.12 * smooth(along, (x + y * 0.5) / 23 + 999);
        d[k + 3] = Math.round(d[k + 3] * Math.max(0, 1 - brush * (gap + thin)));
      }
      g.putImageData(img, 0, 0);
    }
    const advance = e ? digits.map(() => +(slot / tall).toFixed(4)) : adv.map(a => +(a / H).toFixed(4));
    done({ png: cv.toDataURL('image/png'), cell, width: cv.width, height: cv.height, tall, advance });
  });
}

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  const font = `data:font/ttf;base64,${fs.readFileSync(FONT).toString('base64')}`;
  const r = await page.evaluate(([fn, ...a]) => eval('(' + fn + ')')(...a), [bake.toString(), font, H, PAD, EDGE, BRUSH]);
  await browser.close();
  fs.mkdirSync(OUT, { recursive: true });
  fs.writeFileSync(path.join(OUT, 'Digits.png'), Buffer.from(r.png.split(',')[1], 'base64'));
  const meta = { cell: r.cell / r.width, height: r.tall / r.height, pad: PAD / r.height, padX: PAD / r.width, advance: r.advance, ...(STEP !== 1 ? { step: +(r.advance[0] * STEP).toFixed(4) } : {}) };
  fs.writeFileSync(path.join(OUT, 'Digits.json'), JSON.stringify(meta, null, 1));
  console.log('baked digits', r.width, 'x', r.height, 'to', OUT);
})();
