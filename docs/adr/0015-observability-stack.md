# ADR-0015: 오류는 Sentry 무료, 지표·로그·대시보드는 오라클에 직접(Prometheus·Loki·Grafana), 바깥 감시는 UptimeRobot, 알림은 이메일

- 상태: 승인
- 날짜: 2026-11-27
- 결정한 사람: 김민재(설명 자료 `docs/plans/0087-observability.html`을 보고 고름)

## 배경
- 서버가 죽거나 500이 나도, 앱이 아이폰에서 꺼져도 알림이 없다. 숫자는 k6 부하 테스트 때만 손으로 잰다(계획 0070은 "Prometheus·Grafana는 띄우지 않음"으로 최소만 함)
- 사용성 테스트(12월)·출시(종강 뒤) 전에 크래시·오류를 모아야 하고, 4차 멘토링(11/26)에서 "로깅·모니터링이 되고 있나", "비포·애프터를 기록하라", "모니터링으로 추세를 보다가 준비한다"는 말을 들음
- 서버는 오라클 무료 ARM 1대(2코어, 메모리 11 GB 중 약 1.5 GB 사용, 11/27). 예산은 0원이 기본(ADR-0008)

## 선택지
| 층 | 선택지 | 장점 | 단점 |
|---|---|---|---|
| 오류 수집 | **Sentry 무료** | 0원, FastAPI·Unity iOS(C#·네이티브 크래시·dSYM·IL2CPP 줄 번호) 공식 SDK, 한곳에서 봄 | 1명, 월 약 5천 건, 데이터가 Sentry 서버 |
| | Sentry Team 유료 | 여러 명, 월 약 5만 건 | 월 약 26달러 |
| | GlitchTip 직접 운영 | 여러 명 무료, 데이터가 우리 서버, Sentry SDK 호환 | 메모리 0.5–2 GB(2차 출처, [SSD Nodes 정리](https://www.ssdnodes.com/learn/self-hosted-error-tracking-sentry-alternatives)), Unity iOS 네이티브 크래시 기호 풀이 미확인 |
| | Firebase Crashlytics | 무료, Unity 지원 | 앱만(서버 오류는 따로 봐야 함) |
| 지표·로그·대시보드 | **오라클에 직접** | 무료, 보관 기간 우리가 정함, 로그가 밖으로 안 나감 | 운영을 직접, 메모리 1–2 GB 추가(가설), 서버와 같이 죽음 |
| | Grafana Cloud 무료 | 설정 적음 | 지표 1만 시계열·로그 월 50 GB·**14일 보관**, 로그가 밖으로 나감 |
| 로그 모으기 | **Loki**(위 직접 운영에 포함) | 라벨만 색인해 가벼움, Grafana에서 지표와 같이 봄 | 본문 전체 검색은 느림(라벨로 좁힌 뒤 찾음) |
| | ELK(Elasticsearch·Logstash·Kibana) | 로그 본문 전체를 색인해 검색·집계가 강함, 현업에서 오래 씀 | Elasticsearch가 JVM이라 메모리를 많이 씀(가설: 서버 하나에 수 GB, 2차 출처 [SSD Nodes](https://www.ssdnodes.com/learn/self-hosted-log-management-vps)), 지표·대시보드는 따로, 11/28 민재 질문으로 추가 |
| 알림 | **이메일** | 따로 만들 것 없음, Sentry·UptimeRobot이 기본으로 보냄(Grafana 알람은 Sentry를 거침, 아래 결정) | 놓치기 쉬움, Grafana는 SMTP 설정 필요 |
| | Discord / 텔레그램 | 폰 알림이 바로 옴 | 채널·봇을 만들어야 함 |
| Grafana 접근 | **SSH 터널로만** | 밖에 포트를 안 엶 | 오라클 키가 있는 사람만 봄 |
| | Caddy 뒤 주소 + 로그인 | 폰에서도 봄 | 공개 주소라 비밀번호·업데이트 관리 |

## 결정
- **오류 수집 = Sentry 무료(Developer)**. 서버(FastAPI)와 앱(Unity iOS) 둘 다. 계정은 1명(누가 만들고 팀이 어떻게 볼지는 계획 0087 1단계에서)
- **지표·로그·대시보드 = 오라클에 직접**: Prometheus(숫자) + Loki(로그) + Grafana(그래프·검색·알람) + Grafana Alloy(수집), 수집기(서버·컨테이너·Postgres)
- **바깥 감시 = UptimeRobot 무료**(5분 간격): 서버가 통째로 죽어 Grafana도 죽었을 때를 메움
- **알림 = 이메일**(Sentry·UptimeRobot은 직접, Grafana 알람은 Sentry를 거쳐, 11/28 추가)
- **Grafana는 SSH 터널로만** 연다(밖에 포트 안 엶)

## 이유
- 앱 크래시까지 한곳에서 보려면 Sentry가 Unity iOS 네이티브 크래시를 공식 지원하는 게 결정적. GlitchTip은 그 부분이 확인되지 않음
- Grafana Cloud 무료는 14일 보관이라 한 달 추세·발표용 비포·애프터 그래프가 사라질 수 있음. 오라클엔 메모리 여유가 있고, 로그(요청 경로·오류 메시지)가 회사 밖으로 나가지 않음
- 이메일은 세 도구(Sentry·UptimeRobot·Grafana) 모두 지원하고 새 채널을 만들 필요가 없음
- Grafana를 공개 주소로 열면 관리할 공격면이 늘어남. 보는 사람은 서버를 다루는 사람(민재)이 중심

## 결과와 위험
- 서버 부담: 도입 전·후 k6 같은 시나리오로 처리량·p95를 비교(계획 0075와 같이). 떨어지면 수집 주기·보관을 줄임
- Sentry를 쓰면 오류 정보(기기·앱 버전·코드 위치)가 Sentry 회사로 감 → 개인정보처리방침(BL-40)·iOS 개인정보 표시에 적음, 사용자 정보·쿠키·요청 머리·본문·지역 변수는 안 모음(`data_collection`, 2.77에서 `send_default_pii`를 대신함), DB 오류 메시지의 바인드 값도 숨김(`hide_parameters`)
- 무료 1명: 팀원이 직접 보려면 공용 계정이나 유료(Team)가 필요. 월 5천 건을 넘기면(출시 뒤) 다시 검토
- Grafana 알람 메일: SMTP 계정 대신 **Grafana → api `/internal/alerts` → Sentry 오류 → Sentry 메일**(11/28 민재, 계획 0087 2단계). 새 계정·비밀번호가 없고 알람이 드물어 무료 한도에 영향이 거의 없음(가설). 한계: api가 죽으면 이 길도 죽음 → api 죽음은 UptimeRobot이 메일. 이메일 알림을 놓치는 일이 잦으면 Discord로 바꿈
- 다시 검토할 조건: 서버를 옮길 때(ADR-0008), 사용자가 늘어 월 5천 건·디스크가 모자랄 때, Grafana를 팀이 폰에서 자주 봐야 할 때

## 참고
- 설명 자료·후보 비교: `docs/plans/0087-observability.html`, 계획 `docs/plans/0087-observability.md`
- 가격·한도(11/27 검색, 바뀔 수 있음): [Sentry pricing](https://sentry.io/pricing/), [Grafana Cloud Free](https://grafana.com/products/cloud/free-tier/), [UptimeRobot pricing](https://uptimerobot.com/pricing/)
- [Sentry Unity 네이티브 지원](https://docs.sentry.io/platforms/unity/native-support/), [Sentry FastAPI](https://docs.sentry.io/platforms/python/integrations/fastapi/), [Grafana Alloy 리눅스 모니터링 예제](https://grafana.com/docs/alloy/latest/monitor/monitor-linux/), [구글 SRE 책 골든 시그널](https://sre.google/sre-book/monitoring-distributed-systems/)
