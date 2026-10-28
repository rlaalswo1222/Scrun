"""러닝머신 속도 추정 실험 분석 도구.

  python analyze.py timeline <기록파일> [구간파일]            시간에 따른 걸음 수 그래프 (구간 파일 작성용)
  python analyze.py evaluate <기록파일> <구간파일> --height 175  보정 방식별 속도 오차 비교
  python analyze.py camera   <영상파일>                      영상에서 머리 움직임 추출 -> CSV

기록파일: phyphox 내보내기(zip 또는 csv), 또는 camera 명령으로 만든 csv
"""
import argparse
import sys
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import pandas as pd  # noqa: E402

from speedlab import dsp  # noqa: E402
from speedlab.loaders import load_segments, load_track  # noqa: E402
from speedlab.models import all_models, evaluate, summarize  # noqa: E402

pd.set_option("display.width", 200)
pd.set_option("display.max_columns", 20)
plt.rcParams["font.family"] = ["Malgun Gothic", "DejaVu Sans"]  # 윈도우 한글 글꼴
plt.rcParams["axes.unicode_minus"] = False
sys.stdout.reconfigure(encoding="utf-8")  # Git Bash 등에서 한글 깨짐 방지
sys.stderr.reconfigure(encoding="utf-8")


def out_dir(args):
    path = Path(args.out) if args.out else Path("results") / Path(args.track).stem
    path.mkdir(parents=True, exist_ok=True)
    return path


def load_shifted_segments(args):
    segs = load_segments(args.segments)
    segs[["start_s", "end_s"]] += args.offset
    return segs


def plot_timeline(ax, tl, segs=None):
    ax.plot(tl.t, tl.cadence_spm, lw=1, color="black")
    ax.set_xlabel("time (s)")
    ax.set_ylabel("cadence (steps/min)")
    if segs is None:
        return
    top = ax.get_ylim()[1]
    for _, sg in segs.iterrows():
        color = "tab:blue" if sg.set == "calib" else "tab:orange"
        ax.axvspan(sg.start_s, sg.end_s, color=color, alpha=0.15)
        ax.text((sg.start_s + sg.end_s) / 2, top, f"{sg.speed_kmh:g}",
                ha="center", va="top", fontsize=8)


def cmd_timeline(args):
    t, s, fs = dsp.prepare(load_track(args.track), args.fs)
    tl = dsp.cadence_timeline(t, s, fs)
    segs = load_shifted_segments(args) if args.segments else None
    out = out_dir(args)
    tl.to_csv(out / "timeline.csv", index=False)

    fig, (ax1, ax2) = plt.subplots(2, 1, figsize=(12, 6), sharex=True,
                                   gridspec_kw={"height_ratios": [3, 1]})
    plot_timeline(ax1, tl, segs)
    ax1.set_xlabel("")
    ax2.plot(tl.t, tl.quality, lw=1, color="gray")
    ax2.set_ylabel("quality")
    ax2.set_xlabel("time (s)")
    ax2.set_ylim(0, 1)
    fig.suptitle(f"{Path(args.track).name}  ({fs:.0f} Hz)")
    fig.tight_layout()
    fig.savefig(out / "timeline.png", dpi=120)
    print(f"샘플링 {fs:.0f}Hz, 길이 {t[-1] - t[0]:.0f}초")
    print(f"저장: {out / 'timeline.png'}, {out / 'timeline.csv'}")


def cmd_evaluate(args):
    t, s, fs = dsp.prepare(load_track(args.track), args.fs)
    segs = load_shifted_segments(args)
    feats = dsp.segment_features(t, s, fs, segs, args.trim)
    models = all_models(args.height)
    preds, mode = evaluate(feats, models)
    summary = summarize(preds, models)
    out = out_dir(args)

    n_calib, n_test = (feats.set == "calib").sum(), (feats.set == "test").sum()
    print(f"샘플링 {fs:.0f}Hz | 구간 {len(feats)}개 (calib {n_calib}, test {n_test}) | "
          f"앞부분 {args.trim:g}초 제외 | 평가: {mode}\n")
    print("[구간별 특징]")
    print(feats[["start_s", "end_s", "speed_kmh", "set", "cadence_spm", "quality",
                 "amp_rms", "impact_p95"]].round(3).to_string(index=False))
    if "true_cadence_spm" in feats and feats.true_cadence_spm.notna().any():
        cad_err = (feats.cadence_spm - feats.true_cadence_spm).dropna()
        print(f"\n[케이던스 정확도] 정답 대비 평균 {cad_err.abs().mean():.1f} spm, "
              f"최대 {cad_err.abs().max():.1f} spm 차이")
    print("\n[구간별 속도 예측 (km/h)]")
    print(preds[["speed_kmh", "cadence_spm"] + [m.name for m in models]]
          .round(2).to_string(index=False))
    print("\n[요약: 오차율이 낮을수록 좋음]")
    print(summary.round(2).to_string(index=False))

    feats.to_csv(out / "features.csv", index=False)
    preds.to_csv(out / "predictions.csv", index=False)
    summary.to_csv(out / "summary.csv", index=False, encoding="utf-8-sig")
    plot_evaluate(out / "evaluate.png", dsp.cadence_timeline(t, s, fs), segs, preds,
                  summary, models, title=f"{Path(args.track).name}  ({fs:.0f} Hz, {mode})")
    print(f"\n저장: {out}")


def plot_evaluate(path, tl, segs, preds, summary, models, title):
    fig, axes = plt.subplot_mosaic([["tl", "tl"], ["sc", "bar"]], figsize=(12, 8))
    plot_timeline(axes["tl"], tl, segs)
    ax = axes["sc"]
    lo, hi = preds.speed_kmh.min() - 1, preds.speed_kmh.max() + 1
    ax.plot([lo, hi], [lo, hi], color="gray", lw=1, ls="--")
    for m in models:
        ax.scatter(preds.speed_kmh, preds[m.name], label=m.name, s=30)
    ax.set_xlabel("treadmill speed (km/h)")
    ax.set_ylabel("estimated speed (km/h)")
    ax.legend(fontsize=8)
    ax = axes["bar"]
    ax.bar(summary.model, summary["MAPE_%"], color="tab:blue")
    ax.set_ylabel("MAPE (%)")
    ax.tick_params(axis="x", labelrotation=15)
    fig.suptitle(title)
    fig.tight_layout()
    fig.savefig(path, dpi=120)


def cmd_camera(args):
    from speedlab.camera import extract_head_track

    track = extract_head_track(args.video, args.model)
    out = Path(args.out) if args.out else Path(args.video).with_name(
        Path(args.video).stem + "_camera.csv")
    track.to_csv(out, index=False)
    detected = track.nose_y.notna().mean() * 100
    print(f"저장: {out} ({len(track)}프레임, 사람 인식 {detected:.0f}%)")


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)

    def common(sp):
        sp.add_argument("--fs", type=float, help="이 샘플링 주파수(Hz)로 낮춰서 분석 (예: 25)")
        sp.add_argument("--offset", type=float, default=0.0,
                        help="구간 시간에 더할 초. 영상이 센서 기록보다 3초 늦게 시작했으면 -3")
        sp.add_argument("--out", help="결과 폴더 (기본: results/<기록파일 이름>)")

    sp = sub.add_parser("timeline", help="시간에 따른 걸음 수 그래프")
    sp.add_argument("track")
    sp.add_argument("segments", nargs="?")
    common(sp)
    sp.set_defaults(func=cmd_timeline)

    sp = sub.add_parser("evaluate", help="보정 방식별 속도 오차 비교")
    sp.add_argument("track")
    sp.add_argument("segments")
    sp.add_argument("--height", type=float, required=True, help="키(cm)")
    sp.add_argument("--trim", type=float, default=10.0, help="속도 바꾼 직후 버릴 초 (기본 10)")
    common(sp)
    sp.set_defaults(func=cmd_evaluate)

    sp = sub.add_parser("camera", help="영상에서 머리 움직임 추출")
    sp.add_argument("video")
    sp.add_argument("--model", choices=["lite", "full", "heavy"], default="full",
                    help="포즈 모델 크기 (lite가 가장 빠름)")
    sp.add_argument("--out", help="저장할 csv 경로 (기본: <영상이름>_camera.csv)")
    sp.set_defaults(func=cmd_camera)

    args = p.parse_args()
    try:
        args.func(args)
    except ValueError as e:
        sys.exit(f"오류: {e}")


if __name__ == "__main__":
    main()
