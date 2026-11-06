"""모든 요청·응답 모델의 기반. 파이썬에선 snake_case, JSON에선 camelCase(docs/api/README.md)."""
from pydantic import BaseModel, ConfigDict
from pydantic.alias_generators import to_camel


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)
