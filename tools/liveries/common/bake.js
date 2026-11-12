// Bakes a livery's five SVG panels into one PNG atlas and writes livery.json: where each view
// sits in the atlas (for the projection) and where each race number goes, in atlas pixels.
// Front and rear lie turned a quarter in the atlas so everything fits one square.
//
// Run: node bake.js <views dir> <livery dir> <out dir> [--size 2048] [--sample 8] [--guides] [--model <asset path>]
//   --sample draws that number in every number box, --guides lays the views' part edges
//   over the art. Both are for previews; the game's atlas has neither. --model names the car
//   model the livery was drawn on, so the game never puts it on another.
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const args = process.argv.slice(2);
const flag = (k, d) => { const i = args.indexOf(k); return i < 0 ? d : (args[i + 1] && !args[i + 1].startsWith('--') ? args[i + 1] : true); };
const [viewsDir, liveryDir, outDir] = args;
const SIZE = Number(flag('--size', 2048)), SAMPLE = flag('--sample', null), GUIDES = flag('--guides', false), MODEL = flag('--model', '');
const PAD = 8;
const ORDER = ['left', 'right', 'top', 'front', 'rear'];
const TURNED = new Set(['front', 'rear']);

const views = JSON.parse(fs.readFileSync(path.join(viewsDir, 'views.json'))).views;
const marks = JSON.parse(fs.readFileSync(path.join(liveryDir, 'numbers.json')));

// Column one: left, right, top. Column two: front and rear, turned.
const span = n => { const v = views[n]; return [v.u1 - v.u0, v.v1 - v.v0]; };
const colW = Math.max(...['left', 'right', 'top'].map(n => span(n)[0])), colH = ['left', 'right', 'top'].reduce((s, n) => s + span(n)[1], 0);
const col2W = Math.max(...['front', 'rear'].map(n => span(n)[1])), col2H = ['front', 'rear'].reduce((s, n) => s + span(n)[0], 0);
const ppm = Math.floor(Math.min((SIZE - 3 * PAD) / (colW + col2W), (SIZE - 4 * PAD) / Math.max(colH, col2H)));

const place = {};
let y1 = PAD, y2 = PAD;
for (const n of ORDER) {
  const [w, h] = span(n);
  if (TURNED.has(n)) { place[n] = { x: 2 * PAD + Math.ceil(colW * ppm), y: y2, w: Math.round(h * ppm), h: Math.round(w * ppm), turned: true }; y2 += place[n].h + PAD; }
  else { place[n] = { x: PAD, y: y1, w: Math.round(w * ppm), h: Math.round(h * ppm), turned: false }; y1 += place[n].h + PAD; }
}

// Atlas uv (0..1, origin top left) = (dot(a.xyz, p) + a.w, dot(b.xyz, p) + b.w), p in OBJ metres.
const panels = ORDER.map(n => {
  const v = views[n], p = place[n], s = k => (x => x * k);
  const a = p.turned ? [...v.v.map(s(-ppm / SIZE)), (v.v1 * ppm + p.x) / SIZE] : [...v.u.map(s(ppm / SIZE)), (-v.u0 * ppm + p.x) / SIZE];
  const b = p.turned ? [...v.u.map(s(ppm / SIZE)), (-v.u0 * ppm + p.y) / SIZE] : [...v.v.map(s(ppm / SIZE)), (-v.v0 * ppm + p.y) / SIZE];
  return { view: n, look: v.look, a: a.map(x => +x.toFixed(6)), b: b.map(x => +x.toFixed(6)) };
});

// Number boxes in atlas pixels; rotate is clockwise degrees on the atlas.
const toAtlas = (n, u, w) => {
  const v = views[n], p = place[n];
  return p.turned ? [p.x + (v.v1 - w) * ppm, p.y + (u - v.u0) * ppm] : [p.x + (u - v.u0) * ppm, p.y + (w - v.v0) * ppm];
};
const numbers = marks.numbers.map(m => {
  const [x, y] = toAtlas(m.view, m.u, m.v);
  return { view: m.view, x: +x.toFixed(1), y: +y.toFixed(1), w: +(m.w * ppm).toFixed(1), h: +(m.h * ppm).toFixed(1), rotate: m.rotate + (place[m.view].turned ? 90 : 0), ink: m.ink, ...(m.edge ? { edge: m.edge } : {}) };
});

function paint(size, pieces, numbers, sample, font) {
  return new Promise(async done => {
    const face = new FontFace('Titillium', `url(${font})`); await face.load(); document.fonts.add(face);
    const cv = document.createElement('canvas'); cv.width = cv.height = size;
    const g = cv.getContext('2d');
    g.fillStyle = pieces[0].base; g.fillRect(0, 0, size, size);
    const load = src => new Promise((r, no) => { const i = new Image(); i.onload = () => r(i); i.onerror = () => no(new Error('a panel SVG does not load: check it is valid XML')); i.src = src; });
    for (const p of pieces) {
      const img = await load(p.svg);
      g.save(); g.translate(p.turned ? p.x + p.w : p.x, p.y); if (p.turned) g.rotate(Math.PI / 2);
      const [w, h] = p.turned ? [p.h, p.w] : [p.w, p.h];
      g.drawImage(img, 0, 0, w, h);
      if (p.guide) { g.globalAlpha = 0.45; g.globalCompositeOperation = 'multiply'; g.drawImage(await load(p.guide), 0, 0, w, h); }
      g.restore();
    }
    if (sample !== null) for (const n of numbers) {
      g.save(); g.translate(n.x, n.y); g.rotate(n.rotate * Math.PI / 180);
      g.fillStyle = n.ink; g.font = `${n.h / 0.72}px Titillium`; g.textAlign = 'center'; g.textBaseline = 'middle';
      g.fillText(String(sample), 0, n.h * 0.03); g.restore();
    }
    done(cv.toDataURL('image/png'));
  });
}

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  const uri = (file, type) => `data:${type};base64,${fs.readFileSync(file).toString('base64')}`;
  const pieces = ORDER.map(n => ({ ...place[n], base: marks.base, svg: uri(path.join(liveryDir, `${n}.svg`), 'image/svg+xml'),
    guide: GUIDES ? uri(path.join(viewsDir, `${n}.png`), 'image/png') : null }));
  const font = uri(path.join(__dirname, '../../../unity/Assets/Raceday/Resources/Fonts/TitilliumWeb-Bold.ttf'), 'font/ttf');
  const png = await page.evaluate(([fn, ...a]) => eval('(' + fn + ')')(...a), [paint.toString(), SIZE, pieces, numbers, SAMPLE, font]);
  await browser.close();
  fs.mkdirSync(outDir, { recursive: true });
  fs.writeFileSync(path.join(outDir, 'atlas.png'), Buffer.from(png.split(',')[1], 'base64'));
  fs.writeFileSync(path.join(outDir, 'livery.json'), JSON.stringify({ model: MODEL, size: SIZE, ppm, base: marks.base, panels, numbers }, null, 1));
  console.log('baked', outDir, `${SIZE}px at ${ppm} px/m`);
})();
