import asyncpg
import pytest

from tests.conftest import APP_URL, OWNER_URL, _dsn
from tests.integration.helpers import API, new_session

pytestmark = pytest.mark.integration

# Every table with a clinic_id column, read from the catalog (a hand-kept list misses new tables).
TENANT_TABLES_SQL = """
    SELECT c.relname FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'public'
    JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'clinic_id' AND NOT a.attisdropped
    WHERE c.relkind = 'r' ORDER BY c.relname"""
# The worker claims jobs across tenants; the job row holds ids and codes only (ADR-021 gate 3).
NO_RLS = {"processing_job"}


async def _tenant_tables() -> list[str]:
    conn = await asyncpg.connect(_dsn(OWNER_URL))
    try:
        return [r[0] for r in await conn.fetch(TENANT_TABLES_SQL)]
    finally:
        await conn.close()


async def test_every_tenant_table_forces_rls_with_a_policy(schema: None) -> None:
    """Catalog check: a new table with clinic_id but no ENABLE + FORCE + policy fails here.
    The clinic table has no clinic_id and no RLS on purpose: it holds a uuid and a placeholder
    name only, and the scheduler lists tenants from it."""
    # SAFETY: mutation check: ALTER TABLE clinical_fact NO FORCE ROW LEVEL SECURITY ->
    # test_every_tenant_table_forces_rls_with_a_policy failed
    conn = await asyncpg.connect(_dsn(OWNER_URL))
    try:
        rows = await conn.fetch(
            f"""SELECT t.relname, t.relrowsecurity, t.relforcerowsecurity,
                       EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = t.oid) AS has_policy
                FROM pg_class t WHERE t.relname IN ({TENANT_TABLES_SQL})"""  # noqa: S608
        )
    finally:
        await conn.close()
    assert len(rows) >= 15  # the query found the schema
    bad = [
        r["relname"]
        for r in rows
        if r["relname"] not in NO_RLS
        and not (r["relrowsecurity"] and r["relforcerowsecurity"] and r["has_policy"])
    ]
    assert bad == []


async def _app_conn(clinic_id: object | None) -> tuple[asyncpg.Connection, object]:
    conn = await asyncpg.connect(_dsn(APP_URL))
    tx = conn.transaction()
    await tx.start()
    if clinic_id is not None:
        await conn.execute("SELECT set_config('app.clinic_id', $1, true)", str(clinic_id))
    return conn, tx


async def test_api_isolation_and_audit(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    _, c = app_ctx
    _, _, ha = await make_clinic("A")
    _, _, hb = await make_clinic("B")
    sa = await new_session(c, ha)
    sb = await new_session(c, hb)
    assert [s["id"] for s in (await c.get(f"{API}/sessions", headers=ha)).json()] == [sa["id"]]
    assert (await c.get(f"{API}/sessions/{sb['id']}", headers=ha)).status_code == 404
    assert (await c.get(f"{API}/sessions/{sa['id']}", headers=ha)).status_code == 200


async def test_raw_sql_sees_only_own_rows(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    _, c = app_ctx
    a, _, ha = await make_clinic("A")
    b, _, hb = await make_clinic("B")
    await new_session(c, ha)
    await new_session(c, hb)
    for mine, other in ((a, b), (b, a)):
        conn, tx = await _app_conn(mine)
        try:
            for t in set(await _tenant_tables()) - NO_RLS:
                n = await conn.fetchval(f"SELECT count(*) FROM {t} WHERE clinic_id = $1", other)  # noqa: S608
                assert n == 0, t
            assert await conn.fetchval("SELECT count(*) FROM consultation_session") == 1
            assert (
                await conn.fetchval(
                    "SELECT count(*) FROM audit_event WHERE action = 'session_create'"
                )
                == 1
            )
            with pytest.raises(
                asyncpg.PostgresError
            ):  # WITH CHECK blocks writing as the other clinic
                await conn.execute("INSERT INTO retention_policy (clinic_id) VALUES ($1)", other)
        finally:
            await tx.rollback()  # type: ignore[attr-defined]
            await conn.close()


async def test_unscoped_session_sees_nothing_and_does_not_error(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    _, c = app_ctx
    _, _, h = await make_clinic()
    await new_session(c, h)
    conn, tx = await _app_conn(None)
    try:
        for t in set(await _tenant_tables()) - NO_RLS:
            assert await conn.fetchval(f"SELECT count(*) FROM {t}") == 0, t  # noqa: S608
    finally:
        await tx.rollback()  # type: ignore[attr-defined]
        await conn.close()
    # same connection after a scoped transaction ended: setting reverts to '' and still no error
    conn = await asyncpg.connect(_dsn(APP_URL))
    try:
        async with conn.transaction():
            await conn.execute(
                "SELECT set_config('app.clinic_id', $1, true)",
                "00000000-0000-0000-0000-000000000001",
            )
        assert await conn.fetchval("SELECT count(*) FROM consultation_session") == 0
    finally:
        await conn.close()


async def test_audit_is_append_only(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    _, c = app_ctx
    cid, _, h = await make_clinic()
    await new_session(c, h)
    # app role: REVOKE in effect
    conn, tx = await _app_conn(cid)
    try:
        for stmt in (
            "UPDATE audit_event SET action = 'x'",
            "DELETE FROM audit_event",
            "TRUNCATE audit_event",
        ):
            with pytest.raises(asyncpg.InsufficientPrivilegeError):
                async with conn.transaction():
                    await conn.execute(stmt)
    finally:
        await tx.rollback()  # type: ignore[attr-defined]
        await conn.close()
    # owner: trigger in effect
    conn = await asyncpg.connect(_dsn(OWNER_URL))
    try:
        async with conn.transaction():
            await conn.execute("SELECT set_config('app.clinic_id', $1, true)", str(cid))
            for stmt in (
                "UPDATE audit_event SET action = 'x'",
                "DELETE FROM audit_event",
                "TRUNCATE audit_event",
            ):
                with pytest.raises(asyncpg.RestrictViolationError):
                    async with conn.transaction():
                        await conn.execute(stmt)
    finally:
        await conn.close()


async def test_app_role_is_not_privileged() -> None:
    conn = await asyncpg.connect(_dsn(APP_URL))
    try:
        row = await conn.fetchrow(
            "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user"
        )
        assert not row["rolsuper"] and not row["rolbypassrls"]
        owners = await conn.fetchval(
            "SELECT count(*) FROM pg_tables WHERE schemaname='public' AND tableowner = current_user"
        )
        assert owners == 0
    finally:
        await conn.close()
