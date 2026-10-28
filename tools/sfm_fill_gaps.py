"""3D 복원 구멍 메우기: 정규 구간 계산이 다 끝나면, 합친 결과에서 앞길이 끊긴 곳을 찾아
그 자리를 중심으로 다시 자른 짧은 구간(메움 조각)을 계산하고 다시 합친다. 한 번에 2개씩.

  python tools/sfm_fill_gaps.py o0EmQXATQQI
"""
import json
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PY = str(ROOT / ".venv" / "Scripts" / "python.exe")
BASE = ["tools/sfm_video.py", None, "--start", "120", "--end", "1860", "--fps", "4", "--chunk", "60", "--overlap", "20"]
PARALLEL = 2


def run(*extra, capture=False):
    cmd = [PY] + [a if a is not None else VIDEO for a in BASE] + list(extra)
    return subprocess.run(cmd, cwd=ROOT, capture_output=capture, text=True, encoding="utf-8")


def workers_running():
    out = subprocess.run(["powershell", "-NoProfile", "-Command",
                          "@(Get-CimInstance Win32_Process -Filter \"Name='python.exe'\" | Where-Object "
                          "{ $_.CommandLine -like '*sfm_video.py*--worker*' }).Count"],
                         capture_output=True, text=True).stdout.strip()
    return int(out or 0) > 0


def main():
    global VIDEO
    VIDEO = sys.argv[1]
    sys.stdout.reconfigure(encoding="utf-8")
    while workers_running():  # 정규 구간 계산이 먼저 (동시에 2개까지만)
        time.sleep(60)
    for rnd in range(2):  # 메운 뒤에도 남은 구멍은 창을 조금 밀어서 한 번 더
        run("--assemble")
        gaps = json.loads(run("--find-gaps", capture=True).stdout.strip().splitlines()[-1])
        if not gaps:
            break
        if rnd:
            gaps = [[a + 8, d] for a, d in gaps]
        print(f"{time.strftime('%H:%M')} {rnd + 1}차 메우기: {gaps}", flush=True)
        procs = []
        for a, d in gaps:
            while len([p for p in procs if p.poll() is None]) >= PARALLEL:
                time.sleep(15)
            procs.append(subprocess.Popen([PY] + [x if x is not None else VIDEO for x in BASE] + ["--patch", str(a), str(d)],
                                          cwd=ROOT))
        for p in procs:
            p.wait()
    run("--assemble")
    gaps = json.loads(run("--find-gaps", capture=True).stdout.strip().splitlines()[-1])
    print(f"{time.strftime('%H:%M')} 메우기 끝. 남은 구멍: {gaps or '없음'}", flush=True)


if __name__ == "__main__":
    main()
