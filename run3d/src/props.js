// 거리 소품: 가로등, 신호등, 횡단보도, 버스정류장, 울타리·담장·생울타리·가드레일.
// 위치는 course.json의 props(코스 생성기가 OSM 기록 + 규칙으로 정함). 같은 종류는 인스턴싱으로 한 번에 그린다.
// 모든 모델은 "앞쪽"(가로등 팔, 신호등 팔, 정류장 열린 쪽)이 로컬 +z를 향하게 만든다.
import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { newRibbon, pushRibbon, ribbonGeometry } from './geom.js';

const std = (color, opts = {}) => new THREE.MeshStandardMaterial({ color, roughness: 0.8, metalness: 0, ...opts });
const METAL = std(0x4a4f55, { roughness: 0.5, metalness: 0.5 });

// 지도 각도(x=동, y=북 기준) -> 로컬 +z가 그 방향을 향하는 y축 회전
const yaw = (a) => Math.atan2(Math.cos(a), -Math.sin(a));

// 같은 위치·방향 목록으로 여러 부품(인스턴스 메시)을 함께 배치
function placeAll(scene, items, parts, { shadow = true } = {}) {
  if (!items.length) return [];
  const m = new THREE.Matrix4();
  const q = new THREE.Quaternion();
  const up = new THREE.Vector3(0, 1, 0);
  const one = new THREE.Vector3(1, 1, 1);
  return parts.map(([geometry, material]) => {
    const mesh = new THREE.InstancedMesh(geometry, material, items.length);
    items.forEach(([x, y, a], i) => {
      q.setFromAxisAngle(up, yaw(a));
      mesh.setMatrixAt(i, m.compose(new THREE.Vector3(x, 0, -y), q, one));
    });
    mesh.castShadow = shadow;
    mesh.receiveShadow = true;
    scene.add(mesh);
    return mesh;
  });
}

// ---------- 모델 ----------
function lampParts() {
  const pole = mergeGeometries([
    new THREE.CylinderGeometry(0.07, 0.11, 7, 8).translate(0, 3.5, 0),
    new THREE.BoxGeometry(0.08, 0.08, 1.9).translate(0, 6.95, 0.95),
  ]);
  const head = new THREE.BoxGeometry(0.35, 0.12, 0.7).translate(0, 6.86, 1.8);
  return [[pole, METAL], [head, std(0xfff6dd, { emissive: 0xfff2cc, emissiveIntensity: 0.5 })]];
}

function signalParts() {
  const pole = mergeGeometries([
    new THREE.CylinderGeometry(0.12, 0.14, 6, 10).translate(0, 3, 0),
    new THREE.CylinderGeometry(0.07, 0.07, 4.2, 8).rotateX(Math.PI / 2).translate(0, 5.7, 2.1),
  ]);
  // 한국식 가로형 신호등: 팔 끝에 길게 달리고, 다가오는 차 쪽(-x)을 본다
  const head = new THREE.BoxGeometry(0.32, 0.42, 1.35).translate(0, 5.45, 3.6);
  const lamp = (z) => new THREE.BoxGeometry(0.04, 0.26, 0.26).translate(-0.18, 5.45, z);
  return [
    [pole, std(0x5f646a, { roughness: 0.6, metalness: 0.4 })],
    [head, std(0x1d1f22, { roughness: 0.6 })],
    [mergeGeometries([lamp(3.05), lamp(3.42)]), std(0x3a2a22)], // 꺼진 빨강·노랑
    [lamp(4.15), std(0x2bd46a, { emissive: 0x2bd46a, emissiveIntensity: 1.2 })], // 켜진 초록
  ];
}

function stopParts() {
  const frame = mergeGeometries([
    new THREE.BoxGeometry(3.8, 0.1, 1.7).translate(0, 2.5, -0.1), // 지붕
    new THREE.BoxGeometry(0.08, 2.5, 0.08).translate(-1.8, 1.25, -0.85),
    new THREE.BoxGeometry(0.08, 2.5, 0.08).translate(1.8, 1.25, -0.85),
    new THREE.BoxGeometry(2.4, 0.08, 0.4).translate(0, 0.45, -0.6), // 의자
    new THREE.CylinderGeometry(0.05, 0.05, 2.8, 6).translate(2.4, 1.4, 0.4), // 표지 기둥
  ]);
  const glass = new THREE.BoxGeometry(3.6, 2.1, 0.03).translate(0, 1.3, -0.88);
  const sign = new THREE.BoxGeometry(0.5, 0.6, 0.05).translate(2.4, 2.6, 0.4);
  return [
    [frame, std(0x8a9096, { roughness: 0.5, metalness: 0.4 })],
    [glass, std(0xbfd8e6, { transparent: true, opacity: 0.35, roughness: 0.1 })],
    [sign, std(0x1c7ed6, { roughness: 0.5 })],
  ];
}

// ---------- 울타리·담장 ----------
function fenceTexture() {
  const c = document.createElement('canvas');
  c.width = c.height = 64;
  const g = c.getContext('2d');
  g.fillStyle = '#2f3a33';
  g.fillRect(0, 0, 64, 6); // 위 가로대
  g.fillRect(0, 58, 64, 6); // 아래 가로대
  for (let x = 4; x < 64; x += 16) g.fillRect(x, 0, 5, 64); // 세로 살
  const t = new THREE.CanvasTexture(c);
  t.wrapS = THREE.RepeatWrapping;
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

// 선을 따라 세운 벽면 (u = 거리 m, v = 0~1 높이)
function verticalStrip(lines, height) {
  const pos = [];
  const uv = [];
  const idx = [];
  for (const pts of lines) {
    const base = pos.length / 3;
    let s = 0;
    pts.forEach(([x, y], i) => {
      if (i > 0) s += Math.hypot(x - pts[i - 1][0], y - pts[i - 1][1]);
      pos.push(x, 0, -y, x, height, -y);
      uv.push(s / height, 0, s / height, 1);
      if (i > 0) {
        const a = base + (i - 1) * 2;
        idx.push(a, a + 2, a + 1, a + 1, a + 2, a + 3);
      }
    });
  }
  if (!idx.length) return null;
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  g.setIndex(idx);
  g.computeVertexNormals();
  return g;
}

function buildBarriers(scene, barriers) {
  const by = (t) => barriers.filter((b) => b.t === t).map((b) => b.p);
  const add = (geometry, material) => {
    if (!geometry) return;
    const mesh = new THREE.Mesh(geometry, material);
    mesh.castShadow = mesh.receiveShadow = true;
    scene.add(mesh);
  };
  add(verticalStrip(by('fence'), 1.4),
    std(0xffffff, { map: fenceTexture(), alphaTest: 0.5, side: THREE.DoubleSide, roughness: 0.6 }));
  add(verticalStrip(by('rail'), 0.8), std(0xc9cdd1, { side: THREE.DoubleSide, metalness: 0.5, roughness: 0.4 }));
  const walls = newRibbon();
  for (const p of by('wall')) pushRibbon(walls, p, 0.3, new Array(p.length).fill(2), 2);
  add(ribbonGeometry(walls), std(0xb8b2a6, { side: THREE.DoubleSide }));
  const hedges = newRibbon();
  for (const p of by('hedge')) pushRibbon(hedges, p, 0.9, new Array(p.length).fill(1.1), 1.1);
  add(ribbonGeometry(hedges), std(0x3f6b2e, { side: THREE.DoubleSide, roughness: 0.95 }));
}

// ---------- 횡단보도 ----------
// 흰 줄무늬: 차가 다니는 방향과 나란히, 폭 0.45m 줄을 0.9m 간격으로 도로를 가로질러
function buildCrossings(scene, crossings) {
  const stripes = newRibbon();
  for (const [x, y, a, w] of crossings) {
    const dx = Math.cos(a);
    const dy = Math.sin(a);
    for (let o = -w / 2 + 0.6; o <= w / 2 - 0.6; o += 0.9) {
      const cx = x - dy * o;
      const cy = y + dx * o;
      pushRibbon(stripes, [[cx - dx * 1.6, cy - dy * 1.6], [cx + dx * 1.6, cy + dy * 1.6]], 0.45, null);
    }
  }
  const g = ribbonGeometry(stripes);
  if (!g) return;
  const mesh = new THREE.Mesh(g, std(0xeeeeee, { roughness: 0.6, depthWrite: false, side: THREE.DoubleSide }));
  mesh.renderOrder = 3.6; // 도로(3)와 차선(3.5) 위
  mesh.receiveShadow = true;
  scene.add(mesh);
}

export function buildStreetProps(scene, course) {
  const props = course.props;
  if (!props) return;
  placeAll(scene, props.lamps, lampParts());
  placeAll(scene, props.signals, signalParts());
  placeAll(scene, props.stops, stopParts());
  buildCrossings(scene, props.crossings);
  buildBarriers(scene, props.barriers);
}
