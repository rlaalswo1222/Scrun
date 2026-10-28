// 3D 복원(SfM) 데이터: 장면마다 "촬영자가 나중에 지나간 발밑"을 카메라 기준 3D(m)로 담은 표.
// 파일: assets/video/{id}.sfm.json (tools/sfm_video.py가 만든다)
//   frames[i][k] = [x, y, z, rx, ry, rz, occ]  장면 i에서 앞으로 (k+1)*stepM m 지점의 발밑 위치, 길 오른쪽 방향,
//                  지형(3D 점)에 가려진 몸 높이(m, 2 = 완전히 가려짐)
//   좌표: three.js 카메라 기준 (x 오른쪽, y 위, 앞은 -z). 영상과 같은 화각(fov)의 카메라로 그리면 그대로 맞는다.

export class VideoSfm {
  constructor({ fps, t0, fov, stepM, frames }) {
    Object.assign(this, { fps, t0, fov, stepM, frames });
  }

  static async load(videoId) {
    const res = await fetch(`assets/video/${videoId}.sfm.json`);
    if (!res.ok) return null;
    return new VideoSfm(await res.json());
  }

  // 한 장면에서 앞으로 d m 지점: [x, y, z, rx, ry, rz] (없으면 null)
  static at(rows, d, step) {
    if (!rows?.length) return null;
    const f = d / step - 1;
    if (f < 0) {
      // 첫 점(2m)보다 가까우면 카메라 발밑(높이는 첫 점과 같게)과 첫 점 사이를 잇는다
      const [x, y, z, ...r] = rows[0];
      const u = d / step;
      return [x * u, y, z * u, ...r];
    }
    const k = Math.floor(f);
    if (k + 1 >= rows.length) return k < rows.length && f === k ? rows[k] : null;
    const u = f - k;
    return rows[k].map((v, i) => v + (rows[k + 1][i] - v) * u);
  }

  // 영상 시각 time에 앞으로 d m, 길 가운데에서 오른쪽으로 lane m 비켜선 발 위치. 두 장면 사이는 보간
  place(time, d, lane = 0) {
    const f = (time - this.t0) * this.fps;
    const i = Math.floor(f);
    if (i < 0 || i >= this.frames.length) return null;
    const a = VideoSfm.at(this.frames[i], d, this.stepM);
    const b = VideoSfm.at(this.frames[Math.min(i + 1, this.frames.length - 1)], d, this.stepM);
    const p = a && b ? a.map((v, k) => v + (b[k] - v) * (f - i)) : a || b;
    if (!p) return null;
    const [x, y, z, rx, ry, rz, occ = 0] = p;
    // occ: 아래에서부터 지형에 가려진 몸 높이 (m). 2 이상이면 완전히 가려짐
    return { x: x + rx * lane, y: y + ry * lane, z: z + rz * lane, occ };
  }

  covers(time) {
    const i = Math.round((time - this.t0) * this.fps);
    return i >= 0 && i < this.frames.length && this.frames[i].length > 0;
  }
}
