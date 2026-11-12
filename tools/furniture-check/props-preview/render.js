// Renders every view props-preview can find in a views.json, with and without the props, side by side.
// Usage: node render.js <views.json> <out folder>
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const [viewsPath, outDir] = process.argv.slice(2);
const here = __dirname, root = path.resolve(here, '../../..');
const files = {
  '/preview.html': path.join(here, 'preview.html'),
  '/views.json': path.resolve(viewsPath),
  '/hoardings.png': path.join(root, 'unity/Assets/Raceday/Resources/Furniture/hoardings.png'),
};
const types = { '.html': 'text/html', '.json': 'application/json', '.png': 'image/png' };
const server = http.createServer((req, res) => {
  const file = files[req.url.split('?')[0]];
  if (!file) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'Content-Type': types[path.extname(file)] }); fs.createReadStream(file).pipe(res);
}).listen(0, '127.0.0.1', async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
  const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
  page.on('pageerror', e => console.log('page error:', e.message));
  await page.goto(`http://127.0.0.1:${server.address().port}/preview.html`);
  await page.waitForFunction(() => window.ready, null, { timeout: 120000 });
  const names = await page.evaluate(() => Object.keys(data));
  for (const name of names)
    for (const props of [false, true]) {
      const url = await page.evaluate(([n, p]) => window.shoot(n, p, 1600, 900), [name, props]);
      fs.writeFileSync(path.join(outDir, `${name}-${props ? 'after' : 'before'}.jpg`), Buffer.from(url.split(',')[1], 'base64'));
      console.log(name, props ? 'after' : 'before');
    }
  await browser.close(); server.close();
});
