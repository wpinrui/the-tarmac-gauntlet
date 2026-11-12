// Counts what the trees cost the graphics card from the driver's seat all round the lap, for each look the check
// wrote, and prints the worst views. Counts are per screen pixel: 3.0 means the trees make three fragments for every
// pixel on the screen. See cost.html for what each count is. "depth front" is the depth pass's fragments in front
// of what was drawn before them, 64 trees at a time: what that pass pays for where the hardware's coarse depth test
// throws hidden fragments away before the shader runs.
// Usage: node cost.js <check --out folder> [--step 20] [--no-crop] [--compare-crop] [--bands] [--size 384x240] [--far-at 0.25] [--lod-bias 1] [--json out.json] [look ...]
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const args = process.argv.slice(2);
const opt = (name, fallback) => { const i = args.indexOf(name); if (i < 0) return fallback; const v = args[i + 1]; args.splice(i, 2); return v; };
const flag = name => { const i = args.indexOf(name); if (i < 0) return false; args.splice(i, 1); return true; };
const size = opt('--size', '384x240').split('x').map(Number), step = +opt('--step', 20), farAt = +opt('--far-at', 0.25), lodBias = +opt('--lod-bias', 1), jsonOut = opt('--json', null);
const useCrop = flag('--no-crop') ? 0 : 1, compare = flag('--compare-crop'), bands = flag('--bands');
const [planDir, ...looks] = args;
const here = __dirname, root = path.resolve(here, '../..');
const resources = path.join(root, 'unity/Assets/Raceday/Resources');
const circuitPath = path.join(resources, 'Tracks/circuit.json');
const planSource = fs.readFileSync(path.join(root, 'unity/Assets/Raceday/Scripts/LandscapePlan.cs'), 'utf8');
const table = name => planSource.match(new RegExp(`${name} = \\{([^}]*)\\}`))[1].split(',').map(s => parseFloat(s));
const RADIUS = table('ModelRadius'), ABOVE = table('CentreAbove');

// The driver's eye: on the centre line, 1.1 m up, looking 10 m down the road.
const circuit = JSON.parse(fs.readFileSync(circuitPath, 'utf8'));
const c = circuit.centre, n = c.length / 3, every = Math.max(1, Math.round(step / circuit.step));
const views = [];
for (let i = 0; i < n; i += every) {
  const j = (i + Math.round(10 / circuit.step)) % n;
  views.push({ s: Math.round(i * circuit.step), p: [c[3 * i], c[3 * i + 1] + 1.1, c[3 * i + 2]], q: [c[3 * j], c[3 * j + 1] + 1.1, c[3 * j + 2]] });
}

const types = { '.html': 'text/html', '.png': 'image/png', '.json': 'application/json' };
const server = http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  const file = url === '/circuit.json' ? circuitPath : url.startsWith('/plan/') ? path.join(planDir, url.slice(6))
    : url.startsWith('/atlas/') ? path.join(resources, 'Landscape', url.slice(7)) : path.join(here, url);
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' }); res.end(data);
  });
}).listen(0, '127.0.0.1', async () => {
  const port = server.address().port;
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
  const results = {};
  for (const look of looks.length ? looks : ['woodland', 'farmland', 'highland']) {
    const page = await browser.newPage();
    page.on('pageerror', e => console.log('page error:', e.message));
    page.on('console', m => { if (m.type() === 'error') console.log('console:', m.text()); });
    await page.goto(`http://127.0.0.1:${port}/cost.html`);
    await page.waitForFunction(() => window.ready);
    await page.evaluate(([r, a]) => { window.RADIUS = r; window.ABOVE = a; }, [RADIUS, ABOVE]);
    // By default a quarter of 1536 x 960, Medium's 80% of 1920 x 1200: counts per pixel do not depend on the size.
    await page.evaluate(([l, w, h]) => window.setup(l, w, h), [look, size[0], size[1]]);
    const rows = [];
    if (compare) {
      // Whether cutting the quads down changes any tree's outline: pixels that differ, worst views first.
      const diffs = [];
      for (const v of views) diffs.push({ s: v.s, ...(await page.evaluate(([v, o]) => window.cropDiff(v.p, v.q, 70, o), [v, { farAt, lodBias }])) });
      diffs.sort((a, b) => b.diff - a.diff);
      console.log(`${look}: outline differs on ${(diffs.reduce((s, d) => s + d.diff, 0) / diffs.length * 100).toFixed(3)}% of the screen on average`);
      for (const d of diffs.slice(0, 3)) console.log(`  at ${d.s} m: ${(d.diff * 100).toFixed(3)}% of the screen differs, trees on ${(d.trees * 100).toFixed(1)}%`);
      await page.close();
      continue;
    }
    for (const v of views) rows.push({ s: v.s, ...(await page.evaluate(([v, o]) => window.measure(v.p, v.q, 70, o), [v, { farAt, lodBias, useCrop }])) });
    if (bands) {
      // Where the worst views' fragments come from: trees by their distance from the camera.
      const edges = [0, 50, 100, 200, 400, 800, 1e9];
      for (const r of [...rows].sort((a, b) => b.raster - a.raster).slice(0, 3)) {
        const v = views.find(w => w.s === r.s), parts = [];
        for (let b = 0; b + 1 < edges.length; b++) {
          const m = await page.evaluate(([v, o]) => window.measure(v.p, v.q, 70, o), [v, { farAt, lodBias, useCrop, band: [edges[b], edges[b + 1]] }]);
          parts.push(`${edges[b]}+ m ${m.raster.toFixed(2)}`);
        }
        console.log(`  at ${r.s} m, raster by distance: ${parts.join(', ')}`);
      }
    }
    await page.close();
    results[look] = rows;
    const worst = (key, k = 5) => [...rows].sort((a, b) => b[key] - a[key]).slice(0, k);
    const mean = key => rows.reduce((s, r) => s + r[key], 0) / rows.length;
    const f = x => x.toFixed(2).padStart(6);
    console.log(`\n${look}: ${rows.length} views, crop ${useCrop ? 'on' : 'off'}, far at ${farAt}, LOD bias ${lodBias}`);
    console.log('                 raster  leaves   early  primed raster  primed lit  covered  depth front  quads  chunks');
    console.log(`  mean           ${f(mean('raster'))}  ${f(mean('leaves'))}  ${f(mean('early'))}  ${f(mean('primedRaster'))}         ${f(mean('primedLit'))}   ${f(mean('covered'))}       ${f(mean('depthFront'))}`);
    for (const r of worst('raster'))
      console.log(`  at ${String(r.s).padStart(5)} m     ${f(r.raster)}  ${f(r.leaves)}  ${f(r.early)}  ${f(r.primedRaster)}         ${f(r.primedLit)}   ${f(r.covered)}       ${f(r.depthFront)}  ${String(r.quads).padStart(5)}  ${r.chunks} (${r.nearChunks} near)`);
  }
  if (jsonOut) fs.writeFileSync(jsonOut, JSON.stringify(results));
  await browser.close(); server.close();
});
