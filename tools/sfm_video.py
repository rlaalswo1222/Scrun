"""영상을 구간별로 SfM(COLMAP) 복원해서, 장면마다 "앞길(촬영자가 나중에 지나간 발밑)"을 카메라 기준 3D로 저장한다.

RunSocial식 원리: 카메라 궤적을 3D로 복원하고, 아바타를 그 3D 길 위에 놓은 뒤 영상과 같은 카메라로 그린다.
  1) 구간(기본 90초, 30초 겹침)마다 장면을 뽑아 COLMAP으로 카메라 위치·방향 복원 (렌즈 값 고정)
  2) 구간 안 3D 점들로 땅 높이(카메라 아래)를 추정, 진행 막대 거리표로 m 단위로 맞춤
  3) 장면 i마다: 앞으로 최대 AHEAD_M m의 발밑 점들(2m 간격)과 길 오른쪽 방향을 카메라 i 기준 좌표(m)로 기록

결과: run3d/assets/video/{id}.sfm.json
  { "fps", "t0", "fov": 세로 화각, "k": 방사 왜곡, "frames": [ [[x,y,z, rx,ry,rz], ...], ... ] }
  좌표: three.js 카메라 기준 (x 오른쪽, y 위, z 뒤 → 앞은 -z), 단위 m. 장면 i의 k번째 점 = 앞으로 (k+1)·2 m

  python tools/sfm_video.py o0EmQXATQQI --start 120 --end 1900
"""
import argparse
import json
import math
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

import numpy as np
import pycolmap
from scipy.spatial import cKDTree

ROOT = Path(__file__).resolve().parent.parent
VIDEO_DIR = ROOT / "run3d" / "assets" / "video"
WORK = ROOT / "run3d" / "data" / "raw" / "sfm"
W, H = 960, 540
STEP_M = 2.0
AHEAD_M = 80.0
# 시험 구간(960x540) COLMAP 결과: f=705, k=-0.18 -> 640x360으로 환산
F_640 = 705.0  # 960x540 기준 초점거리 (변수 이름은 예전 그대로)
K1 = -0.183


def run_chunk(video, t0, dur, fps, out_dir, mask=False):
    img = out_dir / "img"
    if out_dir.exists():
        shutil.rmtree(out_dir)
    img.mkdir(parents=True)
    subprocess.run(["ffmpeg", "-v", "error", "-ss", str(t0), "-t", str(dur), "-i", str(video),
                    "-vf", f"fps={fps},scale={W}:{H}", "-q:v", "3", str(img / "f%05d.jpg")], check=True)
    db = out_dir / "db.db"
    ro = pycolmap.ImageReaderOptions()
    ro.camera_model = "SIMPLE_RADIAL"
    ro.camera_params = f"{F_640},{W / 2},{H / 2},{K1}"
    if mask:
        # 영상에 박힌 글자(속도 막대·시간·속도)와 하늘은 특징점을 뽑지 않는다: 글자는 카메라가 움직여도
        # 화면에서 그대로라 "안 움직였다"는 거짓 단서가 되고, 바다·하늘뿐인 장면에서는 그게 계산을 망친다
        ro.camera_mask_path = str(WORK / "hud_mask.png")
    eo = pycolmap.FeatureExtractionOptions()
    eo.sift.max_num_features = 4000
    pycolmap.extract_features(db, img, camera_mode=pycolmap.CameraMode.SINGLE, reader_options=ro, extraction_options=eo)
    po = pycolmap.SequentialPairingOptions()
    po.overlap = 8
    pycolmap.match_sequential(db, pairing_options=po)
    names = sorted(p.name for p in img.iterdir())
    best = None
    for attempt in range(2):
        mo = pycolmap.IncrementalPipelineOptions()
        # 렌즈 값(초점거리, 왜곡)은 이미 재 놓은 값으로 고정. 앞으로만 달리는 영상에서 이걸 같이 고치게 두면
        # 조립하던 모델이 100장 넘게 갔다가 통째로 무너지는 일이 반복된다 (구간 10)
        mo.ba_refine_focal_length = False
        mo.ba_refine_extra_params = False
        mo.max_runtime_seconds = 2400 if not attempt else 1800  # 한 시도가 끝없이 붙잡고 있지 않게
        if attempt:
            # 다시 시도: 앞으로 똑바로 달리는 영상은 시작 장면 쌍이 기본 조건(정면 이동 싫어함, 큰 삼각 측량 각도)에
            # 걸려 일부만 조립되기 쉽다 -> 조건 완화
            mo.mapper.init_max_forward_motion = 1.0
            mo.mapper.init_min_tri_angle = 4
            mo.mapper.init_min_num_inliers = 50
            mo.mapper.abs_pose_min_num_inliers = 15
            mo.init_num_trials = 400
            mo.structure_less_registration_fallback = True
            shutil.rmtree(out_dir / "sparse", ignore_errors=True)
            (out_dir / "sparse").mkdir()
        maps = pycolmap.incremental_mapping(db, img, out_dir / "sparse", options=mo)
        if maps:
            rec = max(maps.values(), key=lambda m: m.num_reg_images())
            if best is None or rec.num_reg_images() > best.num_reg_images():
                best = rec
        if best is not None and best.num_reg_images() >= 0.8 * len(names):
            break
        print(f"  {best.num_reg_images() if best else 0}/{len(names)}장만 조립됨 -> 조건 완화해서 다시", flush=True)
    if best is None:
        return None
    return best, names


def clean_track(times, C, R):
    """엉뚱한 곳에 복원된 장면(앞뒤 장면과 거리가 크게 튐) 제거: 이웃 간 이동이 중앙값의 5배를 넘는 곳에서 끊고,
    가장 긴 연속 구간만 남긴다."""
    step = np.linalg.norm(np.diff(C, axis=0), axis=1)
    if not len(step):
        return times, C, R
    bad = step > 5 * np.median(step)
    cuts = np.r_[0, np.flatnonzero(bad) + 1, len(C)]
    a, b = max(zip(cuts[:-1], cuts[1:]), key=lambda ab: ab[1] - ab[0])
    return times[a:b], C[a:b], R[a:b]


def chunk_tracks(rec, names, t0, fps, s_at):
    """복원 결과 -> 장면 시각별 (C, R) 와 m 단위 배율, 위쪽, 카메라 높이."""
    poses = {}
    for im in rec.images.values():
        T = im.cam_from_world() if callable(im.cam_from_world) else im.cam_from_world
        Rm = T.rotation.matrix()
        poses[im.name] = (-Rm.T @ T.translation, Rm)
    times, C, R = [], [], []
    for n in names:
        if n in poses:
            idx = int("".join(ch for ch in n if ch.isdigit())) - 1
            times.append(t0 + idx / fps)
            C.append(poses[n][0])
            R.append(poses[n][1])
    C, R, times = np.array(C), np.array(R), np.array(times)
    times, C, R = clean_track(times, C, R)
    up = -np.mean([r.T @ np.array([0, 1, 0]) for r in R], axis=0)
    up /= np.linalg.norm(up)
    # 배율: 복원 단위 궤적 길이 / 거리표 길이
    path_units = np.r_[0, np.cumsum(np.linalg.norm(np.diff(C, axis=0), axis=1))]
    dist_m = s_at(times[-1]) - s_at(times[0])
    unit_per_m = path_units[-1] / max(dist_m, 1e-3)
    # 카메라 높이: 카메라 바로 앞 길바닥(앞 3~12m, 좌우 1.2m 안, 카메라보다 아래) 3D 점들의 높이 중앙값
    P = np.array([p.xyz for p in rec.points3D.values()])
    hs = []
    for c, r in zip(C[::4], R[::4]):
        fwd = r.T @ np.array([0, 0, 1.0])
        fwd = fwd - up * (fwd @ up)
        fwd /= np.linalg.norm(fwd)
        side = np.cross(fwd, up)
        d = P - c
        a, s, h = d @ fwd, d @ side, -(d @ up)
        m = (a > 3 * unit_per_m) & (a < 12 * unit_per_m) & (np.abs(s) < 1.2 * unit_per_m) & (h > 0.3 * unit_per_m)
        if m.sum() > 10:
            hs.append(np.median(h[m]))
    height = float(np.median(hs)) if hs else 1.2 * unit_per_m
    return times, C, R, up, unit_per_m, height, path_units


def load_chunks(starts, s_at):
    """저장된 조각들을 읽어 튀는 장면을 거르고, 조각별 배율(복원 단위/m)과 카메라 높이(m)를 다시 계산."""
    chunks = []
    for ci in range(len(starts)):
        f = WORK / f"chunk{ci:03d}.npz"
        if not f.exists():
            continue
        z = np.load(f)
        times, C, R = clean_track(z["times"], z["C"], z["R"])
        if len(times) < 20:
            continue
        upm_old = float(z["upm"])
        path_units = np.linalg.norm(np.diff(C, axis=0), axis=1).sum()
        upm = path_units / max(s_at(times[-1]) - s_at(times[0]), 1e-3)
        height_m = CAM_HEIGHT_M
        P = np.array(z["P"]) if "P" in z.files else np.zeros((0, 3))
        chunks.append({"ci": ci, "times": times, "C": C, "R": R, "up": np.array(z["up"]), "upm": upm, "hm": height_m,
                       "P": P, "full": int(z["reg"]) >= 0.95 * int(z["total"])})
    return chunks


def load_all_chunks(starts, s_at):
    """정규 조각 + 구멍 메움 조각(patch_*.npz). 이어 붙이기는 이웃끼리 하므로 영상 시각 순으로 정렬"""
    chunks = load_chunks(starts, s_at)
    for f in sorted(list(WORK.glob("patch_*.npz")) + list(WORK.glob("patchm_*.npz"))):
        z = np.load(f)
        times, C, R = clean_track(z["times"], z["C"], z["R"])
        if len(times) < 20:
            continue
        path_units = np.linalg.norm(np.diff(C, axis=0), axis=1).sum()
        upm = path_units / max(s_at(times[-1]) - s_at(times[0]), 1e-3)
        height_m = CAM_HEIGHT_M
        P = np.array(z["P"]) if "P" in z.files else np.zeros((0, 3))
        chunks.append({"ci": f.stem, "times": times, "C": C, "R": R, "up": np.array(z["up"]), "upm": upm,
                       "hm": height_m, "P": P, "full": int(z["reg"]) >= 0.95 * int(z["total"])})
    chunks.sort(key=lambda c: c["times"][0])
    return chunks


def find_gaps(frames, t0, fps, min_reach=70.0, margin=18.0):
    """앞길이 min_reach m보다 짧은 곳(복원 구멍, 이어 붙이기 끊김)을 찾아 메움 조각 창 [시작, 길이] 목록으로"""
    reach = np.array([len(f) * STEP_M for f in frames])
    t = t0 + np.arange(len(frames)) / fps
    wins = []
    i = 0
    while i < len(reach):
        if reach[i] >= min_reach:
            i += 1
            continue
        j = i
        while j < len(reach) and reach[j] < min_reach:
            j += 1
        # 앞길이 짧아지기 시작한 곳은 구멍 바로 앞일 뿐 -> 진짜 구멍(앞길 0)이나 짧아짐이 끝나는 곳을 중심으로
        hole = t[i:j][reach[i:j] == 0]
        a, b = (hole[0], hole[-1]) if len(hole) else (t[j - 1], t[j - 1])
        wins.append([max(t0, a - margin), max(40.0, b - a + 2 * margin)])
        i = j
    # 이어 붙인 자리가 어긋난 곳: 20m 앞 점까지의 거리가 한 장면 사이에 0.8m 넘게 튐
    # (보통은 장면당 6cm 안팎. 러너가 그 자리에서 앞뒤로 튄다)
    n20 = np.array([np.linalg.norm(f[9][:3]) if len(f) > 9 else np.nan for f in frames])
    jump = np.abs(np.diff(n20)) > 0.8
    i = 0
    while i < len(jump):
        if not jump[i]:
            i += 1
            continue
        j = i
        while j < len(jump) and (jump[j] or (j + 1 < len(jump) and jump[j + 1])):
            j += 1
        wins.append([max(t0, t[i] - margin), max(40.0, t[j] - t[i] + 2 * margin)])
        i = j + 1
    wins.sort()
    # 겹치는 창은 합친다
    merged = []
    for a, d in wins:
        if merged and a <= merged[-1][0] + merged[-1][1]:
            merged[-1][1] = max(merged[-1][1], a + d - merged[-1][0])
        else:
            merged.append([a, d])
    # 긴 창은 50초씩, 20초 겹치게 나눈다 (한 번에 너무 길면 복원이 느리고 잘 무너짐)
    out = []
    for a, d in merged:
        if d <= 60:
            out.append([round(a, 2), round(d, 2)])
            continue
        x = a
        while x < a + d - 20:
            out.append([round(x, 2), 50.0])
            x += 30
    return out


def umeyama(src, dst):
    """dst ≈ s·Q·src + T 가 되는 닮음변환 (s, Q, T)."""
    ms, md = src.mean(0), dst.mean(0)
    a, b = src - ms, dst - md
    U, D, Vt = np.linalg.svd(b.T @ a / len(src))
    S = np.eye(3)
    if np.linalg.det(U) * np.linalg.det(Vt) < 0:
        S[2, 2] = -1
    Q = U @ S @ Vt
    s = np.trace(np.diag(D) @ S) / (a ** 2).sum(1).mean()
    return s, Q, md - s * Q @ ms


def stitch(chunks, fps):
    """이웃 조각을 겹치는 장면(같은 영상 시각)의 카메라 위치로 이어 붙인다.
    반환: 이어진 묶음 목록. 묶음마다 장면 시각 → (위치 C, 방향 R, 위쪽 up, 배율 upm, 높이 hm) (모두 첫 조각의 좌표계)"""
    def try_link(ch, placed):
        """지금 묶음의 조각들 중 겹치는 장면(20장 이상)으로 가장 잘 맞는 조각에 맞춘 닮음변환. 안 맞으면 None"""
        keys = np.round(ch["times"] * fps).astype(int)
        best = None
        for prev in placed:
            pk = {k: n for n, k in enumerate(np.round(prev["times"] * fps).astype(int))}
            common = [(pk[k], n) for n, k in enumerate(keys) if k in pk]
            if len(common) < 20:
                continue
            a = np.array([prev["Cg"][m] for m, _ in common])
            b = np.array([ch["C"][n] for _, n in common])
            # 회전은 겹치는 장면의 카메라 방향으로 맞춘다. 거의 직선으로 달려서 위치만으로는
            # "길을 축으로 한 회전"이 정해지지 않기 때문 (위치는 맞아도 방향이 5~10° 틀어짐)
            M = sum(prev["Rg"][m].T @ ch["R"][n] for m, n in common)
            U, _, Vt = np.linalg.svd(M)
            Q = U @ np.diag([1, 1, np.linalg.det(U @ Vt)]) @ Vt
            ma, mb = a.mean(0), b.mean(0)
            qb = (Q @ (b - mb).T).T
            s_ = ((a - ma) * qb).sum() / (qb ** 2).sum()
            t_ = ma - s_ * Q @ mb
            err = np.linalg.norm(s_ * (Q @ b.T).T + t_ - a, axis=1).mean()
            step = np.median(np.linalg.norm(np.diff(a, axis=0), axis=1))
            score = err / max(step, 1e-9)
            if score < 3 and (best is None or score < best[0]):  # 겹친 구간이 잘 맞으면 이어 붙임
                best = (score, (s_, Q, t_))
        return best[1] if best else None

    segments = []
    point_sets = []
    seg = None
    placed = []  # 지금 묶음에 이미 붙인 조각들
    skipped = []
    for idx, ch in enumerate(chunks):
        keys = np.round(ch["times"] * fps).astype(int)
        T = try_link(ch, placed) if placed else None
        linked = T is not None
        if not linked and placed and any(try_link(c2, placed) is not None for c2 in chunks[idx + 1:idx + 3]):
            # 이 조각만 모양이 어긋남(잘못 복원된 조각): 다음 조각이 지금 묶음에 바로 붙으므로 이 조각은 건너뛴다
            skipped.append(ch)
            continue
        if not linked:
            T = (1.0, np.eye(3), np.zeros(3))
        s_, Q, t_ = T
        ch["Cg"] = s_ * (Q @ ch["C"].T).T + t_
        ch["Rg"] = np.array([r @ Q.T for r in ch["R"]])  # cam_from_world, 세계를 Q로 돌렸으므로
        ch["upg"] = Q @ ch["up"]
        ch["upmg"] = ch["upm"] * s_
        ch["Pg"] = s_ * (Q @ ch["P"].T).T + t_ if len(ch["P"]) else ch["P"]
        if not linked:
            seg = {}
            segments.append(seg)
            seg_points = []
            point_sets.append(seg_points)
            placed = []
        placed.append(ch)
        seg_points.append(ch["Pg"])
        # 겹치는 장면은 뒤 조각의 앞부분보다 앞 조각의 가운데에 가까운 쪽을 쓴다 (조각 가장자리는 덜 정확)
        # 메움 조각은 그 자리를 고치려고 다시 계산한 것이므로, 양 끝 4초를 빼고는 정규 조각보다 우선
        mid = (ch["times"][0] + ch["times"][-1]) / 2
        is_patch = str(ch["ci"]).startswith("patch")
        for n, k in enumerate(keys):
            t = ch["times"][n]
            rank = abs(t - mid)
            if is_patch and ch["times"][0] + 4 <= t <= ch["times"][-1] - 4:
                rank = -1.0
            if k in seg and seg[k][6] <= rank:
                continue
            seg[k] = (ch["Cg"][n], ch["Rg"][n], ch["upg"], ch["upmg"], ch["hm"], t, rank)
    # 건너뛴 조각도 혼자서는 쓸 수 있으니 따로 묶음으로 둔다 (장면마다 더 나은 묶음을 고를 때 후보)
    for ch in skipped:
        ch["Cg"], ch["Rg"], ch["upg"], ch["upmg"], ch["Pg"] = ch["C"], ch["R"], ch["up"], ch["upm"], ch["P"]
        keys = np.round(ch["times"] * fps).astype(int)
        segments.append({k: (ch["C"][n], ch["R"][n], ch["up"], ch["upm"], ch["hm"], ch["times"][n], 0.0)
                         for n, k in enumerate(keys)})
        point_sets.append([ch["P"]])
    out = []
    for seg, pts in zip(segments, point_sets):
        ks = sorted(seg)
        P = np.vstack([q for q in pts if len(q)]) if any(len(q) for q in pts) else np.zeros((0, 3))
        out.append({"keys": np.array(ks), "C": np.array([seg[k][0] for k in ks]), "R": np.array([seg[k][1] for k in ks]),
                    "up": np.array([seg[k][2] for k in ks]), "upm": np.array([seg[k][3] for k in ks]),
                    "hm": np.array([seg[k][4] for k in ks]), "t": np.array([seg[k][5] for k in ks]),
                    "tree": cKDTree(P) if len(P) else None})
    return out


def smooth_scale(segments, s_at, fps, window_s=30):
    """이어 붙인 경로 전체에서 축척(복원 단위/m)을 다시 계산: 앞뒤 window_s초 동안의 경로 길이 / 달린 거리.
    조각마다 따로 잰 축척은 조각끼리 10~20% 달라서, 조각이 바뀌는 순간 거리가 튄다."""
    for g in segments:
        L = np.r_[0, np.cumsum(np.linalg.norm(np.diff(g["C"], axis=0), axis=1))]
        S = s_at(g["t"])
        w = int(window_s * fps)
        n = len(L)
        upm = np.empty(n)
        for i in range(n):
            a, b = max(0, i - w), min(n - 1, i + w)
            upm[i] = (L[b] - L[a]) / max(S[b] - S[a], 1e-3)
        bad = ~np.isfinite(upm) | (upm <= 0)
        upm[bad] = np.median(upm[~bad]) if (~bad).any() else 1.0
        g["upm"] = upm


# 카메라 높이(m): 가슴에 단 카메라라 영상 내내 거의 일정. 조각마다 '앞쪽 3D 점의 가운데 높이'로 재던 값은
# 덤불 꼭대기 점 때문에 0.8~1.3m로 낮고 들쭉날쭉해서 아바타가 20~40cm 떠 보였다. 깊이 사진으로 발 앞 길바닥을
# 재면 중간값 1.48m, 9:59 행인 키로 역산하면 약 1.38m -> 그 사이 값으로 고정 (2026-10-11)
CAM_HEIGHT_M = 1.40
BAD_RANGES = []  # 눈으로 확인한 틀린 구간 (영상 초). main()에서 WORK/bad_ranges.json으로 채운다
OCC_LEVELS = (0.15, 0.45, 0.8, 1.15, 1.5)  # 시선을 그을 러너 몸 높이 (m): 발목·무릎·허리·어깨·머리
OCC_HIDDEN = 2.0  # 머리까지 가려지면 이 값 (러너 키보다 큼 -> 완전히 숨김)


def occlusion(tree, cam, foot, up, upm):
    """카메라에서 러너(발 위치 foot)까지 몸 높이별 시선이 3D 점에 막히는지.
    반환: 아래에서부터 연속으로 가려진 높이(m). 0 = 다 보임, OCC_HIDDEN = 다 가려짐"""
    if tree is None:
        return 0.0
    hidden = 0.0
    for h in OCC_LEVELS:
        target = foot + up * h * upm
        d = target - cam
        dist = np.linalg.norm(d) / upm
        if dist < 4:
            return 0.0
        # 시선 위 점들: 카메라 앞 1.5m부터 러너 앞 max(3m, 15%)까지 (러너 발밑 땅을 가림으로 착각하지 않게)
        end = 1 - max(3.0, 0.15 * dist) / dist
        fr = np.linspace(1.5 / dist, end, 16)
        if end <= fr[0]:
            return 0.0
        samples = cam + np.outer(fr, d)
        r = np.maximum(0.25, fr * dist * 0.006) * upm  # 멀수록 넉넉하게 (약 0.35°)
        # 시선을 막으려면 그 높이까지 솟아 있어야 한다: 시선보다 아래 있는 점(길바닥 등)은 무시
        blocked = False
        for q, rr in zip(samples, r):
            idx = tree.query_ball_point(q, rr)
            if len(idx) >= 3:
                above = ((tree.data[idx] - q) @ up) >= -0.05 * upm
                if above.sum() >= 3:
                    blocked = True
                    break
        if blocked:
            hidden = h + 0.15
        else:
            return hidden
    return OCC_HIDDEN


class DepthStore:
    """tools/sfm_depth.py가 만든 장면별 깊이 사진(상대값, 클수록 가까움, 240×135)을 필요할 때 읽는다"""

    def __init__(self, t0=120.0, fps=4):
        self.dir = WORK / "depth"
        self.t0, self.fps = t0, fps
        self.blocks = {}

    def get(self, t):
        i = int(round((t - self.t0) * self.fps))
        if i < 0:
            return None
        b0 = i - i % 240
        if b0 not in self.blocks:
            f = self.dir / f"{b0:05d}.npz"
            self.blocks[b0] = np.load(f)["disp"] if f.exists() else None
        blk = self.blocks[b0]
        if blk is None or i - b0 >= len(blk):
            return None
        d = blk[i - b0].astype(np.float32)
        return d if d.any() else None


DEPTH = None  # main()에서 DepthStore로 채운다 (깊이 사진이 있을 때)


def to_depth_px(u, v):
    """3D 계산의 화면 좌표(렌즈 휘어짐을 편 정규 좌표) -> 깊이 사진 픽셀 (원래 영상처럼 휘어진 240×135)"""
    d = 1 + K1 * (u * u + v * v)
    return (F_640 * u * d + W / 2) * 240 / W, (F_640 * v * d + H / 2) * 135 / H


class ScreenOccluder:
    """장면 하나에서 3D 점들을 화면에 찍어 두고, 러너 몸 높이별 자리를 러너보다 가까운 점들이 덮는지 본다.
    (시선 위를 16군데만 찔러 보던 occlusion()은 얇은 벽(11:52 보트하우스)이나 깊이 오차가 수 m인 먼 점을 놓쳤다)"""

    def __init__(self, tree, cam, Rc, upm, t=None):
        self.kd = None
        self.Z = None
        if tree is None:
            return
        self.cam, self.Rc, self.upm = cam, Rc, upm
        idx = tree.query_ball_point(cam, (AHEAD_M + 30) * upm)
        if not idx:
            return
        X = (Rc @ (tree.data[idx] - cam).T).T / upm  # 카메라 기준 m (x 오른쪽, y 아래, z 앞)
        X = X[X[:, 2] > 1.0]
        if len(X) < 3:
            return
        self.z = X[:, 2]
        self.uv = X[:, :2] / X[:, 2:3]
        self.kd = cKDTree(self.uv)
        if DEPTH is not None and t is not None:
            self.Z = self.metric_depth(DEPTH.get(t))

    def metric_depth(self, disp):
        """AI 깊이(상대값)를 이 장면의 3D 점으로 미터에 맞춘다: 1/거리 = a·깊이값 + b (튀는 점은 버리며 맞춤).
        창고 뒤 배경처럼 이 장면에서 안 보이는 3D 점도 섞여 있어서, 다수결로(RANSAC) 맞는 점들만 쓴다"""
        if disp is None:
            return None
        u, v = self.uv[:, 0], self.uv[:, 1]
        m = (np.abs(u) < 0.75) & (np.abs(v) < 0.42) & (self.z > 2) & (self.z < 150)
        if m.sum() < 60:
            return None
        px, py = to_depth_px(u[m], v[m])
        ix = np.clip(px.astype(int), 0, 239)
        iy = np.clip(py.astype(int), 0, 134)
        d = disp[iy, ix]
        w = 1 / self.z[m]
        rng = np.random.default_rng(1)
        best, best_n = None, 0
        for _ in range(150):
            a_, b_ = rng.choice(len(d), 2, replace=False)
            if abs(d[a_] - d[b_]) < 1e-3:
                continue
            a = (w[a_] - w[b_]) / (d[a_] - d[b_])
            if a <= 0:
                continue
            b = w[a_] - a * d[a_]
            n = int((np.abs(a * d + b - w) < 0.2 * w).sum())
            if n > best_n:
                best, best_n = (a, b), n
        if best is None or best_n < max(40, 0.3 * len(d)):
            return None
        a, b = best
        inl = np.abs(a * d + b - w) < 0.2 * w
        A = np.c_[d[inl], np.ones(inl.sum())]
        a, b = np.linalg.lstsq(A, w[inl], rcond=None)[0]
        if a <= 0:
            return None
        return 1 / np.clip(a * disp + b, 1 / 500, 2.0)  # 미터 (0.5~500m)

    def depth_blocked(self, us, vs, zs, w):
        """몸 자리 3×3 칸 가운데에서 깊이 사진이 러너보다 확실히 가까우면 그 칸은 가려짐. 5칸 이상이면 가려짐.
        먼 곳은 AI 깊이 오차가 커서 여유를 넉넉히(거리의 15%)"""
        gx, gy = np.meshgrid(np.array([-2, 0, 2]) * w / 3, np.array([-2.5, -1.5, -0.5]) * w / 3)
        px, py = to_depth_px(us + gx.ravel(), vs + gy.ravel())
        ix, iy = px.astype(int), py.astype(int)
        ok = (ix >= 0) & (ix < 240) & (iy >= 0) & (iy < 135)
        if ok.sum() < 5:
            return None
        z = self.Z[iy[ok], ix[ok]]
        return int((z < zs - max(1.5, 0.15 * zs)).sum()) >= 5

    def blocked(self, foot, up, h):
        """몸 높이 h(m) 자리를 러너보다 가까운 점들이 덮는지.
        그 자리(몸 폭 0.6m × 0.3m 위쪽)를 3×3 칸으로 나눠, 앞쪽 점이 있는 칸이 5칸 이상이면 가려짐.
        점 수나 비율이 아니라 '얼마나 넓게 덮나'로 본다: 뒤 배경(언덕·집) 점이 아무리 빽빽해도 상관없고,
        먼 점 몇 개의 깊이 오차로 생기는 드문드문한 가짜 앞쪽 점은 넓게 덮지 못한다"""
        q = self.Rc @ (foot + up * h * self.upm - self.cam) / self.upm
        zs = q[2]
        if zs < 4:
            return False
        us, vs = q[0] / zs, q[1] / zs
        w = 0.3 / zs
        if self.Z is not None:  # 깊이 사진이 있으면 그걸로 (밋밋한 벽도 면으로 잡힌다)
            r = self.depth_blocked(us, vs, zs, w)
            if r is not None:
                return r
        idx = self.kd.query_ball_point((us, vs - w / 2), w * 1.2)
        if len(idx) < 8:
            return False
        idx = np.asarray(idx)
        uv, z = self.uv[idx], self.z[idx]
        # 화면에서 이 높이와 같거나 위에 찍히는 점만 (러너 바로 앞 길바닥은 발보다 아래에 찍힌다)
        m = (np.abs(uv[:, 0] - us) <= w) & (uv[:, 1] <= vs + 0.03 / zs) & (uv[:, 1] >= vs - w)             & (z < zs - max(1.5, 0.08 * zs))
        if m.sum() < 8:
            return False
        cx = np.clip(((uv[m, 0] - us + w) / (2 * w) * 3).astype(int), 0, 2)
        cy = np.clip(((uv[m, 1] - vs + w) / w * 3).astype(int), 0, 2)
        return len(set(zip(cx.tolist(), cy.tolist()))) >= 5

    def hidden(self, foot, up):
        """아래에서부터 가려진 높이(m). 0 = 다 보임, OCC_HIDDEN = 다 가려짐.
        아래가 보여도 머리 포함 대부분(3단 이상)이 가려지면 다 가려진 것으로 (창고 기둥 사이로 발만 보이는 경우 등)"""
        if self.kd is None:
            return 0.0
        b = [self.blocked(foot, up, h) for h in OCC_LEVELS]
        if b[-1] and sum(b) >= 3:
            return OCC_HIDDEN
        hidden = 0.0
        for h, x in zip(OCC_LEVELS, b):
            if not x:
                break
            hidden = h + 0.15
        return OCC_HIDDEN if hidden >= OCC_LEVELS[-1] else hidden


def smooth_occlusion_in_time(frames, r=2):
    """가려짐을 장면(0.25초) 사이로도 이어 준다: 앞뒤 0.5초 안에서 가려졌다가 한 장면만 보이는 깜빡임을 메우고,
    한 장면만 가려지는 깜빡임은 지운다. 같은 거리(앞길 k번째 점)끼리 비교"""
    n = len(frames)
    K = max((len(f) for f in frames), default=0)
    if not n or not K:
        return
    O = np.full((n, K), np.nan)
    for i, f in enumerate(frames):
        for k, row in enumerate(f):
            O[i, k] = row[6]

    def slide(A, rr, f):
        pad = np.concatenate([np.repeat(A[:1], rr, 0), A, np.repeat(A[-1:], rr, 0)])
        S = np.stack([pad[j:j + len(A)] for j in range(2 * rr + 1)])
        with np.errstate(all="ignore"):
            out = f(S, axis=0)
        return np.where(np.isnan(A), np.nan, out)

    O = slide(slide(O, r, np.nanmax), r, np.nanmin)
    O = slide(slide(O, 1, np.nanmin), 1, np.nanmax)
    for i, f in enumerate(frames):
        for k, row in enumerate(f):
            if not np.isnan(O[i, k]):
                row[6] = round(float(O[i, k]), 2)


def manual_rows(mark, h):
    """직접 찍은 장면(sfm-fix.html): 수평선 2점 + 길 한가운데 점들(960×540 픽셀) -> 앞길 rows.
    수평선 = 땅의 소실선이라 땅 방향(기울기)이 정해지고, 카메라 높이 h(m) 아래 평평한 땅과 클릭한 시선이 만나는 곳이 발밑"""
    def ray(px):
        p = np.array([(px[0] - W / 2) / F_640, (px[1] - H / 2) / F_640])
        q = p.copy()
        for _ in range(5):  # 렌즈 휘어짐 되돌리기
            q = p / (1 + K1 * (q @ q))
        return np.array([q[0], q[1], 1.0])
    if len(mark.get("horizon", [])) != 2 or len(mark.get("path", [])) < 2:
        return []
    n = np.cross(ray(mark["horizon"][0]), ray(mark["horizon"][1]))
    n /= np.linalg.norm(n)
    if n[1] > 0:
        n = -n  # 위쪽 (카메라 좌표는 y가 아래)
    pts = [-h * n]  # 카메라 바로 아래 발밑
    for px in mark["path"]:
        r = ray(px)
        if n @ r > -1e-3:
            break  # 수평선 위를 찍음
        pts.append(-h / (n @ r) * r)
    pts = np.array(pts)
    if len(pts) < 2:
        return []
    seg = np.linalg.norm(np.diff(pts, axis=0), axis=1)
    cum = np.r_[0, np.cumsum(seg)]
    up3 = np.array([n[0], -n[1], -n[2]])
    rows = []
    for d in np.arange(STEP_M, min(AHEAD_M, cum[-1]) + 1e-6, STEP_M):
        k = min(int(np.searchsorted(cum, d)), len(pts) - 1)
        u = (d - cum[k - 1]) / (seg[k - 1] or 1)
        X = pts[k - 1] + (pts[k] - pts[k - 1]) * u
        x3 = np.array([X[0], -X[1], -X[2]])
        dv = pts[k] - pts[k - 1]
        d3 = np.array([dv[0], -dv[1], -dv[2]])
        right = np.cross(d3, up3)
        right /= np.linalg.norm(right) or 1
        rows.append([round(float(x3[0]), 2), round(float(x3[1]), 2), round(float(x3[2]), 2),
                     round(float(right[0]), 3), round(float(right[1]), 3), round(float(right[2]), 3), 0.0])
    return rows


def smooth_manual(keys, s_at, window_s=2.0):
    """직접 찍은 장면들의 먼 부분 출렁임을 편다: 앞뒤 window_s초 안에서 찍은 장면의 길을, 그 사이 달린 거리만큼
    옮기고 돌려서(가까운 쪽 길 방향을 맞춤) 이 장면 위에 겹친 뒤 거리별로 평균. 수평선 근처 클릭은 1~2픽셀에도
    몇 m씩 달라지지만, 여러 장면이 같은 길을 가리키므로 평균하면 고르게 다가온다"""
    def ground(rows):
        return np.array([[r[0], -r[2]] for r in rows])  # (오른쪽, 앞) m

    def at(P, d):
        """길을 따라 d m 지점과 그 방향 (P[0]은 2m 지점)"""
        dd = np.arange(1, len(P) + 1) * STEP_M
        if d < dd[0] - 1e-6 or d > dd[-1] + 1e-6:
            return None, None
        x, y = np.interp(d, dd, P[:, 0]), np.interp(d, dd, P[:, 1])
        k = min(int(np.searchsorted(dd, d)), len(P) - 2)
        v = P[k + 1] - P[k]
        return np.array([x, y]), v / (np.linalg.norm(v) or 1)

    out = []
    for t, rows in keys:
        if len(rows) < 3:
            out.append((t, rows))
            continue
        Pk = ground(rows)
        acc = Pk * 2.0
        wsum = np.full(len(Pk), 2.0)
        _, dir0 = at(Pk, STEP_M)
        for t2, rows2 in keys:
            if t2 == t or abs(t2 - t) > window_s or len(rows2) < 3:
                continue
            Pj = ground(rows2)
            delta = float(s_at(t2) - s_at(t))  # 그 장면 카메라가 이 장면보다 앞선 거리
            if delta >= 0:
                o, dk = at(Pk, max(delta, STEP_M))  # 그 카메라 자리 (이 장면 좌표)
                _, dj = at(Pj, STEP_M)
                if o is None:
                    continue
                ang = np.arctan2(dk[0], dk[1]) - np.arctan2(dj[0], dj[1])
                c, s_ = np.cos(ang), np.sin(ang)
                Rm = np.array([[c, s_], [-s_, c]])
                for n in range(len(Pk)):
                    e = (n + 1) * STEP_M - delta
                    if e < STEP_M:
                        continue
                    q, _ = at(Pj, e)
                    if q is None:
                        break
                    acc[n] += o + Rm @ q  # 그 카메라 발밑(o)에서 돌린 길
                    wsum[n] += 1
            else:
                o, dj = at(Pj, max(-delta, STEP_M))  # 이 장면 카메라 자리 (그 장면 좌표)
                if o is None:
                    continue
                ang = np.arctan2(dir0[0], dir0[1]) - np.arctan2(dj[0], dj[1])
                c, s_ = np.cos(ang), np.sin(ang)
                Rm = np.array([[c, s_], [-s_, c]])
                for n in range(len(Pk)):
                    q, _ = at(Pj, (n + 1) * STEP_M - delta)
                    if q is None:
                        break
                    acc[n] += Rm @ (q - o)
                    wsum[n] += 1
        P = acc / wsum[:, None]
        new = [list(r) for r in rows]
        for r, (x, f) in zip(new, P):
            r[0], r[2] = round(float(x), 2), round(float(-f), 2)
        out.append((t, new))
    return out


def depth_from_rows(rows, disp):
    """3D 점이 없거나 3D가 틀린 장면: 앞 2~20m 길바닥(앞길 rows, 미터)으로 깊이 사진을 미터에 맞춘 거리 지도 (240×135)"""
    if disp is None or len(rows) < 4:
        return None
    P = np.array([r[:3] for r in rows], float)
    cv = np.c_[P[:, 0], -P[:, 1], -P[:, 2]]
    near = (cv[:, 2] > 1.5) & (np.arange(len(P)) * STEP_M < 20)
    if near.sum() < 3:
        return None
    px, py = to_depth_px(cv[near, 0] / cv[near, 2], cv[near, 1] / cv[near, 2])
    ok = (px >= 0) & (px < 240) & (py >= 0) & (py < 135)
    if ok.sum() < 3:
        return None
    d = disp[py[ok].astype(int), px[ok].astype(int)]
    w = 1 / cv[near][ok, 2]
    a, b = np.linalg.lstsq(np.c_[d, np.ones(len(d))], w, rcond=None)[0]
    if a <= 0:
        return None
    return 1 / np.clip(a * disp + b, 1 / 500, 2.0)


def occlude_by_depth(rows, disp):
    """3D 점이 없는 장면(직접 찍은 길)의 가림: 앞 2~20m 길바닥(거의 항상 보임)으로 깊이 사진을 미터에 맞추고,
    러너 몸 높이별 자리를 ScreenOccluder.depth_blocked와 같은 기준으로 본다"""
    if disp is None or len(rows) < 4:
        return rows
    P = np.array([r[:3] for r in rows], float)
    cv = np.c_[P[:, 0], -P[:, 1], -P[:, 2]]  # 카메라 좌표 (x 오른쪽, y 아래, z 앞)
    near = (cv[:, 2] > 1.5) & (np.arange(len(P)) * STEP_M < 20)
    if near.sum() < 3:
        return rows
    px, py = to_depth_px(cv[near, 0] / cv[near, 2], cv[near, 1] / cv[near, 2])
    ok = (px >= 0) & (px < 240) & (py >= 0) & (py < 135)
    if ok.sum() < 3:
        return rows
    d = disp[py[ok].astype(int), px[ok].astype(int)]
    w = 1 / cv[near][ok, 2]
    A = np.c_[d, np.ones(len(d))]
    a, b = np.linalg.lstsq(A, w, rcond=None)[0]
    if a <= 0:
        return rows
    Z = 1 / np.clip(a * disp + b, 1 / 500, 2.0)
    occ = []
    for k in range(len(P)):
        fwd = P[min(k + 1, len(P) - 1)] - P[max(k - 1, 0)]
        up = np.cross(np.array(rows[k][3:6]), fwd)
        up = up / (np.linalg.norm(up) or 1)
        if up[1] < 0:
            up = -up
        blocked = []
        for h in OCC_LEVELS:
            q = P[k] + up * h
            zc = -q[2]
            if zc < 4:
                blocked.append(False)
                continue
            us, vs, w_ = q[0] / zc, -q[1] / zc, 0.3 / zc
            gx, gy = np.meshgrid(np.array([-2, 0, 2]) * w_ / 3, np.array([-2.5, -1.5, -0.5]) * w_ / 3)
            qx, qy = to_depth_px(us + gx.ravel(), vs + gy.ravel())
            ix, iy = qx.astype(int), qy.astype(int)
            m = (ix >= 0) & (ix < 240) & (iy >= 0) & (iy < 135)
            blocked.append(m.sum() >= 5 and int((Z[iy[m], ix[m]] < zc - max(1.5, 0.15 * zc)).sum()) >= 5)
        if blocked[-1] and sum(blocked) >= 3:
            occ.append(OCC_HIDDEN)
            continue
        hidden = 0.0
        for h, x in zip(OCC_LEVELS, blocked):
            if not x:
                break
            hidden = h + 0.15
        occ.append(OCC_HIDDEN if hidden >= OCC_LEVELS[-1] else hidden)
    o = np.array(occ)
    if len(o) >= 3:  # 2m마다 켜졌다 꺼졌다 하지 않게 (ahead_rows와 같음)
        def slide(v, r, f):
            pad = np.r_[[v[0]] * r, v, [v[-1]] * r]
            return f(np.stack([pad[k:k + len(v)] for k in range(2 * r + 1)]), axis=0)
        o = slide(slide(o, 2, np.max), 2, np.min)
        o = slide(slide(o, 1, np.min), 1, np.max)
    return [r[:6] + [round(float(v), 2)] for r, v in zip(rows, o)]


def apply_manual(frames, frame_times, video_id, s_at):
    """직접 찍은 장면(sfm-fix.html, 1초 간격)이 있으면 그 장면과 사이 장면의 앞길을 그걸로 바꾼다.
    사이 장면은 앞뒤 찍은 장면의 앞길을 같은 거리끼리 이어서. 카메라 높이는 근처 장면 앞길의 발 높이로"""
    manual_file = WORK / f"manual_{video_id}.json"
    if not manual_file.exists():
        return 0
    marks = json.loads(manual_file.read_text(encoding="utf-8")).get("frames", {})
    foot_h = [(t_, -f[0][1]) for t_, f in zip(frame_times, frames) if f and 0.6 < -f[0][1] < 2.0]

    def cam_h(t):
        near = [h_ for t_, h_ in foot_h if abs(t_ - t) < 30]
        return float(np.median(near)) if near else 1.2

    keys = sorted((float(k), manual_rows(m, cam_h(float(k)))) for k, m in marks.items())
    keys = [(t, r) for t, r in keys if r]
    keys = smooth_manual(keys, s_at)
    kt = np.array([t for t, _ in keys])
    count = 0
    for n, t in enumerate(frame_times):
        if not len(kt) or t < kt[0] - 0.6 or t > kt[-1] + 0.6:
            continue
        j = int(np.searchsorted(kt, t))
        a = keys[max(j - 1, 0)]
        b = keys[min(j, len(keys) - 1)]
        if b[0] - a[0] > 1.6:  # 찍은 장면 사이가 너무 멀면(다른 구간) 가까운 쪽만
            a = b = a if t - a[0] < b[0] - t else b
        if abs(t - a[0]) > 1.6 and abs(t - b[0]) > 1.6:
            continue
        u = 0.0 if b[0] == a[0] else (t - a[0]) / (b[0] - a[0])
        m = min(len(a[1]), len(b[1]))
        rows = [[round(x + (y - x) * u, 3) for x, y in zip(ra, rb)] for ra, rb in zip(a[1][:m], b[1][:m])]
        frames[n] = occlude_by_depth(rows, DEPTH.get(t)) if DEPTH is not None else rows
        count += 1
    return count


def sane_rows(rows):
    """말이 안 되는 앞길을 잘라낸다 (잘못 이어 붙은 곳에서 아바타가 몇 m 뜨거나 박히지 않게).
    앞길 점은 길을 따라 2m 간격이므로, 이웃 점 사이 3D 거리가 0.7~5m(이음새의 작은 배율 차이는 허용)이고 경사가 0.25(약 14°, 카메라 기울기 포함. 영상 전체 99%가 0.17 이하) 이하여야 한다.
    처음(2m 앞) 점은 카메라 아래 0.3~2.6m. 30m 안에서 어긋나면 장면 전체를 버려 이전 방식으로 그리게 한다"""
    if not rows:
        return rows
    x0, y0, z0 = rows[0][:3]
    if not (-2.6 <= y0 <= -0.3 and 1.0 <= math.hypot(x0, y0, z0) <= 5.0):
        return []
    def ok(pa, pb, n=1):
        dx, dy, dz = (pb[i] - pa[i] for i in range(3))
        flat = math.hypot(dx, dz)
        return 0.7 * n <= math.hypot(flat, dy) <= 5.0 * n and abs(dy) <= 0.25 * max(flat, 1e-6)

    rows = [list(r) for r in rows]
    k = 1
    while k < len(rows):
        if ok(rows[k - 1], rows[k]):
            k += 1
            continue
        # 점 1~3개만 살짝 꺾였고 그 뒤가 다시 맞으면(이음새의 작은 흔들림) 앞뒤 점 사이를 이어서 고친다
        for m in range(k + 1, min(k + 4, len(rows))):
            if ok(rows[k - 1], rows[m], m - k + 1) and (m + 1 >= len(rows) or ok(rows[m], rows[m + 1])):
                for j in range(k, m):
                    u = (j - k + 1) / (m - k + 1)
                    rows[j] = [round(a + (b - a) * u, 3) for a, b in zip(rows[k - 1], rows[m])]
                k = m + 1
                break
        else:
            return [] if (k + 1) * STEP_M < 30 else rows[:k]
    # 점마다는 완만해도 꾸준히 솟거나 꺼지는 길(27:19 등): 처음 점 대비 높이 차가 거리의 12% + 0.4m를 넘으면 거기까지
    for k in range(1, len(rows)):
        d = (k + 1) * STEP_M
        if abs(rows[k][1] - rows[0][1]) > 0.12 * d + 0.4:
            return [] if d < 30 else rows[:k]
    return rows


def best_rows(segments, t, s_at, fps, respect_bad=True):
    """장면 t가 여러 묶음에 들어 있으면(이어 붙이지 못한 겹친 조각들) 말이 되는 앞길이 가장 긴 쪽을 쓴다"""
    key = int(round(t * fps))
    best = []
    for g in segments:
        i = np.searchsorted(g["keys"], key)
        if i < len(g["keys"]) and g["keys"][i] == key:
            rows = sane_rows(ahead_rows(g, i, s_at, respect_bad))
            if len(rows) > len(best):
                best = rows
    return best


def ahead_rows(g, i, s_at, respect_bad=True):
    """묶음 g의 장면 i에서 앞으로 2·4·…·AHEAD_M m의 발밑 위치와 길 오른쪽 방향 (카메라 기준, m)."""
    ts, C, R, up, upm, hm = g["t"], g["C"], g["R"], g["up"], g["upm"], g["hm"]
    # 거리 -> 장면: 3D 경로의 실제 길이(m)로 잰다. 진행 막대 거리표는 잠깐씩 멈칫해서(11:48 등)
    # 그대로 쓰면 앞길 점 간격이 2m가 아니라 5m로 늘어난다. 전체 축척은 이미 막대 거리에 맞춰져 있다(smooth_scale)
    step_m = np.linalg.norm(np.diff(C, axis=0), axis=1) / ((upm[1:] + upm[:-1]) / 2)
    s_all = np.r_[0.0, np.cumsum(step_m)]
    s_now = s_all[i]
    occluder = ScreenOccluder(g["tree"], C[i], R[i], upm[i], ts[i])
    rows = []
    for d in np.arange(STEP_M, AHEAD_M + 1e-6, STEP_M):
        if s_now + d > s_all[-1]:
            break
        j = int(np.searchsorted(s_all, s_now + d))
        j = max(1, min(j, len(ts) - 1))
        if respect_bad and any(a <= ts[j] <= b for a, b in BAD_RANGES):
            break  # 앞길이 틀린 구간으로 들어가면 거기까지만
        u = (s_now + d - s_all[j - 1]) / (s_all[j] - s_all[j - 1] or 1)
        u = min(max(u, 0.0), 1.0)
        up_j = up[j] / np.linalg.norm(up[j])
        foot = lambda m: C[m] - up_j * hm[m] * upm[m]  # noqa: E731
        X = foot(j - 1) + (foot(j) - foot(j - 1)) * u
        fwd = C[min(j + 2, len(C) - 1)] - C[max(j - 2, 0)]
        right = np.cross(fwd, up_j)
        right /= np.linalg.norm(right) or 1
        x = R[i] @ (X - C[i]) / upm[i]  # 카메라 i 기준 (x 오른쪽, y 아래, z 앞), m
        rv = R[i] @ right
        occ = occluder.hidden(X, up_j)
        rows.append([round(float(x[0]), 2), round(float(-x[1]), 2), round(float(-x[2]), 2),
                     round(float(rv[0]), 3), round(float(-rv[1]), 3), round(float(-rv[2]), 3), round(occ, 2)])
    # 먼 곳은 점이 듬성듬성해서 가려짐이 2m마다 켜졌다 꺼졌다 한다: 양옆이 가려졌으면 사이도 가려진 것으로,
    # 양옆이 다 보이는데 혼자 가려진 점은 보이는 것으로 (닫기 후 열기)
    o = np.array([r[6] for r in rows])
    if len(o) >= 3:
        def slide(v, r, f):
            pad = np.r_[[v[0]] * r, v, [v[-1]] * r]
            return f(np.stack([pad[k:k + len(v)] for k in range(2 * r + 1)]), axis=0)
        o = slide(slide(o, 2, np.max), 2, np.min)  # 사이 메우기 (4m 틈까지)
        o = slide(slide(o, 1, np.min), 1, np.max)  # 외톨이 지우기 (2m짜리)
        for r, v in zip(rows, o):
            r[6] = round(float(v), 2)
    return rows




def write_sync(segments, video_id, s_at, t0, t1, fps):
    """촬영자 진행 거리표(course.json의 sync)를 3D 경로의 실제 이동 거리로 다시 만든다.
    진행 막대는 2초 계단 + 가끔 멈칫이라 촬영 속도가 출렁이고, 그 속도로 영상 배속을 정하니 배속도 출렁였다.
    3D 경로의 장면별 이동 거리(m)로 속도를 구해 부드럽게 한 뒤, 60초마다 원래 막대 거리에 맞춰(총 거리는 그대로) 적분"""
    tt = np.arange(t0, t1, 1 / fps)
    v_bar = np.gradient(s_at(tt), tt)
    v = np.full(len(tt), np.nan)
    for g in segments:
        if len(g["t"]) < 8:
            continue
        step = np.linalg.norm(np.diff(g["C"], axis=0), axis=1) / ((g["upm"][1:] + g["upm"][:-1]) / 2)
        dt = np.diff(g["t"])
        ok = dt > 1e-3
        vt = np.full(len(step), np.nan)
        vt[ok] = step[ok] / dt[ok]
        tm = (g["t"][1:] + g["t"][:-1]) / 2
        idx = np.round((tm - t0) * fps).astype(int)
        m = (idx >= 0) & (idx < len(tt)) & np.isfinite(vt)
        for k, val in zip(idx[m], vt[m]):
            if np.isnan(v[k]):
                v[k] = val
    for a, b in BAD_RANGES:  # 3D가 틀린 구간은 막대 속도
        v[(tt >= a) & (tt <= b)] = np.nan
    med = np.nanmedian(v)
    v = np.clip(v, 0.3 * med, 2.0 * med)
    v = np.where(np.isnan(v), v_bar, v)
    k = int(3 * fps)  # 3초 평균 (걸음마다 출렁임 제거)
    v = np.convolve(np.r_[[v[0]] * k, v, [v[-1]] * k], np.ones(2 * k + 1) / (2 * k + 1), "valid")
    s_new = s_at(t0) + np.r_[0, np.cumsum((v[1:] + v[:-1]) / 2 * np.diff(tt))]
    # 60초마다 원래 막대 거리에 맞춤 (구간 안에서는 3D 속도 모양, 구간 끝 거리는 막대와 같게)
    anchors = np.arange(t0, t1 + 1e-6, 60.0)
    if anchors[-1] < tt[-1]:
        anchors = np.r_[anchors, tt[-1]]
    s_fix = s_new.copy()
    for a, b in zip(anchors[:-1], anchors[1:]):
        m = (tt >= a) & (tt <= b)
        if m.sum() < 2:
            continue
        na, nb = np.interp([a, b], tt, s_new)
        oa, ob = s_at(a), s_at(b)
        s_fix[m] = oa + (s_new[m] - na) * ((ob - oa) / ((nb - na) or 1))
    path = VIDEO_DIR / f"{video_id}.course.json"
    c = json.loads(path.read_text(encoding="utf-8"))
    ot, os_ = np.array(c["sync"]["t"]), np.array(c["sync"]["s"])
    keep_a, keep_b = ot < t0, ot > tt[-1]
    t_all = np.r_[ot[keep_a], tt, ot[keep_b]]
    s_all = np.maximum.accumulate(np.r_[os_[keep_a], s_fix, os_[keep_b]])
    c["sync"] = {"t": [round(float(x), 2) for x in t_all], "s": [round(float(x), 2) for x in s_all]}
    tmp = path.with_suffix(".json.tmp")
    tmp.write_text(json.dumps(c, ensure_ascii=False), encoding="utf-8")
    tmp.replace(path)
    kmh = v * 3.6
    print(f"거리표 다시 씀: 촬영 속도 {np.percentile(kmh, 5):.1f}~{np.percentile(kmh, 95):.1f} km/h (5~95%), 중간 {np.median(kmh):.1f}")


def export_view(segments, video_id, fps, max_points=250_000):
    """3D 복원 뷰어(run3d/sfm-view.html)용: 묶음마다 카메라 경로·자세와 3D 점(m, 위쪽 = +y, 첫 카메라가 원점)."""
    out_dir = VIDEO_DIR / f"{video_id}.sfmview"
    out_dir.mkdir(exist_ok=True)
    rng = np.random.default_rng(0)
    index = {"fps": fps, "segments": []}
    for n, g in enumerate(segments):
        up0 = np.median(g["up"], axis=0)
        up0 /= np.linalg.norm(up0)
        # up0 -> +y 로 돌리는 회전 (로드리게스)
        v = np.cross(up0, [0, 1, 0])
        c = float(up0 @ [0, 1, 0])
        vx = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
        Rup = np.eye(3) + vx + vx @ vx / (1 + c) if c > -0.999 else np.diag([1, -1, -1])
        s = float(np.median(g["upm"]))
        o = g["C"][0]
        to_m = lambda X: ((Rup @ (X - o).T).T / s).astype(np.float32)  # noqa: E731
        Cm = to_m(g["C"])
        # 장면 i의 앞길(카메라 기준 m, three.js 축)을 화면 좌표로: X = Cm + M @ (x, -y, -z)
        M = np.array([Rup @ R.T * (u / s) for R, u in zip(g["R"], g["upm"])], dtype=np.float32)
        P = g["tree"].data if g["tree"] is not None else np.zeros((0, 3))
        if len(P) > max_points:
            P = P[rng.choice(len(P), max_points, replace=False)]
        Pm = to_m(P)
        (out_dir / f"seg{n}.bin").write_bytes(Pm.tobytes())
        index["segments"].append({
            "keys": g["keys"].tolist(), "points": f"seg{n}.bin", "count": int(len(Pm)),
            "C": np.round(Cm, 2).tolist(), "M": np.round(M.reshape(len(M), 9), 4).tolist()})
    (out_dir / "index.json").write_text(json.dumps(index, separators=(",", ":")), encoding="utf-8")
    print(f"뷰어 데이터: {out_dir} (묶음 {len(segments)}개, 점 {sum(x['count'] for x in index['segments'])}개)")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("video_id")
    p.add_argument("--start", type=float, required=True)
    p.add_argument("--end", type=float, required=True)
    p.add_argument("--fps", type=float, default=3)
    p.add_argument("--chunk", type=float, default=90)
    p.add_argument("--overlap", type=float, default=30)
    p.add_argument("--only-chunk", type=int, default=None, help="이 번호 구간만")
    p.add_argument("--worker", type=int, default=0, help="병렬 실행: 이 작업 번호 (0부터)")
    p.add_argument("--workers", type=int, default=1, help="병렬 실행: 전체 작업 수 (구간을 번갈아 나눠 맡음)")
    p.add_argument("--assemble", action="store_true", help="구간 결과들을 모아 sfm.json 작성")
    p.add_argument("--patch", type=float, nargs=2, metavar=("T0", "DUR"), help="구멍 메움 조각 하나 계산 (영상 초)")
    p.add_argument("--find-gaps", action="store_true", help="합친 결과에서 구멍을 찾아 메움 조각 창 출력 (JSON)")
    p.add_argument("--export-view", action="store_true", help="3D 복원 뷰어용 점·경로 내보내기")
    p.add_argument("--write-sync", action="store_true", help="촬영자 진행 거리표를 3D 경로 이동 거리로 다시 쓰기")
    p.add_argument("--manual-only", action="store_true", help="합친 결과(sfm.json)에 직접 찍은 장면만 빠르게 덮어쓰기")
    p.add_argument("--mask", action="store_true", help="메움 조각: 화면 글자·하늘을 가리고 계산 (patchm_*.npz)")
    args = p.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    video = VIDEO_DIR / f"{args.video_id}.local.mp4"
    course = json.loads((VIDEO_DIR / f"{args.video_id}.course.json").read_text(encoding="utf-8"))
    st, ss = np.array(course["sync"]["t"]), np.array(course["sync"]["s"])
    s_at = lambda t: np.interp(t, st, ss)  # noqa: E731
    starts = list(np.arange(args.start, args.end, args.chunk - args.overlap))
    WORK.mkdir(parents=True, exist_ok=True)

    global DEPTH
    if (WORK / "depth").exists():
        DEPTH = DepthStore(args.start, args.fps)
    if args.manual_only:
        path = VIDEO_DIR / f"{args.video_id}.sfm.json"
        d = json.loads(path.read_text(encoding="utf-8"))
        times = d["t0"] + np.arange(len(d["frames"])) / d["fps"]
        n = apply_manual(d["frames"], times, args.video_id, s_at)
        tmp = path.with_suffix(".json.tmp")
        tmp.write_text(json.dumps(d, separators=(",", ":")), encoding="utf-8")
        tmp.replace(path)
        print(f"직접 찍은 길 적용: {n}장면")
        return

    if args.find_gaps:
        d = json.loads((VIDEO_DIR / f"{args.video_id}.sfm.json").read_text(encoding="utf-8"))
        print(json.dumps(find_gaps(d["frames"], d["t0"], d["fps"])))
        return

    if args.patch:
        t0, dur = args.patch
        tag = "patchm" if args.mask else "patch"
        res_path = WORK / f"{tag}_{int(round(t0 * 10)):05d}.npz"
        started = time.time()
        out = run_chunk(video, t0, dur, args.fps, WORK / f"{tag[1:]}{int(round(t0 * 10)):05d}", mask=args.mask)
        if out is None:
            print(f"메움 {t0:.0f}s+{dur:.0f}s 복원 실패", flush=True)
            return
        rec, names = out
        times, C, R, up, upm, height, _ = chunk_tracks(rec, names, t0, args.fps, s_at)
        P = np.array([pt.xyz for pt in rec.points3D.values() if pt.track.length() >= 3], dtype=np.float32)
        np.savez(res_path, times=times, C=C, R=R, up=up, upm=upm, height=height,
                 reg=len(times), total=len(names), P=P, retried=1)
        shutil.rmtree(WORK / f"{tag[1:]}{int(round(t0 * 10)):05d}", ignore_errors=True)
        print(f"메움 {t0:.0f}s+{dur:.0f}s: {len(times)}/{len(names)} 복원 ({times.min():.1f}~{times.max():.1f}s), "
              f"{time.time() - started:.0f}s", flush=True)
        return

    if not args.assemble and not args.export_view and not args.write_sync:
        def has_points(f):
            """3D 점까지 있고 80% 이상 조립된 조각인지 (아니면 다시 계산)"""
            try:
                z = np.load(f)
                return "P" in z.files and (int(z["reg"]) >= 0.8 * int(z["total"]) or "retried" in z.files)
            except Exception:
                return False

        def pid_alive(pid):
            if os.name != "nt":
                try:
                    os.kill(pid, 0)
                    return True
                except OSError:
                    return False
            import ctypes
            k32 = ctypes.windll.kernel32
            h = k32.OpenProcess(0x1000, False, pid)  # PROCESS_QUERY_LIMITED_INFORMATION
            if not h:
                return False
            code = ctypes.c_ulong()
            k32.GetExitCodeProcess(h, ctypes.byref(code))
            k32.CloseHandle(h)
            return code.value == 259  # STILL_ACTIVE

        def claim(ci):
            """다른 작업이 이미 맡은 조각이면 False (잠금 파일에 맡은 프로세스 번호)"""
            lock = WORK / f"c{ci:03d}.lock"
            for _ in range(2):
                try:
                    fd = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
                except FileExistsError:
                    try:
                        pid = int(lock.read_text())
                    except (OSError, ValueError):
                        pid = 0
                    if pid_alive(pid):
                        return False
                    lock.unlink(missing_ok=True)
                    continue
                os.write(fd, str(os.getpid()).encode())
                os.close(fd)
                return True
            return False

        # 이미 있지만 부실한 조각(3D 점 없음, 80% 미만인데 재시도 전) 먼저 다시, 그다음 아직 없는 조각
        todo = [ci for ci in range(len(starts)) if (WORK / f"chunk{ci:03d}.npz").exists()
                and not has_points(WORK / f"chunk{ci:03d}.npz")]
        todo += [ci for ci in range(len(starts)) if not (WORK / f"chunk{ci:03d}.npz").exists()]
        # 작업들은 같은 순서로 돌며 아직 아무도 안 맡은 조각을 먼저 잡는다 (따로 재시작해도 겹치지 않음)
        for ci in todo:
            t0 = starts[ci]
            if args.only_chunk is not None and ci != args.only_chunk:
                continue
            res_path = WORK / f"chunk{ci:03d}.npz"
            if res_path.exists() and has_points(res_path):
                continue
            if not claim(ci):
                continue
            try:
                started = time.time()
                out = run_chunk(video, t0, args.chunk, args.fps, WORK / f"c{ci:03d}")
                if out is None:
                    print(f"구간 {ci} ({t0:.0f}s) 복원 실패", flush=True)
                    continue
                rec, names = out
                times, C, R, up, upm, height, _ = chunk_tracks(rec, names, t0, args.fps, s_at)
                # 주변 지형의 3D 점 (가려짐 계산용): 여러 장면에서 확인된 점만
                P = np.array([pt.xyz for pt in rec.points3D.values() if pt.track.length() >= 3], dtype=np.float32)
                # retried: 조건 완화 재시도까지 거친 결과 (80% 미만이어도 다시 계산하지 않음)
                np.savez(res_path, times=times, C=C, R=R, up=up, upm=upm, height=height,
                         reg=len(times), total=len(names), P=P, retried=1)
                shutil.rmtree(WORK / f"c{ci:03d}", ignore_errors=True)
                print(f"구간 {ci} ({t0:.0f}s): {len(times)}/{len(names)} 복원, 높이 {height / upm:.2f}m, "
                      f"{time.time() - started:.0f}s", flush=True)
            except Exception as e:  # 한 조각이 이상해도 나머지는 계속
                print(f"구간 {ci} ({t0:.0f}s) 복원 실패: {type(e).__name__}: {e}", flush=True)
            finally:
                (WORK / f"c{ci:03d}.lock").unlink(missing_ok=True)
        return

    # 모으기: 조각들을 겹치는 장면으로 이어 붙여 하나의 3D 경로로 만든 뒤, 장면마다 앞길을 계산
    chunks = load_all_chunks(starts, s_at)
    segments = stitch(chunks, args.fps)
    smooth_scale(segments, s_at, args.fps)
    if args.export_view:
        export_view(segments, args.video_id, args.fps)
        return
    if args.write_sync:
        bad_file = WORK / "bad_ranges.json"
        if bad_file.exists():
            BAD_RANGES[:] = json.loads(bad_file.read_text(encoding="utf-8"))["ranges"]
        write_sync(segments, args.video_id, s_at, args.start, args.end, args.fps)
        return
    frame_times = np.arange(args.start, args.end, 1 / args.fps)
    bad_file = WORK / "bad_ranges.json"
    old_ranges = []
    if bad_file.exists():
        bad_cfg = json.loads(bad_file.read_text(encoding="utf-8"))
        BAD_RANGES[:] = bad_cfg["ranges"]
        old_ranges = bad_cfg.get("old", [])
    frames = [best_rows(segments, t, s_at, args.fps) for t in frame_times]
    smooth_occlusion_in_time(frames)
    # 눈으로 확인해 앞길이 틀린 구간(바다로 가거나 옹벽을 뚫는 등)은 비워서 예전 방식으로 그리게 한다
    # 틀린 구간: 이어 붙인 경로 대신, 그 자리를 거의 다 복원한 조각 하나만으로 (조각끼리 이어 붙이다 망가진 곳).
    # 조각 하나 안에서는 모양이 맞으므로 앞길은 그 조각 끝까지만 보인다. 그런 조각이 없으면 비워서 예전 방식
    solo = []
    if BAD_RANGES:
        for ch in chunks:
            if ch.get("full") and any(ch["times"][0] < b and ch["times"][-1] > a for a, b in BAD_RANGES):
                solo += stitch([dict(ch)], args.fps)
        smooth_scale(solo, s_at, args.fps)
    solo_masked = [g for g, ch in zip(solo, [c for c in chunks if c.get("full") and any(
        c["times"][0] < b and c["times"][-1] > a for a, b in BAD_RANGES)]) if str(ch["ci"]).startswith("patchm")]
    for n, t in enumerate(frame_times):
        if any(a <= t <= b for a, b in BAD_RANGES):
            rows = best_rows(solo_masked, t, s_at, args.fps, respect_bad=False) if solo_masked else []
            frames[n] = rows or (best_rows(solo, t, s_at, args.fps, respect_bad=False) if solo else [])
        if any(a <= t <= b for a, b in old_ranges):
            frames[n] = []
    apply_manual(frames, frame_times, args.video_id, s_at)
    vfov = math.degrees(2 * math.atan(H / 2 / F_640))
    out = {"fps": args.fps, "t0": args.start, "fov": round(vfov, 2), "k": K1, "stepM": STEP_M, "frames": frames}
    path = VIDEO_DIR / f"{args.video_id}.sfm.json"
    # 임시 파일에 쓴 뒤 바꿔치기: 브라우저가 쓰는 도중의 반쪽 파일을 읽지 않게
    tmp = path.with_suffix(".json.tmp")
    tmp.write_text(json.dumps(out, separators=(",", ":")), encoding="utf-8")
    tmp.replace(path)
    filled = sum(1 for f in frames if f)
    print(f"저장: {path} ({filled}/{len(frames)} 장면, 세로 화각 {vfov:.1f}°)")


if __name__ == "__main__":
    main()
