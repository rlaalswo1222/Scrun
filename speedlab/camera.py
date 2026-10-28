"""전면 카메라 영상에서 머리·어깨의 상하 움직임을 뽑는다 (MediaPipe Pose Landmarker)."""
import urllib.request
from pathlib import Path

import numpy as np
import pandas as pd

MODEL_URL = ("https://storage.googleapis.com/mediapipe-models/pose_landmarker/"
             "pose_landmarker_{v}/float16/latest/pose_landmarker_{v}.task")
NOSE, L_SHOULDER, R_SHOULDER = 0, 11, 12


def ensure_model(variant="full", cache_dir="models"):
    path = Path(cache_dir) / f"pose_landmarker_{variant}.task"
    if not path.exists():
        path.parent.mkdir(parents=True, exist_ok=True)
        print(f"포즈 모델 다운로드 중: {path.name}")
        urllib.request.urlretrieve(MODEL_URL.format(v=variant), path)
    return path


def extract_head_track(video_path, variant="full"):
    """반환: DataFrame(t, nose_y, shoulder_y).

    y는 화면 높이 대비 위치(0~1). 사람을 찾지 못한 프레임은 NaN.
    """
    import cv2
    import mediapipe as mp
    from mediapipe.tasks.python import BaseOptions, vision

    cap = cv2.VideoCapture(str(video_path))
    if not cap.isOpened():
        raise ValueError(f"{video_path}: 영상을 열 수 없습니다")
    fps = cap.get(cv2.CAP_PROP_FPS)
    total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    options = vision.PoseLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=str(ensure_model(variant))),
        running_mode=vision.RunningMode.VIDEO,
        num_poses=1,
    )
    rows = []
    with vision.PoseLandmarker.create_from_options(options) as landmarker:
        i = 0
        while True:
            ok, frame = cap.read()
            if not ok:
                break
            t = i / fps
            image = mp.Image(image_format=mp.ImageFormat.SRGB,
                             data=cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
            result = landmarker.detect_for_video(image, int(t * 1000))
            if result.pose_landmarks:
                lm = result.pose_landmarks[0]
                rows.append((t, lm[NOSE].y, (lm[L_SHOULDER].y + lm[R_SHOULDER].y) / 2))
            else:
                rows.append((t, np.nan, np.nan))
            i += 1
            if i % 300 == 0:
                print(f"  {i}/{total} 프레임", flush=True)
    cap.release()
    return pd.DataFrame(rows, columns=["t", "nose_y", "shoulder_y"])
