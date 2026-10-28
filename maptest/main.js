// 섬 트랙 맵 테스트 (실사 버전): GPT 섬 그림을 설계도로 삼아 직접 만든 3D 원형 코스.
// 실사 느낌을 위해: 실제 하늘 사진(배경) + 같은 하늘의 HDR(주변광·반사), 사진 질감(잔디·바위·기와·벽),
// 실제 나무 생성기(ez-tree), 3D 풀잎, 반사하는 물결, 부드러운 그림자, 구석 그늘(AO)·색감 보정.
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { RGBELoader } from 'three/addons/loaders/RGBELoader.js';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { Water } from 'three/addons/objects/Water.js';
import * as SkeletonUtils from 'three/addons/utils/SkeletonUtils.js';
import { buildTrees } from './lib/trees.js';
import { buildGrass } from './lib/grass.js';
import { createPost } from './lib/post.js';

const $ = (id) => document.getElementById(id);
const params = new URLSearchParams(location.search);

// ---------------- 화질 (?quality=high|medium|low, 기본: 폰 medium, 컴퓨터 high) ----------------
// high는 외장 그래픽카드용 (물 반사가 장면을 한 번 더 그려서 무겁다). 기본은 medium
const PRESETS = {
  high: { pixelRatio: 1.5, shadowSize: 4096, treeRadius: 110, grassSize: 60, grassDensity: 26, post: true, msaa: 4, ao: true, water: 'reflect' },
  medium: { pixelRatio: 1, shadowSize: 2048, treeRadius: 65, grassSize: 40, grassDensity: 12, post: true, msaa: 0, ao: false, water: 'simple' },
  low: { pixelRatio: 0.85, shadowSize: 1024, treeRadius: 40, grassSize: 0, grassDensity: 0, post: false, msaa: 0, ao: false, water: 'simple' },
};
const QNAME = PRESETS[params.get('quality')] ? params.get('quality') : 'medium';
const Q = { ...PRESETS[QNAME] };

// ---------------- 트랙: 경기장 모양 고리 (직선 L + 반원 반지름 R) ----------------
const L = 160;
const R = 62;
const LAP = 2 * L + 2 * Math.PI * R; // 한 바퀴 (트랙 가운데 기준, 약 710m)
const TRACK_W = 7.2; // 6레인 × 1.2m

function track(s) {
  s = ((s % LAP) + LAP) % LAP;
  if (s < L) return { x: -L / 2 + s, z: R, dx: 1, dz: 0 };
  s -= L;
  if (s < Math.PI * R) {
    const t = Math.PI / 2 - s / R;
    return { x: L / 2 + R * Math.cos(t), z: R * Math.sin(t), dx: Math.sin(t), dz: -Math.cos(t) };
  }
  s -= Math.PI * R;
  if (s < L) return { x: L / 2 - s, z: -R, dx: -1, dz: 0 };
  s -= L;
  const t = -Math.PI / 2 - s / R;
  return { x: -L / 2 + R * Math.cos(t), z: R * Math.sin(t), dx: Math.sin(t), dz: -Math.cos(t) };
}
// s 지점에서 바깥쪽(+)/안쪽(-)으로 o(m) 떨어진 점
function off(s, o) {
  const p = track(s);
  return { x: p.x - p.dz * o, z: p.z + p.dx * o, dx: p.dx, dz: p.dz };
}
// 어떤 점이 트랙 가운데 선에서 얼마나 떨어졌나 (+ 바깥, - 안쪽)
function trackOffset(x, z) {
  const cx = Math.max(-L / 2, Math.min(L / 2, x));
  return Math.hypot(x - cx, z) - R;
}
const ang = (s) => (2 * Math.PI * s) / LAP;
const coastOff = (s) => 56 + 9 * Math.sin(ang(s) * 3 + 1) + 5 * Math.sin(ang(s) * 7 + 2); // 섬 해안선
const lakeOff = (s) => 15 + 2.5 * Math.sin(ang(s) * 5); // 호숫가 (트랙 안쪽으로)

// 같은 결과가 나오는 난수 (새로고침해도 나무 위치가 같게)
let seed = 7;
const rand = () => {
  seed = (seed + 0x6d2b79f5) | 0;
  let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
  t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
};
const rr = (a, b) => a + (b - a) * rand();
// 부드러운 잡음 (먼 산 모양, 바위 울퉁불퉁)
const hash = (i, j) => { const v = Math.sin(i * 127.1 + j * 311.7) * 43758.5453; return v - Math.floor(v); };
function vnoise(x, z) {
  const i = Math.floor(x), j = Math.floor(z), fx = x - i, fz = z - j;
  const u = fx * fx * (3 - 2 * fx), v = fz * fz * (3 - 2 * fz);
  const a = hash(i, j) + (hash(i + 1, j) - hash(i, j)) * u;
  const b = hash(i, j + 1) + (hash(i + 1, j + 1) - hash(i, j + 1)) * u;
  return a + (b - a) * v;
}
function fbm(x, z) {
  let s = 0, a = 0.5, f = 1;
  for (let k = 0; k < 5; k++) { s += a * vnoise(x * f, z * f); f *= 2.03; a *= 0.5; }
  return s / 0.97;
}

// ---------------- 렌더러·장면·카메라 ----------------
const renderer = new THREE.WebGLRenderer({ canvas: $('view'), antialias: !Q.post });
renderer.setPixelRatio(Math.min(devicePixelRatio, Q.pixelRatio));
renderer.setSize(innerWidth, innerHeight);
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.0;

const scene = new THREE.Scene();
scene.fog = new THREE.FogExp2(0xc3d2df, 0.00021); // 멀수록 뿌옇게 (대기 원근감)
const camera = new THREE.PerspectiveCamera(58, innerWidth / innerHeight, 0.1, 16000);

// ---------------- 질감 ----------------
const texLoader = new THREE.TextureLoader();
const aniso = renderer.capabilities.getMaxAnisotropy();
function tex(file, repeat = 1, color = true) {
  const t = texLoader.load(`tex/${file}`);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.repeat.set(repeat, repeat);
  t.anisotropy = aniso;
  if (color) t.colorSpace = THREE.SRGBColorSpace;
  return t;
}
function pbr(name, repeat, extra = {}) {
  return new THREE.MeshStandardMaterial({
    map: tex(`${name}_diff.jpg`, repeat), normalMap: tex(`${name}_nor_gl.jpg`, repeat, false), roughness: 0.95, ...extra,
  });
}
// 같은 사진이 바둑판처럼 반복돼 보이지 않게: 크기를 달리해 두 번 섞고, 큰 얼룩으로 밝기를 흔든다
function antiTile(material, strength = 0.4) {
  material.onBeforeCompile = (sh) => {
    sh.fragmentShader = sh.fragmentShader.replace('#include <map_fragment>', `
      #ifdef USE_MAP
        vec4 c1 = texture2D(map, vMapUv);
        vec4 c2 = texture2D(map, vMapUv * 0.27 + vec2(0.31, 0.17));
        float blot = texture2D(map, vMapUv * 0.031 + vec2(0.5)).g;
        vec4 sampledDiffuseColor = mix(c1, c2, 0.45);
        sampledDiffuseColor.rgb *= ${(1 - strength).toFixed(2)} + ${(strength * 2.2).toFixed(2)} * blot;
        diffuseColor *= sampledDiffuseColor;
      #endif`);
  };
  return material;
}

const M = {
  grass: antiTile(new THREE.MeshStandardMaterial({
    map: tex('leafy_grass_diff.jpg', 0.25), normalMap: tex('leafy_grass_nor.jpg', 0.25, false), roughness: 1, color: 0x9cc77e,
  })),
  cliff: pbr('rock_face', 1 / 6),
  rock: pbr('aerial_rocks_02', 0.5),
  track: new THREE.MeshStandardMaterial({
    color: 0x8e382d, roughness: 0.95, envMapIntensity: 0.5, normalMap: tex('clean_asphalt_nor.jpg', 0.5, false), normalScale: new THREE.Vector2(0.6, 0.6),
  }),
  line: new THREE.MeshStandardMaterial({ color: 0xecebe5, roughness: 0.7, normalMap: tex('clean_asphalt_nor.jpg', 0.5, false), normalScale: new THREE.Vector2(0.4, 0.4) }),
  curb: new THREE.MeshStandardMaterial({ color: 0xb9b5ab, roughness: 0.9, map: tex('clean_asphalt_diff.jpg', 0.5), normalMap: tex('clean_asphalt_nor.jpg', 0.5, false) }),
  wall: pbr('plastered_wall_04', 1 / 3, { color: 0xf3eee4 }),
  roof: pbr('grey_roof_tiles', 1 / 2.5, { roughness: 0.8 }),
  roofRed: pbr('grey_roof_tiles', 1 / 2.5, { roughness: 0.8, color: 0xd28a70 }),
  glass: new THREE.MeshStandardMaterial({ color: 0x1b2730, roughness: 0.08, metalness: 0.4 }),
  wood: new THREE.MeshStandardMaterial({ color: 0x6e4b30, roughness: 0.85 }),
  woodRed: new THREE.MeshStandardMaterial({ color: 0x7d2a1f, roughness: 0.8 }),
  metal: new THREE.MeshStandardMaterial({ color: 0x3b4046, roughness: 0.5, metalness: 0.7 }),
  concrete: new THREE.MeshStandardMaterial({ color: 0xb5b1a8, roughness: 0.9, map: tex('clean_asphalt_diff.jpg', 0.2), normalMap: tex('clean_asphalt_nor.jpg', 0.2, false) }),
  lamp: new THREE.MeshStandardMaterial({ color: 0xfff1c8, emissive: 0xffe2a8, emissiveIntensity: 0.4 }),
  far: antiTile(pbr('aerial_grass_rock', 1 / 140, { vertexColors: true }), 0.35),
};

// ---------------- 하늘·빛 ----------------
const bg = texLoader.load('tex/sky_4k.jpg');
bg.mapping = THREE.EquirectangularReflectionMapping;
bg.colorSpace = THREE.SRGBColorSpace;
scene.background = bg;
scene.backgroundIntensity = Q.post ? 1.6 : 1.0; // 후처리 색감 보정을 거치면 배경이 어두워져서 보정
scene.environmentIntensity = 1.0;

const sunDir = new THREE.Vector3(0.55, 0.72, 0.42).normalize(); // HDR을 읽으면 사진 속 해 방향으로 바뀐다
const sun = new THREE.DirectionalLight(0xfff0dc, 3.4);
sun.castShadow = true;
sun.shadow.mapSize.set(Q.shadowSize, Q.shadowSize);
sun.shadow.bias = -0.0003;
sun.shadow.normalBias = 0.04;
scene.add(sun, sun.target);
scene.add(new THREE.HemisphereLight(0xcfe3ff, 0x4a5a33, 0.25));
// 그림자는 카메라 앞쪽 일정 범위만 계산해서 선명하게
function aimShadow(center, half) {
  const c = sun.shadow.camera;
  if (c.right !== half) {
    c.left = c.bottom = -half;
    c.right = c.top = half;
    c.near = 1;
    c.far = 1200;
    c.updateProjectionMatrix();
  }
  sun.target.position.copy(center);
  sun.position.copy(center).addScaledVector(sunDir, 500);
}

// ---------------- 바다·호수 ----------------
let water = null;
{
  const geo = new THREE.PlaneGeometry(20000, 20000);
  const normals = tex('waternormals.jpg', 1, false);
  if (Q.water === 'reflect') {
    // 주변(하늘·섬·나무)을 실제로 비추는 물 + 물결
    water = new Water(geo, {
      textureWidth: 1024, textureHeight: 1024, waterNormals: normals, sunDirection: sunDir.clone(),
      sunColor: 0xfff3e0, waterColor: 0x0f4a5e, distortionScale: 2.2, fog: true,
    });
    water.material.uniforms.size.value = 3.5;
  } else {
    normals.repeat.set(1200, 1200);
    water = new THREE.Mesh(geo, new THREE.MeshStandardMaterial({
      color: 0x1d5a6d, roughness: 0.06, metalness: 0.1, normalMap: normals, normalScale: new THREE.Vector2(0.35, 0.35),
    }));
  }
  water.rotation.x = -Math.PI / 2;
  water.position.y = -1.0;
  water.userData.noAO = true;
  scene.add(water);
}

new RGBELoader().load('tex/sky_1k.hdr', (hdr) => {
  // 사진에서 가장 밝은 점 = 해. 그 방향에서 햇빛을 비춰 그림자가 하늘과 맞게
  const { data, width, height } = hdr.image;
  const half = data instanceof Uint16Array;
  const val = (i) => (half ? THREE.DataUtils.fromHalfFloat(data[i]) : data[i]);
  let best = -1, bx = 0, by = 0;
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const i = (y * width + x) * 4;
      const l = val(i) + val(i + 1) + val(i + 2);
      if (l > best) { best = l; bx = x; by = y; }
    }
  }
  const u = (bx + 0.5) / width;
  const el = Math.max(0.35, Math.abs(0.5 - (by + 0.5) / height) * Math.PI);
  const phi = (u - 0.5) * 2 * Math.PI;
  sunDir.set(Math.cos(phi) * Math.cos(el), Math.sin(el), Math.sin(phi) * Math.cos(el)).normalize();
  if (water.material.uniforms?.sunDirection) water.material.uniforms.sunDirection.value.copy(sunDir);
  const pmrem = new THREE.PMREMGenerator(renderer);
  scene.environment = pmrem.fromEquirectangular(hdr).texture;
  hdr.dispose();
  pmrem.dispose();
});

// ---------------- 섬 ----------------
const shapePts = (fn, step = 2) => {
  const pts = [];
  for (let s = 0; s < LAP; s += step) {
    const p = fn(s);
    pts.push(new THREE.Vector2(p.x, -p.z));
  }
  return pts;
};
{
  // 해안선을 바깥 모양, 호수를 구멍으로 한 판을 세운다 (옆면은 바위 절벽)
  const shape = new THREE.Shape(shapePts((s) => off(s, coastOff(s))));
  shape.holes.push(new THREE.Path(shapePts((s) => off(s, -lakeOff(s)))));
  const geo = new THREE.ExtrudeGeometry(shape, { depth: 5, bevelEnabled: false, curveSegments: 1 });
  geo.rotateX(-Math.PI / 2);
  geo.translate(0, -5, 0);
  const island = new THREE.Mesh(geo, [M.grass, M.cliff]);
  island.receiveShadow = true;
  scene.add(island);
}

// 트랙처럼 고리를 따라 이어지는 띠 (질감 좌표: 고리 방향 m, 가로 방향 m)
function ribbon(o1, o2, y, material, step = 1) {
  const n = Math.ceil(LAP / step);
  const pos = [];
  const uv = [];
  const idx = [];
  for (let i = 0; i <= n; i++) {
    const s = (i * LAP) / n;
    const a = off(s, o1);
    const b = off(s, o2);
    pos.push(a.x, y, a.z, b.x, y, b.z);
    uv.push(s, o1, s, o2);
  }
  for (let i = 0; i < n; i++) {
    const k = i * 2;
    idx.push(k, k + 1, k + 2, k + 1, k + 3, k + 2);
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  g.setIndex(idx);
  g.computeVertexNormals();
  const m = new THREE.Mesh(g, material);
  m.receiveShadow = true;
  scene.add(m);
  return m;
}
ribbon(-TRACK_W / 2 - 0.45, TRACK_W / 2 + 0.45, 0.03, M.curb);
ribbon(-TRACK_W / 2, TRACK_W / 2, 0.05, M.track);
for (let i = 0; i <= 6; i++) {
  const o = -TRACK_W / 2 + i * 1.2;
  ribbon(o - 0.025, o + 0.025, 0.06, M.line, 0.5);
}
{
  const p = track(0);
  const line = new THREE.Mesh(new THREE.PlaneGeometry(0.4, TRACK_W), M.line);
  line.rotation.x = -Math.PI / 2;
  line.position.set(p.x, 0.065, p.z);
  line.receiveShadow = true;
  scene.add(line);
}

// ---------------- 집 (회벽 + 기와 맞배지붕 + 창문) ----------------
const houses = [];
function gableRoof(w, d, rh, oh, material) {
  const s = new THREE.Shape([new THREE.Vector2(-w / 2 - oh, 0), new THREE.Vector2(w / 2 + oh, 0), new THREE.Vector2(0, rh)]);
  const g = new THREE.ExtrudeGeometry(s, { depth: d + oh * 2, bevelEnabled: false });
  g.translate(0, 0, -(d + oh * 2) / 2);
  return new THREE.Mesh(g, [M.wall, material]);
}
function house(s, o, w, d, h, roofMat = M.roof) {
  const p = off(s, o);
  const g = new THREE.Group();
  const body = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), M.wall);
  body.position.y = h / 2;
  const base = new THREE.Mesh(new THREE.BoxGeometry(w + 0.3, 0.5, d + 0.3), M.concrete);
  base.position.y = 0.25;
  const roof = gableRoof(w, d, h * 0.45, 0.7, roofMat);
  roof.position.y = h;
  g.add(body, base, roof);
  // 앞면(-z): 문 + 창문, 옆면에도 창문
  const door = new THREE.Mesh(new THREE.BoxGeometry(1.1, 2.1, 0.12), M.wood);
  door.position.set(0, 1.35, -d / 2 - 0.05);
  g.add(door);
  for (const x of [-w / 2 + 1.8, w / 2 - 1.8]) {
    const win = new THREE.Mesh(new THREE.BoxGeometry(1.4, 1.2, 0.1), M.glass);
    win.position.set(x, h * 0.55, -d / 2 - 0.04);
    const frame = new THREE.Mesh(new THREE.BoxGeometry(1.6, 1.4, 0.06), M.wood);
    frame.position.set(x, h * 0.55, -d / 2 - 0.02);
    g.add(frame, win);
  }
  for (const sx of [-1, 1]) {
    const win = new THREE.Mesh(new THREE.BoxGeometry(0.1, 1.1, 1.3), M.glass);
    win.position.set(sx * (w / 2 + 0.04), h * 0.55, 0);
    g.add(win);
  }
  g.traverse((m) => { if (m.isMesh) { m.castShadow = true; m.receiveShadow = true; } });
  g.position.set(p.x, 0, p.z);
  g.rotation.y = Math.atan2(-p.dz, p.dx); // 앞면(-z)이 트랙을 보게
  scene.add(g);
  houses.push({ x: p.x, z: p.z, r: Math.max(w, d) * 0.8 + 3 });
}
house(LAP * 0.05, 20, 12, 8, 4.2);
house(LAP * 0.12, 26, 9, 7, 3.6, M.roofRed);
house(LAP * 0.3, 22, 10, 8, 4);
house(LAP * 0.42, 18, 14, 9, 5, M.roofRed);
house(LAP * 0.47, 30, 8, 7, 3.4);
house(LAP * 0.58, 21, 11, 8, 4.2);
house(LAP * 0.72, 19, 9, 8, 3.8, M.roofRed);
house(LAP * 0.8, 27, 12, 9, 4.6);
house(LAP * 0.93, 22, 10, 7, 3.8);

// ---------------- 호수 가운데 섬 + 정자 ----------------
const isletTrees = [];
{
  const g = new THREE.Group();
  const base = new THREE.Mesh(new THREE.CylinderGeometry(13, 16, 3, 28), M.cliff);
  base.position.y = -1.2;
  const top = new THREE.Mesh(new THREE.CircleGeometry(13, 28), M.grass);
  top.rotation.x = -Math.PI / 2;
  top.position.y = 0.32;
  const pav = new THREE.Group();
  const floor = new THREE.Mesh(new THREE.CylinderGeometry(3.3, 3.4, 0.6, 8), M.concrete);
  floor.position.y = 0.6;
  pav.add(floor);
  for (let i = 0; i < 8; i++) {
    const a = (i / 8) * Math.PI * 2;
    const post = new THREE.Mesh(new THREE.CylinderGeometry(0.14, 0.14, 2.7, 10), M.woodRed);
    post.position.set(Math.cos(a) * 2.9, 2.25, Math.sin(a) * 2.9);
    pav.add(post);
  }
  const roof = new THREE.Mesh(new THREE.ConeGeometry(4.6, 2.1, 8, 1), M.roof);
  roof.position.y = 4.6;
  const ring = new THREE.Mesh(new THREE.CylinderGeometry(3.3, 3.3, 0.35, 8), M.woodRed);
  ring.position.y = 3.6;
  pav.add(roof, ring);
  pav.position.set(4, 0, 2);
  const dock = new THREE.Mesh(new THREE.BoxGeometry(2, 0.25, 9), M.wood);
  dock.position.set(4, -0.3, 16);
  g.add(base, top, pav, dock);
  g.traverse((m) => { if (m.isMesh) { m.castShadow = true; m.receiveShadow = true; } });
  scene.add(g);
  for (let i = 0; i < 12; i++) {
    const a = rr(0, 6.28);
    const d = rr(3, 10.5);
    const x = Math.cos(a) * d;
    const z = Math.sin(a) * d;
    if (Math.hypot(x - 4, z - 2) < 5.5) continue;
    isletTrees.push([x, -z, rr(0.6, 0.95), 1]);
  }
}

// ---------------- 다리 (섬 오른쪽 끝 → 건너편 땅) ----------------
const BRIDGE_X0 = L / 2 + R + TRACK_W / 2 + 2;
const BRIDGE_X1 = 700;
{
  const len = BRIDGE_X1 - BRIDGE_X0;
  const cx = (BRIDGE_X0 + BRIDGE_X1) / 2;
  const deck = new THREE.Mesh(new THREE.BoxGeometry(len, 0.8, 8), M.concrete);
  deck.position.set(cx, -0.35, 0);
  deck.castShadow = deck.receiveShadow = true;
  scene.add(deck);
  for (let x = BRIDGE_X0 + 40; x < BRIDGE_X1 - 10; x += 40) {
    const pier = new THREE.Mesh(new THREE.CylinderGeometry(1.1, 1.3, 4, 12), M.concrete);
    pier.position.set(x, -2.6, 0);
    pier.castShadow = true;
    scene.add(pier);
  }
  for (const zz of [-3.8, 3.8]) {
    const rail = new THREE.Mesh(new THREE.BoxGeometry(len, 0.12, 0.12), M.metal);
    rail.position.set(cx, 1.0, zz);
    scene.add(rail);
  }
  const posts = new THREE.InstancedMesh(new THREE.BoxGeometry(0.1, 1.0, 0.1), M.metal, Math.ceil(len / 2.5) * 2 + 2);
  const o = new THREE.Object3D();
  let k = 0;
  for (let x = BRIDGE_X0; x < BRIDGE_X1; x += 2.5) {
    for (const zz of [-3.8, 3.8]) { o.position.set(x, 0.5, zz); o.updateMatrix(); posts.setMatrixAt(k++, o.matrix); }
  }
  posts.count = k;
  scene.add(posts);
}

// ---------------- 바위·울타리·가로등 ----------------
function rockGeometry() {
  const g = new THREE.DodecahedronGeometry(1, 1);
  const p = g.attributes.position;
  for (let i = 0; i < p.count; i++) {
    const x = p.getX(i), y = p.getY(i), z = p.getZ(i);
    const k = 1 + 0.44 * (vnoise(x * 2.1 + 3, z * 2.1 + y * 1.7) - 0.5);
    p.setXYZ(i, x * k, y * k * 0.75, z * k);
  }
  g.computeVertexNormals();
  return g;
}
function instances(geo, material, list) {
  const m = new THREE.InstancedMesh(geo, material, list.length);
  const o = new THREE.Object3D();
  list.forEach((it, i) => {
    o.position.set(it.x, it.y ?? 0, it.z);
    o.rotation.set(it.rx ?? 0, it.ry ?? rand() * 6.28, 0);
    if (typeof it.s === 'number') o.scale.setScalar(it.s); else o.scale.set(...it.s);
    o.updateMatrix();
    m.setMatrixAt(i, o.matrix);
    if (it.c != null) m.setColorAt(i, new THREE.Color(it.c));
  });
  m.castShadow = true;
  m.receiveShadow = true;
  scene.add(m);
  return m;
}
{
  const rocks = [];
  for (let s = 0; s < LAP; s += 1.6) {
    const p = off(s, coastOff(s) + rr(-0.5, 2.5));
    rocks.push({ x: p.x, y: rr(-1.4, -0.3), z: p.z, s: [rr(1.2, 3.2), rr(1, 2.4), rr(1.2, 3.2)], rx: rr(-0.3, 0.3), c: new THREE.Color().setHSL(0.08, 0.06, rr(0.7, 1)).getHex() });
  }
  for (let s = 0; s < LAP; s += 2.4) {
    const p = off(s, -lakeOff(s) - rr(0, 1.2));
    rocks.push({ x: p.x, y: rr(-1.1, -0.4), z: p.z, s: [rr(0.9, 2), rr(0.7, 1.4), rr(0.9, 2)], rx: rr(-0.3, 0.3), c: new THREE.Color().setHSL(0.08, 0.05, rr(0.75, 1)).getHex() });
  }
  instances(rockGeometry(), M.rock, rocks);

  const posts = [];
  const rails = [];
  const fo = -TRACK_W / 2 - 1;
  for (let s = 0; s < LAP; s += 2.5) {
    const p = off(s, fo);
    posts.push({ x: p.x, y: 0.5, z: p.z, s: 1, ry: Math.atan2(p.dx, p.dz) });
    const q = off(s + 1.25, fo);
    rails.push({ x: q.x, y: 0.88, z: q.z, s: 1, ry: Math.atan2(q.dx, q.dz) });
    rails.push({ x: q.x, y: 0.48, z: q.z, s: 1, ry: Math.atan2(q.dx, q.dz) });
  }
  instances(new THREE.BoxGeometry(0.11, 1.0, 0.11), M.wood, posts);
  instances(new THREE.BoxGeometry(0.05, 0.07, 2.5), M.wood, rails);
  const poles = [];
  const heads = [];
  for (let s = 10; s < LAP; s += 38) {
    const p = off(s, TRACK_W / 2 + 1.4);
    poles.push({ x: p.x, y: 2.25, z: p.z, s: 1 });
    heads.push({ x: p.x, y: 4.55, z: p.z, s: 1 });
  }
  instances(new THREE.CylinderGeometry(0.06, 0.09, 4.5, 10), M.metal, poles);
  instances(new THREE.SphereGeometry(0.26, 16, 12), M.lamp, heads);
}

// ---------------- 먼 땅과 산 (섬을 둘러싼 고리 모양 지형, 멀수록 높게) ----------------
{
  const NR = 110, NT = 320, R0 = 640, R1 = 9000;
  const pos = [];
  const uv = [];
  const idx = [];
  for (let i = 0; i <= NR; i++) {
    const r = R0 * Math.pow(R1 / R0, i / NR);
    for (let j = 0; j <= NT; j++) {
      const a = (j / NT) * Math.PI * 2;
      const x = Math.cos(a) * r;
      const z = Math.sin(a) * r;
      const shore = THREE.MathUtils.smoothstep(r, 660, 1050);
      const amp = 70 + 820 * THREE.MathUtils.smoothstep(r, 1300, 4200);
      const n = fbm(x / 1100 + 7, z / 1100 + 3);
      const ridge = 1 - Math.abs(fbm(x / 2600, z / 2600) * 2 - 1);
      const h = -4 + shore * (18 + amp * Math.pow(n, 1.7) * (0.6 + 0.6 * ridge));
      pos.push(x, h, z);
      uv.push(x, z);
    }
  }
  const W = NT + 1;
  for (let i = 0; i < NR; i++) {
    for (let j = 0; j < NT; j++) {
      const a = i * W + j;
      idx.push(a, a + W, a + 1, a + 1, a + W, a + W + 1);
    }
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  g.setIndex(idx);
  g.computeVertexNormals();
  // 완만한 곳은 숲(진초록), 가파르거나 높은 곳은 바위(회갈색), 물가는 모래빛
  const nrm = g.attributes.normal;
  const cols = new Float32Array(nrm.count * 3);
  const forest = new THREE.Color(0x93b46a), rock = new THREE.Color(0xc4bdac), sand = new THREE.Color(0xe2d6b4), c = new THREE.Color();
  for (let i = 0; i < nrm.count; i++) {
    const y = pos[i * 3 + 1];
    c.copy(forest).lerp(rock, THREE.MathUtils.clamp((0.86 - nrm.getY(i)) * 3 + (y - 500) / 700, 0, 1));
    c.lerp(sand, THREE.MathUtils.clamp((4 - y) / 6, 0, 1));
    c.toArray(cols, i * 3);
  }
  g.setAttribute('color', new THREE.BufferAttribute(cols, 3));
  const land = new THREE.Mesh(g, M.far);
  land.receiveShadow = true;
  scene.add(land);
}

// ---------------- 나무 (ez-tree: 가까운 건 3D 모델, 먼 건 사진 판) ----------------
const treeList = [...isletTrees];
const okSpot = (x, z) => {
  const o = trackOffset(x, z);
  const inner = o < -TRACK_W / 2 - 2.4 && o > -11.5;
  const outer = o > TRACK_W / 2 + 3 && o < 42;
  if (!inner && !outer) return false;
  if (houses.some((h) => Math.hypot(x - h.x, z - h.z) < h.r)) return false;
  if (x > L / 2 + R && Math.abs(z) < 7) return false; // 다리로 가는 길
  if (treeList.some((t) => Math.hypot(x - t[0], -z - t[1]) < 4.2)) return false;
  return true;
};
for (let i = 0; i < 6000 && treeList.length < 470; i++) {
  const x = rr(-L / 2 - R - 50, L / 2 + R + 50);
  const z = rr(-R - 50, R + 50);
  if (!okSpot(x, z)) continue;
  const k = rand();
  const kind = k < 0.28 ? 0 : k < 0.5 ? 1 : k < 0.68 ? 2 : k < 0.86 ? 3 : 4; // 소나무 2종, 참나무, 사시나무, 벚나무
  treeList.push([x, -z, rr(0.75, 1.25), kind]);
}
const updateTrees = await buildTrees(scene, renderer, treeList, Q.treeRadius);
const updateGrass = buildGrass(scene, Q.grassSize, Q.grassDensity, {
  L, R, trackHalf: TRACK_W / 2 + 0.45, innerMin: -12.5, outerMax: 44,
});

// ---------------- 러너 ----------------
const runners = [
  { name: '나', me: true, lane: -0.6, base: 10, s: 0, color: 0x3d7bdc },
  { name: '준호', lane: -1.8, base: 10.6, s: -30, color: 0xe2683a },
  { name: '민지', lane: 0.6, base: 9.3, s: 40, color: 0xc9579c },
  { name: '서연', lane: 1.8, base: 11.3, s: -90, color: 0x34a874 },
];
const me = runners[0];
let groundSpeed = 3; // 달리기 동작에서 발이 뒤로 밀리는 속도 (m/s, 모델 크기 1) — 모델을 읽은 뒤 잰다
const MODEL_H = 1.81;
const AVATAR_H = 1.7;

function makeLabel(bg) {
  const c = document.createElement('canvas');
  c.width = 256;
  c.height = 72;
  const map = new THREE.CanvasTexture(c);
  map.colorSpace = THREE.SRGBColorSpace;
  const sp = new THREE.Sprite(new THREE.SpriteMaterial({ map, depthWrite: false, toneMapped: false, fog: false }));
  sp.scale.set(1.9, 0.53, 1);
  sp.position.y = 2.2;
  sp.renderOrder = 10;
  sp.userData.draw = (t) => {
    const ctx = c.getContext('2d');
    ctx.clearRect(0, 0, c.width, c.height);
    ctx.font = 'bold 34px system-ui, "Malgun Gothic", sans-serif';
    const w = Math.min(250, ctx.measureText(t).width + 36);
    ctx.fillStyle = bg;
    ctx.beginPath();
    ctx.roundRect((c.width - w) / 2, 6, w, 60, 28);
    ctx.fill();
    ctx.fillStyle = '#fff';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(t, c.width / 2, 38);
    map.needsUpdate = true;
  };
  return sp;
}
function measureGroundSpeed(gltf) {
  const clip = THREE.AnimationClip.findByName(gltf.animations, 'run');
  const model = SkeletonUtils.clone(gltf.scene);
  const feet = [];
  model.traverse((o) => { if (o.isBone && /(Left|Right)Foot$/.test(o.name)) feet.push(o); });
  if (!clip || !feet.length) return null;
  const mixer = new THREE.AnimationMixer(model);
  mixer.clipAction(clip).play();
  const n = 120;
  const dt = clip.duration / n;
  const p = new THREE.Vector3();
  const samples = feet.map(() => []);
  for (let i = 0; i <= n; i++) {
    mixer.setTime(i * dt);
    model.updateMatrixWorld(true);
    feet.forEach((f, k) => { f.getWorldPosition(p); samples[k].push([p.y, p.z]); });
  }
  const speeds = [];
  for (const s of samples) {
    const low = Math.min(...s.map((v) => v[0]));
    for (let i = 1; i < s.length; i++) if (s[i][0] < low + 0.02 && s[i - 1][0] < low + 0.02) speeds.push(Math.abs(s[i][1] - s[i - 1][1]) / dt);
  }
  speeds.sort((a, b) => a - b);
  const v = speeds[speeds.length >> 1];
  return v > 0.2 ? v : null;
}
async function loadRunners() {
  const gltf = await new GLTFLoader().loadAsync('assets/Xbot.glb');
  groundSpeed = measureGroundSpeed(gltf) ?? groundSpeed;
  for (const r of runners) {
    r.root = new THREE.Group();
    const model = SkeletonUtils.clone(gltf.scene);
    model.scale.setScalar(AVATAR_H / MODEL_H);
    model.traverse((o) => {
      if (!o.isMesh) return;
      o.castShadow = true;
      o.receiveShadow = true;
      o.frustumCulled = false;
      o.material = o.material.clone();
      o.material.color.set(o.material.name.includes('Joints') ? 0x2b2f36 : r.color);
      o.material.roughness = 0.6;
    });
    r.mixer = new THREE.AnimationMixer(model);
    r.run = r.mixer.clipAction(THREE.AnimationClip.findByName(gltf.animations, 'run'));
    r.idle = r.mixer.clipAction(THREE.AnimationClip.findByName(gltf.animations, 'idle'));
    r.run.play();
    r.idle.play();
    r.root.add(model);
    if (!r.me) {
      r.label = makeLabel(`#${r.color.toString(16).padStart(6, '0')}d0`);
      r.root.add(r.label);
    }
    scene.add(r.root);
  }
}

// ---------------- 카메라 ----------------
let mode = 'tp';
let lookBack = false;
const controls = new OrbitControls(camera, renderer.domElement);
controls.enabled = false;
controls.maxPolarAngle = Math.PI * 0.47;
controls.minDistance = 20;
controls.maxDistance = 1600;
function setMode(m) {
  mode = m;
  document.querySelectorAll('[data-mode]').forEach((b) => b.classList.toggle('on', b.dataset.mode === m));
  controls.enabled = m === 'top';
  if (m === 'top') {
    camera.position.set(-60, 210, 330);
    controls.target.set(0, 0, 0);
    controls.update();
  }
}
document.querySelectorAll('[data-mode]').forEach((b) => { b.onclick = () => setMode(b.dataset.mode); });
$('back').onclick = () => { lookBack = !lookBack; $('back').classList.toggle('on', lookBack); };
addEventListener('keydown', (e) => {
  if (e.key === '1') setMode('fp');
  if (e.key === '2') setMode('tp');
  if (e.key === '3') setMode('top');
  if (e.key === 'b' || e.key === 'B') $('back').click();
});
if (params.get('mode')) setMode(params.get('mode'));
if (params.get('back') === '1') $('back').click();
if (params.get('kmh')) $('speed').value = params.get('kmh');
if (params.get('s')) runners.forEach((r) => { r.s += Number(params.get('s')); });

// ---------------- 화면 글자·미니맵 ----------------
let toastT = 0;
function toast(msg) {
  $('toast').textContent = msg;
  $('toast').style.opacity = 1;
  toastT = 2.5;
}
const gapOf = (r) => {
  let g = (r.s - me.s) % LAP;
  if (g > LAP / 2) g -= LAP;
  if (g < -LAP / 2) g += LAP;
  return g;
};
const mini = $('mini');
const mctx = mini.getContext('2d');
function drawMini() {
  const W = mini.width;
  const H = mini.height;
  const k = Math.min(W / (L + 2 * R + 40), H / (2 * R + 40));
  const tx = (x) => W / 2 + x * k;
  const tz = (z) => H / 2 + z * k;
  mctx.clearRect(0, 0, W, H);
  mctx.strokeStyle = '#c65a48';
  mctx.lineWidth = TRACK_W * k * 1.4;
  mctx.beginPath();
  for (let s = 0; s <= LAP; s += 6) {
    const p = track(s);
    if (s === 0) mctx.moveTo(tx(p.x), tz(p.z)); else mctx.lineTo(tx(p.x), tz(p.z));
  }
  mctx.stroke();
  for (const r of runners) {
    const p = off(r.s, r.lane);
    mctx.fillStyle = `#${r.color.toString(16).padStart(6, '0')}`;
    mctx.beginPath();
    mctx.arc(tx(p.x), tz(p.z), r.me ? 5 : 4, 0, 7);
    mctx.fill();
    if (r.me) { mctx.strokeStyle = '#fff'; mctx.lineWidth = 2; mctx.stroke(); }
  }
}

// ---------------- 매 프레임 ----------------
const post = createPost(renderer, scene, camera, Q);
const clock = new THREE.Clock();
let t = 0;
let labelT = 0;
let dist = 0;
const lastSign = new Map();
const v3 = new THREE.Vector3();
const focus = new THREE.Vector3();
// 초당 프레임을 재서, 느리면 화면 해상도를 조금씩 낮추고 여유가 있으면 다시 올린다
let fpsN = 0, fpsT = 0, ratio = renderer.getPixelRatio();
const MAX_RATIO = ratio;
function adapt(dt) {
  fpsN++;
  fpsT += dt;
  if (fpsT < 2) return;
  const fps = fpsN / fpsT;
  fpsN = fpsT = 0;
  const prev = ratio;
  if (fps < 40 && ratio > 0.55) ratio = Math.max(0.55, ratio - 0.1);
  else if (fps > 57 && ratio < MAX_RATIO) ratio = Math.min(MAX_RATIO, ratio + 0.05);
  if (ratio !== prev) {
    renderer.setPixelRatio(ratio);
    post?.setSize(innerWidth, innerHeight, ratio);
  }
  $('fps').textContent = `${fps.toFixed(0)} fps · 해상도 ${(ratio * 100).toFixed(0)}%`;
}
function tick() {
  const dt = Math.min(0.05, clock.getDelta());
  if (!params.has('fixed')) adapt(dt);
  t += dt;
  me.base = Number($('speed').value);
  for (const r of runners) {
    // 다른 러너는 속도가 조금씩 오르내려서 서로 추월이 생기게
    r.kmh = r.me ? r.base : Math.max(0, r.base + 0.7 * Math.sin(t / 17 + r.base));
    const v = r.kmh / 3.6;
    r.s += v * dt;
    if (r.me) dist += v * dt;
    if (!r.root) continue;
    const p = off(r.s, r.lane);
    r.root.position.set(p.x, 0.05, p.z);
    r.root.rotation.y = Math.atan2(p.dx, p.dz);
    const moving = Math.min(1, v / 1.2);
    r.run.setEffectiveWeight(moving);
    r.idle.setEffectiveWeight(1 - moving);
    r.run.setEffectiveTimeScale(Math.min(3, v / (groundSpeed * (AVATAR_H / MODEL_H)) || 0));
    r.mixer.update(dt);
  }
  // 추월 알림
  for (const r of runners) {
    if (r.me) continue;
    const g = gapOf(r);
    const sign = Math.sign(g);
    const prev = lastSign.get(r);
    if (prev != null && sign !== prev && Math.abs(g) < 20) toast(sign < 0 ? `${r.name}을(를) 추월했어요!` : `${r.name}이(가) 나를 추월했어요`);
    lastSign.set(r, sign);
  }
  labelT -= dt;
  if (labelT <= 0) {
    labelT = 0.5;
    for (const r of runners) if (r.label) {
      const g = gapOf(r);
      r.label.userData.draw(`${r.name} ${g >= 0 ? '+' : ''}${g.toFixed(0)}m`);
    }
  }
  if (toastT > 0 && (toastT -= dt) <= 0) $('toast').style.opacity = 0;

  // 카메라
  const p = off(me.s, me.lane);
  const dir = new THREE.Vector3(p.dx, 0, p.dz).multiplyScalar(lookBack ? -1 : 1);
  if (mode === 'fp') {
    const bob = me.kmh > 0.5 ? 0.03 * Math.sin(t * Math.PI * 2 * 2.8) : 0;
    camera.position.set(p.x, 1.6 + bob, p.z);
    camera.lookAt(v3.set(p.x, 1.45, p.z).addScaledVector(dir, 20));
    focus.set(p.x, 0, p.z).addScaledVector(dir, 25);
    aimShadow(focus, 55);
  } else if (mode === 'tp') {
    camera.position.set(p.x, 2.7, p.z).addScaledVector(dir, -6.5);
    camera.lookAt(v3.set(p.x, 1.35, p.z).addScaledVector(dir, 10));
    focus.set(p.x, 0, p.z).addScaledVector(dir, 22);
    aimShadow(focus, 60);
  } else {
    controls.update();
    focus.copy(controls.target);
    aimShadow(focus, 280);
  }
  if (me.root) me.root.visible = mode !== 'fp';
  updateTrees(mode === 'top' ? v3.copy(camera.position).setY(0) : focus, dt);
  updateGrass(camera.position.x, camera.position.z, dt);
  if (water.material.uniforms?.time) water.material.uniforms.time.value += dt * 0.6;
  else water.material.normalMap?.offset.set(t * 0.004, t * 0.003);

  $('spd').textContent = `${me.kmh.toFixed(1)} km/h`;
  $('dist').textContent = `${(dist / 1000).toFixed(2)} km`;
  $('lap').textContent = `${Math.floor(dist / LAP)}바퀴`;
  $('pace').textContent = me.kmh > 0.5 ? `${Math.floor(60 / me.kmh)}:${String(Math.round((60 / me.kmh % 1) * 60) % 60).padStart(2, '0')} /km` : '–';
  drawMini();
  if (post) post.render(); else renderer.render(scene, camera);
  requestAnimationFrame(tick);
}

$('lapLen').textContent = `한 바퀴 ${Math.round(LAP)}m · 화질 ${QNAME} · 1·2·3 시점, B 뒤 보기`;
addEventListener('resize', () => {
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(innerWidth, innerHeight);
  post?.setSize(innerWidth, innerHeight, renderer.getPixelRatio());
});
await loadRunners();
tick();
