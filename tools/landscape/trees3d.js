// Builds the near 3D trees (trees3d.html) from the Poly Haven glTF models the impostors were baked from, and writes
// them for the game: Resources/Landscape/Trees3D/trees3d.bytes, the bark textures bark_NN.jpg and a leaf atlas per tree.
// Usage: node trees3d.js <folder of Poly Haven <id>/<id>_1k.gltf> [Resources/Landscape folder] [name ...]
//
// trees3d.bytes, little-endian: "TG3D", int version 2, int kinds, int lods, int bark layers; then per kind, per LOD:
// int vertices, int bark indices, int leaf indices, float3 colour (times the tint, to match the impostor), float3 positions, float3 normals, float2 uvs,
// float3 extra (bark layer, shade, sway), int bark indices, int leaf indices. Kinds in trees.json's order.
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('playwright');

const here = __dirname;
const [srcDir, outArg, ...only] = process.argv.slice(2);
const out = path.join(outArg || path.join(here, '../../unity/Assets/Raceday/Resources/Landscape'), 'Trees3D');
const trees = JSON.parse(fs.readFileSync(path.join(here, 'trees.json'), 'utf8'));
const resDir = outArg || path.join(here, '../../unity/Assets/Raceday/Resources/Landscape');
const plan = fs.readFileSync(path.join(here, '../../unity/Assets/Raceday/Scripts/LandscapePlan.cs'), 'utf8');
const RADIUS = plan.match(/ModelRadius = \{([^}]*)\}/)[1].split(',').map(parseFloat);
const types = { '.html': 'text/html', '.js': 'text/javascript', '.wasm': 'application/wasm', '.gltf': 'model/gltf+json', '.bin': 'application/octet-stream', '.jpg': 'image/jpeg', '.png': 'image/png' };

const server = http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  const file = url.startsWith('/src/') ? path.join(srcDir, url.slice(5)) : url.startsWith('/res/') ? path.join(resDir, url.slice(5)) : path.join(here, url);
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' });
    res.end(data);
  });
}).listen(0, '127.0.0.1', async () => {
  const port = server.address().port;
  fs.mkdirSync(out, { recursive: true });
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--js-flags=--max-old-space-size=8192'] });
  const page = await browser.newPage();
  page.on('pageerror', e => console.log('page error:', e.message));
  page.on('console', m => { if (m.type() === 'error') console.log('console:', m.text()); });
  await page.goto(`http://127.0.0.1:${port}/trees3d.html`);
  await page.waitForFunction(() => window.ready, null, { timeout: 60000 });
  const layers = [], kinds = [];
  for (const [name, t] of Object.entries(trees)) {
    if (only.length && !only.includes(name)) continue;
    const near = t.near || {};
    const spec = { url: `/src/${t.model}/${t.model}_1k.gltf`, node: t.node ?? -1, leavesOnly: !!t.leavesOnly, thicken: t.thicken || 1,
      cube: near.cube || 0.5, keep: near.keep ?? 0.25, card: near.card || 1.6, bark0: near.bark0 || 3000, bark1: near.bark1 || 700, tile: 256, tiers: !!near.tiers,
      impostor: `/res/${name}_colour.png`, radius: RADIUS[Object.keys(trees).indexOf(name)] };
    // The cards are grown or shrunk until the lighter level, the one the impostor takes over from, covers what it does.
    let r;
    for (let pass = 0; pass < 5; pass++) {
      r = await page.evaluate(s => window.build(s), spec);
      const cover = r.against[1].coverage;
      console.log(`  ${name}: cards ${spec.card.toFixed(2)} cover ${cover.toFixed(2)} of the impostor, colour ${r.against[1].colour.map(v => v.toFixed(2))}`);
      if (Math.abs(cover - 1) < 0.06) break;
      spec.card *= Math.pow(1 / cover, 1.1);
    }
    // Bark layers shared between trees of the same model.
    const local = r.barkLayers.map(uri => {
      const key = `${t.model}/${uri}`;
      let i = layers.findIndex(l => l.key === key);
      if (i < 0) { i = layers.length; layers.push({ key, image: r.barkImages[uri] }); }
      return i;
    });
    for (const lod of r.lods)
      for (let i = 0; i < lod.extra.length; i += 3) lod.extra[i] = lod.extra[i] >= 0 ? local[lod.extra[i]] : 0;
    fs.writeFileSync(path.join(out, `leaves_${name}.png`), Buffer.from(r.atlas.split(',')[1], 'base64'));
    // Each level's colour, so it averages to the impostor's over the same views.
    r.lods.forEach((l, i) => { l.colour = r.against[i].colour.map(v => Math.min(Math.max(v, 0.35), 2.5)); });
    kinds.push(r.lods);
    console.log(name, JSON.stringify(r.meta));
  }
  await browser.close();
  server.close();

  layers.forEach((l, i) => fs.writeFileSync(path.join(out, `bark_${String(i).padStart(2, '0')}.jpg`), Buffer.from(l.image.split(',')[1], 'base64')));
  const parts = [];
  const head = Buffer.alloc(20);
  head.write('TG3D', 0, 'ascii');
  head.writeInt32LE(2, 4); head.writeInt32LE(kinds.length, 8); head.writeInt32LE(2, 12); head.writeInt32LE(layers.length, 16);
  parts.push(head);
  for (const lods of kinds)
    for (const l of lods) {
      const h = Buffer.alloc(24);
      h.writeInt32LE(l.positions.length / 3, 0); h.writeInt32LE(l.bark.length, 4); h.writeInt32LE(l.leaves.length, 8);
      l.colour.forEach((v, k) => h.writeFloatLE(v, 12 + k * 4));
      parts.push(h, Buffer.from(Float32Array.from(l.positions).buffer), Buffer.from(Float32Array.from(l.normals).buffer),
        Buffer.from(Float32Array.from(l.uvs).buffer), Buffer.from(Float32Array.from(l.extra).buffer),
        Buffer.from(Int32Array.from(l.bark).buffer), Buffer.from(Int32Array.from(l.leaves).buffer));
    }
  fs.writeFileSync(path.join(out, 'trees3d.bytes'), Buffer.concat(parts));
  console.log(`${layers.length} bark layers, ${(fs.statSync(path.join(out, 'trees3d.bytes')).size / 1024).toFixed(0)} KB of meshes`);
});
