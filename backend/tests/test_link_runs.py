"""함께 달린 사람을 원격 허브로(계획 0073): 기존 테스트를 일반 API → 내부 API 길로 한 번 더."""
import pytest

from tests.test_run_records import (  # noqa: F401 (다시 모으는 테스트)
    test_companion_who_left_the_service_is_dropped,
    test_companions_are_saved_at_finish_and_marked_friend_when_read,
    test_companions_start_fresh_each_run,
    test_discard_stops_collecting,
)
from tests.test_runs import db, me  # noqa: F401 (픽스처)

pytestmark = pytest.mark.usefixtures("remote_link")
