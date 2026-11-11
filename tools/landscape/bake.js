// Bakes the impostor atlases for the landscape's trees from Poly Haven glTF models.
// Usage: node bake.js <folder of Poly Haven <id>/<id>_1k.gltf> <out folder> [name ...]
// Writes <name>_colour.png, <name>_normal.png and <name>.json per tree (see trees.json).
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('playwright');

const [srcDir, outDir, ...only] = process.argv.slice(2);
const here = __dirname;
const trees = JSON.parse(fs.readFileSync(path.join(here, 'trees.json'), 'utf8'));
const types = { '.html': 'text/html', '.js': 'text/javascript', '.gltf': 'model/gltf+json', '.bin': 'application/octet-stream', '.jpg': 'image/jpeg', '.png': 'image/png' };

const server = http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  const file = url.startsWith('/src/') ? path.join(srcDir, url.slice(5)) : path.join(here, url);
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' });
    res.end(data);
  });
}).listen(0, '127.0.0.1', async () => {
  const port = server.address().port;
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
  const page = await browser.newPage();
  page.on('pageerror', e => console.log('page error:', e.message));
  await page.goto(`http://127.0.0.1:${port}/bake.html`);
  await page.waitForFunction(() => window.ready, null, { timeout: 60000 });
  for (const [name, t] of Object.entries(trees)) {
    if (only.length && !only.includes(name)) continue;
    const spec = { url: `/src/${t.model}/${t.model}_1k.gltf`, node: t.node ?? -1, leavesOnly: !!t.leavesOnly, thicken: t.thicken || 1, frames: 8, size: 256 };
    const out = await page.evaluate(s => window.bake(s), spec);
    for (const k of ['colour', 'normal'])
      fs.writeFileSync(path.join(outDir, `${name}_${k}.png`), Buffer.from(out[k].split(',')[1], 'base64'));
    fs.writeFileSync(path.join(outDir, `${name}.json`), JSON.stringify(out.meta, null, 1));
    console.log(name, JSON.stringify(out.meta));
  }
  await browser.close();
  server.close();
});
