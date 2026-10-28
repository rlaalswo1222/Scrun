"""계획 0002: 달리는 중 모터 진동 줄 살리기 (평균 발소리 빼기 / 빗살 필터).

실행: .venv\\Scripts\\python speedlab\\exp\\motor_tsa.py
입력: data/speedlab/SpeedLab/ (10/28 기록), 출력: docs/speed/img/0002_*.png + 콘솔 표
"""
import os
import numpy as np
import pandas as pd
from scipy.signal import welch, butter, sosfiltfilt, find_peaks

BASE = 'data/speedlab/SpeedLab/20261028-'
OUT = 'docs/speed/img/'
FS = 420.0
K = 2.80  # Hz per km/h (벨트만 025024 실측: 8→22.38, 10→28.00)

RUN = [('030843', 704.0, 724.0, 7), ('030843', 763.4, 783.4, 8),
       ('030843', 822.6, 842.6, 9), ('030843', 907.7, 927.7, 10)]
BELT = [('025024', 177.9, 197.9, 8), ('025024', 222.2, 242.2, 10)]

_cache = {}


def load(rec, t0, t1):
    if rec not in _cache:
        _cache[rec] = pd.read_csv(BASE + rec + '/acc.csv')
    d = _cache[rec]
    s = d[(d.t >= t0) & (d.t < t1)]
    tu = np.arange(t0, t1, 1 / FS)
    xyz = np.stack([np.interp(tu, s.t.values, s[c].values) for c in 'xyz'])
    mag = np.sqrt((xyz ** 2).sum(0))
    sig = {'x': xyz[0], 'y': xyz[1], 'z': xyz[2], 'mag': mag}
    return {k: v - v.mean() for k, v in sig.items()}


def detect_steps(x):
    """8Hz 이상 포락선 봉우리 = 착지 시점(샘플 번호, 실수)."""
    hp = sosfiltfilt(butter(4, 8, 'hp', fs=FS, output='sos'), x)
    env = sosfiltfilt(butter(2, 6, 'lp', fs=FS, output='sos'), np.abs(hp))
    pk, _ = find_peaks(env, distance=int(0.22 * FS), height=np.median(env) * 1.5)
    return pk.astype(float)


def warp_segments(x, starts, n):
    """starts[i]부터 starts[i+1]까지를 n점으로 늘이거나 줄임 (걸음 하나 = n점)."""
    t = np.arange(len(x))
    return np.stack([np.interp(np.linspace(a, b, n, endpoint=False), t, x)
                     for a, b in zip(starts[:-1], starts[1:])])


def refine(x, starts, n, iters=3):
    """각 걸음을 평균 파형에 맞대어 시작 위치를 소수 샘플까지 보정."""
    starts = starts.copy()
    lags = np.arange(-n // 8, n // 8 + 1)
    for _ in range(iters):
        seg = warp_segments(x, starts, n)
        tmpl = seg.mean(0)
        for i in range(len(seg)):
            c = np.array([np.dot(np.roll(seg[i], -l), tmpl) for l in lags])
            j = int(np.argmax(c))
            frac = 0.0
            if 0 < j < len(c) - 1:
                den = c[j - 1] - 2 * c[j] + c[j + 1]
                frac = 0.5 * (c[j - 1] - c[j + 1]) / den if den != 0 else 0.0
            starts[i] += (lags[j] + frac) * (starts[i + 1] - starts[i]) / n
    return starts


def tsa_subtract(x, peaks):
    """왼발·오른발 따로 평균 발소리를 만들고, 걸음마다 크기 맞춰 뺀다."""
    per = np.median(np.diff(peaks))
    starts = peaks - 0.3 * per
    starts = starts[starts >= 0]
    starts = starts[starts < len(x) - 1]
    n = int(round(per))
    starts = refine(x, starts, n)
    seg = warp_segments(x, starts, n)
    out = x.copy()
    t = np.arange(len(x))
    for foot in (0, 1):
        idx = np.arange(foot, len(seg), 2)
        tmpl = seg[idx].mean(0)
        for i in idx:
            a, b = starts[i], starts[i + 1]
            m = (t >= a) & (t < b)
            local = np.interp(t[m], np.linspace(a, b, n, endpoint=False), tmpl)
            g = np.dot(x[m], local) / max(np.dot(local, local), 1e-12)
            out[m] = x[m] - g * local
    m = (t >= starts[0]) & (t < starts[-1])
    return out, m, starts


def comb_notch(x, f_step, width=0.15, fmax=210):
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / FS)
    for h in range(1, int(fmax / f_step) + 1):
        X[np.abs(f - h * f_step) < width] = 0
    return np.fft.irfft(X, len(x))


def psd(x):
    return welch(x, fs=FS, nperseg=int(FS * 8))


def line_snr(f, p, f0, half=0.6, around=4.0):
    """f0±half 안 최대 봉우리 ÷ 주변(±around, 안쪽 제외) 중앙값, 봉우리 위치."""
    m = np.abs(f - f0) < half
    loc = (np.abs(f - f0) < around) & ~m
    i = np.argmax(p[m])
    return p[m][i] / np.median(p[loc]), f[m][i]


def main():
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    os.makedirs(OUT, exist_ok=True)

    # 0) 벨트만: 축별 모터 줄 세기, k 재확인
    print('== 벨트만 (모터 줄 위치·세기)')
    belt_p = {}
    for rec, t0, t1, v in BELT:
        s = load(rec, t0, t1)
        for ax in s:
            f, p = psd(s[ax])
            snr, fpk = line_snr(f, p, K * v)
            belt_p[(v, ax)] = (f, p)
            print(f'  {v}km/h {ax:>3}: 봉우리 {fpk:.2f}Hz (k={fpk / v:.3f}), 주변 대비 {snr:.0f}배')

    # 1) 달림: 4가지 방법
    methods = ['raw', 'tsa', 'comb', 'tsa+comb']
    rows = []
    spectra = {}
    for rec, t0, t1, v in RUN:
        s = load(rec, t0, t1)
        peaks = detect_steps(s['mag'])
        f_step = FS / np.median(np.diff(peaks))
        for ax in s:
            x = s[ax]
            tsa, valid, _ = tsa_subtract(x, peaks)
            tsa = tsa[valid]
            sig = {'raw': x[valid], 'tsa': tsa,
                   'comb': comb_notch(x[valid], f_step),
                   'tsa+comb': comb_notch(tsa, f_step)}
            for mth in methods:
                f, p = psd(sig[mth])
                spectra[(v, ax, mth)] = (f, p)
                for h in (1, 2, 3, 4):
                    if K * v * h > 205:
                        continue
                    snr, fpk = line_snr(f, p, K * v * h)
                    rows.append(dict(v=v, ax=ax, m=mth, h=h, snr=snr, fpk=fpk,
                                     rms=sig[mth].std(), cad=f_step * 60))
    df = pd.DataFrame(rows)

    print('\n== 달림: 걸음 박자')
    for v in sorted(df.v.unique()):
        print(f'  {v}km/h  {df[df.v == v].cad.iloc[0]:.1f} spm')

    print('\n== 달림: 기본 줄(h=1) 봉우리 ÷ 주변 바닥 (3배 이상이면 성공 기준)')
    t = df[df.h == 1].pivot_table(index=['ax', 'm'], columns='v', values='snr')
    print(t.round(1).to_string())
    print('\n== 봉우리 위치 (예측 2.82×v: 19.7 / 22.6 / 25.4 / 28.2)')
    t2 = df[df.h == 1].pivot_table(index=['ax', 'm'], columns='v', values='fpk')
    print(t2.round(2).to_string())
    print('\n== 배음별 최고 (축·방법 중 최대)')
    print(df.groupby(['v', 'h']).snr.max().unstack().round(1).to_string())
    print('\n== 처리 후 남은 세기(rms) ÷ 원 신호')
    r = df[df.h == 1].pivot_table(index=['ax', 'm'], columns='v', values='rms')
    print((r / r.xs('raw', level='m').reindex(r.index, level='ax').values).round(3).to_string())

    # 2) 주입 시험: 처리 과정이 순수한 줄을 지우지 않는지 (31.3Hz, 벨트만 10km/h 줄 세기)
    print('\n== 주입 시험 (가짜 줄 31.3Hz를 넣고 처리 후에도 남는지)')
    sb = load(*BELT[1][:3])
    fb, pb = psd(sb['mag'])
    amp = np.sqrt(2 * pb[np.abs(fb - K * 10) < 0.6].max() * (fb[1] - fb[0]))
    for rec, t0, t1, v in RUN:
        s = load(rec, t0, t1)
        x = s['mag'] + amp * np.sin(2 * np.pi * 31.3 * np.arange(len(s['mag'])) / FS)
        peaks = detect_steps(x)
        tsa, valid, _ = tsa_subtract(x, peaks)
        for name, y in (('raw', x[valid]), ('tsa', tsa[valid])):
            f, p = psd(y)
            snr, fpk = line_snr(f, p, 31.3, half=0.2)
            print(f'  {v}km/h {name:>4}: {fpk:.2f}Hz 주변 대비 {snr:.1f}배')

    # 3) 그림: 축 mag와 최고 축, 15에서 35Hz
    best_ax = t.xs('tsa+comb', level='m').mean(1).idxmax()
    for ax in sorted({'mag', best_ax}):
        fig, axs = plt.subplots(len(RUN), 1, figsize=(11, 2.6 * len(RUN)), sharex=True)
        for a, (_, _, _, v) in zip(axs, RUN):
            for mth, c in zip(methods, ['0.6', 'tab:red', 'tab:green', 'tab:purple']):
                f, p = spectra[(v, ax, mth)]
                a.semilogy(f, p, color=c, lw=.9, label=mth)
            if (v, ax) in belt_p:
                f, p = belt_p[(v, ax)]
                a.semilogy(f, p, color='tab:blue', lw=.9, label='belt only')
            a.axvline(K * v, color='b', ls=':', lw=1.2)
            a.set_ylabel(f'{v} km/h')
            a.set_xlim(12, 36)
        axs[0].legend(ncol=5, fontsize=8)
        axs[0].set_title(f'axis {ax}: running spectra, blue dotted = 2.82*v')
        axs[-1].set_xlabel('Hz')
        plt.tight_layout()
        plt.savefig(OUT + f'0002_spectra_{ax}.png', dpi=80)
        plt.close()
    print(f'\n그림: {OUT}0002_spectra_*.png (최고 축 {best_ax})')
    null_test()


def null_test():
    """비교군: 예측 자리 점수가 15에서 35Hz의 다른 자리(0.25Hz 간격) 점수 중 상위 몇 %인가.
    봉우리 숲이라 '3배 이상'만으론 아무 자리나 통과하므로, 이것을 최종 판정에 쓴다."""
    print('\n== 비교군 판정 (예측 자리 점수, 다른 자리들 대비 상위 %)')
    offs = np.arange(15.0, 35.01, 0.25)
    for rec, t0, t1, v in RUN:
        s = load(rec, t0, t1)
        pk = detect_steps(s['mag'])
        fst = FS / np.median(np.diff(pk))
        for ax in ('mag', 'x', 'z'):
            x = s[ax]
            tsa, valid, _ = tsa_subtract(x, pk)
            cells = []
            for name, y in (('raw', x[valid]), ('tsa', tsa[valid]),
                            ('tsa+comb', comb_notch(tsa[valid], fst))):
                f, p = psd(y)
                f0 = K * v
                sc, _ = line_snr(f, p, f0, half=0.3)
                null = np.array([line_snr(f, p, o, half=0.3)[0] for o in offs if abs(o - f0) > 0.8])
                cells.append(f'{name} {sc:5.1f}(상위 {100 - (null < sc).mean() * 100:3.0f}%)')
            print(f'  {v:>2}km/h {ax:>3}: ' + ' | '.join(cells))


if __name__ == '__main__':
    main()
