#!/bin/bash
# 플레이 중지 → 컴파일 → 섬 빌드 → 끝날 때까지 대기 → 오류 출력 → 플레이 시작
S="$(cd "$(dirname "$0")" && (pwd -W 2>/dev/null || pwd))"  # 이 폴더(Windows는 C:/... 형식)
U="$S/umcp.sh"
PY="$(command -v python || command -v python3)"  # 맥은 python3뿐
bash "$U" tools/call '{"name":"manage_editor","arguments":{"action":"stop"}}' >/dev/null
"$PY" -c "import time;time.sleep(3)"
bash "$U" tools/call '{"name":"read_console","arguments":{"action":"clear"}}' >/dev/null
bash "$U" tools/call '{"name":"refresh_unity","arguments":{"mode":"force","compile":"request","wait_for_ready":true}}' >/dev/null
for i in $(seq 1 20); do "$PY" -c "import time;time.sleep(5)"; rm -f "${TEMP:-/tmp}/umcp.sid"; bash "$S/ucs.sh" "$S/t_ping.cs" 2>/dev/null | grep -q 'compiling=False' && break; done
E=$(bash "$S/ucon.sh" 20 error); echo "$E" | grep -q '"data":\[\]' || { echo "COMPILE ERRORS: $E"; exit 1; }
bash "$S/umenu.sh" "${1:-Scrun/Build Island Scene}" >/dev/null
for i in $(seq 1 40); do "$PY" -c "import time;time.sleep(10)"; rm -f "${TEMP:-/tmp}/umcp.sid"; bash "$S/ucs.sh" "$S/t_ping.cs" 2>/dev/null | grep -q '"success":true' && break; done
bash "$S/ucon.sh" 60 error,warning,log | "$PY" -c "
import sys,json
t=sys.stdin.read()
try:
  d=json.loads(t)
  for x in d['data']:
    if 'MCP-FOR-UNITY' in x: continue
    print(x[:260])
except Exception as e: print(t[:2000])"
bash "$U" tools/call '{"name":"manage_editor","arguments":{"action":"play"}}' >/dev/null
"$PY" -c "import time;time.sleep(20)"
echo done
