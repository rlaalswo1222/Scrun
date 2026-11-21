# 우주 루프 에셋 요청서 (2026-11-11)

> 2차 요청(11/12, 1차 반입 뒤 남은 형태 차이)은 `space_loop_assets_round2.md`.

목표 화풍: 상준이 준 달 기지 레퍼런스 수준(깨끗한 반실사 SF). 지금 맵은 코드로 만든 도형이라 점토처럼 보이므로, 눈에 띄는 건물·소품·랜드마크는 제대로 만든 3D 모델로 바꾼다.
제작: 상준(메쉬두 등) → 전달 → Claude가 크기·축 맞추기, 폴리곤 줄이기, 재질 정리, Unity 배치.

## 전달 방법
- 폴더: `/Users/sangjun/scrun/art/assets_in/` (저장소 밖, 용량 때문). 구간 상관없이 한 폴더에 넣어도 됨
- 파일 이름: `요청번호_영문이름_v1.glb` (예: `B1_habitat_glass_v1.glb`). 같은 물건을 다시 뽑으면 v2, v3
- 형식: GLB 권장(텍스처가 파일 안에 들어감). FBX면 텍스처 파일을 같은 폴더에
- 텍스처: PBR(색·노멀·거칠기·금속). 큰 건물 2K, 소품 1K면 충분
- 폴리곤: 메쉬두에서 낮은 폴리곤 옵션이 있으면 고르기(소품 3천, 중간 1만, 건물 3만 정도). 많아도 Claude가 줄이지만 원본이 깔끔할수록 결과가 좋음
- 자세·방향은 신경 쓰지 않아도 됨(Claude가 맞춤). 한 파일에 물건 하나

## 공통 화풍 (모든 이미지·프롬프트에 같은 꼬리말을 붙여 화풍을 맞춘다)
기지·기계류:
> clean semi-realistic sci-fi game asset, white and light gray paneled surfaces, yellow-orange accent stripes, small cyan LED lights, matte finish with fine panel lines and bolts, crisp beveled edges, plain background

자연물은 구간별 꼬리말을 아래 각 항목에 적었다.
금지: 실제 우주선·행성·로고를 닮게 하지 않기(아폴로 착륙선, NASA 로고, 지구 등). 글자는 넣지 않기(나중에 Unity에서 붙임).

## 1차 (가장 먼저: 여러 구간에서 같이 쓰는 기지 키트 + 길가 소품)
레퍼런스 화면의 대부분이 이 묶음이다. 달 기지·화성 기지·얼음 연구소·구름 위 관제소가 모두 같은 키트를 다시 쓴다.

| 번호 | 물건 | 크기(대략) | 설명 |
|---|---|---|---|
| B1 | 유리 전면 거주 모듈 | 20 x 10 x 7m | 앞면 전체가 유리, 안에 식물·모니터·책상이 보임, 옆에 출입문, 지붕에 환기 장치 |
| B2 | 격납고 | 30 x 20 x 14m | 큰 문이 열려 있고 안쪽 천장 조명, 문틀에 노란 경고 줄무늬 |
| B3 | 연결 통로 튜브 | 길이 10m, 지름 3m | 둥근 통로, 옆면 작은 창 줄, 이어 붙일 수 있게 양 끝이 열린 모양 |
| B4 | 계단 + 난간 | 높이 2.5m | 철제 계단, 노란 난간, 출입문 앞에 붙이는 용도 |
| B5 | 안테나 탑 | 높이 15m | 격자 철탑, 꼭대기 빨간 경고등, 중간 작은 접시 |
| B6 | 접시 안테나 | 지름 8m | 받침대 위 큰 접시, 기울어진 자세 |
| B7 | 태양광 패널 1장 | 6 x 3m | 받침 기둥 포함(여러 장 줄지어 배치) |
| B10 | 화물 세트 | 상자 1~2m | 상자 2~3종 + 상자를 실은 화물 수레 1종 |
| B11 | 작업 장비 | 1~2m | 충전 스탠드, 작은 바퀴형 작업 로봇(팔 달림), 측정기 삼각대, 기상 센서 기둥 |
| A1 | 길가 볼라드 조명 | 높이 0.9m | 사각 기둥, 위쪽에 따뜻한 조명 창 |
| A2 | 길 바닥 정비 해치 | 1.5 x 1m | 바닥에 묻힌 사각 금속 덮개(볼트·손잡이) |
| A3 | 길 위 게이트 | 폭 16m, 높이 9m | 길을 가로지르는 철골 문, 양쪽 기둥, 위에 조명 띠 |

## 2차 (구간 대표 랜드마크·탈것)
| 번호 | 물건 | 크기 | 설명 |
|---|---|---|---|
| V1 | 탐사차 | 4 x 2m | 바퀴 6개, 흰 차체 + 노란 줄, 지붕 안테나·조명 (창작 디자인) |
| V2 | 착륙선 | 높이 8m | 다리 4개, 금색 단열재 아랫단 + 흰 몸체 (아폴로처럼 생기지 않게) |
| V3 | 로켓 + 발사대 | 높이 45m | 흰 로켓, 옆 철골 발사탑 |
| V4 | 비행선 | 길이 60m | 흰 몸체, 파란 꼬리날개, 아래 객실 (구름 위) |
| V5 | 드론 | 1m | 프로펠러 4개 정찰 드론 (화성) |
| V6 | 떠 있는 플랫폼 | 지름 25m | 원판 위 작은 건물, 아래 매달린 구형 탱크 (구름 위) |

## 3차 (구간별 자연물)
| 번호 | 구간 | 물건 | 꼬리말(프롬프트) |
|---|---|---|---|
| M1 | 화성 | 지층 사암 바위 3~4종(0.5~4m), 윗면 평평 | layered red sandstone boulder, eroded, flat top, stylized realistic |
| M2 | 화성 | 떠 있는 바위 2종(아래가 깨진 뿌리 모양, 청록 결정 박힘) | floating rock island with broken bottom and glowing cyan crystals |
| M3 | 화성·얼음 | 빛나는 결정 무리 2종(1~3m) | cluster of glowing crystals, cyan, translucent |
| F1 | 숲 | 거목 2~3종(높이 15~20m, 굵은 뿌리) | giant ancient tree, thick roots, dense canopy, faint glowing veins on bark |
| F2 | 숲 | 발광 버섯 3종(1~7m) | giant bioluminescent mushroom, purple cap, glowing cyan gills |
| F3 | 숲 | 고사리·덤불 3종 | fern bush, lush, game foliage |
| F4 | 숲 | 유리 오두막 | small modern glass cabin with warm interior light, wooden deck |
| I1 | 얼음 | 눈 덮인 바위 3종, 얼음 덩어리 2종 | snow covered rock / blue ice chunk, stylized realistic |
| L1 | 달 | 달 바위 3~4종(0.3~3m) | gray lunar regolith boulder, pitted |
| S1 | 구름 위 | 발광 해파리 생물 | floating translucent jellyfish creature, glowing pink and blue |

## 상준 예시 시트 검토 (11/11, 5장 x 8개)
판단 기준: 메쉬두로 3D가 잘 나오는가(딱딱한 물체는 잘 됨, 얇은 잎·구름·물은 잘 안 됨), 코드·셰이더로 이미 되는 것과 겹치는가, 실제 물건을 닮았는가, 여러 번 다시 쓰는가.

| 시트 | 쓴다 | 고쳐서 쓴다 | 뺀다 |
|---|---|---|---|
| 달 | 01 거주 모듈(B1), 02 격납고(B2), 03 접시(B6), 05 탐사차(V1), 06 태양광(B7), 08 바위(L1) | 04 착륙선: 아폴로 착륙선과 너무 닮음 → 다리 3개·원통 몸체·흰색+파랑, 금박·사다리 빼기 | 07 크레이터(지형 코드로 만듦) |
| 화성 | 01 아치(길 위 랜드마크), 03 메사(협곡 배경), 04 바위(M1), 06 화성 기지, 08 바위 기둥 | 02 절벽 조각: 긴 지층 벽은 코드로 두고 길가 강조용 2~3개만 | 05 모래 언덕 타일(지형·질감으로), 07 밴은 선택(달 탐사차와 겹침) |
| 구름 위 | 01 관제 기지, 02 떠 있는 원판 플랫폼(V6), 05 비행선(V4), 06 크레인 로봇팔, 07 안테나 탑(B5, 모든 구간 공용) | | 03 다리·04 거꾸로 플랫폼(길은 코드, 02와 겹침), 08 구름(메쉬두로는 무겁고 덩어리져 보임 → Unity에서) |
| 얼음 | 01 얼음 절벽, 03 얼음 동굴(구간 전환 터널, 폭 20m로 키움), 05 얼음 덩어리(I1), 06 연구 돔 | 04 설산: 먼 배경용으로만, 우선순위 낮음 | 02 크레바스·08 얼음 바닥(지형으로), 07 다리(길은 코드) |
| 숲 | 02 뿌리 아치(길 위 통과 랜드마크), 05 이끼 바위, 06 유리 오두막 | 01 거목: 메쉬두는 잎이 덩어리로 나와 무거움 → 길가 대표 나무 2~3그루에만 | 03 고사리·04 빛나는 꽃(얇은 잎은 프로젝트에 있는 Poly Haven 고사리·덤불 카드로), 07 데크 다리, 08 물(물 셰이더) |

### 아직 없는 것 (레퍼런스의 촘촘함은 대부분 이 작은 소품에서 나옴)
A1 길가 볼라드 조명, A2 바닥 정비 해치, A3 길 위 철골 게이트, B3 연결 통로 튜브, B4 계단+난간, B10 화물 상자·수레, B11 작업 장비(충전 스탠드·작은 작업 로봇·측정 삼각대·기상 센서), V3 로켓+발사대, V5 드론, M2 떠 있는 바위, M3 빛나는 결정 무리, F2 발광 버섯, S1 발광 해파리

### 메쉬두에 넣을 때
- 시트 한 장에 여러 개가 있으면 메쉬두가 하나로 붙여 버린다 → **물건 하나씩 따로 잘라서**(배경 단색, 3/4 각도, 번호 글자 없이) 넣기
- 바위처럼 여러 개가 한 그림에 있는 것도 하나씩
- 파일 이름은 위 번호를 따름(예: `B1_moon_habitat_v1.glb`, `MARS06_station_v1.glb`)

## 만드는 흐름 (11/11 확정): GPT로 한 개씩 이미지 → 메쉬두 Image to 3D
GPT 이미지 프롬프트 틀(대괄호만 바꿔 넣기, 예시 시트 이미지를 같이 첨부하면 화풍이 더 잘 맞음):
> A single [물건 설명], game asset concept for image-to-3D. Exactly one object, centered, whole object visible with margin, three-quarter view from slightly above, plain light gray background, soft even studio lighting with no hard shadows, no text, no labels, no people. Style: clean semi-realistic sci-fi, white and light gray paneled surfaces, yellow-orange accent stripes, small cyan LED lights, matte finish with fine panel lines and bolts, crisp beveled edges.

자연물은 Style 부분을 그 항목의 꼬리말로 바꾼다. 메쉬두에서는 이미지만 넣어도 되고, 글 입력칸이 있으면 물건 이름을 짧게(예: "sci-fi habitat module with glass front") 넣는다. PBR 텍스처 켜기, 폴리곤은 낮은 쪽, GLB로 받기.

## 받은 뒤 Claude가 하는 일
1. 크기를 위 표대로 맞추고 바닥 가운데를 기준점으로
2. 폴리곤 줄이기(먼 거리용 단계 LOD 포함), 텍스처 크기 정리, 재질을 Unity URP용으로
3. 구간 장면에 배치하고 아이폰 예산(그리기 호출 300 이하) 안인지 확인
4. 반복되는 소품(볼라드·패널·바위)은 한 번만 그리는 방식(인스턴싱)으로
