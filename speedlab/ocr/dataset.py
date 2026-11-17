"""학습 데이터: 공개(Roboflow 7segmentrecognition_v3, 퍼블릭 도메인) + 합성(synth7seg)을 같은 분류로 읽는다.

입력은 가로 320 × 세로 160 고정(10/29: 정사각 320×320은 작은 사진에서 잔상을 읽어 나빠짐). 비율 유지 + 회색(114) 채움.
라벨은 YOLOX 머리가 받는 [분류, 중심x, 중심y, 너비, 높이](입력 픽셀).
"""
import json
from pathlib import Path

import cv2
import numpy as np
import torch

from synth7seg import CLASSES

IN_H, IN_W = 160, 320
MAX_LABELS = 32


# 공개 데이터(Roboflow 7segmentrecognition_v3 v1)의 분류 이름은 실제 글자와 두 칸씩 밀려 있다(11/17 그림으로 확인:
# 이름 '2' 상자가 실제 0, '1'이 소수점, '0'이 '-', '-'가 8, '.'이 9). 원래 순서 '- . 0 … 9'에 이름만 '0 … 9 - .'로 붙은 것(가설)
PUBLIC_NAME_FIX = {"0": "-", "1": ".", "2": "0", "3": "1", "4": "2", "5": "3", "6": "4", "7": "5", "8": "6", "9": "7",
                   "-": "8", ".": "9"}
PUBLIC_DIR = "7seg_v3"   # 이름 바로잡기는 이 공개 데이터 폴더에만. 우리가 Roboflow로 정답을 단 실사진(채점용)엔 걸지 않는다


def load_coco(folder: Path, images_dir: str = "images") -> list[tuple[Path, np.ndarray, str]]:
    """COCO 폴더 → [(그림 경로, 상자[N,5](분류, x0, y0, x1, y1), 정답 글자)]. 분류 이름으로 우리 CLASSES에 맞춤.
    Roboflow 내보내기(_annotations.coco.json)도 받는다. 이름 바로잡기(PUBLIC_NAME_FIX)는 공개 데이터 폴더(PUBLIC_DIR) 안에서만:
    우리 헬스장 사진에 Roboflow로 정답을 달아 채점할 때 정답이 뒤섞이지 않게(#61 리뷰)."""
    ann_file = folder / "annotations.json"
    if not ann_file.exists():
        ann_file = folder / "_annotations.coco.json"
        images_dir = "."
    fix = PUBLIC_NAME_FIX if PUBLIC_DIR in Path(folder).resolve().parts else {}
    j = json.loads(ann_file.read_text())
    name_of = {c["id"]: fix.get(c["name"], c["name"]) for c in j["categories"]}
    by_img: dict[int, list] = {im["id"]: [] for im in j["images"]}
    for a in j["annotations"]:
        name = name_of[a["category_id"]]
        if name not in CLASSES:
            continue  # 공개 데이터의 상위 분류 'numbers'
        x, y, w, h = a["bbox"]
        by_img[a["image_id"]].append((CLASSES.index(name), x, y, x + w, y + h))
    out = []
    for im in j["images"]:
        boxes = np.array(by_img[im["id"]], np.float32).reshape(-1, 5)
        # 정답 글자는 늘 상자에서(합성의 "text"엔 상자 없는 7세그먼트 글자·빈 자리도 들어 있어 채점이 틀림, 11/17)
        out.append((folder / images_dir / im["file_name"], boxes, text_from_boxes(boxes)))
    return out


def text_from_boxes(boxes: np.ndarray) -> str:
    """정답 상자 → 왼쪽부터 읽은 글자(공개 데이터엔 정답 글자가 없어 상자로 만듦)."""
    order = np.argsort((boxes[:, 1] + boxes[:, 3]) / 2) if len(boxes) else []
    return "".join(CLASSES[int(boxes[i, 0])] for i in order)


def letterbox(img: np.ndarray, scale_jitter: float = 0.0, rng=None):
    """비율 유지로 IN_W×IN_H 안에 넣음. (입력 그림, 배율, 왼쪽 위 위치)."""
    h, w = img.shape[:2]
    r = min(IN_H / h, IN_W / w)
    if rng is not None and scale_jitter:
        r *= rng.uniform(1 - scale_jitter, 1.0)
    nh, nw = max(1, int(round(h * r))), max(1, int(round(w * r)))
    canvas = np.full((IN_H, IN_W, 3), 114, np.uint8)
    oy = (IN_H - nh) // 2 if rng is None else int(rng.integers(0, IN_H - nh + 1))
    ox = (IN_W - nw) // 2 if rng is None else int(rng.integers(0, IN_W - nw + 1))
    canvas[oy:oy + nh, ox:ox + nw] = cv2.resize(img, (nw, nh), interpolation=cv2.INTER_AREA if r < 1 else cv2.INTER_LINEAR)
    return canvas, r, (ox, oy)


def to_tensor(img: np.ndarray) -> torch.Tensor:
    """BGR uint8 → YOLOX 입력(BGR, 0–255 float, CHW). YOLOX 기본 전처리와 같게."""
    return torch.from_numpy(np.ascontiguousarray(img.transpose(2, 0, 1))).float()


class DigitDataset(torch.utils.data.Dataset):
    def __init__(self, items, train: bool, seed: int = 0):
        self.items, self.train, self.seed = items, train, seed

    def __len__(self):
        return len(self.items)

    def __getitem__(self, i):
        path, boxes, _ = self.items[i]
        img = cv2.imread(str(path), cv2.IMREAD_COLOR)
        rng = np.random.default_rng(None) if self.train else None
        if self.train:
            img = _color_jitter(img, rng)
        canvas, r, (ox, oy) = letterbox(img, 0.3 if self.train else 0.0, rng)
        labels = np.zeros((MAX_LABELS, 5), np.float32)
        b = boxes[:MAX_LABELS].copy()
        if len(b):
            b[:, [1, 3]] = b[:, [1, 3]] * r + ox
            b[:, [2, 4]] = b[:, [2, 4]] * r + oy
            labels[:len(b), 0] = b[:, 0]
            labels[:len(b), 1] = (b[:, 1] + b[:, 3]) / 2
            labels[:len(b), 2] = (b[:, 2] + b[:, 4]) / 2
            labels[:len(b), 3] = b[:, 3] - b[:, 1]
            labels[:len(b), 4] = b[:, 4] - b[:, 2]
        return to_tensor(canvas), torch.from_numpy(labels)


def _color_jitter(img, rng):
    """색·밝기만(뒤집기·큰 회전은 하지 않음: 2와 5, 6과 9가 바뀜)."""
    hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV).astype(np.int16)
    hsv[..., 0] = (hsv[..., 0] + int(rng.integers(-15, 16))) % 180
    hsv[..., 1] = np.clip(hsv[..., 1] * rng.uniform(0.6, 1.4), 0, 255)
    hsv[..., 2] = np.clip(hsv[..., 2] * rng.uniform(0.6, 1.4), 0, 255)
    return cv2.cvtColor(hsv.astype(np.uint8), cv2.COLOR_HSV2BGR)
