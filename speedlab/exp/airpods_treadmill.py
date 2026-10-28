"""계획 0008 러닝머신 단계: 러닝머신에서 에어팟 공중 시간·걸음 수가 속도를 따라가는지 (탐색).

실행: .venv/bin/python speedlab/exp/airpods_treadmill.py [기록 id]   (Windows: .venv\\Scripts\\python)
입력: data/runs/<id>/ (에어팟 = 서버 기록, 아이폰 = phone/ 백업이 있으면 그것, 중복 줄 제거)
출력: 콘솔 표 + docs/speed/img/0008_<id>.png

공중 판정은 airpods_flight.py와 같은 함수·같은 기준(THR_G, LP_HZ, GAP, MIN_F, MAX_F)을 그대로 씀.
구간 알림이 빠진 기록은 앱 타이머 간격(알려진 구간에서 구함)으로 시작 시각을 채움.
"""
import json
import sys
from pathlib import Path

import numpy as np
import pandas as pd

sys.path.insert(0, str(Path(__file__).resolve().parent))
import airpods_flight as af  # noqa: E402

ROOT = af.ROOT
SKIP = 10.0      # 구간 앞에서 버릴 시간(s): 속도 맞추는 중
MAX_GAP = 0.15   # 에어팟 샘플 사이가 이보다 길면 그 근처 공중은 버림(블루투스 끊김)


def load_xyz(run, name):
    """phone/ 백업(절대 시각, 중복 줄 있음)이 있으면 그걸, 없으면 서버 기록."""
    t0 = json.load(open(run / 'meta.json'))['t0']
    p = run / 'phone' / f'{name}.csv'
    if p.exists():
        d = pd.read_csv(p).drop_duplicates('t').sort_values('t')
        d['t'] -= t0
    else:
        d = pd.read_csv(run / f'{name}.csv')
    return d.t.values, d[['x', 'y', 'z']].values


def segments(meta):
    """빠진 구간은 알려진 구간 시작 시각에서 구한 간격으로 채움."""
    known = {s['index']: s['start_s'] for s in meta['segments']}
    idx = np.array(sorted(known))
    step = np.polyfit(idx, [known[i] for i in idx], 1)
    out = []
    for i, st in enumerate(meta['stages']):
        s = known.get(i, np.polyval(step, i))
        e = known.get(i + 1, np.polyval(step, i + 1)) if i + 1 < len(meta['stages']) else meta['end_s']
        out.append({'index': i, 'speed': st['speed_kmh'], 'start': s, 'end': e, 'filled': i not in known})
    return out, step[0]


def main():
    rid = sys.argv[1] if len(sys.argv) > 1 else sorted(p.name for p in (ROOT / 'data/runs').iterdir())[-1]
    run = ROOT / 'data/runs' / rid
    meta = json.load(open(run / 'meta.json'))
    segs, step = segments(meta)
    print(f'기록 {rid}  구간 간격 {step:.2f}s, 채운 구간 {[s["index"] for s in segs if s["filled"]]}')

    ta, aa = load_xyz(run, 'airpods_acc')
    tg, ag = load_xyz(run, 'airpods_grav')
    assert np.allclose(ta, tg)
    tp, ap = load_xyz(run, 'iphone_acc')
    print(f'아이폰 {(len(tp) - 1) / np.ptp(tp):.1f} Hz, 에어팟 {(len(ta) - 1) / np.ptp(ta):.1f} Hz')

    # 에어팟 끊김: 샘플 간격이 MAX_GAP보다 긴 곳. 필터가 끊긴 곳을 넘어 번지지 않게 이어진 조각별로 거름
    gaps = np.where(np.diff(ta) > MAX_GAP)[0]
    bounds = np.r_[0, gaps + 1, len(ta)]
    ma = np.full(len(ta), np.nan)
    for a, b in zip(bounds[:-1], bounds[1:]):
        if b - a > 30:
            ma[a:b] = af.lowpass(ta[a:b], np.linalg.norm(aa[a:b] + ag[a:b], axis=1) / af.G)
    ok = ~np.isnan(ma)
    fl = af.flights(ta[ok], ma[ok], af.THR_G)
    bad = np.r_[ta[gaps], ta[gaps + 1]]
    fl = np.array([f for f in fl if not np.any((bad > f[0] - MAX_GAP) & (bad < f[1] + MAX_GAP))]).reshape(-1, 2)
    print(f'에어팟 끊김 {len(gaps)}곳, 공중 {len(fl)}개 (끊김 근처 제외)\n')

    rows = []
    print(f'  {"#":>2} {"속도":>5} {"공중 수":>6} {"공중 평균":>8} {"SD":>5} {"공중/분":>7} {"데이터":>6}')
    for s in segs:
        lo, hi = s['start'] + SKIP, s['end']
        f = fl[(fl[:, 0] >= lo) & (fl[:, 1] < hi)]
        d = (f[:, 1] - f[:, 0]) * 1000
        cover = np.sum(ok & (ta >= lo) & (ta < hi)) / max(1, np.sum((ta >= lo) & (ta < hi))) if np.any((ta >= lo) & (ta < hi)) else 0
        live = np.sum(np.diff(ta[(ta >= lo) & (ta < hi)]).clip(max=MAX_GAP)) / (hi - lo)  # 끊기지 않은 시간 비율
        rows.append({'i': s['index'], 'v': s['speed'], 'n': len(d), 'ms': d.mean() if len(d) else np.nan,
                     'sd': d.std() if len(d) else np.nan, 'per_min': len(d) / (hi - lo) / max(live, 1e-9) * 60,
                     'live': live})
        r = rows[-1]
        print(f'  {r["i"]:>2} {r["v"]:>5.0f} {r["n"]:>6} {r["ms"]:>6.0f}ms {r["sd"]:>4.0f} {r["per_min"]:>7.0f} {live:>5.0%}'
              + (' (구간 시각 채움)' if s['filled'] else ''))

    df = pd.DataFrame(rows)
    use = df[(df.live >= 0.8) & (df.n >= 10)]
    print(f'\n데이터 80% 이상·공중 10개 이상인 구간 {len(use)}/{len(df)}개로 속도별 정리')
    g = use.groupby('v').agg(구간=('ms', 'size'), 공중_ms=('ms', 'mean'), 구간간_SD=('ms', 'std'), 공중_분=('per_min', 'mean'))
    print(g.round(1).to_string())
    if use.v.nunique() >= 2:
        k, b = np.polyfit(use.v, use.ms, 1)
        res = use.ms - (k * use.v + b)
        print(f'\n공중 시간 = {k:+.1f} ms/(km/h) × 속도 + {b:.0f}, 잔차 SD {res.std():.1f} ms')
        print(f'→ 잔차를 속도로 바꾸면 SD {res.std() / abs(k):.2f} km/h (같은 기록 안 맞춤, 검증 아님)')
        kc, bc = np.polyfit(use.v, use.per_min, 1)
        print(f'공중/분 = {kc:+.1f}/(km/h) × 속도 + {bc:.0f}')
    describe(segs, ta, np.linalg.norm(aa + ag, axis=1) / af.G)
    plot(rid, use, df)


def describe(segs, ta, m):
    """기준값 없이: 구간 뒤 20초 중 끊기지 않은 가장 긴 조각에서 걸음 주기(자기상관)와 걸음마다 흔들린 폭."""
    from scipy.signal import find_peaks
    print('\n기준값 없는 지표 (끊기지 않은 가장 긴 조각, 필터 없음)')
    print(f'  {"#":>2} {"속도":>5} {"조각":>5} {"걸음/분":>7} {"흔들림 폭":>8} {"최저 g":>6}')
    for s in segs:
        w = np.where((ta >= s['start'] + SKIP) & (ta < s['end']))[0]
        if len(w) < 2:
            continue
        cut = np.where(np.diff(ta[w]) > MAX_GAP)[0]
        parts = np.split(w, cut + 1)
        w = max(parts, key=lambda q: ta[q[-1]] - ta[q[0]])
        dur = ta[w[-1]] - ta[w[0]]
        if dur < 5:
            print(f'  {s["index"]:>2} {s["speed"]:>5.0f} {dur:>4.0f}s  (짧아서 뺌)')
            continue
        fs = 50.0
        tu = np.arange(ta[w[0]], ta[w[-1]], 1 / fs)
        x = np.interp(tu, ta[w], m[w])
        x0 = x - x.mean()
        ac = np.correlate(x0, x0, 'full')[len(x0) - 1:]
        lo, hi = int(0.25 * fs), int(0.6 * fs)  # 걸음 주기 100에서 240 spm
        lag = lo + int(np.argmax(ac[lo:hi]))
        pk, _ = find_peaks(x, distance=int(0.6 * lag))
        tr, _ = find_peaks(-x, distance=int(0.6 * lag))
        amp = np.median(x[pk]) - np.median(x[tr]) if len(pk) and len(tr) else np.nan
        print(f'  {s["index"]:>2} {s["speed"]:>5.0f} {dur:>4.0f}s {60 * fs / lag:>7.0f} {amp:>7.2f}g {np.median(x[tr]):>6.2f}')


def plot(rid, use, df):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    from matplotlib import font_manager
    have = {f.name for f in font_manager.fontManager.ttflist}
    plt.rcParams['font.family'] = next((f for f in ('AppleGothic', 'Malgun Gothic') if f in have), 'sans-serif')
    fig, axs = plt.subplots(1, 2, figsize=(11, 4))
    drop = df[~df.i.isin(use.i)]
    for ax, col, lab in ((axs[0], 'ms', '공중 시간 (ms)'), (axs[1], 'per_min', '공중 / 분')):
        ax.scatter(use.v, use[col], color='#d1495b', label='쓴 구간')
        ax.scatter(drop.v, drop[col], facecolors='none', edgecolors='gray', label='뺀 구간(끊김)')
        for _, r in use.iterrows():
            ax.annotate(str(r.i), (r.v, r[col]), textcoords='offset points', xytext=(4, 2), fontsize=8)
        ax.set_xlabel('러닝머신 속도 (km/h)')
        ax.set_ylabel(lab)
    axs[0].legend()
    fig.suptitle(f'{rid} 에어팟 (구간 뒤 20초, 번호 = 구간 순서)')
    fig.tight_layout()
    out = ROOT / f'docs/speed/img/0008_{rid}.png'
    fig.savefig(out, dpi=120)
    print('그림:', out.relative_to(ROOT))


if __name__ == '__main__':
    main()
