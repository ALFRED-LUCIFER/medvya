import hashlib
from typing import Any

import httpx

API = "/api/v1"


async def new_session(c: httpx.AsyncClient, h: dict[str, str], n: int = 2) -> dict[str, Any]:
    roles = ["doctor", "patient", "companion"][:n]
    r = await c.post(
        f"{API}/sessions",
        headers=h,
        json={
            "patient_ref": "P-1",
            "scheduled_date": "2026-11-08",
            "participants": [{"role": x, "display_name": f"{x} {i}"} for i, x in enumerate(roles)],
        },
    )
    assert r.status_code == 201, r.text
    return r.json()


async def consent(
    c: httpx.AsyncClient, h: dict[str, str], sid: str, pid: str, status: str = "granted"
) -> httpx.Response:
    return await c.post(
        f"{API}/sessions/{sid}/consents",
        headers=h,
        json={
            "participant_id": pid,
            "status": status,
            "method": "verbal_attested",
            "on_behalf_of": "self",
            "notice_version": "en-v1",
        },
    )


async def grant_all(c: httpx.AsyncClient, h: dict[str, str], s: dict[str, Any]) -> None:
    for p in s["participants"]:
        assert (await consent(c, h, s["id"], p["id"])).status_code == 201


async def segment(c: httpx.AsyncClient, h: dict[str, str], sid: str, no: int = 0) -> httpx.Response:
    return await c.post(
        f"{API}/sessions/{sid}/segments",
        headers=h,
        json={"segment_no": no, "mime_type": "audio/webm;codecs=opus", "start_offset_ms": 0},
    )


async def put_chunk(
    c: httpx.AsyncClient,
    h: dict[str, str],
    sid: str,
    seq: int,
    data: bytes,
    no: int = 0,
    sha: str | None = None,
) -> httpx.Response:
    return await c.put(
        f"{API}/sessions/{sid}/segments/{no}/chunks/{seq}",
        content=data,
        headers={
            **h,
            "Content-Type": "audio/webm",
            "X-Chunk-Sha256": sha or hashlib.sha256(data).hexdigest(),
        },
    )


async def finished_session(c: httpx.AsyncClient, h: dict[str, str], chunks: int = 2) -> str:
    """Create, consent, upload `chunks` chunks in segment 0, complete and finish. Returns id."""
    s = await new_session(c, h)
    await grant_all(c, h, s)
    assert (await segment(c, h, s["id"])).status_code == 200
    for seq in range(chunks):
        assert (await put_chunk(c, h, s["id"], seq, f"audio-{seq}".encode())).status_code == 200
    r = await c.post(
        f"{API}/sessions/{s['id']}/segments/0/complete",
        headers=h,
        json={"chunk_count": chunks, "duration_ms": 60_000, "client_events": []},
    )
    assert r.status_code == 200, r.text
    r = await c.post(f"{API}/sessions/{s['id']}/finish", headers=h)
    assert r.status_code == 202, r.text
    return str(s["id"])
