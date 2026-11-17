"""실패 횟수 제한(친구 코드 맞혀 보기 막기, 계획 0044). 사람마다 최근 window_s 안의 시도 시각만 메모리에 둔다.

확인과 기록을 한 번에 한다(acquire): 동시에 여러 요청이 와도 잠금 안에서 자리를 먼저 잡으므로 limit을 넘지 않는다
(따로 하면 스레드풀 수만큼 한꺼번에 확인을 통과함, #리뷰). 실패가 아니었으면(코드를 찾음) release로 그 자리를 돌려준다.

서버는 uvicorn 한 프로세스(ADR-0010)라 메모리로 충분하다. 다시 켜지면 비지만 코드가 1조 개라
10분에 10번으로는 다시 켜기를 끼워도 맞힐 수 없다. 여러 대로 늘리면 실시간 방과 같이 공유 저장소로 옮긴다.
"""
import threading
import time
from collections import deque
from typing import Callable, Optional

SWEEP_EVERY = 256  # 이만큼 부를 때마다 오래된 사람 줄을 한 번에 지움(매번 전부 돌면 잠금이 길어짐)


class FailureWindow:
    def __init__(self, clock: Callable[[], float] = time.monotonic):
        self._clock = clock
        self._lock = threading.Lock()
        self._fails: dict[str, deque] = {}
        self._calls = 0

    def acquire(self, key: str, limit: int, window_s: float) -> Optional[float]:
        """시도 하나를 실패로 미리 센다. 이미 limit번이면 None(막음), 아니면 돌려줄 때 쓸 표(시각)."""
        with self._lock:
            q = self._recent_locked(key, window_s)
            if len(q) >= limit:
                return None
            at = self._clock()
            q.append(at)
            return at

    def release(self, key: str, ticket: float) -> None:
        """실패가 아니었음(코드를 찾음): 미리 센 것을 뺀다."""
        with self._lock:
            q = self._fails.get(key)
            if q is not None:
                try:
                    q.remove(ticket)
                except ValueError:
                    pass

    def reset(self) -> None:
        """테스트용."""
        with self._lock:
            self._fails.clear()
            self._calls = 0

    def _recent_locked(self, key: str, window_s: float) -> deque:
        now = self._clock()
        self._calls += 1
        if self._calls % SWEEP_EVERY == 0:  # 실패한 사람 수만큼 메모리가 늘지 않게 가끔 한 번에
            for k in [k for k, q in self._fails.items() if not q or now - q[-1] >= window_s]:
                del self._fails[k]
        q = self._fails.setdefault(key, deque())
        while q and now - q[0] >= window_s:
            q.popleft()
        return q


friend_code_failures = FailureWindow()
