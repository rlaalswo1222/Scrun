"""계획 0065 후속(11/25): 워치 새 특징이 '1 km/h 올림'과 '세게 밟기'를 가르는지(가설, 11/21 민재 1명·1회).

특징(워치 WristMotion 100 Hz, 각 틱에서 끝나는 4초 창):
  ① 각도 폭: 회전 속도를 기록 전체 달리기 구간의 주축(PCA 1번)에 투영 → 적분 → 1초 이동평균 빼기(드리프트)
     → 팔 한 주기(창 안 자기상관, 0.5–1.1초)씩 잘라 최대–최소의 중앙값(도)
  ② 위아래 출렁임: 사용자 가속을 중력 방향에 투영한 값의 RMS(Sensor Logger 단위 그대로, 비율만 봄)
  ③ 앞뒤 비대칭: ①의 각도(중심 0)에서 95 백분위 / |5 백분위|(주축 부호는 세트 2 축에 맞춤)
  비교: 팔 회전 RMS(|회전 속도|), 회전 RMS ÷ 걸음 수, 폰 충격 RMS(|가속| − 창 중앙값의 RMS, m/s²)
평가: 세트 2(메트로놈 걸음 수 고정, 단계 경계는 cadence_veto.rec_W와 같음, 단계 시작 5초 뒤부터) 단계별 평균±표준편차,
      이웃 단계 차 ÷ 단계 안 표준편차(d), 세트 3(9 km/h 보통·세게·보통, 구간은 watch_veto와 같음) 세게 변화율 vs 1 km/h 변화율.
실행: set PYTHONIOENCODING=utf-8 && .venv\\Scripts\\python speedlab/exp/watch_features.py
"""
import io
import sys
import zipfile
from pathlib import Path

import numpy as np
import pandas as pd

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cadence_veto import rec_W, score, sl_acc  # noqa: E402
from watch_veto import W2, W3  # noqa: E402

FS = 100.0
WIN = 4.0
SEGS = [('보통', 12, 24), ('세게', 27, 41), ('보통', 45, 54)]


def wrist(zpath):
    """WristMotion만 읽음(위치 파일은 안 읽음)"""
    w = pd.read_csv(io.BytesIO(zipfile.ZipFile(zpath).read('WristMotion.csv')))
    t = w.time.values / 1e9
    rot = w[['rotationRateX', 'rotationRateY', 'rotationRateZ']].values
    g = w[['gravityX', 'gravityY', 'gravityZ']].values
    a = w[['accelerationX', 'accelerationY', 'accelerationZ']].values
    return t, rot, g, a


def main_axis(rot, mask):
    r = rot[mask] - rot[mask].mean(0)
    _, _, vt = np.linalg.svd(r, full_matrices=False)
    return vt[0]


def moving_mean(x, n):
    c = np.concatenate([[0.0], np.cumsum(x)])
    out = np.empty_like(x)
    h = n // 2
    for i in range(len(x)):
        lo, hi = max(0, i - h), min(len(x), i + h + 1)
        out[i] = (c[hi] - c[lo]) / (hi - lo)
    return out


def arm_period(x):
    y = x - x.mean()
    e0 = (y * y).sum()
    if e0 <= 1e-9:
        return np.nan
    lags = np.arange(int(0.5 * FS), int(1.1 * FS))
    c = np.array([(y[:-L] * y[L:]).sum() / e0 for L in lags])
    return lags[c.argmax()] / FS if c.max() > 0.3 else np.nan


def features(zpath, ticks, run_mask_fn, axis=None):
    """ticks: 틱 시각 배열. run_mask_fn(t) → 주축을 구할 달리기 구간 표본 마스크"""
    t, rot, g, a = wrist(zpath)
    ax = main_axis(rot, run_mask_fn(t)) if axis is None else axis
    w = rot @ ax                                     # 주축 회전 속도(rad/s)
    ang = np.cumsum(w) / FS * 180 / np.pi            # 도
    ang = ang - moving_mean(ang, int(1.0 * FS))       # 드리프트·느린 자세 변화 제거
    gu = g / np.linalg.norm(g, axis=1, keepdims=True)
    vert = (a * gu).sum(1)
    rmag = np.linalg.norm(rot, axis=1)
    out = {k: [] for k in ('angle', 'bounce', 'asym', 'rot', 'period')}
    n = int(WIN * FS)
    for tt in ticks:
        hi = np.searchsorted(t, tt)
        lo = hi - n
        if lo < 0 or hi - lo < n * 0.9:
            for k in out:
                out[k].append(np.nan)
            continue
        seg = ang[lo:hi]
        p = arm_period(w[lo:hi])
        if np.isfinite(p):
            L = int(p * FS)
            amps = [seg[i:i + L].max() - seg[i:i + L].min() for i in range(0, len(seg) - L + 1, L)]
            out['angle'].append(np.median(amps))
        else:
            out['angle'].append(np.nan)
        out['bounce'].append(np.sqrt(np.mean(vert[lo:hi] ** 2)))
        c = seg - np.median(seg)
        out['asym'].append(np.percentile(c, 95) / max(1e-6, -np.percentile(c, 5)))
        out['rot'].append(np.sqrt(np.mean(rmag[lo:hi] ** 2)))
        out['period'].append(p)
    return {k: np.array(v) for k, v in out.items()}, ax


def phone_impact(zpath, ticks):
    acc = sl_acc(zpath)
    t = acc.t.values
    m = np.sqrt(acc.x ** 2 + acc.y ** 2 + acc.z ** 2).values
    out = []
    for tt in ticks:
        lo, hi = np.searchsorted(t, tt - WIN), np.searchsorted(t, tt)
        if hi - lo < 300:
            out.append(np.nan); continue
        s = m[lo:hi]
        out.append(np.sqrt(np.mean((s - np.median(s)) ** 2)))
    return np.array(out)


def veto_feat(d, feat, dcad=None, thr=None, dv=0.3, ref_s=6.0):
    """watch_veto.veto2와 같은 틀, 워치 값만 임의 특징으로(feat가 기준보다 thr 비율 넘게 안 오르면 올림 막음)"""
    n_ref = int(ref_s / 0.5)
    out, vetoed = [], []
    hc, hs, hr = [], [], []
    last, cap = np.nan, np.nan
    for a, c, st, r in zip(d.kmh, d.cad, d.state, feat):
        if st != 2 or not np.isfinite(a) or a <= 0 or not np.isfinite(c) or c <= 0:
            hc, hs, hr, cap = [], [], [], np.nan
            out.append(a); vetoed.append(False); last = a
            continue
        cref = np.median(hc) if len(hc) >= 4 else np.nan
        sref = np.median(hs) if len(hs) >= 4 else np.nan
        rref = np.nanmedian(hr) if np.isfinite(hr).sum() >= 4 else np.nan
        low = dcad is not None and np.isfinite(cref) and c < cref - dcad
        weak = (thr is not None and np.isfinite(rref) and np.isfinite(r) and np.isfinite(sref)
                and a > sref + dv and r < rref * (1 + thr))
        o, v = a, False
        if low or weak:
            if not np.isfinite(cap):
                cap = last
            if a > cap:
                o, v = cap, True
        else:
            cap = np.nan
            hc, hs, hr = (hc + [c])[-n_ref:], (hs + [a])[-n_ref:], (hr + [r])[-n_ref:]
        out.append(o); vetoed.append(v); last = o
    return np.array(out), np.array(vetoed)


def main():
    d2, _, d3 = rec_W()
    t2_0 = d2.t.min() - 0.5
    # 주축: 세트 2 달리기(18–115초) 표본으로, 세트 3은 같은 축(같은 손목 착용)
    s2_start = sl_acc(W2).t.iloc[0]
    f2, ax = features(W2, d2.t.values, lambda t: (t >= s2_start + 18) & (t <= s2_start + 115))
    f3, _ = features(W3, d3.t.values, None, axis=ax)
    for f, d, z in ((f2, d2, W2), (f3, d3, W3)):
        f['phone'] = phone_impact(z, d.t.values)
        f['rot_per_step'] = f['rot'] / d.cad.values * 170
    names = [('angle', '① 각도 폭(도)'), ('bounce', '② 위아래 출렁임 RMS'), ('asym', '③ 앞/뒤 비'),
             ('rot', '팔 회전 RMS(rad/s)'), ('rot_per_step', '회전 RMS÷걸음 수(×170)'), ('phone', '폰 충격 RMS')]

    st2 = (d2.state == 2) & (d2.since >= 5)
    run3 = d3.state == 2
    print('세트 2 걸음 수(6초 창) 단계별 중앙: ' + ', '.join(
        f'{v:g} {np.nanmedian(d2.cad[st2 & (d2.v == v)]):.0f}' for v in (8.0, 9.0, 10.0, 11.0)))
    print('세트 3 걸음 수 중앙: ' + ', '.join(
        f'{sn} {np.nanmedian(d3.cad[(d3.rel >= a) & (d3.rel < b) & run3]):.0f}' for sn, a, b in SEGS))
    print('팔 주기 중앙(초): 세트 2 %.2f, 세트 3 %.2f' % (np.nanmedian(f2['period'][st2]), np.nanmedian(f3['period'][run3])))

    print('\n| 특징 | 8 | 9 | 10 | 11 | 1 km/h 변화율(8→9, 9→10, 10→11) | d(차÷표준편차) | 단조 | '
          '세트 3 보통 / 세게 / 보통 | 세게 변화율 | 세게 ÷ 1 km/h 평균 |')
    print('|---|---|---|---|---|---|---|---|---|---|---|')
    summary = {}
    for k, nm in names:
        x2 = f2[k]
        ms, ss = [], []
        for v in (8.0, 9.0, 10.0, 11.0):
            m = st2.values & (d2.v.values == v) & np.isfinite(x2)
            ms.append(x2[m].mean()); ss.append(x2[m].std())
        steps = [ms[i + 1] / ms[i] - 1 for i in range(3)]
        ds = [(ms[i + 1] - ms[i]) / np.sqrt((ss[i] ** 2 + ss[i + 1] ** 2) / 2) for i in range(3)]
        mono = all(ms[i + 1] > ms[i] for i in range(3)) or all(ms[i + 1] < ms[i] for i in range(3))
        x3 = f3[k]
        seg = [np.nanmean(x3[((d3.rel >= a) & (d3.rel < b) & run3).values]) for _, a, b in SEGS]
        stomp = seg[1] / ((seg[0] + seg[2]) / 2) - 1
        mean_step = np.mean(steps)
        summary[k] = (stomp, mean_step, steps)
        print(f'| {nm} | ' + ' | '.join(f'{m:.3g}±{s:.2g}' for m, s in zip(ms, ss)) +
              ' | ' + ', '.join(f'{s * 100:+.1f}%' for s in steps) +
              ' | ' + ', '.join(f'{x:+.1f}' for x in ds) + f' | {"예" if mono else "아니오"}' +
              ' | ' + ' / '.join(f'{x:.3g}' for x in seg) + f' | {stomp * 100:+.1f}% | '
              f'{(stomp / mean_step) if mean_step != 0 else np.nan:+.2f} |')

    # 규칙 시험: 단조 증가이고 세게 변화가 1 km/h 올림 변화(모든 단계)보다 작은 특징만
    test2 = d2.v.notna() & (d2.state == 2)
    cands = [k for k in ('angle', 'bounce', 'asym', 'rot_per_step')
             if all(s > 0 for s in summary[k][2]) and summary[k][0] < min(summary[k][2])]
    print('\n규칙 시험 후보(8→11 모두 오르고, 세게 변화 < 가장 작은 1 km/h 변화): ' + (', '.join(cands) or '없음'))
    for k in cands:
        for thr in (0.5 * min(summary[k][2]), min(summary[k][2])):
            rows = []
            for d, f in ((d3, f3), (d2, f2)):
                rows.append(veto_feat(d, f[k], dcad=3.0, thr=thr))
            e3, _ = rows[0]
            e2, v2 = rows[1]
            parts = [f'{np.nanmean(e3[((d3.rel >= a) & (d3.rel < b) & run3).values]):.2f} '
                     f'({np.nanmax(e3[((d3.rel >= a) & (d3.rel < b) & run3).values]):.2f})' for _, a, b in SEGS]
            s2 = score(d2, e2, test2)
            print(f'  둘 다 Δ3 + {k} {thr * 100:.1f}%: 세트 3 ' + ' / '.join(parts) +
                  f' | 세트 2 막은 틱 {v2[test2].mean() * 100:.1f}% | 반응 중앙 {s2[3]:.1f}초')


if __name__ == '__main__':
    main()
