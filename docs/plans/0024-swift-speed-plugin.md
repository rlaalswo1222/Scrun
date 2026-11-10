# 계획 0024: Swift 속도 플러그인 — `Native.Speed` 진짜 구현 (민재 S2 뒤쪽, BL-5)

- 상태: **승인 · 구현 중** (11/10 민재, 결정 1–4 추천안 그대로. 11/10 밤 맥·Windows 동시 진행 시작, 아래 "Swift ↔ C# 약속")
- 진행
  - Windows 1번 끝(11/10 밤): `fusion_live.py`에 `measure_stage`(TooShort·SensorLost·NoSteps·Irregular·Ok), 누적 걸음(봉우리), 걷기 절편(`WALK_OWN` 켬, 4 km/h 이상 걷기 점, 11/10 민재 결정). 숫자: 걸음 없음 문턱 0.12, 걸음 수 215 이상은 벨트 진동, 4 km/h 이하 달리기 판정은 다시 잼(근거·수치 `docs/speed/README.md` 11/10 줄). **맥은 Swift를 이 파일에 맞춘 뒤** `speedlab/golden/replay_20261029-040156.csv`(기록의 보정 구간으로)와 `..._walkcal.csv`(`model_walk_20261029.json` 보정을 불러와 시작, 걷기 절편까지)를 Swift 재생과 비교(열에 `steps` 추가)
  - Windows 4번 끝(11/10 밤): C# `IosSpeedSensor`(칸 → SpeedReading, 단계 JSON, 보정 넘기기) + `IosSpeedNative`(DllImport) + `Native.cs` iOS 줄 + 테스트 `IosSpeedSensorTests`, EditMode 131개 통과. 아이폰 빌드는 Swift `ScrunSpeed.swift`가 들어와야 링크됨(맥)
  - 맥 2번 끝(11/11): `SpeedEstimator.swift`를 `Assets/Plugins/iOS/Scrun/`으로 옮기고(SpeedRecorder는 참조, 저장 보정은 `ios/SpeedRecorder/SavedCalibration.swift`로 뺌) Python 1번과 같게 고침. 재생 도구 `ios/SpeedReplay/run.sh`(swiftc). 결과: 고치기 전 버전이 그날 폰 `estimate.csv` 488칸을 다시 만듦(걸음 상태 같음, 속도 차 평균 0.002·최대 0.035 km/h, 에어팟 샘플 도착 순서 차이). 고친 뒤 `speedlab/golden` 488칸과 모든 열 차 0.0000(속도·걸음 상태·누적 걸음·보정점 수), 10/29 SpeedLab M·B 59구간 단계 판정·구간 걸음 수 59/59 같음(`--stages`, live_stage_check 입력을 소수 자르지 않고 내보냄)
  - 맥 3번 끝(11/11): `ScrunSpeed.swift`(C 함수 7개, 칸 11개, 흐름 판정, 백그라운드면 폰 Lost·재던 단계 SensorLost), `ScrunHeadphones`가 속도 센서용 핸들러 있는 업데이트를 같은 manager로 켬. iOS SDK 타입 검사 통과. 시뮬레이터 켜고 끄기는 C# 4번이 들어온 뒤. Unity 자체 가속도(`accelerometerFrequency` 60)는 꺼 0으로(우리 코드는 안 씀, CoreMotion manager가 둘이 되지 않게)
  - 맥 5번 코드(11/11): 점검 화면에 "속도 센서" 칸(켜기·끄기, 흐름·초당 샘플·걸음 상태·속도·누적 걸음, 첫 걸음부터 걸음 상태가 바뀌기까지 초, 단계 하나 재기 15초). 초당 샘플은 `ISpeedSensorDiagnostics`(새 파일 `SpeedSensorDiagnostics.cs`, `IosSpeedSensor`가 칸 9·10으로 구현). Windows 4번과 합친 뒤 EditMode 131개 통과. 남은 것: 아이폰 빌드(링크)·책상 확인
  - 맥 아이폰 빌드·책상 확인 1차(11/11, 지환 아이폰 15 Pro Max, iOS 26.5, 책상 위, 사람이 옆을 주먹으로 두드림): 시뮬레이터·아이폰 빌드 성공(C 함수 링크). 폰 흐름 Ok 99–100/초, 에어팟 프로 3 Ok 48–49/초·빼면 Off, 걸음이 잡히기까지 두드리기 시작 뒤 4.2·3.1초, 두드리며 단계 재기 Ok(139.6 spm, ln충격 1.039 = 10/29 8 km/h 점 수준). 점검 화면은 로그인이 유지되면 못 들어가서 개발 빌드 마이 탭에도 버튼, 센서가 켜져 있으면 10초마다 상태 줄을 콘솔에
    - **문제 A(Windows 1번 보강, 그 뒤 맥이 Swift)**: 폰을 가만히 둬도 실시간 걸음 상태가 Running·Walking, **222 spm**(찾는 범위 끝), "Walking 12.82 km/h"까지 나옴. 1번에서 찾은 벨트 모터 진동과 같은 모양인데 `STILL_ENV`·`CAD_EDGE`가 단계 판정에만 있고 실시간(`update`)에는 없음 → 러닝머신 옆 발판에 서 있어도 카운트다운이 시작될 것(가설). 할 일: `update`의 걸음 후보에도 같은 두 규칙(6초 창 `env_level < STILL_ENV` 또는 `cad >= CAD_EDGE`면 0), 10/29 M·B 재생으로 걷기·달리기 구간 판정이 그대로인지 확인, `speedlab/golden` 다시 만들기 → 맥이 Swift를 맞추고 재생 비교 → 아이폰에서 가만히 둔 폰이 None인지
    - **문제 B(맥)**: 가만히 둔 폰의 단계 재기가 NoSteps가 아니라 SensorLost(흐름은 내내 Ok 100/초). 원인 모름(가설: 단계 중 앱 비활성). Swift가 실패 이유를 콘솔에 남기게 함 → 다음 아이폰 확인에서 다시
    - 남은 책상 확인: 가만히 둔 단계 재기 NoSteps, 모션 권한 거부 뒤 가속도, 10분 CPU·발열
- 담당: 민재. 기간 S2 뒤쪽(11/11)에서 S3 앞(11/12), 11/13 시연에 들어감. Python·C# 일부는 Windows, Swift·아이폰은 맥
- 근거: 계획 0020(`ISpeedSensor` 모양 + 가짜, 진짜는 이 계획), 계획 0023 6번(단계 재기·걷기 절편·실패 판정을 Swift에), ADR-0009(센서는 JSON 통로를 쓰지 않음), 계획 0010·[live_iphone.md](../speed/live_iphone.md)(SpeedRecorder 안 실시간 추정, 달리기 5구간 0.16 km/h, 1명·같은 날 보정이라 낙관적)
- 먼저 있어야 하는 것: PR #33(보정 메서드가 들어간 `ISpeedSensor`)이 develop에 합쳐질 것. 구현 브랜치는 그 뒤 develop에서 `feat/speed-plugin`
- 이걸 기다리는 것: 아이폰에서 보정(0023 완료 확인 3), 지환 센서 준비·카운트다운(BL-10·11, 지금은 가짜로 만드는 중), 아바타 속도(BL-42), 11/13 시연

## 왜
지금 아이폰 빌드에서 `Native.Speed`는 바로 예외를 낸다. 속도 계산은 이미 Swift로 있다(`ios/SpeedRecorder/SpeedEstimator.swift`, `speedlab/fusion_live.py`와 한 쌍). 이 계획은 그 계산을 Unity 앱 안에 넣고, C#의 `SpeedSensorBase`가 요구하는 것(프레임마다 값 읽기, 센서 흐름, 걸음 수, 보정 단계 하나 재기, 보정 불러오기)을 채운다. 0023에서 정한 걷기 절편과 단계 실패 판정은 **Python에서 먼저 만들고 기록으로 확인한 뒤** Swift로 옮긴다. 기준 구현이 Python이기 때문이다(두 파일 머리 주석 규칙).

## 모양
```
CMMotionManager 100 Hz ─┐                 ┌─ scrun_speed_read(double* out, n)   매 프레임, 숫자 칸 배열
CMHeadphoneMotion 50 Hz ─┼─ 직렬 큐 ─ SpeedEstimator ─┤
 (ScrunHeadphones와 같은 manager)          └─ 보정: stage_begin / stage_end / stage_cancel / load   드묾, 작은 JSON
```
- **Swift `ScrunSpeed.swift`**(새 파일, `Assets/Plugins/iOS/Scrun/`): 센서 켜기·끄기, 직렬 큐에서 `SpeedEstimator`에 샘플 넣기, 최신 결과를 잠금으로 보호해 한 칸에 둠. 큐 밖(메인 스레드)에서는 이 칸만 읽는다
- **C 함수**(ADR-0009의 `scrun_call`·`scrun_query`와 별도, 속도 묶음 전용): `scrun_speed_start()`, `scrun_speed_stop()`, `scrun_speed_read(double* out, int n)`, `scrun_speed_stage_begin(double kmh)`, `scrun_speed_stage_end() → JSON`, `scrun_speed_stage_cancel()`, `scrun_speed_load(JSON) → bool`. 매 프레임 읽는 것만 숫자 배열로 하고(JSON·문자열 없음), 드문 보정 호출은 결과가 점 하나라 작은 JSON(받은 쪽이 `scrun_free`)
- **읽는 칸**(순서 고정, Swift·C# 두 파일이 짝): 시각, 속도(없으면 NaN), 걸음 상태 0·1·2, 걸음 수(spm), 누적 걸음, 에어팟 섞음, 폰 흐름, 에어팟 흐름, 지난 1초 폰 샘플 수, 에어팟 샘플 수
- **C# `IosSpeedSensor : SpeedSensorBase`**: `Sample()`은 `scrun_speed_read` 한 번, `OnBeginStage`·`OnEndStage`·`OnCancelStage`·`OnCalibrationApplied` 네 개는 위 함수. `Native.cs`의 iOS 줄을 이것으로. C 함수 호출은 작은 인터페이스 뒤에 두어 에디터 테스트에서 가짜로 바꾼다(`IosChannel` 방식)
- **헤드폰 모션 manager 하나**: 지금 `ScrunHeadphones`는 이어폰 연결을 보려고 핸들러 없이 업데이트를 켠다. 속도 센서가 켜져 있으면 같은 manager를 **핸들러 있는 업데이트**로 다시 켜고, 끌 때 원래대로 돌린다(manager를 둘 만들지 않음, ScrunHeadphones 머리 주석에 이미 적어 둔 방향). 연결 알림은 그대로 ScrunHeadphones가 보낸다
- **시계**: 폰·에어팟 모두 기기 부팅 뒤 초(`CMDeviceMotion.timestamp`) 그대로. 에어팟 시각이 2초 넘게 어긋나면 받은 시각을 쓰는 SpeedRecorder 처리는 가져온다. 노트북 시계 맞추기(SpeedRecorder `ServerClient`)는 안 가져온다

## Swift ↔ C# 약속 (11/10 확정, 맥·Windows가 동시에 만들기 위해 먼저 정함. 바꾸면 두 쪽을 같이)
C 함수(Swift `@_cdecl`, C# `[DllImport("__Internal")]`). 모두 **메인 스레드에서** 부른다. Swift는 필요하면 모션 큐로 `sync`해서 처리한다.
| 함수 | 하는 일 |
|---|---|
| `void scrun_speed_start(void)` | 폰 가속도 100 Hz + (연결·허용이면) 헤드폰 모션 켜기. 이미 켜져 있으면 아무것도 안 함. 시각 0을 여기로 |
| `void scrun_speed_stop(void)` | 끄기. 헤드폰 모션은 `ScrunHeadphones`가 지켜보는 중이면 원래 방식(핸들러 없음)으로 돌림. 열린 단계는 취소 |
| `int scrun_speed_read(double* out, int n)` | 아래 칸을 `out[0..]`에 채우고 채운 칸 수를 돌려줌(`min(n, 11)`). 꺼져 있으면 시각 0·흐름 Off |
| `void scrun_speed_stage_begin(double kmh)` | 지금부터 단계 기록(앞 5초는 버림) |
| `char* scrun_speed_stage_end(void)` | 단계를 끝내고 판정 JSON(받은 쪽이 `scrun_free`): `{"result":"Ok","point":{"v":8,"limp":2.41,"cad":158.2,"amp":0.62,"running":true}}`. 실패면 `{"result":"Irregular","point":null}`. `result`는 `Ok`·`NoSteps`·`Irregular`·`SensorLost`·`TooShort`(C# `CalStageResult` 이름 그대로). `amp`는 에어팟을 못 썼으면 `null` |
| `void scrun_speed_stage_cancel(void)` | 열린 단계 버림 |
| `int scrun_speed_load(const char* modelJson)` | 보정 모델(`{"version":1,"points":[...]}`, C# `CalibrationModel`) 불러와 맞춤. 성공 1, 모양이 틀리면 0(지금 보정 그대로). `null`·빈 문자열이면 보정 지움 + 1 |

`scrun_speed_read` 칸(`double`, 순서 고정):
| # | 값 | 비고 |
|---|---|---|
| 0 | 칸 버전 = 1 | C#이 다르면 예외 한 번 |
| 1 | 시각(초, start 뒤) | |
| 2 | 속도 km/h | 없으면 NaN(→ `HasSpeed=false`) |
| 3 | 걸음 상태 0·1·2 | `Gait` None·Walking·Running |
| 4 | 걸음 수 spm | 없으면 0 |
| 5 | 누적 걸음 | start 뒤, 감지 지연 없이 |
| 6 | 에어팟 섞음 0·1 | |
| 7 | 폰 흐름 0·1·2·3 | `SensorFlow` Off·Waiting·Ok·Lost |
| 8 | 에어팟 흐름 0·1·2·3 | 연결·허용 없으면 0 |
| 9 | 지난 1초 폰 샘플 수 | 점검 화면용 |
| 10 | 지난 1초 에어팟 샘플 수 | 점검 화면용 |

- 걷기 절편·단계 판정의 숫자·누적 걸음 방식은 1번(Python)에서 정해지면 Swift를 맞춘다. 그 전까지 Swift는 지금 `SpeedEstimator` 그대로 + 단계 판정은 아래 "판정 기준"의 규칙(NoSteps 문턱은 임시값), 누적 걸음은 임시로 케이던스 × 시간
- 맥·Windows가 같은 브랜치 `feat/speed-plugin`에 올린다. 파일이 갈림: 맥은 `Assets/Plugins/iOS/Scrun/*.swift`·`ios/SpeedRecorder`·Swift 재생 도구, Windows는 `speedlab/`·C# `App/Native/IosSpeed*.cs`·테스트. 올리기 전 `git pull --rebase`. 진행 기록은 이 계획의 "진행"에 줄을 나눠(맥 / Windows) 적고, `handoff/minjae.md`는 Windows 쪽만 고친다(같은 사람 파일이라 겹침)

## 판정 기준 (계획 0020에서 정한 것, 숫자는 가설이고 아이폰에서 고침)
- **폰 흐름**: 지난 1초 샘플 80개 이상이면 Ok, 2초 동안 안 오면 Lost. **에어팟**: 헤드폰 모션이 연결돼 있고 지난 1초 20개 이상이면 Ok, 연결이 없으면 Off(실패 아님)
- **걸음 상태·속도**: `SpeedEstimator` 그대로(6초 창, 걸음 규칙성 0.3, 2.5초 유지, 2초 중앙값). 지연 줄이기는 이 계획에서 안 함
- **누적 걸음 수(새로)**: `SpeedReading.Steps`는 감지 지연 없이 늘어야 한다(다리 애니메이션, BL-13). 지금 `SpeedEstimator`엔 없다. 포락선 봉우리를 세는 방식으로 넣는다(가설: 0.27초 안에 봉우리 하나, 최근 6초 상위 값 기준 문턱). 기록으로 "케이던스 × 시간"과 3% 안에서 맞는지 Python에서 먼저 확인
- **단계 하나 재기(0023)**: 단계 시작 + 5초부터 끝까지의 폰 신호로 판정. 잴 시간이 3초 미만 → TooShort, 그 사이 폰 흐름이 끊김 → SensorLost, 신호 세기가 서 있을 때 수준 → NoSteps, 걸음 규칙성 0.3 미만 → Irregular, 아니면 Ok와 점 하나(`v`·`limp`·`cad`·`amp`·`running`). NotFaster는 C#이 이미 판정. "서 있을 때 수준"의 숫자는 걸음 없는 부분과 걷기 2 km/h 구간 사이에서 정함. 10/29 기록엔 `still` 구간 표시가 없어서(`segments.csv`가 거의 비어 있음) 기록 앞뒤의 걸음 없는 부분을 손으로 골라 쓰고, 모자라면 아이폰 책상 확인 때 "벨트 옆 발판에 30초 서 있기"·"폰을 손에 들고 30초"를 새로 기록
- **걷기 절편(0023)**: 걷기 기울기는 공통 0.0732 그대로, 절편만 이 사람의 걷기 점들로(점들의 `v − 0.0732·cad` 평균). 걷기 점이 없으면 공통 절편 −3.45. **11/10 민재 결정: 켬(`WALK_OWN = True`), 절편은 4 km/h 이상 걷기 점으로(`WALK_MIN_KMH`, 없으면 있는 걷기 점 전부)**. 2 km/h는 오를 때·내릴 때 걸음 수 차이가 가장 크고 걸음 감지가 불안정. 10/29 두 사람에선 뚜렷한 이득이 안 보였지만 공통 공식이 바로 그 두 사람(다리 길이 비슷)으로 만든 것이라 키가 다른 사람을 대표 못 함 → 헬스장 여러 명으로 다시 확인
- **1번에서 더한 판정(11/10, 10/29 기록 근거)**: 걸음 수가 215 이상(찾는 범위 끝)이면 NoSteps(벨트 모터 진동), 안내 속도 4 km/h 이하인데 달리기로 나오면 Irregular(걸음 수를 두 배로 잡음)

## 순서
| # | 할 일 | 어디서 | 끝나면 |
|---|---|---|---|
| 1 | Python: `fusion_live.py`에 걷기 절편·단계 재기(판정 포함)·누적 걸음 수. 재생 스크립트 `speedlab/exp/replay_live.py`(기록 CSV → 0.5초마다 결과 CSV + 단계 판정) | Windows | 10/29 M·B 기록으로 걷기 오차가 [calibration_design.md](../speed/calibration_design.md) 숫자(M 0.14·B 0.25)와 같음. 걸음 없는 부분이 NoSteps, 보정 구간이 Ok |
| 2 | Swift `SpeedEstimator.swift`를 같은 내용으로 고치고 **플러그인 폴더로 옮김**(결정 1). 맥에서 `swiftc`로 같은 재생 명령줄을 만들어 1의 결과와 비교 | 맥 | 0.5초 칸마다 속도 차 0.01 km/h 이하, 걸음 상태·단계 판정 모두 같음. 고치기 전 버전은 `20261029-040156/phone/estimate.csv`(폰이 그날 낸 값)를 다시 만들어 냄 |
| 3 | `ScrunSpeed.swift` + C 함수 + 헤드폰 manager 같이 쓰기 + 앱이 백그라운드로 가면 흐름 Lost | 맥 | 컴파일, 시뮬레이터에서 켜고 끄기(가속도는 없음 → 폰 흐름 Lost가 맞음) |
| 4 | `IosSpeedSensor.cs` + `Native.cs` iOS 줄 + EditMode 테스트(가짜 C 함수로: 칸 읽기, NaN이면 HasSpeed false, 단계 결과 JSON 읽기, 깨진 JSON이면 SensorLost) | Windows나 맥 | 기존 EditMode 79개 + 새 테스트 통과 |
| 5 | 네이티브 점검 화면에 "속도 센서" 칸(흐름·초당 샘플·걸음 상태·속도·걸음 수) | 맥 + 아이폰 | 완료 확인 1 |
| 6 | 러닝머신에서 보정 → 달리기 | 아이폰 + 러닝머신 | 0023 완료 확인 3 |
| 7 | 문서: `unity_map.md` 사용법, `docs/speed/README.md`(걷기 절편·단계 판정·실측), ADR-0009에 "속도는 전용 C 함수" 한 줄, 0020·0023 상태 | | PR |

## 완료 확인
1. **아이폰(책상, 러닝머신 없이)**: 점검 화면에서 폰 흐름 Ok·초당 약 100. 에어팟을 끼면 Ok·약 50, 빼면 Off. 폰을 책상에 평평하게 놓고 옆을 주먹으로 분당 약 160번 일정하게 두드리면(계기판에 전해지는 발 충격 대신, 가설) 걸음 수가 바로 늘고 걸음 상태가 바뀜 → **걸음이 잡히기까지 몇 초인지**와 두드림 세기(걸음 없음 문턱 0.12를 넘는지, 문턱은 S22 값이라 아이폰 확인용) 기록. 단계 재기는 안 두드리면 NoSteps, 두드리면 Ok. 손에 들고 걷는 건 쓰지 않음(팔 흔들림이 섞이고, 0023에서 "손에 듦"은 NoSteps가 나와야 하는 경우라 결과를 판단할 수 없음). 책상 확인은 연결 확인만, 정확도는 완료 확인 2. 모션 권한을 거부한 상태에서도 가속도가 오는지(0020에서 남긴 질문, 오면 권한 필수 목록 조정을 지환에게). 10분 켜 둔 뒤 CPU·발열(Xcode 계측)
2. **러닝머신**: 보정 7단계 → 바로 달려 보정 범위 안 속도 3개 이상과 범위 밖 하나를 계기판과 비교. 기록 원본은 `data/runs/`, 사람·키·몸무게·러닝머신 기종·폰 위치 메모, 결과는 `docs/speed/README.md`
3. 에디터는 지금처럼 가짜(`FakeSpeedSensor`)로 돌고 아무것도 안 바뀜

## 하지 않는 것
에어팟 비중(2.598)·공중 시간 기준 바꾸기(헬스장 비교 뒤, handoff "속도 추정 다음 실험"), 속도가 따라오는 지연(5에서 10초) 줄이기, 끊김 타임아웃·`ISpeedSource` 추상화(competitor_comparison.md 2절, 보류), FTMS, 안드로이드, 폰을 손에 들었는지 감지, 백그라운드에서 계속 재기

## 결정 (11/10 민재, 모두 추천안)
1. **Swift 계산 파일을 어디에 둘까**: (정함) `SpeedEstimator.swift`를 `Assets/Plugins/iOS/Scrun/`으로 옮겨 원본 하나로 두고, SpeedRecorder Xcode 프로젝트는 그 파일을 경로로 참조 / 복사본 두 개(SpeedRecorder는 실험 앱이라 그대로). 복사본이면 Python까지 세 군데를 같이 고쳐야 해서 어긋나기 쉽다
2. **매 프레임 읽는 방식**: (정함) 숫자 칸 배열(`double*`) / C 헤더에 구조체 정의(필드 이름이 생기지만 Swift·C 구조체 배치를 맞출 헤더가 하나 더 생김) / `scrun_query` JSON(ADR-0009가 피하라고 한 방식)
3. **누적 걸음 수**: (정함) 포락선 봉우리 세기, 기록으로 확인 / 케이던스 × 시간(쉽지만 걸음이 잡히기 전 몇 초 동안 0이라 다리가 늦게 움직임)
4. **1번(Python)을 맥 작업 전에 Windows에서 먼저 할까**: (정함) 예. 맥 시간은 Swift·아이폰에만 쓰고, 맥에서는 재생 비교가 같은지만 본다
