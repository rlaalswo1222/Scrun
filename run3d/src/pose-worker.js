// 카메라 포즈 인식을 따로 도는 일꾼(Web Worker): 화면 쪽(가속도 이벤트를 받는 곳)을 막지 않게.
// 받는 것: {type:'init', base} / {type:'frame', bitmap, ts, t}   보내는 것: {type:'ready'} / {type:'pose', t, nose, shoulder, pts}
let landmarker = null;

self.onmessage = async (e) => {
  const m = e.data;
  if (m.type === 'init') {
    try {
      const { FilesetResolver, PoseLandmarker } = await import(`${m.base}vendor/mediapipe/vision_bundle.mjs`);
      const files = await FilesetResolver.forVisionTasks(`${m.base}vendor/mediapipe/wasm`);
      const opts = (delegate) => ({
        baseOptions: { modelAssetPath: `${m.base}vendor/mediapipe/pose_landmarker_lite.task`, delegate },
        runningMode: 'VIDEO', numPoses: 1,
      });
      try {
        landmarker = await PoseLandmarker.createFromOptions(files, opts('GPU'));
      } catch {
        landmarker = await PoseLandmarker.createFromOptions(files, opts('CPU'));
      }
      self.postMessage({ type: 'ready' });
    } catch (err) {
      self.postMessage({ type: 'error', message: String(err?.message || err) });
    }
    return;
  }
  if (m.type === 'frame') {
    if (!landmarker) { m.bitmap.close(); return; }
    const res = landmarker.detectForVideo(m.bitmap, m.ts);
    m.bitmap.close();
    const lm = res.landmarks?.[0];
    if (!lm) { self.postMessage({ type: 'pose', t: m.t, nose: null }); return; }
    self.postMessage({
      type: 'pose', t: m.t, nose: lm[0].y, shoulder: (lm[11].y + lm[12].y) / 2,
      pts: [[lm[0].x, lm[0].y], [lm[11].x, lm[11].y], [lm[12].x, lm[12].y]],
    });
  }
};
