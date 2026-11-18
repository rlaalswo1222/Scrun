"""계기판 인증 API 진짜 흐름(계획 0038, BL-18 완료 기준: 인증 완료·미인증·재촬영 세 경우). OCR은 가짜(파일 이름으로 값)."""
import dataclasses
import time
from datetime import datetime, timedelta, timezone

import pytest
from sqlalchemy import select, update

from app.core.config import ConfigError, VerificationPolicy, load_settings, load_verification
from app.core.ocr import FakeOcr, get_ocr
from app.core.ocr import ConsoleRead
from app.core.run_access import close_expired
from app.core.verification import compare, plausible
from app.db.models import Run, Verification
from app.main import app
from app.routers import verification as vr
from tests.helpers import data, fail, run_together, signup
from tests.test_runs import ago, db, enter, finish, me, start, started_id  # noqa: F401  (db·me는 fixture)

P = VerificationPolicy()


@pytest.fixture(autouse=True)
def fake_ocr():
    app.dependency_overrides[get_ocr] = FakeOcr
    yield
    app.dependency_overrides.pop(get_ocr, None)


def finished(client, headers, distance=3000, duration=1000, pause_s=0, cid="c1") -> str:
    """일시정지 없이(또는 pause_s만큼) 달리고 1분 전에 끝낸 러닝."""
    run_id = started_id(client, headers, cid=cid, started=ago(seconds=60 + duration + pause_s))
    r = finish(client, headers, run_id, ended=ago(seconds=60), distance=distance, duration=duration)
    assert r.status_code == 200, r.text
    return run_id


_seq = iter(range(10 ** 6))


def jpeg(name: str):
    """매번 다른 바이트(같은 사진 다시 쓰기 검사에 안 걸리게)."""
    return "photos", (name, b"\xff\xd8\xff" + next(_seq).to_bytes(4, "big"), "image/jpeg")


def photo(client, headers, run_id, *names, files=None):
    return client.post(f"/v1/runs/{run_id}/verification/photo", headers=headers,
                       files=files or [jpeg(n) for n in names])


def confirm(client, headers, run_id, attempt_id, correct=True):
    return client.post(f"/v1/runs/{run_id}/verification/confirm", headers=headers,
                       json={"attemptId": attempt_id, "correct": correct})


def read_then(client, headers, run_id, *names, files=None, correct=True):
    """사진 → CONFIRM_REQUIRED(읽은 값 확인, 계획 0045) → 맞아요(또는 틀렸어요)."""
    shown = data(photo(client, headers, run_id, *names, files=files))
    assert shown["status"] == "CONFIRM_REQUIRED", shown
    return confirm(client, headers, run_id, shown["attemptId"], correct)


def skip(client, headers, run_id, reason="SKIPPED"):
    return client.post(f"/v1/runs/{run_id}/verification/skip", headers=headers, json={"reason": reason})


def run_row(db, run_id) -> Run:
    db.expire_all()
    return db.get(Run, run_id)


# ---------- 판정(순수 함수) ----------
@pytest.mark.parametrize("console_m,console_s,ok_d,ok_t", [
    (3000, 1000, True, True),
    (3529, 1000, True, True),     # (3529 − 3000) / 3529 = 14.99%
    (3530, 1000, False, True),    # 15.01%
    (2610, 1000, True, True),     # −14.9%
    (3000, 970, True, True),      # 계기판이 30초 짧음까지
    (3000, 969, True, False),
    (3000, 1300, True, True),     # 5분 길게까지(카운트다운 전 걷기)
    (3000, 1301, True, False),
])
def test_compare_edges(console_m, console_s, ok_d, ok_t):
    out = compare(P, ConsoleRead.read(console_m, console_s), 3000, 1000, paused_s=0)
    assert [r.ok for r in out.rows] == [ok_d, ok_t]
    assert out.status.value == ("VERIFIED" if ok_d and ok_t else "UNVERIFIED")


def test_compare_widens_time_by_pause():
    # 2분 쉼: 계기판이 일시정지 중에도 흐르는 기종이면 +5분 + 2분까지, 멈추는 기종이면 그대로 맞음
    assert compare(P, ConsoleRead.read(3000, 1420), 3000, 1000, paused_s=120).status.value == "VERIFIED"
    assert compare(P, ConsoleRead.read(3000, 1421), 3000, 1000, paused_s=120).status.value == "UNVERIFIED"
    assert compare(P, ConsoleRead.read(3000, 1000), 3000, 1000, paused_s=120).status.value == "VERIFIED"
    assert compare(P, ConsoleRead.read(3000, 969), 3000, 1000, paused_s=120).status.value == "UNVERIFIED"  # 아래쪽은 그대로


def test_plausible():
    assert plausible(3000, 1000) and not plausible(0, 1000) and not plausible(3000, 0)
    assert not plausible(9000, 1000)  # 평균 32 km/h: 소수점·자리를 잘못 읽은 것


def test_policy_from_env():
    assert load_verification({}) == VerificationPolicy()
    p = load_verification({"VERIFY_DISTANCE_TOLERANCE": "0.1", "VERIFY_MAX_RETAKES": "2"})
    assert p.distance_tolerance == 0.1 and p.max_retakes == 2
    with pytest.raises(ConfigError, match="0.01에서 1.0"):
        load_verification({"VERIFY_DISTANCE_TOLERANCE": "15"})
    with pytest.raises(ConfigError, match="정수"):
        load_verification({"VERIFY_DEADLINE_S": "10분"})
    with pytest.raises(ConfigError, match="-3600에서 0"):
        load_verification({"VERIFY_DURATION_MIN_S": "10"})  # 최소 ≤ 0 ≤ 최대라 순서가 뒤집힐 수 없음
    with pytest.raises(ConfigError, match="OCR_ENGINE"):
        load_settings({"JWT_SECRET": "x" * 48, "KAKAO_MODE": "fake", "OCR_ENGINE": "tesseract"})
    assert load_settings({"JWT_SECRET": "x" * 48, "KAKAO_MODE": "fake"}).ocr_engine == "none"


# ---------- 인증 완료·미인증 ----------
def test_read_values_are_confirmed_before_judging(db_client, me, db):
    run_id = finished(db_client, me)
    shown = data(photo(db_client, me, run_id, "c0.jpg", "c1_d3100_t1060.jpg", "c2.jpg"))
    assert (shown["status"], shown["reason"], shown["console"], shown["comparison"], shown["retakesLeft"]) == \
        ("CONFIRM_REQUIRED", None, {"distanceM": 3100, "durationS": 1060}, [], 3)   # 센서 대조는 판정 뒤에
    assert shown["attemptId"] and shown["retakeDeadline"].endswith("Z")
    assert run_row(db, run_id).status == "PENDING" and run_row(db, run_id).console_distance_m is None

    out = data(confirm(db_client, me, run_id, shown["attemptId"]))
    assert (out["status"], out["reason"], out["console"], out["sensor"]) == \
        ("VERIFIED", None, {"distanceM": 3100, "durationS": 1060}, {"distanceM": 3000, "durationS": 1000})
    assert [(r["item"], r["ok"]) for r in out["comparison"]] == [("distance", True), ("duration", True)]
    assert out["comparison"][0]["diffRatio"] == pytest.approx(100 / 3100, abs=1e-4)
    assert out["retakeDeadline"] is None and out["attemptId"] is None
    r = out["rewards"]   # 성장 반영(계획 0048): 3.1 km로 Lv.2
    assert (r["xpBeforeM"], r["xpAfterM"], r["levelBefore"], r["levelAfter"], r["leveledUp"], r["outfitStageAfter"]) ==         (0, 3100, 1, 2, True, 2)
    run = run_row(db, run_id)
    assert (run.status, run.official_source, run.official_distance_m, run.official_duration_s) == ("VERIFIED", "CONSOLE", 3100, 1060)
    v = db.scalars(select(Verification).where(Verification.run_id == run_id)).all()
    assert len(v) == 1 and (v[0].method, v[0].status, v[0].confirmed) == ("PHOTO", "VERIFIED", True)
    assert len(v[0].photo_hashes) == 3  # 사진은 해시만


@pytest.mark.parametrize("name", ["d3600_t1000", "d3000_t1400", "d3000_t900"])
def test_confirmed_mismatch_is_unverified_without_retake(db_client, me, db, name):
    """맞아요 뒤 오차 초과는 다시 찍기 없이 미인증(계획 0045 정할 것 3: 값이 맞으니 차이는 진짜)."""
    run_id = finished(db_client, me)
    out = data(read_then(db_client, me, run_id, f"{name}.jpg"))
    assert (out["status"], out["reason"], out["retakesLeft"]) == ("UNVERIFIED", "MISMATCH", None)
    assert out["console"]["distanceM"] == int(name[1:5]) and not all(r["ok"] for r in out["comparison"])
    run = run_row(db, run_id)
    assert (run.status, run.unverified_reason, run.official_source, run.official_distance_m) == \
        ("UNVERIFIED", "MISMATCH", "SENSOR", 3000)
    assert run.console_distance_m == out["console"]["distanceM"]  # 표에 보이려고 읽은 값은 남김
    assert data(photo(db_client, me, run_id, "d3000_t1000.jpg")) == out  # 정해진 뒤에는 그대로


def test_rejected_read_retakes_without_using_a_retake(db_client, me, db):
    run_id = finished(db_client, me)
    for i in range(5):   # 틀렸어요는 횟수를 안 씀(정할 것 2 나), 마감까지
        out = data(read_then(db_client, me, run_id, f"x{i}_d3600_t1000.jpg", correct=False))
        assert (out["status"], out["reason"], out["retakesLeft"], out["console"]) == \
            ("RETAKE_REQUIRED", "USER_REJECTED", 3, None)
    assert data(read_then(db_client, me, run_id, "d3010_t1000.jpg"))["status"] == "VERIFIED"
    rows = db.scalars(select(Verification).where(Verification.run_id == run_id).order_by(Verification.created_at)).all()
    assert [r.confirmed for r in rows] == [False] * 5 + [True]   # 틀렸어요는 읽은 값과 함께 남음(실제 오독률)
    assert rows[0].read_distance_m == 3600 and rows[0].status == "CONFIRM_REQUIRED"


def test_confirm_only_the_latest_attempt_once(db_client, me):
    run_id = finished(db_client, me)
    first = data(photo(db_client, me, run_id, "a_d3000_t1000.jpg"))
    second = data(photo(db_client, me, run_id, "b_d3000_t1010.jpg"))   # 확인 없이 다시 찍음(앱 꺼짐 등)
    fail(confirm(db_client, me, run_id, first["attemptId"]), 409, "CONFIRM_NOT_PENDING")
    assert data(confirm(db_client, me, run_id, second["attemptId"], correct=False))["reason"] == "USER_REJECTED"
    again = data(confirm(db_client, me, run_id, second["attemptId"], correct=False))   # 응답을 못 받아 다시 보냄
    assert again["reason"] == "USER_REJECTED"
    fail(confirm(db_client, me, run_id, second["attemptId"], correct=True), 409, "CONFIRM_NOT_PENDING")   # 다른 답
    third = data(photo(db_client, me, run_id, "c_d3000_t1020.jpg"))
    done = data(confirm(db_client, me, run_id, third["attemptId"]))
    assert done["status"] == "VERIFIED"
    assert data(confirm(db_client, me, run_id, third["attemptId"])) == done   # 맞아요 재시도 → 처음 결과
    fail(confirm(db_client, me, run_id, third["attemptId"], correct=False), 409, "CONFIRM_NOT_PENDING")


def test_confirm_checks_attempt_owner_and_shape(db_client, me):
    run_id = finished(db_client, me)
    other_run = finished(db_client, me, cid="c2")
    shown = data(photo(db_client, me, other_run, "d3000_t1000.jpg"))
    fail(confirm(db_client, me, run_id, shown["attemptId"]), 400, "VALIDATION_ERROR")     # 다른 기록의 시도
    fail(confirm(db_client, me, run_id, "nope"), 400, "VALIDATION_ERROR")
    fail(db_client.post(f"/v1/runs/{run_id}/verification/confirm", headers=me, json={"attemptId": "x"}), 400, "VALIDATION_ERROR")
    other = signup(db_client, "k_2")
    fail(confirm(db_client, other, other_run, shown["attemptId"]), 403, "FORBIDDEN")
    blur = data(photo(db_client, me, run_id, "blur.jpg"))   # 못 읽은 시도는 확인할 것이 없음
    assert blur["attemptId"] is None


def test_same_photo_again_gives_same_answer(db_client, me):
    run_id = finished(db_client, me)
    files = [jpeg("d3600_t1000.jpg")]
    first = data(photo(db_client, me, run_id, files=files))
    assert data(photo(db_client, me, run_id, files=files)) == first   # 같은 시도·같은 값
    data(confirm(db_client, me, run_id, first["attemptId"], correct=False))
    again = data(photo(db_client, me, run_id, files=files))
    assert (again["status"], again["reason"]) == ("RETAKE_REQUIRED", "USER_REJECTED")


def test_confirm_after_deadline_is_closed(db_client, me, db):
    run_id = finished(db_client, me)
    shown = data(photo(db_client, me, run_id, "d3000_t1000.jpg"))
    db.execute(update(Run).where(Run.id == run_id).values(verification_deadline=ago(minutes=3)))
    db.execute(update(Verification).where(Verification.id == shown["attemptId"]).values(created_at=ago(minutes=4)))
    db.commit()
    fail(confirm(db_client, me, run_id, shown["attemptId"]), 409, "VERIFICATION_CLOSED")
    assert (run_row(db, run_id).status, run_row(db, run_id).unverified_reason) == ("UNVERIFIED", "TIMEOUT")


def test_photo_sent_just_before_deadline_can_still_be_confirmed(db_client, me, db):
    """9분 59초에 보낸 사진: 확인하는 사이 마감이 지나도 유예(2분) 안이면 맞아요가 됨."""
    run_id = finished(db_client, me)
    shown = data(photo(db_client, me, run_id, "d3000_t1000.jpg"))
    db.execute(update(Run).where(Run.id == run_id).values(verification_deadline=ago(seconds=30)))
    db.execute(update(Verification).where(Verification.id == shown["attemptId"]).values(created_at=ago(seconds=40)))
    db.commit()
    close_expired(db, run_id=run_id)   # 그 사이 목록 조회 등이 마감으로 닫음
    db.commit()
    assert data(confirm(db_client, me, run_id, shown["attemptId"]))["status"] == "VERIFIED"


@pytest.mark.parametrize("closed_first", [False, True])
def test_reject_in_grace_closes_instead_of_reopening(db_client, me, db, closed_first):
    """마감 뒤 유예 안의 틀렸어요: 다시 찍을 시간이 없으니 오독만 남기고 마감으로 닫음(기록을 PENDING으로 다시 열지 않음)."""
    run_id = finished(db_client, me)
    shown = data(photo(db_client, me, run_id, "d3000_t1000.jpg"))
    db.execute(update(Run).where(Run.id == run_id).values(verification_deadline=ago(seconds=30)))
    db.execute(update(Verification).where(Verification.id == shown["attemptId"]).values(created_at=ago(seconds=40)))
    db.commit()
    if closed_first:
        close_expired(db, run_id=run_id)   # 그 사이 목록 조회 등이 마감으로 닫음
        db.commit()
    for _ in range(2):   # 응답을 못 받은 재시도도 같음
        fail(confirm(db_client, me, run_id, shown["attemptId"], correct=False), 409, "VERIFICATION_CLOSED")
        db.expire_all()
        assert (run_row(db, run_id).status, run_row(db, run_id).unverified_reason) == ("UNVERIFIED", "TIMEOUT")
        assert db.get(Verification, shown["attemptId"]).confirmed is False


def test_give_up_after_unreadable_is_retake_given_up(db_client, me):
    run_id = finished(db_client, me)
    assert data(photo(db_client, me, run_id, "blur.jpg"))["reason"] is None
    out = data(skip(db_client, me, run_id, "RETAKE_GIVEN_UP"))
    assert (out["status"], out["reason"], out["console"]) == ("UNVERIFIED", "RETAKE_GIVEN_UP", None)


def test_paused_run_widens_time(db_client, me):
    run_id = finished(db_client, me, pause_s=120)
    out = data(read_then(db_client, me, run_id, "d3050_t1400.jpg"))  # 계기판은 일시정지 중에도 시간이 흐른 기종
    assert out["status"] == "VERIFIED"
    other = finished(db_client, me, pause_s=120, cid="c2")
    assert data(read_then(db_client, me, other, "d3050_t1500.jpg"))["reason"] == "MISMATCH"


# ---------- 재촬영 ----------
def test_retakes_then_unsupported(db_client, me, db):
    run_id = finished(db_client, me)
    lefts = []
    for i in range(3):
        out = data(photo(db_client, me, run_id, f"blur{i}.jpg"))
        assert out["status"] == "RETAKE_REQUIRED" and out["retakeDeadline"]
        lefts.append(out["retakesLeft"])
        assert run_row(db, run_id).status == "PENDING"  # 저장되는 상태가 아님
    assert lefts == [3, 2, 1]
    out = data(photo(db_client, me, run_id, "blur_last.jpg"))  # 첫 장 + 다시 3번 = 4번째도 못 읽으면 끝
    assert (out["status"], out["reason"], out["retakesLeft"]) == ("UNVERIFIED", "UNSUPPORTED_CONSOLE", None)
    assert run_row(db, run_id).status == "UNVERIFIED"


def test_retake_then_verified(db_client, me):
    run_id = finished(db_client, me)
    assert data(photo(db_client, me, run_id, "blur.jpg"))["status"] == "RETAKE_REQUIRED"
    assert data(read_then(db_client, me, run_id, "d3000_t1010.jpg"))["status"] == "VERIFIED"


def test_same_photo_again_is_not_counted(db_client, me):
    run_id = finished(db_client, me)
    files = [jpeg("blur.jpg")]
    first = data(photo(db_client, me, run_id, files=files))
    again = data(photo(db_client, me, run_id, files=files))  # 응답을 못 받은 앱이 다시 보냄
    assert first["retakesLeft"] == again["retakesLeft"] == 3
    assert data(photo(db_client, me, run_id, "blur2.jpg"))["retakesLeft"] == 2


def test_unsupported_console_can_retake_then_saved(db_client, me):
    """읽을 수 없는 계기판(E-09)도 다시 찍기 가능, 미인증으로 저장하면 UNSUPPORTED_CONSOLE."""
    run_id = finished(db_client, me)
    out = data(photo(db_client, me, run_id, "unsupported.jpg"))
    assert (out["status"], out["reason"], out["console"], out["retakesLeft"]) == \
        ("RETAKE_REQUIRED", "UNSUPPORTED_CONSOLE", None, 3)
    out = data(skip(db_client, me, run_id, "RETAKE_GIVEN_UP"))
    assert (out["status"], out["reason"]) == ("UNVERIFIED", "UNSUPPORTED_CONSOLE")


def test_implausible_read_asks_retake(db_client, me):
    run_id = finished(db_client, me)
    assert data(photo(db_client, me, run_id, "d9000_t100.jpg"))["status"] == "RETAKE_REQUIRED"   # 확인 화면에 안 감


def test_new_photos_with_an_old_one_are_read(db_client, me):
    run_id = finished(db_client, me)
    old = jpeg("blur.jpg")
    assert data(photo(db_client, me, run_id, files=[old]))["retakesLeft"] == 3
    out = data(read_then(db_client, me, run_id, files=[old, jpeg("d3000_t1000.jpg")]))  # 다시 찍은 장 + 지난 장
    assert out["status"] == "VERIFIED"


def test_photo_from_another_run_is_not_read(db_client, me):
    a = finished(db_client, me, cid="a")
    files = [jpeg("d3000_t1000.jpg")]
    assert data(read_then(db_client, me, a, files=files))["status"] == "VERIFIED"
    b = finished(db_client, me, cid="b")
    assert data(photo(db_client, me, b, files=files))["status"] == "RETAKE_REQUIRED"


# ---------- 한 번 정해지면 그대로 ----------
def test_decided_run_returns_first_result(db_client, me, db):
    run_id = finished(db_client, me)
    first = data(read_then(db_client, me, run_id, "d3100_t1060.jpg"))
    again = data(photo(db_client, me, run_id, "d5000_t1060.jpg"))
    skipped = data(skip(db_client, me, run_id))
    assert again == first and skipped == first
    assert run_row(db, run_id).official_distance_m == 3100


# ---------- 건너뛰기 ----------
@pytest.mark.parametrize("reason", ["SKIPPED", "TIMEOUT", "RETAKE_GIVEN_UP"])
def test_skip_saves_unverified(db_client, me, db, reason):
    run_id = finished(db_client, me)
    out = data(skip(db_client, me, run_id, reason))
    assert (out["status"], out["reason"], out["console"], out["comparison"]) == ("UNVERIFIED", reason, None, [])
    run = run_row(db, run_id)
    assert (run.status, run.unverified_reason, run.official_source) == ("UNVERIFIED", reason, "SENSOR")
    fail(skip(db_client, me, run_id, "WHATEVER"), 400, "VALIDATION_ERROR")


# ---------- 마감 ----------
def test_finish_sets_deadline_from_server_time(db_client, me, db):
    run_id = started_id(db_client, me, started=ago(minutes=50))
    r = finish(db_client, me, run_id, ended=ago(minutes=40), duration=600)  # 앱이 꺼졌다 복구해 늦게 끝냄
    assert r.status_code == 200, r.text
    left = run_row(db, run_id).verification_deadline - datetime.now(timezone.utc)
    assert timedelta(minutes=9) < left <= timedelta(minutes=10)


def test_photo_after_deadline_closes_run(db_client, me, db):
    run_id = finished(db_client, me)
    db.execute(update(Run).where(Run.id == run_id).values(verification_deadline=ago(seconds=1)))
    db.commit()
    fail(photo(db_client, me, run_id, "d3000_t1000.jpg"), 409, "VERIFICATION_CLOSED")
    run = run_row(db, run_id)
    assert (run.status, run.unverified_reason) == ("UNVERIFIED", "TIMEOUT")
    fail(photo(db_client, me, run_id, "d3000_t1000.jpg"), 409, "VERIFICATION_CLOSED")  # 다시 보내도 같은 답
    assert data(skip(db_client, me, run_id))["reason"] == "TIMEOUT"
    assert db.scalars(select(Verification.method).where(Verification.run_id == run_id)).all() == []  # 시도가 아님


def test_skip_after_deadline_is_timeout(db_client, me, db):
    run_id = finished(db_client, me)
    db.execute(update(Run).where(Run.id == run_id).values(verification_deadline=ago(seconds=1)))
    db.commit()
    assert data(skip(db_client, me, run_id, "SKIPPED"))["reason"] == "TIMEOUT"


# ---------- 상태·권한 ----------
def test_active_discarded_others(db_client, me):
    active = started_id(db_client, me, cid="act")
    fail(photo(db_client, me, active, "d3000_t1000.jpg"), 409, "RUN_NOT_FINISHED")
    fail(skip(db_client, me, active), 409, "RUN_NOT_FINISHED")
    db_client.post(f"/v1/runs/{active}/discard", headers=me)
    fail(skip(db_client, me, active), 409, "VERIFICATION_CLOSED")
    run_id = finished(db_client, me, cid="mine")
    other = signup(db_client, "k_2")
    fail(photo(db_client, other, run_id, "d3000_t1000.jpg"), 403, "FORBIDDEN")
    fail(skip(db_client, other, run_id), 403, "FORBIDDEN")
    fail(skip(db_client, me, "01JC0ZA4R7N2X5NOSUCHRUN000"), 404, "RUN_NOT_FOUND")
    fail(db_client.post(f"/v1/runs/{run_id}/verification/skip", json={"reason": "SKIPPED"}), 401, "UNAUTHORIZED")


# ---------- 사진 검사·엔진 ----------
def test_photo_validation(db_client, me):
    run_id = finished(db_client, me)
    fail(photo(db_client, me, run_id, "a.jpg", "b.jpg", "c.jpg", "d.jpg"), 400, "VALIDATION_ERROR")
    fail(photo(db_client, me, run_id, files=[("photos", ("x.png", b"\x89PNG....", "image/png"))]), 400, "VALIDATION_ERROR")
    big = b"\xff\xd8\xff" + b"0" * P.max_photo_bytes
    fail(photo(db_client, me, run_id, files=[("photos", ("big.jpg", big, "image/jpeg"))]), 400, "VALIDATION_ERROR")
    fail(db_client.post(f"/v1/runs/{run_id}/verification/photo", headers=me), 400, "VALIDATION_ERROR")
    fail(photo(db_client, me, run_id, files=[jpeg("d3000_t1000.jpg"), ("note", (None, "hi"))]), 400, "VALIDATION_ERROR")
    huge = {**me, "Content-Length": str(vr.BODY_LIMIT + 1), "Content-Type": "multipart/form-data; boundary=x"}
    fail(db_client.post(f"/v1/runs/{run_id}/verification/photo", headers=huge, content=b"x" * 10), 400, "VALIDATION_ERROR")
    assert data(photo(db_client, me, run_id, "d3000_t1000.jpg"))["status"] == "CONFIRM_REQUIRED"  # 실패한 요청은 횟수·상태에 영향 없음


def test_no_engine_is_503_and_not_counted(db_client, me):
    run_id = finished(db_client, me)
    app.dependency_overrides[get_ocr] = lambda: None
    fail(photo(db_client, me, run_id, "blur.jpg"), 503, "SERVICE_UNAVAILABLE")
    app.dependency_overrides[get_ocr] = FakeOcr
    assert data(photo(db_client, me, run_id, "blur.jpg"))["retakesLeft"] == 3


class SlowOcr:
    def read(self, photos):
        time.sleep(0.5)
        return FakeOcr().read(photos)


def test_slow_engine_is_503_and_not_counted(db_client, me, monkeypatch, fresh_ocr):
    run_id = finished(db_client, me)
    monkeypatch.setattr(vr, "POLICY", dataclasses.replace(vr.POLICY, ocr_timeout_s=0.1))
    app.dependency_overrides[get_ocr] = SlowOcr
    fail(photo(db_client, me, run_id, "blur.jpg"), 503, "SERVICE_UNAVAILABLE")
    app.dependency_overrides[get_ocr] = FakeOcr
    assert data(photo(db_client, me, run_id, "blur.jpg"))["retakesLeft"] == 3


def test_busy_engine_is_503_at_once(db_client, me, fresh_ocr):
    run_id = finished(db_client, me)
    vr._ocr.running = vr._OcrRunner.WORKERS  # 다른 요청이 자리를 다 씀
    fail(photo(db_client, me, run_id, "blur.jpg"), 503, "SERVICE_UNAVAILABLE")
    vr._ocr.running = 0
    assert data(photo(db_client, me, run_id, "blur.jpg"))["retakesLeft"] == 3


def test_timed_out_job_gives_back_its_slot(db_client, me, monkeypatch, fresh_ocr):
    run_id = finished(db_client, me)
    monkeypatch.setattr(vr, "POLICY", dataclasses.replace(vr.POLICY, ocr_timeout_s=0.1))
    app.dependency_overrides[get_ocr] = SlowOcr
    for name in ("blur1.jpg", "blur2.jpg"):  # 일꾼 수만큼 시간 초과
        fail(photo(db_client, me, run_id, name), 503, "SERVICE_UNAVAILABLE")
    assert vr._ocr.running == 0 and vr._ocr.abandoned == 2
    fail(photo(db_client, me, run_id, "blur3.jpg"), 503, "SERVICE_UNAVAILABLE")  # 멈춘 작업이 상한 → 바로 503
    time.sleep(0.6)  # 버려진 작업이 끝나면 다시 받음
    app.dependency_overrides[get_ocr] = FakeOcr
    assert vr._ocr.abandoned == 0
    assert data(photo(db_client, me, run_id, "blur4.jpg"))["retakesLeft"] == 3


class BrokenOcr:
    def read(self, photos):
        raise ValueError("깨진 JPEG")


def test_engine_error_is_503_not_counted(db_client, me, fresh_ocr):
    run_id = finished(db_client, me)
    app.dependency_overrides[get_ocr] = BrokenOcr
    fail(photo(db_client, me, run_id, "blur.jpg"), 503, "SERVICE_UNAVAILABLE")
    app.dependency_overrides[get_ocr] = FakeOcr
    assert data(photo(db_client, me, run_id, "blur.jpg"))["retakesLeft"] == 3
    assert vr._ocr.running == 0


class DeadlineOcr:
    """읽는 동안 마감이 지나감(마감 직전에 보낸 사진)."""

    def __init__(self, db, run_id):
        self.db, self.run_id = db, run_id

    def read(self, photos):
        self.db.execute(update(Run).where(Run.id == self.run_id).values(verification_deadline=ago(seconds=1)))
        self.db.commit()
        return FakeOcr().read(photos)


def test_deadline_is_judged_at_arrival(db_client, me, db, fresh_ocr):
    run_id = finished(db_client, me)
    app.dependency_overrides[get_ocr] = lambda: DeadlineOcr(db, run_id)
    shown = data(photo(db_client, me, run_id, "d3000_t1000.jpg"))   # 마감 전에 옴 → 읽는 동안 지나도 확인으로
    assert shown["status"] == "CONFIRM_REQUIRED"
    assert data(confirm(db_client, me, run_id, shown["attemptId"]))["status"] == "VERIFIED"   # 확인 유예 안


def test_pause_widening_is_capped(db_client, me):
    # 앱이 꺼졌다 2시간 뒤 복구: 쉰 시간은 기기 시계라 10분까지만 넓힘
    run_id = started_id(db_client, me, cid="late", started=ago(minutes=59))
    assert finish(db_client, me, run_id, ended=ago(minutes=1), duration=1000).status_code == 200
    assert data(read_then(db_client, me, run_id, "d3000_t1900.jpg"))["status"] == "VERIFIED"   # +5분 + 10분 = +900초
    other = started_id(db_client, me, cid="late2", started=ago(minutes=59))
    finish(db_client, me, other, ended=ago(minutes=1), duration=1000)
    assert data(read_then(db_client, me, other, "d3000_t1901.jpg"))["reason"] == "MISMATCH"


def test_app_timeout_skip_then_photo_returns_first_result(db_client, me):
    run_id = finished(db_client, me)
    first = data(skip(db_client, me, run_id, "TIMEOUT"))  # E-03 촬영 시간 초과(앱)
    assert data(photo(db_client, me, run_id, "d3000_t1000.jpg")) == first


def test_checks_before_reading_body(db_client, me):
    run_id = finished(db_client, me)
    other = signup(db_client, "k_2")
    bad = {"Content-Type": "multipart/form-data; boundary=x"}
    fail(db_client.post(f"/v1/runs/{run_id}/verification/photo", headers={**other, **bad}, content=b"x"), 403, "FORBIDDEN")
    fail(db_client.post(f"/v1/runs/01JC0ZA4R7N2X5NOSUCHRUN000/verification/photo", headers={**me, **bad}, content=b"x"),
         404, "RUN_NOT_FOUND")
    app.dependency_overrides[get_ocr] = lambda: None
    fail(db_client.post(f"/v1/runs/{run_id}/verification/photo", headers={**me, **bad}, content=b"x"), 503,
         "SERVICE_UNAVAILABLE")


def test_close_expired_for_lists(db_client, me, db):
    """기록 목록(BL-19·20)은 읽기 전에 close_expired를 부르면 DB 상태만 보면 됨."""
    a, b = finished(db_client, me, cid="a"), finished(db_client, me, cid="b")
    db.execute(update(Run).where(Run.id == a).values(verification_deadline=ago(seconds=1)))
    db.commit()
    assert close_expired(db) == 1
    db.commit()
    assert (run_row(db, a).status, run_row(db, a).unverified_reason, run_row(db, b).status) == ("UNVERIFIED", "TIMEOUT", "PENDING")


# ---------- 동시에 온 요청 ----------
@pytest.fixture
def fresh_ocr(monkeypatch):
    """앞 테스트의 느린 작업이 OCR 자리를 잡고 있지 않게 새 자리·새 일꾼."""
    monkeypatch.setattr(vr, "_ocr", vr._OcrRunner())


def test_two_photos_at_once_decide_once(db_client, me, db, fresh_ocr):
    run_id = finished(db_client, me)
    app.dependency_overrides[get_ocr] = SlowOcr  # 둘 다 OCR 중일 때 겹치게
    outs = run_together([lambda c, n=n: photo(c, me, run_id, n) for n in ("d3000_t1000.jpg", "d3100_t1000.jpg")])
    bodies = [data(r) for r in outs]   # 둘 다 확인으로. 나중에 저장된 쪽이 마지막 시도
    assert [b["status"] for b in bodies] == ["CONFIRM_REQUIRED"] * 2
    assert len(db.scalars(select(Verification).where(Verification.run_id == run_id)).all()) == 2
    latest = vr._latest_photo(db, run_id).id
    stale = next(b["attemptId"] for b in bodies if b["attemptId"] != latest)
    fail(confirm(db_client, me, run_id, stale), 409, "CONFIRM_NOT_PENDING")
    assert data(confirm(db_client, me, run_id, latest))["status"] == "VERIFIED"


def test_same_retry_during_ocr_counts_once(db_client, me, db, fresh_ocr):
    run_id = finished(db_client, me)
    app.dependency_overrides[get_ocr] = SlowOcr
    files = [jpeg("blur.jpg")]
    outs = run_together([lambda c: photo(c, me, run_id, files=files)] * 2)
    assert [data(r)["retakesLeft"] for r in outs] == [3, 3]
    assert len(db.scalars(select(Verification).where(Verification.run_id == run_id)).all()) == 1


# ---------- #55 리뷰 후속(민재) ----------
def test_finish_resent_after_deadline_reports_closed(db_client, me, db):
    run_id = started_id(db_client, me, cid="late-finish", started=ago(minutes=20))
    ended = ago(minutes=1)
    assert data(finish(db_client, me, run_id, ended=ended, duration=600))["verification"] == "PENDING"
    db.execute(update(Run).where(Run.id == run_id).values(verification_deadline=ago(seconds=1)))
    db.commit()
    assert data(finish(db_client, me, run_id, ended=ended, duration=600))["verification"] == "UNVERIFIED"  # 응답을 못 받아 다시 보냄
    assert run_row(db, run_id).unverified_reason == "TIMEOUT"


def test_corrupt_photo_counts_as_retake(db_client, me):
    run_id = finished(db_client, me)
    out = data(photo(db_client, me, run_id, "corrupt.jpg"))
    assert (out["status"], out["retakesLeft"]) == ("RETAKE_REQUIRED", P.max_retakes)  # 503이 아니라 다시 찍기(횟수 셈)
    assert data(photo(db_client, me, run_id, "corrupt.jpg"))["retakesLeft"] == P.max_retakes - 1


class ClosedByOtherOcr:
    """읽는 동안 마감이 지나고, 늦게 온 다른 요청(목록 조회 등)이 마감으로 닫음. 이 사진은 마감 전에 왔음."""

    def __init__(self, db, run_id):
        self.db, self.run_id = db, run_id

    def read(self, photos):
        time.sleep(0.3)  # 사진은 마감 전에 옴 → 읽는 동안 마감(도착 뒤·지금 앞)이 지나감
        self.db.execute(update(Run).where(Run.id == self.run_id).values(verification_deadline=ago(seconds=0.1)))
        close_expired(self.db, run_id=self.run_id)
        self.db.commit()
        return FakeOcr().read(photos)


def test_photo_in_time_wins_over_close_during_ocr(db_client, me, db, fresh_ocr):
    run_id = finished(db_client, me)
    app.dependency_overrides[get_ocr] = lambda: ClosedByOtherOcr(db, run_id)
    assert data(read_then(db_client, me, run_id, "d3000_t1000.jpg"))["status"] == "VERIFIED"
    assert run_row(db, run_id).official_source == "CONSOLE"
