// node tools/check_video_road.mjs
// 길 키프레임 자료구조(경계 보간, 키프레임 사이 연속성, 보정 저장)와 실제 추적 데이터의 유효성을 검사한다.
// 러너를 어디에 세우는지는 check_video_projection.mjs에서 검사한다.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { VideoRoad, sampleRows, validRows } from '../run3d/src/video-road.js';
import { playbackPlan } from '../run3d/src/video-sync.js';

const a = [[.6,.4,.6],[.9,.1,.8]], b = [[.6,.5,.7],[.9,.2,.9]];
const road = new VideoRoad([{ time: 0, rows:a },{ time:10, rows:b }]);
assert.deepEqual(road.bounds(5,.6), [.45,.6499999999999999]);
assert.equal(road.bounds(5,.5), null);
// 맨 윗줄 높이가 다른 키프레임 사이에서도 보이는 길 끝이 갑자기 튀지 않는다
const uneven = new VideoRoad([{time:0,rows:a},{time:10,rows:[[.7,.55,.65],[.96,.1,.8]]}]);
const beforeKey = uneven.projectionRowsAt(10-1e-8), onKey = uneven.projectionRowsAt(10);
assert.ok(Math.abs(beforeKey[0][0]-onKey[0][0]) < 1e-7);
const middle = uneven.projectionRowsAt(5);
assert.ok(middle[0][0] > .6 && middle[0][0] < .7, '보이는 길 끝은 두 키프레임 사이로 보간');
assert.equal(sampleRows([], .8), null);
assert.equal(validRows([[.8,.5,.4],[.9,.2,.8]]), false);
assert.equal(validRows([[.9,.2,.8],[.8,.2,.8]]), false);
assert.equal(road.set(NaN,a), false);
assert.equal(road.set(5,a), true);
assert.deepEqual(road.bounds(5,.6), [.4,.6]);
assert.ok(playbackPlan({ distance: 0, currentTime: 0, filmedKmh:10, speedKmh:9, continuous:true }).rate === .9);

const data = JSON.parse(await readFile(new URL(`../run3d/assets/video/${process.env.VIDEO || 'o0EmQXATQQI'}.road.json`,import.meta.url)));
const track = new VideoRoad(data.keyframes);
assert.equal(track.keys.length, data.keyframes.length, '모든 키프레임이 유효해야 함 (폭 1.5% 미만 줄 없음)');
assert.ok(track.keys.length > 1000);
for (let time=0;time<data.duration;time+=.5) {
  const rows = track.projectionRowsAt(time);
  assert.ok(rows && rows.length >= 2, `${time}s 길 정보 없음`);
  for (let i=0;i<rows.length;i++) {
    const [y,l,r] = rows[i];
    assert.ok(l>=0 && r<=1 && l<r, `${time}s 경계 이상`);
    if (i) assert.ok(y > rows[i-1][0], `${time}s 줄 순서`);
  }
}
console.log(`PASS: ${track.keys.length}개 길 키프레임 검증, 경계 연속성, 보이는 길 끝 보간, 보정 저장, 연속 배속`);
