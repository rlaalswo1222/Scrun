"""계획 0065 3번 PC 분석: 걸음 수 규칙(cadence_veto)에 워치 팔 회전 규칙을 더하면 세게 밟기 튐이 더 줄고
진짜 속도 올림은 안 막는지(가설, 11/21 민재 1명·1회).

규칙(워치): 앱 값(폰 충격 식)이 기준 속도보다 dv 넘게 올랐는데 워치 팔 회전 RMS(|rotationRate|, win초 창)가
기준 회전보다 thr 넘게 오르지 않았으면, 그 동안은 막히기 직전 값보다 올리지 않음(내려가는 건 그대로).
기준 속도·기준 회전 = 막히지 않은 동안 최근 ref_s초의 중앙값(막힌 동안은 고정). 걸음 수 규칙은 cadence_veto와 같음.
둘 다(OR): 한 틱에서 둘 중 하나라도 걸리면 막음(한 반복문, 직전 값·기준을 같이 씀).

실행: set PYTHONIOENCODING=utf-8 && .venv\\Scripts\\python speedlab/exp/watch_veto.py
10/29 M·B는 워치 기록이 없어 워치 규칙이 꺼짐 → 걸음 수 규칙 결과(cadence_veto)와 같음.
"""
import io
import sys
import zipfile
from pathlib import Path

import numpy as np
import pandas as pd

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cadence_veto import ROOT, rec_W, score  # noqa: E402

W2 = ROOT / 'data/runs/20261121-watch/2026-11-20_19-06-26.zip'
W3 = ROOT / 'data/runs/20261121-watch/2026-11-20_19-13-20.zip'


def wrist_rot(zpath):
    """WristMotion.csv(워치 100 Hz)에서 시각(초, 폰과 같은 Sensor Logger 시계)·|회전 속도|(rad/s). 위치 파일은 안 읽음"""
    w = pd.read_csv(io.BytesIO(zipfile.ZipFile(zpath).read('WristMotion.csv')))
    return w.time.values / 1e9, np.sqrt(w.rotationRateX ** 2 + w.rotationRateY ** 2 + w.rotationRateZ ** 2).values


def attach_rot(d, zpath, win):
    """각 틱 시각에서 끝나는 win초 창의 팔 회전 RMS"""
    t, r = wrist_rot(zpath)
    c = np.concatenate([[0.0], np.cumsum(r ** 2)])
    lo, hi = np.searchsorted(t, d.t - win), np.searchsorted(t, d.t)
    n = hi - lo
    return np.where(n > win * 50, np.sqrt((c[hi] - c[lo]) / np.maximum(n, 1)), np.nan)


def veto2(d, rot=None, dcad=None, thr=None, dv=0.3, ref_s=6.0):
    """dcad: 걸음 수 규칙 문턱(spm, None=끔). thr: 워치 규칙 회전 상승 문턱(비율, None=끔)"""
    n_ref = int(ref_s / 0.5)
    out, vetoed = [], []
    hc, hs, hr = [], [], []
    last, cap = np.nan, np.nan
    rot = np.full(len(d), np.nan) if rot is None else rot
    for a, c, st, r in zip(d.kmh, d.cad, d.state, rot):
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
    d2, pts, d3 = rec_W()
    test2 = d2.v.notna() & (d2.state == 2)
    segs = [('보통', 12, 24), ('세게', 27, 41), ('보통', 45, 54)]
    run3 = d3.state == 2
    rules = [('지금 앱', {})]
    rules += [('걸음 수 Δ3', dict(dcad=3.0))]
    rules += [(f'워치 {int(th * 100)}% 창{w:g}초', dict(thr=th, win=w)) for w in (4.0,) for th in (0.03, 0.05, 0.08)]
    rules += [(f'워치 5% 창{w:g}초', dict(thr=0.05, win=w)) for w in (2.0, 6.0)]
    rules += [(f'둘 다 Δ3 + 워치 {int(th * 100)}% 창4초', dict(dcad=3.0, thr=th, win=4.0)) for th in (0.03, 0.05)]

    for win in (2.0, 4.0, 6.0):
        r3 = attach_rot(d3, W3, win)
        print(f'세트 3 팔 회전 RMS(창 {win:g}초, rad/s): ' + ' | '.join(
            f'{sn} {np.nanmedian(r3[(d3.rel >= a) & (d3.rel < b) & run3]):.2f}' for sn, a, b in segs))
    print('세트 2 팔 회전 RMS(창 4초, 단계 시작 5초 뒤부터): ' + ' | '.join(
        f'{v:g} {np.nanmedian(attach_rot(d2, W2, 4.0)[(d2.v == v) & (d2.since >= 5) & (d2.state == 2)]):.2f}'
        for v in (8.0, 9.0, 10.0, 11.0)))

    print('\n| 규칙 | 세트 3 보통 평균(최대) | 세게 평균(최대) | 보통 평균(최대) | 세트 3 막은 틱 | '
          '세트 2 막은 틱 | 세트 2 반응 중앙 | 세트 2 최대 |오차| |')
    print('|---|---|---|---|---|---|---|---|')
    for name, kw in rules:
        kw = dict(kw)
        win = kw.pop('win', 4.0)
        if not kw:
            e3, v3 = d3.kmh.values, np.zeros(len(d3), bool)
            e2, v2 = d2.kmh.values, np.zeros(len(d2), bool)
        else:
            e3, v3 = veto2(d3, attach_rot(d3, W3, win), **kw)
            e2, v2 = veto2(d2, attach_rot(d2, W2, win), **kw)
        parts = []
        for sn, a, b in segs:
            m = (d3.rel >= a) & (d3.rel < b) & run3
            parts.append(f'{np.nanmean(e3[m]):.2f} ({np.nanmax(e3[m]):.2f})')
        s2 = score(d2, e2, test2)
        print(f'| {name} | ' + ' | '.join(parts) +
              f' | {int(v3[run3].sum())}/{int(run3.sum())} | {int(v2[test2].sum())}/{int(test2.sum())} '
              f'({v2[test2].mean() * 100:.1f}%) | {s2[3]:.1f}초 (못 들어옴 {s2[4]}/{s2[5]}) | {s2[2]:.2f} |')


if __name__ == '__main__':
    main()
