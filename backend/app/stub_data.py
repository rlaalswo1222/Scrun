"""스텁이 돌려주는 고정 예시(JSON 그대로 camelCase).

계획 0014 2단계: 진짜 DB 대신 이 값을 돌려준다. scripts/export_openapi.py가 이 값을 공통 껍데기로 감싸
docs/api/examples/에 내보내고, Unity FakeApi가 같은 파일을 쓴다. 숫자는 화면 확인용 예시이며 실측이 아니다.
"""

DEMO_USER_ID = "usr_demo"

ME = {
    "userId": DEMO_USER_ID,
    "nickname": "새벽러너",
    "heightCm": 175,
    "weightKg": 70,
    "friendCode": "SCR-7K2Q",
    "timezone": "Asia/Seoul",
    "createdAt": "2026-11-01T09:00:00Z",
}

AGREEMENTS = [
    {"agreementId": "terms", "title": "서비스 이용약관", "required": True, "version": "2026-11-01", "url": "https://example.com/terms"},
    {"agreementId": "privacy", "title": "개인정보 수집·이용", "required": True, "version": "2026-11-01", "url": "https://example.com/privacy"},
    {"agreementId": "notice", "title": "운동 알림 받기", "required": False, "version": "2026-11-01", "url": "https://example.com/notice"},
]

LOGIN_EXISTING = {
    "isNewUser": False,
    "accessToken": "stub-access",
    "refreshToken": "stub-refresh",
    "expiresInS": 3600,
    "signupToken": None,
}

LOGIN_NEW = {"isNewUser": True, "accessToken": None, "refreshToken": None, "expiresInS": None, "signupToken": "signup"}

TOKENS = {"accessToken": "stub-access", "refreshToken": "stub-refresh", "expiresInS": 3600}

SETTINGS = {"feedShareDefault": True}

CALIBRATION = {
    "points": [
        {"speedKmh": 4.0, "gait": "WALK", "durationS": 20},
        {"speedKmh": 7.0, "gait": "RUN", "durationS": 20},
        {"speedKmh": 8.5, "gait": "RUN", "durationS": 20},
        {"speedKmh": 10.0, "gait": "RUN", "durationS": 20},
    ],
    "model": {"runA": 1.2, "runB": 2.1, "runC": 0.012, "walkSlope": 0.035},
    "device": "iPhone15,4",
    "createdAt": "2026-11-05T10:00:00Z",
}

WEEKLY_GOAL = {
    "targetCount": 3,
    "cycleStart": "2026-11-03",
    "cycleEnd": "2026-11-09",
    "doneCount": 2,
    "daysLeft": 3,
    "nextTargetCount": None,
    "history": [
        {"cycleStart": "2026-10-27", "targetCount": 3, "doneCount": 3},
    ],
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

PROGRESS = {
    "level": 2,
    "xpM": 4120,
    "nextLevelAtM": 10000,
    "nextLevelRemainingM": 5880,
    "outfitStage": 2,
    "verifiedCount": 3,
    "impacts": IMPACTS,
    "selectedImpactId": "imp_wind",
}

COURSES = [
    {
        "courseId": "crs_island_loop",
        "name": "섬 루프 코스(가칭)",
        "type": "LOOP_3D",
        "lengthM": 3000,
        "runnersNow": 7,
        "thumbnailUrl": "https://example.com/course/island_loop.png",
    },
]

COURSE_DETAIL = {
    **COURSES[0],
    "description": "한 바퀴 3km를 계속 도는 3D 루프 코스.",
    "mood": "해안 길과 숲을 지나는 아침",
    "maxRunnersPerMap": 20,
}

MAP_ENTRY = {
    "mapInstanceId": "map_01",
    "courseId": "crs_island_loop",
    "entryPositionM": 1240.0,
    "realtime": {"url": "wss://example.com/rt", "token": "stub-rt-token"},
}

RUNS = [
    {
        "runId": "run_01HZX",
        "startedAt": "2026-11-05T11:02:00Z",
        "courseId": "crs_island_loop",
        "courseName": "섬 루프 코스(가칭)",
        "distanceM": 3120,
        "durationS": 1080,
        "avgPaceSPerKm": 346,
        "verification": "VERIFIED",
        "shared": True,
    },
    {
        "runId": "run_01HZW",
        "startedAt": "2026-11-03T10:40:00Z",
        "courseId": "crs_island_loop",
        "courseName": "섬 루프 코스(가칭)",
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

ACTIVE_RUN_NONE = {"run": None, "recoverableUntil": None}

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

VERIFIED = {
    "status": "VERIFIED",
    "reason": None,
    "console": {"distanceM": 3120, "durationS": 1080},
    "sensor": {"distanceM": 3080, "durationS": 1080},
    "comparison": [
        {"item": "distance", "console": 3120, "sensor": 3080, "diffRatio": 0.013, "ok": True},
        {"item": "duration", "console": 1080, "sensor": 1080, "diffRatio": 0.0, "ok": True},
    ],
    "retakeDeadline": None,
    "rewards": REWARDS,
}

RETAKE_REQUIRED = {
    "status": "RETAKE_REQUIRED",
    "reason": None,
    "console": None,
    "sensor": {"distanceM": 3080, "durationS": 1080},
    "comparison": [],
    "retakeDeadline": "2026-11-06T10:23:00Z",
    "rewards": None,
}

UNVERIFIED_SKIPPED = {
    "status": "UNVERIFIED",
    "reason": "SKIPPED",
    "console": None,
    "sensor": {"distanceM": 3080, "durationS": 1080},
    "comparison": [],
    "retakeDeadline": None,
    "rewards": {
        "xpBeforeM": 4120,
        "xpAfterM": 4120,
        "levelBefore": 2,
        "levelAfter": 2,
        "leveledUp": False,
        "outfitStageAfter": 2,
        "nextLevelRemainingM": 5880,
        "impactsUnlocked": [],
        "weeklyGoal": {"counted": False, "doneCount": 2, "targetCount": 3},
    },
}

FRIENDS = [
    {"userId": "usr_f1", "nickname": "바닷바람", "level": 3, "verifiedCount": 6, "runningNow": True},
    {"userId": "usr_f2", "nickname": "주말러너", "level": 1, "verifiedCount": 1, "runningNow": False},
]

FRIEND_PROFILE = {
    **FRIENDS[0],
    "outfitStage": 3,
    "recentVerifiedRuns": [
        {"runId": "run_f1_01", "startedAt": "2026-11-05T21:00:00Z", "distanceM": 5000, "durationS": 1620, "avgPaceSPerKm": 324},
    ],
}

FEED = [
    {
        "feedId": "fd_01",
        "userId": "usr_f1",
        "nickname": "바닷바람",
        "runId": "run_f1_01",
        "startedAt": "2026-11-05T21:00:00Z",
        "distanceM": 5000,
        "durationS": 1620,
        "avgPaceSPerKm": 324,
        "verification": "VERIFIED",
    },
    {
        "feedId": "fd_02",
        "userId": "usr_f2",
        "nickname": "주말러너",
        "runId": "run_f2_01",
        "startedAt": "2026-11-04T19:30:00Z",
        "distanceM": 2100,
        "durationS": 840,
        "avgPaceSPerKm": 400,
        "verification": "UNVERIFIED",
    },
]

RUNNING_FRIENDS = [
    {
        "userId": "usr_f1",
        "nickname": "바닷바람",
        "courseId": "crs_island_loop",
        "courseName": "섬 루프 코스(가칭)",
        "distanceM": 1800,
        "paceSPerKm": 330,
    },
]

HOME = {
    "weeklyGoal": WEEKLY_GOAL,
    "progress": PROGRESS,
    "runningFriends": RUNNING_FRIENDS,
    "recentRuns": RUNS,
    "courses": COURSES,
}
