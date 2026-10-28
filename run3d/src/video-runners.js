// 영상 위 투명 레이어만 렌더링한다. 코스·건물·나무는 만들지 않는다.
// 러너를 세우는 방법 두 가지:
//  - 코스 지도 모드 (영상에 {id}.course.json이 있을 때): 촬영자가 실제로 달린 길 위에 카메라와 러너를 놓는다.
//    커브·거리감이 실제 길을 따르고, 시선이 육지(진행 방향 오른쪽) 위를 지나면 가려진 것으로 본다.
//  - 화면 길 추적 모드 (지도가 없을 때): 영상 속 길 경계를 색으로 추적해 평평한 땅 위에 세운다 (video-projection.js).
import * as THREE from 'three';
import { createRunner, loadRunnerModel } from './runner.js';
import { gaitOf } from './race.js';
import { createRoadProjector, updateCrestPlane } from './video-projection.js';

// 1인칭: 영상 카메라 = 내 눈. 내 아바타는 그리지 않고, 다른 러너는 나와의 거리(gap)만큼 앞에 선다
const ME_DEPTH = 0;
const MIN_GAP = 2.5; // 이보다 가까이 앞선 러너는 화면을 가리므로 숨김 (순위표에는 보임)
// 러너 키(m): 러너마다 heightM(video.js). 카메라 높이를 제대로(1.4m) 잡은 뒤로는 발이 땅에 붙어서 실제 키 그대로가 자연스럽다
// (예전에 0.8배로 줄였던 건 발이 30~40cm 떠서 커 보였던 탓). 모델(Xbot) 키 1.81m를 이 키에 맞춰 줄이고 늘린다
const MODEL_HEIGHT_M = 1.81;
// 지형·물체 가림: 아직 멀쩡한 구간에서도 엉뚱하게 잘려서 기본은 끔 (사용자 결정, 2026-10-11). ?occ=1 이면 켬 (확인용)
const OCCLUSION = new URLSearchParams(location.search).get('occ') === '1';
const DEFAULT_HEIGHT_M = 1.70;
const LANE_M = 0.6; // 차선 1칸 = 0.6m
const FADE_PER_S = 3; // 가려지거나 나타날 때 1초에 바뀌는 투명도
// 해 위치 (지도 기준: 방위각은 북쪽에서 시계 방향, 고도는 지평선에서 위로, 도).
// 해안길 영상은 남반구 낮이라 해가 북쪽 -> 북동쪽으로 달리는 동안 왼쪽 앞. 오른쪽 언덕이 밝게 빛나는 것과 맞다
const SUN = { azimuth: 0, elevation: 45 };
const REAL_SHADOW_M = 60; // 이보다 가까운 러너는 모두 몸 모양 그대로의 진짜 그림자, 더 먼 러너는 해 반대쪽으로 늘인 흐린 그림자

export async function createVideoRunners(canvas, runners, alignment, road) {
  await loadRunnerModel('assets/Xbot.glb');
  const renderer = new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true, powerPreference: 'low-power' });
  renderer.setClearColor(0x000000, 0);
  renderer.localClippingEnabled = true; // 보이는 길 끝(가림선) 아래를 잘라 지형에 가려진 것처럼
  // 영상(압축된 720~1080p)보다 또렷하게 그리면 붙여 넣은 티가 난다 -> 해상도를 조금 낮추고 아주 살짝 흐리게.
  // 폰에서는 그릴 점 수가 줄어 발열도 줄어든다
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.25));
  canvas.style.filter = 'blur(0.35px)';
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.15;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  const scene = new THREE.Scene();
  // 멀수록 공기 때문에 뿌옇게 (색은 영상 지평선 색을 따라감)
  scene.fog = new THREE.Fog(0xcfd8e0, 20, 900);
  const DEFAULT_SKY = new THREE.Color(0xe6f2ff);
  const DEFAULT_GROUND = new THREE.Color(0x6c7257);
  const hemi = new THREE.HemisphereLight(DEFAULT_SKY, DEFAULT_GROUND, 2.4);
  scene.add(hemi);
  const sunlight = new THREE.DirectionalLight(0xfff1d5, 2.5);
  sunlight.position.set(-3, 8, 4);
  sunlight.castShadow = true;
  sunlight.shadow.mapSize.set(2048, 2048);
  sunlight.shadow.camera.near = 1;
  sunlight.shadow.camera.far = 200;
  sunlight.shadow.normalBias = 0.02;
  sunlight.shadow.bias = -0.0008; // 그림자 받는 판끼리 겹치는 곳에 생기는 가는 줄무늬 방지
  scene.add(sunlight, sunlight.target);
  // 진짜 그림자를 받는 투명한 땅 (그림자만 보이고 땅 자체는 안 보임)
  // 러너마다 발밑에 그림자만 받는 투명한 판 (진짜 그림자). 해 하나가 모든 러너의 그림자를 한 번에 그린다
  const catcherGeometry = new THREE.PlaneGeometry(1, 1);
  function makeCatcher() {
    const c = new THREE.Mesh(catcherGeometry, new THREE.ShadowMaterial({ opacity: 0.45, depthWrite: false }));
    c.rotation.order = 'YXZ';
    c.rotation.x = -Math.PI / 2;
    c.receiveShadow = true;
    c.visible = false;
    scene.add(c);
    patchMaterial(c.material, false);
    // 판 가장자리로 갈수록 그림자를 흐리게: 판 끝이 그림자 지도 경계와 겹치며 가는 선이 생기지 않게
    const depthPatch = c.material.onBeforeCompile;
    c.material.onBeforeCompile = (shader, r) => {
      depthPatch(shader, r);
      shader.vertexShader = shader.vertexShader
        .replace('void main() {', 'varying vec2 vPlane;\nvoid main() {\n  vPlane = position.xy;');
      shader.fragmentShader = shader.fragmentShader
        .replace('void main() {', 'varying vec2 vPlane;\nvoid main() {')
        .replace('gl_FragColor = vec4( color, opacity * ( 1.0 - getShadowMask() ) );',
          `float edge = (1.0 - smoothstep(0.32, 0.5, abs(vPlane.x))) * (1.0 - smoothstep(0.32, 0.5, abs(vPlane.y)));
  gl_FragColor = vec4( color, opacity * edge * ( 1.0 - getShadowMask() ) );`);
    };
    c.material.customProgramCacheKey = () => 'video-catcher';
    return c;
  }
  // 영상처럼 아주 약한 잡티 (깨끗한 CG 면이 튀지 않게)
  const grain = { value: 0 };
  // 픽셀 단위 가림 (AR 앱처럼): 아바타·그림자·이름표를 그릴 때 픽셀마다 그 자리 영상 속 물체의 거리(깊이 지도)와
  // 비교해, 물체가 더 가까우면 그 픽셀을 버린다. 창고 모서리·난간·덤불·차 모양 그대로 잘려 들어가고 빠져나온다
  const depthU = {
    uDepth: { value: null },
    uDepthTile: { value: new THREE.Vector4(0, 0, 1, 1) },
    uDepth2: { value: null },
    uDepthTile2: { value: new THREE.Vector4(0, 0, 1, 1) },
    uDepthOn: { value: 0 },
    uView: { value: new THREE.Vector2(1, 1) },
    uLens: { value: new THREE.Vector4(705, 480, 270, -0.183) }, // 초점(px), 중심 x·y, 렌즈 휘어짐
    uSrc: { value: new THREE.Vector2(960, 540) },
    uZ: { value: new THREE.Vector2(0.5, 300) }, // 깊이 지도 로그 눈금 범위 (m)
    uNearFar: { value: new THREE.Vector2(0.1, 30000) },
  };
  const depthGlsl = `
uniform sampler2D uDepth; uniform vec4 uDepthTile; uniform sampler2D uDepth2; uniform vec4 uDepthTile2; uniform float uDepthOn; uniform vec2 uView;
uniform vec4 uLens; uniform vec2 uSrc; uniform vec2 uZ; uniform vec2 uNearFar; uniform float uGrainTime;
float videoSceneDepth(vec2 frag) {
  vec2 p = vec2(frag.x / uView.x, 1.0 - frag.y / uView.y) * uSrc;
  vec2 n = (p - uLens.yz) / uLens.x;
  vec2 q = (n * (1.0 + uLens.w * dot(n, n)) * uLens.x + uLens.yz) / uSrc;
  q = clamp(q, vec2(0.004, 0.006), vec2(0.996, 0.994));
  float g = texture2D(uDepth, uDepthTile.xy + q * uDepthTile.zw).r;
  return uZ.x * exp(g * log(uZ.y / uZ.x));
}
`;
  const depthTest = `
  if (uDepthOn > 0.5) {
    float zf = 2.0 * uNearFar.x * uNearFar.y / (uNearFar.y + uNearFar.x - (gl_FragCoord.z * 2.0 - 1.0) * (uNearFar.y - uNearFar.x));
    if (videoSceneDepth(gl_FragCoord.xy) < zf - (0.4 + 0.06 * zf)) discard;
  }
`;
  const grainGlsl = `#include <dithering_fragment>
  gl_FragColor.rgb += (fract(sin(dot(gl_FragCoord.xy + uGrainTime, vec2(12.9898, 78.233))) * 43758.5453) - 0.5) * 0.035;`;
  function patchMaterial(m, withGrain) {
    m.onBeforeCompile = (shader) => {
      Object.assign(shader.uniforms, depthU, { uGrainTime: grain });
      let fs = shader.fragmentShader.replace('void main() {', depthGlsl + 'void main() {' + depthTest);
      if (withGrain) fs = fs.replace('#include <dithering_fragment>', grainGlsl);
      shader.fragmentShader = fs;
    };
    m.customProgramCacheKey = () => (withGrain ? 'video-occ-grain' : 'video-occ');
  }
  const camera = new THREE.PerspectiveCamera(65, 16 / 9, 0.1, 30000);
  camera.position.set(0, 1.5, 0); // 영상 카메라 높이 추정치 (길 폭 2m 가정, 길 폭이 줄어드는 비율에서 계산)
  const roadPosition = createRoadProjector(camera, road);
  const shadowCanvas = document.createElement('canvas');
  shadowCanvas.width = shadowCanvas.height = 64;
  const ctx = shadowCanvas.getContext('2d');
  const gradient = ctx.createRadialGradient(32, 32, 0, 32, 32, 32);
  gradient.addColorStop(0, 'rgba(0,0,0,0.38)');
  gradient.addColorStop(0.45, 'rgba(0,0,0,0.22)');
  gradient.addColorStop(1, 'rgba(0,0,0,0)');
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, 64, 64);
  const shadowTexture = new THREE.CanvasTexture(shadowCanvas);
  const shadowGeometry = new THREE.PlaneGeometry(1, 1);
  const avatars = runners.map((r) => {
    const avatar = createRunner(r.color, r.isMe ? null : r.name);
    avatar.root.rotation.y = Math.PI;
    scene.add(avatar.root);
    const shadow = new THREE.Mesh(shadowGeometry,
      new THREE.MeshBasicMaterial({ map: shadowTexture, transparent: true, depthWrite: false, toneMapped: false, fog: false }));
    shadow.rotation.order = 'YXZ';
    shadow.rotation.x = -Math.PI / 2;
    scene.add(shadow);
    patchMaterial(shadow.material, false);
    // 러너마다 자기 자르기 평면: 길 끝을 넘어간 러너만 가림선 아래가 잘리고, 나머지는 자르지 않는다
    // (가림선 평면은 카메라에서 비스듬히 내려가므로 모두에게 걸면 가까운 러너의 다리까지 잘린다)
    const clip = new THREE.Plane(new THREE.Vector3(0, 1, 0), 1e4);
    avatar.root.traverse((o) => {
      if (!o.material) return;
      o.material.clippingPlanes = [clip];
      o.material.clipShadows = true; // 지형에 가려 잘린 부분은 그림자도 안 생기게
      if (o.isSprite) o.material.fog = false; // 이름표는 멀어도 또렷하게
      patchMaterial(o.material, !o.isSprite);
    });
    const label = avatar.root.children.find((o) => o.isSprite) ?? null;
    // 코스 지도 모드에서 가려질 때 서서히 사라지게 하려고 재질의 원래 투명도를 기억
    const materials = [shadow.material];
    avatar.root.traverse((o) => { if (o.material) materials.push(o.material); });
    for (const m of materials) Object.assign(m.userData, { baseOpacity: m.opacity, baseTransparent: m.transparent });
    return { runner: r, avatar, shadow, clip, label, materials, seen: 1, catcher: makeCatcher() };
  });
  const runnerScale = (r) => (alignment.size / 100) * ((r?.heightM ?? DEFAULT_HEIGHT_M) / MODEL_HEIGHT_M);
  let sfm = null;
  let sfmCamera = false; // 지금 카메라가 3D 복원 모드 설정인지
  function resize() {
    // iframe와 똑같은 16:9 cover 좌표계를 사용해 세로 화면에서도 원근감 유지.
    const w = Math.max(window.innerWidth, window.innerHeight * 16 / 9);
    const h = w * 9 / 16;
    renderer.setSize(w, h, false);
    camera.aspect = w / h;
    if (sfmCamera) {
      // 3D 복원 모드: 영상 렌즈와 같은 화각, 카메라는 원점에서 앞(-z)을 그대로 본다 (기울기는 데이터에 들어 있음)
      camera.fov = sfm.fov;
      camera.position.set(0, 0, 0);
      camera.rotation.set(0, 0, 0);
      camera.clearViewOffset();
      camera.updateProjectionMatrix();
      camera.updateMatrixWorld();
      return;
    }
    camera.position.set(0, 1.5, 0);
    camera.fov = alignment.fov;
    const halfFov = THREE.MathUtils.degToRad(camera.fov / 2);
    camera.rotation.x = -Math.atan((0.5 - alignment.horizon / 100) * 2 * Math.tan(halfFov));
    camera.setViewOffset(w, h, (0.5 - alignment.center / 100) * w, 0, w, h);
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();
  }
  resize();

  // 해 방향 (three.js 카메라 기준, 땅에서 해 쪽을 가리키는 단위 벡터)
  const sunDir = new THREE.Vector3(-3, 8, 4).normalize();
  function updateSun(videoTime) {
    if (!course) return; // 지도가 없으면 진행 방향을 몰라 기본값 (왼쪽 뒤 위)
    const a = THREE.MathUtils.degToRad(SUN.azimuth);
    const e = THREE.MathUtils.degToRad(SUN.elevation);
    const east = Math.sin(a) * Math.cos(e);
    const north = Math.cos(a) * Math.cos(e);
    const [hx, hy] = course.heading(course.sAt(videoTime)); // 진행 방향 (동, 북)
    sunDir.set(east * hy - north * hx, Math.sin(e), -(east * hx + north * hy)).normalize();
  }

  // 발밑 그림자: 해 반대쪽으로 키에 맞게 늘인 흐린 그림자
  const shadowAway = new THREE.Vector3();
  function placeShadow(shadow, position, r) {
    const s = runnerScale(r);
    shadowAway.set(-sunDir.x, 0, -sunDir.z);
    const flat = shadowAway.length();
    const length = 0.5 + Math.min(4, (1.8 * flat) / Math.max(sunDir.y, 0.2)); // 키 1.8m(모델)의 그림자 길이
    if (flat > 1e-3) shadowAway.divideScalar(flat);
    else shadowAway.set(0, 0, 1);
    shadow.rotation.y = Math.atan2(-shadowAway.x, -shadowAway.z);
    shadow.scale.set(0.75 * s, length * s, 1);
    shadow.position.copy(position).addScaledVector(shadowAway, (length / 2 - 0.25) * s);
    shadow.position.y += 0.015;
  }

  // 영상 색 따라가기: 0.5초마다 영상을 작게 떠서 하늘·지평선·길 색과 밝기를 조명에 반영
  const sampler = document.createElement('canvas');
  sampler.width = 32;
  sampler.height = 18;
  const sctx = sampler.getContext('2d', { willReadFrequently: true });
  const tmp = new THREE.Color();
  let lastSample = -Infinity;
  let exposure = 1.15;
  function sampleVideo() {
    const now = performance.now();
    if (now - lastSample < 500) return;
    lastSample = now;
    const video = document.getElementById('player');
    if (!(video instanceof HTMLVideoElement) || video.readyState < 2) return; // 유튜브 모드는 화면을 읽을 수 없음
    let data;
    try {
      sctx.drawImage(video, 0, 0, 32, 18);
      data = sctx.getImageData(0, 0, 32, 18).data;
    } catch {
      return;
    }
    const mean = (y0, y1, x0 = 0, x1 = 32) => {
      let r = 0, g = 0, b = 0, n = 0;
      for (let y = y0; y < y1; y++) for (let x = x0; x < x1; x++) {
        const i = (y * 32 + x) * 4;
        r += data[i]; g += data[i + 1]; b += data[i + 2]; n++;
      }
      return tmp.setRGB(r / n / 255, g / n / 255, b / n / 255, THREE.SRGBColorSpace);
    };
    const k = 0.35; // 장면이 바뀔 때 조명이 갑자기 튀지 않게 조금씩
    hemi.color.lerp(mean(0, 5).lerp(DEFAULT_SKY, 0.5), k);
    hemi.groundColor.lerp(mean(13, 18, 8, 24).lerp(DEFAULT_GROUND, 0.4), k);
    scene.fog.color.lerp(mean(7, 10), k);
    const all = mean(0, 18);
    const lum = 0.2126 * all.r + 0.7152 * all.g + 0.0722 * all.b; // 선형 밝기
    const want = THREE.MathUtils.clamp(1.15 * Math.pow(lum / 0.3, 0.4), 0.8, 1.5);
    exposure += (want - exposure) * k;
    renderer.toneMappingExposure = exposure;
  }

  // 한 장면 그리기: 가장 가까운 러너에게만 진짜 그림자(해 방향 그림자 지도)를 준다
  let depth = null;
  const viewSize = new THREE.Vector2();
  function bindDepth(videoTime) {
    const d = sfmCamera && depth ? depth.at(videoTime) : null;
    depthU.uDepthOn.value = d ? 1 : 0;
    if (!d) return false;
    depthU.uDepth.value = d.texture;
    depthU.uDepthTile.value.set(...d.tile);
    depthU.uDepth2.value = d.texture2;
    depthU.uDepthTile2.value.set(...d.tile2);
    renderer.getDrawingBufferSize(viewSize);
    depthU.uView.value.copy(viewSize);
    depthU.uNearFar.value.set(camera.near, camera.far);
    return true;
  }

  // 60m 안의 러너는 모두 진짜 그림자 (순위가 바뀌어도 그림자 모양이 갑자기 바뀌지 않게)
  const box = new THREE.Box3();
  const center = new THREE.Vector3();
  function render() {
    grain.value = (grain.value + 17.31) % 1000;
    const real = [];
    for (const entry of avatars) {
      const on = entry.avatar.root.visible && entry.shadow.visible
        && entry.avatar.root.position.length() < REAL_SHADOW_M;
      if (entry.castsShadow !== on) {
        entry.avatar.root.traverse((o) => { if (o.isMesh) o.castShadow = on; });
        entry.castsShadow = on;
      }
      entry.catcher.visible = on;
      if (!on) continue;
      real.push(entry);
      const p = entry.avatar.root.position;
      const sc = runnerScale(entry.runner);
      entry.shadow.visible = false; // 흐린 그림자 대신 진짜 그림자
      // 판은 해 반대쪽으로 늘어지는 그림자를 다 담을 만큼: 발밑에서 그림자 쪽으로 밀어 놓는다
      const away = new THREE.Vector3(-sunDir.x, 0, -sunDir.z);
      const flat = away.length() || 1;
      away.divideScalar(flat);
      const len = 1 + Math.min(5, (1.8 * flat) / Math.max(sunDir.y, 0.2));
      entry.catcher.rotation.y = Math.atan2(-away.x, -away.z);
      entry.catcher.scale.set(3.2 * sc, (len + 3) * sc, 1);
      entry.catcher.position.copy(p).addScaledVector(away, (len / 2) * sc);
      entry.catcher.position.y = p.y + 0.01;
      entry.catcher.material.opacity = 0.45 * entry.seen;
    }
    if (real.length) {
      // 해 그림자 카메라를 진짜 그림자 러너들이 다 들어오게 맞춘다
      box.makeEmpty();
      for (const e of real) box.expandByPoint(e.avatar.root.position);
      box.getCenter(center);
      let r = 0;
      for (const e of real) r = Math.max(r, e.avatar.root.position.distanceTo(center));
      r += 25; // 그림자 지도 경계가 그림자 받는 판 위에 걸리면 가는 선이 생긴다 -> 넉넉히
      sunlight.target.position.copy(center);
      sunlight.position.copy(center).addScaledVector(sunDir, 80);
      const cam = sunlight.shadow.camera;
      cam.left = cam.bottom = -r;
      cam.right = cam.top = r;
      cam.updateProjectionMatrix();
    } else {
      sunlight.position.copy(sunDir).multiplyScalar(30);
      sunlight.target.position.set(0, 0, 0);
    }
    renderer.render(scene, camera);
  }

  function setOpacity(entry, v) {
    for (const m of entry.materials) {
      const transparent = v < 1 || m.userData.baseTransparent;
      // 투명 여부가 바뀌면 재질을 다시 준비시켜야 한다 (안 하면 그래픽 카드에 따라 잠깐 안 그려져 깜빡임)
      if (m.transparent !== transparent) {
        m.transparent = transparent;
        m.needsUpdate = true;
      }
      m.opacity = m.userData.baseOpacity * v;
    }
  }

  // 코스 지도 모드: 카메라 기준 (오른쪽, 앞) m를 three.js 좌표로. 카메라는 원점에서 -z를 본다
  function updateOnCourse(dt, race, visible, videoTime, course) {
    const moving = race.state === 'running' || race.state === 'finished';
    const sCam = course.sAt(videoTime);
    for (const entry of avatars) {
      const { runner: r, avatar, shadow, clip, label } = entry;
      clip.set(new THREE.Vector3(0, 1, 0), 1e4);
      const gap = r.isMe ? 0 : r.distance - race.me.distance;
      const sRun = sCam + ME_DEPTH + gap;
      const at = course.toCamera(videoTime, sRun, r.lane * LANE_M);
      // 카메라보다 뒤거나 너무 가까우면(나보다 1.5m 넘게 뒤처짐) 숨김. 순위표에는 보임
      const placeable = visible && !r.isMe && gap >= MIN_GAP && at.forward > 1.5 && sRun <= course.length;
      // 좌우는 항상 영상 속 실제 길(색 추적, 보이는 길 끝 너머는 연장)에 맞춘다. 지도 선은 몇 m씩 틀려서 화면에선 어긋남
      const onScreen = placeable ? roadPosition(at.forward, r.lane, videoTime, { isMe: r.isMe, noCrest: true }) : null;
      // 가려짐은 지도로: 시선이 육지 쪽(언덕·절벽)을 지나면 사라짐. 화면 길 추적의 "길 끝"은 장면마다 흔들려서 쓰지 않는다
      const target = placeable && (r.isMe || !course.occluded(videoTime, sRun)) ? 1 : 0;
      entry.seen += Math.sign(target - entry.seen) * Math.min(Math.abs(target - entry.seen), FADE_PER_S * Math.max(dt, 1 / 30));
      if (!placeable) entry.seen = 0;
      const shown = entry.seen > 0.01;
      avatar.root.visible = shadow.visible = shown;
      if (!shown) continue;
      setOpacity(entry, entry.seen);
      if (label) label.visible = true;
      // 좌우 위치는 항상 영상 속 길(보이는 길 끝 너머는 그 방향으로 연장)에 맞춘다. 지도는 거리·가려짐 판단에만
      const position = onScreen ? onScreen.position.setY(0) : new THREE.Vector3(at.right, 0, -at.forward);
      avatar.root.position.copy(position);
      const next = onScreen ? roadPosition(at.forward + 1, r.lane, videoTime, { isMe: true, noCrest: true }) : null;
      if (next) avatar.root.rotation.y = Math.atan2(next.position.x - position.x, next.position.z - position.z);
      else {
        const n2 = course.toCamera(videoTime, sRun + 1, r.lane * LANE_M);
        avatar.root.rotation.y = Math.atan2(n2.right - at.right, -(n2.forward - at.forward));
      }
      avatar.root.scale.setScalar(runnerScale(r));
      placeShadow(shadow, position, r);
      avatar.animate(dt, moving ? r.cadence : 0, gaitOf(moving ? r.speedKmh : 0), moving ? r.speedKmh / 3.6 : 0, runnerScale(r));
    }
    render();
  }

  // 앞길 추적 모드(가장 정확): 촬영자가 나중에 지나간 길 위 점을 거꾸로 추적한 화면 위치에 발을 놓는다
  const ray = new THREE.Vector3();
  function placeOnScreen(x, y, depth) {
    ray.set(x * 2 - 1, 1 - y * 2, 0.5).unproject(camera).sub(camera.position).normalize();
    if (ray.z >= -1e-3) return null;
    return camera.position.clone().addScaledVector(ray, depth / -ray.z); // 앞으로 depth m 되는 곳 (오르내리막 높이 그대로)
  }
  function updateAhead(dt, race, visible, videoTime) {
    const moving = race.state === 'running' || race.state === 'finished';
    for (const entry of avatars) {
      const { runner: r, avatar, shadow, clip, label } = entry;
      clip.set(new THREE.Vector3(0, 1, 0), 1e4);
      const gap = r.distance - race.me.distance;
      let spot = null;
      if (!r.isMe && visible && gap >= MIN_GAP) {
        spot = ahead.lookup(videoTime, gap, r.lane);
        // 추적표의 가장 가까운 점(약 8m)보다 가까우면 화면 아래쪽 길(색 추적)에 평평한 땅으로 세운다
        if (!spot && gap < ahead.nearest(videoTime)) {
          const near = roadPosition(gap, r.lane, videoTime, { noCrest: true });
          if (near) spot = { position: near.position.setY(0) };
        }
      }
      const target = spot ? 1 : 0;
      entry.seen += Math.sign(target - entry.seen) * Math.min(Math.abs(target - entry.seen), FADE_PER_S * Math.max(dt, 1 / 30));
      if (r.isMe || !visible || gap < MIN_GAP) entry.seen = 0;
      const position = spot ? (spot.position ?? placeOnScreen(spot.x, spot.y, gap)) : entry.last;
      const shown = entry.seen > 0.01 && position;
      avatar.root.visible = shadow.visible = !!shown;
      if (!shown) continue;
      entry.last = position;
      setOpacity(entry, entry.seen);
      if (label) label.visible = true;
      avatar.root.position.copy(position);
      const next = ahead.lookup(videoTime, gap + 1.5, r.lane);
      const nextPos = next && placeOnScreen(next.x, next.y, gap + 1.5);
      if (nextPos) avatar.root.rotation.y = Math.atan2(nextPos.x - position.x, nextPos.z - position.z);
      else avatar.root.rotation.y = Math.PI;
      avatar.root.scale.setScalar(runnerScale(r));
      placeShadow(shadow, position, r);
      avatar.animate(dt, moving ? r.cadence : 0, gaitOf(moving ? r.speedKmh : 0), moving ? r.speedKmh / 3.6 : 0, runnerScale(r));
    }
    render();
  }

  // 3D 복원 모드 (RunSocial식): 러너를 촬영자가 실제로 지나간 3D 발밑 위치에 세우고, 영상과 같은 카메라로 그린다
  function updateSfm(dt, race, visible, videoTime) {
    const moving = race.state === 'running' || race.state === 'finished';
    const pixelOcc = OCCLUSION ? bindDepth(videoTime) : (depthU.uDepthOn.value = 0, false);
    for (const entry of avatars) {
      const { runner: r, avatar, shadow, clip, label } = entry;
      clip.set(new THREE.Vector3(0, 1, 0), 1e4);
      const gap = r.distance - race.me.distance;
      const p = !r.isMe && visible && gap >= MIN_GAP ? sfm.place(videoTime, gap, r.lane * LANE_M) : null;
      entry.debug = { t: videoTime, gap, occ: p?.occ ?? null };
      // 지형 가려짐: 깊이 지도가 있으면 픽셀 단위로(셰이더). 없을 때만 예전 방식 —
      // 머리까지 가려지면 서서히 사라지고, 아래쪽만 가려지면 그 높이 아래를 잘라 그린다
      const occ = !OCCLUSION || pixelOcc ? 0 : (p?.occ ?? 0);
      const target = p && occ < 1.5 ? 1 : 0;
      entry.seen += Math.sign(target - entry.seen) * Math.min(Math.abs(target - entry.seen), FADE_PER_S * Math.max(dt, 1 / 30));
      if (r.isMe || !visible || gap < MIN_GAP) entry.seen = 0;
      const position = p ? new THREE.Vector3(p.x, p.y, p.z) : entry.last;
      const shown = entry.seen > 0.01 && position;
      avatar.root.visible = shadow.visible = !!shown;
      if (!shown) continue;
      entry.last = position;
      setOpacity(entry, entry.seen);
      if (label) label.visible = true;
      avatar.root.position.copy(position);
      if (p && occ > 0.05) clip.set(new THREE.Vector3(0, 1, 0), -(position.y + occ * runnerScale(r)));
      const n = sfm.place(videoTime, gap + 1.5, r.lane * LANE_M);
      if (n) avatar.root.rotation.y = Math.atan2(n.x - position.x, n.z - position.z);
      avatar.root.scale.setScalar(runnerScale(r));
      placeShadow(shadow, position, r);
      shadow.visible = !(p && occ > 0.05); // 발이 가려지면 그림자도 안 보임
      avatar.animate(dt, moving ? r.cadence : 0, gaitOf(moving ? r.speedKmh : 0), moving ? r.speedKmh / 3.6 : 0, runnerScale(r));
    }
    render();
  }

  let course = null;
  let ahead = null;
  // 확인용: 콘솔에서 __runnerDebug()로 러너별 거리·가려짐·보임 상태
  window.__runnerDebug = () => avatars.map((e) => ({ name: e.runner.name, seen: +e.seen.toFixed(2), visible: e.avatar.root.visible, ...e.debug }));
  return {
    resize,
    setCourse(value) { course = value; },
    setAhead(value) { ahead = value; },
    setSfm(value) { sfm = value; },
    setDepth(value) { depth = value; },
    update(dt, race, visible, videoTime = 0) {
      updateSun(videoTime);
      sampleVideo();
      // 3D 복원 데이터가 있는 구간이면 영상과 같은 카메라로, 없으면 이전 방식으로 (카메라 설정도 바꿈)
      const useSfm = !!sfm?.covers(videoTime);
      if (useSfm !== sfmCamera) {
        sfmCamera = useSfm;
        resize();
      }
      if (useSfm) {
        updateSfm(dt, race, visible, videoTime);
        return;
      }
      depthU.uDepthOn.value = 0;
      if (ahead) {
        updateAhead(dt, race, visible, videoTime);
        return;
      }
      if (course) {
        updateOnCourse(dt, race, visible, videoTime, course);
        return;
      }
      const moving = race.state === 'running' || race.state === 'finished';
      const rows = road.projectionRowsAt(videoTime);
      for (const { runner: r, avatar, shadow, clip, label } of avatars) {
        // 카메라는 내 뒤 ME_DEPTH m. 다른 러너도 나를 기준으로 같은 축에 놓고,
        // 나보다 1.5m 넘게 뒤처져 카메라에 너무 가까워진 러너는 숨긴다 (화면을 가리지 않게, 순위표에는 보임)
        const gap = r.distance - race.me.distance;
        const depth = ME_DEPTH + (r.isMe ? 0 : gap);
        // 보이는 길 끝을 넘어가면 다리부터 가림선 아래로 가려지고, 다 넘어가면 숨김 (나는 절반까지만)
        const placed = !r.isMe && depth >= MIN_GAP ? roadPosition(depth, r.lane, videoTime) : null;
        const shown = visible && placed !== null;
        avatar.root.visible = shown;
        shadow.visible = shown && placed.hidden === 0;
        if (!shown) continue;
        const { position } = placed;
        // 머리까지 가림선 아래로 내려가면 이름표만 떠 보이지 않게 이름표도 숨긴다
        if (label) label.visible = placed.hidden < 1.5;
        if (placed.hidden > 0) updateCrestPlane(clip, camera, rows);
        else clip.set(new THREE.Vector3(0, 1, 0), 1e4);
        avatar.root.position.copy(position);
        const ahead = roadPosition(depth+1, r.lane, videoTime, { isMe: true });
        if (ahead) avatar.root.rotation.y = Math.atan2(ahead.position.x-position.x, ahead.position.z-position.z);
        avatar.root.scale.setScalar(runnerScale(r));
        placeShadow(shadow, position, r);
        avatar.animate(dt, moving ? r.cadence : 0, gaitOf(moving ? r.speedKmh : 0), moving ? r.speedKmh / 3.6 : 0, runnerScale(r));
      }
      render();
    },
  };
}
