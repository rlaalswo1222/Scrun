# 속도 테스트 기록 앱 (아이폰 + 에어팟)

아이폰 가속도·자이로(100Hz)와 에어팟 머리 움직임(약 25Hz)을 기록해서 0.5초마다 노트북(server.py)으로 보낸다.
정해 둔 속도 순서대로 에어팟 음성으로 안내하고, 구간 시각은 자동으로 기록한다.
분석은 노트북이 실시간으로 하고, 앱 아래쪽에 그 분석 화면이 같이 뜬다.

## 맥북에서 처음 한 번 설치 (Xcode 설치 빼고 20~30분)

### 0. 준비
1. App Store에서 **Xcode** 설치 (용량이 커서 30분~1시간). 처음 열 때 추가 구성요소에서 **iOS** 체크 → 설치
2. Xcode → **Settings → Accounts → +** → 앱을 설치할 아이폰 주인의 **Apple ID**로 로그인

### 1. 프로젝트 만들기
1. Xcode → **Create New Project** → **iOS** → **App** → Next
2. Product Name: `SpeedRecorder` / Team: 로그인한 이름 (Personal Team) / Interface: **SwiftUI** / Language: **Swift** / 나머지 체크는 끄기
3. 아무 곳에나 저장

### 2. 파일 바꿔 넣기
1. 왼쪽 목록에서 자동으로 생긴 `ContentView.swift`, `SpeedRecorderApp.swift` 우클릭 → Delete → **Move to Trash**
2. 이 폴더 `SpeedRecorder/`의 `.swift` 파일 6개를 왼쪽 목록의 노란 `SpeedRecorder` 폴더로 끌어다 놓기 → **Copy items if needed** 체크 → Finish

### 3. 설정
왼쪽 맨 위 파란 **SpeedRecorder** 아이콘 → 가운데 TARGETS의 **SpeedRecorder**

- **General** 탭: Minimum Deployments → **iOS 17.0**
- **Info** 탭: 아무 줄에 마우스를 올리면 나오는 **+**로 아래 항목 추가 (이름은 입력하면 자동완성)

  | 키 | 값 |
  |---|---|
  | Privacy - Motion Usage Description | 러닝머신 속도를 재려고 폰과 에어팟 움직임을 기록해요 |
  | Privacy - Local Network Usage Description | 같은 와이파이의 노트북으로 기록을 보내요 |
  | App Transport Security Settings | (추가한 뒤 ▶ 펼쳐서 안에 + → **Allow Arbitrary Loads = YES**) |
  | Application supports iTunes file sharing | YES |
  | Supports opening documents in place | YES |

  - Allow Arbitrary Loads를 빠뜨리면 노트북(http 주소)에 연결이 안 된다
  - 아래 두 개는 폰에 백업된 CSV를 **파일 앱 → 나의 iPhone → SpeedRecorder**에서 꺼내 보려고

### 4. 아이폰에 설치
1. 아이폰을 케이블로 맥북에 연결 → 아이폰에서 **신뢰**
2. 아이폰 **설정 → 개인정보 보호 및 보안 → 개발자 모드** 켜기 (재시작됨. 메뉴가 안 보이면 Xcode에 한 번 연결한 뒤 다시 보기)
3. Xcode 맨 위 가운데 기기 선택에서 그 아이폰 선택 → 왼쪽 위 **▶**
4. "신뢰하지 않는 개발자"가 뜨면 아이폰 **설정 → 일반 → VPN 및 기기 관리** → Apple ID → **신뢰** → 다시 ▶

### 빌드 오류가 나면
- 이 코드는 아직 맥에서 한 번도 빌드해 보지 않았다. 빨간 오류가 뜨면 **왼쪽 ⚠️ 탭(오류 목록) 스크린샷**을 보내 주세요.
- Swift 동시성 오류가 많이 나면 TARGETS → **Build Settings** → 검색 "Swift Language" → **Swift 5**
- 무료 Apple ID로 설치한 앱은 **7일 뒤** 안 열린다 → 다시 연결하고 ▶ (테스트 전날 한 번 다시 설치 추천)

## 헬스장에서

1. 노트북(LG 그램)을 **아이폰 핫스팟**에 연결 → `server.py` 실행
   (`.venv\Scripts\python server.py`, 창에 뜨는 주소의 숫자 확인: `ipconfig`의 IPv4)
2. 아이폰 앱 → 노트북 주소 `http://<노트북 IP>:8000` → **연결 확인**
3. 에어팟 양쪽 다 끼기 → '센서'에 에어팟 Hz가 뜨는지 확인
4. 폰을 러닝머신 거치대에 세우고 **기록 시작** → 음성 안내대로 속도를 바꾼다
5. 노트북 화면: `http://localhost:8000/speed.html` (다른 폰에서도 `http://<노트북 IP>:8000/speed.html`)

갤럭시도 같이 기록하려면 갤럭시 크롬에서 `https://<노트북 IP>:8443/speed-galaxy.html`
(처음에 '안전하지 않음' 경고 → 고급 → 계속). 아이폰에서 기록을 시작하면 갤럭시가 자동으로 그 기록에 붙는다.

## 보정 1분 + 자유 달리기 (폰 안 실시간 속도, 계획 0010)

노트북 없이 된다(주소가 비었거나 연결이 안 되면 폰에만 저장).

1. 아이폰을 계기판 위에 평평하게, 에어팟 양쪽. 러닝머신 위에 서서 **보정 1분 + 자유 달리기**
2. 안내대로 7 → 8 → 9 → 10 km/h (첫 구간 25초, 나머지 15초). 7에서 걸어도 됨(걷기로 판단된 구간은 보정에서 빠짐)
3. "자유 달리기"부터 원하는 속도로. 러닝머신 속도를 바꿀 때마다 화면 **±**로 "실제" 값을 맞춘다
4. 화면 큰 숫자 = 폰 안 추정(에어팟 포함), 아래 "폰만" = 에어팟 없이. 끝나면 **기록 끝내기**
5. 기록은 파일 앱 → 나의 iPhone → SpeedRecorder → `<날짜-시각>` 폴더(estimate.csv = 추정, mark.csv = 실제 속도, stage.csv = 구간)
6. 보정한 기록이 끝나면 보정이 저장된다(문서 폴더 `calibration.json`). 다른 날엔 **저장된 보정으로 바로 자유 달리기**로 보정 없이 달려 오차를 본다. 시작하자마자 ±로 실제 속도를 맞출 것

## 제자리 뛰기 (공중 시간 확인, 계획 0008)

러닝머신 없이 집에서. 노트북 `server.py`와 같은 와이파이, 에어팟 양쪽 착용.

1. 다른 기기(갤럭시 등)에서 메트로놈 **170**을 켠다
2. 앱 → **제자리 뛰기 기록 (약 2분)** → 음성 안내대로: 가만히 서기 10초 → 점프 3번 → 낮게·보통·높게 각 30초 → 점프 3번
3. 아이폰은 **두 손으로 가슴에 꾹 붙이고**, 팔은 흔들지 않는다(손에 느슨히 쥐면 팔 움직임이 섞여 정답으로 못 씀)
4. 메모: 사람·키·몸무게·신발·바닥 종류·에어팟 기종

에어팟 중력 방향은 `airpods_grav.csv`로 따로 저장된다(`airpods_acc` + `airpods_grav` = 몸 전체 가속도, 공중에선 약 0g).

## 파일

| 파일 | 하는 일 |
|---|---|
| `SpeedRecorderApp.swift` | 앱 시작 |
| `ContentView.swift` | 준비 화면, 기록 중 화면(진행 + 실시간 분석), 끝난 뒤 분석 화면 |
| `TestPlan.swift` | 구간 목록, 진행·음성 안내, 구간이 바뀔 때 노트북에 알림 |
| `Recorder.swift` | 아이폰·에어팟 센서, 0.5초마다 보내기, 폰에 CSV 백업 (파일 앱 → 나의 iPhone → SpeedRecorder) |
| `SpeedEstimator.swift` | 폰 안 실시간 속도(걷기·달리기 판단, 충격 + 에어팟). `speedlab/fusion_live.py`를 옮긴 것 — 둘 다 같이 고친다 |
| `ServerClient.swift` | 노트북 시계 맞추기, 보내기 |
