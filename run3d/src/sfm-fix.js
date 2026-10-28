// 길 직접 찍기: 3D 앞길이 틀린 장면마다 수평선 2점 + 길 한가운데 여러 점을 찍어 서버에 저장한다.
// 좌표는 3D 계산과 같은 960×540 화면 픽셀. tools/sfm_video.py가 합칠 때 땅 위 3D 앞길로 바꾼다
const VIDEO_ID = new URLSearchParams(location.search).get('v') || 'o0EmQXATQQI';
const $ = (id) => document.getElementById(id);
const W = 960;
const H = 540;
const base = `assets/video/${VIDEO_ID}.fix/`;

const index = await fetch(`${base}index.json`).then((r) => r.json());
const saved = await fetch(`/api/sfm-fix/${VIDEO_ID}`).then((r) => r.json()).catch(() => ({ frames: {} }));
const marks = saved.frames || {};
const frames = index.frames;
let cur = Math.max(0, frames.findIndex((f) => !isDone(marks[key(f)])));
if (cur < 0) cur = 0;

function key(f) { return f.t.toFixed(1); }
function isDone(m) { return m && m.horizon?.length === 2 && m.path?.length >= 2; }
function mark() {
  const k = key(frames[cur]);
  marks[k] ??= { horizon: [], path: [] };
  return marks[k];
}

const cv = $('cv');
const ctx = cv.getContext('2d');
function draw() {
  ctx.clearRect(0, 0, W, H);
  const m = marks[key(frames[cur])] || { horizon: [], path: [] };
  // 수평선
  ctx.lineWidth = 3;
  ctx.strokeStyle = '#5ab0ff';
  ctx.fillStyle = '#5ab0ff';
  if (m.horizon.length === 2) {
    const [[x1, y1], [x2, y2]] = m.horizon;
    const k = (y2 - y1) / ((x2 - x1) || 1e-6);
    ctx.beginPath();
    ctx.moveTo(0, y1 - k * x1);
    ctx.lineTo(W, y1 + k * (W - x1));
    ctx.stroke();
  }
  for (const [x, y] of m.horizon) { ctx.beginPath(); ctx.arc(x, y, 6, 0, 7); ctx.fill(); }
  // 길
  ctx.strokeStyle = '#ff7a1a';
  ctx.fillStyle = '#ff7a1a';
  ctx.beginPath();
  m.path.forEach(([x, y], i) => (i ? ctx.lineTo(x, y) : ctx.moveTo(x, y)));
  ctx.stroke();
  m.path.forEach(([x, y], i) => {
    ctx.beginPath(); ctx.arc(x, y, 7, 0, 7); ctx.fill();
    ctx.fillStyle = '#fff'; ctx.font = 'bold 12px system-ui'; ctx.fillText(String(i + 1), x - 4, y + 4); ctx.fillStyle = '#ff7a1a';
  });
  const step = $('step');
  if (m.horizon.length < 2) {
    step.className = '';
    step.textContent = `① 수평선 ${m.horizon.length === 0 ? '왼쪽' : '오른쪽'} 점을 클릭하세요`;
  } else if (m.path.length < 4) {
    step.className = 'path';
    step.textContent = `② 길 한가운데를 가까운 곳부터 클릭하세요 (${m.path.length}/4점 이상)`;
  } else {
    step.className = 'done';
    step.textContent = `좋아요 (${m.path.length}점). 더 멀리 찍거나 '다음'을 누르세요`;
  }
  $('pos').textContent = `${cur + 1} / ${frames.length} · 영상 ${Math.floor(frames[cur].t / 60)}:${String(Math.floor(frames[cur].t % 60)).padStart(2, '0')}`;
  $('prev').disabled = cur === 0;
  $('copyH').disabled = cur === 0 || !marks[key(frames[cur - 1])]?.horizon?.length;
  [...$('strip').children].forEach((s, i) => {
    s.className = (isDone(marks[key(frames[i])]) ? 'done' : '') + (i === cur ? ' cur' : '');
  });
  const done = frames.filter((f) => isDone(marks[key(f)])).length;
  $('status').textContent = `${done} / ${frames.length}장 완료`;
}

function show(i) {
  cur = Math.max(0, Math.min(frames.length - 1, i));
  $('img').src = base + frames[cur].img;
  draw();
}

let saveTimer = null;
function save() {
  clearTimeout(saveTimer);
  saveTimer = setTimeout(async () => {
    try {
      const r = await fetch(`/api/sfm-fix/${VIDEO_ID}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ frames: marks }),
      });
      if (!r.ok) throw new Error(r.status);
      $('status').textContent += ' · 저장됨';
    } catch (e) {
      $('status').textContent += ` · 저장 실패(${e.message}) — 서버가 켜져 있는지 확인`;
    }
  }, 400);
}

cv.addEventListener('pointerdown', (e) => {
  const r = cv.getBoundingClientRect();
  const x = Math.round(((e.clientX - r.left) / r.width) * W);
  const y = Math.round(((e.clientY - r.top) / r.height) * H);
  const m = mark();
  if (m.horizon.length < 2) m.horizon.push([x, y]);
  else m.path.push([x, y]);
  draw();
  save();
});
$('undo').onclick = () => {
  const m = mark();
  if (m.path.length) m.path.pop(); else m.horizon.pop();
  draw(); save();
};
$('reset').onclick = () => { marks[key(frames[cur])] = { horizon: [], path: [] }; draw(); save(); };
$('copyH').onclick = () => {
  const prev = marks[key(frames[cur - 1])];
  if (!prev?.horizon?.length) return;
  mark().horizon = prev.horizon.map((p) => [...p]);
  draw(); save();
};
$('prev').onclick = () => show(cur - 1);
$('next').onclick = () => show(cur + 1);
addEventListener('keydown', (e) => {
  if (e.key === 'ArrowRight') show(cur + 1);
  if (e.key === 'ArrowLeft') show(cur - 1);
  if ((e.ctrlKey || e.metaKey) && e.key === 'z') $('undo').onclick();
});
frames.forEach((_, i) => {
  const s = document.createElement('span');
  s.title = `${i + 1}`;
  s.onclick = () => show(i);
  $('strip').append(s);
});
show(cur);
