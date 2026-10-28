"""직접 찍은 가림막(장애물): 영상 한 장면에서 장애물을 클릭하면 SAM 2가 그 물체 윤곽을 따고 앞뒤 장면까지
따라간다. 윤곽 안쪽은 깊이 지도에서 '확실히 그 물체 거리 이하'로 고쳐 써서, AI 깊이가 틀려도 확실히 가린다.

  저장: run3d/data/raw/sfm/occluders/{video}/{id}.npz (times, 480×270 윤곽 packbits) + {id}.json (클릭, 상태)
  깊이 지도: run3d/assets/video/{video}.depth/base/*.png (AI 깊이만) + 윤곽 -> run3d/assets/video/{video}.depth/*.png
"""
import json
import math
import threading
import time
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parent.parent
VIDEO_DIR = ROOT / "run3d" / "assets" / "video"
WORK = ROOT / "run3d" / "data" / "raw" / "sfm"
MW, MH = 480, 270  # 윤곽 저장 크기
FPS = 4
SPAN_S = 20.0  # 클릭한 장면 앞뒤로 최대 이만큼 따라간다
_model = None
_lock = threading.Lock()  # 저장 중인 파일을 목록 읽기가 열면 Windows에서 바꿔치기가 거부된다


def odir(vid):
    d = WORK / "occluders" / vid
    d.mkdir(parents=True, exist_ok=True)
    return d


def meta_path(vid, oid):
    return odir(vid) / f"{oid}.json"


def list_objects(vid):
    out = []
    with _lock:
        for f in sorted(odir(vid).glob("*.json")):
            try:
                out.append(json.loads(f.read_text(encoding="utf-8")))
            except Exception:
                pass
    return out


def save_meta(vid, meta):
    with _lock:
        tmp = meta_path(vid, meta["id"]).with_suffix(".json.tmp")
        tmp.write_text(json.dumps(meta, ensure_ascii=False), encoding="utf-8")
        for k in range(20):  # 다른 프로그램(백신 등)이 잠깐 열고 있으면 잠시 뒤 다시
            try:
                tmp.replace(meta_path(vid, meta["id"]))
                return
            except PermissionError:
                time.sleep(0.05 * (k + 1))
        raise PermissionError(f"저장 실패: {meta_path(vid, meta['id'])}")


def load_masks(vid, oid):
    f = odir(vid) / f"{oid}.npz"
    if not f.exists():
        return None, None
    z = np.load(f)
    masks = np.unpackbits(z["bits"], axis=-1)[..., :MW].astype(bool)
    return z["times"], masks


def frame_rgb(cap, t, w=960, h=540):
    cap.set(cv2.CAP_PROP_POS_MSEC, t * 1000)
    ok, f = cap.read()
    if not ok:
        return None
    return cv2.cvtColor(cv2.resize(f, (w, h), interpolation=cv2.INTER_AREA), cv2.COLOR_BGR2RGB)


def model():
    global _model
    if _model is None:
        import torch
        from transformers import Sam2VideoModel, Sam2VideoProcessor
        torch.set_num_threads(12)
        name = "facebook/sam2.1-hiera-tiny"
        _model = (Sam2VideoProcessor.from_pretrained(name), Sam2VideoModel.from_pretrained(name).eval())
    return _model


def track(vid, meta, t_min=120.0, t_max=1860.0, progress=None):
    """meta: {id, t, points: [[x, y, 1(물체)|0(배경)], ...] (960×540 픽셀)}. 앞뒤로 따라가며 윤곽 저장"""
    import torch
    proc, net = model()
    t0 = meta["t"]
    times = np.arange(max(t_min, t0 - SPAN_S), min(t_max, t0 + SPAN_S) + 1e-6, 1 / FPS)
    click = int(np.argmin(np.abs(times - t0)))
    cap = cv2.VideoCapture(str(VIDEO_DIR / f"{vid}.local.mp4"))
    frames = [frame_rgb(cap, t) for t in times]
    keep = [i for i, f in enumerate(frames) if f is not None]
    frames = [frames[i] for i in keep]
    times = times[keep]
    click = int(np.argmin(np.abs(times - t0)))
    sess = proc.init_video_session(video=frames, inference_device="cpu", dtype=torch.float32)
    pts = [[[[p[0], p[1]] for p in meta["points"]]]]
    labels = [[[int(p[2]) for p in meta["points"]]]]
    proc.add_inputs_to_inference_session(inference_session=sess, frame_idx=click, obj_ids=1, input_points=pts, input_labels=labels)
    masks = {}
    done = 0
    with torch.no_grad():
        net(inference_session=sess, frame_idx=click)
        for rev in (False, True):
            empty = 0
            for o in net.propagate_in_video_iterator(sess, start_frame_idx=click, reverse=rev):
                m = proc.post_process_masks([o.pred_masks], original_sizes=[[540, 960]], binarize=True)[0][0, 0].numpy()
                m = cv2.resize(m.astype(np.uint8), (MW, MH), interpolation=cv2.INTER_NEAREST).astype(bool)
                masks[o.frame_idx] = m
                done += 1
                if progress:
                    progress(done / len(frames))
                # 물체가 화면에서 사라지면(아주 작아지면) 그 방향은 그만
                empty = empty + 1 if m.sum() < 30 else 0
                if empty >= 6:
                    break
    idx = sorted(k for k, m in masks.items() if m.sum() >= 30)
    if not idx:
        return 0
    arr = np.stack([masks[k] for k in idx])
    np.savez_compressed(odir(vid) / f"{meta['id']}.npz", times=times[idx], bits=np.packbits(arr, axis=-1))
    meta["t_from"], meta["t_to"] = float(times[idx[0]]), float(times[idx[-1]])
    return len(idx)


_geo = {}


def geometry(vid):
    """장면별 3D 점(화면 좌표, 거리 m)과 앞길: 가림막 물체의 실제 거리를 재는 데 쓴다 (한 번만 계산해 둔다)"""
    if vid in _geo:
        return _geo[vid]
    import sys
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import sfm_video as S
    course = json.loads((VIDEO_DIR / f"{vid}.course.json").read_text(encoding="utf-8"))
    st, ss = np.array(course["sync"]["t"]), np.array(course["sync"]["s"])
    s_at = lambda t: np.interp(t, st, ss)  # noqa: E731
    segs = S.stitch(S.load_all_chunks(list(np.arange(120.0, 1860.0, 40.0)), s_at), FPS)
    S.smooth_scale(segs, s_at, FPS)
    keymap = {}
    for g in segs:
        for i, k in enumerate(g["keys"]):
            keymap.setdefault(int(k), (g, i))
    rows = json.loads((VIDEO_DIR / f"{vid}.sfm.json").read_text(encoding="utf-8"))["frames"]
    _geo[vid] = (S, keymap, rows)
    return _geo[vid]


def object_distance(vid, t, mask):
    """가림막 물체까지의 실제 거리(m). AI 깊이는 밋밋한 벽을 수십 m씩 멀게 잡아서(창고 50m -> 90m) 믿지 않는다.
    1) 이 장면의 3D 점 중 화면에서 윤곽 안에 찍히는 점들의 가까운 쪽(30%) 거리
    2) 없으면 윤곽 맨 아래(물체가 땅에 닿는 곳)와 같은 화면 높이의 앞길 거리 (땅이 평평하다고 보고)"""
    S, keymap, rows = geometry(vid)
    mh, mw = mask.shape
    k = int(round((t - 120.0) * FPS))
    ys = np.nonzero(mask.any(axis=1))[0]
    if len(ys) and 0 <= k < len(rows) and rows[k]:
        bottom = (ys.max() + 0.5) * 540 / mh
        best = None
        for r in rows[k]:
            x, y, z = r[0], -r[1], -r[2]
            if z <= 0.5:
                continue
            u, v = x / z, y / z
            py = 705 * v * (1 - 0.183 * (u * u + v * v)) + 270
            if best is None or abs(py - bottom) < best[0]:
                best = (abs(py - bottom), z)
        if best and best[0] < 6:
            return float(best[1])
    gi = keymap.get(int(round(t * FPS)))
    if gi is not None:
        g, i = gi
        o = S.ScreenOccluder(g["tree"], g["C"][i], g["R"][i], g["upm"][i], None)
        if o.kd is not None:
            px, py = S.to_depth_px(o.uv[:, 0], o.uv[:, 1])  # 240×135 기준
            ix = (px * mw / 240).astype(int)
            iy = (py * mh / 135).astype(int)
            ok = (ix >= 0) & (ix < mw) & (iy >= 0) & (iy < mh)
            inside = np.zeros(len(ix), bool)
            inside[ok] = mask[iy[ok], ix[ok]]
            if inside.sum() >= 20:
                return float(np.percentile(o.z[inside], 5))
    return None


def object_distances(vid, times, masks):
    """물체 거리를 장면마다 재고 앞뒤 1초로 고르게 (튀면 가림 경계가 깜빡인다)"""
    d = np.array([object_distance(vid, t, m) or np.nan for t, m in zip(times, masks)], float)
    out = d.copy()
    for i in range(len(d)):
        w = d[max(0, i - 4):i + 5]
        w = w[np.isfinite(w)]
        out[i] = np.median(w) if len(w) else np.nan
    return out


def compose(vid, files=None):
    """기본 깊이 지도(base) 위에 가림막 윤곽을 새겨 최종 깊이 지도 PNG를 다시 쓴다.
    윤곽 안쪽은 '그 물체의 실제 거리(땅에 닿는 곳, +5%) 이하'로: AI 깊이가 멀게 잡은 벽까지 확실히 물체로"""
    ddir = VIDEO_DIR / f"{vid}.depth"
    meta = json.loads((ddir / "index.json").read_text(encoding="utf-8"))
    per, cols, tw, th = meta["perFile"], meta["cols"], meta["tileW"], meta["tileH"]
    objs = []
    for m in list_objects(vid):
        if m.get("status") != "done":
            continue
        times, masks = load_masks(vid, m["id"])
        if times is not None:
            objs.append((times, masks, object_distances(vid, times, masks)))
    n_files = math.ceil(meta["frames"] / per)
    targets = range(n_files) if files is None else files
    for k in targets:
        base = cv2.imread(str(ddir / "base" / f"{k:04d}.png"), cv2.IMREAD_GRAYSCALE)
        if base is None:
            continue
        out = base.copy()
        for j in range(per):
            fi = k * per + j
            t = meta["t0"] + fi / meta["fps"]
            r, c = divmod(j, cols)
            tile = out[r * th:(r + 1) * th, c * tw:(c + 1) * tw]
            for times, masks, dists in objs:
                i = int(np.argmin(np.abs(times - t)))
                if abs(times[i] - t) > 0.13:
                    continue
                m = cv2.resize(masks[i].astype(np.uint8), (tw, th), interpolation=cv2.INTER_NEAREST)
                m = cv2.dilate(m, np.ones((3, 3), np.uint8)).astype(bool)
                if m.sum() < 4:
                    continue
                z_obj = dists[i] if np.isfinite(dists[i]) else None
                if z_obj is None:  # 거리를 못 재면 AI 깊이의 가까운 쪽
                    q_obj = np.percentile(tile[m], 30)
                else:
                    zl = meta["zmin"], meta["zmax"]
                    q_obj = 255 * np.log(np.clip(z_obj * 1.05, *zl) / zl[0]) / np.log(zl[1] / zl[0])
                tile[m] = np.minimum(tile[m], int(round(q_obj)))
        cv2.imwrite(str(ddir / f"{k:04d}.png"), out, [cv2.IMWRITE_PNG_COMPRESSION, 9])


def files_for(vid, t_from, t_to):
    meta = json.loads((VIDEO_DIR / f"{vid}.depth" / "index.json").read_text(encoding="utf-8"))
    a = int((t_from - meta["t0"]) * meta["fps"]) // meta["perFile"]
    b = int((t_to - meta["t0"]) * meta["fps"]) // meta["perFile"]
    return list(range(max(0, a), b + 1))


def new_id():
    return time.strftime("%Y%m%d-%H%M%S")
