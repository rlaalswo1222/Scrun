# 민재 인수인계 (2026-11-10 새벽, 맥)

민재(git `minjae`)의 개인 작업 메모. 새 채팅은 `CLAUDE.md`(규칙·문서 지도)와 이 파일을 먼저 읽는다. 팀원은 읽기만, 고치지 않는다. 끝난 결론은 영역 문서(`docs/speed/README.md`, `docs/unity/unity_map.md`, `docs/product/`, `docs/adr/`, `docs/plans/`)에 있고, 11/09까지의 전체본은 `docs/log/HANDOFF_minjae_20261109.md`(그 전은 `_20261107.md`, `_20261106.md`).

## 지금: 계획 0018 5번 끝(PR #28 `feat/ios-bridge`) → 6번(카카오 SDK, PR #31)
- **5번(11/10, 맥)**: Swift 브리지 + `IosNative`, 빌드 메뉴 Scrun → iOS → Xcode 만들기, 개발 빌드 전용 "네이티브 점검" 화면(로그인 아래 링크). ADR-0009, 사용법·주의는 `docs/unity/unity_map.md` "아이폰 진짜 구현"·"아이폰 빌드". 테스트 48개 통과, 시뮬레이터에서 카메라·알림 팝업·키체인·설정 앱 확인
- **다음**: `/code-review` → 커밋·PR(커밋할 땐 아래 "맥 주의"의 Unity가 바꾼 파일을 빼고 내 파일만) → 6번: 카카오 SDK를 빌드 후처리로 SPM 추가(`PBXProject.AddRemotePackageReferenceAtVersion`, `kakao-ios-sdk`), URL 스킴 `kakao{앱키}`·`LSApplicationQueriesSchemes`, 앱 키는 `unity/TestRun/ScrunLocal.json`의 `kakaoNativeAppKey`(git 밖), Swift `ScrunKakao.swift`를 채우고 카카오톡 앱에서 돌아오는 URL은 Unity `AppDelegateListener`(Objective-C)로 받아 `AuthController.handleOpenUrl`
- **아이폰 확인(남음)**: 모션·헤드폰 모션 팝업(에어팟 없이도 뜨는지 = 가설), 에어팟 끼고 빼기 → 이벤트, 거부 → 설정 앱에서 허용 → 돌아오면 이벤트, 앱 재실행 뒤 키체인 토큰, 노치·한글 입력, 공용 서버로 실제 가입(6번 뒤). 서명: Xcode → Settings → Accounts에 Apple ID → 팀 ID를 `ScrunLocal.json` `appleTeamId`에(또는 Xcode Signing에서 고름), 아이폰 개발자 모드
- **11/10 계획 0020 구현 끝(PR #29, develop에 합침)**: `Native.Speed`(`ISpeedSensor`) + 에디터 가짜 `FakeSpeedSensor`(Fake Native 창 "속도 센서" 칸, 시나리오 버튼) + `Scripts/NativeSpeedSource` 어댑터. EditMode 49개 통과, Play에서 시나리오 확인. 사용법 `docs/unity/unity_map.md` "앱 코드 묶음". 리뷰 반영: 끌 때 이벤트 없음, 켠 쪽이 끔, 일시정지 시계
- **다음 세션 바로 할 일**: (Windows) PR 합친 뒤 지환에게 알림(아래 "민재가 직접 할 것") → **S2 보정 계획(0021) 쓰기**: 보정 계산(걷기 1 + 달리기 3 × 20초, 계획 0013) + 보정 화면, 가짜 센서·지환 보정값 API 스텁으로 에디터에서 → 그다음 S3 실시간 방 서버 계획. (맥) 아래 0018 5번 + Swift 속도 플러그인(`Native.Speed` 진짜: `SpeedSensorBase.Sample`만 구현). 아이폰에서: `CMMotionManager`에 모션 권한이 필요한지(Apple 포럼은 불필요 → 필요 없으면 권한 필수 목록 조정), 걸음 감지까지 걸리는 시간
- **S2(11/10–11)**: **실시간 방식 결정 끝(11/10): FastAPI WebSocket**(ADR-0010, 설명 자료 `docs/research/realtime_options.html`, 오라클 싱가포르 지연 중앙값 222ms 실측). 센서 준비·걸음 감지 C# 모양 + 가짜는 끝(계획 0020, PR #29). 남은 S2: 보정 계획 0021(보정 계산·보정 화면), Swift 속도 플러그인(맥). 노션 작업 표·기능 보드 민재 상태는 11/10 정리함(진행 중·완료). S3 "실시간 방 서버 기본" 계획에 넣을 것: 메시지 모양(입장·상태·들어옴/나감·봇), 입장 응답 `realtime.url`·`token` 확정, 재접속, 앱 연결 인터페이스(나중에 바꿀 수 있게), 아이폰 2대로 지연 실측
- **맥 상태(11/10)**: Xcode 27.2, Unity 6000.6.7f1 + iOS 모듈, 빌드 대상 iOS로 전환됨, MCP for Unity 서버 켜짐(`unity/tools/*.sh`가 이제 맥에서도 됨). uv·Python 3.12는 `~/.local/bin`(셸 PATH엔 아직 없음: 필요하면 `~/.local/bin/uv python update-shell`). `gh` 없음(푸시·PR 하려면 설치·로그인, Homebrew도 없음). 시뮬레이터 앱이 없고 Xcode 27의 DeviceHub가 시뮬레이터 화면을 보여 주는 듯(가설)
- **맥 주의(Unity가 바꾼 파일, 커밋 금지)**: Mixamo 파일(`Characters/YBot/*.fbx`)이 이 맥에 없어 Unity가 그 `.meta` 7개를 지움 → mixamo.com에서 받기 전엔 커밋 때 빼고, 받은 뒤 `git checkout -- unity/TestRun/Assets/Scrun/Characters/YBot`. Poly Haven은 받았지만 iOS로 전환하며 Unity가 `.meta`에 iOS 칸·`materialImportMode`를 고쳐 씀(빼고 커밋). 맥 기본 화면 배치에 열려 있던 "Unity Version Control" 창이 버전 관리 모드를 계속 "Unity Version Control"로 바꿔서 창을 닫고 "Visible Meta Files"로 되돌림(그 창을 다시 열지 않기). URP 전역 설정·ProjectSettings(시뮬레이터 SDK 값)도 Unity가 다시 썼던 것 되돌림
- `/code-review`(11/10) 반영: 키체인 저장 실패에도 로그인 전환을 끝까지(`Session.Persist`), `AppRoot`가 모든 예외에서 로그인 화면으로, 팝업 기다리기는 "활성 2초 연속"이면 끝(제어 센터를 내려도 안 멈춤), 시작 때 알림 상태를 기다리지 않음, 에어팟 모션은 `Headphones.Changed` 구독 중에만 켬(배터리), 메인 스레드 넘기기는 PlayerLoop 대신 SynchronizationContext. 안 고친 것: 이어폰 `Status`·`DeviceName`이 따로 조회(작음), `unity/tools/*.sh` 머리 3줄 중복
- 빌드(에디터 밖) 안드로이드에선 `Native`가 예외(아이폰만). Island01은 Mixamo가 없어 캐릭터가 빠진 채 빌드됨
- `App.uss`의 `:last-child`는 UI Toolkit이 모름(콘솔 경고, `.field-col`·`.dialog-buttons` 마지막 여백) → 클래스로 바꾸기

## 공용 개발 서버 (계획 0019 완료, ADR-0008)
- `https://161-118-211-155.sslip.io`(오라클 싱가포르 ARM, SSH `ubuntu@161.118.211.155` 키 `~/.ssh/oracle_key` — Windows 노트북에만 있음). 진짜 카카오만, 아이폰용(에디터는 localhost 가짜 `k_123`). develop 기준으로 올라가 있음, 다시 올리기 `~/scrun/backend/deploy/update.sh [브랜치]`. 비밀값은 서버 `backend/deploy/.env`에만. 매일 KST 4:30 백업(`~/scrun-backups`)
- Bandly는 멈춤(볼륨 유지, cron 주석, 백업 `Desktop/Bandly_backup_20261109/`, 되살리기: cron 주석 풀고 `~/server`에서 `docker compose -f docker-compose.oracle.yml up -d`, 단 80·443을 Scrun Caddy가 씀)
- 남은 것: 로그인 시도 제한(429), 서버 밖 백업, 진짜 도메인(BL-51), 오라클 계정이 유휴 회수 대상인지 콘솔 확인
- Windows 노트북: 8000 포트는 예전 속도 실험 `server.py`가 씀 → 로컬 Scrun 서버는 8001(에디터 API 서버 창에 `http://localhost:8001`). `backend/.env`(서명 키 무작위, `KAKAO_MODE=fake`), 개발 DB는 마이그레이션 `3f1c9a7e52d4`까지. 팀원은 각자 `.env` + `alembic upgrade head`
- 서버 정해진 것(11/08, 계획 0016·`docs/api/README.md`): refresh 재사용 30초 유예 + 그 로그인만 폐기, 재로그인은 deviceId로 같은 기기 폐기 + 사용자당 5개, 닉네임은 한글 완성형·영문·숫자·`_`(NFKC). 공통 규칙 "미정"에 "OCR 서버/앱 미정"이 남아 있음(11/06 서버 처리로 확정) → OCR API 만들 때 같이 고침

## 민재가 직접 할 것
- **지환에게**: 11/09 답장(수정본: 샘플 주기·thumbnailUrl·20명 임시 방식) + "FakeNative·앱 틀·로그인 화면 develop에 있음, 권한·에어팟 화면 시작해도 됨(사용법 `docs/unity/unity_map.md` "앱 코드 묶음")". 추가로 알릴 것: 실시간은 WebSocket으로 결정 → "지금 달리는 사람 수"는 연결 수로 세니 샘플 주기·`updated_at` 갱신은 신경 안 써도 됨. **+ 속도 센서 가짜(`Native.Speed`, 계획 0020)로 센서 준비·카운트다운 화면 시작 가능**. 보냈으면 이 줄 지우기
- 원격 옛 브랜치 `feat/dev-server` 지우기(PR 화면)
- `Lm_LampGlass.mat`: 11/07 Unity 설정 저장 때 Unity가 다시 써서 `_EMISSION` 키워드 한 줄 빠짐(의도 아님) → 되돌릴지 결정. 커밋 안 한 것: 이 파일, 지환 실명 파일 2개(아래), 와이어프레임 초안, 중간발표 자료, `.claude/settings.json`
- 스프린트 재분배(11/08)·11/13 시연 범위는 계획 0017 "11/08 재분배"·"11/13 시연"

## 개발 계획 (계획 0017, 노션 SCRUN > [개발 계획](https://app.notion.com/p/4114ae82f1de81a9bec2db16a9bfe635))
- 기능 보드(카드 21개, 보드·타임라인) + 작업 표(약 86줄, 줄마다 **할 일·완료 확인**, "스프린트별 할 일" 보기). 할 일은 백로그 인수 조건 + 저장소 상황 + 구현 단계, 완료 확인은 대부분 Claude 제안. 구분은 FE·BE·3D·디자인·공통(Swift 코드는 FE 안에서 이름에 `[FE·Swift]`, Xcode·실기기 필요)
- 원칙: PRD 핵심 루프(백로그 우선순위 1)를 앱 흐름 순서(로그인 → 권한·에어팟 → 보정 → 코스 → 달리기 → 인증 → 기록)로 S5(11/17)까지, 2순위는 S6부터. 기능 단위로 끝까지(민재·지환이 화면·API까지), 상준은 UI Kit·공통 USS·컨셉아트. 2일 스프린트 S1(11/08–09)에서 S8(11/22–23)
- **민재 S1**: Swift 브리지, 카카오 iOS SDK, ApiClient(서버 호출 한 파일) + 약관·닉네임 확인·내 정보 API, 로그인·약관·프로필 화면, 첫 화면 틀(탭 4개), 권한·에어팟 화면. **맥 없이 되는 것**: API, ApiClient, 로그인 화면(에디터 가짜 로그인 `k_123`), 탭 틀, 권한·에어팟 화면(가짜 상태). `[FE·Swift]` 3줄은 맥·아이폰 필요. → 계획 0018(위 "지금")
- **11/13 시연**: 로그인 → 권한·에어팟 → 보정 → 코스 → 달리기 + 실시간 같이 뛰기 기본(두 사람이 같은 맵에서 보임). 서버는 같은 와이파이 노트북, 기기 2대(없으면 아이폰 + 에디터). 실시간 방식은 S2에 반나절(WebSocket vs Photon 설명 자료 → 민재 선택 → ADR)
- iOS 빌드 환경은 각자(스프린트 밖, 무료 Apple ID는 7일마다 재설치). FakeApi는 안 함(11/06, 스텁 서버로 충분). BL-44 판단(11/07): "진행 중"(카카오 확인·제공자별 연결 됨, 저장·조회 API는 로그인 5개만, 개발·테스트 DB 분리까지, 운영은 BL-51, 실시간 연동은 BL-25 뒤). 맵은 임시로 기존 섬 맵(Island01, 블록아웃 안 함), 컨셉아트가 정해지면 새 맵(BL-43 3km 루프, 기존 코스는 6.7–10.9km)·새 아바타(컨셉아트 S5 상준, 에셋 결정 S6 지환, 제작은 11/24 이후 — 시점은 제안)
- OCR은 서버 처리로 확정(BL-17, 11/06). 옛 메모의 "OCR 위치 미정"은 지난 정보
- **카카오 개발자 앱(11/07)**: 민재 계정 "SCRUN"(ID 1641065), 기본 네이티브 앱 키에 iOS 번들 ID `com.scrun.app`, 카카오 로그인 ON, 동의 항목 기본(회원번호만). Unity iPhone 번들 ID도 `com.scrun.app`(Android `com.scrun.dev`). 앱 키는 저장소에 안 올림(로컬 설정 파일). 계정 삭제(BL-48) 때 연결 해제 웹훅(User Unlinked)
- **정할 것**: 러닝복(BL-34)을 S8에 둘지(실제 세트는 새 아바타에 달림) 11/24 이후로 뺄지, 새 맵·아바타 일정 확정, 11/16(S5 시작)에 범위 조정, Apple Developer 가입은 승인 대기가 있어 일찍 신청할지(연회비 재료비 후보), BL-32를 S8에 넣을지. 작업 표에 제목 빈 줄 하나(안 지움)
- 다음 서버 묶음(스프린트에 흩어 넣음): 내 정보·닉네임 확인(S1), 보정(S2), 코스(S1 지환), 달리기(S5), 설정·계정 삭제 등. 요청 로그·Sentry·Redis는 미룸(Redis는 실시간 방식 정할 때)
- API 명세(계획 0014): 노션 API 명세 표는 `scripts/notion_pages.py`로 생성(서버 API 바꾸면 다시 생성 → 노션에 덮어쓰기), 표에 제목 빈 줄 하나(민재 생성 추정). 남은 것: Figma 화면 ID 확인(약관 A-06·친구 G-05에서 G-09·오류 I-08 추정, H-05·H-06 미확인), 오류 문구 칸은 상준, 백로그에 지표 이벤트(`/v1/events`) 추가 제안. 데이터 모델은 `docs/api/data_model.md`(계획 0015), BL-39는 민재 확인 후 공개 백로그에서 완료로
- 백로그 최신 원본 = **상준 공개 개발 백로그**(https://heavenly-beak-116.notion.site/eb95382a0c1a4745a33d3bd3c2380600, 11/06 민재 "여기가 항상 최신"). Chrome 없이 `POST https://heavenly-beak-116.notion.site/api/v3/queryCollection`(collection `14a69719-4a05-45bb-8bda-36cc20118493`, view `7fe2ce1d-a4a0-4daf-8d77-8a5db8ebd139`, spaceId `e29dea3b-d189-81ed-896d-0003ca665f04`)으로 51개 전부 읽힘. 11번부터 번호가 바뀜(HUD 14→12, 계정 삭제 39→48, 실시간 36→25). 민재 노션 SCRUN > 문서의 기획 문서는 참고 안 함(옛 사본)

## 일정·목표
- 목표는 **캡디 최종 발표 12/11**(발표 + 최종 보고서 60%, 평가 "완성도"·"Proposal 대비 진척"). 출처 노션 SCRUN > 문서 > 캡스톤 디자인 공지
- 피드백: 11/13 교수 + 3차 멘토링 보고서, 11/20 전문가, 11/27 교수 + 4차 보고서, 12/18 최종 보고서 + 사용자 매뉴얼
- 11/23 기능 완성 → 11/24–12/10 안정화(3순위·알림·정책·출시 준비·새 맵 제작) → 12/11 발표
- 위험: 실시간 공동 맵, 에셋 구매(월구독 11/20 17시 전), 헬스장 실험(지환, S5 즈음)

## 기다리는 것·확인할 것
- **팀 공유(민재가 직접)**: 노션 개발 계획 공유(워크스페이스 멤버가 민재뿐 → 초대나 공유 링크, 담당 칸은 선택 칸), 바뀐 역할. 공유되면 CLAUDE.md "역할"과 ADR-0007의 "팀 공유 필요" 지우기
- **팀원에게 확인**: 지환·상준 아이폰 유무(시연 2대), 각자 iOS 빌드 환경(맥·Xcode·Unity iOS 모듈), Apple Developer 계정 공유 방식, `shape-0218`이 상준인지
- **지환 동의 대기**: `data/runs/20261029-004604/note.md`·`docs/speed/airpods_flight.md`의 "김지환" 실명(커밋 안 함). 동의 없으면 "팀원 J"
- **Git 남은 것**: 원격 `feat/airpods-treadmill`·`docs/handoff-1106` 삭제(PR 화면 버튼), 지환 브랜치 `feat/airpods-flight`·`feat/airpods-flight-analysis`는 다 합쳐짐 → 지워도 되는지 지환에게
- **PRD v0.5·유스케이스 v0.2·백로그 v0.2 검토(11/06)** → `docs/product/prd_v05_review.md`, **상준에게 전달 대기**. 결정: 실사 영상 최하위, 몸무게 필드 둠, 오프라인 달리기 미정
- **노션 기타**: SCRUN > 문서 > 기획 문서(열린 질문 Q-01에서 Q-32 등). Front-End의 "API를 Query hook으로 연동하기"는 다른 프로젝트(displayu, React) 템플릿 → 상준에게. 개발이 답할 열린 질문: Q-07·08·09(걷기 1 + 달리기 3 × 20초)·10(calibration.json)·11(5에서 10초)·12·13(ADR-0001·0007)·29(카카오)
- **와이어프레임 v1 초안** `docs/product/scrun_wireframe_v1.html`(커밋 안 함). 앱 기획 미정 항목은 `docs/product/scrun_usecases_v1.md` 끝 "기획 결정·미정 기록"
- **문서 형식 논의(11/02, 상준과 이견)**: `docs/log/team_discussions.md`. 상준 3종 문서 방향 답 확인
- **중간발표 자료**(10/30) `docs/meeting/midterm/`: README(슬라이드 구성·수치 출처·부록 A 속도·B SfM·C 놓친 기술 문제), 그림 `img/`(`figs.py`·`render.sh`), 대본 `대본_13장부터.md`, `질문대비.md` 52개. 커밋 안 함(남의 유튜브 캡처 → 올릴 땐 빼기). 10/12 블라인드는 같은 날 13분 뒤 → 예전 보고서 표현 확인
- **미기록**: B(고상준) 키·몸무게·신발, 민재 키·몸무게. 노트북 `server.py`가 켜져 있을 수 있음
- **교수님 미팅자료**: 최종본 `미팅자료_최종본.html` 위치 못 찾음 → PDF 변환 보류. 고칠 땐 `docs/meeting/prof_body.html` → `python build_prof.py .` → `professor-meeting.html`

## 정해진 것 (근거는 링크)
- **역할**(11/07, CLAUDE.md "역할", 계획 0017)
- **플랫폼**: 아이폰 + 에어팟 전용(ADR-0007). S22는 실험 도구로만
- **앱 구조**: Unity 하나(ADR-0001, 11/06 "SwiftUI 앱 안에 Unity" 비교 보충: 남는 차이는 구조 비용 — 내보내기·합쳐 빌드, Unity 런타임 제약, 두 쪽 디버깅)
- **로그인**: 카카오 먼저, Apple 로그인·계정 삭제는 출시 전
- **Git**(11/06): `develop`·`main` 보호(PR로만, 승인 0명, 강제 푸시·삭제 금지) → 내 작업도 매번 브랜치 + PR. `.gitattributes`는 안 함. CLAUDE.md는 CRLF라 고칠 때 줄바꿈 유지
- **Figma → Unity**(11/05): 94개 화면, 옮기는 방법·완성 기준은 `docs/unity/unity_map.md` 끝 "앱 화면" 절
- **재료비**: 1차(10/19 Claude Max 5x 2개)·멘토링 보고서 2차 완료. 2차(11월 공지 대기): Claude Max 3개(상준 추가, 11/19에서 12/18), 나머지 약 23만원(환율 1,400원 가정) → 캐릭터·환경 에셋 먼저, AI 3D 1개월. 서버는 오라클 무료 먼저, 막히면 **선불** VPS 1개월(후불은 카드 삭제 규칙과 충돌, 11/20 17시 전 판단). Apple Developer 연회비 후보. 조교에게: 에셋 "소프트웨어 일회성 구매" 인정 여부 + 서버 예비 항목
- **서버**: 올리는 곳·백업·저장·테스트는 ADR-0008(제안), 운영 배포는 BL-51(11/24 이후). 직접 테스트: `backend/README.md` "진짜 로그인 해 보기"(Docker Desktop → `docker compose up -d` → uvicorn → /docs에서 `k_123`)

## 속도 추정 다음 실험 (결론은 `docs/speed/README.md`)
- **다음 헬스장**: 저장된 보정으로 보정 없이 달려 다른 날 오차, 10에서 11 km/h 1에서 2분 → 7로 내려 1분(S22에서 틀렸던 상황), 범위 밖(11 이상, 6 이하). 에어팟 공중 시간(계획 0012)·흔들림 폭을 **바꾸지 않고** 비교한 뒤 앱 반영. 기준은 "걸음 수가 같은데 속도가 다를 때 가르는지"
- 그 전에: 맥에서 SpeedRecorder 빌드 + `data/runs/20261029-040156/calibration.json`을 앱 Documents에(계획 0010 5번)
- 보정 설계(계획 0013, `docs/speed/calibration_design.md`): 다른 날 기록에서 같은 비교 → 앱 보정 순서(ADR-0005 아직 안 바꿈). `speedlab/exp/calib_design.py`(캐시 `data/external/soundtrackdb/calib_T.csv`)
- 에어팟 끊김은 5에서 20초 덩어리(헬스장 혼잡·몸 가림 가설) → 끊기면 폰만으로. 헬스장에선 아이폰 핫스팟
- Nijs 2021 기준값이 에어팟에도 맞는지는 가설. 발표·보고서엔 "공개 데이터 재분석"
- 계기판 OCR(`docs/product/console_ocr.md`): 다음은 S22 밝기 낮춘 사진(다른 숫자 화면 포함) 비교. 앱 카메라 ISO 최저·셔터 1/60초 이상·브래킷. 사진은 유리에 사람이 비쳐 저장소에 안 올림

## 작업 요령
- walking skeleton(끝에서 끝까지 얇게 먼저 잇고 살 붙이기)
- Unity: `docs/unity/unity_map.md`, `unity/tools/README.md`. 빌드 대상은 아직 Android(0018 5번에서 iOS로). MCP for Unity 사용자 범위 등록. `ucs.sh`로 설정 바꿀 땐 `AssetDatabase.SaveAssets()`가 다른 에셋도 다시 쓸 수 있음(11/07 머티리얼 1개)
- 보류: 실사 영상 가림 처리 등(메모리 sfm-followups)
- 화면 흐름 `scrun_userflow_v2.html`(사용자 브라우저에선 claude.ai 링크가 안 열림 → 로컬 HTML)
- 다음 후보(선택 대기): 이슈 보드 / 반복 작업 스킬 / SoundTrackDB 몸무게·헬스장·피로 분석
