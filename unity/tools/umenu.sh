#!/bin/bash
S="$(cd "$(dirname "$0")" && (pwd -W 2>/dev/null || pwd))"  # 이 폴더(Windows는 C:/... 형식)
U="$S/umcp.sh"
PY="$(command -v python || command -v python3)"  # 맥은 python3뿐
P=$("$PY" -c "import json,sys;print(json.dumps({'name':'execute_menu_item','arguments':{'menu_path':sys.argv[1]}}))" "$1")
bash "$U" tools/call "$P" | head -c 2000; echo
