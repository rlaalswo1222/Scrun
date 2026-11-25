"""친구 "달리는 중"·같이 달리기 입장을 원격 허브로(계획 0073)."""
import pytest

from tests.test_runs import db  # noqa: F401 (픽스처)
from tests.test_friends import (  # noqa: F401 (픽스처·다시 모으는 테스트)
    running_friend,
    test_run_together_checks_like_a_normal_entry,
    test_run_together_needs_the_friend_in_that_map,
    test_run_together_wraps_past_the_start_line,
    test_running_friends_from_realtime_room,
    two,
)

pytestmark = pytest.mark.usefixtures("remote_link")
