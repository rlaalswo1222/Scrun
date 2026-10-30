"""계획 0011: 귀 센서 논문(Stuchbury-Wass 외, J Appl Biomech 2026) 방식으로 에어팟 공중 시간 — 착지·발 떼기 순간을 찾음.

실행: .venv/bin/python speedlab/exp/airpods_events.py   (Windows: .venv\\Scripts\\python)
입력: data/runs/20261029-025507 (B, 14구간), data/runs/20261029-040156 (M, 보정 + 자유 달리기). 둘 다 phone/ 백업
출력: 콘솔 표 + docs/speed/img/0011_events.png

방법(결과 보기 전에 고정, 계획 0011):
- 수직 = 전체 가속도(에어팟 가속도 + 중력)를 중력 방향에 투영, g 단위(서 있으면 1, 공중이면 0 근처)
- 걸음 = 수직의 큰 봉우리(디딤 가운데), 착지 = 봉우리 앞 0.15초 안 2차 미분 최댓값,
  발 떼기 = 10 Hz 저역 필터 → 저크 → 그 미분의 최솟값(봉우리 뒤 0.25초 안), 극값은 포물선 보간
- 공중 = 다음 착지 − 발 떼기(5 ms에서 0.5초만), 구간마다 중앙값
→ 결과: 발 떼기가 디딤 봉우리 바로 뒤에 잡혀 실제보다 약 0.2초 이름(그림 0011_trace.png). 이 방식은 실패

추가 탐색(그림을 보고 정함, 사전 고정 아님): 계획 0008의 0.3 g 기준을 전체 크기 대신 **수직**에 적용,
걸음당 "수직 0.3 g 아래 시간"(1 ms로 선형 보간해 셈, 걸음 수는 계기판 아이폰)
"""
import json
import sys
from pathlib import Path

import numpy as np
import pandas as pd
from scipy.signal import butter, filtfilt, find_peaks

sys.path.insert(0, str(Path(__file__).resolve().parent))
import soundtrackdb as sdb  # noqa: E402
from live_eval import true_speed  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
G = 9.80665
FS = 50.0          # 조각마다 고르게 다시 뽑는 샘플링(논문과 같음)
MAX_GAP = 0.15     # 에어팟 샘플 사이가 이보다 길면 끊김
HS_WIN = 0.15      # 착지는 디딤 봉우리 앞 이 시간 안
TO_WIN = 0.25      # 발 떼기는 디딤 봉우리 뒤 이 시간 안
LP_HZ = 10.0
FLIGHT_OK = (0.005, 0.5)
LOW_G = 0.3        # 추가 탐색: 계획 0008 기준 그대로


def load(d, name, t0):
    x = pd.read_csv(d / f'{name}.csv').drop_duplicates('t').sort_values('t')
    return x.t.values - t0, x[['x', 'y', 'z']].values


def parabolic(y, i):
    """극값 자리를 이웃 두 샘플로 샘플 사이까지."""
    if i <= 0 or i >= len(y) - 1:
        return float(i)
    a, b, c = y[i - 1], y[i], y[i + 1]
    den = a - 2 * b + c
    return i + (0.5 * (a - c) / den if den != 0 else 0.0)


def chunks(ta, vert):
    """끊김으로 나눈 조각마다 50 Hz로 다시 뽑기 → [(시각, 수직)]"""
    cut = np.where(np.diff(ta) > MAX_GAP)[0]
    out = []
    for a, b in zip(np.r_[0, cut + 1], np.r_[cut + 1, len(ta)]):
        if ta[b - 1] - ta[a] < 3:
            continue
        tu = np.arange(ta[a], ta[b - 1], 1 / FS)
        out.append((tu, np.interp(tu, ta[a:b], vert[a:b])))
    return out


def events(tu, v):
    """한 조각에서 걸음마다 (착지, 발 떼기, 디딤 봉우리) 시각."""
    x0 = v - v.mean()
    ac = np.correlate(x0, x0, 'full')[len(x0) - 1:]
    lo, hi = int(0.25 * FS), int(0.6 * FS)
    if len(ac) <= hi:
        return np.empty((0, 3))
    lag = lo + int(np.argmax(ac[lo:hi]))
    pk, _ = find_peaks(v, distance=max(1, int(0.6 * lag)))
    d2 = np.gradient(np.gradient(v))
    b, a = butter(4, LP_HZ / (FS / 2))
    dj = np.gradient(np.gradient(filtfilt(b, a, v)))
    nh, nt = int(round(HS_WIN * FS)), int(round(TO_WIN * FS))
    ev = []
    for p in pk:
        if p - nh < 1 or p + nt >= len(v) - 1:
            continue
        i = p - nh + int(np.argmax(d2[p - nh:p + 1]))
        j = p + 1 + int(np.argmin(dj[p + 1:p + nt + 1]))
        ev.append((tu[0] + parabolic(d2, i) / FS, tu[0] + parabolic(-dj, j) / FS, tu[p]))
    return np.array(ev).reshape(-1, 3)


def all_events(ta, vert):
    rows = []
    for tu, v in chunks(ta, vert):
        e = events(tu, v)
        # 공중 = 다음 착지 − 이번 발 떼기 (같은 조각 안의 바로 다음 걸음만)
        for k in range(len(e) - 1):
            rows.append((e[k, 0], e[k, 1], e[k + 1, 0]))
    r = np.array(rows).reshape(-1, 3)
    return pd.DataFrame({'hs': r[:, 0], 'to': r[:, 1], 'flight': r[:, 2] - r[:, 1], 'stance': r[:, 1] - r[:, 0]})


def seg_stats(ev, s, e):
    w = ev[(ev.hs >= s) & (ev.hs < e)]
    f = w.flight[(w.flight >= FLIGHT_OK[0]) & (w.flight <= FLIGHT_OK[1])]
    return dict(n=len(w), n_ok=len(f), flight=f.median() * 1000 if len(f) else np.nan,
                stance=w.stance.median() * 1000 if len(w) else np.nan)


def low_fraction(ch, s, e):
    """구간 안 끊기지 않은 조각들에서 수직이 LOW_G 아래인 시간 비율(1 ms 선형 보간)."""
    tot = below = 0
    for tu, x in ch:
        m = (tu >= s) & (tu < e)
        if m.sum() < 2:
            continue
        tf = np.arange(tu[m][0], tu[m][-1], 0.001)
        xf = np.interp(tf, tu[m], x[m])
        tot += len(tf)
        below += int((xf < LOW_G).sum())
    return below / tot if tot else np.nan


def vertical(d, t0):
    ta, aa = load(d, 'airpods_acc', t0)
    _, ag = load(d, 'airpods_grav', t0)
    gu = ag / np.linalg.norm(ag, axis=1, keepdims=True)
    return ta, np.sum((aa + ag) * gu, axis=1) / G


def impact_feats(env, tenv, s, e):
    """계기판 아이폰 충격·걸음 수(SoundTrackDB 재분석·앱과 같은 계산)"""
    x = env[(tenv >= s) & (tenv < e)]
    cad, _ = sdb.cadence(x)
    return np.log(sdb.impact(x)), cad


def record_B():
    run = ROOT / 'data/runs/20261029-025507'
    meta = json.load(open(run / 'meta.json', encoding='utf-8'))
    t0 = meta['t0']
    known = {s['index']: s['start_s'] for s in meta['segments']}
    idx = np.array(sorted(known))
    step = np.polyfit(idx, [known[i] for i in idx], 1)
    segs = []
    for i, st in enumerate(meta['stages']):
        s = known.get(i, np.polyval(step, i)) + 10
        e = known.get(i + 1, np.polyval(step, i + 1)) if i + 1 < len(meta['stages']) else meta['end_s']
        segs.append((f'#{i}', st['speed_kmh'], s, e))
    return run / 'phone', t0, segs


def record_M():
    d = ROOT / 'data/runs/20261029-040156/phone'
    stage = pd.read_csv(d / 'stage.csv')
    mark = pd.read_csv(d / 'mark.csv')
    t0 = stage.t.iloc[0]
    segs = []
    cal = stage[stage.set == 1].reset_index(drop=True)
    for i, r in cal.iterrows():
        e = cal.t[i + 1] if i + 1 < len(cal) else stage.t[stage.set == 3].iloc[0]
        segs.append((f'보정{i}', r.speed, r.t + 5 - t0, e - t0))
    for k, (s, e, v) in enumerate(true_speed(mark, stage.t.iloc[-1])):
        if e - s > 10 + 5:
            segs.append((f'자유{k}', v, s + 10 - t0, e - t0))
    return d, t0, segs


def analyse(name, d, t0, segs):
    ta, vert = vertical(d, t0)
    ev = all_events(ta, vert)
    ch = chunks(ta, vert)
    tp, ap = load(d, 'iphone_acc', t0)
    _, env, _ = sdb.envelopes(tp, ap)
    tenv = tp[0] + np.arange(len(env)) / sdb.ENV_FS
    rows = []
    for lab, v, s, e in segs:
        st = seg_stats(ev, s, e)
        lnimp, cad = impact_feats(env, tenv, s, e)
        vlow = low_fraction(ch, s, e) * 60 / cad * 1000  # ms/걸음
        rows.append(dict(rec=name, seg=lab, v=v, dur=e - s, lnimp=lnimp, cad=cad, vlow=vlow, **st))
    return pd.DataFrame(rows), ev


def loo(r, cols):
    e = []
    for j in range(len(r)):
        tr = r.drop(j)
        X = np.c_[np.ones(len(tr)), tr[cols].values]
        c = np.linalg.lstsq(X, tr.v.values, rcond=None)[0]
        e.append(np.r_[1, r.loc[j, cols].values] @ c - r.v[j])
    e = np.abs(e)
    return f'평균 {e.mean():.2f} / 최대 {e.max():.2f}'


def main():
    from scipy.stats import spearmanr
    out = []
    for name, rec in (('B', record_B), ('M', record_M)):
        df, ev = analyse(name, *rec())
        out.append(df)
        print(f'\n== {name}: 걸음 {len(ev)}개, 공중 {FLIGHT_OK[0] * 1000:.0f} ms에서 {FLIGHT_OK[1] * 1000:.0f} ms 안 '
              f'{((ev.flight >= FLIGHT_OK[0]) & (ev.flight <= FLIGHT_OK[1])).mean():.0%}')
        print(f'  {"구간":<6} {"속도":>4} {"길이":>4} {"걸음":>4} {"공중OK":>6} {"공중 ms":>7} {"닿음 ms":>7} {"걸음/분":>7}'
              f' {"수직<0.3g":>9}')
        for _, r in df.iterrows():
            print(f'  {r.seg:<6} {r.v:>4.0f} {r.dur:>3.0f}s {r.n:>4} {r.n_ok:>6} {r.flight:>7.0f} {r.stance:>7.0f} {r.cad:>7.0f}'
                  f' {r.vlow:>9.0f}')
        run = df[(df.v >= 8) & (df.n_ok >= 10)].reset_index(drop=True) if name == 'B' else \
            df[(df.cad >= 150) & (df.n_ok >= 10)].reset_index(drop=True)
        rho = spearmanr(run.v, run.flight)
        k = np.polyfit(run.v, run.flight, 1)[0] if run.v.nunique() > 1 else np.nan
        print(f'  달리기 {len(run)}구간: 공중 시간 순위 상관 {rho.correlation:+.2f} (p {rho.pvalue:.2f}), 기울기 {k:+.1f} ms/(km/h)')
        ks = np.polyfit(run.v, run.stance, 1)[0] if run.v.nunique() > 1 else np.nan
        print(f'  발 닿는 시간 기울기 {ks:+.1f} ms/(km/h), 순위 상관 {spearmanr(run.v, run.stance).correlation:+.2f}')
        if len(run) >= 5:
            for label, cols in (('충격만', ['lnimp']), ('충격 + 공중 시간', ['lnimp', 'flight']),
                                ('충격 + 발 닿는 시간', ['lnimp', 'stance']), ('공중 시간만', ['flight'])):
                print(f'  달리기 구간 하나씩 빼고 [{label}] {loo(run, cols)}')
        rv = df[df.cad >= 150].reset_index(drop=True) if name == 'M' else df[df.v >= 8].reset_index(drop=True)
        rho = spearmanr(rv.v, rv.vlow)
        print(f'  [추가 탐색] 수직 {LOW_G} g 아래: 걷기 구간 {", ".join(f"{x:.0f}" for x in df[df.cad < 150].vlow)} ms, '
              f'달리기 순위 상관 {rho.correlation:+.2f} (p {rho.pvalue:.3f}), 기울기 {np.polyfit(rv.v, rv.vlow, 1)[0]:+.1f} ms/(km/h)')
        print('    속도별 최소·최대 ' + ', '.join(f'{v:.0f}: {q.min():.0f}에서 {q.max():.0f}' for v, q in rv.groupby('v').vlow))
        for label, cols in (('충격 + 수직 0.3 g 아래', ['lnimp', 'vlow']), ('수직 0.3 g 아래만', ['vlow'])):
            print(f'    달리기 구간 하나씩 빼고 [{label}] {loo(rv, cols)}')
    plot(pd.concat(out))


def plot(df):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    from matplotlib import font_manager
    have = {f.name for f in font_manager.fontManager.ttflist}
    plt.rcParams['font.family'] = next((f for f in ('AppleGothic', 'Malgun Gothic') if f in have), 'sans-serif')
    plt.rcParams['axes.unicode_minus'] = False
    fig, axs = plt.subplots(1, 2, figsize=(11, 4))
    for ax, col, lab in ((axs[0], 'flight', '공중 시간 중앙값 (ms)'), (axs[1], 'stance', '발 닿는 시간 중앙값 (ms)')):
        for rec, c in (('B', '#d1495b'), ('M', '#1f6feb')):
            q = df[df.rec == rec]
            ax.scatter(q.v + (0.05 if rec == 'M' else -0.05), q[col], color=c, label=rec)
        ax.set_xlabel('러닝머신 속도 (km/h)')
        ax.set_ylabel(lab)
    axs[0].legend()
    fig.suptitle('에어팟, 착지·발 떼기 순간 방식 (계획 0011)')
    fig.tight_layout()
    out = ROOT / 'docs/speed/img/0011_events.png'
    fig.savefig(out, dpi=120)
    print('\n그림:', out.relative_to(ROOT))


if __name__ == '__main__':
    main()
