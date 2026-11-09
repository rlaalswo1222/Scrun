# 계획 0019: 팀 공용 개발 서버를 오라클에 미리 올림

- 상태: **완료** (2026-11-09 민재 승인 → 같은 날 구현. 결정: sslip.io 주소, 접속은 민재만, `/docs` 공개)
  - 결과: Bandly 백업(`Desktop/Bandly_backup_20261109/`) → cron 주석·컨테이너 멈춤(볼륨 유지) → `~/scrun`(저장소 backend만 sparse checkout)에서 compose 실행. 확인: 약관 API·`/docs` 200, `k_123`은 401 `INVALID_KAKAO_TOKEN`(진짜 모드), http는 https로 308, DB 5432는 밖에서 닫힘, 재시작 뒤 데이터 유지, `daily.sh` 한 번 실행해 덤프 생성. 이 노트북에서 약관 API 응답 0.29–0.70초(5회)
  - 뜨자마자 모르는 IP가 `/`를 두드림(인터넷 스캐너, 404). 로그인 시도 제한(429)은 아직 없음 → BL-51 전에 넣을 것
- 담당: 민재(서버 공통). 근거: ADR-0008(오라클 무료 + Docker Compose, 제안), 계획 0016(서버 뼈대), 계획 0018 결정 5
- 기간: S1 안, 0018의 1번(C# 네이티브 모양 + `FakeNative` PR) 다음. 반나절 안(가설)

## 왜
- 아이폰이 노트북 http 서버에 붙으려면 보안 예외(http 허용, 로컬 네트워크 팝업)가 필요하고, 같은 와이파이·노트북 IP에 묶인다. 처음부터 HTTPS 서버에 붙으면 출시 앱과 같은 조건이라 예외가 필요 없다
- 서버 `KAKAO_MODE`가 하나라 에디터(가짜 카카오)와 아이폰(진짜 카카오)을 동시에 못 씀 → **에디터는 노트북 localhost(가짜), 아이폰은 오라클(진짜)**로 나누면 풀린다
- 11/13 시연에서 기기 2대가 핫스팟·LTE로도 같은 서버에 붙고, 팀원 3명이 같은 서버로 확인한다

## 지금 서버 (11/09 읽기만 해서 확인)
- 오라클 싱가포르, ARM(aarch64) 2코어, 메모리 11GiB, 디스크 96GB(81GB 남음), Ubuntu 24.04, Docker Compose 2.40, 80일째 켜져 있음
- Bandly(EnsembleSync) 컨테이너 7개가 돌고 있음(api·작업용 3개·PostgreSQL·Redis·Caddy), 1분마다 도는 cron(`router.sh`) 하나. 방화벽은 22·80·443만 열림
- HTTPS 주소 `https://161-118-211-155.sslip.io`(Caddy가 인증서 자동 발급). 이 노트북에서 TCP 연결 시간 0.10–0.31초(5회)

## 단계
1. **Bandly 멈춤(되돌릴 수 있게)**: Bandly DB를 덤프해 이 노트북의 저장소 밖 폴더에 받기 → cron 줄 주석 처리 → `docker compose -f docker-compose.oracle.yml down`(**볼륨은 안 지움**). 되돌리기는 cron 복구 + `up -d`
2. **저장소에 배포 파일**(`backend/deploy/`): API `Dockerfile`(서버에서 ARM으로 빌드), `docker-compose.server.yml`(api + PostgreSQL + Caddy, DB 포트는 밖에 안 엶), `Caddyfile`, `.env.server.example`(값 없이 칸만). API가 뜰 때 `alembic upgrade head`
3. **서버에 올리기**: 공개 저장소를 서버에 clone(`develop`) → `.env`는 서버에서만 만듦(JWT 서명 키·DB 비밀번호 새 무작위 값, `KAKAO_MODE=real`, `KAKAO_APP_ID`) → `docker compose up -d --build`
4. **매일 할 일(cron)**: DB 덤프 7일치 서버 안 보관, `purge_refresh_tokens`. 서버 밖 백업은 BL-51
5. **고치기**: `deploy/update.sh`(git pull → 다시 빌드·재시작) 한 줄로. PR마다 자동 배포는 나중
6. **문서**: `backend/README.md`에 "공용 개발 서버" 절, `docs/api/README.md` 주소, ADR-0008을 "승인"으로(오라클로 확정), 0018 결정 5 갱신

## 결정 필요 (추천 먼저)
1. **주소**: 지금은 `sslip.io`(무료, Bandly에서 검증) / 도메인 구입(연 1만–2만원대 예상, 재료비). 스토어 출시 전엔 진짜 도메인 필요(BL-51)
2. **서버 접속 권한**: 지금은 민재만(SSH 키 하나), 팀원은 배포를 민재에게 요청 / 팀원 키도 등록
3. **`/docs`(API 설명 화면) 공개**: 개발 서버라 공개(저장소가 이미 공개라 숨길 게 없음) / 막기

## 바뀌는 것 (계획 0018 결정 5)
- 아이폰은 오라클 HTTPS만 씀 → Unity "Allow downloads over HTTP"는 **기본값(막음) 그대로**. 에디터의 localhost는 Unity가 항상 허용

## 하지 않는 것
운영 서버 분리·진짜 도메인·서버 밖 백업(BL-51), PR 자동 배포, Redis·모니터링, 실시간 서버(BL-25)

## 완료 확인
- `https://161-118-211-155.sslip.io/v1/agreements`가 약관 목록을 줌, `/docs` 열림
- `k_123`(가짜 토큰)으로 로그인하면 `INVALID_KAKAO_TOKEN`으로 거절됨(진짜 모드라는 증거)
- 서버를 재시작해도 DB가 남음, 다음 날 덤프 파일이 생김
- 아이폰 카카오 로그인 → 가입은 계획 0018의 6번이 끝나면 확인

## 위험
- 싱가포르라 응답 0.1–0.3초 → 일반 API는 괜찮고, 실시간 위치는 S2 방식 결정 때 실측으로 판단
- 무료 계정의 유휴 회수: 80일째 부하 0인데 살아 있어서 이미 유료 전환(PAYG) 계정일 가능성(가설) → 오라클 콘솔에서 확인
- Bandly 앱이 같은 주소를 가리킴 → 아무도 안 써서 영향 없음(민재 확인)
