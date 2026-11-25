# 계획 0073: API를 프로세스 여러 개로 — 실시간 분리

- 상태: **승인**(2026-11-26 민재: 실시간 분리 + 내부 API, OCR 프로세스 따로, CPU 사용처 먼저 재기, 시험 서버 CPU를 물리 코어 2개로)
- 담당: 민재
- 왜: 계획 0070 S1([docs/perf/README.md](../perf/README.md#가상-사용자를-늘릴-때-기록-100만-2분))에서 동시 100명이면 api CPU가 최대 109% = **한 코어에서 막힘**. 파이썬 프로세스 하나는 GIL 때문에 한 번에 한 코어만 씀(스레드 40개여도). 운영도 uvicorn 한 프로세스. 0071 뒤 포화 처리량은 약 185/초(집 PC)
- 결과물: 일반 API(REST)는 **프로세스 N개**, 실시간(WebSocket)은 **따로 한 프로세스**. 포화 처리량이 코어 수만큼 늘어나는지 전후로 잼

## 지금 한 프로세스라서 기대고 있는 것 (코드 확인)
| 메모리 상태 | 어디서 씀 | 프로세스를 늘리면 |
|---|---|---|
| **실시간 허브** `realtime/hub.py`(방·러너·봇·함께 달린 사람) | WebSocket 말고도 REST 6곳: 맵 입장 정원 `occupancy.count_present`, 코스 미리보기 `courses`(달리는 위치), 친구 `friends`(지금 달리는 중·어느 맵), 러닝 시작·종료 `runs`(함께 달린 사람 모으기·읽기), 계정 삭제 `accounts`(forget_user) | 프로세스마다 다른 허브 → 정원이 깨지고, 친구가 달리는데 안 보이고, 함께 달린 사람이 빈 목록 |
| OCR 읽기 프로세스 2개 `core/ocr.py` | 서버 시작 때 띄움 | REST 프로세스마다 2개씩 → 2코어 서버에 OCR 4개 이상, 메모리도 그만큼 |
| 친구 코드 실패 횟수 `core/attempts.py` | 친구 추가 무차별 대입 막기 | 프로세스마다 따로 세서 제한이 N배 느슨해짐 |
| 번호표 12장(0071)·DB 연결 풀 15 | 모든 HTTP 요청 | 프로세스마다 따로 → 합계 N배. DB 최대 연결 100 안인지 맞춰야 함 |

## 하는 것 (추천안: 실시간을 떼고 REST만 늘림)
1. **실시간 프로세스(`realtime`) 하나**: 같은 이미지, WebSocket `/v1/realtime` + 허브만. 허브는 지금 코드 그대로 메모리에 둠(고쳐 쓰지 않음)
2. **REST 프로세스 N개**: uvicorn `--workers N`(먼저 N = 2, 코어 수). Caddy가 `/v1/realtime`은 실시간으로, 나머지는 REST로 보냄
3. **REST → 허브는 내부 HTTP로**: 실시간 프로세스에 컨테이너 안에서만 열리는 내부 API(지금 허브 함수 6개와 같은 모양)를 두고, REST는 `hub.xxx()` 대신 작은 클라이언트로 부름. 같은 기계 안이라 1 ms 안팎(가설, 잼). 실시간이 죽어 있으면: 정원 검사는 DB 입장 기록만으로(지금 "입장 2분" 규칙), 친구 "달리는 중"은 빈 값, 함께 달린 사람은 빈 목록 → 기능이 멈추지 않게
4. **OCR은 한 곳에만**: REST 프로세스에서는 안 띄우고 실시간 프로세스처럼 따로 하나(`ocr`)를 둬서 인증 사진 경로만 그리로 보냄(0072 줄을 나중에 여기에 얹음). 또는 당분간 인증 사진 경로를 실시간 프로세스로 보내기(정할 것 2)
5. **친구 코드 실패 횟수는 DB로**(작은 표 또는 기존 시도 기록으로 셈)
6. **번호표·풀 다시 맞추기**: 프로세스당 번호표·풀을 나눠 합계가 DB 한도 안이 되게(예: REST 2개 × 풀 15 + 실시간 풀 5). 설정 검사(ConfigError)도 같이
7. **덤**: 실시간이 따로 있으면 REST만 바꾼 배포에서는 방이 안 끊기게 할 수 있음(실시간 컨테이너를 다시 안 만들기). 이번엔 측정만, 배포 방식은 따로

### 먼저 할 것: CPU가 어디에 쓰이는지 재기 (반나절)
프로세스를 늘리기 전에 py-spy로 100명일 때 api CPU를 쪼개 봄(JSON 만들기·토큰 확인·ORM·요청 로그 등). 한 요청이 쓰는 CPU를 줄이면 프로세스 수와 곱해져 효과가 커짐. 큰 몫이 보이면 따로 고치고, 이 계획은 그대로 진행

#### 잰 결과 (11/26 01:44–01:48, S1 100명 3분 중 2분, py-spy `--gil` 100 Hz, 원본 `data/perf/20261126/prof_s1_v100/pyspy/`)
- **시험 서버 CPU**: VM 안에선 `0,1`이 한 물리 코어의 두 스레드로 보이지만, 파이썬 계산 2개를 돌리면 `0,1`·`2,4` 모두 약 2배로 늘어남(한 개 `2`는 1배) → WSL2 가상 CPU는 실제 스레드에 묶여 있지 않음. **설정은 그대로, 0071 기준선을 그대로 비교 기준으로 씀**(위 "정할 것" 4의 걱정은 틀림)
- py-spy가 붙어 처리량이 150.9/초로 내려감(평소 181) → 이 측정은 비교에 안 씀. 표본 5,243개(오류 2,444개라 대략 값)
- GIL을 쥔 시간 중 **이벤트 루프 스레드 65%**, 작업 스레드(라우터·DB) 35%
  - 루프 쪽: 미들웨어 두 겹(`request_id`·`request_log`, `@app.middleware("http")` = `BaseHTTPMiddleware`)의 스트림·태스크 처리 약 27%, 동기 의존성(`get_db`·`current_user`)·라우터를 스레드로 넘기고 받는 일(anyio) 큰 몫, 응답 JSON 9%(그중 **`CamelModel._utc_times`가 모든 칸마다 파이썬을 부름 약 5%**), HTTP 파싱·소켓 16%
  - 작업 스레드 쪽: SQLAlchemy ORM + psycopg 32%(같은 표본 기준, 겹침 있음), 매 요청 `current_user`가 사용자를 읽는 몫 9%
- **한 요청 CPU를 줄일 후보**(가설, 재서 확인): ① 미들웨어 두 겹을 순수 ASGI 하나로 ② `_utc_times`를 시각 칸에만 ③ 동기 의존성 스레드 넘기기 줄이기

#### 먼저 고친 것 (11/26, 같은 브랜치의 앞 커밋으로)
- 미들웨어 두 겹 → 순수 ASGI 하나(`RequestContextMiddleware`, `app/core/request_log.py`): 요청 ID 붙이기 + 요청 로그. 로그를 꺼도 요청 ID는 붙음
- `_utc_times`(모든 칸) → 시각 칸에만 `UtcDatetime`(`app/core/model.py`), 19칸. 그냥 `datetime` 칸이 생기면 `test_time_fields_are_utc_typed` 실패. OpenAPI 문서는 전후 글자까지 같음
- 측정: 아래 "결과"

## 구현 (11/26)
- **허브 내부 API** `app/realtime/internal.py`: `POST /internal/hub/{op}`(op 8개 = 일반 API가 쓰는 허브 함수), 비밀 머리 `X-Scrun-Internal`(JWT_SECRET에서 HMAC으로 만든 값, 새 환경 변수 없음), 틀리면 빈 404, OpenAPI에 안 나옴, 이벤트 루프에서 바로 처리
- **`hub_link`** `app/realtime/link.py`: `REALTIME_URL`이 없으면 같은 프로세스의 허브 그대로, 있으면 `RemoteHub`(httpx, 1초 제한). 실패하면 "방에 아무도 없음"으로 진행(정원은 DB 입장 2분 안 사람만, 친구 달리는 중·입장 위치 빈 값, 함께 달린 사람 빈 목록), 계정 삭제의 허브 빼기 실패는 ERROR 로그
- 라우터 5곳(`courses`·`friends`·`runs`·`accounts`·`occupancy`)이 `hub` 대신 `hub_link`. `REALTIME_URL`이 있는 프로세스는 WebSocket·내부 API를 안 붙임(`main.py`)
- **친구 코드 실패 횟수 → DB** `friend_code_failures` 표(마이그레이션 `c4e8a1f7b2d9`), 사람별 `pg_advisory_xact_lock`으로 동시 시도를 줄 세움, 미리 센 줄은 바로 커밋
- **배포** `deploy/docker-compose.server.yml`: 같은 이미지(`scrun-app:local`, `api`만 빌드)로 `api`(uvicorn `--workers 2`, `OCR_ENGINE=none`, `REALTIME_URL`) · `realtime`(허브) · `ocr`(`OCR_ENGINE=onnx`). DB 연결 합계 60(한도 100). `deploy/Caddyfile`: `/internal/*` 404, `/v1/realtime` → realtime, `^/v1/runs/[^/]+/verification/photo$` → ocr, 나머지 → api. `update.sh`는 세 서비스 건강을 모두 기다림
- **시험 서버** `perf/docker-compose.perf.yml`: 같은 셋 + Caddy 8080(운영 Caddyfile). S1·S2는 api 8000에 바로(기준선과 같은 길), S3·S4는 Caddy 8080. `API_WORKERS`로 프로세스 수
- **테스트**: 내부 API·원격 허브·실시간이 죽었을 때·`REALTIME_URL` 검사(`test_realtime_link.py`), 허브를 거치는 기존 테스트 14개를 원격 허브로 한 번 더(`test_link_*.py`: 함께 달린 사람, 친구 달리는 중·같이 달리기 입장, 정원 21번째 거절·동시 입장, 계정 삭제). 서버 테스트 476 통과
- **운영 배포 때**: Caddyfile이 바뀌므로 `update.sh develop` 뒤 `docker compose -f docker-compose.server.yml exec caddy caddy reload --config /etc/caddy/Caddyfile`. OCR 로그는 이제 `scrun-ocr-1`(`journalctl CONTAINER_NAME=scrun-ocr-1`)

### 다른 안: 허브를 Redis로 (ADR-0010 "확장" 항목)
- 모든 프로세스가 같은 일을 하고 방 상태·알림을 Redis로 나눔 → 실시간까지 여러 개로 늘릴 수 있고 배포해도 방이 남음
- 하지만 허브 466줄을 고쳐 써야 하고(잠금·봇 위치·함께 달린 사람), 새 기술이라 ADR·설명부터. 지금 실시간은 병목이 아님(S3 100명 WebSocket 왕복 p95 6–7 ms). 실시간 사용자가 한 프로세스를 넘을 때 Redis로

## 정할 것
1. **실시간 분리 + 내부 HTTP(추천)** vs Redis로 허브 옮기기
2. OCR 자리: 따로 `ocr` 프로세스(추천, 0072 줄을 얹을 곳) vs 당분간 실시간 프로세스에 같이
3. 먼저 py-spy로 재기(추천) vs 바로 프로세스 늘리기
4. 시험 서버 CPU: 지금 `cpuset 0,1`은 Ryzen의 **한 물리 코어의 두 스레드**일 가능성이 큼(perf compose 주석) → 프로세스를 늘려도 이득이 작게 나옴. 운영처럼 **물리 코어 2개**로 바꾸고 기준선을 그 조건으로 다시 잰 뒤 비교(추천)

## 판정 (집 PC, 물리 코어 2개로 바꾼 조건에서 기준선 다시 → 전후)
| | 목표 |
|---|---|
| S1 포화 처리량(200명) | 늘어남. 얼마나 늘었는지 기록하고, 30% 미만이면 프로세스를 늘릴 가치를 다시 판단 |
| S1 100명 서버 p95 | 기준선보다 나빠지지 않음(10% 안) |
| S3 100명 한꺼번에 ×3 | 3번 모두 성공(정원·입장이 프로세스가 달라도 맞게) |
| S2 달리기 한 번 | 함께 달린 사람이 종료 응답에 들어감(REST와 실시간 프로세스가 달라도) |
| 실시간 프로세스를 끈 채 S1 | REST 실패 0%(허브 없이도 멈추지 않음) |
- 서버 테스트 전부와 새 테스트(내부 API 클라이언트, 실시간이 없을 때) 통과, 운영 배포 뒤 `queue_ms`·로그로 두 종류 프로세스 확인
