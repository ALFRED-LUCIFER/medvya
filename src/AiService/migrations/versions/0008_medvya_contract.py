"""Medvya contract (ADR-021): drop HealthOps leftovers, patient_ref is an encounter id.

Revision ID: 0008
Revises: 0007

- privacy_request and receptionist_conversation go with their endpoints: Medvya handles data
  requests itself and never calls the receptionist intake (ADR-021 section 2).
- app_user.password_hash was never a real hash (there is no login); dropped.
- app_user.email becomes nullable: users Medvya asserts in AUTH_MODE=service have no email here.
- consultation_session.patient_ref becomes uuid (the Medvya encounter id, ADR-021 section 4), so a
  name or phone number cannot be stored there. Development rows holding a non-uuid reference
  (synthetic data only) get a fresh random uuid: they never pointed at a Medvya encounter.
"""

from alembic import op

revision = "0008"
down_revision = "0007"
branch_labels = None
depends_on = None

UUID_RE = "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"


def upgrade() -> None:
    op.execute("DROP TABLE receptionist_conversation")
    op.execute("DROP TYPE receptionist_status")
    op.execute("DROP TABLE privacy_request")
    op.execute("DROP TYPE privacy_request_status")
    op.execute("DROP TYPE privacy_request_kind")
    op.execute("ALTER TABLE app_user DROP COLUMN password_hash")
    op.execute("ALTER TABLE app_user ALTER COLUMN email DROP NOT NULL")
    op.execute(
        "ALTER TABLE consultation_session ALTER COLUMN patient_ref TYPE uuid USING "
        f"CASE WHEN patient_ref ~ '{UUID_RE}' THEN patient_ref::uuid ELSE gen_random_uuid() END"
    )


def downgrade() -> None:
    # Forward-only in practice: the dropped tables are not recreated (the 0005/0006 downgrades use
    # IF EXISTS). This restores the column shapes so a full downgrade to base still runs.
    op.execute("ALTER TABLE consultation_session ALTER COLUMN patient_ref TYPE text")
    op.execute("UPDATE app_user SET email = id::text || '@service.invalid' WHERE email IS NULL")
    op.execute("ALTER TABLE app_user ALTER COLUMN email SET NOT NULL")
    op.execute("ALTER TABLE app_user ADD COLUMN password_hash text NOT NULL DEFAULT '!'")
