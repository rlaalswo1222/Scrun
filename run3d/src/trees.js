// 나무: ez-tree(MIT, 절차적 나무 생성기)로 진짜 같은 나무를 몇 종류 만들고,
//  - 가까운 나무(nearR 안): 줄기·가지·잎이 있는 3D 모델 (바람에 흔들림, 그림자 있음)
//  - 먼 나무: 같은 나무를 한 번 사진처럼 찍어 십자 모양 판에 붙인 것(임포스터). 멀리서는 구분이 안 되고 훨씬 가볍다
// 내가 달리면서 8m 움직일 때마다 어느 나무를 3D로 그릴지 다시 고른다.
import * as THREE from 'three';
import { Tree } from '@dgreenheck/ez-tree';

const VARIANTS = [
  { preset: 'Oak Small', seed: 11, height: 9 },
  { preset: 'Ash Small', seed: 23, height: 10 },
  { preset: 'Aspen Medium', seed: 37, height: 12 },
];
const NEAR_CAP = 220; // 종류별 3D 나무 최대 개수
const RESELECT_M = 8;

// ez-tree의 잎 재질은 바람 효과를 넣으면서 인스턴싱(여러 그루 배치)을 빼먹어서 잎이 한곳에 몰린다.
// 그래서 인스턴싱을 지원하는 바람 셰이더를 직접 붙인 재질로 바꾼다. 하늘빛 반사를 받도록 Standard 재질 사용.
const wind = { uTime: { value: 0 } };

function leafMaterial(src) {
  const mat = new THREE.MeshStandardMaterial({
    map: src.map, color: src.color, side: THREE.DoubleSide, alphaTest: src.alphaTest, roughness: 0.75,
  });
  mat.onBeforeCompile = (shader) => {
    shader.uniforms.uTime = wind.uTime;
    shader.vertexShader = `uniform float uTime;\n${shader.vertexShader}`.replace(
      '#include <project_vertex>',
      `vec4 mvPosition = vec4(transformed, 1.0);
      #ifdef USE_INSTANCING
        mvPosition = instanceMatrix * mvPosition;
      #endif
      // 잎 끝(uv.y가 클수록)이 더 많이 흔들리고, 나무마다 위치에 따라 박자가 다르다
      float phase = dot((modelMatrix * mvPosition).xz, vec2(0.15, 0.11));
      float sway = 0.5 * sin(uTime * 0.9 + phase) + 0.3 * sin(uTime * 2.1 + 1.3 * phase);
      mvPosition.xz += uv.y * 0.12 * sway;
      mvPosition = modelViewMatrix * mvPosition;
      gl_Position = projectionMatrix * mvPosition;`,
    );
  };
  return mat;
}

function barkMaterial(src) {
  return new THREE.MeshStandardMaterial({
    map: src.map, normalMap: src.normalMap, color: src.color, roughness: 0.95,
  });
}

function makeVariant({ preset, seed, height }) {
  const tree = new Tree();
  tree.loadPreset(preset);
  tree.options.seed = seed;
  tree.generate();
  const box = new THREE.Box3().setFromObject(tree);
  const k = height / (box.max.y - box.min.y); // ez-tree 단위 -> 미터
  return { tree, k, box };
}

// 나무를 옆에서 찍은 사진(투명 배경)을 만든다
function renderImpostor(renderer, v) {
  const w = Math.max(v.box.max.x - v.box.min.x, v.box.max.z - v.box.min.z) * v.k;
  const h = (v.box.max.y - v.box.min.y) * v.k;
  const rt = new THREE.WebGLRenderTarget(256, Math.round((256 * h) / w), { generateMipmaps: true });
  rt.texture.minFilter = THREE.LinearMipmapLinearFilter;

  const scene = new THREE.Scene();
  scene.add(new THREE.AmbientLight(0xffffff, 1.4));
  const light = new THREE.DirectionalLight(0xfff1dc, 2.6);
  light.position.set(1, 2, 2);
  scene.add(light);
  v.tree.scale.setScalar(v.k);
  v.tree.position.y = -v.box.min.y * v.k;
  scene.add(v.tree);
  const cam = new THREE.OrthographicCamera(-w / 2, w / 2, h, 0, 0.1, 1000);
  cam.position.set(0, 0, 200);

  const prevColor = renderer.getClearColor(new THREE.Color());
  const prevAlpha = renderer.getClearAlpha();
  renderer.setRenderTarget(rt);
  renderer.setClearColor(0x000000, 0);
  renderer.clear();
  renderer.render(scene, cam);
  renderer.setRenderTarget(null);
  renderer.setClearColor(prevColor, prevAlpha);
  scene.remove(v.tree);
  return { texture: rt.texture, w, h };
}

// 십자로 교차한 판 두 장
function crossQuad(w, h) {
  const a = new THREE.PlaneGeometry(w, h).translate(0, h / 2, 0);
  const b = a.clone().rotateY(Math.PI / 2);
  const g = new THREE.BufferGeometry();
  const merge = (attr) => {
    const x = a.getAttribute(attr).array;
    const y = b.getAttribute(attr).array;
    const out = new Float32Array(x.length + y.length);
    out.set(x);
    out.set(y, x.length);
    return new THREE.BufferAttribute(out, a.getAttribute(attr).itemSize);
  };
  g.setAttribute('position', merge('position'));
  g.setAttribute('uv', merge('uv'));
  const n = a.getAttribute('position').count;
  g.setIndex([...a.index.array, ...Array.from(b.index.array, (i) => i + n)]);
  return g;
}

// nearR: 이 반경(m) 안의 나무만 3D로 그린다 (화질 설정에 따라 다름)
export function buildTrees(scene, renderer, trees, nearR) {
  if (!trees.length) return () => {};
  const variants = VARIANTS.map(makeVariant);

  // 먼 나무 (임포스터): 처음엔 전부 보이고, 가까워진 나무만 숨긴다
  const far = variants.map((v) => {
    const imp = renderImpostor(renderer, v);
    const mesh = new THREE.InstancedMesh(
      crossQuad(imp.w, imp.h),
      new THREE.MeshBasicMaterial({ map: imp.texture, alphaTest: 0.35, side: THREE.DoubleSide }),
      Math.ceil(trees.length / variants.length),
    );
    mesh.count = 0;
    mesh.userData.noAO = true; // 판의 투명한 부분까지 그늘이 지지 않게
    scene.add(mesh);
    return mesh;
  });

  // 가까운 나무 (3D 모델): ez-tree의 줄기·잎 메시를 인스턴싱으로 여러 그루 그린다
  const near = variants.map((v) => {
    const make = (src, material) => {
      const m = new THREE.InstancedMesh(src.geometry, material, NEAR_CAP);
      m.castShadow = m.receiveShadow = true;
      m.frustumCulled = false; // 매번 위치가 바뀌므로 화면 밖 판정을 끔
      m.count = 0;
      scene.add(m);
      return m;
    };
    return {
      branches: make(v.tree.branchesMesh, barkMaterial(v.tree.branchesMesh.material)),
      leaves: make(v.tree.leavesMesh, leafMaterial(v.tree.leavesMesh.material)),
    };
  }).map((n) => {
    n.leaves.userData.noAO = true; // 잎은 바람에 움직여서 구석 그늘(AO) 계산에서 뺀다
    return n;
  });

  // 나무마다 위치·방향·크기 행렬을 미리 계산
  const mat = new THREE.Matrix4();
  const q = new THREE.Quaternion();
  const up = new THREE.Vector3(0, 1, 0);
  const placed = trees.map(([x, y, s], i) => {
    const vi = i % variants.length;
    const v = variants[vi];
    q.setFromAxisAngle(up, (i * 2.399) % (Math.PI * 2));
    const base = new THREE.Matrix4().compose(new THREE.Vector3(x, 0, -y), q, new THREE.Vector3(s, s, s));
    const model = new THREE.Matrix4().compose(
      new THREE.Vector3(0, -v.box.min.y * v.k, 0), new THREE.Quaternion(), new THREE.Vector3(v.k, v.k, v.k));
    return { x, z: -y, vi, imposter: base, model: base.clone().multiply(model), farIndex: far[vi].count++ };
  });
  for (const p of placed) far[p.vi].setMatrixAt(p.farIndex, p.imposter);

  const hidden = new THREE.Matrix4().makeScale(0, 0, 0);
  const lastFocus = new THREE.Vector3(Infinity, 0, 0);

  return function update(focus, dt) {
    wind.uTime.value += dt; // 잎 흔들림(바람)
    if (focus.distanceTo(lastFocus) < RESELECT_M) return;
    lastFocus.copy(focus);
    const counts = variants.map(() => 0);
    for (const p of placed) {
      const dx = p.x - focus.x;
      const dz = p.z - focus.z;
      const isNear = dx * dx + dz * dz < nearR * nearR && counts[p.vi] < NEAR_CAP;
      if (isNear) {
        const n = near[p.vi];
        n.branches.setMatrixAt(counts[p.vi], p.model);
        n.leaves.setMatrixAt(counts[p.vi], p.model);
        counts[p.vi]++;
      }
      far[p.vi].setMatrixAt(p.farIndex, isNear ? hidden : p.imposter);
    }
    near.forEach((n, i) => {
      n.branches.count = n.leaves.count = counts[i];
      n.branches.instanceMatrix.needsUpdate = n.leaves.instanceMatrix.needsUpdate = true;
    });
    for (const f of far) f.instanceMatrix.needsUpdate = true;
  };
}
