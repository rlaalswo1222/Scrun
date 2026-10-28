// 화면 위 계기판: 페이스, 속도, 거리, 시간, 순위, 미니맵.
// 폰이 눈에서 50~70cm 떨어져 흔들리므로 글씨를 크게 쓰고, 초당 5번만 갱신한다.

export function fmtTime(sec) {
  const s = Math.max(0, Math.floor(sec));
  const h = Math.floor(s / 3600);
  const mm = String(Math.floor((s % 3600) / 60)).padStart(2, '0');
  const ss = String(s % 60).padStart(2, '0');
  return h ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
}

export function fmtPace(kmh) {
  if (kmh < 1) return `--'--"`;
  const secPerKm = 3600 / kmh;
  return `${Math.floor(secPerKm / 60)}'${String(Math.round(secPerKm % 60)).padStart(2, '0')}"`;
}

const $ = (id) => document.getElementById(id);

export class Hud {
  constructor(race, path, course) {
    this.race = race;
    this.path = path;
    this.last = 0;
    $('course-name').textContent = course.name;
    $('total').textContent = (race.length / 1000).toFixed(2);
    this.map = $('minimap');
    this.ctx = this.map.getContext('2d');
    this.setupMinimap();
  }

  setupMinimap() {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const size = this.map.clientWidth;
    this.map.width = this.map.height = size * dpr;
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    let minx = Infinity, minz = Infinity, maxx = -Infinity, maxz = -Infinity;
    for (const [x, z] of this.path.pts) {
      minx = Math.min(minx, x); maxx = Math.max(maxx, x);
      minz = Math.min(minz, z); maxz = Math.max(maxz, z);
    }
    const pad = 14;
    const k = (size - pad * 2) / Math.max(maxx - minx, maxz - minz);
    const ox = (size - (maxx - minx) * k) / 2;
    const oz = (size - (maxz - minz) * k) / 2;
    this.toMap = ([x, z]) => [ox + (x - minx) * k, oz + (z - minz) * k];
    this.mapSize = size;
  }

  drawMinimap() {
    const { ctx } = this;
    ctx.clearRect(0, 0, this.mapSize, this.mapSize);
    ctx.lineWidth = 4;
    ctx.lineCap = ctx.lineJoin = 'round';
    ctx.strokeStyle = '#e8704a';
    ctx.beginPath();
    this.path.pts.forEach((p, i) => {
      const [x, y] = this.toMap(p);
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    });
    ctx.stroke();
    // 나는 마지막에 크게 그려서 항상 위에 보이게
    const order = [...this.race.runners].sort((a, b) => a.isMe - b.isMe);
    for (const r of order) {
      const [x, y] = this.toMap(this.path.point(r.distance));
      ctx.beginPath();
      ctx.arc(x, y, r.isMe ? 7 : 5, 0, Math.PI * 2);
      ctx.fillStyle = r.color;
      ctx.fill();
      ctx.lineWidth = r.isMe ? 3 : 1.5;
      ctx.strokeStyle = '#fff';
      ctx.stroke();
    }
  }

  update(now, force = false) {
    if (!force && now - this.last < 200) return;
    this.last = now;
    const { race } = this;
    const me = race.me;
    $('pace').textContent = fmtPace(me.speedKmh);
    $('speed').textContent = me.source.speedKmh.toFixed(1);
    $('cad').textContent = Math.round(me.cadence);
    $('dist').textContent = (me.distance / 1000).toFixed(2);
    $('time').textContent = fmtTime(me.finishTime ?? race.elapsed);
    $('progress').style.width = `${(me.distance / race.length) * 100}%`;

    $('board').innerHTML = race.standings().map((r, i) => {
      let gap;
      if (r.isMe) gap = '';
      else if (r.finishTime !== null) gap = fmtTime(r.finishTime);
      else {
        const m = Math.round(r.distance - me.distance);
        gap = m >= 0 ? `+${m}m` : `${m}m`;
      }
      return `<li class="${r.isMe ? 'me' : ''}"><span class="rank">${i + 1}</span>`
        + `<span class="dot" style="background:${r.color}"></span>`
        + `<span class="name">${r.name}</span><span class="gap">${gap}</span></li>`;
    }).join('');

    this.drawMinimap();
  }
}
