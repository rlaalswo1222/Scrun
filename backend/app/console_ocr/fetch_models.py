"""Docker 빌드 때 PP-OCRv5 모델(검출·한국어 인식)을 받아 이미지에 넣는다(계획 0043). 요청 중에 받지 않게.

    python -m app.console_ocr.fetch_models

RapidOCR이 자기 패키지 폴더에 받고 sha256을 확인한다. 받은 뒤 빈 그림을 한 번 읽어 모델이 열리는지 본다.
"""
import numpy as np

from .field import ConsoleReader
from .pool import MODEL


def main() -> None:
    reader = ConsoleReader(str(MODEL), threads=1)
    res = reader.read(np.zeros((200, 400, 3), np.uint8))
    print(f"계기판 OCR 모델 준비됨(빈 그림 → {res.status})")


if __name__ == "__main__":
    main()
