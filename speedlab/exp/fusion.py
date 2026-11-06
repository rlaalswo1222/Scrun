"""계획 0009: 걷기·달리기를 가르고, 공통 기울기 + 사람별 한 값(a)으로 속도 추정 — PC에서 지금까지 기록으로 평가.

실행: .venv\\Scripts\\python speedlab/exp/fusion.py
입력: 앱이 남긴 값 그대로(est.csv 틱마다 impact·cad·q, calib.json 보정점) — 앱에 옮겼을 때와 같은 숫자로 보려고
      data/runs/20261029-025507 (B, 계기판 아이폰 + 에어팟)
출력: 콘솔 표 (결과 정리는 docs/speed/fusion.md)

사람: M = 민재(10/28 보정, 10/29 걷기·검증), B = 고상준(10/29 민재 다음에 뛴 팀원(걷기·보정·검증, 같은 날 에어팟 기록도))
주파수는 쓰지 않음(10/29 결정, 계획 0009).
"""
import json
import sys
from pathlib import Path

import numpy as np
import pandas as pd

ROOT = Path(__file__).resolve().parents[2]
SL = ROOT / 'data/speedlab'

# 상황 판단 기준(이 기록으로 정함 → 새 사람으로 다시 확인할 것)
Q_MIN = 0.3        # 걸음 신뢰가 이보다 낮으면 "걸음 없음"(앱과 같음)
RUN_CAD = 150      # 걸음 수가 이 이상이면 달리기
RUN_IMP = 3.0      # 또는 충격이 이 이상이면 달리기 (걷기 최대 2.7, 달리기 최소 2.7에서 3.5)
# 보정 구간 품질: 20초 안 틱(4초 창)의 10%·90% 값 차이
CAL_CAD_SPREAD = 10.0   # spm
CAL_IMP_RATIO = 1.4     # 90% ÷ 10%
SKIP_S = 3.0            # 기록 구간 앞에서 버릴 시간(걸음 창 4초가 앞 속도를 덜 담게)

PEOPLE = {
    'M': dict(calib=SL / 'speedlab_calib_phone_20261028_2020.json', calib_day='10/28',
              session='20261029-014932', calib_session='20261028-030843'),
    'B': dict(calib=SL / 'speedlab_calib_phone_20261029_team1.json', calib_day='10/29',
              session='20261029-022017', calib_session='20261029-022017'),
}


# ---------- 기록 읽기 ----------
def guide_segments(session):
    """events.csv의 안내 모드 구간: mode(1 보정, 2 검증, 3 걷기), k(순서), v(속도), 기록 구간 t0·t1, 모드 시작 ts"""
    ev = pd.read_csv(SL / 'SpeedLab' / session / 'events.csv')
    out, mode, k, ts, st = [], None, 0, None, None
    for _, r in ev.iterrows():
        if r.event == 'guide_mode':
            mode, k, ts = int(r.value), 0, r.t
        elif r.event == 'guide_start':
            st = (r.t, float(r.value))
        elif r.event == 'guide_end' and st:
            out.append(dict(mode=mode, k=k, v=st[1], t0=st[0], t1=r.t, ts=ts))
            k += 1
    return pd.DataFrame(out)


def ticks(session):
    e = pd.read_csv(SL / 'SpeedLab' / session / 'est.csv')
    return e[['t', 'cad', 'q', 'impact']].copy()


def calib_points(person):
    """앱 보정점(20초) 중 달리기 점 + 그 구간 틱으로 본 품질"""
    p = PEOPLE[person]
    pts = [x for x in json.load(open(p['calib'], encoding='utf-8'))['points']
           if x['q'] >= Q_MIN and x.get('impact', 0) > 0]
    df = pd.DataFrame([dict(v=x['kmh'], cad=x['cad'], impact=x['impact']) for x in pts])
    df['ok'] = True
    # 품질: 보정 구간 틱이 있는 경우만(B는 안내 보정, M 10/28은 틱에 충격 열이 없어 걸음 수만)
    if person == 'B':
        g = guide_segments(p['calib_session'])
        e = ticks(p['calib_session'])
        for _, s in g[g['mode'] == 1].iterrows():
            w = e[(e.t >= s.t0 + SKIP_S) & (e.t < s.t1)]
            spread = w.cad.quantile(.9) - w.cad.quantile(.1)
            ratio = w.impact.quantile(.9) / max(w.impact.quantile(.1), 1e-6)
            bad = spread > CAL_CAD_SPREAD or ratio > CAL_IMP_RATIO
            df.loc[np.isclose(df.v, s.v), ['spread', 'ratio', 'ok']] = [spread, ratio, not bad]
    else:
        e = pd.read_csv(SL / 'SpeedLab' / p['calib_session'] / 'est.csv')
        ev = pd.read_csv(SL / 'SpeedLab' / p['calib_session'] / 'events.csv')
        st, dn = ev[ev.event == 'calib_start'].reset_index(), ev[ev.event == 'calib_done'].reset_index()
        for i in range(len(dn)):
            w = e[(e.t >= st.t[i] + SKIP_S) & (e.t < dn.t[i])]
            spread = w.cad.quantile(.9) - w.cad.quantile(.1)
            df.loc[np.isclose(df.v, float(dn.value[i])), ['spread', 'ok']] = [spread, spread <= CAL_CAD_SPREAD]
    return df


# ---------- 1. 상황 판단 ----------
def state(cad, q, impact):
    """0 걸음 없음, 1 걷기, 2 달리기"""
    s = np.where((cad >= RUN_CAD) | (impact >= RUN_IMP), 2, 1)
    return np.where(q < Q_MIN, 0, s)


# ---------- 2. 걷기: 공통 걸음 수 공식 ----------
def fit_walk(rows):
    b, a = np.polyfit(rows.cad, rows.v, 1)
    return a, b


# ---------- 3·4. 달리기: 공통 기울기 + 사람별 a ----------
def design(df, cad=True):
    return np.c_[np.log(df.impact)] if not cad else np.c_[np.log(df.impact), df.cad]


def fit_slopes(cal_by_person, cad=True):
    """사람마다 평균을 뺀 뒤 함께 맞춤(사람별 a는 따로) → 공통 기울기"""
    X, y = [], []
    for c in cal_by_person:
        d = design(c, cad)
        X.append(d - d.mean(0))
        y.append(c.v.values - c.v.mean())
    return np.linalg.lstsq(np.vstack(X), np.concatenate(y), rcond=None)[0]


def fit_own(cal, cad=True):
    """지금 앱 방식: 그 사람 점으로 a·기울기 모두"""
    X = np.c_[np.ones(len(cal)), design(cal, cad)]
    c = np.linalg.lstsq(X, cal.v.values, rcond=None)[0]
    return c[0], c[1:]


def intercept(cal, slopes, cad=True):
    return float(np.mean(cal.v.values - design(cal, cad) @ slopes))


def predict(df, a, slopes, cad=True):
    return a + design(df, cad) @ slopes


# ---------- 6. 실시간: 계단식 묶음 ----------
def step_hold(cad, limp, raw, n_conf=4, d_cad=6.0, d_imp=0.15):
    """같은 단계 안에선 지금까지 값의 중앙값. 걸음 수·ln(충격)이 단계 값에서 크게 벗어난 틱이 n_conf개 이어지면 새 단계."""
    out = np.full(len(raw), np.nan)
    buf, ref, far = [], None, 0
    for i in range(len(raw)):
        if np.isnan(raw[i]):
            out[i] = out[i - 1] if i else np.nan
            continue
        if ref is not None and (abs(cad[i] - ref[0]) > d_cad or abs(limp[i] - ref[1]) > d_imp):
            far += 1
        else:
            far = 0
        if ref is None or far >= n_conf:
            buf = list(raw[max(0, i - n_conf + 1):i + 1][~np.isnan(raw[max(0, i - n_conf + 1):i + 1])])
            far = 0
        else:
            buf.append(raw[i])
        ref = (np.median(cad[max(0, i - len(buf) + 1):i + 1]), np.median(limp[max(0, i - len(buf) + 1):i + 1]))
        out[i] = np.median(buf)
    return out


def median3(raw):
    s = pd.Series(raw)
    return s.rolling(3, min_periods=1).median().values


# ---------- 평가 ----------
def summ(err):
    a = np.abs(np.asarray(err, float))
    a = a[~np.isnan(a)]
    return f'평균 {a.mean():.2f} 중앙 {np.median(a):.2f} 최대 {a.max():.2f} 0.5 이내 {(a <= 0.5).mean() * 100:3.0f}% (n {len(a)})'


def seg_table(person):
    """검증·걷기 구간마다 틱 묶음"""
    p = PEOPLE[person]
    g = guide_segments(p['session'])
    e = ticks(p['session'])
    rows = []
    for _, s in g.iterrows():
        w = e[(e.t >= s.t0 + SKIP_S) & (e.t < s.t1)].copy()
        w['state'] = state(w.cad.values, w.q.values, w.impact.values)
        rows.append(dict(person=person, mode=s['mode'], k=s.k, v=s.v, w=w))
    return rows


def main():
    segs = {p: seg_table(p) for p in PEOPLE}
    cals = {p: calib_points(p) for p in PEOPLE}

    print('== 보정점과 품질 검사 (spread = 걸음 수 10%·90% 차이, ratio = 충격 90%/10%)')
    for p, c in cals.items():
        print(f'{p} ({PEOPLE[p]["calib_day"]}):')
        print(c.round(2).to_string(index=False))

    print('\n== 1. 상황 판단 (틱 비율: 걸음 없음 / 걷기 / 달리기)')
    for p in PEOPLE:
        for s in segs[p]:
            st = s['w'].state.value_counts(normalize=True)
            tag = {1: '보정', 2: '검증', 3: '걷기'}[s['mode']]
            print(f'  {p} {tag} {s["v"]:4.1f}: ' + ' / '.join(f'{st.get(k, 0):.2f}' for k in (0, 1, 2)))

    print('\n== 2. 걷기: 걸음 수 공식을 다른 사람 기록으로 만들고 맞히기 (걷기 안내 2에서 6 km/h, 걷기로 판단된 틱의 구간 중앙값)')
    walk = {p: pd.DataFrame([dict(v=s['v'], cad=s['w'][s['w'].state == 1].cad.median())
                             for s in segs[p] if s['mode'] == 3 and s['v'] >= 2]).dropna() for p in PEOPLE}
    for tr, te in (('M', 'B'), ('B', 'M')):
        a, b = fit_walk(walk[tr])
        err = a + b * walk[te].cad - walk[te].v
        print(f'  {tr} 공식 → {te}: v = {a:.2f} + {b:.4f}·spm | {summ(err)}')
    a_w, b_w = fit_walk(pd.concat(walk.values()))
    print(f'  두 사람 합친 공식: v = {a_w:.2f} + {b_w:.4f}·spm (아래 검증의 걷기 판단 틱에 씀)')
    print('  1 km/h는 걸음 신뢰가 낮아 대부분 "걸음 없음"(위 상황 판단 표)')

    print('\n== 3. 달리기 공식: 검증 구간(달리기로 판단된 틱의 구간 중앙값) 오차')
    print('    보정점은 품질 검사 통과한 것만, 걷기 공식은 위 합친 것')
    variants = {}
    for cad in (False, True):
        name = '충격 + 걸음 수' if cad else '충격만'
        for p in PEOPLE:
            other = [q for q in PEOPLE if q != p][0]
            c_all, c_ok = cals[p], cals[p][cals[p].ok]
            res = {
                '지금 앱(본인 점으로 전부, 품질 검사 없음)': fit_own(c_all, cad),
                '본인 점으로 전부, 품질 검사': fit_own(c_ok, cad),
            }
            sl_other = fit_slopes([cals[other][cals[other].ok]], cad)
            res['기울기 = 다른 사람, a = 본인 보정 평균'] = (intercept(c_ok, sl_other, cad), sl_other)
            sl_pool = fit_slopes([cals[q][cals[q].ok] for q in PEOPLE], cad)
            res['기울기 = 두 사람 공통, a = 본인 보정 평균'] = (intercept(c_ok, sl_pool, cad), sl_pool)
            # a를 보정점 하나로만: 점마다 해 보고 평균 오차
            one = []
            for j in range(len(c_ok)):
                one.append((intercept(c_ok.iloc[[j]], sl_pool, cad), sl_pool))
            for label, (a, sl) in res.items():
                variants.setdefault((name, label), {})[p] = (a, sl)
            variants.setdefault((name, '기울기 = 두 사람 공통, a = 보정점 하나(점마다 평균)'), {})[p] = one

    def run_errors(p, model, cad, halves=False, correct=None):
        errs = []
        for s in segs[p]:
            if s['mode'] != 2:
                continue
            if halves and s['k'] < 7:
                continue
            w = s['w'][s['w'].state > 0]
            pred = np.where(w.state == 2, predict(w, model[0] + (correct or 0), model[1], cad), a_w + b_w * w.cad)
            errs.append(np.median(pred) - s['v'])
        return errs

    for (name, label), per in variants.items():
        cad = name != '충격만'
        line = []
        for p, model in per.items():
            if isinstance(model, list):
                e = np.mean([np.mean(np.abs(run_errors(p, m, cad))) for m in model])
                line.append(f'{p} 평균 {e:.2f}')
            else:
                line.append(f'{p} {summ(run_errors(p, model, cad))}')
        print(f'  [{name}] {label}\n      ' + '\n      '.join(line))

    print('\n== 4. 인증 사진 흉내: 검증 앞 7구간의 평균 속도(사진)로 a를 고친 뒤 뒤 7구간')
    for cad in (False, True):
        name = '충격 + 걸음 수' if cad else '충격만'
        for label in ('지금 앱(본인 점으로 전부, 품질 검사 없음)', '기울기 = 두 사람 공통, a = 본인 보정 평균'):
            for p in PEOPLE:
                model = variants[(name, label)][p]
                first = [s for s in segs[p] if s['mode'] == 2 and s['k'] < 7]
                tru, pre = [], []
                for s in first:
                    w = s['w'][s['w'].state == 2]
                    tru += [s['v']] * len(w)
                    pre += list(predict(w, model[0], model[1], cad))
                delta = np.mean(tru) - np.mean(pre)
                before = run_errors(p, model, cad, halves=True)
                after = run_errors(p, model, cad, halves=True, correct=delta)
                print(f'  [{name}] {label} {p}: 고친 양 {delta:+.2f} | 전 {summ(before)} | 후 {summ(after)}')

    print('\n== 5. 실시간(틱 0.25초): 검증 기록 구간 안 모든 틱, 기울기 = 두 사람 공통 + a = 본인 보정 평균, 충격 + 걸음 수')
    for p in PEOPLE:
        model = variants[('충격 + 걸음 수', '기울기 = 두 사람 공통, a = 본인 보정 평균')][p]
        g = guide_segments(PEOPLE[p]['session'])
        g = g[g['mode'] == 2]
        e = ticks(PEOPLE[p]['session'])
        e = e[(e.t >= g.ts.min()) & (e.t <= g.t1.max())].reset_index(drop=True)
        e['state'] = state(e.cad.values, e.q.values, e.impact.values)
        run = predict(e, model[0], model[1], True)
        raw = np.where(e.state == 2, run, np.where(e.state == 1, a_w + b_w * e.cad, np.nan))
        limp = np.log(e.impact.clip(lower=1e-3)).values
        outs = {'틱 그대로': raw, '앱 지금(최근 3틱 중앙값)': median3(raw), '계단식 묶음': step_hold(e.cad.values, limp, raw)}
        truth = np.full(len(e), np.nan)
        for _, s in g.iterrows():
            truth[(e.t >= s.t0) & (e.t < s.t1)] = s.v
        for label, o in outs.items():
            m = ~np.isnan(truth)
            err = o[m] - truth[m]
            # 속도를 맞춘 뒤(기록 시작) 0.5 이내로 들어오기까지 걸린 시간
            settle = []
            for _, s in g.iterrows():
                idx = np.where((e.t >= s.t0) & (e.t < s.t1))[0]
                ok = np.abs(o[idx] - s.v) <= 0.5
                # 이후 계속 0.5 안에 머무는 첫 시점
                good = np.where(np.flip(np.cumprod(np.flip(ok))))[0]
                settle.append(e.t[idx[good[0]]] - s.t0 if len(good) else s.t1 - s.t0)
            print(f'  {p} {label:<22} {summ(err)} | 0.5 안에 자리 잡기까지 중앙 {np.median(settle):.1f}초')

    print('\n== 6. 에어팟 (B, 계기판 아이폰 100 Hz, 1명·1회) — docs/speed/airpods_flight.md 표와 같은 계산')
    airpods_block()


def airpods_block():
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import soundtrackdb as sdb
    run = ROOT / 'data/runs/20261029-025507'
    meta = json.load(open(run / 'meta.json', encoding='utf-8'))
    t0 = meta['t0']

    def load(name):
        d = pd.read_csv(run / 'phone' / f'{name}.csv').drop_duplicates('t').sort_values('t')
        d['t'] -= t0
        return d.t.values, d[['x', 'y', 'z']].values

    known = {s['index']: s['start_s'] for s in meta['segments']}
    idx = np.array(sorted(known))
    step = np.polyfit(idx, [known[i] for i in idx], 1)
    tp, ap = load('iphone_acc')
    _, env, _ = sdb.envelopes(tp, ap)
    tenv = tp[0] + np.arange(len(env)) / sdb.ENV_FS
    ta, aa = load('airpods_acc')
    _, ag = load('airpods_grav')
    mag = np.linalg.norm(aa + ag, axis=1) / 9.80665
    bad = np.zeros(len(ta), bool)
    gi = np.where(np.diff(ta) > 0.15)[0]
    bad[gi] = bad[gi + 1] = True
    rows = []
    for i, st in enumerate(meta['stages']):
        s = known.get(i, np.polyval(step, i)) + 10
        e = known.get(i + 1, np.polyval(step, i + 1)) if i + 1 < len(meta['stages']) else meta['end_s']
        x = env[(tenv >= s) & (tenv < e)]
        cad, _ = sdb.cadence(x)
        m = mag[(ta >= s) & (ta < e) & ~bad]
        rows.append(dict(v=st['speed_kmh'], lnimp=np.log(sdb.impact(x)), cad=cad,
                         amp=np.percentile(m, 95) - np.percentile(m, 5), low=(m < 0.5).mean() * 60 / cad * 1000))
    d = pd.DataFrame(rows)
    print('  걷기·달리기 판단: 0.5 g 아래 시간(ms/걸음) 7 km/h(걷기) ' + ', '.join(f'{x:.0f}' for x in d[d.v == 7].low)
          + ' / 8 이상(달리기) 최소 ' + f'{d[d.v >= 8].low.min():.0f}')
    r = d[d.v >= 8].reset_index(drop=True)

    def loo(cols):
        e = []
        for j in range(len(r)):
            tr = r.drop(j)
            X = np.c_[np.ones(len(tr)), tr[cols].values]
            c = np.linalg.lstsq(X, tr.v.values, rcond=None)[0]
            e.append(np.r_[1, r.loc[j, cols].values] @ c - r.v[j])
        return e
    for label, cols in (('충격만', ['lnimp']), ('충격 + 걸음 수', ['lnimp', 'cad']),
                        ('충격 + 에어팟 흔들림 폭', ['lnimp', 'amp']), ('충격 + 0.5 g 아래 시간', ['lnimp', 'low'])):
        print(f'  달리기 8에서 10, 구간 하나씩 빼고 [{label}] {summ(loo(cols))}')


if __name__ == '__main__':
    main()
