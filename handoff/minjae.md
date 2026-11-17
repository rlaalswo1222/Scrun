# 민재 인수인계 (2026-11-18 새벽, Windows)

민재(git `minjae`)의 개인 작업 메모. 새 채팅은 `CLAUDE.md`(규칙·문서 지도)와 이 파일을 먼저 읽는다. 팀원은 읽기만 하고 고치지 않는다. 끝난 결론은 영역 문서(`docs/speed/README.md`, `docs/unity/unity_map.md`, `docs/api/README.md`·`realtime.md`, `docs/adr/`, `docs/plans/`)에 있다. 옛 전체본: `docs/log/HANDOFF_minjae_20261110.md`(11/10 정리 전), `_20261109.md`, `_20261107.md`, `_20261106.md`

## 지금 (11/18 새벽, S7 친구를 미리 함. develop = 오라클 d11c674(#69까지), DB e3a7c1f95b28)
- **S7 친구(계획 0044) #69 머지·오라클 배포(11/18)**: #46(지난 인수인계·계획 0044·0045)도 먼저 머지함. 오라클 사용자 2명 친구 코드 8글자로 바뀜·피드 인덱스 확인. 서버 6개 DB·허브 연결 + 마이그레이션 `e3a7c1f95b28`(친구 코드 8글자로 새로·피드 인덱스), 앱 G-01–G-11 화면·`Native.Share`(Swift `ScrunShare.swift` 새 파일)·러너 카드 "친구" 배지. pytest 전체·EditMode 411 통과, 에디터 Play로 G 화면 7개와 "같이 달리기" → 맵 입장까지 확인, `/code-review` 10건 중 9건 반영(1건은 무관한 머티리얼). 자세히 계획 0044 "진행" 11/18 줄·`unity_map.md` "친구 탭". 원래 로컬 파일들(`Lm_LampGlass.mat`, `.claude/settings.json`, 지환 실명 파일 2개, 미팅·연구 문서)은 그대로 커밋 안 함
  - 로컬 개발 DB를 `e3a7c1f95b28`까지 올림(에디터 계정 친구 코드도 바뀜). 에디터 계정에 가짜 친구 셋(`k_911` 바닷바람·`k_912` 주말러너·`k_913` 하늘보리)과 그들의 기록이 남아 있음(확인용). 에디터 API 주소는 8001로 되돌림. 8001에 떠 있는 서버는 옛 코드라 친구 탭은 8001에선 안 됨 → 다시 켜기(아래 5번)
- **열린 PR**: 이 인수인계(`docs/handoff-1118`). #46은 11/18 머지. 지환 **#61**(OCR 도구, `/code-review` 9건 → 민재가 지환에게 메시지로 보냄, PR 코멘트는 안 올림)
- **11/17 저녁 머지·배포: #63 기록 조회 API(계획 0043)** — 지환 S5 [BE] 줄을 민재가 가져옴. `GET /runs`(끝난 기록, 커서, 내 시간대 달)·`GET /runs/{id}`(그래프 120칸, 함께 달린 사람)·`PATCH /runs/{id}`(공유)·`GET /runs/{id}/ghost`(1초 칸, 원래 지환 S7) DB 연결. 함께 달린 사람은 실시간 허브가 러닝마다 모아 종료 때 `run_companions`에, 친구 표시는 읽을 때. `/rewards`만 스텁(BL-32). 오라클 07e2755 배포(마이그레이션 없음). 노션 API 명세 4줄·작업 표 [BE] 기록 조회·고스트 줄(담당 민재, 완료) 최신화
- **계획 번호**: 0044 S7 친구(승인), **0045 계기판 읽은 값 확인(승인)**: 서버가 읽은 값을 판정 전에 보여 주고(센서·결과는 안 보임) "맞아요 → 판정 / 틀렸어요 → 다시 찍기". 틀렸어요는 다시 찍기 횟수 안 씀(마감 10분까지만), 맞아요 뒤 오차 초과는 다시 찍기 없이 바로 미인증, 틀렸어요 시도는 서버에 남김, 한 칸만 읽히면 지금처럼 E-07. 서버·흐름은 지환(OCR PR 다음), **E-05b 화면 모양과 BL-17 줄 추가는 민재**. 0042는 지환 #61. **다음 새 번호 0046**
- **#62 머지(11/17 밤) → 리뷰는 PR에 안 올리고 민재가 직접 고침 = #64**: 사진 회전(Swift `ScrunCamera.upright`가 픽셀을 세우고 긴 변 2048로 줄여 씀, 아이폰 확인은 0033 맥 할 일 9번)·메인 스레드 디코딩(헤더로 크기만, 파일 읽기 `Task.Run`)·`FakeNativeConfig [Serializable]`·다시 찍기 권한 거부 → `RETAKE_GIVEN_UP`·다시 찍기 카메라 실패 문구·E-08 제목(다시 찍기 남으면 "기록이 서로 달라요", 상준 확인)·칩만 갱신·거리 글자 통일. EditMode 397·pytest 339. 안 고친 것: 포기 사유가 MISMATCH로 저장되는 건 서버 의도. 자세히 계획 0038 "머지 뒤 고친 것". #62의 CLAUDE.md 아이폰 서명(팀 공용 Apple 계정, 2단계 인증 민재 폰)은 그대로 들어감
- **11/17 머지·배포**: #59 페이스 봇(계획 0040) · #60 S6 달리기 화면(계획 0041). 오라클은 171ac99까지 배포(마이그레이션 없음)
  - 페이스 봇: 사람 + 봇 = 6(빈자리 채우기, 사람이 들어오면 사람에게서 가장 먼 봇부터 빠짐), 누가 달리기 시작할 때 그 근처에(느린 봇 앞·빠른 봇 뒤), 위치는 서버가 계산해 states에 `bot: true` → 모든 폰에 같은 자리. 앱 "BOT · 페이스 6:40". 설정 `BOT_FILL_TO`·`BOT_PACES_KMH`
  - S6: 왼쪽 위 미니맵(실제 코스 모양)·오른쪽 아래 거리·시간(D-10), 이름표를 앱 HUD로 → 누르면 러너 카드(D-12, 이름·BOT 배지·나와의 간격·**이번 러닝 거리**), 구간 알림(절반·마지막 500m/3분), 끊김 배너·흐림(D-16), 네트워크 없으면 I-07. 실시간 `state`에 선택 `run`
- **11/17 민재 결정**: 인원(지금 N명·20명·봇 채우기)은 **사람만, 고스트·봇은 안 셈, 고스트는 서버 방에 안 넣음**(`docs/api/realtime.md`) / **달리는 동안 햅틱 없음** / **친구 근접 알림(D-26) 뺌**, S7 친구 기능 때 상준과 다시 / 카드 "누적 거리" → "이번 러닝 거리"(상준 확인) / 속도 추정 알고리즘 손보기는 앱 전체 완성 뒤
- **헬스장 11/16(민재, iPhone 13 mini, 에어팟 없이 폰 센서만, 달리기 2번)**: 멈춤 지연 12번 모두 2초 안(평균 1.44초), 출발 평균 0.78초, 측정 예외 0번. 새 보정 오차 약 0.4–0.6 km/h(눈으로 본 값). 요약은 계획 0034 "진행"의 11/17 맥 줄
  - **민재 답 대기 2개**: ① 멈추기 직전 아바타가 12번 중 11번 2.5–4 km/h → 내려오기 전에 러닝머신을 걷기 속도로 낮췄는지(아니면 추정이 낮음) ② 멈춘 뒤 0.1–0.8초 만에 다시 출발한 4번 → 일부러 내려섰다 올라간 건지(아니면 달리는 중 1.2초 멈춤 판정이 잘못 걸림 → 판정 손봐야 함). 답 받으면 0034 상태를 "완료"로
- **노션 작업 표 11/17 최신화**: 민재 S3–S6 화면 줄은 코드 기준 완료로, 남은 실기기 확인은 새 줄 **"[공통] 아이폰 확인 (민재 S4–S6)"**(①–⑦, ① 러닝머신 지연은 끝)에 모음. 새 줄 "[FE] 달리기 단계 칩"(BL-12 ①, 스프린트 미정). 진행 중으로 남은 민재 줄: 러닝 중 측정 예외(아이폰·문턱), 러너 정보 카드(레벨·친구 칸 S7·S8), 운영 서버, 권한·로그인(아이폰 확인 2개). 지환 센서 준비·카운트다운 2줄도 11/16 헬스장 근거로 완료(남은 확인은 메모에) → **지환에게 알리기**. API 명세: runs 5개·인증 skip·실시간 3개 완료, 사진 인증 진행 중, `verification/reading`은 "지워도 됨" 메모
- **브랜치**: `fix/dev-recalibrate`(개발 빌드 마이 탭 "보정 다시 하기", PR 안 엶. 헬스장 실험용, 진짜 재보정 H-09 전까지) → PR로 올릴지 결정
- **결정·발견(이전)**
  - 맥 기준점 차이(걷기 3.5 → 5.0 등) 원인 = 11/11 `a7fda83`(동작 5개가 서 있기 Avatar 공유)로 클립 분석값이 바뀐 것. Windows에서도 같은 값 재현 → 지금 컨트롤러 + 발 속도 표가 맞음, 다시 재지 말 것(0036)
  - Figma와 다르게 한 것(상준 확인): D-17·D-19 "평균 페이스" 뺌, D-18 아이콘 임시, 측정 예외 임시 문구 둘, S6 위 결정들
  - "측정값 이상" 문턱(25 km/h, 2초 5 km/h)은 가설(헬스장 11/16에도 한 번도 안 걸림)
- **역할 메모**: 지환 S4 남은 것 = 촬영 화면 E-02·E-04–E-10(앱) + 진짜 OCR 엔진(ADR-0012), S5 = 결과·기록 목록·상세 화면(BL-19·20, 서버 API는 민재가 #63으로 끝냄), S6 = 주간 목표·홈 + **에셋 구매 판단 11/20 17시 전**. E-02는 `CourseDetailScreen.ReturnFromMap`의 `MapExit.VerifyConsole` 줄에서 `finishedRunId`로. 기록 목록·상세를 만들 때 마감 지난 PENDING은 `close_expired`를 먼저
- **로컬에만(커밋 안 함)**: 계획 0032 홈(로컬 브랜치 `feat/home-figma`, 홈은 지환 담당), `docs/meeting/midterm_feedback.md`, `docs/meeting/20261114_미팅자료.html`·`.pdf`, 지환 실명 파일 2개, `Lm_LampGlass.mat`, `.claude/settings.json`, 연구 문서 2개, 와이어프레임 초안
- **11/14 교수님 미팅**: 자료(민재 파트 6쪽)는 로컬. 교수님 피드백 6개(정확도 공개·연속 비교·정답값·교차 조건·사용성 테스트로 가설 검증·범위 조정, 아바타·디자인 누락)와 반영 계획은 **멘토링 보고서 3차**에 씀(아래). 3-3 "사람 3명·러닝머신 3기종": **민재 확인 = 3명 모두 러닝머신에서 측정, 중간발표는 1명만 예시**. 저장소엔 민재·상준 러닝머신 기록과 10/12·10/28–29 기계뿐 → 지환 러닝머신 기록·세 번째 기종 원본을 `data/`에 올리고 `docs/speed/README.md` 실측 줄에 추가(최종 발표에 사람별·기종별 표 공개한다고 보고서에 씀)
- **11/17 밤 멘토링 보고서 3차**(상준 초안 PDF를 HTML로 다시 만들어 교수님 반영 계획을 새로 씀): 로컬 `docs/meeting/20261117_멘토링보고서_3차.html`·`.pdf`, **git에 안 올림(민재 결정)**. PDF는 HTML을 Chrome `--headless=new --print-to-pdf`로. 정한 것: 정답 = 계기판 속도(실제 속도 측정 안 함, `docs/speed/README.md` "정답 속도 기준"), 연속 비교, 교차 조건은 추후, 사용성 테스트는 같은 사람이 일반 러닝머신 대 SCRUN(순서 반반, 즐거움 설문·목표 달성·다시 쓸 의향), 워치는 추후, 안정화 항목에 정확도 검증·사용자 테스트·실기기 점검. 진행상황은 지금 상태로 고침(테이블 21개, 기록 조회·고스트 API, S6 달리기 화면, 계기판 인증·0045). **저장소로 확인 못 해 그대로 둔 초안 주장**: 단계별 스킬 표를 CLAUDE.md에 적음(지금 CLAUDE.md엔 없음), 11/13 주간 회고(retro.md에 없음), SYNC.md, 화면 자동 점검, Blender 맵 49종·299곳, Figma 108화면, 아바타 4,058·뼈대 32·러닝복 6단계 → 상준·지환 확인
- **11/14 정한 것**: ① 블루투스 FTMS 지원 러닝머신이면 기구 속도 최우선 → ADR-0006을 고치는 새 ADR부터 ② 워치 팔 스윙 보폭 → 실험 먼저, ADR-0007 보충 ③ 다른 조건 검증은 추후 ④ 뺀 것: 페이스 그룹·코스 표지·갈림길·3D 사전 녹화·사용성 테스트 문구
- **오라클 서버**: Docker Compose 3개(api·db Postgres 16·caddy). 배포 `ssh -i ~/.ssh/oracle_key ubuntu@161.118.211.155 '~/scrun/backend/deploy/update.sh develop'`(마이그레이션도 같이 돎). Claude 배포·머지는 민재가 "배포해"·"머지해"처럼 말해야 권한 검사 통과(DB를 읽기만 하는 SSH도 막힘)
- **메인 캐릭터** = 팀이 만든 흰 모자 캐릭터(CLAUDE.md). 섬 아바타(Y Bot 임시) 교체 전에 #54 클립 지문이 다르다고 알려 줌 → 교체 땐 Build Runner Animator → Measure Runner Gait → 씬 다시 만들기, 지문까지 같이 커밋
- 맥에 일 넘기기: 계획 문서 맨 위 "맥 할 일"에 적고 푸시 → 맥에 "NNNN 맥 할 일 해 줘" 한 줄. Y Bot FBX는 맥에 이미 있음(11/13), Windows 바탕화면 `ybot_fbx.zip`은 지워도 됨
- 계획 번호: 0032(민재 홈, 로컬)·0033 카메라·0034 아바타 멈춤·0035 지환 #49·0036 지환 애니메이터·0037 달리기 화면·0038 지환 인증·0039 측정 예외·0040 페이스 봇·0041 S6 달리기 화면·0042 지환 칸 찾기(#61)·0043 기록 조회 API·0044 S7 친구·0045 읽은 값 확인 → **다음은 pull 뒤 0046부터**

## 다음 (새 세션은 여기부터)
0. **지환 소식**: 지환에게 보낼 메시지(#61 리뷰·0045·#64·#65)는 11/17 밤 채팅으로 줌 → 보냈는지 확인. 지환 다음 PR(서버 진짜 OCR, `OCR_ENGINE=onnx`, Docker 이미지 커짐·빌드 때 모델 받음) 오면: 리뷰 → 머지·배포 전에 오라클 디스크 여유 확인 → 배포 뒤 사진 3장 처리 시간·메모리 재기(지환 부탁) + 0045 "틀렸어요" 반복 부하. 그 PR은 #61 위에 쌓이니 #61 2번(`field.py` 그럴듯한 틀린 값)이 고쳐졌는지 같이 봄. 맥에서 0033 맥 할 일 9번(세로 사진)
0-1. **0045 민재 몫 끝**(11/17 밤): E-05b 화면 모양 **PR #67 머지**(develop 2ae799a, 연결 방법은 계획 0045 "진행"), 공개 백로그 BL-17 줄은 민재가 직접 넣음 → **지환에게 알리기**(#67 머지, PR 본문의 연결 주의점: 보낸 뒤 사진 들고 있기·`Tick`에 확인 단계 넣기)
1. **S7 친구 마무리**(계획 0044, #69 머지·배포 끝): 노션 작업 표 BL-27·28 줄·API 명세 친구 6줄(진짜, `level` null·`lastRunAt`·`runningDistanceM`·`recentRuns`·`details.who`) 최신화 → 아이폰 확인(공유 시트·복사·끌어 새로고침·밀어 삭제, 맥 할 일로) → 상준에게 Figma와 다른 것·임시 문구(계획 0044 "진행" 끝 줄), 지환에게 홈 B-01 "달리는 친구"는 `GET /friends/running` 그대로 쓰면 된다고
2. **헬스장 답 2개**(위 "민재 답 대기") → 0034 마무리
3. **아이폰 확인**(노션 "[공통] 아이폰 확인" ②–⑦): 맥 개발 빌드 → 일시정지·종료 흐름, 가로 HUD·미니맵·이름표·카드, 와이파이 끊기·비행기 모드 입장(I-07), 봇이 두 화면에 같은 자리, 같이 뛰기 WebSocket·왕복 지연, 에어팟 빼기. 맥에 "0039 맥 할 일 해 줘"(2번 아이폰), "0033 맥 할 일 해 줘"(카메라 Swift 8건 → `fix/camera-swift` PR). 계획 0034 맨 위 "맥 할 일(11/17 헬스장 빌드)"·"헬스장 확인 목록"은 다음 헬스장 때 다시 씀(5번 책상 확인·7번만 남음)
4. **팀에 전할 것**(아래 "팀원에게 전할 것" 11/17): 지환 — 노션 S3 2줄 완료로 바꿈, 고스트는 서버 방에 안 넣음, S4·S5 남은 것과 에셋 11/20 17시. 상준 — BOT 배지, S6 Figma와 다른 것(누적 거리 문구·햅틱·D-26·미니맵 모양), 미니맵·이름표·카드 모양
5. **로컬 8001 서버 다시 켜기(민재 직접)**: 옛 코드로 떠 있음 → 끄고 `cd backend; ../.venv/Scripts/python -m uvicorn app.main:app --port 8001`(친구 탭을 에디터에서 보려면 `feat/friends`에서). Claude 확인용은 8002에 이 세션 브랜치로 띄웠다 끔(11/18도)
6. 계획 0037·0039 남은 것: 상준 문구·모양, 지환 E-02 연결, BL-37 복구 화면 I-04(S8, 서버 `GET /runs/active`·`discard`는 있음)
7. **iOS 최소 버전**: 앱 최소 15.0, Xcode 27 빌드가 iOS 16에서 바로 꺼진다는 포럼 보고(가설) → 17로 올릴지
8. **ADR 두 개**(FTMS 우선, 워치), 11/14 미팅 결과 적기, 피드백 문서·미팅 자료 커밋 여부(`midterm/` 폴더는 남의 유튜브 캡처라 안 됨), 홈(0032) 담당 지환과 정하기
9. 아이폰 확인 오래 남은 것: 한글 입력, 권한 거부 → 허용, 스플래시·온보딩
10. 헬스장(지환): 0024 6번, 걷기 절편, 걸음 없음 문턱 0.12, 센서 준비·카운트다운 남은 확인(노션 그 줄 메모), 에어팟 끼고 측정 예외 기록, 블루투스 지원 여부(nRF Connect), 워치 기록, 계기판 브래킷 사진
- 남은 작은 것: 기존 계정 만 14세 동의 기록 없음(출시 전 다시 동의), A-17 컨페티·스플래시 "아바타가 달려 들어옴"은 에셋 생기면, 이름표 쌓기는 겹치는 장면을 못 만들어 코드로만 확인(0041), 에디터가 섬을 불러오는 동안 실시간 연결이 한 번 끊겼다 붙음(아이폰에서도 나는지)
- **S3 메모(11/11 지환)**: 카운트다운·달리기 목표는 `MapScreen.Setup.Goal`, `POST /runs` goal에 `Setup.Goal.ToRequest()`(m·초). 섬 `MapLaunchApplier`가 입장 위치 적용 뒤 `MapLaunch.MarkApplied()`(고칠 때 이 줄 남기기)
- 확인 못 한 것: 처음 Play 때 안내 → 보정 화면이 버튼을 안 눌렀는데 넘어간 적 한 번(재현 안 됨)
- 헬스장 실험에 넣을 것: 0023 "데이터가 없는 곳"(1–15 km/h 1 간격 여러 명, 단계 20초 이상, 2 km/h 걸음 감지율, "속도 안 올림" 기준)과 아래 "속도 추정 다음 실험". 실험 전 메모(사람·키·몸무게·신발·기종·속도 순서·폰 위치) 빠뜨리지 않기(11/16엔 빠짐)

## Claude 작업 요령 (이번 세션에서 배운 것)
- **Figma 읽기**: 이 PC Chrome(Claude 확장 연결, Figma 로그인됨)에서 파일을 열고 `javascript_tool`로 `figma` 전역(플러그인 API). 맥 Chrome도 연결돼 있어 `list_connected_browsers`로 Windows(`Browser 1`)를 `select_browser`. 출력에 `@`·`=`이 많으면 "Cookie/query string data"로 막힐 수 있음(형식 바꾸기), 출력은 1500자쯤에서 잘림(프레임 하나씩)
- **Figma 그림 받기**: `exportAsync({format:'PNG', constraint:{type:'SCALE', value:3}})` → blob 다운로드. Chrome은 같은 탭의 두 번째 자동 다운로드부터 막음 → **새 탭에서 여러 장을 한 파일로 묶어 한 번에**(길이 머리 + 이름 + 바이트) 받고 Python으로 나눔
- **에디터 UI 누르기**: `NavigationSubmitEvent`를 `panel.visualTree.SendEvent`로(`e.target = 버튼`). 화면을 직접 띄우려면 `Scrun.App.AppRoot.Nav.StartFlow(new XxxScreen())` 뒤 `manage_camera` screenshot(파일은 `Assets/Screenshots`, 끝나면 지우기)
- **USS 주의**: 화면 UXML의 USS 규칙은 그 화면 안쪽에만 닿음(app-root 배경은 `App.uss`에). 페이드인 클래스는 첫 `GeometryChangedEvent` 뒤에 붙여야 transition이 돎
- **프로젝트 설정 저장**: `ProjectSettings.asset`은 `SaveAssetIfDirty`로 안 써짐 → `AssetDatabase.SaveAssets()`(다른 에셋도 써질 수 있으니 git 확인)
- **로컬 HTML 보기·PDF**: Chrome 확장은 `file://`을 못 엶 → `.venv/Scripts/python -m http.server 8765 --bind 127.0.0.1`(백그라운드)로 띄워 확인, 끝나면 끄기. PDF는 `chrome.exe --headless=new --no-pdf-header-footer --allow-file-access-from-files --print-to-pdf=<pdf> file:///<html>`(HTML에 `@page{size:1280px 720px}`·`.slide{break-after:page}`). PDF 화면은 확장으로 캡처 안 됨 → `pypdf`로 쪽수·글자 확인. 이 PC엔 poppler·PyMuPDF 없음
- **Figma 탭이 자주 멈춤**: 큰 `findAll`이면 45초 넘게 응답 없음 → 탭 다시 열고 프레임 하나씩 `children`만 읽기
- **Unity 테스트**: 에디터가 Play 중이면 테스트 시작이 막힘(민재에게 묻고 `manage_editor stop`). `execute_code`는 CodeDom(C# 6)이라 `$"..."` 일부·`Object` 모호함 주의. Play 중 값 보기는 `EditorApplication.update` 콜백으로 모아 `AppDomain.SetData`에 두고 다음 호출에서 꺼냄
- **다른 러너 화면 확인(11/16)**: `fake_runner.py`는 남의 상태를 안 찍음 → `scripts.fake_runner`의 `login`을 불러 0 km/h로 서서 `states`의 `v`·`d`를 실제 시각과 함께 찍는 작은 관찰 스크립트(backend 폴더에서 실행). 에디터 쪽은 `FakeSpeedSensor.Instance.SetTargetKmh`를 `EditorApplication.update` 콜백으로 시간표대로 바꾸며 `NativeSpeedSource.SpeedKmh`를 0.25초마다 기록 → 두 로그를 시각으로 맞춤. 앱 흐름은 `Nav.Push(new CourseDetailScreen("crs_lake_loop"))` → `enter` → `goal-next`·`mount-next`·`manual-next`(UXML에 `using`이 없어 `UQueryExtensions.Q<Button>(root, name, (string)null)`로)
- **11/16–17 배운 것**: 커밋 제목은 50자 이내(훅이 막음, 막히면 스테이지가 다음 커밋에 섞이니 확인) / Git Bash `sed -i`는 CRLF 파일을 LF로 바꿈 → `git ls-files --eol`로 확인, CRLF 파일은 Python으로 고침 / 앱 어셈블리(`Scrun.App.asmdef`)는 섬 스크립트(Assembly-CSharp)를 못 부름 → `RunLink`에 `Func` 고리 / UXML 주석에 `--` 쓰면 화면 전체가 안 불림 / C# 테스트 이름은 숫자로 시작 못 함 / 에디터 서버 주소를 잠깐 바꾸려면 EditorPrefs뿐 아니라 `ApiConfig.BaseUrl`도 / 강제 푸시·남의 프로세스 끄기는 권한 검사에 막힘 / `manage_camera` 스크린샷엔 UI Toolkit이 안 찍힘 → 요소 위치·글자를 코드로 확인
- **11/17 배운 것**: 공개 노션 API(`queryCollection`)가 403 → User-Agent를 바꿔 다시 부르는 건 권한 검사에 막힘. 대신 Chrome으로 공개 백로그 표를 열고 `javascript_tool`로 스크롤하며 줄 글자를 모음(BL 번호로 찾기) / Figma `figma` 전역은 파일을 연 뒤 몇 초 기다려야 생김, 노트는 `Note D-xx` 이름 노드 / 오라클 DB를 읽기만 하는 SSH도 권한 검사에 막힘(배포는 "배포해"면 통과) / Python `write_text`는 Windows에서 LF를 CRLF로 바꿈 → `read_bytes`·`write_bytes`로 고치고 `git ls-files --eol`로 확인 / 에디터 Play 확인: Enter Play Mode 옵션이 도메인 리로드를 안 해서 `ApiConfig.BaseUrl`(정적)을 바꾸고 `EditorApplication.EnterPlaymode()` → 홈 → `Nav.Push(new CourseDetailScreen(...))` → `enter` → `goal-next`·`mount-next`·`manual-next` → `FakeSpeedSensor.Instance.SetTargetKmh(9f)`. UI 포함 캡처는 `ScreenCapture.CaptureScreenshot(절대 경로)` 뒤 한 번 더 호출해야 파일이 생김. 끝나면 EditorPrefs·`ApiConfig.BaseUrl`을 8001로 되돌리기 / 봇·위치 확인은 같은 맵에 가짜 두 사람을 붙여 같은 순간 값을 비교(스크래치 `bot_observe.py` 방식)
- **한글 컴퓨터 이름**: Mono가 `localhost`를 못 풀어 ClientWebSocket 실패 → 에디터 서버 주소는 `127.0.0.1`(코드 기본값·저장값 자동 변환)

## 팀원에게 전할 것
- **11/17 밤(지환, 메시지 초안은 채팅)**: #61 리뷰 9건(꼭: Roboflow 이름 고침이 실측 채점을 망침, `field.py`가 애매해도 그럴듯한 값을 확정), 0045 승인·지환 담당, #62 머지 + #64(민재 수정: 사진 회전·메인 스레드 등, 지환 파일 `VerifyScreen`·`VerifyFlow`·`VerifyPhotos`·`FakeNative` 바뀜 → pull 먼저), #65로 응답 시각 UTC 한 곳에서 처리(지환 `retakeDeadline` 따로 안 고쳐도 됨), OCR 배포 뒤 측정은 민재가
- **11/17 저녁**: 지환 — 기록 조회 API(목록·상세·공유)와 고스트 API는 민재가 가져가 DB 연결·배포 끝(#63, 계획 0043). 화면(결과 E-12·기록 F-01–F-04·고스트 재생)은 지금 응답 모양 그대로 만들면 됨. 노션 작업 표 두 줄 담당 바꿈. 계획 번호 0042는 지환 것, 민재 S7은 0044. #62는 머지했고 리뷰에서 나온 것은 민재가 #64로 고침(회전·멈춤 등, 계획 0038 "머지 뒤 고친 것")
- **11/17 새로**: 지환 — 고스트(BL-24)는 맵 인원에 안 넣음·서버 방에 러너로 넣지 않음(`docs/api/realtime.md` "인원"), S4 남은 것(E-02·E-04–E-10 앱 화면, ADR-0012 OCR 엔진)과 S5 범위 조정. 상준 — BOT 배지 모양·봇 이름 문구(지금 "BOT · 페이스 6:40", Figma D-10 칩), S6에서 Figma와 다르게 한 것: 카드 "누적 거리" → "이번 러닝 거리", 달리는 중 햅틱 없음(D-25 노트와 다름), 친구 근접 알림 D-26 뺌(S7에 다시), 미니맵은 실제 코스 모양. 미니맵·이름표·카드 모양 다듬기
- **11/16 새로**: 지환 — 카메라 노출 Swift 줄을 민재가 가져가 끝남(계획 0033: `Native.Camera.ShootAsync` → 경로 3장, 에디터 가짜 카메라는 Fake Native 창), 서버 OCR은 사진 여러 장(브래킷)·큰 용량(한 번 6.6–8.0 MB) 받는 모양으로, #49 리뷰 곧. 상준 — 11/14 미팅 4번 파트 결과, 3-1 서비스 아이디어(그룹런·응원·공간 음향·아바타 반영·시점 버튼) 화면 기획 필요한지
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
