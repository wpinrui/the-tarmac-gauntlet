// The game's grass blades (Grass.shader, Grass.cs) and near 3D trees (NearTree.shader, NearTrees.cs) for the browser
// previews, ported as closely as three.js allows: the same rings, tiles, thinning and shrinking for the grass, the same
// levels of detail and dithered hand-over to the impostors for the trees. Unity's frame is mirrored into three.js by
// z -> -z, as preview.html does.
import * as THREE from 'three';

export const KINDS = ['oak', 'beech', 'birch', 'pine_a', 'pine_b', 'spruce_a', 'spruce_b', 'bush'];

/** NearTrees.Reaches, by setting. */
export const TREE_REACH = [null, { lodAt: 20, lodBand: 6, start: 45, end: 60 }, { lodAt: 40, lodBand: 10, start: 85, end: 105 }];
/** Grass.Settings, by setting. */
export const GRASS_RINGS = [null, { spacing: 0.08, edges: [8, 25] }, { spacing: 0.05, edges: [8, 20, 50] }];
const ACROSS = 16, THINNING = 0.3, SHRINK_FROM = 0.55;

// Foliage.hlsl.
export const ditherGlsl = `
  float foliageNoise(vec2 p) { return fract(52.9829189 * fract(dot(floor(p), vec2(0.06711056, 0.00583715)))); }
  void foliageFade(float share) {
    float n = foliageNoise(gl_FragCoord.xy);
    if ((share >= 0.0 ? share - n : n - (1.0 + share)) < 0.0) discard;
  }`;
export const swayGlsl = `
  uniform vec4 wind;
  vec2 foliageSway(vec3 p, float sway) {
    float t = wind.w, strength = wind.z;
    float along = dot(p.xz, wind.xy);
    float gust = sin(along * 0.07 - t * 1.9) * 0.5 + 0.5; gust *= gust;
    float swing = sin(t * 1.7 + p.x * 0.21 + p.z * 0.17) * 0.35;
    return wind.xy * (strength * sway * (0.35 + swing + gust * 0.9 * strength));
  }`;
export const windValue = () => new THREE.Vector4(1 / Math.hypot(1, 0.35), 0.35 / Math.hypot(1, 0.35), 1, 12.3);

export function parseTrees3d(buf) {
  const dv = new DataView(buf);
  let o = 8;
  const i32 = () => { const v = dv.getInt32(o, true); o += 4; return v; };
  const f32 = n => { const a = new Float32Array(buf.slice(o, o + n * 4)); o += n * 4; return a; };
  const ints = n => { const a = new Int32Array(buf.slice(o, o + n * 4)); o += n * 4; return a; };
  const kinds = i32(), lods = i32(), layers = i32();
  const out = [];
  for (let k = 0; k < kinds; k++) {
    const row = [];
    for (let l = 0; l < lods; l++) {
      const nv = i32(), nb = i32(), nl = i32(), colour = Array.from(f32(3));
      row.push({ colour, P: f32(nv * 3), N: f32(nv * 3), U: f32(nv * 2), X: f32(nv * 3), B: ints(nb), L: ints(nl) });
    }
    out.push(row);
  }
  return { kinds: out, layers };
}

/** Images into a texture array, rows bottom first as Unity reads them, left in sRGB: the shaders decode it. */
export async function textureArray(urls, size) {
  const px = new Uint8Array(size * size * 4 * urls.length);
  const c = document.createElement('canvas'); c.width = c.height = size;
  const ctx = c.getContext('2d', { willReadFrequently: true });
  for (let i = 0; i < urls.length; i++) {
    const img = await new THREE.ImageLoader().loadAsync(urls[i]);
    ctx.setTransform(1, 0, 0, -1, 0, size);
    ctx.clearRect(0, 0, size, size);
    ctx.drawImage(img, 0, 0, size, size);
    px.set(ctx.getImageData(0, 0, size, size).data, i * size * size * 4);
  }
  const arr = new THREE.DataArrayTexture(px, size, size, urls.length);
  arr.wrapS = arr.wrapT = THREE.RepeatWrapping;
  arr.minFilter = THREE.LinearMipmapLinearFilter; arr.magFilter = THREE.LinearFilter;
  arr.generateMipmaps = true;
  arr.needsUpdate = true;
  return arr;
}

const treeVert = `
  precision highp float;
  in vec3 position; in vec3 normal; in vec2 uv; in vec3 extra;
  in vec4 a; in vec4 b; in vec3 tint;
  uniform mat4 viewMatrix, projectionMatrix; uniform float leaves;
  ${swayGlsl}
  out vec2 vUv; out vec3 vN; out vec3 vTint; out float vShade; out float vShare; out float vLayer; out float vFog; out vec3 vView;
  uniform vec3 cameraPosition;
  vec3 turn(vec3 v, float t) { float c = cos(t), s = sin(t); return vec3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z); }
  void main() {
    float scale = a.w, yaw = b.x, squash = b.y;
    vec3 local = position * vec3(scale, scale * squash, scale);
    vec3 w = a.xyz + turn(local, yaw);   // Unity's frame
    if (leaves > 0.5) w += sin(wind.w * 5.3 + dot(w, vec3(1.7, 2.3, 1.1))) * 0.03 * wind.z * extra.z;
    w.xz += foliageSway(a.xyz, extra.z) * (0.15 + 0.012 * local.y);
    vec3 n = turn(normal / vec3(1.0, max(squash, 0.2), 1.0), yaw);
    vN = normalize(vec3(n.x, n.y, -n.z));
    vUv = uv; vTint = tint; vShade = extra.y; vShare = b.z; vLayer = extra.x;
    vView = cameraPosition - vec3(w.x, w.y, -w.z);
    vec4 view = viewMatrix * vec4(w.x, w.y, -w.z, 1.0);
    vFog = -view.z;
    gl_Position = projectionMatrix * view;
  }`;
const treeFrag = `
  precision highp float; precision highp sampler2DArray;
  uniform sampler2DArray layers; uniform float leaves, kind; uniform vec3 colour;
  uniform vec3 sun, sunColour, sky, ground, fogColour; uniform vec2 fogRange;
  in vec2 vUv; in vec3 vN; in vec3 vTint; in float vShade; in float vShare; in float vLayer; in float vFog; in vec3 vView;
  out vec4 o;
  ${ditherGlsl}
  void main() {
    foliageFade(vShare);
    vec4 c = texture(layers, vec3(vUv, leaves > 0.5 ? kind : vLayer));
    if (leaves > 0.5 && c.a < 0.5) discard;
    vec3 n = normalize(vN);
    if (leaves < 0.5 && !gl_FrontFacing) n = -n;
    if (leaves > 0.5) n = normalize(0.7 * n + 0.3 * normalize(vView));
    vec3 albedo = pow(c.rgb, vec3(2.2)) * vTint * vShade * colour;
    vec3 amb = mix(ground, sky, n.y * 0.5 + 0.5);
    vec3 lit = albedo * (amb + sunColour * (max(dot(n, sun), 0.0) * 0.8 + 0.2));
    float fog = clamp((vFog - fogRange.x) / (fogRange.y - fogRange.x), 0.0, 1.0);
    o = vec4(pow(mix(lit, fogColour, fog), vec3(1.0 / 2.2)), 1.0);
  }`;

/**
 * The near trees round a camera (Unity frame), as NearTrees.Draw picks them: a Group of instanced meshes, and the
 * impostors' fade (nearAt in three's frame, start, 1 / width) to set on their materials.
 * trees: trees.bin's floats, 10 a tree; above: each kind's middle over its foot at scale 1.
 */
export function nearTrees(data, bark, leaves, trees, above, eye, setting, lights) {
  const r = TREE_REACH[setting];
  const group = new THREE.Group();
  if (!r) return { group, fade: new THREE.Vector3(0, 0, 0) };
  const lists = KINDS.map(() => [[], []]);
  const lod0 = r.lodAt - r.lodBand / 2;
  let count = 0;
  for (let i = 0; i < trees.length; i += 10) {
    const k = trees[i + 6], scale = trees[i + 3], squash = trees[i + 4];
    const cx = trees[i], cy = trees[i + 1] + above[k] * scale * squash, cz = trees[i + 2];
    const d = Math.hypot(cx - eye[0], cy - eye[1], cz - eye[2]);
    if (d >= r.end) continue;
    const impostor = Math.min(Math.max((d - r.start) / (r.end - r.start), 0), 1);
    const lod = Math.min(Math.max((d - lod0) / r.lodBand, 0), 1);
    if (lod < 1) lists[k][0].push([i, 1 - lod]);
    if (lod > 0) lists[k][1].push([i, impostor > 0 ? 1 - impostor : -lod]);
    count++;
  }
  for (let k = 0; k < KINDS.length; k++)
    for (let l = 0; l < 2; l++) {
      const list = lists[k][l];
      if (!list.length) continue;
      const m = data.kinds[k][l];
      const g = new THREE.InstancedBufferGeometry();
      g.setAttribute('position', new THREE.BufferAttribute(m.P, 3));
      g.setAttribute('normal', new THREE.BufferAttribute(m.N, 3));
      g.setAttribute('uv', new THREE.BufferAttribute(m.U, 2));
      g.setAttribute('extra', new THREE.BufferAttribute(m.X, 3));
      const A = [], B = [], T = [];
      for (const [i, share] of list) {
        A.push(trees[i], trees[i + 1], trees[i + 2], trees[i + 3]);
        B.push(trees[i + 5], trees[i + 4], share, 0);
        T.push(trees[i + 7], trees[i + 8], trees[i + 9]);
      }
      g.setAttribute('a', new THREE.InstancedBufferAttribute(new Float32Array(A), 4));
      g.setAttribute('b', new THREE.InstancedBufferAttribute(new Float32Array(B), 4));
      g.setAttribute('tint', new THREE.InstancedBufferAttribute(new Float32Array(T), 3));
      g.instanceCount = list.length;
      // The z flip turned the winding round; both sides are drawn, so it doesn't matter here.
      const idx = [...m.B, ...m.L];
      g.setIndex(idx);
      g.addGroup(0, m.B.length, 0);
      g.addGroup(m.B.length, m.L.length, 1);
      const mat = (layers, isLeaves) => new THREE.RawShaderMaterial({
        glslVersion: THREE.GLSL3, vertexShader: treeVert, fragmentShader: treeFrag, side: THREE.DoubleSide,
        uniforms: { layers: { value: layers }, leaves: { value: isLeaves ? 1 : 0 }, kind: { value: k }, colour: { value: new THREE.Vector3(...m.colour) }, wind: { value: windValue() }, ...lights },
      });
      const mesh = new THREE.Mesh(g, [mat(bark, false), mat(leaves, true)]);
      mesh.frustumCulled = false;
      group.add(mesh);
    }
  return { group, fade: new THREE.Vector3(r.start, 1 / (r.end - r.start), 1), count };
}

/** One grass tile, as Grass.TileMesh builds it. */
function tileGeometry() {
  const n = ACROSS * ACROSS, roots = [], blades = [], idx = [];
  let seed = 5;
  const rand = () => { seed = (seed * 16807) % 2147483647; return seed / 2147483647; };
  for (let b = 0; b < n; b++) {
    const rx = (b % ACROSS + 0.1 + 0.8 * rand()) / ACROSS, rz = (Math.floor(b / ACROSS) + 0.1 + 0.8 * rand()) / ACROSS;
    const r0 = rand(), r1 = rand();
    for (let v = 0; v < 7; v++) {
      roots.push(rx, rz);
      blades.push(v === 6 ? 0 : v % 2 === 0 ? -1 : 1, v === 6 ? 1 : Math.floor(v / 2) / 3, r0, r1);
    }
    const s = b * 7;
    for (let q = 0; q < 2; q++) { const a = s + q * 2; idx.push(a, a + 2, a + 1, a + 1, a + 2, a + 3); }
    idx.push(s + 4, s + 6, s + 5);
  }
  const g = new THREE.InstancedBufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(new Float32Array(n * 7 * 3), 3));
  g.setAttribute('root', new THREE.Float32BufferAttribute(roots, 2));
  g.setAttribute('blade', new THREE.Float32BufferAttribute(blades, 4));
  g.setIndex(idx);
  return g;
}

const grassVert = `
  precision highp float;
  in vec3 position; in vec2 root; in vec4 blade; in vec4 tile;
  uniform mat4 viewMatrix, projectionMatrix;
  uniform sampler2D fields, mowing, growth, heights;
  uniform vec4 map; uniform vec3 eye; uniform vec4 reach, rings; uniform float stripes;
  uniform vec4 terrain; uniform vec2 counts;   // originX, originZ, spacing, base; countX, countZ
  ${swayGlsl}
  out vec3 vAlbedo; out vec3 vN; out float vFog;
  float h(int x, int z) { return texelFetch(heights, ivec2(x, z), 0).r; }
  // The ground mesh's own triangles, as preview.html draws it.
  float groundAt(vec2 p) {
    vec2 g = (p - terrain.xy) / terrain.z;
    ivec2 c = ivec2(floor(g)); vec2 f = g - vec2(c);
    if (f.x + f.y <= 1.0) return h(c.x, c.y) + f.x * (h(c.x + 1, c.y) - h(c.x, c.y)) + f.y * (h(c.x, c.y + 1) - h(c.x, c.y));
    return h(c.x + 1, c.y + 1) + (1.0 - f.x) * (h(c.x, c.y + 1) - h(c.x + 1, c.y + 1)) + (1.0 - f.y) * (h(c.x + 1, c.y) - h(c.x + 1, c.y + 1));
  }
  vec3 groundAlbedo(vec3 p, float pixel) {
    vec2 uv = (p.xz - map.xy) * map.zw;
    vec3 albedo = texture(fields, uv).rgb;
    vec4 mow = texture(mowing, uv);
    float d = mow.r / 5.0, across = pixel / 5.0;
    float fresh = length(mow.ba);
    float wave = clamp((abs(fract(d * 0.5) - 0.5) * 4.0 - 1.0) / max(across * 2.0, 0.08), -1.0, 1.0);
    float fade = fresh * clamp(1.5 - across * 2.5, 0.0, 1.0) * clamp((mow.g - mow.r) / max(pixel, 0.05) + 0.5, 0.0, 1.0);
    vec2 along = mow.ba / max(fresh, 1e-3);
    vec2 view = p.xz - eye.xz;
    float lean = dot(view, along) / max(length(view), 1e-3);
    return albedo * (1.0 + stripes * 0.15 * wave * fade * (0.35 + 0.65 * lean));
  }
  vec2 mownLean(vec3 p) {
    vec4 mow = texture(mowing, (p.xz - map.xy) * map.zw);
    float fresh = length(mow.ba);
    if (stripes < 0.5 || fresh < 1e-3 || mow.r > mow.g) return vec2(0.0);
    float stripe = abs(fract(mow.r / 5.0 * 0.5) - 0.5) * 4.0 - 1.0 >= 0.0 ? 1.0 : -1.0;
    return mow.ba * stripe;
  }
  void main() {
    vec2 r = root;
    float ring = floor(tile.w), seed = fract(tile.w);
    if (fract(seed * 2.0) >= 0.5) r.x = 1.0 - r.x;
    if (fract(seed * 4.0) >= 0.5) r.y = 1.0 - r.y;
    if (fract(seed * 8.0) >= 0.5) r = r.yx;
    float size = tile.z;
    vec3 p0 = vec3(tile.x + r.x * size, 0.0, tile.y + r.y * size);
    // The road ribbon in the preview reaches 1.6 m past the road's edge; the game's map from above stands in for this.
    vec4 mow = texture(mowing, (p0.xz - map.xy) * map.zw);
    p0.y = groundAt(p0.xz);
    float dist = distance(p0, eye);
    float inner = ring < 0.5 ? 0.0 : ring < 1.5 ? rings.x : rings.y;
    float outer = ring < 0.5 ? rings.x : ring < 1.5 ? rings.y : rings.z;
    bool last = ring > rings.w - 1.5;
    float t = clamp((dist - outer * (1.0 - reach.w)) / (outer * reach.w), 0.0, 1.0);
    float keep = last ? 1.0 - t : mix(1.0, 0.25, t);
    float grow = texture(growth, (p0.xz - map.xy) * map.zw).r;
    bool alive = mow.r > 1.7 && dist >= inner && dist < outer && blade.z < keep && grow > 0.01;
    float shrink = clamp((reach.y - dist) / (reach.y - reach.x), 0.0, 1.0);
    float height = grow * (0.65 + 0.7 * blade.w) * shrink;
    float width = reach.z * exp2(ring) * (0.7 + 0.6 * fract(blade.z * 7.3)) / sqrt(max(keep, 0.25));
    float yaw = (blade.z * 13.7 + seed * 31.1) * 6.2831853;
    vec2 face = vec2(cos(yaw), sin(yaw)), side = vec2(-face.y, face.x);
    float up = blade.y;
    vec2 lean = (mownLean(p0) * 0.45 + face * 0.25) * height + foliageSway(p0, 1.0) * height * 0.4;
    vec3 p = p0;
    p.xz += side * (blade.x * width * 0.5 * (1.0 - up * up));
    p.xz += lean * up * up;
    p.y += height * up * (1.0 - 0.25 * up * dot(lean, lean) / max(height * height, 1e-4));
    vec3 bn = normalize(vec3(face.x, 0.4, face.y));
    vec3 n = normalize(mix(vec3(0.0, 1.0, 0.0), bn, 0.35 * shrink));
    vN = vec3(n.x, n.y, -n.z);
    float vary = (0.78 + 0.44 * up) * (0.85 + 0.3 * fract(blade.w * 5.1));
    vAlbedo = groundAlbedo(p0, dist * 0.0012) * mix(1.0, vary, shrink);
    if (!alive) p = vec3(0.0, -1e5, 0.0);
    vec4 view = viewMatrix * vec4(p.x, p.y, -p.z, 1.0);
    vFog = -view.z;
    gl_Position = projectionMatrix * view;
  }`;
const grassFrag = `
  precision highp float;
  uniform vec3 sun, sunColour, sky, groundAmb, fogColour; uniform vec2 fogRange;
  in vec3 vAlbedo; in vec3 vN; in float vFog;
  out vec4 o;
  void main() {
    vec3 n = normalize(vN);
    // As preview.html lights the ground.
    vec3 lit = vAlbedo / 3.14159 * (mix(groundAmb, sky, n.y * 0.5 + 0.5) * 2.4 + sunColour * 2.6 * max(dot(n, sun), 0.0));
    float fog = clamp((vFog - fogRange.x) / (fogRange.y - fogRange.x), 0.0, 1.0);
    o = vec4(pow(mix(lit, fogColour, fog), vec3(1.0 / 2.2)), 1.0);
  }`;

let tileGeo = null;
/**
 * The grass round a camera (Unity frame position, three.js camera for culling), as Grass.Draw picks its tiles.
 * textures: { fields, mowing, growth, heights }; terrain: circuit.terrain; uniforms: the ground's lights and map.
 */
export function grass(eye, camera, setting, textures, terrain, mapRect, stripes, lights) {
  const r = GRASS_RINGS[setting];
  const group = new THREE.Group();
  if (!r) return { group, blades: 0 };
  tileGeo = tileGeo || tileGeometry();
  const last = r.edges[r.edges.length - 1];
  camera.updateMatrixWorld();
  const frustum = new THREE.Frustum().setFromProjectionMatrix(new THREE.Matrix4().multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse));
  let blades = 0;
  for (let ring = 0; ring < r.edges.length; ring++) {
    const size = r.spacing * ACROSS * (1 << ring), inner = ring ? r.edges[ring - 1] : 0, outer = r.edges[ring];
    const tiles = [];
    for (let z = Math.floor((eye[2] - outer) / size); z <= Math.floor((eye[2] + outer) / size); z++)
      for (let x = Math.floor((eye[0] - outer) / size); x <= Math.floor((eye[0] + outer) / size); x++) {
        const cx = (x + 0.5) * size, cz = (z + 0.5) * size, half = size / 2 + 0.6;
        const dx = Math.abs(eye[0] - cx), dz = Math.abs(eye[2] - cz);
        const near = Math.hypot(Math.max(dx - half, 0), Math.max(dz - half, 0)), far = Math.hypot(dx + half, dz + half);
        if (near >= outer || far < inner) continue;
        const box = new THREE.Box3(new THREE.Vector3(cx - half, eye[1] - 80, -cz - half), new THREE.Vector3(cx + half, eye[1] + 20, -cz + half));
        if (!frustum.intersectsBox(box)) continue;
        tiles.push(x * size, z * size, size, ring + hash(x, z, ring) * 0.999);
      }
    if (!tiles.length) continue;
    const g = tileGeo.clone();
    g.setAttribute('tile', new THREE.InstancedBufferAttribute(new Float32Array(tiles), 4));
    g.instanceCount = tiles.length / 4;
    blades += g.instanceCount * ACROSS * ACROSS;
    const mat = new THREE.RawShaderMaterial({
      glslVersion: THREE.GLSL3, vertexShader: grassVert, fragmentShader: grassFrag, side: THREE.DoubleSide,
      uniforms: {
        ...textures, ...lights, map: { value: mapRect }, eye: { value: new THREE.Vector3(...eye) }, stripes: { value: stripes },
        reach: { value: new THREE.Vector4(last * SHRINK_FROM, last, r.spacing * 0.4, THINNING) },
        rings: { value: new THREE.Vector4(r.edges[0], r.edges.length > 1 ? r.edges[1] : last, last, r.edges.length) },
        terrain: { value: new THREE.Vector4(terrain.originX, terrain.originZ, terrain.spacing, 0) },
        wind: { value: windValue() },
      },
    });
    const mesh = new THREE.Mesh(g, mat);
    mesh.frustumCulled = false;
    group.add(mesh);
  }
  return { group, blades };
}

function hash(x, z, ring) {
  let h = (Math.imul(x, 73856093) ^ Math.imul(z, 19349663) ^ Math.imul(ring, 83492791)) >>> 0;
  h ^= h >>> 13; h = Math.imul(h, 0x5bd1e995) >>> 0; h ^= h >>> 15;
  return (h & 0xffffff) / 16777216;
}
