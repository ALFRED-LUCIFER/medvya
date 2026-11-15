"""Final-MVP capabilities: manual notes, transcript corrections, guardian consent details,
privacy requests, usage accounting.

Revision ID: 0005
Revises: 0004
"""

from alembic import op

revision = "0005"
down_revision = "0004"
branch_labels = None
depends_on = None

APP_ROLE = "medvya_ai_app"

TABLES = (
    # Original transcript text is immutable; a correction is a new append-only row. The latest
    # row per segment is what the clinician sees; fact evidence keeps pointing at the original.
    """CREATE TABLE transcript_correction (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        session_id uuid NOT NULL,
        segment_id uuid NOT NULL,
        corrected_text text NOT NULL CONSTRAINT ck_correction_text CHECK (length(btrim(corrected_text)) > 0),
        reason text,
        author_id uuid NOT NULL,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT fk_correction_session FOREIGN KEY (clinic_id, session_id)
            REFERENCES consultation_session (clinic_id, id),
        CONSTRAINT fk_correction_segment FOREIGN KEY (clinic_id, segment_id)
            REFERENCES transcript_segment (clinic_id, id),
        CONSTRAINT fk_correction_author FOREIGN KEY (clinic_id, author_id)
            REFERENCES app_user (clinic_id, id)
    )""",
    # Patient access / erasure requests: state and decision are tracked, never "delete everything".
    """CREATE TABLE privacy_request (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        patient_ref text NOT NULL,
        kind privacy_request_kind NOT NULL,
        status privacy_request_status NOT NULL DEFAULT 'received',
        identity_verified_by uuid NOT NULL,
        verification_note text,
        decision jsonb NOT NULL DEFAULT '{}'::jsonb,
        created_at timestamptz NOT NULL DEFAULT now(),
        resolved_at timestamptz,
        CONSTRAINT fk_privacy_verifier FOREIGN KEY (clinic_id, identity_verified_by)
            REFERENCES app_user (clinic_id, id)
    )""",
)


def upgrade() -> None:
    op.execute("CREATE TYPE session_mode AS ENUM ('recorded', 'manual')")
    op.execute("CREATE TYPE privacy_request_kind AS ENUM ('access', 'erasure')")
    op.execute("CREATE TYPE privacy_request_status AS ENUM ('received', 'completed', 'rejected')")
    op.execute(
        "ALTER TABLE consultation_session ADD COLUMN mode session_mode NOT NULL DEFAULT 'recorded'"
    )
    # Guardian / representative consent: who consented on the participant's behalf.
    op.execute(
        "ALTER TABLE consent_record "
        "ADD COLUMN representative_name text, ADD COLUMN representative_relationship text"
    )
    # Per-job provider usage (counts only, no PHI): audio ms, tokens, provider/model/region.
    op.execute("ALTER TABLE processing_job ADD COLUMN usage jsonb NOT NULL DEFAULT '{}'::jsonb")
    for stmt in TABLES:
        op.execute(stmt)
    op.execute(
        "CREATE INDEX ix_correction_clinic_segment ON transcript_correction "
        "(clinic_id, segment_id, created_at DESC, id DESC)"
    )
    op.execute(
        "CREATE INDEX ix_privacy_clinic_created ON privacy_request (clinic_id, created_at DESC)"
    )
    for table in ("transcript_correction", "privacy_request"):
        op.execute(f"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY")
        op.execute(f"ALTER TABLE {table} FORCE ROW LEVEL SECURITY")
        op.execute(
            f"CREATE POLICY tenant_isolation ON {table} "
            "USING (clinic_id = app_clinic_id()) WITH CHECK (clinic_id = app_clinic_id())"
        )
    for trigger in ("no_mutation BEFORE UPDATE OR DELETE", "no_truncate BEFORE TRUNCATE"):
        name, _, event = trigger.partition(" ")
        each = "ROW" if name == "no_mutation" else "STATEMENT"
        op.execute(
            f"CREATE TRIGGER trg_transcript_correction_{name} {event} ON transcript_correction "
            f"FOR EACH {each} EXECUTE FUNCTION forbid_mutation()"
        )
    op.execute(f"GRANT SELECT, INSERT ON transcript_correction TO {APP_ROLE}")
    op.execute(f"GRANT SELECT, INSERT, UPDATE ON privacy_request TO {APP_ROLE}")
    # A revoked session's corrections go with its transcript (purge job).
    op.execute(f"GRANT DELETE ON transcript_correction TO {APP_ROLE}")
    op.execute("DROP TRIGGER trg_transcript_correction_no_mutation ON transcript_correction")
    op.execute(
        "CREATE TRIGGER trg_transcript_correction_no_mutation BEFORE UPDATE ON transcript_correction "
        "FOR EACH ROW EXECUTE FUNCTION forbid_mutation()"
    )


def downgrade() -> None:
    op.execute("DROP TABLE IF EXISTS privacy_request")
    op.execute("DROP TABLE IF EXISTS transcript_correction")
    op.execute("ALTER TABLE processing_job DROP COLUMN usage")
    op.execute(
        "ALTER TABLE consent_record "
        "DROP COLUMN representative_name, DROP COLUMN representative_relationship"
    )
    op.execute("ALTER TABLE consultation_session DROP COLUMN mode")
    for t in ("privacy_request_status", "privacy_request_kind", "session_mode"):
        op.execute(f"DROP TYPE IF EXISTS {t}")
