"""오류 코드. 표는 docs/api/README.md "오류 코드"와 같게 유지한다(새 코드는 거기 먼저)."""
from enum import Enum
from typing import Any, Optional


class ErrorCode(str, Enum):
    VALIDATION_ERROR = "VALIDATION_ERROR"
    UNAUTHORIZED = "UNAUTHORIZED"
    TOKEN_EXPIRED = "TOKEN_EXPIRED"
    INVALID_KAKAO_TOKEN = "INVALID_KAKAO_TOKEN"
    SIGNUP_REQUIRED = "SIGNUP_REQUIRED"
    FORBIDDEN = "FORBIDDEN"
    NOT_FOUND = "NOT_FOUND"
    METHOD_NOT_ALLOWED = "METHOD_NOT_ALLOWED"
    USER_NOT_FOUND = "USER_NOT_FOUND"
    RUN_NOT_FOUND = "RUN_NOT_FOUND"
    COURSE_NOT_FOUND = "COURSE_NOT_FOUND"
    FRIEND_CODE_NOT_FOUND = "FRIEND_CODE_NOT_FOUND"
    NICKNAME_DUPLICATED = "NICKNAME_DUPLICATED"
    NICKNAME_NOT_ALLOWED = "NICKNAME_NOT_ALLOWED"
    CALIBRATION_REQUIRED = "CALIBRATION_REQUIRED"
    COURSE_FULL = "COURSE_FULL"
    RUN_ALREADY_FINISHED = "RUN_ALREADY_FINISHED"
    VERIFICATION_CLOSED = "VERIFICATION_CLOSED"
    SELF_FRIEND_CODE = "SELF_FRIEND_CODE"
    ALREADY_FRIEND = "ALREADY_FRIEND"
    FRIEND_LIMIT_EXCEEDED = "FRIEND_LIMIT_EXCEEDED"
    FRIEND_NOT_RUNNING = "FRIEND_NOT_RUNNING"
    RATE_LIMITED = "RATE_LIMITED"
    INTERNAL_ERROR = "INTERNAL_ERROR"
    SERVICE_UNAVAILABLE = "SERVICE_UNAVAILABLE"


# 코드 → (HTTP 상태, 기본 메시지). 화면 문구는 앱이 코드로 고른다. 이 메시지는 개발자용.
ERRORS: dict[ErrorCode, tuple[int, str]] = {
    ErrorCode.VALIDATION_ERROR: (400, "요청 값이 올바르지 않습니다."),
    ErrorCode.UNAUTHORIZED: (401, "로그인이 필요합니다."),
    ErrorCode.TOKEN_EXPIRED: (401, "access 토큰이 만료되었습니다. refresh로 다시 받으세요."),
    ErrorCode.INVALID_KAKAO_TOKEN: (401, "카카오 토큰을 확인하지 못했습니다."),
    ErrorCode.SIGNUP_REQUIRED: (403, "가입을 먼저 마쳐야 합니다."),
    ErrorCode.FORBIDDEN: (403, "접근 권한이 없습니다."),
    ErrorCode.NOT_FOUND: (404, "없는 주소입니다."),
    ErrorCode.METHOD_NOT_ALLOWED: (405, "이 주소에서 쓸 수 없는 동작입니다."),
    ErrorCode.USER_NOT_FOUND: (404, "사용자를 찾을 수 없습니다."),
    ErrorCode.RUN_NOT_FOUND: (404, "기록을 찾을 수 없습니다."),
    ErrorCode.COURSE_NOT_FOUND: (404, "코스를 찾을 수 없습니다."),
    ErrorCode.FRIEND_CODE_NOT_FOUND: (404, "친구 코드를 찾을 수 없습니다."),
    ErrorCode.NICKNAME_DUPLICATED: (409, "이미 쓰는 닉네임입니다."),
    ErrorCode.NICKNAME_NOT_ALLOWED: (400, "쓸 수 없는 닉네임입니다."),
    ErrorCode.CALIBRATION_REQUIRED: (409, "속도 보정을 먼저 해야 합니다."),
    ErrorCode.COURSE_FULL: (409, "코스가 가득 찼습니다."),
    ErrorCode.RUN_ALREADY_FINISHED: (409, "이미 끝난 러닝입니다."),
    ErrorCode.VERIFICATION_CLOSED: (409, "인증 가능 시간이 지났습니다."),
    ErrorCode.SELF_FRIEND_CODE: (400, "내 친구 코드입니다."),
    ErrorCode.ALREADY_FRIEND: (409, "이미 친구입니다."),
    ErrorCode.FRIEND_LIMIT_EXCEEDED: (409, "친구는 50명까지입니다."),
    ErrorCode.FRIEND_NOT_RUNNING: (409, "친구가 달리기를 마쳤습니다."),
    ErrorCode.RATE_LIMITED: (429, "요청이 너무 잦습니다."),
    ErrorCode.INTERNAL_ERROR: (500, "서버 오류가 났습니다."),
    ErrorCode.SERVICE_UNAVAILABLE: (503, "서버를 잠시 쓸 수 없습니다."),
}


class ApiError(Exception):
    """라우터에서 raise ApiError(ErrorCode.X) → 공통 껍데기 + 맞는 상태 코드로 응답."""

    def __init__(self, code: ErrorCode, message: Optional[str] = None, details: Any = None):
        self.code = code
        self.status, default_message = ERRORS[code]
        self.message = message or default_message
        self.details = details
        super().__init__(self.message)
