from ..core.model import CamelModel
from .course import Course
from .friend import RunningFriend
from .progress import Progress
from .run import RunSummary
from .user import WeeklyGoal


class Home(CamelModel):
    """홈 진입 때 한 번에(B-01에서 B-04). 각 항목 API도 따로 있음."""

    weekly_goal: WeeklyGoal
    progress: Progress
    running_friends: list[RunningFriend]
    recent_runs: list[RunSummary]
    courses: list[Course]
