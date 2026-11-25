#!/bin/bash
# 부하 시험 한 번(계획 0070): 노트북(Git Bash)에서 집 PC 시험 서버로. 결과는 data/perf/<날짜>/<이름>/
#   backend/perf/run.sh <이름> <k6 스크립트> [k6 -e 인자…]
#   예: backend/perf/run.sh s1_r1k_v20 backend/perf/k6/s1_open_app.js -e VUS=20 -e USERS=10 -e DURATION=2m
# 남기는 것: ping(무선 상태), k6 요약 JSON·화면 출력, 서버 요청 로그 원본·엔드포인트별 요약, docker stats, 느린 쿼리 10개, 조건 메모
set -euo pipefail
NAME="$1"; SCRIPT="$2"; shift 2
PC="${PC:-admin@192.168.0.2}"; KEY="${KEY:-$HOME/.ssh/scrun_pc}"
BASE="${BASE:-http://192.168.0.2:8000}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/data/perf/$(date +%Y%m%d)/$NAME"; mkdir -p "$OUT"
PERF='cd /d C:\Users\admin\scrun\backend\perf && docker compose -f docker-compose.perf.yml --env-file .env.perf'
FLAG='C:\Users\admin\scrun-perf-stop.flag'
pc() { ssh -i "$KEY" -o LogLevel=ERROR "$PC" "$@" 2>&1 | grep -v -E "post-quantum|store now|upgraded" || true; }
psql_pc() { pc "$PERF exec -T db psql -U scrun -d scrun -At -c \"$1\""; }

{
  echo "이름: $NAME"; echo "시각: $(date -Iseconds)"; echo "스크립트: $SCRIPT $*"
  echo "서버 커밋: $(pc 'cd /d C:\Users\admin\scrun && git log --oneline -1')"
  echo "데이터: $(psql_pc "select (select count(*) from users)||' users, '||(select count(*) from runs)||' runs, '||(select count(*) from run_samples)||' samples, '||pg_size_pretty(pg_database_size('scrun'))")"
} > "$OUT/meta.txt"
ping -n 50 192.168.0.2 > "$OUT/ping_before.txt" 2>&1 || true

psql_pc "select pg_stat_statements_reset()" > /dev/null
SINCE="$(pc 'powershell -NoProfile -Command "(Get-Date).ToUniversalTime().ToString(\"yyyy-MM-ddTHH:mm:ssZ\")"' | tr -d '\r')"

# 시험 동안 docker stats(2초쯤 간격). k6가 끝나면 표시 파일을 만들어 멈춤(PC의 다른 프로세스는 건드리지 않음)
pc "del $FLAG 2>nul"
pc "powershell -NoProfile -Command \"while (-not (Test-Path '$FLAG')) { \$t = Get-Date -Format HH:mm:ss; docker stats --no-stream --format (\$t + ',{{.Name}},{{.CPUPerc}},{{.MemUsage}}') }\"" > "$OUT/docker_stats.csv" &
STATS=$!

K6="$(command -v k6 || echo '/c/Program Files/k6/k6.exe')"
"$K6" run -q -e BASE="$BASE" -e OUT="$OUT/k6_summary.json" "$@" "$SCRIPT" 2>&1 | tee "$OUT/k6_stdout.txt" || true
pc "type nul > $FLAG"
wait $STATS 2>/dev/null || true
pc "del $FLAG 2>nul"

pc "docker logs scrun-perf-api-1 --since $SINCE" > "$OUT/server_log.txt"
python "$ROOT/backend/perf/server_log_stats.py" < "$OUT/server_log.txt" > "$OUT/server_stats.md"
psql_pc "select round(total_exec_time::numeric,1)||' ms | '||calls||' calls | '||round(mean_exec_time::numeric,2)||' ms avg | '||left(regexp_replace(query, '\s+', ' ', 'g'), 160) from pg_stat_statements where query not like '%pg_stat_statements%' order by total_exec_time desc limit 10" > "$OUT/slow_queries.txt"
echo "== 서버 안 시간 ($OUT/server_stats.md)"; cat "$OUT/server_stats.md"
