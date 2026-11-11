"""Shared primitive: an orbitable viewer of a car's game model wearing one or more liveries,
one toggle per livery and one for the doors. The projection here is the same one the game's
livery shader does: each paint face takes the view it faces most, blended where views meet.

Run from a team's folder, e.g. python3 ../common/viewer.py <model.obj> <out.html> <title> <name>=<baked dir> ... [--number 8]
"""
import base64
import gzip
import json
import sys
from pathlib import Path

PAGE = r"""<title>__TITLE__</title>
<meta name="claude:viewport-fit-sides" content="cover">
<style>
:root { --bg: #e7e8ea; --fg: #1b1e22; --chip: #ffffff; --line: #c9cdd2; --on: #1b1e22; --on-fg: #ffffff; }
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { --bg: #141619; --fg: #e9ebee; --chip: #22262b; --line: #3a3f46; --on: #e9ebee; --on-fg: #141619; color-scheme: dark; } }
:root[data-theme="dark"] { --bg: #141619; --fg: #e9ebee; --chip: #22262b; --line: #3a3f46; --on: #e9ebee; --on-fg: #141619; color-scheme: dark; }
* { box-sizing: border-box; }
html, body { margin: 0; height: 100%; }
body { background: var(--bg); color: var(--fg); display: flex; flex-direction: column; font: 500 14px/1.2 system-ui, sans-serif; }
#stage { flex: 1; min-height: 0; }
canvas { display: block; width: 100%; height: 100%; touch-action: none; }
.bar { display: flex; flex-wrap: wrap; justify-content: center; gap: 6px; padding-block: 10px 14px; padding-left: max(16px, env(safe-area-inset-left, 0px)); padding-right: max(16px, env(safe-area-inset-right, 0px)); }
.gap { width: 14px; }
button { font: inherit; color: var(--fg); background: var(--chip); border: 1px solid var(--line); border-radius: 999px; padding: 7px 13px; cursor: pointer; }
button[aria-pressed="true"] { background: var(--on); color: var(--on-fg); border-color: var(--on); }
button:focus-visible { outline: 2px solid var(--on); outline-offset: 2px; }
</style>
<div id="stage"></div>
<div class="bar" id="bar"></div>
<script src="https://cdn.jsdelivr.net/npm/three@0.147.0/build/three.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/three@0.147.0/examples/js/loaders/OBJLoader.js"></script>
<script src="https://cdn.jsdelivr.net/npm/three@0.147.0/examples/js/controls/OrbitControls.js"></script>
<script type="text/plain" id="model">__OBJ__</script>
<script>
const MATS = __MATS__, DOORS = __DOORS__, LIVERIES = __LIVERIES__, DIGITS = __DIGITS__;
const stage = document.getElementById('stage');
const renderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.outputEncoding = THREE.sRGBEncoding;
renderer.toneMapping = THREE.NoToneMapping;
stage.appendChild(renderer.domElement);
const scene = new THREE.Scene();
scene.background = new THREE.Color(getComputedStyle(document.body).backgroundColor).convertSRGBToLinear();
const camera = new THREE.PerspectiveCamera(30, 1, 0.05, 100);
camera.position.set(6.2, 2.4, 5.6);
const controls = new THREE.OrbitControls(camera, renderer.domElement);
controls.target.set(0, 0.55, 0.15); controls.enableDamping = true;
scene.add(new THREE.HemisphereLight(0xffffff, 0x50555c, 0.45));
const sun = new THREE.DirectionalLight(0xffffff, 0.8); sun.position.set(4, 8, 5); scene.add(sun);
const fill = new THREE.DirectionalLight(0xffffff, 0.3); fill.position.set(-6, 3, -4); scene.add(fill);
const pmrem = new THREE.PMREMGenerator(renderer);
const sky = new THREE.Scene(); sky.background = new THREE.Color(0xb9bfc7);
const lamp = new THREE.Mesh(new THREE.SphereGeometry(1, 16, 8), new THREE.MeshBasicMaterial({ color: 0xffffff }));
lamp.position.set(0, 6, 0); lamp.scale.set(4, 0.4, 4); sky.add(lamp);
scene.environment = pmrem.fromScene(sky, 0.04).texture;
const floor = new THREE.Mesh(new THREE.CircleGeometry(9, 64), new THREE.MeshStandardMaterial({ color: 0xcfd3d8, roughness: 1 }));
floor.rotation.x = -Math.PI / 2; scene.add(floor);

const mats = {};
for (const [k, m] of Object.entries(MATS)) {
  const s = Math.min(1, m.s * 2);
  mats[k] = new THREE.MeshStandardMaterial({ name: k, color: new THREE.Color(...m.c).convertSRGBToLinear(),
    metalness: s > 0.95 ? 1 : 0.1, roughness: 1 - 0.85 * s, transparent: m.a < 1, opacity: m.a,
    side: m.a < 1 ? THREE.DoubleSide : THREE.FrontSide, depthWrite: m.a >= 1 });
}

// The livery projection. Keep in step with the game's livery shader.
function liveryMaterial(lv) {
  const tex = new THREE.TextureLoader().load(lv.atlas);
  tex.encoding = THREE.sRGBEncoding; tex.anisotropy = 8;
  const m = new THREE.MeshStandardMaterial({ name: 'body', color: 0xffffff, metalness: 0.1, roughness: 0.28 });
  const P = Object.fromEntries(lv.panels.map(p => [p.view, p]));
  const v4 = a => new THREE.Vector4(...a);
  m.onBeforeCompile = sh => {
    sh.uniforms.uAtlas = { value: tex };
    sh.uniforms.uBase = { value: new THREE.Color(lv.base).convertSRGBToLinear() };
    // The race number, drawn as the game's shader draws it.
    const num = String(lv.number || ''), boxes = (lv.numbers || []).slice(0, 4);
    const dg = DIGITS ? new THREE.TextureLoader().load(DIGITS.png) : null;
    sh.uniforms.uDigits = { value: dg };
    sh.uniforms.uNumBox = { value: [0, 1, 2, 3].map(i => boxes[i] ? new THREE.Vector4(boxes[i].x / lv.size, boxes[i].y / lv.size, boxes[i].h / lv.size, boxes[i].rotate * Math.PI / 180) : new THREE.Vector4()) };
    sh.uniforms.uNumInk = { value: [0, 1, 2, 3].map(i => new THREE.Color(boxes[i] ? boxes[i].ink : '#fff').convertSRGBToLinear()) };
    sh.uniforms.uNumBoxes = { value: boxes.length };
    sh.uniforms.uNumWidth = { value: new THREE.Vector4(...[0, 1, 2, 3].map(i => boxes[i] ? boxes[i].w / lv.size : 0)) };
    sh.uniforms.uNumDigits = { value: new THREE.Vector4(+num[0] || 0, +num[1] || 0, +num[2] || 0, dg ? num.length : 0) };
    const D = DIGITS || { cell: 0, padX: 0, pad: 0, height: 1, advance: [1], w: 1, h: 1 };
    sh.uniforms.uDigitCell = { value: new THREE.Vector4(D.cell, D.padX, D.advance[0] * D.height * D.h / D.w, D.pad) };
    sh.uniforms.uDigitHeight = { value: D.height }; sh.uniforms.uDigitAdvance = { value: D.advance[0] };
    for (const n of ['left', 'right', 'top', 'front', 'rear']) {
      sh.uniforms[`uA_${n}`] = { value: v4(P[n].a) }; sh.uniforms[`uB_${n}`] = { value: v4(P[n].b) };
    }
    sh.vertexShader = 'varying vec3 vLp; varying vec3 vLn;\n' + sh.vertexShader.replace('#include <begin_vertex>', '#include <begin_vertex>\nvLp = position; vLn = normal;');
    sh.fragmentShader = `varying vec3 vLp; varying vec3 vLn; uniform sampler2D uAtlas; uniform vec3 uBase;
uniform vec4 uA_left, uB_left, uA_right, uB_right, uA_top, uB_top, uA_front, uB_front, uA_rear, uB_rear;
uniform sampler2D uDigits; uniform vec4 uNumBox[4]; uniform vec3 uNumInk[4]; uniform float uNumBoxes; uniform vec4 uNumDigits; uniform vec4 uNumWidth;
uniform vec4 uDigitCell; uniform float uDigitHeight, uDigitAdvance;
vec3 lvNumber(vec2 q, vec3 art) {
  float count = uNumDigits.w, width = count * uDigitAdvance;
  for (int i = 0; i < 4; i++) {
    vec4 box = uNumBox[i]; vec2 d = q - box.xy; float s = sin(box.w), c = cos(box.w);
    float h = min(box.z, uNumWidth[i] / max(width, 1e-5));
    vec2 l = vec2(c * d.x + s * d.y, -s * d.x + c * d.y) / max(h, 1e-5);
    float x = l.x + width / 2.0;
    float k = clamp(floor(x / uDigitAdvance), 0.0, 2.0);
    float digit = k < 0.5 ? uNumDigits.x : (k < 1.5 ? uNumDigits.y : uNumDigits.z);
    float gx = clamp((x - k * uDigitAdvance) / uDigitAdvance, 0.0, 1.0);
    vec2 g = vec2(digit * uDigitCell.x + uDigitCell.y + gx * uDigitCell.z, uDigitCell.w + clamp(l.y + 0.5, 0.0, 1.0) * uDigitHeight);
    float inside = step(abs(l.y), 0.5) * step(0.0, x) * step(x, width) * step(float(i) + 0.5, uNumBoxes) * step(0.5, count);
    vec2 smo = vec2(x / uDigitAdvance * uDigitCell.z, -l.y * uDigitHeight);
    art = mix(art, uNumInk[i], textureGrad(uDigits, vec2(g.x, 1.0 - g.y), dFdx(smo), dFdy(smo)).a * inside);
  }
  return art;
}
vec3 lvTap(vec4 a, vec4 b, vec3 p) { vec2 uv = vec2(dot(a.xyz, p) + a.w, dot(b.xyz, p) + b.w); return lvNumber(uv, texture2D(uAtlas, vec2(uv.x, 1.0 - uv.y)).rgb); }
vec3 livery(vec3 p, vec3 n) {
  vec3 k = pow(abs(n), vec3(6.0));
  float wSide = k.x, wTop = n.y > 0.0 ? k.y : 0.0, wBot = n.y < 0.0 ? k.y : 0.0, wFront = n.z > 0.0 ? k.z : 0.0, wRear = n.z < 0.0 ? k.z : 0.0;
  // Which side by where the face is; faces turned in towards the middle keep the base colour.
  vec3 side = n.x * p.x < 0.0 ? uBase : (p.x > 0.0 ? lvTap(uA_left, uB_left, p) : lvTap(uA_right, uB_right, p));
  vec3 c = side * wSide + lvTap(uA_top, uB_top, p) * wTop + uBase * wBot + lvTap(uA_front, uB_front, p) * wFront + lvTap(uA_rear, uB_rear, p) * wRear;
  return c / max(wSide + wTop + wBot + wFront + wRear, 1e-4);
}
` + sh.fragmentShader.replace('#include <map_fragment>', 'diffuseColor.rgb = livery(vLp, normalize(vLn));');
  };
  return m;
}
const liveries = LIVERIES.map(liveryMaterial);
const bodies = [];

const doorObs = {};
const bytes = Uint8Array.from(atob(document.getElementById('model').textContent), c => c.charCodeAt(0));
new Response(new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip'))).text().then(txt => {
  const obj = new THREE.OBJLoader().parse(txt);
  obj.traverse(o => {
    if (!o.isMesh) return;
    const names = Array.isArray(o.material) ? o.material.map(m => m.name) : [o.material.name];
    const mm = names.map(n => mats[n] || mats.body);
    o.material = mm.length === 1 ? mm[0] : mm;
    if (names.includes('body')) bodies.push([o, names]);
    for (const d of DOORS) if (o.name.startsWith(d.name)) { (doorObs[d.name] ||= []).push(o); o.matrixAutoUpdate = false; }
  });
  scene.add(obj);
  wear(0);
});
function wear(i) {
  for (const [o, names] of bodies) {
    const mm = names.map(n => n === 'body' ? liveries[i] : (mats[n] || mats.body));
    o.material = mm.length === 1 ? mm[0] : mm;
  }
  pick.forEach((b, j) => b.setAttribute('aria-pressed', i === j));
}
const bar = document.getElementById('bar');
const button = (label, fn) => { const b = document.createElement('button'); b.textContent = label; b.onclick = fn; bar.appendChild(b); return b; };
const pick = LIVERIES.map((lv, i) => button(lv.name, () => wear(i)));
bar.appendChild(Object.assign(document.createElement('span'), { className: 'gap' }));
let goal = 0, t = 0;
const doorBtn = button('Doors', () => { goal = 1 - goal; doorBtn.setAttribute('aria-pressed', goal === 1); });
doorBtn.setAttribute('aria-pressed', 'false');
// doors.json is in Unity axes (X mirrored), so mirror the hinge back and turn the other way.
function doors(dt) {
  t += Math.sign(goal - t) * Math.min(Math.abs(goal - t), dt / 0.9);
  const e = t * t * (3 - 2 * t);
  for (const d of DOORS) {
    const a = new THREE.Vector3(-d.hinge0[0], d.hinge0[1], d.hinge0[2]);
    const ax = new THREE.Vector3(-d.hinge1[0], d.hinge1[1], d.hinge1[2]).sub(a).normalize();
    const m = new THREE.Matrix4().makeTranslation(a.x, a.y, a.z)
      .multiply(new THREE.Matrix4().makeRotationAxis(ax, -e * d.open * Math.PI / 180))
      .multiply(new THREE.Matrix4().makeTranslation(-a.x, -a.y, -a.z));
    (doorObs[d.name] || []).forEach(o => o.matrix.copy(m));
  }
}
function resize() { const w = stage.clientWidth, h = stage.clientHeight; renderer.setSize(w, h, false); camera.aspect = w / h;
  camera.fov = Math.min(70, 30 * Math.max(1, 1.05 / camera.aspect)); camera.updateProjectionMatrix(); }
new ResizeObserver(resize).observe(stage); resize();
window.view = (p, tg, fov) => { camera.position.set(...p); if (tg) controls.target.set(...tg); controls.update(); };
window.setDoors = v => { goal = t = v; };
let last = performance.now();
(function loop(now = last) { doors(Math.min(0.1, (now - last) / 1000)); last = now; controls.update(); renderer.render(scene, camera); requestAnimationFrame(loop); })();
</script>
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


def _mtl(path):
    mats, cur = {}, None
    for ln in Path(path).read_text().splitlines():
        t = ln.split()
        if not t:
            continue
        if t[0] == "newmtl":
            cur = mats.setdefault(t[1], {"c": [1, 1, 1], "s": 0.3, "a": 1.0})
        elif t[0] == "Kd":
            cur["c"] = [float(x) for x in t[1:4]]
        elif t[0] == "Ks":
            cur["s"] = float(t[1])
        elif t[0] == "d":
            cur["a"] = float(t[1])
    return mats


DIGITS = Path(__file__).resolve().parents[3] / "unity" / "Assets" / "Raceday" / "Resources" / "Livery"


def write(obj_path, html_path, title, liveries, number=None):
    """liveries: [(name, baked dir holding atlas.png and livery.json)]. number: a race number
    drawn the game's way, for checking the game's atlas."""
    obj_path = Path(obj_path)
    obj = base64.b64encode(gzip.compress(_short(obj_path.read_text()).encode(), 9)).decode()
    doors_path = obj_path.with_suffix(".doors.json")
    doors = json.loads(doors_path.read_text())["doors"] if doors_path.exists() else []
    lvs = []
    for name, d in liveries:
        d = Path(d)
        meta = json.loads((d / "livery.json").read_text())
        atlas = "data:image/png;base64," + base64.b64encode((d / "atlas.png").read_bytes()).decode()
        lvs.append({"name": name, "atlas": atlas, "base": meta["base"], "panels": meta["panels"],
                    "size": meta["size"], "numbers": meta["numbers"], "number": number})
    digits = None
    if number:
        from PIL import Image
        digits = json.loads((DIGITS / "Digits.json").read_text())
        w, h = Image.open(DIGITS / "Digits.png").size
        digits.update(w=w, h=h, png="data:image/png;base64," + base64.b64encode((DIGITS / "Digits.png").read_bytes()).decode())
    html = (PAGE.replace("__TITLE__", title).replace("__OBJ__", obj).replace("__MATS__", json.dumps(_mtl(obj_path.with_suffix(".mtl"))))
            .replace("__DOORS__", json.dumps(doors)).replace("__LIVERIES__", json.dumps(lvs)).replace("__DIGITS__", json.dumps(digits)))
    Path(html_path).write_text(html)
    print("wrote", html_path, Path(html_path).stat().st_size // 1024, "KB")


if __name__ == "__main__":
    args = sys.argv[1:]
    number = None
    if "--number" in args:
        i = args.index("--number")
        number = int(args[i + 1])
        del args[i:i + 2]
    obj, out, title, *pairs = args
    write(obj, out, title, [tuple(p.split("=", 1)) for p in pairs], number)
