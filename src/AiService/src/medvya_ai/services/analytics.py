"""Analytics over approved data. Defaults read the views (approved sessions only, rejected facts
excluded); include_draft reads the tables directly because the views exclude drafts by design.
Every query filters clinic_id explicitly as well as through RLS, so the clinic_id indexes are used.
"""

import uuid
from datetime import date, timedelta
from typing import Any, Literal

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

Group = Literal["day", "week", "month"]
_TRUNC = {"day": "day", "week": "week", "month": "month"}  # whitelist: interpolated into SQL

# Used when include_draft=true: approved plus sessions awaiting review, bucketed by approval day
# (creation day for drafts).
_MED_DRAFT_SOURCE = """(
    SELECT m.clinic_id,
           (COALESCE(s.approved_at, s.created_at) AT TIME ZONE 'UTC')::date AS day,
           m.generic_name,
           count(*) AS prescriptions,
           count(DISTINCT s.id) AS sessions
    FROM medication m
    JOIN clinical_fact f ON f.id = m.fact_id AND f.clinic_id = m.clinic_id
    JOIN consultation_session s ON s.id = f.session_id AND s.clinic_id = f.clinic_id
    WHERE m.clinic_id = :cid
      AND s.state IN ('review', 'approved')
      AND f.review_state <> 'rejected'
      AND m.generic_name IS NOT NULL
    GROUP BY m.clinic_id, 2, m.generic_name
) src"""


def bucket_start(day: date, group: Group) -> date:
    """Start of the period containing `day`: itself, the ISO Monday, or the 1st of the month.
    Mirrors date_trunc in the SQL below."""
    if group == "day":
        return day
    if group == "week":
        return day - timedelta(days=day.weekday())
    return day.replace(day=1)


def _range(col: str, d_from: date | None, d_to: date | None, params: dict[str, Any]) -> str:
    sql = ""
    if d_from is not None:
        sql += f" AND {col} >= :d_from"
        params["d_from"] = d_from
    if d_to is not None:
        sql += f" AND {col} <= :d_to"
        params["d_to"] = d_to
    return sql


def medication_sql(
    group: Group, d_from: date | None, d_to: date | None, include_draft: bool
) -> tuple[str, dict[str, Any]]:
    params: dict[str, Any] = {}
    source = _MED_DRAFT_SOURCE if include_draft else "v_medication_usage"
    where = f"clinic_id = :cid{_range('day', d_from, d_to, params)}"
    trunc = _TRUNC[group]
    # Sessions are summable across days: a session has exactly one approval day.
    sql = f"""
        WITH top AS (
            SELECT generic_name FROM {source} WHERE {where}
            GROUP BY generic_name ORDER BY sum(prescriptions) DESC, generic_name LIMIT :limit)
        SELECT date_trunc('{trunc}', day::timestamp)::date AS period, generic_name,
               sum(prescriptions)::int AS prescriptions, sum(sessions)::int AS sessions
        FROM {source}
        WHERE {where} AND generic_name IN (SELECT generic_name FROM top)
        GROUP BY 1, generic_name ORDER BY 1, 3 DESC, generic_name"""  # noqa: S608
    return sql, params


async def medications(
    db: AsyncSession,
    clinic_id: uuid.UUID,
    *,
    group: Group,
    d_from: date | None,
    d_to: date | None,
    limit: int,
    include_draft: bool,
) -> list[dict[str, Any]]:
    sql, params = medication_sql(group, d_from, d_to, include_draft)
    rows = await db.execute(text(sql), {**params, "cid": clinic_id, "limit": limit})
    return [dict(r._mapping) for r in rows]


async def facts(
    db: AsyncSession,
    clinic_id: uuid.UUID,
    *,
    category: str | None,
    d_from: date | None,
    d_to: date | None,
    limit: int,
) -> list[dict[str, Any]]:
    params: dict[str, Any] = {"cid": clinic_id, "limit": limit}
    where = f"clinic_id = :cid{_range('day', d_from, d_to, params)}"
    if category is not None:
        where += " AND category = :category"
        params["category"] = category
    sql = f"""
        SELECT category::text AS category, label, sum(n)::int AS n FROM v_fact_counts
        WHERE {where} GROUP BY category, label ORDER BY n DESC, label LIMIT :limit"""  # noqa: S608
    return [dict(r._mapping) for r in await db.execute(text(sql), params)]


async def usage(
    db: AsyncSession, clinic_id: uuid.UUID, *, d_from: date | None, d_to: date | None
) -> list[dict[str, Any]]:
    """Provider usage per month and job kind from processing_job.usage (counters only).
    processing_job has no RLS, so the clinic filter here is the isolation."""
    params: dict[str, Any] = {"cid": clinic_id}
    day = "(updated_at AT TIME ZONE 'UTC')::date"
    sql = f"""
        SELECT date_trunc('month', {day})::date AS month, kind::text AS kind, count(*)::int AS jobs,
               coalesce(sum((usage->>'audio_ms')::bigint), 0)::bigint AS audio_ms,
               coalesce(sum((usage->>'prompt_tokens')::bigint), 0)::bigint AS prompt_tokens,
               coalesce(sum((usage->>'completion_tokens')::bigint), 0)::bigint AS completion_tokens
        FROM processing_job
        WHERE clinic_id = :cid AND status = 'done'{_range(day, d_from, d_to, params)}
        GROUP BY 1, 2 ORDER BY 1, 2"""  # noqa: S608
    return [dict(r._mapping) for r in await db.execute(text(sql), params)]


async def overview(
    db: AsyncSession, clinic_id: uuid.UUID, *, d_from: date | None, d_to: date | None
) -> dict[str, Any]:
    p1: dict[str, Any] = {"cid": clinic_id}
    p2: dict[str, Any] = {"cid": clinic_id}
    sess_sql = f"""
        SELECT count(*)::int AS sessions,
               (count(*) FILTER (WHERE state = 'approved'))::int AS approved
        FROM consultation_session
        WHERE clinic_id = :cid{_range("scheduled_date", d_from, d_to, p1)}"""  # noqa: S608
    cat_sql = f"""
        SELECT category::text AS category, sum(n)::int AS n FROM v_fact_counts
        WHERE clinic_id = :cid{_range("day", d_from, d_to, p2)}
        GROUP BY category ORDER BY n DESC, category"""  # noqa: S608
    s = (await db.execute(text(sess_sql), p1)).one()
    cats = [dict(r._mapping) for r in await db.execute(text(cat_sql), p2)]
    return {"sessions": s.sessions, "approved": s.approved, "by_category": cats}
