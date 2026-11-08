from typing import Optional

from fastapi import APIRouter, Depends, Query, Request, Response

from .. import stub_data as d
from ..core.auth import Principal, stub_user
from ..core.envelope import Envelope, error_responses, ok
from ..core.errors import ApiError, ErrorCode
from ..schemas.common import CursorPage
from ..schemas.run import (
    ActiveRun,
    GhostTrack,
    RunDetail,
    RunFinishRequest,
    RunStarted,
    RunStartRequest,
    RunSummary,
    RunUpdate,
    SamplesUpload,
)

router = APIRouter(prefix="/v1", tags=["Run"])
E = ErrorCode
AUTH = error_responses(E.UNAUTHORIZED, E.TOKEN_EXPIRED, E.SIGNUP_REQUIRED)
RUN_ERRORS = error_responses(E.RUN_NOT_FOUND, E.FORBIDDEN)
STUB_NOTE = "\n\n스텁: runId `run_other` → FORBIDDEN(남의 기록), 예시에 없는 ID → RUN_NOT_FOUND"


def _check_run(run_id: str, known: set[str]) -> None:
    if run_id == "run_other":
        raise ApiError(E.FORBIDDEN)
    if run_id not in known:
        raise ApiError(E.RUN_NOT_FOUND)


KNOWN = {r["runId"] for r in d.RUNS} | {d.RUN_STARTED["runId"]}


@router.post(
    "/runs",
    status_code=201,
    summary="러닝 시작",
    description="화면 D-01에서 D-03, D-09 · BL-8, BL-11, BL-24. 카운트다운이 끝나면. 같은 clientRunId가 다시 오면 처음 결과를 돌려줌",
    response_model=Envelope[RunStarted],
    responses={**AUTH, **error_responses(E.VALIDATION_ERROR)},
)
def start_run(body: RunStartRequest, request: Request, _: Principal = Depends(stub_user)):
    return ok(request, {**d.RUN_STARTED, "clientRunId": body.client_run_id, "startedAt": body.started_at})


@router.get(
    "/runs/active",
    summary="끝나지 않은 러닝 찾기",
    description="화면 I-04 · BL-37. 앱 재실행 때. 앱 종료 후 24시간 안이면 복구 가능. 없으면 run=null",
    response_model=Envelope[ActiveRun],
    responses=AUTH,
)
def active_run(request: Request, _: Principal = Depends(stub_user)):
    return ok(request, d.ACTIVE_RUN_NONE)


@router.get(
    "/runs",
    summary="내 기록 목록",
    description="화면 F-01, F-02, D-03 · BL-20, BL-24. 월별, 커서 방식. 고스트 고르기에도 씀. 기록이 없으면 items=[]",
    response_model=Envelope[CursorPage[RunSummary]],
    responses={**AUTH, **error_responses(E.VALIDATION_ERROR)},
)
def list_runs(
    request: Request,
    month: Optional[str] = Query(default=None, pattern=r"^\d{4}-\d{2}$", description="YYYY-MM. 없으면 최근부터"),
    cursor: Optional[str] = None,
    _: Principal = Depends(stub_user),
):
    return ok(request, {"items": d.RUNS, "nextCursor": None})


@router.get("/runs/{runId}", summary="기록 상세",
            description="화면 F-03, F-04, E-12 · BL-19, BL-20. 공식 값과 출처(계기판/센서), 그래프용 시계열, 함께 달린 러너" + STUB_NOTE,
            response_model=Envelope[RunDetail], responses={**AUTH, **RUN_ERRORS})
def get_run(runId: str, request: Request, _: Principal = Depends(stub_user)):
    _check_run(runId, KNOWN)
    return ok(request, {**d.RUN_DETAIL, "runId": runId})


@router.patch("/runs/{runId}", summary="피드 공유 바꾸기",
              description="화면 E-12 · BL-19. 공유만. 기록 값 수정·삭제는 없음(PRD)" + STUB_NOTE,
              response_model=Envelope[RunSummary], responses={**AUTH, **RUN_ERRORS, **error_responses(E.VALIDATION_ERROR)})
def patch_run(runId: str, body: RunUpdate, request: Request, _: Principal = Depends(stub_user)):
    _check_run(runId, KNOWN)
    return ok(request, {**d.RUNS[0], "runId": runId, "shared": body.shared})


@router.put(
    "/runs/{runId}/samples",
    status_code=204,
    summary="속도 시계열 올리기",
    description="화면 D-10 · BL-12, BL-19. 고스트 재생·인증 대조 근거. 나눠 올리면 offsetS로 이어 붙임. 같은 묶음이 다시 오면 덮어씀" + STUB_NOTE,
    responses={**AUTH, **RUN_ERRORS, **error_responses(E.VALIDATION_ERROR, E.RUN_ALREADY_FINISHED)},
)
def put_samples(runId: str, body: SamplesUpload, _: Principal = Depends(stub_user)):
    _check_run(runId, KNOWN)
    return Response(status_code=204)


@router.post(
    "/runs/{runId}/finish",
    summary="러닝 종료",
    description="화면 D-18, D-19 · BL-14. 센서 거리·시간·종료 시각 → 인증 대기(PENDING)" + STUB_NOTE,
    response_model=Envelope[RunSummary],
    responses={**AUTH, **RUN_ERRORS, **error_responses(E.VALIDATION_ERROR, E.RUN_ALREADY_FINISHED)},
)
def finish_run(runId: str, body: RunFinishRequest, request: Request, _: Principal = Depends(stub_user)):
    _check_run(runId, KNOWN)
    return ok(request, {**d.RUNS[0], "runId": runId, "verification": "PENDING",
                        "distanceM": body.sensor.distance_m, "durationS": body.sensor.duration_s})


@router.post(
    "/runs/{runId}/discard",
    status_code=204,
    summary="복구한 러닝 버리기",
    description="화면 I-04 · BL-37. 끝나지 않은 러닝만. 끝난 기록은 지울 수 없음(PRD)" + STUB_NOTE,
    responses={**AUTH, **RUN_ERRORS, **error_responses(E.RUN_ALREADY_FINISHED)},
)
def discard_run(runId: str, _: Principal = Depends(stub_user)):
    _check_run(runId, KNOWN)
    return Response(status_code=204)


@router.get("/runs/{runId}/ghost", summary="고스트 재생용 시계열",
            description="화면 D-13 · BL-24. 본인 기록만, 코스와 무관하게 씀(PRD F04)" + STUB_NOTE,
            response_model=Envelope[GhostTrack], responses={**AUTH, **RUN_ERRORS})
def ghost(runId: str, request: Request, _: Principal = Depends(stub_user)):
    _check_run(runId, KNOWN)
    return ok(request, {**d.GHOST, "runId": runId})
