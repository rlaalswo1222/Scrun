// S1 앱 켜기(계획 0070): 로그인한 사용자가 앱을 열고 홈 → 기록 → 친구 탭을 훑는다. 읽기 위주.
// 실행(노트북): k6 run -e BASE=http://192.168.0.2:8000 -e VUS=20 -e USERS=100 backend/perf/k6/s1_open_app.js
//   VUS: 동시 가상 사용자, USERS: seed_load --users 값(로그인 계정 k_{100000 + 0…USERS−1}), DURATION: 유지 시간(기본 5m)
//   OUT: 요약 JSON 경로(기본 s1_summary.json). 웹 대시보드 HTML: K6_WEB_DASHBOARD=true K6_WEB_DASHBOARD_EXPORT=s1.html
// 가상 사용자 하나 = 앱 사용자 한 명. 처음 한 번 로그인하고, 앱 열기 한 바퀴 → 2–5초 쉼을 반복한다.
import http from 'k6/http';
import { check, sleep } from 'k6';

const BASE = __ENV.BASE || 'http://192.168.0.2:8000';
const VUS = Number(__ENV.VUS || 20);
const USERS = Number(__ENV.USERS || 100);
const KAKAO_BASE = Number(__ENV.KAKAO_BASE || 100000);
const DURATION = __ENV.DURATION || '5m';

// 앱이 여는 순서(App/Api/Api.cs): 내 정보 → 홈 → 끝나지 않은 러닝 → 기록 탭 → 친구 탭(목록·달리는 친구·피드) → 진행 상태
const OPEN_APP = [
  ['me', '/v1/users/me'],
  ['home', '/v1/home'],
  ['runs_active', '/v1/runs/active'],
  ['runs', '/v1/runs'],
  ['friends', '/v1/friends'],
  ['friends_running', '/v1/friends/running'],
  ['feed', '/v1/feed'],
  ['progress', '/v1/users/me/progress'],
];

export const options = {
  scenarios: {
    open_app: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: VUS },
        { duration: DURATION, target: VUS },
        { duration: '10s', target: 0 },
      ],
      gracefulRampDown: '10s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    checks: ['rate>0.99'],
    // 엔드포인트별 지표를 요약에 남기려면 문턱이 있어야 함(k6 규칙). 판정용이 아니라 넉넉하게
    ...Object.fromEntries(['login', ...OPEN_APP.map(([n]) => n)].map((n) => [`http_req_duration{name:${n}}`, ['p(95)<60000']])),
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

let auth = null;

function login() {
  const kakao = `k_${KAKAO_BASE + ((__VU - 1) % USERS)}`;
  const r = http.post(`${BASE}/v1/auth/kakao`, JSON.stringify({ kakaoAccessToken: kakao }), {
    headers: { 'Content-Type': 'application/json' },
    tags: { name: 'login' },
  });
  const data = r.status === 200 ? r.json('success.data') : null;
  const ok = check(r, { 'login 200 + 기존 계정(seed_load)': () => data && !data.isNewUser && data.accessToken });
  return ok ? { headers: { Authorization: `Bearer ${data.accessToken}` } } : null;
}

export default function () {
  if (!auth) {
    auth = login();
    if (!auth) {
      sleep(5);
      return;
    }
  }
  for (const [name, path] of OPEN_APP) {
    const r = http.get(BASE + path, { ...auth, tags: { name } });
    check(r, { [`${name} 200`]: (x) => x.status === 200 });
  }
  sleep(2 + Math.random() * 3);
}

export function handleSummary(data) {
  return { [__ENV.OUT || 's1_summary.json']: JSON.stringify(data, null, 2), stdout: textSummary(data) };
}

// 엔드포인트별 p50·p95·p99와 오류율만 짧게(기본 요약은 JSON 파일에)
function textSummary(data) {
  const lines = [`S1 앱 켜기 VUS=${VUS} USERS=${USERS} 반복 ${data.metrics.iterations ? data.metrics.iterations.values.count : 0}`];
  for (const [name] of [['login'], ...OPEN_APP]) {
    const m = data.metrics[`http_req_duration{name:${name}}`];
    if (m) {
      const v = m.values;
      lines.push(`${name.padEnd(16)} p50 ${v.med.toFixed(1)}  p95 ${v['p(95)'].toFixed(1)}  p99 ${v['p(99)'].toFixed(1)} ms`);
    }
  }
  const f = data.metrics.http_req_failed;
  lines.push(`실패율 ${(f ? f.values.rate * 100 : 0).toFixed(2)}%  초당 요청 ${data.metrics.http_reqs.values.rate.toFixed(1)}`);
  return lines.join('\n') + '\n';
}
