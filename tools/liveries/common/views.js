// Draws a car's five livery views at true scale from its game OBJ: where the paint goes, the
// other parts, and every edge between parts (shut lines, glass, lamps, arches) as guides.
// Writes <out>/<view>.png and <out>/views.json (each view's extent in metres along u and v).
//
// Run: node views.js <model.obj> <out dir> [px per metre]
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const [objPath, outDir, ppmArg] = process.argv.slice(2);
const PPM = Number(ppmArg || 400);
const FRAMES = JSON.parse(fs.readFileSync(path.join(__dirname, 'frames.json')));
delete FRAMES._;
const MARGIN = 0.03;

const PAGE = `<!doctype html><html><body>
<script src="https://cdn.jsdelivr.net/npm/three@0.147.0/build/three.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/three@0.147.0/examples/js/loaders/OBJLoader.js"></script>
</body></html>`;

function render(txt, frames, ppm, margin) {
  const obj = new THREE.OBJLoader().parse(txt);
  const box = new THREE.Box3().setFromObject(obj);
  const corners = [];
  for (const x of [box.min.x, box.max.x]) for (const y of [box.min.y, box.max.y]) for (const z of [box.min.z, box.max.z]) corners.push(new THREE.Vector3(x, y, z));
  // One flat colour per part and material, so every boundary shows; paint is flagged in blue.
  let n = 1;
  obj.traverse(o => {
    if (!o.isMesh) return;
    const list = Array.isArray(o.material) ? o.material : [o.material];
    const flat = list.map(m => { const id = n++; return new THREE.MeshBasicMaterial({ color: new THREE.Color(((id >> 8) & 255) / 255, (id & 255) / 255, m.name === 'body' ? 1 : 0), side: THREE.DoubleSide }); });
    o.material = flat.length === 1 ? flat[0] : flat;
  });
  const scene = new THREE.Scene(); scene.add(obj);
  const renderer = new THREE.WebGLRenderer({ antialias: false, preserveDrawingBuffer: true });
  renderer.outputEncoding = THREE.LinearEncoding;
  const out = {};
  for (const [name, f] of Object.entries(frames)) {
    const U = new THREE.Vector3(...f.u), V = new THREE.Vector3(...f.v), L = new THREE.Vector3(...f.look);
    const us = corners.map(c => c.dot(U)), vs = corners.map(c => c.dot(V));
    const u0 = Math.min(...us) - margin, u1 = Math.max(...us) + margin, v0 = Math.min(...vs) - margin, v1 = Math.max(...vs) + margin;
    const w = Math.round((u1 - u0) * ppm), h = Math.round((v1 - v0) * ppm);
    const centre = U.clone().multiplyScalar((u0 + u1) / 2).add(V.clone().multiplyScalar((v0 + v1) / 2));
    const cam = new THREE.OrthographicCamera(-(u1 - u0) / 2, (u1 - u0) / 2, (v1 - v0) / 2, -(v1 - v0) / 2, 0.01, 40);
    cam.position.copy(centre).addScaledVector(L, -20);
    cam.up.copy(V).negate(); cam.lookAt(centre);
    const rt = new THREE.WebGLRenderTarget(w, h);
    renderer.setRenderTarget(rt); renderer.setClearColor(0x000000, 0); renderer.clear(); renderer.render(scene, cam);
    const px = new Uint8Array(w * h * 4); renderer.readRenderTargetPixels(rt, 0, 0, w, h, px);
    const cv = document.createElement('canvas'); cv.width = w; cv.height = h;
    const g = cv.getContext('2d'), img = g.createImageData(w, h);
    const at = (x, y) => { const i = ((h - 1 - y) * w + x) * 4; return px[i + 3] ? (px[i] << 16) | (px[i + 1] << 8) | px[i + 2] : -1; };
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
      const k = at(x, y), o = (y * w + x) * 4;
      const edge = (x + 1 < w && at(x + 1, y) !== k) || (y + 1 < h && at(x, y + 1) !== k);
      const c = edge ? [20, 22, 26, 255] : k < 0 ? [0, 0, 0, 0] : (k & 255) ? [236, 238, 241, 255] : [150, 155, 162, 255];
      img.data.set(c, o);
    }
    g.putImageData(img, 0, 0);
    out[name] = { u: [u0, u1], v: [v0, v1], px: [w, h], png: cv.toDataURL('image/png') };
    rt.dispose();
  }
  return out;
}

(async () => {
  const browser = await chromium.launch({ args: ['--use-gl=swiftshader', '--enable-webgl', '--ignore-gpu-blocklist'] });
  const page = await browser.newPage();
  await page.setContent(PAGE, { waitUntil: 'networkidle' });
  const res = await page.evaluate(([fn, txt, frames, ppm, margin]) => eval('(' + fn + ')')(txt, frames, ppm, margin),
    [render.toString(), fs.readFileSync(objPath, 'utf8'), FRAMES, PPM, MARGIN]);
  await browser.close();
  fs.mkdirSync(outDir, { recursive: true });
  const meta = { ppm: PPM, views: {} };
  for (const [name, v] of Object.entries(res)) {
    fs.writeFileSync(path.join(outDir, `${name}.png`), Buffer.from(v.png.split(',')[1], 'base64'));
    meta.views[name] = { ...FRAMES[name], u0: v.u[0], u1: v.u[1], v0: v.v[0], v1: v.v[1], px: v.px };
  }
  fs.writeFileSync(path.join(outDir, 'views.json'), JSON.stringify(meta, null, 1));
  console.log('wrote', Object.keys(res).length, 'views to', outDir);
})();
