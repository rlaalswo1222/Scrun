"""서버가 쓰는 읽기 코드(backend/app/console_ocr, 계획 0046)를 이 폴더 스크립트에서 가져다 쓰게 경로를 더한다.
읽기 규칙의 원본은 서버 쪽 하나(학습·채점과 서버가 다르게 읽지 않게)."""
import sys
from pathlib import Path

BACKEND = Path(__file__).resolve().parents[2] / "backend"
if str(BACKEND) not in sys.path:
    sys.path.insert(0, str(BACKEND))
