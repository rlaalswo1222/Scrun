"""검출 상자 → 칸에 뜬 글자(소수점·형식 규칙). 엔진과 상관없이 같은 규칙(ADR-0012).

1. 신뢰도 문턱, 분류 상관없이 겹친 상자 정리(같은 자리에 두 숫자를 읽지 않게)
2. 왼쪽부터 정렬. '.'은 숫자 높이의 아래쪽 40%에 있을 때만(위쪽 점은 표시등)
3. 숫자 높이가 다른 줄(작은 단위·다른 칸)은 버림: 가장 큰 숫자 높이의 60%보다 작으면 뺌
"""
import numpy as np

from synth7seg import CLASSES

DOT, COLON, DASH = CLASSES.index("."), CLASSES.index(":"), CLASSES.index("-")


def _iou(a, b):
    x0, y0 = max(a[0], b[0]), max(a[1], b[1])
    x1, y1 = min(a[2], b[2]), min(a[3], b[3])
    inter = max(0, x1 - x0) * max(0, y1 - y0)
    return inter / ((a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - inter + 1e-9)


def read_text(dets: np.ndarray, conf: float = 0.4, iou: float = 0.5) -> str:
    """dets: [N, 6] = x0, y0, x1, y1, 점수, 분류."""
    dets = dets[dets[:, 4] >= conf] if len(dets) else dets
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
