#!/bin/bash
# usage: [ROUTE=거목] [PRESET=1] [WAIT=3] shoot.sh <region|dist> <arg> <outname>
S="C:/Users/rlaal/Desktop/Capstone1/unity/tools"
sed -e "s/__MODE__/$1/" -e "s/__ARG__/$2/" -e "s/__ROUTE__/${ROUTE:-}/" -e "s/__PRESET__/${PRESET:--1}/" "$S/shoot_tp.cs.tmpl" > "$S/_tp.cs"; bash "$S/ucs.sh" "$S/_tp.cs"
python -c "import time;time.sleep(${WAIT:-3})"
sed -e "s|__OUT__|$S/cap/$3.png|" "$S/shoot_cap.cs.tmpl" > "$S/_cap.cs"; bash "$S/ucs.sh" "$S/_cap.cs"
