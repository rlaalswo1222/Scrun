"""7세그먼트 칸 하나 읽기(ADR-0012): YOLOX-Nano ONNX → 상자 → 글자. 서버 엔진과 speedlab/ocr(학습·채점)이 같이 쓰는 원본.

torch 없이 onnxruntime(MIT)·OpenCV·numpy만. 입력은 학습과 같은 전처리(letterbox: 비율 유지, 가로 320 × 세로 160,
회색 채움, BGR 0–255). 출력(YOLOX 디코드 포함) [1, N, 5 + 분류] → 점수 = 물체 × 분류 → 겹침 정리 → 글자 규칙:
1. 신뢰도 문턱, 분류 상관없이 겹친 상자 정리(같은 자리에 두 숫자를 읽지 않게)
2. 왼쪽부터 정렬. '.'은 숫자 높이의 아래쪽 40%에 있을 때만(위쪽 점은 표시등)
3. 숫자 높이가 다른 줄(작은 단위·다른 칸)은 버림: 가장 큰 숫자 높이의 60%보다 작으면 뺌
"""
import cv2
import numpy as np
import onnxruntime as ort

CLASSES = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "-", ".", ":"]   # 모델 출력 순서(학습과 같아야 함)
DOT, COLON, DASH = CLASSES.index("."), CLASSES.index(":"), CLASSES.index("-")
IN_H, IN_W = 160, 320
READ_CONF = 0.4   # 글자로 쓰는 상자 점수 문턱


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


def _iou(a, b):
    x0, y0 = max(a[0], b[0]), max(a[1], b[1])
    x1, y1 = min(a[2], b[2]), min(a[3], b[3])
    inter = max(0, x1 - x0) * max(0, y1 - y0)
    return inter / ((a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - inter + 1e-9)


def read_text(dets: np.ndarray, conf: float = 0.4, iou: float = 0.5, nms: bool = True) -> str:
    """dets: [N, 6] = x0, y0, x1, y1, 점수, 분류. nms=False면 이미 겹침을 정리한 상자(detect)로 보고 다시 안 함."""
    dets = dets[dets[:, 4] >= conf] if len(dets) else dets
    if not nms:
        keep = list(dets)
    else:
        keep = []
        for d in dets[np.argsort(-dets[:, 4])] if len(dets) else []:
            if all(_iou(d, k) < iou for k in keep):
                keep.append(d)
    if not keep:
        return ""
    keep = np.array(keep)
    # 숫자 높이·윗줄·밑줄은 숫자 칸으로만: "-"(가운데 짧은 가로획)가 섞이면 "-1.20"의 소수점을 위쪽 표시등으로 보고 버림(#61 리뷰)
    digits = keep[(keep[:, 5] != DOT) & (keep[:, 5] != COLON) & (keep[:, 5] != DASH)]
    if len(digits):
        tall = (digits[:, 3] - digits[:, 1]).max()
        bottom = np.median(digits[:, 3])
        top = np.median(digits[:, 1])
        ok = []
        for d in keep:
            c = int(d[5])
            h = d[3] - d[1]
            if c == DOT:
                cy = (d[1] + d[3]) / 2
                if cy < top + (bottom - top) * 0.6:
                    continue  # 표시등 점
            elif c == COLON:
                pass
            elif c != DASH and h < tall * 0.6:
                continue  # 작은 글자(다른 줄)
            ok.append(d)
        keep = np.array(ok)
    order = np.argsort((keep[:, 0] + keep[:, 2]) / 2)
    return "".join(CLASSES[int(keep[i, 5])] for i in order)


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
        # 글자 문턱(0.4)으로 한 번에 걸러 겹침 정리도 detect의 cv2 한 번만(같은 IoU 0.5·분류 상관없이, #61·#68 리뷰).
        # 낮은 문턱 detect는 채점(eval)에서 상자를 볼 때만
        return read_text(self.detect(img, conf=READ_CONF), conf=READ_CONF, nms=False)
