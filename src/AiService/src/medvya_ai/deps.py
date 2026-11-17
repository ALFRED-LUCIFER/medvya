"""Request dependencies: settings, blob store, and the tenant-scoped transaction."""

import uuid
from collections.abc import AsyncIterator
from dataclasses import dataclass
from typing import Annotated, Any

from fastapi import Depends, Request
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai import audit
from medvya_ai.auth import TokenVerifier, authorise_service, bearer_token
from medvya_ai.config import Settings
from medvya_ai.crypto import KeyWrapper
from medvya_ai.db import set_tenant
from medvya_ai.errors import AppError
from medvya_ai.seed import ensure_service_user, first_doctor, user_by_subject
from medvya_ai.storage import BlobStore


def get_settings(request: Request) -> Settings:
    settings: Settings = request.app.state.settings
    return settings


def get_blobs(request: Request) -> BlobStore:
    blobs: BlobStore = request.app.state.blobs
    return blobs


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


SERVICE_ROLES = frozenset({"doctor", "admin"})  # user_role enum; anything else is refused


def _header_uuid(request: Request, name: str) -> uuid.UUID:
    raw = request.headers.get(name)
    if raw is None:
        raise AppError(400, "TENANT_HEADER_REQUIRED", f"{name} header is required")
    try:
        return uuid.UUID(raw)
    except ValueError:
        raise AppError(400, "TENANT_HEADER_INVALID", f"{name} must be a UUID") from None


async def get_tenant(request: Request) -> AsyncIterator[Tenant]:
    """One transaction per request, scoped with set_config(..., is_local=true); commits on
    success, rolls back on error.

    AUTH_MODE=service: the token must be Medvya's API (authorise_service); only then are
    X-Clinic-Id, X-User-Id and X-User-Role trusted. They set the RLS tenant and the audit actor.
    AUTH_MODE=entra: the token's subject selects the app_user; clinic and role come from that row
    and tenant headers are ignored.
    AUTH_MODE=disabled (ENVIRONMENT=development only): X-Clinic-Id is required; the request acts
    as that clinic's first user."""
    settings: Settings = request.app.state.settings
    verifier: TokenVerifier | None = request.app.state.verifier
    claims = await verifier.verify(bearer_token(request)) if verifier is not None else None
    mode = settings.auth_mode
    if mode == "service":
        if claims is None:  # build_verifier never returns None outside disabled mode
            raise AppError(401, "UNAUTHENTICATED", "Bearer token required")
        authorise_service(claims, settings)
        medvya_user = _header_uuid(request, "X-User-Id")
        role = request.headers.get("X-User-Role", "")
        if role not in SERVICE_ROLES:
            raise AppError(400, "TENANT_HEADER_INVALID", "X-User-Role is missing or not allowed")
    if mode != "entra":
        header_clinic = _header_uuid(request, "X-Clinic-Id")
    async with request.app.state.sessionmaker() as session, session.begin():
        if mode == "service":
            clinic_id = header_clinic
            user_id = await ensure_service_user(
                session, clinic_id=clinic_id, medvya_user_id=medvya_user, role=role
            )
        elif claims is not None:
            user = await user_by_subject(session, claims.subject)
            if user is None:
                raise AppError(403, "TENANT_ACCESS_DENIED", "User is not provisioned")
            clinic_id, user_id, role = user.clinic_id, user.id, user.role
            await set_tenant(session, clinic_id)
        else:
            clinic_id, role = header_clinic, "doctor"
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
