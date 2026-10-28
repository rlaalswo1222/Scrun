// 앞선 러너가 서야 할 화면 위치표 (assets/video/{id}.ahead.json, tools/track_video_ahead.py가 만든다).
// 장면(1초 간격)마다 [거리 m, 길 왼쪽 x, 가운데 x, 오른쪽 x, 발 y] 목록. 촬영자가 나중에 지나간 길 위 점을
// 옵티컬 플로우로 거꾸로 따라온 것이라 커브·오르내리막이 그대로 들어 있다. 목록에 없는 거리는 가려진 곳.

export class VideoAhead {
  constructor({ t0, step, frames }) {
    this.t0 = t0;
    this.step = step;
    this.frames = frames;
  }

  static async load(videoId) {
    const res = await fetch(`assets/video/${videoId}.ahead.json`);
    if (!res.ok) return null;
    return new VideoAhead(await res.json());
  }

  // 한 장면에서 거리 depth의 [xl, xc, xr, y]. 보이는 점 사이 간격이 너무 벌어진 곳(가려진 구간)이면 null
  static at(rows, depth) {
    if (!rows?.length || depth < rows[0][0] || depth > rows[rows.length - 1][0]) return null;
    for (let i = 1; i < rows.length; i++) {
      const a = rows[i - 1];
      const b = rows[i];
      if (depth <= b[0]) {
        if (b[0] - a[0] > 6) return null; // 0.5초(약 1.5m) 간격인데 6m 넘게 비면 그 사이는 가려짐
        const u = (depth - a[0]) / (b[0] - a[0] || 1);
        return [1, 2, 3, 4].map((k) => a[k] + (b[k] - a[k]) * u);
      }
    }
    return null;
  }

  // 영상 시각 time, 거리 depth의 화면 위치. 두 장면 사이를 보간. 반환 { x, y, width } (0~1) 또는 null(가려짐)
  lookup(time, depth, lane = 0) {
    const f = (time - this.t0) / this.step;
    const i = Math.floor(f);
    if (i < 0 || i >= this.frames.length) return null;
    const a = VideoAhead.at(this.frames[i], depth);
    const b = VideoAhead.at(this.frames[Math.min(i + 1, this.frames.length - 1)], depth);
    const p = a && b ? a.map((v, k) => v + (b[k] - v) * (f - i)) : a || b;
    if (!p) return null;
    const [xl, xc, xr, y] = p;
    const half = (xr - xl) / 2;
    // 차선: 길 가운데에서 lane × 길 폭의 22%씩 (가장자리 풀밭·연석에 붙지 않게)
    const x = xc + Math.max(-0.3, Math.min(0.3, lane * 0.22)) * half * 2;
    return { x, y, width: xr - xl };
  }

  // 그 장면에서 보이는 가장 가까운 거리 (이보다 가까운 러너는 화면 아래쪽 너머)
  nearest(time) {
    const i = Math.round((time - this.t0) / this.step);
    return this.frames[i]?.[0]?.[0] ?? Infinity;
  }
}
