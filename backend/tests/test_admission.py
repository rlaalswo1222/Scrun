"""동시 처리 제한·빠른 거절(계획 0071, app/core/admission.py). 작은 앱에 번호표 1장을 걸고 동시 요청을 보낸다."""
import asyncio

import httpx
from fastapi import FastAPI

from app.core import request_log as rl
from app.core.admission import Admission


def _app(limit: int, wait_s: float, hold: asyncio.Event) -> FastAPI:
    app = FastAPI()

    @app.get("/slow")
    async def slow():
        await hold.wait()
        return {"ok": True}

    app.middleware("http")(Admission(limit, wait_s))
    return app


def _run(coro):
    return asyncio.run(coro)


def test_over_limit_gets_fast_503_with_retry_after():
    async def go():
        hold = asyncio.Event()
        app = _app(1, 0.2, hold)
        async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://t") as c:
            first = asyncio.create_task(c.get("/slow"))
            await asyncio.sleep(0.05)              # 첫 요청이 번호표를 잡음
            second = await c.get("/slow")          # 0.2초 기다리다 거절
            hold.set()
            return await first, second

    first, second = _run(go())
    assert first.status_code == 200
    assert second.status_code == 503
    assert second.headers["Retry-After"] == "1"
    body = second.json()
    assert body["error"]["code"] == "SERVICE_UNAVAILABLE" and body["error"]["details"] == {"reason": "BUSY"}


def test_waits_for_a_ticket_and_logs_queue_time():
    async def go():
        hold = asyncio.Event()
        app = _app(1, 2.0, hold)
        async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://t") as c:
            first = asyncio.create_task(c.get("/slow"))
            await asyncio.sleep(0.05)
            stats = rl._RequestStats()
            token = rl._request_stats.set(stats)   # 두 번째 요청의 번호표 기다림을 이 통계로 받음
            try:
                second = asyncio.create_task(c.get("/slow"))
                await asyncio.sleep(0.2)
                hold.set()                            # 첫 요청이 끝나 번호표 반납 → 두 번째가 들어감
                results = await first, await second
            finally:
                rl._request_stats.reset(token)
            return results, stats

    (first, second), stats = _run(go())
    assert first.status_code == 200 and second.status_code == 200
    assert stats.queue_ms >= 150


def test_probe_bypasses_the_gate():
    async def go():
        hold = asyncio.Event()
        app = _app(1, 0.2, hold)

        @app.get("/health")
        async def health():
            return {"ok": True}

        async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://t") as c:
            first = asyncio.create_task(c.get("/slow"))
            await asyncio.sleep(0.05)
            probe = await c.get("/health", headers={"X-Scrun-Probe": "1"})   # 번호표가 없어도 바로
            busy = await c.get("/health")
            hold.set()
            await first
            return probe, busy

    probe, busy = _run(go())
    assert probe.status_code == 200
    assert busy.status_code == 503


def test_ticket_is_returned_when_route_fails():
    async def go():
        app = FastAPI()

        @app.get("/boom")
        async def boom():
            raise RuntimeError("x")

        @app.get("/ok")
        async def ok():
            return {"ok": True}

        app.middleware("http")(Admission(1, 0.2))
        async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app, raise_app_exceptions=False),
                                     base_url="http://t") as c:
            failed = await c.get("/boom")
            after = await c.get("/ok")              # 번호표가 반납됐으면 바로 들어감
            return failed, after

    failed, after = _run(go())
    assert failed.status_code == 500
    assert after.status_code == 200
