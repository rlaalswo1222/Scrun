// 코스 만들기 화면: 지도에서 출발점을 고르고 거리·모양을 정하면 서버(server.py)가 코스를 만든다.
/* global L */
const $ = (id) => document.getElementById(id);
const DEFAULT_COURSE = { id: null, name: '한강 반포 5km', kind: '편도', length_m: 5000, file: 'data/course.json' };

const map = L.map('map', { zoomControl: false }).setView([37.512, 126.99], 14);
L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
  maxZoom: 19,
  attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
}).addTo(map);

const choice = { length: 5000, mode: 'loop' };
let start = null;
let startMarker = null;
let preview = null;

for (const chip of document.querySelectorAll('.chip')) {
  chip.addEventListener('click', () => {
    const key = chip.dataset.length ? 'length' : 'mode';
    choice[key] = key === 'length' ? Number(chip.dataset.length) : chip.dataset.mode;
    for (const c of document.querySelectorAll(`.chip[data-${key}]`)) c.classList.toggle('on', c === chip);
    resetResult();
  });
}

map.on('click', (e) => {
  start = e.latlng;
  if (startMarker) startMarker.setLatLng(start);
  else startMarker = L.marker(start).addTo(map);
  $('hint').textContent = `출발점: ${start.lat.toFixed(5)}, ${start.lng.toFixed(5)}`;
  $('make').disabled = false;
  resetResult();
});

function resetResult() {
  $('run').hidden = true;
  $('make').hidden = false;
  if (preview) preview.remove();
  preview = null;
}

function setStatus(text, { busy = false, error = false } = {}) {
  $('status').className = error ? 'error' : '';
  $('status').innerHTML = (busy ? '<span class="spin"></span>' : '') + text;
}

// course.json의 미터 좌표(출발점 기준) -> 위경도
function routeLatLngs(course) {
  const [lat0, lon0] = course.origin;
  const ky = (6371000 * Math.PI) / 180;
  const kx = Math.cos((lat0 * Math.PI) / 180) * ky;
  return course.route.map(([x, y]) => [lat0 + y / ky, lon0 + x / kx]);
}

async function showCourse(file, runUrl) {
  const course = await (await fetch(file)).json();
  if (preview) preview.remove();
  const latlngs = routeLatLngs(course);
  preview = L.layerGroup([
    L.polyline(latlngs, { color: '#e8590c', weight: 6, opacity: 0.9 }),
    L.circleMarker(latlngs[0], { radius: 8, color: '#fff', weight: 3, fillColor: '#2b8a3e', fillOpacity: 1 }),
  ]).addTo(map);
  map.fitBounds(L.latLngBounds(latlngs), { padding: [40, 40], paddingBottomRight: [0, 320] });
  setStatus(`${course.name} · ${(course.length_m / 1000).toFixed(2)}km · ${course.kind}`);
  $('run').href = runUrl;
  $('run').hidden = false;
  $('make').hidden = true;
}

async function poll(id) {
  const res = await fetch(`api/courses/${id}`);
  const job = await res.json();
  if (job.status === 'ready') {
    await showCourse(`data/courses/${id}.json`, `index.html?course=${id}`);
    $('make').disabled = false;
    loadList();
  } else if (job.status === 'error') {
    setStatus(job.message, { error: true });
    $('make').disabled = false;
  } else {
    setStatus(job.message || '만드는 중', { busy: true });
    setTimeout(() => poll(id), 2000);
  }
}

$('make').addEventListener('click', async () => {
  if (!start) return;
  $('make').disabled = true;
  setStatus('요청 보내는 중', { busy: true });
  try {
    const res = await fetch('api/courses', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        lat: start.lat, lon: start.lng, length_m: choice.length, mode: choice.mode,
        name: $('name').value.trim() || null,
      }),
    });
    if (!res.ok) throw new Error(res.status === 422 ? '대한민국 안에서 골라 주세요' : `서버 오류 ${res.status}`);
    poll((await res.json()).id);
  } catch (e) {
    setStatus(`${e.message} (server.py가 켜져 있는지 확인하세요)`, { error: true });
    $('make').disabled = false;
  }
});

// 만들어 둔 코스 목록
async function loadList() {
  let courses = [];
  try {
    courses = await (await fetch('api/courses')).json();
  } catch {
    // 서버 없이 파일만 열었을 때는 기본 코스만
  }
  $('list').innerHTML = '';
  for (const c of [DEFAULT_COURSE, ...courses]) {
    const li = document.createElement('li');
    li.innerHTML = `<b></b><br><small>${(c.length_m / 1000).toFixed(1)}km · ${c.kind}${c.created ? ` · ${c.created}` : ''}</small>`;
    li.querySelector('b').textContent = c.name; // 사용자가 지은 이름은 글자로만 넣는다
    li.addEventListener('click', () => {
      $('list').hidden = true;
      showCourse(c.file || `data/courses/${c.id}.json`, c.id ? `index.html?course=${c.id}` : 'index.html');
    });
    $('list').appendChild(li);
  }
}
$('mine').addEventListener('click', () => {
  $('list').hidden = !$('list').hidden;
});
loadList();
