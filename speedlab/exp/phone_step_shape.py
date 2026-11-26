"""폰(계기판 위) 420 Hz 기록에서 걸음마다 착지에 맞춰 평균 낸 걸음 모양이 속도에 따라 바뀌는지(발 떼기 흔적 찾기)."""
import sys
from pathlib import Path
import numpy as np, pandas as pd
from scipy.signal import butter, sosfiltfilt, find_peaks
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
plt.rcParams["font.family"] = "Malgun Gothic"
ROOT = Path(r"C:/Users/rlaal/Desktop/Capstone1")
OUT = ROOT / "docs/speed/img"
FS = 400.0

def segments(ev):
    segs, cur = [], None
    for r in ev.itertuples():
        if r.event in ("guide_set", "calib_ready"): cur = float(r.value)
        if r.event in ("guide_start", "calib_start"): start = r.t
        if r.event in ("guide_end", "calib_done") and cur is not None: segs.append((start + 3, r.t, cur))
    return segs

def analyze(rid, who):
    d = ROOT / "data/speedlab/SpeedLab" / rid
    acc = pd.read_csv(d / "acc.csv").drop_duplicates("t").sort_values("t")
    ev = pd.read_csv(d / "events.csv")
    t = np.arange(acc.t.iloc[0], acc.t.iloc[-1], 1 / FS)
    m = np.sqrt(sum(np.interp(t, acc.t, acc[c]) ** 2 for c in "xyz"))
    hp = sosfiltfilt(butter(4, 8, "high", fs=FS, output="sos"), m)
    env = sosfiltfilt(butter(4, 30, "low", fs=FS, output="sos"), np.abs(hp))
    pre, post = int(0.05 * FS), int(0.40 * FS)
    lag = np.arange(-pre, post) / FS * 1000
    shapes, rows = {}, []
    for a, b, v in segments(ev):
        if v < 6.5: continue   # 달리기만
        i0, i1 = np.searchsorted(t, a), np.searchsorted(t, b)
        e = env[i0:i1]
        if len(e) < FS * 5: continue
        pk, _ = find_peaks(e, distance=int(0.27 * FS), height=np.percentile(e, 80))
        W = np.array([e[p - pre:p + post] for p in pk if p - pre >= 0 and p + post <= len(e)])
        if len(W) < 10: continue
        W = W / W[:, pre][:, None]                       # 착지 높이로 나눔(세기 말고 모양만)
        period = np.median(np.diff(pk)) / FS * 1000
        # 걸음마다: 착지 뒤 120 ms–(걸음 주기 − 40 ms) 안 가장 큰 봉우리 시각(발 떼기 흔적 후보)
        lo = pre + int(0.12 * FS)
        feats = []
        for w in W:
            hi = min(len(w), pre + int((period - 40) / 1000 * FS))
            if hi > lo: feats.append((np.argmax(w[lo:hi]) + lo - pre) / FS * 1000)
        shapes.setdefault(v, []).append(W.mean(0))
        # 착지 뒤 울림이 반으로 줄어드는 시간
        mw = W.mean(0); half = pre + np.argmax(mw[pre:] < 0.5)
        rows.append(dict(who=who, v=v, n=len(W), period=period, sec_ms=np.median(feats), sec_sd=np.std(feats),
                         half_ms=(half - pre) / FS * 1000, late=mw[pre + int(0.15 * FS):pre + int(0.3 * FS)].mean()))
    return lag, {v: np.mean(s, 0) for v, s in shapes.items()}, pd.DataFrame(rows)

fig, axs = plt.subplots(1, 2, figsize=(13, 4.5), sharey=True)
allrows = []
for ax, (rid, who) in zip(axs, [("20261029-014932", "민재(M)"), ("20261029-022017", "상준(B)")]):
    lag, shp, df = analyze(rid, who)
    allrows.append(df)
    cm = plt.get_cmap("viridis")
    vs = sorted(shp)
    for i, v in enumerate(vs):
        ax.plot(lag, shp[v], color=cm(i / max(1, len(vs) - 1)), label=f"{v:g} km/h")
    ax.set_title(f"{who} 10/29 · 착지에 맞춘 평균 걸음 모양(폰, 착지 높이 = 1)")
    ax.set_xlabel("착지 뒤 시간(ms)"); ax.axvline(0, color="#999", lw=0.5); ax.legend(fontsize=8); ax.grid(alpha=0.3)
axs[0].set_ylabel("떨림 크기(상대)")
fig.tight_layout(); fig.savefig(OUT / "phone_step_shape_20261029.png", dpi=110)
df = pd.concat(allrows)
# 같은 속도 여러 구간은 평균
g = df.groupby(["who", "v"]).agg(n=("n", "sum"), period=("period", "mean"), sec_ms=("sec_ms", "mean"), sec_sd=("sec_sd", "mean"),
                                 half_ms=("half_ms", "mean"), late=("late", "mean")).reset_index()
print(g.round(2).to_string(index=False))
for who, gg in g.groupby("who"):
    if len(gg) < 3: continue
    for col in ("sec_ms", "half_ms", "late"):
        s, c = np.polyfit(gg.v, gg[col], 1)
        r = np.corrcoef(gg.v, gg[col])[0, 1]
        extra = f", 걸음마다 흔들림 {gg.sec_sd.mean():.1f} ms → km/h로 {gg.sec_sd.mean()/abs(s):.1f}" if col == "sec_ms" else ""
        print(f"{who} {col}: 1 km/h당 {s:+.3f}, 상관 {r:+.2f}{extra}")
