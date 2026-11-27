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
# Sentry 릴리스 = 이번 커밋(계획 0087): 오류가 어느 배포에서 생겼는지. compose가 SENTRY_RELEASE로 넘김
export SCRUN_RELEASE="$(git rev-parse --short HEAD)"

$C build api
$C up -d db
# 마이그레이션은 따로, 실패하면 여기서 멈춤(지금 돌고 있는 API는 그대로 둠)
$C run --rm --no-deps api python -m alembic upgrade head
$C up -d --remove-orphans
# Caddyfile은 파일 하나를 끼워 넣은(bind mount) 것이라, git reset이 파일을 새로 만들면 컨테이너는 옛 파일을 계속 본다
# (caddy reload도 옛 파일을 읽음). 11/26 0073 첫 배포에서 이것 때문에 /v1/realtime·사진 인증이 몇 분 api로 가 끊김.
# → 컨테이너 안 파일이 서버 파일과 다르면 Caddy를 새로 만든다(몇 초 끊김). 같으면 아무것도 안 함
if ! $C exec -T caddy cat /etc/caddy/Caddyfile | cmp -s - Caddyfile; then
  echo "Caddyfile이 바뀜 → Caddy를 새로 만듦"
  $C up -d --force-recreate --no-deps caddy
  sleep 3
  $C exec -T caddy cat /etc/caddy/Caddyfile | cmp -s - Caddyfile || { echo "Caddy가 새 Caddyfile을 못 읽음: $C logs caddy" >&2; exit 1; }
fi
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
