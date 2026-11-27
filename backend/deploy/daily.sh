#!/bin/bash
# 매일 한 번(cron): DB 백업 7개 보관 + 만료된 refresh 토큰 정리.
# 서버 시계는 UTC → 한국 시간 4시 30분 = UTC 19시 30분:
#   30 19 * * * /home/ubuntu/scrun/backend/deploy/daily.sh >> /home/ubuntu/scrun-daily.log 2>&1
set -euo pipefail
umask 077 # 백업에는 사용자 정보가 들어 있다 → 이 계정만 읽기
cd "$(dirname "$0")"
DIR="$HOME/scrun-backups"
mkdir -p "$DIR" && chmod 700 "$DIR"
STAMP=$(date +%Y%m%d-%H%M)
TMP="$DIR/.scrun-$STAMP.dump.tmp"
trap 'rm -f "$TMP"' EXIT
docker compose -f docker-compose.server.yml exec -T db pg_dump -U scrun -d scrun -Fc > "$TMP"
[ -s "$TMP" ] || { echo "백업 파일이 비었음" >&2; exit 1; }
mv "$TMP" "$DIR/scrun-$STAMP.dump" # 끝까지 성공한 것만 이름을 붙임
./backup_metric.sh "$(date +%s)" # 마지막 백업 시각 → Grafana(26시간 넘게 없으면 알람, 계획 0087). 토큰 정리가 실패해도 백업은 성공으로
ls -1t "$DIR"/scrun-*.dump | tail -n +8 | xargs -r rm -f # 최신 7개만
# PROMETHEUS_MULTIPROC_DIR를 비움: 짧게 도는 이 프로세스가 지표 파일(tmpfs)을 남기지 않게(계획 0087 리뷰)
docker compose -f docker-compose.server.yml exec -T -e PROMETHEUS_MULTIPROC_DIR= api python -m scripts.purge_refresh_tokens
echo "$(date -Iseconds) 백업 $DIR/scrun-$STAMP.dump"
