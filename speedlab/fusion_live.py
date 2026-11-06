"""실시간 속도 기준 구현(Python): 계기판 아이폰(충격·걸음 수) + 에어팟(머리 흔들림) — 계획 0010.
iOS SpeedRecorder의 SpeedEstimator.swift가 이 파일을 그대로 옮긴 것. 숫자·규칙을 바꾸면 둘 다 고친다.

1. 걷기·달리기 판단: 에어팟 "0.5 g 아래 시간"(걷기는 공중이 없어 거의 0). 에어팟이 끊기면 폰 걸음 수.
   HOLD_S 동안 이어져야 바꿈
2. 달리기: 에어팟 있으면 속도 = a + b·ln(충격) + AIR_D·흔들림 폭, 끊기면 a' + b'·ln(충격).
   AIR_D는 10/29 B 기록에서 배운 고정값(1명), a·b는 그 사람의 보정 구간(달리기로 판단된 것)으로
3. 걷기: 공통 걸음 수 공식(10/29 두 사람, 다리 길이 비슷 → 확정 아님)
충격·걸음 수는 StepSpeed.cs를 옮긴 speedlab/exp/soundtrackdb.py와 같은 계산. 필터는 샘플마다 이어서(앱과 같게)
근거 docs/speed/airpods_flight.md, fusion.md
"""
import sys
from collections import deque
from pathlib import Path

import numpy as np
from scipy.signal import butter, sosfilt, sosfilt_zi  # noqa: F401

sys.path.insert(0, str(Path(__file__).resolve().parent / 'exp'))
import soundtrackdb as sdb  # noqa: E402

G = 9.80665
FS = 100             # 아이폰 deviceMotion 100 Hz
WIN_S = 6.0          # 실시간 창
CAL_SKIP_S = 5.0     # 보정 구간 앞에서 버릴 시간(속도 맞추는 중, 올라가는 순서 기준)
LOWG = 0.5           # g, 이 아래 시간 = 공중 근처
RUN_LOWG_MS = 70.0   # 걸음당 이 이상이면 달리기 (10/29 B: 걷기 4에서 43, 달리기 93 이상)
RUN_CAD = 150.0      # 에어팟 없을 때 걸음 수 기준
Q_MIN = 0.3          # 걸음 신뢰가 이보다 낮으면 걸음 없음
AIR_MAX_AGE = 0.5    # 에어팟 마지막 샘플이 이보다 오래되면 끊김
AIR_MIN_COVER = 0.7  # 창 안 에어팟 샘플이 기대(45 Hz)의 이 비율보다 적으면 끊김
HOLD_S = 2.5
SMOOTH_S = 2.0
UPDATE_S = 0.5
AIR_D = 2.598                    # km/h per g (B 달리기 11구간 전체 맞춤)
B_FULL, B_PHONE = 4.828, 6.396   # 보정점이 하나뿐일 때 빌리는 ln(충격) 기울기 (B)
WALK = (-3.45, 0.0732)           # 걷기 v = a + b·spm (fusion.py 두 사람 합친 공식)
HP = butter(4, 8, 'high', fs=FS, output='sos')
LP = butter(4, 5, 'low', fs=FS, output='sos')


class Sos:
    """샘플 하나씩 넣는 sosfilt (Direct Form II transposed, scipy와 같음)"""
    def __init__(self, sos):
        self.sos, self.z = sos, np.zeros((len(sos), 2))

    def __call__(self, x):
        for i, (b0, b1, b2, _, a1, a2) in enumerate(self.sos):
            y = b0 * x + self.z[i, 0]
            self.z[i, 0] = b1 * x - a1 * y + self.z[i, 1]
            self.z[i, 1] = b2 * x - a2 * y
            x = y
        return x


def median(a):
    return float(np.median(a)) if len(a) else None


class SpeedEstimator:
    def __init__(self):
        self.hp = [Sos(HP) for _ in range(3)]
        self.lp = Sos(LP)
        self.env = deque()      # (t, 포락선)
        self.air = deque()      # (t, 전체 가속도 크기 g)
        self.state, self.cand, self.cand_since = 0, 0, None
        self.hist = deque()
        self.stage = None       # (set, speed, t0)
        self.points = []
        self.new_points = 0
        self.full = self.phone = None
        self.last_update = -1e9
        self.snap = None

    # ---------- 입력 ----------
    def add_phone(self, t, x, y, z):
        m = np.sqrt(sum(f(v) ** 2 for f, v in zip(self.hp, (x, y, z))))
        self.env.append((t, self.lp(m)))
        while self.env and self.env[0][0] < t - 30:
            self.env.popleft()
        if t - self.last_update >= UPDATE_S:
            self.last_update = t
            self.snap = self.update(t)

    def add_airpods(self, t, x, y, z):
        self.air.append((t, np.sqrt(x * x + y * y + z * z) / G))
        while self.air and self.air[0][0] < t - 30:
            self.air.popleft()

    def begin_stage(self, set_, speed, t):
        """구간이 바뀔 때: 끝난 구간이 보정이면 보정점 하나"""
        if self.stage and self.stage[0] == 'calib' and self.stage[1]:
            self.add_point(self.stage[1], self.stage[2] + CAL_SKIP_S, t)
        self.stage = (set_, speed, t)

    # ---------- 특징 ----------
    def phone_feats(self, t0, t1):
        s = np.array([e for tt, e in self.env if t0 <= tt <= t1])
        if len(s) < FS * 3:
            return None
        cad, q = sdb.cadence(s)
        return dict(cad=cad, q=q, imp=sdb.impact(s))

    def air_feats(self, t0, t1, cad):
        w = [(tt, m) for tt, m in self.air if t0 <= tt <= t1]
        if len(w) < 10:
            return None
        m = np.array([x for _, x in w])
        step_ms = 60000.0 / cad if cad else 60000.0 / 165
        return dict(amp=float(np.percentile(m, 95) - np.percentile(m, 5)), lowg_ms=float((m < LOWG).mean() * step_ms),
                    cover=len(w) / ((t1 - t0) * 45.0), last=w[-1][0])

    # ---------- 보정 ----------
    def add_point(self, v, t0, t1):
        pf = self.phone_feats(t0, t1)
        if not pf or pf['q'] < Q_MIN or pf['imp'] <= 0:
            return
        af = self.air_feats(t0, t1, pf['cad'])
        air_ok = bool(af and af['cover'] >= AIR_MIN_COVER)
        running = af['lowg_ms'] >= RUN_LOWG_MS if air_ok else pf['cad'] >= RUN_CAD
        self.points.append(dict(v=v, limp=float(np.log(pf['imp'])), cad=pf['cad'], amp=af['amp'] if air_ok else None,
                                running=bool(running)))
        self.new_points += 1
        self.refit()

    def load(self, points):
        """저장해 둔 보정점으로 시작 (보정 없이 바로 달릴 때, 앱의 SavedCalibration)"""
        self.points = [dict(p) for p in points]
        self.refit()

    def refit(self):
        run = [p for p in self.points if p['running']]
        self.phone = self.fit([(p['limp'], p['v']) for p in run], B_PHONE)
        self.full = self.fit([(p['limp'], p['v'] - AIR_D * p['amp']) for p in run if p['amp'] is not None], B_FULL)

    @staticmethod
    def fit(xy, b_default):
        """y = a + b·x. 점 2개 이상이면 둘 다, 하나면 b를 빌림"""
        if not xy:
            return None
        x, y = np.array(xy).T
        if len(xy) >= 2 and np.ptp(x) > 0.05:
            b, a = np.polyfit(x, y, 1)
        else:
            b = b_default
            a = float(np.mean(y - b * x))
        return float(a), float(b)

    # ---------- 실시간 ----------
    def update(self, t1):
        t0 = t1 - WIN_S
        pf = self.phone_feats(t0, t1)
        af = self.air_feats(t0, t1, pf['cad'] if pf else None)
        air_ok = bool(af and af['cover'] >= AIR_MIN_COVER and t1 - af['last'] <= AIR_MAX_AGE)
        if not pf or pf['q'] < Q_MIN:
            cand, by = 0, 'phone'
        elif air_ok:
            cand, by = (2 if af['lowg_ms'] >= RUN_LOWG_MS else 1), 'airpods'
        else:
            cand, by = (2 if pf['cad'] >= RUN_CAD else 1), 'phone'
        if cand != self.cand:
            self.cand, self.cand_since = cand, t1
        if cand != self.state and t1 - self.cand_since >= HOLD_S:
            self.state = cand

        kmh = kmh_phone = None
        if self.state == 2 and pf and pf['imp'] > 0:
            li = np.log(pf['imp'])
            if self.phone:
                kmh_phone = self.phone[0] + self.phone[1] * li
            if air_ok and self.full:
                kmh = self.full[0] + self.full[1] * li + AIR_D * af['amp']
            else:
                kmh = kmh_phone
        elif self.state == 1 and pf:
            kmh = WALK[0] + WALK[1] * pf['cad']
        elif self.state == 0:
            kmh = 0.0
        if kmh is not None:
            self.hist.append((t1, float(np.clip(kmh, 0, 25))))
        while self.hist and self.hist[0][0] < t1 - SMOOTH_S:
            self.hist.popleft()
        return dict(t=t1, kmh=median([k for _, k in self.hist]) if kmh is not None else None, kmh_phone=kmh_phone,
                    state=self.state, by=by, air_ok=air_ok, cad=pf['cad'] if pf else None, imp=pf['imp'] if pf else None,
                    amp=af['amp'] if af else None, lowg_ms=af['lowg_ms'] if af else None)
