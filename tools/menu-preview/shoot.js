// Screenshots every page page.py wrote, at the 1600 x 900 reference size.
// Usage: node shoot.js <pages folder> <png folder> [scale]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

(async () => {
  const [pages, pngs, scale] = process.argv.slice(2);
  fs.mkdirSync(pngs, { recursive: true });
  // Served over http: Chrome won't use file:// images as CSS masks.
  const http = require('http');
  const server = http.createServer((req, res) => {
    const file = path.join(path.resolve(pages), decodeURIComponent(req.url.split('?')[0]));
    fs.readFile(file, (err, data) => {
      if (err) { res.writeHead(404); res.end(); return; }
      const types = { '.html': 'text/html', '.png': 'image/png', '.jpg': 'image/jpeg', '.ttf': 'font/ttf' };
      res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' });
      res.end(data);
    });
  }).listen(0);
  const port = server.address().port;
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1600, height: 900 }, deviceScaleFactor: Number(scale || 1) });
  for (const f of fs.readdirSync(pages).filter(f => f.endsWith('.html'))) {
    await page.goto(`http://127.0.0.1:${port}/${f}`);
    await page.evaluate(() => document.fonts.ready);
    await page.screenshot({ path: path.join(pngs, f.replace('.html', '.png')) });
  }
  await browser.close();
  server.close();
})();
