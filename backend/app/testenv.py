"""테스트·예시 내보내기 전용 설정. app.core.config보다 먼저 불러야 한다(설정은 import 때 한 번 읽음).

테스트 DB + 가짜 카카오만 쓰는 곳(pytest, scripts/export_openapi·notion_pages·real_examples)에서
매번 새 임시 서명 키와 KAKAO_MODE=fake를 쓴다. .env가 없어도 돌고, 개발용 .env의 값과 섞이지 않는다.
운영·개발 서버(uvicorn)나 진짜 DB를 쓰는 스크립트에서는 부르지 않는다.
"""
import os
import secrets
import sys

_applied = False


def use_test_settings() -> None:
    """여러 번 불러도 됨(스크립트끼리 서로 import). 단 첫 호출은 설정을 읽기 전이어야 한다."""
    global _applied
    if _applied:
        return
    if "app.core.config" in sys.modules:
        raise RuntimeError("use_test_settings()는 app을 import하기 전에 불러야 합니다.")
    os.environ["JWT_SECRET"] = secrets.token_urlsafe(48)
    os.environ["KAKAO_MODE"] = "fake"
    _applied = True
