// Renders the looks tools/landscape/check wrote, from a few broadcast-style cameras.
// Usage: node preview.js <check --out folder> <atlas folder> <out folder> [look ...]
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const [planDir, atlasDir, outDir, ...looks] = process.argv.slice(2);
const here = __dirname, root = path.resolve(here, '../..');
const circuitPath = path.join(root, 'unity/Assets/Raceday/Resources/Tracks/circuit.json');
const types = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json', '.png': 'image/png', '.jpg': 'image/jpeg' };
const server = http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  const file = url === '/circuit.json' ? circuitPath : url.startsWith('/plan/') ? path.join(planDir, url.slice(6))
    : url.startsWith('/atlas/') ? path.join(atlasDir, url.slice(7)) : path.join(here, url);
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' }); res.end(data);
  });
}).listen(0, '127.0.0.1', async () => {
  const port = server.address().port;
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
  for (const look of looks.length ? looks : ['woodland', 'farmland', 'highland']) {
    const page = await browser.newPage();
    page.on('pageerror', e => console.log('page error:', e.message));
    page.on('console', m => { if (m.type() === 'error') console.log('console:', m.text()); });
    await page.goto(`http://127.0.0.1:${port}/preview.html`);
    await page.waitForFunction(() => window.ready);
    await page.evaluate(([l]) => window.build(l, 17.5), [look]);
    const shots = JSON.parse(fs.readFileSync(path.join(here, 'shots.json'), 'utf8'));
    for (const s of shots) {
      const data = await page.evaluate(s => window.shoot(s.p, s.q, s.fov, 1600, 900), s);
      fs.writeFileSync(path.join(outDir, `${look}_${s.name}.jpg`), Buffer.from(data.split(',')[1], 'base64'));
      console.log(look, s.name);
    }
    await page.close();
  }
  await browser.close(); server.close();
});
