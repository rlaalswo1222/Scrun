"""가짜 아이폰·에어팟 데이터를 실시간으로 보내서 분석 화면(/speed.html)을 미리 확인한다.

  .venv\\Scripts\\python tools\\speed_simulate.py               # 구간마다 20초, 빠르게
  .venv\\Scripts\\python tools\\speed_simulate.py --seconds 60 --server http://localhost:8000

걸음 수 = 150 + 3.2 × 속도 로 가정한 사인파 + 잡음 (실제 결론은 러닝머신 데이터로).
"""
import argparse
import json
import time
import urllib.request

import numpy as np


def call(server, path, body=None):
    req = urllib.request.Request(server + path, data=None if body is None else json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json"}, method="GET" if body is None else "POST")
    with urllib.request.urlopen(req, timeout=5) as r:
        return json.loads(r.read() or b"{}")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--server", default="http://localhost:8000")
    p.add_argument("--seconds", type=float, default=20)
    a = p.parse_args()
    calib, test = [8, 9, 10, 11, 12], [9.5, 11.5]
    stages = ([{"label": "제자리 점프 3번", "speed_kmh": None, "set": "sync", "duration_s": 5}]
              + [{"label": f"보정 {v}", "speed_kmh": v, "set": "calib", "duration_s": a.seconds} for v in calib]
              + [{"label": f"평가 {v}", "speed_kmh": v, "set": "test", "duration_s": a.seconds} for v in test])
    run = call(a.server, "/api/speed/runs", {"height_cm": 175, "stages": stages, "device": "simulator"})
    rid = run["id"]
    print(f"기록 {rid}: {a.server}/speed.html 에서 보세요")
    rng = np.random.default_rng(0)
    phase = 0.0
    for i, st in enumerate(stages):
        call(a.server, f"/api/speed/runs/{rid}/stage", {**st, "index": i, "t": time.time()})
        v = st["speed_kmh"] or 0
        f = (150 + 3.2 * v) / 60 if v else 0.0
        end = time.time() + st["duration_s"]
        last = time.time()
        while time.time() < end:
            time.sleep(0.5)
            now = time.time()
            t100 = np.arange(last, now, 0.01)
            t25 = np.arange(last, now, 0.04)
            ph = lambda t: phase + 2 * np.pi * f * (t - last)  # noqa: E731
            acc = [[t, 0.3 * np.sin(ph(t)), 9.8 + 2 * np.sin(ph(t) + .3), 0.3 * rng.standard_normal()] for t in t100]
            head = [[t, 0.1 * np.sin(ph(t)), 0.6 * np.sin(ph(t)) + 0.15 * rng.standard_normal(), 0.05 * rng.standard_normal()]
                    for t in t25]
            phase = ph(now)
            last = now
            call(a.server, f"/api/speed/runs/{rid}/samples",
                 [{"source": "iphone_acc", "columns": ["x", "y", "z"], "rows": acc},
                  {"source": "airpods_acc", "columns": ["x", "y", "z"], "rows": head}])
        print(f"  {st['label']} 끝")
    call(a.server, f"/api/speed/runs/{rid}/finish", {"t": time.time()})
    print("끝. 분석 화면에 전체 결과가 나와요.")


if __name__ == "__main__":
    main()
