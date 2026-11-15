import hashlib

import asyncpg
import pytest

from medvya_ai import crypto
from tests.conftest import APP_URL, _dsn
from tests.integration.helpers import API, grant_all, new_session, put_chunk, segment

pytestmark = pytest.mark.integration


async def _ready(c, make_clinic):  # type: ignore[no-untyped-def]
    cid, _uid, h = await make_clinic()
    s = await new_session(c, h)
    await grant_all(c, h, s)
    assert (await segment(c, h, s["id"])).status_code == 200
    return cid, h, s["id"]


async def test_store_duplicate_conflict_and_encryption(app_ctx, make_clinic, blobs) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    cid, h, sid = await _ready(c, make_clinic)
    data = b"RIFF-opus-audio-plaintext"
    assert (await put_chunk(c, h, sid, 0, data)).json() == {"status": "stored"}
    assert (await put_chunk(c, h, sid, 0, data)).json() == {"status": "duplicate"}
    assert len(blobs.blobs) == 1  # no second blob
    r = await put_chunk(c, h, sid, 0, b"different")
    assert r.status_code == 409 and r.json()["error"]["code"] == "CHUNK_CONFLICT"
    assert len(blobs.blobs) == 1

    ((path, stored),) = blobs.blobs.items()
    assert stored != data and data not in stored
    conn = await asyncpg.connect(_dsn(APP_URL))
    try:
        async with conn.transaction():
            await conn.execute("SELECT set_config('app.clinic_id', $1, true)", str(cid))
            row = await conn.fetchrow("SELECT * FROM audio_chunk")
            srow = await conn.fetchrow("SELECT dek_wrapped FROM consultation_session")
    finally:
        await conn.close()
    assert row["blob_path"] == path and row["sha256"] == hashlib.sha256(data).hexdigest()
    import uuid as _uuid

    dek = app.state.key_wrapper.unwrap(srow["dek_wrapped"], _uuid.UUID(sid))
    assert (
        crypto.decrypt_chunk(dek, row["nonce"], stored, crypto.chunk_aad(_uuid.UUID(sid), 0, 0))
        == data
    )


async def test_hash_mismatch_and_size_limit(app_ctx, make_clinic, blobs) -> None:  # type: ignore[no-untyped-def]
    _, c = app_ctx
    _, h, sid = await _ready(c, make_clinic)
    r = await put_chunk(c, h, sid, 0, b"abc", sha="0" * 64)
    assert r.status_code == 400 and r.json()["error"]["code"] == "CHUNK_HASH_MISMATCH"
    r = await put_chunk(c, h, sid, 0, b"x" * 2000)  # settings.max_chunk_bytes = 1024
    assert r.status_code == 413
    assert blobs.blobs == {}


async def test_complete_reports_missing_then_finishes(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    _, c = app_ctx
    _, h, sid = await _ready(c, make_clinic)
    for seq in (0, 2):
        await put_chunk(c, h, sid, seq, f"chunk{seq}".encode())
    body = {
        "chunk_count": 3,
        "duration_ms": 30000,
        "client_events": [{"t_ms": 1000, "type": "track_ended"}],
    }
    done = f"{API}/sessions/{sid}/segments/0/complete"
    r = await c.post(done, headers=h, json=body)
    assert r.status_code == 409
    assert r.json()["error"]["code"] == "MISSING_CHUNKS" and r.json()["error"]["missing"] == [1]
    assert (
        await c.post(f"{API}/sessions/{sid}/finish", headers=h)
    ).status_code == 409  # segment open
    await put_chunk(c, h, sid, 1, b"chunk1")
    assert (await c.post(done, headers=h, json=body)).json()["state"] == "complete"
    r = await c.post(f"{API}/sessions/{sid}/finish", headers=h)
    assert r.status_code == 202 and r.json() == {"state": "processing"}
    assert (
        await c.post(f"{API}/sessions/{sid}/finish", headers=h)
    ).status_code == 202  # idempotent
    s = (await c.get(f"{API}/sessions/{sid}", headers=h)).json()
    assert s["state"] == "processing" and s["jobs"] == [{"kind": "transcribe", "status": "queued"}]
    assert s["segments"] == [{"segment_no": 0, "state": "complete", "chunk_count": 3}]


async def test_complete_allowed_after_withdrawal_and_audited(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    from tests.integration.helpers import consent

    _, c = app_ctx
    cid, h, sid = await _ready(c, make_clinic)
    await put_chunk(c, h, sid, 0, b"a")
    pid = (await c.get(f"{API}/sessions/{sid}", headers=h)).json()["participants"][0]["id"]
    await consent(c, h, sid, pid, "withdrawn")
    r = await c.post(
        f"{API}/sessions/{sid}/segments/0/complete",
        headers=h,
        json={"chunk_count": 1, "duration_ms": 10},
    )
    assert r.status_code == 200
    conn = await asyncpg.connect(_dsn(APP_URL))
    try:
        async with conn.transaction():
            await conn.execute("SELECT set_config('app.clinic_id', $1, true)", str(cid))
            n = await conn.fetchval(
                "SELECT count(*) FROM audit_event WHERE action = 'chunks_uploaded'"
            )
    finally:
        await conn.close()
    assert n == 1
