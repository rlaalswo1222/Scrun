# 계획 0063: 우주 루프 맵과 새 아바타를 저장소에 (SpaceLoop 장면)

- 상태: **구현 · PR #120 리뷰 대기** (11/21 상준). 큰 파일 Release `spaceloop-assets-v1` 올림. 아이폰 15 실측 완료(30fps 유지, 아래)
- 담당: 상준(맵·아바타·이 PR), 리뷰 민재
- 근거: ADR-0013(우주 루프), 작업 기록 `docs/unity/space_loop.md`, 아바타 기록 `unity/TestRun/Assets/Scrun/Experiments/AvatarGait/README.md`
- 왜 지금: 팀원이 자기 기능(속도·실시간·HUD)을 새 맵·아바타 위에서 폰으로 시험하려 한다. 11/21에 unitypackage로 먼저 나눴지만, 저장소에 있어야 develop과 같이 움직인다

## 이 PR에 있는 것
| 무엇 | 위치 |
|---|---|
| 장면(3km 루프, 5구간) | `Assets/Scrun/Scenes/SpaceLoop.unity` |
| 맵 재질·텍스처·후처리·중심선·키트 배치표 | `Assets/Scrun/SpaceLoop/` (큰 파일은 아래 "받기") |
| 맵 스크립트 6개 | `Scripts/SpaceLoopAtmosphere·Streamer·CloudCards·LakeReflection·DevKeys`, `FloatMotion` |
| 맵 에디터 도구 2개 | `Editor/SpaceLoopCloudCardBuilder`, `SpaceLoopFarRidgeBuilder` |
| 셰이더 6개 새로 + 2개 수정 | `Shaders/ScrunCloudCard·Planet·PlanetRing·SpaceSky·VertexEmissive·VertexGlowAlpha` 새로, `ScrunStylized`(+18줄: 하이라이트·하늘 반사 옵션, 기본값 0이라 섬 맵 그대로)·`ScrunWater`(+14줄: 호수 반사, `_ScrunReflectOn` = 0이면 그대로) |
| 레이어 8 `SL_Reflect` | `ProjectSettings/TagManager.asset` (호수 반사 카메라가 그리는 물체) |
| 새 아바타 v8 + 걷기 4·달리기 7(v9) | `Characters/NewAvatar/avatar_lowpoly_rigged_v8.fbx`, `avatar_lowpoly_{walk,run}_v9_kNN.fbx` (Humanoid, 루트 굽기) |
| 아바타 구동 | `Experiments/AvatarGait/AvatarGaitDriver.cs`·`AvatarGaitHud.cs`·`AvatarGait_NewAvatarV5Fix.controller` |
| 큰 파일 받기 | `unity/tools/spaceloop_fetch.py`, `spaceloop_manifest.txt`, `.gitignore` |

- 장면의 러너는 develop의 `RunnerController`·`FakeSpeedSource`·`SplineTrack`·`FollowCamera`를 그대로 쓴다(이 PR은 팀 스크립트를 고치지 않음)
- 장면에 꺼 두었던 옛 아바타(v3, v4)는 지웠다. 그래서 옛 FBX·옛 컨트롤러·Y Bot 달리기 동작은 필요 없다. Y Bot은 서 있을 때 동작(`Y Bot@Breathing Idle.fbx`) 하나만 쓴다(README "처음 받았을 때" 3번으로 각자 받는 파일)

## 받기 (처음 한 번, Unity를 열기 전에)
1. `python unity/tools/polyhaven_fetch.py` (맵이 Poly Haven 텍스처 9개를 씀)
2. `python unity/tools/spaceloop_fetch.py` → GitHub Release `spaceloop-assets-v1`의 zip(131MB)을 받아 파일 199개(147MB)를 채우고 SHA-256을 대조 → "모두 같은 내용"
   - 인터넷이 막히면 zip을 따로 받아 `--zip <경로>`. 확인만은 `--check`
   - 맵 큰 파일을 바꾼 사람만: `--manifest`로 목록 → `--pack <zip>` → Release에 **새 이름**(`-v2`)으로 올리고 스크립트의 `ZIP_URL`을 바꿔 커밋(옛 zip은 지우지 않음, 옛 커밋도 받을 수 있게)
3. Unity에서 `Assets/Scrun/Scenes/SpaceLoop.unity` 열고 재생. 키보드 ↑/↓ = 속도 ±0.5 km/h, Space = 멈춤(`FakeSpeedSource`), 1–5 = 구간 이동, [ ] = ±100m(`SpaceLoopDevKeys`)

## 자기 기능 붙이기
- **속도**: 장면의 `Runner`(`RunnerController`)에 `SetSpeedSource(ISpeedSource)`로 진짜 센서를 꽂는다(계획 0029). 아바타 쪽은 할 일 없음
- **아바타**: `AvatarGaitDriver`(Mode = External)가 Runner 속도를 읽어 6.2 km/h 위에서 달리기, 5.4 km/h 아래에서 걷기로 바꾸고, 디딘 발이 바닥에 붙도록 재생 속도를 맞춘다. 다른 러너·봇에 쓰려면 v8 FBX + V5Fix 컨트롤러 + `AvatarGaitDriver`(runner에 그 러너)
- **폰 빌드**: Build Settings에 `SpaceLoop`을 넣는다. 이 장면에는 `ScrunQuality`가 없어 iOS 기본값(30fps)으로 돈다

## 확인 (11/21, 맥, 최신 develop `8b1510a` 위 새 작업 폴더)
- 새 Library로 처음 열기: 컴파일 오류 0
- 장면 검사: 오브젝트 1,237개, 빠진 스크립트 0, 끊긴 연결 0, 깨진 재질 0, 셰이더 오류 0. AvatarV5 Animator = V5Fix 컨트롤러, v8 Avatar 유효
- 재생 6초: 10 km/h에서 러너 16.1m 이동(5.8초, 속도와 일치), 아바타 `RunV9_k10` 재생, 게임 코드 실행 오류 0(편집기 검색 색인 예외 1건은 Unity 내부)
- 받기 스크립트: 빈 폴더에서 GitHub Release zip(131MB, 24초) → 199개 모두 같은 내용, `--check`는 없을 때 실패 코드
- **아이폰 15 실측(11/21, 앱과 같은 Medium·30fps, 가로)**: 5구간 15곳 x 3바퀴 모두 30.0fps(p95 33.4ms), CPU 3.3–5.0ms, 그리기 호출 60–110, 6분 내내 발열 보통. 60fps로 풀어도 15곳 모두 60fps(약 2배 여유). 아바타 20명도 30fps·그리기 호출 112–142. 자세히 `docs/unity/space_loop.md` "아이폰 실측", 원본 `data/perf/`. 측정 빌드: 메뉴 Scrun → Space Loop → iOS 성능 측정 빌드(`SpaceLoopPerfProbe`, 번들 ID 따로라 앱을 덮어쓰지 않음)
- **못 한 것**: 메모리 4GB 폰(Low 단계), 30분 이상 연속 발열, 앱 HUD·실시간과 함께, Windows에서 받기 스크립트 실행

## 다음 (이 PR 뒤)
1. 아이폰 실측: 아이폰 15는 끝(11/21). 남은 것은 4GB 폰·30분 연속·앱 흐름 안에서. 줄여야 하면 하늘(`SpaceSky`)부터
2. 팀 `RunnerAnimator`(`YBotRunner`)와 새 아바타 구동 합치기(민재·지환과 상의). 그때 구동 스크립트·컨트롤러를 `Experiments`에서 정식 위치로 옮김
3. 앱 러닝 흐름에서 이 장면을 쓸지(섬 맵 대신), 실시간 공동 맵·봇·구간 시작 지점 연결
4. ADR-0013 승인되면 `CLAUDE.md` "이미 정한 방향"과 `docs/unity/unity_map.md`의 섬 맵 문구 고치기

## 정할 것 (리뷰 때)
1. 큰 파일을 Release zip + 받기 스크립트로 두는 방식(Poly Haven과 같음)이 괜찮은지
2. ADR-0013(섬 맵 대신 우주 루프) 승인 여부
