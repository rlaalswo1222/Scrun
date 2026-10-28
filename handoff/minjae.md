# 민재 인수인계 (2026-10-29)

민재(git `minjae`)의 개인 작업 메모. 새 채팅은 `CLAUDE.md`(규칙·문서 지도)와 이 파일(지금 하던 일)을 먼저 읽는다. 팀원은 읽기만, 고치지 않는다. 영역별 세부는 CLAUDE.md의 문서 지도를 따라 필요한 것만 읽는다.

## 지금 하던 일 (10/29 새벽 헬스장 뒤)
- **방향: 아이폰 전용 앱 검토** — 계기판 위 아이폰(충격·걸음 수) + 에어팟, 폰 안에서 실시간 속도(계획 0010, `docs/plans/0010-iphone-live-speed.md`). 기준 구현 `speedlab/fusion_live.py` = `ios/SpeedRecorder/SpeedEstimator.swift`(같은 원본에서 차이 중앙 0.001 km/h, 걷기·달리기 판단 100% 일치 확인)
  - 1회 시험(04:01, 민재, 같은 날 보정 7·8·9·10 → 자유 달리기 3분): 달리기 **폰만 0.12**, 에어팟 포함 0.16 km/h, 걷기 0.29(4 km/h +0.53), 속도 바꾼 뒤 따라오기까지 5에서 10초 → `docs/speed/live_iphone.md`(팀원 jhwn223 분석). 같은 날·범위 안·짧음이라 낙관적. 내린 직후 과대 추정 없었던 건 에어팟 덕이 아님(폰만도 없음, 빠른 구간 짧고 걷기 휴식)
  - 보정 저장·재사용 구현(커밋 72a796f): 보정한 기록이 끝나면 폰 `calibration.json`, "저장된 보정으로 바로 자유 달리기". 04:01 보정을 원본에서 다시 만든 `data/runs/20261029-040156/calibration.json` → **맥에서 빌드 + 이 파일을 앱 Documents에 넣기 남음**(맥 Claude에 줄 지시는 대화 기록·계획 0010 5번)
  - **다음 방문**: 저장된 보정으로 보정 없이 = 다른 날 오차, 10에서 11 km/h 1에서 2분 → 7로 내려 1분(S22에서 틀렸던 상황), 범위 밖(11 이상, 6 이하 달리기). 에어팟을 속도 계산에 남길지는 다른 날 결과 보고
- **에어팟이 주는 것**: 머리 가속도(중력 뺀 값)·중력 방향·자이로, 약 45에서 50 Hz. 앱은 전체 가속도 크기로 "0.5 g 아래 시간"(걷기·달리기 판단, 걷기 0, 달리기 100 ms 이상)과 "흔들림 폭"(95% − 5%, 달리기 속도에 ×2.598, B 기록에서 배운 값)을 씀. 달릴 땐 낮게 떠서 진짜 공중 시간은 못 잼(제자리 점프에서만 됨). B에선 0.37 → 0.21, 민재에선 도움 안 됨(폰이 틀린 구간이 없었음). 블루투스 끊김은 5에서 20초 덩어리(안내 음성·와이파이 탓 아님, 헬스장 혼잡 또는 몸 가림 가설) → 끊기면 폰만으로
- **10/29 헬스장 S22 결과**(`docs/speed/README.md`, `fusion.md`): 민재 다른 날 0.52(+0.51 치우침), 팀원 B 같은 날 0.55. 벨트만 주파수는 2·4 km/h도 보이나 걸을 때 안 보임 → **주파수 안 쓰기로 결정**. 계획 0009(공통 기울기·인증 사진 보정·계단식 묶음) 효과 없음, 걷기·달리기 가르기만 됨, "+0.5 빼면 0.28" 철회. 오차의 주인은 잡음이 아니라 구간마다 흘러가는 충격
- **계기판 사진 OCR 첫 시험**(`docs/product/console_ocr.md`, `speedlab/ocr/` — **커밋 전**): LLM API는 비용 때문에 제외(민재). 사진 2장(같은 화면) 10칸: 일반 OCR 0, ssocr 0에서 2, Tesseract 7세그먼트 학습 파일 최고 5. 병목은 노출(LED 번짐·꺼진 888). 직접 획 판별(`speedlab/ocr/seg7_grid.py`)은 보통 노출에서 0/10 → 노출 낮춘 사진이 전제. 앱 카메라로 ISO 최저·셔터 1/60초 이상·브래킷 촬영 가능. **다음: S22 밝기 막대 내려 찍은 사진(다른 숫자 화면 포함) → 같은 비교**. 라벨("시간", "km")은 일반 OCR이 읽음 → 칸 뜻은 라벨로. 계기판 사진은 유리에 찍는 사람이 비쳐 저장소에 안 올림
- **미기록**: B 이름·키·몸무게·신발, 민재 키·몸무게. 노트북 `server.py`가 켜져 있을 수 있음(이번 아이폰 시험엔 불필요)
- **Git**: 작업 브랜치 `feat/airpods-treadmill`(팀원 브랜치들 포함, 오늘 커밋 다 있음) → **develop으로 PR 아직 안 만듦**. 브랜치 규칙(develop에서 feat/... → develop PR) 커밋됨. 할 일: GitHub 기본 브랜치 develop·보호 규칙, 팀원 GitHub 초대
- **와이어프레임 v1 초안** `docs/product/scrun_wireframe_v1.html`(커밋 안 함, 사용자가 더 고칠 예정). 결정할 것: 달리는 중 속도 ±(ADR-0006 충돌), 보정 건너뛰기, 헬스장 알아내는 법, 화면 방향, 소셜 로그인(구글 로그인은 스토어 없이 가능, OAuth 테스트 사용자·SHA-1 주의), 방 실시간 여부
- 관련 연구 `docs/research/treadmill_sensing_papers.md`. 다음 후보(선택 대기): 이슈 보드 / 반복 작업 스킬 / SoundTrackDB 몸무게·헬스장·피로 분석

## 프로젝트 한 줄
「스크런」: 폰(러닝머신 계기판에 올려놓기) + 무선 이어폰 센서만으로 러닝머신 속도를 추정하고, 그 속도로 아바타가 가상 섬 코스를 달리며 여러 명이 함께 달리는 앱. Unity로 통일(ADR-0001), 서버 FastAPI(ADR-0002), 개발 방식 walking skeleton.

## Unity 본 앱 (상세 일지 `docs/unity/unity_map.md`)
- Unity 6.6 (6000.6.7f1), 프로젝트 `unity/TestRun`(URP), 빌드 대상 Android. 팀 전원 Unity 경험 없음
- MCP for Unity(CoplayDev v10.3.0, `Packages/manifest.json` 고정): Unity 메뉴 Window → MCP for Unity → Start Server(`127.0.0.1:8080`). Claude Code엔 사용자 범위로 등록됨. 채팅에 도구가 안 뜨면 `unity/tools/`(README)의 curl 스크립트 사용: `ucs.sh`(C# 실행, CodeDom = C# 6), `umenu.sh`, `ucon.sh`, `rebuild.sh`, `fast.sh`, `shoot.sh`
- 현재 상태: 섬 월드 Island01(도로망 16.4km, 지역 5개, 손그림풍 식생·하늘·물, 페이스 봇·고스트·함께 달리는 무리, 품질 단계 ScrunQuality). 설계 원본 `Assets/Scrun/Course/IslandLayout.json`. 메뉴 Scrun → Build Island Scene(약 3분), Rebuild Vegetation Meshes (fast)(약 1분). 캐릭터는 Mixamo Y Bot(임시)
- 폰: S22에서 Medium 30fps(High도 30, 60fps 여유는 미측정). 본 앱 APK = MCP `manage_build`(IL2CPP·ARM64, 첫 빌드 15분). adb는 Unity 설치 폴더 `Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe`, Git Bash에선 `MSYS_NO_PATHCONV=1` (그때 로컬 경로는 Windows 형식)
- 함정: `SaveMesh`는 Mesh API로 다시 채울 것(CopySerialized면 옛 메쉬 남음·정점 색 쓰레기), 정점 색은 선형 값(`.linear`), `Material.SetColor`는 sRGB→선형 자동 변환(또 `.linear` 하면 이중), 임시 카메라로 찍으면 지형 안 나옴 → 메인 카메라 이동, execute_code는 `AssetDatabase.DeleteAsset` 막힘, `runInBackground = true` 유지
- 남은 큰 것: 손그림 텍스처 에셋(땅·바위·건물), 실제 캐릭터(체형 변화 가능해야 함), 0~3 km/h·20 km/h 발 미끄러짐, 디버그 HUD 교체, 60fps·발열 측정, 앱 화면 흐름(UI Toolkit)

## 앱 기획 결정 (2026-10-27)
- 유저플로우 v2: `docs/product/scrun_userflow_v2.html` (사용자 브라우저에선 claude.ai 링크가 안 열림 → 로컬 HTML로 줄 것). 유스케이스 `docs/product/scrun_usecases_v1.md`
- 결정: 로그인 필수, 친구 기능 필수, 달린 뒤 계기판을 앱 카메라로 찍어 인증(인증마크·보상), 연속 일수·배지, 같이 뛰는 방 이름표에 인증마크·연속 일수, 자주 뛰면 탄탄·안 뛰면 둥근 체형 변화, 키는 아바타 크기에만(몸무게 입력 없음). 판정은 서버
- 미정: 같이 뛰는 방이 실시간인지, 인증 허용 오차·촬영 시한, 계기판 7세그먼트 숫자 읽기(사진 20~30장 필요), 화면 방향(제안: 달리기 관련 가로, 나머지 세로)
- 4주 계획(10/27~11/22): 1주 git·요구사항·유저플로우·아키텍처 / 2주 와이어프레임·끝-끝 뼈대·센서 / 3주 보정·에어팟·서버·디자인 / 4주 친구 고스트·성능·사용성 테스트
- 개발 순서 제안: git 저장소(완료) → 와이어프레임(초안 v1) → 계기판 OCR·속도 추정 검증 → 아키텍처·데이터 모델·API → 끝-끝 뼈대. Notion을 작업 보드로 쓰는 것도 제안함(답 대기)

## 문서 작업
- 교수님 미팅자료 `docs/meeting/`: `prof_body.html` 수정 → `python build_prof.py .` → `professor-meeting.html`. 사용자 최종본 `미팅자료_최종본.html` 위치 못 찾음(PDF 변환 보류)
- HWP는 한컴 없이 `docs/hwp/` 스크립트로 수정
- Zwift 벤치마킹 `docs/research/zwift_benchmark.md`(내부 참고용, 보고서에 "Zwift 분석" 표현 금지)
- 중간발표 10/30(15분): 실측 수치만, Zwift는 "유사 서비스"로만
- 재료비 1차(10/19, Claude Max 5x 2개)·멘토링 보고서 2차: 완료
- 재료비 2차(11월 공지 대기): AI 3D 생성 1개월 + Unity 에셋 + Claude 재결제. 월구독은 11/20 17시 전 결제, 에셋 구매가 "소프트웨어 일회성 구매"로 인정되는지 조교 확인
- 실사 영상 가림 처리 등은 보류(메모리 sfm-followups)
