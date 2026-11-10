# 계획 0020: 속도 센서 C# 모양 + 에디터 가짜 — 센서 준비·걸음 감지 (민재 S2)

- 상태: **완료** (11/10 민재 승인, 결정 1–3 추천안 그대로. 아이폰 진짜 구현은 S2 Swift 속도 플러그인 BL-5)
  - 11/10 구현: `App/Native/SpeedSensor.cs`(모양 + 공통 `SpeedSensorBase`·프레임 갱신 `SpeedSensorPump`), `FakeSpeedSensor.cs`, Fake Native 창 "속도 센서" 칸, 테스트 12개(`FakeSpeedSensorTests`, 전체 EditMode 49개 통과), `Scripts/NativeSpeedSource.cs`, `unity_map.md` 사용법. Play에서 시나리오(서 있음 → 걷기 → 달리기)로 이벤트 순서 확인
  - 리뷰(`/code-review`)로 바꾼 것: `Stop()`은 이벤트 없이 꺼짐(일부러 끈 것을 "걸음 없음"·"끊김"과 구분), 센서는 하나라 켠 쪽이 끔(어댑터는 자기가 켠 경우만), 가짜 시계는 프레임 시각(에디터 일시정지 뒤 걸음 수가 뛰지 않게), Play 재시작 때 시나리오 상태도 지움, 키는 프레임당 한 번, 진짜 구현이 없는 빌드에서 어댑터는 예외를 한 번만 내고 속도 0. 그대로 둔 것: 시작 지연을 2초 넘게 주면 '끊김'이 먼저 보임(진짜 센서도 같음)
- 담당: 민재. 기간 S2(11/10–11/11) 앞부분, 반나절. 근거: 계획 0017(11/08 재분배: "센서 준비·걸음 감지는 S2에 C# 모양 + 가짜 먼저"), 계획 0018(같은 방식의 `Native`·`FakeNative`), ADR-0004·0006(폰은 그냥 올려놓기, 버튼 입력 없음 → 걸음으로 자동 시작)
- 이걸 기다리는 것: 지환 S3 **센서 준비 확인(BL-10, D-07·I-01)**, **달림 인식·카운트다운(BL-11, D-08·D-09)**. 민재 S2 보정 화면, S3 아바타(BL-42)도 같은 모양을 씀

## 왜
지금 C#에는 `ISpeedSource.SpeedKmh` 하나뿐이다. 센서 준비 화면에는 "폰 센서 데이터가 들어오는지·에어팟 데이터가 들어오는지·보정값이 있는지"가 필요하다. 카운트다운 화면에는 "걸음이 시작됐는지"가 필요하다. 진짜 구현(Swift 속도 플러그인)은 맥·아이폰이 있어야 하고 S2 뒤쪽이다. 그래서 0018 1번처럼 **모양과 에디터 가짜를 먼저 develop에 올린다.** 지환이 에디터에서 화면을 끝까지 만들고, 나중에 Swift를 붙여도 화면 코드는 그대로 둔다.

## 모양 (안)
`Scrun.App`에 `Native.Speed`(`ISpeedSensor`)를 추가한다. 0018 브리지 설계대로 센서는 호출 통로를 쓰지 않는다. Swift가 속도까지 계산하고, C#은 **한 프레임에 한 번** 결과를 읽는다. 바뀐 것은 C# 쪽이 이벤트로 알린다(메인 스레드).
```csharp
public enum Gait { None, Walking, Running }        // SpeedRecorder state 0·1·2와 같음
public enum SensorFlow { Off, Waiting, Ok, Lost }  // 꺼짐 / 켰는데 아직 안 옴 / 들어옴 / 끊김
public struct SpeedReading {
    public double Time;          // 센서 세션 시작 뒤 초
    public bool HasSpeed; public float SpeedKmh;   // 걸음 없을 땐 HasSpeed=false
    public Gait Gait;
    public float CadenceSpm;
    public int Steps;            // 세션 시작 뒤 걸음 수. 다리 애니메이션이 바로 반응(BL-13 ②)
    public bool UsingAirPods;    // 이번 값에 에어팟을 섞었는지
}
public interface ISpeedSensor {
    void Start(); void Stop();   // 센서 준비 화면에 들어올 때 켜고, 달리기 끝·취소 때 끔
    SpeedReading Current { get; }
    SensorFlow Phone { get; }    // 폰 가속도
    SensorFlow AirPods { get; }  // 헤드폰 모션(연결·권한은 기존 IHeadphones·IPermissions)
    bool HasCalibration { get; } // 보정점이 있는지(없으면 보정 화면으로)
    event Action<Gait> GaitChanged;
    event Action FlowChanged;    // Phone·AirPods 중 하나가 바뀜
}
```
보정 쪽(단계 시작·보정점·저장)은 S2 보정 계획에서 같은 인터페이스에 덧붙인다. 지금은 `HasCalibration` 읽기만 둔다. 기존 달리기 씬용으로는 `ISpeedSource`를 구현한 작은 어댑터(`NativeSpeedSource`)를 두어 `RunnerController`에 그대로 꽂는다.

## 판정 기준 제안 (백로그 열린 질문 "센서 준비 판정 기준"·"걸음 감지 임계값"에 대한 개발 답, 숫자는 가설)
- **폰 센서 Ok**: 최근 1초에 샘플이 80개 이상 들어옴(요청 100 Hz). 2초 동안 안 오면 `Lost`. 실패하면 재시도 안내(BL-10 ③)
- **에어팟**: 연결됨 + 헤드폰 모션 허용 + 최근 1초에 20개 이상(에어팟 프로 3 실측 48.9 Hz, [airpods_flight.md](../speed/airpods_flight.md))이면 Ok. 아니면 실패가 아니라 "이어폰 없이 진행"으로 표시(BL-10 ②)
- **걸음 감지 → 카운트다운**: `Gait`가 `None`이 아니게 되면 시작한다. Swift `SpeedEstimator`의 기준(6초 창에서 걸음 규칙성 0.3 이상, 2.5초 유지)을 그대로 쓴다. 감지까지 몇 초 걸리는지는 아직 재지 않았다(속도가 따라오는 데 5에서 10초 걸린 것은 실측, [live_iphone.md](../speed/live_iphone.md)) → S2 플러그인을 아이폰에 올릴 때 잰다. 카운트다운 중 `None`으로 돌아가면 대기 화면으로 돌아간다(제안, 화면 동작은 지환·상준이 정함)
- **보정값**: `HasCalibration`. **네트워크**는 이 모양에 넣지 않는다. 같이 뛰기 연결은 S3 실시간 계획에서 정한다

## 순서
| # | 할 일 | 끝나면 |
|---|---|---|
| 1 | 모양: `ISpeedSensor`·`SpeedReading`·`Native.Speed`. 빌드에서는 Swift 전까지 0018처럼 바로 예외 | 컴파일 |
| 2 | `FakeSpeedSensor` + **Fake Native 창에 "속도 센서" 칸**: 폰·에어팟 흐름(정상·안 옴·끊김), 보정값 있음/없음, 걸음(없음·걷기·달리기)과 속도 슬라이더, 감지 지연(기본 2.5초). Play 중 키보드 ↑↓(±0.5 km/h)·Space(정지)는 `FakeSpeedSource`와 같게. 버튼 "3초 뒤 걷기 → 10초 뒤 8 km/h 달리기". 걸음 수는 속도에 맞는 케이던스로 늘어남 | 지환이 창만으로 D-07·D-08·D-09 상태를 다 만들어 봄 |
| 3 | EditMode 테스트: 이벤트가 바뀔 때만 한 번씩 옴, 감지 지연, Stop 뒤 걸음 수 0, 흐름 끊김 → `Lost`, Play 다시 시작하면 초기화 | 기존 37개 + 새 테스트 통과 |
| 4 | `NativeSpeedSource` 어댑터 → 섬 씬 아바타를 가짜 센서로 움직여 봄 | 키보드로 아바타 속도가 바뀜 |
| 5 | `docs/unity/unity_map.md` "앱 코드 묶음"에 사용법(화면 예시 코드 5줄), 지환에게 알림 | 바로 PR |

## 같이 확인한 것
- 0018에서 **모션 및 피트니스 권한을 필수**로 두었다. 그런데 우리 속도 계산은 가속도 원본(`CMMotionManager`)만 쓰고, 걸음 수 API(`CMPedometer`)는 쓰지 않는다. Apple 개발자 포럼의 Apple 직원 답변에 따르면 `CMMotionManager`에는 이 권한이 필요 없다([포럼](https://developer.apple.com/forums/thread/762886)). Apple 공식 문서는 직접 확인하지 않았다 → 0018 5번에서 아이폰으로 확인하고, 필요 없으면 권한 화면의 필수 목록에서 뺀다(지환 화면에 영향, 확인 전엔 그대로). 설명 문구 `NSMotionUsageDescription`은 바뀔 경우를 대비해 계속 넣어 둔다

## 하지 않는 것
Swift 속도 플러그인·실제 판정(S2 뒤쪽, 맥), 보정 계산·보정 화면(S2 별도 계획), 센서 준비·카운트다운 화면(지환 S3), 거리·시간 기록(달리기 세션, S3·S5), 멈춤 처리(BL-13·15), 폰을 손에 들었는지 감지(후보, 필요하면 나중에)

## 결정 필요 (추천 먼저)
1. **이름·위치**: `Native.Speed`(`ISpeedSensor`, `Scrun.App`) / 별도 `Speed` 묶음
2. **읽는 방식**: 한 프레임에 한 번 `Current` 읽기 + C#이 만드는 이벤트 / Swift가 매번 콜백
3. **판정 기준**: 위 제안 숫자로 시작하고 아이폰에서 잰 뒤 고침 / 지금 더 조사
