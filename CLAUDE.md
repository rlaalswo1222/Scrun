# 스크런 (Scrun) — Claude 작업 안내

폰(러닝머신 거치대) + 무선 이어폰 센서만으로 러닝머신 속도를 추정하고, 그 속도로 아바타가 가상 섬 코스를 달리며 여러 명이 함께 달리는 앱. 캡스톤 팀 3명, 목표는 출시 가능한 완성도.

## 작업 규칙
- 답변은 항상 **한국어**. 범위는 "5에서 10분", "5–10분"처럼 쓰고 물결표(~)는 쓰지 않는다(채팅에서 취소선으로 보임)
- 흐름: **계획 → 사용자 검토 → 구현 → 리뷰**. 기능·실험마다 `docs/plans/NNNN-이름.md` 1쪽(급하면 5줄) → 사용자 승인 → 구현 → `/code-review` → 자기 handoff 갱신
- 기술 선택: 생소한 기술은 설명(로컬 HTML·그림)과 학습 자료를 먼저 주고 사용자가 이해한 뒤 고른다 → `docs/adr/`에 기록(템플릿 `0000-template.md`)
- 보고서·발표 자료에 지어낸 수치, 실제로 없는 앱 흐름, 실측하지 않은 성능을 쓰지 않는다. 쓸 수 있는 수치는 `docs/speed/README.md`의 실측값뿐
- 가설은 "가설"이라고 표시한다. 근거 연구를 말할 땐 실제로 찾아서 출처를 단다
- 실험 전에 조건을 확인한다(사람이 벨트 위인지 옆 발판인지, 속도 순서, 버튼 누를 사람). 실험 기록은 원본까지 남긴다
- 사용자 브라우저에선 claude.ai 아티팩트 링크가 안 열림 → 보여줄 HTML은 로컬 파일로 준다
- 세션이 끝나면 `docs/log/retro.md`에 5줄 회고(잘된 것 / 잘못된 것 / 배운 것 / 규칙으로 올릴 것)
- git 커밋은 `docs/git_convention.md` 규칙(`유형: 제목`, 한글, 한 커밋 한 문제). 커밋·푸시는 사용자가 요청할 때만

## 문서 지도 — 하는 일에 맞는 것만 읽기
| 하는 일 | 먼저 읽을 것 |
|---|---|
| 이어서 작업 | `handoff/<git user.name>.md` (그 사람의 지금 하던 일. `git config user.name`으로 확인, 남의 파일은 고치지 않음) |
| 속도 추정·센서·SpeedLab 앱 | `docs/speed/README.md` → 필요하면 `speedlab_20261028.md`, `speed_ideas.md` |
| Unity 본 앱(섬 맵·아바타·성능) | `docs/unity/unity_map.md`, `unity/tools/README.md` |
| 화면·기능 기획 | `docs/product/scrun_userflow_v2.html`, `scrun_usecases_v1.md` |
| 기술 결정 이유 | `docs/adr/` (0001 Unity 통일, 0002 FastAPI, 0003 MCP 커뮤니티판, 0004 달릴 때 속도 추정, 0005 안내형 보정) |
| 계획 중·승인된 작업 | `docs/plans/` |
| 교수님·멘토 문서, HWP | `docs/meeting/`, `docs/hwp/` |
| 유사 서비스 조사 | `docs/research/` (내부용, 보고서에 "Zwift 분석" 표현 금지) |
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
