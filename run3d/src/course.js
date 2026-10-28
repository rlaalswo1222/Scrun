// 코스 경로: "출발점에서 몇 m"를 3D 좌표와 진행 방향으로 바꿔 준다.
// 좌표 규칙: course.json은 (x=동, y=북) 미터, three.js는 (x=동, y=위, z=남).

export class CoursePath {
  constructor(route, lengthM) {
    this.pts = route.map(([x, y]) => [x, -y]);
    this.cum = [0];
    for (let i = 1; i < this.pts.length; i++) {
      const [ax, az] = this.pts[i - 1];
      const [bx, bz] = this.pts[i];
      this.cum.push(this.cum[i - 1] + Math.hypot(bx - ax, bz - az));
    }
    // 점을 일정 간격으로 찍으면서 생긴 미세한 길이 차이를 공식 거리(5km)에 맞춘다
    this.length = lengthM;
    this.scale = this.cum[this.cum.length - 1] / lengthM;
  }

  // 거리 d(m) 지점의 (x, z)
  point(d) {
    const s = Math.min(Math.max(d, 0), this.length) * this.scale;
    let lo = 0;
    let hi = this.cum.length - 1;
    while (hi - lo > 1) {
      const mid = (lo + hi) >> 1;
      if (this.cum[mid] <= s) lo = mid;
      else hi = mid;
    }
    const t = (s - this.cum[lo]) / (this.cum[hi] - this.cum[lo] || 1);
    const [ax, az] = this.pts[lo];
    const [bx, bz] = this.pts[hi];
    return [ax + (bx - ax) * t, az + (bz - az) * t];
  }

  // 거리 d 지점의 위치와 진행 방향. span으로 앞뒤를 보고 방향을 정해서 흔들림을 줄인다
  frame(d, lateral = 0, span = 3) {
    const [ax, az] = this.point(d - span);
    const [bx, bz] = this.point(d + span);
    let tx = bx - ax;
    let tz = bz - az;
    const n = Math.hypot(tx, tz) || 1;
    tx /= n;
    tz /= n;
    const [x, z] = this.point(d);
    // 진행 방향의 오른쪽으로 lateral만큼 비켜선 위치
    return { x: x - tz * lateral, z: z + tx * lateral, tx, tz };
  }
}
