"""시간순으로 정렬되는 ID(ULID, 26자). 앞 10자 = 밀리초 시각, 뒤 16자 = 무작위."""
import os
import time

_ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"  # Crockford base32


def _encode(value: int, length: int) -> str:
    out = []
    for _ in range(length):
        value, r = divmod(value, 32)
        out.append(_ALPHABET[r])
    return "".join(reversed(out))


def new_id() -> str:
    ms = int(time.time() * 1000)
    rand = int.from_bytes(os.urandom(10), "big")
    return _encode(ms, 10) + _encode(rand, 16)
