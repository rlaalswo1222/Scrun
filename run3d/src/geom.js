// 지도 도형을 three.js 형상으로 바꾸는 공용 함수들.
// 좌표 규칙: course.json은 (x=동, y=북) 미터, three.js는 (x=동, y=위, z=남).
import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';

export const v2 = ([x, y]) => new THREE.Vector2(x, y);

// 다각형들(구멍 포함) -> 바닥 평면. ShapeGeometry는 도형 좌표(미터)를 그대로 uv로 쓰므로 텍스처 반복이 바로 맞는다
export function polygonsGeometry(polys) {
  const geoms = polys.map((rings) => {
    const shape = new THREE.Shape(rings[0].map(v2));
    shape.holes = rings.slice(1).map((r) => new THREE.Path(r.map(v2)));
    return new THREE.ShapeGeometry(shape);
  });
  if (!geoms.length) return null;
  const g = mergeGeometries(geoms);
  g.rotateX(-Math.PI / 2); // (x, 북) 평면 -> (x, -z) 바닥
  return g;
}

export const newRibbon = () => ({ pos: [], uv: [], idx: [] });

// 선(점 목록)을 폭 width의 띠로 만든다. thick > 0이면 옆면·밑면까지 만든 상판(다리)
export function pushRibbon(out, pts, width, heights, thick = 0) {
  const n = pts.length;
  if (n < 2) return;
  const base = out.pos.length / 3;
  const rows = thick > 0 ? 4 : 2;
  const vert = (x, h, z) => {
    out.pos.push(x, h, z);
    out.uv.push(x, -z);
  };
  for (let i = 0; i < n; i++) {
    const [px, py] = pts[Math.max(i - 1, 0)];
    const [qx, qy] = pts[Math.min(i + 1, n - 1)];
    let tx = qx - px;
    let tz = -(qy - py);
    const len = Math.hypot(tx, tz) || 1;
    tx /= len;
    tz /= len;
    const [x, y] = pts[i];
    const z = -y;
    const h = heights ? heights[i] : 0;
    const ox = -tz * (width / 2);
    const oz = tx * (width / 2);
    vert(x + ox, h, z + oz);
    vert(x - ox, h, z - oz);
    if (thick > 0) {
      vert(x + ox, h - thick, z + oz);
      vert(x - ox, h - thick, z - oz);
    }
  }
  const quad = (a, b, c, d) => out.idx.push(a, b, c, b, d, c);
  for (let i = 0; i < n - 1; i++) {
    const a = base + i * rows;
    const b = a + rows;
    quad(a, a + 1, b, b + 1); // 윗면
    if (thick > 0) {
      quad(a + 2, a + 3, b + 2, b + 3); // 밑면
      quad(a, a + 2, b, b + 2); // 왼쪽 옆면
      quad(a + 1, a + 3, b + 1, b + 3); // 오른쪽 옆면
    }
  }
}

export function ribbonGeometry(out) {
  if (!out.idx.length) return null;
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(out.pos, 3));
  g.setAttribute('uv', new THREE.Float32BufferAttribute(out.uv, 2));
  g.setIndex(out.idx);
  g.computeVertexNormals();
  return g;
}

// 선을 진행 방향 오른쪽으로 d m 평행 이동 (음수면 왼쪽). 차선·보도 위치 계산용
export function offsetLine(pts, d) {
  const n = pts.length;
  return pts.map(([x, y], i) => {
    const [px, py] = pts[Math.max(i - 1, 0)];
    const [qx, qy] = pts[Math.min(i + 1, n - 1)];
    const len = Math.hypot(qx - px, qy - py) || 1;
    
    return [x + ((qy - py) / len) * d, y - ((qx - px) / len) * d];
  
  });
}

// 선 위 s0~s1 m 구간만 잘라낸 점 목록
function subLine(pts, cum, s0, s1) {
  const at = (s) => {
    let i = 1;
    while (i < pts.length - 1 && cum[i] < s) i++;
    const t = (s - cum[i - 1]) / (cum[i] - cum[i - 1] || 1);
    return [pts[i - 1][0] + (pts[i][0] - pts[i - 1][0]) * t, pts[i - 1][1] + (pts[i][1] - pts[i - 1][1]) * t];
  };
  const out = [at(s0)];
  for (let i = 0; i < pts.length; i++) if (cum[i] > s0 && cum[i] < s1) out.push(pts[i]);
  out.push(at(s1));
  return out;
}

// 점선: dash m 그리고 gap m 비우기를 반복
export function pushDashes(out, pts, width, dash, gap) {
  const cum = [0];
  for (let i = 1; i < pts.length; i++) cum.push(cum[i - 1] + Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]));
  const total = cum[cum.length - 1];
  for (let s = gap / 2; s + dash <= total; s += dash + gap) pushRibbon(out, subLine(pts, cum, s, s + dash), width, null);
}

// 지도 좌표 선 -> three.js 점 목록 + 누적 거리. at(s)로 s m 지점의 위치·진행 방향을 얻는다
export function polylineWalker(line, y = 0) {
  const pts = line.map(([x, n]) => new THREE.Vector3(x, y, -n));
  const cum = [0];
  for (let i = 1; i < pts.length; i++) cum.push(cum[i - 1] + pts[i].distanceTo(pts[i - 1]));
  const length = cum[cum.length - 1];
  function at(s) {
    s = Math.min(Math.max(s, 0), length);
    let i = 1;
    while (i < pts.length - 1 && cum[i] < s) i++; //
    const t = (s - cum[i - 1]) / (cum[i] - cum[i - 1] || 1);
    const pos = pts[i - 1].clone().lerp(pts[i], t);
    const dir = pts[i].clone().sub(pts[i - 1]).normalize();
    const side = new THREE.Vector3(-dir.z, 0, dir.x); // 진행 방향의 오른쪽
    return { pos, dir, side };
  }
  return { length, at };
}
