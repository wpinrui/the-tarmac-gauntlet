"""Shared primitive: an orbitable viewer page for an exported OBJ, with a wireframe toggle,
one toggle per lamp group, one per part that can be hidden, one row per placement option group,
and a toggle that swings each door open on its hinge. The model is inlined."""
import base64
import gzip
import json
from pathlib import Path

PAGE = r"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="claude:viewport-fit-sides" content="cover">
<title>__TITLE__</title>
<style>
:root { --bg: #e7e8ea; --fg: #1b1e22; --chip: #ffffff; --line: #c9cdd2; --on: #1b1e22; --on-fg: #ffffff; }
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { --bg: #141619; --fg: #e9ebee; --chip: #22262b; --line: #3a3f46; --on: #e9ebee; --on-fg: #141619; color-scheme: dark; } }
:root[data-theme="dark"] { --bg: #141619; --fg: #e9ebee; --chip: #22262b; --line: #3a3f46; --on: #e9ebee; --on-fg: #141619; color-scheme: dark; }
* { box-sizing: border-box; }
html, body { margin: 0; height: 100%; }
body { background: var(--bg); color: var(--fg); display: flex; flex-direction: column; font: 500 14px/1.2 system-ui, sans-serif; }
#stage { flex: 1; min-height: 0; }
canvas { display: block; width: 100%; height: 100%; touch-action: none; }
.bar { display: flex; flex-wrap: wrap; justify-content: center; align-items: center; gap: 6px 14px; padding: 10px max(16px, env(safe-area-inset-right, 0px)) 10px max(16px, env(safe-area-inset-left, 0px)); }
.set { display: flex; align-items: center; gap: 4px; }
.set b { font-weight: 600; margin-right: 4px; }
button { font: inherit; color: var(--fg); background: var(--chip); border: 1px solid var(--line); border-radius: 999px; padding: 7px 13px; min-width: 38px; cursor: pointer; }
button[aria-pressed="true"] { background: var(--on); color: var(--on-fg); border-color: var(--on); }
button:focus-visible { outline: 2px solid var(--on); outline-offset: 2px; }
</style>
</head>
<body>
<div id="stage"></div>
<div class="bar" id="toggles"></div>
<div class="bar" id="options"></div>
<script src="https://cdn.jsdelivr.net/npm/three@0.147.0/build/three.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/three@0.147.0/examples/js/loaders/OBJLoader.js"></script>
<script src="https://cdn.jsdelivr.net/npm/three@0.147.0/examples/js/controls/OrbitControls.js"></script>
<script type="text/plain" id="model">__OBJ__</script>
<script>
const MATS = __MATS__, PICKS = __PICKS__, LAMPS = __LAMPS__, GROUPS = __GROUPS__, SHOW = __SHOW__, DOORS = __DOORS__;
const parts = {};
const stage = document.getElementById('stage');
const renderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.outputEncoding = THREE.sRGBEncoding;
renderer.toneMapping = THREE.ACESFilmicToneMapping; renderer.toneMappingExposure = 0.9;
stage.appendChild(renderer.domElement);
const scene = new THREE.Scene();
scene.background = new THREE.Color(getComputedStyle(document.body).backgroundColor).convertSRGBToLinear();
const camera = new THREE.PerspectiveCamera(30, 1, 0.05, 100);
camera.position.set(6.4, 2.3, 5.4);
const controls = new THREE.OrbitControls(camera, renderer.domElement);
controls.target.set(0, 0.6, 0); controls.enableDamping = true;
scene.add(new THREE.HemisphereLight(0xffffff, 0x50555c, 0.75));
const sun = new THREE.DirectionalLight(0xffffff, 1.1); sun.position.set(4, 8, 5); scene.add(sun);
const fill = new THREE.DirectionalLight(0xffffff, 0.45); fill.position.set(-6, 3, -4); scene.add(fill);
const pmrem = new THREE.PMREMGenerator(renderer);
const sky = new THREE.Scene(); sky.background = new THREE.Color(0xb9bfc7);
const lamp = new THREE.Mesh(new THREE.SphereGeometry(1, 16, 8), new THREE.MeshBasicMaterial({ color: 0xffffff }));
lamp.position.set(0, 6, 0); lamp.scale.set(4, 0.4, 4); sky.add(lamp);
scene.environment = pmrem.fromScene(sky, 0.04).texture;
const floor = new THREE.Mesh(new THREE.CircleGeometry(9, 64), new THREE.MeshStandardMaterial({ color: 0xcfd3d8, roughness: 1 }));
floor.rotation.x = -Math.PI / 2; scene.add(floor);
const mats = {};
for (const [k, m] of Object.entries(MATS)) {
  mats[k] = new THREE.MeshStandardMaterial({ name: k, color: new THREE.Color(...m.c).convertSRGBToLinear(),
    metalness: m.s > 0.95 ? 1 : 0.1, roughness: 1 - 0.85 * m.s, transparent: m.a < 1, opacity: m.a,
    side: m.a < 1 ? THREE.DoubleSide : THREE.FrontSide, depthWrite: m.a >= 1 });
  mats[k].userData.emit = m.e;
}
const bytes = Uint8Array.from(atob(document.getElementById('model').textContent), c => c.charCodeAt(0));
new Response(new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip'))).text().then(txt => {
  const obj = new THREE.OBJLoader().parse(txt);
  const opts = {};
  obj.traverse(o => {
    if (!o.isMesh) return;
    const names = Array.isArray(o.material) ? o.material.map(m => m.name) : [o.material.name];
    const mm = names.map(n => mats[n] || mats.body);
    o.material = mm.length === 1 ? mm[0] : mm;
    const g = /^opt_([a-z]+)_(\d+)_/.exec(o.name);
    if (g) ((opts[g[1]] ||= {})[g[2]] ||= []).push(o);
    for (const [label, names] of Object.entries(SHOW)) if (names.some(n => o.name.startsWith(n))) (parts[label] ||= []).push(o);
    for (const d of DOORS) if (o.name.replace(/^opt_[a-z]+_\d+_/, '').startsWith(d.parts)) { (d.obs ||= []).push(o); o.matrixAutoUpdate = false; }
  });
  for (const d of DOORS) {
    const keys = Object.keys(d.styles);
    d.pick = d.pick || keys[0];
    if (keys.length < 2) continue;
    const set = document.createElement('div'); set.className = 'set';
    const b = document.createElement('b'); b.textContent = d.label; set.appendChild(b);
    const pick = k => { d.pick = k; set.querySelectorAll('button').forEach(x => x.setAttribute('aria-pressed', x.dataset.k === k)); };
    keys.forEach(k => { const x = document.createElement('button'); x.textContent = k; x.dataset.k = k;
      x.addEventListener('click', () => pick(k)); set.appendChild(x); });
    document.getElementById('options').appendChild(set); pick(d.pick);
  }
  scene.add(obj);
  const bar = document.getElementById('options');
  for (const [g, label] of Object.entries(GROUPS)) {
    if (!opts[g]) continue;
    const set = document.createElement('div'); set.className = 'set';
    const b = document.createElement('b'); b.textContent = label; set.appendChild(b);
    const keys = Object.keys(opts[g]).sort();
    const pick = k => { keys.forEach(n => opts[g][n].forEach(o => o.visible = n === k));
      set.querySelectorAll('button').forEach(x => x.setAttribute('aria-pressed', x.dataset.k === k)); };
    keys.forEach(k => { const x = document.createElement('button'); x.textContent = k; x.dataset.k = k;
      x.addEventListener('click', () => pick(k)); set.appendChild(x); });
    bar.appendChild(set); pick(String(PICKS[g] || keys[0]));
  }
});
const toggles = document.getElementById('toggles');
const toggle = (label, fn, on0 = false) => { const x = document.createElement('button'); x.textContent = label;
  x.setAttribute('aria-pressed', String(on0));
  x.addEventListener('click', () => { const on = x.getAttribute('aria-pressed') !== 'true'; x.setAttribute('aria-pressed', on); fn(on); });
  toggles.appendChild(x); };
toggle('Wireframe', on => Object.values(mats).forEach(m => m.wireframe = on));
for (const [label, list] of Object.entries(LAMPS)) toggle(label, on => list.forEach(n => { const m = mats[n]; if (!m) return;
  m.emissive = on ? new THREE.Color(...m.userData.emit) : new THREE.Color(0, 0, 0); m.emissiveIntensity = on ? 2 : 0; m.needsUpdate = true; }));
for (const label of Object.keys(SHOW)) toggle(label, on => (parts[label] || []).forEach(o => o.visible = on), true);
for (const d of DOORS) { d.t = 0; d.goal = 0; toggle(d.label, on => d.goal = on ? 1 : 0); }
const toThree = p => new THREE.Vector3(p[0], p[2], -p[1]);
function doors(dt) {
  for (const d of DOORS) {
    if (!d.obs) continue;
    d.t += Math.sign(d.goal - d.t) * Math.min(Math.abs(d.goal - d.t), dt / 1.2);
    const s = d.styles[d.pick], a = toThree(s.axis[0]), ax = toThree(s.axis[1]).sub(a).normalize();
    const e = d.t * d.t * (3 - 2 * d.t);
    const m = new THREE.Matrix4().makeTranslation(a.x, a.y, a.z)
      .multiply(new THREE.Matrix4().makeRotationAxis(ax, e * s.open * Math.PI / 180))
      .multiply(new THREE.Matrix4().makeTranslation(-a.x, -a.y, -a.z));
    d.obs.forEach(o => o.matrix.copy(m));
  }
}
let fov0 = 30;
function resize() { const w = stage.clientWidth, h = stage.clientHeight; renderer.setSize(w, h, false); camera.aspect = w / h;
  camera.fov = Math.min(70, fov0 * Math.max(1, 1.05 / camera.aspect)); camera.updateProjectionMatrix(); }
new ResizeObserver(resize).observe(stage); resize();
window.view = (p, t, fov) => { camera.position.set(...p); if (t) controls.target.set(...t); if (fov) { fov0 = fov; resize(); } controls.update(); };
let last = performance.now();
(function loop(now = last) { doors(Math.min(0.1, (now - last) / 1000)); last = now; controls.update(); renderer.render(scene, camera); requestAnimationFrame(loop); })();
</script>
</body>
</html>
"""


def _short(txt):
    """Fewer digits for the page: 0.1 mm positions, 3-place normals."""
    out = []
    for ln in txt.splitlines():
        if ln.startswith("v "):
            ln = "v " + " ".join(f"{float(t):.4f}" for t in ln.split()[1:])
        elif ln.startswith("vn "):
            ln = "vn " + " ".join(f"{float(t):.3f}" for t in ln.split()[1:])
        out.append(ln)
    return "\n".join(out)


def write(obj_path, html_path, title, materials, picks, lamps, groups, show=None, doors=None):
    """materials {name: (rgb, spec, alpha, emit)}; lamps {label: [material names]};
    groups {option group: label}; show {label: [object name prefixes]}, parts with a show and
    hide toggle; doors [{"label", "parts": object name prefix, "styles": {label: {"axis": [p0,
    p1], "open": degrees}}, "pick"}], hinges in build space, turning by the right hand rule."""
    obj = base64.b64encode(gzip.compress(_short(Path(obj_path).read_text()).encode(), 9)).decode()
    mats = {k: {"c": v[0], "s": v[1], "a": v[2], "e": v[3] or (0, 0, 0)} for k, v in materials.items()}
    html = (PAGE.replace("__TITLE__", title).replace("__OBJ__", obj).replace("__MATS__", json.dumps(mats))
            .replace("__PICKS__", json.dumps(picks)).replace("__LAMPS__", json.dumps(lamps))
            .replace("__GROUPS__", json.dumps(groups)).replace("__SHOW__", json.dumps(show or {}))
            .replace("__DOORS__", json.dumps(doors or [], default=float)))
    Path(html_path).write_text(html)
    print("wrote", html_path, Path(html_path).stat().st_size // 1024, "KB")
