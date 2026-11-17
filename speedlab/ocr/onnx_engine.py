"""ONNX 모델로 계기판 칸 하나 읽기: torch 없이 onnxruntime(MIT)·OpenCV·numpy만(서버 엔진의 바탕, ADR-0012).

입력은 학습과 같은 전처리(dataset.letterbox: 비율 유지, 가로 320 × 세로 160, 회색 채움, BGR 0–255).
출력(YOLOX 디코드 포함) [1, N, 5 + 분류] → 점수 = 물체 × 분류 → 겹침 정리 → readout.read_text.
"""
import cv2
import numpy as np
import onnxruntime as ort

from dataset import letterbox
from readout import read_text

READ_CONF = 0.4   # 글자로 쓰는 상자 점수 문턱


class OnnxReader:
    def __init__(self, path: str, threads: int = 2):
        opts = ort.SessionOptions()
        opts.intra_op_num_threads = threads   # 오라클 서버 2코어(ADR-0008)
        opts.inter_op_num_threads = 1
        self.sess = ort.InferenceSession(path, opts, providers=["CPUExecutionProvider"])
        self.name = self.sess.get_inputs()[0].name

    def detect(self, img: np.ndarray, conf: float = 0.05) -> np.ndarray:
        """BGR 그림 → [N, 6] = 원래 그림 좌표 x0, y0, x1, y1, 점수, 분류."""
        canvas, r, (ox, oy) = letterbox(img)
        x = canvas.transpose(2, 0, 1)[None].astype(np.float32)
        out = self.sess.run(None, {self.name: x})[0][0]
        cls = out[:, 5:].argmax(1)
        score = out[:, 4] * out[np.arange(len(out)), 5 + cls]
        m = score >= conf
        out, cls, score = out[m], cls[m], score[m]
        cx, cy, w, h = out[:, 0], out[:, 1], out[:, 2], out[:, 3]
        boxes = np.stack([cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2], 1)
        xywh = np.concatenate([boxes[:, :2], boxes[:, 2:] - boxes[:, :2]], 1)
        keep = cv2.dnn.NMSBoxes(xywh.tolist(), score.tolist(), conf, 0.5) if len(boxes) else []  # 분류 상관없이
        keep = np.array(keep, int).reshape(-1)
        boxes = boxes[keep]
        boxes[:, [0, 2]] = (boxes[:, [0, 2]] - ox) / r
        boxes[:, [1, 3]] = (boxes[:, [1, 3]] - oy) / r
        return np.concatenate([boxes, score[keep, None], cls[keep, None].astype(np.float32)], 1)

    def read(self, img: np.ndarray) -> str:
        # 글자 문턱(0.4)으로 한 번에 걸러 겹침 정리도 한 번만(0.05로 다 정리한 뒤 다시 거르면 서버 CPU 낭비, #61 리뷰).
        # 낮은 문턱 detect는 채점(eval)에서 상자를 볼 때만
        return read_text(self.detect(img, conf=READ_CONF), conf=READ_CONF)
