"""Drop login_lookup: there is no login any more, so nothing may read password rows by email.

Revision ID: 0002
Revises: 0001
"""

from alembic import op

revision = "0002"
down_revision = "0001"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.execute("DROP POLICY IF EXISTS login_lookup ON app_user")


def downgrade() -> None:
    op.execute(
        "CREATE POLICY login_lookup ON app_user FOR SELECT "
        "USING (email = NULLIF(current_setting('app.login_email', true), ''))"
    )
