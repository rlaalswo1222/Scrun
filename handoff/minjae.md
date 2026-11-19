# 민재 인수인계 (2026-11-19 밤, Windows)

민재(git `minjae`)의 개인 작업 메모. 새 채팅은 `CLAUDE.md`(규칙·문서 지도)와 이 파일을 먼저 읽는다. 팀원은 읽기만 하고 고치지 않는다. 끝난 결론은 영역 문서(`docs/speed/README.md`, `docs/unity/unity_map.md`, `docs/api/`, `docs/adr/`, `docs/plans/`)에, Claude 작업 요령(Figma·에디터·노션·HTML)은 `docs/claude_howto.md`에 있다. 옛 전체본: `docs/log/HANDOFF_minjae_20261119.md`(11/19 밤 정리 전: 그날 PR별 자세한 기록·지난 팀원 전달 전부), `_20261118.md`(11/18 정리 전, 끝난 머지·결정의 자세한 기록), `_20261## 지금 (11/19 밤, 세션 초기화 전)
- **develop 0bf7099(#113까지), 오라클 e75d48c(#112까지 배포, 그 뒤는 문서만)**, 마이그레이션 없음, DB `d5b8e3f2a6c1`. 열린 PR 없음
- **11/19 한 것**(자세한 건 각 계획 "진행"·`docs/log/HANDOFF_minjae_20261119.md`):
  - 지환 #94·#95 리뷰·머지, 세션 복구 #97(0052), OCR 시간 초과 #98·#99(0055), 공통 상태 #102·#103(0056), #66 카메라 Swift(0033, 맥 확인 끝)
  - **홈 BL-31을 지환에게서 가져와 #106(0057)**: `GET /home` DB 연결 + B-01–B-04, I-04 가운데 안내창·E-01 Figma대로. 아이폰 확인 끝(노션 홈 화면·BL-37 완료)
  - **계정 삭제 BL-48 #108(0058)**: 어드민 키로 카카오 연결 끊기 → 전부 삭제, 카카오 연결 해제 웹훅(Claude가 콘솔 > 앱 > 웹훅에 등록), 앱 H-10 확인 시트. 어드민 키는 민재가 오라클 `.env`에 넣음(들어간 것 확인)
  - **러너 레벨 배지 BL-22 #112(0059)**: 실시간 `level` → 카드·이름표 Lv 칩
  - **결정(CLAUDE.md "이미 정한 방향")**: 목표는 12/11 발표, 스토어 출시는 종강(12/21) 뒤, 에셋은 사지 않음
- **오라클 임시 설정**: `.env`에 `VERIFY_OCR_TIMEOUT_S=60`(compose 기본 15) → 실물 계기판 사진으로 잰 뒤 정함(0055). OCR 로그 `journalctl CONTAINER_NAME=scrun-api-1 --since "1 hour ago" | grep "계기판 OCR"`
- **로컬 개발 상태**: 이 세션에서 켠 로컬 서버(8001·8002·8003)는 세션이 끝나면 꺼짐(다시: `backend`에서 `../.venv/Scripts/python -m uvicorn app.main:app --port 8001`). 에디터 주소 EditorPrefs = 8001, 열린 씬 App, 게임 뷰 크기 목록에 1179×2556·2556×1179 추가. 에디터 로그인은 가짜 카카오 `k_520873140`(에디터 계정)으로 다시 됨. 개발 DB에 시험 계정: `k_905`(가짜905, 경험치 12 km = Lv.3, 레벨 배지 확인용), 지운 시험 계정 `k_770058`(이제 새 가입). Chrome은 Windows·맥 둘 다 연결 → Figma·카카오 콘솔은 Windows(로그인됨)
- **에디터에서 다른 러너 아바타가 안 보임**(섬 Y Bot 메시 오류 `Alpha_Joints` "does not match the expected mesh data size"): 그래서 `RunLink.Heads`가 비어 이름표도 안 뜸 → 아이폰에서도 그런지 같이 달리기 확인 때 보기
- **로컬에만(커밋 안 함)**: `Lm_LampGlass.mat`(`_EMISSION` 빠짐, 되돌릴지 결정), `.claude/settings.json`, 지환 실명 파일 2개(`data/runs/20261029-004604/note.md`·`docs/speed/airpods_flight.md`, 동의 전엔 커밋 안 함), 계획 0032 홈 초안(0057이 대신함 → 지워도 됨)·로컬 브랜치 `feat/home-figma`, 미팅 자료·`midterm_feedback.md`·중간발표 폴더(**git에 안 올림 = 민재 결정**), 연구 문서 2개, 와이어프레임 초안. 브랜치 `fix/dev-recalibrate`(PR 안 엶) → 올릴지 결정

## 다음 (새 세션은 여기부터)
1. **발표·보고서(11/27 교수 + 4차 보고서, 12/11 발표)**: 교수님 피드백 6개(정확도 공개·연속 비교·정답값·교차 조건·사용성 테스트로 가설 검증·범위 조정, 아바타·디자인 누락). 보고서엔 실측만 → **사람별·기종별 실측 표**: 지환 러닝머신 기록·세 번째 기종 원본을 `data/`에, `docs/speed/README.md` 실측 줄. 헬스장 실험 다시(아래 "헬스장"). 11/14 미팅 결정 → ADR(FTMS 지원 기구면 기구 속도 먼저 = ADR-0006 고치는 새 ADR, 워치 팔 스윙 보폭 = 실험 먼저). 헬스장 11/16 질문 2개 → 0034 완료(① 멈추기 직전 2.5–4 km/h가 러닝머신을 걷기로 낮춘 것인지 ② 멈춘 뒤 0.1–0.8초 재출발 4번이 일부러인지). 속도 추정 알고리즘 손보기는 앱 완성 뒤(11/17)
2. **11/23 기능 완성까지 개발 후보**: 설정 "준비 중" 스위치(주간 목표 리마인더 BL-46 = Swift 로컬 알림·맥 확인, 친구가 달릴 때 알림, 공동 맵 인증 마크 = 서버 칸·모양 필요), 달리기 단계 칩 BL-12(단계 속도 기준 먼저), **섬 아바타 Y Bot → 메인 캐릭터**(에셋 안 삼 → 3D를 어떻게 만들지부터, 교수님 "아바타 누락"), 마이 "준비 중"(인증 마크 H-06), 내 머리 위 "나 Lv.2" 이름표(Figma D-12, 상준과)
3. **아이폰 확인 모아서(맥 + 아이폰)**: 계정 삭제 2가지(앱에서 지우기 → 카카오 "연결된 서비스"에서 SCRUN 사라짐 / 카카오 쪽에서 끊기 → 오라클 로그 "계정 삭제"), 같이 달리기 2대(시작 순간 친구 바로 앞, 이름표 위치·Lv 칩, 와이파이·LTE), 측정 예외(에어팟 빼기·폰 들기·발열), 공통 상태(비행기 모드 → I-07 → 저절로 다시), 고스트·운동 결과·친구(노션 "[공통] 아이폰 확인" ②–⑫), 실물 계기판 OCR 시간(0055 표, 18:05 맥 시험 줄도 아직), 한글 입력·권한 거부→허용·스플래시(오래 남은 것, `unity_map.md` "아이폰에 설치"), iOS 최소 버전 15 → 17로 올릴지(가설: Xcode 27 빌드가 16에서 꺼짐)
4. **헬스장(지환)**: 0024 6번, 걷기 절편, 걸음 없음 문턱 0.12, 에어팟 끼고 측정 예외 기록, 블루투스 FTMS 지원 여부(nRF Connect), 워치 기록, 계기판 브래킷 사진. 0023 "데이터가 없는 곳"(1–15 km/h 1 간격 여러 명, 단계 20초 이상, 2 km/h 걸음 감지율). 실험 전 메모(사람·키·몸무게·신발·기종·속도 순서·폰 위치) 빠뜨리지 않기
5. **종강 뒤 출시 전(지금은 안 급함)**: 서버 로그인 시도 제한(429)·사람별 OCR 요청 제한(BL-17)·서버 밖 백업·fail2ban·운영 분리·도메인(BL-44·51), Apple Developer·TestFlight(BL-49·50), 기존 계정 만 14세 동의 다시
- 남은 작은 것: A-17 컨페티·스플래시 "아바타가 달려 들어옴"은 그림 생기면, 이름표 쌓기는 코드로만 확인(0041), 에디터가 섬을 불러오는 동안 실시간 연결이 한 번 끊겼다 붙음, `App.uss`의 `:last-child`를 클래스로, 개발 DB의 ACTIVE 고스트 시험 달리기 `01M7PRTB5P0X2VCYW1YPSC6RNY`

## 팀원에게 전할 것 (보냈으면 지우기, 예전 것은 `docs/log/HANDOFF_minjae_20261119.md`)
- **지환**
  - **홈 BL-31은 11/19 민재가 가져가 끝냄(#106)**. H-07 목표 시트를 `WeeklyGoalSheet`로 빼서 홈 B-02와 같이 씀(`WeeklyGoalStatusScreen` 동작 같음), `courses`·`friends`·`runs` 라우터에서 `course_rows`·`running_rows`·`recent_rows`를 함수로 뺌 → **pull 먼저**
  - 11/19 #94·#95: 민재가 리뷰 반영을 두 브랜치에 직접 커밋해 머지·배포. 기록 탭 셋째 칸 "N/목표"는 민재가 함(목표가 있으면 세 칸 모두 주간 목표 주기). `handoff/jhwn223.md` "지금" 절 합치기는 지환
  - **OCR 정확도는 지환 몫**(실사진 3장 모두 "못 읽음", 원인 모름, 같은 값 두 장이면 멈춤 규칙 #99) — 11/19 채팅에서 준 메시지 보냈는지 확인
  - **에셋은 사지 않음(11/19 민재 결정)** → 새 맵·아바타·러닝복은 팀 제작·무료 에셋. 스토어 출시는 종강 뒤
  - 계정 삭제(#108)로 `stub_data`의 안 쓰는 예시가 지워졌고 `DELETE /users/me`가 진짜. 실명 파일 2개 동의(없으면 "팀원 J"), 옛 브랜치 `feat/airpods-flight`·`feat/airpods-flight-analysis` 지워도 되는지
- **상준**
  - **Figma에 없어 임시로 쓴 문구 확인**: 홈 `HomeText`(달리는 친구 없음 "지금 달리는 친구가 없어요"·친구 추가, "2번 더 달리면 달성!", 코스 "0명 달리는 중", 홈을 못 받았을 때 "달리기 시작하기"), I-04 버리기 확인 창(Figma엔 없음, 되돌릴 수 없어 넣음), E-01 인증할 수 없을 때 안내 `RecoveryText.LateLead`, 계정 삭제 시트 `MyText.Delete*`, 로그인 화면 "계정을 삭제했어요."가 빨간 글자(오류 색)
  - 홈 위 카드 Figma 그라데이션 → 단색, 러닝복 이름 Figma B "기능성 러닝 세트" vs 마이 "기능성 세트" 하나로, I-04 덮개가 아래 탭 바는 안 덮음, 레벨 칩 색(카드 연초록·이름표 라임)
  - 개인정보처리방침(BL-40)에 "탈퇴하면 바로 모두 삭제(카카오 연결도 끊김)", 인증 마크가 공동 맵 어디에 보일지(설정 H-10 스위치가 잠긴 채)
  - 그 전 것(운동 결과 `RunResultText`, 친구·같이 달리기 임시 문구, S6 HUD 모양 등)은 `docs/log/HANDOFF_minjae_20261119.md` "상준"
- **둘 다**: 아이폰 유무(시연 2대), 각자 iOS 빌드 환경
- **팀 공유(민재가 직접)**: 노션 개발 계획 공유(워크스페이스 멤버가 민재뿐 → 초대나 공유 링크), 바뀐 역할. 공유되면 CLAUDE.md "역할"과 ADR-0007의 "팀 공유 필요"를 지움

크), 바뀐 역할. 공유되면 CLAUDE.md "역할"과 ADR-0007의 "팀 공유 필요"를 지움

## 민재가 정할 것
- 러닝복(BL-34)을 S8에 둘지 11/24 뒤로, BL-32를 S8에, 새 맵·아바타 일정(**11/19 결정: 에셋은 안 삼** → 팀 제작·무료 에셋). Apple Developer·TestFlight·심사는 **종강(12/21) 뒤 출시라 그때**(11/19 결정, CLAUDE.md), 노션 기능 보드를 작업 표에 맞출지(작업 표·API 명세 표에 제목 빈 줄 하나씩, 안 지움)
- 원격 옛 브랜치 지우기(PR 화면 버튼): `feat/dev-server`, `feat/airpods-treadmill`, `docs/handoff-1106`, 오늘 머지한 `docs/deploy-1112b`·`docs/handoff-1118`·`docs/handoff-1118b`·`feat/friends`

## 일정
- 목표 **캡디 최종 발표 12/11**(발표 + 최종 보고서 60%, 평가 "완성도"·"Proposal 대비 진척", 출처 노션 SCRUN > 문서 > 캡스톤 디자인 공지). 11/20 전문가, 11/27 교수 + 4차 보고서, 12/18 최종 보고서 + 사용자 매뉴얼
- 11/23 기능 완성 → 11/24–12/10 안정화(3순위·알림·정책·출시 준비·새 맵 제작·정확도 검증·사용자 테스트) → 12/11 발표. 스프린트·담당은 계획 0017과 노션 [개발 계획](https://app.notion.com/p/4114ae82f1de81a9bec2db16a9bfe635)
- 위험: 헬스장 실험(지환), 실시간 공동 맵 실기기. 에셋은 사지 않기로(11/19), 스토어 출시는 12/21 종강 뒤(11/19)

## 환경 메모 (영역 문서에 없는 것만)
- **오라클**(주소·배포는 `backend/README.md`, 계획 0019, ADR-0008): SSH 키 `~/.ssh/oracle_key`는 Windows 노트북에만. 배포 `ssh -i ~/.ssh/oracle_key ubuntu@161.118.211.155 '~/scrun/backend/deploy/update.sh develop'`(마이그레이션도 같이). 남은 것 0019(429·서버 밖 백업·진짜 도메인 BL-51·유휴 회수). BL-44는 "진행 중"(운영 분리는 BL-51). 공통 규칙 "미정"에 "OCR 서버/앱 미정"이 남음(11/06 서버 처리로 확정) → OCR API 때 고침
- **Windows 노트북**: 로컬 서버 8001(EditorPrefs `Scrun.ApiBaseUrl`, 기본값 8000은 옛 `server.py` 속도 실험이라 켜져 있을 수 있음), 세션 백그라운드로 띄우면 세션이 끝날 때 꺼짐. `backend/.env`(`KAKAO_MODE=fake`), 개발 DB는 `alembic upgrade head`. 이 PC `PlayerPrefs`에 소개 봄(`scrun.intro.seen`)·온보딩 끝남이 남아 있어 처음 실행 흐름을 보려면 지우기
- **맥**: `uv`·Python 3.12·`gh`는 `~/.local/bin`(PATH 없음, `~/.local/bin/uv python update-shell`). Homebrew 없음. 시뮬레이터 키보드는 Mac 입력기를 영어로. Claude in Chrome: VS Code에 전체 디스크 접근 → 확장 안 CLI(`~/.vscode/extensions/anthropic.claude-code-*/resources/native-binary/claude --chrome`) 한 번 실행 → `/chrome`에서 Enabled by default → 새 대화. 일 넘기기: 계획 문서 맨 위 "맥 할 일"에 적고 푸시 → 맥에 "NNNN 맥 할 일 해 줘" 한 줄. Y Bot FBX는 맥에 있음(Windows 바탕화면 `ybot_fbx.zip`은 지워도 됨)
- **맥에서 커밋하지 말 것**: Mixamo 파일이 없어 Unity가 지운 `Characters/YBot` `.meta` 7개(`git checkout -- unity/TestRun/Assets/Scrun/Characters/YBot`), iOS 전환 때 Poly Haven `.meta`의 iOS 칸·`materialImportMode`, URP 전역 설정·ProjectSettings(시뮬레이터 SDK 값). 그 밖은 `unity_map.md` 171–172줄. 아이폰 빌드에서 Island01은 Mixamo가 없어 캐릭터가 빠짐
- `unity/Builds/device-console.log`(git 밖)에 카카오 토큰이 찍혀 있음 → 공유 금지
- **카카오 개발자 앱**: 민재 계정 "SCRUN"(ID 1641065), 네이티브 앱 키에 iOS `com.scrun.app`(Android `com.scrun.dev`), 동의 항목 기본(회원번호만). 계정 삭제(BL-48) 때 연결 해제 웹훅
- API 명세(0014) 남은 것: Figma 화면 ID 확인(약관 A-06·오류 I-08 추정, H-05·H-06 미확인), 오류 문구 칸은 상준, 백로그에 지표 이벤트(`/v1/events`) 추가 제안, BL-39는 민재 확인 후 공개 백로그에서 완료로
- Git: `develop`·`main` 보호(PR로만, 승인 0명). 재료비 2차(11월 공지 대기): Claude Max 3개(상준 추가, 11/19–12/18), 나머지 약 23만원 → 캐릭터·환경 에셋 먼저, AI 3D 1개월. 조교에게: 에셋 "소프트웨어 일회성 구매" 인정 여부 + 서버 예비 항목

## 속도 추정 다음 실험 (결론은 `docs/speed/README.md`)
- 다음 헬스장: 저장된 보정으로 보정 없이 달려 다른 날 오차, 10에서 11 km/h 1–2분 → 7로 내려 1분(S22에서 틀렸던 상황), 범위 밖(11 이상, 6 이하). 에어팟 공중 시간(계획 0012)·흔들림 폭을 **바꾸지 않고** 비교한 뒤 앱 반영. 기준은 "걸음 수가 같은데 속도가 다를 때 가르는지"
- 그 전에 맥에서 SpeedRecorder 빌드 + `data/runs/20261029-040156/calibration.json`을 앱 Documents에(계획 0010 5번). 0024 결정 1이면 SpeedRecorder가 플러그인 `SpeedEstimator.swift`를 참조
- 에어팟 끊김은 5–20초 덩어리(헬스장 혼잡·몸 가림 가설) → 끊기면 폰만으로. 헬스장에선 아이폰 핫스팟. Nijs 2021 기준값이 에어팟에도 맞는지는 가설(발표·보고서엔 "공개 데이터 재분석")
- 계기판 OCR(`docs/product/console_ocr.md`): S22 밝기 낮춘 사진(다른 숫자 화면 포함) 비교, 앱 카메라 ISO 최저·셔터 1/60초 이상·브래킷. 사진은 유리에 사람이 비쳐 저장소에 안 올림
- 미기록: B(고상준) 키·몸무게·신발, 민재 키·몸무게

## 그 밖에 기다리는 것·보류
- 노션 SCRUN > 문서 > 기획 문서 열린 질문 중 개발이 답할 것: Q-07·08·09(11/10: 2–14 km/h 7단계 × 15초)·10(재보정 기준 미정)·11(5–10초)·12·13(ADR-0001·0007)·29(카카오). 앱 기획 미정은 `docs/product/scrun_usecases_v1.md` 끝
- 중간발표 자료(10/30) `docs/meeting/midterm/`: README·그림 `img/`(`figs.py`·`render.sh`)·대본 `대본_13장부터.md`·`질문대비.md` 52개. 10/12 블라인드는 같은 날 13분 뒤 → 예전 보고서 표현 확인. 교수님 미팅자료 최종본 `미팅자료_최종본.html`은 못 찾음(고칠 땐 `docs/meeting/prof_body.html` → `python build_prof.py .`)
- 유사 서비스 비교(`docs/research/competitor_comparison.md`) 적용 후보 4개: `ISpeedSource` 추상화, 끊김 타임아웃(ADR-0006 후보), 코스 = 스플라인, 고스트 파일. 결론: 세 앱 모두 속도를 기구·GPS에서 받음 → 폰 + 이어폰 추정이 차별점(보고서용). REA Windows Ghidra는 arm64 .so를 거부
- 실사 영상 가림 처리 등(메모리 sfm-followups), 이슈 보드·반복 작업 스킬·SoundTrackDB 분석. 작업 요령: walking skeleton(끝에서 끝까지 얇게 먼저), 화면 흐름 `scrun_userflow_v2.html`은 로컬 HTML로
