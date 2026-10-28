"""속도 테스트 API (server.py에 붙는다). 화면: /speed.html (분석), /speed-galaxy.html (갤럭시 기록기)

  GET  /api/speed/clock                   -> {t}  기기 시계를 노트북 시계에 맞출 때
  POST /api/speed/runs                    {height_cm, stages, device} -> 새 기록 {id, t0}
  GET  /api/speed/runs                    -> 기록 목록
  GET  /api/speed/runs/{id|current}       -> 기록 정보
  POST /api/speed/runs/{id}/samples       {source, columns, rows: [[t_abs, ...]]}
  POST /api/speed/runs/{id}/stage         {index, label, speed_kmh, set, t}  새 구간 시작
  POST /api/speed/runs/{id}/finish        {t}
  GET  /api/speed/runs/{id}/live          -> 실시간 걸음 수·추정 속도
  GET  /api/speed/runs/{id}/report        -> 전체 분석 (?refresh=1 로 다시 계산)
  POST /api/speed/runs/{id}/phyphox       phyphox zip/csv 업로드 -> 시각 자동 맞춤 후 galaxy_phyphox로 추가
"""
import io
import json
import time

from fastapi import APIRouter, HTTPException, Request, UploadFile

from .live import Store, align_offset
from .loaders import load_track

router = APIRouter(prefix="/api/speed")
store = Store()


def run_or_404(run_id):
    run = store.get(run_id)
    if run is None:
        raise HTTPException(404, "기록이 없어요")
    return run


@router.get("/clock")
def clock():
    return {"t": time.time()}


@router.post("/runs")
def create_run(body: dict):
    run = store.create({"height_cm": body.get("height_cm"), "stages": body.get("stages", []),
                        "device": body.get("device", "")})
    return {"id": run.id, "t0": run.t0}


@router.get("/runs")
def list_runs():
    return store.list()


@router.get("/runs/{run_id}")
def run_info(run_id: str):
    run = run_or_404(run_id)
    return {**run.meta, "sources": sorted(run.sources)}


@router.post("/runs/{run_id}/samples")
async def add_samples(run_id: str, request: Request):
    run = run_or_404(run_id)
    body = await request.json()
    batches = body if isinstance(body, list) else [body]
    n = 0
    for b in batches:
        n += run.add_samples(str(b["source"]), b["columns"], b["rows"])
    return {"ok": True, "added": n}


@router.post("/runs/{run_id}/stage")
def stage(run_id: str, body: dict):
    run = run_or_404(run_id)
    run.stage(body.get("index"), body.get("label"), body.get("speed_kmh"), body.get("set"),
              float(body.get("t") or time.time()))
    return {"ok": True}


@router.post("/runs/{run_id}/finish")
def finish(run_id: str, body: dict):
    run = run_or_404(run_id)
    run.finish(float(body.get("t") or time.time()))
    return {"ok": True}


@router.get("/runs/{run_id}/live")
def live(run_id: str):
    return run_or_404(run_id).live()


@router.get("/runs/{run_id}/report")
def report(run_id: str, refresh: int = 0):
    run = run_or_404(run_id)
    f = run.dir / "report.json"
    if refresh or not f.exists():
        return run.report()
    return json.loads(f.read_text(encoding="utf-8"))


@router.post("/runs/{run_id}/phyphox")
async def phyphox(run_id: str, file: UploadFile):
    run = run_or_404(run_id)
    raw = await file.read()
    tmp = run.dir / ("phyphox_upload" + (".zip" if file.filename.lower().endswith(".zip") else ".csv"))
    tmp.write_bytes(raw)
    track = load_track(tmp)
    ref_name = next((n for n in ("iphone_acc", "galaxy_acc", "airpods_acc") if n in run.sources), None)
    if ref_name is None:
        raise HTTPException(400, "시각을 맞출 기준 센서(아이폰 진동)가 없어요")
    ref = run.sources[ref_name].frame()
    offset = align_offset(ref, track)
    rows = [[t + offset + run.t0, x, y, z] for t, x, y, z in track[["t", "x", "y", "z"]].itertuples(index=False)]
    old = run.dir / "galaxy_phyphox.csv"
    if old.exists():
        old.unlink()
    run.sources.pop("galaxy_phyphox", None)
    run.add_samples("galaxy_phyphox", ["x", "y", "z"], rows)
    run.cache = (0.0, None)
    return {"ok": True, "offset_s": round(offset, 2), "reference": ref_name, "samples": len(rows)}
