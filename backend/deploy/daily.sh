#!/bin/bash
# 매일 한 번(cron): DB 백업 7일치 보관 + 만료된 refresh 토큰 정리.
# crontab: 30 4 * * * /home/ubuntu/scrun/backend/deploy/daily.sh >> /home/ubuntu/scrun-daily.log 2>&1
set -euo pipefail
cd "$(dirname "$0")"
DIR="$HOME/scrun-backups"
mkdir -p "$DIR"
STAMP=$(date +%Y%m%d-%H%M)
docker compose -f docker-compose.server.yml exec -T db pg_dump -U scrun -d scrun -Fc > "$DIR/scrun-$STAMP.dump"
find "$DIR" -name 'scrun-*.dump' -mtime +7 -delete
docker compose -f docker-compose.server.yml exec -T api python -m scripts.purge_refresh_tokens
echo "$(date -Iseconds) 백업 $DIR/scrun-$STAMP.dump"
