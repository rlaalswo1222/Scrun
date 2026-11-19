# 계획 0059: 러너 이름표·정보 카드에 레벨 배지 (BL-22 남은 것)

- 상태: **승인** (2026-11-19 민재, 정할 것 1·2 모두 (가)) · **구현 완료**(#112 머지·오라클 배포 11/19. 남은 것: 아이폰 확인)
- 담당: 민재(노션 작업 표 "[FE] 러너 정보 카드" 남은 것: 레벨 배지·인증 마크). 모양은 상준
- 근거: Figma D-12(604:856, 11/19 다시 읽음): 카드 = 얼굴·이름·**"친구"(파랑) · "Lv.3"(연초록) 칩**·나와의 간격·누적 거리·닫기. 지도 위 이름표도 "하늘보리 · Lv.1", "비락식혜 · 친구 · Lv.2". 노트 "경쟁 요소는 최소화: **레벨 배지·간격·누적 거리만** 표시". 공개 백로그 BL-22 ②는 "누적 거리·인증 마크만", 설정 H-10에 "공동 맵에서 인증 마크 보이기" 스위치(지금 잠김·준비 중)
- 지금: 실시간 서버가 러너마다 `userId`·`nickname`·위치·속도·`run`(이번 거리)만 보냄. 레벨은 서버 `growth.level_of(경험치)`로 계산(계획 0048), 앱에선 마이·결과 화면에서만 씀. 카드엔 "친구"/"BOT" 배지 한 칸(`hud-card-badge`), 이름표는 글자 한 줄

## 하는 것
1. **서버(마이그레이션 없음)**: 실시간 입장 때(`realtime/router._seat`) 그 사람 레벨을 읽어 `hub.join(..., level)` → 러너 공개 정보(`welcome`의 `runners`·`joined`)에 `level`(1–6) 한 칸. 봇은 없음. 레벨은 인증 뒤(맵 밖)에만 바뀌므로 입장 때 값으로 충분(다음 입장에 새 값). 옛 앱은 모르는 칸을 무시
2. **앱**: `RunTogetherRoom`이 `level`을 읽어 `RemoteRunner.Level`(없으면 null). 카드 머리에 "친구" 칩 옆 **"Lv.N" 칩**(Figma 연초록), 봇·레벨 모름이면 숨김. **이름표**도 이름 옆에 작은 Lv 칩(정할 것 2)
3. **확인**: pytest(입장한 사람의 welcome·joined에 레벨, 경험치 → 레벨, 봇엔 없음), EditMode(파싱·카드 글자·칩 숨김), 에디터 Play(로컬 서버 + `fake_runner`로 다른 사람 입장 → 이름표·카드에 Lv)

## 정할 것
1. **인증 마크**: (가) **이번엔 레벨만(Figma D-12 그대로) (추천: Figma 노트가 "레벨 배지·간격·누적 거리만", 인증 마크가 지도 어디에 보일지 Figma에 없음. H-10 "공동 맵에서 인증 마크 보이기" 스위치는 상준이 모양을 정하면 그때 서버 설정 칸·스위치와 같이)** vs (나) 카드에 "인증 마크 N개" 줄 + H-10 스위치를 이번에(설정 칸 마이그레이션, 모양은 임시)
2. **이름표에도 레벨**: (가) **이름 옆 작은 Lv 칩 (추천: Figma 이름표에 Lv가 있고, 누르기 전에도 보임)** vs (나) 카드에만(이름표가 짧게 유지)

## 안 하는 것
- 내 머리 위 "나 · Lv.2" 이름표(Figma엔 있음, 지금 앱엔 내 이름표 자체가 없음 → 상준과 따로), 카드 얼굴 그림(아바타 그림 전, 지금처럼 없음), 친구 추가 버튼(계획 0044 정할 것 3: 카드에서 안 함)

## 확인 (끝났다고 볼 기준)
- 노션 줄 완료 확인 + 에디터에서 다른 러너 이름표·카드에 Lv, 봇은 없음
- pytest·EditMode, `/code-review`. 아이폰 확인은 같이 달리기 확인 때(노션 공통 아이폰 확인)

## 진행
- 11/19 민재 Windows: Figma D-12·H-10 다시 읽음, 서버 `realtime/router`·`hub`, 앱 `RunnerTagsView`·`RunTogetherRoom`·`RunHud.uxml/uss` 읽고 초안(`feat/runner-card-level`)
- 11/19 민재 구현(`feat/runner-card-level`):
  - 서버: `realtime/router._seat`가 `growth.level_of(경험치)`를 읽어 `hub.join(level=)`, `Runner.level` → `public()`에 `level`(봇 없음, `states`엔 없음). `docs/api/realtime.md`에 적음. pytest `test_runners_carry_level`(Lv.2 사람이 welcome에, 새 사람 Lv.1이 joined에)·`test_bots_have_no_level`
  - 앱: `RemoteRunner.Level`, `RunTogetherRoom.LevelFrom`(welcome·joined, 이미 아는 사람도), 카드 `hud-card-level`(연초록 `--color-success`), 이름표 `hud-tag-level`(라임 칩, 봇은 칩 없음), `RunnerTagsView.LevelText`. EditMode `LevelComesWithRunnerAndShowsAsChip`
  - 에디터 확인(Play, 이 브랜치 서버 8003, `fake_runner` k_905 경험치 12 km = Lv.3): 카드 "가짜905 · Lv.3"(연초록 칩), 이름표 "가짜905 Lv.3"(라임 칩). 에디터에선 다른 러너 아바타(Y Bot 메시 오류, 인수인계에 있던 문제)가 안 떠 머리 위치가 비어 이름표는 위치를 넣어 찍음 → 아이폰에서 실제 위치 확인 필요
  - `/code-review` 반영: 다시 붙을 때 welcome의 아는 사람 레벨을 안 바꾸던 것, int 범위 밖 정수에서 welcome 전체를 잃던 것(long으로 읽음), 이름표가 매 프레임 "Lv.N" 글자를 새로 만들던 것(바뀔 때만), 봇 이름표에도 빈 칩을 달던 것, 카드 Lv 칸을 생성자에서 확인, 겹치는 USS 클래스, 문서의 레벨 범위("1 이상, 지금 1–6")
  - 테스트: pytest 전체 433 통과·2 건너뜀, EditMode 527 통과
  - 남은 것: 아이폰 같이 달리기 확인 때 이름표·카드(노션 공통 아이폰 확인), 상준 칩 색·크기, 섬 Y Bot 메시 오류(에디터에서 다른 러너가 안 보임)
