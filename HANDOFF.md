# 새 채팅 인수인계 (2026-10-28)

새 채팅은 `CLAUDE.md`(규칙·문서 지도)와 이 파일(지금 하던 일)을 먼저 읽는다. 영역별 세부는 CLAUDE.md의 문서 지도를 따라 필요한 것만 읽는다.

## 지금 하던 일
- 10/28: 헬스장 속도 실험(SpeedLab 앱) → 결론과 다음 할 일은 `docs/speed/README.md`, 결정은 ADR-0004·0005
- 10/28: 계획 0002(달리는 중 모터 진동 줄 살리기: 평균 발소리 빼기·빗살 필터) → **실패**, 기존 기록으로 PC 분석. 남은 후보는 FTMS 확인, 걸음 수 × 충격 블라인드 검증, 마이크(모터 소리)
- 10/28: 계획 0003 SpeedLab 안내 모드(보정·검증, 버튼 없음) 구현 + 계획 0004 걷기 기록 모드(3에서 6 km/h, 기록만), 안내 구간 모두 20초 → 에디터 확인, `unity/Builds/SpeedLab.apk` 빌드(20:17, 걷기 1→6→1, 검증 7→10→7→10 피라미드, 실시간 속도 = 충격 + 걸음 수 계획 0005). **폰 설치·확인 남음**(S22 미연결). 10/28 저녁 팀원과 헬스장: 맨 처음 벨트만 2·4 km/h 각 30초(사람은 옆 발판) / 사용자 = 걷기 → 검증(10/28 보정값 그대로, 다른 날 오차) / 팀원 = 걷기 → 안내 보정 → 검증. 팀원 보정이 폰 보정 파일을 덮으므로 사용자 먼저, 원본으로 PC 재계산 가능
- 10/28: 사용자 판단: 마이크(헬스장 소음)·반자동 입력·FTMS는 제외. 차별성 = 외부 장비·입력 없이 추정. 보정은 사람 × 헬스장마다 처음 한 번
- 10/28: 문서 구조 정리 + CLAUDE.md + ADR 5개 (계획 `docs/plans/0001-docs-restructure.md`)
- 다음 후보(사용자 선택 대기): 속도 앱에 걸음 수 × 보폭(충격) 모델 + 안내형 보정 / git·GitHub 저장소(아직 없음, 팀원이 아무것도 못 보는 상태) / 이슈 보드 / 반복 작업 스킬(기록 가져오기·APK 빌드·회고)
- 멘토 권장 AI 활용 흐름(계획→검토→구현→리뷰, ADR, 공통 문서 공간, 이슈 연결, HTML 설명 자료, 스킬, 필요한 문서만 읽기, 회고) 중 2·3·5번 적용 완료. 남은 것: 1 GitHub를 공통 공간으로(사용자 동의·저장소 이름·팀원 GitHub 아이디 필요, `data/`·Unity `Library/`·빌드·큰 에셋은 제외), 4 4주 계획을 이슈·보드로(UC·ADR 번호로 연결), 6 스킬
- 폰(S22) SpeedLab 상태: 보정 파일에 10/28 달리기 7, 8, 9, 10 km/h 충격 세기가 PC에서 계산돼 들어가 있음(같은 거치 위치 기준). 실험 원본은 `data/speedlab/SpeedLab/`

## 프로젝트 한 줄
「스크런」: 폰(러닝머신 거치대) + 무선 이어폰 센서만으로 러닝머신 속도를 추정하고, 그 속도로 아바타가 가상 섬 코스를 달리며 여러 명이 함께 달리는 앱. Unity로 통일(ADR-0001), 서버 FastAPI(ADR-0002), 개발 방식 walking skeleton.

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
- 개발 순서 제안(답 대기): git 저장소(아직 없음) → 와이어프레임(로컬 HTML) → 계기판 OCR·속도 추정 검증 → 아키텍처·데이터 모델·API → 끝-끝 뼈대. Notion을 작업 보드로 쓰는 것도 제안함(답 대기)

## 문서 작업
- 교수님 미팅자료 `docs/meeting/`: `prof_body.html` 수정 → `python build_prof.py .` → `professor-meeting.html`. 사용자 최종본 `미팅자료_최종본.html` 위치 못 찾음(PDF 변환 보류)
- HWP는 한컴 없이 `docs/hwp/` 스크립트로 수정
- Zwift 벤치마킹 `docs/research/zwift_benchmark.md`(내부 참고용, 보고서에 "Zwift 분석" 표현 금지)
- 중간발표 10/30(15분): 실측 수치만, Zwift는 "유사 서비스"로만
- 재료비 1차(10/19, Claude Max 5x 2개)·멘토링 보고서 2차: 완료
- 재료비 2차(11월 공지 대기): AI 3D 생성 1개월 + Unity 에셋 + Claude 재결제. 월구독은 11/20 17시 전 결제, 에셋 구매가 "소프트웨어 일회성 구매"로 인정되는지 조교 확인
- 실사 영상 가림 처리 등은 보류(메모리 sfm-followups)
