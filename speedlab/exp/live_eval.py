"""계획 0010: 폰 안 실시간 속도(SpeedRecorder "보정 1분 + 자유 달리기") 오차.

실행: .venv/bin/python speedlab/exp/live_eval.py <기록 id>   (Windows: .venv\\Scripts\\python)
입력: data/runs/<id>/phone/ (estimate.csv = 폰 안 추정 0.5초마다, mark.csv = ±로 남긴 실제 속도, stage.csv)
출력: 콘솔 표 + docs/speed/img/0010_<id>.png

- ±를 연달아 누른 것(MERGE초 안)은 마지막 값 하나로 봄
- 실제 속도가 바뀐 뒤 SKIP초(러닝머신 가감속·추정 따라오는 시간)는 버림 (계획 0010 판정)
- kmh = 에어팟 포함 추정, kmh_phone = 폰만(걷기로 판단된 동안은 -1 → 그 칸은 비교에서 뺌)
"""
import sys
from pathlib import Path

import numpy as np
import pandas as pd

ROOT = Path(__file__).resolve().parents[2]
MERGE = 1.5
SKIP = 10.0
STATE = {0: '준비', 1: '걷기', 2: '달리기'}


def true_speed(mark, t_end):
    """±로 남긴 값 → (시작, 끝, km/h) 구간."""
    keep = []
    for t, v in zip(mark.t, mark.true_kmh):
        if keep and t - keep[-1][0] < MERGE:
            keep[-1] = (t, v)  # 연달아 누름: 시각도 마지막 누른 때로
        else:
            keep.append((t, v))
    return [(t, keep[i + 1][0] if i + 1 < len(keep) else t_end, v) for i, (t, v) in enumerate(keep)]


def main():
    rid = sys.argv[1]
    d = ROOT / 'data/runs' / rid / 'phone'
    est = pd.read_csv(d / 'estimate.csv')
    mark = pd.read_csv(d / 'mark.csv')
    stage = pd.read_csv(d / 'stage.csv')
    t0 = stage.t.iloc[0]
    t_end = stage.t.iloc[-1]
    segs = true_speed(mark, t_end)

    rows = []
    print(f'기록 {rid}  자유 달리기 {(t_end - mark.t.iloc[0]):.0f}초, 실제 속도 구간 {len(segs)}개 (바뀐 뒤 {SKIP:.0f}초 버림)\n')
    print(f'  {"시작":>5} {"길이":>4} {"실제":>4} {"쓴 칸":>5} {"상태":>4} {"추정":>6} {"오차":>6} {"폰만":>6} {"오차":>6}')
    for s, e, v in segs:
        w = est[(est.t >= s + SKIP) & (est.t < e)]
        if len(w) == 0:
            print(f'  {s - t0:>5.0f} {e - s:>3.0f}s {v:>4.0f}   (10초보다 짧아 뺌)')
            continue
        st = w.state.round().mode().iloc[0]
        ph = w.kmh_phone[w.kmh_phone >= 0]
        r = {'v': v, 'n': len(w), 'state': STATE.get(int(st), '?'), 'kmh': w.kmh.mean(), 'err': w.kmh.mean() - v,
             'abs_tick': (w.kmh - v).abs().mean(),
             'phone': ph.mean() if len(ph) else np.nan, 'perr': ph.mean() - v if len(ph) else np.nan,
             'pabs_tick': (ph - v).abs().mean() if len(ph) else np.nan, 'start': s - t0}
        rows.append(r)
        print(f'  {r["start"]:>5.0f} {e - s:>3.0f}s {v:>4.0f} {len(w):>5} {r["state"]:>4} {r["kmh"]:>6.2f} {r["err"]:>+6.2f}'
              f' {r["phone"]:>6.2f} {r["perr"]:>+6.2f}')

    df = pd.DataFrame(rows)
    print('\n구간 평균 오차의 절댓값 평균 (칸 하나하나 오차 절댓값 평균)')
    for name, q in (('전체', df), ('달리기 구간', df[df.state == '달리기']), ('걷기 구간', df[df.state == '걷기'])):
        if len(q) == 0:
            continue
        print(f'  {name:<7} {len(q)}구간  에어팟 포함 {q.err.abs().mean():.2f} ({q.abs_tick.mean():.2f})'
              + (f'  폰만 {q.perr.abs().mean():.2f} ({q.pabs_tick.mean():.2f})' if q.perr.notna().any() else ''))
    plot(rid, est, segs, t0)


def plot(rid, est, segs, t0):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    from matplotlib import font_manager
    have = {f.name for f in font_manager.fontManager.ttflist}
    plt.rcParams['font.family'] = next((f for f in ('AppleGothic', 'Malgun Gothic') if f in have), 'sans-serif')
    plt.rcParams['axes.unicode_minus'] = False
    fig, ax = plt.subplots(figsize=(11, 4))
    t = est.t - t0
    ax.plot(t, est.kmh.where(est.kmh >= 0), color='#d1495b', lw=1.2, label='추정(에어팟 포함)')
    ax.plot(t, est.kmh_phone.where(est.kmh_phone >= 0), color='#1f6feb', lw=0.8, alpha=0.7, label='폰만')
    for s, e, v in segs:
        ax.plot([s - t0, e - t0], [v, v], color='black', lw=2)
        ax.axvspan(s - t0, s - t0 + SKIP, color='gray', alpha=0.12, lw=0)
    ax.plot([], [], color='black', lw=2, label='실제(±)')
    ax.set_xlabel('시작부터 (s)')
    ax.set_ylabel('km/h')
    ax.set_title(f'{rid} 폰 안 실시간 속도 (회색 = 바뀐 뒤 {SKIP:.0f}초, 평가에서 뺌)')
    ax.legend(loc='lower left')
    fig.tight_layout()
    out = ROOT / f'docs/speed/img/0010_{rid}.png'
    fig.savefig(out, dpi=120)
    print('그림:', out.relative_to(ROOT))


if __name__ == '__main__':
    main()
