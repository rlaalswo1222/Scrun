// ?debug=1 일 때 화면 위에 성능 정보를 띄운다 (폰에서 개발자 도구 없이 확인하려고).
//  - FPS: 최근 1초 평균 프레임 수, 가장 느린 프레임(ms)
//  - 그리기 호출 수, 삼각형 수: 한 프레임에 GPU에 보내는 일의 양
//  - GPU 이름, 화면 해상도, 현재 화질 설정
// 화질 버튼을 누르면 같은 위치(?skip)에서 그 화질로 다시 연다.
import { Q } from './quality.js';

export function createDebug(renderer, race) {
  const params = new URLSearchParams(location.search);
  if (!params.has('debug')) return null;

  const gl = renderer.getContext();
  const ext = gl.getExtension('WEBGL_debug_renderer_info');
  const gpu = ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : '알 수 없음';
  renderer.info.autoReset = false; // 후처리까지 포함한 한 프레임 전체를 세려고 직접 초기화

  const panel = document.createElement('div');
  panel.id = 'debug';
  document.body.appendChild(panel);

  const reopen = (overrides) => {
    const next = new URLSearchParams(location.search);
    for (const [k, v] of Object.entries(overrides)) next.set(k, v);
    next.set('skip', Math.round(race.elapsed)); // 지금 달리던 지점에서 다시 시작
    location.search = next.toString();
  };
  panel.addEventListener('click', (e) => {
    const b = e.target.closest('button');
    if (b) reopen(JSON.parse(b.dataset.set));
  });

  let frames = 0;
  let worst = 0;
  let lastFrame = performance.now();
  let lastReport = lastFrame;
  let calls = 0;
  let triangles = 0;

  return {
    beginFrame() {
      renderer.info.reset();
    },
    endFrame(now) {
      frames++;
      worst = Math.max(worst, now - lastFrame);
      lastFrame = now;
      calls = renderer.info.render.calls;
      triangles = renderer.info.render.triangles;
      if (now - lastReport < 1000) return;
      const fps = (frames * 1000) / (now - lastReport);
      const canvas = renderer.domElement;
      const button = (label, set) =>
        `<button data-set='${JSON.stringify(set)}' class="${set.quality === Q.name ? 'on' : ''}">${label}</button>`;
      panel.innerHTML = `
        <b class="${fps < 25 ? 'bad' : fps < 29 ? 'warn' : 'good'}">${fps.toFixed(1)} fps</b>
        <span>최악 ${worst.toFixed(0)}ms</span><br>
        그리기 ${calls}회 · 삼각형 ${(triangles / 1e6).toFixed(2)}M<br>
        ${canvas.width}x${canvas.height} (배율 ${renderer.getPixelRatio()}) · 화질 ${Q.name}<br>
        <small>${gpu}</small><br>
        ${button('high', { quality: 'high' })}${button('medium', { quality: 'medium' })}${button('low', { quality: 'low' })}
        ${button(params.get('cap') === '0' ? '30fps 제한' : '제한 풀기', { cap: params.get('cap') === '0' ? '30' : '0' })}`;
      frames = 0;
      worst = 0;
      lastReport = now;
    },
  };
}
