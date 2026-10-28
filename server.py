"""코스 만들기 서버 + 3D 화면 파일 제공.

  .venv\\Scripts\\python server.py          # http://localhost:8000 (같은 와이파이의 폰: http://<노트북 IP>:8000)

API
  POST /api/courses          {lat, lon, length_m, mode, name?} -> {id, status}  (백그라운드에서 생성)
  GET  /api/courses/{id}     -> {id, status: pending|ready|error, message}
  GET  /api/courses          -> 만들어 둔 코스 목록
코스 파일: run3d/data/courses/{id}.json (3D 화면: /index.html?course={id})
"""
import hashlib
import json
import threading
import time
import traceback
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import uvicorn
from fastapi import FastAPI, HTTPException
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field

from coursegen.build import build_course

ROOT = Path(__file__).resolve().parent
WEB = ROOT / "run3d"
COURSES = WEB / "data" / "courses"
COURSES.mkdir(parents=True, exist_ok=True)

app = FastAPI(title="같이 달리기 코스 서버")
# 공용 OSM 서버에 부담을 주지 않도록 한 번에 하나씩만 만든다
executor = ThreadPoolExecutor(max_workers=1)
jobs = {}  # id -> {"status", "message"}
lock = threading.Lock()


class CourseRequest(BaseModel):
    lat: float = Field(ge=33, le=39)  # 대한민국 범위
    lon: float = Field(ge=124, le=132)
    length_m: int = Field(ge=1000, le=10000)
    mode: str = Field(pattern="^(loop|out_and_back)$")
    name: str | None = Field(default=None, max_length=30)


def course_id(req):
    # 같은 출발점(약 10m 단위)·거리·형태면 같은 코스로 보고 다시 만들지 않는다
    key = f"{req.lat:.4f},{req.lon:.4f},{req.length_m},{req.mode}"
    return hashlib.sha1(key.encode()).hexdigest()[:10]


def run_job(cid, req):
    def progress(msg):
        with lock:
            jobs[cid]["message"] = msg

    try:
        course = build_course(req.lat, req.lon, req.length_m, req.mode, req.name, progress)
        (COURSES / f"{cid}.json").write_text(
            json.dumps(course, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
        meta = {"id": cid, "name": course["name"], "kind": course["kind"], "length_m": course["length_m"],
                "lat": req.lat, "lon": req.lon, "request_m": req.length_m, "mode": req.mode,
                "created": time.strftime("%Y-%m-%d %H:%M")}
        (COURSES / f"{cid}.meta.json").write_text(json.dumps(meta, ensure_ascii=False), encoding="utf-8")
        with lock:
            jobs[cid] = {"status": "ready", "message": "완성"}
    except Exception as e:  # 사용자에게 보여 줄 메시지로 바꿔서 저장
        traceback.print_exc()
        msg = str(e) if isinstance(e, RuntimeError) else f"코스를 만들지 못했어요 ({type(e).__name__})"
        with lock:
            jobs[cid] = {"status": "error", "message": msg}


@app.post("/api/courses")
def create_course(req: CourseRequest):
    cid = course_id(req)
    if (COURSES / f"{cid}.json").exists():
        return {"id": cid, "status": "ready"}
    with lock:
        if jobs.get(cid, {}).get("status") == "pending":
            return {"id": cid, "status": "pending"}
        waiting = sum(j["status"] == "pending" for j in jobs.values())
        jobs[cid] = {"status": "pending", "message": f"대기 중 (앞에 {waiting}개)" if waiting else "시작"}
    executor.submit(run_job, cid, req)
    return {"id": cid, "status": "pending"}


@app.get("/api/courses/{cid}")
def course_status(cid: str):
    if (COURSES / f"{cid}.json").exists():
        return {"id": cid, "status": "ready", "message": "완성"}
    with lock:
        job = jobs.get(cid)
    if job is None:
        raise HTTPException(404, "없는 코스예요")
    return {"id": cid, **job}


@app.get("/api/courses")
def list_courses():
    metas = [json.loads(p.read_text(encoding="utf-8")) for p in COURSES.glob("*.meta.json")]
    return sorted(metas, key=lambda m: m["created"], reverse=True)


# 실사 모드 3D 복원이 틀린 장면에서 사용자가 직접 찍은 길(sfm-fix.html). tools/sfm_video.py가 합칠 때 쓴다
SFM_WORK = WEB / "data" / "raw" / "sfm"


@app.get("/api/sfm-fix/{vid}")
def get_sfm_fix(vid: str):
    if not vid.replace("-", "").replace("_", "").isalnum():
        raise HTTPException(400, "잘못된 영상 id")
    f = SFM_WORK / f"manual_{vid}.json"
    return json.loads(f.read_text(encoding="utf-8")) if f.exists() else {"frames": {}}


@app.post("/api/sfm-fix/{vid}")
def save_sfm_fix(vid: str, body: dict):
    if not vid.replace("-", "").replace("_", "").isalnum():
        raise HTTPException(400, "잘못된 영상 id")
    frames = body.get("frames")
    if not isinstance(frames, dict):
        raise HTTPException(400, "frames가 없어요")
    SFM_WORK.mkdir(parents=True, exist_ok=True)
    tmp = SFM_WORK / f"manual_{vid}.json.tmp"
    tmp.write_text(json.dumps({"frames": frames, "saved": time.strftime("%Y-%m-%d %H:%M:%S")}, ensure_ascii=False),
                   encoding="utf-8")
    tmp.replace(SFM_WORK / f"manual_{vid}.json")
    return {"ok": True, "count": len(frames)}


# 실사 모드 가림막(장애물) 직접 찍기 (sfm-occ.html): 클릭 -> SAM 2가 앞뒤 장면을 따라가며 윤곽 -> 깊이 지도에 새김
import sys  # noqa: E402

sys.path.insert(0, str(ROOT / "tools"))
import cv2  # noqa: E402
import occluders  # noqa: E402

sam_executor = ThreadPoolExecutor(max_workers=1)  # 무거운 계산이라 한 번에 하나씩
frame_lock = threading.Lock()
frame_caps = {}


def check_vid(vid):
    if not vid.replace("-", "").replace("_", "").isalnum():
        raise HTTPException(400, "잘못된 영상 id")


@app.get("/api/frame/{vid}")
def video_frame(vid: str, t: float):
    """영상 한 장면 (960×540 JPEG)"""
    from fastapi.responses import Response
    check_vid(vid)
    with frame_lock:
        cap = frame_caps.get(vid)
        if cap is None:
            cap = frame_caps[vid] = cv2.VideoCapture(str(WEB / "assets" / "video" / f"{vid}.local.mp4"))
        img = occluders.frame_rgb(cap, t)
    if img is None:
        raise HTTPException(404, "그 시각 장면이 없어요")
    ok, buf = cv2.imencode(".jpg", cv2.cvtColor(img, cv2.COLOR_RGB2BGR), [cv2.IMWRITE_JPEG_QUALITY, 85])
    return Response(buf.tobytes(), media_type="image/jpeg")


@app.get("/api/occluders/{vid}")
def occluder_list(vid: str):
    check_vid(vid)
    return occluders.list_objects(vid)


def run_occluder(vid, meta):
    def progress(p):
        meta["progress"] = round(p, 2)
        occluders.save_meta(vid, meta)
    try:
        meta["status"] = "running"
        occluders.save_meta(vid, meta)
        n = occluders.track(vid, meta, progress=progress)
        meta["status"] = "done" if n else "empty"
        meta["frames"] = n
        occluders.save_meta(vid, meta)
        if n:
            occluders.compose(vid, occluders.files_for(vid, meta["t_from"], meta["t_to"]))
    except Exception as e:  # noqa: BLE001
        meta["status"] = "error"
        meta["error"] = f"{type(e).__name__}: {e}"
        occluders.save_meta(vid, meta)
        traceback.print_exc()


def resume_occluders():
    """서버를 다시 켜면 줄 서 있던(또는 하다 만) 가림막을 다시 줄 세운다"""
    for d in (occluders.WORK / "occluders").glob("*"):
        if not d.is_dir():
            continue
        for meta in occluders.list_objects(d.name):
            if meta.get("status") in ("queued", "running"):
                meta["status"], meta["progress"] = "queued", 0
                occluders.save_meta(d.name, meta)
                sam_executor.submit(run_occluder, d.name, meta)


resume_occluders()


@app.post("/api/occluders/{vid}")
def occluder_add(vid: str, body: dict):
    check_vid(vid)
    pts = body.get("points") or []
    if not pts or not all(isinstance(p, list) and len(p) == 3 for p in pts):
        raise HTTPException(400, "points: [[x, y, 1|0], ...]")
    meta = {"id": occluders.new_id(), "t": float(body["t"]), "points": pts, "status": "queued", "progress": 0,
            "created": time.strftime("%Y-%m-%d %H:%M:%S")}
    occluders.save_meta(vid, meta)
    sam_executor.submit(run_occluder, vid, meta)
    return meta


@app.delete("/api/occluders/{vid}/{oid}")
def occluder_delete(vid: str, oid: str):
    check_vid(vid)
    check_vid(oid)
    meta_file = occluders.meta_path(vid, oid)
    if not meta_file.exists():
        raise HTTPException(404, "없는 가림막")
    meta = json.loads(meta_file.read_text(encoding="utf-8"))
    meta_file.unlink()
    (occluders.odir(vid) / f"{oid}.npz").unlink(missing_ok=True)
    if "t_from" in meta:
        occluders.compose(vid, occluders.files_for(vid, meta["t_from"], meta["t_to"]))
    return {"ok": True}


@app.get("/api/occluders/{vid}/{oid}/mask")
def occluder_mask(vid: str, oid: str, t: float):
    """미리보기용 윤곽 (480×270, 흰색 = 물체)"""
    from fastapi.responses import Response
    check_vid(vid)
    check_vid(oid)
    times, masks = occluders.load_masks(vid, oid)
    if times is None:
        raise HTTPException(404, "윤곽 없음")
    i = int(abs(times - t).argmin())
    if abs(times[i] - t) > 0.2:
        raise HTTPException(404, "그 시각 윤곽 없음")
    ok, buf = cv2.imencode(".png", masks[i].astype("uint8") * 255)
    return Response(buf.tobytes(), media_type="image/png")


class NoCacheStaticFiles(StaticFiles):
    """코드를 고친 뒤 폰·브라우저가 옛 파일을 쓰지 않도록, 매번 바뀌었는지 확인하게 한다."""

    async def get_response(self, path, scope):
        response = await super().get_response(path, scope)
        response.headers["Cache-Control"] = "no-cache"
        return response


# 러닝머신 속도 테스트 (아이폰·에어팟·갤럭시 실시간 기록과 분석): /speed.html
from speedlab.api import router as speed_router  # noqa: E402

app.include_router(speed_router)

app.mount("/", NoCacheStaticFiles(directory=WEB, html=True), name="web")


def https_cert():
    """같은 와이파이의 갤럭시 크롬은 HTTPS 주소에서만 가속도·카메라를 쓸 수 있다 -> 자체 서명 인증서 (처음 한 번 만든다)"""
    import subprocess
    d = ROOT / "certs"
    key, crt = d / "key.pem", d / "cert.pem"
    if not crt.exists():
        d.mkdir(exist_ok=True)
        subprocess.run(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "3650",
                        "-keyout", str(key), "-out", str(crt), "-subj", "/CN=capstone-laptop"],
                       check=True, capture_output=True)
    return str(key), str(crt)


if __name__ == "__main__":
    import asyncio

    async def main():
        servers = [uvicorn.Server(uvicorn.Config(app, host="0.0.0.0", port=8000))]
        try:
            key, crt = https_cert()
            servers.append(uvicorn.Server(uvicorn.Config(app, host="0.0.0.0", port=8443,
                                                         ssl_keyfile=key, ssl_certfile=crt)))
        except Exception as e:  # noqa: BLE001
            print(f"HTTPS(8443)는 못 켰어요 (openssl 필요): {e}")
        await asyncio.gather(*(s.serve() for s in servers))

    asyncio.run(main())
