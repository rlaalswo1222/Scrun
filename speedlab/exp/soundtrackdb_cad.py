"""계획 0006 추가: 충격 없이 걸음 수만으로 (SoundTrackDB). soundtrackdb.py가 만든 sessions.csv를 씀.

    .venv/Scripts/python speedlab/exp/soundtrackdb_cad.py
"""
import itertools

import numpy as np
import pandas as pd

from soundtrackdb import DB, MPH, summ

s = pd.read_csv(DB / "sessions.csv")
info = pd.read_csv(DB / "SoundTrack_subject_info.csv")[["subject", "height"]]
s = s.merge(info, left_on="subj", right_on="subject", how="left")
s["step"] = s.kmh / 3.6 / (s.cad / 60)          # 한 걸음 길이(m)
s["ratio"] = s.step / (s.height / 100)          # 키 대비 보폭
multi = s[s.groupby("grp").mph.transform("nunique") >= 2]


def slope_pool(d, x):
    """km/h per x. 걸음 수는 잡음이 커서 속도를 걸음 수에 회귀하면 기울기가 0으로 눌림 → x를 속도에 회귀하고 뒤집음"""
    xc = d[x] - d.groupby("grp")[x].transform("mean")
    yc = d.kmh - d.groupby("grp").kmh.transform("mean")
    return (yc ** 2).sum() / (xc * yc).sum()


rows = []
for g, d in multi.groupby("grp"):
    o = multi[multi.grp != g]
    c_pop = slope_pool(o, "cad")
    # 보정 없음 ①: 다른 사람들 v = a + c·spm / ②: 보폭 = 키 × (다른 사람들 키 대비 보폭, 걸음 수 따라 직선)
    a, c = np.polyfit(o.cad, o.kmh, 1)[::-1]
    rb, ra = np.polyfit(o.cad, o.ratio, 1)
    for _, t in d.iterrows():
        rows.append(dict(grp=g, plan="보정 없음 · 걸음 수 공식", err=a + c * t.cad - t.kmh))
        if not np.isnan(t.height):
            v = (ra + rb * t.cad) * t.height / 100 * t.cad / 60 * 3.6
            rows.append(dict(grp=g, plan="보정 없음 · 키 × 걸음 수", err=v - t.kmh))
    speeds = sorted(d.mph.unique())
    for k in (1, 2, 3):
        if len(speeds) <= k:
            continue
        for cs in itertools.combinations(speeds, k):
            cal, test = d[d.mph.isin(cs)], d[~d.mph.isin(cs)]
            if k == 1:
                a1 = (cal.kmh - c_pop * cal.cad).mean()
                pred, name = a1 + c_pop * test.cad, "1개 · 기울기는 다른 사람들"
            else:
                if cal.cad.max() - cal.cad.min() < 1:
                    continue
                cc, aa = np.polyfit(cal.cad, cal.kmh, 1)
                pred, name = aa + cc * test.cad, f"{k}개 · 직선"
            for (_, t), p in zip(test.iterrows(), pred):
                inside = k > 1 and min(cs) <= t.mph <= max(cs)
                rows.append(dict(grp=g, plan=name + (" · 범위 안" if inside else " · 범위 밖" if k > 1 else ""), err=p - t.kmh))
ev = pd.DataFrame(rows)
res = pd.DataFrame([dict(plan=p, **summ(e)) for p, e in ev.groupby("plan", sort=False)])
print(res.round(2).to_string(index=False))

# 잡음 바닥: 같은 사람·날·속도 두 번 → 걸음 수 차이를 속도로
c_all = slope_pool(multi, "cad")
r = s.groupby(["grp", "mph"]).filter(lambda x: len(x) >= 2).groupby(["grp", "mph"]).cad
dc = r.agg(lambda x: x.max() - x.min())
print(f"공통 기울기 {c_all:.3f} km/h per spm (= {1 / c_all:.1f} spm per km/h)")
print(f"같은 속도 두 번: 걸음 수 차이 중앙값 {dc.median():.1f} spm = 속도 {dc.median() * c_all:.2f} km/h, 90% {dc.quantile(.9) * c_all:.2f}")
