# 계획 0036: 섬·코스 씬 만들기가 러너 애니메이터를 덮어쓰지 않게 (BL-42 후속, 지환)

- 상태: **구현 · 확인 끝, PR 전** (2026-11-16 지환 승인, 급한 수정이라 5줄)
- 문제: Scrun → Build Island Scene(그리고 Build TestTrack·Course 씬)이 매번 `RunnerAnimatorBuilder.Build()`로 `YBotRunner.controller`를 지우고 클립을 다시 재서 만든다. 11/16 이 맥에서 돌리니 블렌드 기준점이 걷기 3.5 → 5.0, 조깅 6.0 → 5.5(순서 뒤바뀜), 11.4 → 9.1, 15.6 → 13.4로 바뀜 → 계획 0027의 발 속도 표(`RunnerGait.asset`, 커밋된 기준점으로 잰 것)와 어긋나 발 미끄러짐이 되살아난다. 컨트롤러는 git이 추적하는 파일이라 모르고 커밋하면 develop까지 퍼진다
- 고치는 것: 씬 만들기 메뉴들은 `RunnerAnimatorBuilder.EnsureBuilt()`를 부른다 → 컨트롤러가 이미 있으면 그대로 쓰고(가장 빠른 기준점만 읽어 `maxClipKmh`에 넣음), 없을 때만 새로 잰다. 기준점을 다시 재고 싶을 때만 메뉴 Scrun → Build Runner Animator(그 뒤 Measure Runner Gait). README "처음 받았을 때"·`unity_map.md`에 같은 설명
- 확인: Build Island Scene 뒤 `git status`에 `YBotRunner.controller`·`Y Bot@*.fbx.meta`가 안 바뀜, 씬 `maxClipKmh`가 커밋된 값(18.84)과 같음, 섬 Play에서 아바타 정상
- 하지 않는 것: 클립 분석값과 실행 중 발 속도가 다른 원인(0027 남은 것), 이 맥에서 기준점이 달리 재지는 원인
- 진행(11/16 맥): 고친 뒤 Build Island Scene → 콘솔 "있는 것 그대로(가장 빠른 기준점 18.8 km/h)", `git status`에 컨트롤러·Y Bot `.fbx.meta` 변경 없음, 씬 `maxClipKmh` 19곳 모두 18.84(커밋된 값). 섬 Play로 달리는 아바타 눈 확인은 아직(컨트롤러가 커밋된 파일 그대로라 0027 확인 결과가 유지된다고 봄, 가설). 재질 `Course_ArchGlow`·`Lm_Mist`는 빌드마다 직렬화 차이(`_EMISSION` 키워드 등)가 생겨 되돌림(동작과 무관, 이번 범위 밖)
