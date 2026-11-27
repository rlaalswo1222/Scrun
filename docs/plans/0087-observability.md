# 계획 0087: 로깅·모니터링 — 서버와 앱이 지금 어떤지 보고, 이상하면 알기

- 상태: **도구 고름(11/27 민재) → ADR-0015 승인 → 1단계 완료(11/28, #160 머지·운영 배포·확인) → 2단계 완료(11/28, #162 머지·운영 배포·확인) → 3단계 구현·리뷰 반영(11/28, `feat/observability-3`, 서버 journald 읽기는 배포 뒤 확인). 남은 것: 집 PC에서 2단계 도입 전·후 k6 비교, 4단계(앱 크래시)**. 설명 그림 [0087-observability.html](0087-observability.html)
- 고른 것(ADR-0015): 오류 = **Sentry 무료**, 지표·로그·대시보드 = **오라클에 직접**(Prometheus·Loki·Grafana·Alloy), 바깥 감시 = **UptimeRobot 무료**, 알림 = **이메일**, Grafana = **SSH 터널로만**. 범위는 1–4단계 전부(순서대로, 단계마다 PR)
- 담당: 민재(서버 공통·배포). 앱 크래시 수집은 맥 빌드가 필요(4단계)
- 이어서: 계획 0070(관측 최소: 요청 로그 한 줄·k6, "Prometheus·Grafana는 띄우지 않음"), 0071·0073(성능), ADR-0008(서버 오라클, "백로그 후보: 앱 오류 수집"), 4차 멘토링(11/26 "로깅·모니터링이 되고 있나", "비포·애프터 기록", "모니터링으로 추세를 보다가 준비")

## 왜
- 지금은 **일이 터져도 우리가 모름**. 서버가 죽거나 500이 나도, 앱이 아이폰에서 꺼져도 알림이 없고, 사용자가 말해 주거나 우리가 `journalctl`을 열어 볼 때만 앎
- 성능 숫자는 k6 부하 테스트를 할 때만 손으로 잼 → 평소 추세(요청 수·응답 시간·오류율·CPU)를 모름. 멘토님 말대로 비포·애프터가 그래프로 자동으로 남아야 함
- 사용성 테스트(12월)·출시(종강 뒤) 전에 크래시·오류를 모아야 고칠 수 있음

## 지금 있는 것 (11/27 확인)
- **요청 로그**(`backend/app/core/request_log.py`, 0070): 요청마다 JSON 한 줄 `rid·method·path(라우트 틀)·status·ms·db_n·db_ms·pool_ms·queue_ms`. WebSocket·헬스체크·내부 API는 안 남김
- **OCR 로그**: 사진마다 시간·결과(`[계기판 OCR] 사진 1/3 … → READ`), 묶음 시간
- 로그 저장: 컨테이너 로그를 **journald**로(`logging: driver: journald`), `journalctl CONTAINER_NAME=scrun-api-1`. 디스크 1.6 GB 쓰는 중, 상한 설정 없음(가설: 기본값은 디스크의 10%)
- 요청 ID: 응답 머리 `X-Request-Id`·껍데기 `meta.requestId`
- 매일: DB 백업 7개·만료 토큰 정리(`daily.sh`, cron). 성공·실패 알림 없음
- 앱: 개발 빌드만 `RunEventLog`·`VerifyPhotoLog`(폰 안 파일). 출시 빌드는 오류를 어디에도 안 보냄
- 지표 이벤트 `POST /v1/events`: 자리만(204, 저장 안 함)
- **없는 것**: 오류 수집, 지표(시간별 숫자) 저장, 대시보드, 알람, 서버 밖에서 살아 있나 보기, 앱 크래시 수집
- 오라클 서버 여유(11/27): ARM 2코어, 메모리 11 GB 중 약 1.5 GB 사용, 디스크 96 GB 중 20 GB 사용

## 층별 후보 (자세한 설명·그림은 HTML)
| 층 | 하는 일 | 후보 | 추천(가설) |
|---|---|---|---|
| ① 오류 수집 | 서버 예외·500, 앱 크래시를 모아 묶고 몇 명이 겪었는지 | Sentry(클라우드) / GlitchTip(직접 운영, Sentry SDK 호환) / Firebase Crashlytics(앱만) | **Sentry** — 서버(FastAPI)와 Unity iOS(C#·네이티브 크래시, dSYM) 둘 다 공식 지원, 한곳에서 봄 |
| ② 지표 | 초당 요청 수·응답 시간·오류율·CPU·메모리·DB 연결을 15초마다 숫자로 저장 | Prometheus 직접 운영 / Grafana Cloud 무료 | **오라클에 직접**(데이터가 밖으로 안 나감, 무료, 서버 여유 있음) |
| ③ 로그 모으기 | 컨테이너 로그를 한곳에서 검색(요청 ID·경로·상태로) | Loki + Alloy 직접 운영 / Grafana Cloud Logs / journalctl 그대로 | **Loki 직접**(②와 같은 Grafana에서 봄) |
| ④ 대시보드·알람 | 그래프, 기준 넘으면 알림 | Grafana(②·③과 같이) | Grafana 알람 → api `/internal/alerts` → Sentry → 메일(11/28 정함, 새 메일 계정 없음) |
| ⑤ 밖에서 보기 | 서버가 통째로 죽어도 알 수 있게 서버 밖에서 주기적으로 찔러 봄 | UptimeRobot 무료 / Grafana Cloud 합성 검사 | **UptimeRobot 무료**(5분 간격) |
| ⑥ 제품 지표 | 보정 완주율·인증 성공률 같은 사용 이벤트 | `/v1/events`를 우리 DB에 | **따로 계획**(이벤트 목록은 상준과) |

## 정할 것 (11/27 민재가 고른 것은 맨 위 "고른 것": 1 (가), 2 (가), 3 (나) 이메일, 4 (가), 5 전부)
1. **오류 수집**: (가) Sentry 무료(Developer, 1명·월 약 5천 건, 공식 가격 페이지로 다시 확인) / (나) Sentry Team 유료(월 약 26달러, 여러 명, 재료비) / (다) GlitchTip 오라클 직접 운영(여러 명 무료, Unity iOS 네이티브 크래시 기호 풀이가 되는지 확인 필요 — 가설: 안 될 수 있음)
2. **지표·로그·대시보드 둘 곳**: (가) 오라클에 직접(추천) / (나) Grafana Cloud 무료(지표 1만 시계열·로그 월 50 GB·14일 보관, 로그가 밖으로 나감)
3. **알림 받을 곳**: (가) Discord 채널(웹훅, UptimeRobot 무료도 지원) / (나) 이메일 / (다) 텔레그램. 카카오톡은 단체방 웹훅이 없음(가설)
4. **Grafana 보는 법**: (가) SSH 터널로만(밖에 안 엶, 추천) / (나) Caddy 뒤에 로그인 걸어 주소로
5. **범위·순서**: 아래 1–4단계 전부 / 일부만

## 단계
1. **빨리 효과 큰 것**(이메일은 Sentry·UptimeRobot이 기본으로 보내서 SMTP 필요 없음)
   - 서버 Sentry: `sentry-sdk`(FastAPI 자동 연동은 끔, 아래 진행), `SENTRY_DSN`이 있을 때만 켬(없으면 아무것도 안 함 → 로컬·테스트 그대로). 환경(`production`·`local`)·릴리스(git 커밋)·서비스 이름(api·realtime·ocr)·요청 ID 태그, `data_collection`으로 사용자 정보·쿠키·머리·본문·지역 변수 끔, 성능 추적은 안 씀(무료 한도 아낌). 처리 못 한 예외(500)와 **ERROR 이상 로그**(OCR 프로세스가 죽음·카카오 어드민 키 없음·WebSocket 예외 — 바로 알아야 할 것, 11/27 리뷰 뒤 범위에 넣음)를 보냄, 4xx·의도한 503은 안 보냄. DB 오류 메시지에 바인드 값이 안 들어가게 엔진 `hide_parameters`
   - 서비스별 헬스체크 주소: 지금 공개 `/v1/agreements`는 api만 확인 → realtime·ocr이 죽어도 모름. `GET /healthz`(DB 확인)를 세 서비스에 두고 Caddy가 `/healthz/api`·`/healthz/realtime`·`/healthz/ocr`로 나눔. **번호표·요청 로그는 다른 요청과 같이 지남**(11/27 구현 때 바꿈: 서버가 꽉 차 503이 나면 바깥에서도 "안 됨"으로 보이는 게 실제 상태. 그래서 우리 부하 측정 중엔 UptimeRobot 메일이 올 수 있음 → 측정 전에 UptimeRobot에서 잠시 멈춤. 요청 로그는 5분마다 3줄). 한계: OCR 서비스의 읽기 프로세스가 죽은 건 못 봄(그건 ERROR 로그 → Sentry)
   - UptimeRobot: 위 세 주소를 5분마다, 이메일 알림(민재 계정 만들기)
   - journald 상한: `backend/deploy/journald-scrun.conf`(예: `SystemMaxUse=4G`, 정할 때 디스크 보고 확정)를 서버 `/etc/systemd/journald.conf.d/`에, `backend/README.md`에 한 줄
   - 시험: 일부러 500(개발 전용 경로가 아니라 테스트에서 가짜 DSN 전송 확인 + 운영에선 컨테이너 하나 멈춰 UptimeRobot 메일 확인)
   - 민재가 할 것: Sentry 계정·프로젝트(Python 하나, 4단계에 Unity 하나) 만들고 DSN을 서버 `.env`에, UptimeRobot 계정
   - `daily.sh` 실패 알림은 2단계로(백업 시각을 지표로 내보내 "26시간 넘게 백업 없음" 알람, 새 서비스 없이)
2. **지표·대시보드**: 서버에 `/metrics`(API별 요청 수·응답 시간 분포·오류, 연결 풀·번호표 대기, OCR 시간·READ 비율, 실시간 접속 수) + node·컨테이너·Postgres 수집기 + Prometheus + Grafana. 대시보드 "골든 시그널"(트래픽·지연·오류·포화도). uvicorn 프로세스가 여럿이라 Prometheus 다중 프로세스 방식 필요. 알람은 Sentry를 거쳐 메일(11/28 정함, SMTP 계정 없음), 백업 시각 지표·알람
3. **로그 모으기**: Alloy가 journald를 읽어 Loki로, Grafana에서 요청 ID로 검색. 로그 레벨 정리(INFO·WARNING·ERROR), 개인정보·토큰이 로그에 안 찍히는지 점검
4. **앱 크래시**: Sentry Unity SDK(C#·네이티브), dSYM 자동 올리기(맥 빌드), 앱 오류에 서버 요청 ID 붙이기, 개발·출시 환경 나눔, 개인정보처리방침(BL-40)·iOS 개인정보 표시에 "오류 정보 수집" 추가(상준)

## 지키는 것
- 공개 저장소: Sentry DSN·Grafana 비밀번호·웹훅 주소는 `.env`(git 밖). DSN은 앱에 들어가 완전한 비밀은 아니지만 저장소엔 안 올림
- 개인정보: 로그·오류에 카카오 토큰·JWT·이메일·사진을 넣지 않음(지금 요청 로그는 라우트 틀만). 오류 수집 도구의 "개인 정보 보내기"(send_default_pii) 끔
- 서버 부하: 수집 주기 15초, 새로 띄우는 수집·저장·화면 컨테이너(alloy·prometheus·grafana)마다 메모리 상한. 도입 전·후 k6로 같은 시나리오 비교(0075와 같이) → 비포·애프터
- 모니터링이 서버와 같이 죽는 것: ⑤ 밖에서 보기로 메움

## 판정
- 서버를 일부러 멈추면 5분 안에 이메일 알림, 일부러 500을 내면 Sentry에 요청 ID와 함께 뜸, 대시보드에서 API별 초당 처리량·p95·오류율이 보임, 도입 전후 k6 처리량 차이가 5% 안(가설 기준, 정할 때 확정)

## 진행
- 11/27 1단계 구현(`feat/observability-1`, develop `afefb13`에서)
  - `app/core/error_report.py`: `SENTRY_DSN`이 있을 때만 `sentry_sdk.init`(2.77.0, 3.0은 갓 나온 큰 판이라 안 씀). 자동 연동(FastAPI·Starlette·SQLAlchemy·httpx 감싸기)은 끄고 500 처리기(`envelope.unhandled_error_handler`)에서만 보냄 → 요청마다 드는 비용 없음. 태그 `request_id`(껍데기 `meta.requestId`와 같음, 500 응답엔 `X-Request-Id` 머리가 없음)·`route`(라우트 틀)·`method`·`service`. ERROR 이상 로그도 오류로(OCR 프로세스가 죽음·카카오 어드민 키 없음·WebSocket 예외), INFO 빵부스러기는 안 모음. `data_collection`으로 사용자 정보·쿠키·머리·본문·쿼리·DB 값·지역 변수를 끔(`send_default_pii`는 2.77에서 폐기 예정 경고), `include_local_variables=False`도(2.77이 스택을 만들 때 아직 읽음). 500 처리기가 보낸 예외를 uvicorn이 ERROR 로그로 또 남기면 표시(`_scrun_reported`)로 버림(Sentry 같은 예외 거르기는 같은 컨텍스트일 때만 확실)
  - 설정 `ErrorReportSettings`(`SENTRY_DSN` 모양 검사, 비밀 키 붙은 옛 모양 거부, `SENTRY_ENVIRONMENT`·`SENTRY_RELEASE`·`SCRUN_SERVICE`)
  - `GET /healthz`(DB `SELECT 1`, Swagger 밖) + Caddy `/healthz/api`·`/healthz/realtime`·`/healthz/ocr` → 각 서비스의 `/healthz`
  - compose: 세 서비스에 `SCRUN_SERVICE`, `SENTRY_DSN`(`.env`)·`SENTRY_ENVIRONMENT=production`·`SENTRY_RELEASE`(`update.sh`가 `git rev-parse --short HEAD`)
  - `deploy/journald-scrun.conf`: 보관 상한 4 GB·디스크 10 GB 남기기(서버에 한 번 넣는 명령은 파일 안)
  - 테스트 `tests/test_error_report.py` 13개(가짜 transport로 나가는 내용 확인: 요청 ID·라우트 틀·환경·릴리스, 요청·사용자·지역 변수·토큰·쿠키·기록 ID가 없음, uvicorn 로그로 두 번 안 감, ERROR만·빵부스러기 없음, DSN 없으면 꺼짐, 설정 검사, `/healthz`, compose·Caddy 경로). 전체 500 통과·9 실패(전부 사진 인증 `SERVICE_UNAVAILABLE`, develop에서도 같은 9개 — 작업 폴더에 로컬 `.env`가 없어 가짜 OCR이 안 켜짐, 이 PR과 무관)
- 11/27 `/code-review` 반영: DB 엔진 `hide_parameters=True`(IntegrityError 등 예외 메시지의 바인드 값이 Sentry로 가던 길), 테스트 설정(`testenv`)이 `SENTRY_DSN`을 비움(로컬 `.env`에 DSN이 있어도 pytest가 진짜로 안 보냄), 요청 ID·라우트 틀 읽기를 `request_log.request_id`·`route_path` 한 곳으로, 설정 칸마다 오류 문구, 위 1단계 글을 구현에 맞춤(`/healthz`가 번호표를 지남, ERROR 로그도 보냄). ERROR 로그 중 `realtime/router.py`의 "leave 뒤 자리 지우기 실패 user=…"는 내부 사용자 ID(ULID)가 메시지에 들어감 — 이름·카카오 번호가 아닌 내부 번호라 그대로 둠. OCR이 계속 죽으면 월 5천 건을 빨리 쓸 수 있음 → Sentry에서 같은 오류 묶음의 알림만 받고, 넘으면 다시 봄
- 11/28 계정(민재 승인, Claude가 Chrome으로): **Sentry** — 민재 GitHub 계정으로 가입, 조직 `SCRUN`(`scrun.sentry.io`), 저장 지역 미국(US·EU 둘뿐, 한국 없음), 프로젝트 `python-fastapi`(오류 모니터링만, 로그·추적·프로파일링 끔), 저장소 연결 안 함, 기본 오류 알림 규칙 1개(가입 메일로). 처음 14일 체험 뒤 무료(오류 월 5,000건, 화면에서 확인). DSN은 서버 `deploy/.env`에 넣음(백업 `.env.bak-날짜`, 권한 600). **UptimeRobot** — 같은 GitHub 계정, 첫 감시 `/v1/agreements` 5분·이메일 알림(`/healthz` 셋은 배포 뒤 추가)
- 11/28 #160 머지(develop `09e1573`)·운영 배포. `update.sh`는 실행 전에 파일 전체를 읽어 두어 첫 배포 땐 옛 스크립트가 돌아 릴리스 칸이 빔 → 한 번 더 배포해 세 서비스 모두 `release=09e1573`. 확인: 공개 `/healthz/api`·`/healthz/realtime`·`/healthz/ocr` 모두 200 `{"status":"ok"}`, 세 컨테이너 설정 DSN 있음·`production`·서비스 이름 맞음, journald 설정 넣음(`Storage=persistent`·`SystemMaxUse=4G`·`SystemKeepFree=10G`, 적용 때 1.6 GB, 재시작 뒤에도 컨테이너 로그 계속 쌓임), api 컨테이너 안에서 시험 오류 하나 → Sentry `python-fastapi`에 도착(`RuntimeError 배포 확인용 시험 오류`, 무시해도 됨), UptimeRobot에 `/healthz` 셋 추가(5분·이메일, 감시 4개/50), `/healthz/api` 첫 확인 응답 796 ms(북미에서 잼). 알림 경로는 서버를 멈추지 않고 UptimeRobot "Test Notification"으로 시험 메일 보냄(받았는지 민재 확인)
- 2단계 전에 정할 것: ~~Grafana 이메일 알림용 SMTP 계정~~(11/28 Sentry 거치기로 정해 필요 없음), 부하 측정(0075)은 집 PC에서만(운영엔 부하를 안 걺)
- 11/28 2단계 정함(민재): 알람 메일은 **Sentry 거치기**(Grafana → api `/internal/alerts` → Sentry 오류 → Sentry 메일, 새 메일 계정 없음), Grafana는 **보기는 로그인 없이**(SSH 터널이 잠금, 관리자 비밀번호는 서버에서 무작위로 만들어 `.env`, Claude는 값을 안 봄)
- 11/28 2단계 구현(`feat/observability-2`, develop `e1ab66b`에서)
  - 앱: `app/core/metrics.py`(`prometheus-client==0.26.0`) — `scrun_http_requests_total{method,route,status}`, `scrun_http_request_duration_seconds{method,route}`, 요청당 DB 시간·DB 연결 기다림·번호표 기다림 히스토그램, `scrun_ocr_batches_total{result}`·`scrun_ocr_batch_seconds`, `scrun_realtime_connections`(livesum). 라벨은 라우트 틀·메서드·상태·OCR 결과만(ID 없음). 요청 로그 미들웨어가 이미 재는 값으로 셈(`METRICS=0`이면 끔, 요청 로그와 따로). uvicorn 프로세스 여럿이라 `PROMETHEUS_MULTIPROC_DIR`(compose tmpfs `/run/scrun-metrics`), 프로세스가 끝날 때 `mark_process_dead`. `GET /metrics`는 번호표·요청 로그·지표 밖(`is_internal`), Caddy가 밖에선 404. OCR 결과 이름은 던지는 자리에서 붙임(`_ocr_error`: busy·timeout·stuck·엔진 reason, 나머지 error)
  - 알람 받기: `POST /internal/alerts`(비밀 `ALERT_WEBHOOK_TOKEN` Bearer, 없거나 틀리면 404) → 울리는 알람만 `report_alert`(Sentry 묶음 = 알람 이름 + 시작 시각 → 알람마다 메일 한 통, Grafana가 12시간마다 다시 보내도 같은 묶음). 로그는 WARNING(ERROR면 로그 연동이 한 번 더 보냄)
  - 서버: `deploy/observability/` — `config.alloy`(서버·cAdvisor·Postgres 수집기 + 앱 셋 긁기 → remote_write), `prometheus.yml`(보관 90일 또는 5 GB, remote write 받기), Grafana provisioning(데이터 원본, 대시보드 `SCRUN 서버`(그래프·숫자 20칸 + 줄 제목 4), 알람 규칙 8개·연락처·정책). compose에 `alloy`(v1.21.3)·`prometheus`(v3.15.1)·`grafana`(13.3.2, `127.0.0.1:3000`만), 메모리 상한 384·768·512 MB(첫 값). `daily.sh` → `backup_metric.sh`가 마지막 백업 시각, `update.sh`가 `~/scrun-metrics`를 만들고 처음이면 가장 최근 백업 파일 시각으로 채움
  - 로컬 확인(이 노트북 Docker, 시험 묶음 Postgres + api(uvicorn 2개) + Alloy·Prometheus·Grafana): 두 프로세스 숫자가 합쳐 나옴, 없는 주소는 라우트 `-`, 대시보드 그려짐, 알람 규칙·연락처 불러옴·익명은 고치기 403, **realtime·ocr이 없는 상태에서 "앱 서비스 지표가 안 긁힘"이 실제로 울려 api `/internal/alerts` 204·`[서버 알람]` 로그**. 시험에서 고친 것: cAdvisor가 컨테이너 이름을 못 붙임(새 Docker는 containerd로 찾음 → `/run/containerd/containerd.sock` 붙임), Grafana가 켜자마자 343 MB(상한 384 → 512), Alloy를 다시 만들면 instance 라벨이 바뀌어 같은 지표가 새 줄(→ `hostname: scrun-alloy`), 5xx 비율이 5xx가 없으면 빔(→ `or vector(0)`). 로컬(Docker Desktop)에선 루트 디스크가 ext4가 아니라 디스크 칸이 비어 서버에서 확인해야 함
  - 테스트 `tests/test_metrics.py` 20개(라우트 틀로 셈·없는 주소 `-`·`/metrics`는 안 셈·공개 목록 밖, 요청 로그와 따로 켜고 끔, OCR 결과별, 알람 → Sentry 1건·지문·풀림은 없음·비밀 틀리면 404, 설정, 대시보드·알람이 쓰는 `scrun_` 이름이 실제로 있음, provisioning 파일, compose가 판 고정·Grafana만 127.0.0.1·tmpfs, Caddy `/metrics` 404)
- 11/28 `/code-review`(규칙·계획 두 갈래) 반영
  - 배포 날 거짓 알람: 백업 알람이 `for: 0s`·데이터 없음 = 울림 → `for: 30m`·지표가 아예 없으면 9999시간으로 계산(`absent`), 수집 자체가 멈춘 건 새 규칙 **"지표 수집 멈춤"**(`absent(up{job="scrun"})` 5분)으로 따로(규칙 8개). `daily.sh`가 백업 시각을 토큰 정리 **뒤**에 적어 토큰 정리만 실패해도 "백업 없음" → `mv` 바로 뒤로
  - `.env`에 새 비밀 둘이 없으면 compose 전체가 멈춤(`${..:?}`, 배포·매일 백업까지) → `update.sh`가 compose 전에 없으면 서버에서 무작위로 만들어 넣음(화면에 안 찍음). 첫 배포는 옛 `update.sh`가 돌아 이 줄이 안 돌므로 배포 전에 손으로 준비(아래)
  - `~/scrun-metrics`를 Docker가 먼저 만들면 root 소유 → `daily.sh`가 못 씀 → `update.sh`가 내 소유가 아니면 `sudo chown`, 첫 배포 전엔 손으로 만듦
  - 짧게 도는 스크립트(`daily.sh`의 토큰 정리)가 지표 파일을 tmpfs에 남김 → `exec -e PROMETHEUS_MULTIPROC_DIR=`로 끔
  - `METRICS=0`이어도 OCR·실시간은 셈 → `metrics.set_enabled`로 모두 끔. OCR 결과 라벨을 소문자로 맞춤(`read`·`unreadable`·`busy`·`timeout`·`stuck`·`ocr_timeout`·`error`), 결과 이름은 예외 타입 `OcrUnavailable(outcome)`로(몰래 붙인 속성 대신). 메서드 라벨은 정해진 7개 밖이면 `OTHER`. 알람 받기가 이상한 본문에 500 대신 400·무시, Sentry가 꺼져 있으면 서버 로그에 ERROR(조용히 사라지지 않게)
  - 대시보드에 API별 5xx 비율·OCR 읽힘 비율(6시간) 추가
  - 남은 한계(가설·알고 둠): Prometheus remote-write·Grafana 익명 보기는 compose 네트워크 안 다른 컨테이너가 인증 없이 닿음(api가 뚫리면 가짜 지표를 넣을 수 있음). Alloy에 `/`·Docker 소켓이 붙어 있어 사실상 서버 관리자 권한(`.env`도 읽힘) → 이미지 판 고정·밖에 안 엶. Postgres 수집기는 앱 DB 계정을 씀(`pg_monitor` 전용 계정이 더 좁음, 출시 전 보안 정리 때). api·realtime·ocr·db·caddy엔 메모리 상한 없음(그대로)
- 11/28 #162 머지(develop `81c1a86`)·운영 배포(11/28 민재: 도입 전·후 비교 전에 배포, 되돌리기 쉬움·지금 운영 트래픽 적음). 첫 배포는 옛 `update.sh`가 돌아 손으로 먼저 준비: `~/scrun-metrics`(ubuntu 소유), `backup.prom`을 가장 최근 백업(11/27 04:30 한국 시간) 시각으로, `.env`에 `GRAFANA_ADMIN_PASSWORD`·`ALERT_WEBHOOK_TOKEN`(서버에서 무작위, 백업 `.env.bak-날짜`)
  - 확인: 공개 `/metrics`·`/internal/alerts` 404, `/healthz/*` 200, 바깥에서 Grafana 3000 안 닿음. Prometheus에 `up{job="scrun"}` 셋 다 1, 디스크 `/`(로컬에선 비던 칸)·컨테이너별 메모리(이름 붙음)·`pg_up`·백업 시각·앱 요청. Grafana 규칙 8개·대시보드 불러옴(익명 보기). Grafana 컨테이너 안에서 그 비밀로 api `/internal/alerts`에 시험 알람 → 204 → **Sentry에 `[서버 알람] 배포 확인용 시험 알람` 도착(`source:grafana`, 무시해도 됨)**
  - 배포 직후 "서버 CPU 85% 넘음"이 대기(pending): 5분 평균에 서버의 Docker 이미지 빌드가 들어감. 확인 때 1분 CPU 8.9%, `for: 10m`이라 안 울림
  - **수집 도구 부담(운영, 평소)**: CPU(코어 1개 = 100%) Alloy 1.4%·Grafana 1.0%·Prometheus 0.5% = 서버 2코어의 약 1.5%. 메모리 Alloy 102 MB·Grafana 297 MB·Prometheus 42 MB ≈ 440 MB(상한 384·512·768 안). 앱 쪽(`/metrics` 셈)의 요청당 부담은 부하가 있어야 보임 → 집 PC 비교로
- 남은 것: 집 PC가 켜지면 도입 전·후 k6(0073 시나리오, `METRICS=0` + `docker compose stop alloy prometheus grafana` vs 켬, 처리량 5% 안), 3단계(로그 모으기 Loki: Alloy가 journald → Loki, Grafana에서 요청 ID로 검색), 4단계(앱 크래시, 맥)
- 11/28 3단계 구현(`feat/observability-3`, develop `f3cc82c`에서) — 로그를 한곳에 모아 Grafana에서 검색
  - `deploy/observability/loki.yaml`: Loki 3.7.11 한 프로세스·파일 저장, **보관 30일**(compactor가 실제로 지움), 들어오는 양 상한 4 MB/초, 사용 통계 안 보냄. compose `loki`(메모리 512 MB, 밖에 안 엶), Alloy가 `/var/log/journal`·`/etc/machine-id`를 읽기 전용으로
  - `config.alloy` 로그 줄: `loki.source.journal`(처음 켤 때 12시간 거슬러) → relabel: `scrun-` 컨테이너만 남김(keep, sshd·cron은 버림) → 라벨 `service`(컨테이너 이름 `scrun-<서비스>-1`에서)·`container`·`stream`(Docker가 stdout 6·stderr 3으로 적음. 접속 줄·요청 로그 JSON은 stdout, uvicorn 시작·경고는 stderr → 오류 등급으로 안 씀) → **컨테이너 헬스체크 접속 줄 버림**(api 하나만 시간당 약 350줄, 서버 전체 journald는 시간당 약 5천 줄, 11/28) → `loki.write`. 요청 ID·상태는 라벨로 안 둠(값이 끝없이 늘어남) → 검색 때 `|= "req_…"`·`| json`
  - Grafana: Loki 데이터 원본, 대시보드 **SCRUN 로그**(서비스 고르기·검색어 칸, 서비스별 줄 수·오류처럼 보이는 줄 수, 로그 목록, 요청 로그 JSON 중 5xx·1초 넘는 요청 표)
  - 로컬 확인(Docker, Loki + Grafana, 시험 줄 5개를 Loki에 직접 넣음): 대시보드 쿼리 전부 Grafana(익명 보기)로 성공, 검색어 빈칸 5줄, **`req_ccc333ddd444` → api·ocr 두 서비스의 그 요청 줄 2개**, 5xx·1초 표가 api 500·ocr 4.7초를 거름. 서버 journald 읽기는 로컬(Docker Desktop)에 journald가 없어 배포 뒤 서버에서 확인
  - **로그 점검(11/28, 운영 7일 `journalctl --since -7d CONTAINER_NAME=scrun-{api,realtime,ocr,caddy}-1 | grep -cE <패턴>`, 9만 7,612줄)**: Bearer 토큰·JWT 모양(`eyJ…`)·`access_token`류·password·secret·Sentry DSN·이메일 주소 **모두 0건**(실시간 토큰은 머리로 와서 주소에 안 남음, 요청 로그는 라우트 틀만). 9만 979줄(93%)이 헬스체크 `GET /v1/agreements` 접속 줄 → 버리면 Loki에 쌓는 양이 크게 줆
  - 로그 레벨은 **정리하지 않음**(이유: 요청 로그 INFO·문제 WARNING·고장 ERROR로 이미 나뉘어 있고, ERROR는 1단계에서 Sentry로 감. 바꿀 게 보이면 그때)
  - 개인정보: uvicorn 접속 줄에 접속 IP·기록 ID가 든 주소가 남음(이미 서버 journald에 있던 것을 한곳에 모음) → 30일 보관, 개인정보처리방침(BL-40, 상준)에 "서버 접속 기록 30일 보관" 한 줄
  - 11/28 리뷰 반영: **`stage.drop { source = "service" value = "" }`가 거꾸로 동작**(Alloy는 빈 value를 "값 없음"으로 봐 service가 있는 줄, 즉 scrun 줄을 다 버림 → 로그 화면이 빌 뻔함, 리뷰가 로컬 Alloy로 재현) → relabel `keep`(`scrun-.+`) + `stage.match {service=""} drop`으로 바꾸고 **실제 Alloy v1.21.3에 시험 줄을 흘려 확인**(scrun api·바깥 IP 약관 요청·db 줄은 남고 헬스체크·sshd·다른 컨테이너 줄은 버려짐, 라벨 `service`·`container`·`stream` 맞음). db·caddy는 `logging`이 없어 journald로 안 갔음 → 둘 다 journald(배포 때 몇 초 다시 시작). 요청 표 쿼리에 `| __error__=""`. 검색어에 `"`·`\`는 쿼리를 깸(화면 설명에 적음). 가설: ARM Alloy 이미지가 서버 journal 형식을 읽는지는 배포 뒤 첫 줄로 확인
  - 테스트 `test_metrics.py` 21개(로그 줄: scrun-만·헬스체크 버림·30일·사용 통계 끔·로그 화면 쿼리가 service 라벨로·Alloy journald 마운트·Loki 밖에 안 엶, 데이터 원본 둘)
