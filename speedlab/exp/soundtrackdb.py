"""계획 0006: SoundTrackDB(61명, 헬스장 13곳, 계기판 위 폰 가속도 100Hz)로 충격 + 걸음 수 공식 확인.

SpeedLab(StepSpeed.cs)의 충격 세기·걸음 수 계산을 그대로 옮겨 파일마다 점을 만들고,
같은 사람·같은 날 묶음마다 보정 속도 1, 2, 3개로 남은 속도를 맞혀 오차를 낸다.

    .venv/Scripts/python speedlab/exp/soundtrackdb.py
"""
import itertools
import math
from pathlib import Path

import numpy as np
import pandas as pd
from scipy.signal import butter, sosfilt

ROOT = Path(__file__).resolve().parents[2]
DB = ROOT / "data" / "external" / "soundtrackdb"
OUT = ROOT / "docs" / "speed"
MPH = 1.609344
ENV_FS = 100
WIN_S, TRIM_S = 20.0, 30.0


# ---------- StepSpeed.cs 옮김 ----------
def envelopes(t, xyz):
    fs = round(1 / np.median(np.diff(t)))
    hp = butter(4, 8, "high", fs=fs, output="sos")
    m = np.sqrt(sum(sosfilt(hp, xyz[:, i]) ** 2 for i in range(3)))
    env = sosfilt(butter(4, 5, "low", fs=fs, output="sos"), m)
    e30 = sosfilt(butter(4, 30, "low", fs=fs, output="sos"), m)
    dec = max(1, fs // ENV_FS)
    nb = len(m) // dec
    return fs, env[: nb * dec : dec], e30[: nb * dec].reshape(nb, dec).max(1)


def cadence(s):
    s = s - s.mean()
    n = len(s)
    lo, hi = int(math.floor(ENV_FS * 60 / 220)), int(math.ceil(ENV_FS * 60 / 70))
    hi = min(hi + 2, n // 2)
    r0 = (s * s).mean()
    if r0 <= 1e-12:
        return 0.0, 0.0
    r = np.zeros(hi + 2)
    for k in range(lo - 1, min(hi + 2, n)):
        r[k] = (s[: n - k] * s[k:]).sum() / (n - k) / r0
    best = lo + int(np.argmax(r[lo : hi + 1]))
    for mm in (3, 2):
        c = best / mm
        if c < lo:
            continue
        a, b = max(lo, int(c) - 2), min(hi, int(c) + 3)
        kh = a + int(np.argmax(r[a : b + 1]))
        if r[kh] > 0.1:
            best = kh
            break
    d = 0.0
    if lo < best < hi:
        y0, y1, y2 = r[best - 1], r[best], r[best + 1]
        den = y0 - 2 * y1 + y2
        if den != 0:
            d = max(-0.5, min(0.5, 0.5 * (y0 - y2) / den))
    return 60.0 / ((best + d) / ENV_FS), max(0.0, r[best])


def impact(s):
    thr = np.sort(s)[int(0.75 * (len(s) - 1))]
    pk = [s[i] for i in range(12, len(s) - 12)
          if s[i] >= thr and np.argmax(s[i - 12 : i + 13]) == 12]
    return float(np.median(pk)) if pk else 0.0


# ---------- 파일 → 점 ----------
def file_points():
    cache = DB / "points.csv"
    if cache.exists():
        return pd.read_csv(cache)
    rows = []
    for f in sorted((DB / "imu_csv").glob("*.csv")):
        day, subj, mph, trial = f.stem.split("_")
        a = pd.read_csv(f)
        t = a.time.values
        if not 0.009 < np.median(np.diff(t)) < 0.011:   # 시간 열이 깨진 파일(d77_P26_8_0) 건너뜀
            print("건너뜀", f.name)
            continue
        fs, env, imp = envelopes(t, a[["ax", "ay", "az"]].values)
        w, k0 = int(WIN_S * ENV_FS), int(TRIM_S * ENV_FS)
        for i in range(k0, len(env) - k0 - w + 1, w):
            c, q = cadence(env[i : i + w])
            rows.append(dict(day=day, subj=subj, mph=int(mph), trial=int(trial), fs=fs, win=i // w,
                             cad=c, q=q, impact=impact(imp[i : i + w])))
    df = pd.DataFrame(rows)
    df.to_csv(cache, index=False)
    return df


def session_points(win):
    ok = win[(win.q >= 0.3) & (win.impact > 0)]
    g = ok.groupby(["day", "subj", "mph", "trial"])
    s = g.agg(cad=("cad", "median"), impact=("impact", "median"), nwin=("win", "size")).reset_index()
    s = s[s.nwin >= 3]
    s["kmh"] = s.mph * MPH
    s["L"] = np.log(s.impact)
    s["grp"] = s.subj + "@" + s.day
    return s


# ---------- 보정 → 예측 ----------
def pooled_slopes(df):
    """묶음마다 절편을 따로 두고 공통 기울기(충격, 걸음 수)를 맞춤 = 1구간 보정용 사전값"""
    d = df.copy()
    for c in ("kmh", "L", "cad"):
        d[c + "_c"] = d[c] - d.groupby("grp")[c].transform("mean")
    bL = (d.L_c * d.kmh_c).sum() / (d.L_c ** 2).sum()
    X = d[["L_c", "cad_c"]].values
    bLc = np.linalg.lstsq(X, d.kmh_c.values, rcond=None)[0]
    return bL, bLc


def predict(cal, test, method, prior):
    bL, bLc = prior
    if method == "imp_1pt":           # 기울기는 다른 사람들 값, 절편만 이 사람
        a = (cal.kmh - bL * cal.L).mean()
        return a + bL * test.L
    if method == "impcad_1pt":
        a = (cal.kmh - bLc[0] * cal.L - bLc[1] * cal.cad).mean()
        return a + bLc[0] * test.L + bLc[1] * test.cad
    if method == "imp_fit":           # 이 사람 점들로 a, b
        b, a = np.polyfit(cal.L, cal.kmh, 1)
        return a + b * test.L
    if method == "impcad_fit":
        X = np.c_[np.ones(len(cal)), cal.L, cal.cad]
        a, b, c = np.linalg.lstsq(X, cal.kmh.values, rcond=None)[0]
        return a + b * test.L + c * test.cad
    if method == "imp_fit_prior_cad":  # 충격은 이 사람 a, b + 걸음 수 기울기는 다른 사람들 값
        y = cal.kmh - bLc[1] * cal.cad
        b, a = np.polyfit(cal.L, y, 1)
        return a + b * test.L + bLc[1] * test.cad
    raise ValueError(method)


PLANS = [(1, "imp_1pt"), (1, "impcad_1pt"), (2, "imp_fit"), (2, "imp_fit_prior_cad"), (3, "imp_fit"), (3, "imp_fit_prior_cad"), (3, "impcad_fit")]


def evaluate(s):
    rows = []
    groups = {g: d for g, d in s.groupby("grp")}
    for g, d in groups.items():
        speeds = sorted(d.mph.unique())
        if len(speeds) < 2:
            continue
        others = s[s.grp != g]
        prior = pooled_slopes(others[others.grp.isin([k for k, v in groups.items() if v.mph.nunique() >= 2])])
        for k, method in PLANS:
            if len(speeds) <= k:
                continue
            for cs in itertools.combinations(speeds, k):
                cal, test = d[d.mph.isin(cs)], d[~d.mph.isin(cs)]
                pred = predict(cal, test, method, prior)
                for (_, t), p in zip(test.iterrows(), pred):
                    inside = min(cs) <= t.mph <= max(cs) if k > 1 else False
                    rows.append(dict(grp=g, k=k, method=method, cal=",".join(map(str, cs)), mph=t.mph,
                                     err=p - t.kmh, inside=inside, dist=min(abs(t.mph - c) for c in cs) * MPH))
    return pd.DataFrame(rows)


def no_calibration(s):
    """보정 없이: 다른 사람들로 맞춘 한 공식(절편 포함)으로 이 사람을 맞힘"""
    rows = []
    for g, d in s.groupby("grp"):
        o = s[s.grp != g]
        X = np.c_[np.ones(len(o)), o.L, o.cad]
        a, b, c = np.linalg.lstsq(X, o.kmh.values, rcond=None)[0]
        for _, t in d.iterrows():
            rows.append(dict(grp=g, err=a + b * t.L + c * t.cad - t.kmh))
    return pd.DataFrame(rows)


def summ(e):
    a = e.err.abs()
    return dict(n=len(a), groups=e.grp.nunique(), median=a.median(), p90=a.quantile(0.9),
                le03=(a <= 0.3).mean(), le05=(a <= 0.5).mean(), bias=e.err.mean())


def main():
    win = file_points()
    print("창", len(win), "q≥0.3 비율", round((win.q >= 0.3).mean(), 3), "fs", sorted(win.fs.unique()))
    s = session_points(win)
    print("세션 점", len(s), "묶음", s.grp.nunique(), "사람", s.subj.nunique())
    s.to_csv(DB / "sessions.csv", index=False)

    # 묶음 안에서 빠를수록 충격이 센가 / 걸음 수가 느는가
    tr = []
    for g, d in s.groupby("grp"):
        if d.mph.nunique() >= 2:
            tr.append(dict(grp=g, bL=np.polyfit(d.kmh, d.L, 1)[0], bc=np.polyfit(d.kmh, d.cad, 1)[0]))
    tr = pd.DataFrame(tr)
    print(f"속도↑ → 충격↑ 묶음 {(tr.bL > 0).mean():.0%} / 걸음 수↑ {(tr.bc > 0).mean():.0%} (묶음 {len(tr)}), "
          f"걸음 수 기울기 중앙값 {tr.bc.median():.1f} spm per km/h")

    ev = evaluate(s)
    ev.to_csv(DB / "eval.csv", index=False)
    rows = []
    nc = no_calibration(s[s.grp.isin(ev.grp.unique())])
    rows.append(dict(plan="보정 없음 (다른 사람 공식)", **summ(nc)))
    for (k, m), e in ev.groupby(["k", "method"], sort=False):
        rows.append(dict(plan=f"{k}개 {m}", **summ(e)))
        if k >= 2:
            rows.append(dict(plan=f"{k}개 {m} · 보정 범위 안", **summ(e[e.inside])))
            rows.append(dict(plan=f"{k}개 {m} · 범위 밖", **summ(e[~e.inside])))
    res = pd.DataFrame(rows)
    pd.set_option("display.width", 200)
    print(res.round(3).to_string(index=False))
    res.to_csv(DB / "summary.csv", index=False)


if __name__ == "__main__":
    main()
