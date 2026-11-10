#!/bin/bash
# usage: ucs.sh file.cs   (C# method body; runs via MCP execute_code)
S="$(cd "$(dirname "$0")" && (pwd -W 2>/dev/null || pwd))"  # 이 폴더(Windows는 C:/... 형식)
U="$S/umcp.sh"
PY="$(command -v python || command -v python3)"  # 맥은 python3뿐
P=$("$PY" -c "import json,sys;print(json.dumps({'name':'execute_code','arguments':{'action':'execute','code':open(sys.argv[1],encoding='utf-8').read(),'compiler':'codedom'}}))" "$1")
bash "$U" tools/call "$P" | "$PY" -c "
import sys,json
for line in sys.stdin:
  try: d=json.loads(line)
  except: print(line); continue
  r=d.get('result',d)
  for c in r.get('content',[]): print(c.get('text','')[:6000])
"
