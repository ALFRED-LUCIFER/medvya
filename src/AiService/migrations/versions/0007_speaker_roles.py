"""Content-based speaker role suggestions and per-phrase speaker overrides.

Revision ID: 0007
Revises: 0006

Provider diarization clusters voices; it cannot tell a patient from a companion who sound alike
(measured on the three-speaker test recording: two clusters in every setting). The normalise step
now suggests a role per phrase from what is said; the clinician confirms it, which is stored as an
append-only transcript_correction carrying speaker_label instead of corrected_text.
"""

from alembic import op

revision = "0007"
down_revision = "0006"
branch_labels = None
depends_on = None

ROLES = "('doctor', 'patient', 'companion', 'interpreter', 'unknown')"


def upgrade() -> None:
    op.execute(
        "ALTER TABLE transcript_segment "
        f"ADD COLUMN suggested_role text CONSTRAINT ck_suggested_role CHECK (suggested_role IN {ROLES}), "
        "ADD COLUMN role_confidence numeric(5,4)"
    )
    op.execute("ALTER TABLE transcript_correction DROP CONSTRAINT ck_correction_text")
    op.execute("ALTER TABLE transcript_correction ALTER COLUMN corrected_text DROP NOT NULL")
    op.execute("ALTER TABLE transcript_correction ADD COLUMN speaker_label text")
    op.execute(
        "ALTER TABLE transcript_correction ADD CONSTRAINT ck_correction_payload CHECK ("
        "(corrected_text IS NOT NULL AND length(btrim(corrected_text)) > 0) OR "
        "(speaker_label IS NOT NULL AND length(btrim(speaker_label)) > 0))"
    )


def downgrade() -> None:
    op.execute("DELETE FROM transcript_correction WHERE corrected_text IS NULL")
    op.execute("ALTER TABLE transcript_correction DROP CONSTRAINT ck_correction_payload")
    op.execute("ALTER TABLE transcript_correction DROP COLUMN speaker_label")
    op.execute("ALTER TABLE transcript_correction ALTER COLUMN corrected_text SET NOT NULL")
    op.execute(
        "ALTER TABLE transcript_correction ADD CONSTRAINT ck_correction_text "
        "CHECK (length(btrim(corrected_text)) > 0)"
    )
    op.execute(
        "ALTER TABLE transcript_segment DROP COLUMN suggested_role, DROP COLUMN role_confidence"
    )
