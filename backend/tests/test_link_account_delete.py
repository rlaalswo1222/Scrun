"""계정 삭제 때 허브에서 빼기를 원격 허브로(계획 0073)."""
import pytest

from tests.test_runs import db  # noqa: F401 (픽스처)
from tests.test_account_delete import (  # noqa: F401 (픽스처·다시 모으는 테스트)
    kakao,
    test_companion_deleted_mid_run_is_not_saved,
    test_delete_removes_everything_and_unlinks_kakao,
)

pytestmark = pytest.mark.usefixtures("remote_link")
