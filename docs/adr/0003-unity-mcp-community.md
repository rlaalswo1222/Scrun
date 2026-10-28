# ADR-0003: Claude와 Unity 연결은 커뮤니티판 MCP for Unity를 쓴다

- 상태: 승인 (소급 기록)
- 날짜: 2026-10-26
- 결정한 사람: 사용자

## 배경
Claude Code가 Unity 에디터를 직접 조작(씬 생성, C# 실행, 빌드, 화면 캡처)해야 개발 속도가 난다.

## 선택지
| 선택지 | 장점 | 단점 |
|---|---|---|
| 공식 Unity MCP | Unity 공식 지원 | Unity Cloud + AI 유료 구독 필요 |
| 커뮤니티판 MCP for Unity (CoplayDev) | 무료, 기능 충분(execute_code, manage_build, read_console 등) | 개인·커뮤니티 유지보수, 버전 바뀌면 동작이 달라질 수 있음 |
| MCP 없이 사람이 에디터 조작 | 추가 도구 없음 | 매 작업마다 사람이 클릭, 느림 |

## 결정
CoplayDev MCP for Unity v10.3.0을 `Packages/manifest.json`에 버전 고정해서 쓴다. 서버는 로컬 전용(`127.0.0.1:8080`), Claude Code에는 사용자 범위로 등록.

## 이유
- 비용 없음
- 이 프로젝트에서 필요한 기능(씬 빌더 메뉴 실행, C# 실행, APK 빌드, 콘솔 읽기)이 모두 됨

## 결과와 위험
- 버전 고정 → 업데이트는 직접 판단해서 올림
- 에디터를 끄면 연결이 끊김. 채팅에 도구가 안 뜨면 `unity/tools/umcp.sh`(curl)로 직접 호출
- execute_code는 C# 6(CodeDom), `AssetDatabase.DeleteAsset` 막힘
- 다시 검토할 조건: 커뮤니티판 유지보수가 멈추거나, 학교·팀이 공식 구독을 갖게 될 때

## 참고
- `unity/tools/README.md`
