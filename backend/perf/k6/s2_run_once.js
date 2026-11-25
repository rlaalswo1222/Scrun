// S2 달리기 한 번(계획 0070): 맵 입장 → 러닝 시작 → 시계열 여러 번 → 종료 → 인증 건너뛰기. 쓰기 위주.
// 실제 앱은 30분 동안 60초마다 시계열을 올리지만, 여기선 묶음 SAMPLES개를 0.5초 간격으로 몰아서 보냄(서버 처리량을 보려고)
// 실행: backend/perf/run.sh s2_v20 backend/perf/k6/s2_run_once.js -e VUS=20 -e USERS=10000 -e DURATION=2m
import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE, JSON_HEADERS, TREND_STATS, login, perName, summary } from './common.js';

const VUS = Number(__ENV.VUS || 20);
const SAMPLES = Number(__ENV.SAMPLES || 5);
// 코스마다 맵이 하나, 정원 20명(넘으면 COURSE_FULL). 가상 사용자를 코스 5개에 나눠 넣음
const COURSES = ['crs_space_loop', 'crs_island_tour', 'crs_lake_loop', 'crs_forest_run', 'crs_coast_run'];
const COURSE = __ENV.COURSE || COURSES[(__VU - 1) % COURSES.length];
const NAMES = ['login', 'entry', 'run_start', 'samples', 'finish', 'skip'];

export const options = {
  scenarios: {
    run_once: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: VUS },
        { duration: __ENV.DURATION || '5m', target: VUS },
        { duration: '10s', target: 0 },
      ],
      gracefulRampDown: '30s',
    },
  },
  thresholds: { http_req_failed: ['rate<0.01'], checks: ['rate>0.99'], ...perName(NAMES) },
  summaryTrendStats: TREND_STATS,
};

let auth = null;

function call(method, path, body, name) {
  const r = http.request(method, BASE + path, body ? JSON.stringify(body) : null, {
    headers: { ...JSON_HEADERS, ...auth },
    tags: { name },
  });
  check(r, { [`${name} 2xx`]: (x) => x.status >= 200 && x.status < 300 });
  return r;
}

export default function () {
  if (!auth) {
    auth = login();
    if (!auth) return sleep(5);
  }
  const entry = call('POST', `/v1/courses/${COURSE}/entries`, null, 'entry');
  if (entry.status !== 201) return sleep(2);
  const started = new Date();
  const start = call('POST', '/v1/runs', {
    clientRunId: `k6-${__VU}-${__ITER}-${started.getTime()}`,
    mapInstanceId: entry.json('success.data.mapInstanceId'),
    goal: { type: 'NONE' },
    startedAt: started.toISOString(),
  }, 'run_start');
  if (start.status !== 201 && start.status !== 200) return sleep(2);
  const runId = start.json('success.data.runId');
  for (let i = 0; i < SAMPLES; i++) {
    const speeds = Array.from({ length: 60 }, () => Math.round((8 + Math.random()) * 100) / 100);
    call('PUT', `/v1/runs/${runId}/samples`, { offsetS: i * 60, intervalS: 1, speedsKmh: speeds }, 'samples');
    sleep(0.5);
  }
  // 종료 시각은 미래면 400, 센서 시간은 시작부터 종료까지보다 길면 400(서버 검사) → 실제로 흐른 시간만큼
  const ended = new Date();
  const durationS = Math.floor((ended - started) / 1000);
  call('POST', `/v1/runs/${runId}/finish`, {
    endedAt: ended.toISOString(),
    sensor: { distanceM: Math.round((8.5 / 3.6) * durationS), durationS },
  }, 'finish');
  call('POST', `/v1/runs/${runId}/verification/skip`, { reason: 'SKIPPED' }, 'skip');
  sleep(1 + Math.random() * 2);
}

export function handleSummary(data) {
  return {
    [__ENV.OUT || 's2_summary.json']: JSON.stringify(data, null, 2),
    stdout: summary(`S2 달리기 한 번 VUS=${VUS} SAMPLES=${SAMPLES}`, NAMES, data),
  };
}
