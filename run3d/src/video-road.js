const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
export function validRows(rows) {
  if (!Array.isArray(rows) || rows.length < 2) return false;
  return rows.every((r, i) => Array.isArray(r) && r.length === 3 && r.every(Number.isFinite)
    && r[0] > 0 && r[0] <= 1 && r[1] >= 0 && r[2] <= 1 && r[2] - r[1] >= .015
    && (!i || r[0] > rows[i - 1][0]));
}
export function sampleRows(rows, y) {
  if (!validRows(rows) || y < rows[0][0] || y > 1) return null;
  for (let i = 1; i < rows.length; i++) {
    if (y <= rows[i][0]) {
      const a = rows[i-1], b = rows[i], t = (y-a[0])/(b[0]-a[0]);
      return [a[1]+(b[1]-a[1])*t, a[2]+(b[2]-a[2])*t];
    }
  }
  return rows.at(-1).slice(1);
}
export class VideoRoad {
  constructor(keyframes = []) {
    this.keys = keyframes.filter((k) => Number.isFinite(k.time) && k.time >= 0 && validRows(k.rows))
      .map((k) => ({ time: k.time, rows: k.rows.map((r) => [...r]) })).sort((a,b) => a.time-b.time);
  }
  bracket(time) {
    let lo = 0, hi = this.keys.length;
    while (lo < hi) { const mid = (lo+hi) >> 1; if (this.keys[mid].time <= time) lo = mid+1; else hi = mid; }
    const a = this.keys[Math.max(0, lo-1)], b = this.keys[Math.min(this.keys.length-1, lo)];
    return [a, b, a && b && a !== b ? clamp((time-a.time)/(b.time-a.time), 0, 1) : 0];
  }
  bounds(time, y) {
    const [a,b,t] = this.bracket(time);
    if (!a) return null;
    const left = sampleRows(a.rows,y), right = sampleRows(b.rows,y);
    if (!left || !right) return null;
    return left.map((v,i) => v+(right[i]-v)*t);
  }
  projectionRowsAt(time) {
    const [a,b,t] = this.bracket(time);
    if (!a) return null;
    if (a === b || t === 0) return a.rows;
    // Interpolate corresponding points along the visible path, including its
    // endpoint. Different top rows must not produce a sudden vertical jump.
    const fractions = [a,b].flatMap((k) => k.rows.map((r) => (r[0]-k.rows[0][0])/(k.rows.at(-1)[0]-k.rows[0][0])))
      .sort((x,y) => x-y).filter((v,i,all) => !i || v-all[i-1] > 1e-8);
    return fractions.map((p) => {
      const aY = a.rows[0][0]+p*(a.rows.at(-1)[0]-a.rows[0][0]);
      const bY = b.rows[0][0]+p*(b.rows.at(-1)[0]-b.rows[0][0]);
      const left = [aY, ...sampleRows(a.rows,aY)], right = [bY, ...sampleRows(b.rows,bY)];
      return left.map((v,i) => v+(right[i]-v)*t);
    });
  }
  rowsAt(time) {
    const [a,b,t] = this.bracket(time);
    if (!a) return [[.62,.44,.56],[.70,.36,.64],[.82,.25,.75],[.96,.1,.9]];
    const minY = Math.max(a.rows[0][0], b.rows[0][0]);
    const ys = [...new Set([...a.rows,...b.rows].map((r) => r[0]))].filter((y) => y >= minY).sort((x,y) => x-y);
    return ys.map((y) => [y, ...this.bounds(time,y)]);
  }
  set(time, rows) {
    if (!Number.isFinite(time) || time < 0 || !validRows(rows)) return false;
    this.keys = this.keys.filter((k) => Math.abs(k.time-time) >= .1);
    this.keys.push({ time, rows: rows.map((r) => [...r]) });
    this.keys.sort((a,b) => a.time-b.time);
    return true;
  }
}
