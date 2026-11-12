// Renders each near 3D tree at both levels of detail beside its impostor (trees3d-view.html), level and from above.
// Usage: node trees3d-view.js <out folder> [Resources/Landscape folder] [name ...] [--calibrate]
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const here = __dirname;
const args = process.argv.slice(2);
const calibrate = args.includes('--calibrate');
const [outDir, resArg, ...only] = args.filter(a => a !== '--calibrate');
const res = resArg || path.join(here, '../../unity/Assets/Raceday/Resources/Landscape');
const plan = fs.readFileSync(path.join(here, '../../unity/Assets/Raceday/Scripts/LandscapePlan.cs'), 'utf8');
const table = name => plan.match(new RegExp(`${name} = \\{([^}]*)\\}`))[1].split(',').map(s => parseFloat(s));
const RADIUS = table('ModelRadius'), ABOVE = table('CentreAbove');
// LandscapeLooks.Leaves, by kind.
const TINT = [[0.82, 0.98, 0.72], [0.86, 1.02, 0.70], [0.95, 1.02, 0.85], [0.80, 0.92, 0.78], [0.80, 0.92, 0.78], [0.80, 0.95, 0.85], [0.80, 0.95, 0.85], [0.78, 0.98, 0.70]];
const KINDS = ['oak', 'beech', 'birch', 'pine_a', 'pine_b', 'spruce_a', 'spruce_b', 'bush'];
const types = { '.html': 'text/html', '.js': 'text/javascript', '.jpg': 'image/jpeg', '.png': 'image/png' };
const server = http.createServer((req, rs) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  const file = url.startsWith('/trees3d/') ? path.join(res, 'Trees3D', url.slice(9)) : url.startsWith('/atlas/') ? path.join(res, url.slice(7)) : path.join(here, url);
  fs.readFile(file, (err, data) => {
    if (err) { rs.writeHead(404); rs.end(); return; }
    rs.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' }); rs.end(data);
  });
}).listen(0, '127.0.0.1', async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
  const page = await browser.newPage();
  page.on('pageerror', e => console.log('page error:', e.message));
  page.on('console', m => { if (m.type() === 'error') console.log('console:', m.text()); });
  await page.goto(`http://127.0.0.1:${server.address().port}/trees3d-view.html`);
  await page.waitForFunction(() => window.ready);
  // With --calibrate, each level's colour is scaled so that, lit from several suns and seen level and from above, it
  // averages to its impostor's colour, and trees3d.bytes is written back.
  const suns = calibrate ? [[0.4, 0.75, 0.5], [-0.4, 0.5, -0.7], [0.9, 0.3, 0], [0, 1, 0.05], [-0.6, 0.35, 0.6]] : [null];
  const fixes = {};
  for (const [k, name] of KINDS.entries()) {
    if (only.length && !only.includes(name)) continue;
    const sum = [[0, 0, 0], [0, 0, 0]];
    for (const sun of suns)
      for (const [view, lift] of [['level', 0.05], ['above', 0.45]]) {
        await page.evaluate(s => { window.SUN = s; }, sun);
        const r = await page.evaluate(a => window.render(...a), [name, RADIUS[k], ABOVE[k], TINT[k], 1500, 600, lift]);
        if (!calibrate) fs.writeFileSync(path.join(outDir, `${name}-${view}.png`), Buffer.from(r.url.split(',')[1], 'base64'));
        for (let l = 0; l < 2; l++) for (let c = 0; c < 3; c++) sum[l][c] += Math.log(r.means[2][c] / r.means[l][c]);
      }
    fixes[name] = sum.map(s => s.map(v => Math.exp(v / (suns.length * 2))));
    console.log(name, calibrate ? JSON.stringify(fixes[name].map(f => f.map(v => +v.toFixed(2)))) : '');
  }
  if (calibrate) {
    const file = path.join(res, 'Trees3D', 'trees3d.bytes'), buf = fs.readFileSync(file);
    let o = 20;
    for (const name of KINDS)
      for (let l = 0; l < 2; l++) {
        const nv = buf.readInt32LE(o), nb = buf.readInt32LE(o + 4), nl = buf.readInt32LE(o + 8);
        if (fixes[name]) for (let c = 0; c < 3; c++) buf.writeFloatLE(Math.min(Math.max(buf.readFloatLE(o + 12 + c * 4) * fixes[name][l][c], 0.05), 3), o + 12 + c * 4);
        o += 24 + nv * 4 * (3 + 3 + 2 + 3) + (nb + nl) * 4;
      }
    fs.writeFileSync(file, buf);
  }
  await browser.close(); server.close();
});
