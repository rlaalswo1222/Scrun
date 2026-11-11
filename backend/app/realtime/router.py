"""실시간 같이 뛰기 WebSocket(계획 0029, ADR-0010). 메시지 모양은 docs/api/realtime.md.

주소: `GET /v1/realtime` (WebSocket), 헤더 `Authorization: Bearer {realtime.token}`(맵 입장 응답, 그 맵 하나, 6시간).
토큰을 주소(?token=)에 넣지 않는다: 서버·프록시 접속 기록에 주소가 그대로 남기 때문.
연결마다 받기(receive)와 보내기(states 초당 2번 + 다른 연결이 넣은 이벤트)를 같은 루프에서 따로 돈다.
보내기는 states 때가 되거나 깨울 때(이벤트·내 답)만 돈다(폴링 없음).
"""
import asyncio
import json
import logging
import time

from fastapi import APIRouter, Depends, WebSocket, WebSocketDisconnect
from sqlalchemy.orm import sessionmaker
from starlette.concurrency import run_in_threadpool

from ..core import tokens
from ..core.errors import ApiError
from ..db.models import Course, CourseEntry, User
from ..db.session import get_sessionmaker
from .hub import Conn, hub, server_time_ms

log = logging.getLogger("scrun.realtime")
router = APIRouter()

STATES_INTERVAL_S = 0.5   # states 보내는 간격(초당 2번, ADR-0010)
IDLE_TIMEOUT_S = 10.0     # 폰이 이만큼 아무것도 안 보내면 끊긴 것으로 봄(앱은 초당 2번 state)
MAX_MESSAGE_CHARS = 1024  # 폰 → 서버 메시지는 짧다(state·ping·leave). 넘으면 닫음

# 닫는 코드(앱은 4000번대면 다시 붙지 않는다, docs/api/realtime.md)
CLOSE_LEFT = 1000          # 앱이 leave를 보내고 나감
CLOSE_REPLACED = 4001      # 같은 사람이 다른 연결로 붙음
CLOSE_BAD_MESSAGE = 4400   # 모양이 틀린 메시지(바이너리 포함)
CLOSE_UNAUTHORIZED = 4401  # 토큰 없음·틀림·만료 → 다시 입장
CLOSE_NOT_IN_MAP = 4403    # 이 맵에 자리가 없음(다른 코스로 옮김) → 다시 입장

_BAD, _LEAVE = object(), object()


def map_id_of(course_id: str) -> str:
    return f"map_{course_id}"


def _seat(make_session: sessionmaker, user_id: str, map_id: str):
    """(닉네임, 코스 ID, 코스 길이) 또는 None(사용자 없음·이 맵에 자리 없음)."""
    with make_session() as db:
        user = db.get(User, user_id)
        entry = db.get(CourseEntry, user_id)
        if user is None or entry is None or map_id_of(entry.course_id) != map_id:
            return None
        course = db.get(Course, entry.course_id)
        if course is None or not course.active:
            return None
        return user.nickname, course.id, course.length_m


def _bearer(ws: WebSocket) -> str:
    scheme, _, value = ws.headers.get("authorization", "").partition(" ")
    return value.strip() if scheme.lower() == "bearer" else ""


@router.websocket("/v1/realtime")
async def realtime(ws: WebSocket, make_session: sessionmaker = Depends(get_sessionmaker)):
    await ws.accept()
    try:
        claims = tokens.decode_realtime(_bearer(ws))
    except ApiError:
        await ws.close(CLOSE_UNAUTHORIZED)
        return
    user_id, map_id = claims["sub"], claims["map"]
    seat = await run_in_threadpool(_seat, make_session, user_id, map_id)
    if seat is None:
        await ws.close(CLOSE_NOT_IN_MAP)
        return
    nickname, course_id, length_m = seat

    loop = asyncio.get_running_loop()
    wake = asyncio.Event()
    conn, welcome = hub.join(map_id, course_id, length_m, user_id, nickname,
                             wake=lambda: loop.call_soon_threadsafe(wake.set))
    left = False
    try:
        await ws.send_json(welcome)
        code = await _serve(ws, conn, wake)
        left = code == CLOSE_LEFT
        if code is not None:
            await ws.close(code)
    except (WebSocketDisconnect, RuntimeError):
        pass  # 폰이 먼저 끊음(RuntimeError: 닫힌 소켓에 보내기)
    finally:
        hub.leave(conn, intentional=left)


async def _serve(ws: WebSocket, conn: Conn, wake: asyncio.Event):
    """연결이 끝날 때까지. 닫는 코드(서버가 닫을 때) 또는 None(폰이 끊음)."""
    last_heard = time.monotonic()
    close_code = None

    async def receive():
        nonlocal last_heard, close_code
        while True:
            msg = await ws.receive()
            if msg["type"] == "websocket.disconnect":
                raise WebSocketDisconnect(msg.get("code", 1000))
            last_heard = time.monotonic()
            reply = _handle(conn, msg.get("text"))
            if reply is _BAD or reply is _LEAVE:
                close_code = CLOSE_BAD_MESSAGE if reply is _BAD else CLOSE_LEFT
                return
            if reply is not None:
                conn.outbox.append(reply)
                wake.set()  # pong은 바로(왕복 지연 실측)

    receiver = asyncio.create_task(receive())
    receiver.add_done_callback(lambda _: wake.set())
    next_states = 0.0
    try:
        while not receiver.done():
            if conn.replaced:  # 다른 연결로 바뀜(4001) 또는 다른 맵으로 옮겨 방에서 빠짐(4403)
                return CLOSE_NOT_IN_MAP if hub.states(conn) is None else CLOSE_REPLACED
            if time.monotonic() - last_heard > IDLE_TIMEOUT_S:
                return 1001  # going away: 앱이 다시 붙음
            for msg in hub.drain(conn):
                await ws.send_json(msg)
            now = time.monotonic()
            if now >= next_states:
                next_states = now + STATES_INTERVAL_S
                states = hub.states(conn)
                if states is None:  # 방에서 빠짐(30초 넘게 끊겼다 이 연결만 남은 경우 등)
                    return CLOSE_NOT_IN_MAP
                await ws.send_json(states)
            try:
                await asyncio.wait_for(wake.wait(), max(0.0, next_states - time.monotonic()))
            except asyncio.TimeoutError:
                pass
            wake.clear()
        exc = receiver.exception()
        if exc is not None and not isinstance(exc, WebSocketDisconnect):
            raise exc
        return close_code
    finally:
        receiver.cancel()


def _handle(conn: Conn, text):
    """폰 메시지 하나. 답할 것(dict), 없으면 None, 틀렸으면 _BAD, 나가기면 _LEAVE."""
    if not isinstance(text, str) or len(text) > MAX_MESSAGE_CHARS:
        return _BAD  # 바이너리·너무 긴 메시지
    try:
        msg = json.loads(text)
    except ValueError:
        return _BAD
    if not isinstance(msg, dict):
        return _BAD
    kind = msg.get("type")
    if kind == "state":
        ok = hub.update(conn, msg.get("d"), msg.get("v"), msg.get("running") is True)
        return None if ok or conn.replaced else _BAD
    if kind == "ping":
        return {"type": "pong", "t": msg.get("t"), "serverTime": server_time_ms()}
    if kind == "leave":
        return _LEAVE
    return None  # 모르는 종류는 무시(앱이 먼저 새 메시지를 보내도 끊기지 않게)
