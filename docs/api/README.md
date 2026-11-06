# SCRUN API 공통 규칙

서버(FastAPI)와 Unity 앱이 함께 따르는 규칙. 계획 [0014](../plans/0014-api-spec.md).

- **API 목록·진행판**: 노션 SCRUN > 문서 > [API 명세](https://app.notion.com/p/52fe88346253480692c492c862982087) (한 줄 = API 하나, 화면 ID·백로그 연결)
- **요청·응답 세부 필드**: 서버 코드 [`backend/`](../../backend/README.md)(지금은 스텁)가 만드는 `/docs`(Swagger)와 [`openapi.json`](openapi.json). 예시 응답은 [`examples/`](examples/)(`python -m scripts.export_openapi`로 다시 만듦). 노션이나 이 문서에 필드를 손으로 적지 않는다(코드와 어긋남)
- **DB 테이블(서버가 저장하는 구조)**: [data_model.md](data_model.md)
- **이 문서**: 모든 API에 공통인 것만

## 주소
- `https://{apiHost}/v1/...`. 경로는 소문자·하이픈(`weekly-goal`), 모음은 복수형(`/runs`, `/friends`), 내 것은 `/users/me/...`
- JSON 필드 이름은 camelCase(`heightCm`, `distanceM`)

## 로그인
1. 앱이 카카오 iOS SDK로 로그인해 카카오 토큰을 받는다
2. `POST /v1/auth/kakao`로 보내면 서버가 카카오에서 확인한다
   - 이미 가입한 사람: `accessToken`(짧게) + `refreshToken`
   - 처음: `isNewUser: true` + 가입용 토큰 → `POST /v1/auth/signup`(약관 동의·닉네임·키·몸무게·시간대) → 우리 토큰
3. 이후 요청 헤더: `Authorization: Bearer {accessToken}`
- 로그인 없이 되는 API: `POST /v1/auth/kakao`, `GET /v1/agreements`, `POST /v1/auth/refresh`. 가입용 토큰으로 되는 API: `POST /v1/auth/signup`, `GET /v1/users/nickname/check`. 나머지는 전부 `accessToken` 필요(실시간 메시지 포함)
4. 401 `TOKEN_EXPIRED`를 받으면 앱이 `POST /v1/auth/refresh`로 새 토큰을 받고 원래 요청을 한 번 다시 보낸다
- 토큰은 아이폰 키체인에만 저장, 로그에 찍지 않는다
- 로그인 제공자별 연결(제공자, 제공자 쪽 ID)을 계정과 따로 저장한다. Apple 로그인을 나중에 붙여도 계정 구조가 안 바뀜(PRD Q-39)

## 응답 껍데기
성공:
```json
{
  "resultType": "SUCCESS",
  "success": { "data": { } },
  "error": null,
  "meta": { "timestamp": "2026-11-06T06:16:17Z", "path": "/v1/users/me", "requestId": "req_8f3a2c" }
}
```
실패:
```json
{
  "resultType": "FAIL",
  "success": null,
  "error": { "code": "NICKNAME_DUPLICATED", "message": "이미 쓰는 닉네임이에요.", "details": null },
  "meta": { "timestamp": "2026-11-06T06:16:17Z", "path": "/v1/users/nickname/check", "requestId": "req_91bd04" }
}
```
- **HTTP 상태 코드도 맞게 쓴다**(항상 200 금지). 바깥 숫자 = 큰 분류(앱이 재로그인·재시도 결정), 안쪽 `error.code` = 세부 이유(앱이 화면 문구 결정)
- `meta`엔 화면에 쓰지 않는 것만: `timestamp`(UTC, `Z`), `path`, `requestId`(서버 기록에서 그 요청을 찾는 번호. 앱 오류 화면·오류 수집에 남김)
- 목록 넘기기 정보(`nextCursor`)는 화면이 쓰므로 `data` 안에
- FastAPI 기본 검증 오류(422)도 이 껍데기로 바꾼다: 400 `VALIDATION_ERROR`, 틀린 칸은 `details`
- 업무 결과는 오류가 아니다: 계기판 OCR이 못 읽음(재촬영 필요), 오차 초과(미인증)는 200 + 결과 값으로 돌려준다

## 상태 코드
| 코드 | 언제 |
|---|---|
| 200 | 성공 |
| 201 | 새로 만들어짐(러닝 시작, 친구 추가) |
| 204 | 성공, 돌려줄 내용 없음(로그아웃) |
| 400 | 보낸 값이 잘못됨 |
| 401 | 로그인 안 됨·토큰 만료 |
| 403 | 로그인은 했지만 권한 없음(남의 기록) |
| 404 | 없음 |
| 409 | 지금 상태와 충돌(중복, 가득 참, 이미 끝남) |
| 429 | 너무 자주 요청 |
| 500 | 서버 버그 |
| 503 | 점검·과부하 |

## 오류 코드
화면 문구는 상준이 채운다(Figma 예외 화면과 맞춤). 새 코드는 여기 먼저 추가.

| 코드 | 상태 | 뜻 | 화면 | 문구 |
|---|---|---|---|---|
| `VALIDATION_ERROR` | 400 | 값 형식·범위가 틀림(`details`에 칸) | 입력 화면 | |
| `UNAUTHORIZED` | 401 | 토큰 없음·잘못됨 | 로그인으로 | |
| `TOKEN_EXPIRED` | 401 | access 토큰 만료 → refresh | (앱이 자동 처리) | |
| `INVALID_KAKAO_TOKEN` | 401 | 카카오 토큰 확인 실패 | A-05 | |
| `SIGNUP_REQUIRED` | 403 | 가입 전 계정으로 일반 API 호출 | A-06 | |
| `FORBIDDEN` | 403 | 남의 것에 접근 | I-08 | |
| `NOT_FOUND` | 404 | 없는 주소(앱 버전이 서버와 안 맞을 때 등) | I-08 | |
| `METHOD_NOT_ALLOWED` | 405 | 그 주소에서 안 되는 동작 | I-08 | |
| `USER_NOT_FOUND` | 404 | 사용자 없음 | I-08 | |
| `RUN_NOT_FOUND` | 404 | 기록 없음 | I-08 | |
| `COURSE_NOT_FOUND` | 404 | 코스 없음 | I-08 | |
| `FRIEND_CODE_NOT_FOUND` | 404 | 친구 코드 없음 | G-08 | |
| `NICKNAME_DUPLICATED` | 409 | 닉네임 중복 | A-06, H-01 | |
| `NICKNAME_NOT_ALLOWED` | 400 | 금칙어·길이 | A-06, H-01 | |
| `CALIBRATION_REQUIRED` | 409 | 보정 없이 맵 입장 | A-11 | |
| `COURSE_FULL` | 409 | 맵 인스턴스가 모두 가득 참(20명) | I-11 | |
| `RUN_ALREADY_FINISHED` | 409 | 이미 끝난 러닝에 종료·시계열 올리기 | | |
| `VERIFICATION_CLOSED` | 409 | 인증 가능 시간이 지남(운동 직후만) | E-10 | |
| `SELF_FRIEND_CODE` | 400 | 내 코드를 입력 | G-08 | |
| `ALREADY_FRIEND` | 409 | 이미 친구 | G-08 | |
| `FRIEND_LIMIT_EXCEEDED` | 409 | 친구 50명 초과 | G-08 | |
| `FRIEND_NOT_RUNNING` | 409 | 같이 달리기 전에 친구가 끝냄 → 일반 입장 | D-21 | |
| `RATE_LIMITED` | 429 | 너무 자주(친구 코드 반복 실패 등) | I-08 | |
| `INTERNAL_ERROR` | 500 | 서버 오류 | I-08 | |
| `SERVICE_UNAVAILABLE` | 503 | 점검·과부하 | I-08 | |

(I-07 네트워크 없음은 서버 응답이 없는 경우라 앱이 처리)

## 단위·형식
- 거리 m(정수), 시간 초, 속도 km/h(소수), 페이스는 앱이 계산
- 시각은 UTC ISO 8601(`Z`). 사용자 시간대(`Asia/Seoul` 등)는 프로필에 두고, 주간 목표 "설정일부터 7일"은 서버가 그 시간대로 센다
- ID는 문자열(`run_...`, `usr_...`)
- 목록은 커서 방식: 요청 `cursor`, 응답 `data.nextCursor`(없으면 null)

## 판정은 서버
인증 결과, 공식 기록 값(인증 완료 = 계기판, 미인증 = 센서), 레벨·경험치(인증 완료 누적 거리, 임계값 서버 설정), 인증 마크·임팩트 해금, 주간 목표 집계(인증 완료 + 5분 이상). 앱은 서버가 준 값을 보여주기만 한다. 보정 계산은 폰이 하고 서버는 보관만(OCR 값은 보정에 안 씀, PRD 독립성 원칙).

## 같은 요청 두 번
러닝 시작·시계열·종료는 앱이 만든 ID(`clientRunId`)를 같이 보낸다. 같은 ID가 다시 오면 새로 만들지 않고 처음 결과를 돌려준다. 네트워크가 끊겨 다시 보내거나, 오프라인으로 달린 기록을 나중에 올려도(정책 미정) API는 그대로.

## 미정 (정해지면 이 문서와 노션 표 갱신)
- OCR을 서버에서 할지 앱에서 할지 → `verification/photo`와 `verification/reading` 둘 다 자리만
- 오프라인 달리기 허용 여부
- 실시간 공동 맵 전송 방식(FastAPI WebSocket vs Photon, BL-25). 노션 표의 Realtime 줄은 메시지 이름·필드만
- 실사 영상 코스는 최하위 → 코스 `type` 필드만 열어 둠
