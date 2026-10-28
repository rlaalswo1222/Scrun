// YouTube가 지원하는 배속 중 선택. 영상이 뒤처지면 배속을 올려 따라잡는다.
// target을 주면(코스 지도 모드: 내 거리에 해당하는 실제 영상 시각) 그 값을, 없으면 일정한 촬영 속도로 계산
export function playbackPlan({ distance, currentTime, speedKmh, filmedKmh, startTime = 0, rates = [1], continuous = false, target: given }) {
  const target = given ?? startTime + distance / (filmedKmh / 3.6);
  const error = target - currentTime;
  const available = rates.filter((v) => Number.isFinite(v) && v > 0);
  if (!available.length) available.push(1);
  const want = speedKmh / filmedKmh + error * 0.08;
  const rate = continuous ? Math.round(Math.max(.25, Math.min(4, want))*1000)/1000
    : available.reduce((a, b) => Math.abs(b - want) < Math.abs(a - want) ? b : a);
  return { target, error, rate, seek: Math.abs(error) > 8, stopped: speedKmh < 0.5 };
}

export function videoCourseLength(duration, startTime, filmedKmh, requestedM) {
  if (!(duration > 0)) return requestedM;
  return Math.max(0, Math.min(requestedM, (duration - startTime) * filmedKmh / 3.6));
}
