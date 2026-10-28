// 속도 테스트 분석 화면: 0.5초마다 실시간 걸음 수·추정 속도를 받아 그리고, 기록이 끝나면 전체 분석을 보여준다.
// ?run=current(기본)|<id>  ?compact=1 (아이폰 기록 앱 안에 띄울 때: 실시간만)
const params = new URLSearchParams(location.search);
if (params.get('compact') === '1') document.body.classList.add('compact');
const $ = (id) => document.getElementById(id);
const css = (name) => getComputedStyle(document.documentElement).getPropertyValue(name).trim() || '#aaa';
const color = (src) => css(`--s-${src}`);
const fmtT = (s) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, '0')}`;
const qClass = (q) => (q >= 0.6 ? 'ok' : q >= 0.35 ? 'warn' : 'bad');
const qText = (q) => (q >= 0.6 ? '좋음' : q >= 0.35 ? '보통' : '나쁨');
let runId = params.get('run') || 'current';
let lastData = null;
let reportFor = null;

async function loadRuns() {
  try {
    const runs = await fetch('/api/speed/runs').then((r) => r.json());
    const sel = $('runSel');
    sel.replaceChildren(new Option('지금 기록 (자동)', 'current'),
      ...runs.map((r) => new Option(`${r.created} · ${r.sources.length}센서 · ${r.segments}구간${r.finished ? '' : ' · 진행 중'}`, r.id)));
    sel.value = runId;
  } catch { /* 서버가 아직 없음 */ }
}
$('runSel').onchange = () => { runId = $('runSel').value; reportFor = null; $('report').textContent = '불러오는 중…'; tick(); };
$('reload').onclick = () => { loadRuns(); reportFor = null; tick(); };
$('refresh').onclick = () => loadReport(true);

function stageView(d) {
  const stages = d.stages || [];
  const segs = d.segments || [];
  const cur = d.current;
  if (d.finished) {
    $('stNow').textContent = '기록 끝';
    $('stNext').textContent = `${segs.filter((s) => s.set === 'calib' || s.set === 'test').length}개 구간 기록됨`;
  } else if (cur) {
    $('stNow').textContent = cur.speed_kmh ? `${cur.speed_kmh} km/h · ${cur.label}` : cur.label;
    const idx = stages.findIndex((s, i) => i === cur.index);
    const st = stages[idx];
    const left = st ? Math.max(0, st.duration_s - (d.now_s - cur.start_s)) : null;
    const next = stages[idx + 1];
    $('stNext').textContent = `${cur.set === 'calib' ? '보정' : cur.set === 'test' ? '평가' : '준비'} 구간`
      + (left != null ? ` · ${Math.ceil(left)}초 남음` : '') + (next ? ` · 다음: ${next.speed_kmh ? `${next.speed_kmh} km/h` : next.label}` : ' · 마지막 구간');
  } else {
    $('stNow').textContent = '시작 전';
    $('stNext').textContent = '';
  }
  $('stClock').textContent = fmtT(d.now_s || 0);
  const bar = $('bar');
  const total = stages.reduce((a, s) => a + (s.duration_s || 0), 0) || 1;
  bar.replaceChildren(...stages.map((s, i) => {
    const el = document.createElement('span');
    el.style.flex = String(s.duration_s || 1);
    el.className = s.set + (cur && cur.index === i ? ' cur' : segs.some((g) => g.index === i && g.end_s != null) ? ' done' : '');
    el.title = `${s.label} ${s.duration_s}s`;
    return el;
  }));
  bar.hidden = !stages.length || !total;
}

function cards(d) {
  const box = $('cards');
  const names = Object.keys(d.sources);
  if (!names.length) {
    box.innerHTML = '<div class="panel msg">센서 데이터를 기다리는 중이에요. 아이폰 기록 앱이나 갤럭시 기록기를 시작하세요.</div>';
    return;
  }
  const curSpeed = d.current?.speed_kmh;
  // 다른 센서와 비교해 정확히 절반이면 두 걸음을 한 걸음으로 센 것 (머리 좌우 흔들림 등)
  const halfOf = (n) => names.find((m) => {
    const a = d.sources[n], b = d.sources[m];
    return m !== n && a.cadence && b.cadence && b.quality >= 0.5 && Math.abs(a.cadence * 2 - b.cadence) / b.cadence < 0.06;
  });
  box.replaceChildren(...names.map((n) => {
    const s = d.sources[n];
    const half = halfOf(n);
    const el = document.createElement('div');
    el.className = 'card' + (s.lag_s > 3 && !d.finished ? ' stale' : '');
    el.style.setProperty('--c', color(n));
    const est = s.est_kmh != null && s.est_kind === 'height'
      ? `<span class="est" style="opacity:.6">${s.est_kmh.toFixed(1)}</span> km/h <span style="color:var(--warn)">(키 기준 대략)</span>`
      : s.est_kmh != null && s.est_kind === 'prev'
      ? `<span class="est">${s.est_kmh.toFixed(1)}</span> km/h <span class="msg">(지난 보정)</span>`
      : s.est_kmh != null
      ? `<span class="est">${s.est_kmh.toFixed(1)}</span> km/h${curSpeed ? ` <span style="color:${Math.abs(s.est_kmh - curSpeed) / curSpeed < 0.05 ? 'var(--ok)' : 'var(--warn)'}">(${((s.est_kmh - curSpeed) / curSpeed * 100).toFixed(0)}%)</span>` : ''}`
      : '<span>보정 후 표시</span>';
    el.innerHTML = `<div class="name">${s.label}</div>
      <div class="cad">${s.cadence ? s.cadence.toFixed(0) : '–'} <small>걸음/분</small></div>
      <div class="row"><span>신호 <span class="q ${qClass(s.quality)}">${qText(s.quality)}</span></span><span>빠른 반응 ${s.cadence_fast ? s.cadence_fast.toFixed(0) : '–'}</span></div>
      <div class="row"><span>추정 속도</span><span>${est}</span></div>
      ${half ? `<div class="row" style="color:var(--warn)"><span>${d.sources[half].label}의 절반 — 두 걸음을 한 걸음으로 센 것 같아요</span></div>` : ''}
      <div class="row"><span>${s.rate_hz} Hz</span><span>${s.lag_s > 3 && !d.finished ? `${s.lag_s.toFixed(0)}초째 안 들어옴` : '수신 중'}</span></div>`;
    return el;
  }));
}

function chart(d) {
  $('chartTitle').textContent = `걸음 수 (분당) · ${d.finished ? '전체 기록' : '최근 3분'}`;
  const svg = $('chart');
  const names = Object.keys(d.sources);
  const W = 1000;
  const H = 260;
  const pad = { l: 38, r: 8, t: 8, b: 22 };
  const tMax = d.now_s || 1;
  const tMin = d.finished ? 0 : Math.max(0, tMax - 180);
  let lo = 130;
  let hi = 200;
  for (const n of names) for (const [, c] of d.sources[n].timeline) { lo = Math.min(lo, c - 5); hi = Math.max(hi, c + 5); }
  const x = (t) => pad.l + ((t - tMin) / Math.max(1, tMax - tMin)) * (W - pad.l - pad.r);
  const y = (c) => pad.t + (1 - (c - lo) / (hi - lo)) * (H - pad.t - pad.b);
  const grid = getComputedStyle(document.body).getPropertyValue('--line') || '#2b3742';
  let s = '';
  // 구간 배경
  for (const g of d.segments || []) {
    const a = Math.max(tMin, g.start_s);
    const b = Math.min(tMax, g.end_s ?? tMax);
    if (b <= a) continue;
    const fill = g.set === 'calib' ? 'rgba(75,98,118,.28)' : g.set === 'test' ? 'rgba(106,90,58,.3)' : 'rgba(60,70,80,.15)';
    s += `<rect x="${x(a)}" y="${pad.t}" width="${x(b) - x(a)}" height="${H - pad.t - pad.b}" fill="${fill}"/>`;
    if (g.speed_kmh) s += `<text x="${x(a) + 4}" y="${pad.t + 14}" fill="#93a1b0" font-size="12">${g.speed_kmh}</text>`;
  }
  for (let c = Math.ceil(lo / 10) * 10; c <= hi; c += 10) {
    s += `<line x1="${pad.l}" x2="${W - pad.r}" y1="${y(c)}" y2="${y(c)}" stroke="${grid}" stroke-width="1"/>`;
    s += `<text x="${pad.l - 6}" y="${y(c) + 4}" fill="#93a1b0" font-size="11" text-anchor="end">${c}</text>`;
  }
  const every = tMax - tMin > 400 ? 60 : 30;
  for (let t = Math.ceil(tMin / every) * every; t <= tMax; t += every) {
    s += `<text x="${x(t)}" y="${H - 6}" fill="#93a1b0" font-size="11" text-anchor="middle">${fmtT(t)}</text>`;
  }
  for (const n of names) {
    const pts = d.sources[n].timeline.filter(([t]) => t >= tMin);
    if (pts.length < 2) continue;
    // 신호가 나쁜 점은 끊어서 그린다
    let path = '';
    let pen = false;
    for (const [t, c, q] of pts) {
      if (q < 0.3) { pen = false; continue; }
      path += `${pen ? 'L' : 'M'}${x(t).toFixed(1)},${y(c).toFixed(1)}`;
      pen = true;
    }
    s += `<path d="${path}" fill="none" stroke="${color(n)}" stroke-width="2.5" stroke-linejoin="round"/>`;
  }
  svg.innerHTML = s;
  $('legend').innerHTML = names.map((n) => `<span><i style="background:${color(n)}"></i>${d.sources[n].label}</span>`).join('');
}

function warnings(d) {
  const out = [];
  if (d.current && !d.finished) {
    for (const [n, s] of Object.entries(d.sources)) {
      if (s.lag_s > 3) out.push(`${s.label}: ${s.lag_s.toFixed(0)}초째 데이터가 안 들어와요 (에어팟이 빠졌거나 연결 끊김).`);
      else if (d.now_s - d.current.start_s > 15 && s.quality < 0.35) out.push(`${s.label}: 신호가 약해요 — 걸음 리듬이 잘 안 잡혀요.`);
    }
  }
  $('warn').innerHTML = out.map((w) => `<div class="warnbox">${w}</div>`).join('');
}

async function loadReport(refresh = false) {
  $('report').textContent = refresh ? '다시 계산하는 중…' : '불러오는 중…';
  try {
    const r = await fetch(`/api/speed/runs/${encodeURIComponent(lastData.id)}/report${refresh ? '?refresh=1' : ''}`).then((x) => x.json());
    reportFor = lastData.id;
    renderReport(r);
  } catch (e) {
    $('report').textContent = `분석을 불러오지 못했어요: ${e.message}`;
  }
}

// 보정 구간이 없는 기록(갤럭시만 등): 걸음 수가 일정했던 구간을 자동으로 찾아 센서별로 보여 준다
function renderFreeReport(r, names) {
  const from = names.map((n) => r.sources[n].free?.calib_from).find(Boolean);
  let html = from
    ? `<div class="msg" style="margin-bottom:6px">이 기록에는 속도를 아는 구간이 없어서 <b>${from} 기록의 보정(보폭)</b>으로 속도를 추정했어요.
      그 기록과 같은 센서별로 따로 계산해요. 그 센서가 그 기록에 없으면 키 기준 대략값이에요.</div>`
    : `<div class="msg" style="margin-bottom:6px">보정 구간(러닝머신 속도를 아는 구간)이 없어 오차율은 계산하지 않았어요.
    속도는 <b>키 ${r.height_cm}cm 기준 대략값</b>이라 실제와 10~20% 다를 수 있어요.</div>`;
  for (const n of names) {
    const s = r.sources[n];
    const head = `<div style="margin:12px 0 4px;color:${color(n)};font-weight:700">${s.label} <span class="msg">${s.fs ? `${s.fs} Hz` : ''}</span></div>`;
    if (s.error || !s.free) { html += `${head}<div class="msg ${s.error ? 'bad' : ''}">${s.error || s.note || '–'}</div>`; continue; }
    const rows = s.free.plateaus.map((p) => `<tr><td>${fmtT(p.start_s)} ~ ${fmtT(p.end_s)}</td><td>${p.cadence.toFixed(0)}</td><td>±${(p.spread / 2).toFixed(1)}</td><td>${p.kmh.toFixed(1)}</td></tr>`).join('');
    html += `${head}<div class="scroll"><table><tr><th>걸음 수가 일정했던 구간</th><th>걸음/분</th><th>흔들림</th><th>추정 km/h</th></tr>
      ${rows || '<tr><td colspan="4">10초 넘게 일정했던 구간이 없어요</td></tr>'}</table></div>`;
  }
  html += `<div class="msg" style="margin-top:8px">${r.made} 계산</div>`;
  $('report').innerHTML = html;
}

function renderReport(r) {
  const names = Object.keys(r.sources);
  const models = [['height_only', '키만'], ['single_point', '1개 속도 보정'], ['multi_cadence', '여러 속도 보정'], ['multi_features', '여러 속도+보조']];
  if (!names.some((n) => r.sources[n].summary)) { renderFreeReport(r, names); return; }
  // 1) 센서 × 보정 방식 오차율
  let best = Infinity;
  for (const n of names) for (const m of r.sources[n].summary || []) if (m.model !== 'height_only') best = Math.min(best, m['MAPE_%']);
  let html = `<div class="msg" style="margin-bottom:6px">속도 오차율(%) — 낮을수록 좋아요. 평가 구간을 보정에 쓰지 않고 맞힌 결과예요.</div>
    <div class="scroll"><table><tr><th>센서</th>${models.map(([, l]) => `<th>${l}</th>`).join('')}<th>샘플링</th></tr>`;
  for (const n of names) {
    const s = r.sources[n];
    if (s.error) { html += `<tr><td style="color:${color(n)}">${s.label}</td><td colspan="5" class="bad" style="text-align:left">${s.error}</td></tr>`; continue; }
    if (!s.summary) { html += `<tr><td style="color:${color(n)}">${s.label}</td><td colspan="4" style="text-align:left">${s.note || '–'}</td><td>${s.fs} Hz</td></tr>`; continue; }
    const by = Object.fromEntries(s.summary.map((m) => [m.model, m['MAPE_%']]));
    html += `<tr><td style="color:${color(n)}">${s.label}</td>${models.map(([k]) => `<td class="${by[k] === best ? 'best' : ''}">${by[k] != null ? by[k].toFixed(1) : '–'}</td>`).join('')}<td>${s.fs} Hz</td></tr>`;
  }
  html += '</table></div>';
  // 2) 구간 표: 구간마다 센서별 걸음 수와 신호
  const first = names.find((n) => r.sources[n].features);
  if (first) {
    const segs = r.sources[first].features;
    html += `<div class="msg" style="margin:14px 0 6px">구간별 걸음 수(분당) · <span class="q bad">나쁨</span> 신호는 다시 뛰는 게 좋아요</div>
      <div class="scroll"><table><tr><th>구간</th><th>속도</th>${names.map((n) => `<th style="color:${color(n)}">${r.sources[n].label}</th>`).join('')}</tr>`;
    segs.forEach((g, i) => {
      html += `<tr><td>${g.set === 'calib' ? '보정' : '평가'} ${fmtT(g.start_s)}</td><td>${g.speed_kmh}</td>`;
      for (const n of names) {
        const f = r.sources[n].features?.[i];
        html += f ? `<td class="${f.quality < 0.35 ? 'bad' : ''}">${f.cadence_spm.toFixed(0)} <span class="q ${qClass(f.quality)}">${f.quality.toFixed(2)}</span></td>` : '<td>–</td>';
      }
      html += '</tr>';
    });
    html += '</table></div>';
    // 3) 평가 구간 예측
    const n0 = names.filter((n) => r.sources[n].predictions);
    if (n0.length) {
      html += `<div class="msg" style="margin:14px 0 6px">평가 구간 추정 속도 (여러 속도 보정, km/h)</div>
        <div class="scroll"><table><tr><th>실제 속도</th>${n0.map((n) => `<th style="color:${color(n)}">${r.sources[n].label}</th>`).join('')}</tr>`;
      r.sources[n0[0]].predictions.forEach((p, i) => {
        html += `<tr><td>${p.speed_kmh}</td>${n0.map((n) => {
          const q = r.sources[n].predictions[i];
          const err = q ? (q.multi_cadence - q.speed_kmh) / q.speed_kmh * 100 : null;
          return q ? `<td class="${Math.abs(err) > 8 ? 'bad' : ''}">${q.multi_cadence.toFixed(2)} (${err >= 0 ? '+' : ''}${err.toFixed(1)}%)</td>` : '<td>–</td>';
        }).join('')}</tr>`;
      });
      html += '</table></div>';
    }
  }
  html += `<div class="msg" style="margin-top:8px">${r.made} 계산 · 키 ${r.height_cm}cm · ${names.map((n) => r.sources[n].mode).find(Boolean) || ''}</div>`;
  $('report').innerHTML = html;
}

let busy = false;
async function tick() {
  if (busy) return;
  busy = true;
  try {
    const res = await fetch(`/api/speed/runs/${encodeURIComponent(runId)}/live`);
    if (res.ok) {
      const d = await res.json();
      lastData = d;
      stageView(d);
      cards(d);
      chart(d);
      warnings(d);
      if (d.finished && reportFor !== d.id && !document.body.classList.contains('compact')) loadReport();
    } else if (res.status === 404) {
      $('stNow').textContent = '아직 기록이 없어요';
      $('stNext').textContent = '아이폰 기록 앱에서 시작하세요';
    }
  } catch {
    $('stNext').textContent = '노트북 서버에 연결되지 않아요';
  } finally {
    busy = false;
  }
}

// phyphox 파일 올리기
async function upload(file) {
  if (!lastData) { $('dropMsg').textContent = '먼저 기록을 고르세요.'; return; }
  $('dropMsg').textContent = `${file.name} 올리는 중…`;
  const fd = new FormData();
  fd.append('file', file);
  try {
    const r = await fetch(`/api/speed/runs/${encodeURIComponent(lastData.id)}/phyphox`, { method: 'POST', body: fd });
    const j = await r.json();
    if (!r.ok) throw new Error(j.detail || r.status);
    $('dropMsg').textContent = `추가했어요: 시각 차이 ${j.offset_s}초 (${j.reference} 기준), 샘플 ${j.samples}개. 분석을 다시 계산해요.`;
    loadReport(true);
  } catch (e) {
    $('dropMsg').textContent = `올리지 못했어요: ${e.message}`;
  }
}
const drop = $('drop');
drop.addEventListener('dragover', (e) => { e.preventDefault(); drop.classList.add('over'); });
drop.addEventListener('dragleave', () => drop.classList.remove('over'));
drop.addEventListener('drop', (e) => { e.preventDefault(); drop.classList.remove('over'); if (e.dataTransfer.files[0]) upload(e.dataTransfer.files[0]); });
$('file').onchange = () => { if ($('file').files[0]) upload($('file').files[0]); };

loadRuns();
tick();
setInterval(tick, 500);
setInterval(loadRuns, 15000);
