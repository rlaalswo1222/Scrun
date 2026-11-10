#!/bin/bash
# 빠른 식생 메쉬 갱신: 플레이 중지 → 컴파일 → Rebuild Vegetation Meshes → 씬 저장 → 플레이
S="$(cd "$(dirname "$0")" && (pwd -W 2>/dev/null || pwd))"  # 이 폴더(Windows는 C:/... 형식)
U="$S/umcp.sh"
PY="$(command -v python || command -v python3)"  # 맥은 python3뿐
T0=$(date +%s)
bash "$U" tools/call '{"name":"manage_editor","arguments":{"action":"stop"}}' >/dev/null
"$PY" -c "import time;time.sleep(2)"
bash "$U" tools/call '{"name":"read_console","arguments":{"action":"clear"}}' >/dev/null
bash "$U" tools/call '{"name":"refresh_unity","arguments":{"mode":"force","compile":"request","wait_for_ready":true}}' >/dev/null
for i in $(seq 1 30); do "$PY" -c "import time;time.sleep(3)"; rm -f "${TEMP:-/tmp}/umcp.sid"; bash "$S/ucs.sh" "$S/t_ping.cs" 2>/dev/null | grep -q 'compiling=False' && break; done
E=$(bash "$S/ucon.sh" 20 error); echo "$E" | grep -q '"data":\[\]' || { echo "ERRORS: $E"; exit 1; }
bash "$S/umenu.sh" "Scrun/Rebuild Vegetation Meshes (fast)" >/dev/null
for i in $(seq 1 30); do "$PY" -c "import time;time.sleep(3)"; rm -f "${TEMP:-/tmp}/umcp.sid"; bash "$S/ucs.sh" "$S/t_ping.cs" 2>/dev/null | grep -q '"success":true' && break; done
bash "$S/ucon.sh" 10 error,log | grep -o '\[Scrun\][^"]*\|Exception[^"]*'
echo "rebuild $(( $(date +%s) - T0 ))s"
bash "$U" tools/call '{"name":"manage_editor","arguments":{"action":"play"}}' >/dev/null
"$PY" -c "import time;time.sleep(18)"
