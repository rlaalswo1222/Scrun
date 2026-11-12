"""가짜 러너: 로컬 서버(KAKAO_MODE=fake)에 가짜 계정으로 들어가 실시간 방에서 일정한 속도로 달린다(계획 0029 확인용).

에디터 하나로 "같은 맵에 다른 사람" 확인, 아이폰 없이 여러 명 부하 보기, 왕복 지연 재기에 쓴다.
가짜 카카오 서버에서만 된다(진짜 카카오 서버는 k_ 토큰을 거절). 처음이면 가입·보정(가짜 값)까지 한다.

실행(backend 폴더):
  ../.venv/Scripts/python -m scripts.fake_runner --base http://127.0.0.1:8001 --kakao k_901 --kmh 9
  ../.venv/Scripts/python -m scripts.fake_runner --kakao k_902 --kmh 11 --course crs_island_tour --seconds 120
끝내기: Ctrl+C 또는 --seconds가 지나면 leave를 보내고 나감
"""
import argparse
import asyncio
import json
import statistics
import time

import httpx
import websockets


def _data(r: httpx.Response) -> dict:
    j = r.json()
    if j.get("resultType") != "SUCCESS":
        raise SystemExit(f"{r.request.method} {r.request.url} 실패 {r.status_code}: {j.get('error')}")
    return j["success"]["data"]


def login(api: httpx.Client, kakao: str) -> str:
    """access 토큰. 처음이면 가입하고 가짜 보정을 올린다."""
    first = _data(api.post("/v1/auth/kakao", json={"kakaoAccessToken": kakao}))
    if not first["isNewUser"]:
        return first["accessToken"]
    nickname = f"가짜{kakao.split('_')[-1]}"[:12]
    signed = _data(api.post("/v1/auth/signup", headers={"Authorization": f"Bearer {first['signupToken']}"},
                            json={"agreedIds": ["terms", "privacy", "age14"], "nickname": nickname, "heightCm": 175, "weightKg": 70}))
    token = signed["accessToken"]
    _data(api.put("/v1/users/me/calibration", headers={"Authorization": f"Bearer {token}"},
                  json={"points": [{"speedKmh": 8, "gait": "RUN", "durationS": 15}], "model": {"fake": True},
                        "device": "fake-runner"}))
    print(f"가입: {nickname}")
    return token


async def run(entry: dict, kmh: float, seconds: float) -> None:
    rt = entry["realtime"]
    d, lap = float(entry["entryPositionM"]), None
    names: dict[str, str] = {}
    rtts: list[float] = []
    async with websockets.connect(rt["url"], additional_headers={"Authorization": f"Bearer {rt['token']}"}) as ws:
        start = last = time.monotonic()
        next_ping = 0.0

        async def receive():
            nonlocal lap
            async for raw in ws:
                m = json.loads(raw)
                kind = m["type"]
                if kind == "welcome":
                    lap = m["courseLengthM"]
                    names.update({r["userId"]: r["nickname"] for r in m["runners"]})
                    print(f"붙음: 코스 {lap:.0f}m, 지금 {len(m['runners'])}명 → {', '.join(names.values()) or '없음'}")
                elif kind == "joined":
                    names[m["runner"]["userId"]] = m["runner"]["nickname"]
                    print(f"들어옴: {m['runner']['nickname']}")
                elif kind == "left":
                    print(f"나감: {names.pop(m['userId'], m['userId'])}")
                elif kind == "pong":
                    rtts.append(time.monotonic() * 1000 - m["t"])
                    print(f"왕복 {rtts[-1]:.0f}ms (중앙값 {statistics.median(rtts):.0f}ms, {len(rtts)}번)")

        receiver = asyncio.create_task(receive())
        try:
            while seconds <= 0 or time.monotonic() - start < seconds:
                now = time.monotonic()
                d += kmh / 3.6 * (now - last)
                last = now
                pos = d % lap if lap else d
                await ws.send(json.dumps({"type": "state", "d": round(pos, 2), "v": kmh, "running": True}))
                if now >= next_ping:
                    next_ping = now + 5
                    await ws.send(json.dumps({"type": "ping", "t": now * 1000}))
                if receiver.done():
                    receiver.result()  # 서버가 닫았으면 이유를 보여 줌
                    break
                await asyncio.sleep(0.5)
        finally:
            receiver.cancel()
            try:
                await ws.send(json.dumps({"type": "leave"}))  # 바로 빠짐(안 보내면 서버가 30초 남김)
            except websockets.ConnectionClosed:
                pass


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--base", default="http://127.0.0.1:8001", help="API 주소(가짜 카카오 서버)")
    p.add_argument("--kakao", default="k_901", help="가짜 카카오 토큰(k_숫자). 숫자가 다르면 다른 사람")
    p.add_argument("--course", default="crs_lake_loop")
    p.add_argument("--kmh", type=float, default=9.0)
    p.add_argument("--seconds", type=float, default=0, help="0이면 Ctrl+C까지")
    a = p.parse_args()
    with httpx.Client(base_url=a.base, timeout=10) as api:
        token = login(api, a.kakao)
        entry = _data(api.post(f"/v1/courses/{a.course}/entries", headers={"Authorization": f"Bearer {token}"}))
    print(f"입장: {entry['courseId']} {entry['entryPositionM']}m, {a.kmh} km/h")
    try:
        asyncio.run(run(entry, a.kmh, a.seconds))
    except KeyboardInterrupt:
        pass
    except websockets.ConnectionClosed as e:
        print(f"서버가 닫음: {e.code} {e.reason}")


if __name__ == "__main__":
    main()
