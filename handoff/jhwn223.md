# jhwn223 인수인계 (2026-11-27, OCR 실사진 첫 채점·규칙·재학습)

jhwn223(git `jhwn223`, 지환)의 개인 작업 메모. 새 채팅은 `CLAUDE.md`와 이 파일을 먼저 읽는다. 팀원은 읽기만.

## 지금 (11/27): 계획 0082 규칙 **PR #156**(`feat/ocr-field-rules`, 리뷰 대기) · 계획 0083 7세그먼트 다시 학습 중
- **헬스장 실사진 첫 채점**(11/27 새벽, 기종 4개 23장, 정답 지환 확인, 결과 `docs/product/console_ocr.md` 끝 절): 정답 있는 13장 중 맞음 1, **틀린 READ 0**. 사진·정답·자른 칸은 로컬 `speedlab/ocr/data/gym_raw/`·`gym_cells/`(얼굴 비침, 저장소에 안 올림), 확인 페이지 `speedlab/ocr/runs/gym/review.html`
  - 러닝머신 A(빨간 LED): 라벨 짝짓기는 맞고 **7세그먼트가 꺼진 자리 베이지 "8"·번짐을 잘못 읽음** → 0083
  - DRAX: 7세그먼트는 맞는데 규칙이 "0.018"·"0042"를 버림 → 0082
- **0082**(구현·PR): km 소수 셋째 자리, 콜론 없는 시간은 두 읽기가 같을 때만, 낱말(점수 ≥ 0.8)엔 7세그먼트 안 돌림. pytest 497, 합성·실사진 틀린 READ 0 유지(맞음은 안 늘어남)
- **0083**(학습 중): 생성기에 색이 다른 꺼진 획·큰 번짐·둘레 판·분홍빛 빨강 → `data/synth_v2`로 `runs/yolox_nano_v2` 60 에포크(맥 GPU 약 67분, 로그 `runs/yolox_nano_v2.log`). 기준: 실사진 온전한 칸 9개 중 옛 모델 2개. 끝나면 `eval_cells.py`(옛·새 모델 나란히), 공개 test, `bench_det.py`, 헬스장 23장 → 좋으면 `models/yolox_nano_7seg_v2.onnx`로 바꿔 PR. 0083 코드(`synth7seg.py`·`train_yolox.py`)는 0082 PR에 안 넣음
- 앱 빌드: 11/26 밤 최신 develop(c876789)을 `Scrun-s4`에서 iOS 개발 빌드 → 지환 아이폰 15 Pro Max 설치(팀 계정 서명 신뢰함). 원래 `Scrun` 폴더는 안 건드림(stash·브랜치 바꾸기 막힘). `Scrun-s4`에 우주 루프 큰 파일 199개·`ScrunLocal.json` 복사해 둠

## 그전 (11/26): 계획 0081 OCR 검출 크기 **#154 머지**(11/26 민재) · 계획 0082 초안
- #154 민재 리뷰: Windows x86 1스레드 장당 4.1 → 2.0초(오라클 3.1–4.3초와 비슷 → 오라클도 절반쯤, 가설). **계기판이 사진 폭 25%로 작으면 960에서 못 읽음** → 헬스장에서 계기판이 사진에 얼마나 크게 찍히는지 같이 잴 것. 배포·오라클 로그는 민재
- 계기: 민재 부하 시험 중 "OCR 10초 걸린다, 더 빠르게 안 되냐"(11/19 실측 묶음 11.70초). 원인: RapidOCR 기본 설정이 앱 사진 2048×1342를 **원본 크기로 글자 찾기**(검출이 한 장의 약 60%)
- 0081 구현(워크트리 `Scrun-s4`, develop 17a73ad에서): `field.DET_SIDE = 960`(검출만 줄이고 읽기는 원본), 로그에 찾기·인식 시간 따로, 테스트 1개, pytest 487. 합성 200장 1스레드 한 장 **1.07 → 0.55초**, 맞음 75.0 → 78.0%, 틀린 READ 1 → 0(합성이라 정확도 아님). 비교 스크립트 `speedlab/ocr/bench_det.py`, 결과 `speedlab/ocr/runs/bench_det/`(git 밖)
- 0082 초안: 인터넷 사진 2장(로컬 `speedlab/ocr/data/web/`, 저장소에 안 올림)에서 찾은 라벨 오인식(DISTACE)·점·콜론 빠짐·두 읽기 불일치를 못 잡음(00:86이 해석 안 돼 빠짐 → 7세그 8초만 남음)·7세그 자르기. 헬스장 실사진 본 뒤 고칠 것 확정
- **다음**: (1) 11/26–27 헬스장 — 앱으로 여러 기종·각도·반사, 사진마다 계기판 거리·시간 적기(`truth.json`), 원본·조건은 `data/` (2) 실사진으로 `bench_det.py`(default vs 960, 계기판 폭 %)·`eval_console.py --fails` → 0081 판정 확정 (3) 오라클 배포 뒤 묶음 시간(민재) → 0055 시간 제한 기본값 (4) 0082 정할 것 1·2- 민재에게: 0081 결과(한 장 절반) 알림, 11/21·25 헬스장 실물 계기판 사진이 폰에 남았는지 물어볼 것(서버엔 안 남음)
- 브랜치를 develop에서 새로 땄으므로 이 파일의 11/19 변경(커밋 안 했던 것)도 이 브랜치에 같이 있음

## 그전 (11/19): 남은 내 몫은 OCR 실사진 정확도(BL-17)
- 11/18–19 머지·배포(민재가 리뷰 반영을 브랜치에 직접 커밋): **#94 기록 탭**(계획 0053, 셋째 칸 "N/목표"는 민재), **#95 주간 목표**(계획 0054). **홈 BL-31은 민재가 #106으로 끝냄**(계획 0057, `WeeklyGoalSheet`를 홈 B-02와 같이 씀) → 내 홈 작업(`feat/home`)은 쓰지 않음, `git stash` "11/19 feat/home 지환판"(버려도 됨). 계획 번호는 develop 기준 0059까지 씀
- **OCR(계획 0055 민재 "빠르게 읽기", #99)**: 앱이 가이드 + 15%로 잘라 보냄(2048×1342), 서버는 0 EV부터 읽고 같은 값 두 장이면 멈춤(내 합치기 규칙 "읽힌 값이 모두 같아야 READ"가 "두 장 일치"로 바뀜), 시간 초과는 503 `details.reason: OCR_TIMEOUT`, 오라클 `VERIFY_OCR_TIMEOUT_S=60` 임시. 아이폰 시험 6장 모두 "못 읽음"인데 **모니터에 띄운 인터넷 계기판**이라 정확도 근거가 아니고 원인도 모름(서버는 읽은 글자를 로그에 안 남김)
- **다음(내 몫)**: 실물 러닝머신 계기판 사진 모으기(헬스장 + 상준 BL-45) → `speedlab/ocr/eval_console.py --fails`로 칸 찾기·이름 붙이기·7세그먼트 중 어디서 떨어지는지 → 고치기 → 점수는 `docs/product/console_ocr.md`. 시간 제한 기본값·한 장 시간(검출 입력 크기·긴 변 1600)도 민재와 같이 정함
- 정한 것(11/19 민재·팀): **에셋 구매 안 함**(팀 제작·무료 에셋), **스토어 출시는 종강 뒤**(#110)
- 민재가 물어본 것: 실명 파일 2개 동의(없으면 "팀원 J"), 옛 브랜치 `feat/airpods-flight`·`feat/airpods-flight-analysis` 지워도 되는지
- 로컬 개발 DB: `k_7781`(빈기록)에 주간 목표·주기별 인증 기록, `k_7782`(새러너) 새 계정, `k_777`의 빈 보정 줄을 예시 값으로 채움

## 그전 (11/17): S4 계획 0038 앱 화면 PR·OCR PR #61, 계획 0042
- **작업 폴더**: S4는 워크트리 `../Scrun-s4`에서만(원래 `Scrun` 폴더는 다른 창이 Unity·아이폰 빌드에 씀). 워크트리 Unity를 열면 Y Bot·PolyHaven 원본이 없어 메타가 지워지거나 바뀜 → 원래 폴더에서 원본만 복사해 둠(11/17), 커밋 전 `git checkout -- …/YBot …/PolyHaven`
- **앱 화면**(`feat/verification-screens`, 계획 0038 "앱 화면"): E-02·E-04–E-10·I-06, Figma와 서버 어긋난 네 곳 정함(E-08·09 다시 찍기, E-04 사진만, 남은 시간 = 서버 마감, 종료 시각 줄 뺌). 에디터 메뉴 **Scrun → 개발 → 계기판 인증 화면 열기**로 달리기 없이 확인. 가짜 카메라가 같은 바이트를 써서 서버 같은 사진 막기에 걸리던 것 고침. EditMode 382, pytest 295, 11/17 에디터에서 결과 화면 전부 눈 확인
- **OCR**(PR #61, ADR-0012·계획 0042 승인): YOLOX-Nano 칸 읽기 + PP-OCRv5 라벨로 거리·시간 칸 이름 붙이기(센서 값 안 씀). 합성 새 200장 80.5%(정확도 아님). 다음: **새 기종 실사진**(상준 BL-45 + 헬스장) → 오라클 ARM 시간 → 서버 엔진 연결(자식 프로세스) → RF-DETR·YOLO11 비교
- **아이폰 서명**(11/17 결정, CLAUDE.md "자주 쓰는 명령"): 팀 공용 Apple 계정 무료 팀 TEAM07 Capstone + `com.scrun.app`, 2단계 인증 코드는 민재. 내 15 Pro Max 설치됨(11/24 11:39까지). 원래 폴더 `fix/ios-local-bundle-id`(번들 ID 바꾸기)는 공용 팀이면 필요 없음 → 그 창에서 정리
- 상준에게: Figma E-04 읽은 값 칩·E-08 "종료 시각" 줄 빼기, E-02 예시 사진, 한글 줄바꿈 끝 글자 넘어감(E-02 나가기·E-08 안내), 계기판 실사진(BL-45)
- 민재에게: 노션 BL-18 "판독값" API 빼기, Swift 촬영 화면의 가이드 위치를 사진 자르기에 쓰게, 공용 Apple 계정 메시지(보냄?), PR에 형 파일 건드린 곳(FakeCamera·FakeNative·CourseDetailScreen·ApiClient·HttpTransport)

## 그전 (11/16 밤): S4 계획 [0038](../docs/plans/0038-console-verification.md) — 서버 인증 API PR #55(리뷰 대기)
- 브랜치 `feat/verification-api`(develop efef985에서) → **PR #55**. pytest 280/280, `/code-review` 두 축 반영(계획 0038 "진행"), 실제 uvicorn에서 2 MB 사진 2장 → VERIFIED 확인
- 정한 것(11/16): 거리 15%, 시간 계기판 − 앱 −30초에서 +5분(+ 쉰 시간), 마감 = 서버가 종료를 받은 뒤 10분, 다시 찍기 3번, E-03 0:45. `reading` API 뺌. 값은 `VERIFY_*` 환경 변수
- 민재 답(11/16): 달리기 API DB #52 머지·오라클 배포, 0033 카메라 PR 연다(C# `ShootAsync`·가짜 그대로), #49·#50 머지·후속은 민재(#51). 계획 번호 0037은 민재 것이라 내 건 0038
- 로컬: `backend/.env`에 `OCR_ENGINE=fake` 넣음, 개발 DB `alembic upgrade head`(b8e1f0c3d5a7). 오라클 배포 때도 마이그레이션 필요(민재에게)
- 다음: (1) #55 민재 리뷰 반영 (2) OCR 엔진 비교 설명 HTML(YOLO AGPL vs Apache·MIT 검출기, 라이선스 원문 출처) → ADR-0012 (3) `speedlab/ocr/` 데이터·합성 생성기·채점 다시(10/29 모델 없어짐) (4) 0033 머지 뒤 앱 화면 E-02·E-04–E-10(`feat/verification-screens`) (5) 노션 작업 표 BL-18 줄의 "판독값" API 빼 달라고 민재에게(민재 워크스페이스라 못 고침)
- 기록 조회 API(S5) 만들 때 인증 상태는 `core/run_access.verification_of`(마감 지난 PENDING = 미인증)

## 그전 (11/16): 계획 0035 센서 준비·달림 인식·카운트다운(BL-10·11) **PR #49**(11/16 민재 리뷰 1–8 반영, develop(#48) 합침, 다시 리뷰 대기)
- 브랜치 `feat/run-ready-countdown`(develop e14a16b에서). `/code-review` 반영 끝(계획 0035 끝 절, EditMode 268/268). 다음: 민재 리뷰 반영, 머지되면 계획 0035 상태 "완료", 그다음 S4(계기판 촬영 E-02·E-04·서버 OCR, 민재 카메라 0033 위에)
- 한 것·확인·바뀐 것은 [계획 0035](../docs/plans/0035-run-ready-countdown.md) "진행". EditMode 268/268, 에디터 Play로 D-07 → D-08 → D-09 → HUD, I-01·다시 시도, X → D-01, 카운트다운 중 멈춤 → D-08 확인
- 민재 코드 건드린 곳(PR에 표시): `MapScreen`(임시 `StartRunning()` 지움, 센서 준비 층 연결), `RunSession`(`ClientRunId`·`RunId`·`StartBody` 추가), `Api.cs`·`Dtos.cs`(`RunsApi.StartAsync`)
- 남은 확인: 아이폰 책상(폰 센서 Ok·걸음 감지까지 몇 초), 러닝머신(1.2초 걸음 문턱이 2 km/h에서 잘못 멈추지 않는지, 카운트다운 전 계기판 거리)
- 상준에게: D-07 오른쪽 그림·D-08 고리 움직임·I-01 계속 실패 문구, 백로그 BL-11 ④ "(C-01)" 번호 확인(문구는 러닝 설정, 번호는 코스 목록)
- 민재에게: 노션 작업 표 정리(내 정보·설정 API 두 줄 "완료", 카메라 노출 줄 → 민재), 홈 B-01–B-04 계획 0032 누가 할지(홈은 지환 S6)
- 보관 중: `git stash` "0036 러너 애니메이터 덮어쓰기 방지(fix/keep-runner-animator, PR 전)" — 씬 만들기가 `YBotRunner.controller`를 다시 재서 덮어쓰던 것(계획 0036). 브랜치 `fix/keep-runner-animator`에서 `git stash pop` 뒤 커밋·PR
- 섬 씬: 11/16 Build Island Scene으로 이 맥에 다시 만듦(전엔 메쉬 연결이 끊겨 빈 섬). 씬·조명 파일 변경은 커밋하지 않음
- 로컬 서버: `backend`에서 `.venv/bin/python -m uvicorn app.main:app --port 8000`(11/16 `alembic upgrade head`로 만 14세 약관 마이그레이션 받음)

## 지금 (11/12): S2 내 몫 끝
- **S2 네 개 모두 develop에 머지됨**: 러닝 설정·거치 안내 #35(계획 0026), 아바타 속도 맞춤 #39(0027), 내 정보·설정 API #47(0028, 11/12), 권한·이어폰 #34(0025). 계획 상태는 모두 "완료"
  - #47: `GET·PATCH /users/me`·`/users/me/settings`가 DB와 연결됨. 가입·수정 시간대는 IANA 이름만(`core/timezones.py`, `tzdata`). develop 합친 뒤 pytest 222/222
  - 민재에게 보낸 것(11/12): 가입 시간대 검사·`tzdata`(오라클 배포 때 `pip install -r requirements.txt` 다시), 노션 API 명세 4개 "진짜"로
  - 아바타(#39): 아이폰·섬 씬(봇·고스트) 눈 확인 남음. 클립 분석값(GaitAnalysis)과 실행 중 발 속도가 다른 원인 미확인. 다시 찍기는 Play 중 메뉴 Scrun/Dev/Capture Runner Side
  - 러닝 설정(#35): 고른 목표는 `MapScreen.Setup` → S3 카운트다운이 `POST /runs`에 씀(민재와 맞출 것). 그림 칸은 상준 그림 올 때까지 빈 칸
- 로컬에만 있고 커밋하지 않는 것: `.gitignore` 4줄, PolyHaven `.meta`(Unity가 다시 만듦), `docs/meeting/` 발표 자료, `ios/` 서명 설정, 계획 0009·0011 초안, Y Bot `Beta_*.asset`
- 로컬 변경 보관: `git stash` "11/11 맥 Mixamo 재임포트·씬 재저장"(Unity가 다시 만드는 meta·씬·라이팅. 버려도 되는지 확인 뒤 지움). Unity가 켜져 있으면 Y Bot·PolyHaven meta는 다시 생김 → 커밋에서 빼기
- Unity MCP: `unity/tools/ucs.sh`는 bash에서 옛 인텔 python3를 잡아 "Bad CPU type" → zsh의 python3로 JSON을 만들어 `umcp.sh`에 넘기면 됨. 버튼은 `NavigationSubmitEvent`, 화면은 `ScreenCapture.CaptureScreenshot`(UI 포함)

## 그전 (11/10 밤)
- **PR #30(코스 API + 보정값 API)·#32(코스 화면) 머지됨**(민재). 리뷰 반영 내용은 계획 0021·0022
- **PR #34(권한 안내·이어폰, 계획 [0025](../docs/plans/0025-permission-earphone-screens.md)) 머지됨**(11/11). 계획 번호를 0023 → 0025로 바꿈(민재 보정 계획과 겹침)
  - 민재에게 부탁할 것: `Navigator.Replace`(형이 넣어 주겠다고 함), 코스 상세 "맵 입장" 순서는 모션 확인(#34) → 보정(#33) → 입장으로 형이 #33에서 맞춤
- 노션 작업 표는 민재 워크스페이스라 내 노션 연결(jhwn223@cau.ac.kr, 개인 워크스페이스)로는 못 고침 → 민재가 워크스페이스에 초대 + claude.ai Notion 연결을 그 워크스페이스로 다시 하면 Claude가 상태를 바꿀 수 있음. 지금 바꿀 줄: [FE] 권한 안내·요청 화면, [FE] 에어팟 연결 확인 화면 → 진행 중(#34 머지 뒤 완료), [BE] 코스 API·보정값 API·[FE] 코스 화면·[3D] 섬 맵 입장 → 완료(#30·#32 머지)
- 이 맥 환경: Homebrew PostgreSQL 16, 로컬 서버는 develop의 `backend`에서 `/Users/jihwankim/Desktop/Projects/Scrun/.venv/bin/python -m uvicorn app.main:app --port 8000`(#30 머지됨). Unity MCP는 다시 켜면 `rm /tmp/umcp.sid` 뒤 연결. 긴 명령은 끊기니 짧게 나눠서. `unity/tools`는 develop의 맥 대응판을 씀(내 옛 수정은 버림)
- **develop에 커밋 금지**: `feat/...`·`docs/...` 브랜치를 따서 나눠 올린다

## 다음 할 일
1. 노션 작업 표(11/12 확인, 이제 내 Notion 연결로 읽힘): S2 여섯 줄 모두 완료. S1 [BE] 코스 API·[FE] 코스 목록·상세·맵 입장 화면도 11/12 완료로 바꿈(#30·#32 머지). 지환 S1 5줄·S2 7줄 모두 완료. 아이폰(PR #28 뒤): 권한 팝업·설정 앱 왕복 때 앱이 꺼지는지(꺼지면 입장하려던 코스·온보딩 단계 저장해 이어 가기, 계획 0025), 에어팟, 가로 맵·노치
2. **S3**(0035로 센서 준비·카운트다운 진행 중. 카메라 노출 제어는 민재가 가져감(계획 0033, `feat/camera-exposure`). 노션 작업 표 11/12 확인, 달림 인식·센서 준비는 11/13 시연 흐름): [FE] 센서 준비 확인(BL-10, D-07), [FE] 달림 인식·카운트다운(BL-11, D-09), [3D] 섬 맵 위 아바타 달리기·아이폰 성능(60fps·발열, BL-43), [FE·Swift] 카메라 노출 제어(BL-16). 센서 값과 가짜 버전은 민재 속도 플러그인, 화면 모양은 상준. 시작 전 계획 0029(#40 같이 뛰기·기본 HUD) 읽기

## 맡은 영역 (계획 0017, 노션 [개발 계획](https://tough-wednesday-fe0.notion.site/11-08-11-23-4114ae82f1de81a9bec2db16a9bfe635) 작업 표 지환 36줄)
기능 단위로 화면(UXML·동작)·서버 API(DB 연결·pytest)·연결까지 끝까지. 예외: 권한·에어팟·센서 준비·카운트다운은 화면만(밑의 Swift는 민재). 상준은 UI Kit·USS와 화면 모양 다듬기, PR 리뷰는 민재.
- 영역: 섬 맵·3D, 코스 선택, 러닝 설정, 달리기 준비 화면, 계기판 촬영·OCR·인증, 결과·기록, 고스트, 목표·홈·마이, 성장·보상(레벨·러닝복·임팩트), 새 맵·아바타(상준과)

| 스프린트 | 지환 |
|---|---|
| S1 11/08–09 | 코스 목록·상세 화면 + 코스 API (BL-7), Island01 입장 (BL-43), 권한 안내 (BL-2)·에어팟 화면 (BL-3) |
| S2 11/10–11 | 러닝 설정 목표 (BL-8), 거치 안내·매뉴얼 (BL-9), API: 보정값 `GET·PUT /users/me/calibration`(BL-4), 내 정보 `GET·PATCH /users/me`(닉네임은 `core/nickname.py` check, 충돌 409), 설정 `GET·PATCH /users/me/settings`(BL-36), Y Bot 애니메이션 속도 맞춤 3·6·10·15 km/h (BL-42, `FakeBeltKmh`) |
| S3 11/12–13 (11/13 시연) | 센서 준비 확인 (BL-10)·달림 인식·카운트다운 (BL-11), [FE·Swift] 카메라 노출 제어 ISO 최저·셔터 1/60초 이상·브래킷 (BL-16), 기존 맵 아이폰 fps·발열 품질 단계별 → `docs/unity/unity_map.md` |
| S4 11/14–15 | 앱 카메라 계기판 촬영 (BL-16), 서버 OCR (BL-17), 센서-계기판 대조·인증 API `POST /runs/{id}/verification/photo·reading·skip` (BL-18), 인증 결과 화면 |
| S5 11/16–17 | 운동 결과 (BL-19), 기록 목록·상세·그래프 (BL-20), 기록 조회 API. **11/16 범위 재조정**(헬스장 실험·맵 아트) |
| S6 11/18–19 | 주간 목표 (BL-30)·홈 (BL-31) 화면 + API, 에셋 목록·만들기/구매 결정(상준과, **구매는 11/20 17시 전 주문**) |
| S7 11/20–21 | 고스트 (BL-24, 기존 GhostSpeed 재사용) + API, 마이·설정 화면 (BL-36) |
| S8 11/22–23 | 인증 후 목표·경험치·레벨 반영 (BL-32), 성장·임팩트 API, 레벨별 러닝복 구조·배지 (BL-34) |
| 11/24 이후(제안) | 새 맵(3km 루프), 새 아바타·애니메이션, 새 맵으로 교체·아이폰 성능, 임팩트 해금 (BL-33, 35) |

- 11/13 시연에서 내 몫: 권한·에어팟 → 코스 → 러닝 설정·거치 안내 → 센서 준비·카운트다운. 결과·OCR·인증은 시연에 안 들어감
- 노션 불일치(11/08 확인): 기능 보드 "보정·달리기 준비"엔 BL-10·11이 아직 민재 S4. 작업 표(11/08 재분배)가 최신 → 민재에게 보드 고쳐 달라고 할 것
- 노션 읽기(Chrome 없이): `POST https://tough-wednesday-fe0.notion.site/api/v3/loadCachedPageChunk`(page `4114ae82-f1de-81a9-bec2-db16a9bfe635`), 작업 표는 `queryCollection`(collection `f05a969f-1b7b-4edd-a699-2ccfd1500cf8`, view `c46ab9e9-f45a-4ea7-bb8a-6cb9a3b250d3`, spaceId `4d714b86-f72b-41e0-abc8-9b674b4630ee`), 기능 보드 collection `ead40cea-b2aa-4fec-bcef-981b5d00bd1a`(view `0d421861-6e18-4f1d-b446-104189313849`)

## 커밋 대기·정리 남은 것
- 커밋 안 한 것(나눠서, 각자 브랜치): unity/tools Mac 대응 / githooks 실행 권한 / `.gitignore`(iOS·Mac) / iOS SpeedRecorder Xcode 프로젝트(`ios/project.yml`, `.xcodeproj`, `set_team.sh`, `Signing.xcconfig`, `Info.plist`) / 계획 문서 2개 / 이 handoff. `docs/meeting/` 중간발표 zip·폴더는 올릴지 확인
- **계획 번호 겹침**: 내 `0009-speedrecorder-xcodeproj.md`(원격 0009-speed-fusion), `0011-dashboard-photo-ocr.md`(원격 0011-airpods-events-flight) → 올리기 전에 마지막 번호 + 1로 바꾸기
- `git stash` stash@{0}: CLAUDE.md Mac 명령 + ios/README Xcode 사용법(10/29 보관) → develop CLAUDE.md와 합쳐서 올릴지 판단
- 남음(환경): Xcode `xcode-select`(직접), Mixamo Y Bot 받기, Unity로 `unity/TestRun` 열어 Reimport Mixamo Characters → Build Island Scene, MCP for Unity 확인, Unity iOS Build Support

## 지난 작업 (10/29, 요약)
- Mac 환경: hooksPath, `.venv`(Python 3.11), Poly Haven, Unity 6000.6.7f1 + Android 모듈, ffmpeg. `unity/tools/*.sh` Mac·Windows 공용화
- SpeedRecorder Xcode 프로젝트(XcodeGen) → iPhone 15 설치(고상준 Personal Team, 7일마다 재설치). 계획 0008 첫 시험 `data/runs/20261029-004604` → `docs/speed/airpods_flight.md`. 헬스장 14구간 `data/runs/20261029-025507` → `speedlab/exp/airpods_treadmill.py`(제자리 방법 안 통함). Recorder.flush() 재전송 때 폰 백업 중복 저장 버그(민재 코드, 안 고침)
- 계획 0010 시험 `data/runs/20261029-040156` → `docs/speed/live_iphone.md`(달리기 에어팟 포함 0.16, 폰만 0.12 km/h, 따라오는 데 5에서 10초)
- 계기판 사진 OCR(내 계획 0011, S4 BL-17로 이어짐): YOLO11n + 공개 데이터(Roboflow 7segmentrecognition_v3) + 합성 4,000장 + 형식 규칙으로 세 기종 맞힘. **코드·모델(`synth7seg.py`, `postproc.py`, `eval_*.py`, `best.pt`)은 그때 Claude 임시 폴더에 있었고 11/08 이 맥의 `/private/tmp/claude-501`에서 못 찾음** → S4 전에 다른 곳에 남았는지 확인, 없으면 계획 0011 표대로 다시 만들기. 서버 엔진은 `docs/product/console_ocr.md` 기준으로 고름. Roboflow 키 `~/.roboflow_key`(저장소 밖). LEXCO·DK City 사진은 출처 불명이라 안 올림
