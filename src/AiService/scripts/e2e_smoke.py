"""End-to-end smoke over the real pipeline with fake providers and in-memory blobs.

Run from src/AiService after `alembic upgrade head` and the seed:
    uv run python scripts/e2e_smoke.py
Exits 0 with a SKIP line when PostgreSQL or the required settings are unavailable.
Flow: session -> consent -> record 2 chunks -> finish -> worker -> result -> approve ->
export -> analytics -> retention sweep. Prints counts and ids only, never clinical text.
"""

import asyncio
import hashlib
import sys
from datetime import UTC, datetime, timedelta

import asyncpg
import httpx

from medvya_ai.config import Settings, get_settings
from medvya_ai.jobs.context import Deps
from medvya_ai.jobs.worker import run_once
from medvya_ai.main import create_app
from medvya_ai.providers.llm import build_llm
from medvya_ai.providers.transcription import build_transcriber
from medvya_ai.seed import ensure_demo_user
from medvya_ai.services.retention import sweep
from medvya_ai.storage import MemoryBlobStore

API = "/api/v1"


def step(msg: str) -> None:
    print(f"[smoke] {msg}")


async def main() -> int:
    try:
        settings = get_settings().model_copy(
            update={
                "transcription_provider": "fake",
                "llm_provider": "fake",
                "auth_mode": "disabled",
            }
        )
        dsn = settings.database_url.get_secret_value().replace("+asyncpg", "", 1)
        await (await asyncpg.connect(dsn, timeout=3)).close()
    except Exception as exc:  # no settings, no Postgres, bad credentials: nothing to smoke
        print(f"SKIP e2e_smoke: environment not ready ({type(exc).__name__}); start docker compose")
        return 0
    return await run(settings)


async def run(settings: Settings) -> int:
    blobs = MemoryBlobStore()
    app = create_app(settings, blobs)
    deps = Deps(
        settings, blobs, app.state.key_wrapper, build_transcriber(settings), build_llm(settings)
    )
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://smoke"
    ) as c:
        h: dict[str, str] = {}  # auth disabled above: the API acts as the demo doctor

        s = (
            await c.post(
                f"{API}/sessions",
                headers=h,
                json={
                    "patient_ref": "SMOKE-1",
                    "scheduled_date": datetime.now(UTC).date().isoformat(),
                    "participants": [
                        {"role": "doctor", "display_name": "Dr"},
                        {"role": "patient", "display_name": "Pt"},
                    ],
                },
            )
        ).json()
        sid = s["id"]
        for p in s["participants"]:
            await c.post(
                f"{API}/sessions/{sid}/consents",
                headers=h,
                json={
                    "participant_id": p["id"],
                    "status": "granted",
                    "method": "verbal_attested",
                    "on_behalf_of": "self",
                    "notice_version": "en-v1",
                },
            )
        await c.post(
            f"{API}/sessions/{sid}/segments",
            headers=h,
            json={"segment_no": 0, "mime_type": "audio/webm;codecs=opus", "start_offset_ms": 0},
        )
        for seq in range(2):
            data = f"smoke-audio-{seq}".encode()
            up = await c.put(
                f"{API}/sessions/{sid}/segments/0/chunks/{seq}",
                content=data,
                headers={
                    **h,
                    "Content-Type": "audio/webm",
                    "X-Chunk-Sha256": hashlib.sha256(data).hexdigest(),
                },
            )
            if up.status_code != 200:
                print(f"FAIL chunk {seq} -> {up.status_code}")
                return 1
        done = await c.post(
            f"{API}/sessions/{sid}/segments/0/complete",
            headers=h,
            json={"chunk_count": 2, "duration_ms": 20_000, "client_events": []},
        )
        fin = await c.post(f"{API}/sessions/{sid}/finish", headers=h)
        if (done.status_code, fin.status_code) != (200, 202):
            print(f"FAIL complete/finish -> {done.status_code}/{fin.status_code}")
            return 1
        step("recorded and finished")

        jobs = 0
        while jobs < 10 and await run_once(app.state.sessionmaker, deps, "smoke-worker"):
            jobs += 1
        step(f"worker ran {jobs} jobs")

        res = await c.get(f"{API}/sessions/{sid}/result", headers=h)
        if res.status_code != 200:
            print(f"FAIL result -> {res.status_code} (state may be failed; check worker logs)")
            return 1
        body = res.json()
        step(
            f"result: state={body['state']} facts={len(body['facts'])} "
            f"transcript={len(body['transcript'])}"
        )
        ap = await c.post(f"{API}/sessions/{sid}/approve", headers=h)
        ex = await c.get(f"{API}/sessions/{sid}/export", headers=h)
        ov = await c.get(f"{API}/analytics/overview", headers=h)
        codes = (ap.status_code, ex.status_code, ov.status_code)
        if codes != (200, 200, 200):
            print(f"FAIL approve/export/analytics -> {codes}")
            return 1
        step(f"approved, exported, overview={ov.json()['approved']} approved sessions")

    async with app.state.sessionmaker() as db, db.begin():
        clinic_id, _ = await ensure_demo_user(db)
        n = await sweep(
            db, blobs, clinic_id=clinic_id, now=datetime.now(UTC) + timedelta(hours=25),
            settings=settings,
        )  # fmt: skip
    step(f"retention sweep deleted {n} chunks; blobs left={len(blobs.blobs)}")
    await app.state.engine.dispose()
    print("OK e2e_smoke")
    return 0 if blobs.blobs == {} else 1


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
