# 민재 인수인계 (2026-11-12 저녁, Windows)

민재(git `minjae`)의 개인 작업 메모. 새 채팅은 `CLAUDE.md`(규칙·문서 지도)와 이 파일을 먼저 읽는다. 팀원은 읽기만 하고 고치지 않는다. 끝난 결론은 영역 문서(`docs/speed/README.md`, `docs/unity/unity_map.md`, `docs/api/README.md`·`realtime.md`, `docs/adr/`, `docs/plans/`)에 있다. 옛 전체본: `docs/log/HANDOFF_minjae_20261110.md`(11/10 정리 전), `_20261109.md`, `_20261107.md`, `_20261106.md`

## 지금 (11/12 밤, develop·오라클 서버 둘 다 e14a16b)
- **11/11–12 develop에 합친 것**: #39 아바타 발 속도(지환 0027), #40 같이 뛰기 기본(계획 0029: 진짜 속도 → 아바타, 실시간 방 `backend/app/realtime/`, 기본 HUD, "지금 N명" = 실시간 인원), #41·#43 문서, #42 에디터 흐름에서 나온 것(에디터 서버 `127.0.0.1`, `leave`면 자리 지움, 목록·상세는 나 빼고, 앱에선 섬 시험용 고스트·녹화기 끔), #44 스플래시(계획 0030, A-01), #45 온보딩 A-02–A-19(계획 0031), #47 지환 내 정보·설정 API DB 연결(계획 0028, BL-36: `GET`·`PATCH /users/me`·`/users/me/settings`, 가입 `timezone`은 IANA 이름만, pytest 222 통과 확인 후 머지). 열린 PR: **#46**(이 인수인계, "머지해"면 머지)
- **앱 흐름(develop)**: 스플래시 → (처음 켠 기기) 소개 A-02–A-04 → 로그인 A-05(카카오 취소·오류면 A-19) → 새 회원: 약관 A-18 → 프로필 A-06 → 권한·이어폰 → 주간 목표 A-16 → 완료 A-17 → 홈 → 코스 → (보정 없으면 보정) → 맵 입장 → 러닝 설정·거치 안내 → "시작할게요" → HUD·같이 뛰기
- **오라클 서버**: 11/12 밤 e14a16b 배포(#47, 마이그레이션 없음, 컨테이너 안 시간대 목록 598개·`Asia/Seoul` 있음 확인. DB는 `7a1d4e2b9c30` 만 14세 약관까지). 서버는 Docker Compose 3개(api 직접 빌드·db Postgres 16·caddy HTTPS), `update.sh`가 api 이미지를 다시 빌드하므로 requirements는 따로 설치 안 해도 됨. 배포 명령 `ssh -i ~/.ssh/oracle_key ubuntu@161.118.211.155 '~/scrun/backend/deploy/update.sh develop'`. Claude 배포·남의 PR 머지는 민재가 "배포해"·"머지해"처럼 직접 말해야 권한 검사 통과("ㄱㄱ"는 막힘). 11/11 개인 프로젝트 `system-server`는 걷어 냄
- **메인 캐릭터** = 팀이 직접 만든 흰 모자 캐릭터(11/12, CLAUDE.md "이미 정한 방향"). 섬 아바타(Y Bot 임시)를 바꿀지는 지환·상준과 따로
- **노션 개발 계획 11/12 최신화**: 실시간 방 서버 완료, 다른 러너·속도 → 아바타·HUD 진행 중(아이폰 확인 남음), 페이스 봇 S4, 지환 BL-42 완료, 지환 설정·내 정보 API 진행 중(계획 0028, 저장소엔 아직 없음), 운영 서버 메모. 기능 보드 "함께 달리기" 진행 중. 스플래시·온보딩 줄은 아직 안 넣음(로그인·온보딩 줄 메모에 덧붙이기)
- 맥에 일 넘기기: 계획 문서 맨 위 "맥 할 일"에 적고 푸시 → 맥에 "NNNN 맥 할 일 해 줘" 한 줄(맥은 이 파일 안 고침, 결과는 계획 "진행"의 맥 줄)
- PR 합치기: 남의 PR·리뷰 없는 문서 PR은 자동 권한 검사가 막음 → 민재가 "ㄱ"으로 허락

## 다음 (교수 미팅·시연 11/14 토로 미뤄짐, 11/12 밤 공지)
- **홈 B-01–B-04 Figma대로(계획 0032, 작성·대기)**: 11/12 Figma B 섹션(601:317) 읽고 계획만 씀. 파일은 이 PC에만(커밋 안 함, 로컬 브랜치 `feat/home-figma`). 정할 것 5개(누가 — 홈은 지환 담당, `/home` 예시 응답을 "없음"으로 바꿀지, 달리는 친구 없을 때, 코스 그림, 시연 뒤 머지) 민재 답 대기
1. **시연 구성 정하기(가장 급함)**: 아이폰 빌드는 오라클(진짜 카카오), 에디터는 로컬(가짜 카카오)이라 그대로면 같은 방에 못 붙음(가설). 아이폰 2대가 오라클로 / 노트북 서버를 진짜 카카오로 + 아이폰 주소 바꾸기 / 에디터도 오라클(진짜 카카오) 중 고르기. 11/12 채팅에서 제안만 함
2. **아이폰 확인(맥 + 아이폰)**: 실시간 `ClientWebSocket`이 IL2CPP에서 붙는지(가설, 안 되면 NativeWebSocket) → 책상에서 붙는지만 + 책상 두드려 아바타. 손에 들고 걷기는 안 함(속도 추정은 계기판 충격 + 에어팟 공중 시간이라 의미 없음, 11/12) — 계획 0029 확인 기준 문구 고치기 제안함(미반영). 같이: 스플래시 실행 화면(검은 번쩍임 없는지), 온보딩(손가락 넘기기·카카오 취소 → A-19·키보드가 프로필 칸 가림), 왕복 지연 와이파이·LTE 1분씩(`realtime.md` "지연 실측"·ADR-0010)
3. **러닝머신 시연 리허설**(민재 직접): 시연 전 에디터 로컬 계정 닉네임(테스트 욕설) 바꾸거나 API 서버 창 "저장된 로그인 지우기"
4. 계획 0029 확인 기준 중 안 한 것: 서버를 껐다 켜면 다시 붙음(에디터 + 로컬 서버로 Claude가 할 수 있음)
5. 계획 0024 6번 러닝머신 확인(헬스장 날짜) + 미룬 책상 확인 2개(권한 거부 뒤 가속도, 10분 발열). 헬스장에서: 걷기 절편 "2 포함 대 4 이상"·키로 조정 비교, 걸음 없음 문턱 0.12(S22 값) 아이폰으로 다시
- 남은 작은 것: 기존 계정의 만 14세 동의 기록 없음(출시 전 다시 동의 받기 흐름), A-17 "[모션] 환호 + 컨페티"·스플래시 "아바타가 달려 들어옴"은 에셋 생기면, 서비스 소개 그림은 Figma 그림
- 시연 흐름(에디터)은 민재가 직접 돌려 봄(Claude가 제안하지 않음)
- **S3 메모(11/11 지환)**: 카운트다운·달리기 목표는 `MapScreen.Setup.Goal`, `POST /runs` goal에 `Setup.Goal.ToRequest()`(m·초). 섬 `MapLaunchApplier`가 입장 위치 적용 뒤 `MapLaunch.MarkApplied()`(고칠 때 이 줄 남기기), `RunnerLabels`는 카메라가 꺼져 있으면 안 그림
- 확인 못 한 것: 처음 Play 때 안내 → 보정 화면이 버튼을 안 눌렀는데 넘어간 적 한 번(재현 안 됨, Submit 입력 의심 가설)
- 헬스장 실험에 넣을 것: 0023 "데이터가 없는 곳"(1–15 km/h 1 간격 여러 명, 단계 20초 이상, 2 km/h 걸음 감지율, "속도 안 올림" 기준)과 아래 "속도 추정 다음 실험"

## Claude 작업 요령 (이번 세션에서 배운 것)
- **Figma 읽기**: 이 PC Chrome(Claude 확장 연결, Figma 로그인됨)에서 파일을 열고 `javascript_tool`로 `figma` 전역(플러그인 API). 맥 Chrome도 연결돼 있어 `list_connected_browsers`로 Windows(`Browser 1`)를 `select_browser`. 출력에 `@`·`=`이 많으면 "Cookie/query string data"로 막힐 수 있음(형식 바꾸기), 출력은 1500자쯤에서 잘림(프레임 하나씩)
- **Figma 그림 받기**: `exportAsync({format:'PNG', constraint:{type:'SCALE', value:3}})` → blob 다운로드. Chrome은 같은 탭의 두 번째 자동 다운로드부터 막음 → **새 탭에서 여러 장을 한 파일로 묶어 한 번에**(길이 머리 + 이름 + 바이트) 받고 Python으로 나눔
- **에디터 UI 누르기**: `NavigationSubmitEvent`를 `panel.visualTree.SendEvent`로(`e.target = 버튼`). 화면을 직접 띄우려면 `Scrun.App.AppRoot.Nav.StartFlow(new XxxScreen())` 뒤 `manage_camera` screenshot(파일은 `Assets/Screenshots`, 끝나면 지우기)
- **USS 주의**: 화면 UXML의 USS 규칙은 그 화면 안쪽에만 닿음(app-root 배경은 `App.uss`에). 페이드인 클래스는 첫 `GeometryChangedEvent` 뒤에 붙여야 transition이 돎
- **프로젝트 설정 저장**: `ProjectSettings.asset`은 `SaveAssetIfDirty`로 안 써짐 → `AssetDatabase.SaveAssets()`(다른 에셋도 써질 수 있으니 git 확인)
- **한글 컴퓨터 이름**: Mono가 `localhost`를 못 풀어 ClientWebSocket 실패 → 에디터 서버 주소는 `127.0.0.1`(코드 기본값·저장값 자동 변환)

## 팀원에게 전할 것
- **11/12 보낼 것(초안은 11/12 채팅)**: 지환 — A-16 주간 목표가 `PUT weekly-goal`을 부르는데 서버가 예시 응답이라 저장 안 남음(BL-30 DB 연결), 가입 필수 약관에 `age14` 추가(테스트 `agreedIds`), 계획 0028(가입 timezone IANA 검사·tzdata) (가)로 OK 답장, 카운트다운 끝에서 `MapScreen.StartRunning()`(지금은 `OnSetupDone`이 임시로 바로 부름). 상준 — 프로필 A-06 안내 문구 확정(Figma 문구가 사실과 달라 임시 "키와 몸무게는 앞으로 속도 추정에 쓸 예정이에요"), 약관 화면에 Figma에 없는 선택 "운동 알림 받기"가 서버 목록에 있음, HUD·간격 카드·토스트 모양(D-10·D-11)
- **상준**: Figma 보정 화면 고칠 곳은 0023 "Figma와 다른 점"(문구는 `CalibrationCopy.cs`, 녹음 문구 목록은 `CalibrationVoice` 주석). 탭 아이콘 4개(홈·기록·친구·내 정보)를 UI Kit에. PRD v0.5·유스케이스 v0.2·백로그 v0.2 검토 `docs/product/prd_v05_review.md` 전달(결정: 실사 영상 최하위, 몸무게 필드 둠, 오프라인 달리기 미정). 노션 Front-End의 "API를 Query hook으로 연동하기"는 다른 프로젝트(displayu, React) 템플릿. 문서 형식 논의(11/02, `docs/log/team_discussions.md`)의 3종 문서 방향 답 확인
- **지환**: (보정 API·#34·#35는 11/11 develop에 합침) "지금 N명"은 (나) S3 실시간 인원으로 하자는 답장(민재가 카톡으로). BL-42 끝나면 알려 달라고. `data/runs/20261029-004604/note.md`·`docs/speed/airpods_flight.md`의 실명 동의(없으면 "팀원 J", 그 전엔 커밋 안 함). 브랜치 `feat/airpods-flight`·`feat/airpods-flight-analysis`를 지워도 되는지
- **둘 다**: 아이폰 유무(시연 2대), 각자 iOS 빌드 환경(맥·Xcode·Unity iOS 모듈), Apple Developer 계정 공유 방식, `shape-0218`이 상준인지
- **팀 공유(민재가 직접)**: 노션 개발 계획 공유(워크스페이스 멤버가 민재뿐 → 초대나 공유 링크), 바뀐 역할. 공유되면 CLAUDE.md "역할"과 ADR-0007의 "팀 공유 필요"를 지움

## 민재가 직접 할 것
- **아이폰 확인 2개(맥 + 아이폰, 각 5분)**. 설치는 `unity_map.md` "아이폰에 설치"
  1. 한글 입력: 가입 화면 닉네임 칸에 한글(받침·쌍자음 포함)이 바로 쳐지고 지우기·커서 이동이 되는지. 이미 가입돼 있으니 앱을 지우고 다시 설치(키체인은 새 설치 때 지워짐). 어색하면 ADR-0001 11/06 보충대로 입력 칸만 iOS 네이티브
  2. 권한 거부 → 허용: 다시 설치 → 점검 화면에서 모션 요청 → "허용 안 함" → 설정 앱 → 동작 및 피트니스 켜기 → 돌아오면 점검 화면에 `Motion → Granted` 이벤트가 오는지(허용 → 거부 방향은 iOS가 앱을 꺼서 이벤트 없음)
- 원격 옛 브랜치 지우기(PR 화면 버튼): `feat/dev-server`, `feat/airpods-treadmill`, `docs/handoff-1106`
- `Lm_LampGlass.mat`: 11/07 Unity가 다시 써서 `_EMISSION` 키워드 한 줄이 빠짐(의도 아님) → 되돌릴지 결정
- **커밋 안 한 것**: 위 머티리얼, 지환 실명 파일 2개, 와이어프레임 초안 `docs/product/scrun_wireframe_v1.html`, 중간발표 자료 `docs/meeting/midterm/`(남의 유튜브 캡처 → 올릴 땐 빼기), 연구 문서 `docs/research/mywhoosh_benchmark.md`·`competitor_comparison.md`(넣을지 결정, 넣으면 텍스트만. APK는 `C:\Users\rlaal\tools\rea-targets\`에만), `.claude/settings.json`
- **정할 것**: 러닝복(BL-34)을 S8에 둘지 11/24 뒤로 뺄지, 새 맵·아바타 일정, 11/16(S5 시작) 범위 조정, Apple Developer를 일찍 신청할지(승인 대기, 연회비 재료비 후보), BL-32를 S8에 넣을지, 노션 기능 보드를 작업 표에 맞출지. 작업 표·API 명세 표에 제목 빈 줄 하나씩(안 지움)

## 일정
- 목표 **캡디 최종 발표 12/11**(발표 + 최종 보고서 60%, 평가 "완성도"·"Proposal 대비 진척", 출처 노션 SCRUN > 문서 > 캡스톤 디자인 공지)
- **11/14(토)** 교수 + 3차 멘토링 보고서·시연(원래 11/13, 11/12 밤 미뤄짐. 범위는 0017 "11/13 시연" 그대로), 11/20 전문가, 11/27 교수 + 4차 보고서, 12/18 최종 보고서 + 사용자 매뉴얼
- 11/23 기능 완성 → 11/24–12/10 안정화(3순위·알림·정책·출시 준비·새 맵 제작) → 12/11 발표
- 위험: 실시간 공동 맵, 에셋 구매(월구독 11/20 17시 전), 헬스장 실험(지환, S5 즈음)
- 스프린트·담당·작업 표는 0017과 노션 SCRUN > [개발 계획](https://app.notion.com/p/4114ae82f1de81a9bec2db16a9bfe635). 작업 표는 11/10 밤 최신화(보정 계산 이름 "사다리 2–14 km/h 7단계 × 15초", 보정값 API 메모 "급함(#30 입장 409)")

## 환경 메모 (영역 문서에 없는 것만)
- **공용 개발 서버**(0019, ADR-0008, 주소·배포는 `docs/api/README.md`·`backend/README.md`): SSH `ubuntu@161.118.211.155` 키 `~/.ssh/oracle_key`는 Windows 노트북에만. 매일 KST 4:30 DB 백업(`~/scrun-backups`). 남은 것은 0019(429·서버 밖 백업·진짜 도메인 BL-51·유휴 회수 확인). BL-44 판단(11/07)은 "진행 중"(저장·조회 API는 로그인 5개뿐, 개발·테스트 DB 분리까지, 운영은 BL-51, 실시간 연동은 BL-25 뒤). 공통 규칙 "미정"에 "OCR 서버/앱 미정"이 남아 있음(11/06 서버 처리로 확정, BL-17) → OCR API 만들 때 고침
- **Windows 노트북**: 로컬 Scrun 서버는 8001(`unity_map.md`, 에디터 API 주소도 EditorPrefs `Scrun.ApiBaseUrl` = 8001로 맞춰 둠, 기본값 8000은 옛 실험 서버), 개발 DB는 11/12 `7a1d4e2b9c30`(만 14세 약관)까지 올림, 에디터 API 주소 `http://127.0.0.1:8001`. 로컬 8001 서버는 Claude 세션 백그라운드라 세션이 끝나면 꺼짐 → `cd backend; ../.venv/Scripts/python -m uvicorn app.main:app --port 8001`. 에디터 로그인 계정은 보정 있음·닉네임이 테스트 욕설. 이 PC `PlayerPrefs`에 소개 봄(`scrun.intro.seen`)·온보딩 끝남이 남아 있어 처음 실행 흐름을 보려면 지우기, `backend/.env`(`KAKAO_MODE=fake`), 개발 DB는 `alembic upgrade head`. 노트북 `server.py`(8000, 옛 속도 실험)가 켜져 있을 수 있음
- **맥**: `uv`·Python 3.12·`gh`는 `~/.local/bin`(셸 PATH엔 없음, 필요하면 `~/.local/bin/uv python update-shell`). Homebrew 없음. 시뮬레이터 키보드는 Mac 입력기를 영어로 바꿔야 영어가 쳐짐. Claude in Chrome 연결: VS Code에 전체 디스크 접근 권한 → 확장 안 CLI(`~/.vscode/extensions/anthropic.claude-code-*/resources/native-binary/claude --chrome`)를 한 번 실행 → `/chrome`에서 Enabled by default → 새 대화
- **맥에서 커밋하지 말 것**: Mixamo 파일이 없어 Unity가 지운 `Characters/YBot` `.meta` 7개(받은 뒤 `git checkout -- unity/TestRun/Assets/Scrun/Characters/YBot`), iOS 전환 때 Unity가 Poly Haven `.meta`에 쓴 iOS 칸·`materialImportMode`, URP 전역 설정·ProjectSettings(시뮬레이터 SDK 값). 그 밖은 `unity_map.md` 171–172줄
- `unity/Builds/device-console.log`(git 밖)에 카카오 토큰이 찍혀 있음 → 공유 금지
- 아이폰 빌드에서 Island01은 Mixamo가 없어 캐릭터가 빠진 채 빌드됨(맥). `App.uss`의 `:last-child`는 UI Toolkit이 모름(콘솔 경고, `.field-col`·`.dialog-buttons` 마지막 여백) → 클래스로 바꾸기
- **카카오 개발자 앱**: 민재 계정 "SCRUN"(ID 1641065), 기본 네이티브 앱 키에 iOS 번들 ID `com.scrun.app`(Android `com.scrun.dev`), 동의 항목 기본(회원번호만). 계정 삭제(BL-48) 때 연결 해제 웹훅(User Unlinked)
- **공개 개발 백로그를 Chrome 없이 읽기**: `POST https://heavenly-beak-116.notion.site/api/v3/queryCollection`(collection `14a69719-4a05-45bb-8bda-36cc20118493`, view `7fe2ce1d-a4a0-4daf-8d77-8a5db8ebd139`, spaceId `e29dea3b-d189-81ed-896d-0003ca665f04`)으로 51개 전부. 11번부터 번호가 바뀜(HUD 14→12, 계정 삭제 39→48, 실시간 36→25)
- API 명세(0014) 남은 것: Figma 화면 ID 확인(약관 A-06·친구 G-05에서 G-09·오류 I-08 추정, H-05·H-06 미확인), 오류 문구 칸은 상준, 백로그에 지표 이벤트(`/v1/events`) 추가 제안, BL-39는 민재 확인 후 공개 백로그에서 완료로
- Git: `develop`·`main` 보호(PR로만, 승인 0명, 강제 푸시·삭제 금지). CLAUDE.md는 CRLF라 고칠 때 줄바꿈 유지
- 재료비 2차(11월 공지 대기): Claude Max 3개(상준 추가, 11/19에서 12/18), 나머지 약 23만원 → 캐릭터·환경 에셋 먼저, AI 3D 1개월. 서버는 ADR-0008. 조교에게 물을 것: 에셋 "소프트웨어 일회성 구매" 인정 여부 + 서버 예비 항목

## 속도 추정 다음 실험 (결론은 `docs/speed/README.md`)
- **다음 헬스장**: 저장된 보정으로 보정 없이 달려 다른 날 오차, 10에서 11 km/h 1에서 2분 → 7로 내려 1분(S22에서 틀렸던 상황), 범위 밖(11 이상, 6 이하). 에어팟 공중 시간(계획 0012)·흔들림 폭을 **바꾸지 않고** 비교한 뒤 앱 반영. 기준은 "걸음 수가 같은데 속도가 다를 때 가르는지"
- 그 전에: 맥에서 SpeedRecorder 빌드 + `data/runs/20261029-040156/calibration.json`을 앱 Documents에(계획 0010 5번). 0024 결정 1을 따르면 SpeedRecorder가 플러그인의 `SpeedEstimator.swift`를 참조하게 바뀜
- 에어팟 끊김은 5에서 20초 덩어리(헬스장 혼잡·몸 가림 가설) → 끊기면 폰만으로. 헬스장에선 아이폰 핫스팟
- Nijs 2021 기준값이 에어팟에도 맞는지는 가설. 발표·보고서엔 "공개 데이터 재분석"
- 계기판 OCR(`docs/product/console_ocr.md`): 다음은 S22 밝기 낮춘 사진(다른 숫자 화면 포함) 비교. 앱 카메라 ISO 최저·셔터 1/60초 이상·브래킷. 사진은 유리에 사람이 비쳐 저장소에 안 올림
- 미기록: B(고상준) 키·몸무게·신발, 민재 키·몸무게

## 그 밖에 기다리는 것
- 노션 SCRUN > 문서 > 기획 문서의 열린 질문 중 개발이 답할 것: Q-07·08·09(11/10 결정: 2–14 km/h 7단계 × 15초)·10(재보정 기준 미정, 보정은 서버 calibration)·11(5에서 10초)·12·13(ADR-0001·0007)·29(카카오)
- 앱 기획 미정 항목은 `docs/product/scrun_usecases_v1.md` 끝 "기획 결정·미정 기록"
- 중간발표 자료(10/30) `docs/meeting/midterm/`: README(슬라이드 구성·수치 출처·부록), 그림 `img/`(`figs.py`·`render.sh`), 대본 `대본_13장부터.md`, `질문대비.md` 52개. 10/12 블라인드는 같은 날 13분 뒤 → 예전 보고서 표현 확인
- 교수님 미팅자료: 최종본 `미팅자료_최종본.html` 위치 못 찾음 → PDF 변환 보류. 고칠 땐 `docs/meeting/prof_body.html` → `python build_prof.py .` → `professor-meeting.html`

## 나중에 (보류)
- 유사 서비스 비교(11/10, `docs/research/competitor_comparison.md`)에서 나온 적용 4개: `ISpeedSource` 추상화, 끊김 타임아웃(ADR-0006 추가 후보), 코스 = 스플라인, 고스트 파일. 결론: 세 앱 모두 속도를 기구·GPS에서 받고 추정하지 않음 → 폰 + 이어폰 추정이 우리 차별점(보고서용). REA Windows Ghidra는 arm64 .so를 거부함(더 하려면 실행 중 통신 실측이나 데스크톱 Ghidra)
- BLE FTMS 지원 러닝머신이면 FTMS를 선택적으로 먼저 쓸지(기본은 센서 추정, ADR-0004·0006). 정하면 별도 ADR. `competitor_comparison.md` 4절
- 실사 영상 가림 처리 등(메모리 sfm-followups). 다음 후보: 이슈 보드 / 반복 작업 스킬 / SoundTrackDB 몸무게·헬스장·피로 분석
- 작업 요령: walking skeleton(끝에서 끝까지 얇게 먼저 잇기). Unity는 `unity_map.md`·`unity/tools/README.md`, 빌드 대상은 맥 iOS·Windows Android. `ucs.sh`로 설정 바꿀 땐 `AssetDatabase.SaveAssets()`가 다른 에셋도 다시 쓸 수 있음. 화면 흐름 `scrun_userflow_v2.html`은 로컬 HTML로
