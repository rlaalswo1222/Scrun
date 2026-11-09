#!/bin/bash
# 서버에서 최신 코드로 다시 올리기: ~/scrun/backend/deploy/update.sh [브랜치, 기본 develop]
# git reset이 실행 중인 이 파일을 바꿀 수 있으므로 전체를 { } 안에 두어 bash가 먼저 다 읽게 한다.
{
set -euo pipefail
cd "$(dirname "$0")"
BRANCH="${1:-develop}"
C="docker compose -f docker-compose.server.yml"

# DB 비밀번호는 DB 주소 URL에 그대로 들어간다 → URL에 안전한 글자만
PW=$(grep -E '^DB_PASSWORD=' .env | cut -d= -f2-)
if ! [[ "$PW" =~ ^[A-Za-z0-9_-]{24,}$ ]]; then
  echo "DB_PASSWORD는 영문·숫자·-·_로 24자 이상이어야 합니다(token_urlsafe 값)." >&2; exit 1
fi

git fetch -q origin "$BRANCH"
git checkout -q "$BRANCH"
git reset -q --hard "origin/$BRANCH"   # 서버에서는 코드를 고치지 않는다(.env는 git 밖이라 안 지워짐)

$C build api
$C up -d db
# 마이그레이션은 따로, 실패하면 여기서 멈춤(지금 돌고 있는 API는 그대로 둠)
$C run --rm --no-deps api python -m alembic upgrade head
$C up -d
docker image prune -f >/dev/null

# API가 건강해질 때까지 기다림(최대 60초)
for _ in $(seq 1 30); do
  [ "$($C ps --format '{{.Service}} {{.Health}}' | awk '$1=="api"{print $2}')" = "healthy" ] && break
  sleep 2
done
$C ps --format 'table {{.Service}}\t{{.Status}}'
[ "$($C ps --format '{{.Service}} {{.Health}}' | awk '$1=="api"{print $2}')" = "healthy" ] || { echo "API가 건강하지 않음: $C logs api" >&2; exit 1; }
exit 0
}
