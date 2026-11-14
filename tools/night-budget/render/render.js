// Renders the night views of the full grid with render.html, from the scene tools/night-budget writes and the cars
// cars.py packs. Needs Node with Playwright and Chromium.
//   node render.js <scene-folder> <out-folder>
// Writes head-on.png, stands.png, behind.png, behind-braking.png, head-on-uncapped.png, stands-floodlights-only.png and stands-no-lamps.png, each the full grid at night with every floodlight and the most
// headlamps the graphics options allow.
const http = require('http');
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const [sceneDir, outDir] = process.argv.slice(2).map(p => path.resolve(p));
fs.mkdirSync(outDir, { recursive: true });
const page = path.join(__dirname, 'render.html');
const server = http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  const file = url === '/' ? page : path.join(sceneDir, path.normalize(url).replace(/^(\.\.[/\\])+/, ''));
  if (!file.startsWith(sceneDir) && file !== page) { res.writeHead(403); res.end(); return; }
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'Content-Type': file.endsWith('.html') ? 'text/html' : 'application/octet-stream' });
    res.end(data);
  });
});

const sub = (a, b) => a.map((x, i) => x - b[i]), add = (a, b) => a.map((x, i) => x + b[i]), mul = (a, s) => a.map(x => x * s);

(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await chromium.launch({ args: ['--enable-unsafe-swiftshader', '--use-angle=swiftshader'] });
  const tab = await browser.newPage({ viewport: { width: 1600, height: 900 } });
  tab.on('console', m => console.log('page:', m.text()));
  tab.on('pageerror', e => console.log('page error:', e.message));
  await tab.goto(`http://127.0.0.1:${port}/`);
  await tab.evaluate(() => window.load('/'));
  const info = await tab.evaluate(() => window.sceneInfo());
  const grid = info.grid, pole = grid[0], last = grid[grid.length - 1];
  const f = pole.forward, up = [0, 1, 0];
  const middle = mul(grid.reduce((a, g) => add(a, g.at), [0, 0, 0]), 1 / grid.length);
  const flat = sub(middle, info.stand); flat[1] = 0;
  const toward = mul(flat, 1 / Math.hypot(...flat));
  const views = {
    'head-on': { eye: add(add(pole.at, mul(f, 24)), mul(up, 1.4)), target: add(grid[12].at, mul(up, 0.6)), fov: 38 },
    // From the main stand's seats, a few rows up, looking down across the grid.
    stands: { eye: add(add(info.stand, mul(toward, 4)), mul(up, 2.5)), target: add(grid[10].at, mul(up, -1)), fov: 55 },
    // The same head-on view with the budget off, every lamp URP's own: what the caps keep off the screen.
    'head-on-uncapped': { eye: add(add(pole.at, mul(f, 24)), mul(up, 1.4)), target: add(grid[12].at, mul(up, 0.6)), fov: 38, budget: false },
    // From the stands with the floodlights alone, and with no lamps at all: what the floodlights do on their own.
    'stands-floodlights-only': { eye: add(add(info.stand, mul(toward, 4)), mul(up, 2.5)), target: add(grid[10].at, mul(up, -1)), fov: 55, headlamps: 0 },
    'stands-no-lamps': { eye: add(add(info.stand, mul(toward, 4)), mul(up, 2.5)), target: add(grid[10].at, mul(up, -1)), fov: 55, headlamps: 0, floods: false },
    behind: { eye: add(add(last.at, mul(f, -16)), mul(up, 2.2)), target: add(grid[4].at, mul(up, 0.5)), fov: 38 },
    // The same with every car on the brakes: the brake lamps lit over the tail lamps.
    'behind-braking': { eye: add(add(last.at, mul(f, -16)), mul(up, 2.2)), target: add(grid[4].at, mul(up, 0.5)), fov: 38, braking: grid.map((g, i) => i) },
  };
  const only = process.env.VIEWS ? process.env.VIEWS.split(',') : Object.keys(views);
  for (const name of only) {
    const v = { width: 1600, height: 900, cars: grid.length, headlamps: Number(process.env.HEADLAMPS || 20), ...views[name] };
    const t = Date.now();
    const r = await tab.evaluate(v => window.render(v), v);
    await tab.screenshot({ path: path.join(outDir, name + '.png') });
    console.log(`${name}: ${r.floods} floodlights and ${r.headlamps} headlamps, ${((Date.now() - t) / 1000).toFixed(0)} s`);
  }
  await browser.close();
  server.close();
})().catch(e => { console.error(e); process.exit(1); });
