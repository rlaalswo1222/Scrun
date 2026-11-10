"""계획 0023: 모두에게 같은 보정 사다리 — 보정점 간격이 얼마면 그 사이·바깥 속도 오차가 얼마인지.

실행: .venv\\Scripts\\python speedlab/exp/calib_fixed.py
입력: calib_design.py와 같음(우리 10/29 M·B 기록, SoundTrackDB — 우리 실측 아님)
질문: 1에서 15 km/h를 덮는 고정 사다리를 만들 때 점 사이 간격을 얼마로 해야 하나, 전체 모델 하나(직선) vs 이웃 점끼리 잇기 중 무엇이 나은가
"""
import itertools
import sys
from pathlib import Path

import numpy as np
import pandas as pd

sys.path.insert(0, str(Path(__file__).resolve().parent))
import calib_design as cd   # noqa: E402

line, med = cd.line, cd.med


def by_gap(rows, label):
    r = pd.DataFrame(rows)
    if r.empty:
        print(f'  {label}: 없음')
        return
    for g, d in sorted(r.groupby('gap')):
        print(cd.line(f'{label} 간격 {g:.1f}', d.err))


# ---------- SoundTrackDB: 달리기 8에서 16 ----------
def std_main():
    s = cd.std_points()
    groups = {g: d for g, d in s.groupby('grp')}
    priors = {}

    def prior(g):
        if g not in priors:
            priors[g] = cd.pooled([x for k, x in groups.items() if k != g and x.v.nunique() >= 2])
        return priors[g]

    print('\n=== SoundTrackDB (우리 실측 아님), 모델 B(절편 + ln 충격 기울기) ===')
    # 1) 두 점 사이(안쪽) 속도 오차를 두 점 간격별로
    inner, outer = [], []
    for g, d in groups.items():
        sp = sorted(d.v.unique())
        if len(sp) < 3:
            continue
        for lo, hi in itertools.combinations(sp, 2):
            cal = d[d.v.isin((lo, hi))].groupby('v', as_index=False)[['cad', 'impact']].median()
            coef = cd.fit(cal, 'B', prior(g))
            test = d[~d.v.isin((lo, hi))]
            err = cd.pred(coef, test) - test.v.values
            for v, e in zip(test.v.values, err):
                if lo < v < hi:
                    inner.append(dict(gap=round(hi - lo, 1), err=e))
                else:
                    outer.append(dict(gap=round(min(abs(v - lo), abs(v - hi)), 1), err=e))
    print('\n[두 점 보정, 그 사이 속도] 간격이 넓을수록 커지나')
    by_gap(inner, '사이')
    print('\n[두 점 보정, 바깥 속도] 가까운 보정점에서 떨어진 거리')
    by_gap(outer, '바깥 거리')

    # 2) 속도 5개 이상 묶음: 모든 속도를 보정에 쓴 사다리에서 하나 빼고 맞히기(빠진 점 = 사다리 사이 속도)
    #    전체 직선 vs 이웃 두 점 잇기
    print('\n[촘촘한 사다리에서 한 점 빼기] 속도 5개 이상 묶음, 빠진 속도를 남은 점으로')
    lin, pw = [], []
    for g, d in groups.items():
        sp = sorted(d.v.unique())
        if len(sp) < 5:
            continue
        for k in range(1, len(sp) - 1):
            v = sp[k]
            rest = [x for x in sp if x != v]
            cal = d[d.v.isin(rest)].groupby('v', as_index=False)[['cad', 'impact']].median()
            test = d[d.v == v]
            coef = cd.fit(cal, 'B', prior(g))
            lin += list(cd.pred(coef, test) - test.v.values)
            nb = cal[cal.v.isin((sp[k - 1], sp[k + 1]))]
            coef2 = cd.fit(nb, 'B', prior(g))
            pw += list(cd.pred(coef2, test) - test.v.values)
    print(line('전체 직선(남은 점 전부)', lin))
    print(line('이웃 두 점 잇기', pw))


# ---------- 우리 기록: 달리기 7에서 10, 걷기 1에서 6 ----------
def our_main():
    segs = {p: cd.our_segments(p) for p in cd.fu.PEOPLE}
    run_pts = {p: pd.DataFrame([cd.seg_point(s) for s in cd.run_segs(segs[p]) if cd.seg_point(s)]) for p in segs}
    print('\n=== 우리 기록 (2명, 같은 날, 낙관적) ===')
    for p, other in (('M', 'B'), ('B', 'M')):
        prior = cd.pooled([run_pts[other]])
        rs = cd.run_segs(segs[p])
        full = {s['i']: cd.seg_point(s) for s in rs}
        speeds = sorted({s['v'] for s in rs})
        inner = []
        for lo, hi in itertools.combinations(speeds, 2):
            for a in [s for s in rs if s['v'] == lo]:
                for b in [s for s in rs if s['v'] == hi]:
                    cal = [cd.seg_point(a, T=17), cd.seg_point(b, T=17)]
                    if None in cal:
                        continue
                    coef = cd.fit(pd.DataFrame(cal), 'B', prior)
                    test = pd.DataFrame([full[i] for i in full if full[i] and lo < full[i]['v'] < hi])
                    if test.empty:
                        continue
                    for e in cd.pred(coef, test) - test.v.values:
                        inner.append(dict(gap=hi - lo, err=e))
        print(f'\n[{p} 달리기 두 점, 그 사이 속도]')
        by_gap(inner, p)

    print('\n[걷기] 걸음 수 공식, 걷기 안내 구간(1 km/h 포함해서 걸음이 잡히는지도 봄)')
    for p in segs:
        walk = [s for s in segs[p] if s['mode'] == 3]
        got = {}
        for s in walk:
            x = cd.seg_point(s, state=1)
            got.setdefault(s['v'], []).append(x is not None)
        print(f'  {p} 걷기 속도별 걸음 잡힘: ' + ', '.join(f'{v:g}: {sum(o)}/{len(o)}' for v, o in sorted(got.items())))
    wp = {p: pd.DataFrame([x for x in (cd.seg_point(s, state=1) for s in segs[p] if s['mode'] == 3) if x]) for p in segs}
    for p, other in (('M', 'B'), ('B', 'M')):
        bo, _ = np.polyfit(wp[other].cad, wp[other].v, 1)
        d = wp[p]
        sp = sorted(d.v.unique())
        for combo in [(4,), (3, 5), (2, 4), (2, 5), (2, 4, 6), (2, 6)]:
            if not all(c in sp for c in combo):
                continue
            cal = d[d.v.isin(combo)]
            if len(combo) == 1:
                b, a = bo, np.mean(cal.v - bo * cal.cad)
            else:
                b, a = np.polyfit(cal.cad, cal.v, 1)
            test = d[~d.v.isin(combo)]
            err = a + b * test.cad.values - test.v.values
            print(line(f'{p} 걷기 보정 {combo} → 나머지 {sorted(test.v.unique())}', err))


if __name__ == '__main__':
    our_main()
    std_main()
