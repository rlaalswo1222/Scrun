"""모든 요청·응답 모델의 기반. 파이썬에선 snake_case, JSON에선 camelCase(docs/api/README.md)."""
from datetime import datetime, timezone
from typing import Annotated, Any

from pydantic import BaseModel, ConfigDict, SerializerFunctionWrapHandler, WrapSerializer
from pydantic.alias_generators import to_camel


def _utc_json(value: Any, handler: SerializerFunctionWrapHandler):  # 반환 형식을 안 적어야 OpenAPI가 칸 원래 형식(date-time)을 씀
    if isinstance(value, datetime) and value.tzinfo is not None:
        value = value.astimezone(timezone.utc)
    return handler(value)


UTC_JSON = WrapSerializer(_utc_json, when_used="json")

# API 시각은 늘 UTC(Z)(docs/api/README.md). DB 연결 시간대(서버 TZ)나 앱이 보낸 +09:00이 그대로 나가지 않게, 모델의 시각 칸은
# datetime 대신 이 타입을 쓴다(Optional[UtcDatetime]도 됨). 그냥 datetime 칸이 있으면 tests/test_contract.py가 실패한다.
# 예전엔 CamelModel에 field_serializer("*")를 걸어 모든 칸이 JSON이 될 때마다 파이썬을 불렀다 → 시각 칸에만(계획 0073 측정:
# S1 100명 이벤트 루프 CPU의 약 5%가 그 함수)
UtcDatetime = Annotated[datetime, UTC_JSON]


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)
