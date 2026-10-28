# 스크런 (Scrun)

폰(러닝머신 위에 올려놓기) + 무선 이어폰 센서만으로 러닝머신 속도를 추정하고, 그 속도로 아바타가 가상 섬 코스를 달리며 여러 명이 함께 달리는 앱. 캡스톤 팀 프로젝트.

- 각자 지금 하던 일: [handoff/](handoff/) (사람별 파일) / 작업 규칙·문서 지도: [CLAUDE.md](CLAUDE.md)
- 속도 추정 현황: [docs/speed/README.md](docs/speed/README.md) / Unity 본 앱: [docs/unity/unity_map.md](docs/unity/unity_map.md)
- 기술 결정: [docs/adr/](docs/adr/) / 작업 계획: [docs/plans/](docs/plans/)
- 커밋 메시지 규칙: [docs/git_convention.md](docs/git_convention.md)

## 처음 받았을 때 (Unity를 열기 전에)

용량 때문에 일부 파일은 저장소에 없고 각자 받거나 다시 만든다. **Unity를 열기 전에** 1, 2를 해야 연결(.meta)이 안 깨진다.

1. 커밋 규칙 검사기 켜기: `git config core.hooksPath tools/githooks`
2. Poly Haven 에셋(CC0, 약 230MB) 받기: `python unity/tools/polyhaven_fetch.py` → "모두 같은 내용"이 나오면 끝
3. Mixamo 캐릭터 받기(무료 Adobe 계정): [mixamo.com](https://www.mixamo.com) → 캐릭터 **Y Bot** → 아래 동작을 Format **FBX for Unity(.fbx)**로 받아 `unity/TestRun/Assets/Scrun/Characters/YBot/`에 **정확히 이 이름**으로 저장
   - `Y Bot@Breathing Idle.fbx` (Skin: **With Skin**)
   - `Y Bot@Walking.fbx`, `Y Bot@Jogging.fbx`, `Y Bot@Slow Run.fbx`, `Y Bot@Standard Run.fbx`, `Y Bot@Fast Run.fbx`, `Y Bot@Sprint.fbx` (Skin: **Without Skin**, In Place 있으면 켜기)
4. Unity Hub에서 `unity/TestRun` 열기 (Unity 6.6, 6000.6.7f1). 처음엔 Library를 만드느라 오래 걸림
5. 메뉴 Scrun → Reimport Mixamo Characters → Scrun → Build Island Scene (약 3분, 섬 지형·도로·식생을 다시 만듦)

빠진 것: `Course/Generated`(섬 메시, 5번에서 생성), APK 빌드, SoundTrackDB 등 공개 데이터(`data/external`), 옛 프로토타입 영상·3D 자료(`run3d/assets`, `run3d/data`), 인증서(`certs`)

## 폴더
| 위치 | 내용 |
|---|---|
| `unity/TestRun` | Unity 프로젝트. 우리 코드 `Assets/Scrun/`, 센서 플러그인 `Assets/Plugins/Android/` |
| `unity/tools` | Unity를 명령줄에서 조작하는 스크립트, Poly Haven 받기 |
| `speedlab/`, `analyze.py`, `server.py` | 속도 분석(Python), 실험 서버(FastAPI) |
| `data/speedlab`, `data/runs` | 속도 실험 원본 기록 |
| `docs/` | 계획·결정·실험 결과·기획 |
| `run3d/`, `maptest/`, `ios/` | 옛 프로토타입(참고용) |

## Python 설치 (최초 1회)

```powershell
py -3.11 -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt
```

## 옛 속도 실험 도구 (phyphox, analyze.py)

거치대에 올린 폰의 진동(phyphox)과 전면 카메라 영상만으로 러닝머신 속도를 얼마나 정확히 맞힐 수 있는지 처음 검증하던 도구. 지금 실험은 SpeedLab 앱([docs/speed/README.md](docs/speed/README.md)).
`속도 = 케이던스 × 보폭`에서 보폭을 정하는 네 가지 방식을 비교한다.

| 모델 | 방식 |
|---|---|
| `height_only` | 키로만 보폭 추정 (보정 없음) |
| `single_point` | 한 가지 속도(10km/h)로 보정한 보폭을 모든 속도에 사용 |
| `multi_cadence` | 여러 속도로 보정: 보폭 = a + b × 케이던스 |
| `multi_features` | 여러 속도로 보정 + 진폭·충격 보조지표 (릿지 회귀) |

이후 명령은 모두 `.venv\Scripts\python`으로 실행한다.

### 1. 기록하기

1. S22에 **phyphox** 설치 → "가속도 (g 포함)" 실험 선택
2. 폰을 러닝머신 거치대에 올리고 기록 시작
3. 8 → 9 → 10 → 11 → 12 km/h를 60초씩 (보정용), 이어서 9.5 → 11.5 km/h를 60초씩 (평가용)
4. 속도를 바꾼 시각을 기록지에 적는다 (phyphox 화면의 경과 시간 기준)
5. 기록 정지 → 내보내기 → **CSV (Comma, decimal point)** → zip 파일을 노트북 `data/`에 복사
6. `data/segments_template.csv`를 복사해서 실제 시각으로 고친다

카메라도 볼 거라면 팀원 폰(또는 S22를 다른 기록 때)으로 전면에서 상반신을 촬영한다.
걸음 수 정답이 필요하면 영상에서 30초 동안 걸음 수를 세서 ×2 한 값을 `true_cadence_spm`에 적는다.

### 2. 분석하기

```powershell
# 시간에 따른 걸음 수 그래프: 구간 시각을 확인하거나 고칠 때
.venv\Scripts\python analyze.py timeline data\run1.zip data\run1_segments.csv

# 보정 방식별 속도 오차 비교 (키 cm)
.venv\Scripts\python analyze.py evaluate data\run1.zip data\run1_segments.csv --height 175

# 에어팟처럼 샘플링이 낮은 센서라면? 25Hz로 낮춰서 다시 평가
.venv\Scripts\python analyze.py evaluate data\run1.zip data\run1_segments.csv --height 175 --fs 25

# 카메라: 영상 → 머리 움직임 CSV → 같은 방식으로 평가
.venv\Scripts\python analyze.py camera data\run1.mp4
.venv\Scripts\python analyze.py evaluate data\run1_camera.csv data\run1_segments.csv --height 175 --offset -3
```

- `--trim 10`: 속도를 바꾼 직후 몇 초를 버릴지 (기본 10초, 러닝머신 가속 구간)
- `--offset`: 영상과 센서 기록의 시작 시각이 다를 때 보정. 영상이 3초 늦게 시작했으면 `-3`.
  기록 시작 직후 제자리 점프 3번을 해두면 양쪽 그래프에서 시작점을 맞추기 쉽다.

결과는 `results/<기록파일 이름>/`에 저장된다.

| 파일 | 내용 |
|---|---|
| `evaluate.png` | 걸음 수 변화, 실제 속도 vs 추정 속도, 모델별 오차율 |
| `summary.csv` | 모델별 MAPE(평균 오차율), 최대 오차, 편향 |
| `predictions.csv` | 평가 구간별 예측값 |
| `features.csv` | 구간별 케이던스, 신뢰도(quality 0~1), 진폭, 충격 |

### 실제 데이터 없이 코드 확인

```powershell
.venv\Scripts\python tools\make_synthetic.py
.venv\Scripts\python analyze.py evaluate data\synthetic\accel.csv data\synthetic\segments.csv --height 175
```

가짜 데이터는 "빨라질수록 보폭이 늘어나는" 가정으로 만들었기 때문에 `multi_cadence`가 이기는 게 당연하다.
코드가 제대로 도는지 확인하는 용도일 뿐이고, 실제 결론은 러닝머신 데이터로 내야 한다.

### 구조

```
analyze.py            명령줄 도구 (timeline / evaluate / camera)
speedlab/loaders.py   phyphox zip·csv, 구간 기록표 읽기
speedlab/dsp.py       필터링, 주축 투영, 자기상관으로 케이던스 추정
speedlab/models.py    보폭 모델 4종과 평가
speedlab/camera.py    MediaPipe로 영상에서 코·어깨 높이 추출
tools/make_synthetic.py  테스트용 가짜 데이터
tools/build_course.py    기본 코스(한강 반포 5km) 만들기
coursegen/               오픈스트리트맵 -> 3D 코스 데이터 (경로 자동 생성 포함)
server.py                코스 만들기 서버 + 3D 화면 제공 (run3d/README.md 참고)
run3d/                   3D 러닝 화면, 코스 만들기 지도 화면
```
