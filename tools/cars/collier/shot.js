// Renders reference-matching views of viewer.html into PNGs: node shot.js viewer.html outdir
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const [, , page_, out] = process.argv;
  const b = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
  const p = await b.newPage({ viewport: { width: 1400, height: 820 } });
  p.on('console', m => console.log('console', m.text()));
  p.on('pageerror', e => console.log('err', e.message));
  // Serve the CDN scripts from a local three install when offline: THREE_DIR=.../node_modules/three
  if (process.env.THREE_DIR) await p.route('https://cdn.jsdelivr.net/npm/three@0.147.0/**', r =>
    r.fulfill({ path: path.join(process.env.THREE_DIR, r.request().url().split('three@0.147.0/')[1]), contentType: 'text/javascript' }));
  await p.goto('file://' + path.resolve(page_));
  await p.waitForTimeout(4000);
  const views = { side: [0, 0.55, 12, 25], front: [12, 0.6, 0, 22], q_front: [7, 2.6, 5, 30], q_rear: [-7, 2.6, 5, 30], rear: [-12, 0.9, 0, 22], top: [0, 12, 0.01, 25],
    nose: [4.2, 1.6, 1.8, 18], tail: [-4.5, 1.4, 2.2, 18], fin: [-1.0, 1.2, 4.5, 20] };
  if (process.env.CLICK) for (const id of process.env.CLICK.split(',')) { await p.click('#' + id); await p.waitForTimeout(200); }
  for (const [k, v] of Object.entries(views)) {
    await p.evaluate(v => window.setView(...v), v);
    await p.waitForTimeout(600);
    await p.screenshot({ path: path.join(out, k + '.png') });
  }
  await b.close();
})();
