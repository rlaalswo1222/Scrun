// 한강 랜드마크 다리 장식
//  - 반포대교: 다리 양옆에서 강으로 뿜는 달빛무지개분수 (물줄기를 점 입자로 그림)
//  - 동작대교·동호대교: 강 가운데 구간의 아치
// 다리 위치·높이는 course.json의 landmarks(OSM에서 뽑은 강 위 구간)를 쓴다.
// 아치의 크기·개수는 실제 구조를 측량한 값이 아니라 분위기를 살리기 위한 근사치다.
import * as THREE from 'three';
import { polylineWalker } from './geom.js';

// ---------- 반포대교 달빛무지개분수 ----------
const FOUNTAIN = {
  halfWidth: 12.5, // 다리 중심선에서 분수 노즐까지 (m)
  every: 2, // 노즐 간격 (m)
  particles: 24, // 물줄기 하나에 그리는 입자 수
  speedOut: 6, // 바깥쪽 속도 (m/s)
  speedUp: 2, // 위쪽 속도 (m/s)
};

function softDot() {
  const c = document.createElement('canvas');
  c.width = c.height = 32;
  const g = c.getContext('2d');
  const grad = g.createRadialGradient(16, 16, 0, 16, 16, 16);
  grad.addColorStop(0, 'rgba(255,255,255,1)');
  grad.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = grad;
  g.fillRect(0, 0, 32, 32);
  return new THREE.CanvasTexture(c);
}

function buildFountain(scene, bridge) {
  const walker = polylineWalker(bridge.line, bridge.z - 1);
  const nozzle = [];
  const dir = [];
  const phase = [];
  for (let s = 0; s < walker.length; s += FOUNTAIN.every) {
    const { pos, side } = walker.at(s);
    for (const sign of [-1, 1]) {
      const out = side.clone().multiplyScalar(sign);
      const p = pos.clone().addScaledVector(out, FOUNTAIN.halfWidth);
      for (let k = 0; k < FOUNTAIN.particles; k++) {
        nozzle.push(p.x, p.y, p.z);
        dir.push(out.x, 0, out.z);
        phase.push(k / FOUNTAIN.particles + Math.random() * 0.03);
      }
    }
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(new Float32Array(nozzle.length), 3));
  g.setAttribute('aNozzle', new THREE.Float32BufferAttribute(nozzle, 3));
  g.setAttribute('aDir', new THREE.Float32BufferAttribute(dir, 3));
  g.setAttribute('aPhase', new THREE.Float32BufferAttribute(phase, 1));

  const uniforms = { uTime: { value: 0 } };
  const material = new THREE.PointsMaterial({
    color: 0xe6f6ff, size: 3, map: softDot(), transparent: true, opacity: 0.45, depthWrite: false,
  });
  // 노즐에서 나와 포물선을 그리며 2초 뒤 강물에 닿는 물방울
  const fall = (FOUNTAIN.speedUp + Math.sqrt(FOUNTAIN.speedUp ** 2 + 2 * 9.8 * (bridge.z - 1))) / 9.8;
  material.onBeforeCompile = (shader) => {
    Object.assign(shader.uniforms, uniforms);
    shader.vertexShader = `
      attribute vec3 aNozzle;
      attribute vec3 aDir;
      attribute float aPhase;
      uniform float uTime;
      ${shader.vertexShader}`.replace('#include <begin_vertex>', `
        float t = fract(uTime / ${fall.toFixed(3)} + aPhase) * ${fall.toFixed(3)};
        vec3 transformed = aNozzle + aDir * ${FOUNTAIN.speedOut.toFixed(1)} * t
          + vec3(0.0, ${FOUNTAIN.speedUp.toFixed(1)} * t - 4.9 * t * t, 0.0);
      `);
  };
  const points = new THREE.Points(g, material);
  points.frustumCulled = false;
  points.renderOrder = 8;
  scene.add(points);
  return (dt) => {
    uniforms.uTime.value += dt;
  };
}

// ---------- 아치 ----------
function buildArch(scene, bridge, { span, rise, halfWidth, color }) {
  const walker = polylineWalker(bridge.line, 0);
  const mid = walker.at(walker.length / 2);
  span = Math.min(span, walker.length * 0.6);
  const material = new THREE.MeshStandardMaterial({ color, roughness: 0.45, metalness: 0.5 });
  const deck = bridge.z;
  const archY = (s) => deck + rise * (1 - (2 * s / span) ** 2);

  for (const sign of [-1, 1]) {
    const offset = mid.side.clone().multiplyScalar(sign * halfWidth);
    const at = (s, y) => mid.pos.clone().addScaledVector(mid.dir, s).add(offset).setY(y);
    // 아치 갈비(곡선 관)
    const pts = [];
    for (let i = 0; i <= 32; i++) {
      const s = -span / 2 + (span * i) / 32;
      pts.push(at(s, archY(s)));
    }
    const rib = new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 64, 0.9, 10), material);
    rib.castShadow = true;
    scene.add(rib);
    // 상판과 아치를 잇는 수직 줄
    for (let s = -span / 2 + 8; s < span / 2 - 4; s += 8) {
      const top = archY(s);
      const hanger = new THREE.Mesh(new THREE.CylinderGeometry(0.12, 0.12, top - deck, 6), material);
      hanger.position.copy(at(s, (top + deck) / 2));
      scene.add(hanger);
    }
  }
  // 두 아치 꼭대기 근처를 잇는 가로 보
  for (let s = -span / 4; s <= span / 4; s += span / 8) {
    const a = mid.pos.clone().addScaledVector(mid.dir, s).addScaledVector(mid.side, -halfWidth).setY(archY(s));
    const b = mid.pos.clone().addScaledVector(mid.dir, s).addScaledVector(mid.side, halfWidth).setY(archY(s));
    const beam = new THREE.Mesh(new THREE.CylinderGeometry(0.35, 0.35, a.distanceTo(b), 8), material);
    beam.position.copy(a).lerp(b, 0.5);
    beam.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), b.clone().sub(a).normalize());
    scene.add(beam);
  }
}

const ARCHES = {
  동작대교: { span: 170, rise: 32, halfWidth: 12, color: 0x7d8fa3 },
  동호대교: { span: 150, rise: 28, halfWidth: 12, color: 0x8a96a8 },
};

export function buildLandmarks(scene, course) {
  const updates = [];
  for (const bridge of course.landmarks || []) {
    if (bridge.name === '반포대교') updates.push(buildFountain(scene, bridge));
    if (ARCHES[bridge.name]) buildArch(scene, bridge, ARCHES[bridge.name]);
  }
  return (dt) => updates.forEach((u) => u(dt));
}
