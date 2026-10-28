#!/bin/bash
# usage: ucs.sh file.cs   (C# method body; runs via MCP execute_code)
P=$(python -c "import json,sys;print(json.dumps({'name':'execute_code','arguments':{'action':'execute','code':open(sys.argv[1],encoding='utf-8').read(),'compiler':'codedom'}}))" "$1")
bash /c/Users/rlaal/Desktop/Capstone1/unity/tools/umcp.sh tools/call "$P" | python -c "
import sys,json
for line in sys.stdin:
  try: d=json.loads(line)
  except: print(line); continue
  r=d.get('result',d)
  for c in r.get('content',[]): print(c.get('text','')[:6000])
"
