// Bakes the race-number digits 0 to 9 for the livery shader: one row of cells, each digit drawn
// from the cell's left edge, cap height filling the cell. Writes Digits.png and Digits.json (each
// digit's advance as a fraction of its height).
//
// The shared digits are white on clear, in Titillium, for every livery without its own. A livery
// can bake its own in another font with an outline: the outline is black and the face white, so
// the shader colours the face in the box's ink and the outline in its edge. Each digit is then
// centred in one shared advance, so the outlines of neighbours never overlap.
//
// Run: node digits.js                                              (the shared digits)
//      node digits.js --font <ttf> --edge 0.09 --step 0.8 --out <dir>   (a livery's own; edge in digit
//      heights, step how far apart digits sit as a share of their width, so they can overlap)
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const args = process.argv.slice(2);
const flag = (k, d) => { const i = args.indexOf(k); return i < 0 ? d : args[i + 1]; };
const OUT = flag('--out', path.join(__dirname, '../../../unity/Assets/Raceday/Resources/Livery'));
const FONT = flag('--font', path.join(__dirname, '../../../unity/Assets/Raceday/Resources/Fonts/TitilliumWeb-Bold.ttf'));
const EDGE = Number(flag('--edge', 0));
const STEP = Number(flag('--step', 1));
const H = 192;   // digit height in pixels
const PAD = 8;   // clear border round each digit, so mip levels keep digits apart

function bake(font, H, PAD, edge) {
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
    const advance = e ? digits.map(() => +(slot / tall).toFixed(4)) : adv.map(a => +(a / H).toFixed(4));
    done({ png: cv.toDataURL('image/png'), cell, width: cv.width, height: cv.height, tall, advance });
  });
}

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  const font = `data:font/ttf;base64,${fs.readFileSync(FONT).toString('base64')}`;
  const r = await page.evaluate(([fn, ...a]) => eval('(' + fn + ')')(...a), [bake.toString(), font, H, PAD, EDGE]);
  await browser.close();
  fs.mkdirSync(OUT, { recursive: true });
  fs.writeFileSync(path.join(OUT, 'Digits.png'), Buffer.from(r.png.split(',')[1], 'base64'));
  const meta = { cell: r.cell / r.width, height: r.tall / r.height, pad: PAD / r.height, padX: PAD / r.width, advance: r.advance, ...(STEP !== 1 ? { step: +(r.advance[0] * STEP).toFixed(4) } : {}) };
  fs.writeFileSync(path.join(OUT, 'Digits.json'), JSON.stringify(meta, null, 1));
  console.log('baked digits', r.width, 'x', r.height, 'to', OUT);
})();
