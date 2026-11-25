// S3 실시간 같이 뛰기(계획 0070): 맵 입장 → WebSocket(/v1/realtime) → 0.5초마다 내 상태, 5초마다 ping → HOLD초 뒤 leave.
// 맵은 코스마다 하나(정원 20명). VUS를 코스 5개에 나누므로 100명까지 들어감.
// 요청 로그(HTTP 미들웨어)에는 WebSocket이 안 남으므로 왕복(ws_rtt)·받은 상태 묶음 수는 여기서 잼
// 실행: backend/perf/run.sh s3_v40 backend/perf/k6/s3_realtime.js -e VUS=40 -e USERS=10000 -e HOLD=60
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';
import { WebSocket } from 'k6/websockets';
import { setInterval, clearInterval, setTimeout } from 'k6/timers';
import { BASE, TREND_STATS, login, perName, summary } from './common.js';

const VUS = Number(__ENV.VUS || 20);
const HOLD = Number(__ENV.HOLD || 60);
// 코스마다 맵이 하나, 정원 20명(넘으면 COURSE_FULL). 가상 사용자를 코스 5개에 나눠 넣음
const COURSES = ['crs_space_loop', 'crs_island_tour', 'crs_lake_loop', 'crs_forest_run', 'crs_coast_run'];
const COURSE = __ENV.COURSE || COURSES[(__VU - 1) % COURSES.length];
const rtt = new Trend('ws_rtt', true);
const statesIn = new Counter('ws_states_in');
const closedEarly = new Counter('ws_closed_early');

export const options = {
  scenarios: {
    realtime: { executor: 'per-vu-iterations', vus: VUS, iterations: 1, maxDuration: `${HOLD + 120}s` },
  },
  thresholds: { http_req_failed: ['rate<0.01'], checks: ['rate>0.99'], ws_rtt: ['p(95)<600000'], ...perName(['login', 'entry']) },
  summaryTrendStats: TREND_STATS,
};

export default function () {
  const auth = login();
  if (!auth) return;
  const entry = http.post(`${BASE}/v1/courses/${COURSE}/entries`, null, { headers: auth, tags: { name: 'entry' } });
  if (!check(entry, { 'entry 201': (r) => r.status === 201 })) return;
  const rt = entry.json('success.data.realtime');
  let d = Number(entry.json('success.data.entryPositionM') || 0);
  const kmh = 7 + Math.random() * 5;
  let done = false;

  const ws = new WebSocket(rt.url, null, { headers: { Authorization: `Bearer ${rt.token}` } });
  ws.onopen = () => {
    let last = Date.now();
    const tick = setInterval(() => {
      const now = Date.now();
      d += (kmh / 3.6) * ((now - last) / 1000);
      last = now;
      ws.send(JSON.stringify({ type: 'state', d: Math.round(d * 100) / 100, v: kmh, running: true, run: Math.round(d) }));
    }, 500);
    const ping = setInterval(() => ws.send(JSON.stringify({ type: 'ping', t: Date.now() })), 5000);
    setTimeout(() => {
      clearInterval(tick);
      clearInterval(ping);
      done = true;
      ws.send(JSON.stringify({ type: 'leave' }));
      ws.close();
    }, HOLD * 1000);
  };
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data);
    if (m.type === 'pong') rtt.add(Date.now() - m.t);
    else if (m.type === 'states') statesIn.add(1);
  };
  ws.onclose = () => {
    if (!done) closedEarly.add(1);
  };
  ws.onerror = () => closedEarly.add(1);
}

export function handleSummary(data) {
  return {
    [__ENV.OUT || 's3_summary.json']: JSON.stringify(data, null, 2),
    stdout: summary(`S3 실시간 VUS=${VUS} HOLD=${HOLD}s`, ['login', 'entry'], data, ['ws_rtt', 'ws_states_in', 'ws_closed_early', 'ws_sessions']),
  };
}
