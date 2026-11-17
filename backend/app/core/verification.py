"""센서 기록과 계기판 값 대조(BL-18, 계획 0038). 순수 함수만: DB·사진은 라우터에서.

판정은 서버 한 곳에서(docs/api/README.md "판정은 서버"). 값은 설정(config.VerificationPolicy)에서 받는다.
- 거리: |계기판 − 센서| / 계기판 ≤ 허용 오차
- 시간: 계기판 − 센서가 [최소, 최대 + 쉰 시간(상한 있음)] 안. 계기판은 카운트다운 전 걷기부터 재므로 보통 앱보다 길고,
  앱 시간은 일시정지를 뺀 값인데 계기판은 기종마다 일시정지 중 시간이 흐르거나 멈춘다(Q-33 조사 전, 정할 것 5)
- BL-18 ① "종료 시각": 계기판에는 종료 시각이 뜨지 않으므로 "종료 직후에 찍었는가"는 서버 마감으로 지킨다(run_access)
- 계기판 값이 달릴 수 없는 속도(평균 30 km/h 초과)거나 0이면 잘못 읽은 것으로 보고 다시 찍기
"""
from dataclasses import dataclass
from typing import Optional

from ..schemas.run import MAX_SPEED_KMH
from ..schemas.verification import ComparisonRow, UnverifiedReason, VerificationStatus
from .config import VerificationPolicy
from .ocr import ConsoleRead, ReadStatus

S, R = VerificationStatus, UnverifiedReason


@dataclass(frozen=True)
class Outcome:
    """사진 한 번(또는 건너뛰기)의 결과. RETAKE_REQUIRED는 저장되는 상태가 아님."""

    status: VerificationStatus
    reason: Optional[UnverifiedReason] = None
    read: Optional[ConsoleRead] = None          # 읽었을 때만(MISMATCH도 표에 보이려고 남김)
    rows: tuple[ComparisonRow, ...] = ()


def plausible(distance_m: int, duration_s: int) -> bool:
    """읽은 값이 계기판에 실제로 뜰 만한 값인지(잘못 읽은 소수점·자리 거르기)."""
    return distance_m > 0 and duration_s > 0 and distance_m <= MAX_SPEED_KMH / 3.6 * duration_s


def _ratio(console: float, sensor: float) -> Optional[float]:
    return round((console - sensor) / console, 4) if console else None


def compare(policy: VerificationPolicy, read: ConsoleRead, sensor_m: int, sensor_s: int, paused_s: float) -> Outcome:
    """읽은 값(READ, 그럴듯함 확인 뒤)과 센서 값 → VERIFIED 또는 UNVERIFIED(MISMATCH)."""
    d_ratio = _ratio(read.distance_m, sensor_m)
    d_ok = d_ratio is not None and abs(d_ratio) <= policy.distance_tolerance
    widen = min(max(paused_s, 0), policy.max_pause_s)  # 기기 시계로 센 쉰 시간이라 상한을 둠
    t_ok = policy.duration_min_s <= read.duration_s - sensor_s <= policy.duration_max_s + widen
    rows = (ComparisonRow(item="distance", console=read.distance_m, sensor=sensor_m, diff_ratio=d_ratio, ok=d_ok),
            ComparisonRow(item="duration", console=read.duration_s, sensor=sensor_s,
                          diff_ratio=_ratio(read.duration_s, sensor_s), ok=t_ok))
    if d_ok and t_ok:
        return Outcome(S.VERIFIED, read=read, rows=rows)
    return Outcome(S.UNVERIFIED, R.MISMATCH, read=read, rows=rows)


def decide(policy: VerificationPolicy, read: ConsoleRead, sensor_m: int, sensor_s: int, paused_s: float,
           retakes_used: int) -> Outcome:
    """OCR 결과 하나 → 결과. VERIFIED만 바로 확정. 오차 초과(MISMATCH)·읽을 수 없는 계기판(UNSUPPORTED_CONSOLE)·못 읽음(이유 없음)은
    다시 찍기 횟수가 남았으면 RETAKE_REQUIRED + 이유(Figma E-07·08·09의 "다시 촬영", 잘못 읽어 생긴 오차를 다시 찍어 바로잡게,
    11/17 지환 결정), 다 썼으면 그 이유로 UNVERIFIED(못 읽음은 UNSUPPORTED_CONSOLE)."""
    last = retakes_used >= policy.max_retakes
    if read.status == ReadStatus.READ and plausible(read.distance_m, read.duration_s):
        out = compare(policy, read, sensor_m, sensor_s, paused_s)
        if out.status == S.VERIFIED or last:
            return out
        return Outcome(S.RETAKE_REQUIRED, R.MISMATCH, read=read, rows=out.rows)
    if last:
        return Outcome(S.UNVERIFIED, R.UNSUPPORTED_CONSOLE)
    if read.status == ReadStatus.UNSUPPORTED:
        return Outcome(S.RETAKE_REQUIRED, R.UNSUPPORTED_CONSOLE)
    return Outcome(S.RETAKE_REQUIRED, read=read if read.status == ReadStatus.READ else None)
