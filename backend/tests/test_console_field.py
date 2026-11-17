"""계기판 칸 찾기(field.py) 규칙: 라벨 분류, 숫자 꺼내기, 짝짓기 방향, 판정(#68 리뷰). 모델 없이 규칙만."""
import numpy as np
import pytest

from app.console_ocr.field import ConsoleReader, Region, decide, label_kind, pair, parse_km, parse_time


@pytest.mark.parametrize("text,kind", [
    ("DISTANCE", "distance"),
    ("거리(km)", "distance"),
    ("TIME", "time"),
    ("TIME HR:MIN", "time"),        # 약한 그 밖 낱말(HR)보다 시간이 먼저
    ("ELAPSED TIME", "time"),       # ELAPSED 안의 LAP은 구간 시간이 아님
    ("LAP TIME", "other"),
    ("남은 시간", "other"),
    ("PACE MIN", "other"),          # 페이스가 운동 시간이 되지 않게
    ("페이스(분)", "other"),
    ("SPEED km/n", "other"),        # km/h를 잘못 읽어도 속도가 거리가 되지 않게
    ("속도 km", "other"),
    ("CALORIES", "other"),
    ("km/h", "other"),
    ("12.3", None),
])
def test_label_kind(text, kind):
    assert label_kind(text) == kind


@pytest.mark.parametrize("text,number", [
    ("3.12", "3.12"),
    ("3.I2km", "3.12"),     # 단위가 붙어도 숫자 사이 혼동 글자는 바로잡음
    ("S.12", "5.12"),       # 라벨 없는 숫자는 전체를 바로잡음
    ("28:40", "28:40"),
    ("BODY", None),         # 숫자가 없으면 바로잡지 않음
    ("12.345", None),       # 잘라 읽지 않음
])
def test_number(text, number):
    assert ConsoleReader._number(text) == number


def reg(x0, y0, x1, y1, rec="", kind=None, number=None, cands=None, name=None, how=""):
    r = Region(np.float32([[x0, y0], [x1, y0], [x1, y1], [x0, y1]]), rec, 1.0)
    r.kind, r.number, r.name, r.how = kind, number, name, how
    r.cands = cands if cands is not None else ([number] if number else [])
    return r


def test_decide_ignores_unparseable_region_but_not_disagreeing_reads():
    d = reg(0, 0, 1, 1, number="3.12", name="distance", how="label")
    junk = reg(0, 0, 1, 1, number="12", name="distance", how="label")        # parse_km 안 됨 → 무시
    t = reg(0, 0, 1, 1, number="28:40", name="time", how="label")
    res = decide([d, junk, t])
    assert (res.status, res.distance_m, res.duration_s) == ("READ", 3120, 1720)

    split = reg(0, 0, 1, 1, number="3.12", cands=["3.12", "3.42"], name="distance", how="label")
    assert decide([split, t]).status == "UNREADABLE"                           # 한 칸을 두 읽기가 다르게 읽음


def test_pair_keeps_minority_direction_when_no_competing_pair():
    # 윗줄은 라벨이 숫자 위(2칸), 아랫줄은 라벨이 숫자 아래(1칸): 아랫줄 시간을 버리지 않음
    dist_l = reg(0, 0, 100, 20, "DISTANCE", kind="distance")
    cal_l = reg(200, 0, 300, 20, "CALORIES", kind="other")
    time_l = reg(0, 250, 100, 270, "TIME", kind="time")
    d = reg(0, 30, 100, 70, number="3.12")
    c = reg(200, 30, 300, 70, number="150")
    t = reg(0, 200, 100, 240, number="28:40")
    pair([dist_l, cal_l, time_l], [d, c, t])
    assert (d.name, c.name, t.name) == ("distance", "other", "time")
    res = decide([dist_l, cal_l, time_l, d, c, t])
    assert (res.status, res.distance_m, res.duration_s) == ("READ", 3120, 1720)


def test_parsers():
    assert parse_km("3.12") == 3120 and parse_km("12") is None
    assert parse_time("28:40") == 1720 and parse_time("1:02:05") == 3725 and parse_time("28:70") is None
