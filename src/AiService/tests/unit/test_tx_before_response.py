"""F1: the transaction must commit (or fail) before the response reaches the client. DB-free."""

import uuid
from types import SimpleNamespace
from typing import Any

import httpx

from medvya_ai.config import Settings
from medvya_ai.main import create_app
from medvya_ai.storage import MemoryBlobStore


class _Begin:
    def __init__(self, events: list[str], fail: bool) -> None:
        self.events, self.fail = events, fail

    async def __aenter__(self) -> None:
        return None

    async def __aexit__(self, et: object, ev: object, tb: object) -> None:
        if et is not None:
            self.events.append("rollback")
        elif self.fail:
            self.events.append("commit_failed")
            raise RuntimeError("commit failed")
        else:
            self.events.append("commit")


class _Session:
    def __init__(self, events: list[str], fail: bool, user: Any) -> None:
        self.events, self.fail, self.user = events, fail, user

    async def __aenter__(self) -> "_Session":
        return self

    async def __aexit__(self, *a: object) -> None:
        return None

    def begin(self) -> _Begin:
        return _Begin(self.events, self.fail)

    async def execute(self, *a: object, **k: object) -> None:
        return None

    async def scalar(self, *a: object) -> Any:
        return self.user.id


async def _call(settings: Settings, *, fail: bool) -> tuple[list[str], httpx.Response | None]:
    app = create_app(settings, MemoryBlobStore())
    uid, cid = uuid.uuid4(), uuid.uuid4()
    user = SimpleNamespace(id=uid, clinic_id=cid, is_active=True, role="doctor")
    events: list[str] = []
    app.state.sessionmaker = lambda: _Session(events, fail, user)

    async def wrapped(scope: Any, receive: Any, send: Any) -> None:
        async def spy(message: Any) -> None:
            if message["type"] == "http.response.start":
                events.append(f"response_{message['status']}")
            await send(message)

        await app(scope, receive, spy)

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=wrapped, raise_app_exceptions=False), base_url="http://t"
    ) as c:
        r = await c.get("/api/v1/consent-notice", headers={"X-Clinic-Id": str(cid)})
    return events, r


async def test_commit_happens_before_response_is_sent(settings: Settings) -> None:
    events, r = await _call(settings, fail=False)
    assert r is not None and r.status_code == 200
    assert events == ["commit", "response_200"]


async def test_commit_failure_is_not_a_2xx(settings: Settings) -> None:
    events, r = await _call(settings, fail=True)
    assert "response_200" not in events
    assert r is not None and r.status_code == 500
