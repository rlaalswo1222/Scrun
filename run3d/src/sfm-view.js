// 3D 복원 뷰어: tools/sfm_video.py --export-view 결과(점구름·카메라 경로)와 sfm.json(장면별 앞길)을 보여준다.
// 시간 막대 아래 띠: 초록 = 앞길 80m, 노랑 = 짧음, 빨강 = 3D 없음(예전 방식으로 그림)
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

const params = new URLSearchParams(location.search);
const VIDEO_ID = params.get('v') || 'o0EmQXATQQI';
const $ = (id) => document.getElementById(id);

const renderer = new THREE.WebGLRenderer({ canvas: $('view'), antialias: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x0d1117);
const camera = new THREE.PerspectiveCamera(55, 1, 0.1, 5000);
camera.position.set(-30, 40, 30);
const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
function resize() {
  renderer.setSize(innerWidth, innerHeight, false);
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
}
addEventListener('resize', resize);
resize();

const [index, sfm] = await Promise.all([
  fetch(`assets/video/${VIDEO_ID}.sfmview/index.json`).then((r) => r.json()),
  fetch(`assets/video/${VIDEO_ID}.sfm.json`).then((r) => r.json()),
]);
const fps = sfm.fps;
const frameCount = sfm.frames.length;
$('slider').max = frameCount - 1;

// 장면 번호 -> (묶음, 묶음 안 번호)
const where = new Array(frameCount).fill(null);
index.segments.forEach((g, n) => {
  g.keys.forEach((k, j) => {
    const i = k - Math.round(sfm.t0 * fps);
    if (i >= 0 && i < frameCount && !where[i]) where[i] = [n, j]; // 앞쪽 묶음(이어 붙인 본 경로) 우선
  });
});

// 시간 막대 아래 품질 띠
const strip = $('strip');
strip.width = frameCount;
const sctx = strip.getContext('2d');
sfm.frames.forEach((rows, i) => {
  const reach = rows.length * sfm.stepM;
  sctx.fillStyle = reach >= 78 ? '#3ddc84' : reach > 0 ? '#e8c547' : '#e25b3a';
  sctx.fillRect(i, 0, 1, 1);
});
const good = sfm.frames.filter((r) => r.length * sfm.stepM >= 78).length;
const any = sfm.frames.filter((r) => r.length > 0).length;

// 높이(위 = +y) 색: 파랑 -> 초록 -> 노랑 -> 빨강
const ramp = [[0.18, 0.37, 0.82], [0.21, 0.76, 0.42], [0.91, 0.85, 0.29], [0.89, 0.36, 0.23]];
function heightColor(u, out, o) {
  const x = Math.min(0.999, Math.max(0, u)) * (ramp.length - 1);
  const k = Math.floor(x);
  const f = x - k;
  for (let c = 0; c < 3; c++) out[o + c] = ramp[k][c] + (ramp[k + 1][c] - ramp[k][c]) * f;
}

const loaded = new Map();
async function segmentObject(n) {
  if (loaded.has(n)) return loaded.get(n);
  const g = index.segments[n];
  const buf = await fetch(`assets/video/${VIDEO_ID}.sfmview/${g.points}`).then((r) => r.arrayBuffer());
  const pos = new Float32Array(buf);
  // 색은 카메라 경로 높이 기준 (길 바닥 근처 = 초록, 아래 = 파랑, 위 = 노랑·빨강)
  const pathY = g.C.map((c) => c[1]).sort((a, b) => a - b);
  const groundY = pathY[pathY.length >> 1] - 1.2;
  const col = new Float32Array(pos.length);
  for (let i = 0; i < pos.length; i += 3) heightColor((pos[i + 1] - groundY + 1) / 9, col, i);
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
  const points = new THREE.Points(geo, new THREE.PointsMaterial({ size: 0.25, vertexColors: true, sizeAttenuation: true }));
  const path = new THREE.Line(new THREE.BufferGeometry().setFromPoints(g.C.map((c) => new THREE.Vector3(...c))),
    new THREE.LineBasicMaterial({ color: 0xffffff }));
  const group = new THREE.Group();
  group.add(points, path);
  const entry = { group, g };
  loaded.set(n, entry);
  return entry;
}

// 지금 카메라(작은 사각뿔)와 앞길
const camMarker = new THREE.Mesh(new THREE.ConeGeometry(0.6, 1.4, 4),
  new THREE.MeshBasicMaterial({ color: 0xff7a1a, wireframe: true }));
scene.add(camMarker);
const aheadGeo = new THREE.BufferGeometry();
const ahead = new THREE.Points(aheadGeo, new THREE.PointsMaterial({ color: 0x3ddc84, size: 0.7, sizeAttenuation: true }));
scene.add(ahead);

let current = -1;
let shown = null;
const tmp = new THREE.Vector3();
async function show(i) {
  current = i;
  const t = sfm.t0 + i / fps;
  $('time').textContent = `${Math.floor(t / 60)}:${String(Math.floor(t % 60)).padStart(2, '0')}`;
  const w = where[i];
  const rows = sfm.frames[i];
  if (!w) {
    $('info').textContent = `이 장면은 3D 복원 경로 밖이에요 (예전 방식으로 그림). 전체 ${frameCount}장면 중 앞길 80m ${good} · 3D 있음 ${any}`;
    ahead.visible = camMarker.visible = false;
    return;
  }
  const [n, j] = w;
  const entry = await segmentObject(n);
  if (current !== i) return;
  if (shown !== entry) {
    if (shown) scene.remove(shown.group);
    scene.add(entry.group);
    shown = entry;
  }
  const C = new THREE.Vector3(...entry.g.C[j]);
  const M = new THREE.Matrix3().fromArray(entry.g.M[j]).transpose(); // 행 우선 -> three.js 열 우선
  camMarker.visible = true;
  camMarker.position.copy(C);
  // 사각뿔 꼭짓점이 카메라 앞(-z)을 향하게
  const fwd = new THREE.Vector3(0, 0, 1).applyMatrix3(M).normalize();
  camMarker.quaternion.setFromUnitVectors(new THREE.Vector3(0, -1, 0), fwd);
  const pts = rows.map((r) => tmp.set(r[0], -r[1], -r[2]).applyMatrix3(M).add(C).clone());
  aheadGeo.setFromPoints(pts);
  ahead.visible = pts.length > 0;
  const reach = rows.length * sfm.stepM;
  $('info').textContent = `묶음 ${n + 1}/${index.segments.length} · 앞길 ${reach}m${reach ? '' : ' (3D 없음, 예전 방식)'}`
    + ` · 점 ${entry.g.count.toLocaleString()}개 · 전체 ${frameCount}장면 중 앞길 80m ${good} (${Math.round(good / frameCount * 100)}%), 3D 있음 ${any} (${Math.round(any / frameCount * 100)}%)`;
  if (follow) {
    const move = C.clone().sub(controls.target);
    controls.target.add(move);
    camera.position.add(move);
  }
  if (camView) {
    camera.position.copy(C);
    controls.target.copy(C.clone().add(fwd.clone().multiplyScalar(10)));
  }
}

let follow = true;
let camView = false;
let playing = false;
$('slider').oninput = () => show(Number($('slider').value));
$('follow').onclick = () => { follow = !follow; $('follow').classList.toggle('on', follow); };
$('cam').onclick = () => {
  camView = !camView;
  $('cam').classList.toggle('on', camView);
  if (!camView && shown) camera.position.add(new THREE.Vector3(-14, 16, 14));
  show(current);
};
$('play').onclick = () => { playing = !playing; $('play').textContent = playing ? '멈춤' : '재생'; };
strip.onclick = (e) => {
  const i = Math.round((e.offsetX / strip.clientWidth) * (frameCount - 1));
  $('slider').value = i;
  show(i);
};

// ?t=초 로 시작 장면 지정 (예: sfm-view.html?t=712 = 11:52)
const start = Math.max(0, Math.min(frameCount - 1, Math.round((Number(params.get('t')) || sfm.t0) - sfm.t0) * fps));
$('slider').value = start;
await show(start);
if (shown) {
  controls.target.copy(camMarker.position);
  camera.position.copy(camMarker.position).add(new THREE.Vector3(-14, 16, 14));
}
let last = performance.now();
let acc = 0;
renderer.setAnimationLoop((now) => {
  const dt = (now - last) / 1000;
  last = now;
  if (playing) {
    acc += dt * fps * 4; // 4배속
    if (acc >= 1) {
      const next = Math.min(frameCount - 1, current + Math.floor(acc));
      acc %= 1;
      $('slider').value = next;
      show(next);
    }
  }
  controls.update();
  renderer.render(scene, camera);
});
