"""AI receptionist intake conversations (POC).

Revision ID: 0006
Revises: 0005
"""

from alembic import op

revision = "0006"
down_revision = "0005"
branch_labels = None
depends_on = None

APP_ROLE = "medvya_ai_app"


def upgrade() -> None:
    op.execute("CREATE TYPE receptionist_status AS ENUM ('open', 'completed', 'escalated')")
    op.execute(
        """CREATE TABLE receptionist_conversation (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            clinic_id uuid NOT NULL REFERENCES clinic(id),
            status receptionist_status NOT NULL DEFAULT 'open',
            language text,
            intake jsonb NOT NULL DEFAULT '{}'::jsonb,
            messages jsonb NOT NULL DEFAULT '[]'::jsonb,
            session_id uuid,
            created_at timestamptz NOT NULL DEFAULT now(),
            updated_at timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT fk_reception_session FOREIGN KEY (clinic_id, session_id)
                REFERENCES consultation_session (clinic_id, id)
        )"""
    )
    op.execute(
        "CREATE INDEX ix_reception_clinic_created ON receptionist_conversation "
        "(clinic_id, created_at DESC)"
    )
    op.execute("ALTER TABLE receptionist_conversation ENABLE ROW LEVEL SECURITY")
    op.execute("ALTER TABLE receptionist_conversation FORCE ROW LEVEL SECURITY")
    op.execute(
        "CREATE POLICY tenant_isolation ON receptionist_conversation "
        "USING (clinic_id = app_clinic_id()) WITH CHECK (clinic_id = app_clinic_id())"
    )
    op.execute(f"GRANT SELECT, INSERT, UPDATE ON receptionist_conversation TO {APP_ROLE}")


def downgrade() -> None:
    op.execute("DROP TABLE IF EXISTS receptionist_conversation")
    op.execute("DROP TYPE IF EXISTS receptionist_status")
