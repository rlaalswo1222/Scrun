import * as THREE from 'three';
import { sampleRows } from './video-road.js';

// 영상 속 땅을 평평한 면으로 보고 러너를 "실제 거리"에 세운다.
// 이 영상의 길 폭은 화면 높이에 따라 거의 정확히 직선으로 줄어든다(지평선 약 46%, 카메라 약 1.5m).
// 그래서 크기(거리)와 발 위치가 같은 평면 원근을 따르게 하고, 좌우 위치만 영상 속 길에 맞춘다.
//
// 가려짐: 길 추적기(tools/track_video_road.py)는 길이 더 이상 안 보이는 줄에서 멈춘다.
// 그 줄(보이는 길 끝)은 언덕 마루나 덤불 뒤로 굽어 들어가는 곳이므로 "가림선"으로 쓴다.
//  - 길 끝을 넘어간 러너는 넘어간 거리만큼 가림선(시선) 아래로 내려 보내고,
//  - 가림선 아래 부분은 잘라서(clipping) 다리부터 가려지게 한다. 몸 전체가 내려가면 숨긴다.
// 추적기가 살피는 맨 윗줄(50%)까지 길이 보이면 가림선이 없다고 본다(길이 계속 이어짐).

const TRACK_LIMIT_Y = 0.505; // 추적기가 살피는 맨 윗줄
const SINK_PER_M = 0.3; // 길 끝을 1m 넘어갈 때마다 시선 아래로 내려가는 높이 (m)
const BODY_M = 1.9; // 이만큼 내려가면 머리까지 가려짐
const MY_MAX_HIDDEN_M = 0.8; // 나는 몸 절반 이상 가리지 않음
const PATH_WIDTH_M = 2; // 좌우 차선 간격 계산용 길 폭 가정

// 화면 높이 y에서 길의 [왼쪽, 오른쪽] (0~1). 추적된 맨 윗줄보다 먼 곳은 길을 연장해서 추정한다.
// 연장 방향: 보이는 모든 줄의 길 가운데에 맞춘 직선 (맨 위 두 줄만 쓰면 짧고 흔들려서 바다·언덕 쪽으로 꺾인다)
export function roadBoundsAt(rows, y, horizon) {
  if (!rows?.length) return null;
  const top = rows[0];
  if (y >= top[0]) return sampleRows(rows, y);
  if (rows.length < 2 || y <= horizon) return null;
  // 먼 줄(지평선에 가까운 줄)일수록 연장할 곳과 가까우므로 비중을 크게 (가중치 1/(y - 지평선))
  let sw = 0, sy = 0, sc = 0, syy = 0, syc = 0;
  for (const [ry, l, r] of rows) {
    const c = (l + r) / 2;
    const w = 1 / Math.max(0.01, ry - horizon);
    sw += w; sy += w * ry; sc += w * c; syy += w * ry * ry; syc += w * ry * c;
  }
  const slope = (sw * syc - sy * sc) / Math.max(1e-12, sw * syy - sy * sy);
  const fitAt = (yy) => (sc - slope * sy) / sw + slope * yy;
  // 맨 윗줄에서는 실제 위치에서 출발해 직선 기울기로 이어 간다
  const center = (top[1] + top[2]) / 2 + (fitAt(y) - fitAt(top[0]));
  const width = (top[2] - top[1]) * (y - horizon) / Math.max(1e-3, top[0] - horizon); // 지평선에서 폭 0
  return [center - width / 2, center + width / 2];
}

// 보이는 길 끝(가림선)의 화면 높이. 길이 추적 한계까지 보이면 null(가림선 없음)
export function crestY(rows) {
  return rows && rows[0][0] > TRACK_LIMIT_Y ? rows[0][0] : null;
}

export function createRoadProjector(camera, road) {
  const p = new THREE.Vector3();
  const ray = new THREE.Vector3();
  const screenToGround = (x, y) => {
    ray.set(x * 2 - 1, 1 - y * 2, 0.5).unproject(camera).sub(camera.position).normalize();
    if (ray.y >= 0) return null;
    return new THREE.Vector3().copy(camera.position).addScaledVector(ray, -camera.position.y / ray.y);
  };
  // 반환: { position: 발 위치, hidden: 가림선 아래로 가려진 높이(m) } 또는 null(화면 밖·완전히 가려짐)
  // noCrest: 보이는 길 끝(가림선) 처리 없이 길을 연장한 땅 위 위치만 (코스 지도 모드: 가려짐은 지도로 판단)
  return (depth, lane, time, { isMe = false, noCrest = false } = {}) => {
    p.set(0, 0, -1e6).project(camera);
    const horizon = (1 - p.y) / 2;
    p.set(0, 0, -depth).project(camera); // 평평한 땅 위, 그 거리의 발 높이
    const y = (1 - p.y) / 2;
    if (y > 1.02 || y <= horizon) return null;
    const rows = road.projectionRowsAt(time);
    if (!rows) return null;
    const bounds = roadBoundsAt(rows, Math.min(y, 1), horizon);
    if (!bounds) return null;
    // 차선: 길 가운데에서 lane × 0.6m씩 (길 폭 2m 가정), 길 가장자리 15%는 비운다
    const fraction = Math.max(0.15, Math.min(0.85, 0.5 + lane * 0.6 / PATH_WIDTH_M));
    const position = screenToGround(bounds[0] + (bounds[1] - bounds[0]) * fraction, Math.min(y, 1));
    if (!position) return null;
    let hidden = 0;
    const crest = crestY(rows);
    if (!noCrest && crest !== null && y < crest) {
      const end = screenToGround(0.5, crest);
      const dEnd = end ? -end.z : depth;
      const over = depth - dEnd;
      // 가림선(카메라에서 길 끝을 지나는 시선)의 이 거리에서 높이. 발을 그보다 넘어간 거리만큼 아래로
      const sight = camera.position.y * (1 - depth / dEnd);
      hidden = Math.min(SINK_PER_M * over, isMe ? MY_MAX_HIDDEN_M : Infinity);
      if (hidden >= BODY_M) return null;
      position.y = sight - hidden;
    }
    return { position, hidden };
  };
}

// 가림선 위만 남기는 자르기 평면: 카메라와 "보이는 길 끝 줄"을 지나는 평면. 가림선이 없으면 아무것도 자르지 않음
export function updateCrestPlane(plane, camera, rows) {
  const crest = crestY(rows);
  if (crest === null) {
    plane.set(new THREE.Vector3(0, 1, 0), 1e4);
    return;
  }
  const ground = (x) => {
    const ray = new THREE.Vector3(x * 2 - 1, 1 - crest * 2, 0.5).unproject(camera).sub(camera.position).normalize();
    return camera.position.clone().addScaledVector(ray, -camera.position.y / Math.min(ray.y, -1e-6));
  };
  plane.setFromCoplanarPoints(camera.position, ground(0), ground(1));
  // 위쪽(카메라 바로 위)이 남는 쪽(양수)이 되게
  if (plane.distanceToPoint(camera.position.clone().add(new THREE.Vector3(0, 1, -1))) < 0) plane.negate();
}
