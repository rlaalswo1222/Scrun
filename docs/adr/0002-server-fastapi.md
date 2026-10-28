# ADR-0002: 서버는 FastAPI(Python)로 만든다

- 상태: 승인 (소급 기록)
- 날짜: 2026-10-26
- 결정한 사람: 팀

## 배경
계정, 달리기 기록, 계기판 사진 인증, 연속 일수·순위·체형 점수 판정을 서버에서 해야 한다(유저플로우 v2 설계 원칙). 학교 수업에선 Spring이 흔하다.

## 선택지
| 선택지 | 장점 | 단점 |
|---|---|---|
| Spring Boot (Java) | 학교·취업에서 흔함, 자료 많음 | 팀에 경험자 없음, 설정·코드량 많음 |
| FastAPI (Python) | 이미 Python 사용 중(`server.py`, `speedlab/`), 코드 짧음, 자동 API 문서 | 대규모 운영 경험 자료는 Spring보다 적음 |
| Firebase·Supabase 같은 BaaS | 로그인·DB를 거의 공짜로 | 인증·판정 로직 제어가 제한적, 서비스 종속 |

## 결정
FastAPI로 만든다.

## 이유
- 팀에 Spring 경험자가 없음
- 속도 분석 코드(`speedlab/`, numpy·scipy)와 같은 언어라 서버에서 재분석·검증하기 쉬움
- 이미 `server.py`(FastAPI)로 실험 서버를 운영해 봄

## 결과와 위험
- 실시간 "같이 뛰는 방"은 아직 미정(실시간 동시 접속인지 답 대기). 실시간이면 Photon 같은 별도 서비스나 WebSocket 설계를 따로 정해야 함 → 그때 ADR 추가
- 다시 검토할 조건: 로그인·저장만 필요하고 판정 로직이 단순해지면 BaaS가 더 쌀 수 있음

## 참고
- 유저플로우 v2 [docs/product/scrun_userflow_v2.html](../product/scrun_userflow_v2.html)
