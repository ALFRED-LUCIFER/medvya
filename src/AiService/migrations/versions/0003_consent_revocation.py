"""Consent revocation: consent_epoch, revoked session state, job cancellation and purge.

Revision ID: 0003
Revises: 0002

ADR: src/AiService/docs/decisions/0001-stop-vs-withdraw.md. Enum values added here must not be used in DDL
expressions of this migration (PostgreSQL forbids using a new enum value in the transaction
that adds it); the trigger body below only compares at run time.
"""

from alembic import op

revision = "0003"
down_revision = "0002"
branch_labels = None
depends_on = None

APP_ROLE = "medvya_ai_app"
PURGEABLE = (
    "transcript_segment",
    "clinical_note",
    "note_revision",
    "clinical_fact",
    "medication",
    "fact_evidence",
    "extraction_rejection",
)

# note_revision stays append-only except for the purge of a revoked (never approved) session.
REVISION_GUARD = """CREATE FUNCTION forbid_revision_mutation() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
    BEGIN
        IF TG_OP = 'DELETE' AND EXISTS (
            SELECT 1 FROM clinical_note n
            JOIN consultation_session s ON s.id = n.session_id AND s.clinic_id = n.clinic_id
            WHERE n.id = OLD.note_id AND s.state = 'revoked') THEN
            RETURN OLD;
        END IF;
        RAISE EXCEPTION USING
            MESSAGE = TG_TABLE_NAME || ' is append-only',
            ERRCODE = 'restrict_violation';
    END
    $$"""


def upgrade() -> None:
    op.execute("ALTER TYPE session_state ADD VALUE IF NOT EXISTS 'revoked'")
    op.execute("ALTER TYPE job_status ADD VALUE IF NOT EXISTS 'cancelled'")
    op.execute("ALTER TYPE job_kind ADD VALUE IF NOT EXISTS 'purge_session'")
    op.execute(
        "ALTER TABLE consultation_session "
        "ADD COLUMN consent_epoch integer NOT NULL DEFAULT 0, "
        "ADD COLUMN revoked_at timestamptz"
    )
    op.execute("ALTER TABLE processing_job ADD COLUMN consent_epoch integer NOT NULL DEFAULT 0")
    op.execute(REVISION_GUARD)
    op.execute("DROP TRIGGER trg_note_revision_no_mutation ON note_revision")
    op.execute(
        "CREATE TRIGGER trg_note_revision_no_mutation BEFORE UPDATE OR DELETE ON note_revision "
        "FOR EACH ROW EXECUTE FUNCTION forbid_revision_mutation()"
    )
    # The purge job deletes derived artifacts of a revoked session; audio rows keep deleted_at.
    op.execute(f"GRANT DELETE ON {', '.join(PURGEABLE)} TO {APP_ROLE}")


def downgrade() -> None:
    op.execute(f"REVOKE DELETE ON {', '.join(PURGEABLE)} FROM {APP_ROLE}")
    op.execute("DROP TRIGGER trg_note_revision_no_mutation ON note_revision")
    op.execute(
        "CREATE TRIGGER trg_note_revision_no_mutation BEFORE UPDATE OR DELETE ON note_revision "
        "FOR EACH ROW EXECUTE FUNCTION forbid_mutation()"
    )
    op.execute("DROP FUNCTION forbid_revision_mutation()")
    op.execute("ALTER TABLE processing_job DROP COLUMN consent_epoch")
    op.execute("ALTER TABLE consultation_session DROP COLUMN consent_epoch, DROP COLUMN revoked_at")
    # Enum values cannot be removed; rows with the new values would have to be rewritten first.
