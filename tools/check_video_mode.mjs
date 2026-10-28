// node --experimental-vm-modules tools/check_video_mode.mjs
// DOM/YouTube를 대체해 상태 흐름을 검사한다. 실제 재생·WebGL 시각 검증은 별도.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { resolve } from 'node:path';
const root = resolve(import.meta.dirname, '..', 'run3d', 'src');
const source = await readFile(resolve(root, 'video-sync.js'), 'utf8');
const { playbackPlan, videoCourseLength } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
const rates = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2];
assert.equal(videoCourseLength(0, 0, 10, 5000), 5000);
assert.equal(videoCourseLength(4000, 0, 10, 5000), 5000);
assert.equal(videoCourseLength(120, 30, 12, 5000), 300);
assert.equal(videoCourseLength(20, 30, 10, 5000), 0);
assert.equal(playbackPlan({ distance: 100, currentTime: 36, speedKmh: 15, filmedKmh: 10, rates }).rate, 1.5);
assert.equal(playbackPlan({ distance: 100, currentTime: 36, speedKmh: 0, filmedKmh: 10, rates }).stopped, true);
assert.equal(playbackPlan({ distance: 100, currentTime: 10, speedKmh: 10, filmedKmh: 10, rates }).seek, true);
let time = 0, distance = 0, maxError = 0;
for (let i = 0; i < 1200; i++) {
  const speedKmh = i < 300 ? 9 : i < 600 ? 15 : i < 700 ? 0 : 8;
  const p = playbackPlan({ distance, currentTime: time, speedKmh, filmedKmh: 10, rates });
  if (p.seek) time = p.target;
  if (!p.stopped) time += p.rate * 0.5;
  distance += speedKmh / 3.6 * 0.5;
  maxError = Math.max(maxError, Math.abs(distance / (10 / 3.6) - time));
}
assert.ok(maxError < 2, `영상 오차 ${maxError}초`);
const html = await readFile(resolve(root, '../video.html'), 'utf8');
const appSource = await readFile(resolve(root, 'video.js'), 'utf8');
for (const [, id] of appSource.matchAll(/\$\('([^']+)'\)/g))
  if (id !== 'again') assert.ok(html.includes(`id="${id}"`), `누락된 UI: ${id}`);

async function setup(query = '', duration = 3600, readyMode = 'immediate', sourceMode = 'youtube') {
  // 재생 흐름 검사는 코스 지도가 없는 영상(일정한 촬영 속도 가정) 기준. 영상을 따로 고르지 않으면 산길 영상
  if (!query.includes('v=')) query = `${query || '?'}${query.length > 1 ? '&' : ''}v=DQBCdbV2Enc`;
  query = `${query || '?'}${query.includes('?') && query.length > 1 ? '&' : ''}source=${sourceMode}`;
  const elements = new Map();
  const drawing = new Proxy({}, { get: (obj, key) => obj[key] ?? (() => {}) });
  function el(id) {
    if (!elements.has(id)) elements.set(id, {
      textContent: '', innerHTML: '', value: '', disabled: false, hidden: false, clientWidth: 90, style: {}, dataset: {},
      append() {}, replaceChildren() {}, setAttribute() {}, matches() { return false; }, getContext() { return drawing; },
      click() { if (!this.disabled) this.onclick?.(); },
    });
    return elements.get(id);
  }
  for (const tag of html.matchAll(/<[^>]*\bid="([^"]+)"[^>]*>/g)) {
    const item = el(tag[1]);
    for (const key of ['value', 'min', 'max']) item[key] = tag[0].match(new RegExp(`${key}="([^"]+)"`))?.[1] ?? '';
    item.hidden = /\bhidden\b/.test(tag[0]); item.disabled = /\bdisabled\b/.test(tag[0]);
  }
  let frame, clock = 0, race, callbacks, player, ready, nativeVideo;
  function createElement(tag) {
    if (tag !== 'video') return el(`created-${tag}`);
    const listeners = new Map();
    const video = nativeVideo = {
      duration, currentTime: 0, playbackRate: 1, paused: true, readyState: 4, ended: false, seeking: false,
      calls: { play: 0, pause: 0 },
      addEventListener(name, fn) { listeners.set(name, fn); },
      emit(name) { listeners.get(name)?.(); },
      play() { this.calls.play++; this.paused = false; return Promise.resolve(); },
      pause() { this.calls.pause++; this.paused = true; },
      removeAttribute() {}, load() {}, remove() { this.removed = true; },
    };
    Object.defineProperty(video, 'src', { set(value) {
      video.source = value;
      ready = () => video.emit('loadedmetadata');
      if (readyMode === 'immediate') queueMicrotask(ready);
    } });
    return video;
  }
  class Player {
    constructor(id, opts) {
      this.state = 5; this.time = 0; this.rate = 1; this.muted = false; this.duration = duration;
      this.calls = { play: 0, pause: 0, seek: 0, rate: 0 };
      callbacks = opts.events; player = this;
      // 실제 API처럼 준비 전에는 제어 메서드가 없는 객체를 반환한다.
      const methods = ['getDuration', 'getCurrentTime', 'getPlayerState', 'getAvailablePlaybackRates',
        'getPlaybackRate', 'setPlaybackRate', 'playVideo', 'pauseVideo', 'seekTo', 'mute', 'unMute', 'isMuted'];
      const pending = methods.map((key) => [key, this[key]]);
      for (const key of methods) this[key] = undefined;
      ready = () => {
        for (const [key, fn] of pending) this[key] = fn;
        callbacks.onReady({ target: this });
      };
      if (readyMode === 'immediate') queueMicrotask(ready);
      else if (readyMode === 'synchronous') ready();
    }
    mute() { this.muted = true; } unMute() { this.muted = false; } isMuted() { return this.muted; }
    getDuration() { return this.duration; } getCurrentTime() { return this.time; }
    getVideoData() { return { title: 'Test course', author: 'Test author' }; }
    getPlayerState() { return this.state; } getAvailablePlaybackRates() { return rates; }
    getPlaybackRate() { return this.rate; } setPlaybackRate(v) { this.calls.rate++; this.rate = v; }
    playVideo() { this.calls.play++; this.state = 1; }
    pauseVideo() { this.calls.pause++; this.state = 2; }
    seekTo(t) { this.calls.seek++; this.time = t; }
    destroy() { this.destroyed = true; }
  }
  const YT = { Player, PlayerState: { PLAYING: 1, PAUSED: 2, BUFFERING: 3, ENDED: 0 } };
  const context = vm.createContext({ console, URLSearchParams, queueMicrotask, YT,
    performance: { now: () => clock }, location: { search: query, origin: 'http://localhost:8000', reload() {} },
    fetch: async () => ({ ok: true, json: async () => ({ keyframes: [
      { time: 0, rows: [[.6,.4,.6],[.95,.1,.9]] }, { time: 3600, rows: [[.6,.3,.5],[.95,.1,.8]] },
    ] }) }),
    localStorage: { getItem() { return null; }, setItem() {}, removeItem() {} },
    setTimeout: () => 1, clearTimeout() {}, requestAnimationFrame: (fn) => { frame = fn; },
    document: { getElementById: el, hidden: false, createElement, head: { append() {} }, addEventListener() {} },
    window: { YT, devicePixelRatio: 1, addEventListener() {} },
  });
  const cache = new Map();
  async function load(file) {
    if (cache.has(file)) return cache.get(file);
    let mod;
    if (file.endsWith('video-runners.js')) mod = new vm.SyntheticModule(['createVideoRunners'], function () {
      this.setExport('createVideoRunners', async () => ({ resize() {}, update(dt, current) { race = current; } }));
    }, { context, identifier: file });
    else mod = new vm.SourceTextModule(await readFile(file, 'utf8'), { context, identifier: file });
    cache.set(file, mod);
    await mod.link((specifier, parent) => load(resolve(parent.identifier, '..', specifier)));
    return mod;
  }
  await (await load(resolve(root, 'video.js'))).evaluate();
  function tick(seconds) {
    for (let i = 0; i < Math.ceil(seconds * 30); i++) {
      clock += 1000 / 30;
      if (nativeVideo && !nativeVideo.paused) nativeVideo.currentTime += nativeVideo.playbackRate / 30;
      if (player?.state === 1) player.time += player.rate / 30;
      frame(clock);
    }
  }
  tick(0.1);
  return { el, tick, ready, get video() { return nativeVideo; }, get player() { return player; }, get callbacks() { return callbacks; }, get race() { return race; } };
}
const loading = await setup('', 3600, 'delayed');
loading.tick(2);
assert.equal(loading.el('start').disabled, true);
assert.equal(loading.race.state, 'ready');
assert.equal(loading.race.me.distance, 0);
loading.ready();
loading.el('start').click(); loading.tick(4);
assert.equal(loading.race.state, 'running');
assert.equal(loading.player.state, 1);
const eager = await setup('?autostart=1', 3600, 'synchronous');
eager.tick(4);
assert.equal(eager.race.state, 'running');
const app = await setup();
assert.equal(app.el('start').disabled, false);
app.el('start').click(); app.tick(2);
assert.deepEqual(app.player.calls, { play: 0, pause: 0, seek: 0, rate: 0 });
app.tick(2);
assert.equal(app.race.state, 'running'); assert.ok(app.race.me.distance > 0); assert.equal(app.player.state, 1);
app.el('faster').click(); app.tick(1); assert.equal(app.race.me.speedKmh, 9.5);
app.el('pause').click(); const beforePause = app.race.me.distance; app.tick(2);
assert.equal(app.race.me.distance, beforePause); assert.equal(app.player.state, 2);
app.el('pause').click(); app.tick(1); assert.ok(app.race.me.distance > beforePause);
app.race.me.source.speedKmh = 0; app.tick(1); assert.equal(app.player.state, 2);
app.race.me.source.speedKmh = 10; app.tick(1); assert.equal(app.player.state, 1);
app.el('toggle-runners').click(); assert.equal(app.el('runners').hidden, true);
app.el('tune').click(); assert.equal(app.el('alignment').hidden, false);
app.el('close-tune').click(); assert.equal(app.el('alignment').hidden, true);
app.el('sound').click(); assert.equal(app.player.isMuted(), false);
app.callbacks.onAutoplayBlocked(); app.tick(1); assert.equal(app.el('pause').textContent, '계속 달리기');
app.el('pause').click(); app.tick(1); assert.equal(app.player.state, 1);
app.callbacks.onError({ data: 101 }); const beforeFailure = app.race.me.distance; app.tick(1);
assert.equal(app.race.me.distance, beforeFailure); assert.ok(app.el('error').textContent.includes('외부 재생'));
assert.equal(app.el('video-recovery').hidden, false);
const oldCallbacks = app.callbacks;
const oldPlayer = app.player;
app.el('retry-video').click();
await Promise.resolve();
assert.equal(oldPlayer.destroyed, true);
assert.equal(app.el('video-recovery').hidden, true);
assert.equal(app.el('pause').textContent, '계속 달리기');
oldCallbacks.onError({ data: 5 });
assert.equal(app.el('error').textContent, '');
app.el('pause').click(); app.tick(1);
assert.equal(app.player.state, 1);
app.player.state = 3;
const beforeBuffering = { ...app.player.calls };
app.tick(2);
assert.deepEqual(app.player.calls, beforeBuffering);
app.callbacks.onError({ data: 5 });
assert.ok(app.el('error').textContent.includes('HTML5'));
const delayed = await setup('', 0);
assert.equal(delayed.el('start').disabled, false);
delayed.el('start').click(); delayed.tick(4); delayed.player.duration = 120; delayed.tick(2);
assert.ok(Math.abs(delayed.race.length - (120 - 30) * 10 / 3.6) < 0.01); // 산길 영상은 어느 모드든 30초부터
const invalid = await setup('?from=500', 120);
assert.equal(invalid.el('start').disabled, true); assert.ok(invalid.el('error').textContent.includes('시작 위치'));
const finishing = await setup('?km=0.1&speed=20');
finishing.el('start').click(); finishing.tick(25);
assert.equal(finishing.race.state, 'finished'); assert.ok(finishing.el('overlay').innerHTML.includes('완주!'));
assert.equal(finishing.player.state, 2);
const local = await setup('', 3600, 'immediate', 'local');
assert.equal(local.video.controls, false);
assert.equal(local.video.currentTime, 30);
assert.equal(local.el('start').disabled, false);
local.el('start').click(); local.tick(5);
assert.equal(local.video.paused, false);
assert.ok(local.video.playbackRate > .8 && local.video.playbackRate < 1);
local.el('faster').click(); local.tick(4);
assert.ok(local.video.playbackRate > .9 && local.video.playbackRate < 1);
local.el('tune').click(); local.el('edit-road').click();
const editDistance = local.race.me.distance, editVideoTime = local.video.currentTime;
assert.equal(local.el('road-editor').hidden, false);
local.tick(3);
assert.equal(local.race.me.distance, editDistance);
assert.equal(local.video.currentTime, editVideoTime);
local.el('edit-road').click();
assert.equal(local.el('road-editor').hidden, true);
local.el('pause').click(); local.tick(2);
assert.ok(local.race.me.distance > editDistance);
local.video.error = { code: 4 }; local.video.emit('error');
assert.ok(local.el('error').textContent.includes('다운로드 영상'));
local.el('retry-video').click(); await Promise.resolve();
assert.equal(local.el('error').textContent, '');
local.el('pause').click(); local.tick(2);
assert.equal(local.video.paused, false);
const delayedLocal = await setup('', 3600, 'delayed', 'local');
delayedLocal.tick(2); assert.equal(delayedLocal.el('start').disabled, true);
delayedLocal.ready(); assert.equal(delayedLocal.el('start').disabled, false);
console.log(`PASS: 지연/동기 플레이어 초기화, 동기화(최대 ${maxError.toFixed(2)}초), 시작/정지/재개/완주, 지연 메타데이터, YouTube 오류, UI 조작`);
console.log('PASS: 실제 로컬 어댑터로 초기화, 연속 배속, 길 편집 중 정지, 재개, 파일 오류/재시도');
