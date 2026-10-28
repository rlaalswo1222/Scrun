# Zwift 벤치마킹 분석 (2026-10-26)

> **내부 참고용.** 보고서에 "Zwift를 분석했다"고 쓰지 말 것 (Zwift 약관상 리버스 엔지니어링 금지).
> 보고서에는 "상용 앱 사례 조사" 수준으로만 쓰고, 아래 내용 중 공개 자료로 확인되는 것만 인용.
> 여기 있는 건 전부 **이름(함수·변수·파일 경로) 기반 정적 관찰**이다. 코드를 디컴파일하거나 앱을 실행·후킹하지 않았다.
> 따라서 "이런 기능/구조가 있다"는 근거는 되지만, "정확히 어떻게 계산한다"는 근거는 아니다. 수치는 하나도 얻지 않았다.

## 대상과 방법

- 대상: Zwift Android `com.zwift.zwiftgame` 1.124.0 (versionCode 167761), S22에서 adb로 base.apk 추출 (46.6MB)
- 도구: REA(APK 인벤토리), Android NDK `llvm-nm`/`llvm-strings`, aapt2, 자체 DEX 문자열 파서
- 분석 스크립트: `docs/research/zwift_scripts/` (APK·덤프 파일은 저장하지 않음)
- 표기: **[관찰]** = 파일에 실제로 있는 이름 / **[추정]** = 이름으로 짐작한 동작 / **[적용]** = 스크런에 쓰는 방법 제안

## 0. 전체 구조

- [관찰] APK 안 구성: `lib/arm64-v8a/libzwiftjni.so` 92.8MB(엔진+게임 전체, C++), `libNoesis.so` 8.2MB(NoesisGUI, UI), `classes.dex` 약 10MB(Java/Kotlin)
- [관찰] Unity 흔적(`libunity.so`, `libil2cpp.so`) 없음 → 자체 C++ 엔진
- [관찰] Manifest `uses-gl-es 0x30000`, `libGLESv3.so` 사용, `glDrawElementsInstanced`·`glDispatchCompute` 임포트, Vulkan 없음
- [관찰] 맵·모델 에셋은 APK에 없음. `zwift::WADStreamReader`, `Streamer::StreamFile` → 설치 후 WAD 묶음 파일을 받아 스트리밍
- [관찰] 엔진 심볼이 거의 지워지지 않음 (내보낸 이름 135,837개) → 이름만으로 구조 파악 가능했던 이유
- [관찰] Java 쪽 패키지: `com.zwift.zwiftgame.ble`(16), `antplus.pluginsampler`(131), `downloader`, `gl` → 블루투스/ANT+ 수신은 Java, 데이터 해석은 C++(`FTMS_ProcessTreadmillData` 등)

## 1. 속도 입력원 (러닝머신 / 발 센서)

- [관찰] 블루투스 FTMS 러닝머신: `FTMS_ProcessTreadmillData(BLEDevice*, const uchar*, int)`, `FTMS_ProcessMachineStatus`, `FTMS_ProcessMachineFeatures`, `isTreadmill(string)`
- [관찰] 발 센서: `isFootPod(string)`, `IsRunPod(...)`, `RunPod_BLE` 클래스(자사 RunPod 전용 프로토콜·펌웨어 업데이트), ANT+ `AntPlusStrideSdmPcc`(Stride SDM), `ANTDevice::ProcessFootpodSpeedData`
- [관찰] 제조사 전용 프로토콜도 따로 처리: `WahooTreadmillControlComponent`, `BowflexTreadmillStatusHandler`, `isDeviceGymConnectSupportedTreadmill`, LAN 기기(`LanExerciseDeviceManager`, mDNS 탐색)
- [관찰] 속도 입력은 "RunSpeed" 슬롯 하나로 관리: `FitnessDeviceManagerImpl::PairRunSpeedSensor`, `g_oldRunSpeedDevice`, UI 문자열 `Device - RUNSPEED`, `BikeEntity::GetPlayerRunSpeed()`
- [관찰] `g_RunSensorSecondsSincePacket` → 센서 패킷이 끊긴 시간을 추적
- [관찰] Stryd 문자열 존재 (특정 발 센서 브랜드 인식)
- [추정] 자동 우선순위가 아니라 **사용자가 페어링 화면에서 속도 소스 하나를 고르는** 구조. 패킷이 일정 시간 안 오면 속도 0(정지) 처리.
- [적용] 스크런: `ISpeedSource` 인터페이스 하나에 `FtmsSpeedSource` / `SensorEstimateSpeedSource`를 꽂는 구조. FTMS가 연결되면 자동으로 우선 사용하고, 없으면 센서 추정. "마지막 데이터 후 N초 지나면 정지" 타임아웃 포함.

### 1-1. (보너스) 러닝머신 경사 자동 제어

- [관찰] `GAME_SetTreadmillIncline(float)`, `TreadmillControlComponent::SetIncline`, `GAME_SetTreadmillInclineScale`, `GAME_SetTreadmillNegativeInclineControlEnabled`, `g_FTMS_GradeLookaheadTime`, `FitnessDeviceManagerImpl::GetTreadmillGradeLookAhead`
- [추정] 맵 경사를 러닝머신 경사로 보냄. 경사 반영 비율(스케일), 내리막 허용 여부 설정, **몇 초 앞의 경사를 미리 보냄**(러닝머신 경사 모터가 느리기 때문).
- [적용] FTMS 제어 지원 기종이면 스크런 코스 고도 → 러닝머신 경사 연동 가능. 캡스톤 범위 밖이면 "향후 확장"으로만 언급.

## 2. 속도 보정 (캘리브레이션) ← 가장 중요

- [관찰] `UI_CalibrateRunSensorController` 클래스: `InitCalibratingLow/Mid/High`, `GetSelectedKPH`, `CalcStats(float&, float&, float*, uint)`, `DeviceIsInCurrentRange`, `LoadMultiPointCalibration`, `CompareCalibrationPoints(VEC3, VEC3)`
- [관찰] 로그 문자열:
  - `Run Calibration: Targets = MidKPH:%1.1f, LowKPH:%1.1f, HighKPH:%1.1f`
  - `Run Calibration: Sample = KPH:%1.1f Cadence:%1.1f Discarded:%s`
  - `Run Calibration: Calibration Mode Results %s = KPH:%1.1f Cadence:%1.1f`
  - `Run Calibration: Sample out of range!`, `WarnMinimumSpeed`, `WarnOutofRange`
  - `RunSpeed MultiCalibration`, `MULTIPOINT_CALIBRATION`, `%s%s_%s_%s_calibration.csv`, `LAST_CALIBRATION`, `LOC_PREVIOUS_CALIBRATION_DATE`
- [관찰] `ExerciseDevice::SetCalibrationScale(float)`, `ExerciseDevice::SetCalibrationPoints(vector<VEC3>)`, `ExerciseDevice::SetSupportsRunCalibration(bool)`, `FIT_SetSpeedScaleCorrection(VEC3, float)`
- [관찰] 단위 선택: `GetSelectedHMorDMI` (km/h 또는 mph)
- [추정] 보정 방식 두 가지:
  1. 단일 배율(`SetCalibrationScale`): 센서 속도 × 계수
  2. **다점 보정**: 사용자가 러닝머신을 저/중/고 속도로 맞추고 각각 달림 → 각 구간에서 (실제 km/h, 케이던스) 샘플을 모으고 범위 밖 샘플은 버림 → 보정점(VEC3) 3개 저장 → 속도별로 다른 보정을 적용
  - 보정 데이터는 기기별로 저장하고 날짜를 표시함
- [적용] 스크런은 걸음 수 × 보폭이므로 같은 구조가 거의 그대로 맞는다:
  - 첫 실행 때 "러닝머신을 8 → 10 → 12 km/h로 맞추고 각 1분" (속도값은 예시, 실험으로 정할 것)
  - 각 구간에서 (실제 속도, 케이던스) → 보폭 = 속도 / 케이던스 계산, 흔들린 샘플은 버림
  - 보폭을 케이던스(또는 속도)의 함수로 보간해서 저장 → 이후 추정에 사용
  - **검증 실험**: 보정 없음 vs 1점 보정 vs 3점 보정, 다른 날 블라인드 오차 비교 → 보고서용 실측 수치 확보 가능

## 3. 페이스 파트너 (봇 러너)

- [관찰] `PacePartner` 클래스: `ParseDynamicPaces`, `ParseGroupChatMessages`, `ParseEndOfEventMessages`, `PacePartnerConfig::GetTargetFramerate`
- [관찰] `ENTITY_TYPE_BOT_PACER`, `k_RoboPacerSleepTimeout`, `UI_Refactor::StartGameUpdater::StartRoboPacer(PlayerNetworkID, ..., WORLD_ID, ...)`, `GetPacerBotFutureRoadId`
- [관찰] 설정 문자열: `Pace Partner - Activity Range`, `Pace Partner - Is Holographic`, `Pace Partner - Show On Minimap`, `Pace Partner - Drops Multiplier`
- [관찰] 분석 이벤트: `PacePartnerJoined`("enters range"), `PacePartnerLeft`("exits range or ends ride"), `distance_with_pace_partners`, `time_with_pace_partner_seconds`, `pace_partner_follower_count`
- [관찰] `UI_ActionListCellTeleportPacers` → 봇 옆으로 순간이동 기능
- [추정] 봇은 서버에서 도는 네트워크 플레이어(네트워크 ID 보유). 일정 거리 안에 들어오면 "합류"로 판정, 함께 달린 거리·시간 기록. 페이스가 동적으로 바뀔 수 있고, 그룹 채팅 메시지(응원 멘트)를 냄. 반투명(홀로그램)으로 그려 사람과 구분.
- [적용] 스크런: 서버 없이 클라이언트에서 돌려도 충분. 봇 = "정해진 페이스로 스플라인 거리만 늘리는 플레이어". 합류 판정은 거리 차 N m 이내. 반투명 머티리얼로 사람과 구분. 접속자가 적은 시연 상황의 핵심 해결책.

## 4. 도로 = 스플라인 + 그래프, 위치 = (도로 ID, 진행값)

- [관찰] `RoadSegmentSpline_CatmullRom`, `RoadSegmentSpline_Bezier`, `RoadGraph`, `RoadGraphNode`, `RoadGraphEdge`, `RoadManager`
- [관찰] 위치 표현: `road_time`/`roadTime`(protobuf 필드), assert 문자열 `outRoadTime >= 0.0 && outRoadTime <= 1.0`, `roadTimeCurr >= 0 && roadTimeCurr <= 1`
- [관찰] `RoadSegment::GetRoadPointLinearDistanceAt`, `VolumeEventTrigger::ConvertDistanceToRoadtime`, `RoadSegment::GetCyclistAltitudeAtRoadTime`, `PlayerRouteDistance`
- [관찰] 차선: `SteeringModule::InitiateLaneChange`, `CollisionResolver::ApplyRepulsionLaterally`, `AvoidRidingOnOppositeLane`, `maxLateralAvoidanceDistanceInCm`
- [관찰] 경로 분기: `Switch By MustTurnRoadTime. Route Decision: Merging onto new road %d from old road %d`, `OffRoutePathPredictor`
- [추정] 위치 = (도로 ID, 0~1 정규화 진행값) + 좌우 차선 오프셋. 거리(m) ↔ roadTime 변환 테이블을 둠. 고도도 roadTime으로 조회. 겹치는 아바타는 좌우로 밀어냄.
- [적용] Unity Splines: 위치 = (코스 ID, 누적 거리 m) + 좌우 오프셋. `SplineContainer.EvaluatePosition(t)`에 거리 → t 변환. 아바타끼리 겹치면 좌우 오프셋으로 비켜 서게.

## 5. 네트워크 동기화

- [관찰] `zwift::protobuf::PlayerState`, `BikeEntity::SendPlayerState`, `BikeManager::ProcessPlayerState`, `NetworkClient::latestPlayerState`
- [관찰] `UdpStatistics::PlayerStateInfo::PLAYER_STATE_AGED_TIMEOUT`, `PLAYER_STATE_STARVATION_TIMEOUT`, `incrementGameInterpolationTimeoutCount`, 문자열 `gameInterpolationTimeout`
- [관찰] `RoadManager::IsRiderNearby`, `nearby_players`, `max_players`, `LeanPlayerState`
- [관찰] `OffRoutePathPredictor`(경로 예측), `CollisionResolver::GetRoadTransitionLinearInterpolationValue`
- [추정] 위치는 UDP + protobuf로 주고받음. 수신 상태가 오래되면(aged), 아예 안 오면(starvation) 타임아웃 처리. 수신 사이에는 보간하고 일정 시간 넘게 안 오면 보간 중단. 주변 플레이어만 관리.
- [적용] 스크런: 상대 상태 = (코스 거리, 속도, 시각)만 전송. 수신 사이에는 "거리 + 속도 × 경과시간"으로 예측, 새 값이 오면 부드럽게 보정. N초 이상 안 오면 아바타 정지/반투명 처리.

## 6. 그래픽 품질 단계

- [관찰] 설정 변수 문자열: `gLODBias`, `gSSAO`, `gFXAA`, `gFoliagePercent`, `sres`(`To Change use: sres WIDTHxHEIGHT`), `g_FORCELOD`
- [관찰] `androidPerformanceMode`, `GFX_GetGraphicsPerfFlagsResScale`, `ZwiftIOS_UpdateFramerateCap`, 하드웨어 정보 이벤트 `gpu_model`, `gpu_vendor`, `gpu_memory_mb`, `gpu_api`
- [관찰] `g_animLODLevel` (애니메이션도 거리별 단순화)
- [추정] GPU 정보로 품질 단계를 정하고, 단계별로 해상도 배율·SSAO·FXAA·풀 밀도·LOD 편향을 바꿈. Android 전용 성능 모드가 있음.
- [적용] Unity URP Quality 2~3단계 + 해상도 배율(Render Scale) + 풀 밀도 + LOD Bias. 첫 실행 때 기기 성능으로 자동 선택.

## 7. 러닝 HUD

- [관찰] `HUD_RenderRunStats()`, `HUD_GetRunPaceData()`, `zwift::hud::RunPaceViewModel`
- [관찰] 단위 문자열 `min/km`, `min/mi`, `SetPaceDisplayUnitsSetting`, `GetPaceRangeText(PaceTypeEnum, float, float, distance_unit)`
- [관찰] 랩/구간 기록: `zwift::hud::splits::SplitsService`, `RunningSplitModel`, `UpdateRunningSplitData`, `GetBestTime(SplitType)`, 수동 split 버튼
- [추정] 러닝 HUD는 페이스(분/km) 중심, 구간(split) 기록과 최고 기록 비교 제공.
- [적용] 스크런 HUD: 페이스(분/km), 거리, 시간, 함께 뛰는 사람과의 간격. 1km마다 자동 구간 기록.

## 8. 걷기·달리기·정지 애니메이션

- [관찰] 러너 애니메이션 파일(남/여/코스튬 각각): `runnerIdle`, `RunnerWalk`, `RunnerWalkFast`, `RunnerWalkFaster`, `RunnerJog`, `RunnerJogFast`, `RunnerRun`, `RunnerSprint`, `RunnerStoreIdle`
- [관찰] `BikeEntity::UpdateRunnerAnimation(float)`, `BikeAnimation::InitRunnerAnimationSets()`, 문자열 `WALK_RUN_SPEED`, `WALK_RUN_CADENCE`
- [관찰] 정지/일시정지: `BikeEntity::SetPlayerPaused`, `VirtualBikeComputer::SetIsPaused`, `g_workoutAutoPauseTime`, `STOPPED`, `PAUSED`
- [관찰] `g_StrideDistance` (보폭 거리 변수)
- [추정] 속도 구간에 따라 걷기 3단계 → 조깅 2단계 → 달리기 → 전력질주, 총 8단계 애니메이션을 섞어 씀. 걷기/달리기 전환 기준을 속도와 케이던스 두 가지로 둠. 일정 시간 멈추면 자동 일시정지.
- [적용] Unity Animator Blend Tree 1D(파라미터 = 속도): Idle / Walk / Jog / Run / Sprint 5개 정도면 충분. 재생 속도는 실제 케이던스에 맞춰 발 구름과 화면이 일치하도록. 정지 N초 → 자동 일시정지.

## 9. 추가로 발견한 것

### 9-1. 실사 영상 코스 동기화 ← 우리 실사 영상 코스와 직결
- [관찰] `RoadSegment::SetVideoSpeeds`, `RoadSegment::SetNodeTimesAndVideoSpeeds`, `zwift::mochi::SeekTask::AwaitVideoCatchup`, 로그 `starting seek, video_time=%lf, road_time=%lf`, `finished seek, ...`, `g_MochiWorldShaderHandle`
- [추정] 영상 기반 월드(내부 이름 mochi로 추정)가 있고, 도로 노드마다 영상 시각과 영상 속도를 매핑해 둠. 플레이어 위치(road_time)에 맞춰 영상을 seek/재생속도 조절하고, 영상이 따라올 때까지 기다리는 상태가 있음.
- [적용] 스크런 실사 코스: 영상 프레임 ↔ 코스 거리 매핑 테이블(촬영 GPS로 생성) → 사용자 거리에 해당하는 영상 시각으로 재생속도 조절. 큰 차이는 seek, 작은 차이는 재생 속도로 맞추기.

### 9-2. 내 기록 고스트 (HoloReplay)
- [관찰] `HoloReplayManager`, `RidePlayback::Recorder`, `RidePlaybackRepository::ReplacePrPlayback`, `fetching PR playback from backend`, `CubicInterpolationNextPositionAndDirection`, `HoloReplayPrivacyEnabled`
- [추정] 내 최고 기록(PR) 주행을 기록해 두었다가 홀로그램으로 재생해 같이 달리게 함. 기록 사이 위치는 3차 보간.
- [적용] 스크런: 이전 달리기의 (시간, 거리) 기록만 저장하면 바로 구현 가능. 봇과 같은 반투명 아바타로 재생. **접속자 없이도 경쟁 요소** → 페이스 파트너와 함께 데모 가치 높음.

### 9-3. Ride On (응원)
- [관찰] `BikeEntity::ReceiveRideOn(PlayerNetworkID, RideOnStyle)`, `com::zwift::zop::RideOn`(protobuf), `ArtConfig::g_RideOnLightConfig`
- [적용] 함께 뛰는 사람에게 버튼 하나로 응원 보내기. 네트워크 메시지 1종 + 이펙트만으로 구현.

## 10. 화면 관찰 (공개 스크린샷 기준)

- 출처: DC Rainmaker 리뷰(2020-01) 러닝 스크린샷 8장 중 4장 확인. 현재 버전은 조명이 개선됐을 수 있음.
- 카메라: 아바타 뒤쪽 위에서 따라감, 아바타는 화면 중앙 아래쪽.
- 스타일: 사실적 비율 + 단순한 질감 + 진한 색. AAA 수준 아님.
- 하늘·조명: 노을(주황), 밤(달빛) 등 시간대 변화, 먼 곳 안개. 분위기의 큰 부분을 차지.
- 도로: 파란 트랙, 흰 차선, 가장자리 주황 발광선, 연석, 관중석.
- 도로변: 풀·꽃·덤불을 매우 빽빽하게 배치. 먼 산·절벽은 단순.
- 아바타: 티셔츠·반바지·머리카락 정도로 단순, 달리기 동작은 자연스러움. 다른 러너 머리 위에 이름·국기.
- 효과: 워크아웃 링(반구 홀로그램), 발광 카드.
- HUD: 위 중앙(페이스·거리·시간·큰 속도·심박), 왼쪽(워크아웃 목록·구간 기록·오늘 기록), 오른쪽(주변 러너 목록 + 시간 차), 오른쪽 위(미니맵), 아래(경사 색 고도 그래프), 갈림길 방향 아이콘.
- 결론: 완성도는 모델링보다 **카메라·달리기 동작·도로·하늘/조명/안개·도로변 식생·HUD의 일관된 마감**에서 나옴 → 코스 1개 범위라면 학생 팀도 "보이는 수준"은 근접 가능. 콘텐츠 양(맵 수·꾸미기 아이템)은 불가.

## 11. 그래픽 구현 계획 (Unity, 폰 기준) — 의견

**전략**: 2~5km 루프 코스 1개를, 카메라에 잡히는 범위만 Zwift 수준으로 완성.

### 맵
1. **도로 (최우선)**: Unity Splines 경로 → 도로 단면(노면·연석·발광선) 메시 생성. 노면 텍스처 하나에 차선·발광선 포함. 실제 장소라면 경로만 OSM·GPS에서 가져오고 주변은 손으로 꾸밈(자동 생성 풍경은 밋밋함).
2. **지형**: Unity Terrain + 레이어(잔디·흙·바위). 먼 산은 단순, 안개로 가림.
3. **식생**: 도로 양옆 10~20m에 풀·꽃·덤불 빽빽하게(GPU 인스턴싱), 멀리는 나무 몇 그루 + 빌보드.
4. **하늘·조명·후처리 (적은 비용으로 효과 최대)**: 하늘 그라디언트 + 안개, URP 블룸·컬러 그레이딩·약한 비네트. 시간대 2개(노을·낮).
5. **소품**: 관중석·아치·배너·표지판. AI 3D 생성(Meshy/Tripo) 대상으로 적합(카메라와 거리 있음).

### 아바타
1. **기본 모델에 투자 집중** (항상 화면 중앙에 크게 보임). AI 생성 캐릭터는 이 거리에서 어색할 가능성 → 에셋 스토어 사실형 인간 캐릭터(리깅 포함, 의상 교체 가능).
2. **애니메이션**: Mixamo 걷기·조깅·달리기·전력질주 → 속도 기반 1D Blend Tree. **재생 속도를 실제 케이던스에 맞춤**.
3. **꾸미기**: 셔츠·바지 색 변경(머티리얼 파라미터).
4. **다른 러너·봇·고스트**: 머리 위 이름표, 봇·고스트는 반투명 홀로그램 머티리얼.

### 카메라·HUD
- Cinemachine 뒤쪽 위 추적 카메라 + 감속.
- HUD는 Zwift보다 축소: 위(페이스·거리·시간), 오른쪽(주변 러너 간격), 아래(고도 그래프).

### 폰 성능
- 거치대 위 발열 고려, 30fps 안정 목표.
- 실시간 조명은 해(Directional) 하나, 나머지 bake. 안개로 그리기 거리 제한. 화면에 그리는 아바타 수 상한. 품질 단계 2~3개.

### 투자 우선순위 ("Zwift 같아 보이는" 기여 순)
1. 카메라 + 아바타 + 케이던스 맞춘 달리기 동작
2. 도로
3. 하늘·조명·안개·후처리
4. 도로변 식생
5. HUD
6. 먼 풍경·소품

주의: Zwift 맵 디자인·에셋을 그대로 베끼지 말 것. 스타일 참고만.

## 12. 11월 재료비 에셋 구매 후보 (카테고리)

> 구체 제품은 구매 전에 고를 것. 에셋 스토어 구매가 "소프트웨어 일회성 구매"로 인정되는지 조교 확인이 먼저 (HANDOFF 2번 항목).

| 우선 | 카테고리 | 무료로 해결 가능? | 고를 때 확인할 것 |
|---|---|---|---|
| 1 | 사실형 인간 캐릭터 (남·여, 러닝복) | 일부 (Mixamo 캐릭터는 품질 낮음) | 휴머노이드 리그, 의상 교체/색 변경, 모바일 폴리곤 수, URP 지원 |
| 2 | 러닝 애니메이션 세트 | 예 (Mixamo) | 걷기~전력질주 단계, 제자리(in-place) 버전 |
| 3 | 자연 환경 팩 (풀·꽃·덤불·나무, LOD 포함) | 일부 | URP·모바일 지원, LOD/빌보드 포함, 스타일 통일 |
| 4 | 하늘·시간대·안개 | 예 (URP 기본 + 직접 작업) | 모바일 성능, 시간대 프리셋 |
| 5 | 도로/스플라인 도구 | 예 (Unity Splines + 직접 메시) | 단면 프로필 지정, 연석·발광선 |
| 6 | 소품 (관중석·아치·배너) | 예 (AI 3D 생성, ProBuilder) | 스타일 통일 |
| - | AI 3D 생성 구독 1개월 | - | Meshy/Tripo/Rodin 무료로 먼저 비교 (HANDOFF 2번) |

무료 도구: URP, Cinemachine, Unity Splines, Unity Terrain, ProBuilder, Mixamo.

## 스크런 적용 우선순위 (의견)

1. **다점 보정** (2번): 정확도 핵심 문제 + 검증 실험으로 보고서 수치 확보
2. **스플라인 거리 기반 위치** (4번): 이동·봇·고스트·네트워크·영상 동기화 전부의 기반
3. **페이스 파트너 + 내 기록 고스트** (3번, 9-2): 혼자 시연해도 "같이 달리는" 경험
4. **속도 소스 인터페이스 + FTMS 우선** (1번)
5. **실사 영상 거리 동기화** (9-1)
6. 나머지(애니메이션 블렌드, HUD, 품질 단계, 네트워크 예측, Ride On)는 구현 단계에서 자연스럽게
