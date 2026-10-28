// 영상 코스 지도: 영상 속 촬영자가 실제로 달린 길(OSM)과 "영상 시각 → 그 길 위 위치" 표.
// 파일: assets/video/{id}.course.json (tools/build_video_course.py가 만든다)
//   route: [[x, y], ...]  길 모양 (미터, x=동쪽, y=북쪽), 2m 간격
//   sync:  { t: [영상 시각(초)...], s: [길 위 위치(m)...] }  s는 줄어들지 않는다
//
// 카메라는 영상 시각의 길 위 위치에서 진행 방향을 바라보고, 러너는 길을 따라 앞의 실제 위치에 선다.
// 그래서 커브에서는 앞선 러너가 옆으로 비켜 보이고, 거리에 맞는 크기로 보인다.

const lerp = (a, b, t) => a + (b - a) * t;

// 오름차순 배열 xs에서 x의 위치를 찾아 ys를 선형 보간
function interp(xs, ys, x) {
  if (x <= xs[0]) return ys[0];
  const n = xs.length;
  if (x >= xs[n - 1]) return ys[n - 1];
  let lo = 0;
  let hi = n - 1;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (xs[mid] <= x) lo = mid;
    else hi = mid;
  }
  const t = (x - xs[lo]) / (xs[hi] - xs[lo] || 1);
  return lerp(ys[lo], ys[hi], t);
}

export class VideoCourse {
  constructor({ route, sync, name }) {
    this.name = name;
    this.pts = route;
    this.cum = [0];
    for (let i = 1; i < route.length; i++) {
      this.cum.push(this.cum[i - 1] + Math.hypot(route[i][0] - route[i - 1][0], route[i][1] - route[i - 1][1]));
    }
    this.length = this.cum[this.cum.length - 1];
    this.t = sync.t;
    this.s = sync.s;
    // s가 같은 값으로 머무는 구간(인트로·멈춤)은 s -> t 역방향이 모호하므로 그 구간의 마지막 시각을 쓴다.
    // (첫 시각을 쓰면 출발점 0m가 영상 0초 = 인트로로 되감긴다)
    this.sUnique = [];
    this.tUnique = [];
    for (let i = 0; i < this.s.length; i++) {
      const n = this.sUnique.length;
      if (n && this.s[i] <= this.sUnique[n - 1] + 1e-6) this.tUnique[n - 1] = this.t[i];
      else {
        this.sUnique.push(this.s[i]);
        this.tUnique.push(this.t[i]);
      }
    }
  }

  static async load(videoId) {
    const res = await fetch(`assets/video/${videoId}.course.json`);
    if (!res.ok) return null;
    return new VideoCourse(await res.json());
  }

  sAt(time) { return interp(this.t, this.s, time); }
  timeAt(s) { return interp(this.sUnique, this.tUnique, s); }

  // 영상 시각 근처에서 촬영자가 달린 속도 (km/h). 재생 속도 계산용
  filmedKmhAt(time) {
    const ds = this.sAt(time + 5) - this.sAt(time - 5);
    return Math.max(1, (ds / 10) * 3.6);
  }

  point(s) {
    const c = Math.max(0, Math.min(this.length, s));
    const xs = this.cum;
    let lo = 0;
    let hi = xs.length - 1;
    while (hi - lo > 1) {
      const mid = (lo + hi) >> 1;
      if (xs[mid] <= c) lo = mid;
      else hi = mid;
    }
    const t = (c - xs[lo]) / (xs[hi] - xs[lo] || 1);
    return [lerp(this.pts[lo][0], this.pts[hi][0], t), lerp(this.pts[lo][1], this.pts[hi][1], t)];
  }

  // 진행 방향(단위 벡터). 앞뒤 여러 지점을 평균해서 GPS·지도 선의 잔떨림을 줄인다
  heading(s, back = 3, ahead = 15) {
    const [ax, ay] = this.point(s - back);
    const [bx, by] = this.point(s + ahead);
    const n = Math.hypot(bx - ax, by - ay) || 1;
    return [(bx - ax) / n, (by - ay) / n];
  }

  // 카메라(영상 시각 time)에서 본 길 위 위치(sTarget)와 좌우 비켜섬(lane, m, 오른쪽 +)
  // 반환: { right, forward } (m) — 카메라 기준 오른쪽, 앞쪽
  toCamera(time, sTarget, lane = 0) {
    const sCam = this.sAt(time);
    const [cx, cy] = this.point(sCam);
    const [hx, hy] = this.heading(sCam);
    const [px, py] = this.point(sTarget);
    const [tx, ty] = this.heading(sTarget, 2, 2);
    // 러너 자리: 그 지점 진행 방향의 오른쪽으로 lane m
    const rx = px + ty * lane;
    const ry = py - tx * lane;
    const dx = rx - cx;
    const dy = ry - cy;
    return { forward: dx * hx + dy * hy, right: dx * hy - dy * hx };
  }

  // 카메라에서 러너까지의 시선이 길에서 크게 벗어나 진행 방향 오른쪽(육지·언덕·절벽 쪽)을 지나면 가려짐.
  // 이 영상은 왼쪽이 바다라서 왼쪽으로 벗어난 시선(바다 건너)은 보인다고 본다.
  occluded(time, sTarget, landSide = 1, margin = 12) {
    const sCam = this.sAt(time);
    const [cx, cy] = this.point(sCam);
    const [px, py] = this.point(sTarget);
    const steps = 12;
    for (let k = 1; k < steps; k++) {
      const f = k / steps;
      const qx = lerp(cx, px, f);
      const qy = lerp(cy, py, f);
      // 시선 위 점에 가장 가까운 길 위치를 카메라~러너 사이에서 찾는다
      let best = Infinity;
      let side = 0;
      for (let s = sCam; s <= sTarget; s += 3) {
        const [ox, oy] = this.point(s);
        const d = Math.hypot(qx - ox, qy - oy);
        if (d < best) {
          best = d;
          const [hx, hy] = this.heading(s, 2, 2);
          side = Math.sign((qx - ox) * hy - (qy - oy) * hx); // 오른쪽 +
        }
      }
      if (best > margin && side === landSide) return true;
    }
    return false;
  }

  // 미니맵용: s0부터 length m 구간의 점 목록
  slice(s0, length, step = 10) {
    const out = [];
    for (let s = s0; s < s0 + length; s += step) out.push(this.point(s));
    out.push(this.point(s0 + length));
    return out;
  }
}
