# 계기판 OCR 모델 (계획 0043, ADR-0012)

| 파일 | 무엇 | 만든 것 | sha256 |
|---|---|---|---|
| `yolox_nano_7seg.onnx` | 7세그먼트 칸 하나의 글자 상자(분류 `0–9 - . :`, `seg.CLASSES` 순서), 입력 1×3×160×320 BGR 0–255 | 11/17 지환. YOLOX-Nano(Apache-2.0, 처음부터 학습), 공개 Roboflow `7segmentrecognition_v3` 1,636장(퍼블릭 도메인, 분류 이름 바로잡음) + 합성 6,000장, 60 에포크(맥 M5 GPU 67분). 코드 `speedlab/ocr/train_yolox.py`·`eval.py` | `4bbedaaea0f88e10095724d3797dc7a49b27c3d0461b4c0f1fe42447a4af22ee` |

- 점수(정확도 아님, `speedlab/ocr/README.md`): 공개 test 93.1%(202칸), 합성 val 99.0%(600칸). 새 기종 러닝머신 실사진 점수는 아직 없음
- 글자 영역 찾기·라벨 인식은 PP-OCRv5(검출 mobile·한국어 인식 mobile, Baidu·PaddleOCR, Apache-2.0). 저장소에 넣지 않고 Docker 빌드 때 RapidOCR이 받음(`python -m app.console_ocr.fetch_models`)
- 다시 학습하면 파일 이름에 날짜나 버전을 붙여 새로 넣고, 이 표와 `field.py`의 기본 경로를 바꾼다
