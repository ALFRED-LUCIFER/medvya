"""Append-only audit trail. metadata: ids and counts only, never PHI."""

import uuid
from typing import Any

from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai.models import AuditEvent


async def record(
    db: AsyncSession,
    *,
    clinic_id: uuid.UUID,
    actor_user_id: uuid.UUID | None,
    action: str,
    entity_type: str,
    entity_id: uuid.UUID | None,
    ip: str | None,
    metadata: dict[str, Any] | None = None,
) -> None:
    db.add(
        AuditEvent(
            clinic_id=clinic_id,
            actor_user_id=actor_user_id,
            action=action,
            entity_type=entity_type,
            entity_id=entity_id,
            meta=metadata or {},
            ip=ip,
        )
    )
    await db.flush()
