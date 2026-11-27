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

# 지표·대시보드(계획 0087 2단계) 비밀이 .env에 없으면 서버에서 무작위로 만들어 넣음(화면에 안 찍음). compose가 ${..:?}로 검사해서
# 없으면 build부터 멈추고 매일 백업(daily.sh의 compose exec)까지 멈추므로 compose를 부르기 전에
for KEY in GRAFANA_ADMIN_PASSWORD ALERT_WEBHOOK_TOKEN; do
  if ! grep -qE "^$KEY=.+" .env; then
    sed -i "/^$KEY=\$/d" .env
    echo "$KEY=$(python3 -c 'import secrets; print(secrets.token_urlsafe(36))')" >> .env
    echo "$KEY를 .env에 새로 만듦(값 확인: grep $KEY .env)"
  fi
done

OLD=$(git rev-parse -q --verify HEAD || true)   # 이번 배포 전 커밋: 관측 설정이 바뀌었는지 볼 때(아래)
git fetch -q origin "$BRANCH"
git checkout -q "$BRANCH"
git reset -q --hard "origin/$BRANCH"   # 서버에서는 코드를 고치지 않는다(.env는 git 밖이라 안 지워짐)
# Sentry 릴리스 = 이번 커밋(계획 0087): 오류가 어느 배포에서 생겼는지. compose가 SENTRY_RELEASE로 넘김
export SCRUN_RELEASE="$(git rev-parse --short HEAD)"
# 지표 텍스트 파일 폴더(계획 0087 2단계): Alloy가 읽고 daily.sh가 마지막 백업 시각을 씀. 처음이면 가장 최근 백업 파일 시각으로 채움
# (안 채우면 첫 daily.sh 전까지 "백업 26시간 넘게 없음" 알람이 울림)
mkdir -p "$HOME/scrun-metrics"
# Docker가 먼저 만들면 root 소유가 되어 daily.sh가 못 씀(처음 배포가 옛 update.sh로 돌 때) → 내 소유로
[ -O "$HOME/scrun-metrics" ] || sudo -n chown "$(id -u):$(id -g)" "$HOME/scrun-metrics"
if [ ! -f "$HOME/scrun-metrics/backup.prom" ]; then
  LAST=$(ls -1t "$HOME"/scrun-backups/scrun-*.dump 2>/dev/null | head -1 || true)
  if [ -n "$LAST" ]; then
    ./backup_metric.sh "$(stat -c %Y "$LAST")"
  fi
fi

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
# 관측 설정(계획 0087)도 바뀌면 다시 읽게. config.alloy·prometheus.yml·loki.yaml은 파일 하나를 끼워 넣어 위 Caddy와 같은 문제
# → 이번 배포에서 바뀌었으면 새로 만듦. Grafana는 데이터 원본·알람 provisioning을 켜질 때만 읽음 → 바뀌었으면 다시 시작
# (11/28 3단계 첫 배포에서 Grafana가 새 Loki 원본을 몰라 로그 화면이 오류). 컨테이너 안쪽만이라 사용자 영향 없음
changed() { [ -z "$OLD" ] || ! git diff --quiet "$OLD" HEAD -- "$@"; }
for PAIR in "alloy observability/config.alloy" "prometheus observability/prometheus.yml" "loki observability/loki.yaml"; do
  read -r SVC FILE <<< "$PAIR"
  if changed "$FILE"; then
    echo "$FILE이 바뀜 → $SVC를 새로 만듦"
    $C up -d --force-recreate --no-deps "$SVC"
  fi
done
if changed observability/grafana; then
  echo "Grafana 설정이 바뀜 → 다시 시작"
  $C restart grafana
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
