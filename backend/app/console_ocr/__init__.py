"""계기판 사진 읽기 엔진(BL-17, ADR-0012, 계획 0042·0046).

- `seg`: 7세그먼트 칸 하나 읽기(우리가 학습한 YOLOX-Nano ONNX, `models/`)
- `field`: 사진 전체 → 거리·시간(PP-OCRv5 글자 영역·라벨 + `seg`, 센서 값은 안 씀)
- `pool`: 위를 자식 프로세스에서 돌림(시간 제한을 넘기면 죽이고 새로 띄움)

판정·API는 `app.core.ocr`(엔진 자리)·`app.core.verification`. 학습·채점·합성은 `speedlab/ocr`가 이 코드를 가져다 씀.
이 패키지는 `app.core`(설정)를 부르지 않는다(speedlab에서도 쓰게).
"""
