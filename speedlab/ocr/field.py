"""계기판 사진 전체 → 거리·시간(계획 0042 방법 B + A 보조).

1. 글자 영역 찾기: PP-OCRv5 검출(DBNet, RapidOCR·onnxruntime)이 사진의 글자 덩어리를 모두 찾는다
2. 읽기: 영역마다 PP-OCRv5 한국어 인식(라벨·LCD 글자)과 우리 YOLOX(7세그먼트 숫자, onnx_engine)
3. 이름 붙이기: 숫자 칸마다 가장 가까운 라벨(거리·DIST·km / 시간·TIME·min / 그 밖)을 짝짓는다.
   라벨이 없으면 형식 규칙(A): 콜론 → 시간, 소수 둘째 자리 → 거리
4. 판정: 거리·시간 후보가 하나씩일 때만 READ. 없거나 둘 이상이면 UNREADABLE(추측하지 않음)

센서 값은 쓰지 않는다(독립성, 계획 0038): 센서와 가까운 칸을 고르면 다른 칸이 우연히 맞아도 통과된다.
"""
import re
from dataclasses import dataclass, field as dfield
from typing import Optional

import cv2
import numpy as np

from onnx_engine import OnnxReader

SEG_CONFUSION = str.maketrans({"S": "5", "s": "5", "O": "0", "o": "0", "D": "0", "I": "1", "l": "1", "|": "1",
                               "B": "8", "Z": "2", "z": "2", "G": "6", "q": "9", ",": "."})
NUM = re.compile(r"^\d{1,3}(?:[.:]\d{1,2}){0,2}$")

# 라벨 낱말(대문자·공백 없앤 글자에서 찾음). 속도(km/h)를 거리(km)보다 먼저 본다
OTHER_WORDS = ["KM/H", "KMH", "KPH", "MPH", "속도", "SPEED", "페이스", "PACE", "/KM", "칼로리", "KCAL", "CAL",
               "경사", "INCLINE", "ELEV", "%", "심박", "PULSE", "BPM", "HR", "와트", "WATT", "LEVEL", "레벨"]
DIST_WORDS = ["거리", "DISTANCE", "DIST", "KM"]
TIME_WORDS = ["운동시간", "시간", "TIME", "MIN", "분"]


UNIT = re.compile(r"KM|MIN|KCAL|KM/H|KPH|%|BPM|M|분")   # 숫자 오른쪽에 붙는 단위(그 밖의 오른쪽 라벨은 다음 칸 것)


def _norm(text: str) -> str:
    return re.sub(r"\s+", "", text).upper()


def label_kind(text: str) -> Optional[str]:
    t = _norm(text)
    if not re.search(r"[A-Z가-힣%/]", t):
        return None
    if any(w in t for w in OTHER_WORDS):
        return "other"
    if any(w in t for w in DIST_WORDS):
        return "distance"
    if any(w in t for w in TIME_WORDS):
        return "time"
    return None


def parse_time(s: str) -> Optional[int]:
    """'28:40'·'28.40'(점으로 보이는 기종)·'1:02:05' → 초. 초 자리가 60 이상이면 None."""
    parts = re.split(r"[.:]", s)
    if not all(p.isdigit() for p in parts) or not 2 <= len(parts) <= 3 or any(len(p) != 2 for p in parts[1:]):
        return None
    v = [int(p) for p in parts]
    if any(x >= 60 for x in v[1:]):
        return None
    return v[0] * 3600 + v[1] * 60 + v[2] if len(v) == 3 else v[0] * 60 + v[1]


def parse_km(s: str) -> Optional[int]:
    if not re.fullmatch(r"\d{1,3}\.\d{1,2}", s):
        return None
    return int(round(float(s) * 1000))


@dataclass
class Region:
    box: np.ndarray            # 네 점(x, y)
    rec: str                   # PP-OCRv5 인식 글자
    rec_score: float
    seg: str = ""              # YOLOX 7세그먼트 읽기(숫자 영역만)
    kind: Optional[str] = None  # 라벨이면 distance·time·other
    number: Optional[str] = None  # 대표 숫자(그림·짝짓기용)
    cands: list = dfield(default_factory=list)  # 읽은 숫자 후보(prefer 순서): 7세그먼트·인식 글자
    name: Optional[str] = None  # 짝지은 뒤 distance·time·other
    how: str = ""               # label·rule

    @property
    def rect(self):
        x0, y0 = self.box.min(0)
        x1, y1 = self.box.max(0)
        return float(x0), float(y0), float(x1), float(y1)


@dataclass
class ConsoleRead:
    status: str                       # READ·UNREADABLE
    distance_m: Optional[int] = None
    duration_s: Optional[int] = None
    why: str = ""
    regions: list = dfield(default_factory=list)


class ConsoleReader:
    def __init__(self, seg_onnx: str, threads: int = 2, prefer: str = "seg"):
        from rapidocr import LangDet, LangRec, ModelType, OCRVersion, RapidOCR
        self.ocr = RapidOCR(params={
            "Det.ocr_version": OCRVersion.PPOCRV5, "Det.model_type": ModelType.MOBILE, "Det.lang_type": LangDet.CH,
            "Rec.ocr_version": OCRVersion.PPOCRV5, "Rec.model_type": ModelType.MOBILE, "Rec.lang_type": LangRec.KOREAN,
            "Global.use_cls": False, "Global.log_level": "error", "Global.text_score": 0.3,
            "EngineConfig.onnxruntime.intra_op_num_threads": threads,   # 오라클 2코어(ADR-0008)
            "EngineConfig.onnxruntime.inter_op_num_threads": 1})
        self.seg = OnnxReader(seg_onnx, threads)
        self.prefer = prefer   # seg: 7세그먼트 읽기 먼저, 안 되면 인식 글자 / rec: 반대

    def regions(self, img: np.ndarray) -> list:
        r = self.ocr(img)
        if r.boxes is None:
            return []
        out = [Region(np.asarray(b, np.float32), str(t), float(c)) for b, t, c in zip(r.boxes, r.txts, r.scores)]
        for reg in out:
            reg.kind = label_kind(reg.rec)
        out = merge_split(out)
        for reg in out:
            num_rec = self._number(reg.rec)
            if reg.kind is None or num_rec:      # 숫자가 보이는 영역만 7세그먼트로 다시 읽음("3.12 km"처럼 붙은 것 포함)
                reg.seg = self.seg.read(self._crop(img, reg))
                num_seg = reg.seg if NUM.match(reg.seg) else None
                order = (num_seg, num_rec) if self.prefer == "seg" else (num_rec, num_seg)
                reg.cands = list(dict.fromkeys(c for c in order if c))
                reg.number = reg.cands[0] if reg.cands else None
        return out

    @staticmethod
    def _number(text: str) -> Optional[str]:
        t = text.replace(" ", "")
        if label_kind(t) is None:   # 숫자 영역: 7세그먼트를 일반 글자로 읽을 때 흔한 혼동
            t = t.translate(SEG_CONFUSION)
        m = re.search(r"\d{1,3}(?:[.:]\d{1,2}){0,2}", t)
        return m.group(0) if m else None

    @staticmethod
    def _crop(img, reg: Region, pad: float = 0.15):
        x0, y0, x1, y1 = reg.rect
        h = y1 - y0
        x0, y0 = max(0, int(x0 - h * pad * 3)), max(0, int(y0 - h * pad))
        x1, y1 = min(img.shape[1], int(x1 + h * pad * 3)), min(img.shape[0], int(y1 + h * pad))
        return img[y0:y1, x0:x1]

    def read(self, img: np.ndarray) -> ConsoleRead:
        regs = self.regions(img)
        nums = [r for r in regs if r.number]
        labels = [r for r in regs if r.kind and not r.number]
        for r in nums:          # "3.12 km"처럼 단위가 붙은 숫자는 그 단위로
            if r.kind:
                r.name, r.how = r.kind, "label"
        pair(labels, [r for r in nums if not r.name])
        if any(r.kind for r in regs):   # 라벨이 있는 계기판은 라벨로만(규칙이 페이스 3:33을 시간으로 잡는 등 틀린 READ를 냄)
            return decide(regs)
        for r in nums:          # 라벨이 하나도 없으면(아이콘뿐) 형식 규칙(A)
            if not r.name:
                if ":" in r.number:
                    r.name, r.how = "time", "rule"
                elif re.fullmatch(r"\d{1,3}\.\d\d", r.number):
                    r.name, r.how = "distance", "rule"
        return decide(regs)


def merge_split(regs: list) -> list:
    """글자 영역 찾기가 넓은 틈(소수점·콜론·7세그먼트 1)에서 쪼갠 숫자를 다시 합친다.
    같은 줄, 틈 < 숫자 높이 0.6, 그리고 쪼개진 모양(앞이 . : 로 끝남, 뒤가 . : 로 시작, 한쪽이 숫자 하나)일 때만:
    옆 칸 숫자(틈이 좁은 계기판)까지 합치지 않게."""
    def numeric(r):
        return r.kind is None and re.search(r"\d", r.rec) is not None
    regs = sorted(regs, key=lambda r: r.rect[0])
    changed = True
    while changed:
        changed = False
        for i, a in enumerate(regs):
            if not numeric(a):
                continue
            ax0, ay0, ax1, ay1 = a.rect
            for b in regs[i + 1:]:
                if not numeric(b):
                    continue
                bx0, by0, bx1, by1 = b.rect
                h = min(ay1 - ay0, by1 - by0)
                yover = min(ay1, by1) - max(ay0, by0)
                split = (a.rec.rstrip()[-1:] in ".:" or b.rec.lstrip()[:1] in ".:"
                         or len(re.sub(r"\D", "", a.rec)) == 1 or len(re.sub(r"\D", "", b.rec)) == 1)
                if split and yover >= 0.6 * h and -0.2 * h <= bx0 - ax1 <= 0.6 * h:
                    x0, y0, x1, y1 = min(ax0, bx0), min(ay0, by0), max(ax1, bx1), max(ay1, by1)
                    m = Region(np.float32([[x0, y0], [x1, y0], [x1, y1], [x0, y1]]), a.rec + b.rec,
                               min(a.rec_score, b.rec_score))
                    regs = [r for r in regs if r is not a and r is not b] + [m]
                    regs.sort(key=lambda r: r.rect[0])
                    changed = True
                    break
            if changed:
                break
    return regs


def pair(labels: list, nums: list) -> None:
    """라벨과 숫자를 가까운 순서로 하나씩 짝짓는다(라벨은 숫자 위·아래·왼쪽·오른쪽, 거리는 숫자 높이로 나눔)."""
    cands = []
    for li, L in enumerate(labels):
        lx0, ly0, lx1, ly1 = L.rect
        lcx, lcy = (lx0 + lx1) / 2, (ly0 + ly1) / 2
        for ni, N in enumerate(nums):
            nx0, ny0, nx1, ny1 = N.rect
            h = max(1.0, ny1 - ny0)
            xover = min(lx1, nx1 + h) - max(lx0, nx0 - h) > 0           # 가로로 겹침(숫자 높이만큼 여유)
            yover = min(ly1, ny1) - max(ly0, ny0) > 0
            if xover and ly1 <= ny0 + h * 0.3:                          # 라벨이 위
                gap = (ny0 - ly1) / h
            elif xover and ly0 >= ny1 - h * 0.3:                        # 라벨이 아래
                gap = (ly0 - ny1) / h
            elif yover and lx1 <= nx0 + h * 0.3:                        # 라벨이 왼쪽
                gap = (nx0 - lx1) / h
            elif yover and lx0 >= nx1 - h * 0.3 and (lx0 - nx1) / h <= 0.6 and UNIT.fullmatch(_norm(L.rec)):  # 단위만 오른쪽
                gap = (lx0 - nx1) / h + 0.5
            else:
                continue
            gap += abs(lcx - (nx0 + nx1) / 2) / h * 0.15 if (ly1 <= ny0 or ly0 >= ny1) else abs(lcy - (ny0 + ny1) / 2) / h * 0.15
            if gap <= 2.5:
                cands.append((gap, li, ni))
    used_l, used_n = set(), set()
    for gap, li, ni in sorted(cands):
        if li in used_l or ni in used_n:
            continue
        used_l.add(li)
        used_n.add(ni)
        nums[ni].name, nums[ni].how = labels[li].kind, "label"


def decide(regs: list) -> ConsoleRead:
    def pick(name, parse):
        cands = [r for r in regs if r.name == name and r.number]
        labeled = [r for r in cands if r.how == "label"]
        use = labeled or cands                       # 라벨로 정한 것이 있으면 규칙 후보는 버림
        vals = {next((v for v in map(parse, r.cands) if v is not None), None) for r in use} - {None}
        if len(vals) == 1:
            return vals.pop(), ""
        return None, f"{name} 후보 {len(use)}개" + (f"({', '.join(r.number for r in use)})" if use else "")

    d, why_d = pick("distance", parse_km)
    t, why_t = pick("time", parse_time)
    if d is None or t is None:
        return ConsoleRead("UNREADABLE", d, t, " · ".join(w for w in (why_d, why_t) if w), regs)
    if not (0 < d <= 100_000 and 0 < t <= 6 * 3600 and 1 <= d / t * 3.6 <= 30):   # 계기판 값끼리만 봄(센서 아님)
        return ConsoleRead("UNREADABLE", d, t, f"거리·시간이 서로 안 맞음({d} m, {t} s)", regs)
    return ConsoleRead("READ", d, t, "", regs)


def draw(img: np.ndarray, res: ConsoleRead) -> np.ndarray:
    """확인용 그림: 숫자 칸은 이름 색(거리 초록, 시간 파랑, 그 밖 회색), 라벨은 노랑."""
    out = img.copy()
    col = {"distance": (80, 200, 80), "time": (230, 140, 40), "other": (150, 150, 150)}
    for r in res.regions:
        c = col.get(r.name, (60, 60, 230)) if r.number else (40, 200, 230)
        cv2.polylines(out, [r.box.astype(np.int32)], True, c, 2)
        tag = f"{r.number}>{r.name or '?'}({r.how})" if r.number else f"[{r.kind}]"
        x0, y0, _, _ = r.rect
        cv2.putText(out, tag, (int(x0), max(12, int(y0) - 4)), cv2.FONT_HERSHEY_SIMPLEX, 0.5, c, 1)
    head = f"{res.status} d={res.distance_m} t={res.duration_s} {res.why}"
    cv2.putText(out, head, (8, 22), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)
    return out
