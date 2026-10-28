/* global YT */
import { CoursePath } from './course.js';
import { BotSource, ManualSource, Race } from './race.js';
import { fmtTime, Hud } from './hud.js';
import { createVideoRunners } from './video-runners.js';
import { playbackPlan, videoCourseLength } from './video-sync.js';
import { VideoCourse } from './video-course.js';
import { VideoAhead } from './video-ahead.js';
import { VideoSfm } from './video-sfm.js';
import { VideoDepth } from './video-depth.js';
import { LocalVideoPlayer, PLAYER_STATE } from './video-player.js';
import { VideoRoad } from './video-road.js';

const params = new URLSearchParams(location.search);
const $ = (id) => document.getElementById(id);
const number = (key, fallback, min, max) => {
  const value = Number(params.get(key));
  return params.has(key) && Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : fallback;
};
// 내려받아 둔 영상(assets/video/{id}.local.mp4, file이 있으면 그 파일)과 영상별 기본값. 첫 번째가 기본 영상
const LOCAL_VIDEOS = {
  // 해안길: 처음 50초는 지도 인트로. 영상에 찍힌 속도가 6mph 안팎이라 촬영 속도 9.7km/h
  // 화면에는 1080p판을 쓴다 (720p판 .local.mp4는 3D 복원 계산용, 시간은 같다)
  o0EmQXATQQI: { title: '해안길 · 더니든 오타고 반도', from: 120, filmed: 9.7, file: 'o0EmQXATQQI.1080.mp4' }, // 방파제가 왼쪽으로 휘는 곳에서 출발 (사용자 지정)
  DQBCdbV2Enc: { title: '산길 · 마운트쿡 후커밸리', from: 30, filmed: 10 },
};
const VIDEO_ID = /^[\w-]{11}$/.test(params.get('v') || '') ? params.get('v') : Object.keys(LOCAL_VIDEOS)[0];
const LOCAL_INFO = LOCAL_VIDEOS[VIDEO_ID];
const LOCAL_MODE = params.get('source') === 'local' || (params.get('source') !== 'youtube' && !!LOCAL_INFO);
const FILMED_KMH = number('filmed', LOCAL_INFO?.filmed ?? 10, 1, 40);
const START_TIME = number('from', LOCAL_INFO?.from ?? 0, 0, 86400);
const REQUESTED_M = number('km', 5, 0.1, 20) * 1000;
const me = new ManualSource(number('speed', 9, 0, 20));
const race = new Race(REQUESTED_M, [
  { id: 'me', name: '나', color: '#e8590c', isMe: true, source: me, lane: 0 }, // 나는 길 가운데, 나머지는 양옆으로 나눠 서로 가리지 않게
  { id: 'b1', name: '민지', color: '#1c7ed6', source: new BotSource(9.6, 1), lane: -1, heightM: 1.70 },
  { id: 'b2', name: '준호', color: '#2f9e44', source: new BotSource(10.4, 2), lane: 1, heightM: 1.70 },
  { id: 'b3', name: '서연', color: '#ae3ec9', source: new BotSource(8.6, 3), lane: 0, heightM: 1.70 },
]);
let path = new CoursePath([[0, 0], [0, REQUESTED_M]], REQUESTED_M);
const hud = new Hud(race, path, { name: '실사 + 3D 러너' });
let player = null, playerReady = false, paused = false, playerFailed = false, autoplayBlocked = false;
let layer = null, showRunners = true, lastSync = -Infinity, lastSeek = -Infinity;
let bufferingSince = null, lastNudge = -Infinity; // 버퍼링 멈춤 감지·깨우기
let lastMetadata = -Infinity;
let apiTimeout, playerInstance, playerGeneration = 0, lastPlayRequest = -Infinity;
const alignmentDefaults = { horizon: 46, center: 50, fov: 65, size: 100 }; // 지평선: 길 폭이 0이 되는 높이(추적 데이터로 추정)
const alignment = { ...alignmentDefaults };
const road = new VideoRoad();
const roadStorageKey = `video-road:${VIDEO_ID}`;
let roadCorrections = [], editingRoad = false, editRows = [], editTime = 0;
try {
  const saved = JSON.parse(localStorage.getItem(roadStorageKey));
  roadCorrections = new VideoRoad(Array.isArray(saved) ? saved : []).keys;
} catch { /* 저장이 차단돼도 영상은 사용 가능 */ }
let originalRoad = [];
async function loadRoad() {
  try {
    const response = await fetch(`assets/video/${VIDEO_ID}.road.json`);
    if (!response.ok) throw new Error('길 정보 없음');
    const data = await response.json();
    originalRoad = new VideoRoad(data.keyframes || []).keys;
    if (!originalRoad.length) throw new Error('유효한 길 정보 없음');
    road.keys = new VideoRoad(originalRoad).keys;
    for (const k of roadCorrections) road.set(k.time, k.rows);
    $('road-status').textContent = '길 경계 추정 적용 중 · 어긋난 장면은 보정해 주세요.';
  } catch {
    for (const k of roadCorrections) road.set(k.time, k.rows);
    $('road-status').textContent = '이 영상에는 길 정보가 없어요. 현재 장면 길 보정으로 위치를 지정해 주세요.';
  }
}
loadRoad();
const editor = $('road-editor');
function drawRoadEditor() {
  const points = [...editRows.map((r) => [r[1]*1280,r[0]*720]), ...editRows.slice().reverse().map((r) => [r[2]*1280,r[0]*720])];
  editor.innerHTML = `<polygon points="${points.map((p) => p.join(',')).join(' ')}"/>`
    + editRows.map((r,i) => [1,2].map((side) => `<circle data-row="${i}" data-side="${side}" cx="${r[side]*1280}" cy="${r[0]*720}" r="12"/>`).join('')).join('');
}
let drag = null;
editor.onpointerdown = (event) => {
  const { row, side } = event.target.dataset;
  if (row === undefined) return;
  drag = { row: Number(row), side: Number(side) };
  editor.setPointerCapture(event.pointerId);
  event.preventDefault();
};
editor.onpointermove = (event) => {
  if (!drag) return;
  const rect = editor.getBoundingClientRect();
  const r = editRows[drag.row];
  const min = drag.side === 1 ? 0 : r[1]+.015, max = drag.side === 1 ? r[2]-.015 : 1;
  r[drag.side] = Math.max(min, Math.min(max, (event.clientX-rect.left)/rect.width));
  road.set(editTime, editRows);
  drawRoadEditor();
};
editor.onpointerup = editor.onpointercancel = () => { drag = null; };
$('edit-road').onclick = () => {
  if (!playerReady || playerFailed) return;
  editingRoad = !editingRoad;
  if (editingRoad) {
    if (race.state === 'running') {
      paused = true;
      $('pause').textContent = '계속 달리기';
    }
    player.pauseVideo();
    editTime = player.getCurrentTime();
    editRows = road.rowsAt(editTime);
    road.set(editTime, editRows);
    drawRoadEditor();
    $('road-status').textContent = `${editTime.toFixed(1)}초 장면 · 원을 길 가장자리로 옮기세요.`;
  } else {
    roadCorrections = roadCorrections.filter((k) => Math.abs(k.time-editTime) >= .1);
    roadCorrections.push({ time: editTime, rows: editRows });
    try { localStorage.setItem(roadStorageKey, JSON.stringify(roadCorrections)); } catch { /* 세션에는 유지 */ }
    $('road-status').textContent = race.state === 'ready' ? '길 보정 적용 완료 · 시작을 누르세요.' : '길 보정 적용 완료 · 계속 달리기를 누르세요.';
  }
  editor.hidden = !editingRoad;
  $('edit-road').textContent = editingRoad ? '길 보정 완료' : '현재 장면 길 보정';
  $('edit-road').setAttribute('aria-pressed', String(editingRoad));
  $('start').disabled = editingRoad;
  $('pause').disabled = editingRoad;
};
$('reset-road').onclick = () => {
  if (editingRoad) $('edit-road').click();
  roadCorrections = [];
  road.keys = new VideoRoad(originalRoad).keys;
  try { localStorage.removeItem(roadStorageKey); } catch { /* 선택적 저장 */ }
  $('road-status').textContent = '길 보정을 초기화했어요.';
};
const storageKey = `video-alignment:${VIDEO_ID}`;
try {
  const saved = JSON.parse(localStorage.getItem(storageKey));
  for (const key of Object.keys(alignment)) {
    const input = $(key);
    if (Number.isFinite(saved?.[key])) alignment[key] = Math.min(Number(input.max), Math.max(Number(input.min), saved[key]));
  }
} catch { /* 저장 불가능한 브라우저에서도 기본값으로 실행 */ }
function updateAlignment() {
  for (const key of Object.keys(alignment)) {
    $(key).value = alignment[key];
    $(`${key}-value`).textContent = `${alignment[key]}${key === 'fov' ? '°' : '%'}`;
  }
  layer?.resize();
  try { localStorage.setItem(storageKey, JSON.stringify(alignment)); } catch { /* 선택적 저장 */ }
}
for (const key of Object.keys(alignment)) $(key).oninput = (e) => {
  alignment[key] = Number(e.target.value);
  updateAlignment();
};
updateAlignment();
$('reset-alignment').onclick = () => { Object.assign(alignment, alignmentDefaults); updateAlignment(); };
const openAlignment = (open) => {
  $('alignment').hidden = !open;
  $('tune').setAttribute('aria-expanded', String(open));
};
$('tune').onclick = () => openAlignment($('alignment').hidden);
$('close-tune').onclick = () => openAlignment(false);
$('video-id').value = VIDEO_ID;
$('video-source').value = LOCAL_MODE ? 'local' : 'youtube';
const sourceParams = new URLSearchParams(params);
sourceParams.set('source', LOCAL_MODE ? 'youtube' : 'local');
sourceParams.set('from', String(START_TIME));
$('source-switch').href = `video.html?${sourceParams}`;
$('source-switch').textContent = LOCAL_MODE ? 'YouTube 모드' : '다운로드 영상 모드';
$('filmed').value = FILMED_KMH;
$('start-time').value = START_TIME;
$('target-length').value = REQUESTED_M / 1000;
$('toggle-runners').onclick = () => {
  showRunners = !showRunners;
  $('toggle-runners').setAttribute('aria-pressed', String(showRunners));
  $('toggle-runners').textContent = showRunners ? '러너 켜짐' : '러너 꺼짐';
  $('runners').hidden = !showRunners;
};
$('credit').append('영상: ');
const credit = document.createElement('a');
credit.href = `https://www.youtube.com/watch?v=${VIDEO_ID}`;
credit.target = '_blank';
credit.rel = 'noopener noreferrer';
credit.textContent = 'YouTube 원본';
$('credit').append(credit, ' · 위치·거리 근사');
$('open-video').href = credit.href;
$('check-video').href = `video-check.html?v=${VIDEO_ID}`;
$('retry-video').onclick = () => {
  editingRoad = false;
  editor.hidden = true;
  $('edit-road').textContent = '현재 장면 길 보정';
  $('edit-road').setAttribute('aria-pressed', 'false');
  playerInstance?.destroy?.();
  player = null;
  playerReady = false;
  playerFailed = false;
  autoplayBlocked = false;
  lastPlayRequest = lastSync = lastSeek = -Infinity;
  $('video-wrap').innerHTML = '<div id="player"></div>';
  $('video-recovery').hidden = true;
  $('error').textContent = '';
  $('rate').textContent = '영상 다시 연결 중…';
  if (LOCAL_MODE || window.YT?.Player) makePlayer(); else location.reload();
};

// 모델/그래픽 오류가 나도 영상 모드는 사용할 수 있다.
// 코스 지도(영상 속 실제 길 + 영상 시각별 위치)가 있으면 러너를 실제 길 위에 세우고 영상도 그 표로 맞춘다
let course = null;
const courseReady = VideoCourse.load(VIDEO_ID).then((c) => {
  course = c;
  if (c) $('road-status').textContent = '코스 지도 모드: 러너가 영상 속 실제 길을 따라 달려요.';
  readMetadata();
  return c;
}).catch(() => null);
// 앞길 추적표(촬영자가 지나간 길 위 점을 거꾸로 추적)가 있으면 러너를 그 위치에 세운다 (가장 정확)
const aheadReady = VideoAhead.load(VIDEO_ID).catch(() => null);
// 3D 복원(SfM) 데이터가 있으면 그 구간은 3D 카메라로 그린다 (가장 자연스러움)
const sfmReady = VideoSfm.load(VIDEO_ID).catch(() => null);
// 픽셀 단위 가림용 깊이 지도 (없으면 예전 방식 가림)
const depthReady = VideoDepth.load(VIDEO_ID).catch(() => null);
createVideoRunners($('runners'), race.runners, alignment, road).then(async (result) => {
  layer = result;
  layer.setCourse(await courseReady);
  layer.setAhead(await aheadReady);
  const sfm = await sfmReady;
  if (sfm) layer.setSfm(sfm);
  const depth = await depthReady;
  if (depth) layer.setDepth(depth);
}).catch((e) => {
  $('toggle-runners').disabled = true;
  $('toggle-runners').textContent = '러너 로드 실패';
  $('error').textContent = `3D 러너를 불러오지 못했어요. 영상은 계속 사용할 수 있어요. ${e.message}`;
});

function failPlayer(message) {
  playerReady = false;
  playerFailed = true;
  paused = true;
  $('start').disabled = true;
  $('pause').disabled = true;
  $('rate').textContent = '영상 연결 실패';
  $('error').textContent = message;
  $('video-recovery').hidden = false;
}
function readMetadata() {
  if (!playerReady) return;
  const duration = player.getDuration();
  if (!(duration > 0)) return;
  // 코스 지도 모드: 영상 시작 위치부터 실제 길이 남은 만큼, 아니면 일정한 촬영 속도로 계산
  const sStart = course ? course.sAt(START_TIME) : 0;
  const length = course
    ? Math.max(0, Math.min(REQUESTED_M, course.sAt(duration) - sStart))
    : videoCourseLength(duration, START_TIME, FILMED_KMH, REQUESTED_M);
  if (length <= 0) {
    failPlayer('영상 시작 위치가 영상 길이보다 길어요. 화면 맞춤에서 시작 위치를 줄여 주세요.');
    return;
  }
  // 코스 지도가 늦게 불러와져도 미니맵을 실제 코스 모양으로 다시 그린다
  if (Math.abs(race.length - length) > 0.1 || (course && !path.fromCourse)) {
    race.length = length;
    path = course ? new CoursePath(course.slice(sStart, length), length) : new CoursePath([[0, 0], [0, length]], length);
    path.fromCourse = !!course;
    hud.path = path;
    hud.setupMinimap();
    $('total').textContent = (length / 1000).toFixed(2);
  }
  const data = player.getVideoData?.();
  if (data?.title) $('course-name').textContent = data.title;
  if (data?.author) credit.textContent = data.author;
}
function startRace() {
  if (!playerReady || playerFailed || editingRoad || race.state !== 'ready') return;
  readMetadata();
  if (playerFailed) return;
  // 첫 재생을 바로 멈추지 않는다. 카운트다운이 끝나면 syncVideo에서 한 번 요청.
  paused = false;
  race.start();
  $('start').hidden = true;
  $('pause').hidden = false;
  $('pause').disabled = true;
}
$('start').onclick = startRace;
$('pause').onclick = () => {
  if (!playerReady || race.state !== 'running' || playerFailed || editingRoad) return;
  paused = !paused;
  autoplayBlocked = false;
  $('pause').textContent = paused ? '계속 달리기' : '일시정지';
  if (paused) player.pauseVideo();
  else { player.playVideo(); lastPlayRequest = performance.now(); lastSync = -Infinity; }
};
$('sound').onclick = () => {
  if (!playerReady || playerFailed) return;
  const audible = player.isMuted();
  if (audible) player.unMute(); else player.mute();
  $('sound').setAttribute('aria-pressed', String(audible));
  $('sound').textContent = audible ? '소리 켜짐' : '소리 꺼짐';
};
$('faster').onclick = () => me.change(+0.5);
$('slower').onclick = () => me.change(-0.5);
window.addEventListener('keydown', (e) => {
  if (e.target.matches('input, button, a, select, textarea')) return;
  if (e.key === 'ArrowUp' || e.key === '+' || e.key === '=') { e.preventDefault(); me.change(+0.5); }
  else if (e.key === 'ArrowDown' || e.key === '-') { e.preventDefault(); me.change(-0.5); }
  else if (e.key === ' ' || e.key === 'Enter') {
    e.preventDefault();
    if (race.state === 'ready') startRace(); else $('pause').click();
  }
});
window.addEventListener('resize', () => { layer?.resize(); hud.setupMinimap(); });
document.addEventListener('visibilitychange', () => {
  if (document.hidden && race.state === 'running' && !paused) $('pause').click();
});

function syncVideo(now) {
  if (!playerReady || playerFailed || now - lastSync < 500) return;
  lastSync = now;
  if (now - lastMetadata > 1000) { readMetadata(); lastMetadata = now; }
  if (playerFailed) return;
  const r = race.me;
  const running = race.state === 'running' && !paused && !editingRoad && r.finishTime === null && r.speedKmh >= 0.5;
  const state = player.getPlayerState();
  if (!running) {
    lastPlayRequest = -Infinity;
    if (state === PLAYER_STATE.PLAYING || state === PLAYER_STATE.BUFFERING) player.pauseVideo();
    $('rate').textContent = paused ? (autoplayBlocked ? '계속 달리기를 눌러 영상을 재생해 주세요' : '일시정지')
      : race.state === 'running' ? '멈춤' : race.state === 'ready' ? '준비 완료 · 촬영 속도는 가정값' : '';
    return;
  }
  // 초기 로딩 중 배속·탐색·재생 명령을 반복해서 보내지 않는다.
  if (state === PLAYER_STATE.BUFFERING) {
    $('rate').textContent = '영상 버퍼링 중…';
    bufferingSince ??= now;
    // 4초 넘게 멈춰 있으면 깨운다: 같은 위치로 다시 이동하면 브라우저가 데이터를 새로 요청한다
    if (now - bufferingSince > 4000 && now - lastNudge > 4000) {
      player.seekTo(player.getCurrentTime() + 0.05, true);
      player.playVideo();
      lastNudge = now;
    }
    return;
  }
  bufferingSince = null;
  if (state !== PLAYER_STATE.PLAYING) {
    if (now - lastPlayRequest > 3000) {
      player.playVideo();
      lastPlayRequest = now;
    }
    $('rate').textContent = '영상 재생 준비 중…';
    return;
  }
  const now_t = player.getCurrentTime();
  const plan = playbackPlan({ distance: r.distance, currentTime: now_t,
    speedKmh: r.speedKmh, filmedKmh: course ? course.filmedKmhAt(now_t) : FILMED_KMH, startTime: START_TIME,
    rates: player.getAvailablePlaybackRates(), continuous: player.continuousRate,
    target: course ? Math.max(START_TIME, course.timeAt(course.sAt(START_TIME) + r.distance)) : undefined });
  if (plan.seek && now - lastSeek > 5000) {
    player.seekTo(plan.target, true);
    lastSeek = now;
  }
  if (plan.rate !== player.getPlaybackRate()) player.setPlaybackRate(plan.rate);
  $('rate').textContent = course
    ? `영상 ${player.getPlaybackRate().toFixed(2)}배속 · 실제 코스 동기화`
    : `영상 ${player.getPlaybackRate().toFixed(2)}배속 · 촬영 ${FILMED_KMH}km/h 가정`;
}
function updateOverlay() {
  const overlay = $('overlay');
  if (race.state === 'countdown') {
    overlay.hidden = false;
    overlay.innerHTML = `<div class="count">${Math.ceil(race.countdown)}</div>`;
  } else if (race.state === 'finished') {
    if (overlay.dataset.shown) return;
    overlay.dataset.shown = '1';
    $('pause').hidden = true;
    overlay.hidden = false;
    const rank = race.standings().indexOf(race.me) + 1;
    overlay.innerHTML = `<div class="card"><h2>완주!</h2><p class="big">${fmtTime(race.me.finishTime)}</p>`
      + `<p>${race.runners.length}명 중 ${rank}위</p><button id="again">다시 달리기</button></div>`;
    $('again').onclick = () => location.reload();
  } else {
    overlay.hidden = true;
    if (race.state === 'running' && !playerFailed && !editingRoad) $('pause').disabled = false;
  }
}

// 확인용: ?freeze=영상초&gaps=20,45,70 -> 영상을 그 순간에 멈추고 다른 러너를 내 앞 그 거리(m)에 세워 둔다 (가림 확인)
const FREEZE = params.has('freeze') ? Number(params.get('freeze')) : null;
const FREEZE_GAPS = (params.get('gaps') || '').split(',').filter((v) => v.trim() !== '').map(Number).filter((v) => Number.isFinite(v));

let last = performance.now();
function loop(now) {
  if (now - last < 1000 / 31) { requestAnimationFrame(loop); return; }
  const dt = Math.min((now - last) / 1000, 0.1);
  last = now;
  if (FREEZE !== null) {
    race.runners.filter((r) => !r.isMe).forEach((r, i) => { r.distance = race.me.distance + (FREEZE_GAPS[i] ?? 20 + 20 * i); });
    if (playerReady && Math.abs(player.getCurrentTime() - FREEZE) > 0.2) { player.seekTo(FREEZE, true); player.pauseVideo(); }
    layer?.update(dt, race, true, FREEZE);
    requestAnimationFrame(loop);
    return;
  }
  if (!paused && !playerFailed && !editingRoad) race.update(dt);
  if (FREEZE_GAPS.length && race.state === 'running') {
    // 확인용: ?gaps=30,50,70 -> 다른 러너를 내 앞 그 거리에 붙여 두고 같은 속도로 (장애물 뒤를 지나는 모습 보기)
    race.runners.filter((r) => !r.isMe).forEach((r, i) => {
      r.distance = race.me.distance + (FREEZE_GAPS[i] ?? 20 + 20 * i);
      r.speedKmh = race.me.speedKmh;
      r.cadence = race.me.cadence;
    });
  }
  syncVideo(now);
  hud.update(now);
  updateOverlay();
  layer?.update(paused || editingRoad ? 0 : dt, race, showRunners, playerReady ? player.getCurrentTime() : START_TIME);
  requestAnimationFrame(loop);
}
requestAnimationFrame(loop);

function makePlayer() {
  const generation = ++playerGeneration;
  clearTimeout(apiTimeout);
  apiTimeout = setTimeout(() => failPlayer('영상에 연결하지 못했어요. 영상 다시 연결을 눌러 주세요.'), 20000);
  // 생성자는 준비 전 객체를 반환한다. 제어용 참조는 onReady에서만 등록한다.
  const events = {
      onReady: (event) => {
        if (generation !== playerGeneration || playerFailed) return;
        clearTimeout(apiTimeout);
        player = event.target;
        playerReady = true;
        player.mute();
        $('sound').setAttribute('aria-pressed', 'false');
        $('sound').textContent = '소리 꺼짐';
        readMetadata();
        if (!playerFailed) {
          $('start').disabled = false;
          if (race.state === 'running') {
            $('pause').disabled = false;
            $('pause').textContent = '계속 달리기';
          }
          $('rate').textContent = '준비 완료 · 촬영 속도는 가정값';
          if (params.get('autostart') === '1') startRace();
        }
      },
      onError: (event) => {
        if (generation !== playerGeneration) return;
        clearTimeout(apiTimeout);
        const messages = { 2: '영상 주소가 잘못되었어요.', 5: 'YouTube HTML5 재생 오류예요. 영상 다시 연결 또는 영상만 확인을 눌러 주세요.',
          100: '영상을 찾을 수 없거나 비공개예요.', 101: '영상 소유자가 외부 재생을 허용하지 않았어요.',
          150: '영상 소유자가 외부 재생을 허용하지 않았어요.', 153: 'YouTube가 사이트 정보를 받지 못했어요. localhost 주소로 열어 주세요.' };
        failPlayer(`${String(event.data).startsWith('local-') ? '다운로드 영상을 재생하지 못했어요. 영상 파일과 서버를 확인하거나 YouTube 모드로 전환해 주세요.' : messages[event.data] || 'YouTube 재생 오류가 발생했어요.'} (오류 ${event.data})`);
      },
      onAutoplayBlocked: () => {
        if (generation !== playerGeneration || playerFailed) return;
        paused = true;
        autoplayBlocked = true;
        $('pause').textContent = '계속 달리기';
        if (race.state === 'countdown' || race.state === 'ready') race.state = 'running';
        $('pause').hidden = false;
        $('start').hidden = true;
      },
    };
  playerInstance = LOCAL_MODE
    ? new LocalVideoPlayer($('video-wrap'), {
      src: `assets/video/${LOCAL_INFO?.file ?? `${VIDEO_ID}.local.mp4`}`, startTime: START_TIME, events, title: LOCAL_INFO?.title,
    })
    : new YT.Player('player', { videoId: VIDEO_ID,
      playerVars: { controls: 0, playsinline: 1, rel: 0, fs: 0, origin: location.origin, start: Math.floor(START_TIME) }, events });
}
if (LOCAL_MODE || window.YT?.Player) makePlayer();
else {
  apiTimeout = setTimeout(() => failPlayer('YouTube에 연결하지 못했어요. 인터넷 연결을 확인해 주세요.'), 20000);
  window.onYouTubeIframeAPIReady = makePlayer;
  const script = document.createElement('script');
  script.src = 'https://www.youtube.com/iframe_api';
  script.onerror = () => { clearTimeout(apiTimeout); failPlayer('YouTube 스크립트를 불러오지 못했어요. 인터넷 연결을 확인해 주세요.'); };
  document.head.append(script);
}
