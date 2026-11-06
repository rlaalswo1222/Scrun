# SCRUN 서버 (`backend/`)

FastAPI. **지금은 스텁**(계획 [0014](../docs/plans/0014-api-spec.md) 2단계): 42개 API가 모두 고정 예시를 돌려준다. 진짜 DB·카카오 연결은 계획 [0015](../docs/plans/0015-account-deletion.md)부터 이 폴더에 채운다. 옛 `server.py`(코스 만들기 실험 서버)와는 별개.

공통 규칙(응답 껍데기·상태 코드·오류 코드·단위)은 [docs/api/README.md](../docs/api/README.md), API 목록·진행은 노션 SCRUN > 문서 > API 명세.

## 실행
```bash
cd backend
../.venv/Scripts/python -m pip install -r requirements-dev.txt   # 처음 한 번(맥은 ../.venv/bin/python)
../.venv/Scripts/python -m uvicorn app.main:app --reload --port 8000
```
브라우저에서 http://localhost:8000/docs → 오른쪽 위 **Authorize**에 아무 토큰(예: `demo`) → 각 API의 "Try it out".

## 스텁 규칙 (오류 화면 확인용)
| 넣는 값 | 결과 |
|---|---|
| 토큰 없음 | 401 `UNAUTHORIZED` |
| 토큰 `expired` | 401 `TOKEN_EXPIRED` |
| 토큰 `signup` | 가입용 토큰(가입·닉네임 확인만, 나머지 403 `SIGNUP_REQUIRED`) |
| 그 밖 아무 토큰 | 예시 사용자 `usr_demo` |
| 카카오 토큰 `new` / `invalid` | 새 사용자 / 401 `INVALID_KAKAO_TOKEN` |
| 닉네임 `taken` / `admin` | 중복 / 금칙어 |
| 코스 `crs_full` / `crs_nocalib` | 409 `COURSE_FULL` / 409 `CALIBRATION_REQUIRED` |
| 기록 `run_other` / 없는 ID | 403 `FORBIDDEN` / 404 `RUN_NOT_FOUND` |
| 계기판 사진 파일 이름에 `blur` | 200 + `RETAKE_REQUIRED` |
| 친구 코드 `SCR-7K2Q`(내 코드) / `SCR-NONE` / `SCR-FRND` / `SCR-FULL` | 내 코드 / 없음 / 이미 친구 / 50명 초과 |
| 같이 달리기 `usr_f2` | 409 `FRIEND_NOT_RUNNING` |

## 구조
```
app/
  main.py            앱 만들기, 오류 처리 연결, 라우터 묶기
  core/              공통: envelope(응답 껍데기·오류 처리·requestId), errors(오류 코드 표), auth(로그인 확인), model(camelCase)
  schemas/           요청·응답 모델(도메인별). 이게 Swagger의 Schemas가 됨
  routers/           API 주소(도메인별: auth, users, courses, runs, verification, progress, friends, events)
  stub_data.py       스텁이 돌려주는 예시 값(실측 아님)
  stub_requests.py   모든 API를 한 번씩 부르는 예시 요청 표(테스트·내보내기 공용)
scripts/export_openapi.py   docs/api/openapi.json + docs/api/examples/ 만들기
tests/test_contract.py      모든 API가 공통 규칙대로 답하는지
```

## 테스트·내보내기
```bash
../.venv/Scripts/python -m pytest -q                 # 101개
../.venv/Scripts/python -m scripts.export_openapi    # API를 바꾸면 다시 실행해서 같이 커밋
```
- 새 API를 만들면 `stub_requests.py`에 한 줄 추가(빠지면 테스트가 실패)
- 오류 코드를 더하면 `core/errors.py`와 `docs/api/README.md` 표를 같이(다르면 테스트가 실패)
- `docs/api/examples/`는 앱 개발 때 참고할 예시 응답. 시각·requestId는 고정값

## 노션 API 명세 페이지 본문
노션 표 맨 위 "공통 규칙 (먼저 읽기)" 줄(`docs/api/README.md`에서)과, 각 줄을 열면 보이는 상세(설명 → Request Header → Path·Query Parameter → Request Body → 성공·실패 응답, DU 모양)는 **손으로 쓰지 않고** 서버 코드에서 만든다.
```bash
../.venv/Scripts/python -m scripts.notion_pages      # build/notion/*.md + index.json (git 제외)
```
그다음 Claude에게 "노션 API 페이지 다시 올려"라고 하면 `index.json`의 pageId마다 본문을 덮어쓴다(노션 MCP). 새 API를 노션 표에 추가하면 `scripts/notion_pages.py`의 `NOTION_PAGE_IDS`에도 한 줄.
