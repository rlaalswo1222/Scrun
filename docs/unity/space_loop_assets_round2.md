# 우주 루프 에셋 2차 요청서 (2026-11-12)

1차 49개를 넣고 배경·후처리까지 다듬은 뒤, 레퍼런스 5장과 지금 게임 화면을 나란히 놓고 남은 차이를 추렸다.
비교 이미지: `art/map/space_loop/renders/unity/ref_vs_now_1112.png` (왼쪽 레퍼런스, 오른쪽 지금).
만드는 흐름은 1차와 같다: GPT로 한 개씩 이미지 → 메쉬두 Image to 3D → GLB → Claude가 크기·LOD·재질·배치. 1차 요청서는 `space_loop_assets.md`.

## 구간별로 남은 차이
| 구간 | 레퍼런스에 있는데 지금 없는 것 | 해결 |
|---|---|---|
| 구름 위 기지 | 길에 붙은 관제 기지(유리 관제실, 따뜻한 조명의 격납고, 바닥 아래 철골) | 에셋(SKY03, SKY04, SKY07). 지금은 코드로 만든 흰 상자 건물 |
| | 뭉게구름 | Claude가 직접(아래 "에셋 없이 Claude가 할 것") |
| 화성 협곡 | 길 위로 덮이는 절벽, 절벽 밑 돌무더기 | 에셋(MARS09, MARS10, MARS04 b). 지금은 평평한 벽과 빈 바닥 |
| 밤 숲 호수 | 굵고 휘어진 고목, 층층이 퍼진 수관, 호숫가 바위 | 에셋(FOR03, FOR04, FOR07, FOR09). 지금은 코드로 만든 곧은 검은 줄기와 덩어리 잎, 나무 모델이 1종뿐 |
| | 큰 고사리, 흰 꽃 | Claude가 직접(Poly Haven 고사리) + 꽃은 시험 1개(FOR11) |
| 얼음 위성 | 길 위로 말려 올라간 얼음 벽, 눈 덮인 바위, 뾰족한 얼음 | 에셋(ICE02, ICE07, ICE08). 지금 결정은 보석처럼 보이고(화성용 결정을 같이 씀) 바닥이 비어 있음 |
| | 길 옆 크레바스 | Claude가 직접(지형) |
| 달 기지 | 달 바위(지금은 화성 바위를 회색으로 바꾼 임시), 저장 탱크, 조명탑 | 에셋(L1, B8, B12, A2) |
| | 크레이터, 건물 창 불빛 | Claude가 직접(지형, 발광) |

## 1순위: 형태가 문제인 것 (11종, 이미지 18장)
| 번호_파일이름 | 구간 | 물건 | 크기(대략) | 쓰는 곳 |
|---|---|---|---|---|
| FOR03_canopy_tree_a | 숲 | 수관이 넓게 퍼진 고목 | 높이 18m | 길가 대표 나무, 옛 나무 교체 |
| FOR03_canopy_tree_b | 숲 | 길 쪽으로 기운 고목 | 높이 14m | 위와 섞어 반복이 안 보이게 |
| FOR04_trunk_roots | 숲 | 잎 없이 굵은 줄기와 판 모양 뿌리만 | 높이 12m | 화면 앞쪽 큰 줄기(잎은 Claude가 잎 카드로 얹음) |
| FOR09_shore_rock_a, b | 숲 | 젖은 호숫가 바위 큰 것·작은 것 | 3m, 1.2m | 호수 가장자리 |
| ICE02_ice_overhang | 얼음 | 길 위로 말려 올라간 큰 얼음 벽 | 폭 30m, 높이 22m | 구간 대표 랜드마크 |
| ICE07_ice_spikes_a, b | 얼음 | 눈 얹힌 뾰족한 얼음 무리 | 6m, 2.5m | 지금 보석 같은 결정 교체 |
| ICE08_snow_rock_a, b, c | 얼음 | 눈 덮인 바위 3크기 | 5m, 2m, 0.8m | 빈 눈밭 채우기 |
| L1_lunar_rock_a, b, c | 달 | 달 바위 3크기 | 3m, 1m, 0.4m | 임시 바위 교체 |
| SKY03_station_wing | 구름 위 | 길 높이에 붙는 관제동(유리 관제실, 아래 철골) | 폭 30m | 옛 흰 건물 교체 |
| SKY04_sky_hangar | 구름 위 | 문이 열린 격납고 동(안쪽 따뜻한 조명, 아래 철골) | 폭 26m | 옛 흰 건물 교체 |
| SKY07_support_truss | 구름 위 | 길 아래를 받치는 철골 기둥(매달린 구형 탱크) | 높이 14m | 길 아래, 여러 번 반복 |
| MARS09_overhang_cliff | 화성 | 위쪽이 길 쪽으로 튀어나온 절벽 조각 | 폭 30m, 높이 25m | 길 왼쪽 가까운 절벽 |

## 2순위: 촘촘함을 채우는 것 (9종, 이미지 9장)
| 번호_파일이름 | 구간 | 물건 | 크기(대략) | 쓰는 곳 |
|---|---|---|---|---|
| MARS04_boulder_b | 화성 | 중간 크기 둥근 바위(1차에서 빠짐) | 2m | 길가 |
| MARS10_rubble_pile | 화성 | 절벽 밑 돌무더기 | 폭 5m | 절벽과 바닥이 만나는 곳 |
| FOR07_fallen_log | 숲 | 이끼 낀 쓰러진 통나무 | 길이 8m | 길가 |
| FOR08_stump | 숲 | 이끼 낀 큰 그루터기 | 폭 2.5m | 길가 |
| FOR11_glow_flower_bush | 숲 | 빛나는 흰 꽃 덤불(시험용 1개) | 높이 1m | 얇은 잎은 메쉬두가 약해서 결과를 보고 쓸지 정함 |
| ICE09_ice_wall_b | 얼음 | 얼음 절벽 두 번째 모양 | 폭 30m | 1차 절벽(ICE01)과 섞기 |
| B8_storage_tank | 공용 | 받침대 위 원통 저장 탱크 2개 묶음 | 높이 5m | 달·화성·얼음 기지 |
| B12_floodlight_mast | 공용 | 조명탑(위에 투광등 4개) | 높이 8m | 밤 구간 기지에 빛 웅덩이 |
| A2_floor_hatch | 공용 | 바닥 정비 해치(1차에서 빠짐) | 1.5 x 1m | 길 바닥·기지 바닥 |

## 요청하지 않는 것 (이유)
- **구름**: 메쉬두로는 덩어리로 나온다. Claude가 Blender에서 부피 구름을 렌더해 이미지 카드로 만든다
- **고사리·풀**: 얇은 잎은 메쉬두가 약하다. 프로젝트에 있는 Poly Haven 식생을 쓴다
- **크레이터·크레바스·모래 언덕**: 지형이라 코드로 만든다
- **먼 산·행성**: 이미 셰이더와 먼 능선으로 만들었다
- **길·난간**: 코드로 만든 것을 유지한다

## 1차에서 배운 것 (이번 이미지에 반영)
- 얼음은 메쉬두 결과가 **칙칙한 남색**으로 나왔다. 이번 프롬프트는 밝은 하늘색·흰색을 명시한다
- 나무 수관은 덩어리로 나온다. 잎 덩어리 몇 개가 층층이 보이는 실루엣을 명시하고, 줄기만 있는 것(FOR04)을 따로 받는다
- 1차 49개 중 30개는 노멀 텍스처 없이 왔다. 메쉬두에서 **PBR 옵션이 켜져 있는지** 한 번 확인(이유는 모름, 없어도 처리는 됨)
- 달 바위는 1차 이미지를 못 받아 화성 바위를 회색으로 바꿔 썼다. 이번에 제대로 받는다

## 전달
- 폴더: `/Users/sangjun/scrun/art/assets_in/round2/` (1차 파일과 섞이지 않게 새 폴더)
- 파일 이름: 가능하면 위 `번호_파일이름` (예: `ICE08_snow_rock_a.glb`). 메쉬두 기본 이름 그대로여도 Claude가 맞춰 넣지만, a·b·c처럼 크기만 다른 것은 이름이 있어야 헷갈리지 않는다
- 메쉬두 설정은 1차와 같게: 이미지 한 장에 물건 하나, PBR 텍스처 켜기, 폴리곤 낮은 쪽, GLB
- 1순위만 먼저 넣어도 된다. 받은 만큼 바로 처리한다

## GPT 이미지 요청 프롬프트 (그대로 붙여 넣기)
레퍼런스 5장(`art/map/reference_space/`)과 1차 예시 시트를 같이 첨부하면 화풍이 더 잘 맞는다.

```
너는 모바일 러닝 게임의 3D 에셋 원화가야. 아래 목록의 물건을 "이미지→3D 변환(Meshy Image to 3D)"에 넣을 원화로 한 장씩 그려 줘. 첨부한 이미지들의 화풍을 기준으로 맞춰 줘.

[진행 규칙]
- 한 번 답할 때 이미지는 딱 1장만 만들어. 목록 순서대로 1번부터.
- 이미지 아래에는 파일 이름 한 줄만 적어(예: FOR03_canopy_tree_a). 다른 설명은 쓰지 마.
- 내가 "다음"이라고 하면 다음 번호를, "다시"라고 하면 같은 번호를 다시 그려.
- a, b, c로 나뉜 항목은 각각 한 장씩 따로 그려.

[모든 이미지 공통 조건]
- 물건은 딱 하나만, 화면 가운데, 잘린 곳 없이 전체가 여유 있게 보이게
- 약간 위에서 내려다본 3/4 각도
- 밝은 회색 단색 배경, 바닥 그림자는 아주 옅게
- 부드럽고 고른 스튜디오 조명(진한 그림자·강한 반사 없이 → 3D 변환 때 색이 깨끗하게 나오도록)
- 글자, 숫자, 로고, 사람 없음
- 실제 우주선·행성·기관을 닮게 하지 않기

[화풍 꼬리말]
- SF: clean semi-realistic sci-fi game asset, white and light gray paneled surfaces, yellow-orange accent stripes, small cyan LED lights, matte finish with fine panel lines and bolts, crisp beveled edges
- 화성 자연물: layered red sandstone, eroded, cream and rust strata bands, stylized realistic game asset
- 얼음 자연물: pale bright glacial ice in light sky blue and white, thick white snow on top surfaces, soft cyan glow inside, overall light value and never dark navy, stylized realistic game asset
- 숲 자연물: ancient fantasy forest, dark brown bark with green moss, faint glowing cyan veins, stylized realistic game asset
- 달 자연물: gray lunar regolith rock, pitted, dusty, stylized realistic game asset

[목록] (번호_파일이름: 그릴 것 / 꼬리말)
1. FOR03_canopy_tree_a: giant ancient tree with a thick twisted trunk, big buttress roots, and a wide flat umbrella-shaped canopy made of a few large layered leaf masses with a clear silhouette, dark green leaves / 숲 자연물
2. FOR03_canopy_tree_b: ancient tree leaning strongly to one side, twisted trunk, exposed roots, two or three flat layered leaf masses reaching out on the leaning side / 숲 자연물
3. FOR04_trunk_roots: lower part of a giant ancient tree only, massive trunk with wide buttress roots spreading on the ground, a few thick bare branch stumps at the top, no leaves at all / 숲 자연물
4. FOR09_shore_rock (a, b): a=large wet dark lakeside rock about 3 m with moss on top, b=small cluster of wet dark pebbles and rocks about 1 m / 숲 자연물
5. ICE02_ice_overhang: huge curved glacial ice wall that curls over like a frozen wave, forming an overhang tall enough for a road to pass underneath, thick snow on the outer top / 얼음 자연물
6. ICE07_ice_spikes (a, b): a=cluster of tall jagged natural ice shards about 6 m with snow at the base, b=low cluster of short broken ice shards about 2 m. Natural glacier ice, not cut gemstones / 얼음 자연물
7. ICE08_snow_rock (a, b, c): gray rocks half buried in thick white snow, a=large about 5 m, b=medium about 2 m, c=small about 0.8 m / 얼음 자연물
8. L1_lunar_rock (a, b, c): a=large angular boulder about 3 m, b=medium rounded rock about 1 m, c=small rock chunk about 0.4 m / 달 자연물
9. SKY03_station_wing: long sky station building that sits beside an elevated road, curved panoramic glass control room with warm interior lights and desks, white radome and short antennas on the roof, exposed steel truss structure hanging underneath / SF
10. SKY04_sky_hangar: sky station hangar bay with the wide front door open, warm ceiling lights and a few crates inside, landing deck in front with guard rails, steel truss structure hanging underneath / SF
11. SKY07_support_truss: tall inverted steel truss support that hangs below a platform, narrowing downward, with one spherical white tank and small warning lights at the bottom / SF
12. MARS09_overhang_cliff: tall sandstone cliff section whose upper half leans forward into a deep overhang, clear horizontal strata bands, a few fallen blocks at the base / 화성 자연물
13. MARS04_boulder_b: medium rounded sandstone boulder about 2 m / 화성 자연물
14. MARS10_rubble_pile: low sloped pile of broken sandstone blocks and scree about 5 m wide / 화성 자연물
15. FOR07_fallen_log: large fallen tree log about 8 m long covered with moss, broken at one end / 숲 자연물
16. FOR08_stump: wide mossy tree stump with spreading roots / 숲 자연물
17. FOR11_glow_flower_bush: low chunky bush with clusters of softly glowing white-cyan flowers, thick simple leaves / 숲 자연물
18. ICE09_ice_wall_b: tall glacial ice cliff wall section with vertical cracks and layered blue bands, snow cap on top / 얼음 자연물
19. B8_storage_tank: two horizontal cylindrical storage tanks on a steel frame base with pipes and a small ladder / SF
20. B12_floodlight_mast: steel floodlight mast about 8 m tall with four rectangular lamps on top, small base box / SF
21. A2_floor_hatch: rectangular metal maintenance hatch set flush in a concrete slab, bolts and a recessed handle, seen from above at a slight angle / SF

준비됐으면 1번부터 시작해 줘.
```

## 에셋 없이 Claude가 할 것 (요청과 별개, 승인 후 진행)
1. 구름 바다: Blender 부피 구름 렌더 → 이미지 카드로 교체
2. 숲: Poly Haven 고사리를 길가 앞쪽에 크게, 새 나무가 오면 옛 나무 숨김
3. 얼음: 길 오른쪽 크레바스 지형과 얼음 벽면
4. 달: 크레이터 지형, 건물 창 불빛(발광)
5. 화성: 오른쪽 협곡 풍경이 안개에 묻히는 문제
