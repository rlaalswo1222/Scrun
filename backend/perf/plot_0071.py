"""계획 0071 전후 그림: 기준선(번호표 없음, data/perf/20261125/s1b_*) vs 번호표 12장(data/perf/20261126/a12_*).

  .venv/Scripts/python backend/perf/plot_0071.py data/perf docs/perf/img

원본만 읽어 그린다(같은 원본 → 같은 그림). 서버 p95는 요청 로그(무선 빠짐), 처리량·실패율은 k6 요약, 회복은 recovery.txt.
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

BEFORE, AFTER = "#868e96", "#2a6fdb"


def k6(d: Path) -> dict:
    m = json.loads((d / "k6_summary.json").read_text(encoding="utf-8"))["metrics"]
    return {"rps": m["http_reqs"]["values"]["rate"], "fail": m["http_req_failed"]["values"]["rate"] * 100}


def server_p95(d: Path) -> float:
    with gzip.open(d / "server_log.txt.gz", "rt", encoding="utf-8", errors="replace") as f:
        ms = sorted(json.loads(l)["ms"] for l in f if l.startswith("{") and '"path"' in l and '"path":"-"' not in l)
    return ms[int(len(ms) * 0.95)]


def recovery_s(d: Path) -> float:
    m = re.search(r"회복까지 (\d+)초", (d / "recovery.txt").read_text(encoding="utf-8"))
    return float(m.group(1))


def median(xs):
    xs = sorted(xs)
    return xs[len(xs) // 2]


def main() -> None:
    root, out = Path(sys.argv[1]), Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    b100 = [root / "20261125" / f"s1b_r1m_v100_{i}" for i in (1, 2, 3)]
    a100 = [root / "20261126" / f"a12_s1_v100_{i}" for i in (1, 2, 3)]
    b200 = [root / "20261125" / "s1b_r1m_v200"]
    a200 = [root / "20261126" / f"a12_s1_v200_{i}" for i in (1, 2)]

    panels = [
        ("200명: 실패율 (%)", [k6(d)["fail"] for d in b200], [k6(d)["fail"] for d in a200], "{:.1f}"),
        ("200명: 처리량 (요청/초)", [k6(d)["rps"] for d in b200], [k6(d)["rps"] for d in a200], "{:.0f}"),
        ("200명 뒤 회복 시간 (초)", [recovery_s(d) for d in b200], [recovery_s(d) for d in a200], "{:.0f}"),
        ("100명: 서버 p95 (ms)", [server_p95(d) for d in b100], [server_p95(d) for d in a100], "{:.0f}"),
    ]
    fig, axes = plt.subplots(1, 4, figsize=(12, 3.8), dpi=150)
    for ax, (title, before, after, fmt) in zip(axes, panels):
        vals = [median(before), median(after)]
        bars = ax.bar(["번호표 없음", "번호표 12장"], vals, color=[BEFORE, AFTER], width=0.6)
        for b, v in zip(bars, vals):
            ax.text(b.get_x() + b.get_width() / 2, v, fmt.format(v), ha="center", va="bottom", fontsize=10)
        ax.set_title(title, fontsize=11)
        ax.set_ylim(0, max(vals) * 1.2 or 1)
        ax.spines[["top", "right"]].set_visible(False)
        ax.tick_params(axis="x", labelsize=9)
    fig.suptitle("계획 0071 동시 처리 제한 전후 (집 PC 시험 서버, 가짜 데이터 기록 100만, S1 5분, 여러 번이면 가운데 값)", fontsize=11)
    fig.tight_layout()
    fig.savefig(out / "0071_before_after.png")
    for title, before, after, fmt in panels:
        print(title, "전", [fmt.format(v) for v in before], "후", [fmt.format(v) for v in after])


if __name__ == "__main__":
    main()
