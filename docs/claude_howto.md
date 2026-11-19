# Claude로 작업할 때 요령 (Figma·Unity 에디터·노션·HTML)

세션마다 다시 찾느라 시간을 쓴 것들. 민재 인수인계(`handoff/minjae.md`)에서 11/18 옮김. 팀원 Claude도 같이 쓴다.

## Figma
- 파일: `https://www.figma.com/design/w5YpqhN1yyWXf03z23eTKK/SCRUN`(상준 소유라 Figma 파일 검색엔 안 나옴, 주소로 바로 연다). 와이어프레임은 페이지 "스크런 와이어 프레임"(`585:18179`), 화면 프레임 이름이 `A-01`·`G-01 친구 탭 · …` 식, 노트는 `Note G-01` 같은 이름 노드
- 읽기: Chrome(Claude 확장 연결, Figma 로그인)에서 파일을 열고 `javascript_tool`로 `figma` 전역(플러그인 API). 파일을 연 뒤 10–20초 기다려야 `figma`가 생김. 페이지는 `await page.loadAsync()` 뒤 읽기
- 맥 Chrome도 연결돼 있으면 `list_connected_browsers`로 Windows(`Browser 1`)를 `select_browser`
- 출력은 1500자쯤에서 잘림 → 프레임마다 글자·색·위치를 `window.G` 같은 전역에 모아 두고 조각씩 꺼냄. 출력에 `@`·`=`이 많으면 "Cookie/query string data"로 막힐 수 있음(형식 바꾸기)
- 큰 `findAll`은 45초 넘게 멈춤 → 탭 다시 열고 프레임 하나씩 `children`만
- 캔버스가 비고 `exportAsync`가 "no visible layers"로 실패하면 렌더러가 아직 안 뜬 것 → 탭을 다시 열고 기다림(새 탭은 한동안 안 됨). 로그인 안 된 창(보기 전용)은 그림은 보이지만 `figma`가 없음. 그림을 페이지 위 `<img>` 덮개로 띄워 스크린샷으로 보는 게 빠름(계획 0061)
- 그림 받기: `exportAsync({format:'PNG', constraint:{type:'SCALE', value:3}})` → blob 다운로드. Chrome은 같은 탭의 두 번째 자동 다운로드부터 막음 → 새 탭에서 여러 장을 한 파일로 묶어 한 번에(길이 머리 + 이름 + 바이트) 받고 Python으로 나눔

## 파일 고치기(Windows)
- **Python `Path.write_text`·`open(..., 'w')`는 Windows에서 줄바꿈을 CRLF로 바꿔 씀** → 저장소 파일(LF)이 통째로 바뀐 diff가 됨(11/20 #115에서 겪음). `write_bytes(s.encode())`나 `open(..., 'w', newline='\n')`로 쓰고, 커밋 전에 `git diff --stat`이 실제 바꾼 줄 수와 맞는지 봄. bash heredoc 안 Python 문자열의 `\\n`은 진짜 줄바꿈이 되기도 함 → C# 문자열의 `\n`은 Edit로 넣기

## Unity 에디터(MCP)
- 버튼 누르기: `NavigationSubmitEvent`를 `panel.visualTree.SendEvent`로(`e.target = 버튼`). 버튼에 바로 `SendEvent`는 가끔 안 먹음. UXML에 `using`이 없으니 `UQueryExtensions.Q<Button>(root, name, (string)null)`
- 화면 바로 띄우기: `Scrun.App.AppRoot.Nav.StartFlow(new XxxScreen())`·`Nav.Push(...)`·`Nav.SelectTab(AppTab.Friends)`
- 캡처: UI Toolkit까지 찍으려면 `ScreenCapture.CaptureScreenshot(절대 경로)`를 부르고 한 번 더 불러야 파일이 생김(에디터가 포커스가 없으면 몇 프레임 늦음 → 같은 경로로 다시 찍어 확인). `manage_camera` 스크린샷엔 UI Toolkit이 안 찍힘 → 요소 위치·글자를 코드로 확인
- Play로 앱 흐름 확인: Enter Play Mode 옵션이 도메인 리로드를 안 해서 `ApiConfig.BaseUrl`(정적)과 EditorPrefs `Scrun.ApiBaseUrl`을 둘 다 바꾸고 `EditorApplication.EnterPlaymode()`. 달리기는 홈 → `Nav.Push(new CourseDetailScreen("crs_lake_loop"))` → `enter` → `goal-next`·`mount-next`·`manual-next` → `FakeSpeedSensor.Instance.SetTargetKmh(9f)`. 끝나면 주소를 8001로 되돌리기
- 이 브랜치 서버로 보기: `cd backend; ../.venv/Scripts/python -m uvicorn app.main:app --host 127.0.0.1 --port 8002`(8001은 민재 서버) → 위처럼 주소를 8002로. 다른 사람 역할은 `scripts.fake_runner`(가짜 카카오 `k_숫자`, `--base http://127.0.0.1:8002`), 기록은 개발 DB에 바로 넣기
- 다른 러너 화면 확인: `fake_runner`는 남의 상태를 안 찍음 → `scripts.fake_runner`의 `login`을 불러 0 km/h로 서서 `states`의 `v`·`d`를 시각과 함께 찍는 작은 스크립트. 에디터는 `FakeSpeedSensor.Instance.SetTargetKmh`를 `EditorApplication.update` 콜백으로 시간표대로 바꾸며 `NativeSpeedSource.SpeedKmh`를 기록 → 두 로그를 시각으로 맞춤. 봇·위치는 같은 맵에 가짜 두 사람을 붙여 같은 순간 값을 비교
- Play 중 값 모으기: `EditorApplication.update` 콜백으로 모아 `AppDomain.SetData`에 두고 다음 호출에서 꺼냄
- `execute_code`는 CodeDom(C# 6): `$"..."` 일부·`Object` 모호함 주의. 테스트는 Play 중이면 시작이 막힘(민재에게 묻고 `manage_editor stop`)
- EditMode 테스트: 패널에 안 붙은 요소는 값 바뀜 이벤트가 안 옴 → 화면 쪽 갱신 함수를 `internal`로 두고 직접 부름. 화면은 `screen.Create(null)`로 만들 수 있음(서버 호출은 `OnShow`에 두기)
- USS: 화면 UXML의 USS 규칙은 그 화면 Root 안쪽에만 닿음(app-root 배경은 `App.uss`에). 코드로 화면 Root에 붙이는 덮개·시트는 `.fr .x`처럼 범위 클래스 아래가 아니게 되니 범위 클래스를 직접 붙인다. 페이드인 클래스는 첫 `GeometryChangedEvent` 뒤에. UXML 주석에 `--` 쓰면 화면 전체가 안 불림. `:last-child`는 UI Toolkit이 모름
- `ProjectSettings.asset`은 `SaveAssetIfDirty`로 안 써짐 → `AssetDatabase.SaveAssets()`(다른 에셋도 써질 수 있으니 git 확인). `unity/tools/ucs.sh`로 설정을 바꿀 때도 같음
- `manage_camera` screenshot 파일은 `Assets/Screenshots`에 생김 → 끝나면 지우기
- 앱 어셈블리(`Scrun.App.asmdef`)는 섬 스크립트(Assembly-CSharp)를 못 부름 → `RunLink`에 `Func` 고리. C# 테스트 이름은 숫자로 시작 못 함
- 한글 컴퓨터 이름: Mono가 `localhost`를 못 풀어 ClientWebSocket 실패 → 에디터 서버 주소는 `127.0.0.1`(코드가 자동 변환)

## 노션
- 개발 계획(작업 표·기능 보드): `https://app.notion.com/p/4114ae82f1de81a9bec2db16a9bfe635`, API 명세 표는 SCRUN > 문서 > API 명세. 노션 MCP로 표를 SQL로 읽고 줄 속성을 고침
- API 명세 각 줄 본문은 손으로 쓰지 않는다: `cd backend; ../.venv/Scripts/python -m scripts.notion_pages` → `build/notion/<동작>_<주소>.md`를 그 줄 페이지(`NOTION_PAGE_IDS`)에 통째로 덮어씀. 공통 규칙 줄은 `_common.md`. 이 스크립트와 `scripts.export_openapi`는 pytest와 같은 테스트 DB를 비우고 쓰므로 pytest가 도는 동안 돌리지 않기(11/19 "예시 흐름 실패 401")
- 상준 공개 개발 백로그: 공개 API `POST https://heavenly-beak-116.notion.site/api/v3/queryCollection`(collection `14a69719-4a05-45bb-8bda-36cc20118493`, view `7fe2ce1d-a4a0-4daf-8d77-8a5db8ebd139`, spaceId `e29dea3b-d189-81ed-896d-0003ca665f04`)으로 51개를 읽었는데 11/17부터 403. User-Agent를 바꿔 다시 부르는 건 권한 검사에 막힘 → Chrome으로 표를 열고 `javascript_tool`로 스크롤하며 줄 글자를 모음(BL 번호로 찾기). 11번부터 번호가 바뀜(HUD 14→12, 계정 삭제 39→48, 실시간 36→25)

## 로컬 HTML·PDF
- Chrome 확장은 `file://`을 못 엶 → `.venv/Scripts/python -m http.server 8765 --bind 127.0.0.1`(백그라운드)로 띄워 확인, 끝나면 끄기
- PDF: `chrome.exe --headless=new --no-pdf-header-footer --allow-file-access-from-files --print-to-pdf=<pdf> file:///<html>`(HTML에 `@page{size:1280px 720px}`·`.slide{break-after:page}`). PDF 화면은 확장으로 캡처 안 됨 → `pypdf`로 쪽수·글자 확인. 이 PC엔 poppler·PyMuPDF 없음

## git·셸
- 커밋 제목은 50자 이내(훅이 막음, 막히면 스테이지가 다음 커밋에 섞이니 확인)
- Git Bash `sed -i`는 CRLF 파일을 LF로 바꿈, Python `write_text`는 Windows에서 LF를 CRLF로 바꿈 → `read_bytes`·`write_bytes`로 고치고 `git ls-files --eol`로 확인(CLAUDE.md는 CRLF)
- Bash에서 `python -`는 Windows 스토어 파이썬을 불러 멈출 수 있음 → `.venv/Scripts/python`
- 지금 브랜치를 그대로 두고 다른 브랜치 문서만 고칠 땐 `git worktree add <스크래치 폴더> <브랜치>` → 커밋·푸시 → `git worktree remove`
- 권한 검사: 강제 푸시·남의 프로세스 끄기는 막힘. 오라클 배포·머지는 민재가 "배포해"·"머지해"라고 해야 통과(DB를 읽기만 하는 SSH도 그때만). DB 확인은 SQL 파일을 `ssh … 'docker exec -i scrun-db-1 sh -c "psql -U \$POSTGRES_USER -d \$POSTGRES_DB -tA"' < 파일`
- 지난 세션 기록(`~/.claude/projects/c--Users-rlaal-Desktop-Capstone1/*.jsonl`)을 grep하면 주소·ID를 다시 찾을 수 있음
