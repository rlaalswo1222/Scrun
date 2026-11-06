"""닉네임 규칙. 다른 러너에게 보이므로 금칙어를 거른다(심사 1.2). 목록은 운영하며 늘린다(초안)."""
BANNED_WORDS = ("admin", "관리자", "운영자", "스크런", "scrun")


def normalize(nickname: str) -> str:
    return nickname.strip()


def is_allowed(nickname: str) -> bool:
    lowered = nickname.lower()
    return not any(word in lowered for word in BANNED_WORDS)
