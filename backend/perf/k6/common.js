// 시나리오 공통(계획 0070): 주소·로그인·요약 출력
import http from 'k6/http';
import { check } from 'k6';

export const BASE = __ENV.BASE || 'http://192.168.0.2:8000';
export const USERS = Number(__ENV.USERS || 100);
export const KAKAO_BASE = Number(__ENV.KAKAO_BASE || 100000);
export const JSON_HEADERS = { 'Content-Type': 'application/json' };

// 가상 사용자 번호 → seed_load 계정 하나(k_{KAKAO_BASE + i}). 실패하면 null
export function login(offset = 0) {
  const kakao = `k_${KAKAO_BASE + ((__VU - 1 + offset) % USERS)}`;
  const r = http.post(`${BASE}/v1/auth/kakao`, JSON.stringify({ kakaoAccessToken: kakao }), {
    headers: JSON_HEADERS,
    tags: { name: 'login' },
  });
  const data = r.status === 200 ? r.json('success.data') : null;
  const ok = check(r, { 'login 200 + 기존 계정(seed_load)': () => data && !data.isNewUser && data.accessToken });
  return ok ? { Authorization: `Bearer ${data.accessToken}` } : null;
}

// 엔드포인트별 지표를 요약에 남기려면 문턱이 있어야 함(k6 규칙). 판정용이 아니라 넉넉하게
export function perName(names, metric = 'http_req_duration') {
  return Object.fromEntries(names.map((n) => [`${metric}{name:${n}}`, ['p(95)<600000']]));
}

export function summary(title, names, data, extra = []) {
  const lines = [`${title} 반복 ${data.metrics.iterations ? data.metrics.iterations.values.count : 0}`];
  for (const name of names) {
    const m = data.metrics[`http_req_duration{name:${name}}`];
    if (m) {
      const v = m.values;
      lines.push(`${name.padEnd(16)} p50 ${v.med.toFixed(1)}  p95 ${v['p(95)'].toFixed(1)}  p99 ${v['p(99)'].toFixed(1)} ms  (${m.values.count ?? ''})`);
    }
  }
  for (const key of extra) {
    const m = data.metrics[key];
    if (m) lines.push(`${key.padEnd(16)} ${JSON.stringify(m.values)}`);
  }
  const f = data.metrics.http_req_failed;
  const reqs = data.metrics.http_reqs;
  lines.push(`실패율 ${(f ? f.values.rate * 100 : 0).toFixed(2)}%  초당 요청 ${reqs ? reqs.values.rate.toFixed(1) : 0}`);
  return lines.join('\n') + '\n';
}

export const TREND_STATS = ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'];
