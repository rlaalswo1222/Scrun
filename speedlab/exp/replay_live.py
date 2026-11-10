"""계획 0024 2번: 아이폰 기록(SpeedRecorder phone/ 폴더)을 fusion_live에 다시 넣어 0.5초마다 값을 CSV로 남긴다.
Swift 재생 도구(맥)가 같은 기록으로 낸 값과 비교하는 정답 파일이 된다.

실행: .venv\\Scripts\\python speedlab/exp/replay_live.py [기록 폴더] [출력 CSV]
      기본: data/runs/20261029-040156 → speedlab/golden/replay_20261029-040156.csv
확인: 그날 폰이 낸 estimate.csv와 0.5초 칸마다 비교(같은 계산이면 거의 0)
입력 열: phone/iphone_acc.csv(t,x,y,z m/s², 중력 포함), phone/airpods_acc.csv + airpods_grav.csv(더하면 전체 가속도),
        phone/stage.csv(구간: set 1 = 보정, SpeedRecorder와 같음)
"""
import sys
from pathlib import Path

import numpy as np
import pandas as pd

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'speedlab'))
import fusion_live as fl  # noqa: E402

SET_NAME = {0: 'sync', 1: 'calib', 2: 'test', 3: 'free', 4: 'hop', 5: 'warmup', 6: 'still', 9: 'end'}
COLS = ['t', 'kmh', 'kmh_phone', 'state', 'by_airpods', 'air_ok', 'cad', 'imp', 'amp', 'lowg_ms', 'n_run_points', 'steps']


def replay(run: Path) -> pd.DataFrame:
    ph = run / 'phone'
    acc = pd.read_csv(ph / 'iphone_acc.csv')
    events = [(r.t, 0, (r.x, r.y, r.z)) for r in acc.itertuples()]
    if (ph / 'airpods_acc.csv').exists():
        a, g = pd.read_csv(ph / 'airpods_acc.csv'), pd.read_csv(ph / 'airpods_grav.csv')
        m = a.merge(g, on='t', suffixes=('', '_g'))
        events += [(r.t, 1, (r.x + r.x_g, r.y + r.y_g, r.z + r.z_g)) for r in m.itertuples()]
    if (ph / 'stage.csv').exists():
        events += [(r.t, 2, (SET_NAME.get(int(r.set), '?'), None if r.speed < 0 else float(r.speed)))
                   for r in pd.read_csv(ph / 'stage.csv').itertuples()]
    events.sort(key=lambda e: (e[0], e[1]))

    est, rows = fl.SpeedEstimator(), []
    for t, kind, v in events:
        if kind == 0:
            before = est.snap
            est.add_phone(t, *v)
            s = est.snap
            if s is not None and s is not before:
                rows.append([s['t'], s['kmh'], s['kmh_phone'], s['state'], 1 if s['by'] == 'airpods' else 0,
                             1 if s['air_ok'] else 0, s['cad'], s['imp'], s['amp'], s['lowg_ms'],
                             sum(p['running'] for p in est.points), s['steps']])
        elif kind == 1:
            est.add_airpods(t, *v)
        else:
            est.begin_stage(v[0], v[1], t)
    d = pd.DataFrame(rows, columns=COLS)
    return d.fillna(-1)   # estimate.csv처럼 값 없음 = -1


def compare(d: pd.DataFrame, phone_csv: Path):
    if not phone_csv.exists():
        return
    p = pd.read_csv(phone_csv)
    p = p.rename(columns={c: 'p_' + c for c in p.columns if c != 't'})
    m = pd.merge_asof(d.sort_values('t'), p.sort_values('t'), on='t', direction='nearest', tolerance=0.02)
    print(f'0.5초 칸 Python {len(d)} / 폰 {len(p)}, 짝 {int(m.p_kmh.notna().sum())}')
    for c in ('kmh', 'state', 'cad', 'imp', 'amp'):
        a, b = m[c], m['p_' + c]
        ok = a.notna() & b.notna()
        both = ok & (a >= 0) & (b >= 0)
        diff = (a[both] - b[both]).abs()
        none_mismatch = int(((a < 0) != (b < 0))[ok].sum())
        print(f'  {c:6s} 차이 최대 {diff.max():.4f} 중앙 {diff.median():.4f} | 한쪽만 값 없음 {none_mismatch}칸')


def main():
    run = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / 'data/runs/20261029-040156'
    out = Path(sys.argv[2]) if len(sys.argv) > 2 else ROOT / 'speedlab/golden' / f'replay_{run.name}.csv'
    d = replay(run)
    out.parent.mkdir(parents=True, exist_ok=True)
    d.to_csv(out, index=False, float_format='%.5f', lineterminator='\n')
    print(f'{out.relative_to(ROOT)}: {len(d)}줄, 마지막 누적 걸음 {int(d.steps.iloc[-1])}')
    compare(d, run / 'phone/estimate.csv')


if __name__ == '__main__':
    main()
