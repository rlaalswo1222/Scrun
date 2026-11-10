# 계획 0018: S1 앱 뼈대 — 브리지·ApiClient·탭 틀·로그인 (민재 S1)

- 상태: **승인 · 구현 완료(1–6) · 아이폰 실기기 확인(11/10, 권한·에어팟·키체인·카카오톡 로그인)** (11/09 민재, 결정 1–4 추천안 그대로, 결정 5는 계획 0019(오라클 공용 개발 서버)로 대체: 아이폰은 HTTPS만, http 허용 안 켬). 설명 자료 [0018-decisions.html](0018-decisions.html)
  - **1번 완료(11/09)**: `Assets/Scrun/App/`(asmdef `Scrun.App`)에 `Native.Permissions`·`Native.Headphones` 모양, 에디터 가짜 `FakeNative` + 메뉴 Scrun → Fake Native 창, EditMode 테스트 7개. 빌드에서는 진짜 구현(5번) 전까지 `Native`가 바로 예외(가짜가 몰래 "허용"을 주지 않게, 기기에서 가짜로 보려면 `SCRUN_FAKE_NATIVE`). 사용법은 `docs/unity/unity_map.md` "앱 코드 묶음"
  - **2번 완료(11/09)**: `Api.Auth`(카카오 로그인·약관·가입·로그아웃)·`Api.Users`(닉네임 확인), `ApiClient`(응답 껍데기·`ApiException`·재발급 한 번만·재시도 한 번), `Session`(토큰은 `Native.SecureStore`). 테스트 18개(가짜 HTTP: 동시 만료 3개 → 재발급 1번, 재발급 중 로그아웃해도 옛 토큰이 안 살아남, 다른 카카오 계정이 새로 오면 예전 로그인 지움 등), 메뉴 Scrun → 개발 → 서버 로그인 흐름 확인으로 로컬 서버 실제 가입 성공. 에디터 서버 주소는 Scrun → API 서버 창. 로딩·오류 화면(I-07–I-09)은 3·4번 화면과 같이
  - **3번 완료(11/09)**: 앱 씬 + `AppRoot`·`Navigator`(탭 4개, 탭마다 쌓기, 흐름 모드)·`AppScreen`·안전 영역·세로 고정/가로 전환, Pretendard 글꼴, 메뉴 Scrun → Build App Scene. 테스트 8개 추가(전체 33개), 프로젝트 화면 방향 설정(세로가 막혀 있던 것)도 바로잡음. 노치 여백은 Device Simulator·아이폰에서 확인 남음
  - **4번 완료(11/09, 진짜 카카오만 6번)**: 로그인(A-05)·약관(A-06)·프로필 화면, `Native.Kakao`(에디터 가짜), 로딩·오류 공통 처리 `Feedback`(I-07–I-09). 테스트 4개 추가(전체 37개), 로컬 서버로 화면을 눌러 가입까지 확인. 화면을 직접 눌러 보다가 오류창 "다시 시도"가 아무것도 안 하던 버그를 찾아 고침
  - **5번 구현·시뮬레이터 확인(11/10, 맥)**: ADR-0009. Swift `Assets/Plugins/iOS/Scrun/`(통로·권한 4종·이어폰·키체인·카카오 자리) ↔ C# `NativeBridge`·`IosNative`·`IosChannel`, 빌드 메뉴 Scrun → iOS → Xcode 만들기 + `IosPostprocess`(권한 문구·Swift 5·프레임워크·서명 팀, 개인 값은 git 밖 `ScrunLocal.json`). 개발 빌드 전용 "네이티브 점검" 화면. 테스트 11개 추가(전체 48개). 시뮬레이터(iPhone 18 Pro): 카메라·알림 팝업 → 허용이 이벤트·결과로 옴, 모션·헤드폰 모션은 팝업 없는 기기라 2초 안에 `NotDetermined`, 키체인·설정 앱 정상. 키체인을 못 읽어도 앱이 뜨게 `Session` 보강. **아이폰 확인(11/10, 15 Pro Max·iOS 26.5)**: 모션·카메라·알림 팝업 → 허용, 헤드폰 모션은 모션과 같은 권한이라 같이 `Granted`, 에어팟 끼기·빼기·케이스가 이벤트로 옴, 키체인 정상. 다시 설치·재실행 뒤 로그인 유지, 노치에 안 가림. 탭 바 밑 빈 띠·높이·눌림 표시가 남는 것을 고침. **남은 것**: 거부 → 설정 앱에서 허용 → 이벤트, 한글 입력(닉네임)
  - **6번 구현·시뮬레이터 확인(11/10)**: 카카오 SDK 2.30.0(SPM)을 빌드 후처리로 추가, `ScrunKakao.swift`(카카오톡 있으면 앱, 없으면 웹, 취소 null, 카카오톡에서 그냥 돌아오면 2초 뒤 취소). 시뮬레이터에서 카카오계정 웹 로그인 → 공용 서버(`KAKAO_MODE=real`) 실제 가입 → 홈, 재실행 뒤 로그인 유지. 처음엔 SDK 기본(유니버설 링크)이 카카오톡 없을 때 Safari로 열려 웹 로그인으로 안 넘어갔음 → 스킴으로 먼저 확인하게 고침. **아이폰 확인(11/10)**: 카카오톡 없을 때 웹 로그인·취소, 카카오톡 앱으로 넘어가 동의 → 돌아와 공용 서버 로그인까지 됨
- 담당: 민재. 기간 S1(11/08–11/09). 근거: 계획 0017(스프린트·11/08 재분배), ADR-0001(Unity 하나), ADR-0007(아이폰 우선), [API 공통 규칙](../api/README.md)
- 노션 작업 표 S1 민재 6줄: 브리지(BL-6), 카카오 iOS SDK(BL-1), 권한 Swift(BL-2), API 연결 코드(BL-38), 앱 기본 틀(BL-41), 로그인·약관·프로필 화면(BL-1). 서버 쪽 약관·닉네임 확인 API는 끝남(PR #16)

## 왜
Unity 프로젝트엔 아직 앱 화면(UI Toolkit)이 하나도 없고, 서버를 부르는 코드나 iOS 네이티브를 부르는 통로도 없다. 지환의 권한·에어팟 화면, 코스 화면, 이후 모든 화면이 이 세 가지(**네이티브 통로, 서버 통로, 화면 틀**) 위에 올라가므로 S1에 먼저 깐다.

## 순서 (지환이 안 막히는 것부터)
| # | 할 일 | 어디서 | 끝나면 |
|---|---|---|---|
| 1 | **C# 네이티브 모양 + `FakeNative`**: `Native.Permissions`(상태 읽기·요청·설정 앱 열기), `Native.Headphones`(사용 가능·연결됨·바뀜 이벤트). 에디터에선 가짜 구현, 메뉴 창에서 허용/거부/연결/끊김을 바꿔 봄 | 에디터 | **바로 PR** → 지환이 화면 시작 |
| 2 | **ApiClient**: 응답 껍데기 해석, `ApiError(code, status, requestId)`, 토큰 붙이기, 401 `TOKEN_EXPIRED`면 재발급 한 번만(single-flight) 후 재시도, 시간 초과·네트워크 끊김 구분. API 묶음 `Auth`(카카오·가입·재발급·로그아웃·약관)·`Users`(닉네임 확인). 토큰 보관은 `ISecureStore`(에디터 가짜, iOS 키체인) | 에디터 + 노트북 서버 | EditMode 테스트 + `k_123`로 실제 가입·로그인 |
| 3 | **앱 기본 틀**: UI Toolkit 첫 파일(PanelSettings·UXML·USS), 화면 전환(쌓기·뒤로), 아래 탭 4개(홈·기록·친구·내 정보, 내용은 빈 화면), 세로 고정 + 달리기 씬만 가로, 안전 영역(노치) | 에디터 Device Simulator | 아이폰 기종별로 탭·전환이 깨지지 않음 |
| 4 | **로그인·약관·프로필 화면**(A-05, A-06): 카카오 버튼 → 처음이면 약관 동의 → 닉네임(확인 API, 정규화된 값 보여 줌)·키·몸무게 → 가입 → 홈. 다음 실행엔 저장된 토큰으로 바로 홈. 로딩·오류 공통 처리(I-07, I-08, I-09) | 에디터(가짜 카카오 `k_123`) | 아래 "완료 확인" 1–3 |
| 5 | **iOS 전환 + 진짜 브리지**: 빌드 대상 iOS, Swift 파일 `Assets/Plugins/iOS/`, 1번 모양의 진짜 구현(권한 4종·헤드폰 연결·키체인) | 맥, 시뮬레이터 | 시뮬레이터에서 권한 팝업·설정 앱 이동 |
| 6 | **카카오 iOS SDK**: SDK 넣기, URL 스킴·`LSApplicationQueriesSchemes`, 앱 키는 git 밖 로컬 파일에서 빌드 때 주입 | 맥, 시뮬레이터(웹 로그인) → 11/09 아이폰(카카오톡 앱 로그인) | 실제 카카오 계정으로 가입 |

1–4는 맥 없이 오늘 끝까지 간다. 5–6은 맥에서 이어 하고, 센서·에어팟·키체인·카카오톡 앱 로그인은 11/09 아이폰에서 확인한다.

## 브리지 설계 (11/08 설명안 그대로, 승인되면 ADR-0009)
- **C# → Swift**: `[DllImport("__Internal")]` 함수 하나 `scrun_call(reqId, "permissions.request", json)` + 바로 답하는 상태 읽기용 `scrun_query(name, json) → json`. Swift 쪽은 `@_cdecl`로 내보냄
- **Swift → C#**: 시작할 때 등록한 콜백 `(reqId, resultJson)` → C# 큐에 넣고 메인 스레드에서 꺼내 `Awaitable` 완료. 연결·끊김 같은 이벤트도 같은 콜백(`reqId = 0` + 이벤트 이름)
- **센서는 이 통로를 안 씀**: Swift가 속도까지 계산하고 Unity는 매 프레임 결과 숫자만 읽음(S2, 계획 따로)
- **Xcode 프로젝트 종류**: Unity 기본(Objective-C 틀, 우리 Swift 파일은 그대로 들어감) 추천. Swift 틀은 Unity 6.5에 생긴 실험 기능이고 최소 iOS 16을 강제함(우리는 15.0) ([Unity 문서](https://docs.unity3d.com/Manual/ios-swift-xcode-project-type.html))
- 같은 C# 모양에 `IosNative`·`FakeNative` 두 구현 → 에디터와 아이폰에서 화면 코드가 똑같음

## 결정 필요 (추천 먼저)
1. **브리지 방식·Xcode 종류**: 위 설계 + 기본(Objective-C) 프로젝트 → ADR-0009
2. **JSON 라이브러리**: Unity 공식 `com.unity.nuget.newtonsoft-json` 추천(`JsonUtility`는 null·딕셔너리·중첩 선택 칸을 못 다룸) / 직접 파서
3. **카카오 SDK 넣는 방법**: 빌드 후처리 스크립트가 Xcode 프로젝트에 Swift Package Manager 패키지(`kakao-ios-sdk`)를 자동으로 추가(`PBXProject.AddRemotePackageReferenceAtVersion`, Unity 6 문서에 있음). 카카오는 2.23.0부터 SPM만 지원(CocoaPods 중단, [카카오 문서](https://developers.kakao.com/docs/latest/ko/ios/getting-started)). 실제 빌드에서 되는지는 5번에서 확인(가설), 안 되면 Unity가 만든 Xcode 프로젝트에 "덧붙이기(Append)" 빌드로 손으로 한 번 추가
4. **asmdef**: 새 앱 코드만 `Scrun.App` + 테스트 `Scrun.App.Tests`로 묶기 추천(ApiClient 테스트 때문). 기존 달리기 스크립트는 S2에 속도 플러그인 붙일 때 옮김
5. **노트북 서버로 http 허용**: Player Settings "Allow downloads over HTTP"를 **개발 빌드에서만**, 출시 빌드는 HTTPS만. 아이폰은 로컬 네트워크 권한 문구(`NSLocalNetworkUsageDescription`)가 필요할 수 있음(가설, 11/09 확인)

## 하지 않는 것
Apple 로그인·계정 삭제(출시 전, BL-48), 홈·기록·친구·내 정보 탭 내용, 내 정보 API(지환 S2), 권한·에어팟 화면(지환 S1, 민재는 1·5번만), 센서·속도 플러그인(S2), UI 모양 다듬기(상준 UI Kit이 오면 USS 변수만 바꿈)

## 완료 확인 (노션 작업 표 "완료 확인" 칸에 옮김)
1. 에디터 Play: 처음 실행 → `k_123` 로그인 → 약관(필수 빠지면 다음 버튼 꺼짐) → 닉네임 확인(`ＡＢ` 입력 시 `AB`로 보임, 중복·금칙어·형식 문구) → 가입 → 홈 탭. 서버 DB에 사용자·약관 동의·refresh 토큰이 생김
2. 다시 Play → 로그인 화면 없이 홈. access를 만료시키고 동시에 요청 3개 → 서버 재발급 로그 1번, 3개 다 성공. 서버를 끄면 네트워크 오류 화면에 `requestId` 대신 "연결 안 됨"
3. EditMode 테스트: 응답 껍데기·오류 변환·single-flight·재발급 실패 시 로그아웃 상태로
4. `FakeNative` 창에서 권한·에어팟 상태를 바꾸면 이벤트가 옴(지환 화면이 이것으로 개발)
5. 시뮬레이터: 권한 4종(모션·피트니스, 카메라, 헤드폰 모션, 알림) 팝업 → 상태가 C#으로 돌아옴, 거부 후 설정 앱 열기. 카카오 웹 로그인 → 서버 `KAKAO_MODE=real`로 가입
6. 11/09 아이폰: 카카오톡 앱 로그인, 키체인 토큰이 앱 재실행 뒤에도 남음, 에어팟 끼고 빼면 연결 상태 바뀜, 같은 와이파이 노트북 서버 연결

## 위험
- 카카오 SDK + Unity Xcode 프로젝트 합치기는 처음 → 막히면 6번을 11/09로 미루고 1–5부터 PR(시연은 에디터 가짜 로그인으로도 흐름이 보임, 다만 시연 목표는 진짜 로그인)
- 서버가 `KAKAO_MODE` 하나라 에디터(가짜)와 아이폰(진짜)을 동시에 못 씀 → 확인할 때 `.env`를 바꿔 재시작(운영과 개발 서버를 나누는 BL-51 전까지)
