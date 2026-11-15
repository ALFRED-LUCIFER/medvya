"""Request dependencies: settings, blob store, and the tenant-scoped transaction."""

import uuid
from collections.abc import AsyncIterator
from dataclasses import dataclass
from typing import Annotated, Any

from fastapi import Depends, Header, Request
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai import audit
from medvya_ai.auth import TokenVerifier, bearer_token
from medvya_ai.config import Settings
from medvya_ai.crypto import KeyWrapper
from medvya_ai.db import set_tenant
from medvya_ai.errors import AppError
from medvya_ai.providers.llm.base import LLMClient
from medvya_ai.seed import ensure_demo_user, first_doctor, user_by_subject
from medvya_ai.storage import BlobStore


def get_settings(request: Request) -> Settings:
    settings: Settings = request.app.state.settings
    return settings


def get_blobs(request: Request) -> BlobStore:
    blobs: BlobStore = request.app.state.blobs
    return blobs


def get_llm(request: Request) -> LLMClient:
    llm: LLMClient = request.app.state.llm
    return llm


def get_key_wrapper(request: Request) -> KeyWrapper:
    wrapper: KeyWrapper = request.app.state.key_wrapper
    return wrapper


@dataclass
class Tenant:
    db: AsyncSession
    user_id: uuid.UUID
    clinic_id: uuid.UUID
    role: str
    ip: str | None

    async def audit(
        self,
        action: str,
        entity_type: str,
        entity_id: uuid.UUID | None,
        metadata: dict[str, Any] | None = None,
    ) -> None:
        await audit.record(
            self.db,
            clinic_id=self.clinic_id,
            actor_user_id=self.user_id,
            action=action,
            entity_type=entity_type,
            entity_id=entity_id,
            ip=self.ip,
            metadata=metadata,
        )


async def get_tenant(
    request: Request,
    x_clinic_id: Annotated[uuid.UUID | None, Header()] = None,
) -> AsyncIterator[Tenant]:
    """One transaction per request, scoped with set_config(..., is_local=true); commits on
    success, rolls back on error.

    AUTH_MODE=entra: the bearer token is verified, its subject selects the app_user, and clinic
    and role come from that row. X-Clinic-Id is ignored: a client can never choose its tenant.
    AUTH_MODE=disabled (local/test build): every request acts as the clinic's first doctor; the
    clinic is X-Clinic-Id when sent, else the demo clinic (created on first use)."""
    verifier: TokenVerifier | None = request.app.state.verifier
    claims = await verifier.verify(bearer_token(request)) if verifier is not None else None
    async with request.app.state.sessionmaker() as session, session.begin():
        role = "doctor"
        if claims is not None:
            user = await user_by_subject(session, claims.subject)
            if user is None:
                raise AppError(403, "TENANT_ACCESS_DENIED", "User is not provisioned")
            clinic_id, user_id, role = user.clinic_id, user.id, user.role
            await set_tenant(session, clinic_id)
        elif x_clinic_id is None:
            clinic_id, user_id = await ensure_demo_user(session)
        else:
            clinic_id = x_clinic_id
            await set_tenant(session, clinic_id)
            found = await first_doctor(session)
            if found is None:
                raise AppError(404, "CLINIC_NOT_FOUND", "Unknown clinic")
            user_id = found
        ip = request.client.host if request.client else None
        yield Tenant(session, user_id, clinic_id, role, ip)


# scope="function": commit/rollback runs before the response is sent (a default yield dependency
# exits after it, so a 2xx could reach the client before its data is committed).
TenantDep = Annotated[Tenant, Depends(get_tenant, scope="function")]
SettingsDep = Annotated[Settings, Depends(get_settings)]
BlobsDep = Annotated[BlobStore, Depends(get_blobs)]
WrapperDep = Annotated[KeyWrapper, Depends(get_key_wrapper)]
LLMDep = Annotated[LLMClient, Depends(get_llm)]
