# ADR-0014: 서버 부하 테스트는 k6로 한다

- 상태: 승인
- 날짜: 2026-11-25
- 결정한 사람: 민재 (계획 0070, 비교 자료 `docs/plans/0070-load-tool.html`)

## 배경
서버 개선(OCR 비동기 큐, 피드·목록 인덱스, 실시간 확장)을 하려면 어디가 얼마나 느린지 먼저 재야 한다. 운영 DB가 작아(사용자 2명·기록 20개) API가 5–18 ms라 부하와 대량 데이터 없이는 개선 전후가 안 보인다. 계획 0070의 시나리오 네 개(S1 앱 켜기, S2 달리기 한 번, S3 실시간 WebSocket 20명, S4 계기판 사진 3장 업로드)를 같은 조건으로 다시 돌릴 도구가 필요하다.

## 선택지
| 선택지 | 장점 | 단점 |
|---|---|---|
| JMeter | 1998년부터 쓰인 업계 표준 | GUI·XML(`.jmx`)이라 git 리뷰가 어려움, WebSocket은 플러그인, JVM·스레드라 무거움 |
| Gatling | WebSocket 내장, 코드로 시나리오 | JVM·Maven/Gradle이 따라옴, 팀이 JVM을 안 씀 |
| Locust | 파이썬(서버·speedlab과 같은 언어), 실시간 웹 화면 | WebSocket·문턱 판정이 내장 아님(직접 짜거나 locust-plugins), 프로세스 하나 = 코어 하나, `fake_runner.py`의 asyncio 실시간 코드는 gevent에 그대로 못 넣음 |
| **k6** | WebSocket·multipart 내장, `thresholds`로 p95 문턱 판정·종료 코드(CI에 그대로), Go라 노트북·작은 VM에서도 부하를 많이 만듦, HTML 보고서·Prometheus 내보내기 | 시나리오가 JavaScript(팀 주 언어 아님), Node.js가 아니라 npm 패키지는 그대로 못 씀 |
| wrk·Vegeta·hey | 가볍고 빠름 | 주소 하나만 두드림, 로그인 → 홈 → 피드 같은 흐름을 못 그림 |

## 결정
k6. 시나리오는 저장소 `backend/perf/`(구현 때 확정)에 JavaScript로 두고, 노트북에서 시험 서버로 건다. 운영 서버에는 걸지 않는다.

## 이유
- S3 실시간이 WebSocket이라 내장 지원이 결정적이다
- 계획 0070 판정("같은 조건 3번, p95 차이 10% 안")과 나중 CI 성능 문턱을 `thresholds` 한 줄로 쓸 수 있다
- 부하를 만드는 쪽이 가벼워 노트북(Core Ultra 5 225H, 15 GB)으로 충분하다. 우리 규모는 가상 사용자 수백 명(가설, 첫 실행 때 k6 CPU 기록으로 확인)
- 쓰는 정도: GitHub 별 32,401(Locust 28,312, JMeter 9,613, 2026-11-25 GitHub API), ThoughtWorks Technology Radar 2022-10 Adopt. 사용률 공식 조사는 못 찾음

## 결과와 위험
- 시나리오가 JavaScript라 파이썬 코드(`fake_runner.py`)와 따로 관리한다. 로그인·입장 순서는 같은 API 문서(`docs/api/`)를 따른다
- 시험 서버는 집 PC(Docker), k6는 같은 공유기의 노트북(계획 0070 정한 것 1). 측정값에 무선 흔들림(왕복 1–18 ms)이 섞인다 → 서버 안 처리 시간은 지연 로그 미들웨어로 따로 본다(계획 0070 관측)
- 발표 그림: k6 웹 대시보드 HTML 보고서 + `data/perf/` 원본으로 그린 전후 비교(matplotlib). 서버에 Grafana는 띄우지 않음(필요하면 나중에, 노트북 쪽에)
- 라이선스 AGPL-3.0: 시험할 때 돌리기만 하고 우리 서버·앱에 넣거나 배포하지 않아 공개 의무와 무관
- 다시 검토할 조건: k6로 S3 WebSocket 시나리오가 실제 앱 흐름(입장 → `welcome` → 상태 0.5초마다 → `leave`)을 못 그릴 때, 노트북이 부하 만드는 쪽 병목이 될 때

## 참고
- 비교 자료: `docs/plans/0070-load-tool.html`
- [ThoughtWorks Technology Radar — k6](https://www.thoughtworks.com/radar/tools/k6), [k6 문서](https://grafana.com/docs/k6/latest/), [Locust 문서](https://docs.locust.io/en/stable/)
