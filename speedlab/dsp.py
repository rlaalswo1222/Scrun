"""신호 처리: 균일 샘플링 → 대역통과 → 주축 투영 → 자기상관으로 케이던스 추정."""
import numpy as np
import pandas as pd
from scipy import signal

# 케이던스 탐색 범위(분당 걸음 수): 느린 걷기(70~)부터 달리기(~220)까지.
# 범위가 2배를 넘으므로 '두 걸음 주기'를 한 걸음으로 착각할 수 있다 -> cadence_acf에서 절반 주기를 다시 확인
CAD_MIN, CAD_MAX = 70.0, 220.0


def _interp_nan(tt, t, v):
    ok = np.isfinite(v)
    return np.interp(tt, t[ok], v[ok])


def to_uniform(track, fs=None):
    """불규칙한 타임스탬프를 균일 간격으로 보간한다. 반환: (t, x[샘플, 축], fs)

    fs를 주면 그 주파수로 낮춘다. 에어팟처럼 샘플링이 낮은 센서를 흉내 낼 때 쓴다.
    """
    t = track["t"].to_numpy(float)
    vals = track.drop(columns="t").to_numpy(float)
    native = 1.0 / np.median(np.diff(t))
    tt = np.arange(t[0], t[-1], 1.0 / native)
    x = np.column_stack([_interp_nan(tt, t, v) for v in vals.T])
    if fs is None or fs >= native:
        return tt, x, native
    # 다운샘플 전에 저역통과로 앨리어싱 방지
    sos = signal.butter(4, 0.4 * fs, btype="low", fs=native, output="sos")
    x = signal.sosfiltfilt(sos, x, axis=0)
    t_low = np.arange(tt[0], tt[-1], 1.0 / fs)
    x = np.column_stack([np.interp(t_low, tt, v) for v in x.T])
    return t_low, x, float(fs)


def bandpass(x, fs, lo=0.8, hi=12.0):
    hi = min(hi, 0.45 * fs)
    sos = signal.butter(4, [lo, hi], btype="band", fs=fs, output="sos")
    return signal.sosfiltfilt(sos, x, axis=0)


def main_axis(x):
    """여러 축 신호를 진동이 가장 큰 방향 하나로 투영한다(PCA 1번 축).

    폰을 어떤 방향으로 거치하든 같은 방식으로 분석하기 위함.
    """
    if x.shape[1] == 1:
        return x[:, 0]
    xc = x - x.mean(axis=0)
    _, vecs = np.linalg.eigh(np.cov(xc.T))
    return xc @ vecs[:, -1]


def impact_envelope(x, fs, hp=8.0, lp=5.0):
    """러닝머신 거치대 위 폰: 발이 벨트를 칠 때의 짧은 충격(8Hz 이상 떨림)의 세기 변화.

    거치대 폰의 낮은 주파수 흔들림에는 모터·벨트 떨림(약 11Hz)과 거치대 출렁임이 섞여 걸음 리듬이 묻힌다.
    높은 주파수만 남겨 세기(포락선)를 구하면 '쿵' 할 때마다 한 번씩 솟아 걸음 주기가 깨끗하게 나온다.
    """
    sos = signal.butter(4, hp, btype="high", fs=fs, output="sos")
    h = signal.sosfiltfilt(sos, x, axis=0)
    e = np.sqrt((h ** 2).sum(axis=1))
    sos = signal.butter(4, lp, btype="low", fs=fs, output="sos")
    return signal.sosfiltfilt(sos, e)


def prepare(track, fs=None, mode="motion"):
    """기록 → (t, 1차원 신호, fs). 이후 분석은 모두 이 신호로 한다.

    mode="motion": 몸에 붙은 센서(에어팟, 주머니 폰, 카메라 머리 위치) — 움직임 자체
    mode="impact": 러닝머신 거치대 위 폰 — 발 충격의 세기 (샘플링 25Hz 이상일 때만)
    """
    t, x, fs = to_uniform(track, fs)
    if mode == "impact" and fs >= 25:
        return t, impact_envelope(x, fs, hp=min(8.0, 0.3 * fs)), fs
    return t, main_axis(bandpass(x, fs)), fs


def cadence_acf(s, fs):
    """자기상관이 가장 큰 지연 = 한 걸음 주기. 반환: (케이던스 spm, 신뢰도 0~1)."""
    s = s - s.mean()
    n = len(s)
    r = signal.correlate(s, s, mode="full", method="fft")[n - 1:]
    r = r / (n - np.arange(n))  # 지연이 길수록 겹치는 샘플 수가 줄어드는 것 보정
    r = r / r[0]
    lo = int(np.floor(fs * 60 / CAD_MAX))
    hi = int(np.ceil(fs * 60 / CAD_MIN))
    # 걸음 주기의 2배·3배(두 걸음, 세 걸음) 지연에도 봉우리가 생긴다. 범위가 2배를 넘어서 그쪽이 가장 높을 수 있으므로
    # 가장 높은 봉우리의 1/3, 1/2 지점에도 뚜렷한 봉우리가 있으면 그게 한 걸음 (왼·오른발 차이가 커도)
    k = lo + int(np.argmax(r[lo:hi + 1]))
    for m in (3, 2):
        c = k / m
        if c < lo:
            continue
        a_, b_ = max(lo, int(c) - 2), min(hi, int(c) + 3)
        kh = a_ + int(np.argmax(r[a_:b_ + 1]))
        if r[kh] > 0.1:  # 한 걸음 주기라면 그 절반·1/3 지연은 반대 위상(음수)이라 양수 봉우리가 뜨지 않는다
            k = kh
            break
    d = 0.0
    if lo < k < hi:  # 포물선 보간으로 샘플 간격보다 정밀하게
        y0, y1, y2 = r[k - 1], r[k], r[k + 1]
        denom = y0 - 2 * y1 + y2
        if denom != 0:
            d = float(np.clip(0.5 * (y0 - y2) / denom, -0.5, 0.5))
    return 60.0 / ((k + d) / fs), float(r[k])


def cadence_timeline(t, s, fs, win_s=8.0, hop_s=1.0):
    """슬라이딩 창으로 시간에 따른 케이던스 변화를 구한다."""
    n, hop = int(win_s * fs), int(hop_s * fs)
    rows = []
    for i in range(0, len(s) - n + 1, hop):
        cad, q = cadence_acf(s[i:i + n], fs)
        rows.append((t[i + n // 2], cad, q))
    return pd.DataFrame(rows, columns=["t", "cadence_spm", "quality"])


def segment_features(t, s, fs, segments, trim_s=10.0):
    """구간마다 특징을 계산한다: 케이던스, 신뢰도, 진폭(RMS), 충격(상위 5% 크기)."""
    rows = []
    for _, seg in segments.iterrows():
        a, b = seg.start_s + trim_s, seg.end_s
        m = (t >= a) & (t < b)
        if m.sum() < 10 * fs:
            raise ValueError(f"구간 {a:.0f}~{b:.0f}s: 기록 범위 밖이거나 10초보다 짧습니다")
        x = s[m]
        cad, q = cadence_acf(x, fs)
        rows.append({**seg.to_dict(), "cadence_spm": cad, "quality": q,
                     "amp_rms": float(np.sqrt(np.mean(x ** 2))),
                     "impact_p95": float(np.percentile(np.abs(x), 95))})
    return pd.DataFrame(rows, index=segments.index)
