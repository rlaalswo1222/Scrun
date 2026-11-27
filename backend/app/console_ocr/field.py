"""계기판 사진 전체 → 거리·시간(계획 0042 방법 B + A 보조).

1. 글자 영역 찾기: PP-OCRv5 검출(DBNet, RapidOCR·onnxruntime)이 사진의 글자 덩어리를 모두 찾는다
2. 읽기: 영역마다 PP-OCRv5 한국어 인식(라벨·LCD 글자)과 우리 YOLOX(7세그먼트 숫자, onnx_engine)
3. 이름 붙이기: 숫자 칸마다 가장 가까운 라벨(거리·DIST·km / 시간·TIME·min / 그 밖)을 짝짓는다.
   라벨이 없으면 형식 규칙(A): 콜론 → 시간, 소수 둘째 자리 → 거리
4. 판정: 거리·시간 후보가 하나씩일 때만 READ. 없거나 둘 이상이면 UNREADABLE(추측하지 않음)

센서 값은 쓰지 않는다(독립성, 계획 0038): 센서와 가까운 칸을 고르면 다른 칸이 우연히 맞아도 통과된다.
"""
import re
import time
from collections import Counter
from dataclasses import dataclass, field as dfield
from typing import Optional

import cv2
import numpy as np


SEG_CONFUSION = str.maketrans({"S": "5", "s": "5", "O": "0", "o": "0", "D": "0", "I": "1", "l": "1", "|": "1",
                               "B": "8", "Z": "2", "z": "2", "G": "6", "q": "9", ",": "."})
# 숫자 모양: 정수·소수 둘째 자리까지·시간(28:40, 1:02:05), 그리고 km 소수 셋째 자리("0.018", 계획 0082: DRAX 기구)
NUM = re.compile(r"^(?:\d{1,3}(?:[.:]\d{1,2}){0,2}|\d{1,3}\.\d{3})$")
# 콜론이 안 찍힌 시간 "0042" = 0:42 — 두 읽기가 같을 때만(계획 0082). 4자리(분 2 + 초 2)만: 실물은 앞 0까지 다 켜는 "0042"였고,
# 3자리까지 받으면 시간 라벨이 옆 칸 칼로리 "120"과 잘못 짝지어졌을 때 1:20으로 READ될 수 있음(리뷰 11/27)
PLAIN_TIME = re.compile(r"\d{4}")
# 7세그먼트를 돌리지 않는 낱말: 글자 3개 이상·숫자 없음·인식 점수 이만큼 이상(라벨·버튼·로고). 실사진 11/27:
# 진짜 낱말은 0.84–1.0, 빨간 LED 숫자를 글자로 잘못 읽은 것("AFO" = 1.00)은 0.31–0.72(계획 0082)
WORD_SCORE = 0.8

# 라벨 낱말(대문자·공백 없앤 글자에서 찾음). 순서: 속도·페이스·마일(거리 km보다 먼저) → 거리·시간 → 그 밖
RATE_WORDS = ["KM/H", "KMH", "KPH", "MPH", "/KM", "/MIN", "/분", "MILE"]   # 마일은 km로 못 바꿈 → 거리 칸으로 안 봄
DIST_WORDS = ["거리", "DISTANCE", "DIST", "KM"]
TIME_WORDS = ["운동시간", "시간", "TIME", "MIN", "분"]
NOT_WORKOUT_TIME = ["남은", "REMAIN", "LAP", "SPLIT", "구간"]          # 남은 시간·구간 시간은 운동 시간이 아님
# 거리·시간 낱말과 같이 있어도 그 밖: "PACE MIN"·"페이스(분)"(페이스), "SPEED km/n"(OCR이 km/h를 잘못 읽음)이
# 시간·거리 칸이 되면 페이스 5:30이 운동 시간 330초로 읽힘(#68 리뷰)
STRONG_OTHER = ["속도", "SPEED", "페이스", "PACE", "칼로리", "KCAL", "CAL", "경사", "INCLINE", "심박", "PULSE", "BPM",
                "와트", "WATT"]
OTHER_WORDS = STRONG_OTHER + ["ELEV", "%", "HR", "LEVEL", "레벨"]   # 뒤 다섯은 거리·시간 낱말보다 약함("TIME HR:MIN"은 시간)
# 글자 찾기(검출) 입력의 긴 변 상한(계획 0081). RapidOCR 기본(짧은 변 736 "이상"으로 맞춤)은 앱 사진 2048×1342를
# 원본 크기로 찾아 한 장 시간의 약 60%가 검출이었음. 긴 변 960으로 줄여도 인식·7세그먼트는 원본에서 잘라 읽음
# (합성 200장 1스레드: 한 장 1.07 → 0.55초, 맞음 75.0 → 78.0%, 틀린 READ 1 → 0. 합성이라 정확도 아님)
DET_SIDE = 960
AMBIGUOUS_GAP = 0.35   # 짝짓기: 다른 종류 라벨(또는 다른 숫자)과의 거리 차가 숫자 높이의 이만큼보다 작으면 애매 → 못 읽음

UNIT = re.compile(r"KM|MIN|KCAL|KM/H|KPH|%|BPM|M|분")   # 숫자 오른쪽에 붙는 단위(그 밖의 오른쪽 라벨은 다음 칸 것)
NUM_IN_TEXT = re.compile(r"([A-Z가-힣/%]*)(\d{1,3}\.\d{3}|\d{1,3}(?:[.:]\d{1,2}){0,2})([A-Z가-힣/%]*)")   # 라벨·단위 + 숫자 + 라벨·단위


def _norm(text: str) -> str:
    return re.sub(r"\s+", "", text).upper()


def label_kind(text: str) -> Optional[str]:
    """라벨 글자 → distance·time·other, 모르면 None. 속도·페이스·칼로리 등(STRONG_OTHER)은 거리·시간 낱말이 있어도
    그 밖, 그 밖의 약한 낱말보다는 거리·시간 낱말을 먼저 봄("TIME HR:MIN"은 시간), 남은·구간 시간은 그 밖,
    한 라벨에 거리·시간이 다 있으면 모름(#61·#68 리뷰)."""
    t = _norm(text)
    if not re.search(r"[A-Z가-힣%/]", t):
        return None
    if any(w in t for w in RATE_WORDS) or any(w in t for w in STRONG_OTHER):
        return "other"
    has_dist = any(w in t for w in DIST_WORDS)
    has_time = any(w in t for w in TIME_WORDS)
    if has_time and any(w in t.replace("ELAPSED", "") for w in NOT_WORKOUT_TIME):   # "ELAPSED TIME" 안의 LAP은 구간 아님
        return "other"
    if has_dist and has_time:
        return None
    if has_dist:
        return "distance"
    if has_time:
        return "time"
    if any(w in t for w in OTHER_WORDS):
        return "other"
    return None


def parse_time(s: str, plain_ok: bool = False) -> Optional[int]:
    """'28:40'·'28.40'(점으로 보이는 기종)·'1:02:05' → 초. 초 자리가 60 이상이면 None.
    plain_ok: 콜론이 안 찍힌 4자리 '0042'(= 0:42)도 받음 — 두 읽기가 같은 칸만(계획 0082, 꺼진 자리 "8888" 막기)."""
    if plain_ok and PLAIN_TIME.fullmatch(s):
        m, sec = int(s[:-2]), int(s[-2:])
        return None if sec >= 60 else m * 60 + sec
    parts = re.split(r"[.:]", s)
    if not all(p.isdigit() for p in parts) or not 2 <= len(parts) <= 3 or any(len(p) != 2 for p in parts[1:]):
        return None
    v = [int(p) for p in parts]
    if any(x >= 60 for x in v[1:]):
        return None
    return v[0] * 3600 + v[1] * 60 + v[2] if len(v) == 3 else v[0] * 60 + v[1]


def parse_km(s: str) -> Optional[int]:
    if not re.fullmatch(r"\d{1,3}\.\d{1,3}", s):   # 소수 셋째 자리 "0.018" km = 18 m(계획 0082)
        return None
    if set(s.replace(".", "")) == {"8"}:
        # 모든 자리가 8 = 꺼진 칸의 잔상("8.88"). 11/27 p05: 거리 칸이 꺼져 있는데 8.88 km로 READ(계획 0082).
        # 진짜 8.8·8.88 km는 못 읽음(다시 찍기) — 틀린 READ보다 낫다. 시간은 초 자리가 88이라 원래 안 됨
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
    agree: bool = False         # 7세그먼트와 인식 글자가 같은 숫자열(콜론 없는 시간을 받을 조건)

    @property
    def rect(self):
        x0, y0 = self.box.min(0)
        x1, y1 = self.box.max(0)
        return float(x0), float(y0), float(x1), float(y1)


@dataclass
class PhotoRead:
    status: str                       # READ·UNREADABLE
    distance_m: Optional[int] = None
    duration_s: Optional[int] = None
    why: str = ""
    regions: list = dfield(default_factory=list)


class ConsoleReader:
    def __init__(self, seg_onnx: str, threads: int = 2, prefer: str = "seg", det_side: Optional[int] = DET_SIDE):
        """det_side: 검출 입력 긴 변 상한(None이면 RapidOCR 기본 — 비교용, `speedlab/ocr/bench_det.py`)."""
        self.stats: dict = {}   # 마지막 regions()의 단계별 시간·영역 수(서버 로그용, 계획 0055·0081)
        # 무거운 엔진은 여기서만(칸 찾기 규칙·테스트는 onnxruntime·rapidocr 없이 돌게)
        from rapidocr import LangDet, LangRec, ModelType, OCRVersion, RapidOCR

        from .seg import OnnxReader
        params = {
            "Det.ocr_version": OCRVersion.PPOCRV5, "Det.model_type": ModelType.MOBILE, "Det.lang_type": LangDet.CH,
            "Rec.ocr_version": OCRVersion.PPOCRV5, "Rec.model_type": ModelType.MOBILE, "Rec.lang_type": LangRec.KOREAN,
            "Global.use_cls": False, "Global.log_level": "error", "Global.text_score": 0.3,
            "EngineConfig.onnxruntime.intra_op_num_threads": threads,   # 오라클 2코어(ADR-0008)
            "EngineConfig.onnxruntime.inter_op_num_threads": 1}
        if det_side is not None:
            params.update({"Det.limit_type": "max", "Det.limit_side_len": det_side})
        self.ocr = RapidOCR(params=params)
        self.seg = OnnxReader(seg_onnx, threads)
        self.prefer = prefer   # seg: 7세그먼트 읽기 먼저, 안 되면 인식 글자 / rec: 반대

    def warm_up(self) -> None:
        """검출·인식 모델을 지금 다 올린다. RapidOCR은 인식 모델을 글자를 처음 찾았을 때 올리므로(없으면 그때 받음),
        빈 그림으로는 인식 모델이 안 올라온다(11/18 오라클: 빌드 때 한국어 인식 모델을 안 받아 요청 중 Permission denied).
        글자를 그린 그림을 읽혀 확인하고, 못 읽으면 RuntimeError(빌드·프로세스 시작을 실패시킴)."""
        img = np.zeros((160, 640, 3), np.uint8)
        cv2.putText(img, "DISTANCE 12.34", (20, 105), cv2.FONT_HERSHEY_SIMPLEX, 2.0, (255, 255, 255), 4, cv2.LINE_AA)
        r = self.ocr(img)
        if r.txts is None or not any("12" in t for t in r.txts):
            raise RuntimeError(f"OCR 예열 실패: 글자를 그린 그림을 못 읽음({r.txts})")

    def regions(self, img: np.ndarray) -> list:
        t0 = time.perf_counter()
        r = self.ocr(img)
        det_s, _, rec_s = (v if isinstance(v, float) else 0.0 for v in (list(r.elapse_list) + [None] * 3)[:3])
        self.stats = {"ocr_s": time.perf_counter() - t0, "det_s": det_s, "rec_s": rec_s,
                      "boxes": 0 if r.boxes is None else len(r.boxes), "seg_s": 0.0, "segs": 0}
        if r.boxes is None:
            return []
        out = [Region(np.asarray(b, np.float32), str(t), float(c)) for b, t, c in zip(r.boxes, r.txts, r.scores)]
        for reg in out:
            reg.kind = label_kind(reg.rec)
        out = merge_split(out)
        for reg in out:
            num_rec = self._number(reg.rec)
            if reg.kind is None and self._is_word(reg):
                continue                         # 라벨·버튼·로고 낱말: 7세그먼트가 숫자를 지어냄("TOTAL STEPS" → 8:5)
            if reg.kind is None or num_rec:      # 숫자가 보이는 영역만 7세그먼트로 다시 읽음("3.12 km"처럼 붙은 것 포함)
                t0 = time.perf_counter()
                reg.seg = self.seg.read(self._crop(img, reg))
                self.stats["seg_s"] += time.perf_counter() - t0
                self.stats["segs"] += 1
                plain_rec = re.sub(r"\s+", "", reg.rec)
                reg.agree = bool(reg.seg) and PLAIN_TIME.fullmatch(reg.seg) is not None and reg.seg == plain_rec
                num_seg = reg.seg if NUM.match(reg.seg) or reg.agree else None
                order = (num_seg, num_rec) if self.prefer == "seg" else (num_rec, num_seg)
                reg.cands = list(dict.fromkeys(c for c in order if c))
                reg.number = reg.cands[0] if reg.cands else None
        return out

    @staticmethod
    def _is_word(reg: "Region") -> bool:
        t = re.sub(r"\s+", "", reg.rec)
        return (reg.rec_score >= WORD_SCORE and not any(ch.isdigit() for ch in t)
                and sum(ch.isalpha() for ch in t) >= 3)

    @staticmethod
    def _number(text: str) -> Optional[str]:
        """인식 글자 → 숫자. 글자 전체가 (라벨·단위) + 숫자 + (라벨·단위) 모양일 때만("12.345"·"1680"을 잘라 읽지 않게).
        7세그먼트 혼동(S→5 등)은 진짜 숫자가 하나라도 있고 바로잡은 뒤 전부 숫자일 때만("BODY"·"STOP"이 숫자가 되지 않게)."""
        t = re.sub(r"\s+", "", text)
        digits = [i for i, ch in enumerate(t) if ch.isdigit()]
        if label_kind(t) is None and digits:
            fixed = t.translate(SEG_CONFUSION)
            if re.fullmatch(r"[\d.:]+", fixed):
                t = fixed
        elif digits:   # 단위·라벨이 붙은 "3.I2km": 첫 숫자와 끝 숫자 사이만 바로잡음(라벨 글자는 그대로, #68 리뷰)
            a, b = digits[0], digits[-1] + 1
            core = t[a:b].translate(SEG_CONFUSION)
            if re.fullmatch(r"[\d.:]+", core):
                t = t[:a] + core + t[b:]
        m = NUM_IN_TEXT.fullmatch(t.upper())
        if not m:
            return None
        affix = m.group(1) + m.group(3)
        if affix and label_kind(affix) is None and not UNIT.fullmatch(affix):
            return None   # 숫자 옆 글자가 라벨·단위가 아님(로고·버튼 글자 등)
        return m.group(2)

    @staticmethod
    def _crop(img, reg: Region, pad: float = 0.15):
        x0, y0, x1, y1 = reg.rect
        h = y1 - y0
        x0, y0 = max(0, int(x0 - h * pad * 3)), max(0, int(y0 - h * pad))
        x1, y1 = min(img.shape[1], int(x1 + h * pad * 3)), min(img.shape[0], int(y1 + h * pad))
        return img[y0:y1, x0:x1]

    def read(self, img: np.ndarray) -> PhotoRead:
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
    """라벨과 숫자를 가까운 순서로 하나씩 짝짓는다(라벨은 숫자 위·아래·왼쪽, 단위는 오른쪽, 거리는 숫자 높이로 나눔).
    계기판은 라벨 위치가 한 방향으로 일정하므로, 서로 가장 가까운 짝들이 대부분 한 방향이면 그 방향을 앞세운다
    (줄 사이 라벨이 위·아래 숫자 어느 쪽인지). 다른 방향 짝은 그 라벨·숫자에 주된 방향 후보가 있을 때만 버림:
    윗줄은 라벨이 위, 아랫줄은 라벨이 아래인 계기판의 아랫줄을 통째로 버리지 않게(#68 리뷰). 그래도 거리 차·비율이 비슷하게 겨루는 짝이 있으면 그럴듯한 쪽을
    고르지 않고 "ambiguous"(못 읽음, #61 리뷰)."""
    cands = []   # (gap, 라벨 번호, 숫자 번호, 방향)
    for li, L in enumerate(labels):
        lx0, ly0, lx1, ly1 = L.rect
        lcx, lcy = (lx0 + lx1) / 2, (ly0 + ly1) / 2
        for ni, N in enumerate(nums):
            nx0, ny0, nx1, ny1 = N.rect
            h = max(1.0, ny1 - ny0)
            xover = min(lx1, nx1 + h) - max(lx0, nx0 - h) > 0           # 가로로 겹침(숫자 높이만큼 여유)
            yover = min(ly1, ny1) - max(ly0, ny0) > 0
            if xover and ly1 <= ny0 + h * 0.3:
                gap, way = (ny0 - ly1) / h, "above"
            elif xover and ly0 >= ny1 - h * 0.3:
                gap, way = (ly0 - ny1) / h, "below"
            elif yover and lx1 <= nx0 + h * 0.3:
                gap, way = (nx0 - lx1) / h, "left"
            elif yover and lx0 >= nx1 - h * 0.3 and (lx0 - nx1) / h <= 0.6 and UNIT.fullmatch(_norm(L.rec)):
                gap, way = (lx0 - nx1) / h + 0.5, "unit"
            else:
                continue
            gap += abs(lcx - (nx0 + nx1) / 2) / h * 0.15 if way in ("above", "below") else abs(lcy - (ny0 + ny1) / 2) / h * 0.15
            if gap <= 2.5:
                cands.append((gap, li, ni, way))

    # 서로 가장 가까운 짝들의 방향으로 이 계기판의 라벨 방향을 정함(분명할 때만)
    best_l = {li: min((c for c in cands if c[1] == li), key=lambda c: c[0]) for li in {c[1] for c in cands}}
    best_n = {ni: min((c for c in cands if c[2] == ni), key=lambda c: c[0]) for ni in {c[2] for c in cands}}
    ways = Counter(c[3] for li, c in best_l.items() if best_n.get(c[2]) == c and c[3] != "unit")
    if ways:
        way, n = ways.most_common(1)[0]
        if n >= 2 and n >= 0.6 * sum(ways.values()):
            major_l = {c[1] for c in cands if c[3] == way}
            major_n = {c[2] for c in cands if c[3] == way}
            cands = [c for c in cands if c[3] in (way, "unit") or (c[1] not in major_l and c[2] not in major_n)]

    gaps = {(li, ni): gap for gap, li, ni, _ in cands}

    def rivals(g, gap):   # 거리 차도 작고 비율도 비슷해야 겨룸(0.08 대 0.42는 안 겨룸)
        return g - gap < AMBIGUOUS_GAP and g <= gap * 2 + 0.1

    def claimed_elsewhere(g, others):   # 그 라벨·숫자에 분명히 더 가까운 다른 짝이 있음
        return any(g2 < g - AMBIGUOUS_GAP or (g2 < g and not rivals(g, g2)) for g2 in others)

    used_l, used_n = set(), set()
    for gap, li, ni, _ in sorted(cands):
        if li in used_l or ni in used_n:
            continue
        used_l.add(li)
        used_n.add(ni)
        kind = labels[li].kind
        rival_label = any(lj != li and labels[lj].kind != kind and rivals(g, gap)
                          and not claimed_elsewhere(g, [g2 for (lk, nk), g2 in gaps.items() if lk == lj and nk != ni])
                          for (lj, nj), g in gaps.items() if nj == ni)
        rival_num = any(nj != ni and rivals(g, gap)
                        and not claimed_elsewhere(g, [g2 for (lk, nk), g2 in gaps.items() if nk == nj and lk != li])
                        for (lj, nj), g in gaps.items() if lj == li)
        nums[ni].name, nums[ni].how = ("ambiguous", "label") if rival_label or rival_num else (kind, "label")


def decide(regs: list) -> PhotoRead:
    def pick(name, parse):
        cands = [r for r in regs if r.name == name and r.number]
        labeled = [r for r in cands if r.how == "label"]
        use = labeled or cands                       # 라벨로 정한 것이 있으면 규칙 후보는 버림
        vals = set()
        for r in use:
            if parse is parse_time:   # 콜론 없는 시간("0042")은 두 읽기가 같은 칸만(계획 0082)
                parsed = {v for v in (parse_time(c, plain_ok=r.agree) for c in r.cands) if v is not None}
            else:
                parsed = {v for v in map(parse, r.cands) if v is not None}
            if len(parsed) >= 2:
                vals.add(None)    # 한 칸을 두 읽기가 다르게 읽음 → 애매(추측하지 않음)
            else:
                vals |= parsed    # 해석 안 되는 칸(정수 "12" 등)은 다른 칸의 값을 막지 않음(#68 리뷰)
        if len(vals) == 1 and None not in vals:
            return vals.pop(), ""
        return None, f"{name} 후보 {len(use)}개" + (f"({', '.join(r.number for r in use)})" if use else "")

    d, why_d = pick("distance", parse_km)
    t, why_t = pick("time", parse_time)
    if d is None or t is None:
        return PhotoRead("UNREADABLE", d, t, " · ".join(w for w in (why_d, why_t) if w), regs)
    if not (0 < d <= 100_000 and 0 < t <= 6 * 3600 and 1 <= d / t * 3.6 <= 30):   # 계기판 값끼리만 봄(센서 아님)
        return PhotoRead("UNREADABLE", d, t, f"거리·시간이 서로 안 맞음({d} m, {t} s)", regs)
    return PhotoRead("READ", d, t, "", regs)


def draw(img: np.ndarray, res: PhotoRead) -> np.ndarray:
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
