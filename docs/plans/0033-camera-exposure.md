# 계획 0033: 계기판 촬영용 카메라 — 노출 제어 Swift (BL-16 일부, S3 지환 → 민재)

- 상태: **구현 중 · 맥 빌드·아이폰 확인 남음** (2026-11-12 민재 승인: 정할 것 1 (가), 2 브래킷 3장 → ADR-0011. 3은 옵션 값으로 두고 지환·상준이 정함)
- **맥 할 일**(Windows가 적고 맥이 읽음. 맥은 `git pull --rebase` 뒤 위에서부터, 끝낸 줄은 지우고 결과는 아래 "진행"의 맥 줄에. 사용자는 맥에 "0033 맥 할 일 해 줘"만 침)
  1. 브랜치 `feat/camera-exposure` 받아 Unity에서 Xcode 만들기 → `xcodebuild`로 **컴파일만** 확인(`ScrunCamera.swift`는 Windows에서 써서 한 번도 컴파일 안 됨). 오류·경고를 그대로 "진행"에
  2. 아이폰(케이블 페어링된 기기)에 설치 → 로그인 화면 아래 "네이티브 점검" → "계기판 카메라 (브래킷 촬영)": 미리보기·가이드·남은 시간·X·셔터, 찍으면 몇 장·몇 KB인지 로그 줄, 화면 나간 뒤 초록 점이 사라지는지
  3. 사진 받기: `xcrun devicectl device copy from --device <UDID> --domain-type appDataContainer --domain-identifier com.scrun.app --source Documents/camera-check --destination ~/camera-check` → 저장소에 올리지 말고 Windows로 옮겨 OCR(`console_ocr.md` 방식)
- 담당: 민재(11/12 지환 S3 줄 "카메라 노출 제어 Swift"를 가져옴 → 지환에게 알림, 0017 표 고치기). 촬영 화면 E-02·E-04와 OCR·인증(BL-16 화면·17·18)은 지환 S4 그대로, 모양은 상준
- 기준: 공개 백로그 BL-16 ①–⑥, `docs/product/console_ocr.md`(10/29 첫 시험: **병목은 인식기보다 노출**, LED가 하얗게 타고 꺼진 획이 비침), Figma E-02(607:1115)·E-03(607:1174)·E-04(607:1216) 노트, ADR-0009(네이티브 통로), OCR은 서버에서(BL-17 ①, 11/06 확정)

## Figma E-03이 요구하는 것
- 앱 카메라만(갤러리 없음), 어두운 전체 화면에 **미리보기 + 겹친 UI**: 왼쪽 위 X, 위 "남은 시간 0:45", 점선 가이드 프레임과 "계기판이 이 안에 들어오게", "숫자가 선명하게 보이도록 맞춰 주세요", 셔터. 시간 초과 → E-10, 찍으면 → E-04 확인(다시 찍기 가능)
- 시뮬레이터엔 카메라가 없음 → **아이폰 실기기(케이블 페어링)가 있어야 확인 가능**

## 하는 것
1. Swift `ScrunCamera.swift`: `AVCaptureSession`(후면 광각, 사진), 미리보기, 찍기
   - **노출**: 계기판 LED가 타지 않게 낮게. 한 번 누르면 **브래킷 3장**(자동 / −1.5 EV / −3 EV 근처, `AVCapturePhotoBracketSettings`) — 7세그먼트 LED는 어두운 장, LCD·터치스크린은 자동 장이 맞을 것(가설) → 서버 OCR이 고름. 가이드 프레임 가운데에 초점·노출 기준점, 셔터 1/60초보다 빠르게(LED 깜빡임·손떨림, 가설). 수치는 첫 실기기 시험으로 정함
   - 사진은 앱 임시 폴더에 JPEG로 쓰고 경로만 C#에 돌려줌(사진 바이트를 통로에 안 태움). 화면을 떠나면 지움(사진은 서버 OCR 직후 폐기 원칙과 같은 방향)
2. C# `Native.Camera`: `Open`·`Capture` → 파일 경로 3개·`Close`, 권한 없음·카메라 없음 실패. 에디터는 가짜(예시 사진 3장, 지연) → 지환이 E-03 → E-04 흐름을 에디터에서 만들 수 있게. ADR-0009 통로(`scrun_call`) 그대로
3. 미리보기를 그리는 방법(정할 것 1)
4. 확인용 시험 화면(점검 화면 `NativeCheck`에 "카메라" 줄) — 지환의 진짜 E-03 전에 실기기에서 노출만 먼저 시험

## 정할 것
1. **미리보기를 어디서 그릴지**(새 기술 → 고르면 ADR-0011):
   - (가) **E-03만 Swift 화면**(`UIViewController`를 Unity 위에 띄움, 겹친 UI도 Swift). 미리보기가 가장 매끄럽고 노출 제어와 한곳. 대신 E-03 한 화면은 UXML·USS가 아니라 Swift라 상준 UI Kit 값을 Swift에 옮겨야 하고, 지환은 E-02 → `Native.Camera.Shoot()` → 결과 경로 → E-04만 함 **(추천: 단순하고 실패할 곳이 적음, 아이폰 카메라 앱들이 흔히 쓰는 구조. E-03 UI는 작음)**
   - (나) **미리보기 프레임을 Unity 텍스처로**(Swift가 Metal 텍스처를 넘기고 UI Toolkit `Image`에 그림, 겹친 UI는 UXML). 화면 전부 UI Toolkit으로 통일. 대신 Metal·텍스처 수명·회전·프레임마다 넘기기를 새로 만들어야 하고 실기기 전엔 확인 불가 → S4 안에 끝낼 위험
   - (다) 미리보기를 Unity 화면 **뒤에** 깔고 Unity를 투명하게 — 섬 렌더링 설정까지 건드려 빼는 게 좋음
2. **브래킷 3장 vs 1장**: 3장(추천: 노출이 병목이었고 기종마다 맞는 노출이 다름) vs 낮은 노출 1장(올리는 데이터 적음). 서버 OCR(지환 BL-17)과 "사진 여러 장 받기"를 맞춰야 함
3. **남은 시간**: Figma 0:45, 백로그 Q-02 미정 → 지환·상준이 정함(Swift 화면이면 값만 받음)

## 확인 (끝났다고 볼 기준)
- 에디터: 가짜 카메라로 `Shoot` → 경로 3개, 권한 거부·취소·시간 초과 각각 실패 값, EditMode 테스트
- 아이폰(케이블 페어링 뒤): 헬스장 계기판(또는 집 LED 시계)을 브래킷으로 찍어 `console_ocr.md`의 Tesseract 7seg 방식으로 다시 읽음 → 10/29(10칸 중 5칸)보다 나아지는지. 사진은 저장소에 안 올림(유리에 사람이 비침), 결과만 `console_ocr.md`에
- 미리보기 30fps 이상 끊김 없음, 화면을 나가면 카메라 꺼짐(초록 점 사라짐), 다시 들어와도 됨

## 순서
11/14(토) 교수 미팅·시연과 겹치지 않음(시연 범위에 촬영 없음). Windows에서 C#·가짜·Swift 초안 → 맥에서 빌드 → 아이폰(케이블 필요)

## 진행
- 11/12 민재 Windows: BL-16·Figma E-02–E-05 읽음, 계획 작성 → 승인 뒤 구현(`feat/camera-exposure`)
  - Swift `ScrunCamera.swift`: E-03 화면(미리보기 둥근 칸·점선 가이드 + 태그·남은 시간·X·안내·셔터, 색은 UI Kit ink·sky/700), 세션은 `ScrunCaptureEngine`(자기 큐), 브래킷 `AVCapturePhotoBracketSettings`(기기 최대 장수까지, 못 하면 자동 한 장), 노출 기준점 = 가이드 가운데, 셔터 상한 1/60초(`activeMaxExposureDuration`), JPEG를 `tmp/scrun-camera/`에, 1시간 지난 것 정리, 지우기는 그 폴더 안만. 권한은 `ScrunPermissions.request(.camera)`(안 물어봤으면 팝업). 브리지 `camera.shoot`(call)·`camera.discard`(query)
  - C#: `ICamera`·`CameraShot`·`CameraShootOptions`(`Native/Camera.cs`), `IosNative`, 에디터 가짜 `FakeCamera`(권한은 가짜 권한 그대로, 결과·지연은 Fake Native 창, 찍음이면 노출만 다른 설명용 그림 JPEG), 점검 화면 "계기판 카메라"(사진을 Documents/camera-check로 옮김)
  - 테스트: `CameraTests` 새로(이름·JSON 모양, 결과 5종, 사진 없는 찍음·모르는 결과 = 오류, 지우기, 가짜 권한·결과·폴더 밖 안 지움·동시 호출)
  - Figma E-03 세부 수치는 탭이 멈춰 못 읽음 → 화면 캡처와 UI Kit 값으로 그림, 상준이 나란히 놓고 다듬기
