// 러너 캐릭터: 모션캡처 동작이 들어 있는 사람 모델(Xbot)을 쓰고,
// 다리 움직임 빠르기를 실제 걸음 수(cadence)에 맞춘다.
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import * as SkeletonUtils from 'three/addons/utils/SkeletonUtils.js';

const GAITS = ['idle', 'walk', 'run'];
let base = null;
// 동작마다 "발이 땅에 붙어 있을 때 뒤로 밀리는 속도" (m/s, 재생 속도 1, 모델 키 그대로).
// 러너가 실제로 움직이는 속도를 이걸로 나누면 발이 미끄러지지 않는 재생 속도가 된다
const groundSpeed = {};

export async function loadRunnerModel(url) {
  base = await new GLTFLoader().loadAsync(url);
  for (const name of ['walk', 'run']) groundSpeed[name] = measureGroundSpeed(name);
}

function measureGroundSpeed(name) {
  const clip = THREE.AnimationClip.findByName(base.animations, name);
  const model = SkeletonUtils.clone(base.scene);
  const feet = [];
  model.traverse((o) => { if (o.isBone && /(Left|Right)Foot$/.test(o.name)) feet.push(o); });
  if (!clip || !feet.length) return null;
  const mixer = new THREE.AnimationMixer(model);
  mixer.clipAction(clip).play();
  const n = 120;
  const dt = clip.duration / n;
  const p = new THREE.Vector3();
  const samples = feet.map(() => []);
  for (let i = 0; i <= n; i++) {
    mixer.setTime(i * dt);
    model.updateMatrixWorld(true);
    feet.forEach((f, k) => { f.getWorldPosition(p); samples[k].push([p.y, p.z]); });
  }
  // 발이 가장 낮은 높이 근처(2cm 안)에 있는 동안 = 땅을 딛고 있는 동안. 그때 앞뒤로 움직인 속도
  const speeds = [];
  for (const s of samples) {
    const low = Math.min(...s.map((v) => v[0]));
    for (let i = 1; i < s.length; i++) {
      if (s[i][0] < low + 0.02 && s[i - 1][0] < low + 0.02) speeds.push(Math.abs(s[i][1] - s[i - 1][1]) / dt);
    }
  }
  if (speeds.length < 4) return null;
  speeds.sort((a, b) => a - b);
  const v = speeds[speeds.length >> 1];
  return v > 0.2 ? v : null; // 제자리 동작이 아니면(발이 안 밀림) 쓰지 않음
}

export function createRunner(color, label) {
  const root = new THREE.Group();
  const model = SkeletonUtils.clone(base.scene);
  model.traverse((o) => {
    if (!o.isMesh) return;
    o.castShadow = true;
    o.frustumCulled = false; // 움직이는 팔다리가 화면 가장자리에서 잘려 보이지 않게
    o.material = o.material.clone();
    // 몸통은 러너 색, 관절은 어두운 회색
    if (o.material.name.includes('Joints')) o.material.color.set(0x2b2f36);
    else o.material.color.set(color);
    o.material.roughness = 0.5;
  });
  root.add(model);

  const mixer = new THREE.AnimationMixer(model);
  const clips = {};
  const actions = {};
  const weights = { idle: 1, walk: 0, run: 0 };
  for (const name of GAITS) {
    clips[name] = THREE.AnimationClip.findByName(base.animations, name);
    actions[name] = mixer.clipAction(clips[name]);
    actions[name].setEffectiveWeight(weights[name]);
    actions[name].play();
  }

  if (label) {
    const tag = makeLabel(label, { fg: '#ffffff', bg: color, fontSize: 56, height: 0.32 });
    tag.position.y = 2.15;
    root.add(tag);
  }

  // speed(m/s)와 scale(모델 크기 배율)을 주면 발이 땅에서 미끄러지지 않게 재생 속도를 이동 속도에 맞춘다.
  // 안 주면 분당 걸음 수(cadence)에 맞춘다
  function animate(dt, cadence, gait, speed = 0, scale = 1) {
    // 걷기·달리기·서 있기 사이를 부드럽게 바꾼다
    const k = 1 - Math.exp(-dt * 6);
    for (const name of GAITS) {
      weights[name] += ((name === gait ? 1 : 0) - weights[name]) * k;
      actions[name].setEffectiveWeight(weights[name]);
    }
    for (const name of ['walk', 'run']) {
      if (speed > 0 && groundSpeed[name]) {
        actions[name].setEffectiveTimeScale(Math.min(3, speed / (groundSpeed[name] * scale)));
      } else if (cadence > 0) {
        // 동작 한 바퀴 = 2걸음. 분당 걸음 수에 맞춰 재생 속도를 정한다
        actions[name].setEffectiveTimeScale((cadence / 120) * clips[name].duration);
      }
    }
    mixer.update(dt);
  }

  return { root, animate };
}

// 캔버스에 글자를 그려서 항상 카메라를 보는 표지(스프라이트)로 만든다
export function makeLabel(text, { fg = '#1d2433', bg = '#ffffff', fontSize = 64, height = 1 } = {}) {
  const canvas = document.createElement('canvas');
  const ctx = canvas.getContext('2d');
  const font = `bold ${fontSize}px system-ui, "Malgun Gothic", sans-serif`;
  ctx.font = font;
  const pad = fontSize * 0.45;
  canvas.width = Math.ceil(ctx.measureText(text).width + pad * 2);
  canvas.height = Math.ceil(fontSize * 1.5);
  ctx.font = font;
  ctx.fillStyle = bg;
  ctx.beginPath();
  ctx.roundRect(0, 0, canvas.width, canvas.height, canvas.height / 2.6);
  ctx.fill();
  ctx.fillStyle = fg;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(text, canvas.width / 2, canvas.height / 2 + fontSize * 0.04);
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  // 글자 색이 조명·색감 보정에 영향받지 않게 toneMapped: false
  const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, depthWrite: false, toneMapped: false }));
  sprite.scale.set((height * canvas.width) / canvas.height, height, 1);
  sprite.renderOrder = 10;
  return sprite;
}
