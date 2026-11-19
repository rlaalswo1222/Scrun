"""스텁이 돌려주는 고정 예시(JSON 그대로 camelCase).

계획 0014 2단계: 진짜 DB 대신 이 값을 돌려준다. scripts/export_openapi.py가 이 값을 공통 껍데기로 감싸
docs/api/examples/에 내보낸다(앱 개발 참고용). 숫자는 화면 확인용 예시이며 실측이 아니다.
"""

DEMO_USER_ID = "usr_demo"

ME = {
    "userId": DEMO_USER_ID,
    "nickname": "새벽러너",
    "heightCm": 175,
    "weightKg": 70,
    "friendCode": "K7Q2M9XA",
    "timezone": "Asia/Seoul",
    "createdAt": "2026-11-01T09:00:00Z",
}


WEEKLY_GOAL_EMPTY = {
    "targetCount": None,
    "cycleStart": None,
    "cycleEnd": None,
    "doneCount": 0,
    "daysLeft": None,
    "nextTargetCount": None,
    "history": [],
}

IMPACTS = [
    {"impactId": "imp_spark", "name": "발자국 불꽃", "unlockAtCount": 1, "unlocked": True},
    {"impactId": "imp_wind", "name": "바람 꼬리", "unlockAtCount": 2, "unlocked": True},
    {"impactId": "imp_wave", "name": "물결", "unlockAtCount": 4, "unlocked": False},
    {"impactId": "imp_star", "name": "별 가루", "unlockAtCount": 7, "unlocked": False},
    {"impactId": "imp_aurora", "name": "오로라", "unlockAtCount": 12, "unlocked": False},
]

RUNS = [
    {
        "runId": "run_01HZX",
        "startedAt": "2026-11-05T11:02:00Z",
        "courseId": "crs_lake_loop",
        "courseName": "노을 호수 한 바퀴",
        "distanceM": 3120,
        "durationS": 1080,
        "avgPaceSPerKm": 346,
        "verification": "VERIFIED",
        "shared": True,
    },
    {
        "runId": "run_01HZW",
        "startedAt": "2026-11-03T10:40:00Z",
        "courseId": "crs_lake_loop",
        "courseName": "노을 호수 한 바퀴",
        "distanceM": 2450,
        "durationS": 900,
        "avgPaceSPerKm": 367,
        "verification": "UNVERIFIED",
        "shared": True,
    },
]

SPEED_SERIES = {"intervalS": 30.0, "speedsKmh": [6.0, 8.0, 9.5, 10.0, 10.0, 10.5, 10.0, 9.0]}

RUN_DETAIL = {
    **RUNS[0],
    "officialSource": "CONSOLE",
    "sensor": {"distanceM": 3080, "durationS": 1080},
    "console": {"distanceM": 3120, "durationS": 1080},
    "speedSeries": SPEED_SERIES,
    "companions": [
        {"nickname": "숲길러너", "kind": "RUNNER"},
        {"nickname": "바닷바람", "kind": "FRIEND"},
        {"nickname": "페이스 9", "kind": "BOT"},
    ],
    "ghostRunId": None,
}

RUN_STARTED = {"runId": "run_01J00", "clientRunId": "c-2026-11-06-001", "startedAt": "2026-11-06T10:00:00Z"}

ACTIVE_RUN = {
    "run": {**RUNS[0], "runId": "run_01J00", "verification": "PENDING", "distanceM": 1500, "durationS": 540},
    "recoverableUntil": "2026-11-07T10:09:00Z",
}

ACTIVE_RUN_NONE = {"run": None, "recoverableUntil": None, "lastActiveAt": None}

GHOST = {"runId": "run_01HZX", "distanceM": 3120, "durationS": 1080, "speedSeries": SPEED_SERIES}

REWARDS = {
    "xpBeforeM": 1000,
    "xpAfterM": 4120,
    "levelBefore": 1,
    "levelAfter": 2,
    "leveledUp": True,
    "outfitStageAfter": 2,
    "nextLevelRemainingM": 5880,
    "impactsUnlocked": [IMPACTS[1]],
    "weeklyGoal": {"counted": True, "doneCount": 2, "targetCount": 3},
}
