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
2. 이 폴더 `SpeedRecorder/`의 `.swift` 파일 5개를 왼쪽 목록의 노란 `SpeedRecorder` 폴더로 끌어다 놓기 → **Copy items if needed** 체크 → Finish

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

## 파일

| 파일 | 하는 일 |
|---|---|
| `SpeedRecorderApp.swift` | 앱 시작 |
| `ContentView.swift` | 준비 화면, 기록 중 화면(진행 + 실시간 분석), 끝난 뒤 분석 화면 |
| `TestPlan.swift` | 구간 목록, 진행·음성 안내, 구간이 바뀔 때 노트북에 알림 |
| `Recorder.swift` | 아이폰·에어팟 센서, 0.5초마다 보내기, 폰에 CSV 백업 (파일 앱 → 나의 iPhone → SpeedRecorder) |
| `ServerClient.swift` | 노트북 시계 맞추기, 보내기 |
