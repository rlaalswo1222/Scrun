# 계획 0028: 내 정보·설정 API를 DB와 연결 — BL-36 (S2 지환)

- 상태: **구현 완료 · PR 준비** (2026-11-12 지환 승인·구현, 가입 시간대 검사·tzdata는 민재에게 알림)
- 담당: 지환. 리뷰는 민재
- 기준: 노션 작업 표 "[BE] 내 정보 조회·수정 API", "[BE] 설정 API"(BL-36), 계획 0014(API 명세)·0015(데이터 모델), `docs/api/README.md`(공통 규칙·오류 코드), 쓰는 화면은 S7 마이·설정(H-01·H-10, 지환)

## 지금 상태
- `GET·PATCH /v1/users/me`, `GET·PATCH /v1/users/me/settings`는 **예시 응답만** 준다(`stub_user`, `stub_data.ME`·`SETTINGS`. PATCH는 저장하지 않고 보낸 값을 섞어 돌려줌, 닉네임 `taken`이면 409 흉내)
- DB `users` 표에 필요한 칸은 이미 있다: `nickname`·`height_cm`·`weight_kg`·`timezone`·`friend_code`·`feed_share_default`·`created_at`·`updated_at`(닉네임은 `lower(nickname)` 유일 인덱스). **마이그레이션 없음**
- 닉네임 판정은 `core/nickname.py` 하나(정규화 → 형식 → 금칙어 → 중복), 가입·닉네임 확인 API가 이미 씀

## 하는 것
1. **`GET /v1/users/me`**: 로그인한 사람(`current_user`)의 줄을 그대로(`userId`·`nickname`·`heightCm`·`weightKg`·`friendCode`·`timezone`·`createdAt`(UTC Z)). 응답 모양은 지금 명세 그대로
2. **`PATCH /v1/users/me`**: 보낸 칸만 바꿈(빈 본문이면 그대로 돌려줌)
   - 닉네임: `nick.check(db, 값, exclude_user_id=나)` → 형식 400 `VALIDATION_ERROR`(fields), 금칙어 400 `NICKNAME_NOT_ALLOWED`, 중복 409 `NICKNAME_DUPLICATED`. 저장은 정규화된 값. 지금 내 닉네임과 같으면(대소문자만 바꿔도) 통과
   - 동시에 두 사람이 같은 닉네임으로 바꾸면 DB 유일 인덱스가 막음 → `IntegrityError`를 409 `NICKNAME_DUPLICATED`로(가입과 같은 방식)
   - 키 100에서 230, 몸무게 20에서 250(가입과 같은 범위, 지금 스키마 그대로)
   - 시간대: IANA 이름만(`Asia/Seoul` 등, `zoneinfo`로 확인), 아니면 400. 주간 목표 주기(S6)를 이 시간대로 계산하므로 잘못된 값을 막는다. **가입(`POST /auth/signup`)의 `timezone`도 같은 검사**를 붙인다(지금은 아무 글자나 저장됨). 서버 이미지에 시간대 자료가 없을 수 있어 `tzdata` 패키지를 넣는다
3. **`GET /v1/users/me/settings`**: `{ feedShareDefault }`(DB 기본값 켜짐)
4. **`PATCH /v1/users/me/settings`**: 보낸 칸만. `feedShareDefault`가 불리언이 아니면 400
5. **진짜 API 목록**: `stub_requests.REAL`에 네 개 추가 → 스텁 계약 테스트에서 빠짐. 계약 테스트 중 `/v1/users/me`를 스텁으로 쓰던 두 개(만료·가입용 토큰 401/403, 400 fields 모양)는 아직 스텁인 다른 API(`/v1/users/me/weekly-goal`)로 옮긴다. 진짜 API에서도 같은 오류가 나는지는 새 테스트에서 따로 확인
6. **테스트(pytest, 테스트 DB)** `tests/test_me.py`: 가입한 값 그대로 조회, 칸별 수정과 안 보낸 칸 유지, 닉네임 정규화·형식·금칙어·중복·내 이름 그대로·대소문자만 바꾸기, 동시에 같은 닉네임으로 바꾸기(하나만 성공), 키·몸무게 범위, 시간대 형식(가입·수정), 설정 조회·수정·기본값, 로그인 없이·만료 토큰·가입용 토큰, 남의 정보가 안 바뀜
7. **문서**: `openapi.json`·`examples` 다시 만들기(`scripts/export_openapi.py`, 스텁 설명 "닉네임 taken" 빼기), `docs/api/README.md`에 진짜가 된 API 표시, 노션 API 명세 상태는 민재 워크스페이스라 민재에게 부탁

## 하지 않는 것
계정 삭제 `DELETE /users/me`(BL-48, 카카오 연결 해제·전부 삭제라 따로), 앱 마이·설정 화면(S7), 알림 설정(폰 로컬), 프로필 사진(명세에 없음)

## 정할 것
- 없음(응답 모양·오류 코드는 계획 0014 명세 그대로). 시간대 검사를 가입에도 붙이는 것만 명세보다 엄격해짐. 앱은 가입 때 늘 `"Asia/Seoul"`을 보내므로(`SignupRequest.Timezone` 기본값, 기기 시간대를 읽지 않음) 막히는 앱은 없다. 나중에 기기 시간대를 보내게 되면 윈도우 에디터의 "Korea Standard Time" 같은 이름은 IANA로 바꿔 보내야 함

## 진행 상황 (11/12)
- 위 1–7 구현. 코드: `routers/users.py`(내 정보·설정 4개를 `current_user`·DB로), `core/timezones.py`(새로), `core/nickname.require_usable`(가입과 내 정보 수정이 같은 최종 판정), `schemas/auth.py`·`user.py`(시간대 검사, 설정 `StrictBool`), `stub_requests.REAL` 4개 추가, `stub_data.SETTINGS` 삭제, `requirements.txt`에 `tzdata==2026.6`
- 테스트 `tests/test_me.py` 22개(만료 토큰 포함), 계약 테스트 2개는 `/users/me/weekly-goal`로 옮김. pytest 193/193
- 문서: `openapi.json`·예시 다시 만듦(진짜 흐름, 사용자 ID·친구 코드는 고정 값), `backend/README.md`(진짜 15개·스텁 27개, 스텁 규칙에서 닉네임 `taken` 줄 삭제), `docs/api/README.md`
- **계획과 달라진 것**: 금칙어는 명세대로 **400** `NICKNAME_NOT_ALLOWED`(계획 초안에 409로 잘못 적음). 시간대는 `ZoneInfo`로 열어 보지 않고 IANA 목록(`available_timezones`, 자리표시 `Factory` 뺌)과 정확히 대조: 맥에서는 `asia/seoul`도 열리고 `Asia` 같은 폴더 이름은 500이 나서
- `/code-review`(11/12) 반영: 시간대 판정이 환경마다 다르고 폴더 이름이 500이던 것, 만료 토큰 테스트 추가, 닉네임 판정 중복(가입·수정)을 한 함수로, 시간대 길이 상한 한 곳(`tz.MAX_LEN`), 동시 저장 충돌은 닉네임 중복을 다시 확인한 뒤 409(아니면 그대로 오류), `null`은 안 보낸 것과 같다고 설명에 적음
- 리뷰에서 안 고친 것(판단): `heightCm`에 `"180"` 같은 문자열도 받는 것(가입과 같은 기존 규칙, 앱은 숫자를 보냄)
- 남은 것: 노션 API 명세 상태(민재 워크스페이스) → 민재에게 부탁, 공용 개발 서버 배포 때 `pip install -r requirements.txt`(tzdata)

