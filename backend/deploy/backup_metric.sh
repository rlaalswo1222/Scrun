#!/bin/bash
# 마지막 DB 백업 성공 시각(유닉스 초)을 Alloy가 읽는 텍스트 지표로(계획 0087 2단계). daily.sh·update.sh가 부름
#   ./backup_metric.sh 1764280200
set -euo pipefail
TS="${1:?유닉스 초}"
[[ "$TS" =~ ^[0-9]+$ ]] || { echo "유닉스 초(숫자)여야 함: $TS" >&2; exit 1; }
DIR="$HOME/scrun-metrics"
mkdir -p "$DIR"
TMP="$DIR/.backup.prom.tmp"
printf '# HELP scrun_backup_last_success_timestamp_seconds 마지막 DB 백업 성공 시각\n# TYPE scrun_backup_last_success_timestamp_seconds gauge\nscrun_backup_last_success_timestamp_seconds %s\n' "$TS" > "$TMP"
mv "$TMP" "$DIR/backup.prom" # 반쯤 쓴 파일을 Alloy가 읽지 않게 이름 바꾸기로 한 번에
