# 같이 달리기 3D 프로토타입

실제 한강 반포 코스(오픈스트리트맵)를 로우폴리 3D로 만들고, 가짜 러너 3명과 함께 5km를 달린다.
속도는 지금은 키보드/버튼으로 정하고, 나중에 센서 추정 속도로 바꾼다.

## 실사 영상 + 3D 러너 실험

`http://localhost:8000/video.html`을 열고 **시작**을 누른다. 기본 영상은
사용자가 지정한 [YouTube 영상](https://www.youtube.com/watch?v=DQBCdbV2Enc)이다.
다운로드한 `assets/video/DQBCdbV2Enc.local.mp4`를 HTML video로 재생한다.
720p/60fps H.264 + AAC, 약 1.39GB, 50분 20초다. 재생 바·YouTube 오버레이 없이 재생한다.
첫 안내판 장면을 건너뛰고 기본 30초부터 시작한다. 파일이 없으면 YouTube 모드로 전환할 수 있다.

- 나와 봇 3명을 투명한 Three.js 레이어로 영상 위에 그린다. 앞뒤 거리 차이에 따라 원근 크기가 달라진다.
- `+ / −`로 내 속도를 바꾸면 영상 배속과 러너 걸음도 바뀐다. 일시정지/재개와 소리 전환을 지원한다.
- **러너 켜짐** 버튼으로 캐릭터를 숨겨 영상만 비교한다. **원래 3D로**는 기존 화면으로 돌아간다.
- 러너 발 위치는 영상 시간별 길 좌우 경계 안에 투영하고, 몸 방향은 길의 굽는 방향을 따른다.
- `assets/video/DQBCdbV2Enc.road.json`에 2초 간격의 길 경계를 저장했다. 자갈길의 색을 이용한 오프라인 추정이며 영상 재생 중에는 무거운 영상 분석을 하지 않는다.
- **화면 맞춤 → 현재 장면 길 보정**에서 영상을 멈추고 흰 원을 좌우 길 가장자리로 끌어 보정한다. 완료 후 시작/계속 달리기를 누른다. 해당 시각의 보정은 브라우저에 저장된다. **길 보정 초기화**로 추정값으로 복원한다.
- 지평선, 카메라 중심, 원근감, 러너 크기도 조절할 수 있다. 이 값은 영상별로 브라우저에 저장한다.
- 기본 목표는 5km이며 촬영 속도는 **10km/h 가정**이다. 실제 촬영 속도/GPS를 알 수 없어 실제 코스 거리와 일치하지 않을 수 있다.
- 화면 맞춤에서 영상 시작 시각, 촬영 속도 가정, 목표 거리를 바꾸고 다시 시작할 수 있다. 영상이 짧으면 목표 거리를 남은 영상 길이에 맞춰 줄인다.
- URL 예: `video.html?filmed=10&from=60&km=5&speed=9`. `v`로 다른 영상 ID도 지정할 수 있다.
- 로컬 파일은 연속 배속(0.25~4배)을 사용한다. **YouTube 모드** 또는 `?source=youtube`로 공식 임베드도 사용할 수 있으며, 이때는 YouTube가 제공하는 단계 배속을 사용한다.
- 재생 오류가 나면 **영상 다시 연결**로 플레이어를 새로 만든다. **영상만 확인**은 자동 재생·API 제어 없이 같은 영상을 기본 YouTube 플레이어로 연다(`video-check.html`). 여기서도 실패하는지와 YouTube 원본 재생 여부를 비교해 브라우저/임베드 문제와 앱 제어 문제를 구분한다. 오류 5만으로 브라우저가 영상을 지원하지 않는다고 단정할 수는 없다.
- GPS·깊이 정보가 없어 실제 거리·카메라 높이는 근사치다. 길 경계 추정은 눈·바위·다리·갈림길에서 오차가 날 수 있으며 완전한 자동 추적은 아니다. 발 위치는 영상에서 보이는 길의 끝과 가까운 경계 사이에 배치하고, 캐릭터 크기는 거리 차이에 따라 줄인다. 카메라 지평선까지 길을 연장하지 않는다. 이는 영상과 맞추기 위한 근사 투영이며 실제 사람/물체에 가려지는 효과는 구현하지 않았다.
- 새 영상에는 해당 영상의 길 경계 데이터가 필요하다. 지정한 산길 영상의 경계를 다시 추출하려면 `python tools/track_video_road.py run3d/assets/video/DQBCdbV2Enc.local.mp4 run3d/assets/video/DQBCdbV2Enc.road.json`을 사용한다. 이 색상 규칙은 이 영상에 맞춘 것이며 일반적인 도로 인식 모델은 아니다.

기존 3D 화면/코스 생성 코드는 유지되어 있다. 변경 전 실사 모드의 두 파일도
프로젝트 루트의 `.backups/video-before-overlay/`에 보관했다.
변경 전 실사 모드까지 되돌리려면 프로젝트 루트에서:

```powershell
Copy-Item -LiteralPath .backups/video-before-overlay/video.html -Destination run3d/video.html
Copy-Item -LiteralPath .backups/video-before-overlay/video.js -Destination run3d/src/video.js
```

동기화·레이스·로컬 플레이어·길 보간 검사(실제 브라우저의 재생/WebGL 화면 검증은 별도):

```powershell
node --experimental-vm-modules tools/check_video_mode.mjs
node tools/check_video_road.mjs
```

## 실행

```powershell
.venv\Scripts\python server.py      # 프로젝트 폴더(Capstone1)에서
```

- 노트북: http://localhost:8000 (3D 달리기), http://localhost:8000/make.html (코스 만들기)
- 갤럭시: 노트북과 같은 와이파이에서 `http://<노트북 IP>:8000/make.html` (IP는 `ipconfig`의 IPv4 주소).
  윈도우 방화벽이 물어보면 허용. three.js·지도 타일을 인터넷에서 받아오므로 폰도 인터넷이 돼야 한다.

## 코스 만들기 (지도에서 고르기)

1. `make.html`에서 지도를 눌러 출발점을 고른다 (길 가까이, 300m 안에 길이 있어야 함)
2. 거리(3/5/10km)와 모양(한 바퀴 = 순환, 갔다 오기 = 왕복)을 고르고 **코스 만들기**
3. 서버가 OSM에서 길·주변 지도를 받아 코스를 만든다 (처음 고른 동네는 1~2분, 받아 둔 동네는 몇 초)
4. **이 코스로 달리기** -> `index.html?course=<id>`

- 순환은 출발점-A-B-출발점 삼각형 모양을 여러 방향으로 시도해 길 겹침이 적고 목표 거리에 가까운 것을 고른다.
  그래서 실제 거리가 목표와 조금 다를 수 있다 (예: 3km -> 2.91km). 마땅치 않으면 왕복으로 바꾼다.
- 왕복은 정확히 목표 거리다.
- 만든 코스는 `data/courses/`에 저장되고, 같은 출발점·거리·모양을 다시 요청하면 새로 만들지 않는다.
- 공용 OSM 서버를 쓰므로 바쁘면 실패할 수 있다 (잠시 후 다시). 하루 수십 개 정도가 적당하다.
- 코드: `server.py`(API), `coursegen/`(코스 생성), `src/make.js`(지도 화면)

## 조작

| 키 / 버튼 | 동작 |
|---|---|
| 시작, Space | 3초 카운트다운 후 출발 |
| +, ↑ | 속도 0.5km/h 올리기 |
| −, ↓ | 속도 0.5km/h 내리기 |

주소 뒤에 붙이는 옵션:
- `?speed=10`: 시작 속도 (기본 9km/h)
- `?skip=600`: 바로 출발해서 600초 달린 지점으로 건너뛰기 (시연용)

## 기본 코스(한강 반포 5km) 다시 만들기

```powershell
.venv\Scripts\python tools\build_course.py            # 받아 둔 OSM 원본으로 다시 생성
.venv\Scripts\python tools\build_course.py --refetch  # OSM 원본부터 새로 받기
```

출발점(`START`), 방향(`END`), 거리(`LENGTH_M`)는 [tools/build_course.py](../tools/build_course.py) 맨 위에서 바꾼다.
이 코스는 한강남자전거길을 우선해서 자전거길·산책로만 따라간다.

## 구조

| 파일 | 역할 |
|---|---|
| `src/main.js` | 시작점: 장면, 러너, 카메라, 30fps 루프 |
| `src/race.js` | 레이스 상태. **속도 데이터 출처(source)를 여기서 갈아 끼운다** |
| `src/world.js` | course.json -> 땅, 강, 공원, 도로, 다리, 건물, 코스, 가로등·벤치, 하늘·빛 |
| `src/trees.js` | 나무 (가까이는 3D, 멀리는 임포스터) |
| `src/grass.js` | 3D 잔디 |
| `src/landmarks.js` | 분수·아치 |
| `src/post.js` | 후처리 |
| `src/quality.js` | 화질 설정 |
| `src/geom.js` | 지도 도형 -> 3D 형상 공용 함수 |
| `src/runner.js` | 사람 모델 러너. 다리 움직임 = 실제 걸음 수 |
| `src/course.js` | "출발점에서 몇 m" -> 3D 위치와 방향 |
| `src/hud.js` | 페이스, 거리, 순위, 미니맵 |

### 진짜 데이터로 바꾸려면

러너마다 `source.sample(t)`가 `{ speedKmh, cadence }`를 돌려주기만 하면 된다.

- 내 속도: `ManualSource` 대신 센서 추정값을 주는 source
- 다른 사람: `BotSource` 대신 서버에서 받은 값을 주는 source

화면 코드(world, runner, hud)는 고칠 필요 없다.

## 그래픽

| 요소 | 내용 | 파일 |
|---|---|---|
| 하늘 | 실제 하늘 사진(HDRI) 배경 + 같은 하늘의 빛으로 조명. 해 방향도 사진 속 해에 맞춤 | `assets/tex/sky_4k.jpg`, `sky_1k.hdr` |
| 바닥 | 아스팔트·잔디·보도 사진 텍스처, 물결치는 강 | `assets/tex/` |
| 잔디 | 내 주변 풀잎 수만 개(바람에 흔들림). 위치는 지도에 고정, 셰이더에서 감아 돌림 | `src/grass.js` |
| 나무 | ez-tree로 만든 참나무·물푸레나무·사시나무. 가까이는 3D, 멀리는 사진 판(임포스터) | `src/trees.js` |
| 건물 | 층마다 창문, 유리에 하늘이 비침 | `src/world.js` |
| 랜드마크 | 반포대교 달빛무지개분수, 동작대교·동호대교 아치 | `src/landmarks.js` |
| 길·땅 | 코스 표면(자전거길·보도·차도), 차도 양옆 보도와 연석, 차선(노란 중앙선·흰 점선), 땅 용도(단지 잔디·주차장·학교·운동장) | `src/world.js` |
| 건물 외관 | 아파트(베란다), 유리 오피스, 상가(1층 가게+간판 띠), 주택 | `src/facades.js` |
| 거리 소품 | 신호등·횡단보도(OSM 기록 + 큰길 교차로 규칙), 버스정류장, 울타리·담장, 가로등(30m), 가로수(10m) | `src/props.js`, `coursegen/props.py` |
| 캐릭터 | 모션캡처 동작이 든 마네킹(Xbot). 다리 움직임 = 실제 걸음 수 | `src/runner.js` |
| 후처리 | 계단 현상 제거, 구석 그늘(AO), 빛 번짐, 색감·비네트 | `src/post.js` |

### 화질 설정

주소 뒤에 `?quality=high|medium|low`를 붙인다. 기본값은 폰 medium, 컴퓨터 high.

| | high | medium | low |
|---|---|---|---|
| 해상도 배율 (최대) | 1.5 | 1.5 | 1 |
| 그림자 해상도 | 2048 | 1024 | 1024 |
| 3D 나무 반경 | 110m | 80m | 50m |
| 3D 잔디 | 60m 사방, 촘촘 | 44m 사방 | 없음 |
| 후처리 | 전부 | 구석 그늘 제외 | 없음 |

**자동 해상도 조절**: 2초마다 평균 fps를 보고 26fps 아래면 해상도 배율을 0.1씩(최소 0.75) 낮추고,
10초 연속 여유가 있으면 0.05씩 다시 올린다. 폰이 뜨거워져 느려져도 30fps를 지키기 위함. `?adaptive=0`이면 끈다.

S22 측정(2026-10): medium 30fps 유지(제한 해제 시 48fps), high 17.5fps.

항목별로 덮어쓸 수도 있다: `?quality=medium&grassSize=0&ao=0&treeRadius=40` (폰에서 무엇이 무거운지 찾을 때)

### 확인용 옵션

- `?debug=1`: fps·삼각형 수·GPU 표시, 화질 바꾸기 버튼
- `?cap=0`: 30fps 제한 해제 (최대 성능 측정용)
- `?exposure=0.9`: 밝기 (기본 0.75)
- `?cam=x,y,z,보는x,보는y,보는z`: 카메라 고정. 예) 반포대교 분수 보기
  `?skip=60&cam=1556,6,-495,1519,31,-944`, 동작대교 아치 보기 `?skip=60&cam=-158,6,-4,274,37,-361`

### 출처

- 텍스처·하늘: [Poly Haven](https://polyhaven.com) (CC0, 출처 표기 의무 없음)
- 물결: three.js 예제 `waternormals.jpg`, 캐릭터: three.js 예제 `Xbot.glb` (Mixamo 계열)
- 나무: [ez-tree](https://github.com/dgreenheck/ez-tree) (MIT, `vendor/ez-tree.es.js`, 라이선스 `vendor/ez-tree.LICENSE`)
- 지도: © OpenStreetMap contributors (ODbL, 화면 표기 필수)

## 알려진 한계

- 다리 끝 경사로는 실제 구조가 아니라 직선으로 내려오게 단순화했다.
- 건물 높이 정보가 없는 아파트는 30~50m 사이 임의 높이다.
- 아치의 크기·개수와 분수 모양은 실제 구조를 측량한 값이 아니라 분위기를 살린 근사치다.
- 동호대교는 지도 범위 끝에 걸려 강 위 구간 일부만 잡혀서, 아치 위치가 실제 강 가운데와 다를 수 있다.
- 지도 데이터 출처(© OpenStreetMap contributors)는 화면에 반드시 표시해야 한다 (ODbL 라이선스).

## 실사 모드 (video.html)

1인칭 러닝 영상 위에 3D 러너를 겹쳐 그린다. 기본 영상은 뉴질랜드 더니든 해안길(Vauxhall → Broad Bay 11.2km).
영상 파일은 `assets/video/{id}.local.mp4` (개인 시연용으로 내려받은 것, 배포 금지).

**1인칭**: 영상 카메라가 곧 내 눈이다. 내 아바타는 그리지 않고, 다른 러너는 나와의 거리만큼 앞에 선다.

**앞길 추적 모드** (`assets/video/{id}.ahead.json`이 있을 때, 가장 정확)
- 카메라(촬영자)는 실제로 그 길을 달렸으므로 "지금 D m 앞의 길 위 점" = "촬영자가 나중에 지나간 곳"
- 나중 장면에서 촬영자 발 앞 6m의 길 위 점(왼쪽 끝·가운데·오른쪽 끝)을 찍고, 옵티컬 플로우로 지금 장면까지 거꾸로 따라온다
- 그 점이 곧 그 거리 러너의 발 위치 → 커브·오르내리막이 그대로 맞고, 추적이 끊기는 곳(덤불·언덕에 가려짐)에서는 사라진다
- `python tools/track_video_ahead.py o0EmQXATQQI --start 55` (영상 1시간 기준 약 25분)

**코스 지도 모드** (`assets/video/{id}.course.json`이 있을 때)
- 영상 속 촬영자가 실제로 달린 길(OSM) + "영상 시각 → 그 길 위 위치" 표
- 표는 영상 왼쪽의 초록 진행 막대(Relive/Garmin 오버레이)를 2초마다 읽어 만든다
- 영상 재생 속도: 내 거리에 해당하는 실제 영상 시각을 따라가므로, 촬영자가 빨랐다 느렸다 해도 내 속도와 맞는다
- 러너 거리 = 지도상 실제 거리, 좌우 위치 = 영상 속 실제 길(색 추적, 보이는 길 끝 너머는 그 방향으로 연장)
- 보이는 길 끝을 넘어가고 시선이 육지 쪽(언덕·절벽)을 지나면 서서히 사라진다

만드는 방법 (해안길 예):
```powershell
.venv\Scripts\python tools\track_video_road.py run3d\assets\video\o0EmQXATQQI.local.mp4 run3d\assets\video\o0EmQXATQQI.road.json --start 55
.venv\Scripts\python tools\build_video_course.py o0EmQXATQQI --osm run3d\data\raw\osm_otago.json --start -45.8893 170.5272 --end -45.8506 170.6177 --total-km 11.2 --run-start 53 --prefer "Otago Harbour Trail" --bar-cache run3d\data\raw\o0EmQXATQQI.bar.json --s0 1200
.venv\Scripts\python tools\check_course_overlay.py o0EmQXATQQI 확인.png 600,1200,1800   # 지도 길을 영상 위에 그려 확인
```
OSM 원본(`osm_otago.json`)은 Overpass 장애 때 공식 API 조각을 `tools/osm_xml_to_json.py`로 합친 것.

**알려진 한계**
- 지도(OSM) 선이 실제 길과 몇 m씩 달라 먼 곳(50m+) 커브는 정확하지 않다. 그래서 좌우는 영상 속 길에 맞춘다
- 길 추적이 옆 차도까지 길로 잡는 장면이 있다(약 40분 지점), 나무 데크 다리(약 50분)는 길을 놓친다
- 출발점(s0=1200m)은 지도 길을 영상에 겹쳐 보며 눈으로 고른 값
