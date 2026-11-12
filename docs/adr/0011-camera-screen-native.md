# ADR-0011: 계기판 촬영 화면(E-03)은 Swift가 통째로 그리고, Unity에는 사진 경로만 돌려준다

- 상태: 승인
- 날짜: 2026-11-12
- 결정한 사람: 김민재 (계획 0033 정할 것 1·2, 비교 그림 `docs/plans/0033-decisions.html`을 보고 "괜찮은 걸로" → 추천안)

## 배경
인증(BL-16–18)을 위해 달린 뒤 러닝머신 계기판을 앱 카메라로 찍는다. 10/29 첫 시험(`docs/product/console_ocr.md`)에서 병목은 인식기보다 **노출**이었다(LED가 하얗게 타고 꺼진 획이 비침). 노출을 제어하려면 iOS `AVCaptureSession`을 직접 써야 하고, Unity 기본 카메라(`WebCamTexture`)로는 노출·셔터를 못 정한다. Figma E-03은 카메라 미리보기 **위에** X·남은 시간·점선 가이드·셔터가 겹쳐 있어, 미리보기와 겹친 UI를 누가 그릴지 정해야 했다. 앱 화면은 모두 UI Toolkit(ADR-0001)이다.

## 선택지
| 선택지 | 장점 | 단점 |
|---|---|---|
| A. E-03만 Swift 화면(`UIViewController`를 Unity 위에 전체 화면으로) | 미리보기가 네이티브 그대로, 노출·찍기와 한 파일, 새로 만들 게 적음 | E-03 한 화면은 UXML이 아님(UI Kit 값을 Swift로 옮김), 에디터에서 진짜 E-03이 안 보임 |
| B. 미리보기 프레임을 Metal 텍스처로 Unity에 넘기고 UI는 UXML | 모든 화면이 UI Toolkit | 텍스처 다리·수명·회전·매 프레임 갱신을 새로 만듦, 시뮬레이터에 카메라가 없어 실기기 전엔 확인 불가 |
| C. 미리보기를 Unity 뒤에 깔고 Unity를 투명하게 | UI는 UXML, 미리보기는 네이티브 | 프레임버퍼 알파 설정이 섬·달리기 화면 렌더링에도 번짐, URP 후처리가 알파를 덮어씀 |

## 결정
A. 그리고 셔터 한 번에 **노출만 다른 사진 3장(브래킷, 0·−1.5·−3 EV)**을 찍는다.
- C# `Native.Camera.ShootAsync(옵션)` → Swift가 E-03을 띄움 → 찍으면 닫고 `{outcome, photos[{path, bias}]}`. 취소·시간 초과·권한 없음·카메라 없음도 같은 답의 `outcome`
- 사진은 앱 임시 폴더에 JPEG, C#에는 경로만(ADR-0009 통로에 사진 바이트를 태우지 않음). 화면 쪽이 업로드 뒤 `Discard`
- 문구(가이드·안내)와 남은 시간·노출 값은 C#이 옵션으로 넘긴다(상준·지환이 C#에서 고침)
- 노출 규칙(가설): 초점·노출 기준점 = 가이드 가운데, 자동 노출의 셔터 상한 1/60초

## 이유
- 촬영은 한 번에 끝나는 짧은 화면이고 겹친 UI가 4개뿐이라 Swift로 옮기는 양이 작다. 노출이 핵심인 기능에서 실패할 곳을 줄이는 게 먼저
- C#이 보는 모양(`ShootAsync` → 경로)은 A·B가 같아서, 나중에 B로 바꿔도 지환의 E-02·E-04 화면은 그대로
- 브래킷: 계기판 종류(LED·LCD·터치스크린)와 헬스장 조명마다 맞는 노출이 다르고(가설), 서버 OCR(BL-17 ①, 11/06 서버 처리)이 가장 잘 읽힌 장을 고르면 된다

## 결과와 위험
- E-03 모양은 상준이 Figma와 나란히 놓고 Swift 값(`ScrunCamera.swift`의 색·크기)을 다듬는다
- 서버 OCR API는 사진 여러 장을 받아야 한다(지환 BL-17과 맞춤). 올리는 데이터가 3배
- 노출 값(EV·셔터 상한)과 브래킷이 실제로 OCR을 낫게 하는지는 **실기기 시험 전까지 가설**. 나아지지 않으면 1장으로 줄이거나 값을 바꾼다
- 브래킷을 못 하는 기기는 자동 노출 한 장(`maxBracketedCapturePhotoCount`)
- 다시 검토할 조건: E-03에 애니메이션·복잡한 UI가 늘어 Swift로 유지하기 힘들어지면 B

## 참고
- 계획 0033, 비교 그림 `docs/plans/0033-decisions.html`, ADR-0009(네이티브 통로)
- Apple [AVCapturePhotoBracketSettings](https://developer.apple.com/documentation/avfoundation/avcapturephotobracketsettings), [AVCaptureVideoPreviewLayer](https://developer.apple.com/documentation/avfoundation/avcapturevideopreviewlayer)
