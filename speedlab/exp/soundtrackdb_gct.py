"""계획 0006 추가: 발 닿는 시간(GCT)·공중에 뜬 시간을 더하면 나아지나 (SoundTrackDB).

GCT는 데이터의 정답(YOLO_Contact_Time, 영상 기반)이라 "완벽하게 잰다면"의 상한선.
soundtrackdb.py가 만든 sessions.csv(충격·걸음 수)에 세션별 GCT를 붙여 같은 방식으로 맞히기.

    cd speedlab/exp && ../../.venv/Scripts/python soundtrackdb_gct.py
"""
import itertools

import numpy as np
import pandas as pd

from soundtrackdb import DB, MPH, TRIM_S, summ

s = pd.read_csv(DB / "sessions.csv")
rows = []
for f in sorted((DB / "gct_csv").glob("*.csv")):
    day, subj, mph, trial = f.stem.split("_")
    g = pd.read_csv(f, usecols=["YOLO_Contact_Time", "YOLO_Start_Time"]).dropna()
    t = g.YOLO_Start_Time
    g = g[(t > TRIM_S) & (t < t.max() - TRIM_S) & (g.YOLO_Contact_Time > 50) & (g.YOLO_Contact_Time < 600)]
    if len(g) < 30:
        continue
    step = np.diff(np.sort(g.YOLO_Start_Time.values))        # 왼발·오른발 번갈아 → 한 걸음 시간
    step = step[(step > 0.2) & (step < 0.6)]
    rows.append(dict(day=day, subj=subj, mph=int(mph), trial=int(trial),
                     G=g.YOLO_Contact_Time.mean(),            # 영상 프레임 단위라 중앙값 대신 평균
                     F=np.median(step) * 1000 - g.YOLO_Contact_Time.mean()))
s = s.merge(pd.DataFrame(rows), on=["day", "subj", "mph", "trial"])
multi = s[s.groupby("grp").mph.transform("nunique") >= 2].copy()
print(f"GCT 붙은 세션 {len(s)}, 속도 2개 이상 묶음 {multi.grp.nunique()}, 3개 이상 {s[s.groupby('grp').mph.transform('nunique') >= 3].grp.nunique()}")

# 같은 묶음 안에서 속도에 따라 어떻게 변하나
tr = pd.DataFrame([dict(G=np.polyfit(d.kmh, d.G, 1)[0], F=np.polyfit(d.kmh, d.F, 1)[0]) for _, d in multi.groupby("grp")])
print(f"1 km/h 빨라질 때: 발 닿는 시간 중앙값 {tr.G.median():.1f} ms (줄어드는 묶음 {(tr.G < 0).mean():.0%}), "
      f"공중 시간 {tr.F.median():.1f} ms (늘어나는 묶음 {(tr.F > 0).mean():.0%})")


def pooled(d, feats):
    c = {x: d[x] - d.groupby("grp")[x].transform("mean") for x in feats + ["kmh"]}
    X = np.c_[[c[x] for x in feats]].T
    return dict(zip(feats, np.linalg.lstsq(X, c["kmh"].values, rcond=None)[0]))


def fit_predict(cal, test, own, prior, pc):
    y = cal.kmh - sum(pc[x] * cal[x] for x in prior) if prior else cal.kmh.copy()
    X = np.c_[np.ones(len(cal)), *[cal[x] for x in own]] if own else np.ones((len(cal), 1))
    coef = np.linalg.lstsq(X, y.values, rcond=None)[0]
    Xt = np.c_[np.ones(len(test)), *[test[x] for x in own]] if own else np.ones((len(test), 1))
    return Xt @ coef + (sum(pc[x] * test[x] for x in prior) if prior else 0)


# (이름, 보정 개수, 이 사람 점으로 맞출 것, 다른 사람들 기울기를 빌릴 것)
SPECS = [
    ("충격", 1, [], ["L"]), ("발 닿는 시간", 1, [], ["G"]), ("충격 + 발 닿는 시간", 1, [], ["L", "G"]),
    ("충격 + 발 닿는·공중 시간 + 걸음 수", 1, [], ["L", "G", "F", "cad"]),
    ("충격", 2, ["L"], []), ("발 닿는 시간", 2, ["G"], []), ("충격 + 발 닿는 시간", 2, ["L"], ["G"]),
    ("충격", 3, ["L"], []), ("발 닿는 시간", 3, ["G"], []), ("공중 시간", 3, ["F"], []),
    ("충격 + 발 닿는 시간", 3, ["L"], ["G"]), ("충격 + 발 닿는 시간(둘 다 이 사람)", 3, ["L", "G"], []),
    ("충격 + 발 닿는·공중 시간 + 걸음 수", 3, ["L"], ["G", "F", "cad"]),
]
out = []
groups = dict(list(multi.groupby("grp")))
for g, d in groups.items():
    o = multi[multi.grp != g]
    pc = pooled(o, ["L", "G", "F", "cad"])
    pcs = {}
    speeds = sorted(d.mph.unique())
    # 보정 없음: 다른 사람들 공식 하나
    for feats in (["L", "cad"], ["L", "G", "F", "cad"]):
        X = np.c_[np.ones(len(o)), *[o[x] for x in feats]]
        coef = np.linalg.lstsq(X, o.kmh.values, rcond=None)[0]
        p = np.c_[np.ones(len(d)), *[d[x] for x in feats]] @ coef
        for e in p - d.kmh.values:
            out.append(dict(grp=g, plan="보정 없음 · " + "+".join(feats), err=e))
    for name, k, own, prior in SPECS:
        if len(speeds) <= k:
            continue
        key = tuple(sorted(own + prior))
        if key not in pcs:
            pcs[key] = pooled(o, list(key)) if key else {}
        for cs in itertools.combinations(speeds, k):
            cal, test = d[d.mph.isin(cs)], d[~d.mph.isin(cs)]
            pred = fit_predict(cal, test, own, prior, pcs[key])
            for (_, t), p in zip(test.iterrows(), pred):
                where = "" if k == 1 else (" · 범위 안" if min(cs) <= t.mph <= max(cs) else " · 범위 밖")
                out.append(dict(grp=g, plan=f"{k}개 · {name}{where}", err=p - t.kmh))
ev = pd.DataFrame(out)
res = pd.DataFrame([dict(plan=p, **summ(e)) for p, e in ev.groupby("plan", sort=False)])
pd.set_option("display.width", 200)
print(res.round(2).to_string(index=False))
res.to_csv(DB / "summary_gct.csv", index=False)

# 잡음 바닥: 같은 사람·날·속도 두 번
pa = pooled(multi, ["G"])["G"]
r = s.groupby(["grp", "mph"]).filter(lambda x: len(x) >= 2).groupby(["grp", "mph"]).G
dg = r.agg(lambda x: x.max() - x.min())
print(f"같은 속도 두 번: 발 닿는 시간 차이 중앙값 {dg.median():.1f} ms = 속도 {abs(pa) * dg.median():.2f} km/h (90% {abs(pa) * dg.quantile(.9):.2f}), 쌍 {len(dg)}")
