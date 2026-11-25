"""부하 시험용 가짜 데이터(계획 0070). 시험 서버(KAKAO_MODE=fake)에서만 돈다 — 운영 DB를 지우지 않게.

만드는 것(사람마다): 계정·카카오 연결(가짜)·약관 동의·진행 상태·보정값, 친구 0–N명, 기록 R개(+ 1초 속도 시계열 60초 묶음,
인증 시도 1–2개). 기록 상태는 인증 55% · 미인증 40% · 버림 5%(인증 대기 PENDING은 만들지 않음: 마감이 지난 대기 기록은
첫 조회 때 서버가 닫으며 쓰기를 해서 같은 시험을 다시 돌릴 때 결과가 달라짐).
경험치·레벨은 인증 기록 거리로 계산(core/growth와 같은 식). 보상 기록(run_rewards)·주간 목표·임팩트는 넣지 않음.

같은 --seed·--users·--anchor면 같은 데이터(ID 포함). 날짜는 --anchor(기본 오늘 UTC) 전 180일 안.
k6는 kakaoAccessToken `k_{kakao_base + i}`(i = 0 … users−1)로 이 계정에 로그인한다.

실행(시험 서버의 backend/perf 폴더, 이미지에 scripts가 들어 있음):
  docker compose -f docker-compose.perf.yml --env-file .env.perf exec api python -m scripts.seed_load --users 10 --wipe
  단계(계획 0070): --users 10 → 100 → 1000 → 10000 (사람당 기록 100 = 기록 1천 → 100만). 10만 기록부터 --samples-every로 시계열을 줄임
"""
import argparse
import random
import time
from datetime import date, datetime, timedelta, timezone

import psycopg
from psycopg.types.json import Jsonb

from app.core import growth
from app.core.config import settings
from app.core.friend_code import ALPHABET as CODE_ALPHABET, LENGTH as CODE_LENGTH

_ULID = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
COURSES = ("crs_space_loop", "crs_island_tour", "crs_lake_loop", "crs_forest_run", "crs_coast_run")
NICK_PREFIX = "부하"
SAMPLE_CHUNK_S = 60   # 앱이 60초마다 올림(계획 0037, Api.PutSamplesAsync)


class Seeder:
    def __init__(self, rng: random.Random, anchor: datetime):
        self.rng = rng
        self.anchor = anchor

    def ulid(self, at: datetime) -> str:
        ms = int(at.timestamp() * 1000)
        rand = self.rng.getrandbits(80)
        out = []
        for value, length in ((ms, 10), (rand, 16)):
            chars = []
            for _ in range(length):
                value, r = divmod(value, 32)
                chars.append(_ULID[r])
            out.append("".join(reversed(chars)))
        return "".join(out)

    def friend_code(self, i: int) -> str:
        # 사람 번호를 그대로 32진수로(겹치지 않음) + 앞은 시드 무작위. 형식은 core/friend_code와 같음
        tail = []
        v = i
        for _ in range(4):
            v, r = divmod(v, len(CODE_ALPHABET))
            tail.append(CODE_ALPHABET[r])
        head = "".join(self.rng.choice(CODE_ALPHABET) for _ in range(CODE_LENGTH - 4))
        return head + "".join(reversed(tail))

    def when(self, days_back_max: int = 180) -> datetime:
        return self.anchor - timedelta(seconds=self.rng.uniform(600, days_back_max * 86400))


def _url() -> str:
    return settings.database_url.replace("postgresql+psycopg://", "postgresql://", 1)


def wipe(cur) -> None:
    # users를 지우면 딸린 행(기록·시계열·인증·친구·진행 상태 등)은 ON DELETE CASCADE로 같이 지워짐
    cur.execute("TRUNCATE users CASCADE")


def build(args) -> dict[str, list[tuple]]:
    rng = random.Random(args.seed)
    anchor = datetime.combine(args.anchor, datetime.min.time(), tzinfo=timezone.utc)
    s = Seeder(rng, anchor)
    rows: dict[str, list[tuple]] = {k: [] for k in (
        "users", "auth_identities", "user_agreements", "user_progress", "calibrations",
        "friendships", "runs", "run_samples", "verifications")}

    user_ids = []
    for i in range(args.users):
        created = s.when(365)
        uid = s.ulid(created)
        user_ids.append(uid)
        rows["users"].append((uid, f"{NICK_PREFIX}{i}", rng.randint(155, 190), rng.randint(45, 95), "Asia/Seoul",
                              s.friend_code(i), True, created, created))
        rows["auth_identities"].append((s.ulid(created), uid, "KAKAO", str(args.kakao_base + i), created))
        for agreement_id, version in args.agreements:
            rows["user_agreements"].append((uid, agreement_id, version, created))
        rows["calibrations"].append((s.ulid(created), uid,
                                     Jsonb([{"speedKmh": 8, "gait": "RUN", "durationS": 15}]),
                                     Jsonb({"seed": True}), "seed-load", created))

    # 친구: 사람마다 0 … friends_max명 목표(이미 맺어진 쪽도 셈). 순서 규칙 user_a < user_b
    pairs: set[tuple[str, str]] = set()
    degree = [0] * args.users
    for i in range(args.users):
        want = rng.randint(0, min(args.friends_max, args.users - 1))
        tries = 0
        while degree[i] < want and tries < want * 4:
            tries += 1
            j = rng.randrange(args.users)
            if j == i or degree[j] >= args.friends_max:
                continue
            a, b = sorted((user_ids[i], user_ids[j]))
            if (a, b) in pairs:
                continue
            pairs.add((a, b))
            degree[i] += 1
            degree[j] += 1
    rows["friendships"] = [(a, b, anchor - timedelta(days=200)) for a, b in sorted(pairs)]

    run_no = 0
    for i, uid in enumerate(user_ids):
        xp_m = verified = 0
        for _ in range(args.runs_per_user):
            run_no += 1
            started = s.when()
            dur = rng.randint(10 * 60, 60 * 60)
            kmh = rng.uniform(5.5, 12.5)
            dist = int(kmh / 3.6 * dur)
            ended = started + timedelta(seconds=dur)
            rid = s.ulid(started)
            roll = rng.random()
            goal_type = rng.choice(("NONE", "DISTANCE", "TIME"))
            goal_value = {"NONE": None, "DISTANCE": rng.choice((3000, 5000, 10000)), "TIME": rng.choice((1200, 1800, 3600))}[goal_type]
            console_d = console_s = source = off_d = off_s = reason = None
            if roll < 0.55:
                status = "VERIFIED"
                console_d, console_s = int(dist * rng.uniform(0.9, 1.1)), dur + rng.randint(0, 120)
                source, off_d, off_s = "CONSOLE", console_d, console_s
                xp_m += console_d
                verified += 1
                rows["verifications"].append((s.ulid(ended), rid, "PHOTO", console_d, console_s, "VERIFIED", None,
                                              Jsonb([]), [f"{run_no:064x}"], True, ended))
            elif roll < 0.95:
                status = "UNVERIFIED"
                source, off_d, off_s = "SENSOR", dist, dur
                reason = rng.choice(("SKIPPED", "SKIPPED", "MISMATCH", "TIMEOUT"))
                method = "SKIP" if reason in ("SKIPPED", "TIMEOUT") else "PHOTO"
                read = (int(dist * 0.5), dur) if method == "PHOTO" else (None, None)
                rows["verifications"].append((s.ulid(ended), rid, method, read[0], read[1], "UNVERIFIED", reason,
                                              Jsonb([]), [f"{run_no:064x}"] if method == "PHOTO" else [], None, ended))
            else:
                status = "DISCARDED"
            rows["runs"].append((rid, uid, f"seed-{run_no}", rng.choice(COURSES), None, goal_type, goal_value, status,
                                 started, ended, dist, dur, console_d, console_s, source, off_d, off_s, reason,
                                 ended + timedelta(minutes=10) if status != "DISCARDED" else None,
                                 rng.random() < 0.9, started, ended))
            if status != "DISCARDED" and run_no % args.samples_every == 0:
                for off in range(0, dur, SAMPLE_CHUNK_S):
                    n = min(SAMPLE_CHUNK_S, dur - off)
                    rows["run_samples"].append((rid, float(off), 1.0, [round(kmh + rng.uniform(-0.4, 0.4), 2) for _ in range(n)]))
        level = growth.level_of(xp_m)
        rows["user_progress"].append((uid, xp_m, level, growth.outfit_of(level), verified, anchor))
    return rows


COLUMNS = {
    "users": ("id, nickname, height_cm, weight_kg, timezone, friend_code, feed_share_default, created_at, updated_at", None),
    "auth_identities": ("id, user_id, provider, provider_user_id, created_at", None),
    "user_agreements": ("user_id, agreement_id, version, agreed_at", None),
    "calibrations": ("id, user_id, points, model, device, created_at", None),
    "user_progress": ("user_id, xp_m, level, outfit_stage, verified_count, updated_at", None),
    "friendships": ("user_a_id, user_b_id, created_at", None),
    "runs": ("id, user_id, client_run_id, course_id, map_instance_id, goal_type, goal_value, status, started_at, ended_at, "
             "sensor_distance_m, sensor_duration_s, console_distance_m, console_duration_s, official_source, "
             "official_distance_m, official_duration_s, unverified_reason, verification_deadline, shared, created_at, updated_at",
             None),
    "run_samples": ("run_id, offset_s, interval_s, speeds_kmh", ["text", "real", "real", "real[]"]),
    "verifications": ("id, run_id, method, read_distance_m, read_duration_s, status, reason, comparison, photo_hashes, "
                      "confirmed, created_at", None),
}


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--users", type=int, required=True)
    p.add_argument("--runs-per-user", type=int, default=100)
    p.add_argument("--friends-max", type=int, default=50)
    p.add_argument("--samples-every", type=int, default=1, help="N번째 기록마다 시계열(1 = 모두)")
    p.add_argument("--seed", type=int, default=70)
    p.add_argument("--kakao-base", type=int, default=100000)
    p.add_argument("--anchor", type=date.fromisoformat, default=datetime.now(timezone.utc).date())
    p.add_argument("--wipe", action="store_true", help="먼저 모든 사용자와 딸린 행을 지움")
    a = p.parse_args()

    if settings.kakao_mode != "fake":
        raise SystemExit("KAKAO_MODE=fake 서버에서만 돕니다(운영 DB 보호).")

    t0 = time.perf_counter()
    with psycopg.connect(_url()) as conn, conn.cursor() as cur:
        a.agreements = cur.execute("SELECT id, version FROM agreements WHERE required ORDER BY id").fetchall()
        known = {r[0] for r in cur.execute("SELECT id FROM courses")}
        if missing := set(COURSES) - known:
            raise SystemExit(f"코스가 없습니다(마이그레이션 먼저): {sorted(missing)}")
        if a.wipe:
            wipe(cur)
        elif cur.execute("SELECT 1 FROM users WHERE nickname LIKE %s LIMIT 1", (NICK_PREFIX + "%",)).fetchone():
            raise SystemExit("이미 시드 데이터가 있습니다. --wipe로 지우고 다시 넣으세요.")

        rows = build(a)
        t_build = time.perf_counter()
        for table, (cols, types) in COLUMNS.items():
            with cur.copy(f"COPY {table} ({cols}) FROM STDIN") as cp:
                if types:
                    cp.set_types(types)
                for row in rows[table]:
                    cp.write_row(row)
        conn.commit()
        cur.execute("ANALYZE")
        size = cur.execute("SELECT pg_size_pretty(pg_database_size(current_database()))").fetchone()[0]

    counts = ", ".join(f"{t} {len(r):,}" for t, r in rows.items())
    print(f"넣음: {counts}")
    print(f"만들기 {t_build - t0:.1f}초, 넣기 {time.perf_counter() - t_build:.1f}초, DB 크기 {size}")


if __name__ == "__main__":
    main()
