#!/bin/bash
# usage: ucon.sh [count] [types]  -> recent console
S="$(cd "$(dirname "$0")" && (pwd -W 2>/dev/null || pwd))"  # 이 폴더(Windows는 C:/... 형식)
U="$S/umcp.sh"
PY="$(command -v python || command -v python3)"  # 맥은 python3뿐
P=$("$PY" -c "import json,sys;print(json.dumps({'name':'read_console','arguments':{'action':'get','count':sys.argv[1],'types':sys.argv[2].split(','),'format':'plain'}}))" "${1:-20}" "${2:-error,warning,log}")
bash "$U" tools/call "$P" | "$PY" -c "
import sys,json
for line in sys.stdin:
  try: d=json.loads(line)
  except: continue
  for c in d.get('result',{}).get('content',[]): print(c.get('text','')[:8000])
"
