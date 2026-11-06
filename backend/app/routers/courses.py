from fastapi import APIRouter, Depends, Request

from .. import stub_data as d
from ..core.auth import Principal, current_user
from ..core.envelope import Envelope, error_responses, ok
from ..core.errors import ApiError, ErrorCode
from ..schemas.course import Course, CourseDetail, MapEntry

router = APIRouter(prefix="/v1", tags=["Course"])
E = ErrorCode
AUTH = error_responses(E.UNAUTHORIZED, E.TOKEN_EXPIRED, E.SIGNUP_REQUIRED)


@router.get("/courses", summary="코스 목록", description="화면 C-01 · BL-7, BL-43. 실사 영상 코스는 최하위라 type만 열어 둠",
            response_model=Envelope[list[Course]], responses=AUTH)
def list_courses(request: Request, _: Principal = Depends(current_user)):
    return ok(request, d.COURSES)


@router.get("/courses/{courseId}", summary="코스 상세", description="화면 C-02 · BL-7",
            response_model=Envelope[CourseDetail], responses={**AUTH, **error_responses(E.COURSE_NOT_FOUND)})
def get_course(courseId: str, request: Request, _: Principal = Depends(current_user)):
    if courseId != d.COURSE_DETAIL["courseId"]:
        raise ApiError(E.COURSE_NOT_FOUND)
    return ok(request, d.COURSE_DETAIL)


@router.post(
    "/courses/{courseId}/entries",
    status_code=201,
    summary="맵 입장",
    description="화면 C-02, D-01, I-10, I-11 · BL-7, BL-21. 맵 인스턴스 배정(최대 20명), 입장 위치, 실시간 접속 정보(모양 미정, BL-25)."
    "\n\n스텁: courseId `crs_full` → COURSE_FULL, `crs_nocalib` → CALIBRATION_REQUIRED",
    response_model=Envelope[MapEntry],
    responses={**AUTH, **error_responses(E.COURSE_NOT_FOUND, E.COURSE_FULL, E.CALIBRATION_REQUIRED)},
)
def enter_map(courseId: str, request: Request, _: Principal = Depends(current_user)):
    if courseId == "crs_full":
        raise ApiError(E.COURSE_FULL)
    if courseId == "crs_nocalib":
        raise ApiError(E.CALIBRATION_REQUIRED)
    if courseId != d.MAP_ENTRY["courseId"]:
        raise ApiError(E.COURSE_NOT_FOUND)
    return ok(request, d.MAP_ENTRY)
