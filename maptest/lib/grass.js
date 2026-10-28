// 3D 잔디: 카메라 주변 정사각형 안에 풀잎 수만 개 (run3d/src/grass.js와 같은 방식).
// 풀잎 위치는 땅에 고정돼 있고, 카메라가 움직이면 뒤로 빠진 풀잎이 앞쪽으로 "감겨" 나타난다(셰이더에서 계산).
// 잔디가 아닌 곳(트랙·호수·해안)은 지도 대신 트랙 모양 식(경기장 고리)으로 바로 걸러낸다.
import * as THREE from 'three';

function bladeGeometry() {
  const segs = 4;
  const pos = [];
  const uv = [];
  const col = [];
  const idx = [];
  const base = new THREE.Color(0x26401a);
  const tip = new THREE.Color(0x7aa244);
  const c = new THREE.Color();
  for (let i = 0; i <= segs; i++) {
    const t = i / segs;
    const w = 0.024 * (1 - t * 0.85);
    pos.push(-w, t, 0, w, t, 0);
    uv.push(0, t, 1, t);
    c.copy(base).lerp(tip, t);
    col.push(c.r, c.g, c.b, c.r, c.g, c.b);
    if (i < segs) {
      const a = i * 2;
      idx.push(a, a + 1, a + 2, a + 1, a + 3, a + 2);
    }
  }
  const g = new THREE.InstancedBufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  g.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
  g.setIndex(idx);
  return g;
}

// shape: { L, R, trackHalf, innerMin, outerMax } — 트랙 가운데 선에서 떨어진 거리로 잔디 구역을 정한다
export function buildGrass(scene, size, density, shape) {
  if (!size) return () => {};
  const count = Math.round(size * size * density);
  const geometry = bladeGeometry();
  const offsets = new Float32Array(count * 4);
  for (let i = 0; i < count; i++) {
    offsets.set([Math.random() * size, Math.random() * size, Math.random() * Math.PI, 0.09 + Math.random() * 0.17], i * 4);
  }
  geometry.setAttribute('aOffset', new THREE.InstancedBufferAttribute(offsets, 4));
  geometry.instanceCount = count;

  const uniforms = {
    uFocus: { value: new THREE.Vector2() },
    uSize: { value: size },
    uTime: { value: 0 },
    uShape: { value: new THREE.Vector4(shape.L, shape.R, shape.trackHalf, 0) },
    uBand: { value: new THREE.Vector2(shape.innerMin, shape.outerMax) },
  };
  const material = new THREE.MeshStandardMaterial({ vertexColors: true, side: THREE.DoubleSide, roughness: 0.9 });
  material.onBeforeCompile = (sh) => {
    Object.assign(sh.uniforms, uniforms);
    sh.vertexShader = `
      attribute vec4 aOffset;
      uniform vec2 uFocus;
      uniform float uSize;
      uniform float uTime;
      uniform vec4 uShape;
      uniform vec2 uBand;
      ${sh.vertexShader}`
      .replace('#include <beginnormal_vertex>', 'vec3 objectNormal = vec3(0.0, 1.0, 0.0);')
      .replace('#include <begin_vertex>', `
        vec2 corner = uFocus - uSize * 0.5;
        vec2 wp = corner + mod(aOffset.xy - corner, uSize);
        // 트랙 가운데 선에서 떨어진 거리 (+ 바깥, - 안쪽)
        float cx = clamp(wp.x, -uShape.x * 0.5, uShape.x * 0.5);
        float o = length(vec2(wp.x - cx, wp.y)) - uShape.y;
        float grass = step(uShape.z + 0.7, abs(o)) * step(uBand.x, o) * step(o, uBand.y);
        grass *= 1.0 - step(uShape.x * 0.5 + uShape.y, wp.x) * step(abs(wp.y), 6.0); // 다리로 가는 길
        float fade = smoothstep(uSize * 0.5, uSize * 0.5 - 8.0, length(wp - uFocus));
        float h = aOffset.w * grass * fade;
        float c = cos(aOffset.z), s = sin(aOffset.z);
        float bend = (0.12 + 0.1 * sin(uTime * 1.6 + wp.x * 0.35 + wp.y * 0.27)) * position.y * position.y;
        vec3 local = vec3(position.x * c, position.y, position.x * s) * vec3(1.0, h, 1.0);
        local.xz += vec2(0.8, 0.5) * bend * h;
        local *= step(0.001, h);
        vec3 transformed = vec3(wp.x, 0.0, wp.y) + local;
      `);
    sh.fragmentShader = sh.fragmentShader.replace(
      '#include <normal_fragment_begin>',
      THREE.ShaderChunk.normal_fragment_begin.replaceAll('normal *= faceDirection;', ''),
    );
  };
  const mesh = new THREE.Mesh(geometry, material);
  mesh.frustumCulled = false;
  mesh.receiveShadow = true;
  mesh.renderOrder = 6;
  mesh.userData.noAO = true;
  scene.add(mesh);
  return (x, z, dt) => {
    uniforms.uFocus.value.set(x, z);
    uniforms.uTime.value += dt;
  };
}
