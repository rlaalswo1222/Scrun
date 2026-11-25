"""부하 시험 결과 그림(계획 0070): data/perf/<날짜>/<이름>/의 원본으로 다시 그린다(같은 원본 → 같은 그림).

  .venv/Scripts/python backend/perf/plot.py data/perf/20261125 docs/perf/img

그림 두 장(서버 안 시간 = 요청 로그, 무선 왕복 빠짐):
  1. s1_by_data.png: 데이터 크기(기록 1천 → 100만)별 엔드포인트 p95, 가상 사용자 20명
  2. s1_by_vus.png: 기록 100만에서 가상 사용자 수별 초당 처리 수와 p95(전체 요청)
이름 규칙: s1_r{1k,10k,100k,1m}_v{N}[_b|_c] (backend/perf/run.sh 첫 인자)
"""
import gzip
import json
import re
import sys
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager

for name in ("Pretendard", "Malgun Gothic", "AppleSDGothicNeo"):
    if any(name.lower() in f.name.lower() for f in font_manager.fontManager.ttflist):
        plt.rcParams["font.family"] = name
        break
plt.rcParams["axes.unicode_minus"] = False

DATA_ORDER = ["1k", "10k", "100k", "1m"]
DATA_LABEL = {"1k": "1천", "10k": "1만", "100k": "10만", "1m": "100만"}
ENDPOINTS = ["/v1/home", "/v1/feed", "/v1/runs", "/v1/friends", "/v1/users/me/progress", "/v1/friends/running"]
COLORS = ["#2a6fdb", "#e8590c", "#2b8a3e", "#ae3ec9", "#868e96", "#f59f00"]


def server_lines(run_dir: Path) -> list[dict]:
    out = []
    gz = run_dir / "server_log.txt.gz"   # run.sh가 요약 뒤 압축함
    text = (gzip.open(gz, "rt", encoding="utf-8", errors="replace").read() if gz.exists()
            else (run_dir / "server_log.txt").read_text(encoding="utf-8", errors="replace"))
    for raw in text.splitlines():
        raw = raw.strip()
        if raw.startswith("{") and '"path"' in raw:
            try:
                out.append(json.loads(raw))
            except json.JSONDecodeError:
                pass
    return out


def p95(xs: list[float]) -> float:
    xs = sorted(xs)
    return xs[min(len(xs) - 1, int(len(xs) * 0.95))] if xs else float("nan")


def by_data(root: Path, out: Path) -> None:
    runs = {}
    for d in root.iterdir():
        m = re.fullmatch(r"s1_r(1k|10k|100k|1m)_v20", d.name)
        if m and any((d / n).exists() for n in ("server_log.txt", "server_log.txt.gz")):
            runs[m.group(1)] = server_lines(d)
    sizes = [s for s in DATA_ORDER if s in runs]
    if not sizes:
        return
    fig, ax = plt.subplots(figsize=(8, 4.5), dpi=150)
    for ep, color in zip(ENDPOINTS, COLORS):
        ys = [p95([r["ms"] for r in runs[s] if r["path"] == ep]) for s in sizes]
        ax.plot([DATA_LABEL[s] for s in sizes], ys, marker="o", label=ep.replace("/v1", ""), color=color)
    ax.set_xlabel("기록 수(사용자 수 = 기록 / 100)")
    ax.set_ylabel("서버 안 처리 시간 p95 (ms)")
    ax.set_title("S1 앱 켜기 — 데이터가 늘 때 (가상 사용자 20명)")
    ax.grid(alpha=0.3)
    ax.legend(fontsize=8, ncol=2)
    fig.tight_layout()
    fig.savefig(out / "s1_by_data.png")


def by_vus(root: Path, out: Path) -> None:
    points = []
    for d in root.iterdir():
        m = re.fullmatch(r"s1_r1m_v(\d+)", d.name)
        if m and (d / "k6_summary.json").exists():
            summary = json.loads((d / "k6_summary.json").read_text(encoding="utf-8"))
            lines = server_lines(d)
            points.append((int(m.group(1)), summary["metrics"]["http_reqs"]["values"]["rate"],
                           p95([r["ms"] for r in lines if r["path"] != "-"]),
                           summary["metrics"]["http_req_duration"]["values"]["p(95)"]))
    if not points:
        return
    points.sort()
    vus, rps, server_p95, client_p95 = zip(*points)
    fig, ax1 = plt.subplots(figsize=(8, 4.5), dpi=150)
    ax1.plot(vus, rps, marker="o", color="#2a6fdb", label="초당 처리 수")
    ax1.set_xlabel("동시 가상 사용자")
    ax1.set_ylabel("초당 처리 수 (요청/초)", color="#2a6fdb")
    ax2 = ax1.twinx()
    ax2.plot(vus, server_p95, marker="s", color="#e8590c", label="서버 안 p95")
    ax2.plot(vus, client_p95, marker="^", color="#e8590c", linestyle="--", alpha=0.6, label="노트북에서 잰 p95(무선 포함)")
    ax2.set_ylabel("p95 (ms, 로그 눈금)", color="#e8590c")
    ax2.set_yscale("log")   # 포화 뒤 p95가 수십 초로 뛰어 선형 눈금이면 앞쪽이 안 보임
    ax1.set_title("S1 앱 켜기 — 가상 사용자를 늘릴 때 (기록 100만)")
    ax1.grid(alpha=0.3)
    handles = ax1.get_legend_handles_labels()[0] + ax2.get_legend_handles_labels()[0]
    ax1.legend(handles, [h.get_label() for h in handles], fontsize=8, loc="upper left")
    fig.tight_layout()
    fig.savefig(out / "s1_by_vus.png")


def main() -> None:
    root, out = Path(sys.argv[1]), Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    by_data(root, out)
    by_vus(root, out)
    print("그림:", ", ".join(p.name for p in sorted(out.glob("s1_*.png"))))


if __name__ == "__main__":
    main()
