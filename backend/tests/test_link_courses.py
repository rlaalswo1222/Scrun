"""맵 정원(지금 있는 사람 수)을 원격 허브로(계획 0073)."""
import pytest

from tests.test_courses import (  # noqa: F401 (픽스처·다시 모으는 테스트)
    db,
    me,
    test_21st_person_is_refused,
    test_concurrent_entries_never_exceed_max,
    test_disconnected_runner_counts_until_grace_ends,
    test_runners_now_counts_people_in_the_map,
)

pytestmark = pytest.mark.usefixtures("remote_link")
