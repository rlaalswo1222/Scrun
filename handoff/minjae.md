# 민재 인수인계 (2026-10-28)

민재(git `minjae`)의 개인 작업 메모. 새 채팅은 `CLAUDE.md`(규칙·문서 지도)와 이 파일(지금 하던 일)을 먼저 읽는다. 팀원은 읽기만, 고치지 않는다. 영역별 세부는 CLAUDE.md의 문서 지도를 따라 필요한 것만 읽는다.

## 지금 하던 일 (10/28 밤 기준)
- **오늘 밤 팀원과 헬스장(10/28)**: SpeedLab APK(20:17) S22에 설치됨, 원래 보정값 백업 `data/speedlab/speedlab_calib_phone_20261028_2020.json`. 폰 화면 확인은 못 함(잠김) → 보정 줄에 "속도 = −3.94 + 2.77×ln(충격) + 0.0505×spm" 보이는지 확인. 순서: 맨 처음 벨트만 2·4 km/h 각 30초(사람은 옆 발판, 앱 실제 속도 버튼으로 맞춤) → 민재: 걷기 기록(1→6→1) → 검증(7→10→7→10, 10/28 보정값 그대로 = 다른 날 오차) → 팀원 각자: 걷기 → 보정 → 검증(팀원 보정이 폰 보정을 덮으니 민재 먼저). "폰 들었다 놓기" 실험은 사용자가 빼기로 함. 메모: 이름·키·몸무게·러닝머신 기종·최저 속도·폰 위치 사진·모드 시작 시각·실수
- **10/29 에어팟 공중 시간 (계획 0008, 브랜치 `feat/airpods-flight` 푸시됨)**: SpeedRecorder에 `airpods_grav` 기록·"제자리 뛰기" 버튼 추가(서버 쪽 확인함, Swift는 맥에서 아직 한 번도 빌드 안 됨). 다음: 맥북에서 브랜치 받아 Xcode 빌드·아이폰 설치(ios/README) → 집에서 제자리 뛰기(폰 가슴 고정, 메트로놈 170) → 분석 스크립트 `speedlab/exp/airpods_flight.py` 작성. 원리: 걸음 수가 같아도 빨라지면 발 닿는 시간 줄고 공중 시간 늚(SoundTrackDB 1 km/h당 −15/+12 ms) → 에어팟이 공중 시간, 폰이 충격
- **10/29 새벽 헬스장 끝 + 계획 0009 평가**: M 다른 날 0.52(+0.51 치우침), B 같은 날 0.55, 주파수는 걸을 때도 안 보여 **안 쓰기로 결정**, 걷기 걸음 수 두 사람 비슷(다리 길이 비슷해서일 수 있음). B 에어팟 러닝머신: 충격 + 흔들림 폭 0.37 → 0.21(1회). 계획 0009(`speedlab/exp/fusion.py`, `docs/speed/fusion.md`): 걷기·달리기 가르기는 됨, 공통 기울기·인증 사진 보정·계단식 묶음은 효과 없음, "+0.5 빼면 0.28"은 철회. 다음: 에어팟 지표를 다른 사람·날로 확인, 다리 길이 다른 사람 걷기, 같은 속도 처음·중간·끝 반복 순서. 할 것: B 이름·키·몸무게 기록, SpeedRecorder 백업 CSV 중복 줄 버그, 보정 파일(앱이 못 쓰던 것) 지우고 팀원 보정으로 새로 생김 — 민재 10/28 보정은 `data/speedlab/speedlab_calib_phone_20261028_2020.json`. 브랜치 `feat/airpods-treadmill`에 문서·스크립트·데이터 **커밋 전**
- **10/29 집 제자리 뛰기 끝(팀원 jhwn223 기록·분석, 브랜치 `feat/airpods-flight-analysis`)**: 에어팟 프로 3은 48.9 Hz, 공중 순서 구분, 낮게·보통 아이폰 대비 약 −20 ms 일정. 높게 ±38 ms는 가슴의 아이폰이 공중에서 0.4 g까지 튄 탓으로 보임(이륙만 퍼짐, `airpods_flight.md`에 추가, 그림 `0008_high_takeoff.png`). 이 분석 추가·스크립트 utf-8 수정은 팀원 브랜치 위에서 **커밋 전**. 나머지(정확도·달리기)는 헬스장에서: 계획 0008 "다음: 러닝머신"
- **돌아오면 할 일**: 폰에서 기록 pull → 판정 3가지: ① 다른 날 오차(0.5 이내 / 0.5에서 1 / 1 이상 → 다음 방향, 표는 대화 기록 참고) ② 걸을 때 모터 진동 보이나(벨트만 2·4와 비교) ③ 420Hz에서 충격 폭(발 닿는 시간 대용)이 오차를 줄이나. 같은 속도 올림·내림 비교로 "내린 직후 충격이 20에서 30초 천천히 줄어듦" 가설 확인. 팀원 기록으로 보정 구간 수(1, 2, 3)별 오차도
- **속도 추정 현황**: 제약은 ADR-0006(폰 그냥 올려놓기, 입력 없음, 마이크·FTMS·반자동·버튼 감지 제외, 매번 보정은 최후). 공개 데이터 SoundTrackDB(59명, 계기판 위 폰) 재분석 `docs/speed/soundtrackdb.md`: 충격만 3개 보정·범위 안 0.44 km/h(0.5 이내 52%), 걸음 수·소리·폰으로 잰 충격 모양은 도움 안 됨, 영상 정답 발 닿는 시간만 0.36으로 개선. 같은 속도 두 번 뛰어도 0.6 차이 → 우리 1명 결과(0.28에서 0.43)는 낙관적일 수 있음. 남은 입력 없는 후보: 걷기 중 모터 진동으로 기준점, 폰 기울기 보정, 인증 사진으로 계속 학습. 착지 뒤 울림이 걸음 사이를 채움(모터 주파수 실패 이유 중 하나, README). 딥러닝·소리→발 닿는 시간 AI는 보류
- **SpeedLab 앱**: 계획 0003(안내 모드), 0004(걷기 1→6→1, 검증 피라미드), 0005(실시간 = 충격 + 걸음 수, est.csv에 imp_kmh·impcad_kmh). SoundTrackDB에선 걸음 수 기울기까지 그 사람 점으로 맞추면 오히려 나빠짐 → 오늘 기록 보고 "걸음 수 기울기는 고정값"으로 바꿀지 결정. /code-review 아직 안 돌림
- **와이어프레임 v1 초안** `docs/product/scrun_wireframe_v1.html`(커밋 안 함, 사용자가 더 고칠 예정). 결정: 보정은 첫 실행이 아니라 홈 → 코스(방) 선택 뒤 앱이 필요 여부 판단(헬스장 처음·폰 기종 바뀜·인증 사진과 계속 안 맞음). 결정할 것: 달리는 중 속도 ± 버튼(ADR-0006과 충돌), 보정 건너뛰기 허용, 헬스장 알아내는 법(위치 권한?), 화면 방향, 소셜 로그인, 방 실시간 여부. 유저플로우 v2·유스케이스(UC09 1점 입력, UC12 ±)는 아직 옛 순서 → 와이어프레임 확정 뒤 맞추기
- **GitHub** https://github.com/rlaalswo1222/Scrun (공개). README "처음 받았을 때"(Poly Haven = `unity/tools/polyhaven_fetch.py` 검증됨, Mixamo = 각자 받기, 섬 메시 = Build Island Scene). 커밋 규칙 `docs/git_convention.md` + 검사기 `tools/githooks/commit-msg`. 인수인계는 사람별 `handoff/<git user.name>.md`. 10/28 `develop` 브랜치 만들어 푸시, 브랜치 규칙(develop에서 feat/... → develop PR, 발표·출시 때 main) git_convention·CLAUDE.md에 반영(커밋 전). 할 일: GitHub 기본 브랜치 develop으로·main/develop 보호 규칙. CLAUDE.md에 팀 규칙(출시 기준, 보고서 규칙, 문서화 순서, 이미 정한 방향, 팀 git 규칙) 반영. 남은 것: 팀원 GitHub 아이디 받아 초대, 새로 받은 저장소에서 Unity 열기 확인(섬 다시 만들기 미검증)
- 관련 연구 정리 `docs/research/treadmill_sensing_papers.md`(CaNRun, SoundTrack, VibRun 원고 직접 읽음, Hung 2004, 폰 앞카메라 2026). 폰 앞카메라는 아직 검토 안 한 단서
- 다음 후보(사용자 선택 대기): 이슈 보드 / 반복 작업 스킬(기록 가져오기·APK 빌드·회고) / SoundTrackDB 몸무게·헬스장·피로 분석
- 멘토 권장 AI 활용 흐름 중 남은 것: 4주 계획을 이슈·보드로(UC·ADR 번호로 연결), 스킬

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
