# Unity 작업 일지 (2026-10-26 ~ 10-27, HANDOFF에서 옮김)

5. **Unity 시작 (2026-10-26)** — 팀 전원 Unity 경험 없음.
   - Unity 6.6 (6000.6.7f1) 설치. 모듈: Web·Windows·**Android(OpenJDK·SDK·NDK 포함, 10/27 설치)**. 현재 활성 빌드 대상 = **Android**
   - 프로젝트: `unity/TestRun` (Universal 3D/URP 템플릿). 사용자가 ScrunApp 대신 TestRun으로 만듦
   - Unity MCP: 커뮤니티판 MCP for Unity(CoplayDev) v10.3.0을 `Packages/manifest.json`에 고정. 공식 Unity MCP는 Unity Cloud + AI 유료 구독 필요해서 제외. `uv` winget으로 설치함
   - 서버: Unity 메뉴 Window → MCP for Unity → Start Server (HTTP, `127.0.0.1:8080`, 로컬 전용 확인함). Unity를 끄면 연결 끊김
   - Claude Code 등록(2026-10-28 사용자 범위로 옮김): `claude mcp add -s user --transport http UnityMCP http://127.0.0.1:8080/mcp`. 로컬 범위로 두면 VS Code 확장이 프로젝트 경로를 소문자 `c:/`로 기록해 CLI의 `C:/` 설정을 못 읽음 → VS Code 채팅에서 MCP가 안 보였던 원인. Notion MCP(`https://mcp.notion.com/mcp`)도 사용자 범위로 등록·인증 완료. 사용자가 마법사에서 모든 클라이언트를 체크해서 VS Code mcp.json·Codex·Copilot CLI에도 항목 들어감(무해)
   - **1차 완료 (2026-10-26)**: `Assets/Scrun/` — Scripts(`ISpeedSource`, `FakeSpeedSource` ↑↓±0.5/Space 정지, `CircleTrack` 400m 원형·거리→위치/방향, `RunnerController` 누적 거리, `FollowCamera`, `SpeedHud` OnGUI 임시), Editor(`TestTrackSceneBuilder` = 메뉴 Scrun → Build Test Track Scene, 씬 `Assets/Scrun/Scenes/TestTrack.unity` 덮어 생성·빌드 목록 0번). 플레이 확인: 10 km/h 약 6초에 16.7 m 이동, HUD 한글 정상
   - MCP 도구가 VS Code 채팅 세션에 안 뜨는 경우(서버를 세션 시작 후 켰을 때 등): `bash unity/tools/umcp.sh tools/call '{"name":"read_console","arguments":{}}'` 처럼 curl로 직접 호출 가능. execute_code는 CodeDom(C# 6) → `UnityEngine.Object` 전체 이름, 보간 문자열 대신 + 연결
   - **2차 완료 (2026-10-26)**: Mixamo Y Bot + 동작 7개. 원본 `unity/mixamo/`, 프로젝트 `Assets/Scrun/Characters/YBot/` (파일명 `Y Bot@동작.fbx`, Idle만 With Skin)
     - 받는 법: Claude in Chrome(VS Code 패널에서 연결) → mixamo.com 페이지에서 JS로 내부 API(`/api/v1/products`, `/animations/export`, `/characters/{id}/monitor`, 헤더 `X-Api-Key: mixamo2` + localStorage 토큰) 호출 → a 태그 클릭으로 다운로드. Chrome "자동 다운로드" 허용 필요. 결과에 서명 URL이 나오면 차단되니 URL은 출력하지 말 것
     - Y Bot id `4f5d21e1-4ccc-41f1-b35b-fb2547bd8493`. 계정 기본 캐릭터는 Remy(안 바꿈). Downloads에 사용자가 받은 `Remy.fbx` 있음
     - Mixamo In Place가 Sprint엔 적용 안 됨 → `MixamoImportSetup`(AssetPostprocessor)에서 XZ를 루트 모션으로 빼고 `applyRootMotion=false`로 버림. 모든 동작 제자리 확인(엉덩이 ±4cm)
     - Blend Tree 기준점은 `GaitAnalysis`가 클립을 240Hz로 샘플링해 "디딘 발의 엉덩이 대비 속도 중앙값"으로 자동 측정(몸통 이동량 기준은 틀렸음). 현재 Walking 3.4 / Jogging 5.2 / Slow Run 9.3 / Standard Run 13.6 / Fast Run 17.6 / Sprint 18.2 km/h. 최고 기준점 초과 시 재생 속도 배율(`RunnerAnimator`)
     - Jogging 원본엔 걸음 주기 3번 → 왼발 착지~다음 왼발 착지 1주기로 자동 자름. 모든 클립 cycleOffset = 왼발 착지(박자 맞춤)
     - 메뉴: Scrun → Build Test Track Scene 하나로 Animator까지 생성 (캐릭터 새로 넣었을 땐 먼저 Reimport Mixamo Characters)
     - 바닥 기준 무늬: 트랙 1m 가로줄·흰 차선, 잔디 1m 체커, 5m 기둥(50m마다 높게). 텍스처는 빌더가 절차 생성(`Assets/Scrun/Textures`)
     - `FootSlipProbe`(개발용): 디딘 발 미끄러짐 중앙값/평균. 결과(중앙값): 기준점에서 ±6% 이내, 기준점 사이 4.5 km/h 19%·7 km/h 21% (조깅~Slow Run 사이 간격이 넓음 — 러닝머신 주 사용 구간). 평균은 조깅·Slow Run 클립 자체가 디딤 중 미끄러져 큼
     - **남은 개선**: ① 기준점 사이 속도 보정 곡선(파라미터→실제 발 속도 측정 후 역보정) 또는 6~8 km/h 동작 추가 ② 발 IK 고정(Animation Rigging)으로 클립 자체 미끄러짐 제거
     - Unity `PlayerSettings.runInBackground = true`로 바꿈(꺼져 있으면 Unity 창이 비활성일 때 Play가 멈춰 MCP 테스트 불가). 모바일 빌드엔 영향 없음
   - **마지막에 고칠 것(사용자 요청)**: ① 0~3 km/h(Idle↔Walk 블렌드)에서 발이 심하게 미끄러져 보임 ② 20 km/h에서 발 동작이 이상함(최고 기준점 Sprint 18.2 초과 구간 — 재생 배율·Fast Run/Sprint 블렌드 확인)
   - **맵 1단계 완료 (2026-10-26)**: 가상 코스 "노을 호수 코스"(사용자 선택: 가상 코스 직접 제작 + 세미 리얼). 설계 미리보기 `docs/unity/course_preview.html` (아티팩트 https://claude.ai/artifact/2ASihiWwj1VehRnZyqCoBo, 사용자 브라우저에선 안 열렸음)
     - 메뉴 Scrun → Build Course Scene → `Assets/Scrun/Scenes/Course01_SunsetLake.unity` (3000 m 루프, 반시계)
     - `Track`(추상) ← `CircleTrack`, `SplineTrack`(자체 centripetal Catmull-Rom + 0.5m 누적거리표). Unity Splines 패키지는 비활성 창에서 임포트가 안 돼 제거하고 자체 구현
     - `CourseRoadBuilder`: 8m 아스팔트·흰 차선·중앙 점선·주황 발광선(emission)·연석·자갈 갓길, 200m 청크. `CourseTerrainBuilder`: 도로 거리장 → 30m 평지, 언덕, 750m 밖 산맥, 호숫가 구간(0.47~1.16km) 왼쪽을 따라 긴 호수, 1024 색 지도 + 잔디 디테일 맵. 노을 절차 하늘·햇빛 18°·지수제곱 안개·ACES/블룸/색보정/비네트, 출발 아치
     - 공용: `ScrunEditorUtil`(재질·절차 텍스처·메쉬 저장), `RunnerRigBuilder`(러너+카메라+HUD)
     - 알려진 문제: 호수 수면이 노을 하늘을 그대로 반사해 노란 판처럼 보임(물결 노멀맵 있는 물 셰이더 필요). 잔디가 맨바닥이라 밋밋함 → 식생 단계
     - MCP 팁: 임시 카메라로 찍으면 지형이 안 나옴(원인 미확인) → 메인 카메라를 옮겨 찍을 것. 공중 시점은 안개 끄고 촬영
   - **맵 2단계(무료 에셋) (2026-10-26)**: 사용자 결정 "일단 무료, 나중에 유료(식생 팩·캐릭터) 쓸 것"
     - Poly Haven CC0 1k 21종(141MB) → `Assets/Scrun/ThirdParty/PolyHaven/`(README에 출처·라이선스). 텍스처 6(asphalt_02, grass_ground, gravelly_sand, concrete_floor_02, coast_sand_01, rocky_terrain_02), 모델 15(덤불·고사리·꽃·바위·그루터기·쓰러진 나무). API는 User-Agent 헤더 필수
     - Poly Haven 나무·잔디 모델은 수십만~천만 폴리곤이라 제외 → 나무 없음(숲길 구간 비어 있음, 유료 팩 대상)
     - `PolyHavenImportSetup`(노멀/선형 자동 설정), `InstancedScatter`(런타임, 칸별 시야·거리 컬링 + LOD0/LOD2 전환, GPU 인스턴싱), `VegetationBuilder`(변형·LOD별 메쉬 굽기, 잎 색+알파 합성, 코드 생성 풀 묶음 4종, 배치 규칙). 아스팔트는 실사 위에 차선 합성(+노멀)
     - 결과: 약 17만 개(풀 대부분). 에디터 Play 27~37fps(PC) → 폰 품질 단계 필요(`InstancedScatter.distanceScale`, 풀 밀도)
     - 남은 문제: 나무 없음, 호수 노란 판(물 셰이더), 갓길 자갈이 붉은 갈색으로 보임, 풀 띠 밖은 맨 지형, 바람 흔들림 없음
   - **섬 월드 설계 (2026-10-26)**: 사용자 "Watopia랑 똑같이" → 구조만 따르고 지도·지명·랜드마크는 새로(저작권). 선택: 보편적 자연 섬, 도로 15~20km·지역 4~5개
     - 설계 원본 `Assets/Scrun/Course/IslandLayout.json`(scale 1.25, 노드 8·엣지 11·코스 4, 지역 5: 등대 항구/노을 호수/거목 협곡/풍차 고원/동쪽 해안 절벽 + 400m 트랙). 도로망 16.4km, 코스 6.7/8.8/9.7/10.9km
     - 미리보기 `docs/unity/island_preview.html` (아티팩트 https://claude.ai/artifact/A1TCAEHbkAYe3vCei9BJZp). 계산 스크립트는 스크래치(island_calc.py) — Unity 빌더가 같은 JSON을 읽게 할 예정
     - 구현 순서: ① 도로망 구조(엣지+교차로+코스 경로, 러너는 코스 거리만) ② 섬 지형(해안 절벽·바다·호수·협곡, 구역 분할) ③ 지역 꾸미기 ④ 코스 선택 화면
     - **① 완료 (2026-10-26)**: 메뉴 Scrun → Build Island Scene → `Assets/Scrun/Scenes/Island01.unity` (약 2분 반). 기본 코스 lake_loop
       - 런타임: `Polyline`(+`IPath`), `RoadNetwork`(노드·엣지·코스, 코스 = 엣지 이어 붙임 + 교차로 모서리 반경 12m 베지어), `RouteTrack : Track`(SetRoute로 코스 변경). `SplineTrack`도 Polyline 사용, `Track`이 IPath
       - 에디터: `MiniJson`(JSON 읽기), `IslandSceneBuilder`, 도로는 엣지별로 교차로 앞 7m에서 끊고 반지름 9m 차선 없는 원판(`Course_AsphaltPlain`). 지형 7.2km·셀 12m·색 지도 2048, 산맥은 도로 범위 밖
       - 식생 데이터는 씬이 아니라 `.bytes`(float 5개/개체)로 분리 → 섬 95만 개 19MB. 코스01도 새 형식으로 재생성(17만 개)
       - 측정: 에디터 Play 19~41fps(PC). 칸 2.5만 개를 매 프레임 전부 검사 → 2단계에서 칸 묶음·거리 컷 필요
       - 확인 필요: 공중 시점(70m 위)에서 도로가 청록색으로 찍힘(달리는 시점은 정상). 교차로는 원판뿐이라 도로 사이 연석·갓길 처리가 없음
     - **② 완료 (2026-10-26)**: 섬 지형. 설계 JSON에 `coast`(radius·roadMargin·cliffWidth·seaLevel·seaFloor, 가로만 scale), `canyons`, 엣지별 `coastMargin`(해안 절벽 길) 추가
       - 해안선 = 물결 원(반지름 1700m) ∪ 도로 130m 이내(육지 보장 거리장 `BuildLandField`). 절벽 길은 바다 쪽 37m만 남기고 섬 안쪽으로 옮긴 선으로 육지 보장. 해안 250m 안은 언덕 낮춤
       - 절벽 60m 폭으로 해저 -45m, 바다 수면 -25m(40km 판). 협곡 폭 70m·깊이 46m(바다와 이어져 물 참) + 다리 163m(바닥판·난간 0.55m·교각, `CourseRoadBuilder.BuildDeck`)
       - 버그 수정: `SaveMesh`가 CopySerialized로 덮어쓰면 GPU에 옛 메쉬가 남아 화면에 옛 지형이 그려짐 → Mesh API로 다시 채움 + 빌드 끝에 SaveAssets
       - 성능: `InstancedScatter` 256m 묶음 컬링, 바위 최대 400m → 에디터 Play 37~51fps(PC)
       - 개발용 이동 도구 `DevTravel`(섬 씬에 자동 배치): 1~5 지역 바로가기(그 지역에 가장 가까운 코스 지점), Tab 코스 바꾸기, [ ] ±250m, F 자유 비행(WASD·우클릭·Shift·Q/E), M 섬 전체. `RunnerController.Teleport`, `FollowCamera.Snap`, `RoadNetwork.regions` 추가. 출시 전 제거/비활성 필요
     - **③ 1차 (2026-10-26)**: `LandmarkBuilder` — 출발 광장(보도블록·가로등·벤치), 등대(코드 회전체, 줄무늬·빛나는 등실), 항구(부두·범선·부표·상자), 400m 트랙(국제 규격 스타디움, 8레인, 결승선, 평지 구역), 호수 데크(부두·벤치·피크닉 테이블), 풍력발전기 7기(날개 회전, 고원 높은 곳 자동 선정), 아치 바위·바위 기둥 3(울퉁불퉁 단면 밀어내기), 협곡 폭포(경사면 따라 흐르는 물줄기·물보라·웅덩이), 해안 절벽 가드레일. 런타임 `Rotator`, `UVScroller`
       - Poly Haven 소품 9종 추가(97.6MB, 부위별 맵): 소품 재질은 FBX 재질 이름(부위)으로 URP 재질 생성. FBX의 가져오기 회전을 유지해야 함(가로등이 누웠던 원인)
       - 지형: `FlatZones`(트랙·광장 평탄화), `Exclusions`(식생 제외), 해안 절벽 폭 40m·경사 0.16 이상 바위색, 트랙 위치 (0,-945)×1.25로 이동
       - 등대 회전 스포트라이트 제거: 특정 각도에서 화면(하늘)이 검게 깨짐(원인 미확정, 등실 유리 근접 반사→블룸 추정). 카메라 stopNaN 켬
       - 아쉬운 점: 폭포가 아직 단순(거품·물보라 약함), 항구 부두가 멀리서 작게 보임·절벽이 완전한 수직은 아님, 바위 질감 단조, 나무 없음
     - **Zwift 수준 마감 1~7 (2026-10-26 밤, 사용자 자는 동안 일괄)**: 방향 = 손그림풍(Zwift식). 실사 Poly Haven 식생은 더 이상 안 씀(소품 일부만)
       - 셰이더 `Shaders/ScrunStylized`(정점 색·빛 감싸기·바람·인스턴싱, 주변광은 전역 `_ScrunAmbientSky/Ground`), `Shaders/ScrunWater`(구운 지형 높이로 수심 색·물가 거품, 잔물결 노멀 2장, 하늘색 전역 반사, 안개 픽셀 단위 — 정점 4개 수면이라 정점 안개는 망가짐)
       - ① 하늘: `SkyBuilder`(구름 파노라마 절차 생성) + `LightingPresets`(맑은 오후 기본, T 키 노을)
       - ② 나무·덤불·꽃·바위: `StylizedFlora`(코드 생성, LOD 3단계) + `VegetationBuilder` 재작성(지역별 수종·꽃 색, 숲·작은 숲·가로수). 약 54만 개 / 10.7MB
       - ③ 색감: 지형 손그림풍 색 지도 + 봉우리 3개(`AutoPeaks`, 하늘선), 언덕을 도로 가까이(Flat 16m), 아스팔트 = 노란 중앙 겹선·흰 가장자리, 갓길 풀색
       - ④ 도로변: `RoadsideBuilder`(지역별 돌담·나무 울타리·가로등·건초, 급커브 화살표, 지역 구간 아치) 182개
       - ⑤ 지형 형태: 봉우리·협곡·절벽 바위색(경사 0.2+)
       - ⑥ 물: `WaterBuilder`(바다·호수·폭포 웅덩이), 호수는 `_MaxDepth`로 바다 위 삐짐 잘라냄
       - ⑦ 함께 달리기: `CompanionBuilder` — 페이스 봇 5명(8.0~12.6km/h, `PaceSpeed`), 고스트(`GhostRecorder`가 1분 이상 달리면 persistentDataPath/ghost_<코스>.json 저장 → `GhostSpeed` 재생), 머리 위 이름표·근처 러너 목록(`RunnerLabels`), 새 무리(`BirdFlock`). `RunnerController`에 좌우 오프셋·Setup·ISpeedSource
       - 폰 성능: `ScrunQuality`(Auto: PC=High, 폰=Medium/저사양 Low; 30fps 고정, 렌더 배율 0.85/0.7, 그림자 40/25m, 식생 거리 60/45%, 풀 50/30%, Low는 식생 그림자·블룸 끔·봇 3명, 화면 꺼짐 방지). 봇 스킨 메쉬 단순화 `SkinnedDecimator`+`SkinnedLod`(5.5만→4.3천 삼각형), 가로등·부두를 코드 모델로(원본 3만·8만 삼각형), 도로 정점 2m 간격, 지형 청크 LOD+치마, 깊은 바다 밑 청크 생략
       - 측정(에디터, Medium 흉내): 삼각형 38~61만, 그리기 호출 280~575(setpass ~110). **실제 폰에서는 아직 못 돌려 봄** — Android Build Support 모듈 설치 후 S22로 확인 필요. 그리기 호출이 모바일 기준 다소 많음(목표 300 이하) → 다음 최적화 후보: 식생 칸 크기·종류 수 줄이기, 도로변 메쉬 합치기
       - 큰 버그 2건 고침: SaveMesh가 정점 색을 안 복사해 쓰레기 값(3e38) → 화면 하얗게 폭발 / 물 정점 안개
       - 남은 것: 다리 아래 모습 미확인, 섬 전체 보기(M)에서 호수 가운데 고리 모양 자국, 캐릭터 Y Bot 그대로(유료 대상), 바위 기둥 질감 실사 그대로, 코스01(노을 호수 코스)도 같은 스타일로 재생성됨 항구(절벽 아래 작은 항구)·등대·트랙 없음, 해안은 모두 절벽(모래사장 없음)
   - **Zwift 영상 비교 개선 1~6 + 나무 (2026-10-26 낮, 사용자 자는 동안 일괄)**: 사용자 지적 "오브젝트가 꿈틀거림", "Zwift 나무가 훨씬 나무 같음". Zwift 실플레이 영상(YouTube wppE2vFqNf0)을 Chrome으로 장면 캡처해 비교. 전후 비교 `docs/unity/map_before_after_20261026.png`. 작업 전 코드 백업 `unity/backup_20261026_night2/`
     - ① 꿈틀거림 원인 = 식생 LOD를 64m 칸 단위로 통째 교체(덤불 덩어리 4→3→1개 등 모양 자체가 다름) + LOD0에서만 그림자 + 풀 24m 칸이 40m에서 뚝 생김 + MSAA 없음. 수정: `InstancedScatter`가 개체마다 거리로 LOD를 나눠 담고(CPU 분할, 중복 없음) 경계는 디더 교차(`ScrunCommon.hlsl` `_ScrunLodFade`, 폭 10%), 풀은 작아지며 사라짐(`_FadeShrink`), 모든 LOD가 같은 골격·뭉치 자리, 그림자는 LOD0만(LOD0 거리를 그림자 거리보다 길게), URP 에셋 MSAA 4x + 잎 알파→커버리지, SMAA 끔. 바람은 휘되 길이 유지(높이 6m 상한)
     - ② 카메라: `FollowCamera`가 코스 위 뒤쪽 지점(4.6m 뒤·2.2m 위)에서 앞길 10m를 봄, FOV 58 → 지평선 화면 위 약 43%
     - ③ 땅·도로: 지역별 땅 팔레트(`CourseTerrainBuilder.PaletteFor`), 후처리 채도 18→4, 흰 연석은 출발 광장 220m만(나머지는 아스팔트→흙→풀 갓길, 갓길 정점 색 = 그 자리 지형 색), 아스팔트 월드 얼룩 2겹(`PatchNoise`), 풀은 지형 색 지도로 물듦(`_GroundTint`)
     - ④ 도로 옆 높이: 이때는 길 자체를 평지로 유지(→ 10/27 화면 전용 고저차로 바뀜, 아래 3차 참고). 대신 지역별 둑(`terrain.BankAmount`, 24~60m에서 솟음, 숲 16m·절벽 12m·고원 9m), 둑 앞 14~20m 바위벽(`RoadsideBuilder.RockOutcrop`, 지층 띠 `_StrataStrength`), 가로수 2줄(9~13m 촘촘, 15~26m)
     - ⑤ 대기: 해 방향 산란 안개(`ScrunFog`: 거리 지수 + 높이 감쇠, 해 쪽 따뜻한 색), 수평선 너머 눈 덮인 먼 산 섬 6개(`SceneryBuilder.FarIslands`, 6~8km, far clip 12km)
     - ⑥ 지역 성격·볼거리(`SceneryBuilder.PointsOfInterest`, 120~210m 간격): 출발 항구 마을(지중해식 집 39채·만국기), 노점, 보트 창고+갈대, 통나무집+모닥불, 장작더미, 빨간 헛간+사일로, 유럽식 풍차(날개 회전) 4, 양 떼, 절벽 전망대, 돌탑, 이정표. 고원·호수 꽃밭 2.1만 포기
     - 나무 새로(`StylizedFlora` 재작성 + `FoliageAtlas` 손그림 잎 텍스처 2×2 코드 생성): 활엽수 = 줄기→굵은 가지 4~5→잔가지, 가지 끝 잎 뭉치(작은 안쪽 덩어리 + 뭉치 크기에 비례한 잎 카드, 둥근 법선), 침엽수 = 처진 가지 카드 11층 + 안쪽 원뿔, **거목(레드우드)** = 굵고 붉은 줄기(버팀뿌리·세로 골) 34~46m(거목 협곡 지역), 야자(항구), 고사리(숲 바닥). 나무 LOD 4단계(55/140/380/1000m)
     - 실사 재질 정리: `MakeMat`이 빛나는 것 외엔 Scrun/Stylized를 씀(Poly Haven 소품도 같은 조명·안개), 아치 바위·트랙 잔디 손그림풍. **정점 색은 선형 값** → 눈으로 고른 색은 `.linear` 변환(안 하면 하얗게 바램)
     - 성능(에디터 2560×1440 MSAA4, PC High): 항구 56fps / 호수 57 / 숲 41(삼각형 99만, 그리기 667) / 고원 54 / 절벽 62. Medium 흉내 숲: 삼각형 73만·그리기 583(에디터는 그림자 4단계라 폰 1단계보다 많음). 먼 숲은 256m 묶음 단위로 합쳐 그림. **실제 S22 측정은 아직** — 숲 구간이 가장 무거움
     - 남은 것: 침엽수 가까이서 카드 면이 종이처럼 보임, 정지 화면에서 LOD 교차 중인 나무에 디더 점무늬(움직이면 안 보임), 봇 이름표 겹침, 캐릭터 Y Bot 임시, Course01·TestTrack도 새 재질로 재생성됨
     - **채우기 2차(사용자: "여전히 단순하고 텅 비어 보임")**: 넓은 땅 나무 1.6만→4.1만(격자 8m, 숲 덩어리 넓게, 경사 0.5까지), 먼 활엽수 LOD3 = 십자 잎 카드(삼각형 8개), 숲 가장자리·초지 덤불 2.5천, 길가 덤불 띠 덜 끊기게(2~4개씩), 풀 띠 32m까지, 도로변 담·울타리 간격 50~120m, 능선 바위 봉우리(`Crag`) 233, **함께 달리는 무리 12명**(`RunnerTag.crowd`, 이름표·목록 없음, 플레이어 뒤 40m~앞 110m에 모임, Medium 6명·Low 0명), 이름표 겹치면 위로 쌓음. 식생 82만 개
     - 채우기 2차 후 성능: PC High 숲 42fps(삼각형 149만), Medium 흉내 숲 삼각형 120만·그리기 604 → **폰에선 무거울 가능성 큼**, S22 실측 후 Medium 식생 거리·나무 밀도 조절 필요
     - **3차 (2026-10-27, 사용자 자는 동안)**: 비교 이미지 `docs/unity/map_before_after_20261027.png`
       - **결정 변경**: 길 높낮이를 화면에만 넣음(속도·러닝머신 경사와 무관, 사용자 승인). `IslandLayout.json` 노드 `[x, z, 높이]` + 엣지 `elev`(경유점 높이) → `IslandSceneBuilder.HeightProfile`(코사인 보간, 경사 7% 이하, 교차로 앞뒤 30m 평평) → `RoadNetwork.Edge.heights`(5m 간격). 항구 0m → 고원 50~56m, 절벽 길 44m. `RunnerController`가 노면 높이를 따라감
       - 지형: `CourseTerrainBuilder`가 가장 가까운 도로 높이(`RoadHeight`) + 주변 도로 높이 가중 평균(`BlendHeight`)을 바탕으로 깔고 그 위에 언덕. 협곡은 `CanyonFloor`(-30)까지 파냄. 둑·바위벽·봉우리 판정은 노면 기준
       - 연출 장면 `SetPieceBuilder`(JSON `features`): 협곡 길(forest_south, 양옆 높이 20m 바위 판), 바위 터널(cliff_road, 둔덕 + 아치 + 안쪽 등), 호숫가 나무 데크(lake_west, 판자 노면·난간·가로등), 능선 풍차 4(highland_east). 협곡 다리는 돌 고가교(24m마다 교각 + 반원 아치)
       - 땅 그늘: 나무·덤불·바위·건물 8.6만 개 밑을 지형 색 지도에 구움(`BakeGroundShade`, 실행 비용 0). 땅 가까이 붓결 풀 잔결(`_DetailMap2` = `GrassStrokes`)
       - 폰 성능: 먼 나무 LOD3(십자 카드) 240m부터, 덤불 LOD 줄임, 봇 스킨 LOD 3단계(원본 6m·중간 2cm 격자·단순 4.5cm 격자, 품질별 거리 배율), 도로변·볼거리 덩어리를 작게 나누고 화면에서 작아지면 안 그림(LODGroup)
       - 측정(에디터, Mobile_RPAsset + Medium 흉내, 2560×1440×0.8): 삼각형 51만~73만, 그리기 286~501, 그림자 물체 27~41. Low: 28만~44만·229~383. PC High 41~53fps. **실제 S22는 여전히 미측정**
       - 남은 것: 땅·바위·건물 텍스처는 여전히 코드 생성(손그림 텍스처 에셋이 가장 큰 남은 격차), 캐릭터 Y Bot, 디버그 HUD, 터널 안 조명 단순, 협곡 다리 아래에서 본 모습 미확인
     - **폰 실기 테스트 (2026-10-27)**: S22(SM-S906N)에 APK 설치·실행 성공
       - 설정: 앱 이름 Scrun, 패키지 `com.scrun.dev`, IL2CPP·ARM64, 가로 고정(좌우 회전만), 빌드 씬은 Island01만. 출력 `unity/Builds/Scrun_dev.apk` (**85MB**, 압축 전 데이터 159MB·네이티브 69MB). 옆의 `Scrun_dev_BackUpThisFolder_ButDontShipItWithYourGame`은 심볼 백업(배포 금지)
       - 빌드 방법: MCP `manage_build`(action build, target android, options ["auto_run"]) → 끝나면 폰에 자동 설치·실행. 첫 빌드 약 15분(IL2CPP C++ 컴파일), 이후 증분 빌드는 수 분. `execute_code`로 `BuildPipeline`을 delayCall 예약하면 플랫폼 전환 직후 도메인 리로드에 지워져 안 돌았음
       - adb 위치: Unity 설치 폴더 `Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe`. Git Bash에서 `/sdcard` 경로는 `MSYS_NO_PATHCONV=1` 필요(화면 캡처: `shell screencap -p /sdcard/x.png` → `pull`)
       - 폰 테스트 조작 `MobileTestControls`(폰에서만 보임, OnGUI): 속도 ±0.5/±2/정지, 지역 1~5·코스, 위 가운데 FPS(1초 평균·최저)·품질 단계(누르면 Low→Medium→High). `RunnerRigBuilder`가 넣음(Island01엔 직접 추가·저장함). 폰에선 DevTravel·HUD의 키보드 안내 숨김
       - 결과: 자동 선택 Medium, 2340×1080, **30fps(최저 30)**, 호수 구간. **High로 바꿔도 30fps**. 단 안드로이드는 `targetFrameRate=-1`이면 기본 30 제한 → 여유(60fps 가능 여부) 미측정. logcat의 `AssetPackManager ClassNotFound`는 Play Asset Delivery 미사용이라 무해
       - 다음: FPS 표시 옆 30/60 전환 버튼으로 여유 측정, 협곡(가장 무거움)에서 최저 FPS, 10~20분 연속 실행 발열·스로틀링. 출시 시 Play 기본 모듈 200MB 제한(현재 85MB라 여유)
     - 작업 도구: `unity/tools/`(README 참고) — `ucs.sh`(C# 실행), `rebuild.sh`(섬 전체 빌드), `fast.sh`(식생만 빠르게), `shoot.sh`(코스·거리 순간이동 후 캡처 → `unity/tools/cap/`), `phget.py`(Poly Haven 받기). MCP execute_code는 `AssetDatabase.DeleteAsset` 차단 → 생성 메쉬 정리는 파일 삭제 후 새로고침
   - **나무·바위 사진 텍스처화 (2026-10-27 저녁)**: 사용자 "나무·돌이 Zwift랑 너무 차이남". Zwift Titans Grove 영상 캡처와 비교 → 차이 = Zwift는 껍질·잎·바위에 사진 기반 텍스처, 우리는 단색 면. 비교 이미지 `docs/unity/trees_rocks_before_after_20261027.png`
     - 잎: `unity/tools/foliage_photo_atlas.py`(Poly Haven CC0 fir_tree_01 가지·tree_small_02 잎을 오려 붙여 합성, 원본은 `unity/tools/foliage_src/`) → `FoliageAtlas` 2048(침엽수 가지·활엽수 잎 뭉치·원거리 침엽수 실루엣 칸 + 코드로 그린 깃털잎). 색은 회색 명암으로 정규화, 색은 정점 색
     - 껍질: `NatureTextures.BarkArray`(4종 → 텍스처 배열), 줄기 정점 UV1.y = 종류+1, 셰이더 `_BARK_ARRAY`
     - 바위: 셰이더 `_ROCK_TRIPLANAR`(rock_boulder_dry 색÷평균색 + 노멀 3면 투영 + 윗면 이끼, `_RockSteep`이면 가파른 면만) → Sty_Rock·Sty_RockWall·Lm_SeaRockSty·Island_Terrain. 바위 메쉬는 평면 깎기로 쪼개진 화강암 모양(35° 이내만 부드러운 법선)
     - 침엽수 재작성(`NeedleTiers`: 사진 가지 18층, 안쪽 원뿔은 아주 어둡게 — 밝으면 청록 삼각형으로 보였음), 침엽수·거목 LOD3 = 실루엣 카드 십자. 활엽수 LOD2 = 어두운 덩어리 + 큰 잎 카드
     - 함정: `Material.SetColor`는 sRGB→선형 변환을 함 → `.linear`를 또 하면 두 번 변환(바위가 하얗게 탔던 원인). 정점 색은 변환 없음
     - **빠른 메뉴** Scrun → Rebuild Vegetation Meshes (fast): 배치는 그대로, 식생 메쉬·재질만 다시(컴파일 포함 약 1분, 전체 빌드는 약 3분). LOD 수·변형 수를 바꾸면 전체 빌드 필요
     - 측정(PC High, 숲): 삼각형 103만·그리기 671·setpass 185. S22 재측정 필요
     - 남은 것: 장작더미·담 등 소품은 여전히 단색 코드 모델, 침엽수 밝기 변화(햇빛 받는 끝) 약함, 정지 화면 LOD 디더 점무늬
   - **다음 할 일 후보**: 폰 60fps·발열 측정(위), 손그림 텍스처 에셋(땅·바위·건물 — 현재 가장 큰 격차), 디버그 HUD 교체, 물 셰이더, 소품, 앱 화면 흐름(UI Toolkit: 시작→홈→기기 연결·보정→달리기→결과) + 디자인 시스템, 실제 캐릭터 선정(Y Bot은 임시), 고스트/다른 러너 여러 명, git 저장소 만들기
   - 방향 결정(10/26): 앱 전체를 Unity로 통일(Flutter+Unity 혼합 안 함). 목표는 출시 가능한 완성도
   - 결정: 서버는 Spring 대신 FastAPI(팀에 Spring 경험자 없음). 개발 방식은 walking skeleton(끝-끝 뼈대 먼저). git 저장소 아직 없음

## 앱 화면(UI Toolkit): Figma에서 옮기기 (11/05 확인, 11/06 민재 인수인계에서 옮김)
- Figma 와이어프레임 94개 화면: A 온보딩 17, B 홈 4, C 준비 2, D 러닝 가로 22, E 인증 12, F 기록 5, G 친구 10, H 마이 10, I 예외 12 + UI Kit. Chrome 확장에서 `figma` 전역(플러그인 API)으로 읽기만 함
- "분석" 화면은 F-03·E-12의 속도 변화 그래프(지환). D-10 코스 이름 "한강 러닝 코스" → 가상 섬 이름으로 바꿀 것
- 옮기는 방법:
  - 오토 레이아웃 1361·인스턴스 523 → UI Toolkit(flexbox, UXML 템플릿)으로 거의 그대로
  - 그림자 158개(80장) → USS에 그림자가 없으니 UI Kit에서 9-slice 이미지 등으로 한 번 만들어 재사용
  - 그라데이션 12장 → 이미지나 코드로 그리기
  - 배경 흐림 → URP 후처리
  - 글꼴 Pretendard·Paytone One
- 완성 기준: 아이폰에서 Figma와 나란히 놓고 상준이 같다고 판단
- 확인 흐름: 평소엔 UI Builder(UXML·USS 편집) + Device Simulator(아이폰 기종·노치·안전 영역·회전) + Play 모드(Live Reload, `FakeSpeedSource`·FakeApi), 화면 묶음 하나가 끝날 때 아이폰 빌드로 손맛·한글 입력·센서·성능 확인. 컴퓨터에서 못 보는 것: 손가락 스크롤 관성, 실제 iOS 키보드, 센서·에어팟, 프레임·발열, 손에 든 글자 크기
- 남은 위험(가설): 아이폰에서 한글 입력·스크롤 감 → 화면 틀 첫 화면을 닉네임 입력 + 긴 목록으로 골라 확인. 입력이 어색하면 입력 칸만 iOS 네이티브 입력 플러그인(ADR-0001 11/06 보충)

## 앱 코드 묶음 `Scrun.App` (11/09, 계획 0018)
- 새 앱 코드(서버 연결·네이티브·화면)는 `Assets/Scrun/App/` 아래에 둔다. 이 폴더는 asmdef로 따로 묶인 덩어리(`Scrun.App`)라서 기존 `Assets/Scrun/Scripts/`(섬 맵·달리기, 기본 덩어리)를 쓸 수 없다. S2에 속도를 붙일 때 기존 스크립트도 묶음으로 옮긴다
  - `App/Editor/` = 에디터 전용(`Scrun.App.Editor`), `App/Tests/` = 자동 테스트(`Scrun.App.Tests`, Window → General → Test Runner → EditMode)
- **아이폰 기능은 `Native`로만 부른다**: `Native.Permissions`(권한 4종: 모션·피트니스, 카메라, 헤드폰 모션, 알림 / 상태 읽기·`RequestAsync`·`OpenSettings`·`Changed`), `Native.Headphones`(`Status` 없음/모션 없는 이어폰/에어팟, `DeviceName`, `Changed`)
  ```csharp
  var s = await Native.Permissions.RequestAsync(PermissionKind.Camera); // 팝업을 누를 때까지 기다림
  if (s == PermissionStatus.Denied) Native.Permissions.OpenSettings();
  Native.Headphones.Changed += st => Refresh(st);
  ```
- **에디터에서는 가짜**: 메뉴 Scrun → Fake Native 창. Play 전엔 처음 상태와 "팝업에서 허용/거부"·누르는 시간을 정하고(이 컴퓨터에 저장), Play 중엔 지금 상태를 바꾸면 설정 앱에서 바꾸고 돌아온 것·에어팟을 끼고 뺀 것처럼 `Changed`가 온다. Play를 시작할 때마다 처음 상태로 돌아감
- **아이폰 진짜 구현**(11/10, 계획 0018의 5번, ADR-0009): Swift `Assets/Plugins/iOS/Scrun/`(통로 `ScrunBridge`, 권한 `ScrunPermissions`, 이어폰 `ScrunHeadphones`, 키체인 `ScrunKeychain`, 카카오 `ScrunKakao`) ↔ C# `App/Native/`(`NativeBridge` 요청 번호 짝·메인 스레드 큐, `IosNative`, iOS 빌드에서만 `IosChannel`). 화면 코드는 그대로
  - 새 기능 더하기: Swift `ScrunBridge.swift`의 `scrun_call`(답을 기다림)이나 `scrunQuery`(바로 답)에 `case` 하나 + C# `IosNative`에 메서드 하나. 이름 문자열은 두 파일이 짝
  - 센서·속도는 이 통로를 안 쓴다(S2: Swift가 속도까지, Unity는 매 프레임 숫자만)
  - 안드로이드 등 다른 빌드에선 `Native`가 바로 예외(아이폰만, ADR-0007). 가짜로 화면만 보려면 Scripting Define Symbols에 `SCRUN_FAKE_NATIVE`
  - 키체인을 못 읽어도 앱은 "로그인 안 됨"으로 뜬다(`Session`). 키체인은 앱을 지워도 남아서 새로 설치하면 처음 쓸 때 지움
  - 개발 빌드 전용 **네이티브 점검** 화면: 로그인 화면 아래 링크 → 권한 4종 요청·설정 앱·이어폰 상태·키체인 저장/읽기/지우기와 이벤트 기록(출시 빌드엔 링크가 안 보임)
  - 시뮬레이터 확인(11/10, iPhone 18 Pro): 카메라·알림 팝업 → 허용이 이벤트·결과로 옴, 모션·헤드폰 모션은 팝업 없이 2초 안에 `NotDetermined`(센서 없는 기기), 키체인·설정 앱 정상. iOS는 설정 앱에서 권한을 **끄면** 앱을 종료시킨다(그래서 "허용 → 거부" 이벤트는 없고 다음 실행 때 상태로 앎)
  - 에어팟 모션 연결(`Connected`)은 누군가 `Native.Headphones.Changed`를 구독하는 동안에만 지켜본다(모션 업데이트 = 배터리). 이어폰 상태를 보여 주는 화면은 `OnShow`에서 구독, `OnHide`에서 빼기. 구독자가 없으면 에어팟도 `ConnectedNoMotion`으로 보일 수 있음
  - 아이폰 확인(11/10, 아이폰 15 Pro Max·iOS 26.5): 모션·카메라·알림 팝업 → 허용이 결과·이벤트로 옴. **헤드폰 모션은 모션과 같은 "동작 및 피트니스" 권한**이라 모션을 허용하면 따로 묻지 않고 바로 `Granted` 이벤트가 옴(그래서 "이어폰 없이 헤드폰 모션 팝업이 뜨나"는 앱 흐름에선 상관없음). 에어팟 끼기 → `Connected`, 귀에서 빼기 → `ConnectedNoMotion`, 케이스 → `NotConnected`가 이벤트로 옴. 키체인 저장·읽기·지우기 정상
  - 다시 설치·재실행해도 로그인 유지(키체인 토큰, 11/10 아이폰). 아직 안 본 것: 거부 → 설정 앱에서 허용 → 돌아오면 이벤트, 한글 입력(닉네임)
  - 안전 영역(11/10 아이폰): 노치·다이내믹 아일랜드에 안 가림. 아래 탭이 보이면 홈 바 여백은 `safe-area`가 아니라 **탭 바 안쪽 아래 padding**(`SafeArea.Apply(safeArea, tabBar)`) → 탭 바 배경이 화면 끝까지, 버튼은 홈 바 위. 탭 높이 49(아이폰 기본 탭 바, 폭 390 기준 단위). 터치 화면에선 `:hover`가 마지막으로 누른 곳에 남으므로 누름 표시는 `:active`로. 탭 아이콘은 상준 UI Kit에서(지금은 글자만이라 글자 밑이 비어 보임)
- **카카오 로그인**(11/10, 계획 0018의 6번): `Native.Kakao.LoginAsync()` → 카카오 access 토큰(취소면 null) → `Api.Auth.KakaoLoginAsync`
  - Swift `ScrunKakao.swift`: 카카오톡이 깔려 있으면(`kakaokompassauth` 스킴) 카카오톡 로그인, 없거나 카카오톡을 열지 못하면(`NotSupported`) 카카오계정 웹 로그인(앱 안 창). 그 밖의 실패는 오류로 돌려줘 화면이 "다시 시도"를 띄움. SDK 기본(유니버설 링크)은 카카오톡이 없을 때 Safari로 열려 "성공"이 되므로 스킴으로 먼저 확인한다. 카카오톡에서 로그인 없이 돌아오면 2초 뒤 취소. 돌아온 URL은 Unity가 뿌리는 `kUnityOnOpenURL` 알림에서 받아 SDK에 넘김(Objective-C 불필요)
  - 빌드 후처리가 SDK 2.30.0(SPM, `kakao-ios-sdk`, Common·Auth·User)을 UnityFramework에 붙이고 Info.plist에 `KAKAO_APP_KEY`·URL 스킴 `kakao{키}`·`LSApplicationQueriesSchemes`(kakaokompassauth, kakaolink)를 넣는다. 키는 `ScrunLocal.json`의 `kakaoNativeAppKey`(카카오 개발자 → SCRUN → 앱 → 플랫폼 키 → 네이티브 앱 키, 번들 ID `com.scrun.app` 등록됨). 키가 없으면 경고 + SDK 없이 빌드(로그인은 "SDK 없음" 오류)
  - 시뮬레이터 확인(11/10): 카카오계정 웹 로그인 → 공용 서버로 약관·프로필·가입 → 홈, 웹 창 취소 뒤 다시 누르기, 앱 재실행 뒤 로그인 유지(키체인)
  - 아이폰 확인(11/10): 카카오톡 없을 때 웹 로그인 창 → 취소 두 번 모두 로그인 버튼이 다시 눌림, 카카오톡 설치 뒤 카카오톡 앱으로 넘어가 동의 → 앱으로 돌아와 토큰 받음 → 공용 서버 로그인(오류 없음)
  - 개발(Debug) 빌드에서는 카카오 SDK가 카카오 토큰·인가 코드를 콘솔에 찍는다(SDK의 `#if DEBUG`, 출시 Release 빌드는 안 찍음) → 개발 빌드 로그를 남과 공유하지 않기
  - Xcode 경고 "KakaoSDK… was not compiled with library evolution support"는 UnityFramework를 따로 배포할 때만 의미가 있어 무시
- **속도 센서는 `Native.Speed`**(11/10, 계획 0020, `App/Native/SpeedSensor.cs`): 센서 준비(D-07)·대기·카운트다운(D-08·09)·보정·달리기 화면이 쓴다. Swift가 속도까지 계산하고 C#은 **한 프레임에 한 번** 읽는다(다른 Update보다 먼저 갱신돼 같은 프레임엔 모든 화면이 같은 값)
  ```csharp
  Native.Speed.Start();                                   // 센서 준비 화면에 들어올 때. 끝·취소 때 Stop(이벤트 없이 꺼짐). 센서는 하나라 켠 쪽이 끈다
  Native.Speed.FlowChanged += Refresh;                    // Phone·AirPods: 꺼짐/기다림/정상/끊김(2초 넘게 안 옴)
  Native.Speed.GaitChanged += g => { if (g != Gait.None) StartCountdown(); else BackToWaiting(); };
  var r = Native.Speed.Current;                           // r.HasSpeed, r.SpeedKmh, r.Gait, r.Steps(감지 지연 없이 바로 늘어남), r.UsingAirPods
  if (!Native.Speed.HasCalibration) Nav.Push(new CalibrationScreen());
  ```
  - 판정(계획 0020, 숫자는 가설 → 아이폰에서 잼): 폰은 1초에 80개 이상이면 정상, 에어팟은 연결 + 헤드폰 모션 허용 + 1초에 20개 이상(아니면 꺼짐 = "이어폰 없이 진행", 실패 아님). `Gait`는 걸음 상태가 2.5초 유지되면 바뀜. 네트워크는 여기 없음(S3 실시간)
  - **에디터 가짜**: Fake Native 창 "속도 센서" 칸. 폰·에어팟 데이터 들어옴(끄면 2초 뒤 끊김, Play 중에도 바로), 보정값 있음, 켜고 들어오기까지·걸음 감지 지연. Play 중엔 사람 속도 슬라이더·"서 있음/걷기 4/달리기 8"·시나리오(3초 뒤 걷기 → 10초 뒤 달리기)와 화면이 읽는 값이 보이고, 키보드 ↑/↓(±0.5)·Space(정지). 7 km/h 미만은 걷기(흉내 경계). 에어팟은 위 이어폰·헤드폰 모션 권한을 따름
  - 섬 씬 아바타에 꽂기: `Scripts/NativeSpeedSource`(`ISpeedSource`)를 `RunnerController`의 속도 소스로(그 씬의 `FakeSpeedSource`는 빼기: 키가 둘 다 먹음). 센서가 꺼져 있으면 켜고, 자기가 켠 경우에만 끔
  - 아이폰 진짜 구현은 S2 Swift 속도 플러그인(BL-5). 그 전 아이폰 빌드에서 `Native.Speed`는 예외(`NativeSpeedSource`는 한 번 알리고 속도 0)
- **아이폰 빌드**(11/10, 맥): 메뉴 **Scrun → iOS → Xcode 만들기**(아이폰 개발 / 시뮬레이터 개발 / 아이폰 출시) → `unity/Builds/iOS-*`. 빌드 대상이 iOS가 아니면 먼저 전환(끝나면 메뉴 한 번 더)
  - 빌드 직후 `IosPostprocess`가 Info.plist 권한 문구(모션·카메라)·`CFBundleDevelopmentRegion ko`·암호화 신고 면제, UnityFramework에 Swift 5·CoreMotion 등 프레임워크, 서명 팀을 채운다
  - 사람마다 다른 값은 git 밖 `unity/TestRun/ScrunLocal.json`(예시 `ScrunLocal.example.json`, 메뉴 Scrun → iOS → 로컬 설정 파일 열기): `appleTeamId`(비우면 Xcode Signing에서 고름), `kakaoNativeAppKey`(6번)
  - 터미널로 시뮬레이터 빌드: `xcodebuild -project Unity-iPhone.xcodeproj -scheme Unity-iPhone -sdk iphonesimulator -destination 'platform=iOS Simulator,name=iPhone 18 Pro' CODE_SIGN_IDENTITY=- build` → `xcrun simctl install/launch`. 서명을 끄면(`CODE_SIGNING_ALLOWED=NO`) 키체인이 -34018로 실패한다. Xcode 27은 시뮬레이터 화면을 DeviceHub 앱에서 보여 줌(가설, 11/10 확인 중)
  - **아이폰에 설치**(11/10 확인): ① 케이블로 연결 → 아이폰에서 "신뢰" ② 아이폰 설정 → 개인정보 보호 및 보안 → 개발자 모드 켜기(재시작 뒤 "켜기") ③ Xcode → Settings(⌘,) → Accounts → + Apple Account ④ 팀 ID를 `ScrunLocal.json` `appleTeamId`에(처음 한 번은 Xcode에서 Unity-iPhone 타깃 → Signing & Capabilities → Automatically manage signing 체크 → Team 고르면 `project.pbxproj`의 `DEVELOPMENT_TEAM`에 보임) ⑤ 메뉴 Xcode 만들기(아이폰, 개발) ⑥ 터미널: `xcodebuild -project unity/Builds/iOS-Device/Unity-iPhone.xcodeproj -scheme Unity-iPhone -configuration Debug -destination 'id=<UDID>' -derivedDataPath unity/Builds/DerivedData-Device -allowProvisioningUpdates build` → `xcrun devicectl device install app --device <UDID> unity/Builds/DerivedData-Device/Build/Products/Debug-iphoneos/Scrun.app` → `xcrun devicectl device process launch --console --device <UDID> com.scrun.app`(콘솔 로그가 터미널로 옴). UDID는 `xcrun devicectl list devices`. 화면 캡처는 `xcrun devicectl device capture screenshot --device <UDID> --destination 파일.png`(Claude가 직접 화면을 보고 확인할 때) ⑦ 무료 개인 팀이면 처음 실행이 막힘 → 아이폰 설정 → 일반 → VPN 및 기기 관리 → 개발자 앱 신뢰. 무료 팀은 7일마다 다시 설치
  - 아이폰 빌드 콘솔 경고(11/10): URP 피사계 심도(Gaussian·Bokeh DOF)·Panini 셰이더가 빌드에서 빠져 그 후처리가 안 돈다 → 달리기 화면에 쓸 거면 URP 설정에서 남기기, 안 쓰면 볼륨에서 끄기(할 일)
  - 맥에서 처음 열면 "Unity Version Control" 창이 기본 배치에 있고, 이 창이 버전 관리 모드를 "Unity Version Control"로 바꾼다 → 창을 닫고 Project Settings → Version Control → Mode를 "Visible Meta Files"로(우리는 git)
  - 새로 받은 컴퓨터에서 Unity를 처음 열기 **전에** `python unity/tools/polyhaven_fetch.py`(Poly Haven 파일)와 Mixamo 파일(`README` "처음 받았을 때")을 채운다. 파일 없이 열면 Unity가 짝 없는 `.meta`를 지우는데 이걸 커밋하면 팀원 쪽 참조(GUID)가 깨진다 → `git checkout -- <폴더>`로 되살림
- **서버는 `Api`로만 부른다**(11/09, 계획 0018의 2번, `App/Api/`):
  ```csharp
  var r = await Api.Auth.KakaoLoginAsync(kakaoToken);     // 처음이면 r.IsNewUser → Api.Session.IsSigningUp
  var list = await Api.Auth.AgreementsAsync();
  var n = await Api.Users.CheckNicknameAsync(text);        // n.Nickname = 서버가 정규화한 값(화면에 이걸 보여 줌)
  await Api.Auth.SignupAsync(new SignupRequest { AgreedIds = {...}, Nickname = n.Nickname, HeightCm = 175, WeightKg = 70 });
  Api.Session.Expired += GoToLogin;                        // 재발급이 거절돼 다시 로그인해야 할 때
  ```
  - 실패는 전부 `ApiException`: `Failure`(Server = 서버가 거절, `Code`로 문구 / Network = 연결 안 됨·시간 초과 / BadResponse = 응답을 못 읽음 / NotLoggedIn = 로그인 화면으로), `Status`, `RequestId`(오류 화면에 작게)
  - 토큰은 `ApiClient`가 붙이고, 401 `TOKEN_EXPIRED`면 재발급을 **한 번만** 보내고(동시에 여러 요청이 만료돼도) 원래 요청을 한 번 다시 보낸다. 재발급 중 네트워크가 끊기면 토큰은 그대로(서버 30초 유예), 거절되면 로그인을 지우고 `Session.Expired`. 로그아웃은 서버에 못 알려도 이 기기 토큰을 지움. 재발급이 도는 사이 로그아웃·다른 계정 로그인이 끼면 그 재발급 결과는 버린다(`Session.Epoch`)
  - 토큰 저장: `Native.SecureStore`(아이폰 키체인은 5번, 에디터는 가짜 PlayerPrefs). 가입용 토큰은 저장 안 함
  - 서버 주소 `ApiConfig.BaseUrl`: 에디터 기본 로컬 `http://localhost:8000`(가짜 카카오 `k_123`), 빌드는 공용 개발 서버(HTTPS). 에디터에서는 메뉴 **Scrun → API 서버** 창(주소 바꾸기, 저장된 로그인 지우기)
  - 확인: 메뉴 **Scrun → 개발 → 서버 로그인 흐름 확인**(Play 중, 로컬 서버) = 약관 → 새 사람 로그인 → 닉네임 확인 → 가입 → 다시 로그인 → 로그아웃을 실제 서버로. 11/09 로컬 서버로 성공, DB에 사용자·약관 동의 2개 확인
  - 서버 모양을 바꾸면 `App/Api/Dtos.cs`도 같이. 아이폰 빌드에서 DTO가 지워지지 않게 `App/link.xml`
- **화면 틀**(11/09, 계획 0018의 3번, `App/UI/`, UXML·USS는 `App/Resources/UI/`):
  - 앱 씬 `Assets/Scrun/Scenes/App/App.unity`(빌드 씬 맨 앞). 메뉴 **Scrun → Build App Scene**이 글꼴 에셋·테마·패널 설정·씬을 코드로 다시 만든다(손 설정 없음)
  - 새 화면 만들기: `AppScreen`을 상속하고 `Uxml`(Resources 기준 경로)만 정하면 됨. 버튼 연결은 `OnCreate`, 보일 때마다 `OnShow`, 없어질 때 `OnDestroy`
    ```csharp
    public sealed class CourseListScreen : AppScreen {
        protected override string Uxml => "UI/CourseList";   // App/Resources/UI/CourseList.uxml
        protected override void OnCreate() => Q<Button>("enter").clicked += () => Nav.Push(new CourseDetailScreen());
    }
    ```
  - 화면 전환은 `AppRoot.Nav`: `Push`/`Pop`(에디터 Esc = 뒤로), 탭(홈·기록·친구·내 정보)은 탭마다 따로 쌓이고 지금 탭을 다시 누르면 첫 화면으로. 로그인·온보딩·달리기처럼 아래 탭 없는 흐름은 `StartFlow`, 끝나면 `ShowTabs`. 상세 화면에서 탭을 숨기려면 `HidesTabBar`, 가로 화면은 `Landscape`(나머지는 세로 고정)
  - 크기: 패널 기준 390×844(세로, 폭 맞춤), 가로 화면이면 뒤집음. 노치·홈 바는 `Screen.safeArea`만큼 안쪽 여백(배경은 끝까지)
  - 색·글자 크기는 `App/Resources/UI/App.uss`의 변수. **11/10부터 Figma "SCRUN" > 스크런 와이어 프레임 > UI Kit 값**(sky/50 바탕 `#F3F8FF`, ink `#172036`, body `#4A5468`, mute `#8A93A6`, sky/700 `#2F6FE4`, sky/100, line `#E3EAF6`, lime `#D8FF3E`, 글자 Title 28·Heading 22·Subheading 18·Body 16·BodySm 14·Caption 12, 버튼 높이 54·모서리 16, Button/Ghost는 `btn-ghost`). 글꼴 Pretendard 1.3.9(OFL, `App/UI/Fonts/`, 라이선스 파일 같이) Regular·Medium(본문)·SemiBold·Bold. 기본 글꼴은 테마(`AppTheme.tss`)에서 Regular
  - Figma → USS 옮길 때 주의: 그림자·배경 흐림·줄 높이는 USS에 없어 빠짐. `:last-child`는 USS가 모름(경고만 나고 안 먹음 → 클래스로). `border-radius: 999px`는 타원처럼 그려짐 → 높이의 절반. 자간은 Figma가 %라 px로 바꿈(28px × −2% = −0.56px). SVG 아이콘은 그대로 넣으면 UI Toolkit 벡터 이미지가 되는데, 가져오기 설정 Viewport Options를 **Preserve Viewport**로 해야 늘어나지 않음(`App/UI/Icons/`). 기본 테마의 버튼 hover·active 회색은 클래스마다 같은 색으로 막음
  - 에디터에서 Play 중에 UXML·USS를 고치면 Live Reload로 앱 화면 틀이 다시 만들어져 홈으로 돌아감(정상)
  - 글꼴 에셋은 쓰는 글자만 그때 그리는 동적 에셋이라 화면을 그릴 때마다 글자가 쌓인다 → **저장 직전에 비운다**(`AppFontReset`, 파일은 늘 빈 상태라 git 변경이 안 생김). 빌드 때도 비움
  - 화면 방향: 프로젝트 설정은 세로·가로(왼쪽·오른쪽) 허용 + 자동 회전(아이폰은 Info.plist에 적힌 방향으로만 돌 수 있음), 실제 고정은 `AppRoot`가 화면마다(세로 고정, `Landscape` 화면만 가로). Build App Scene이 이 설정도 맞춤. SpeedLab 빌드는 끝나면 원래대로 되돌림
  - 빌드 씬 맨 앞은 앱 씬. 섬 씬 빌더(`ScrunEditorUtil.AddToBuildSettings`)는 이제 뒤에 붙인다
  - 확인(11/09): 한글 표시, 탭 전환·화면 쌓기, 아이폰 크기(1170×2532)로 그린 세로 배치. 아직 못 본 것: 노치 여백(Device Simulator·아이폰), 실제 아이폰의 한글 입력·스크롤 손맛(4번 닉네임 입력 때)
- **로그인·가입 화면**(11/09, 계획 0018의 4번, `App/UI/Screens/`): 저장된 로그인이 없으면 `LoginScreen`(A-05) → 카카오 로그인 → 처음이면 `TermsScreen`(A-06, 서버 약관 목록·모두 동의·보기) → `ProfileScreen`(닉네임은 입력을 멈추고 0.4초 뒤 서버 확인·정리된 값 표시, 키 100–230·몸무게 20–250) → 가입 → 홈. 이미 가입했으면 바로 홈. 재발급이 거절되면 로그인 화면으로(안내 문구)
  - 카카오는 `Native.Kakao`(아이폰은 6번 카카오 SDK). 에디터는 Fake Native 창의 "카카오 로그인" 토큰(로컬 서버 가짜 카카오, "새 사람" 버튼 = 처음 오는 사람). 취소 흉내도 됨
  - **서버 호출은 `Feedback.Run`으로 감싼다**: 로딩 덮개(I-07), 실패 종류별 안내창(I-08·I-09, 다시 보내면 달라질 때만 "다시 시도"), 로그인이 끝났으면 로그인 화면으로. 문구는 임시(상준 확정 대기)
  - 확인(11/09, 로컬 서버 8001): 화면을 눌러 가며 새 사람 가입 → 홈, DB에 닉네임(앞뒤 공백 제거)·키·몸무게·약관 3개·로그인 1개. 서버가 꺼진 주소로는 "연결이 안 돼요" 창 → "다시 시도"는 카카오 로그인을 다시 시키지 않고 서버만 다시 부름. 약관 불러오기 실패 뒤 창을 닫으면 "다시 불러오기", 닉네임 확인이 연결 문제로 실패하면 3초 뒤 저절로 다시, 키·몸무게 오류는 칸을 벗어난 뒤에만, 로딩·오류창 중엔 Esc(뒤로) 막음
  - 남은 것: 진짜 카카오(6번), 권한·에어팟 화면(지환)을 가입 뒤·홈 앞에 넣기, 아이폰 한글 입력·키보드 가림, Apple 로그인(출시 전)
- **코스 화면·맵 입장**(11/10, 계획 0022, Figma C-01·C-02·I-10·I-11·D-10 기준): 홈 "달리기 시작하기" → `CourseListScreen`(C-01 "어디서 달릴까요?", 칩 전체·지금 달리는 중, 코스 카드: 그림·이름·"한 바퀴 6.7km"·지금 달리는 사람 칩, 홈 탭에 쌓이고 뒤로 가기는 탭 "홈") → `CourseDetailScreen`(C-02, 사진 위 동그란 뒤로 가기·인원 칩, 이름·설명, 달리는 러너·코스 길이 칸, "맵 입장", 없는 코스면 화면 안에 "다른 코스 보기") → `MapScreen`(가로)
  - **입장은 가로 맵 화면이 한다**(Figma 흐름: C-02 → 가로 전환 → I-10 로딩 → 맵 또는 I-11). `MapScreen`이 I-10(남색 바탕·"코스를 불러오는 중이에요"·라임 진행 막대: 입장 요청 15% + 씬 불러오기) 아래에서 `POST /entries` → 섬 씬을 겹쳐 불러옴. 입장이 안 되면 I-11 카드: 가득 참·연결 문제는 "다른 코스 보기"·"다시 입장하기", 없는 코스는 "다른 코스 보기"만. 보정 없음은 상세로 돌아가 안내(보정 화면·C-03 시트는 민재 S2). 오른쪽 위 X = 나가기(S3에 멈춤 버튼 D-10·D-17로 바뀜), 왼쪽 위 "한 바퀴 6.7km · 코스 이름"
  - **`Feedback.Run`의 예외**: 맵 입장은 로딩을 I-10이, 실패 안내를 I-11 카드가 맡아 `Feedback.Run`으로 감싸지 않는다(로그인 만료만 `Feedback.Handle`). 러닝 설정의 "이번 주 목표까지 N회"(주간 목표)도 곁들이는 안내라 감싸지 않고, 못 불러오면 그 줄만 숨긴다(11/11). 다른 화면은 계속 `Feedback.Run`
  - 섬 씬은 한 번에 한 맵 화면만 잡는다: 불러오기 시작부터 다 내릴 때까지 다음 맵 화면은 기다림(불러오는 도중 나갔다 바로 들어와도 섬 씬이 둘 겹치지 않음). 맵에 있는 동안 앱 카메라·AudioListener는 끄고, 노치 여백까지 남색이 되게 앱 카메라 배경을 바꾸고, 섬 씬이 바꾼 `targetFrameRate`는 나갈 때 되돌림
  - 앱 → 섬 씬은 `MapLaunch.Store`(앱이 남김) → `MapLaunchApplier`(섬 씬이 읽어 `RouteTrack.HasRoute` 확인 뒤 코스·입장 위치 적용). 섬 씬을 혼자 Play하면 기본 코스. 앱에서 들어오면 섬 개발 도구(속도판·순간이동·폰 테스트 버튼·근처 러너 목록)는 끄고 머리 위 이름표는 남김(`RunnerLabels.showNearbyList`)
  - 확인(11/10, 로컬 서버, 에디터 Play·Game 창 390×844와 852×393): 위 흐름, 코스 4개 각각 그 도로망 코스·서버 입장 위치, 불러오는 도중 나갔다 재입장, 서버 연결 끊김 → 복구 뒤 다시 입장하기, 없는 코스 상세. 가득 참·보정 없음은 실패를 직접 넣어 확인. Figma에서 뺀 것: 코스 사진(서버 썸네일이 아직 null → 하늘색 칸 + 첫 글자), "친구가 있는 코스" 칩·"친구" 칸(친구 API 전), [모션] 3D 프리뷰·로딩 아바타(에셋 필요), 배경 흐림
  - 에디터에서 MCP로 버튼을 누를 때(`NavigationSubmitEvent`) Game 창에 포커스를 주면 앞서 누른 버튼이 다시 눌리는 일이 있었다(가설: 포커스 받은 버튼에 제출이 다시 들어감). 자동 확인 때는 포커스를 주지 않는다
- **러닝 설정·거치 안내**(11/11, 계획 0026, Figma D-01·D-05·D-06): 섬이 열리면 `MapScreen` 위에 `RunSetupView`(새 화면이 아니라 맵 화면 안의 층, `RunSetup.uxml`)가 D-01 러닝 설정 → D-05 거치 안내 → D-06 달리는 매뉴얼을 차례로 띄우고, 끝나면 맵(S3부터는 D-07 센서 준비). 뒤로 가기(버튼·Esc)는 한 단계 앞으로, D-01의 X·뒤로는 맵 나가기. 고른 목표는 `MapScreen.Setup`(`RunGoal`: 거리 0.1 km·시간 분 정수, `ToRequest()` = 서버 m·초)에 남고 서버에는 S3 카운트다운 끝에 `POST /runs`로 보냄
  - 목표: 거리 0.5 km씩 0.5에서 42.2 km, 퀵 2·3·5 km / 시간 5분씩 5에서 180분, 퀵 20·30·45분, 직접 입력(범위 밖이면 이유 표시·확인 막음). 마지막 목표와 "다시 보지 않기"(D-06만 건너뜀, D-05는 매번)는 폰 `PlayerPrefs`(`RunSetupPrefs`). 고스트 카드는 S7까지 숨김
  - **배경 흐림**: USS에 흐림이 없어서 `MapBackdrop`이 섬 카메라로 한 장 찍어(화면의 1/4) 반씩 세 번 줄인(1/32) 그림을 화면 전체(노치 여백까지, 패널 맨 아래)에 깔고 남색 50%를 덮는다. 안내가 떠 있는 동안 **섬 카메라를 끄고**(발열·배터리) 앱 카메라는 그리는 것 없이 지우기만(`cullingMask = 0`). 섬 쪽 `RunnerLabels`는 카메라가 꺼져 있으면 이름표를 안 그림. 섬이 열린 뒤 3프레임 기다렸다 찍는다(입장 위치로 옮긴 뒤)
  - 맵 화면을 떠날 때는 흐린 배경을 먼저 걷고(앱 카메라 상태 복구) 섬 씬을 내림(`CloseSetup` → `ReleaseIsland`)
  - 확인(11/11, 에디터 Play·로컬 서버): D-01 거리·시간 탭, +, 직접 입력(200 → 범위 안내, 4.5 → 4.5 km), D-05 → D-06 → 맵(목표 `DISTANCE/4500`), D-05 뒤로 → D-01, 다시 들어오면 4.5 km·"다시 보지 않기"로 D-05 다음 바로 맵, D-01 X → 홈·섬 씬 내려감·앱 카메라 복구. 안내 중 Live Reload로 화면이 다시 만들어져도 섬 씬·카메라 정리됨. 아직: 아이폰 노치·키보드, D-05·D-06 그림(출처 확인 중)
- **아래 탭 바**(11/10, UI Kit Tab Bar): 아이콘 24 + 글자 12, 홈·친구·기록·마이 순서, 고른 탭은 파란 아이콘(`tab-*-on.svg`)·굵은 글자. 탭 버튼 이름(`tab-home` 등)은 그대로라 `Navigator`는 안 바뀜
- 패키지: Test Framework 1.8.0, Newtonsoft Json 3.2.2(서버 JSON, ApiClient에서 씀)를 manifest에 직접 적음(원래 다른 패키지를 따라 들어와 있던 버전)

## 남은 큰 것 (11/06 기준)
- 손그림 텍스처 에셋(땅·바위·건물), 실제 캐릭터(자주 뛰면 탄탄·안 뛰면 둥근 체형 변화가 가능해야 함, 지금은 Mixamo Y Bot 임시), 0에서 3 km/h·20 km/h 발 미끄러짐, 디버그 HUD 교체, 60fps·발열 측정, 앱 화면 흐름(UI Toolkit), 빌드 대상 iOS로 전환(ADR-0007)
- 맵: 11/05엔 크게 다시 만들기로 했으나, 11/07 민재 결정으로 새 블록아웃은 하지 않고 기존 섬 맵(Island01, 도로망·코스 4개)을 그대로 쓴다. 이건 임시이고, 맵·아바타 컨셉아트가 정해지면 에셋을 만들어(또는 구매) 새 맵(BL-43 3km 루프)·새 아바타(BL-42)를 새로 만든다(11/07 민재, 노션 기능 "새 맵·아바타")
