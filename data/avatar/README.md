# 아바타(Y Bot) 발 미끄러짐 측정 (계획 0027)

- 언제·어디서: 2026-11-11, 지환 맥(에디터 Play), TestTrack 씬(원 트랙 400 m), 러너 1명(Y Bot, Mixamo 동작 6개 블렌드)
- 사람·러닝머신 실험 아님(에디터 안 아바타만)

| 파일 | 내용 |
|---|---|
| `20261111_slip_before.csv` | 고치기 전, 발목 기준 FootSlipProbe(디딘 발목의 땅 기준 앞뒤 속도 중앙값, m/s. +면 발이 앞으로 밀림) |
| `20261111_slip_before_avatarSetup1.csv` | 같은 측정, Y Bot 동작 파일이 각자 Avatar를 만드는 옛 설정(차이 없음 확인용) |
| `20261111_slip_before_contact.csv` | 고치기 전, 땅에 닿은 점(발목·발끝 중 가장 낮은 것) 기준 |
| `20261111-013225_gait_ankle_probe.csv` | 처음 만든 발 속도 표(발목 기준, 달리며 잼) — 버림 |
| `20261111-015814_gait.csv` | 발 속도 표, 땅에 닿은 점 기준·달리며 잼(3분) |
| `20261111-020524_gait.csv` | 발 속도 표, 제자리에서 Animator를 직접 돌려 잼(몇 초), 3초 전체 최저 높이 기준 → 7.5에서 9 km/h에서 달리며 잰 값과 어긋남 |
| `20261111-020925_gait.csv` | 발 속도 표, 제자리·최근 최저 높이 기준(FootSlipProbe와 같은 규칙) = 지금 `Resources/RunnerGait.asset` |
| `20261111_slip_after.csv` | 표를 적용한 뒤, 땅에 닿은 점 기준 |

열: `kmh`(러너 속도), `median_slip_ms`·`mean_slip_ms`(m/s), `seconds`(디딘 것으로 본 시간), `blend_param`, `anim_speed`(재생 배속)

`*_gait.csv` 열: `blend_param_kmh`(블렌드 값), `foot_kmh`(그 블렌드를 1배로 틀 때 땅에 닿은 점이 뒤로 밀리는 속도 중앙값), `contact_frames`(3초 720프레임 중 디딘 것으로 본 수). 첫 파일(`015814`)만 달리며 잰 것이라 열이 `blend_param_kmh,median_slip_ms,foot_kmh,seconds`

