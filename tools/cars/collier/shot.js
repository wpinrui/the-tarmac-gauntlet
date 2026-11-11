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
  const views = { side: [0, 0.75, 11, 24], front: [11, 0.8, 0, 13], q_front: [6.5, 2.2, 4.6, 24], q_rear: [-6.5, 2.2, 4.6, 24], rear: [-11, 0.8, 0, 13], top: [0, 12, 0.01, 22],
    nose: [4.6, 1.3, 2.3, 16], tail: [-4.8, 1.4, 2.4, 16] };
  if (process.env.VIEWS) Object.assign(views, JSON.parse(process.env.VIEWS));
  if (process.env.ONLY) for (const k of Object.keys(views)) if (!process.env.ONLY.split(',').includes(k)) delete views[k];
  if (process.env.CLICK) for (const id of process.env.CLICK.split(',')) { await p.click('#' + id); await p.waitForTimeout(200); }
  for (const [k, v] of Object.entries(views)) {
    await p.evaluate(([v, t]) => { if (t) window.setTarget(...t.split(',').map(Number)); window.setView(...v); }, [v, process.env.TARGET || '']);
    await p.waitForTimeout(600);
    await p.screenshot({ path: path.join(out, k + '.png') });
  }
  await b.close();
})();
