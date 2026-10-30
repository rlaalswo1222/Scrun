"""계획 0012: 이어버드 + 힘판 공개 데이터(Nijs 외 2021)로 착지·발 떼기 규칙을 정답에 맞춤 → 우리 에어팟 기록에 적용.

실행: .venv/bin/python speedlab/exp/earbud_events.py   (Windows: .venv\\Scripts\\python)
입력: data/external/nijs2021/raw/Data_P??_speeds_?.csv (Europe PMC PMC8659722 부록, git 제외)
      열: time(500 Hz), S7kmh_force_plate_1..3(N, 3 = 수직), S7kmh_earbuds_1..3(g, 왼쪽 귀), S10kmh, S13kmh, max
출력: 콘솔 표 + docs/speed/img/0012_*.png

- 정답(논문과 같음): 수직 힘이 체중의 25%를 위로 넘으면 착지, 아래로 내려가면 발 떼기. 체중 = 1분 평균 수직 힘
- 이어버드는 20 Hz 저역 필터 → 50 Hz로 솎음(에어팟 조건). 수직 = 0.3 Hz 저역 필터로 구한 중력 방향에 투영
- 착지·발 떼기 후보 규칙을 학습 7명(P01에서 P07)으로 고르고(치우침도 학습 사람으로 뺌) 시험 7명(P08에서 P14)으로 오차
- 논문(Nijs)처럼 시작·끝 5초는 버리고 가운데 50초
"""
import sys
from pathlib import Path

import numpy as np
import pandas as pd
from scipy.signal import butter, filtfilt, find_peaks

ROOT = Path(__file__).resolve().parents[2]
RAW = ROOT / 'data/external/nijs2021/raw'
FS_RAW = 500.0
FS = 50.0
TRAIN = {f'P{i:02d}' for i in range(1, 8)}
SPEEDS = {'S7kmh': 7, 'S10kmh': 10, 'S13kmh': 13, 'max': np.nan}   # max = 사람마다 최고(16까지), 값은 데이터에 없음
LEVELS = (0.2, 0.3, 0.4, 0.5, 0.6, 0.8, 1.0)                     # 문턱 후보(g)


def lp(x, hz, fs, order=4):
    b, a = butter(order, hz / (fs / 2))
    return filtfilt(b, a, x, axis=0)


def crossings(t, x, thr, up):
    """x가 thr를 위로(up)/아래로 지나는 시각(선형 보간)."""
    s = x - thr
    i = np.where((s[:-1] < 0) & (s[1:] >= 0))[0] if up else np.where((s[:-1] >= 0) & (s[1:] < 0))[0]
    return t[i] + (t[i + 1] - t[i]) * (-s[i]) / (s[i + 1] - s[i])


def truth(t, fz):
    """힘판 착지·발 떼기 → 걸음 [(착지, 발 떼기, 다음 착지)]"""
    fz = lp(fz, 30, FS_RAW)
    thr = 0.25 * fz.mean()
    hs, to = crossings(t, fz, thr, True), crossings(t, fz, thr, False)
    steps = []
    for k in range(len(hs) - 1):
        o = to[(to > hs[k]) & (to < hs[k + 1])]
        if len(o) == 1 and 0.1 < o[0] - hs[k] < 0.5:
            steps.append((hs[k], o[0], hs[k + 1]))
    return np.array(steps).reshape(-1, 3)


def vertical50(acc):
    """500 Hz 3축(g) → 50 Hz 수직(g, 서 있으면 +1)."""
    gdir = lp(acc, 0.3, FS_RAW, 2)
    gdir /= np.linalg.norm(gdir, axis=1, keepdims=True)
    v = np.sum(acc * gdir, axis=1)
    v = lp(v, 20, FS_RAW)[::int(FS_RAW / FS)]
    return v if v.mean() > 0 else -v


def parabolic(y, i):
    if i <= 0 or i >= len(y) - 1:
        return float(i)
    a, b, c = y[i - 1], y[i], y[i + 1]
    den = a - 2 * b + c
    return i + (0.5 * (a - c) / den if den != 0 else 0.0)


def candidates(tu, v):
    """디딤 봉우리마다 착지·발 떼기 후보 시각들. 반환: 봉우리 시각, {규칙: 착지}, {규칙: 발 떼기}"""
    x0 = v - v.mean()
    ac = np.correlate(x0, x0, 'full')[len(x0) - 1:]
    lo, hi = int(0.25 * FS), int(0.6 * FS)
    lag = lo + int(np.argmax(ac[lo:hi]))
    pk, _ = find_peaks(v, distance=max(1, int(0.6 * lag)))
    d2 = np.gradient(np.gradient(v))
    dj = np.gradient(np.gradient(lp(v, 10, FS)))
    nh, nt = int(0.15 * FS), int(0.25 * FS)
    rows = []
    for a, p in enumerate(pk[:-1]):
        q = pk[a + 1]
        if p - nh < 1 or q >= len(v) - 1:
            continue
        hs, to = {}, {}
        # Stuchbury-Wass 식(계획 0011에서 쓴 것): 착지 = 봉우리 앞 2차 미분 최대, 발 떼기 = 봉우리 뒤 저크 미분 최소
        i = p - nh + int(np.argmax(d2[p - nh:p + 1]))
        hs['d2max'] = tu[0] + parabolic(d2, i) / FS
        j = p + 1 + int(np.argmin(dj[p + 1:min(p + nt, q) + 1]))
        to['djmin'] = tu[0] + parabolic(-dj, j) / FS
        # 저크 미분의 국소 최솟값들 중 두 번째(봉우리 꼭대기 다음), 국소 최댓값(내려와 평평해지는 무릎)
        seg = dj[p + 1:q]
        mins = np.where((seg[1:-1] < seg[:-2]) & (seg[1:-1] <= seg[2:]))[0] + 1
        if len(mins) >= 2:
            to['djmin2'] = tu[0] + parabolic(-dj, p + 1 + mins[1]) / FS
        k = p + 1 + int(np.argmax(seg))
        to['djmax'] = tu[0] + parabolic(dj, k) / FS
        # 문턱(Nijs 식 "평평한 구간 경계"를 수직에서): 봉우리 뒤 처음 아래로, 봉우리 앞 마지막 위로
        for L in LEVELS:
            dn = crossings(tu[p:q + 1], v[p:q + 1], L, False)
            if len(dn):
                to[f'thr{L}'] = dn[0]
            st = max(0, a and pk[a - 1])
            up = crossings(tu[st:p + 1], v[st:p + 1], L, True)
            if len(up):
                hs[f'thr{L}'] = up[-1]
        rows.append((tu[p], hs, to))
    return rows


def segment(pid, sess, key, df):
    cols = [f'{key}_force_plate_3'] + [f'{key}_earbuds_{i}' for i in (1, 2, 3)]
    if cols[0] not in df:
        return []
    d = df[['time'] + cols].dropna()
    if len(d) < FS_RAW * 20:
        return []
    d = d[(d.time >= d.time.iloc[0] + 5) & (d.time <= d.time.iloc[-1] - 5)]
    t = d.time.values
    gt = truth(t, d[cols[0]].values)
    tu = t[::int(FS_RAW / FS)]
    v = vertical50(d[cols[1:]].values)
    n = min(len(tu), len(v))
    cand = candidates(tu[:n], v[:n])
    out = []
    # 정답 걸음 k ↔ 그 디딤 안(앞뒤 50 ms)에 봉우리가 있는 이어버드 걸음, 다음 착지는 다음 걸음의 착지 후보
    peaks = np.array([c[0] for c in cand])
    for hs0, to0, hs1 in gt:
        a = np.where((peaks >= hs0 - 0.05) & (peaks <= to0 + 0.05))[0]
        if len(a) != 1 or a[0] + 1 >= len(cand):
            continue
        b = a[0] + 1
        if not (hs1 - 0.05 <= cand[b][0] <= hs1 + 0.3):
            continue
        out.append(dict(pid=pid, sess=sess, key=key, v=SPEEDS[key], hs=hs0, to=to0, hs1=hs1,
                        e_hs={r: cand[a[0]][1][r] - hs0 for r in cand[a[0]][1]},
                        e_to={r: cand[a[0]][2][r] - to0 for r in cand[a[0]][2]},
                        hs_next={r: cand[b][1][r] for r in cand[b][1]}, to_eb=cand[a[0]][2]))
    return out


def load_all():
    rows = []
    for f in sorted(RAW.glob('Data_P??_speeds_?.csv')):
        pid, sess = f.stem.split('_')[1], int(f.stem.split('_')[-1])
        df = pd.read_csv(f)
        for key in SPEEDS:
            rows += segment(pid, sess, key, df)
    return rows


def event_table(rows, which):
    recs = []
    for r in rows:
        for rule, e in r[which].items():
            recs.append(dict(pid=r['pid'], train=r['pid'] in TRAIN, rule=rule, err=e * 1000))
    return pd.DataFrame(recs)


def pick(tab, label):
    """학습 사람으로: 치우침(중앙값)을 뺀 뒤 절댓값 오차 중앙값이 가장 작은 규칙. 걸음 90% 이상에서 잡힌 규칙만."""
    tr = tab[tab.train]
    n_all = tr.groupby('rule').size().max()
    g = tr.groupby('rule').err.agg(['size', 'median', lambda e: np.median(np.abs(e - np.median(e)))])
    g.columns = ['n', 'bias', 'mad']
    g = g[g.n >= 0.9 * n_all].sort_values('mad')
    print(f'\n[{label}] 학습 7명, 규칙별 (ms): 치우침 중앙값, 치우침 뺀 절댓값 오차 중앙값')
    print(g.round(1).to_string())
    return g.index[0], g.bias.iloc[0]


def main():
    rows = load_all()
    print(f'걸음 {len(rows)}개 (학습 {sum(r["pid"] in TRAIN for r in rows)}, 시험 {sum(r["pid"] not in TRAIN for r in rows)}), '
          f'구간 {len({(r["pid"], r["sess"], r["key"]) for r in rows})}개')
    hs_rule, hs_bias = pick(event_table(rows, 'e_hs'), '착지')
    to_rule, to_bias = pick(event_table(rows, 'e_to'), '발 떼기')
    print(f'\n고른 규칙: 착지 {hs_rule} (치우침 {hs_bias:+.0f} ms), 발 떼기 {to_rule} (치우침 {to_bias:+.0f} ms) → 이제 바꾸지 않음')

    recs = []
    for r in rows:
        if hs_rule not in r['hs_next'] or to_rule not in r['to_eb']:
            continue
        fl_eb = (r['hs_next'][hs_rule] - hs_bias / 1000) - (r['to_eb'][to_rule] - to_bias / 1000)
        st_eb = (r['to_eb'][to_rule] - to_bias / 1000) - (r['hs'] + r['e_hs'][hs_rule] - hs_bias / 1000) \
            if hs_rule in r['e_hs'] else np.nan
        recs.append(dict(pid=r['pid'], sess=r['sess'], key=r['key'], v=r['v'], train=r['pid'] in TRAIN,
                         fl=(r['hs1'] - r['to']) * 1000, fl_eb=fl_eb * 1000,
                         st=(r['to'] - r['hs']) * 1000, st_eb=st_eb * 1000))
    d = pd.DataFrame(recs)
    d['e_fl'], d['e_st'] = d.fl_eb - d.fl, d.st_eb - d.st
    report(d)
    within_person(rows, hs_rule, hs_bias, to_rule, to_bias)
    cadence_plus_flight(rows, hs_rule, hs_bias, to_rule, to_bias)
    plot(d)
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    apply_ours(hs_rule, hs_bias, to_rule, to_bias)


def report(d):
    for name, q in (('학습 7명', d[d.train]), ('시험 7명', d[~d.train])):
        print(f'\n== {name}: 걸음 {len(q)}개')
        print(f'  걸음당 공중 시간 오차: 평균 절댓값 {q.e_fl.abs().mean():.1f} ms, 치우침 {q.e_fl.mean():+.1f}, SD {q.e_fl.std():.1f}'
              f'  (정답 공중 평균 {q.fl.mean():.0f} ms)')
        print(f'  걸음당 발 닿는 시간 오차: 평균 절댓값 {q.e_st.abs().mean():.1f} ms, 치우침 {q.e_st.mean():+.1f}')
        seg = q.groupby(['pid', 'sess', 'key']).agg(v=('v', 'first'), fl=('fl', 'mean'), fl_eb=('fl_eb', 'mean'),
                                                    n=('fl', 'size'))
        seg['e'] = seg.fl_eb - seg.fl
        print(f'  구간(약 50초, 평균 {seg.n.mean():.0f}걸음) 평균 공중 시간 오차: 평균 절댓값 {seg.e.abs().mean():.1f} ms, '
              f'최대 {seg.e.abs().max():.1f}')
    # 공중 시간이 속도 따라 얼마나 변하나(정답) → 오차를 km/h로
    q = d[d.v.notna()].groupby(['pid', 'sess', 'v']).agg(fl=('fl', 'mean'), fl_eb=('fl_eb', 'mean')).reset_index()
    slopes = []
    for (pid, sess), g in q.groupby(['pid', 'sess']):
        if g.v.nunique() >= 2:
            slopes.append((np.polyfit(g.v, g.fl, 1)[0], np.polyfit(g.v, g.fl_eb, 1)[0]))
    s = np.array(slopes)
    print(f'\n정답 공중 시간 기울기(사람·회차별, 7·10·13 km/h): 중앙 {np.median(s[:, 0]):+.1f} ms/(km/h) '
          f'(범위 {s[:, 0].min():+.1f}에서 {s[:, 0].max():+.1f}), 이어버드 중앙 {np.median(s[:, 1]):+.1f}')
    seg = d[~d.train].groupby(['pid', 'sess', 'key']).agg(e=('e_fl', 'mean'))
    print(f'→ 시험 구간 평균 오차 절댓값 {seg.e.abs().mean():.1f} ms ÷ 기울기 {np.median(s[:, 0]):.1f} = '
          f'약 {seg.e.abs().mean() / abs(np.median(s[:, 0])):.2f} km/h 상당')
    # 같은 사람·같은 속도 두 회차(정답·이어버드)
    w = d.groupby(['pid', 'key', 'sess']).agg(fl=('fl', 'mean'), fl_eb=('fl_eb', 'mean')).unstack('sess').dropna()
    if len(w):
        print(f'같은 속도 두 회차 차이(구간 평균, {len(w)}쌍): 정답 중앙 {np.median(np.abs(w.fl[1] - w.fl[2])):.1f} ms, '
              f'이어버드 중앙 {np.median(np.abs(w.fl_eb[1] - w.fl_eb[2])):.1f} ms')


def within_person(rows, hs_rule, hs_bias, to_rule, to_bias):
    """사람마다 보정하면 사라지는 치우침을 뺀 뒤: 이어버드가 속도 변화를 따라가나, 공중 시간만으로 속도를 맞히나."""
    recs = []
    for r in rows:
        if hs_rule in r['hs_next'] and to_rule in r['to_eb'] and not np.isnan(r['v']):
            recs.append(dict(pid=r['pid'], sess=r['sess'], v=r['v'], fl=(r['hs1'] - r['to']) * 1000,
                             eb=((r['hs_next'][hs_rule] - hs_bias / 1000) - (r['to_eb'][to_rule] - to_bias / 1000)) * 1000))
    s = pd.DataFrame(recs).groupby(['pid', 'sess', 'v']).agg(fl=('fl', 'mean'), eb=('eb', 'mean')).reset_index()
    s = s[s.groupby(['pid', 'sess']).v.transform('nunique') >= 2]
    sl = np.array([(np.polyfit(q.v, q.fl, 1)[0], np.polyfit(q.v, q.eb, 1)[0]) for _, q in s.groupby(['pid', 'sess'])])
    fc = s.fl - s.groupby(['pid', 'sess']).fl.transform('mean')
    ec = s.eb - s.groupby(['pid', 'sess']).eb.transform('mean')
    print(f'\n사람 안(사람·회차 {len(sl)}개): 이어버드 기울기 ÷ 정답 기울기 중앙 {np.median(sl[:, 1] / sl[:, 0]):.2f}, '
          f'사람 평균 뺀 구간 값 상관 {np.corrcoef(ec, fc)[0, 1]:+.2f}, 차이 중앙 {np.median(np.abs(ec - fc)):.1f} ms')

    def loo(col):
        e = []
        for _, q in s.groupby(['pid', 'sess']):
            if len(q) < 3:
                continue
            for j in range(len(q)):
                tr = q.drop(q.index[j])
                e.append(abs(np.polyval(np.polyfit(tr[col], tr.v, 1), q[col].iloc[j]) - q.v.iloc[j]))
        return np.median(e)
    print(f'공중 시간만으로 속도(사람마다 맞춤, 7·10·13 중 하나 빼고) 오차 중앙: 이어버드 {loo("eb"):.2f} km/h, '
          f'정답 힘판 공중 시간으로도 {loo("fl"):.2f}')


def cadence_plus_flight(rows, hs_rule, hs_bias, to_rule, to_bias):
    """원래 아이디어 확인: 빨라져도 걸음 수가 그대로인 구간을 공중 시간이 가려 주나.
    걸음 수·공중 시간 기울기는 다른 사람들 값(이 사람 빼고 사람 안 변화로 맞춤), 이 사람은 10 km/h 한 점으로 보정 → 7·13 맞히기."""
    recs = []
    for r in rows:
        if hs_rule in r['hs_next'] and to_rule in r['to_eb'] and not np.isnan(r['v']):
            recs.append(dict(pid=r['pid'], sess=r['sess'], v=r['v'], step=r['hs1'] - r['hs'], fl=(r['hs1'] - r['to']) * 1000,
                             eb=((r['hs_next'][hs_rule] - hs_bias / 1000) - (r['to_eb'][to_rule] - to_bias / 1000)) * 1000))
    s = pd.DataFrame(recs).groupby(['pid', 'sess', 'v']).agg(step=('step', 'mean'), fl=('fl', 'mean'), eb=('eb', 'mean')).reset_index()
    s['cad'] = 60 / s.step
    s = s[s.groupby(['pid', 'sess']).v.transform(lambda x: set(x) >= {7, 10, 13})]
    dc = s.groupby(['pid', 'sess', 'v']).cad.first().unstack('v')
    print(f'\n걸음 수 + 공중 시간 (사람·회차 {len(dc)}개, 7·10·13 모두 있는 것): 걸음 수 변화 중앙 7→10 {np.median(dc[10] - dc[7]):+.1f}, '
          f'10→13 {np.median(dc[13] - dc[10]):+.1f} spm')
    cols_sets = (('걸음 수만', ['cad']), ('걸음 수 + 공중(정답 힘판)', ['cad', 'fl']), ('걸음 수 + 공중(이어버드 50 Hz)', ['cad', 'eb']))
    for label, cols in cols_sets:
        err = []
        for (pid, sess), q in s.groupby(['pid', 'sess']):
            o = s[s.pid != pid]
            oc = o[cols + ['v']] - o.groupby(['pid', 'sess'])[cols + ['v']].transform('mean')  # 사람 안 변화만
            k = np.linalg.lstsq(oc[cols].values, oc.v.values, rcond=None)[0]
            base = q[q.v == 10].iloc[0]
            for _, x in q[q.v != 10].iterrows():
                err.append(abs(base.v + (x[cols].values - base[cols].values).astype(float) @ k - x.v))
        print(f'  [{label}] 7·13 맞히기 오차 중앙 {np.median(err):.2f}, 평균 {np.mean(err):.2f} km/h')


def apply_ours(hs_rule, hs_bias, to_rule, to_bias):
    """고정한 규칙을 우리 에어팟 기록(B 고상준, M 민재)에 그대로."""
    import airpods_events as ae
    from scipy.stats import spearmanr
    print(f'\n== 우리 에어팟 기록에 적용 (규칙·치우침은 위에서 고정한 값 그대로)')
    for name, rec in (('B', ae.record_B), ('M', ae.record_M)):
        d, t0, segs = rec()
        df, _ = ae.analyse(name, d, t0, segs)
        ta, vert = ae.vertical(d, t0)
        fl = []
        for tu, v in ae.chunks(ta, vert):
            c = candidates(tu, v)
            for k in range(len(c) - 1):
                if to_rule in c[k][2] and hs_rule in c[k + 1][1]:
                    f = (c[k + 1][1][hs_rule] - hs_bias / 1000) - (c[k][2][to_rule] - to_bias / 1000)
                    if 0 < f < 0.5:
                        fl.append((c[k][0], f * 1000))
        fl = np.array(fl)
        df['fl12'] = [np.mean(fl[(fl[:, 0] >= s) & (fl[:, 0] < e), 1]) if np.any((fl[:, 0] >= s) & (fl[:, 0] < e)) else np.nan
                      for _, _, s, e in segs]
        walk = df[df.cad < 150]
        run = (df[df.v >= 8] if name == 'B' else df[df.cad >= 150]).dropna(subset=['fl12']).reset_index(drop=True)
        rho = spearmanr(run.v, run.fl12)
        print(f'  {name}: 걷기 구간 공중 {", ".join(f"{x:.0f}" for x in walk.fl12)} ms / 달리기 속도별 '
              + ', '.join(f'{v:.0f}: {q.min():.0f}에서 {q.max():.0f}' for v, q in run.groupby('v').fl12)
              + f' / 순위 상관 {rho.correlation:+.2f} (p {rho.pvalue:.3f}), 기울기 {np.polyfit(run.v, run.fl12, 1)[0]:+.1f} ms/(km/h)')
        print(f'    달리기 걸음 수 속도별: ' + ', '.join(f'{v:.0f}: {q.min():.0f}에서 {q.max():.0f}' for v, q in run.groupby('v').cad))
        for label, cols in (('걸음 수만', ['cad']), ('걸음 수 + 공중', ['cad', 'fl12']), ('충격만', ['lnimp']),
                            ('충격 + 걸음 수', ['lnimp', 'cad']), ('충격 + 공중(0012 규칙)', ['lnimp', 'fl12']),
                            ('충격 + 걸음 수 + 공중', ['lnimp', 'cad', 'fl12'])):
            print(f'    달리기 구간 하나씩 빼고 [{label}] {ae.loo(run, cols)}')


def plot(d):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    from matplotlib import font_manager
    have = {f.name for f in font_manager.fontManager.ttflist}
    plt.rcParams['font.family'] = next((f for f in ('AppleGothic', 'Malgun Gothic') if f in have), 'sans-serif')
    plt.rcParams['axes.unicode_minus'] = False
    seg = d.groupby(['pid', 'sess', 'key']).agg(fl=('fl', 'mean'), fl_eb=('fl_eb', 'mean'), train=('train', 'first'))
    fig, ax = plt.subplots(figsize=(5.5, 5))
    for tr, c, lab in ((True, '#999999', '학습 7명'), (False, '#d1495b', '시험 7명')):
        q = seg[seg.train == tr]
        ax.scatter(q.fl, q.fl_eb, color=c, label=lab, s=18)
    lim = [seg[['fl', 'fl_eb']].min().min() - 10, seg[['fl', 'fl_eb']].max().max() + 10]
    ax.plot(lim, lim, color='black', lw=0.8)
    ax.set_xlabel('정답 공중 시간 (힘판, 구간 평균 ms)')
    ax.set_ylabel('이어버드 50 Hz (구간 평균 ms)')
    ax.legend()
    ax.set_title('Nijs 외 2021 데이터, 계획 0012')
    fig.tight_layout()
    out = ROOT / 'docs/speed/img/0012_nijs_flight.png'
    fig.savefig(out, dpi=120)
    print('그림:', out.relative_to(ROOT))


if __name__ == '__main__':
    main()
