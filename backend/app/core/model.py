"""모든 요청·응답 모델의 기반. 파이썬에선 snake_case, JSON에선 camelCase(docs/api/README.md)."""
from datetime import datetime, timezone
from typing import Any

from pydantic import BaseModel, ConfigDict, SerializationInfo, SerializerFunctionWrapHandler, field_serializer
from pydantic.alias_generators import to_camel


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)

    # API 시각은 늘 UTC(Z)(docs/api/README.md). DB 연결 시간대(서버 TZ)나 앱이 보낸 +09:00이 그대로 나가지 않게
    # 모든 모델의 시각 칸을 JSON으로 낼 때 UTC로 바꾼다(라우터마다 astimezone을 잊지 않게 한 곳에서)
    @field_serializer("*", mode="wrap", when_used="json", check_fields=False)
    def _utc_times(self, value: Any, handler: SerializerFunctionWrapHandler, info: SerializationInfo):  # 반환 형식을 안 적어야 OpenAPI가 칸 원래 형식을 씀
        if isinstance(value, datetime) and value.tzinfo is not None:
            value = value.astimezone(timezone.utc)
        return handler(value)
