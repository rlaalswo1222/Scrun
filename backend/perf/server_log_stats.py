"""요청 로그(app/core/request_log.py) 줄을 읽어 엔드포인트별 서버 안 시간을 요약한다(계획 0070).

  docker logs scrun-perf-api-1 --since … | python backend/perf/server_log_stats.py > server_stats.md
"""
import json
import statistics
import sys
from collections import defaultdict


def pct(xs: list[float], p: float) -> float:
    xs = sorted(xs)
    return xs[min(len(xs) - 1, int(len(xs) * p))]


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")   # Windows 기본(cp949)이면 표 한글이 깨짐
    by: dict[str, list[dict]] = defaultdict(list)
    for raw in sys.stdin:
        raw = raw.strip()
        if not raw.startswith("{") or '"path"' not in raw:
            continue
        try:
            line = json.loads(raw)
        except json.JSONDecodeError:
            continue
        by[f'{line["method"]} {line["path"]}'].append(line)
    print("| 엔드포인트 | 요청 | 서버 p50 ms | p95 | p99 | 최대 | 쿼리 수(중앙) | DB ms p95 | 풀 대기 ms p95 | 번호표 ms p95 | 503 | 그 밖 5xx |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|")
    for key in sorted(by, key=lambda k: -len(by[k])):
        rows = by[key]
        ms = [r["ms"] for r in rows]
        print(f'| `{key}` | {len(rows)} | {statistics.median(ms):.1f} | {pct(ms, .95):.1f} | {pct(ms, .99):.1f} | {max(ms):.1f} '
              f'| {statistics.median(r["db_n"] for r in rows):.0f} | {pct([r["db_ms"] for r in rows], .95):.1f} '
              f'| {pct([r.get("pool_ms", 0) for r in rows], .95):.1f} | {pct([r.get("queue_ms", 0) for r in rows], .95):.1f} '
              f'| {sum(r["status"] == 503 for r in rows)} | {sum(r["status"] >= 500 and r["status"] != 503 for r in rows)} |')


if __name__ == "__main__":
    main()
