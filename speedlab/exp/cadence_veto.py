"""계획 0063 진행(11/21 맥): "걸음 수가 내려간 동안은 속도를 더 올리지 않음" 규칙 시험(세게 밟기 튐 막기, 가설).
앱과 같은 계산(speedlab/fusion_live.py)으로 0.5초 틱 값을 낸 뒤, 그 위에 규칙을 얹어 비교한다.

실행: .venv\\Scripts\\python speedlab/exp/cadence_veto.py (맥: numpy·pandas·scipy가 있는 python)

기록:
  M  data/runs/20261029-040156 (SpeedRecorder, 기록 안 보정 set 1, 자유 달리기 정답 mark.csv)
  B  data/runs/20261029-025507 (보정 = 앞 8·9·10 구간, stride_model.timeline_B와 같게)
  W2 data/runs/20261121-watch/2026-11-20_19-06-26.zip (메트로놈 170, 8·9·10·11, 구간은 폰 충격 계단으로 추정 → 보정)
  W3 data/runs/20261121-watch/2026-11-20_19-13-20.zip (9 km/h 보통 → 세게 → 보통, W2 보정으로)
"""
import io
import json
import sys
import zipfile
from pathlib import Path

import numpy as np
import pandas as pd

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'speedlab'))
sys.path.insert(0, str(ROOT / 'speedlab/exp'))
import fusion_live as fl  # noqa: E402
from live_eval import true_speed  # noqa: E402

SETTLE_S = 10.0


def run_estimator(acc, air=None, stages=(), points=None):
    """acc: DataFrame t,x,y,z(m/s², 중력 포함). stages: [(t, set, speed)]. → 틱 DataFrame, 보정점"""
    ev = [(r.t, 0, (r.x, r.y, r.z)) for r in acc.itertuples()]
    if air is not None:
        ev += [(r.t, 1, (r.x + r.x_g, r.y + r.y_g, r.z + r.z_g, r.x_g, r.y_g, r.z_g)) for r in air.itertuples()]
    ev += [(t, 2, (s, v)) for t, s, v in stages]
    ev.sort(key=lambda e: (e[0], e[1]))
    est, rows = fl.SpeedEstimator(), []
    if points is not None:
        est.load(points)
    for t, k, v in ev:
        if k == 0:
            before = est.snap
            est.add_phone(t, *v)
            s = est.snap
            if s is not None and s is not before:
                rows.append(dict(t=s['t'], kmh=s['kmh'], state=s['state'], cad=s['cad'], imp=s['imp'],
                                 kmh_base=s['kmh_base']))
        elif k == 1:
            est.add_airpods(t, *v)
        else:
            est.begin_stage(v[0], v[1], t)
    return pd.DataFrame(rows), est.points


def veto(d, dcad=3.0, both=False, ref_s=6.0):
    """규칙: 걸음 수가 기준보다 dcad 넘게 낮아진 동안은 속도를 그 직전 값보다 올리지 않음(내려가는 건 그대로).
    기준 걸음 수 = 걸음 수가 정상인 동안 최근 ref_s 중앙값(낮아진 동안은 고정). 직전 값 = 낮아지기 직전 내보낸 값.
    both: 걸음 수가 dcad 넘게 높아진 동안은 직전 값보다 내리지 않음. 걷기·값 없음이면 앱 값 그대로, 기준 초기화"""
    out, vetoed, hist, ref, cap, floor = [], [], [], np.nan, np.nan, np.nan
    n_ref = int(ref_s / 0.5)
    last = np.nan
    for a, c, st in zip(d.kmh, d.cad, d.state):
        if st != 2 or not np.isfinite(a) or a <= 0 or not np.isfinite(c) or c <= 0:
            hist, ref, cap, floor = [], np.nan, np.nan, np.nan
            out.append(a); vetoed.append(False); last = a
            continue
        low = np.isfinite(ref) and c < ref - dcad
        high = both and np.isfinite(ref) and c > ref + dcad
        o, v = a, False
        if low:
            if not np.isfinite(cap):
                cap = last
            if a > cap:
                o, v = cap, True
        else:
            cap = np.nan
        if high:
            if not np.isfinite(floor):
                floor = last
            if a < floor:
                o, v = floor, True
        else:
            floor = np.nan
        if not low and not high:
            hist = (hist + [c])[-n_ref:]
            ref = float(np.median(hist)) if len(hist) >= 4 else np.nan
        out.append(o); vetoed.append(v); last = o
    return np.array(out), np.array(vetoed)


def score(d, est, mask_test):
    e = (est - d.v)[mask_test & (d.since >= SETTLE_S) & d.v.notna()]
    lag = []
    sub = d[mask_test].assign(e=(est - d.v)[mask_test])
    for _, g in sub.groupby((sub.v != sub.v.shift()).cumsum()):
        ok = (g.e.abs() < 0.5).rolling(6).sum() == 6
        lag.append(g.since[ok].iloc[0] - 2.5 if ok.any() else np.nan)
    return e.abs().median(), e.abs().mean(), e.abs().max(), np.nanmedian(lag), int(np.isnan(lag).sum()), len(lag)


def truth(d, changes):
    v, since = [], []
    for t in d.t:
        cv, cs = np.nan, np.nan
        for s, sp in changes:
            if s <= t:
                cv, cs = sp, t - s
        v.append(cv); since.append(cs)
    d['v'], d['since'] = v, since


def load_air(ph):
    a, g = pd.read_csv(ph / 'airpods_acc.csv'), pd.read_csv(ph / 'airpods_grav.csv')
    return a.merge(g, on='t', suffixes=('', '_g'))


def rec_M():
    ph = ROOT / 'data/runs/20261029-040156/phone'
    stage, mark = pd.read_csv(ph / 'stage.csv'), pd.read_csv(ph / 'mark.csv')
    names = {0: 'sync', 1: 'calib', 2: 'test', 3: 'free', 4: 'hop', 5: 'warmup', 6: 'still', 9: 'end'}
    stages = [(r.t, names.get(int(r.set), '?'), None if r.speed < 0 else float(r.speed)) for r in stage.itertuples()]
    d, pts = run_estimator(pd.read_csv(ph / 'iphone_acc.csv'), load_air(ph), stages)
    calr = stage[stage.set == 1].reset_index(drop=True)
    free0 = stage.t[stage.set == 3].iloc[0]
    ch = [(r.t, r.speed) for r in calr.itertuples()] + [(s, v) for s, e, v in true_speed(mark, stage.t.iloc[-1])]
    truth(d, ch)
    return d, (d.t >= free0) & (d.v >= 7), pts


def rec_B():
    run = ROOT / 'data/runs/20261029-025507'
    meta = json.load(open(run / 'meta.json', encoding='utf-8'))
    known = {s['index']: s['start_s'] for s in meta['segments']}
    idx = np.array(sorted(known))
    step = np.polyfit(idx, [known[i] for i in idx], 1)
    starts = [known.get(i, np.polyval(step, i)) for i in range(len(meta['stages']))]
    speeds = [s['speed_kmh'] for s in meta['stages']]
    t0 = meta['t0']
    ph = run / 'phone'
    acc = pd.read_csv(ph / 'iphone_acc.csv').drop_duplicates('t')   # 같은 시각 중복(옛 SpeedRecorder) 뺌
    stages = [(t0 + starts[i], 'calib', float(speeds[i])) for i in (1, 2, 3)] + [(t0 + starts[4], 'free', None)]
    d, pts = run_estimator(acc, load_air(ph), stages)
    truth(d, [(t0 + s, v) for s, v in zip(starts, speeds)])
    return d, (d.t >= t0 + starts[4]) & (d.v >= 8), pts


def sl_acc(zpath):
    z = zipfile.ZipFile(zpath)
    a = pd.read_csv(io.BytesIO(z.read('Accelerometer.csv')))
    g = pd.read_csv(io.BytesIO(z.read('Gravity.csv')))
    m = a.merge(g, on='time', suffixes=('', '_g'))
    return pd.DataFrame(dict(t=m.time / 1e9, x=m.x + m.x_g, y=m.y + m.y_g, z=m.z + m.z_g))


def rec_W():
    w2 = sl_acc(ROOT / 'data/runs/20261121-watch/2026-11-20_19-06-26.zip')
    t0 = w2.t.iloc[0]
    cut = [(18, 8.0), (50, 9.0), (74, 10.0), (90, 11.0)]          # 폰 충격 계단으로 추정한 단계 시작(초)
    ends = [47, 71, 87, 115]
    # 보정: 각 단계 시작부터(앱이 앞 5초를 버림) 끝까지
    stages = []
    for (s, v), e in zip(cut, ends):
        stages.append((t0 + s, 'calib', v))
        stages.append((t0 + e, 'free', None))
    d2, pts = run_estimator(w2, None, stages)
    truth(d2, [(t0 + s, v) for s, v in cut] + [(t0 + 116, np.nan)])
    w3 = sl_acc(ROOT / 'data/runs/20261121-watch/2026-11-20_19-13-20.zip')
    s0 = w3.t.iloc[0]
    d3, _ = run_estimator(w3, None, [], points=pts)
    d3['rel'] = d3.t - s0
    return d2, pts, d3


def main():
    variants = [('규칙 Δ걸음 2', dict(dcad=2.0)), ('규칙 Δ걸음 3', dict(dcad=3.0)), ('규칙 Δ걸음 5', dict(dcad=5.0)),
                ('규칙 Δ걸음 3, 양쪽', dict(dcad=3.0, both=True))]
    for name, fn in (('M 민재 10/29 (에어팟)', rec_M), ('B 고상준 10/29 (에어팟)', rec_B)):
        d, test, pts = fn()
        run = d[test & (d.state == 2)]
        print(f'\n== {name}: 보정점 {len(pts)}개, 뒤 달리기 틱 {len(run)}개, 속도 {sorted(d[test].v.dropna().unique())}')
        print(f'  6초 창 걸음 수(뒤 안정 틱): 같은 속도 안 표준편차 중앙 '
              f'{d[test & (d.since >= SETTLE_S)].groupby("v").cad.std().median():.1f} spm')
        # 진짜 속도 바뀜 때 걸음 수가 어느 쪽으로 움직였나(바뀌기 전 6초 → 바뀐 뒤 10–16초)
        moves = []
        for (s, v) in sorted({(r.t - r.since, r.v) for r in d[test].itertuples() if np.isfinite(r.since)}):
            pre = d[(d.t >= s - 6) & (d.t < s)]
            post = d[(d.t >= s + 10) & (d.t < s + 16)]
            pv = pre.v.dropna()
            if len(pre) and len(post) and len(pv):
                moves.append((pv.iloc[-1], v, post.cad.median() - pre.cad.median()))
        print('  진짜 속도 바뀜 때 걸음 수 변화(spm): ' + ', '.join(f'{a:g}→{b:g} {c:+.1f}' for a, b, c in moves))
        base = d.kmh.values
        r = score(d, base, test)
        print(f'  {"지금 앱":<20} 안정 |오차| 중앙 {r[0]:.2f} / 평균 {r[1]:.2f} / 최대 {r[2]:.2f} | 반응 중앙 {r[3]:.1f}초, 못 들어옴 {r[4]}/{r[5]}')
        for vn, kw in variants:
            est, vt = veto(d, **kw)
            r = score(d, est, test)
            print(f'  {vn:<20} 안정 |오차| 중앙 {r[0]:.2f} / 평균 {r[1]:.2f} / 최대 {r[2]:.2f} | 반응 중앙 {r[3]:.1f}초, '
                  f'못 들어옴 {r[4]}/{r[5]} | 막은 틱 {int(vt[test].sum())}개({vt[test].mean() * 100:.1f}%)')

    d2, pts, d3 = rec_W()
    print('\n== W 민재 11/21 (폰만, 에어팟 없음): 보정점 ' + ', '.join(f'{p["v"]:g}' for p in pts if p['running']))
    test2 = d2.v.notna() & (d2.state == 2)
    r = score(d2, d2.kmh.values, test2)
    print(f'  세트 2(보정 데이터 그대로라 정확도 아님) 지금 앱 안정 |오차| 중앙 {r[0]:.2f}')
    for vn, kw in variants:
        est, vt = veto(d2, **kw)
        r = score(d2, est, test2)
        print(f'  세트 2 {vn:<20} 안정 |오차| 중앙 {r[0]:.2f} / 최대 {r[2]:.2f} | 반응 {r[3]:.1f}초 | 막은 틱 {int(vt[test2].sum())}개')
    print('  세트 3(9 km/h 고정, 보통 10–24 / 세게 25–41 / 보통 43–54초, 구간은 기록으로 추정): 구간별 평균 km/h')
    segs = [('보통', 12, 24), ('세게', 27, 41), ('보통', 45, 54)]
    rows = [('지금 앱', d3.kmh.values, np.zeros(len(d3), bool))] + [(vn, *veto(d3, **kw)) for vn, kw in variants]
    for vn, est, vt in rows:
        parts = []
        for sn, a, b in segs:
            m = (d3.rel >= a) & (d3.rel < b) & (d3.state == 2)
            parts.append(f'{sn} {np.nanmean(est[m]):.2f}(최대 {np.nanmax(est[m]):.2f})')
        print(f'    {vn:<20} ' + ' | '.join(parts) + f' | 막은 틱 {int(vt.sum())}개')
    m = (d3.state == 2)
    print('    걸음 수(6초 창): ' + ' | '.join(f'{sn} {d3.cad[(d3.rel >= a) & (d3.rel < b) & m].median():.0f}' for sn, a, b in segs))


if __name__ == '__main__':
    main()
