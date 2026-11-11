// Bakes the race-number digits 0 to 9 for the livery shader: one row of cells, white on clear,
// each digit drawn from the cell's left edge, cap height filling the cell. Writes Digits.png and
// Digits.json (each digit's advance as a fraction of its height) into the game's livery folder.
//
// Run: node digits.js
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const OUT = path.join(__dirname, '../../../unity/Assets/Raceday/Resources/Livery');
const FONT = path.join(__dirname, '../../../unity/Assets/Raceday/Resources/Fonts/TitilliumWeb-Bold.ttf');
const H = 192;   // digit height in pixels
const PAD = 8;   // clear border round each digit, so mip levels keep digits apart

function bake(font, H, PAD) {
  return new Promise(async done => {
    const face = new FontFace('Num', `url(${font})`); await face.load(); document.fonts.add(face);
    const cv = document.createElement('canvas'), g = cv.getContext('2d');
    const probe = px => { g.font = `${px}px Num`; return g.measureText('0').actualBoundingBoxAscent; };
    const px = H / probe(100) * 100;   // font size whose digits are H tall
    g.font = `${px}px Num`;
    const adv = [...'0123456789'].map(d => g.measureText(d).width);
    const cell = Math.ceil(Math.max(...adv)) + 2 * PAD;
    cv.width = cell * 10; cv.height = H + 2 * PAD;
    g.font = `${px}px Num`; g.fillStyle = '#ffffff'; g.textBaseline = 'alphabetic';
    [...'0123456789'].forEach((d, i) => g.fillText(d, i * cell + PAD, PAD + H));
    done({ png: cv.toDataURL('image/png'), cell, width: cv.width, height: cv.height, advance: adv.map(a => +(a / H).toFixed(4)) });
  });
}

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  const font = `data:font/ttf;base64,${fs.readFileSync(FONT).toString('base64')}`;
  const r = await page.evaluate(([fn, ...a]) => eval('(' + fn + ')')(...a), [bake.toString(), font, H, PAD]);
  await browser.close();
  fs.mkdirSync(OUT, { recursive: true });
  fs.writeFileSync(path.join(OUT, 'Digits.png'), Buffer.from(r.png.split(',')[1], 'base64'));
  const meta = { cell: r.cell / r.width, height: H / r.height, pad: PAD / r.height, padX: PAD / r.width, advance: r.advance };
  fs.writeFileSync(path.join(OUT, 'Digits.json'), JSON.stringify(meta, null, 1));
  console.log('baked digits', r.width, 'x', r.height);
})();
