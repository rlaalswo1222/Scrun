#!/bin/bash
# 서버에서 최신 코드로 다시 올리기: ~/scrun/backend/deploy/update.sh [브랜치, 기본 develop]
set -euo pipefail
cd "$(dirname "$0")"
BRANCH="${1:-develop}"
git fetch origin "$BRANCH"
git checkout -q "$BRANCH"
git reset -q --hard "origin/$BRANCH"   # 서버에서는 코드를 고치지 않는다(.env는 git 밖이라 안 지워짐)
docker compose -f docker-compose.server.yml up -d --build
docker image prune -f >/dev/null
docker compose -f docker-compose.server.yml ps
