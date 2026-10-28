#!/bin/bash
P=$(python -c "import json,sys;print(json.dumps({'name':'execute_menu_item','arguments':{'menu_path':sys.argv[1]}}))" "$1")
bash /c/Users/rlaal/Desktop/Capstone1/unity/tools/umcp.sh tools/call "$P" | head -c 2000; echo
