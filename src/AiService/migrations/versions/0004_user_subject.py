"""Authenticated identity: app_user.external_subject (OIDC oid/sub) and its lookup policy.

Revision ID: 0004
Revises: 0003

The app verifies a bearer token, then needs the user row before the clinic is known. It sets the
transaction-local app.login_subject for that one SELECT; external_subject is globally unique so
at most one row is visible. Roles stay in app_user.role, never in the token.
"""

from alembic import op

revision = "0004"
down_revision = "0003"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.execute("ALTER TABLE app_user ADD COLUMN external_subject text")
    op.execute("ALTER TABLE app_user ADD CONSTRAINT uq_app_user_subject UNIQUE (external_subject)")
    op.execute(
        "CREATE POLICY subject_lookup ON app_user FOR SELECT "
        "USING (external_subject IS NOT NULL AND "
        "external_subject = NULLIF(current_setting('app.login_subject', true), ''))"
    )


def downgrade() -> None:
    op.execute("DROP POLICY IF EXISTS subject_lookup ON app_user")
    op.execute("ALTER TABLE app_user DROP COLUMN external_subject")
