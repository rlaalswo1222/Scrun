# ADR-0012: 계기판 숫자 읽기는 Apache-2.0 검출 모델(YOLOX 먼저)로, 같은 채점에서 RF-DETR·YOLO11과 비교해 정한다

- 상태: 승인 (엔진은 잠정: 비교 채점 뒤 확정)
- 날짜: 2026-11-16
- 결정한 사람: 김지환 (계획 0038 정할 것 1, 비교 자료 [`docs/plans/0038-ocr-engine.html`](../plans/0038-ocr-engine.html)을 보고 추천안)

## 배경
인증(BL-17·18)은 서버가 계기판 사진에서 거리·시간을 읽는다(11/06 서버 처리 확정). 판정 API는 계획 0038(PR #55)에서 엔진 자리(`backend/app/core/ocr.py`)만 두고 만들었다. 10/29 시험(`docs/product/console_ocr.md`, 계획 초안 0011)에서 일반 OCR·규칙 해독기는 7세그먼트를 거의 못 읽었다. **Ultralytics YOLO11n + 공개 데이터 + 합성 + 소수점·형식 규칙**만 세 기종 44칸을 맞혔다(사진 3장, 생성기를 그 사진 보고 만들어 과대평가 가능). 그때 모델·코드는 남아 있지 않다(11/08 확인).

## 선택지
| 선택지 | 장점 | 단점 |
|---|---|---|
| A. YOLO11n(Ultralytics) | 10/29에 됨, 학습·내보내기 쉬움, 2.6M | AGPL-3.0. Ultralytics는 학습한 모델도 AGPL로 보고, 서비스하려면 서버 소스·가중치 공개 또는 유료 라이선스. 저장소에 LICENSE가 없어 지금 상태로는 못 씀 |
| B. YOLOX-Nano·Tiny(Megvii) | Apache-2.0, 가장 작음(0.91M·5.06M), A와 같은 계열이라 데이터 형식·입력 크기 그대로 | 우리 문제에선 안 해 봄, 공개 가중치 조건 따로 확인, 최근 관리 상태 모름 |
| C. RF-DETR-Nano(Roboflow) | Apache-2.0(Nano·Small·Medium), 활발히 관리 | 약 30M로 무거움, CPU 수치 공개 없음, 작은 소수점을 잡는지 모름 |
| D. 규칙 해독기(학습 없음) | 라이선스 문제 없음, 가벼움 | 다른 기종에서 0/4·0/3 |

## 결정
B를 먼저 학습하고, **같은 채점**에서 C와 A(로컬 기준선, 서버엔 안 올림)를 비교한다. 데이터·합성 생성기·칸 묶기·소수점 규칙·채점은 엔진과 떼어 `speedlab/ocr/`에 만든다. 서버는 ONNX로 내보낸 모델을 `onnxruntime`(MIT)으로 돌린다.
- 채점: 학습에 안 쓴 새 기종 사진으로 (1) 칸 단위 정확도 (2) 사진 단위로 거리·시간이 둘 다 맞은 비율 (3) 오라클 ARM 2코어에서 브래킷 3장 처리 시간
- B가 A보다 확실히 못하면 그때 C, 또는 A의 라이선스 비용(서버 AGPL 공개 또는 유료)을 팀과 다시 본다

## 이유
- 출시 기준에서 라이선스가 막히지 않는 길을 먼저 간다. A의 의무가 가중치까지 미치는지는 다툼이 있지만, Ultralytics의 공식 입장이 그렇고 판례가 없어 위험을 떠안을 이유가 없다
- B는 A와 같은 앵커 없는 YOLO 계열이라 10/29 방식(데이터 형식·입력 320×160·후처리)을 거의 그대로 옮길 수 있다
- 서버가 ARM 2코어(ADR-0008)라 작은 모델이 유리하다(가설, 채점 (3)으로 확인)
- 로컬 비교 실험은 배포·서비스가 아니라 A의 AGPL 의무가 생기지 않는다(AGPL 13조는 고친 프로그램으로 원격 사용자와 상호작용할 때)

## 결과와 위험
- 정확도는 새 기종 사진이 모여야 말할 수 있다. 상준 BL-45 사진 + 헬스장 앱 브래킷 사진(0033). 사람이 비친 사진은 저장소에 안 올리고 결과만 `console_ocr.md`
- 공개 데이터(Roboflow `7segmentrecognition_v3`, 퍼블릭 도메인)와 YOLOX 공개 가중치의 조건을 받기 전에 다시 확인한다
- 다시 검토할 조건: B·C가 A보다 사진 단위 정확도가 확실히 낮을 때, 서버 3장 처리 시간이 OCR 시간 제한(15초)에 가까울 때

## 참고
- 비교 자료 [`docs/plans/0038-ocr-engine.html`](../plans/0038-ocr-engine.html), 계획 [0038](../plans/0038-console-verification.md), `docs/product/console_ocr.md`, ADR-0008(서버), ADR-0011(브래킷 3장)
- [Ultralytics License](https://ultralytics.com/license), [Ultralytics 포럼: AGPL on-prem](https://community.ultralytics.com/t/yolov11-agpl-license-for-on-premises-use/1529), [GNU AGPL v3](https://www.gnu.org/licenses/agpl-3.0.html)
- [YOLOX LICENSE](https://github.com/Megvii-BaseDetection/YOLOX/blob/main/LICENSE), [YOLOX 논문](https://arxiv.org/abs/2107.08430), [RF-DETR 라이선스](https://roboflow.com/model-licenses/rf-detr)
