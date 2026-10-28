"""계획 0008: 에어팟으로 공중 시간을 잴 수 있는지 (제자리 뛰기, 아이폰을 가슴에 고정한 값을 정답으로).

실행: .venv/bin/python speedlab/exp/airpods_flight.py [기록 id]   (Windows: .venv\\Scripts\\python)
입력: data/runs/<id>/ (iphone_acc, airpods_acc + airpods_grav, meta.json segments)
출력: 콘솔 표 + docs/speed/img/0008_<id>.png

공중 = 전체 가속도 크기(두 기기 모두 LP_HZ 저역 필터)가 기준(THR_G)보다 작은 구간, GAP보다 짧은 틈은 합침.
기준을 넘는 순간은 샘플 사이를 선형 보간(에어팟은 약 50Hz라 샘플 간격 20ms보다 세밀하게 잡으려고).
"""
import json
import sys
from pathlib import Path

import numpy as np
import pandas as pd

ROOT = Path(__file__).resolve().parents[2]
G = 9.80665
THRS = (0.2, 0.3, 0.4)   # 공중 기준(g). 결과가 기준에 얼마나 흔들리는지 같이 봄
THR_G = 0.3
MIN_F, MAX_F = 0.1, 0.6   # 공중 시간으로 인정할 길이(s). 착지 직후 폰만 잠깐(약 60ms) 0.2g로 떨어지는 흔들림을 빼려고 0.1
TRIM = 3.0               # 구간 앞뒤로 버릴 시간(s): 높이 바꾸는 중
GAP = 0.06               # 이보다 짧은 틈으로 나뉜 공중은 하나로 합침(공중에서 손에 쥔 폰이 떨려 기준을 넘나듦)
LP_HZ = 10.0             # 두 기기 모두 이 대역으로 맞춤(에어팟 약 50Hz는 그 위를 못 담음)


def load(run, name):
    d = pd.read_csv(run / f'{name}.csv')
    return d.t.values, d[['x', 'y', 'z']].values


def lowpass(t, m):
    from scipy.signal import butter, sosfiltfilt
    fs = (len(t) - 1) / (t[-1] - t[0])
    return sosfiltfilt(butter(2, LP_HZ, 'lp', fs=fs, output='sos'), m)


def magnitude_g(run):
    tp, ap = load(run, 'iphone_acc')
    ta, aa = load(run, 'airpods_acc')
    tg, ag = load(run, 'airpods_grav')
    assert np.allclose(ta, tg), 'airpods_acc와 airpods_grav 시각이 다름'
    return {'iphone': (tp, lowpass(tp, np.linalg.norm(ap, axis=1) / G)),
            'airpods': (ta, lowpass(ta, np.linalg.norm(aa + ag, axis=1) / G))}


def flights(t, m, thr):
    """m < thr 인 연속 구간 → (시작, 끝) 배열. 경계는 선형 보간."""
    below = m < thr
    d = np.diff(below.astype(int))
    starts, ends = np.where(d == 1)[0], np.where(d == -1)[0]
    if below[0]:
        ends = ends[ends > starts[0]] if len(starts) else ends[1:]
    out = []
    for s in starts:
        e = ends[ends > s]
        if not len(e):
            break
        e = e[0]
        t0 = t[s] + (m[s] - thr) / (m[s] - m[s + 1]) * (t[s + 1] - t[s])
        t1 = t[e] + (thr - m[e]) / (m[e + 1] - m[e]) * (t[e + 1] - t[e])
        if out and t0 - out[-1][1] < GAP:
            out[-1] = (out[-1][0], t1)
        else:
            out.append((t0, t1))
    out = [f for f in out if MIN_F <= f[1] - f[0] <= MAX_F]
    return np.array(out).reshape(-1, 2)


def clock_offset(sig, segs):
    """점프 구간(sync)에서 두 기기 가속도 크기의 상호상관 → 에어팟 시각에 더할 값(s)."""
    fs = 200.0
    best = []
    for s in segs:
        if s['set'] != 'sync':
            continue
        tu = np.arange(s['start_s'], s['end_s'], 1 / fs)
        a = np.interp(tu, *sig['iphone']) - 1
        b = np.interp(tu, *sig['airpods']) - 1
        lags = np.arange(-int(0.3 * fs), int(0.3 * fs) + 1)
        cc = [np.dot(a[max(0, -k):len(a) - max(0, k)], b[max(0, k):len(b) - max(0, -k)]) for k in lags]
        best.append(-lags[int(np.argmax(cc))] / fs)
    return best


def pair(fp, fa, tol=0.1):
    """시작 시각이 tol 안에서 가장 가까운 공중끼리 짝."""
    out = []
    for s, e in fa:
        if not len(fp):
            break
        i = np.argmin(np.abs(fp[:, 0] - s))
        if abs(fp[i, 0] - s) < tol:
            out.append((fp[i, 1] - fp[i, 0], e - s, fp[i, 0] - s))
    return np.array(out).reshape(-1, 3)


def main():
    rid = sys.argv[1] if len(sys.argv) > 1 else sorted(p.name for p in (ROOT / 'data/runs').iterdir())[-1]
    run = ROOT / 'data/runs' / rid
    segs = json.load(open(run / 'meta.json', encoding='utf-8'))['segments']
    sig = magnitude_g(run)
    ta, ma = sig['airpods']
    fs_a = (len(ta) - 1) / (ta[-1] - ta[0])
    print(f'기록 {rid}  아이폰 {(len(sig["iphone"][0]) - 1) / np.ptp(sig["iphone"][0]):.1f} Hz, 에어팟 {fs_a:.1f} Hz')

    off = clock_offset(sig, segs)
    print('시계 차이(점프 구간별, 에어팟에 더할 값):', ', '.join(f'{o * 1000:+.0f} ms' for o in off))
    print('(보정하지 않음: 공중 길이는 시계 차이와 무관, 짝짓기만 tol 안에서)\n')

    hops = [s for s in segs if s['set'] == 'hop']
    for thr in THRS:
        fl = {k: flights(*v, thr) for k, v in sig.items()}
        print(f'기준 {thr} g')
        print(f'  {"구간":<8} {"아이폰":>7} {"에어팟":>7} {"짝":>4} {"짝 아이폰":>9} {"짝 에어팟":>9} {"짝 차이 평균±SD":>15} {"공중/분":>7}')
        for s in hops:
            lo, hi = s['start_s'] + TRIM, s['end_s'] - TRIM
            fp = fl['iphone'][(fl['iphone'][:, 0] >= lo) & (fl['iphone'][:, 0] < hi)]
            fa = fl['airpods'][(fl['airpods'][:, 0] >= lo) & (fl['airpods'][:, 0] < hi)]
            dp, da = (fp[:, 1] - fp[:, 0]) * 1000, (fa[:, 1] - fa[:, 0]) * 1000
            pr = pair(fp, fa)
            dd = (pr[:, 1] - pr[:, 0]) * 1000
            spm = len(fp) / (hi - lo) * 60
            print(f'  {s["label"]:<8} {len(dp):>5}개 {len(da):>5}개 {len(pr):>4} {pr[:, 0].mean() * 1000:>7.0f}ms'
                  f' {pr[:, 1].mean() * 1000:>7.0f}ms {dd.mean():>+7.0f}±{dd.std():<3.0f}ms {spm:>7.0f}')
        print()

    plot(rid, sig, segs, flights(*sig['iphone'], THR_G), flights(*sig['airpods'], THR_G))


def plot(rid, sig, segs, fp, fa):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    from matplotlib import font_manager
    have = {f.name for f in font_manager.fontManager.ttflist}
    plt.rcParams['font.family'] = next((f for f in ('AppleGothic', 'Malgun Gothic') if f in have), 'sans-serif')
    hop = [s for s in segs if s['set'] == 'hop'][1]
    t0 = hop['start_s'] + 10
    fig, ax = plt.subplots(figsize=(11, 4))
    for k, c in (('iphone', '#1f6feb'), ('airpods', '#d1495b')):
        t, m = sig[k]
        w = (t >= t0) & (t < t0 + 4)
        ax.plot(t[w], m[w], color=c, lw=1.2, marker='.' if k == 'airpods' else None, ms=3, label=k)
    for f, y, c in ((fp, -0.15, '#1f6feb'), (fa, -0.3, '#d1495b')):
        for s, e in f[(f[:, 0] >= t0) & (f[:, 0] < t0 + 4)]:
            ax.plot([s, e], [y, y], color=c, lw=4)
    ax.axhline(THR_G, color='gray', ls='--', lw=0.8)
    ax.set_xlabel('시간 (s)')
    ax.set_ylabel('전체 가속도 (g)')
    ax.set_title(f'{rid} {hop["label"]} 4초: 막대 = 공중({THR_G} g 아래)')
    ax.legend(loc='upper right')
    out = ROOT / f'docs/speed/img/0008_{rid}.png'
    out.parent.mkdir(parents=True, exist_ok=True)
    fig.tight_layout()
    fig.savefig(out, dpi=120)
    print('그림:', out.relative_to(ROOT))


if __name__ == '__main__':
    main()
