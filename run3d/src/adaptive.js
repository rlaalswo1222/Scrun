// 자동 해상도 조절: 폰이 뜨거워져 느려지면(스로틀링) 해상도를 조금씩 낮추고, 여유가 생기면 다시 올린다.
// 2초마다 평균 fps를 보고 판단한다. ?adaptive=0 이면 끈다 (성능을 정확히 잴 때).
const CHECK_S = 2;
const LOW_FPS = 26; // 이보다 느리면 낮춤
const STEP_DOWN = 0.1;
const STEP_UP = 0.05;
const CALM_CHECKS = 5; // 10초 연속 여유가 있으면 올림

export function createAdaptive(renderer, { min, max, onChange }) {
  if (new URLSearchParams(location.search).get('adaptive') === '0') return () => {};
  let time = 0;
  let frames = 0;
  let calm = 0;
  return (elapsed) => {
    time += elapsed;
    frames++;
    if (time < CHECK_S) return;
    const fps = frames / time;
    time = 0;
    frames = 0;
    const pr = renderer.getPixelRatio();
    if (fps < LOW_FPS && pr > min) {
      renderer.setPixelRatio(Math.max(min, +(pr - STEP_DOWN).toFixed(2)));
      calm = 0;
      onChange();
    } else if (fps > 29 && pr < max) {
      if (++calm >= CALM_CHECKS) {
        renderer.setPixelRatio(Math.min(max, +(pr + STEP_UP).toFixed(2)));
        calm = 0;
        onChange();
      }
    } else {
      calm = 0;
    }
  };
}
