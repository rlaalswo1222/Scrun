# 우주 루프 에셋: GPT 이미지 요청 프롬프트 (2026-11-11)

GPT에 예시 시트 이미지(달·화성·구름 위·얼음·숲)를 첨부하고 아래 전체를 한 번에 붙여 넣는다. GPT는 한 번에 한 장씩 만들고, "다음"이라고 하면 다음 물건, "다시"라고 하면 같은 물건을 다시 그린다. 받은 이미지는 메쉬두 Image to 3D에 넣고, GLB를 `/Users/sangjun/scrun/art/assets_in/`에 `파일이름_v1.glb`로 저장한다.

```
너는 모바일 러닝 게임의 3D 에셋 원화가야. 아래 목록의 물건을 "이미지→3D 변환(Meshy Image to 3D)"에 넣을 원화로 한 장씩 그려 줘. 첨부한 예시 시트들의 화풍을 기준으로 맞춰 줘.

[진행 규칙]
- 한 번 답할 때 이미지는 딱 1장만 만들어. 목록 순서대로 1번부터.
- 이미지 아래에는 파일 이름 한 줄만 적어(예: B1_moon_habitat). 다른 설명은 쓰지 마.
- 내가 "다음"이라고 하면 다음 번호를, "다시"라고 하면 같은 번호를 다시 그려.
- 바위처럼 여러 개가 필요한 항목은 a, b, c로 나눠서 각각 한 장씩 따로 그려.

[모든 이미지 공통 조건]
- 물건은 딱 하나만, 화면 가운데, 잘린 곳 없이 전체가 여유 있게 보이게
- 약간 위에서 내려다본 3/4 각도
- 밝은 회색 단색 배경, 바닥 그림자는 아주 옅게
- 부드럽고 고른 스튜디오 조명(진한 그림자·강한 반사 없이 → 3D 변환 때 색이 깨끗하게 나오도록)
- 글자, 숫자, 로고, 사람 없음
- 실제 우주선·행성·기관을 닮게 하지 않기(아폴로 착륙선, NASA 등)

[화풍 꼬리말]
- SF(기지·기계): clean semi-realistic sci-fi game asset, white and light gray paneled surfaces, yellow-orange accent stripes, small cyan LED lights, matte finish with fine panel lines and bolts, crisp beveled edges
- 화성 자연물: layered red sandstone, eroded, cream and rust strata bands, stylized realistic game asset
- 화성 기지류: 위 SF 꼬리말에서 yellow-orange 대신 orange accent stripes, slightly dusty
- 얼음 자연물: glacial blue ice with white snow on top, glowing cyan inner light, stylized realistic game asset
- 숲 자연물: dark mossy fantasy forest, faint glowing cyan veins, stylized realistic game asset
- 달 자연물: gray lunar regolith rock, pitted, stylized realistic game asset

[목록] (번호_파일이름: 그릴 것 / 꼬리말)
1. B1_moon_habitat: sci-fi lunar habitat module, large glass front window showing interior with plants, monitors and desks, side entrance door with warm lights, rooftop vents and short antennas / SF
2. B2_hangar: large sci-fi hangar with the big front door open, warm ceiling lights inside, a few crates inside, yellow warning stripes on the door frame / SF
3. B6_dish: large satellite dish antenna on a sturdy hexagonal base, tilted up / SF
4. V1_rover: six-wheeled exploration rover, white body with yellow stripes, cargo rack, roof antenna and headlights, original design / SF
5. B7_solar_panel: one solar panel array of three panels on an angled steel stand with concrete feet / SF
6. L1_lunar_rock (a, b, c): a=large boulder about 3 m, b=medium about 1 m, c=small about 0.4 m / 달 자연물
7. B5_antenna_tower: lattice antenna mast about 15 m tall with two small dishes and a red warning light on top, small base box / SF
8. V2_lander: original lunar lander, cylindrical white body with blue bands, three angled landing legs with round pads, small top antenna. No gold foil, no ladder, must not look like the Apollo lander / SF
9. MARS01_arch: large natural sandstone rock arch, wide span, thick legs / 화성 자연물
10. MARS03_mesa: flat-topped mesa with stepped strata cliffs / 화성 자연물
11. MARS04_boulder (a, b, c): a=large flat-topped block, b=medium rounded, c=small chunk / 화성 자연물
12. MARS06_station: Mars research station module with large window, white geodesic radome on the roof, antenna mast, side fuel tanks, small entrance stairs / 화성 기지류
13. MARS08_spire: tall eroded sandstone rock spire (hoodoo) with a wider cap rock / 화성 자연물
14. A1_bollard_light: square roadside bollard post about 0.9 m tall with a warm light window near the top / SF
15. B10_crate (a, b, c): a=small cargo crate, b=long cargo crate, c=flat cargo cart loaded with crates / SF
16. B11_equipment (a, b, c, d): a=charging station stand with cable, b=small tracked work robot with a robotic arm, c=surveying instrument on a tripod, d=weather sensor pole / SF
17. A3_road_gate: steel truss gate spanning a road, two pillars, light strip along the top beam / SF
18. B3_corridor: straight round connector corridor tube about 10 m long with a row of small windows, open ends / SF
19. B4_stairs: short metal staircase with yellow handrails / SF
20. V3_rocket: white rocket with blue band standing next to a steel launch tower, original design / SF
21. V5_drone: small quadcopter survey drone with a camera / SF
22. M2_floating_rock: floating rock island, flat top, broken pointed bottom with glowing cyan crystals underneath / 화성 자연물
23. M3_crystal: cluster of glowing translucent cyan crystals / 얼음 자연물
24. SKY01_control_station: sky control station with white radome, curved glass control room, open hangar bay, antenna tower and dish / SF
25. SKY02_platform: circular floating platform with guard rails, truss underside and a hanging spherical tank / SF
26. SKY05_airship: long sci-fi airship, white paneled hull, blue tail fins, gondola with warm windows / SF
27. SKY06_crane_arm: industrial crane tower with a robotic arm and gripper, small walkway platforms / SF
28. ICE01_ice_cliff: tall glacial ice cliff wall section / 얼음 자연물
29. ICE03_ice_cave: wide ice cave tunnel entrance, big enough for a road to pass through / 얼음 자연물
30. ICE05_ice_chunk (a, b, c): three sizes of ice chunks / 얼음 자연물
31. ICE06_research_dome: polar research station with white dome observatory, lit windows, antenna mast / SF
32. FOR02_root_arch: giant twisted tree roots forming a natural arch over a path / 숲 자연물
33. FOR05_mossy_rock (a, b, c): three sizes of dark mossy rocks / 숲 자연물
34. FOR06_glass_cabin: small modern glass cabin with warm interior light and indoor plants, concrete base / SF
35. FOR01_giant_tree: giant ancient tree with thick roots and a broad canopy, glowing veins on the bark / 숲 자연물
36. F2_mushroom (a, b, c): giant bioluminescent mushrooms, purple caps, glowing cyan gills, three heights / 숲 자연물
37. S1_jellyfish: floating translucent sky jellyfish creature, glowing pink and blue / 얼음 자연물 대신: whimsical glowing creature, stylized realistic

준비됐으면 1번부터 시작해 줘.
```
