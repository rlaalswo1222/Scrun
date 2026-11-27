"""서버 지표(계획 0087 2단계, ADR-0015). Alloy가 서비스마다(api·realtime·ocr) 15초마다 GET /metrics로 긁어 Prometheus에 쌓는다.

- uvicorn 프로세스가 여럿(api 2개)이라 PROMETHEUS_MULTIPROC_DIR(compose의 tmpfs, 컨테이너가 뜰 때마다 비어 있음)에 프로세스마다
  파일로 적고 /metrics가 합친다. 이 환경 변수는 prometheus_client를 불러오기 전에 있어야 해서 compose가 넣는다.
  없으면(로컬·테스트) 한 프로세스 기본 레지스트리
- 라벨은 라우트 틀(`/v1/runs/{runId}`)·메서드·상태 코드·OCR 결과 종류만: 사용자·기록 ID가 안 들어가 줄 수가 늘지 않고 개인정보도 없음
- 요청마다 드는 일: 카운터 1개 + 히스토그램 4개 더하기(다중 프로세스면 mmap 파일 쓰기). 요청 로그(request_log)가 이미 재는 값을 씀
- 이름은 scrun_ 앞붙이. Grafana 대시보드·알람(deploy/observability/grafana)이 이 이름을 씀 → 바꾸면 그쪽도(tests/test_metrics.py가 대조)
"""
import os

from prometheus_client import (
    CONTENT_TYPE_LATEST,
    REGISTRY,
    CollectorRegistry,
    Counter,
    Gauge,
    Histogram,
    generate_latest,
    multiprocess,
)

MULTIPROC_DIR = os.environ.get("PROMETHEUS_MULTIPROC_DIR") or None
CONTENT_TYPE = CONTENT_TYPE_LATEST
_enabled = True   # METRICS=0이면 main.py가 끔 → 아래 기록 함수가 모두 아무것도 안 함(요청은 미들웨어가 따로 봄)
# 메서드 라벨은 정해진 것만(클라이언트가 보낸 이상한 메서드가 줄을 늘리지 않게)
METHODS = frozenset({"GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"})

# 0073 측정: 보통 수십 ms, 200명이면 p95 0.5초 → 그 주변을 촘촘히. 사진 인증은 몇 초라 뒤쪽 칸
HTTP_BUCKETS = (0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0, 30.0)
WAIT_BUCKETS = (0.001, 0.005, 0.01, 0.05, 0.1, 0.25, 0.5, 1.0, 2.0, 5.0)
# 실사진 OCR 묶음 3.9–4.6초(11/27 오라클), 시간 제한 VERIFY_OCR_TIMEOUT_S(지금 60)
OCR_BUCKETS = (0.5, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0, 15.0, 30.0, 60.0)

HTTP_REQUESTS = Counter("scrun_http_requests_total", "처리한 HTTP 요청 수", ["method", "route", "status"])
HTTP_SECONDS = Histogram("scrun_http_request_duration_seconds", "서버 안에서 걸린 시간(응답 머리까지)",
                         ["method", "route"], buckets=HTTP_BUCKETS)
DB_SECONDS = Histogram("scrun_db_seconds_per_request", "요청 하나가 DB 쿼리에 쓴 시간 합", buckets=WAIT_BUCKETS)
POOL_WAIT = Histogram("scrun_db_pool_wait_seconds", "요청 하나가 DB 연결을 기다린 시간 합", buckets=WAIT_BUCKETS)
QUEUE_WAIT = Histogram("scrun_admission_wait_seconds", "요청 하나가 입구 번호표를 기다린 시간(계획 0071)", buckets=WAIT_BUCKETS)
OCR_BATCHES = Counter("scrun_ocr_batches_total", "계기판 사진 묶음 읽기 결과", ["result"])
OCR_SECONDS = Histogram("scrun_ocr_batch_seconds", "계기판 사진 묶음 하나를 읽는 데 걸린 시간", buckets=OCR_BUCKETS)
REALTIME_CONNECTIONS = Gauge("scrun_realtime_connections", "지금 붙어 있는 실시간 WebSocket 수",
                             multiprocess_mode="livesum")


def set_enabled(on: bool) -> None:
    global _enabled
    _enabled = on


def observe_request(method: str, route: str, status: int, seconds: float, db_s: float, pool_s: float, queue_s: float) -> None:
    method = method if method in METHODS else "OTHER"
    HTTP_REQUESTS.labels(method, route, str(status)).inc()
    HTTP_SECONDS.labels(method, route).observe(seconds)
    DB_SECONDS.observe(db_s)
    POOL_WAIT.observe(pool_s)
    QUEUE_WAIT.observe(queue_s)


def observe_ocr(result: str, seconds: float) -> None:
    """result(소문자): read·unreadable 등 읽기 결과(ReadStatus), 또는 503 — busy(읽기 자리 다 참)·timeout(엔진 바깥 시간 초과)·
    stuck(시간 넘긴 작업에 멈춤)·엔진이 준 reason(예: ocr_timeout = 엔진이 자식 프로세스를 끊음)·error(엔진 고장)"""
    if not _enabled:
        return
    OCR_BATCHES.labels(result.lower()).inc()
    OCR_SECONDS.observe(seconds)


def realtime_joined() -> None:
    if _enabled:
        REALTIME_CONNECTIONS.inc()


def realtime_left() -> None:
    if _enabled:
        REALTIME_CONNECTIONS.dec()


def render() -> bytes:
    if MULTIPROC_DIR is None:
        return generate_latest(REGISTRY)
    registry = CollectorRegistry()
    multiprocess.MultiProcessCollector(registry)
    return generate_latest(registry)


def mark_process_dead() -> None:
    """프로세스가 끝날 때(lifespan). 실시간 접속 수(livesum)에서 이 프로세스 몫을 뺌"""
    if MULTIPROC_DIR is not None:
        multiprocess.mark_process_dead(os.getpid())

