"""계획 0013: 처음 보정을 몇 구간, 몇 초로 할지 — 기존 기록으로 보정 조건을 바꿔 가며 남은 구간 오차 비교.

실행: .venv\\Scripts\\python speedlab/exp/calib_design.py
입력: 우리 10/29 안내 모드 기록(est.csv 틱 = 앱이 4초 창으로 0.25초마다 낸 걸음 수·충격),
      SoundTrackDB(data/external/soundtrackdb, 우리 실측 아님)
출력: 콘솔 표 (결과 정리는 docs/speed/calibration_design.md)

사람: M = 민재(20261029-014932, 검증 14구간 7에서 10 km/h 반복 + 걷기 1에서 6), B = 고상준(20261029-022017, 보정 7 + 검증 14 + 걷기)
판정(결과 전에 고정, 계획 0013): 점·시간을 늘려도 시험 오차 중앙값이 0.05 km/h 넘게 줄지 않으면 거기서 멈춤
"""
import itertools
import sys
from pathlib import Path

import numpy as np
import pandas as pd

sys.path.insert(0, str(Path(__file__).resolve().parent))
import fusion as fu            # noqa: E402  안내 구간·틱 읽기, 상황 판단(걷기·달리기)
import soundtrackdb as st      # noqa: E402  StepSpeed.cs 옮긴 걸음 수·충격 계산

SKIP_S = 3.0      # 구간 앞에서 버릴 시간(fusion.py와 같음). 구간 20초라 우리 기록은 최대 17초
STOP = 0.05       # 판정 기준


def med(a):
    a = np.abs(np.asarray(a, float))
    return np.median(a) if len(a) else np.nan


def line(label, err, extra=''):
    a = np.abs(np.asarray(err, float))
    if not len(a):
        return f'  {label:<34} (없음)'
    return f'  {label:<34} 중앙 {np.median(a):.2f}  평균 {a.mean():.2f}  0.5 이내 {(a <= .5).mean() * 100:3.0f}%  (n {len(a)}){extra}'


# ---------- 모델 ----------
# A: 절편만 이 사람 점으로(기울기는 다른 사람들 값)  B: 절편 + 충격 기울기(걸음 수 기울기는 다른 사람들)
# C: 절편 + 충격 + 걸음 수 기울기 모두 이 사람(지금 앱, 4점 이상)
def fit(cal, model, prior):
    L, c, v = np.log(cal.impact.values), cal.cad.values, cal.v.values
    bL, bc = prior
    if model == 'A' or len(cal) < 2 or np.ptp(L) < 1e-6:
        return np.mean(v - bL * L - bc * c), bL, bc
    if model == 'B' or len(cal) < 4:
        y = v - bc * c
        b, a = np.polyfit(L, y, 1)
        return a, b, bc
    X = np.c_[np.ones(len(L)), L, c]
    a, b1, b2 = np.linalg.lstsq(X, v, rcond=None)[0]
    return a, b1, b2


def pred(coef, df):
    a, bL, bc = coef
    return a + bL * np.log(df.impact.values) + bc * df.cad.values


def pooled(groups):
    """묶음마다 평균을 뺀 뒤 공통 기울기(ln 충격, 걸음 수)"""
    X, y = [], []
    for g in groups:
        d = np.c_[np.log(g.impact.values), g.cad.values]
        X.append(d - d.mean(0))
        y.append(g.v.values - g.v.mean())
    return np.linalg.lstsq(np.vstack(X), np.concatenate(y), rcond=None)[0]


# ---------- 우리 기록 ----------
def our_segments(person):
    p = fu.PEOPLE[person]
    g = fu.guide_segments(p['session'])
    e = fu.ticks(p['session'])
    rows = []
    for i, s in g.iterrows():
        w = e[(e.t >= s.t0 + SKIP_S) & (e.t < s.t1)].copy()
        w['state'] = fu.state(w.cad.values, w.q.values, w.impact.values)
        rows.append(dict(i=i, mode=int(s['mode']), v=s.v, w=w, t0=s.t0))
    return rows


def seg_point(s, T=None, part=None, state=2):
    """구간 틱 중 앞 T초(또는 part = (시작, 끝) 초)의 중앙값 한 점"""
    w = s['w']
    t = w.t - (s['t0'] + SKIP_S)
    if T is not None:
        w = w[t < T]
    if part is not None:
        w = w[(t >= part[0]) & (t < part[1])]
    w = w[(w.state == state) & (w.q >= fu.Q_MIN) & (w.impact > 0)]
    if len(w) < 3:
        return None
    return dict(v=s['v'], cad=w.cad.median(), impact=w.impact.median())


def run_segs(segs):
    """달리기 구간(틱 절반 이상이 달리기)"""
    return [s for s in segs if s['mode'] in (1, 2) and (s['w'].state == 2).mean() >= .5]


def our_eval(segs, prior, n, model, T=17.0, pick=None):
    """달리기 구간 중 서로 다른 속도 n개를 보정으로(조합 전부), 나머지 구간 전체를 시험. 범위 안·밖 나눔"""
    rs = run_segs(segs)
    full = {s['i']: seg_point(s) for s in rs}
    speeds = sorted({s['v'] for s in rs})
    ein, eout = [], []
    for combo in itertools.combinations(speeds, n):
        for cal_segs in itertools.product(*[[s for s in rs if s['v'] == v] for v in combo]):
            if pick and not pick(cal_segs):
                continue
            cal = [seg_point(s, T=T) for s in cal_segs]
            if any(c is None for c in cal):
                continue
            coef = fit(pd.DataFrame(cal), model, prior)
            used = {s['i'] for s in cal_segs}
            test = [full[i] for i in full if i not in used and full[i]]
            if not test:
                continue
            td = pd.DataFrame(test)
            err = pred(coef, td) - td.v.values
            inr = (td.v >= min(combo)) & (td.v <= max(combo))
            ein += list(err[inr])
            eout += list(err[~inr])
    return ein, eout


def our_main():
    segs = {p: our_segments(p) for p in fu.PEOPLE}
    # 사전 기울기: 다른 사람 하나뿐이라 SoundTrackDB 공통 기울기도 함께 비교
    run_pts = {p: pd.DataFrame([seg_point(s) for s in run_segs(segs[p]) if seg_point(s)]) for p in segs}
    print('\n=== 우리 기록 (2명, 같은 기종, 같은 날 보정·시험 → 낙관적) ===')
    for p in segs:
        rp = run_pts[p]
        print(f'{p}: 달리기 구간 {len(rp)}개, 속도 {sorted(rp.v.unique())}')

    for p, other in (('M', 'B'), ('B', 'M')):
        prior = pooled([run_pts[other]])
        print(f'\n[{p}] 점 개수 (구간 17초, 사전 기울기 = {other})')
        for model in ('A', 'B', 'C'):
            for n in range(1, 6):
                if model == 'C' and n < 4 or model == 'B' and n < 2:
                    continue
                ein, eout = our_eval(segs[p], prior, n, model)
                if not ein and not eout:
                    continue
                print(line(f'모델 {model}, {n}점 · 범위 안', ein) + f'   | 범위 밖 중앙 {med(eout):.2f} (n {len(eout)})'
                      + f' | 전체 중앙 {med(ein + eout):.2f}')

    print('\n[구간 길이] 모델 B 3점, 보정 구간 앞 T초만 사용')
    for p, other in (('M', 'B'), ('B', 'M')):
        prior = pooled([run_pts[other]])
        for T in (3, 5, 8, 10, 15, 17):
            ein, _ = our_eval(segs[p], prior, 3, 'B', T=T)
            print(line(f'{p} T = {T:>2}초', ein))

    print('\n[같은 속도 두 번 짧게 vs 한 번 길게] M, 모델 B 3점 (M은 속도마다 3에서 4번 반복)')
    rs = run_segs(segs['M'])
    prior = pooled([run_pts['B']])
    full = {s['i']: seg_point(s) for s in rs}
    speeds = sorted({s['v'] for s in rs})
    res = {'한 번 17초': [], '한 번 8초': [], '두 번 × 8초(다른 시점)': []}
    for combo in itertools.combinations(speeds, 3):
        opts = [[s for s in rs if s['v'] == v] for v in combo]
        for pairs in itertools.product(*[list(itertools.combinations(o, 2)) for o in opts]):
            used = {s['i'] for pr in pairs for s in pr}
            test = pd.DataFrame([full[i] for i in full if i not in used and full[i]])
            for key, cal in (('한 번 17초', [seg_point(pr[0], T=17) for pr in pairs]),
                             ('한 번 8초', [seg_point(pr[0], T=8) for pr in pairs]),
                             ('두 번 × 8초(다른 시점)', [seg_point(s, T=8) for pr in pairs for s in pr])):
                if any(c is None for c in cal):
                    continue
                cd = pd.DataFrame(cal)
                coef = fit(cd, 'B', prior)
                inr = (test.v >= min(combo)) & (test.v <= max(combo))
                res[key] += list((pred(coef, test) - test.v.values)[inr])
    for k, e in res.items():
        print(line(k, e))

    print('\n[순서] M, 모델 B 3점: 보정점을 오름(속도 올린 직후)에서만 vs 내림(속도 내린 직후) 한 점 섞기')
    seq = [s['v'] for s in rs]
    up = {s['i']: (j == 0 or seq[j] > seq[j - 1]) for j, s in enumerate(rs)}
    for label, pick in (('오름만', lambda c: all(up[s['i']] for s in c)),
                        ('내림 한 점 이상', lambda c: any(not up[s['i']] for s in c))):
        ein, eout = our_eval(segs['M'], prior, 3, 'B', pick=pick)
        print(line(label, ein) + f'   | 범위 밖 중앙 {med(eout):.2f}')

    print('\n[걷기] 걸음 수 공식 v = a + b·spm, 걷기 안내 2에서 6 km/h(1은 걸음 못 잡음), 걷기 판단 틱')
    walk = {p: [s for s in segs[p] if s['mode'] == 3 and s['v'] >= 2] for p in segs}
    wp = {p: pd.DataFrame([x for x in (seg_point(s, state=1) for s in walk[p]) if x]) for p in segs}
    for p, other in (('M', 'B'), ('B', 'M')):
        bo, _ = np.polyfit(wp[other].cad, wp[other].v, 1)
        full = {s['i']: seg_point(s, state=1) for s in walk[p]}
        for n in (1, 2, 3):
            ein, eout = [], []
            sp = sorted({s['v'] for s in walk[p]})
            for combo in itertools.combinations(sp, n):
                for cs in itertools.product(*[[s for s in walk[p] if s['v'] == v] for v in combo]):
                    cal = [full[s['i']] for s in cs]
                    if any(c is None for c in cal):
                        continue
                    cd = pd.DataFrame(cal)
                    if n == 1 or np.ptp(cd.cad) < 1:
                        b, a = bo, np.mean(cd.v - bo * cd.cad)
                    else:
                        b, a = np.polyfit(cd.cad, cd.v, 1)
                    used = {s['i'] for s in cs}
                    td = pd.DataFrame([full[i] for i in full if i not in used and full[i]])
                    err = a + b * td.cad.values - td.v.values
                    inr = (td.v >= min(combo)) & (td.v <= max(combo))
                    ein += list(err[inr]); eout += list(err[~inr])
            print(line(f'{p} 걷기 {n}점 · 범위 안', ein) + f'   | 범위 밖 중앙 {med(eout):.2f} (n {len(eout)})'
                  + f' | 전체 중앙 {med(ein + eout):.2f}')


# ---------- SoundTrackDB ----------
def std_points():
    s = st.session_points(st.file_points())
    s = s.rename(columns={'kmh': 'v'})
    return s


def std_eval(s, n, model, maxgap=None):
    """묶음(같은 사람·같은 날)마다 속도 n개 보정 → 나머지 속도. 사전 기울기는 그 묶음을 뺀 나머지로"""
    rows = []
    groups = {g: d for g, d in s.groupby('grp')}
    for g, d in groups.items():
        sp = sorted(d.v.unique())
        if len(sp) < n + 1:
            continue
        prior = pooled([x for k, x in groups.items() if k != g and x.v.nunique() >= 2])
        for combo in itertools.combinations(sp, n):
            cal = d[d.v.isin(combo)].groupby('v', as_index=False)[['cad', 'impact']].median()
            test = d[~d.v.isin(combo)]
            coef = fit(cal, model, prior)
            err = pred(coef, test) - test.v.values
            lo, hi = min(combo), max(combo)
            dist = np.where(test.v < lo, lo - test.v, np.where(test.v > hi, test.v - hi, 0))
            for e, dd in zip(err, dist):
                rows.append(dict(grp=g, n=n, err=e, dist=dd))
    return pd.DataFrame(rows)


def std_windows(T_list, start_s=30.0):
    """보정 구간 길이 시험용: 앱처럼 4초 창을 0.5초마다, 세션 앞(30초 뒤)부터 T초 → 중앙값. 캐시"""
    cache = st.DB / 'calib_T.csv'
    if cache.exists():
        return pd.read_csv(cache)
    rows = []
    Tm = max(T_list)
    for f in sorted((st.DB / 'imu_csv').glob('*.csv')):
        day, subj, mph, trial = f.stem.split('_')
        a = pd.read_csv(f)
        t = a.time.values
        if not 0.009 < np.median(np.diff(t)) < 0.011:
            continue
        fs, env, imp = st.envelopes(t, a[['ax', 'ay', 'az']].values)
        w = 4 * st.ENV_FS
        k0 = int(start_s * st.ENV_FS)
        for k in range(k0, min(len(env) - w, k0 + int(Tm * st.ENV_FS)), st.ENV_FS // 2):
            c, q = st.cadence(env[k:k + w])
            rows.append(dict(day=day, subj=subj, mph=int(mph), trial=int(trial),
                             t=(k - k0) / st.ENV_FS + 4, cad=c, q=q, impact=st.impact(imp[k:k + w])))
    df = pd.DataFrame(rows)
    df.to_csv(cache, index=False)
    return df


def std_main():
    s = std_points()
    print('\n=== SoundTrackDB 재분석 (59명, 헬스장 13곳, 8에서 16 km/h · 1.6 간격, 우리 실측 아님) ===')
    ns = s.groupby('grp').v.nunique()
    print('묶음별 속도 개수:', ns.value_counts().sort_index().to_dict())

    print('\n[점 개수] 묶음마다 보정 속도 n개 → 남은 속도 (같은 묶음 집합끼리 비교하려고 속도 4개 이상 묶음만)')
    s4 = s[s.grp.isin(ns[ns >= 4].index)]
    print(f'  속도 4개 이상 묶음 {s4.grp.nunique()}개')
    for model in ('A', 'B', 'C'):
        for n in (1, 2, 3):
            if model == 'B' and n < 2 or model == 'C' and n < 3:
                continue
            r = std_eval(s4, n, model)
            print(line(f'모델 {model}, {n}점 · 범위 안', r[r.dist == 0].err)
                  + f'   | 범위 밖 중앙 {med(r[r.dist > 0].err):.2f} (n {(r.dist > 0).sum()})')

    print('\n[범위 밖 거리] 모델 B 2점·3점, 속도 3개 이상 묶음: 보정 범위에서 몇 km/h 떨어졌나')
    s3 = s[s.grp.isin(ns[ns >= 3].index)]
    for n in (2, 3):
        r = std_eval(s3, n, 'B')
        for lo, hi, lab in ((0, 0, '범위 안'), (0.01, 2, '밖 1.6'), (2, 4, '밖 3.2'), (4, 99, '밖 4.8 이상')):
            e = r[(r.dist == 0)].err if hi == 0 else r[(r.dist >= lo) & (r.dist < hi)].err
            print(line(f'{n}점 {lab}', e))

    print('\n[구간 길이] 모델 B 3점(속도 4개 이상 묶음): 보정 속도는 세션 앞 T초(4초 창, 0.5초 간격)만, 시험은 세션 전체 중앙값')
    T_list = (5, 10, 15, 20, 30, 60)
    wins = std_windows(T_list)
    wins = wins[(wins.q >= 0.3) & (wins.impact > 0)]
    wins['grp'] = wins.subj + '@' + wins.day
    wins['v'] = wins.mph * st.MPH
    groups = {g: d for g, d in s4.groupby('grp')}
    for T in T_list:
        errs = []
        for g, d in groups.items():
            prior = pooled([x for k, x in s.groupby('grp') if k != g and x.v.nunique() >= 2])
            wg = wins[(wins.grp == g) & (wins.t <= T + 4)]
            wg = wg[wg.trial == wg.groupby('v').trial.transform('min')]   # 속도마다 첫 세션 하나만
            for combo in itertools.combinations(sorted(d.v.unique()), 3):
                cal = wg[np.isclose(wg.v.values[:, None], np.array(combo)).any(1)]
                cal = cal.groupby('v', as_index=False)[['cad', 'impact']].median()
                if len(cal) < 3:
                    continue
                test = d[~d.v.isin(combo)]
                test = test[(test.v >= min(combo)) & (test.v <= max(combo))]
                errs += list(pred(fit(cal, 'B', prior), test) - test.v.values)
        print(line(f'T = {T:>2}초', errs))


if __name__ == '__main__':
    our_main()
    std_main()
