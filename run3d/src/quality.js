// 화질 설정. 주소 뒤 ?quality=high|medium|low 로 고른다.
// 기본값: 폰(터치 화면)은 medium, 컴퓨터는 high.
const PRESETS = {
  // 해상도 배율, 그림자 해상도, 3D 나무 반경(m), 잔디 영역 한 변(m, 0이면 없음), 잔디 밀도(개/m²),
  // 후처리 사용, 계단 현상 제거(MSAA 샘플 수), 구석 그늘(AO)
  high: { pixelRatio: 1.5, shadowSize: 2048, treeRadius: 110, grassSize: 60, grassDensity: 30, post: true, msaa: 4, ao: true },
  medium: { pixelRatio: 1.5, shadowSize: 1024, treeRadius: 80, grassSize: 44, grassDensity: 18, post: true, msaa: 2, ao: false },
  low: { pixelRatio: 1, shadowSize: 1024, treeRadius: 50, grassSize: 0, grassDensity: 0, post: false, msaa: 0, ao: false },
};

const params = new URLSearchParams(location.search);
const touch = window.matchMedia('(pointer: coarse)').matches;
const name = PRESETS[params.get('quality')] ? params.get('quality') : touch ? 'medium' : 'high';

export const Q = { name, ...PRESETS[name] };
// 항목별로 덮어쓰기: 예) ?quality=medium&grassSize=0&ao=0 (폰에서 무엇이 무거운지 찾을 때)
for (const key of Object.keys(Q)) {
  if (key !== 'name' && params.has(key)) Q[key] = typeof Q[key] === 'boolean' ? params.get(key) === '1' : Number(params.get(key));
}
