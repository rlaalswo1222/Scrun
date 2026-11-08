"""닉네임 규칙(정규화·형식·금칙어). DB가 필요한 중복 검사는 test_auth.py."""
import pytest

from app.core import nickname as nick


@pytest.mark.parametrize("raw,expected", [
    ("  새벽러너  ", "새벽러너"),
    ("ＲＵＮＮＥＲ１", "RUNNER1"),  # 전각 → 반각
    ("run_9", "run_9"),
])
def test_normalize_then_format_ok(raw, expected):
    assert nick.validate_field(raw) == expected


@pytest.mark.parametrize("raw", [
    "   ",  # 공백만
    "가",  # 1자
    "가" * 13,  # 13자
    " 가 ",  # 정규화하면 1자
    "새벽 러너",  # 가운데 공백
    "러너​",  # zero-width space
    "러　너",  # 전각 공백(가운데)
    "ㅋㅋ러너",  # 자모 단독
    "러너🏃",
    "runner!",
    "‮runner",  # 방향 바꾸기 문자
])
def test_invalid_format(raw):
    with pytest.raises(ValueError):
        nick.validate_field(raw)


def test_length_counts_after_normalize():
    assert nick.validate_field("가" * 12) == "가" * 12
    assert nick.validate_field("  " + "가" * 12 + "  ") == "가" * 12


@pytest.mark.parametrize("name", ["admin", "ADMIN", "a_d_m_i_n", "SuperAdmin", "운영자입니다", "스크런", "Scrun_1"])
def test_banned_words(name):
    assert not nick.is_allowed(nick.normalize(name))


def test_fullwidth_banned_word_caught_after_normalize():
    assert not nick.is_allowed(nick.normalize("ＡＤＭＩＮ"))


def test_allowed():
    assert nick.is_allowed("새벽러너") and nick.is_allowed("runner_kim")
