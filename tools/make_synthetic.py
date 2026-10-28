"""분석 코드 동작 확인용 가짜 데이터 생성 (실제 러닝머신 데이터가 생기기 전 테스트용).

거치대 위 폰의 가속도를 흉내 낸다: 착지 충격 + 상하 흔들림 + 모터 진동 + 잡음.
정답 모델: 보폭(m) = 0.30 + 0.065 × 속도(km/h)  →  빨라질수록 보폭이 늘어남

  python tools/make_synthetic.py            -> data/synthetic/accel.csv, segments.csv
"""
import argparse
import sys
from pathlib import Path

import numpy as np
import pandas as pd

PLAN = [("calib", v) for v in (8, 9, 10, 11, 12)] + [("test", v) for v in (9.5, 11.5, 10.5)]
SEG_S = 60.0   # 속도 단계별 시간
RAMP_S = 5.0   # 러닝머신이 목표 속도까지 가속하는 시간


def true_step_m(v_kmh):
    return 0.30 + 0.065 * v_kmh


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--out", default="data/synthetic")
    p.add_argument("--fs", type=float, default=200.0)
    p.add_argument("--seed", type=int, default=0)
    args = p.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    rng = np.random.default_rng(args.seed)
    fs = args.fs

    t = np.arange(0, SEG_S * len(PLAN), 1 / fs)
    speeds = np.array([v for _, v in PLAN])
    idx = np.minimum((t // SEG_S).astype(int), len(PLAN) - 1)
    prev = np.r_[speeds[0], speeds[:-1]][idx]
    frac = np.clip((t - idx * SEG_S) / RAMP_S, 0, 1)
    v = prev + (speeds[idx] - prev) * frac

    # 걸음 빈도(걸음/초)에 2% 흔들림을 주고 적분해서 착지 시점을 만든다
    wobble = np.convolve(rng.normal(0, 1, len(t)), np.ones(int(fs)) / np.sqrt(fs), "same")
    step_hz = v / 3.6 / true_step_m(v) * (1 + 0.02 * wobble)
    phase = np.cumsum(step_hz) / fs
    steps = np.flatnonzero(np.diff(np.floor(phase)) > 0) + 1

    impulses = np.zeros(len(t))
    left_right = np.where(np.arange(len(steps)) % 2 == 0, 1.1, 0.9)  # 좌우 착지 세기 차이
    impulses[steps] = (1.0 + 0.15 * (v[steps] - 8)) * left_right * rng.normal(1, 0.05, len(steps))
    k = np.arange(0, 0.15, 1 / fs)
    kernel = np.exp(-k / 0.03) * np.sin(2 * np.pi * 18 * k)  # 거치대가 18Hz로 울림
    vib = np.convolve(impulses, kernel)[: len(t)]
    bob = 0.3 * (1 + 0.05 * (v - 8)) * np.sin(2 * np.pi * phase)

    direction = np.array([0.2, 0.9, 0.4]) / np.linalg.norm([0.2, 0.9, 0.4])
    gravity = np.array([0.0, 9.81 * np.cos(0.3), 9.81 * np.sin(0.3)])
    acc = gravity + (vib + bob)[:, None] * direction + rng.normal(0, 0.15, (len(t), 3))
    acc[:, 0] += 0.1 * np.sin(2 * np.pi * 33 * t)  # 모터 진동
    t_obs = t + np.clip(rng.normal(0, 0.1 / fs, len(t)), -0.3 / fs, 0.3 / fs)

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    pd.DataFrame({
        "Time (s)": t_obs,
        "Acceleration x (m/s^2)": acc[:, 0],
        "Acceleration y (m/s^2)": acc[:, 1],
        "Acceleration z (m/s^2)": acc[:, 2],
        "Absolute acceleration (m/s^2)": np.linalg.norm(acc, axis=1),
    }).to_csv(out / "accel.csv", index=False)

    rows = []
    for i, (set_name, speed) in enumerate(PLAN):
        a, b = i * SEG_S, (i + 1) * SEG_S
        stable = (t >= a + RAMP_S) & (t < b)
        # 영상에서 30초 걸음 수를 세서 ×2 한 것처럼 짝수 정수로 만든다
        counted = round(step_hz[stable].mean() * 30) * 2
        rows.append({"start_s": a, "end_s": b, "speed_kmh": speed, "set": set_name,
                     "true_cadence_spm": counted})
    pd.DataFrame(rows).to_csv(out / "segments.csv", index=False)
    print(f"저장: {out / 'accel.csv'} ({len(t)}샘플, {fs:g}Hz), {out / 'segments.csv'}")


if __name__ == "__main__":
    main()
