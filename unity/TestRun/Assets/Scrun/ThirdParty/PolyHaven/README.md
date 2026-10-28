# Poly Haven 에셋

- 출처: https://polyhaven.com (API: https://api.polyhaven.com)
- 라이선스: CC0 1.0 (퍼블릭 도메인, 출처 표기 의무 없음, 상업적 사용 가능) — https://polyhaven.com/license
- 받은 날: 2026-10-26, 해상도 1k
- 파일 이름: `<에셋>_diff_1k.jpg`(색), `_nor_gl_1k.png`(노멀, OpenGL 방식), `_rough_1k.jpg`(거칠기), `_alpha_1k.png`(잎 투명도), `<에셋>.fbx`(모델)

## Textures
asphalt_02, grass_ground, gravelly_sand, concrete_floor_02, coast_sand_01, rocky_terrain_02

## Models
fern_02, shrub_sorrel_01, shrub_03, shrub_04, weed_plant_02, nettle_plant,
flower_empodium, flower_gazania, dandelion_01,
rock_moss_set_01, rock_07, rock_09, boulder_01, tree_stump_01, dead_tree_trunk_02

## Models (소품, 2026-10-26 추가)
modular_wooden_pier, street_lamp_01, painted_wooden_bench, wooden_picnic_table, dutch_ship_medium,
ocean_buoy, wooden_crate_01, barrel_03, lifebuoy
- 여러 부위 모델은 부위별 맵: `<에셋>_<부위>_diff_1k.jpg` 등 (예: modular_wooden_pier_planks_diff_1k.jpg)

## Textures (나무껍질·바위, 2026-10-27 추가)
metasequoia_bark(거목), pine_bark(침엽수), bark_brown_02(활엽수), palm_bark(야자) → `NatureTextures.BarkArray`가 세로로 이어 텍스처 배열로
rock_boulder_dry(색·노멀) → 바위·바위벽·가파른 지형의 3면 투영(`NatureTextures.ApplyRock`)

## 잎 사진(Assets 밖)
`unity/tools/foliage_src/`: fir_tree_01 twig, tree_small_02 leaves (2k diff/alpha) → `unity/tools/foliage_photo_atlas.py`가
`Sty_FoliagePhoto.png`(침엽수 가지·활엽수 잎 뭉치·원거리 침엽수 실루엣)로 합성 → `FoliageAtlas`가 읽어 잎 아틀라스에 넣음
