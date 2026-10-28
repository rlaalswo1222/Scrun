// 가림막 찍기: 장면에서 장애물을 클릭하면 서버가 SAM 2로 앞뒤 장면을 따라가며 윤곽을 따고, 깊이 지도에 새긴다.
const params = new URLSearchParams(location.search);
const VIDEO_ID = params.get('v') || 'o0EmQXATQQI';
const $ = (id) => document.getElementById(id);
const COLORS = ['#ff3b3b', '#3bb0ff', '#ffd23b', '#b06bff', '#3dff8c', '#ff8a3b'];
let t = Number(params.get('t')) || 723;
let clicks = [];
let objects = [];
const maskImg = new Map(); // `${id}@${t}` -> Image

const cv = $('cv');
const ctx = cv.getContext('2d');
const tmp = document.createElement('canvas');
tmp.width = 480;
tmp.height = 270;
const tctx = tmp.getContext('2d');

const fmt = (s) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, '0')}${s % 1 ? `.${String(Math.round((s % 1) * 100)).padStart(2, '0')}` : ''}`;

function setTime(v) {
  t = Math.max(120, Math.min(1860, Math.round(v * 4) / 4));
  $('slider').value = t;
  $('time').textContent = fmt(t);
  $('img').src = `/api/frame/${VIDEO_ID}?t=${t}`;
  clicks = [];
  draw();
}

function draw() {
  ctx.clearRect(0, 0, 960, 540);
  objects.forEach((o, k) => {
    if (o.status !== 'done' || t < o.t_from - 0.13 || t > o.t_to + 0.13) return;
    const key = `${o.id}@${t}`;
    let im = maskImg.get(key);
    if (!im) {
      im = new Image();
      im.onload = draw;
      im.src = `/api/occluders/${VIDEO_ID}/${o.id}/mask?t=${t}`;
      maskImg.set(key, im);
      return;
    }
    if (!im.complete || !im.naturalWidth) return;
    // 흰색 윤곽을 물체 색으로 반투명하게
    tctx.clearRect(0, 0, 480, 270);
    tctx.drawImage(im, 0, 0);
    const d = tctx.getImageData(0, 0, 480, 270);
    const [r, g, b] = COLORS[k % COLORS.length].match(/\w\w/g).map((h) => parseInt(h, 16));
    for (let i = 0; i < d.data.length; i += 4) {
      const on = d.data[i] > 127;
      d.data[i] = r; d.data[i + 1] = g; d.data[i + 2] = b; d.data[i + 3] = on ? 110 : 0;
    }
    tctx.putImageData(d, 0, 0);
    ctx.drawImage(tmp, 0, 0, 960, 540);
  });
  for (const [x, y, pos] of clicks) {
    ctx.fillStyle = pos ? '#3dff8c' : '#ff3b3b';
    ctx.strokeStyle = '#000';
    ctx.lineWidth = 2;
    ctx.beginPath(); ctx.arc(x, y, 8, 0, 7); ctx.fill(); ctx.stroke();
    ctx.fillStyle = '#000';
    ctx.fillRect(x - 4, y - 1, 8, 2);
    if (pos) ctx.fillRect(x - 1, y - 4, 2, 8);
  }
  $('go').disabled = !clicks.some((c) => c[2] === 1);
}

function point(e) {
  const r = cv.getBoundingClientRect();
  return [Math.round(((e.clientX - r.left) / r.width) * 960), Math.round(((e.clientY - r.top) / r.height) * 540)];
}
cv.addEventListener('click', (e) => { clicks.push([...point(e), 1]); draw(); });
cv.addEventListener('contextmenu', (e) => { e.preventDefault(); clicks.push([...point(e), 0]); draw(); });
$('clear').onclick = () => { clicks = []; draw(); };
$('slider').oninput = () => setTime(Number($('slider').value));
$('back').onclick = () => setTime(t - 0.25);
$('fwd').onclick = () => setTime(t + 0.25);
$('back1').onclick = () => setTime(t - 1);
$('fwd1').onclick = () => setTime(t + 1);
addEventListener('keydown', (e) => {
  if (e.key === 'ArrowRight') setTime(t + (e.shiftKey ? 1 : 0.25));
  if (e.key === 'ArrowLeft') setTime(t - (e.shiftKey ? 1 : 0.25));
});
$('go').onclick = async () => {
  $('go').disabled = true;
  try {
    const r = await fetch(`/api/occluders/${VIDEO_ID}`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ t, points: clicks }),
    });
    if (!r.ok) throw new Error(await r.text());
    $('msg').textContent = `${fmt(t)} 물체를 따라가는 중이에요. 다른 물체를 계속 찍어도 돼요.`;
    clicks = [];
    refresh();
  } catch (err) {
    $('msg').textContent = `보내지 못했어요: ${err.message}`;
  }
  draw();
};

async function refresh() {
  try {
    objects = await fetch(`/api/occluders/${VIDEO_ID}`).then((r) => r.json());
  } catch { return; }
  const list = $('list');
  list.replaceChildren();
  objects.forEach((o, k) => {
    const el = document.createElement('div');
    el.className = 'item';
    const label = { queued: '대기 중', running: `따라가는 중 ${Math.round((o.progress || 0) * 100)}%`, done: '완료', empty: '못 찾음', error: '오류' }[o.status] || o.status;
    const span = o.t_from != null ? `${fmt(o.t_from)} ~ ${fmt(o.t_to)}` : `${fmt(o.t)}부터`;
    el.innerHTML = `<span class="sw" style="background:${COLORS[k % COLORS.length]}"></span><span class="st ${o.status}">${label}</span><span>${span}</span>`;
    const view = document.createElement('button');
    view.textContent = '보기';
    view.onclick = () => setTime(o.t);
    const del = document.createElement('button');
    del.textContent = '삭제';
    del.onclick = async () => { await fetch(`/api/occluders/${VIDEO_ID}/${o.id}`, { method: 'DELETE' }); refresh(); };
    el.append(view, del);
    if (o.error) el.append(Object.assign(document.createElement('span'), { textContent: o.error, style: 'color:var(--bad)' }));
    list.append(el);
  });
  draw();
}
setInterval(refresh, 3000);
setTime(t);
refresh();
