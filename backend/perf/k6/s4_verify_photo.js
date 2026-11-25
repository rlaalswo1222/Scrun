// S4 계기판 인증(계획 0070): 러닝을 짧게 시작·종료하고 계기판 사진 3장(multipart)을 올린다. 동시 VUS명(1·2·4·8).
// 사진은 합성 계기판(backend/tests/data/console_synth_sample.jpg). 서버는 다른 기록에 쓴 사진(같은 해시)을 못 읽은 것으로
// 보므로 요청마다 JPEG 끝(EOI 뒤, 디코더가 무시)에 무작위 바이트를 붙여 해시를 바꾼다. 읽은 값이 맞는지는 이 시험의 관심이 아님
// 실행: backend/perf/run.sh s4_v4 backend/perf/k6/s4_verify_photo.js -e VUS=4 -e USERS=10000 -e DURATION=2m
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter } from 'k6/metrics';
import { BASE, JSON_HEADERS, TREND_STATS, login, perName, summary } from './common.js';

const VUS = Number(__ENV.VUS || 1);
const PHOTO = open(__ENV.PHOTO || '../../tests/data/console_synth_sample.jpg', 'b');
// 코스마다 맵이 하나, 정원 20명(넘으면 COURSE_FULL). 가상 사용자를 코스 5개에 나눠 넣음
const COURSES = ['crs_space_loop', 'crs_island_tour', 'crs_lake_loop', 'crs_forest_run', 'crs_coast_run'];
const COURSE = __ENV.COURSE || COURSES[(__VU - 1) % COURSES.length];
const NAMES = ['login', 'entry', 'run_start', 'finish', 'photo'];
const outcome = new Counter('photo_outcome');
const OUTCOMES = ['CONFIRM_REQUIRED', 'RETAKE_REQUIRED', 'UNVERIFIED', 'VERIFIED', 'HTTP_503', 'HTTP_400', 'HTTP_409', 'HTTP_500'];

export const options = {
  scenarios: {
    verify: { executor: 'constant-vus', vus: VUS, duration: __ENV.DURATION || '3m' },
  },
  thresholds: {   // 503(밀림)도 결과로 보므로 실패율 문턱은 두지 않음. 결과별 횟수를 요약에 남기려고 넉넉한 문턱
    checks: ['rate>0'],
    ...perName(NAMES),
    ...Object.fromEntries(OUTCOMES.map((o) => [`photo_outcome{outcome:${o}}`, ['count>=0']])),
  },
  summaryTrendStats: TREND_STATS,
};

let auth = null;

function unique(buf) {
  const src = new Uint8Array(buf);
  const out = new Uint8Array(src.length + 16);
  out.set(src);
  for (let i = 0; i < 16; i++) out[src.length + i] = Math.floor(Math.random() * 256);
  return out.buffer;
}

// 같은 이름(photos)으로 여러 장: k6 기본 multipart는 이름당 한 개라 몸통을 직접 만든다
const CRLF = '\r\n';
function multipart(boundary, count) {
  const enc = (t) => Uint8Array.from(unescape(encodeURIComponent(t)), (c) => c.charCodeAt(0));
  const parts = [];
  for (let i = 0; i < count; i++) {
    parts.push(enc(`--${boundary}${CRLF}Content-Disposition: form-data; name="photos"; filename="console${i}.jpg"${CRLF}` +
      `Content-Type: image/jpeg${CRLF}${CRLF}`));
    parts.push(new Uint8Array(unique(PHOTO)));
    parts.push(enc(CRLF));
  }
  parts.push(enc(`--${boundary}--${CRLF}`));
  const out = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
  let at = 0;
  for (const p of parts) {
    out.set(p, at);
    at += p.length;
  }
  return out.buffer;
}

function post(path, body, name) {
  return http.post(BASE + path, body ? JSON.stringify(body) : null, { headers: { ...JSON_HEADERS, ...auth }, tags: { name } });
}

export default function () {
  if (!auth) {
    auth = login(VUS * 1000);   // S1·S2와 다른 계정을 쓰게 띄움
    if (!auth) return sleep(5);
  }
  const entry = post(`/v1/courses/${COURSE}/entries`, null, 'entry');
  if (entry.status !== 201) return sleep(2);
  const started = new Date();   // 맵 입장보다 앞이면 400(서버 검사)
  const start = post('/v1/runs', {
    clientRunId: `k6s4-${__VU}-${__ITER}-${Date.now()}`,
    mapInstanceId: entry.json('success.data.mapInstanceId'),
    goal: { type: 'NONE' },
    startedAt: started.toISOString(),
  }, 'run_start');
  if (start.status !== 200 && start.status !== 201) return sleep(2);
  const runId = start.json('success.data.runId');
  // 종료 시각은 미래면 400, 센서 시간은 시작부터 종료까지보다 길면 400(서버 검사) → 실제로 흐른 시간만큼
  const ended = new Date();
  const durationS = Math.floor((ended - started) / 1000);
  post(`/v1/runs/${runId}/finish`, { endedAt: ended.toISOString(), sensor: { distanceM: Math.round(durationS * 2.5), durationS } }, 'finish');

  const boundary = `----scrunk6${__VU}x${__ITER}`;
  const r = http.post(`${BASE}/v1/runs/${runId}/verification/photo`, multipart(boundary, 3), {
    headers: { ...auth, 'Content-Type': `multipart/form-data; boundary=${boundary}` }, tags: { name: 'photo' }, timeout: '60s',
  });
  const status = r.status === 200 ? r.json('success.data.status') : `HTTP_${r.status}`;
  outcome.add(1, { outcome: status });
  check(r, { 'photo 200': (x) => x.status === 200 });
  sleep(1);
}

export function handleSummary(data) {
  return {
    [__ENV.OUT || 's4_summary.json']: JSON.stringify(data, null, 2),
    stdout: summary(`S4 계기판 인증 VUS=${VUS}`, NAMES, data, OUTCOMES.map((o) => `photo_outcome{outcome:${o}}`)),
  };
}
