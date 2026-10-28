// 갤럭시 기록기: 가속도(진동)와 전면 카메라 머리 위치를 노트북 시계에 맞춰 0.5초마다 보낸다.
// 크롬은 HTTPS 주소에서만 가속도·카메라를 준다 -> https://<노트북 IP>:8443/speed-galaxy.html
const $ = (id) => document.getElementById(id);
const SEND_MS = 500;
let offset = 0; // 노트북 시계 - 이 폰 시계 (초)
let bestRtt = Infinity;
let runId = null;
let running = false;
let soloRun = null; // 갤럭시만으로 만든 기록 (멈출 때 끝낸다)
const queue = { galaxy_acc: [], galaxy_cam: [] };
const counts = { acc: 0, cam: 0, sent: 0, camLost: 0 };
const nowAbs = () => (performance.timeOrigin + performance.now()) / 1000 + offset;

async function syncClock() {
  for (let i = 0; i < 5; i++) {
    const a = (performance.timeOrigin + performance.now()) / 1000;
    try {
      const { t } = await fetch('/api/speed/clock', { cache: 'no-store' }).then((r) => r.json());
      const b = (performance.timeOrigin + performance.now()) / 1000;
      if (b - a < bestRtt) { bestRtt = b - a; offset = t - (a + b) / 2; }
      $('net').innerHTML = `<span class="ok">연결됨</span> · 왕복 ${(bestRtt * 1000).toFixed(0)}ms`;
    } catch {
      $('net').innerHTML = '<span class="bad">노트북에 연결 안 됨</span>';
      return;
    }
  }
}

async function findRun() {
  if (soloRun) { runId = soloRun; $('run').innerHTML = `<span class="ok">${soloRun}</span> · 갤럭시만`; return; }
  try {
    const r = await fetch('/api/speed/runs/current', { cache: 'no-store' });
    if (!r.ok) { $('run').innerHTML = '<span class="warn">아이폰에서 기록을 시작하세요</span>'; runId = null; return; }
    const m = await r.json();
    if (m.finished) { $('run').innerHTML = '<span class="warn">새 기록을 기다리는 중</span>'; runId = null; return; }
    if (runId !== m.id) { runId = m.id; }
    const cur = (m.segments || []).at(-1);
    $('run').innerHTML = `<span class="ok">${m.id}</span>${cur && cur.end_s == null ? ` · ${cur.label}` : ''}`;
  } catch { /* 다음에 다시 */ }
}

async function flush() {
  if (!runId) return;
  const batches = [];
  if (queue.galaxy_acc.length) batches.push({ source: 'galaxy_acc', columns: ['x', 'y', 'z'], rows: queue.galaxy_acc.splice(0) });
  if (queue.galaxy_cam.length) batches.push({ source: 'galaxy_cam', columns: ['nose_y', 'shoulder_y'], rows: queue.galaxy_cam.splice(0) });
  if (!batches.length) return;
  try {
    const r = await fetch(`/api/speed/runs/${runId}/samples`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(batches) });
    if (!r.ok) throw new Error(r.status);
    counts.sent += batches.reduce((a, b) => a + b.rows.length, 0);
    $('sent').textContent = counts.sent.toLocaleString();
  } catch {
    // 못 보낸 건 다시 앞에 넣어 다음에 보낸다 (노트북 연결이 잠깐 끊겨도 잃지 않게)
    for (const b of batches) queue[b.source].unshift(...b.rows);
    $('net').innerHTML = '<span class="bad">보내기 실패 · 다시 시도 중</span>';
  }
}

function onMotion(e) {
  if (!running) return;
  const a = e.accelerationIncludingGravity;
  if (!a || a.x == null) return;
  queue.galaxy_acc.push([nowAbs(), a.x, a.y, a.z]);
  counts.acc++;
}

// ---- 카메라: MediaPipe 포즈로 코·어깨 높이 (일꾼 스레드에서: 가속도 이벤트가 버려지지 않게) ----
let video = null;
let worker = null;
async function startCamera() {
  video = $('video');
  video.srcObject = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user', width: 640, height: 480, frameRate: 30 }, audio: false });
  await video.play();
  $('preview').hidden = false;
  // 일반(classic) 일꾼: MediaPipe가 wasm 로더를 importScripts로 불러서 module 일꾼에서는 안 된다
  worker = new Worker(new URL('./pose-worker.js', import.meta.url));
  const base = new URL('../', import.meta.url).href;
  let busyFrame = false;
  const ctx = $('overlay').getContext('2d');
  worker.onmessage = (e) => {
    const m = e.data;
    if (m.type === 'error') { $('cam').innerHTML = `<span class="bad">포즈 인식 실패: ${m.message}</span>`; return; }
    if (m.type !== 'pose') return;
    busyFrame = false;
    const cv = $('overlay');
    cv.width = video.videoWidth; cv.height = video.videoHeight;
    ctx.clearRect(0, 0, cv.width, cv.height);
    if (m.nose == null) { counts.camLost++; return; }
    queue.galaxy_cam.push([m.t, m.nose, m.shoulder]);
    counts.cam++;
    ctx.fillStyle = '#3ddc84';
    for (const [x, y] of m.pts) { ctx.beginPath(); ctx.arc(x * cv.width, y * cv.height, 7, 0, 7); ctx.fill(); }
  };
  await new Promise((resolve) => {
    const once = (e) => { if (e.data.type === 'ready' || e.data.type === 'error') { worker.removeEventListener('message', once); resolve(); } };
    worker.addEventListener('message', once);
    worker.postMessage({ type: 'init', base });
  });
  let lastVt = -1;
  const loop = async () => {
    if (!running) return;
    // 일꾼이 앞 장면을 처리 중이면 이번 장면은 건너뛴다 (밀리지 않게)
    if (!busyFrame && video.readyState >= 2 && video.currentTime !== lastVt) {
      lastVt = video.currentTime;
      busyFrame = true;
      const t = nowAbs();
      const bitmap = await createImageBitmap(video, { resizeWidth: 320, resizeHeight: 240 });
      worker.postMessage({ type: 'frame', bitmap, ts: performance.now(), t }, [bitmap]);
    }
    requestAnimationFrame(loop);
  };
  requestAnimationFrame(loop);
}

let wake = null;
let last = { acc: 0, cam: 0, t: performance.now() };
function status() {
  const dt = (performance.now() - last.t) / 1000;
  const ra = (counts.acc - last.acc) / dt;
  const rc = (counts.cam - last.cam) / dt;
  last = { acc: counts.acc, cam: counts.cam, t: performance.now() };
  if ($('optAcc').checked) $('acc').innerHTML = running ? (ra > 20 ? `<span class="ok">${ra.toFixed(0)} Hz</span>` : '<span class="bad">안 들어옴</span>') : '–';
  if ($('optCam').checked) $('cam').innerHTML = running ? (rc > 5 ? `<span class="ok">${rc.toFixed(0)} fps</span>` : '<span class="warn">사람을 못 찾음 — 상반신이 화면에 나오게</span>') : '–';
}

$('go').onclick = async () => {
  if (running) {
    running = false;
    window.removeEventListener('devicemotion', onMotion);
    video?.srcObject?.getTracks().forEach((t) => t.stop());
    worker?.terminate();
    worker = null;
    wake?.release();
    await flush();
    if (soloRun) {
      await fetch(`/api/speed/runs/${soloRun}/finish`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ t: nowAbs() }) }).catch(() => {});
      $('run').innerHTML = `<span class="ok">${soloRun}</span> · 끝 (분석 화면에서 보세요)`;
      soloRun = null;
    }
    $('go').textContent = '기록 시작';
    $('go').classList.remove('stop');
    return;
  }
  if (!window.isSecureContext) {
    alert('HTTPS 주소(https://노트북IP:8443/speed-galaxy.html)로 열어야 센서와 카메라를 쓸 수 있어요.');
    return;
  }
  if ($('optSolo').checked) {
    try {
      const r = await fetch('/api/speed/runs', { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ height_cm: 175, stages: [], device: 'Galaxy only' }) }).then((x) => x.json());
      soloRun = r.id;
      runId = r.id;
      // 구간 하나(자유 기록)로 시작: 분석 화면 그래프에 표시된다
      await fetch(`/api/speed/runs/${soloRun}/stage`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ index: 0, label: '자유 기록', speed_kmh: null, set: 'warmup', t: nowAbs() }) });
      $('run').innerHTML = `<span class="ok">${soloRun}</span> · 갤럭시만`;
    } catch {
      $('run').innerHTML = '<span class="bad">기록을 만들지 못했어요</span>';
      return;
    }
  }
  running = true;
  $('go').textContent = '기록 멈춤';
  $('go').classList.add('stop');
  try { wake = await navigator.wakeLock.request('screen'); } catch { /* 화면 꺼짐 방지 실패해도 계속 */ }
  if ($('optAcc').checked) window.addEventListener('devicemotion', onMotion);
  if ($('optCam').checked) startCamera().catch((e) => { $('cam').innerHTML = `<span class="bad">카메라 실패: ${e.message}</span>`; });
};

await syncClock();
findRun();
setInterval(findRun, 3000);
setInterval(syncClock, 15000);
setInterval(flush, SEND_MS);
setInterval(status, 1000);
