"""Writes viewer.html for the Liberty Collier: an orbitable viewer with wireframe and lamp toggles, model inlined."""
import base64
import gzip
import json
import sys
from pathlib import Path

from export import MATERIALS, NAME

src = Path(sys.argv[1] if len(sys.argv) > 1 else Path(__file__).with_name("out"))
dst = Path(sys.argv[2]) if len(sys.argv) > 2 else Path(__file__).with_name("viewer.html")
obj = base64.b64encode(gzip.compress((src / f"{NAME}.obj").read_bytes(), 9)).decode()
mats = {k: {"c": v[0], "s": v[1], "a": v[2], "e": v[3]} for k, v in MATERIALS.items()}

html = r"""<title>Liberty Collier</title>
<meta name="claude:viewport-fit-sides" content="cover">
<style>
:root { --bg: #e4e6e9; --fg: #1d2127; --chip: #ffffff; --chip-on: #1c2a3a; --chip-on-fg: #ffffff; --line: #c3c8cf; }
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { --bg: #16191d; --fg: #e8ebef; --chip: #23282e; --chip-on: #e3a33a; --chip-on-fg: #16191d; --line: #3a4048; color-scheme: dark } }
:root[data-theme="dark"] { --bg: #16191d; --fg: #e8ebef; --chip: #23282e; --chip-on: #e3a33a; --chip-on-fg: #16191d; --line: #3a4048; color-scheme: dark }
html, body { height: 100%; }
body { background: var(--bg); color: var(--fg); display: flex; flex-direction: column; font: 500 14px/1.2 system-ui, sans-serif; }
#view { flex: 1; min-height: 0; position: relative; }
canvas { display: block; width: 100%; height: 100%; touch-action: none; }
nav { display: flex; align-items: center; flex-wrap: wrap; gap: 8px; padding-block: 12px; padding-left: max(16px, env(safe-area-inset-left, 0px)); padding-right: max(16px, env(safe-area-inset-right, 0px)); justify-content: center; }
button { font: inherit; color: var(--fg); background: var(--chip); border: 1px solid var(--line); border-radius: 999px; padding: 8px 14px; cursor: pointer; }
button[aria-pressed="true"] { background: var(--chip-on); color: var(--chip-on-fg); border-color: var(--chip-on); }
.grp { display: flex; gap: 4px; align-items: center; }
.grp span { margin-right: 4px; }
.grp button { min-width: 40px; }
button:focus-visible { outline: 2px solid var(--chip-on); outline-offset: 2px; }
</style>
<div id="view"></div>
<nav>
<button id="t-wire" aria-pressed="false">Wireframe</button>
<button id="t-head" aria-pressed="false" data-lamp="lamp_head">Headlights</button>
<button id="t-tail" aria-pressed="false" data-lamp="lamp_tail">Tail</button>
<button id="t-fog" aria-pressed="false" data-lamp="lamp_fog">Fog</button>
</nav>
<nav id="opts"></nav>
<script src="https://cdn.jsdelivr.net/npm/three@0.147.0/build/three.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/three@0.147.0/examples/js/loaders/OBJLoader.js"></script>
<script src="https://cdn.jsdelivr.net/npm/three@0.147.0/examples/js/controls/OrbitControls.js"></script>
<script type="text/plain" id="obj">__OBJ__</script>
<script>
const MATS = __MATS__;
const PICKS = __PICKS__;
const view = document.getElementById('view');
const renderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.outputEncoding = THREE.sRGBEncoding;
renderer.toneMapping = THREE.ACESFilmicToneMapping; renderer.toneMappingExposure = 0.85;
view.appendChild(renderer.domElement);
const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(30, 1, 0.05, 100);
camera.position.set(6.5, 2.4, 5.5);
const controls = new THREE.OrbitControls(camera, renderer.domElement);
controls.target.set(0, 0.45, 0);
controls.enableDamping = true;
scene.add(new THREE.HemisphereLight(0xffffff, 0x6d727a, 0.55));
const sun = new THREE.DirectionalLight(0xffffff, 1.9); sun.position.set(4, 8, 5); scene.add(sun);
const fill = new THREE.DirectionalLight(0xffffff, 0.6); fill.position.set(-5, 3, -4); scene.add(fill);
const ground = new THREE.Mesh(new THREE.CircleGeometry(6, 64), new THREE.MeshStandardMaterial({ color: 0x9aa0a8, roughness: 1 }));
ground.rotation.x = -Math.PI / 2; scene.add(ground);
function bg() { const c = getComputedStyle(document.documentElement).getPropertyValue('--bg').trim(); scene.background = new THREE.Color(c); }
bg(); matchMedia('(prefers-color-scheme: dark)').addEventListener('change', bg);
new MutationObserver(bg).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });

const mats = {};
for (const [k, m] of Object.entries(MATS)) {
  const mat = new THREE.MeshStandardMaterial({ color: new THREE.Color(...m.c).convertSRGBToLinear(), roughness: 1 - m.s * 0.8, metalness: k === 'hub' || k === 'mirror_glass' || k === 'chrome' ? 0.8 : 0.05, transparent: m.a < 1, opacity: m.a, side: m.a < 1 ? THREE.DoubleSide : THREE.FrontSide });
  mat.userData.emit = m.e; mats[k] = mat;
}
(async () => {
const bin = Uint8Array.from(atob(document.getElementById('obj').textContent), c => c.charCodeAt(0));
const text = await new Response(new Blob([bin]).stream().pipeThrough(new DecompressionStream('gzip'))).text();
const obj = new THREE.OBJLoader().parse(text);
const GROUPS = { frontbadge: 'Front badge', rearbadge: 'Rear badge', name: 'Name' };
const opts = {};
obj.traverse(o => {
  const m = /^opt_([a-z]+)_(\d+)_/.exec(o.name);
  if (o.isMesh && m) ((opts[m[1]] ||= {})[m[2]] ||= []).push(o);
});
const bar = document.getElementById('opts');
for (const [g, label] of Object.entries(GROUPS)) {
  if (!opts[g]) continue;
  const wrap = document.createElement('div'); wrap.className = 'grp';
  const l = document.createElement('span'); l.textContent = label; wrap.appendChild(l);
  const keys = Object.keys(opts[g]).sort();
  const pick = k => { keys.forEach(n => opts[g][n].forEach(o => o.visible = n === k));
    wrap.querySelectorAll('button').forEach(b => b.setAttribute('aria-pressed', b.dataset.k === k)); };
  keys.forEach(k => { const b = document.createElement('button'); b.textContent = k; b.dataset.k = k; b.id = `o-${g}-${k}`;
    b.addEventListener('click', () => pick(k)); wrap.appendChild(b); });
  bar.appendChild(wrap); pick(String(PICKS[g] || keys[0]));
}
obj.traverse(o => {
  if (!o.isMesh) return;
  const groups = o.geometry.groups; const names = Array.isArray(o.material) ? o.material.map(m => m.name) : [o.material.name];
  o.material = names.map(n => mats[n] || mats.body);
  if (!Array.isArray(o.material) || o.material.length === 1) o.material = o.material[0];
});
scene.add(obj);
})();

function setLamp(name, on) {
  const m = mats[name]; const e = m.userData.emit;
  m.emissive = on ? new THREE.Color(...e) : new THREE.Color(0, 0, 0); m.emissiveIntensity = on ? (name === 'lamp_head' ? 2.2 : 1.6) : 0; m.toneMapped = !on; m.needsUpdate = true;
}
document.querySelectorAll('nav:not(#opts) button').forEach(b => b.addEventListener('click', () => {
  const on = b.getAttribute('aria-pressed') !== 'true'; b.setAttribute('aria-pressed', on);
  if (b.dataset.lamp) setLamp(b.dataset.lamp, on);
  else Object.values(mats).forEach(m => m.wireframe = on);
}));

function resize() { const w = view.clientWidth, h = view.clientHeight; renderer.setSize(w, h, false); camera.aspect = w / h; camera.updateProjectionMatrix(); }
new ResizeObserver(resize).observe(view); resize();
window.setView = (x, y, z, fov) => { camera.position.set(x, y, z); if (fov) { camera.fov = fov; camera.updateProjectionMatrix(); } controls.update(); };
(function loop() { controls.update(); renderer.render(scene, camera); requestAnimationFrame(loop); })();
</script>
"""
dst.write_text(html.replace("__OBJ__", obj).replace("__MATS__", json.dumps(mats)).replace("__PICKS__", "{}"))
print("wrote", dst, dst.stat().st_size // 1024, "KB")
