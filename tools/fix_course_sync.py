"""영상 진행 막대 거리표(course.json의 sync)가 잠깐 멈췄다가 따라잡는 곳을 고르게 편다.

막대가 멈춘 동안(예: 25:00~25:34) 촬영자는 실제로 계속 달리고 있어서, 그대로 쓰면
  - 촬영 속도가 시속 1km 아래로 계산돼 영상을 10배 넘게 빨리 돌리려다 버퍼링에 걸리고
  - 3D 복원의 미터 단위(smooth_scale)도 틀어진다.
멈춘 곳 앞뒤 창 안에서 속도를 주변 보통 속도의 60% 아래로 떨어지지 않게 올린 뒤,
창 전체 거리는 원래와 같게 맞춘다 (창 밖 거리·시각은 그대로).

  python tools/fix_course_sync.py o0EmQXATQQI
"""
import json
import shutil
import sys
from pathlib import Path

import numpy as np

VIDEO_DIR = Path(__file__).resolve().parent.parent / "run3d" / "assets" / "video"


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    vid = sys.argv[1]
    path = VIDEO_DIR / f"{vid}.course.json"
    backup = path.with_suffix(".json.orig")
    if not backup.exists():
        shutil.copy(path, backup)
    c = json.loads(backup.read_text(encoding="utf-8"))  # 항상 원본에서 다시 계산
    t = np.array(c["sync"]["t"], float)
    s = np.array(c["sync"]["s"], float)
    tt = np.arange(t[0], t[-1] + 1e-6, 1.0)
    ss = np.interp(tt, t, s)
    v = np.gradient(ss, tt)
    moving = v > 0.5
    med = np.array([np.median(v[max(0, i - 60):i + 60][moving[max(0, i - 60):i + 60]]) if moving[max(0, i - 60):i + 60].any()
                    else 0 for i in range(len(v))])
    slow = (v < 0.45 * med) & (tt > tt[0] + 60)  # 인트로(처음 정지)는 그대로
    fixed = ss.copy()
    i = 0
    n_fix = 0
    while i < len(slow):
        if not slow[i]:
            i += 1
            continue
        j = i
        while j + 1 < len(slow) and (slow[j + 1] or (j + 3 < len(slow) and slow[j + 3])):
            j += 1
        if j - i < 4:
            i = j + 1
            continue
        a = max(0, i - 10)
        # 멈춘 뒤 막대가 '보통 속도로 달렸으면 갔을 거리'를 다시 따라잡는 곳까지를 한 창으로, 그 사이는 일정한 속도
        b = None
        # (따라잡는 동안은 보통보다 빠르므로, 빠른 구간이 끝난 뒤여야 한다)
        for k in range(j + 1, min(len(tt) - 10, j + 300)):
            if ss[k] - ss[a] >= 0.97 * med[a] * (tt[k] - tt[a]) and v[k:k + 10].mean() < 1.25 * med[a]:
                b = k
                break
        if b is None:
            b = min(len(tt) - 1, j + 60)
        seg_v = np.full(b - a, (ss[b] - ss[a]) / (tt[b] - tt[a]))
        fixed[a + 1:b + 1] = ss[a] + np.cumsum(seg_v) * (tt[1] - tt[0])
        print(f"폄: {int(tt[i]) // 60}:{int(tt[i]) % 60:02d}~{int(tt[j]) // 60}:{int(tt[j]) % 60:02d} "
              f"(창 {int(tt[a]) // 60}:{int(tt[a]) % 60:02d}~{int(tt[b]) // 60}:{int(tt[b]) % 60:02d}, "
              f"속도 {v[i:j + 1].mean():.1f} -> {seg_v[i - a:j - a + 1].mean():.1f} m/s)")
        n_fix += 1
        i = b + 1
    c["sync"] = {"t": [round(x, 2) for x in tt], "s": [round(x, 2) for x in fixed]}
    path.write_text(json.dumps(c, ensure_ascii=False), encoding="utf-8")
    print(f"{n_fix}곳 폄 -> {path.name} (원본: {backup.name})")


if __name__ == "__main__":
    main()
