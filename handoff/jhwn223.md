# jhwn223 인수인계 (2026-11-10 밤)

jhwn223(git `jhwn223`, 지환)의 개인 작업 메모. 새 채팅은 `CLAUDE.md`와 이 파일을 먼저 읽는다. 팀원은 읽기만.

## 지금 (11/10 밤)
- **코스 화면 끝, PR 준비**: 브랜치 `feat/course-screens`, 계획 [0022](../docs/plans/0022-course-screens.md). Figma C-01·C-02·I-10·I-11·D-10 + 아래 탭 바를 Figma대로, `/code-review` 두 번 반영, EditMode 58/58, 에디터 Play로 코스 4개 입장·재입장·연결 끊김·없는 코스 확인. 무엇이 바뀌었는지는 계획 0022 "진행 상황", `docs/unity/unity_map.md` "코스 화면·맵 입장"
  - Figma: "SCRUN"(키 `w5YpqhN1yyWXf03z23eTKK`) > 페이지 "스크런 와이어 프레임"(585:18179). claude.ai Figma 연결로 읽음(`get_design_context`)
  - **PR #30(코스 API, `feat/course-api`)이 먼저 머지돼야 함**(민재 리뷰 대기). 이 handoff 파일은 두 브랜치에 다 있음 → 합칠 때 이 브랜치 쪽(나중 내용)을 고른다
  - 민재에게 알릴 것: 흐름 변경(입장은 가로 맵 화면이 I-10 아래에서), App.uss UI Kit 값(로그인·약관·프로필 색·제목 28), 아래 탭 바 Figma대로 바꿈(앱 틀), App.uss `:last-child` 두 곳이 USS에서 안 먹음, 나가도 서버 자리 10분 남음(러닝 종료 API BL-14에서 `course_entries` 지우기)
- 코스 API(계획 0021) 상세는 PR #30. 남은 것: 러닝 종료 API(민재)에서 `course_entries` 지우기 제안, 노션 API 명세 다시 생성
- 이 맥 환경: Homebrew PostgreSQL 16(`brew services`), `backend/.env`(KAKAO_MODE=fake). 로컬 서버는 `feat/course-api` 작업 폴더(다른 세션 scratchpad의 `course-api` worktree)의 `backend`에서 `../.venv` 대신 `/Users/jihwankim/Desktop/Projects/Scrun/.venv/bin/python -m uvicorn app.main:app --port 8000`(PR #30이 develop에 들어가면 저장소 `backend`에서)
- Unity(맥): MCP for Unity 켜고 `unity/tools/*.sh` 앞에 `PATH=/usr/bin:/bin:/opt/homebrew/bin`(옛 인텔 파이썬이 "Bad CPU type"). Game 창은 세로 고정이라 가로는 Game 창 크기를 852×393 비율로. Play 중 UXML·USS를 고치면 Live Reload로 홈으로. MCP로 버튼을 누를 때 Game 창에 포커스를 주지 말 것(앞서 누른 버튼이 다시 눌림)
- **develop에 커밋 금지**: `feat/...`·`docs/...` 브랜치를 따서 나눠 올린다

## 다음 할 일
1. 코스 화면: develop 받아 합치기 → 커밋·PR(사용자 요청 시) → 민재 리뷰. 아이폰에서 가로 배치·노치 여백, 상준에게 Figma와 나란히 확인 요청
2. **[FE] 권한 안내·에어팟 화면(S1 남은 것)**: Figma A-07(권한 안내)·A-09·A-10(이어폰 연결), `FakeNative`(develop에 있음)로. 계획 문서부터(develop pull 뒤 마지막 번호 + 1)
3. S2: 러닝 설정·거치 안내(D-01·D-05, 상세 → 맵 사이), 내 정보·설정·보정값 API, Y Bot 애니메이션 속도
4. S3: 센서 준비·카운트다운(D-07·D-09), 맵 HUD(D-10 진행 막대·멈춤 D-17), MapScreen 쪼개기

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
