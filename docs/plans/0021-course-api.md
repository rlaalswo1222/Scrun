# 계획 0021: 코스 API 3개를 DB와 연결 — BL-7 (S1 지환)

- 상태: **구현 완료 · PR 리뷰 반영** (11/08 지환 승인 → 구현 → `/code-review` 반영 → 11/10 민재 답변 반영(아래 "11/10 바뀐 것") → 11/10 민재 PR 리뷰 반영(아래 "PR #30 리뷰 반영"). 테스트 173개 통과)
- 담당: 지환. 리뷰는 민재(서버 PR 리뷰)
- 번호: 처음엔 0019로 썼으나 develop에 0019(공용 개발 서버)·0020(속도 센서, PR)이 먼저 생겨 0021로 바꿈
- 왜: 코스 API가 고정 예시(`crs_island_loop` 한 개, 러너 7명)만 돌려줬다. 코스 화면(C-01, C-02)과 맵 입장(I-10, I-11)을 실제 데이터로 만들려면 DB에서 읽어야 한다
- 기준: 노션 작업 표 "[BE] 코스 API", BL-7 인수 조건, [데이터 모델](../api/data_model.md), [API 공통 규칙](../api/README.md) "코스와 맵 인원"과 `COURSE_NOT_FOUND`·`COURSE_FULL`·`CALIBRATION_REQUIRED`

## 한 것
1. **코스 데이터**: 마이그레이션 `seed_courses`로 기존 섬 맵(Island01) 코스 4개. 원본은 `IslandLayout.json`의 `routes`. 길이는 Unity `RoadNetwork.BuildRoute`(Catmull-Rom 곡선, 높낮이, 교차로 모서리 12m)를 파이썬으로 그대로 옮겨 잼. 문서의 6.7/8.8/9.7/10.9km와 맞음

| id | 이름 | 한 바퀴 |
|---|---|---|
| `crs_lake_loop` | 노을 호수 한 바퀴 | 6671 m |
| `crs_coast_run` | 해안 절벽 순환 | 8776 m |
| `crs_forest_run` | 거목 협곡 순환 | 9695 m |
| `crs_island_tour` | 섬 일주 | 10884 m |

2. **`GET /v1/courses`**: `active` 코스를 짧은 것부터. `runnersNow` = 그 맵에 지금 있는 사람 수(아래 결정 1)
3. **`GET /v1/courses/{courseId}`**: + 설명·분위기·`maxRunnersPerMap`(20). 없거나 숨긴 코스는 404
4. **`POST /v1/courses/{courseId}/entries`**: 코스 줄을 잠그고(`SELECT … FOR UPDATE`) 판정 → 코스 없음 404 → 보정 없음 409 `CALIBRATION_REQUIRED` → 나 말고 20명 이상 409 `COURSE_FULL` → 자리 잡기(`course_entries`에 쓰고 커밋). 응답은 `map_<courseId>`, 코스 위 랜덤 위치, `realtime: null`
5. 세 API 모두 진짜 로그인(`current_user`) 필요(진짜가 된 API 공통 규칙). 썸네일은 없으면 `null`(앱 기본 그림)
6. **테스트**(`tests/test_courses.py` 14개): 목록·상세, 사람 단위로 세기(입장만 한 사람 포함, 오래된 기록·입장 제외), 숨긴 코스, 보정 없음, 자리 옮기기, 21번째 거절, **19명일 때 세 명 동시 입장 → 한 명만**(잠금을 끄면 실패하는 것까지 확인), 로그인 없음 401·가입용 토큰 403
7. 함께 고친 것: `stub_requests.real_key`(주소에 ID가 든 진짜 API를 계약 테스트·예시 내보내기가 거르지 못하던 것), `export_openapi`가 어느 OS에서든 CRLF로 씀(맥에서 다시 만들어도 예시가 전부 바뀌지 않게), 스텁 예시의 `crs_island_loop`·`map_01` → 실제 값, 테스트 도우미를 `tests/helpers.py`로

## 결정
1. **인원 세기(실시간 서버 전까지 임시)**: 최근 10분 안에 입장(`course_entries`)했거나 달리는 중(`runs` ACTIVE + `updated_at` 최근 10분)인 사람 수. 세는 곳은 `app/core/occupancy.count_present` 하나. 실시간 서버(ADR-0010 WebSocket)가 생기면 연결 수로 바꾼다(11/10 민재)
2. **동시 입장**: 코스 줄 잠금 + 입장 기록(11/10 지환 결정). 입장만으로는 DB에 남는 게 없어서 잠그기만 하면 19명일 때 셋이 차례로 들어와도 셋 다 19명을 보고 통과함 → 입장할 때 자리를 써야 함. 새 테이블 `course_entries`(사람당 한 줄)
3. **입장 위치**: 러너 위치는 실시간 서버에만 있으므로 지금은 랜덤. BL-25 뒤에 "러너 많은 구간 근처"로
4. **임시 코스 길이**: BL-43은 3km 루프인데 기존 코스는 6.7–10.9km. 새 맵이 나올 때까지 실제 길이 그대로, 바뀔 때 마이그레이션 하나로 교체

## 11/10 바뀐 것 (민재 답변)
- 인원 기준은 실시간 방식(WebSocket)으로 정해짐 → 연결 수. `runs.updated_at` 갱신 방식은 쓰지 않음. 그 전까지 지금 방식, 세는 곳 한 곳으로
- `thumbnailUrl`은 `null`(앱은 null이면 기본 그림)
- 입장 때 코스를 잠가 동시 입장에도 최대 인원을 넘지 않게

## PR #30 리뷰 반영 (11/10 민재)
- **보정값 API(BL-4, 원래 지환 S2)를 이 PR에서 DB와 연결**: 입장이 `calibrations`를 보는데 `PUT /v1/users/me/calibration`이 스텁이라 저장이 안 되고 스텁 GET은 늘 "있음"이라, 개발 서버에서 보정을 마쳐도 입장이 계속 `CALIBRATION_REQUIRED`(앱은 보정 ↔ 코스를 오감). 이제 PUT은 이력으로 한 줄 쌓고(가장 최근 것이 현재 보정, `docs/api/data_model.md`), GET은 가장 최근 것 또는 null. 요청 모양은 그대로(points·model·device, model은 폰 SpeedEstimator 값을 해석하지 않고 보관 — 민재 보정 화면이 `{"version":1,"points":[{v,limp,cad,amp,running}]}`를 넣음). 응답 시각은 UTC(Z). 테스트 7개(없음 → null, 그대로 돌려줌, 가장 최근 것, 남의 것 안 보임, 형식 틀림 400, 로그인 없음 401, 올리면 입장 가능)
- 입장 잠금을 `FOR NO KEY UPDATE`로(입장끼리만 줄 세움, 이 코스를 가리키는 `runs`·`course_entries` 삽입의 외래 키 확인은 막지 않음)
- 인원 세기 인덱스: `course_entries(course_id, entered_at)`(기존 course_id 인덱스 대신), `runs(course_id, status, updated_at)`. 마이그레이션 `5b7e2d9c4a13`
- `runnersNow` 설명을 실제대로("그 맵에 지금 있는 사람 수, 임시: 최근 10분 안에 입장했거나 달리는 중"), `data_model.md` ID 예시 `crs_island_loop` → `crs_lake_loop`
- `scripts/real_examples.py`: 보정을 DB에 직접 넣던 것 → PUT으로(사용자를 아무나 집던 문제도 없어짐), 실패 응답이 오면 멈춤, 저장 시각을 고정 값으로(다시 만들어도 예시가 안 바뀜). 가입 도우미를 `tests/helpers.py`로
- 안 고친 것: `real_key` 경로 틀 매칭이 `test_contract.py`·`notion_pages.py`에도 따로 있는 것(세 벌 → 하나로, 다음에), `export_openapi`만 CRLF 고정(`.gitattributes`는 팀 결정, 11/06엔 안 하기로)

## 남은 것
- **20명 제한이 실제로는 10분 뒤 풀린다(임시 방식의 한계)**: 인원은 입장 뒤 10분(FRESH)만 세고, 달리는 중 기록(`runs.updated_at`)을 갱신하는 곳이 아직 없다(runs API 스텁). 그래서 20명이 30분 달리면 10분 뒤 0명으로 보이고 20명이 더 들어올 수 있다. 실시간 서버(S3, ADR-0010)의 연결 수로 바꾸면 풀림
- 입장 기록은 10분 동안 자리를 차지한다. 달리기를 끝내도(`runs` 종료) 10분 안에는 인원에 남으므로, 러닝 종료 API(BL-14, 민재)에서 그 사람의 `course_entries`를 지우면 정확해진다. 실시간 서버로 바뀌면 이 표는 다시 본다
- 노션 API 명세 페이지(코스 3개) 다시 생성(`scripts.notion_pages`)

## 완료 확인
pytest 173개 통과(보정 포함). `/docs`에서 코스 목록·상세가 위 4개 코스로 나오고, 21번째 입장은 거절된다
