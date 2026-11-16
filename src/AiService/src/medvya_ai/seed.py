"""Clinic and user provisioning.

AUTH_MODE=service: no seeding; the clinic and a user row per Medvya user are created on first
use from what Medvya asserts (ensure_service_user).
AUTH_MODE=disabled: `python -m medvya_ai.seed` creates a demo clinic + doctor and prints the
clinic id to send as X-Clinic-Id.
AUTH_MODE=entra: `python -m medvya_ai.seed --provision --email ... --subject <oid> [--clinic-id ...
--clinic-name ... --role doctor|admin --name ...]` creates (or reuses) a clinic and adds a user
who can log in with that OIDC subject. Roles are assigned here, never read from tokens."""

import argparse
import asyncio
import uuid

from sqlalchemy import select, text
from sqlalchemy.dialects.postgresql import insert as pg_insert
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai.config import get_settings
from medvya_ai.db import make_engine, make_sessionmaker, set_tenant, uuid7
from medvya_ai.models import AppUser, Clinic, RetentionPolicy

DEV_EMAIL = "doctor@example.test"


async def create_clinic_with_user(
    db: AsyncSession, *, clinic_name: str, email: str, display_name: str = "Dr Demo"
) -> tuple[uuid.UUID, uuid.UUID]:
    clinic_id, user_id = uuid7(), uuid7()
    db.add(Clinic(id=clinic_id, name=clinic_name))
    await db.flush()
    await set_tenant(db, clinic_id)
    db.add(RetentionPolicy(clinic_id=clinic_id))
    db.add(
        AppUser(
            id=user_id,
            clinic_id=clinic_id,
            email=email.lower(),
            role="doctor",
            display_name=display_name,
        )
    )
    await db.flush()
    return clinic_id, user_id


async def provision_user(
    db: AsyncSession,
    *,
    clinic_id: uuid.UUID,
    email: str,
    display_name: str,
    role: str,
    subject: str,
) -> uuid.UUID:
    """Adds a login-capable user to an existing clinic (db must be scoped to that clinic)."""
    user_id = uuid7()
    db.add(
        AppUser(
            id=user_id,
            clinic_id=clinic_id,
            email=email.lower(),
            role=role,
            display_name=display_name,
            external_subject=subject,
        )
    )
    await db.flush()
    return user_id


async def user_by_subject(db: AsyncSession, subject: str) -> AppUser | None:
    """Lookup before the clinic is known: the subject_lookup RLS policy (migration 0004) exposes
    exactly the row whose external_subject equals the transaction-local app.login_subject."""
    await db.execute(text("SELECT set_config('app.login_subject', :s, true)"), {"s": subject})
    return await db.scalar(
        select(AppUser).where(AppUser.external_subject == subject, AppUser.is_active)
    )


async def first_doctor(db: AsyncSession) -> uuid.UUID | None:
    """First active user of the clinic already set with set_tenant (RLS scopes the query)."""
    return await db.scalar(
        select(AppUser.id).where(AppUser.is_active).order_by(AppUser.created_at).limit(1)
    )


async def ensure_service_user(
    db: AsyncSession, *, clinic_id: uuid.UUID, medvya_user_id: uuid.UUID, role: str
) -> uuid.UUID:
    """AUTH_MODE=service: the clinic row (no PHI) and one app_user per (clinic, Medvya user) are
    created on first use, so FKs and the audit actor resolve. Leaves db scoped to the clinic.
    The Medvya user id lives in external_subject: a user may work in several Medvya tenants."""
    await db.execute(
        pg_insert(Clinic).values(id=clinic_id, name="Medvya tenant").on_conflict_do_nothing()
    )
    await set_tenant(db, clinic_id)
    subject = f"medvya:{clinic_id}:{medvya_user_id}"
    await db.execute(
        pg_insert(AppUser)
        .values(
            id=uuid7(),
            clinic_id=clinic_id,
            role=role,
            display_name="Medvya user",
            external_subject=subject,
        )
        .on_conflict_do_nothing()
    )
    user_id = await db.scalar(select(AppUser.id).where(AppUser.external_subject == subject))
    if user_id is None:  # unreachable unless RLS hid the row: never act as someone else
        raise RuntimeError("service user not visible after insert")
    return user_id


async def _main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--provision", action="store_true", help="add a login-capable user")
    ap.add_argument("--email")
    ap.add_argument("--subject", help="OIDC subject: Entra object id (oid)")
    ap.add_argument("--name", default="Doctor")
    ap.add_argument("--role", default="doctor", choices=("doctor", "admin"))
    ap.add_argument("--clinic-id", type=uuid.UUID, help="existing clinic; omit to create one")
    ap.add_argument("--clinic-name", default="Clinic")
    args = ap.parse_args()
    engine = make_engine(get_settings().database_url.get_secret_value())
    async with make_sessionmaker(engine)() as db, db.begin():
        if not args.provision:
            clinic_id, _ = await create_clinic_with_user(
                db, clinic_name="Demo Clinic", email=f"{uuid7().hex[:8]}.{DEV_EMAIL}"
            )
            print(f"demo clinic {clinic_id} (send as X-Clinic-Id)")
        else:
            if not (args.email and args.subject):
                ap.error("--provision needs --email and --subject")
            clinic_id = args.clinic_id or uuid7()
            if args.clinic_id is None:
                db.add(Clinic(id=clinic_id, name=args.clinic_name))
                await db.flush()
                await set_tenant(db, clinic_id)
                db.add(RetentionPolicy(clinic_id=clinic_id))
            else:
                await set_tenant(db, clinic_id)
            user_id = await provision_user(
                db,
                clinic_id=clinic_id,
                email=args.email,
                display_name=args.name,
                role=args.role,
                subject=args.subject,
            )
            print(f"clinic {clinic_id} user {user_id} role {args.role}")
    await engine.dispose()


if __name__ == "__main__":
    asyncio.run(_main())
