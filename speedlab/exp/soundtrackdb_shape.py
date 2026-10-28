"""계획 0006 추가: 폰 가속도에서 충격 펄스 "모양"(폭 등)을 뽑아 발 닿는 시간 대신 쓸 수 있나 (SoundTrackDB).

영상 시각과 폰 신호 정렬이 흐려서, 폰 신호에서 스스로 찾은 충격 봉우리에 맞춰 평균 펄스를 만든다.
    cd speedlab/exp && ../../.venv/Scripts/python soundtrackdb_shape.py
"""
import itertools

import numpy as np
import pandas as pd
from scipy.signal import butter, find_peaks, sosfiltfilt

from soundtrackdb import DB, TRIM_S, summ
from soundtrackdb_gct import fit_predict, pooled, s as sg   # 정답 GCT가 붙은 세션 표

FS = 100


def shape_features(path, cad):
    a = pd.read_csv(path)
    X = a[["ax", "ay", "az"]].values
    m = np.sqrt((sosfiltfilt(butter(4, 8, "high", fs=FS, output="sos"), X, axis=0) ** 2).sum(1))
    e = sosfiltfilt(butter(4, 30, "low", fs=FS, output="sos"), m)
    k0 = int(TRIM_S * FS)
    e = e[k0:len(e) - k0]
    period = 60 / cad * FS
    pk, _ = find_peaks(e, distance=int(0.7 * period), height=np.percentile(e, 60))
    lag = np.arange(-25, 26)
    pk = pk[(pk > 25) & (pk < len(e) - 26)]
    if len(pk) < 50:
        return None
    T = np.array([e[p + lag] for p in pk]).mean(0)
    base, top = np.percentile(e, 20), T[25]
    half = base + 0.5 * (top - base)
    i0 = 25
    while i0 > 0 and T[i0 - 1] >= half: i0 -= 1
    i1 = 25
    while i1 < len(T) - 1 and T[i1 + 1] >= half: i1 += 1
    w = (i1 - i0 + 1) * 1000 / FS                           # 반높이 폭(ms)
    tail = (T[30:46].mean() - base) / (top - base)           # 봉우리 뒤 50에서 200 ms 남은 세기 비율
    pre = (T[5:21].mean() - base) / (top - base)             # 봉우리 앞 40에서 200 ms
    return dict(W=w, tail=tail, pre=pre)


rows = []
for _, r in sg.iterrows():
    f = shape_features(DB / "imu_csv" / f"{r.day}_{r.subj}_{r.mph}_{r.trial}.csv", r.cad)
    if f:
        rows.append(dict(day=r.day, subj=r.subj, mph=r.mph, trial=r.trial, **f))
s = sg.merge(pd.DataFrame(rows), on=["day", "subj", "mph", "trial"])
multi = s[s.groupby("grp").mph.transform("nunique") >= 2].copy()

# 같은 사람 안에서: 모양 값이 정답 GCT를 따라가나 (묶음 평균을 뺀 상관)
c = lambda x: multi[x] - multi.groupby("grp")[x].transform("mean")
for x in ["W", "tail", "pre", "L"]:
    print(f"묶음 안 상관: {x} ↔ 정답 GCT {np.corrcoef(c(x), c('G'))[0, 1]:+.2f}, ↔ 속도 {np.corrcoef(c(x), c('kmh'))[0, 1]:+.2f}")
# 속도를 빼고도 GCT와 관계있나(충격으로 설명되는 부분 뺀 나머지)
res_g = c("G") - np.polyval(np.polyfit(c("kmh"), c("G"), 1), c("kmh"))
for x in ["W", "tail", "pre"]:
    rx = c(x) - np.polyval(np.polyfit(c("kmh"), c(x), 1), c("kmh"))
    print(f"속도를 뺀 나머지끼리 상관: {x} ↔ GCT {np.corrcoef(rx, res_g)[0, 1]:+.2f}")

out = []
SPECS = [("충격", ["L"], []), ("충격 + 정답 GCT", ["L"], ["G"]), ("충격 + 폭", ["L"], ["W"]),
         ("충격 + 뒤꼬리", ["L"], ["tail"]), ("충격 + 폭·앞·뒤", ["L"], ["W", "tail", "pre"])]
for g, d in multi.groupby("grp"):
    o = multi[multi.grp != g]
    speeds = sorted(d.mph.unique())
    for k in (2, 3):
        if len(speeds) <= k:
            continue
        for name, own, prior in SPECS:
            pc = pooled(o, own + prior)
            for cs in itertools.combinations(speeds, k):
                cal, test = d[d.mph.isin(cs)], d[~d.mph.isin(cs)]
                for (_, t), p in zip(test.iterrows(), fit_predict(cal, test, own, prior, pc)):
                    where = "범위 안" if min(cs) <= t.mph <= max(cs) else "범위 밖"
                    out.append(dict(grp=g, plan=f"{k}개 · {name} · {where}", err=p - t.kmh))
ev = pd.DataFrame(out)
res = pd.DataFrame([dict(plan=p, **summ(e)) for p, e in ev.groupby("plan")])
pd.set_option("display.width", 200)
print(res.round(2).to_string(index=False))
