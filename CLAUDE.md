# 스크런 (Scrun) — Claude 작업 안내

폰(러닝머신 계기판에 올려놓기) + 무선 이어폰 센서만으로 러닝머신 속도를 추정하고, 그 속도로 아바타가 가상 섬 코스를 달리며 여러 명이 함께 달리는 앱. 캡스톤 팀 3명(고상준, 김민재, 김지환)이 각자 Claude와 함께 개발한다.

## 작업 규칙
- 답변은 항상 **한국어**. 범위는 "5에서 10분", "5–10분"처럼 쓰고 물결표(~)는 쓰지 않는다(채팅에서 취소선으로 보임)
- 기준은 **실제 스토어에 출시해도 될 완성도**. "캡스톤 수준엔 충분", "데모엔 문제없다"처럼 기준을 낮추지 않는다. 한계는 피하는 법이 아니라 해결하는 법으로 제시
- 흐름: **계획 → 사용자 검토 → 구현 → 리뷰**. 기능·실험마다 `docs/plans/NNNN-이름.md` 1쪽(급하면 5줄) → 사용자 승인 → 구현 → `/code-review` → 문서 갱신
- **문서화하면서 개발한다.** 일이 끝나면 (1) 결론·실측·바뀐 동작은 그 영역의 공용 문서에(속도 `docs/speed/README.md`, Unity `docs/unity/unity_map.md`, 기획 `docs/product/`), (2) 계획 문서의 상태(승인·구현·완료·실패)를, (3) 자기 `handoff/<git user.name>.md`에 지금 하던 일·다음 할 일을 적는다. 개인 인수인계에만 쓰고 끝내지 않는다
- 기술 선택: 생소한 기술은 설명(로컬 HTML·그림)과 학습 자료를 먼저 주고 사용자가 이해한 뒤 고른다 → `docs/adr/`에 기록(템플릿 `0000-template.md`). 이미 정한 ADR과 다른 방향을 제안할 땐 그 ADR을 먼저 읽고 근거를 댄다
- 가설은 "가설"이라고 표시한다. 근거 연구를 말할 땐 실제로 찾아서 출처를 단다
- 실험 전에 조건을 확인한다(사람이 벨트 위인지 옆 발판인지, 속도 순서, 버튼 누를 사람). 실험 기록은 원본까지 `data/`에 남기고, 사람·키·몸무게·러닝머신 기종·폰 위치를 메모한다
- 보여줄 HTML은 로컬 파일로 준다(claude.ai 아티팩트 링크가 안 열리는 환경이 있음)
- 세션이 끝나면 `docs/log/retro.md`에 5줄 회고(잘된 것 / 잘못된 것 / 배운 것 / 규칙으로 올릴 것)

## 보고서·발표 자료 (교수님·멘토)
- 지어낸 수치, 실제로 없는 앱 흐름, 실측하지 않은 성능을 쓰지 않는다. 계획·목표 수치도 지어내지 않는다. 쓸 수 있는 수치는 `docs/speed/README.md`의 실측값뿐. 공개 데이터 재분석은 출처를 달고 우리 실측과 구분
- 앱 화면 흐름(유저플로우)은 참고용이라 보고서에 넣지 않는다. PDR, Kinomap/RunSocial, "Zwift 분석" 표현 금지(유사 서비스로만)
- 팀원 수정본과 합칠 때는 양쪽 수정 사항을 모두 반영한다
- HWP는 한컴 없이 `docs/hwp/` 스크립트(hwplib·dump·fix)로 고친다. 채팅에서 복사하면 검은 음영·형광펜·별표가 딸려 오니 "텍스트만 붙여넣기(Ctrl+Shift+V)" 안내

## 이미 정한 방향 (바꾸려면 ADR부터)
- 속도 추정: 폰은 계기판에 **그냥 올려놓은** 상태, 사용자 입력 없음. 마이크·FTMS·반자동 입력·버튼 감지 제외, 매번 보정은 최후의 수단 (ADR-0004, 0005, 0006)
- 3D: Unity 하나로(ADR-0001). 지도·이름·지형은 창작한 가상 섬, 손그림풍, 달리는 도로는 평지(경사 안내 없음). 서울 S-Map·실사 영상 코스는 제외

## 팀 작업 (git·GitHub `rlaalswo1222/Scrun`, 공개 저장소)
- 브랜치: `develop`에서 `feat/...`(또는 `fix/...`, `docs/...`)를 따서 작업하고 `develop`으로 PR. `main`·`develop`에 직접 커밋하지 않는다. `develop` → `main`은 발표·출시 때만 (`docs/git_convention.md`)
- 커밋은 `docs/git_convention.md` 규칙(`유형: 제목`, 한글, 한 커밋 한 문제). 커밋·푸시·PR은 사용자가 요청할 때만
- 작업 시작 전에 `develop`에서 `git pull`. 새 계획 번호는 pull한 뒤 마지막 번호 + 1 (동시에 만들면 겹침)
- 남의 `handoff/` 파일은 고치지 않는다
- 팀 전체가 지킬 규칙·결정은 개인 Claude 메모리가 아니라 이 파일이나 `docs/adr/`에 적는다(메모리는 그 사람 컴퓨터에만 있음)
- 공개 저장소라 비밀(키·토큰·인증서)과 다른 사람의 개인정보는 올리지 않는다

## 문서 지도 — 하는 일에 맞는 것만 읽기
| 하는 일 | 먼저 읽을 것 |
|---|---|
| 이어서 작업 | `handoff/<git user.name>.md` (그 사람의 지금 하던 일. `git config user.name`으로 확인, 남의 파일은 고치지 않음) |
| 속도 추정·센서·SpeedLab 앱 | `docs/speed/README.md` → 필요하면 `speedlab_20261028.md`, `speed_ideas.md` |
| Unity 본 앱(섬 맵·아바타·성능) | `docs/unity/unity_map.md`, `unity/tools/README.md` |
| 화면·기능 기획 | `docs/product/scrun_userflow_v2.html`, `scrun_usecases_v1.md` |
| 기술 결정 이유 | `docs/adr/` (0001 Unity 통일, 0002 FastAPI, 0003 MCP 커뮤니티판, 0004 달릴 때 속도 추정, 0005 안내형 보정, 0006 속도 추정 제약) |
| 계획 중·승인된 작업 | `docs/plans/` |
| 교수님·멘토 문서, HWP | `docs/meeting/`, `docs/hwp/` |
| 유사 서비스·관련 연구 | `docs/research/` (Zwift 자료는 참고용, 보고서에 "Zwift 분석" 표현 금지. 관련 연구 정리 `treadmill_sensing_papers.md`) |
| 지난 기록 | `docs/log/` (정리 전 인수인계 전체본, 회고) |

## 코드 위치
- Unity 프로젝트 `unity/TestRun` (Unity 6.6 6000.6.7f1, URP, Android). 우리 코드는 `Assets/Scrun/`, 센서 플러그인 `Assets/Plugins/Android/`
- 속도 분석(Python) `speedlab/`, `analyze.py`, 실험 서버 `server.py`(FastAPI). Python은 `.venv\Scripts\python`
- 옛 프로토타입(참고용): `run3d/`, `maptest/`(Three.js), `ios/SpeedRecorder`

## 자주 쓰는 명령
- Unity 조작: 에디터에서 Window → MCP for Unity → Start Server. 도구가 안 보이면 Git Bash로 `unity/tools/` 스크립트(`ucs.sh` C# 실행, `umenu.sh` 메뉴, `ucon.sh` 콘솔)
- 섬 씬 다시 만들기: 메뉴 Scrun → Build Island Scene / 식생만: Rebuild Vegetation Meshes (fast)
- 속도 실험 앱: 메뉴 Scrun → Build SpeedLab APK → `unity/Builds/SpeedLab.apk`
- adb: `C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe`. Git Bash에서 폰 경로는 `MSYS_NO_PATHCONV=1`, 그때 PC 쪽 경로는 Windows 형식(`C:\...`)
