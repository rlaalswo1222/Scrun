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
$C up -d --remove-orphans
# Caddyfile은 파일을 끼워 넣은 것이라 바뀌어도 컨테이너가 다시 안 만들어짐 → 늘 다시 읽힘(같으면 아무 일 없음).
# 빠뜨리면 경로 나눔(계획 0073)이 옛 설정으로 남아 /v1/realtime이 api로 가 실시간이 끊긴다(0071·0073 리뷰)
for i in 1 2 3 4 5; do   # 막 새로 뜬 Caddy는 관리 API가 늦게 열릴 수 있음
  $C exec -T caddy caddy reload --config /etc/caddy/Caddyfile && break
  [ "$i" = 5 ] && { echo "Caddy 설정을 다시 읽지 못함: $C logs caddy" >&2; exit 1; }
  sleep 2
done
docker image prune -f >/dev/null

# 앱 서비스 셋(api·realtime·ocr, 계획 0073)이 모두 건강해질 때까지 기다림(최대 90초, ocr은 모델을 올리느라 조금 늦음)
unhealthy() {
  for s in api realtime ocr; do
    [ "$($C ps --format '{{.Service}} {{.Health}}' | awk -v s="$s" '$1==s{print $2}')" = "healthy" ] || echo "$s"
  done
}
for _ in $(seq 1 45); do
  [ -z "$(unhealthy)" ] && break
  sleep 2
done
$C ps --format 'table {{.Service}}\t{{.Status}}'
bad="$(unhealthy | tr '\n' ' ')"
[ -z "$bad" ] || { echo "건강하지 않은 서비스: $bad— $C logs <서비스>" >&2; exit 1; }
exit 0
}
