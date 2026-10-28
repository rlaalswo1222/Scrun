// 픽셀 단위 가림용 깊이 지도: tools/sfm_depth_export.py가 장면(1초에 4장)마다 미터로 맞춘 거리 지도를
// 160×90, 로그 눈금 8비트로 줄여 15초(60장)씩 한 PNG(6×10칸)에 담아 둔다. 재생 위치 근처 PNG만 받아 텍스처로 쓴다.
import * as THREE from 'three';

export class VideoDepth {
  static async load(videoId) {
    const res = await fetch(`assets/video/${videoId}.depth/index.json`);
    if (!res.ok) return null;
    return new VideoDepth(videoId, await res.json());
  }

  constructor(videoId, meta) {
    this.base = `assets/video/${videoId}.depth/`;
    this.meta = meta;
    this.cache = new Map();
    this.loading = new Set();
    this.loader = new THREE.TextureLoader();
  }

  ensure(file) {
    const m = this.meta;
    if (file < 0 || file * m.perFile >= m.frames || this.cache.has(file) || this.loading.has(file)) return;
    this.loading.add(file);
    this.loader.load(`${this.base}${String(file).padStart(4, '0')}.png`, (tex) => {
      tex.colorSpace = THREE.NoColorSpace;
      tex.flipY = false; // 이미지 맨 윗줄 = v 0 (칸 위치 계산과 같게)
      tex.minFilter = THREE.LinearFilter;
      tex.magFilter = THREE.LinearFilter;
      tex.generateMipmaps = false;
      tex.needsUpdate = true;
      this.cache.set(file, tex);
      this.loading.delete(file);
      for (const [k, old] of this.cache) {
        if (Math.abs(k - file) > 2) { old.dispose(); this.cache.delete(k); }
      }
    }, undefined, () => this.loading.delete(file));
  }

  // 영상 시각 t의 깊이 지도: 앞뒤 두 장면(0.25초 간격) { texture, tile, texture2, tile2 } (아직 안 받았으면 null).
  // 영상은 60프레임인데 깊이 지도는 1초에 4장이라, 둘 중 가까운 쪽(더 많이 가리는 쪽)을 쓰면 장면 사이에도 덜 삐져나온다
  at(t) {
    const m = this.meta;
    const f = (t - m.t0) * m.fps;
    const k0 = Math.round(f); // 가장 가까운 장면
    if (k0 < 0 || k0 >= m.frames) return null;
    const a = this.tile(k0);
    if (!a) return null;
    const b = this.tile(Math.min(k0 + 1, m.frames - 1)) ?? a;
    return { texture: a.texture, tile: a.tile, texture2: b.texture, tile2: b.tile };
  }

  tile(k) {
    const m = this.meta;
    const file = Math.floor(k / m.perFile);
    this.ensure(file);
    this.ensure(file + 1); // 다음 15초도 미리
    const texture = this.cache.get(file);
    if (!texture) return null;
    const j = k % m.perFile;
    const r = Math.floor(j / m.cols);
    const c = j % m.cols;
    const W = m.cols * m.tileW;
    const H = m.rows * m.tileH;
    return { texture, tile: [(c * m.tileW) / W, (r * m.tileH) / H, m.tileW / W, m.tileH / H] };
  }
}
