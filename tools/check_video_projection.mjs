// Use the same Three.js build as video.html. Optional argument: module file path.
// node --experimental-vm-modules tools/check_video_projection.mjs .tmp/three.module.mjs
//
// 검사하는 규칙
//  1. 원근 일관성: 보이는 길 안의 러너는 땅(높이 0) 위, 화면 높이 = 그 거리의 평평한 땅 높이 (크기와 위치가 같은 원근)
//  2. 좌우: 발의 땅 위치는 그 높이의 길 경계 안
//  3. 가려짐: 보이는 길 끝을 넘은 러너는 넘은 거리 × 0.3m만큼 가림선 아래로 내려가
//     자르기 평면에 발은 잘리고 머리는 남는다. 1.9m 이상 내려가면 숨긴다. 가림선이 없으면 아무것도 자르지 않는다
//  4. 나(isMe)는 길 추적이 끊겨도 보이고, 0.8m 넘게 가려지지 않는다
//  5. 멀수록 작아진다
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { VideoRoad } from '../run3d/src/video-road.js';
const THREE = await import(pathToFileURL(resolve(process.argv[2] || '.tmp/three.module.mjs')).href);
const context = vm.createContext({});
const three = new vm.SyntheticModule(Object.keys(THREE), function () {
  for (const key of Object.keys(THREE)) this.setExport(key, THREE[key]);
}, { context });
const roadSource = new vm.SourceTextModule(await readFile(new URL('../run3d/src/video-road.js',import.meta.url),'utf8'), { context });
await roadSource.link(() => {}); await roadSource.evaluate();
const projection = new vm.SourceTextModule(await readFile(new URL('../run3d/src/video-projection.js',import.meta.url),'utf8'), { context });
await projection.link((spec) => spec === 'three' ? three : roadSource); await projection.evaluate();
const { createRoadProjector, roadBoundsAt, crestY, updateCrestPlane } = projection.namespace;
const data = JSON.parse(await readFile(new URL(`../run3d/assets/video/${process.env.VIDEO || 'o0EmQXATQQI'}.road.json`,import.meta.url),'utf8'));
const road = new VideoRoad(data.keyframes);

function makeCamera(fov, horizon, center) {
  const camera = new THREE.PerspectiveCamera(fov,16/9,.1,30000);
  camera.position.set(0,1.5,0);
  camera.rotation.x = -Math.atan((.5-horizon/100)*2*Math.tan(THREE.MathUtils.degToRad(fov/2)));
  camera.setViewOffset(1280,720,(.5-center/100)*1280,0,1280,720);
  camera.updateProjectionMatrix(); camera.updateMatrixWorld();
  return camera;
}
const screen = (camera, v) => { const s = v.clone().project(camera); return { x: (s.x+1)/2, y: (1-s.y)/2 }; };

let onRoad = 0, partly = 0, hidden = 0;
const plane = new THREE.Plane();
for (const fov of [40,65,95]) for (const horizon of [30,46,60]) for (const center of [15,50,85]) {
  const camera = makeCamera(fov, horizon, center);
  const place = createRoadProjector(camera,road);
  const horizonY = screen(camera, new THREE.Vector3(0,0,-1e6)).y;
  for (let time=30;time<3000;time+=30) {
    const rows = road.projectionRowsAt(time);
    const crest = crestY(rows);
    updateCrestPlane(plane, camera, rows);
    assert.ok(plane.distanceToPoint(new THREE.Vector3(0, 3, -2)) > 0, '카메라 위쪽은 잘리지 않음');
    for (const depth of [1,4.8,8,12,20,30,60,200]) for (const lane of [-1.1,0,.55,1.1]) {
      const placed = place(depth,lane,time);
      const flatY = screen(camera, new THREE.Vector3(0,0,-depth)).y;
      const beyond = crest !== null && flatY < crest;
      if (!placed) {
        assert.ok(flatY > 1.02 || flatY <= horizonY || beyond, `보이는 길 안인데 숨겨짐: ${time}s ${depth}m`);
        hidden++;
        continue;
      }
      const { position, hidden: cut } = placed;
      const ground = position.clone().setY(0);
      const g = screen(camera, ground);
      assert.ok(Math.abs(g.y - Math.min(flatY,1)) < 1e-4 || flatY > 1, `원근 불일치 ${time}s ${depth}m`);
      const bounds = roadBoundsAt(rows, Math.min(g.y,1), horizonY);
      assert.ok(g.x >= bounds[0]-1e-6 && g.x <= bounds[1]+1e-6, `길 밖 ${time}s ${depth}m`);
      if (!beyond) {
        assert.equal(cut, 0); assert.ok(Math.abs(position.y) < 1e-6, '발은 땅 위');
        onRoad++;
      } else {
        assert.ok(cut > 0 && cut < 1.9, `가려진 높이 ${cut}`);
        assert.ok(plane.distanceToPoint(position) < 1e-6, `길 끝 너머 발은 잘려야 함 ${time}s ${depth}m`);
        assert.ok(plane.distanceToPoint(position.clone().add(new THREE.Vector3(0,1.9,0))) > 0, '머리는 보여야 함');
        partly++;
      }
    }
    if (fov===65 && horizon===46) {
      const mine = place(4.8,0,time,{ isMe:true });
      assert.ok(mine, `내 러너 누락: ${time}s`);
      assert.ok(mine.hidden <= .8 + 1e-9, `내 러너가 너무 많이 가려짐: ${time}s`);
    }
  }
  if (fov===65 && horizon===46 && center===50) {
    const heights=[5,8,12,20].map(depth=>{
      const foot=place(depth,0,1200,{ isMe:true }).position, head=foot.clone().add(new THREE.Vector3(0,1.8,0));
      return Math.abs(screen(camera,foot).y-screen(camera,head).y);
    });
    assert.ok(heights.every((v,i)=>!i || v<heights[i-1]), '멀어질수록 캐릭터 크기가 감소');
  }
}
console.log(`PASS: 길 위 ${onRoad}개, 길 끝 너머 부분 가림 ${partly}개, 숨김 ${hidden}개 — 원근 일관성, 길 안, 가림선 자르기, 내 러너 표시, 원근 크기 감소`);
