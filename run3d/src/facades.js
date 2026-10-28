// 건물 외관: 종류마다 다른 벽 그림을 층 단위로 반복해서 붙인다.
//  apt    아파트: 넓은 창 + 베란다 난간 띠
//  office 오피스: 유리 외벽(하늘이 비침) + 얇은 창틀
//  shop   저층 상가: 1층은 가게 유리창과 간판 띠, 위층은 작은 창
//  house  주택: 벽돌·미장 벽에 작은 창
// 벽 그림은 코드로 그리고(외부 이미지 없음), 건물마다 벽 색은 정해진 후보 중에서 번갈아 쓴다.
import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { v2 } from './geom.js';

const SHOP_H = 4.2; // 가게 층 높이 (m)
const ROOF = new THREE.Color(0x8d8a84);

// 한 칸 그림: [x, y, 폭, 높이, 색, 거칠기] (0~1, y는 위에서부터). 색이 배열이면 위->아래 그라데이션.
// 왼쪽 위 모서리는 지붕 색을 뽑는 곳이라 항상 밝은 색으로 둔다.
const STYLES = {
  apt: {
    w: 3.0, h: 2.9, walls: [0xf4f1ea, 0xefe9df, 0xe9edf0, 0xf2ece4, 0xe6e2da],
    rects: [
      [0.12, 0.16, 0.76, 0.58, ['#61778a', '#26313b'], 0.3], // 창
      [0.12, 0.56, 0.76, 0.18, '#dcdcd8', 0.7], // 베란다 난간
      [0.12, 0.555, 0.76, 0.015, '#b8b8b2', 0.6],
      [0, 0.9, 1, 0.1, '#e2dfd8', 0.9], // 층 사이 슬래브
    ],
  },
  office: {
    w: 3.0, h: 3.6, walls: [0xc4d3de, 0xb9cccd, 0xcfd4d9, 0xb0c4d4],
    rects: [
      // 유리: 하늘 사진의 아래쪽 절반이 검어서 너무 매끈하면 시커멓게 비친다 -> 거칠기 0.35로 하늘빛을 섞음
      [0, 0, 1, 1, ['#9db4c6', '#5d7488'], 0.35],
      [0, 0.8, 1, 0.2, '#6b7883', 0.4], // 층 사이 불투명 패널
      [0, 0, 0.03, 1, '#d4dade', 0.5], // 세로 창틀
      [0.5, 0, 0.02, 0.8, '#c4ccd2', 0.5],
      [0, 0, 1, 0.03, '#d4dade', 0.5], // 가로 창틀
    ],
  },
  shop: {
    w: 3.5, h: 3.2, walls: [0xe8dcc8, 0xc98f6e, 0xd9d4cc, 0xb8a58e, 0xe6e1d6, 0xa86f5a],
    rects: [
      [0.2, 0.2, 0.6, 0.48, ['#5a6b7d', '#2b333d'], 0.3],
      [0.18, 0.68, 0.64, 0.035, '#d6d6d6', 0.7], // 창턱
      [0.62, 0.74, 0.16, 0.11, '#cfcfcf', 0.6], // 에어컨 실외기
    ],
  },
  house: {
    w: 3.0, h: 3.0, walls: [0xb5735a, 0xd8c7ad, 0xc9b8a0, 0x9e6a55, 0xe2d8c6],
    rects: [
      [0.3, 0.26, 0.4, 0.38, ['#5d6c7a', '#2e353d'], 0.3],
      [0.28, 0.64, 0.44, 0.03, '#dddddd', 0.7],
    ],
  },
  // 가게 1층: 간판 띠(건물마다 다른 색) + 통유리 + 기둥
  front: {
    w: 4.0, h: SHOP_H, walls: [0xe03131, 0x1971c2, 0x2f9e44, 0xf08c00, 0x7048e8, 0xc2255c, 0x0c8599, 0xfab005],
    rects: [
      [0, 0.24, 1, 0.03, '#555555', 0.8],
      [0.04, 0.27, 0.92, 0.73, ['#4d555e', '#22282d'], 0.3],
      [0, 0.24, 0.04, 0.76, '#c8c8c8', 0.6],
      [0.96, 0.24, 0.04, 0.76, '#c8c8c8', 0.6],
      [0.495, 0.27, 0.012, 0.73, '#a0a0a0', 0.5],
    ],
  },
};

function paintTextures(style, maxAniso) {
  const S = 256;
  const make = (paint) => {
    const c = document.createElement('canvas');
    c.width = c.height = S;
    const g = c.getContext('2d');
    paint(g);
    const t = new THREE.CanvasTexture(c);
    t.wrapS = t.wrapT = THREE.RepeatWrapping;
    // 벽 uv는 (가로 m, 1 - 높이 m). 한 칸 = w x h m, 바닥에서부터 층이 맞도록 offset, 캔버스 위쪽 = 층의 위쪽
    t.flipY = false;
    t.repeat.set(1 / style.w, 1 / style.h);
    t.offset.set(0, -1 / style.h);
    t.anisotropy = maxAniso;
    return t;
  };
  const map = make((g) => {
    g.fillStyle = '#ffffff';
    g.fillRect(0, 0, S, S);
    for (const [x, y, w, h, color] of style.rects) {
      if (Array.isArray(color)) {
        const grad = g.createLinearGradient(0, y * S, 0, (y + h) * S);
        grad.addColorStop(0, color[0]);
        grad.addColorStop(1, color[1]);
        g.fillStyle = grad;
      } else {
        g.fillStyle = color;
      }
      g.fillRect(x * S, y * S, w * S, h * S);
    }
  });
  map.colorSpace = THREE.SRGBColorSpace;
  const roughnessMap = make((g) => {
    g.fillStyle = '#e6e6e6';
    g.fillRect(0, 0, S, S);
    for (const [x, y, w, h, , rough] of style.rects) {
      const v = Math.round(rough * 255);
      g.fillStyle = `rgb(${v},${v},${v})`;
      g.fillRect(x * S, y * S, w * S, h * S);
    }
  });
  return { map, roughnessMap };
}

function extrude(shape, z0, z1) {
  const g = new THREE.ExtrudeGeometry(shape, { depth: z1 - z0, bevelEnabled: false });
  g.translate(0, 0, z0);
  return g;
}

// 벽 색·지붕 색을 정점 색으로 칠하고, 지붕은 그림의 왼쪽 위(밝은 곳)만 쓰게 uv를 고정
function paint(g, style, wall) {
  const normal = g.attributes.normal;
  const pos = g.attributes.position;
  const uv = g.attributes.uv;
  const roofU = 0.02 * style.w;
  const roofV = 1 + 0.02 * style.h;
  const colors = new Float32Array(normal.count * 3);
  for (let k = 0; k < normal.count; k++) {
    const isCap = Math.abs(normal.getZ(k)) > 0.5;
    if (isCap) uv.setXY(k, roofU, roofV);
    const c = isCap ? ROOF : wall;
    // 벽 아래쪽을 살짝 어둡게 해서 땅에 붙은 느낌
    const shade = isCap ? 1 : 0.8 + 0.2 * Math.min(1, pos.getZ(k) / 12);
    colors.set([c.r * shade, c.g * shade, c.b * shade], k * 3);
  }
  g.setAttribute('color', new THREE.BufferAttribute(colors, 3));
  return g;
}

export function buildBuildings(scene, buildings, maxAniso) {
  const groups = Object.fromEntries(Object.keys(STYLES).map((k) => [k, []]));
  const wall = new THREE.Color();
  buildings.forEach((b, i) => {
    // 예전 코스 파일에는 종류(t)가 없음: 높이로 추정
    const type = STYLES[b.t] ? b.t : b.h >= 30 ? 'apt' : 'shop';
    const shape = new THREE.Shape(b.p.map(v2));
    if (type === 'shop') {
      const front = STYLES.front;
      wall.setHex(front.walls[i % front.walls.length]).lerp(new THREE.Color(0xffffff), 0.15);
      groups.front.push(paint(extrude(shape, 0, Math.min(SHOP_H, b.h)), front, wall));
      if (b.h > SHOP_H + 1) {
        const upper = STYLES.shop;
        wall.setHex(upper.walls[i % upper.walls.length]);
        groups.shop.push(paint(extrude(shape, SHOP_H, b.h), upper, wall));
      }
    } else {
      const style = STYLES[type];
      wall.setHex(style.walls[i % style.walls.length]);
      groups[type].push(paint(extrude(shape, 0, b.h), style, wall));
    }
  });
  for (const [type, geoms] of Object.entries(groups)) {
    if (!geoms.length) continue;
    const merged = mergeGeometries(geoms);
    merged.rotateX(-Math.PI / 2); // 압출 방향 -> 위쪽
    const material = new THREE.MeshStandardMaterial({
      vertexColors: true, roughness: 1, metalness: 0, ...paintTextures(STYLES[type], maxAniso),
    });
    const mesh = new THREE.Mesh(merged, material);
    mesh.castShadow = mesh.receiveShadow = true;
    scene.add(mesh);
  }
}
