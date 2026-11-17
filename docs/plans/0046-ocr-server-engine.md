# 계획 0046: 서버 OCR 엔진 연결 — 가짜 엔진 자리에 진짜 읽기 (BL-17, S4 지환)

- 상태: **승인(11/17 지환: 정할 것 1–3 추천안) · 구현 · PR #68 · 11/18 민재 `/code-review` 반영** (#61은 11/18 머지). 남은 것: 오라클 배포 뒤 시간·메모리, 실사진 채점
- 이어서: 계획 0038 "하는 것 2"(엔진 자리 `core/ocr.py`, "진짜 엔진은 자식 프로세스에서 돌려 죽이는 방식으로"), ADR-0012(엔진), 계획 0042(칸 찾기)

## 하는 것
1. **읽기 코드 한 곳**: `backend/app/console_ocr/`(readout·칸 하나 읽기·칸 찾기 `field`)를 원본으로, `speedlab/ocr`는 이것을 가져다 씀(학습·채점·합성만 남김)
2. **엔진 `OCR_ENGINE=onnx`**: 모델을 미리 올린 **자식 프로세스 2개**(onnxruntime 1스레드씩, 오라클 2코어 ADR-0008)가 번갈아 읽음. 시간 제한을 넘기면 그 프로세스를 죽이고 새로 띄움(지금 스레드 방식은 멈춘 작업을 못 죽임). 프로세스가 죽으면 503(다시 찍기 횟수 안 셈)
3. **사진 3장(브래킷) 합치기**: 읽힌 사진의 거리·시간이 **모두 같을 때만** READ, 하나라도 다르면 UNREADABLE(다시 찍기). 모두 못 열면 BadPhoto. 센서 값은 안 씀
4. **모델 파일**: 우리가 학습한 YOLOX-Nano ONNX(3.7 MB)는 **저장소에 커밋**(`console_ocr/models/`, 출처·점수·sha256 기록). PP-OCRv5(Baidu·PaddleOCR, Apache-2.0)는 Docker 빌드 때 RapidOCR이 받아 이미지에 넣음(요청 중에 받지 않음)
5. 배포: `requirements.txt`에 onnxruntime·rapidocr, Dockerfile에 모델 받기, 서버 compose `OCR_ENGINE: onnx`(민재 배포)
6. 확인: pytest(합치기 규칙, 프로세스 시간 초과 → 죽이고 503, 실제 모델로 합성 계기판 한 장 READ), aarch64 휠이 있는지(서버는 ARM), 맥에서 사진 3장 처리 시간. 오라클 시간은 배포 뒤 민재와

## 정한 것 (11/17 지환)
1. 모델 파일: 저장소에 커밋 (GitHub Release 아님)
2. 3장 결과가 다르면: 다시 찍기 (다수결 아님)
3. 실행: 자식 프로세스 2개 (메모리 더 씀, 서버에서 잼)

## 위험 (가설)
- 오라클 ARM에서 3장 처리 시간: 맥 M5 2스레드 한 장 약 450 ms → ARM 1스레드는 몇 배 느릴 수 있음. 15초 제한 안인지 배포 뒤 잼. 넘으면 3장 중 0 EV 먼저 읽고 맞으면 멈추기
- 정확도는 실사진 전까지 모름(계획 0042). 실사진이 오면 같은 엔진으로 채점

## 진행 (11/17)
- `backend/app/console_ocr/`: `seg`(칸 하나, speedlab readout·onnx_engine·letterbox를 옮김), `field`(칸 찾기, 결과 이름 `PhotoRead`), `pool`(자식 프로세스), `fetch_models`(빌드 때 PP-OCRv5), `models/yolox_nano_7seg.onnx`(3.7 MB, sha256·출처 README). speedlab은 `_backend.py`로 가져다 씀 → 옮긴 뒤 합성 200장 78.0%(#61 리뷰 반영 뒤 값) 그대로
- `core/ocr.py`: `OCR_ENGINE=onnx` → `OnnxOcr`(브래킷 `combine`, `PoolBusy`·`ReadTimeout` → `EngineUnavailable` → 라우터 503, 프로세스 죽음·엔진 예외 → 503 + 오류 기록). 서버 시작 때 프로세스를 미리 띄움(`start_ocr`), 끌 때 닫음
- 배포: requirements(onnxruntime 1.32.0·rapidocr 3.10.0·opencv-python-headless·numpy, **aarch64 휠 모두 있음** 11/17 확인), Dockerfile에서 `fetch_models`, 서버 compose `OCR_ENGINE: onnx`. 이 맥엔 Docker가 없어 이미지 빌드는 서버에서 처음 확인(민재)
- 찾아 고친 것: 프로세스가 뜨다 죽으면 3분(START_TIMEOUT) 기다린 뒤에야 알던 것 → 살아 있는지 같이 봐서 바로 다시 띄움
- 확인: pytest 352(develop #63–#67 합친 뒤, 새 `test_ocr_engine` 11: 합치기 5, 시간 초과 → 죽이고 새로, 죽은 프로세스 교체, 뜨다 죽음, 일꾼 없음 → 바쁨, 라우터 503·횟수 안 셈, **실제 모델로 합성 계기판(시간 34.49 점·페이스 11:06) READ**). 맥 M5 1스레드 사진 3장 **2.0–3.0초**, 프로세스 띄우기 0.8초
- 재촬영 마감 UTC는 민재 #65(응답 모델 한 곳에서 UTC)로 고쳐져 이 PR에서 뺌
- 남은 것: 서버 배포 뒤 오라클 ARM 1스레드 3장 시간·메모리(민재가 디스크 여유부터 보고 배포), 실사진 채점. 다음은 계획 0045 확인 단계(서버 `CONFIRM_REQUIRED`·`confirm` API, 앱 E-05b 연결)

## 진행 (11/18 민재 Windows)
- #61 반영 커밋 리뷰 10건 중 칸 찾기 규칙(`field.py`): "PACE MIN"·"페이스(분)"·"SPEED km/n"이 시간·거리 칸이 되던 것(틀린 READ), "ELAPSED TIME"이 LAP 때문에 구간 시간이 되던 것, 방향 필터가 소수 방향 칸을 통째로 버리던 것, 해석 안 되는 칸이 다른 칸 값을 막던 것, "3.I2km" 혼동 글자. 규칙 테스트 `test_console_field` 23개(고치기 전 8개 실패). 겹침 정리 한 번만, speedlab 이름 고침은 `load_coco(public=True)`
- #68 리뷰 10건 중 엔진: 기다린 요청이 멀쩡한 프로세스를 죽이던 것(읽기 자체가 넘길 때만 죽임), 연달아 못 뜨면 바로 503(EngineDown), 압축 폭탄(작은 JPEG에 큰 크기 → `OPENCV_IO_MAX_IMAGE_PIXELS` 50 MP), 자식 OpenCV 1스레드, close 경쟁, 종료 때 join을 스레드로, VERIFY_OCR_TIMEOUT_S 1–2초. 틀린 지적: rapidocr 3.10.0은 opencv-python-headless에 의존(GUI 판과 겹치지 않음). 남긴 것: 라우터 스레드 제한과 프로세스 제한 두 겹(엔진에 마감을 넘기는 쪽이 단순, 지금은 1초 차로 맞춤)
- 합성 200장(Windows 맑은 고딕, 시드 21): 고치기 전·후 같음 82.0%, 틀린 READ 0, 한 장 약 670 ms(i7 노트북 2스레드). 맥 78.0%와 다른 건 글꼴. pytest 376
