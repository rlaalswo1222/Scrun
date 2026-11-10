# ADR-0009: Unity와 iOS 네이티브는 C 함수 3개 + 콜백 하나로 잇고, 센서는 이 통로를 쓰지 않는다

- 상태: 승인
- 날짜: 2026-11-10
- 결정한 사람: 김민재 (11/09 계획 0018 결정 1 추천안 그대로 승인, 11/10 구현·시뮬레이터 확인)

## 배경
앱 화면(UI Toolkit, C#)이 아이폰 기능(권한 팝업·에어팟 연결·키체인·카카오 로그인)을 불러야 한다(ADR-0001 Unity 하나, ADR-0007 아이폰 우선). 기능마다 따로 잇는 방법을 정하지 않으면 화면이 늘 때마다 Objective-C 접착 코드와 스레드 문제가 다시 생긴다. 지환의 권한·에어팟 화면(S1)이 이 모양 위에서 만들어지고 있어 S1 안에 정해야 했다.

## 선택지
| 선택지 | 장점 | 단점 |
|---|---|---|
| A. 함수 3개(`scrun_call`·`scrun_query`·`scrun_free`) + 등록 콜백 하나, 이름 + JSON으로 구분 | 기능을 더해도 C 함수가 안 늘어남. C#은 `Awaitable`로 기다림. 에디터에서 가짜 통로로 같은 C# 코드를 테스트 | JSON 만들고 읽는 비용(권한·로그인처럼 드문 호출이라 무시할 만함). 이름 오타는 실행해야 드러남 |
| B. 기능마다 `[DllImport]` 함수 | 타입이 분명 | 기능마다 C·C# 양쪽 함수, 비동기 답마다 콜백 따로 |
| C. `UnitySendMessage`로 답받기 | Unity 기본 | 게임오브젝트 이름에 묶임, 요청-답 짝 맞추기를 직접, 문자열 하나만 |
| D. Swift 틀 Xcode 프로젝트(Unity 6.5 실험 기능) | Swift로 앱 진입점 | 실험 기능, 최소 iOS 16 강제(우리는 15.0) ([Unity 문서](https://docs.unity3d.com/Manual/ios-swift-xcode-project-type.html)) |

## 결정
A + Unity 기본(Objective-C 틀) Xcode 프로젝트.
- C# → Swift: `scrun_call(요청 번호, 이름, JSON)`(답은 나중에), `scrun_query(이름, JSON) → JSON`(바로 답: 상태 읽기·키체인), 받은 문자열은 `scrun_free`
- Swift → C#: 시작할 때 `scrun_set_callback`으로 등록한 콜백 `(요청 번호, JSON)`. 요청 번호 0은 이벤트(`{"event": "permission" | "headphones", ...}`)
- 답 모양: `{"ok": true, "value": ...}` / `{"ok": false, "error": "..."}` → C#에서 `NativeException`
- 콜백은 큐에 넣기만 하고 Unity PlayerLoop(PreUpdate)에서 꺼내 메인 스레드에서 `Awaitable`을 끝낸다
- 문자열은 UTF-8 바이트로 직접 넘긴다(IL2CPP 기본 문자열 변환에 기대지 않음)
- **센서·속도는 이 통로를 쓰지 않는다**: Swift가 100 Hz 센서를 읽어 속도까지 계산하고, Unity는 매 프레임 결과 숫자만 읽는다(S2 따로)

## 이유
- 권한 4종·이어폰·키체인·카카오가 같은 C 함수 3개로 들어갔다(11/10). 카카오(계획 0018의 6번)도 Swift `case` 하나만 더함
- 스레드: iOS 콜백(카메라 권한, 알림 설정, 헤드폰 모션)은 아무 스레드에서나 온다. 큐 + 메인 스레드에서 끝내기로 화면 코드는 스레드를 신경 쓰지 않는다
- 테스트: `INativeChannel`만 바꾸면 C# 쪽 전부(요청-답 짝, 실패, 이벤트, 다른 스레드의 답)를 에디터에서 확인(`IosNativeTests` 11개, 11/10 통과)
- 시뮬레이터(iPhone 18 Pro, 11/10): 카메라·알림 팝업 → 허용이 이벤트와 요청 결과로 C#에 옴, 모션·헤드폰 모션은 팝업이 없는 기기라 2초 안에 `NotDetermined`로 끝남(멈추지 않음), 키체인 저장·읽기·지우기, 설정 앱 열기

## 결과와 위험
- 새 아이폰 기능은 Swift `ScrunBridge.swift`의 `case` + C# `IosNative`에 메서드 하나. 이름 문자열은 두 파일이 짝(바꾸면 같이)
- 매 프레임 많이 오가는 값(센서·속도)을 이 통로에 태우면 JSON 비용과 한 프레임 지연이 생긴다 → 이 규칙을 지킨다
- 가설(아이폰에서 확인): 헤드폰 모션 권한 팝업이 이어폰 없이도 뜨는지, 에어팟 연결 알림을 받으려면 모션 업데이트를 켜 둬야 하는지
- 키체인은 앱을 지워도 남아서, 새 설치 뒤 처음 쓸 때 예전 값을 지운다(`UserDefaults` 표시). 접근성은 이 기기에서만·첫 잠금 해제 뒤

## 참고
- 계획 0018 "브리지 설계", [0018-decisions.html](../plans/0018-decisions.html)
- 코드: `unity/TestRun/Assets/Plugins/iOS/Scrun/*.swift`, `Assets/Scrun/App/Native/{NativeBridge,IosNative,IosChannel}.cs`, 빌드 `App/Editor/{IosBuild,IosPostprocess}.cs`
