// course.json으로 3D 지도(하늘, 땅, 강, 공원, 잔디, 도로, 다리, 건물, 나무, 코스, 가로등·벤치)를 만든다.
// 같은 종류는 하나의 메시로 합쳐서 그리고, 그림자는 내 앞쪽 120m 사각형 안에서만 계산한다(배터리 절약).
// 텍스처: Poly Haven (CC0), 물결: three.js 예제 waternormals.jpg
import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { RGBELoader } from 'three/addons/loaders/RGBELoader.js';
import { makeLabel } from './runner.js';
import { buildTrees } from './trees.js';
import { buildGrass } from './grass.js';
import { buildLandmarks } from './landmarks.js';
import { buildBuildings } from './facades.js';
import { buildStreetProps } from './props.js';
import { newRibbon, offsetLine, polygonsGeometry, pushDashes, pushRibbon, ribbonGeometry } from './geom.js';

export const PALETTE = {
  ground: 0xb4b8bc, // 정보 없는 땅: 베이지색 보도블록 사진을 회색 쪽으로 눌러서 차분한 포장으로
  water: 0x2e6d8c,
  green: 0x9ccc66, // 잔디 (갈색 기운이 있는 텍스처를 푸르게)
  road: 0xe6e0d6, // 아스팔트 (텍스처에 살짝 따뜻한 색)
  deck: 0xb8b4ab, // 콘크리트
  route: 0xb4644c, // 자전거길 붉은 포장
  sidewalk: 0xd9d5cd,
};

// 해 방향: 하늘 사진(HDRI)에서 가장 밝은 점 (고도 48°, 남동쪽). 그림자도 이 방향으로 생긴다
const SUN_DIR = new THREE.Vector3(0.553, 0.743, 0.377).normalize();

const standard = (color, opts = {}) =>
  new THREE.MeshStandardMaterial({ color, roughness: 0.95, metalness: 0, ...opts });

// ---------- 텍스처 ----------
// 바닥·도로의 uv는 미터 단위로 넣어 두고, texture.repeat로 "몇 m마다 한 장"을 정한다
const loader = new THREE.TextureLoader();
let maxAniso = 1;

function texture(file, tileM, srgb = false) {
  const t = loader.load(`assets/tex/${file}`);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.repeat.set(1 / tileM, 1 / tileM);
  t.anisotropy = maxAniso; // 비스듬히 보이는 바닥이 뭉개지지 않게
  if (srgb) t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

function surface(name, tileM) {
  return { map: texture(`${name}_diff.jpg`, tileM, true), normalMap: texture(`${name}_nor.jpg`, tileM) };
}

// ---------- 바닥 평면 ----------
// 바닥에 붙은 평면들은 깊이 버퍼에 쓰지 않고 그리는 순서(renderOrder)로 겹침을 정한다.
// 땅이 평평하므로 이렇게 해도 건물·사람과의 앞뒤가 틀어지지 않고, 멀리서 깜빡임(z-fighting)도 없다.
function addFlat(scene, geometry, material, order) {
  if (!geometry) return null;
  Object.assign(material, { depthWrite: false, side: THREE.DoubleSide });
  const mesh = new THREE.Mesh(geometry, material);
  mesh.renderOrder = order;
  mesh.receiveShadow = true;
  scene.add(mesh);
  return mesh;
}

// 점 (x, 북 y)가 코스에서 r m 안에 있는지
function nearRoute(route, x, y, r) {
  return route.some(([rx, ry]) => (rx - x) ** 2 + (ry - y) ** 2 < r * r);
}

// ---------- 도로·다리 ----------
// 보도가 있는 도로 등급, 차선을 그리는 도로 등급
const SIDEWALK_ROADS = new Set(['primary', 'secondary', 'tertiary', 'residential', 'unclassified']);
const MARKED_ROADS = new Set(['primary', 'secondary', 'tertiary', 'trunk', 'motorway']);
const CURB = 0.15; // 보도 높이 (m)

// 차선: 양방향이면 가운데 노란 겹선, 차선 사이 흰 점선, 가장자리 흰 실선
function pushMarkings(yellow, white, r) {
  const half = r.w / 2;
  if (!r.o) {
    pushRibbon(yellow, offsetLine(r.p, 0.15), 0.12, null);
    pushRibbon(yellow, offsetLine(r.p, -0.15), 0.12, null);
  }
  const perSide = r.o
    ? Math.max(1, r.n ?? Math.floor(r.w / 3.3))
    : Math.max(1, r.n ? Math.round(r.n / 2) : Math.floor(half / 3.3));
  if (r.o) {
    const lane = r.w / perSide;
    for (let k = 1; k < perSide; k++) pushDashes(white, offsetLine(r.p, -half + k * lane), 0.12, 3, 5);
  } else {
    const lane = half / perSide;
    for (let k = 1; k < perSide; k++) {
      pushDashes(white, offsetLine(r.p, k * lane), 0.12, 3, 5);
      pushDashes(white, offsetLine(r.p, -k * lane), 0.12, 3, 5);
    }
  }
  pushRibbon(white, offsetLine(r.p, half - 0.3), 0.12, null);
  pushRibbon(white, offsetLine(r.p, -half + 0.3), 0.12, null);
}

function buildRoads(scene, roads, route) {
  const flat = newRibbon();
  const deck = newRibbon();
  const walks = newRibbon();
  const yellow = newRibbon();
  const white = newRibbon();
  const pillars = [];
  for (const r of roads) {
    if (!r.z) {
      pushRibbon(flat, r.p, r.w, null);
      // 양옆 보도: 차도보다 15cm 높고 옆면(연석)이 있다
      if (SIDEWALK_ROADS.has(r.k) && !r.o) {
        const curb = new Array(r.p.length).fill(CURB);
        for (const side of [-1, 1]) pushRibbon(walks, offsetLine(r.p, side * (r.w / 2 + 1.5)), 3, curb, CURB);
      }
      if (MARKED_ROADS.has(r.k)) pushMarkings(yellow, white, r);
      continue;
    }
    pushRibbon(deck, r.p, r.w, r.z, 1.2);
    // 높은 구간에 40m마다 기둥
    let acc = 20;
    for (let i = 1; i < r.p.length; i++) {
      const [ax, ay] = r.p[i - 1];
      const [bx, by] = r.p[i];
      const seg = Math.hypot(bx - ax, by - ay);
      while (acc <= seg) {
        const t = acc / seg;
        const h = r.z[i - 1] + (r.z[i] - r.z[i - 1]) * t;
        const px = ax + (bx - ax) * t;
        const py = ay + (by - ay) * t;
        // 코스 위에 기둥이 서서 길을 막지 않게
        if (h > 4 && !nearRoute(route, px, py, 5)) pillars.push([px, py, h - 1.2]);
        acc += 40;
      }
      acc -= seg;
    }
  }
  // 어두운 아스팔트에 하늘빛이 너무 비치면 파랗게 보여서 반사를 줄인다
  addFlat(scene, ribbonGeometry(walks), standard(PALETTE.sidewalk, surface('concrete_pavement', 2.5)), 2.5);
  addFlat(scene, ribbonGeometry(flat), standard(PALETTE.road, { ...surface('clean_asphalt', 4), envMapIntensity: 0.4 }), 3);
  addFlat(scene, ribbonGeometry(yellow), standard(0xd9b440, { roughness: 0.6 }), 3.5);
  addFlat(scene, ribbonGeometry(white), standard(0xe6e6e6, { roughness: 0.6 }), 3.5);
  const deckGeom = ribbonGeometry(deck);
  const concrete = surface('concrete_pavement', 3);
  if (deckGeom) {
    const mesh = new THREE.Mesh(deckGeom, standard(PALETTE.deck, { side: THREE.DoubleSide }));
    mesh.castShadow = mesh.receiveShadow = true;
    scene.add(mesh);
  }
  if (pillars.length) {
    const mesh = new THREE.InstancedMesh(
      new THREE.CylinderGeometry(1, 1, 1, 16).translate(0, 0.5, 0),
      standard(PALETTE.deck, { map: concrete.map }),
      pillars.length,
    );
    mesh.castShadow = mesh.receiveShadow = true;
    const m = new THREE.Matrix4();
    pillars.forEach(([x, y, h], i) => {
      m.makeScale(1, h, 1).setPosition(x, 0, -y);
      mesh.setMatrixAt(i, m);
    });
    scene.add(mesh);
  }
}

// ---------- 가로등·벤치 ----------
// 코스 오른쪽(강 반대편)에 가로등 35m마다, 벤치 120m마다
function instancesAlong(scene, path, geometry, material, every, lateral, offset = 0) {
  const spots = [];
  for (let d = offset; d < path.length - 10; d += every) spots.push(path.frame(d, lateral, 4));
  const mesh = new THREE.InstancedMesh(geometry, material, spots.length);
  const m = new THREE.Matrix4();
  spots.forEach((f, i) => {
    m.makeRotationY(Math.atan2(f.tx, f.tz)).setPosition(f.x, 0, f.z);
    mesh.setMatrixAt(i, m);
  });
  mesh.castShadow = true;
  scene.add(mesh);
}

function buildProps(scene, path) {
  const metal = standard(0x3b3f45, { roughness: 0.5, metalness: 0.6 });
  // 가로등: 기둥 + 길 쪽으로 뻗은 팔 + 등 (로컬 +z = 진행 방향, -x = 길 쪽)
  const lamp = mergeGeometries([
    new THREE.CylinderGeometry(0.06, 0.09, 5, 8).translate(0, 2.5, 0),
    new THREE.BoxGeometry(1.1, 0.06, 0.06).translate(-0.5, 4.95, 0),
  ]);
  instancesAlong(scene, path, lamp, metal, 35, 3.0);
  const head = new THREE.BoxGeometry(0.5, 0.12, 0.25).translate(-1.0, 4.88, 0);
  instancesAlong(scene, path, head, standard(0xfff6dd, { emissive: 0xfff2cc, emissiveIntensity: 0.6 }), 35, 3.0);

  const wood = standard(0x8a6748, { roughness: 0.8 });
  const bench = mergeGeometries([
    new THREE.BoxGeometry(0.45, 0.06, 1.6).translate(0, 0.45, 0), // 앉는 판
    new THREE.BoxGeometry(0.06, 0.4, 1.6).translate(0.22, 0.7, 0), // 등받이
  ]);
  instancesAlong(scene, path, bench, wood, 120, 3.6, 60);
  const legs = mergeGeometries([
    new THREE.BoxGeometry(0.4, 0.42, 0.06).translate(0, 0.21, -0.65),
    new THREE.BoxGeometry(0.4, 0.42, 0.06).translate(0, 0.21, 0.65),
  ]);
  instancesAlong(scene, path, legs, metal, 120, 3.6, 60);
}

// ---------- 코스 ----------
function buildCourse(scene, course, path) {
  // 코스 표면: 자전거길은 붉은 포장 + 흰 점선, 보도·산책로는 보도블록, 차도는 도로 그대로 둔다
  // (예전 코스 파일에는 surfaces가 없으므로 전체를 자전거길로)
  const surfaces = course.surfaces ?? [{ a: 0, b: path.length, s: 'bike' }];
  const bike = newRibbon();
  const walk = newRibbon();
  const dashes = newRibbon();
  const slice = (a, b) => {
    const pts = [];
    for (let d = a; d < b; d += 4) pts.push(path.point(d));
    pts.push(path.point(b));
    return pts.map(([x, z]) => [x, -z]);
  };
  for (const seg of surfaces) {
    const a = seg.a * (path.length / (surfaces[surfaces.length - 1].b || path.length));
    const b = seg.b * (path.length / (surfaces[surfaces.length - 1].b || path.length));
    if (seg.s === 'bike') {
      pushRibbon(bike, slice(a, b), 4, null);
      for (let d = a; d + 3 < b; d += 6) pushRibbon(dashes, slice(d, d + 3), 0.15, null);
    } else if (seg.s === 'walk') {
      pushRibbon(walk, slice(a, b), 3.2, null);
    }
  }
  const bumps = surface('clean_asphalt', 4).normalMap;
  addFlat(scene, ribbonGeometry(bike), standard(PALETTE.route, { normalMap: bumps }), 4);
  addFlat(scene, ribbonGeometry(walk), standard(PALETTE.sidewalk, surface('concrete_pavement', 2.5)), 4);
  addFlat(scene, ribbonGeometry(dashes), standard(0xf2f2f2, { roughness: 0.7 }), 5);

  const post = (x, z, h, color = 0xffffff) => {
    const m = new THREE.Mesh(new THREE.BoxGeometry(0.15, h, 0.15).translate(0, h / 2, 0), standard(color));
    m.castShadow = true;
    m.position.set(x, 0, z);
    scene.add(m);
  };
  const place = (sprite, d, lateral, y) => {
    const f = path.frame(d, lateral);
    sprite.position.set(f.x, y, f.z);
    scene.add(sprite);
    return f;
  };

  // 1km마다 거리 표지
  for (let km = 1; km < path.length / 1000; km++) {
    const f = place(makeLabel(`${km} km`, { bg: '#1d2433', fg: '#ffffff', height: 0.9 }), km * 1000, 3.2, 2.6);
    post(f.x, f.z, 2.1);
  }
  // 다리 표지판
  for (const s of course.signs) {
    place(makeLabel(s.label, { bg: '#ffffffee', height: 2.2 }), Math.min(s.d, path.length - 30), -6, 7);
  }
  // 출발·도착 아치 (순환·왕복처럼 출발점과 도착점이 같으면 하나로)
  const [sx, sz] = path.point(0);
  const [ex, ez] = path.point(path.length);
  const gates = Math.hypot(ex - sx, ez - sz) < 25
    ? [[0, 'START · FINISH', '#2b8a3e']]
    : [[0, 'START', '#2b8a3e'], [path.length, 'FINISH', '#d6336c']];
  for (const [d, text, bg] of gates) {
    const f = place(makeLabel(text, { bg, fg: '#ffffff', height: 1.4 }), d, 0, 4.4);
    post(f.x - f.tz * 2.6, f.z + f.tx * 2.6, 4.2, 0x333333);
    post(f.x + f.tz * 2.6, f.z - f.tx * 2.6, 4.2, 0x333333);
  }
}

// ---------- 땅 용도 ----------
// 아파트 단지는 조경 잔디(나무는 따로 심음), 상업지는 회색 포장, 주차장은 아스팔트, 학교는 흙 운동장, 운동장은 인조잔디
function buildLanduse(scene, landuse) {
  const styles = {
    residential: [0x8fbb5e, 'leafy_grass', 3, 0.5],
    commercial: [0xb8bcc0, 'concrete_pavement', 3, 0.5],
    school: [0xc8ab84, null, 0, 0.7],
    parking: [0xb0b0ae, 'clean_asphalt', 4, 0.8],
    pitch: [0x4c8a3a, null, 0, 2.2],
  };
  for (const [kind, [color, tex, tile, order]] of Object.entries(styles)) {
    if (!landuse[kind]?.length) continue;
    addFlat(scene, polygonsGeometry(landuse[kind]), standard(color, tex ? surface(tex, tile) : {}), order);
  }
}

// ---------- 하늘·빛 ----------
// 배경: 실제 하늘 사진(4K), 환경광: 같은 하늘의 HDR(밝기 정보까지 담긴 사진). 해 그림자는 내 앞쪽만 계산
function buildLighting(scene, renderer, Q) {
  const bg = loader.load('assets/tex/sky_4k.jpg');
  bg.mapping = THREE.EquirectangularReflectionMapping;
  bg.colorSpace = THREE.SRGBColorSpace;
  scene.background = bg;
  // 후처리를 쓰면 배경도 색감 보정(톤 매핑)을 한 번 더 거쳐 어두워지므로 밝기를 보정
  scene.backgroundIntensity = Q.post ? 1.7 : 1.0;
  new RGBELoader().load('assets/tex/sky_1k.hdr', (hdr) => {
    const pmrem = new THREE.PMREMGenerator(renderer);
    scene.environment = pmrem.fromEquirectangular(hdr).texture;
    hdr.dispose();
    pmrem.dispose();
  });
  scene.environmentIntensity = 1.1;

  scene.fog = new THREE.FogExp2(0xb8c3d1, 0.00055); // 멀리 있는 건물이 뿌옇게 (대기 원근감)
  const sun = new THREE.DirectionalLight(0xfff1dc, 3.0);
  sun.castShadow = true;
  sun.shadow.mapSize.set(Q.shadowSize, Q.shadowSize);
  Object.assign(sun.shadow.camera, { left: -60, right: 60, top: 60, bottom: -60, near: 1, far: 800 });
  sun.shadow.bias = -0.0004;
  sun.shadow.normalBias = 0.04;
  scene.add(sun, sun.target);

  return (target) => {
    sun.target.position.copy(target);
    sun.position.copy(target).addScaledVector(SUN_DIR, 400);
    sun.target.updateMatrixWorld();
  };
}

export function buildWorld(scene, course, path, renderer, Q) {
  maxAniso = Math.min(8, renderer.capabilities.getMaxAnisotropy());
  const updateLighting = buildLighting(scene, renderer, Q);

  const [minx, miny, maxx, maxy] = course.bounds;
  const ground = new THREE.PlaneGeometry(maxx - minx + 8000, maxy - miny + 8000);
  ground.rotateX(-Math.PI / 2);
  ground.translate((minx + maxx) / 2, 0, -(miny + maxy) / 2);
  const pos = ground.attributes.position;
  for (let k = 0; k < pos.count; k++) ground.attributes.uv.setXY(k, pos.getX(k), -pos.getZ(k)); // 미터 단위 uv
  addFlat(scene, ground, standard(PALETTE.ground, surface('concrete_pavement', 3)), 0);

  // 물결: 물결 텍스처를 천천히 흘려서 강물이 움직이는 것처럼
  const waterNormal = texture('waternormals.jpg', 24);
  addFlat(scene, polygonsGeometry(course.water), standard(PALETTE.water, {
    roughness: 0.1, normalMap: waterNormal, normalScale: new THREE.Vector2(0.35, 0.35),
  }), 1);
  addFlat(scene, polygonsGeometry(course.green), standard(PALETTE.green, surface('leafy_grass', 3)), 2);
  buildLanduse(scene, course.landuse ?? {});
  buildRoads(scene, course.roads, course.route);
  buildCourse(scene, course, path);
  buildBuildings(scene, course.buildings, maxAniso);
  buildProps(scene, path);
  const updateTrees = buildTrees(scene, renderer, course.trees, Q.treeRadius);
  const updateGrass = buildGrass(scene, renderer, course, Q);
  const updateLandmarks = buildLandmarks(scene, course);
  buildStreetProps(scene, course);

  const focus = new THREE.Vector3();
  return {
    // distance: 내가 달린 거리(m)
    update(distance, dt) {
      const ahead = path.frame(distance + 35, 0); // 그림자·3D 나무는 화면에 주로 보이는 앞쪽 기준
      focus.set(ahead.x, 0, ahead.z);
      updateLighting(focus);
      updateTrees(focus, dt);
      const near = path.frame(distance + 12, 0); // 잔디는 내 바로 앞뒤
      updateGrass(near.x, near.z, dt);
      updateLandmarks(dt);
      waterNormal.offset.x += dt * 0.004;
      waterNormal.offset.y += dt * 0.0025;
    },
  };
}
