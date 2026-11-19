"""계획 0060 ①: 달리기 속도를 충격 중심(지금 앱)에서 "걸음 수 × 보폭"으로 바꾸면 어떤가 — PC 분석.

실행: .venv\\Scripts\\python speedlab/exp/stride_model.py
입력: data/external/nijs2021/raw (공개 데이터, git 제외), data/runs/20261029-025507 (B), 20261029-040156 (M)
출력: 콘솔 표 + docs/speed/img/0060_*.png

1. Nijs 2021(이어버드 + 힘판, 공개 데이터 재분석): 계획 0012 규칙(착지 수직 0.2 g 위로, 발 떼기 0.3 g 아래로)으로
   잰 공중·발 닿는 시간을 보폭 식에 넣으면 걸음 수만보다 나은가. 여기서 **여러 사람 공통 보폭 기울기**를 얻음
2. 우리 에어팟 기록: 기록 앞 보정 구간(올라가는 순서)만으로 사람 값을 맞추고 **뒤 구간**을 0.5초마다 맞힘
   (6초 창, 최근 2초 중앙값 = 앱과 같은 틱). 속도 바뀐 뒤 10초는 "안정" 오차에서 뺌
3. 계단식 변화 판정(러닝머신 속도는 버튼으로만 바뀜): 후보 값이 고정값에서 한쪽으로 2초 넘게 벗어날 때만 새 값
4. 세게 밟기 민감도: 충격만 1.5배가 될 때 각 모델이 움직이는 양(식에서 바로). 실제로 밟을 때 공중·흔들림도
   같이 변하는지는 흔들림 시험 기록(계획 0060 ②)에서
"""
import contextlib
import io
import json
import sys
from pathlib import Path

import numpy as np
import pandas as pd

sys.path.insert(0, str(Path(__file__).resolve().parent))
import airpods_events as ae  # noqa: E402
import earbud_events as em  # noqa: E402
import soundtrackdb as sdb  # noqa: E402
from live_eval import true_speed  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
G = 9.80665
WIN_S, TICK_S, SMOOTH_TICKS = 6.0, 0.5, 4     # 앱과 같음: 6초 창, 0.5초마다, 최근 2초 중앙값
SETTLE_S = 10.0                               # 속도 바뀐 뒤 이만큼은 안정 오차에서 뺌
CAL_SKIP_S = 5.0                              # 보정 구간 앞에서 버림(앱 calSkipS)
AIR_D = 2.598                                 # 지금 앱의 흔들림 폭 비중(km/h per g)
MIN_STEPS = 5                                 # 창 안 에어팟 걸음이 이보다 적으면 공중·발 닿는 시간 없음
STOMP = np.log(1.5)                           # 세게 밟기: 충격 1.5배
CP_DELTA, CP_HOLD_TICKS = 0.4, 4
STOMP_S = 10.0                                # 세게 밟기 흉내 길이              # 계단식: 고정값에서 0.4 km/h 넘게, 2초(4틱) 연속 같은 쪽이면 새 값


# ---------- 1. Nijs: 보폭 식과 공통 기울기 ----------
def nijs_segments():
    with contextlib.redirect_stdout(io.StringIO()):
        rows = em.load_all()
        hs_rule, hs_bias = em.pick(em.event_table(rows, 'e_hs'), '착지')
        to_rule, to_bias = em.pick(em.event_table(rows, 'e_to'), '발 떼기')
    print(f'Nijs 규칙(계획 0012와 같아야 함): 착지 {hs_rule} 치우침 {hs_bias:+.0f} ms, 발 떼기 {to_rule} 치우침 {to_bias:+.0f} ms')
    recs = []
    for r in rows:
        if np.isnan(r['v']) or hs_rule not in r['hs_next'] or to_rule not in r['to_eb'] or hs_rule not in r['e_hs']:
            continue
        to_eb = r['to_eb'][to_rule] - to_bias / 1000
        hs_eb = r['hs'] + r['e_hs'][hs_rule] - hs_bias / 1000
        recs.append(dict(pid=r['pid'], sess=r['sess'], v=r['v'], step=r['hs1'] - r['hs'],
                         fl=(r['hs1'] - r['to']) * 1000, st=(r['to'] - r['hs']) * 1000,
                         fl_eb=((r['hs_next'][hs_rule] - hs_bias / 1000) - to_eb) * 1000, st_eb=(to_eb - hs_eb) * 1000))
    d = pd.DataFrame(recs)
    d = d[(d.fl_eb > 0) & (d.fl_eb < 500) & (d.st_eb > 50) & (d.st_eb < 500)]
    s = d.groupby(['pid', 'sess', 'v']).agg(step=('step', 'mean'), fl=('fl', 'mean'), st=('st', 'mean'),
                                           fl_eb=('fl_eb', 'mean'), st_eb=('st_eb', 'mean'), n=('fl', 'size')).reset_index()
    s['cad'] = 60 / s.step
    s['L'] = s.v / 3.6 / (s.cad / 60)          # 걸음 하나 길이(m)
    return s[s.groupby(['pid', 'sess']).v.transform(lambda x: set(x) >= {7, 10, 13})].reset_index(drop=True)


def within(s, cols):
    """사람·회차 평균을 뺀 사람 안 변화"""
    return s[cols] - s.groupby(['pid', 'sess'])[cols].transform('mean')


def pooled_k(s, feats):
    """보폭 L = L0(사람) + feats·k 의 공통 k(사람 안 변화로 맞춤)"""
    if not feats:
        return np.zeros(0)
    w = within(s, feats + ['L'])
    return np.linalg.lstsq(w[feats].values, w.L.values, rcond=None)[0]


def nijs_report(s):
    print(f'\n== 1. Nijs 2021 (공개 데이터 재분석, 사람·회차 {s.groupby(["pid", "sess"]).ngroups}개, 7·10·13 km/h)')
    w = within(s, ['v', 'fl_eb', 'st_eb', 'fl', 'st', 'cad', 'L'])
    for c, lab in (('cad', '걸음 수'), ('fl', '공중(정답)'), ('fl_eb', '공중(이어버드)'), ('st', '발 닿음(정답)'),
                   ('st_eb', '발 닿음(이어버드)')):
        k = np.polyfit(s.v - s.groupby(['pid', 'sess']).v.transform('mean'), w[c], 1)[0]
        print(f'  사람 안 {lab:<12} 속도와 상관 {np.corrcoef(w.v, w[c])[0, 1]:+.2f}, 1 km/h당 {k:+.1f}')
    print(f'  발 닿음 이어버드 ↔ 정답(사람 안) 상관 {np.corrcoef(w.st_eb, w.st)[0, 1]:+.2f}')
    # 10 km/h 한 점 보정 → 7·13 맞히기, 공통 k는 이 사람 빼고(사람 안 변화)
    sets = [('걸음 수만(보폭 일정)', []), ('+ 공중(이어버드)', ['fl_eb']), ('+ 발 닿음(이어버드)', ['st_eb']),
            ('+ 공중 + 발 닿음(이어버드)', ['fl_eb', 'st_eb']), ('+ 공중 + 발 닿음(정답 힘판)', ['fl', 'st'])]
    print('  보폭 식 v = 걸음 수 × (L0 + k·지표), 10 km/h 한 점으로 L0 → 7·13 맞히기 (오차 중앙 / 평균 km/h)')
    for lab, feats in sets:
        err = []
        for (pid, sess), q in s.groupby(['pid', 'sess']):
            k = pooled_k(s[s.pid != pid], feats)
            base = q[q.v == 10].iloc[0]
            L0 = base.L - (base[feats].values.astype(float) @ k if feats else 0)
            for _, x in q[q.v != 10].iterrows():
                L = L0 + (x[feats].values.astype(float) @ k if feats else 0)
                err.append(abs(x.cad * L * 60 / 1000 - x.v))
        print(f'    {lab:<26} {np.median(err):.2f} / {np.mean(err):.2f}')
    return {tuple(f): pooled_k(s, f) for _, f in sets}


# ---------- 2. 우리 기록: 0.5초 틱 특징 ----------
def timeline_B():
    run = ROOT / 'data/runs/20261029-025507'
    meta = json.load(open(run / 'meta.json', encoding='utf-8'))
    known = {s['index']: s['start_s'] for s in meta['segments']}
    idx = np.array(sorted(known))
    step = np.polyfit(idx, [known[i] for i in idx], 1)
    starts = [known.get(i, np.polyval(step, i)) for i in range(len(meta['stages']))]
    speeds = [st['speed_kmh'] for st in meta['stages']]
    # 보정 = 앞 올라가는 8·9·10(B는 7에서 걸음 → 달리기 보정점이 아님)
    cal = [(speeds[i], starts[i] + CAL_SKIP_S, starts[i + 1]) for i in (1, 2, 3)]
    return run / 'phone', meta['t0'], list(zip(starts, speeds)), meta['end_s'], cal, starts[4], 8


def timeline_M():
    d = ROOT / 'data/runs/20261029-040156/phone'
    stage, mark = pd.read_csv(d / 'stage.csv'), pd.read_csv(d / 'mark.csv')
    t0 = stage.t.iloc[0]
    calr = stage[stage.set == 1].reset_index(drop=True)
    free0 = stage.t[stage.set == 3].iloc[0] - t0
    ch = [(r.t - t0, r.speed) for r in calr.itertuples()]
    ch += [(s - t0, v) for s, e, v in true_speed(mark, stage.t.iloc[-1])]
    cal = [(r.speed, r.t - t0 + CAL_SKIP_S, (calr.t[i + 1] if i + 1 < len(calr) else free0 + t0) - t0)
           for i, r in calr.iterrows()]
    return d, t0, ch, stage.t.iloc[-1] - t0, cal, free0, 7


def air_steps(d, t0):
    """에어팟 걸음마다 (봉우리 시각, 공중 ms, 발 닿음 ms) — 계획 0012 규칙·치우침 그대로"""
    ta, vert = ae.vertical(d, t0)
    out = []
    for tu, v in ae.chunks(ta, vert):
        c = em.candidates(tu, v)
        for k in range(len(c) - 1):
            hs, to, hs1 = c[k][1].get('thr0.2'), c[k][2].get('thr0.3'), c[k + 1][1].get('thr0.2')
            if hs is None or to is None or hs1 is None:
                continue
            to_, hs_, hs1_ = to + 0.016, hs + 0.002, hs1 + 0.002
            fl, st = (hs1_ - to_) * 1000, (to_ - hs_) * 1000
            if 0 < fl < 500 and 50 < st < 500:
                out.append((c[k][0], fl, st))
    return np.array(out).reshape(-1, 3)


def features(d, t0, t_end):
    tp, ap = ae.load(d, 'iphone_acc', t0)
    _, env, _ = sdb.envelopes(tp, ap)
    tenv = tp[0] + np.arange(len(env)) / sdb.ENV_FS
    ta, aa = ae.load(d, 'airpods_acc', t0)
    _, ag = ae.load(d, 'airpods_grav', t0)
    mag = np.linalg.norm(aa + ag, axis=1) / G
    steps = air_steps(d, t0)

    def at(t0w, t1w):
        x = env[(tenv >= t0w) & (tenv < t1w)]
        if len(x) < sdb.ENV_FS * 3:
            return None
        cad, q = sdb.cadence(x)
        imp = sdb.impact(x)
        m = mag[(ta >= t0w) & (ta < t1w)]
        sw = steps[(steps[:, 0] >= t0w) & (steps[:, 0] < t1w)]
        ok = len(sw) >= MIN_STEPS
        return dict(cad=cad, q=q, lnimp=np.log(imp) if imp > 0 else np.nan,
                    amp=np.percentile(m, 95) - np.percentile(m, 5) if len(m) >= 10 else np.nan,
                    fl=np.median(sw[:, 1]) if ok else np.nan, st=np.median(sw[:, 2]) if ok else np.nan,
                    st2=60000 / cad - np.median(sw[:, 1]) if ok and cad > 0 else np.nan)   # 폰 걸음 시간 − 에어팟 공중

    rows = []
    for t in np.arange(WIN_S, t_end, TICK_S):
        f = at(t - WIN_S, t)
        if f:
            rows.append(dict(t=t, **f))
    return pd.DataFrame(rows), at


def truth_at(t, changes):
    v, since = np.nan, np.nan
    for s, sp in changes:
        if s <= t:
            v, since = sp, t - s
    return v, since


# ---------- 모델 ----------
def fit_line(x, y, b_default):
    x, y = np.asarray(x, float), np.asarray(y, float)
    if len(x) >= 2 and np.ptp(x) > 0.05:
        b = np.polyfit(x, y, 1)[0]
    else:
        b = b_default
    return y.mean() - b * x.mean(), b


def make_models(cal, k):
    """보정점(구간 특징)으로 모델들 → {이름: (예측 함수, 충격 1.5배 민감도 km/h)}"""
    c = pd.DataFrame(cal)
    c['L'] = c.v / 3.6 / (c.cad / 60)
    a0, b0 = fit_line(c.lnimp, c.v - AIR_D * c.amp, 4.828)
    a1, b1 = fit_line(c.lnimp, c.v, 6.396)
    m = {'지금 앱(충격 + 흔들림 폭)': (lambda f: a0 + b0 * f.lnimp + AIR_D * f.amp, b0 * STOMP),
         '충격만': (lambda f: a1 + b1 * f.lnimp, b1 * STOMP)}

    def stride(feats, kk, name):
        L0 = (c.L - (c[feats].values @ kk if feats else 0)).mean()
        m[name] = (lambda f: f.cad / 60 * (L0 + (f[feats].values @ kk if feats else 0)) * 3.6, 0.0)
    stride([], np.zeros(0), '걸음 수 × 보폭(보폭 일정)')
    stride(['fl'], k[('fl_eb',)], '걸음 수 × 보폭(공중)')
    stride(['fl', 'st'], k[('fl_eb', 'st_eb')], '걸음 수 × 보폭(공중 + 발 닿음)')
    # 비교: 보폭 기울기를 이 사람 보정점으로(3–4점에 값 2개)
    kf = np.polyfit(c.fl, c.L, 1)[0] if c.fl.notna().all() and len(c) >= 2 else 0.0
    stride(['fl'], np.array([kf]), '걸음 수 × 보폭(공중, 기울기도 이 사람)')
    ks = np.polyfit(c.st, c.L, 1)[0] if c.st.notna().all() and len(c) >= 2 else 0.0
    stride(['st'], np.array([ks]), '걸음 수 × 보폭(발 닿음, 기울기도 이 사람)')
    ks2 = np.polyfit(c.st2, c.L, 1)[0] if c.st2.notna().all() and len(c) >= 2 else 0.0
    stride(['st2'], np.array([ks2]), '걸음 수 × 보폭(폰 걸음 − 에어팟 공중, 이 사람)')
    # 충격 의존만 조금 줄이기: 지금 앱과 보폭 모델을 섞음(w = 보폭 몫). 세게 밟기 민감도는 (1 − w)배
    app, app_s = m['지금 앱(충격 + 흔들림 폭)']
    for other in ('걸음 수 × 보폭(발 닿음, 기울기도 이 사람)', '걸음 수 × 보폭(폰 걸음 − 에어팟 공중, 이 사람)',
                  '걸음 수 × 보폭(보폭 일정)'):
        fo = m[other][0]
        short = '발 닿음' if '발 닿음' in other else ('폰−공중' if '폰 걸음' in other else '걸음 수만')
        for w in BLEND_W:
            m[f'섞기 {short} {w:.0%}'] = ((lambda f, fo=fo, w=w: (1 - w) * app(f) + w * fo(f)), (1 - w) * app_s)
    return m


BLEND_W = (0.25, 0.4, 0.5)


def changepoint(x):
    """러닝머신 속도는 계단식: 고정값 h에서 같은 쪽으로 CP_HOLD_TICKS 연속 CP_DELTA 넘게 벗어나면 새 값.
    고정 동안 h = 고정 시작 뒤 후보들의 평균(점점 매끄럽게)"""
    out, h, buf, side = [], np.nan, [], []
    for i, v in enumerate(x):
        if np.isnan(v):          # 걷기·값 없음 → 앱처럼 처음부터
            h, side = np.nan, []
            out.append(h)
            continue
        if np.isnan(h):
            buf = [v]
        else:
            side = (side + [np.sign(v - h) if abs(v - h) > CP_DELTA else 0])[-CP_HOLD_TICKS:]
            if len(side) == CP_HOLD_TICKS and abs(sum(side)) == CP_HOLD_TICKS:
                buf, side = [y for y in x[i - CP_HOLD_TICKS + 1:i + 1] if not np.isnan(y)], []
            else:
                buf.append(v)
        h = float(np.mean(buf))
        out.append(h)
    return np.array(out)


def consensus(A, T, idx_ok=None):
    """합의형: 값은 충격 쪽 A, 바뀜은 시간 지표 쪽 T도 같은 쪽으로 움직였을 때만 인정.
    A가 고정값에서 같은 쪽으로 CP_HOLD_TICKS 연속 CP_DELTA 넘게 벗어나면 후보 → 그동안 T의 평균이 고정 동안 T 평균보다
    같은 쪽으로 CONS_FRAC × (A 변화) 넘게 움직였으면 새 값(T가 없으면 지금 앱처럼 그냥 바꿈). 고정 동안 값 = 고정 뒤 A의 중앙값(세게 밟은 시간이 절반 밑이면 안 끌림)"""
    out, h, bufA, bufT, side = [], np.nan, [], [], []
    for i, (a, t) in enumerate(zip(A, T)):
        if np.isnan(a):          # 걷기·값 없음 → 앱처럼 처음부터
            h, side = np.nan, []
            out.append(h)
            continue
        if np.isnan(h):
            bufA, bufT = [a], [t]
        else:
            side = (side + [np.sign(a - h) if abs(a - h) > CP_DELTA else 0])[-CP_HOLD_TICKS:]
            jump = len(side) == CP_HOLD_TICKS and abs(sum(side)) == CP_HOLD_TICKS
            if jump:
                newA = [y for y in A[i - CP_HOLD_TICKS + 1:i + 1] if not np.isnan(y)]
                newT = [y for y in T[i - CP_HOLD_TICKS + 1:i + 1] if not np.isnan(y)]
                dA = np.mean(newA) - h
                tv = [y for y in bufT if not np.isnan(y)]
                dT = (np.mean(newT) - np.mean(tv)) if newT and tv else np.nan
                if np.isnan(dT) or (np.sign(dT) == np.sign(dA) and abs(dT) >= CONS_FRAC * abs(dA)):
                    bufA, bufT, side = newA, newT, []
                else:
                    jump = False
            if not jump:
                bufA.append(a)
                bufT.append(t)
        h = float(np.median(bufA))
        out.append(h)
    return np.array(out)


CONS_FRAC = 0.5


def run_models(models, ft):
    """모델마다 2초 중앙값 → {이름: 값 배열}, 계단식·합의형 포함"""
    res, sens = {}, {}
    for mname, (fn, sv) in models.items():
        res[mname] = pd.Series(fn(ft).values.astype(float)).rolling(SMOOTH_TICKS, min_periods=1).median().values
        sens[mname] = sv
    run = (ft.cad >= 150).values                    # 걷기 틱은 달리기 식 대상 아님(앱: 걸음 수 150, 에어팟 없을 때 기준)
    A = np.where(run, res['지금 앱(충격 + 흔들림 폭)'], np.nan)
    res['지금 앱 + 계단식'] = changepoint(A)
    res['합의형(지금 앱 값, 발 닿음 보폭이 같은 쪽일 때만 바꿈)'] = consensus(
        A, res['걸음 수 × 보폭(발 닿음, 기울기도 이 사람)'])
    res['합의형(지금 앱 값, 걸음 수가 같은 쪽일 때만 바꿈)'] = consensus(
        A, res['걸음 수 × 보폭(보폭 일정)'])
    T = np.where(run, res['걸음 수 × 보폭(폰 걸음 − 에어팟 공중, 이 사람)'], np.nan)
    for tau in BIAS_TAU_S:
        res[f'치우침 고치기 {tau:.0f}초'] = bias_fix(A, T, tau, 0.0)
    res[f'섞기 폰−공중 50% + 치우침 고치기 {BIAS_TAU_S[-1]:.0f}초'] = bias_fix(A, T, BIAS_TAU_S[-1], 0.5)
    return res, sens


BIAS_TAU_S = (20.0, 60.0)


def bias_fix(A, T, tau_s, w):
    """값은 충격(A)으로, A가 보폭(T)과 오래 어긋나면 그 차이(치우침 b)를 시간 상수 tau_s로 천천히 따라가 뺌.
    짧게 흔들리는 T 잡음은 평균으로 사라지고, 충격이 천천히 흘러가는 것(지침·다른 날)은 고쳐짐. 출력 = (1 − w)(A − b) + w·T"""
    a = TICK_S / tau_s
    b, out = 0.0, []
    for x, t in zip(A, T):
        if np.isnan(x):
            out.append(np.nan)
            continue
        if not np.isnan(t):
            b += a * ((x - t) - b)
        out.append((1 - w) * (x - b) + (w * t if not np.isnan(t) else w * (x - b)))
    return np.array(out)


def drift_test(models, ft, test):
    """흉내: 뒤 구간 내내 충격이 서서히 커짐(끝에서 DRIFT배, 지침 가설). 안정 틱 |오차| 중앙"""
    f2 = ft.copy()
    t0, t1 = test.t.min(), test.t.max()
    f2['lnimp'] += np.clip((f2.t - t0) / (t1 - t0), 0, 1) * np.log(DRIFT)
    r2, _ = run_models(models, f2)
    steady = test.since >= SETTLE_S
    return {k: float(np.nanmedian(np.abs(pd.Series(v, index=ft.index)[test.index][steady] - test.v[steady]))) for k, v in r2.items()}


DRIFT = 1.3


def stomp_test(models, ft, test):
    """흉내: 안정 구간마다 STOMP_S 동안 충격만 1.5배(공중·발 닿음·흔들림·걸음 수는 그대로라는 가정) → 추정이 움직인 최대 폭"""
    base, _ = run_models(models, ft)
    moved = {k: [] for k in base}
    for _, g in test.groupby((test.v != test.v.shift()).cumsum()):
        s0 = g.t.iloc[0] - g.since.iloc[0]               # 이 속도 시작
        if g.t.iloc[-1] - s0 < SETTLE_S + STOMP_S + 3:
            continue
        a, b = s0 + SETTLE_S, s0 + SETTLE_S + STOMP_S
        f2 = ft.copy()
        hit = (f2.t >= a + 3) & (f2.t < b + 3)          # 6초 창에서 밟은 걸음이 절반 넘는 틱(가정)
        f2.loc[hit, 'lnimp'] += STOMP
        r2, _ = run_models(models, f2)
        span = (ft.t >= a) & (ft.t < min(b + 8, g.t.iloc[-1]))
        for k in base:
            moved[k].append(np.nanmax(np.abs(r2[k][span] - base[k][span])))
    return {k: (np.mean(v) if v else np.nan) for k, v in moved.items()}, len(next(iter(moved.values())))


def evaluate(name, timeline, k):
    d, t0, changes, t_end, cal, test0, vmin = timeline()
    ft, at = features(d, t0, t_end)
    cp = pd.DataFrame([dict(v=v, **at(s, e)) for v, s, e in cal])
    # 보정점의 공중·발 닿음은 구간 전체 걸음 중앙값(창 6초보다 김)
    steps = air_steps(d, t0)
    for i, (v, s, e) in enumerate(cal):
        sw = steps[(steps[:, 0] >= s) & (steps[:, 0] < e)]
        cp.loc[i, 'fl'], cp.loc[i, 'st'] = np.median(sw[:, 1]), np.median(sw[:, 2])
        cp.loc[i, 'st2'] = 60000 / cp.cad[i] - cp.fl[i]
    models = make_models(cp, k)
    tv = np.array([truth_at(t, changes) for t in ft.t])
    ft['v'], ft['since'] = tv[:, 0], tv[:, 1]
    test = ft[(ft.t >= test0) & (ft.v >= vmin)].copy()
    print(f'\n== 2. {name}: 보정 ' + ', '.join(f'{r.v:.0f}(걸음 {r.cad:.0f}, 공중 {r.fl:.0f}, 닿음 {r.st:.0f} ms)'
                                          for r in cp.itertuples())
          + f' → 뒤 달리기 틱 {len(test)}개({test.v.nunique()}가지 속도)')
    w = test[test.since >= SETTLE_S]
    print(f'  뒤 안정 틱의 6초 창 특징 흔들림(같은 속도 안 표준편차 중앙): 걸음 수 '
          f'{w.groupby("v").cad.std().median():.1f} spm, 공중 {w.groupby("v").fl.std().median():.1f} ms, '
          f'발 닿음 {w.groupby("v").st.std().median():.1f} ms, 폰 걸음 − 에어팟 공중 {w.groupby("v").st2.std().median():.1f} ms, '
          f'폰 걸음 시간 {(60000 / w.cad).groupby(w.v).std().median():.1f} ms, ln(충격) {w.groupby("v").lnimp.std().median():.3f}')
    res, sens = run_models(models, ft)
    moved, nseg = stomp_test(models, ft, test)
    drift = drift_test(models, ft, test)
    print(f'  {"모델":<44} 안정 틱 |오차| 중앙 / 평균 / 최대 | 반응(바뀐 뒤 0.5 안으로, 초) | 세게 밟기 흉내 {nseg}번 평균 움직임'
          f' | 지침 흉내(충격 서서히 {DRIFT}배) 오차 중앙')
    for tag, est in res.items():
        e = pd.Series(est, index=ft.index)[test.index] - test.v
        steady = e[test.since >= SETTLE_S]
        # 반응: 속도 바뀐 뒤 처음으로 0.5 안에 들어와 3초 이어진 때
        lag = []
        for _, g in test.assign(e=e).groupby((test.v != test.v.shift()).cumsum()):
            ok = (g.e.abs() < 0.5).rolling(6).sum() == 6
            lag.append(g.since[ok].iloc[0] - 2.5 if ok.any() else np.nan)
        lag = np.array(lag)
        res[tag] = est
        print(f'  {tag:<44} {steady.abs().median():.2f} / {steady.abs().mean():.2f} / {steady.abs().max():.2f}'
              f' | 중앙 {np.nanmedian(lag):.1f}, 못 들어옴 {int(np.isnan(lag).sum())}/{len(lag)}'
              f' | {moved[tag]:+.2f} km/h | {drift[tag]:.2f}')
    return ft, test, res, changes


def plot(results):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    from matplotlib import font_manager
    have = {f.name for f in font_manager.fontManager.ttflist}
    plt.rcParams['font.family'] = next((f for f in ('AppleGothic', 'Malgun Gothic') if f in have), 'sans-serif')
    plt.rcParams['axes.unicode_minus'] = False
    fig, axs = plt.subplots(len(results), 1, figsize=(12, 4 * len(results)))
    show = [('지금 앱(충격 + 흔들림 폭)', '#d1495b'), ('걸음 수 × 보폭(발 닿음, 기울기도 이 사람)', '#1f6feb'),
            ('합의형(지금 앱 값, 발 닿음 보폭이 같은 쪽일 때만 바꿈)', '#2a9d8f')]
    for ax, (name, (ft, test, res, changes)) in zip(np.atleast_1d(axs), results.items()):
        ax.step(ft.t, ft.v, where='post', color='black', lw=1.5, label='계기판(정답)')
        for k, c in show:
            ax.plot(ft.t, res[k], color=c, lw=1, label=k)
        ax.axvline(test.t.min(), color='gray', ls=':')
        ax.set_ylim(3, 13)
        ax.set_title(f'{name} (점선 왼쪽 = 보정)')
        ax.set_xlabel('기록 시각 (초)')
        ax.set_ylabel('km/h')
        ax.legend(loc='lower right', fontsize=8)
    fig.tight_layout()
    out = ROOT / 'docs/speed/img/0060_stride.png'
    fig.savefig(out, dpi=110)
    print('\n그림:', out.relative_to(ROOT))


def main():
    s = nijs_segments()
    k = nijs_report(s)
    print('  공통 보폭 기울기(m per ms): 공중 ' + f'{k[("fl_eb",)][0] * 1000:.2f} mm/ms, 공중 + 발 닿음 '
          + ', '.join(f'{x * 1000:.2f}' for x in k[('fl_eb', 'st_eb')]) + ' mm/ms')
    results = {}
    for name, tl in (('B 고상준', timeline_B), ('M 민재', timeline_M)):
        results[name] = evaluate(name, tl, k)
    plot(results)


if __name__ == '__main__':
    main()
