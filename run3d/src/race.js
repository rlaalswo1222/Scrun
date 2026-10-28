// 레이스 상태. 화면은 이 상태만 보고 그린다.
// 각 러너의 속도·걸음 수는 source가 준다. 지금은 키보드(ManualSource)와 봇(BotSource)이고,
// 나중에 센서 추정값이나 서버에서 받은 값으로 source만 갈아 끼우면 된다.

// 속도로 걸음 수 추정 (실제 앱에서는 센서가 직접 걸음 수를 준다)
export function cadenceFromSpeed(kmh) {
  if (kmh < 0.5) return 0;
  const step = kmh < 7 ? 0.45 + 0.05 * kmh : 0.3 + 0.065 * kmh; // 걷기 / 달리기 보폭(m)
  return (kmh / 3.6 / step) * 60;
}

export const gaitOf = (kmh) => (kmh < 0.5 ? 'idle' : kmh < 7 ? 'walk' : 'run');

export class ManualSource {
  constructor(speedKmh = 9) {
    this.speedKmh = speedKmh;
  }
  change(delta) {
    this.speedKmh = Math.min(20, Math.max(0, Math.round((this.speedKmh + delta) * 10) / 10));
  }
  sample() {
    return { speedKmh: this.speedKmh, cadence: cadenceFromSpeed(this.speedKmh) };
  }
}

export class BotSource {
  constructor(baseKmh, seed) {
    this.base = baseKmh;
    this.seed = seed;
  }
  sample(t) {
    const v = this.base + 0.4 * Math.sin(t / 23 + this.seed) + 0.2 * Math.sin(t / 7 + this.seed * 3);
    return { speedKmh: v, cadence: cadenceFromSpeed(v) };
  }
}

export class Race {
  constructor(lengthM, runners) {
    this.length = lengthM;
    this.runners = runners.map((r) => ({ ...r, distance: 0, speedKmh: 0, cadence: 0, finishTime: null }));
    this.me = this.runners.find((r) => r.isMe);
    this.state = 'ready'; // ready -> countdown -> running -> finished
    this.elapsed = 0;
    this.countdown = 0;
  }

  start() {
    if (this.state !== 'ready') return;
    this.state = 'countdown';
    this.countdown = 3;
  }

  update(dt) {
    if (this.state === 'countdown') {
      this.countdown -= dt;
      if (this.countdown <= 0) this.state = 'running';
      return;
    }
    // 내가 완주한 뒤에도 다른 러너들은 계속 달린다
    if (this.state !== 'running' && this.state !== 'finished') return;
    this.elapsed += dt;
    for (const r of this.runners) {
      if (r.finishTime !== null) {
        r.speedKmh = 0;
        r.cadence = 0;
        continue;
      }
      const s = r.source.sample(this.elapsed);
      r.speedKmh = s.speedKmh;
      r.cadence = s.cadence;
      const next = r.distance + (s.speedKmh / 3.6) * dt;
      if (next >= this.length) {
        // 결승선을 넘은 정확한 순간을 보간
        const over = (next - this.length) / ((s.speedKmh / 3.6) || 1);
        r.finishTime = this.elapsed - over;
        r.distance = this.length;
        r.speedKmh = 0;
        r.cadence = 0;
      } else {
        r.distance = next;
      }
    }
    if (this.me.finishTime !== null) this.state = 'finished';
  }

  // 완주자는 기록 순, 나머지는 거리 순
  standings() {
    return [...this.runners].sort((a, b) => {
      if (a.finishTime !== null && b.finishTime !== null) return a.finishTime - b.finishTime;
      if (a.finishTime !== null) return -1;
      if (b.finishTime !== null) return 1;
      return b.distance - a.distance;
    });
  }
}
