# 계기판 숫자 읽기(OCR) 실험 — 계획 0038, ADR-0012

서버가 계기판 사진에서 거리·시간을 읽는 모델을 만들고 채점하는 곳. 엔진은 **YOLOX 먼저**(Apache-2.0), 같은 채점으로 RF-DETR-Nano·YOLO11n(로컬 기준선)과 비교한다. 서버 쪽 판정·API는 `backend/app/core/verification.py`·`ocr.py`.

## 환경 (맥, 저장소에 안 올림)
```bash
cd speedlab/ocr
../../.venv/bin/python -m venv .venv          # 서버 .venv와 따로
.venv/bin/pip install torch torchvision onnx onnxruntime opencv-python-headless numpy pillow tqdm \
    loguru tabulate pycocotools ninja psutil roboflow
.venv/bin/pip install --no-build-isolation --no-deps "git+https://github.com/Megvii-BaseDetection/YOLOX.git"
```
- 공개 데이터: Roboflow `mattia-dolce-rcg61/7segmentrecognition_v3` v1(퍼블릭 도메인, 2,312장, 0–9·`-`·`.`) → `data/public/7seg_v3`. 키는 저장소 밖 `~/.roboflow_key`
- **공개 데이터의 분류 이름은 틀려 있다**(11/17 그림으로 확인): 이름 `2` 상자가 실제 `0`, `1`이 소수점, `0`이 `-`, `-`가 `8`, `.`이 `9` — 13개가 두 칸씩 밀림(원래 순서 `- . 0 … 9`에 이름만 `0 … 9 - .`로 붙은 것, 가설). `dataset.PUBLIC_NAME_FIX`로 바로잡아 읽는다. 처음 학습(40 에포크)은 이걸 모르고 돌려 공개 검증 9%였다. 10/29 시험도 같은 데이터를 썼으므로 그때 "공개 데이터만으로는 약함"도 이 때문이었을 수 있다(가설)
- 학습 환경은 원래 폴더의 `.venv`(torch·YOLOX·onnxruntime 설치됨). 위 "환경"의 따로 만든 `.venv`는 선택
- `data/`, `runs/`, 모델 파일, `*.log`는 `.gitignore`. 사람이 비친 실사진은 절대 올리지 않는다

## 파일
읽기 규칙(상자 → 글자, 칸 찾기, 전처리, 분류 순서)은 **서버 `backend/app/console_ocr/`가 원본**이고 이 폴더는 `_backend.py`로 경로를 더해 가져다 쓴다(학습·채점과 서버가 다르게 읽지 않게, 계획 0043). 여기 남은 것은 학습·채점·합성. 새 모델을 서버에 넣으려면 `console_ocr/models/`에 복사하고 그 README 표를 고친다

| 파일 | 하는 일 |
|---|---|
| `synth7seg.py` | 7세그먼트 칸 합성(COCO). 꺼진 88·글자·단위는 상자 없이, 색·번짐·과노출·LCD·기울기·원근·흐림·JPEG·작게 찍힘. 콜론(`:`)은 공개 데이터에 없어서 합성으로만 |
| `preview.py` | 상자와 정답을 그린 모음 그림(눈으로 확인) |
| `dataset.py` | 공개 + 합성을 같은 분류(`0–9 - . :`)로, 입력 가로 320 × 세로 160(비율 유지·회색 채움), 색만 흔듦(뒤집기 없음: 2↔5) |
| `mps_patch.py` | YOLOX의 글자 형 변환(`'torch.mps.FloatTensor'`)을 맥 GPU에서 받게(학습·채점에서만) |
| `train_yolox.py` | YOLOX 모델·손실 + 자체 학습 루프(YOLOX Trainer는 CUDA 전용). 5 에포크마다 글자 정확도 |
| `readout.py` | 상자 → 글자: 점수 문턱, 겹침 정리, 아래쪽 점만 소수점, 작은 글자 줄 버림 — **원본은 서버 `backend/app/console_ocr/seg.py`**(계획 0043, 여기선 가져다 씀) |
| `onnx_engine.py` | torch 없이 onnxruntime·OpenCV로 칸 하나 읽기 — 원본은 서버 `console_ocr/seg.py` |
| `field.py` | **계기판 전체 사진 → 거리·시간**(계획 0042): PP-OCRv5 글자 영역 찾기 → 라벨은 PP-OCRv5 한국어 인식, 숫자는 YOLOX → 가까운 라벨로 이름(라벨 없으면 형식 규칙) → 후보가 하나씩일 때만 READ. 센서 값은 안 씀 — 원본은 서버 `console_ocr/field.py` |
| `console_synth.py` | 계기판 전체 합성(칸 4–6개, 한·영 라벨 위·아래·왼쪽, 시간 점·콜론, 페이스·km/h, 아이콘뿐, 반사·원근) |
| `eval_console.py` | 사진 단위 채점(거리·시간 둘 다 맞음 / 틀린 READ / UNREADABLE), `--fails`로 틀린 그림 |
| `eval.py` | ONNX로 내보내 채점: 공개 test·합성 val·(있으면) 새 기종 실사진, 2스레드 처리 시간 |
| `seg7_grid.py`, `winocr.ps1`, `Dockerfile` | 10/29 첫 시험(규칙 해독기, Windows OCR, Tesseract) |

- 칸 찾기(`field.py`)는 RapidOCR 3.10.0 추가: 공용 `.venv`의 OpenCV·torch를 안 건드리게 `pip install --no-deps rapidocr==3.10.0 && pip install pyclipper omegaconf colorlog`. 모델(PP-OCRv5 검출 mobile·한국어 인식 mobile)은 처음 부를 때 받음. 모델 조건은 패키지의 `MODEL_LICENSES.md`(Apache-2.0, 저작권 Baidu·PaddleOCR)

## 돌리기
```bash
.venv/bin/python synth7seg.py data/synth/train 6000 1
.venv/bin/python synth7seg.py data/synth/val 600 2
.venv/bin/python -W ignore train_yolox.py --size nano --epochs 60 --out runs/yolox_nano
.venv/bin/python -W ignore eval.py --size nano --run runs/yolox_nano [--photos <실사진 COCO 폴더>]
.venv/bin/python console_synth.py data/console_synth_b 200 21          # 계기판 전체 합성 + truth.json
.venv/bin/python eval_console.py data/console_synth_b --onnx runs/yolox_nano/model.onnx [--fails out]
```

## 채점 기준 (ADR-0012)
1. 칸 단위 글자 정확도(소수점·콜론 포함 완전히 같아야 맞음)
2. 사진 단위로 거리·시간이 둘 다 맞은 비율(칸 찾기·칸 이름 붙이기가 생긴 뒤)
3. 오라클 ARM 2코어에서 브래킷 3장 처리 시간(지금은 맥 2스레드로 흉내)

**공개 test·합성 val 점수는 정확도가 아니다.** 공개 데이터는 학습과 같은 출처, 합성은 생성기가 만든 것이라 실제보다 높게 나온다. 보고서·발표에 쓸 수 있는 수치는 학습에 안 쓴 **새 기종 실사진** 점수뿐이고, 실측이 생기면 `docs/product/console_ocr.md`에 사람·기종·조명과 함께 적는다(CLAUDE.md 보고서 규칙이 이 파일을 OCR 실측 출처로 둠).

## 결과 (모델 비교용, 정확도 아님)
| 날짜 | 모델 | 학습 | 공개 test(202) | 합성 val(600) | 칸 3개 시간 | ONNX |
|---|---|---|---|---|---|---|
| 11/17 | YOLOX-Nano | 공개 1,636(이름 바로잡음) + 합성 6,000, 60 에포크, 맥 M5 GPU 67분 | 93.1% | 99.0% | 7.3 ms(맥 M5, 2스레드) | 3.7 MB |

- 공개 test에서 틀린 14개: 글자 빠짐 6(`1000.0` → `000.0` 등), 소수점만 3, 다른 글자 3, 더 읽음 2. 정답 쪽이 이상한 것도 있음(`1..00`)
- 합성 val의 남은 틀림은 거의 `S`(7세그먼트로 5와 같은 모양) → 생성기에서 뺌(다음 학습부터)
- 둘 다 **실제 정확도가 아니다**: 공개 test는 학습과 같은 출처(멀티미터 등), 합성은 생성기 그림. 보고서에 쓸 수 있는 건 새 기종 러닝머신 실사진 점수뿐. 오라클 ARM 2코어 시간도 다시 잼

### 칸 찾기·이름 붙이기 (계획 0042, 합성 계기판 사진 단위, 정확도 아님)
| 날짜 | 방법 | 새 합성 200장(시드 21) | 틀린 READ | 한 장 처리 |
|---|---|---|---|---|
| 11/17 | PP-OCRv5(검출·한국어 인식) + YOLOX-Nano + 라벨 짝짓기 | 80.5% | 1(끝자리 0.01 km) | 약 450 ms(맥 M5, 2스레드) |
| 11/17 #61 리뷰 뒤 | 같음 + 애매하면 못 읽음(아래) | 78.0% | 1(2.91 → 2.9 km, 두 읽기가 같이 끝자리를 놓침, 서버 허용 오차 안) | 약 440 ms |

- 못 읽은 것 대부분은 라벨 없는 계기판(시간·페이스 둘 다 콜론 → 다시 찍기가 맞는 동작). 고치면서 쓴 시드 11 200장은 85.0% → 리뷰 뒤 79.0%(틀린 READ 0)
- #61 리뷰(11/17 민재)로 "애매하면 못 읽음": 글자 전체가 숫자 모양일 때만 숫자("12.345"·"1680"을 잘라 읽지 않음), 7세그먼트 혼동(S→5)은 진짜 숫자가 있고 바로잡은 뒤 전부 숫자일 때만("BODY"·"STOP" 제외), 라벨은 거리·시간 낱말을 먼저("TIME HR:MIN" = 시간, 남은·LAP 시간 = 그 밖, 둘 다 있으면 모름), 짝짓기는 계기판의 라벨 방향(위·아래·왼쪽)을 먼저 정하고 거리 차·비율이 비슷하게 겨루면 못 읽음, 한 칸을 두 읽기가 다르게 읽으면 못 읽음

## 아직 없는 것
- 새 기종 실사진(상준 BL-45 + 헬스장 앱 브래킷)
