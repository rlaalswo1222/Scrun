import * as THREE from 'three';
import { CoursePath } from './course.js';
import { buildWorld } from './world.js';
import { createRunner, loadRunnerModel } from './runner.js';
import { BotSource, gaitOf, ManualSource, Race } from './race.js';
import { fmtTime, Hud } from './hud.js';
import { createPost } from './post.js';
import { Q } from './quality.js';
import { createDebug } from './debug.js';
import { createAdaptive } from './adaptive.js';

const params = new URLSearchParams(location.search);
// ?course=ID 면 지도에서 만든 코스, 없으면 기본 코스(한강 반포 5km)
const courseId = params.get('course');
const courseFile = /^[0-9a-f]{10}$/.test(courseId ?? '') ? `data/courses/${courseId}.json` : 'data/course.json';
const [course] = await Promise.all([
  fetch(courseFile).then((r) => r.json()),
  loadRunnerModel('assets/Xbot.glb'),
]);
const path = new CoursePath(course.route, course.length_m);

const canvas = document.getElementById('scene');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'low-power' });
renderer.setPixelRatio(Math.min(window.devicePixelRatio, Q.pixelRatio));
renderer.toneMapping = THREE.ACESFilmicToneMapping; // 사진 같은 색감
renderer.toneMappingExposure = Number(params.get('exposure')) || 0.75;
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(60, 1, 0.5, 3000);
const world = buildWorld(scene, course, path, renderer, Q);
const post = createPost(renderer, scene, camera, Q); // 화질 low면 null (후처리 없이 바로 그림)

// 나 + 가짜 러너 3명. lane은 코스 가운데에서 오른쪽(+)/왼쪽(-)으로 비켜선 거리(m)
const me = new ManualSource(Number(params.get('speed')) || 9);
const race = new Race(course.length_m, [
  { id: 'me', name: '나', color: '#e8590c', isMe: true, source: me, lane: 0.6 },
  { id: 'b1', name: '민지', color: '#1c7ed6', source: new BotSource(9.6, 1), lane: -0.6 },
  { id: 'b2', name: '준호', color: '#2f9e44', source: new BotSource(10.4, 2), lane: 1.6 },
  { id: 'b3', name: '서연', color: '#ae3ec9', source: new BotSource(8.6, 3), lane: -1.6 },
]);
const avatars = new Map(race.runners.map((r) => {
  const a = createRunner(r.color, r.isMe ? null : r.name);
  scene.add(a.root);
  return [r.id, a];
}));
const hud = new Hud(race, path, course);
const debug = createDebug(renderer, race); // ?debug=1 이면 성능 정보 표시

// 조작: 키보드 ↑↓ 또는 화면 버튼 (실제 앱에서는 센서 추정 속도로 대체)
const $ = (id) => document.getElementById(id);
const startRace = () => {
  race.start();
  $('start').hidden = true;
  $('pick').hidden = true;
};
$('start').onclick = startRace;
$('faster').onclick = () => me.change(+0.5);
$('slower').onclick = () => me.change(-0.5);
window.addEventListener('keydown', (e) => {
  if (e.key === 'ArrowUp' || e.key === '+' || e.key === '=') me.change(+0.5);
  else if (e.key === 'ArrowDown' || e.key === '-') me.change(-0.5);
  else if (e.key === ' ' || e.key === 'Enter') startRace();
});

// ?skip=초: 바로 시작해서 그 시간만큼 달린 상태로 건너뛰기 (시연·화면 확인용)
if (params.has('skip')) {
  startRace();
  race.state = 'running';
  for (let t = 0; t < Number(params.get('skip')); t += 0.1) race.update(0.1);
}

function resize() {
  const w = window.innerWidth;
  const h = window.innerHeight;
  renderer.setSize(w, h, false);
  post?.setSize(w, h, renderer.getPixelRatio());
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
  hud.setupMinimap();
}
window.addEventListener('resize', resize);
resize();

const maxPixelRatio = Math.min(window.devicePixelRatio, Q.pixelRatio);
const adapt = createAdaptive(renderer, { min: 0.75, max: maxPixelRatio, onChange: resize });

// 3인칭 카메라: 내 뒤 위쪽에서 앞을 본다. 부드럽게 따라가고 위아래로 흔들지 않는다(멀미 방지)
const camPos = new THREE.Vector3();
const camLook = new THREE.Vector3();
const dir = new THREE.Vector3(0, 0, -1);
let cameraReady = false;
// ?cam=x,y,z,보는x,보는y,보는z : 카메라를 고정 (화면 확인·시연용)
const fixedCam = params.get('cam')?.split(',').map(Number);
function updateCamera(dt) {
  if (fixedCam) {
    camera.position.set(fixedCam[0], fixedCam[1], fixedCam[2]);
    camera.lookAt(fixedCam[3], fixedCam[4], fixedCam[5]);
    return;
  }
  const r = race.me;
  const here = path.frame(r.distance, r.lane);
  // 뒤 10m ~ 앞 15m 방향의 평균: 급커브나 결승선 근처에서도 카메라가 홱 돌지 않게
  const behind = path.frame(r.distance - 10, r.lane);
  const ahead = path.frame(r.distance + 15, r.lane, 8);
  const dx = ahead.x - behind.x;
  const dz = ahead.z - behind.z;
  const n = Math.hypot(dx, dz);
  if (n > 1) dir.set(dx / n, 0, dz / n);
  const targetPos = new THREE.Vector3(here.x - dir.x * 6.5, 3.2, here.z - dir.z * 6.5);
  const targetLook = new THREE.Vector3(here.x + dir.x * 10, 1.2, here.z + dir.z * 10);
  const k = cameraReady ? 1 - Math.exp(-dt * 3) : 1;
  camPos.lerp(targetPos, k);
  camLook.lerp(targetLook, k);
  cameraReady = true;
  camera.position.copy(camPos);
  camera.lookAt(camLook);
}

function updateOverlay() {
  const overlay = $('overlay');
  if (race.state === 'countdown') {
    overlay.hidden = false;
    overlay.innerHTML = `<div class="count">${Math.ceil(race.countdown)}</div>`;
  } else if (race.state === 'finished') {
    if (overlay.dataset.shown) return;
    overlay.dataset.shown = '1';
    const rank = race.standings().indexOf(race.me) + 1;
    overlay.hidden = false;
    overlay.innerHTML = `<div class="card"><h2>완주!</h2>`
      + `<p class="big">${fmtTime(race.me.finishTime)}</p>`
      + `<p>${race.runners.length}명 중 ${rank}위</p>`
      + `<button onclick="location.reload()">다시 달리기</button></div>`;
  } else {
    overlay.hidden = true;
  }
}

// 배터리를 아끼려고 초당 30프레임으로 제한 (?cap=0 이면 제한 없음: 폰이 최대 몇 fps까지 버티는지 볼 때)
const minFrame = params.get('cap') === '0' ? 0 : 1 / 31;
let last = performance.now();
renderer.setAnimationLoop((now) => {
  const elapsed = (now - last) / 1000;
  if (elapsed < minFrame) return;
  last = now;
  adapt(elapsed);
  const dt = Math.min(elapsed, 0.1);
  race.update(dt);
  for (const r of race.runners) {
    const a = avatars.get(r.id);
    const f = path.frame(r.distance, r.lane);
    a.root.position.set(f.x, 0, f.z);
    a.root.rotation.y = Math.atan2(f.tx, f.tz);
    a.animate(dt, r.cadence, gaitOf(r.speedKmh));
  }
  updateCamera(dt);
  world.update(race.me.distance, dt);
  hud.update(now);
  updateOverlay();
  debug?.beginFrame();
  if (post) post.render();
  else renderer.render(scene, camera);
  debug?.endFrame(now);
});
