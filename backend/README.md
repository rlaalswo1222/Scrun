# SCRUN 서버 (`backend/`)

FastAPI + PostgreSQL. API 42개 중 **로그인 5개 + 닉네임 확인은 진짜**(DB 저장·토큰 발급, 계획 [0016](../docs/plans/0016-server-skeleton.md)), **나머지 36개는 아직 스텁**(고정 예시, 계획 [0014](../docs/plans/0014-api-spec.md)). 스텁은 묶음별로 하나씩 진짜로 바꾼다. 옛 `server.py`(코스 만들기 실험 서버)와는 별개.

공통 규칙(응답 껍데기·상태 코드·오류 코드·단위)은 [docs/api/README.md](../docs/api/README.md), DB 테이블은 [docs/api/data_model.md](../docs/api/data_model.md), API 목록·진행은 노션 SCRUN > 문서 > API 명세.

## 처음 한 번
```bash
cd backend
../.venv/Scripts/python -m pip install -r requirements-dev.txt   # 맥은 ../.venv/bin/python
cp .env.example .env                                            # 접속 정보·키. .env는 git에 안 올림
../.venv/Scripts/python -c "import secrets; print(secrets.token_urlsafe(48))"   # 나온 값을 .env의 JWT_SECRET=에
docker compose up -d                                            # PostgreSQL(개발 DB scrun + 테스트 DB scrun_test)
../.venv/Scripts/python -m alembic upgrade head                 # 테이블 20개 + 약관 목록
```
`.env`에 `JWT_SECRET`이 없거나 32자 미만이면 서버가 시작하지 않는다(`ConfigError`). `KAKAO_MODE`를 안 적으면 `real`이고 `KAKAO_APP_ID`가 필요하다. 로컬 개발은 `.env.example`대로 `KAKAO_MODE=fake`. 테스트·예시 내보내기는 `.env` 없이도 돈다(임시 키 + 가짜 카카오, `app/testenv.py`).
Docker Desktop이 켜져 있어야 한다. DB를 지우고 처음부터: `docker compose down -v` → 위 두 줄 다시.

## 실행
```bash
docker compose up -d
../.venv/Scripts/python -m uvicorn app.main:app --reload --port 8000
```
http://localhost:8000/docs 에서 눌러 본다.

### 진짜 로그인 해 보기(개발 서버는 가짜 카카오)
1. `POST /v1/auth/kakao`에 `{"kakaoAccessToken": "k_123"}` → 처음이면 `signupToken`
2. 오른쪽 위 **Authorize**에 그 `signupToken` → `POST /v1/auth/signup`(약관 `terms`·`privacy`, 닉네임·키·몸무게) → `accessToken`·`refreshToken`
3. 같은 `k_123`으로 다시 로그인하면 바로 토큰. `k_` 뒤 숫자를 바꾸면 다른 카카오 계정
- 실제 카카오로 확인하려면 `.env`에 `KAKAO_MODE=real` + `KAKAO_APP_ID=`(카카오 개발자 > 내 애플리케이션 > 앱 ID). 다른 앱에서 발급된 카카오 토큰은 거절
- 닉네임은 `GET /v1/users/nickname/check`로 먼저 확인(가입과 같은 판정, 규칙은 [docs/api/README.md "닉네임"](../docs/api/README.md))

### 스텁 API(36개) 규칙 — 오류 화면 확인용
스텁 API는 토큰을 검사하지 않는다(아무 토큰이나 예시 사용자 `usr_demo`). 진짜가 되면 진짜 토큰만 받는다.
| 넣는 값 | 결과 |
|---|---|
| 토큰 없음 | 401 `UNAUTHORIZED` |
| 토큰 `expired` | 401 `TOKEN_EXPIRED` |
| 토큰 `signup` | 가입용 토큰 흉내(스텁 API는 403 `SIGNUP_REQUIRED`) |
| 내 정보 수정 닉네임 `taken` | 409 `NICKNAME_DUPLICATED` |
| 코스 `crs_full` / `crs_nocalib` | 409 `COURSE_FULL` / 409 `CALIBRATION_REQUIRED` |
| 기록 `run_other` / 없는 ID | 403 `FORBIDDEN` / 404 `RUN_NOT_FOUND` |
| 계기판 사진 파일 이름에 `blur` | 200 + `RETAKE_REQUIRED` |
| 친구 코드 `SCR-7K2Q`(내 코드) / `SCR-NONE` / `SCR-FRND` / `SCR-FULL` | 내 코드 / 없음 / 이미 친구 / 50명 초과 |
| 같이 달리기 `usr_f2` | 409 `FRIEND_NOT_RUNNING` |

## 구조
```
app/
  main.py            앱 만들기, 오류 처리 연결, 라우터 묶기
  core/              공통: envelope(응답 껍데기·오류 처리·requestId), errors(오류 코드 표), config(환경 변수 + 검사),
                     envfile(.env 읽기·DB 주소만, 마이그레이션용),
                     tokens(access·가입용·refresh 토큰), sessions(refresh 토큰 수명: 재발급·재사용·기기·상한),
                     kakao(카카오 확인, 가짜/진짜), security(진짜 로그인 확인), auth(스텁 API용 로그인 확인),
                     nickname(정규화·형식·금칙어·중복), model(camelCase)
  db/                models(테이블 20개), session(DB 연결), ids(ULID), testing(테스트 DB로 바꿔 끼우기)
  schemas/           요청·응답 모델(도메인별). Swagger의 Schemas
  routers/           API 주소(도메인별). auth.py와 users.py의 닉네임 확인이 진짜, 나머지는 스텁
  testenv.py         테스트·예시 내보내기 전용 설정(임시 서명 키 + 가짜 카카오, app보다 먼저 부름)
  stub_data.py       스텁이 돌려주는 예시 값(실측 아님)
  stub_requests.py   모든 API 예시 요청 표 + 진짜가 된 API 목록(REAL)
migrations/          DB 변경 기록(Alembic). 테이블을 바꾸면 여기 새 파일
scripts/             export_openapi(OpenAPI·예시), notion_pages(노션 본문), real_examples(진짜 API 예시 흐름),
                     purge_refresh_tokens(만료 토큰 정리, 운영에서 하루 한 번)
tests/               test_contract(스텁 API 계약), test_auth(로그인·닉네임 진짜 흐름, 동시 요청 포함),
                     test_config(설정 검사), test_kakao(진짜 카카오 응답 처리), test_nickname(닉네임 규칙)
```

## 테스트·내보내기 (DB가 떠 있어야 함)
```bash
../.venv/Scripts/python -m pytest -q                 # 테스트 DB(scrun_test)를 비우고 씀. 개발 DB는 안 건드림
../.venv/Scripts/python -m scripts.export_openapi    # API를 바꾸면 다시 실행해서 같이 커밋
```
- 새 API는 `stub_requests.py`에 한 줄(빠지면 테스트 실패). 스텁을 진짜로 바꾸면 `REAL`에 넣고 `scripts/real_examples.py`에 예시 흐름, 테스트는 `tests/`에 진짜 흐름
- 테이블을 바꾸면 `app/db/models.py` 고치고 `../.venv/Scripts/python -m alembic revision --autogenerate -m "무엇"` → 생긴 파일 확인 → `alembic upgrade head`. `docs/api/data_model.md`도 같이
- 오류 코드를 더하면 `core/errors.py`와 `docs/api/README.md` 표를 같이(다르면 테스트 실패)
- `docs/api/examples/`는 앱 개발 때 참고할 예시 응답. 시각·requestId·토큰은 고정 글자

## 노션 API 명세 페이지 본문
노션 표 맨 위 "공통 규칙 (먼저 읽기)" 줄(`docs/api/README.md`에서)과, 각 줄을 열면 보이는 상세(설명 → Request Header → Path·Query Parameter → Request Body → 성공·실패 응답, DU 모양)는 **손으로 쓰지 않고** 서버 코드에서 만든다.
```bash
../.venv/Scripts/python -m scripts.notion_pages      # build/notion/*.md + index.json (git 제외)
```
그다음 Claude에게 "노션 API 페이지 다시 올려"라고 하면 `index.json`의 pageId마다 본문을 덮어쓴다(노션 MCP). 새 API를 노션 표에 추가하면 `scripts/notion_pages.py`의 `NOTION_PAGE_IDS`에도 한 줄.

## 팀 공용 개발 서버 (11/09, 계획 0019)
- 주소 **`https://161-118-211-155.sslip.io`** (`/docs`로 눌러 보기). 오라클 싱가포르 ARM, Docker Compose(API + PostgreSQL + Caddy), HTTPS 인증서는 Caddy가 자동
- **진짜 카카오만**(`KAKAO_MODE=real`) → 아이폰 앱이 붙는 곳. 에디터의 가짜 로그인(`k_123`)은 이 서버에서 `INVALID_KAKAO_TOKEN`으로 거절되니 에디터는 지금처럼 로컬 서버(localhost)를 쓴다
- 접속은 민재만(SSH). 다시 올리기: 서버에서 `~/scrun/backend/deploy/update.sh [브랜치, 기본 develop]`(git pull → 다시 빌드·재시작, 뜰 때 마이그레이션 자동)
- 비밀값은 서버의 `backend/deploy/.env`에만(칸은 `deploy/.env.server.example`). 매일 한국 시간 4시 30분 `deploy/daily.sh`: DB 백업 7일치(`~/scrun-backups`) + 만료 토큰 정리. 서버 밖 백업·운영 서버 분리·진짜 도메인은 BL-51
- 지금은 개발용이라 데이터가 지워질 수 있음(테이블을 바꾸는 PR이 합쳐지면 마이그레이션이 그대로 돎)

## 운영 서버에서 꼭 바꿀 것
`.env`의 `JWT_SECRET`(새로 만든 무작위 값, 개발용과 다르게), `DATABASE_URL`(운영 DB), `KAKAO_MODE=real` + `KAKAO_APP_ID`(`fake`는 절대 안 됨). 하루 한 번 `python -m scripts.purge_refresh_tokens`. 배포는 BL-51
