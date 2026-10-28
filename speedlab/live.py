"""실시간 속도 테스트: 기록(run)마다 여러 센서의 샘플을 받아 쌓고, 실시간 걸음 수와 끝난 뒤 전체 분석을 만든다.

센서(source) 이름 예: iphone_acc, iphone_gyro, airpods_acc, airpods_gyro, galaxy_acc, galaxy_cam
모든 기기는 노트북(서버) 시계로 맞춘 시각을 보낸다 (GET /api/speed/clock로 차이를 잰다).
기록 폴더: data/runs/<id>/  meta.json, <source>.csv, segments.csv, report.json
"""
import bisect
import json
import threading
import time
from pathlib import Path

import numpy as np
import pandas as pd

from . import dsp
from .models import HeightOnly, all_models, evaluate, summarize, step_length_m, speed_kmh

RUNS = Path(__file__).resolve().parent.parent / "data" / "runs"
WIN_S, FAST_WIN_S = 8.0, 4.0  # 실시간 걸음 수 창 (안정 / 빠른 반응)
TRIM_S = 10.0  # 속도를 바꾼 직후 버리는 시간 (러닝머신 가속)
TIMELINE_S = 180.0  # 실시간 그래프에 보여줄 최근 시간
LABELS = {
    "airpods_acc": "에어팟 가속도", "airpods_gyro": "에어팟 회전",
    "iphone_acc": "아이폰 진동", "iphone_gyro": "아이폰 회전",
    "galaxy_acc": "갤럭시 진동", "galaxy_cam": "갤럭시 카메라(머리)",
    "galaxy_phyphox": "갤럭시 phyphox",
}
RAW_ONLY = {"airpods_grav"}  # 저장만 하고 걸음 분석은 안 하는 센서 (공중 시간 계산용, 계획 0008)


# 러닝머신 거치대에 세워 두는 폰 진동: 걸음 = 발이 벨트를 치는 충격 (dsp.prepare mode="impact")
IMPACT = {"galaxy_acc", "iphone_acc", "galaxy_phyphox"}


def mode_of(source):
    return "impact" if source in IMPACT else "motion"


WALK_KMH = 6.5  # 이 속도 이하로 기록된 구간은 걷기 보폭으로 따로 본다
_BORROW = {}


def step_of(cal, cad):
    """보정 결과로 걸음 수 -> 보폭(m). 걷기 보폭이 있고 걸음 수가 걷기 쪽에 가까우면 걷기 보폭"""
    w = cal.get("walk")
    if w and cad < (w["cad"] + cal.get("run_min_cad", w["cad"] + 30)) / 2:
        return w["step"]
    step = cal["a"] + cal["b"] * cad
    return max(step, w["step"]) if w else step  # 직선을 아래로 늘려 써도 걷기 보폭보다 짧아지지는 않게


def borrowed_calibration(source, exclude):
    """같은 센서로 보정 구간을 3개 이상 기록한 가장 최근(끝난) 기록의 보폭 모델. 없으면 None"""
    for p in sorted(RUNS.iterdir(), reverse=True):
        f = p / "meta.json"
        if p.name == exclude or not f.exists() or not (p / f"{source}.csv").exists():
            continue
        key = (p.name, source, f.stat().st_mtime)
        if key not in _BORROW:
            m = json.loads(f.read_text(encoding="utf-8"))
            n = sum(1 for g in m.get("segments", []) if g.get("set") == "calib" and g.get("end_s") is not None)
            cal = None
            if m.get("finished") and n >= 3:
                run = Run(p.name, m, readonly=True)
                cal = run.calibration(run.sources[source], borrow=False)
                if cal:
                    cal["from_run"] = p.name
            _BORROW[key] = cal
        if _BORROW[key]:
            return _BORROW[key]
    return None


def trim_for(duration):
    """구간 앞부분에서 버릴 시간: 보통 10초, 짧은 구간(확인용)은 길이의 30%"""
    return min(TRIM_S, 0.3 * duration)


def label_of(source):
    return LABELS.get(source, source)


def plateaus(tl, tol=4.0, min_s=10.0):
    """걸음 수가 한동안 일정했던 구간들 (보정 없는 기록에서 박자별로 나눠 보려고). tl: t, cadence_spm (신호 좋은 줄만)"""
    out, cur = [], []
    def close():
        if cur and cur[-1][0] - cur[0][0] >= min_s:
            c = np.array([x[1] for x in cur])
            out.append({"start_s": round(cur[0][0], 1), "end_s": round(cur[-1][0], 1),
                        "cadence": round(float(np.median(c)), 1), "spread": round(float(np.ptp(c)), 1)})
    for t, c in zip(tl.t.to_numpy(), tl.cadence_spm.to_numpy()):
        if cur and (t - cur[-1][0] > 2.5 or abs(c - np.median([x[1] for x in cur[-8:]])) > tol):
            close()
            cur = []
        cur.append((float(t), float(c)))
    close()
    return out


class Source:
    def __init__(self, name, columns, path):
        self.name, self.columns, self.path = name, list(columns), path
        self.t, self.rows = [], []
        self.lock = threading.Lock()
        self.fresh = not path.exists()

    def add(self, rows):
        """rows: [[t_run, v1, v2, ...], ...] (기록 시작부터 초). 시간 순서가 아니어도 된다"""
        rows = [r for r in rows if len(r) == len(self.columns) + 1 and all(np.isfinite(r))]
        if not rows:
            return 0
        rows.sort(key=lambda r: r[0])
        with self.lock:
            if self.t and rows[0][0] < self.t[-1]:  # 늦게 도착한 묶음: 제자리에 끼워 넣는다
                for r in rows:
                    k = bisect.bisect(self.t, r[0])
                    self.t.insert(k, r[0])
                    self.rows.insert(k, r)
            else:
                self.t.extend(r[0] for r in rows)
                self.rows.extend(rows)
            with open(self.path, "a", encoding="utf-8") as f:
                if self.fresh:
                    f.write(",".join(["t"] + self.columns) + "\n")
                    self.fresh = False
                f.writelines(",".join(f"{v:.5f}" if i == 0 else f"{v:.6g}" for i, v in enumerate(r)) + "\n"
                             for r in rows)
        return len(rows)

    def window(self, t0, t1):
        with self.lock:
            a = bisect.bisect_left(self.t, t0)
            b = bisect.bisect_right(self.t, t1)
            return np.array(self.rows[a:b], float) if b > a else np.zeros((0, len(self.columns) + 1))

    def frame(self):
        with self.lock:
            arr = np.array(self.rows, float)
        return pd.DataFrame(arr, columns=["t"] + self.columns)

    @property
    def last_t(self):
        return self.t[-1] if self.t else None

    def rate(self, span=5.0):
        with self.lock:
            if len(self.t) < 3:
                return 0.0
            a = bisect.bisect_left(self.t, self.t[-1] - span)
            n, dt = len(self.t) - a, self.t[-1] - self.t[a]
        return (n - 1) / dt if dt > 0 else 0.0


def cadence_of(arr, mode="motion"):
    """샘플 묶음(t, 값...) -> (걸음 수 spm, 신뢰도). 데이터가 모자라면 (None, 0)"""
    if len(arr) < 16:
        return None, 0.0
    track = pd.DataFrame(arr, columns=["t"] + [f"v{i}" for i in range(arr.shape[1] - 1)])
    try:
        t, s, fs = dsp.prepare(track, mode=mode)
        if len(s) < fs * 3:
            return None, 0.0
        cad, q = dsp.cadence_acf(s, fs)
        return float(cad), float(max(0.0, q))
    except Exception:
        return None, 0.0


class Run:
    def __init__(self, run_id, meta=None, readonly=False):
        self.id = run_id
        self.dir = RUNS / run_id
        self.dir.mkdir(parents=True, exist_ok=True)
        self.meta_path = self.dir / "meta.json"
        if meta is None:
            meta = json.loads(self.meta_path.read_text(encoding="utf-8"))
        self.meta = meta
        self.sources = {}
        self.lock = threading.Lock()
        self.cache = (0.0, None)
        for f in self.dir.glob("*.csv"):
            if f.stem == "segments":
                continue
            df = pd.read_csv(f)
            src = Source(f.stem, df.columns[1:], f)
            src.t = df["t"].tolist()
            src.rows = df.to_numpy(float).tolist()
            self.sources[f.stem] = src
        if not readonly:
            self.save()

    # ---- 기록 정보 ----
    @property
    def t0(self):
        return self.meta["t0"]

    def now(self):
        return time.time() - self.t0

    def save(self):
        tmp = self.meta_path.with_suffix(".json.tmp")
        tmp.write_text(json.dumps(self.meta, ensure_ascii=False, indent=1), encoding="utf-8")
        tmp.replace(self.meta_path)
        segs = [s for s in self.meta.get("segments", []) if s.get("end_s") is not None and s["set"] in ("calib", "test")]
        pd.DataFrame(segs, columns=["start_s", "end_s", "speed_kmh", "set"]).to_csv(self.dir / "segments.csv", index=False)

    def add_samples(self, source, columns, rows_abs):
        with self.lock:
            src = self.sources.get(source)
            if src is None:
                src = self.sources[source] = Source(source, columns, self.dir / f"{source}.csv")
        rows = [[r[0] - self.t0] + list(r[1:]) for r in rows_abs]
        return src.add(rows)

    def stage(self, index, label, speed, set_, t_abs):
        """새 구간 시작: 앞 구간을 닫고 새 구간을 연다"""
        t = t_abs - self.t0
        segs = self.meta.setdefault("segments", [])
        if segs and segs[-1].get("end_s") is None:
            segs[-1]["end_s"] = round(t, 3)
        if label is not None:
            segs.append({"index": index, "label": label, "speed_kmh": speed, "set": set_,
                         "start_s": round(t, 3), "end_s": None})
        self.save()

    def finish(self, t_abs):
        self.stage(None, None, None, None, t_abs)
        self.meta["finished"] = time.strftime("%Y-%m-%d %H:%M:%S")
        self.meta["end_s"] = round(t_abs - self.t0, 3)
        self.save()
        threading.Thread(target=self.report, daemon=True).start()

    # ---- 분석 ----
    def segment_cadence(self, src, seg):
        if seg["end_s"] is None:
            return None, 0.0
        a, b = seg["start_s"] + trim_for(seg["end_s"] - seg["start_s"]), seg["end_s"]
        if b - a < 8:
            return None, 0.0
        return cadence_of(src.window(a, b), mode_of(src.name))

    def calibration(self, src, borrow=True):
        """끝난 보정 구간들로 보폭 모델: 3개 이상이면 보폭 = a + b×걸음 수, 1~2개면 평균 보폭.
        속도를 아는 걷기 구간(6.5km/h 이하)이 있으면 걷기 보폭도 따로 둔다 (걷기는 달리기 직선에서 벗어남).
        이 기록에 보정 구간이 없으면 같은 센서로 보정한 가장 최근 기록의 것을 빌려 온다 (borrow)."""
        pts, walk = [], []
        for seg in self.meta.get("segments", []):
            if seg.get("end_s") is None or not seg.get("speed_kmh"):
                continue
            is_walk = seg["speed_kmh"] <= WALK_KMH
            if seg["set"] != "calib" and not is_walk:
                continue
            cad, q = self.segment_cadence(src, seg)
            if cad and q > 0.3:
                (walk if is_walk else pts).append((cad, step_length_m(seg["speed_kmh"], cad)))
        if not pts:
            return borrowed_calibration(src.name, self.id) if borrow else None
        cads, steps = np.array(pts).T
        if len(pts) >= 3 and np.ptp(cads) > 3:
            b, a = np.polyfit(cads, steps, 1)
            cal = {"kind": "multi", "a": float(a), "b": float(b), "n": len(pts)}
        else:
            cal = {"kind": "single", "a": float(steps.mean()), "b": 0.0, "n": len(pts)}
        cal["run_min_cad"] = float(cads.min())
        if walk:
            wc, ws = np.array(walk).T
            cal["walk"] = {"cad": float(wc.mean()), "step": float(ws.mean())}
        return cal

    def live(self):
        now = time.time()
        ts, cached = self.cache
        if cached is not None and now - ts < (30 if self.meta.get("finished") else 0.4):
            return cached
        t_now = self.now() if not self.meta.get("finished") else self.meta.get("end_s", self.now())
        segs = self.meta.get("segments", [])
        cur = segs[-1] if segs and segs[-1].get("end_s") is None else None
        out = {"id": self.id, "now_s": round(t_now, 2), "finished": bool(self.meta.get("finished")),
               "height_cm": self.meta.get("height_cm"), "stages": self.meta.get("stages", []),
               "segments": segs, "current": cur, "sources": {}}
        for name, src in sorted(self.sources.items()):
            last = src.last_t
            if last is None or name in RAW_ONLY:
                continue
            mode = mode_of(name)
            cad, q = cadence_of(src.window(last - WIN_S, last), mode)
            cad_f, q_f = cadence_of(src.window(last - FAST_WIN_S, last), mode)
            cal = self.calibration(src)
            if cal and cad:
                est, est_kind = speed_kmh(cad, step_of(cal, cad)), ("prev" if cal.get("from_run") else "calib")
            elif cad:  # 보정 전: 키로만 잡은 보폭으로 대략값 (오차가 큼)
                est, est_kind = speed_kmh(cad, HeightOnly(self.meta.get("height_cm") or 170).step), "height"
            else:
                est, est_kind = None, None
            # 최근 그래프: 2초마다 8초 창 걸음 수
            tl = []
            span = last if self.meta.get("finished") else TIMELINE_S  # 끝난 기록은 처음부터 끝까지
            for tt in np.arange(max(0.0, last - span), last + 1e-6, 2.0):
                c, qq = cadence_of(src.window(tt - WIN_S, tt), mode)
                if c:
                    tl.append([round(float(tt), 1), round(c, 1), round(qq, 2)])
            out["sources"][name] = {
                "label": label_of(name), "rate_hz": round(src.rate(), 1), "lag_s": round(t_now - last, 2),
                "cadence": cad and round(cad, 1), "quality": round(q, 2),
                "cadence_fast": cad_f and round(cad_f, 1), "quality_fast": round(q_f, 2),
                "est_kmh": est and round(float(est), 2), "est_kind": est_kind, "calib": cal, "timeline": tl}
        self.cache = (now, out)
        return out

    def report(self):
        """끝난 뒤 전체 분석: 센서마다 구간별 걸음 수·신뢰도, 보정 방식별 오차율 (analyze.py evaluate와 같은 계산)"""
        segs = pd.DataFrame([s for s in self.meta.get("segments", [])
                             if s.get("end_s") is not None and s["set"] in ("calib", "test")])
        height = float(self.meta.get("height_cm") or 170)
        out = {"id": self.id, "made": time.strftime("%Y-%m-%d %H:%M:%S"), "height_cm": height, "sources": {}}
        for name, src in sorted(self.sources.items()):
            if name in RAW_ONLY:
                continue
            res = {"label": label_of(name)}
            try:
                t, s, fs = dsp.prepare(src.frame(), mode=mode_of(name))
                res["fs"] = round(fs, 1)
                tl = dsp.cadence_timeline(t, s, fs)
                res["timeline"] = tl.iloc[::2].round(2).values.tolist()
                if segs.empty or (segs.set == "calib").sum() < 3:
                    good = tl[tl.quality > 0.35]
                    cal = self.calibration(src)
                    hstep = HeightOnly(height).step
                    kmh_of = (lambda c: speed_kmh(c, step_of(cal, c))) if cal else (lambda c: speed_kmh(c, hstep))
                    if len(good):
                        cad = float(good.cadence_spm.median())
                        res["free"] = {"median": round(cad, 1), "kmh": round(float(kmh_of(cad)), 2),
                                       "calib_from": cal and cal.get("from_run"),
                                       "plateaus": [dict(p, kmh=round(float(kmh_of(p["cadence"])), 2))
                                                    for p in plateaus(good)]}
                        res["note"] = (f"{cal['from_run']} 기록의 보정(보폭)으로 계산했어요" if cal
                                       else "보정 구간이 없어 키 기준 대략 속도만 계산했어요")
                    else:
                        res["note"] = "걸음 리듬을 잡지 못했어요"
                    out["sources"][name] = res
                    continue
                trim = trim_for(float((segs.end_s - segs.start_s).min()))
                feats = dsp.segment_features(t, s, fs, segs, trim)
                models = all_models(height)
                preds, mode = evaluate(feats, models)
                summ = summarize(preds, models)
                res.update({
                    "mode": mode,
                    "features": feats[["start_s", "end_s", "speed_kmh", "set", "cadence_spm", "quality"]]
                    .round(3).to_dict("records"),
                    "predictions": preds[["speed_kmh", "set", "cadence_spm"] + [m.name for m in models]]
                    .round(2).to_dict("records"),
                    "summary": summ.round(2).to_dict("records"),
                })
            except Exception as e:  # noqa: BLE001
                res["error"] = f"{type(e).__name__}: {e}"
            out["sources"][name] = res
        (self.dir / "report.json").write_text(json.dumps(out, ensure_ascii=False), encoding="utf-8")
        return out


class Store:
    def __init__(self):
        RUNS.mkdir(parents=True, exist_ok=True)
        self.runs = {}
        self.current = None
        self.lock = threading.Lock()

    def create(self, meta):
        run_id = time.strftime("%Y%m%d-%H%M%S")
        meta = {**meta, "id": run_id, "t0": time.time(), "created": time.strftime("%Y-%m-%d %H:%M:%S"), "segments": []}
        run = Run(run_id, meta)
        with self.lock:
            self.runs[run_id] = run
            self.current = run_id
        return run

    def get(self, run_id):
        if run_id == "current":
            run_id = self.current or self.latest()
            if run_id is None:
                return None
        with self.lock:
            if run_id in self.runs:
                return self.runs[run_id]
        if not (RUNS / run_id / "meta.json").exists():
            return None
        run = Run(run_id)
        with self.lock:
            self.runs[run_id] = run
        return run

    def latest(self):
        ids = sorted(p.name for p in RUNS.iterdir() if (p / "meta.json").exists())
        return ids[-1] if ids else None

    def list(self):
        out = []
        for p in sorted(RUNS.iterdir(), reverse=True):
            f = p / "meta.json"
            if f.exists():
                m = json.loads(f.read_text(encoding="utf-8"))
                out.append({"id": m["id"], "created": m.get("created"), "finished": m.get("finished"),
                            "sources": sorted(x.stem for x in p.glob("*.csv") if x.stem != "segments"),
                            "segments": len([s for s in m.get("segments", []) if s.get("set") in ("calib", "test")])})
        return out


def align_offset(ref, other, fs=25.0, max_lag=180.0):
    """두 기록의 시각 차이(초): 진동 세기(가속도 크기 변화)를 서로 밀어 가며 가장 잘 겹치는 곳.
    ref, other: DataFrame(t, x, y, z). 반환: other 시각에 더하면 ref 시각이 되는 값"""
    def envelope(df):
        t = df["t"].to_numpy(float)
        mag = np.linalg.norm(df.iloc[:, 1:4].to_numpy(float), axis=1)
        tt = np.arange(t[0], t[-1], 1 / fs)
        m = np.interp(tt, t, mag)
        m = np.abs(m - pd.Series(m).rolling(int(fs), center=True, min_periods=1).mean().to_numpy())
        e = pd.Series(m).rolling(int(fs), center=True, min_periods=1).mean().to_numpy()
        return tt, (e - e.mean()) / (e.std() or 1)
    ta, ea = envelope(ref)
    tb, eb = envelope(other)
    full = np.correlate(ea, eb, mode="full")
    lags = np.arange(-len(eb) + 1, len(ea))
    k = int(max_lag * fs)
    ok = np.abs(lags + int(round((ta[0] - tb[0]) * fs))) <= k
    best = lags[ok][np.argmax(full[ok])]
    return float(ta[0] - tb[0] + best / fs)
