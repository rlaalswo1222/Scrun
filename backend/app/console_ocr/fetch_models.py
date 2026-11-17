"""Docker 빌드 때 PP-OCRv5 모델(검출·한국어 인식)을 받아 이미지에 넣는다(계획 0046). 요청 중에 받지 않게.

    python -m app.console_ocr.fetch_models

RapidOCR이 자기 패키지 폴더에 받고 sha256을 확인한다. 인식 모델은 글자를 처음 찾았을 때 받으므로 글자를 그린 그림을
읽혀(warm_up) 둘 다 받는다. 못 받으면 빌드가 실패한다(컨테이너는 app 사용자라 실행 중에는 패키지 폴더에 못 씀).
"""
from .field import ConsoleReader
from .pool import MODEL


def main() -> None:
    ConsoleReader(str(MODEL), threads=1).warm_up()
    print("계기판 OCR 모델 준비됨(검출·한국어 인식)")


if __name__ == "__main__":
    main()
