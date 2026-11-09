# ADR-0010: 실시간 같이 뛰기는 우리 서버(FastAPI)에 WebSocket을 붙여 주고받는다

- 상태: **승인**
- 날짜: 2026-11-10
- 결정한 사람: 김민재

## 배경
같은 코스(= 맵 하나, 최대 20명)에 들어온 사람들의 폰이 서로의 상태를 계속 알아야 한다(BL-21 공동 맵, BL-23 페이스 봇). 전송 방식은 BL-25 스파이크로 미뤄 두었고(ADR-0002·0008 위험 항목), S3(11/12–13) "실시간 방 서버 기본"과 11/13 시연이 이 결정을 기다린다.

주고받는 것은 단순하다. 폰은 "코스 시작점에서 몇 m, 지금 몇 km/h"를 1초에 1–2번 보내고, 서버는 들어옴·나감과 봇 정보를 보낸다. 위치는 1차원(코스 위 거리)이고 방향·충돌·물리가 없다. 속도 추정 자체의 반영 지연이 예비 실험 평균 약 5–6초(BL-13 Q-11)라 네트워크 지연 0.2초는 상대적으로 작다.

설명 자료: `docs/research/realtime_options.html`

## 선택지
| 선택지 | 장점 | 단점 |
|---|---|---|
| A. FastAPI WebSocket (우리 서버) | 20명·입장 잠금·봇·지금 달리는 수를 입장 API와 같은 곳에서 정함, 로그인 토큰 그대로, 출시 뒤에도 서버비 없음, 아는 기술만 씀 | 중계·재접속을 직접 짬, 오라클 싱가포르라 지연이 김(실측 아래), 서버를 여러 대로 늘리면 공유 저장소(Redis) 필요 |
| B. Photon Fusion 2 (공유 모드) | 중계 서버를 안 짬, 서울 지역(`kr`), 방장이 나가면 자동 승계 | 게임용 틀(틱·예측·물리)이라 우리에겐 큼, 우리 서버와 Photon 두 곳의 인원을 맞춰야 함, 커스텀 인증 따로, 무료 100 CCU(출시 가능·앱 1개)를 넘으면 500 CCU 월 $125, 개발용 무료 20 CCU는 출시 불가 |
| Photon PUN 2·Realtime, Unity Netcode + Relay, Colyseus, Nakama | — | 출시 가능한 Photon 무료는 Fusion·Quantum만 해당. 나머지는 게임 넷코드이거나 서버가 하나 더 생겨 B와 같은 이유로 깊게 보지 않음(가격 미확인) |

## 결정
A. 공용 서버의 FastAPI 앱에 WebSocket 엔드포인트를 붙여 방(맵)마다 상태를 중계한다. 앱은 Unity용 WebSocket 라이브러리로 연결하고, 다른 사람 아바타는 받은 위치를 보간해서 그린다.

## 이유
- 서버가 정해야 하는 규칙(맵당 20명, 입장 잠금, 봇 수·페이스는 서버 설정값, 지금 달리는 사람 수)이 이미 우리 서버에 있다. A는 한 곳에서 정하고, B는 두 곳을 맞춰야 한다
- 주고받는 양이 작다(계산: 한 사람 상태 약 80바이트 × 20명 × 1초 2번 = 폰 한 대가 받는 양 초당 약 3.2KB). Fusion의 강점을 쓸 데가 없다
- 로그인 토큰·오류 규칙(`docs/api/README.md`)을 그대로 쓴다
- 지연 실측(11/10, 민재 Windows 노트북 와이파이, TCP 연결 왕복 12–15회 중앙값): 오라클 싱가포르(우리 서버) **222ms**, AWS 싱가포르 96ms, AWS 서울 39ms. 10 km/h에서 0.22초면 약 0.6m(계산)라 보간으로 가릴 수 있다고 본다(가설, S3 실측으로 확인)
- 앞단 Caddy는 WebSocket을 설정 없이 통과시킨다([Caddy reverse_proxy](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy)). 서버는 uvicorn 한 프로세스라 방 상태를 메모리에 둬도 된다

## 결과와 위험
- **지금 달리는 사람 수**(`runnersNow`)는 실시간 서버의 연결 수로 센다. 샘플을 올릴 때 `runs.updated_at`을 갱신하는 임시 방식은 실시간 서버가 생길 때까지만(`docs/api/README.md` "코스와 맵 인원")
- **봇**: 서버가 봇의 출발 시각·페이스를 내려 주고 각 폰이 위치를 계산한다(모두에게 같은 위치, BL-23 ③)
- **메시지 모양·재접속 규칙**은 S3 계획에서 정한다. 입장 API 응답의 `realtime.url`·`token`(지금 스텁 모양)도 그때 확정
- **지연**: S3에 아이폰 2대(LTE·헬스장 와이파이 포함)로 실제 메시지 왕복을 잰다. 화면에서 문제가 보이면 서버를 서울·춘천 지역으로 옮기는 것을 운영 서버 작업(BL-51)에 넣는다
- **끊김**: 헬스장 와이파이 70초 끊김(10/29 실측)이 있었다. S3는 "다시 붙으면 방 상태 다시 받기"까지, 오래 끊긴 경우는 S4 측정 예외(BL-15)와 같이
- **확장**: 서버를 여러 대로 늘릴 때 방 상태를 Redis 같은 공유 저장소로 옮긴다(그 전엔 안 함)
- **다시 검토할 조건**: 실측 지연이 보간으로 가려지지 않는데 서버를 옮기기 어려울 때, 같이 뛰기에 게임다운 실시간 동작(몸싸움·추월 판정 등)이 많이 붙을 때 → B를 다시 본다. 앱은 화면 코드가 "같이 뛰기 연결" 인터페이스만 보게 만들어 바꾸는 비용을 줄인다

## 참고
- 설명 자료 `docs/research/realtime_options.html`(비교표·실측·공부 자료)
- 백로그 BL-7·13·21·23·25(상준 공개 노션), 계획 0017(S2·S3), 계획 0019(서버 사양)
- [FastAPI WebSockets](https://fastapi.tiangolo.com/advanced/websockets/), [NativeWebSocket](https://github.com/endel/NativeWebSocket), [Entity Interpolation](https://www.gabrielgambetta.com/entity-interpolation.html)
- [Photon Fusion 가격](https://www.photonengine.com/fusion/pricing), [Photon 요금제](https://doc.photonengine.com/photon/current/pricing), [Photon 지역](https://doc.photonengine.com/realtime/current/connection-and-authentication/regions), [Fusion 방장 규칙](https://doc.photonengine.com/fusion/current/manual/shared-mode-master-client)
