"""A database error never carries row values into logs or the response (AGENTS.md 1.2)."""

import logging
import uuid

import httpx
import pytest
from sqlalchemy import text
from sqlalchemy.exc import IntegrityError

pytestmark = pytest.mark.integration
SECRET = "Asha Patil synthetic-0001"  # synthetic stand-in for a patient value


async def test_integrity_error_logs_no_parameter_values(
    app_ctx, caplog: pytest.LogCaptureFixture
) -> None:  # type: ignore[no-untyped-def]
    # SAFETY: mutation check: removed hide_parameters=True from db.make_engine ->
    # this test failed (the bound name appeared in the exception text); logging str(exc) in the
    # catch-all handler instead of the class name -> this test failed
    app, _ = app_ctx
    clinic = uuid.uuid4()
    insert = text("INSERT INTO clinic (id, name) VALUES (:id, :name)")

    @app.get("/boom")
    async def boom() -> None:
        async with app.state.sessionmaker() as db, db.begin():
            await db.execute(insert, {"id": clinic, "name": SECRET})
            await db.execute(insert, {"id": clinic, "name": SECRET})  # duplicate key

    with pytest.raises(IntegrityError) as exc:
        async with app.state.sessionmaker() as db, db.begin():
            await db.execute(insert, {"id": clinic, "name": SECRET})
            await db.execute(insert, {"id": clinic, "name": SECRET})
    assert SECRET not in str(exc.value)

    caplog.set_level(logging.INFO)
    transport = httpx.ASGITransport(app=app, raise_app_exceptions=False)
    async with httpx.AsyncClient(transport=transport, base_url="http://t") as c:
        r = await c.get("/boom")
    assert r.status_code == 500 and r.json()["error"]["code"] == "INTERNAL_ERROR"
    assert SECRET not in r.text
    assert "IntegrityError" in caplog.text and SECRET not in caplog.text
    assert "Asha" not in caplog.text and str(clinic) not in caplog.text
