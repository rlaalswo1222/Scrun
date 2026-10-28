// 3D 잔디: 내 주변 정사각형 안에 풀잎 수만 개를 그린다.
// 풀잎 위치는 지도에 고정돼 있고, 내가 움직이면 뒤로 빠진 풀잎이 앞쪽으로 "감겨" 나타난다(셰이더에서 계산).
// 그래서 매 프레임 CPU 작업이 없다. 잔디밭이 아닌 곳(길·도로·강)은 위에서 찍은 지도 마스크로 걸러낸다.
import * as THREE from 'three';
import { newRibbon, polygonsGeometry, pushRibbon, ribbonGeometry } from './geom.js';

const MASK_SIZE = 2048;
const MARGIN = 80; // 코스 바깥 몇 m까지 마스크를 만들지

// 위에서 내려다본 잔디 지도(흰색 = 잔디)를 한 번 그려 둔다
function renderMask(renderer, course) {
  let minX = Infinity, maxX = -Infinity, minN = Infinity, maxN = -Infinity;
  for (const [x, n] of course.route) {
    minX = Math.min(minX, x); maxX = Math.max(maxX, x);
    minN = Math.min(minN, n); maxN = Math.max(maxN, n);
  }
  minX -= MARGIN; maxX += MARGIN; minN -= MARGIN; maxN += MARGIN;
  const rect = new THREE.Vector4(minX, -minN, maxX - minX, maxN - minN); // minX, 남쪽 끝 z, 가로, 세로

  const scene = new THREE.Scene();
  const add = (geometry, color, order) => {
    if (!geometry) return;
    const mesh = new THREE.Mesh(geometry, new THREE.MeshBasicMaterial({ color, side: THREE.DoubleSide, depthTest: false }));
    mesh.renderOrder = order;
    scene.add(mesh);
  };
  add(polygonsGeometry(course.green), 0xffffff, 0);
  add(polygonsGeometry(course.water), 0x000000, 1);
  add(polygonsGeometry(course.landuse?.pitch ?? []), 0x000000, 1); // 운동장(인조잔디)
  // 길·도로는 양옆 보도(3m씩)까지 넉넉히 지워서 풀이 길 위로 삐져나오지 않게
  const paths = newRibbon();
  for (const r of course.roads) if (!r.z) pushRibbon(paths, r.p, r.w + 8, null);
  pushRibbon(paths, course.route, 7, null);
  add(ribbonGeometry(paths), 0x000000, 2);

  const w = maxX - minX;
  const h = maxN - minN;
  const cam = new THREE.OrthographicCamera(-w / 2, w / 2, h / 2, -h / 2, 1, 1000);
  cam.position.set(minX + w / 2, 500, -(minN + h / 2));
  cam.up.set(0, 0, -1); // 화면 위쪽 = 북쪽
  cam.lookAt(minX + w / 2, 0, -(minN + h / 2));

  const rt = new THREE.WebGLRenderTarget(MASK_SIZE, MASK_SIZE);
  const prev = renderer.getClearColor(new THREE.Color());
  const prevAlpha = renderer.getClearAlpha();
  renderer.setRenderTarget(rt);
  renderer.setClearColor(0x000000, 1);
  renderer.clear();
  renderer.render(scene, cam);
  renderer.setRenderTarget(null);
  renderer.setClearColor(prev, prevAlpha);
  return { texture: rt.texture, rect };
}

// 풀잎 하나: 아래가 넓고 위로 갈수록 좁아지는 띠. 아래는 어두운 초록, 끝은 밝은 연두
function bladeGeometry() {
  const segs = 4;
  const pos = [];
  const uv = [];
  const col = [];
  const idx = [];
  const base = new THREE.Color(0x2a4119);
  const tip = new THREE.Color(0x6f9a3c);
  const c = new THREE.Color();
  for (let i = 0; i <= segs; i++) {
    const t = i / segs;
    const w = 0.022 * (1 - t * 0.85);
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

export function buildGrass(scene, renderer, course, Q) {
  if (!Q.grassSize) return () => {};
  const size = Q.grassSize;
  const count = Math.round(size * size * Q.grassDensity);
  const mask = renderMask(renderer, course);

  const geometry = bladeGeometry();
  // 풀잎마다: 정사각형 안 위치(x, z), 방향, 높이
  const offsets = new Float32Array(count * 4);
  for (let i = 0; i < count; i++) {
    offsets.set([Math.random() * size, Math.random() * size, Math.random() * Math.PI, 0.1 + Math.random() * 0.16], i * 4);
  }
  geometry.setAttribute('aOffset', new THREE.InstancedBufferAttribute(offsets, 4));
  geometry.instanceCount = count;

  const uniforms = {
    uFocus: { value: new THREE.Vector2() },
    uSize: { value: size },
    uTime: { value: 0 },
    uMask: { value: mask.texture },
    uMaskRect: { value: mask.rect },
  };
  const material = new THREE.MeshStandardMaterial({ vertexColors: true, side: THREE.DoubleSide, roughness: 0.9 });
  material.onBeforeCompile = (shader) => {
    Object.assign(shader.uniforms, uniforms);
    shader.vertexShader = `
      attribute vec4 aOffset;
      uniform vec2 uFocus;
      uniform float uSize;
      uniform float uTime;
      uniform sampler2D uMask;
      uniform vec4 uMaskRect;
      ${shader.vertexShader}`
      // 조명은 땅과 같게: 풀잎 법선을 위쪽으로
      .replace('#include <beginnormal_vertex>', 'vec3 objectNormal = vec3(0.0, 1.0, 0.0);')
      .replace('#include <begin_vertex>', `
        // 지도에 고정된 위치를 내 주변 정사각형 안으로 감아 넣는다
        vec2 corner = uFocus - uSize * 0.5;
        vec2 wp = corner + mod(aOffset.xy - corner, uSize);
        // 잔디밭이 아니면(마스크 검정) 풀잎을 0 크기로
        vec2 muv = vec2((wp.x - uMaskRect.x) / uMaskRect.z, (uMaskRect.y - wp.y) / uMaskRect.w);
        float inside = step(0.0, muv.x) * step(muv.x, 1.0) * step(0.0, muv.y) * step(muv.y, 1.0);
        float grass = step(0.5, texture2D(uMask, muv).r) * inside;
        // 정사각형 가장자리로 갈수록 작아져서 경계가 안 보이게
        float fade = smoothstep(uSize * 0.5, uSize * 0.5 - 8.0, length(wp - uFocus));
        float h = aOffset.w * grass * fade;
        float c = cos(aOffset.z), s = sin(aOffset.z);
        float bend = (0.12 + 0.1 * sin(uTime * 1.6 + wp.x * 0.35 + wp.y * 0.27)) * position.y * position.y;
        vec3 local = vec3(position.x * c, position.y, position.x * s) * vec3(1.0, h, 1.0);
        local.xz += vec2(0.8, 0.5) * bend * h;
        // 숨길 풀잎은 높이만 0이면 납작한 선이 남아 가끔 점처럼 찍히므로 한 점으로 완전히 접는다
        local *= step(0.001, h);
        vec3 transformed = vec3(wp.x, 0.0, wp.y) + local;
      `);
    // 양면 재질은 뒷면의 법선을 뒤집어(아래를 보게 해) 어둡게 칠하므로, 풀잎은 뒤집지 않는다
    shader.fragmentShader = shader.fragmentShader.replace(
      '#include <normal_fragment_begin>',
      THREE.ShaderChunk.normal_fragment_begin.replaceAll('normal *= faceDirection;', ''),
    );
  };
  const mesh = new THREE.Mesh(geometry, material);
  mesh.frustumCulled = false; // 위치를 셰이더에서 정하므로 화면 밖 판정을 끔
  mesh.receiveShadow = true;
  mesh.renderOrder = 6;
  mesh.userData.noAO = true;
  scene.add(mesh);

  return (x, z, dt) => {
    uniforms.uFocus.value.set(x, z);
    uniforms.uTime.value += dt;
  };
}
