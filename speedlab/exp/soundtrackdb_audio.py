"""계획 0007: 소리를 더하면 속도 오차가 줄어드나 (SoundTrackDB, 계기판 위 폰 마이크).

ffmpeg로 16kHz 한 채널로 풀고 10 ms 칸마다 대역별 세기 → 20초 창마다 발소리 크기·바탕 대비 비율.
soundtrackdb.py가 만든 sessions.csv(충격·걸음 수)와 합쳐 같은 방식으로 맞히기.

    cd speedlab/exp && ../../.venv/Scripts/python soundtrackdb_audio.py
"""
import itertools
import subprocess

import numpy as np
import pandas as pd

from soundtrackdb import DB, ENV_FS, TRIM_S, WIN_S, cadence, summ

SR, HOP, NFFT = 16000, 160, 512           # 10 ms 칸 = ENV_FS 100
BANDS = {"low": (50, 300), "mid": (300, 2000), "high": (2000, 6000)}


def decode(path):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(path), "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.float32)


def band_power(x):
    n = (len(x) - NFFT) // HOP
    idx = np.arange(NFFT)[None, :] + HOP * np.arange(n)[:, None]
    P = np.abs(np.fft.rfft(x[idx] * np.hanning(NFFT), axis=1)) ** 2
    f = np.fft.rfftfreq(NFFT, 1 / SR)
    return {b: P[:, (f >= lo) & (f < hi)].sum(1) for b, (lo, hi) in BANDS.items()}


def peak_level(s):
    thr = np.sort(s)[int(0.75 * (len(s) - 1))]
    pk = [s[i] for i in range(12, len(s) - 12) if s[i] >= thr and np.argmax(s[i - 12:i + 13]) == 12]
    return float(np.median(pk)) if pk else np.nan


def audio_points():
    cache = DB / "audio_points.csv"
    if cache.exists():
        return pd.read_csv(cache)
    rows = []
    for f in sorted((DB / "audio_m4a").glob("*.m4a")):
        day, subj, mph, trial = f.stem.split("_")
        try:
            bp = band_power(decode(f))
        except subprocess.CalledProcessError:
            print("못 읽음", f.name)
            continue
        w, k0, n = int(WIN_S * ENV_FS), int(TRIM_S * ENV_FS), len(bp["mid"])
        for i in range(k0, n - k0 - w + 1, w):
            r = dict(day=day, subj=subj, mph=int(mph), trial=int(trial), win=i // w)
            c, q = cadence(bp["mid"][i:i + w].astype(float))
            r.update(acad=c, aq=q)
            for b, p in bp.items():
                seg = p[i:i + w].astype(float)
                pk, bg = peak_level(seg), np.percentile(seg, 20)
                r["AL_" + b] = np.log(pk + 1e-12)
                r["AR_" + b] = np.log((pk + 1e-12) / (bg + 1e-12))
            rows.append(r)
    df = pd.DataFrame(rows)
    df.to_csv(cache, index=False)
    return df


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


def main():
    w = audio_points()
    print(f"소리 창 {len(w)}, 소리로 걸음 잡힘(q≥0.3) {(w.aq >= 0.3).mean():.0%}")
    feats = [c for c in w.columns if c.startswith("AL_") or c.startswith("AR_")]
    a = w.groupby(["day", "subj", "mph", "trial"])[feats + ["acad"]].median().reset_index()
    s = pd.read_csv(DB / "sessions.csv").merge(a, on=["day", "subj", "mph", "trial"])
    multi = s[s.groupby("grp").mph.transform("nunique") >= 2].copy()
    print(f"세션 {len(s)}, 속도 2개 이상 묶음 {multi.grp.nunique()}")

    c = lambda x: multi[x] - multi.groupby("grp")[x].transform("mean")
    rk = c("kmh")
    print("묶음 안 상관(속도와):", ", ".join(f"{x} {np.corrcoef(c(x), rk)[0, 1]:+.2f}" for x in ["L"] + feats))
    # 충격으로 설명 안 되는 속도 부분을 소리가 설명하나
    res = rk - np.polyval(np.polyfit(c("L"), rk, 1), c("L"))
    print("충격을 뺀 나머지와 상관:", ", ".join(f"{x} {np.corrcoef(c(x), res)[0, 1]:+.2f}" for x in feats))

    SPECS = [("충격", ["L"], []), ("소리 크기(중간)", ["AL_mid"], []), ("소리 비율(중간)", ["AR_mid"], []),
             ("충격 + 소리 크기(중간)", ["L"], ["AL_mid"]), ("충격 + 소리 비율(중간)", ["L"], ["AR_mid"]),
             ("충격 + 소리 3대역 크기", ["L"], ["AL_low", "AL_mid", "AL_high"]),
             ("충격 + 소리 3대역 비율", ["L"], ["AR_low", "AR_mid", "AR_high"])]
    out = []
    for g, d in multi.groupby("grp"):
        o = multi[multi.grp != g]
        speeds = sorted(d.mph.unique())
        for name, own, prior in SPECS:
            pc = pooled(o, own + prior)
            for k in (1, 2, 3):
                if len(speeds) <= k:
                    continue
                o1, p1 = (own, prior) if k > 1 else ([], own + prior)
                for cs in itertools.combinations(speeds, k):
                    cal, test = d[d.mph.isin(cs)], d[~d.mph.isin(cs)]
                    for (_, t), p in zip(test.iterrows(), fit_predict(cal, test, o1, p1, pc)):
                        where = "" if k == 1 else (" · 범위 안" if min(cs) <= t.mph <= max(cs) else " · 범위 밖")
                        out.append(dict(grp=g, plan=f"{k}개 · {name}{where}", err=p - t.kmh))
    ev = pd.DataFrame(out)
    r = pd.DataFrame([dict(plan=p, **summ(e)) for p, e in ev.groupby("plan")])
    pd.set_option("display.width", 200)
    print(r.round(2).to_string(index=False))
    r.to_csv(DB / "summary_audio.csv", index=False)

    # 잡음 바닥: 같은 사람·날·속도 두 번 → 속도로 환산
    rep = s.groupby(["grp", "mph"]).filter(lambda x: len(x) >= 2)
    for x in ["L", "AL_mid", "AR_mid"]:
        b = pooled(multi, [x])[x]
        dx = rep.groupby(["grp", "mph"])[x].agg(lambda v: v.max() - v.min())
        print(f"같은 속도 두 번 {x}: 속도로 중앙값 {abs(b) * dx.median():.2f}, 90% {abs(b) * dx.quantile(.9):.2f} km/h")


if __name__ == "__main__":
    main()
