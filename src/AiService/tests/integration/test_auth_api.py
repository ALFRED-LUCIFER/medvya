"""AUTH_MODE=entra: identity from the token, clinic and role from app_user, never from headers."""

import secrets
from collections.abc import AsyncIterator
from typing import Any

import httpx
import pytest

from medvya_ai.auth import Claims
from medvya_ai.db import set_tenant
from medvya_ai.main import create_app
from medvya_ai.seed import provision_user
from tests.integration.helpers import API, finished_session
from tests.integration.pipeline import deps_for, drain

pytestmark = pytest.mark.integration


class FakeVerifier:
    """Stands in for JWKS+signature checks (covered by tests/unit/test_auth.py): the token text is
    the subject, 'bad' is an invalid token."""

    async def verify(self, token: str) -> Claims:
        if token == "bad":
            from medvya_ai.errors import AppError

            raise AppError(401, "UNAUTHENTICATED", "bad", headers={"WWW-Authenticate": "Bearer"})
        return Claims(subject=token)


@pytest.fixture
async def entra(
    schema: None, settings: Any, blobs: Any
) -> AsyncIterator[tuple[Any, httpx.AsyncClient]]:  # type: ignore[no-untyped-def]
    app = create_app(
        settings.model_copy(update={"auth_mode": "entra"}), blobs, verifier=FakeVerifier()
    )
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://t") as c:
        yield app, c
    await app.state.engine.dispose()


async def _user(app: Any, make_clinic: Any, role: str = "doctor") -> tuple[Any, dict[str, str]]:
    cid, _, _ = await make_clinic()
    subject = secrets.token_hex(8)
    async with app.state.sessionmaker() as db, db.begin():
        await set_tenant(db, cid)
        await provision_user(
            db,
            clinic_id=cid,
            email=f"{subject}@example.test",
            display_name="Dr",
            role=role,
            subject=subject,
        )
    return cid, {"Authorization": f"Bearer {subject}"}


async def test_missing_or_invalid_token_is_401(entra) -> None:  # type: ignore[no-untyped-def]
    _, c = entra
    r = await c.get(f"{API}/sessions")
    assert r.status_code == 401 and r.headers["www-authenticate"] == "Bearer"
    assert r.json()["error"]["code"] == "UNAUTHENTICATED"
    assert (
        await c.get(f"{API}/sessions", headers={"Authorization": "Bearer bad"})
    ).status_code == 401
    assert (
        await c.get(f"{API}/sessions", headers={"Authorization": "Basic abc"})
    ).status_code == 401


async def test_unprovisioned_subject_is_403(entra) -> None:  # type: ignore[no-untyped-def]
    _, c = entra
    r = await c.get(f"{API}/sessions", headers={"Authorization": "Bearer nobody"})
    assert r.status_code == 403 and r.json()["error"]["code"] == "TENANT_ACCESS_DENIED"


async def test_tenant_comes_from_user_not_header(entra, app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    app, c = entra
    cid_a, ha = await _user(app, make_clinic)
    _, hb = await _user(app, make_clinic)
    sa = (await c.post(f"{API}/sessions", headers=ha, json=_body())).json()
    assert (await c.get(f"{API}/sessions/{sa['id']}", headers=hb)).status_code == 404
    # X-Clinic-Id cannot pick another tenant
    r = await c.get(f"{API}/sessions", headers={**hb, "X-Clinic-Id": str(cid_a)})
    assert r.status_code == 200 and r.json() == []
    assert [s["id"] for s in (await c.get(f"{API}/sessions", headers=ha)).json()] == [sa["id"]]
    # the audit actor is the authenticated user, not a demo doctor
    async with app.state.sessionmaker() as db, db.begin():
        await set_tenant(db, cid_a)
        from sqlalchemy import text

        actor = await db.scalar(
            text(
                "SELECT u.external_subject FROM audit_event a "
                "JOIN app_user u ON u.id = a.actor_user_id WHERE a.action = 'session_create'"
            )
        )
    assert actor == ha["Authorization"].removeprefix("Bearer ")


async def test_admin_role_cannot_approve(entra, app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    app, c = entra
    _, hd = await _user(app, make_clinic, "doctor")
    sid = await finished_session(c, hd)
    await drain(app, deps_for(app))
    # an admin of the same clinic
    cid = (await c.get(f"{API}/sessions/{sid}", headers=hd)).status_code == 200
    assert cid
    async with app.state.sessionmaker() as db, db.begin():
        from sqlalchemy import text

        clinic = await db.scalar(
            text("SELECT clinic_id FROM processing_job WHERE session_id = :s"), {"s": sid}
        )
        await set_tenant(db, clinic)
        subject = secrets.token_hex(8)
        await provision_user(
            db,
            clinic_id=clinic,
            email=f"{subject}@x.test",
            display_name="Adm",
            role="admin",
            subject=subject,
        )
    ha = {"Authorization": f"Bearer {subject}"}
    assert (await c.get(f"{API}/sessions/{sid}/result", headers=ha)).status_code == 200
    r = await c.post(f"{API}/sessions/{sid}/approve", headers=ha)
    assert r.status_code == 403 and r.json()["error"]["code"] == "FORBIDDEN"
    assert (await c.post(f"{API}/sessions/{sid}/approve", headers=hd)).status_code == 200


def _body() -> dict[str, Any]:
    return {
        "patient_ref": "P-1",
        "scheduled_date": "2026-11-09",
        "participants": [
            {"role": "doctor", "display_name": "D"},
            {"role": "patient", "display_name": "P"},
        ],
    }
