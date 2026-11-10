"""실시간 속도 기준 구현(Python): 계기판 아이폰(충격·걸음 수) + 에어팟(머리 흔들림) — 계획 0010, 0024.
Unity 플러그인의 SpeedEstimator.swift(옛 iOS SpeedRecorder)가 이 파일을 그대로 옮긴 것. 숫자·규칙을 바꾸면 둘 다 고친다.

1. 걷기·달리기 판단: 에어팟 "0.5 g 아래 시간"(걷기는 공중이 없어 거의 0). 에어팟이 끊기면 폰 걸음 수.
   HOLD_S 동안 이어져야 바꿈
2. 달리기: 에어팟 있으면 속도 = a + b·ln(충격) + AIR_D·흔들림 폭, 끊기면 a' + b'·ln(충격).
   AIR_D는 10/29 B 기록에서 배운 고정값(1명), a·b는 그 사람의 보정 구간(달리기로 판단된 것)으로
3. 걷기: 걸음 수 공식. 기울기는 공통, 절편은 이 사람의 걷기 보정점이 있으면 그걸로(계획 0023·0024), 없으면 공통
4. 앱 보정 단계(계획 0024): measure_stage가 단계 하나를 재서 (결과, 점)을 돌려줌. 결과 이름은 C# CalStageResult와 같음
5. 누적 걸음: 포락선 봉우리를 그때그때 셈(다리 애니메이션용, 감지 지연 없이). 문턱은 0.5초마다 최근 3초로 다시 정함
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
# 이 사람 걷기 절편(계획 0023 설계, 11/10 민재 결정으로 켬). 공통 공식은 10/29 두 사람(다리 길이 비슷)으로 만든 것이라
# 키가 다른 사람은 보폭이 달라 틀림. 절편은 WALK_MIN_KMH 이상 걷기 점으로(2 km/h는 오를 때·내릴 때 걸음 수 차이가 가장 크고
# 걸음 감지도 불안정, live_stage_check.py), 그런 점이 없으면 있는 걷기 점 전부. 헬스장 여러 명으로 "2 포함 대 4 이상" 다시 확인
WALK_OWN = True
WALK_MIN_KMH = 4.0
# 앱 보정 단계(계획 0024). 숫자는 가설, speedlab/exp/live_stage_check.py로 10/29 기록에서 정함 → 아이폰에서 다시 확인
STAGE_MIN_S = 3.0    # 앞을 버린 뒤 잴 시간이 이보다 짧으면 TooShort
LOST_GAP_S = 2.0     # 폰 샘플이 이만큼 끊기면 SensorLost(앱 폰 흐름 Lost와 같음)
STILL_ENV = 0.12     # 포락선 75% 값이 이보다 작으면 걸음 없음(NoSteps). 10/29 S22: 벨트만(사람 옆 발판) 0.031·0.081,
                     # 걷기 2 km/h 최소 0.166 → 그 사이. 아이폰은 세기가 다를 수 있음(가설, 책상 확인 때 다시)
CAD_EDGE = 215.0     # 걸음 수가 찾는 범위 끝(220)에 붙으면 걸음이 아니라 벨트 모터 진동(10/29 벨트만 4 km/h에서 222, 규칙성 0.64)
WALK_ONLY_KMH = 4.0  # 이 속도 이하에서 달리기로 나오면 걸음 수를 두 배로 잡은 것(10/29 B 2 km/h에서 174) → 다시 잼(가설)
# 누적 걸음(봉우리 세기)
STEP_HALF = 12       # 봉우리: 앞뒤 0.12초 안 최댓값(impact와 같음)
STEP_GAP_S = 60 / 220  # 220 spm보다 빠른 봉우리는 같은 걸음
STEP_REL = 0.5       # 문턱 = 최근 3초 포락선 90% 값 × 이 비율(아래는 STILL_ENV)
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


def env_level(s):
    """걸음 없음 판정에 쓰는 포락선 세기: 75% 값(impact의 봉우리 문턱과 같은 자리)"""
    return float(np.sort(s)[int(0.75 * (len(s) - 1))])


def steps_like(level, cad):
    """사람 걸음으로 볼 수 있나: 세기가 서 있을 때보다 크고, 걸음 수가 찾는 범위 끝(벨트 모터 진동)에 붙지 않음.
    실시간 걸음 상태(update)와 보정 단계 판정(stage_point)이 같이 쓴다(11/11 맥 아이폰 확인: 가만히 둔 폰이 222 spm으로 걸음이 됨)"""
    return level >= STILL_ENV and cad < CAD_EDGE


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
        self.walk_a = None      # 이 사람의 걷기 절편(걷기 보정점이 있을 때)
        self.last_update = -1e9
        self.snap = None
        self.steps = 0          # 누적 걸음
        self.step_thr = None    # 봉우리 문턱(0.5초마다)
        self.last_step_t = -1e9

    # ---------- 입력 ----------
    def add_phone(self, t, x, y, z):
        m = np.sqrt(sum(f(v) ** 2 for f, v in zip(self.hp, (x, y, z))))
        self.env.append((t, self.lp(m)))
        while self.env and self.env[0][0] < t - 30:
            self.env.popleft()
        self.count_step()
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

    def count_step(self):
        """STEP_HALF 샘플 전 값이 앞뒤 STEP_HALF 안 최댓값이고 문턱을 넘으면 한 걸음(0.12초 늦게 셈)"""
        n = len(self.env)
        if self.step_thr is None or n < 2 * STEP_HALF + 1:
            return
        c = n - 1 - STEP_HALF
        tc, vc = self.env[c]
        if vc < self.step_thr or tc - self.last_step_t < STEP_GAP_S:
            return
        for i in range(c - STEP_HALF, n):
            if self.env[i][1] > vc or (self.env[i][1] == vc and i < c):
                return
        self.steps += 1
        self.last_step_t = tc

    def update_step_thr(self, t1):
        s = [e for tt, e in self.env if tt >= t1 - 3.0]
        if len(s) >= FS:
            self.step_thr = max(STILL_ENV, STEP_REL * float(np.percentile(s, 90)))

    # ---------- 특징 ----------
    def phone_feats(self, t0, t1):
        s = np.array([e for tt, e in self.env if t0 <= tt <= t1])
        if len(s) < FS * 3:
            return None
        cad, q = sdb.cadence(s)
        return dict(cad=cad, q=q, imp=sdb.impact(s), level=env_level(s))

    def air_feats(self, t0, t1, cad):
        w = [(tt, m) for tt, m in self.air if t0 <= tt <= t1]
        if len(w) < 10:
            return None
        m = np.array([x for _, x in w])
        step_ms = 60000.0 / cad if cad else 60000.0 / 165
        return dict(amp=float(np.percentile(m, 95) - np.percentile(m, 5)), lowg_ms=float((m < LOWG).mean() * step_ms),
                    cover=len(w) / ((t1 - t0) * 45.0), last=w[-1][0])

    # ---------- 보정 ----------
    def measure_stage(self, v, t_begin, t_end):
        """앱 보정 단계 하나(계획 0024). t_begin에 단계 시작(속도 안내), 앞 CAL_SKIP_S 버리고 t_end까지.
        (결과, 점). 결과는 Ok·NoSteps·Irregular·SensorLost·TooShort(C# CalStageResult), 점은 Ok일 때만"""
        t0 = t_begin + CAL_SKIP_S
        if t_end - t0 < STAGE_MIN_S:
            return 'TooShort', None
        ts = [tt for tt, _ in self.env if t0 <= tt <= t_end]
        if not ts or ts[0] - t0 > LOST_GAP_S or t_end - ts[-1] > LOST_GAP_S or \
                (len(ts) > 1 and float(np.max(np.diff(ts))) > LOST_GAP_S):
            return 'SensorLost', None
        return self.stage_point(v, t0, t_end)

    def stage_point(self, v, t0, t1):
        """[t0, t1](이미 앞을 버린 창)의 보정점 판정"""
        s = np.array([e for tt, e in self.env if t0 <= tt <= t1])
        if len(s) < FS * STAGE_MIN_S:
            return 'TooShort', None
        cad, q = sdb.cadence(s)
        if not steps_like(env_level(s), cad):
            return 'NoSteps', None
        imp = sdb.impact(s)
        if q < Q_MIN or imp <= 0:
            return 'Irregular', None
        af = self.air_feats(t0, t1, cad)
        air_ok = bool(af and af['cover'] >= AIR_MIN_COVER)
        running = af['lowg_ms'] >= RUN_LOWG_MS if air_ok else cad >= RUN_CAD
        if running and v <= WALK_ONLY_KMH:
            return 'Irregular', None
        return 'Ok', dict(v=v, limp=float(np.log(imp)), cad=float(cad), amp=af['amp'] if air_ok else None,
                          running=bool(running))

    def add_point(self, v, t0, t1):
        """SpeedRecorder 구간 방식(begin_stage): 끝난 보정 구간에서 점 하나"""
        r, p = self.stage_point(v, t0, t1)
        if r != 'Ok':
            return
        self.points.append(p)
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
        walk = [p for p in self.points if not p['running']]
        walk = [p for p in walk if p['v'] >= WALK_MIN_KMH] or walk
        self.walk_a = float(np.mean([p['v'] - WALK[1] * p['cad'] for p in walk])) if walk else None

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
        self.update_step_thr(t1)
        pf = self.phone_feats(t0, t1)
        af = self.air_feats(t0, t1, pf['cad'] if pf else None)
        air_ok = bool(af and af['cover'] >= AIR_MIN_COVER and t1 - af['last'] <= AIR_MAX_AGE)
        if not pf or pf['q'] < Q_MIN or not steps_like(pf['level'], pf['cad']):
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
            kmh = (self.walk_a if WALK_OWN and self.walk_a is not None else WALK[0]) + WALK[1] * pf['cad']
        elif self.state == 0:
            kmh = 0.0
        if kmh is not None:
            self.hist.append((t1, float(np.clip(kmh, 0, 25))))
        while self.hist and self.hist[0][0] < t1 - SMOOTH_S:
            self.hist.popleft()
        return dict(t=t1, kmh=median([k for _, k in self.hist]) if kmh is not None else None, kmh_phone=kmh_phone,
                    state=self.state, by=by, air_ok=air_ok, cad=pf['cad'] if pf else None, imp=pf['imp'] if pf else None,
                    amp=af['amp'] if af else None, lowg_ms=af['lowg_ms'] if af else None, steps=self.steps)
