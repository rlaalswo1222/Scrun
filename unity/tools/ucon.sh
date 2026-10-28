#!/bin/bash
# usage: ucon.sh [count] [types]  -> recent console
P=$(python -c "import json,sys;print(json.dumps({'name':'read_console','arguments':{'action':'get','count':sys.argv[1],'types':sys.argv[2].split(','),'format':'plain'}}))" "${1:-20}" "${2:-error,warning,log}")
bash /c/Users/rlaal/Desktop/Capstone1/unity/tools/umcp.sh tools/call "$P" | python -c "
import sys,json
for line in sys.stdin:
  try: d=json.loads(line)
  except: continue
  for c in d.get('result',{}).get('content',[]): print(c.get('text','')[:8000])
"
