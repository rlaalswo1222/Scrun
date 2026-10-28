# Unity 작업 도구 (MCP for Unity를 curl로 호출)

Unity에서 Window → MCP for Unity → Start Server 켠 상태에서 Git Bash로 실행.

| 파일 | 하는 일 |
|---|---|
| `umcp.sh <method> '<json>'` | MCP 요청 한 번 (세션 ID는 `$TEMP/umcp.sid`) |
| `ucs.sh file.cs` | C# 메서드 본문 실행(execute_code, CodeDom = C# 6) |
| `umenu.sh "Scrun/..."` | 메뉴 실행 |
| `ucon.sh [개수] [error,warning,log]` | 콘솔 읽기 |
| `rebuild.sh` | 플레이 중지 → 컴파일 → Build Island Scene(약 3분) → 콘솔 → 플레이 |
| `fast.sh` | 플레이 중지 → 컴파일 → Rebuild Vegetation Meshes (fast)(약 1분) → 플레이 |
| `ROUTE=거목 PRESET=0 WAIT=4 shoot.sh dist 4410 이름` | 코스·거리로 순간이동 후 메인 카메라 캡처 → `cap/이름.png` (`region 0~4`도 가능, PRESET 0 = 맑은 오후, 1 = 노을) |
| `t_ping.cs` / `t_stats.cs` | 상태 확인 / 삼각형·그리기 호출 통계(플레이 중) |
| `phget.py <폴더> <해상도> <맵들> <에셋...>` | Poly Haven 파일 받기 (예: `phget.py out 1k Diffuse,nor_gl rock_face`) |
| `foliage_photo_atlas.py foliage_src out.png` | 사진 잎 아틀라스 합성(`FoliageAtlas`가 읽음) |

- execute_code는 `safety_checks`를 끄면 실패함 → `File.Delete`·`AssetDatabase.DeleteAsset` 막힘
- 빌드 중에는 Unity가 멈춰 MCP 연결이 끊김(정상). 스크립트가 다시 연결될 때까지 기다림
