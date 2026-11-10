"""계획 0024 1번: 앱 보정 단계 판정·걷기 절편·누적 걸음을 10/29 기록으로 확인(PC 분석).

실행: .venv\\Scripts\\python speedlab/exp/live_stage_check.py
입력: 10/29 SpeedLab 세션(갤럭시 S22를 계기판 위에, 420 Hz 원본 acc.csv → 100 Hz로 맞춤, 에어팟 없음)
      M = 민재 20261029-014932(벨트만 2·4 km/h 사람은 옆 발판, 걷기 1→6→1, 달리기 검증 7–10)
      B = 고상준 20261029-022017(걷기 1→6→1, 보정 7–10, 달리기 검증)
방법: 원본을 fusion_live.SpeedEstimator에 실시간처럼 넣고, 안내 구간마다 앱과 같은 단계 재기(measure_stage).
      단계 시작 = 속도 안내 시각(true_kmh), 끝 = 구간 끝(guide_end). 앞 5초는 버림(앱과 같음)
주의: S22 기록이라 포락선 세기(걸음 없음 문턱)는 아이폰에서 다를 수 있음(가설) → 아이폰 책상 확인 때 다시 정함
"""
import sys
from pathlib import Path

import numpy as np
import pandas as pd

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'speedlab'))
import fusion_live as fl  # noqa: E402

SL = ROOT / 'data/speedlab/SpeedLab'
SESSIONS = {'M': '20261029-014932', 'B': '20261029-022017'}


def stages(session):
    """(v, t_begin, t_end, kind). kind: walk(안내 걷기)·run(안내 보정·검증)·still(벨트만, 사람은 옆 발판)"""
    ev = pd.read_csv(SL / session / 'events.csv')
    out, mode, last_true, st = [], None, None, None
    for _, r in ev.iterrows():
        if r.event == 'guide_mode':
            mode = int(r.value)
        elif r.event == 'true_kmh':
            last_true = r.t
        elif r.event == 'guide_start':
            st = (last_true if last_true is not None else r.t - fl.CAL_SKIP_S, float(r.value))
        elif r.event == 'guide_end' and st:
            out.append(dict(v=st[1], t_begin=st[0], t_end=r.t, kind='walk' if mode == 3 else 'run'))
            st = None
        elif r.event == 'calib_start' and session == SESSIONS['M'] and r.t < 200:
            st = (r.t - fl.CAL_SKIP_S, float(r.value))
        elif r.event == 'calib_done' and st and session == SESSIONS['M'] and r.t < 200:
            out.append(dict(v=st[1], t_begin=st[0], t_end=r.t, kind='still'))
            st = None
    return sorted(out, key=lambda s: s['t_end'])


def resample(session):
    a = pd.read_csv(SL / session / 'acc.csv')
    t = np.arange(a.t.iloc[0], a.t.iloc[-1], 1 / fl.FS)
    return t, np.interp(t, a.t, a.x), np.interp(t, a.t, a.y), np.interp(t, a.t, a.z)


def replay(session):
    """원본을 넣으며 구간 끝마다 단계 재기. 구간마다 결과·점·세기·걸음 수"""
    est = fl.SpeedEstimator()
    segs = stages(session)
    t, x, y, z = resample(session)
    k, rows, steps_at = 0, [], {}
    starts = sorted((s['t_begin'] + fl.CAL_SKIP_S, i) for i, s in enumerate(segs))
    j = 0
    snaps, last = [], None
    for i in range(len(t)):
        est.add_phone(t[i], x[i], y[i], z[i])
        if est.snap is not last:
            last = est.snap
            snaps.append((last['t'], last['state']))
        while j < len(starts) and t[i] >= starts[j][0]:
            steps_at[starts[j][1]] = est.steps
            j += 1
        while k < len(segs) and t[i] >= segs[k]['t_end']:
            s = segs[k]
            res, p = est.measure_stage(s['v'], s['t_begin'], s['t_end'])
            w = np.array([e for tt, e in est.env if s['t_begin'] + fl.CAL_SKIP_S <= tt <= s['t_end']])
            cad, q = fl.sdb.cadence(w) if len(w) > fl.FS else (0, 0)
            dur = s['t_end'] - s['t_begin'] - fl.CAL_SKIP_S
            st = [v for tt, v in snaps if s['t_begin'] + fl.CAL_SKIP_S <= tt <= s['t_end']]
            rows.append(dict(**s, result=res, live=float(np.mean([v != 0 for v in st])) if st else 0.0, level=fl.env_level(w) if len(w) else 0, q=q, cad=cad,
                             running=p['running'] if p else None, limp=p['limp'] if p else None,
                             steps=est.steps - steps_at.get(segs.index(s), est.steps), expect=cad * dur / 60))
            k += 1
    return pd.DataFrame(rows)


def walk_points(d):
    return d[(d.kind == 'walk') & (d.result == 'Ok') & (~d.running.astype(bool))]


def walk_check(d, other, label):
    """앱 사다리처럼 걷기 오름 2·4·6 km/h 점을 보정으로 → 앱 규칙(fusion_live.refit)으로 절편 → 나머지 걷기 구간.
    비교: 다른 사람 걷기 점 전부로 만든 절편(새 사람에게 남의 공식을 쓰는 경우), 공통 공식(이 두 사람으로 만든 것이라 유리)"""
    ok = walk_points(d)
    first = ok.drop_duplicates('v')                     # 오름(처음 나온 것)
    cal = first[first.v.isin([2, 4, 6])]
    test = ok.drop(cal.index)
    if cal.empty or test.empty:
        print(f'  {label}: 걷기 점 부족(보정 {len(cal)}, 시험 {len(test)})')
        return
    est = fl.SpeedEstimator()
    est.load([dict(v=r.v, limp=r.limp, cad=r.cad, amp=None, running=False) for r in cal.itertuples()])
    a = est.walk_a
    o = walk_points(other)
    a_other = float(np.mean(o.v - fl.WALK[1] * o.cad))
    err = {name: np.median(np.abs(x + fl.WALK[1] * test.cad - test.v))
           for name, x in (('이 사람', a), ('다른 사람', a_other), ('공통', fl.WALK[0]))}
    used = sorted(v for v in cal.v if v >= fl.WALK_MIN_KMH) or sorted(cal.v)
    print(f'  {label}: 보정 {sorted(cal.v.tolist())} 중 {used}로 절편 {a:.2f}(다른 사람 {a_other:.2f}, 공통 {fl.WALK[0]}) | '
          f'시험 {len(test)}구간 오차 절댓값 중앙 ' + ' / '.join(f'{k} {v:.2f}' for k, v in err.items()) + ' km/h')


def main():
    pd.set_option('display.width', 160)
    allr = []
    for who, session in SESSIONS.items():
        d = replay(session)
        d['who'] = who
        allr.append(d)
        print(f'\n=== {who} {session} ===')
        print(d[['kind', 'v', 'result', 'live', 'level', 'q', 'cad', 'running', 'steps', 'expect']]
              .round(3).to_string(index=False))
    d = pd.concat(allr)

    print('\n[걸음 없음 문턱] 포락선 75% 값: 벨트만(사람 옆 발판) vs 걷기 1–2 km/h vs 그 밖')
    for name, sel in (('벨트만', d.kind == 'still'), ('걷기 1', (d.kind == 'walk') & (d.v == 1)),
                      ('걷기 2', (d.kind == 'walk') & (d.v == 2)), ('걷기 3 이상', (d.kind == 'walk') & (d.v >= 3)),
                      ('달리기', d.kind == 'run')):
        lv = d[sel].level
        print(f'  {name:8s} n {len(lv):2d}  최소 {lv.min():.3f}  중앙 {lv.median():.3f}  최대 {lv.max():.3f}')

    print(f'\n[걷기 절편] 오름 2·4·6 km/h 보정, 절편은 {fl.WALK_MIN_KMH:g} km/h 이상 점으로 → 나머지 걷기 구간(같은 날, 2명)')
    for who, other in (('M', 'B'), ('B', 'M')):
        walk_check(d[d.who == who], d[d.who == other], who)

    print('\n[실시간 걸음 상태] 구간(앞 5초 뺌) 중 걸음으로 잡힌 0.5초 칸 비율(1 = 내내 걷기·달리기)')
    for name, sel in (('벨트만', d.kind == 'still'), ('걷기 1', (d.kind == 'walk') & (d.v == 1)),
                      ('걷기 2', (d.kind == 'walk') & (d.v == 2)), ('걷기 3 이상', (d.kind == 'walk') & (d.v >= 3)),
                      ('달리기', d.kind == 'run')):
        lv = d[sel].live
        print(f'  {name:8s} n {len(lv):2d}  평균 {lv.mean():.3f}  최소 {lv.min():.3f}')

    ok = d[(d.result == 'Ok') & (d.expect > 0)]
    r = ok.steps / ok.expect
    print(f'\n[누적 걸음] Ok 구간 {len(ok)}개, 봉우리로 센 걸음 ÷ (걸음 수 × 시간): 중앙 {r.median():.3f}, '
          f'10% {r.quantile(.1):.3f}, 90% {r.quantile(.9):.3f}, 3% 밖 {int(((r - 1).abs() > .03).sum())}개')
    for kind in ('walk', 'run'):
        rk = r[ok.kind == kind]
        print(f'  {kind}: 중앙 {rk.median():.3f} (n {len(rk)})')


if __name__ == '__main__':
    main()
